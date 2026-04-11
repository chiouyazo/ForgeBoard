using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Sources;

public sealed class SmbFeedAdapter : IFeedAdapter
{
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".iso",
        ".qcow2",
        ".vhdx",
        ".box",
        ".vmdk",
    };

    private readonly ILogger<SmbFeedAdapter> _logger;

    public FeedType SourceType => FeedType.Smb;

    public SmbFeedAdapter(ILogger<SmbFeedAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<bool> TestConnectivityAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);
        bool exists = Directory.Exists(feed.ConnectionString);
        return Task.FromResult(exists);
    }

    public Task<Stream> PullAsync(
        Feed feed,
        string fileName,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(fileName);

        string filePath = Path.Combine(feed.ConnectionString, fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found on SMB share: {filePath}");
        }

        _logger.LogInformation(
            "Pulling file {FileName} from SMB share {Path}",
            fileName,
            feed.ConnectionString
        );
        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
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

        string filePath = Path.Combine(feed.ConnectionString, fileName);
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation(
            "Pushed file {FileName} to SMB share {Path}",
            fileName,
            feed.ConnectionString
        );
    }

    public Task<List<string>> ListFilesAsync(
        Feed feed,
        string? prefix = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (!Directory.Exists(feed.ConnectionString))
        {
            return Task.FromResult(new List<string>());
        }

        string searchPattern = prefix is not null ? $"{prefix}*" : "*";
        List<string> files = Directory
            .GetFiles(feed.ConnectionString, searchPattern, SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .Select(f => Path.GetRelativePath(feed.ConnectionString, f))
            .ToList();

        return Task.FromResult(files);
    }

    public Task<List<FeedImage>> BrowseImagesAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (!Directory.Exists(feed.ConnectionString))
        {
            return Task.FromResult(new List<FeedImage>());
        }

        List<FeedImage> images = new List<FeedImage>();

        foreach (
            string filePath in Directory.GetFiles(
                feed.ConnectionString,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            string extension = Path.GetExtension(filePath);
            if (!ImageExtensions.Contains(extension))
            {
                continue;
            }

            FileInfo info = new FileInfo(filePath);
            string relativePath = Path.GetRelativePath(feed.ConnectionString, filePath);

            images.Add(
                new FeedImage
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Path = relativePath,
                    Format = extension.TrimStart('.'),
                    SizeBytes = info.Length,
                }
            );
        }

        return Task.FromResult(images);
    }
}
