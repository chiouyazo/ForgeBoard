using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface ICacheService
{
    Task<string> EnsureCachedAsync(
        BaseImage image,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task EvictAsync(string imageId, CancellationToken cancellationToken = default);

    Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default);

    Task CleanupAsync(long maxSizeBytes, CancellationToken cancellationToken = default);
}
