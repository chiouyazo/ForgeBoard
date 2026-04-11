using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class CacheService : ICacheService
{
    private readonly ForgeBoardDatabase _db;
    private readonly IAppPaths _appPaths;
    private readonly IEnumerable<IFeedAdapter> _adapters;
    private readonly ILogger<CacheService> _logger;

    public CacheService(
        ForgeBoardDatabase db,
        IAppPaths appPaths,
        IEnumerable<IFeedAdapter> adapters,
        ILogger<CacheService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _appPaths = appPaths;
        _adapters = adapters;
        _logger = logger;
    }

    public async Task<string> EnsureCachedAsync(
        BaseImage image,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.IsCached && image.LocalCachePath is not null && File.Exists(image.LocalCachePath))
        {
            _logger.LogDebug("Image {Id} already cached at {Path}", image.Id, image.LocalCachePath);
            image.LastUsedAt = DateTimeOffset.UtcNow;
            _db.BaseImages.Update(image);
            return image.LocalCachePath;
        }

        Feed? feed = _db.Feeds.FindById(image.SourceId);
        if (feed is null)
        {
            throw new InvalidOperationException($"Feed {image.SourceId} not found");
        }

        IFeedAdapter? adapter = _adapters.FirstOrDefault(a => a.SourceType == feed.SourceType);
        if (adapter is null)
        {
            throw new InvalidOperationException(
                $"No adapter found for feed type {feed.SourceType}"
            );
        }

        string cachePath = Path.Combine(_appPaths.CacheDirectory, image.Id, image.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        _logger.LogInformation(
            "Downloading image {Id} ({FileName}) to cache",
            image.Id,
            image.FileName
        );

        using (
            Stream downloadStream = await adapter.PullAsync(
                feed,
                image.FileName,
                progress,
                cancellationToken
            )
        )
        {
            using (
                FileStream fileStream = new FileStream(
                    cachePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true
                )
            )
            {
                byte[] buffer = new byte[81920];
                long totalBytesRead = 0;
                int bytesRead;

                while (
                    (
                        bytesRead = await downloadStream.ReadAsync(
                            buffer,
                            0,
                            buffer.Length,
                            cancellationToken
                        )
                    ) > 0
                )
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalBytesRead += bytesRead;
                    progress?.Report(totalBytesRead);
                }
            }
        }

        image.LocalCachePath = cachePath;
        image.IsCached = true;
        image.LastUsedAt = DateTimeOffset.UtcNow;
        image.FileSizeBytes = new FileInfo(cachePath).Length;

        _db.BaseImages.Update(image);

        _logger.LogInformation(
            "Image {Id} cached at {Path} ({Size} bytes)",
            image.Id,
            cachePath,
            image.FileSizeBytes
        );
        return cachePath;
    }

    public Task EvictAsync(string imageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageId);

        BaseImage? image = _db.BaseImages.FindById(imageId);
        if (image is null)
        {
            return Task.CompletedTask;
        }

        if (image.LocalCachePath is not null && File.Exists(image.LocalCachePath))
        {
            string? cacheDir = Path.GetDirectoryName(image.LocalCachePath);
            if (cacheDir is not null && Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
                _logger.LogInformation(
                    "Deleted cache directory {Path} for image {Id}",
                    cacheDir,
                    imageId
                );
            }
        }

        image.IsCached = false;
        image.LocalCachePath = null;
        _db.BaseImages.Update(image);

        _logger.LogInformation("Evicted image {Id} from cache", imageId);
        return Task.CompletedTask;
    }

    public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_appPaths.CacheDirectory))
        {
            return Task.FromResult(0L);
        }

        DirectoryInfo directory = new DirectoryInfo(_appPaths.CacheDirectory);
        long size = directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        return Task.FromResult(size);
    }

    public async Task CleanupAsync(long maxSizeBytes, CancellationToken cancellationToken = default)
    {
        long currentSize = await GetCacheSizeAsync(cancellationToken);
        if (currentSize <= maxSizeBytes)
        {
            _logger.LogDebug(
                "Cache size {Size} is within limit {Max}, no cleanup needed",
                currentSize,
                maxSizeBytes
            );
            return;
        }

        _logger.LogInformation(
            "Cache size {Size} exceeds limit {Max}, starting cleanup",
            currentSize,
            maxSizeBytes
        );

        List<BaseImage> cachedImages = _db
            .BaseImages.Find(i => i.IsCached)
            .OrderBy(i => i.LastUsedAt ?? DateTimeOffset.MinValue)
            .ToList();

        foreach (BaseImage image in cachedImages)
        {
            if (currentSize <= maxSizeBytes)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            long freed = 0;
            if (image.LocalCachePath is not null && File.Exists(image.LocalCachePath))
            {
                freed = new FileInfo(image.LocalCachePath).Length;
            }

            await EvictAsync(image.Id, cancellationToken);
            currentSize -= freed;

            _logger.LogInformation(
                "Cache cleanup: evicted {Id}, freed {Bytes} bytes, remaining {Size} bytes",
                image.Id,
                freed,
                currentSize
            );
        }
    }
}
