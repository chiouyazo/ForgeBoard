using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Sources;

public sealed class NexusFeedAdapter : IFeedAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".box",
        ".iso",
        ".vhdx",
        ".qcow2",
        ".vmdk",
        ".tar.gz",
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<NexusFeedAdapter> _logger;

    public FeedType SourceType => FeedType.Nexus;

    public NexusFeedAdapter(HttpClient httpClient, ILogger<NexusFeedAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> TestConnectivityAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        try
        {
            string baseUrl = feed.ConnectionString.TrimEnd('/');
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/service/rest/v1/repositories"
            );
            ApplyAuth(request, feed);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                cancellationToken
            );
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Nexus connectivity test failed for {Url}",
                feed.ConnectionString
            );
            return false;
        }
    }

    public async Task<List<string>> ListRepositoriesAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        string baseUrl = feed.ConnectionString.TrimEnd('/');
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/service/rest/v1/repositories"
        );
        ApplyAuth(request, feed);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);

        List<string> repos = new List<string>();
        foreach (System.Text.Json.JsonElement element in doc.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("name", out System.Text.Json.JsonElement nameEl))
            {
                string? name = nameEl.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    repos.Add(name);
                }
            }
        }
        return repos;
    }

    public async Task<Stream> PullAsync(
        Feed feed,
        string fileName,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(fileName);

        string baseUrl = feed.ConnectionString.TrimEnd('/');
        string repo = feed.Repository ?? string.Empty;
        string url = $"{baseUrl}/repository/{repo}/{fileName}";
        _logger.LogInformation("Pulling file {FileName} from Nexus {Url}", fileName, url);

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, feed);

        HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        Stream networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (progress is null)
        {
            return networkStream;
        }

        MemoryStream buffered = new MemoryStream();
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while (
            (bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken))
            > 0
        )
        {
            await buffered.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;
            progress.Report(totalRead);
        }

        await networkStream.DisposeAsync();
        buffered.Position = 0;
        return buffered;
    }

    public async Task PushAsync(
        Feed feed,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        string baseUrl = feed.ConnectionString.TrimEnd('/');
        string repo = (feed.Repository ?? string.Empty).Trim('/');
        string repoPath = string.IsNullOrEmpty(repo) ? fileName : $"{repo}/{fileName}";
        string url = $"{baseUrl}/repository/{repoPath}";
        _logger.LogInformation("Pushing file {FileName} to Nexus {Url}", fileName, url);

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuth(request, feed);
        StreamContent streamContent = new StreamContent(content, 4 * 1024 * 1024);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/octet-stream"
        );
        if (content.CanSeek)
        {
            streamContent.Headers.ContentLength = content.Length;
        }
        request.Content = streamContent;

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutCts.CancelAfter(TimeSpan.FromHours(4));

        HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token
        );
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<string>> ListFilesAsync(
        Feed feed,
        string? prefix = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        string baseUrl = feed.ConnectionString.TrimEnd('/');
        string repo = feed.Repository ?? string.Empty;

        _logger.LogInformation("Listing files from Nexus repository {Repository}", repo);

        List<NexusComponent> components = await LoadComponentsAsync(
            baseUrl,
            repo,
            feed,
            cancellationToken
        );

        List<string> files = new List<string>();
        foreach (NexusComponent component in components)
        {
            if (component.Assets is null)
            {
                continue;
            }

            foreach (NexusAsset asset in component.Assets)
            {
                if (string.IsNullOrEmpty(asset.Path))
                {
                    continue;
                }

                string filePath = asset.Path.TrimStart('/');
                if (
                    prefix is null
                    || filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
                {
                    files.Add(filePath);
                }
            }
        }

        return files;
    }

    public async Task<List<FeedImage>> BrowseImagesAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        string baseUrl = feed.ConnectionString.TrimEnd('/');
        string repo = feed.Repository ?? string.Empty;

        _logger.LogInformation("Browsing images from Nexus repository {Repository}", repo);

        List<NexusComponent> components = await LoadComponentsAsync(
            baseUrl,
            repo,
            feed,
            cancellationToken
        );

        List<NexusAsset> allAssets = new List<NexusAsset>();
        foreach (NexusComponent component in components)
        {
            if (component.Assets is not null)
            {
                allAssets.AddRange(component.Assets);
            }
        }

        Dictionary<string, NexusAsset> manifestsByDir = new Dictionary<string, NexusAsset>();
        Dictionary<string, List<NexusAsset>> imageFilesByDir =
            new Dictionary<string, List<NexusAsset>>();

        foreach (NexusAsset asset in allAssets)
        {
            if (string.IsNullOrEmpty(asset.Path))
            {
                continue;
            }

            string path = asset.Path.TrimStart('/');
            string[] parts = path.Split('/');
            string dir =
                parts.Length > 1 ? string.Join("/", parts.Take(parts.Length - 1)) : string.Empty;

            if (path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                manifestsByDir[dir] = asset;
            }
            else if (IsImageFile(path))
            {
                if (!imageFilesByDir.ContainsKey(dir))
                {
                    imageFilesByDir[dir] = new List<NexusAsset>();
                }
                imageFilesByDir[dir].Add(asset);
            }
        }

        List<FeedImage> results = new List<FeedImage>();

        foreach (KeyValuePair<string, List<NexusAsset>> kvp in imageFilesByDir)
        {
            NexusManifest? manifest = null;

            // Try exact directory match first, then parent directory
            if (manifestsByDir.TryGetValue(kvp.Key, out NexusAsset? manifestAsset))
            {
                manifest = await FetchManifestAsync(
                    baseUrl,
                    repo,
                    feed,
                    manifestAsset,
                    cancellationToken
                );
            }
            else
            {
                string[] dirParts = kvp.Key.Split('/');
                if (dirParts.Length > 1)
                {
                    string parentDir = dirParts[0];
                    if (manifestsByDir.TryGetValue(parentDir, out NexusAsset? parentManifestAsset))
                    {
                        manifest = await FetchManifestAsync(
                            baseUrl,
                            repo,
                            feed,
                            parentManifestAsset,
                            cancellationToken
                        );
                    }
                }
            }

            foreach (NexusAsset imageAsset in kvp.Value)
            {
                string assetPath = (imageAsset.Path ?? string.Empty).TrimStart('/');
                string fileName = Path.GetFileName(assetPath);
                string extension = GetImageExtension(assetPath);

                FeedImage feedImage = new FeedImage
                {
                    Name = manifest?.Title ?? Path.GetFileNameWithoutExtension(fileName),
                    Path = assetPath,
                    Format = extension.TrimStart('.'),
                    SizeBytes = imageAsset.FileSize ?? 0,
                    Version = manifest?.Version ?? ExtractVersion(assetPath),
                    Tags = manifest?.Features ?? new List<string>(),
                };

                results.Add(feedImage);
            }
        }

        return results;
    }

    private async Task<List<NexusComponent>> LoadComponentsAsync(
        string baseUrl,
        string repo,
        Feed feed,
        CancellationToken cancellationToken
    )
    {
        List<NexusComponent> allComponents = new List<NexusComponent>();
        string? continuationToken = null;

        do
        {
            string url =
                $"{baseUrl}/service/rest/v1/components?repository={Uri.EscapeDataString(repo)}";
            if (continuationToken is not null)
            {
                url += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
            }

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request, feed);

            HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            NexusComponentResponse? result = JsonSerializer.Deserialize<NexusComponentResponse>(
                json,
                JsonOptions
            );

            if (result?.Items is not null)
            {
                allComponents.AddRange(result.Items);
            }

            continuationToken = result?.ContinuationToken;
        } while (continuationToken is not null);

        return allComponents;
    }

    private async Task<NexusManifest?> FetchManifestAsync(
        string baseUrl,
        string repo,
        Feed feed,
        NexusAsset manifestAsset,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string manifestPath = (manifestAsset.Path ?? string.Empty).TrimStart('/');
            string manifestUrl = $"{baseUrl}/repository/{repo}/{manifestPath}";

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            ApplyAuth(request, feed);

            HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<NexusManifest>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch manifest from {Path}", manifestAsset.Path);
            return null;
        }
    }

    private static bool IsImageFile(string path)
    {
        foreach (string ext in ImageExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetImageExtension(string path)
    {
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return "tar.gz";
        }
        return Path.GetExtension(path).TrimStart('.');
    }

    private static string? ExtractVersion(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (
                parts[i].Equals("versions", StringComparison.OrdinalIgnoreCase)
                && i + 1 < parts.Length
            )
            {
                return parts[i + 1];
            }
        }

        return null;
    }

    private static void ApplyAuth(HttpRequestMessage request, Feed feed)
    {
        if (feed.Username is not null && feed.Password is not null)
        {
            string credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{feed.Username}:{feed.Password}")
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private sealed class NexusComponentResponse
    {
        public List<NexusComponent> Items { get; set; } = new List<NexusComponent>();
        public string? ContinuationToken { get; set; }
    }

    private sealed class NexusComponent
    {
        public string Name { get; set; } = string.Empty;
        public string? Group { get; set; }
        public string? Version { get; set; }
        public List<NexusAsset>? Assets { get; set; }
    }

    private sealed class NexusAsset
    {
        public string? Path { get; set; }
        public string? DownloadUrl { get; set; }
        public long? FileSize { get; set; }
        public DateTime? LastModified { get; set; }
    }

    private sealed class NexusManifest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? ReleaseNotes { get; set; }
        public List<string>? Features { get; set; }
        public string? ImageType { get; set; }
    }
}
