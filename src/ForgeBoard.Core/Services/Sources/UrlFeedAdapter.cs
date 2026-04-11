using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Sources;

public sealed class UrlFeedAdapter : IFeedAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UrlFeedAdapter> _logger;

    public FeedType SourceType => FeedType.Url;

    public UrlFeedAdapter(HttpClient httpClient, ILogger<UrlFeedAdapter> logger)
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
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Head,
                feed.ConnectionString
            );
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                cancellationToken
            );
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "URL connectivity test failed for {Url}", feed.ConnectionString);
            return false;
        }
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

        string url = feed.ConnectionString.TrimEnd('/');
        if (!string.IsNullOrEmpty(fileName) && fileName != Path.GetFileName(feed.ConnectionString))
        {
            url = url + "/" + fileName;
        }

        _logger.LogInformation("Pulling file from URL {Url}", url);

        HttpResponseMessage response = await _httpClient.GetAsync(
            url,
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

    public Task PushAsync(
        Feed feed,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException("Push is not supported for URL feeds");
    }

    public Task<List<string>> ListFilesAsync(
        Feed feed,
        string? prefix = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        string fileName = Path.GetFileName(new Uri(feed.ConnectionString).AbsolutePath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = feed.ConnectionString;
        }

        List<string> files = new List<string> { fileName };
        return Task.FromResult(files);
    }

    public Task<List<FeedImage>> BrowseImagesAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        Uri uri = new Uri(feed.ConnectionString);
        string fileName = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = feed.ConnectionString;
        }

        string extension = Path.GetExtension(fileName).TrimStart('.');

        FeedImage image = new FeedImage
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            Path = fileName,
            Format = string.IsNullOrEmpty(extension) ? "unknown" : extension,
            SizeBytes = 0,
        };

        List<FeedImage> result = new List<FeedImage> { image };
        return Task.FromResult(result);
    }
}
