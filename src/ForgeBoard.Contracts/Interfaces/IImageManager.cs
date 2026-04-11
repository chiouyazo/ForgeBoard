using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface IImageManager
{
    Task<BaseImage> CreateBaseImageAsync(
        BaseImage image,
        CancellationToken cancellationToken = default
    );

    Task<BaseImage?> GetBaseImageAsync(string id, CancellationToken cancellationToken = default);

    Task<List<BaseImage>> GetAllBaseImagesAsync(CancellationToken cancellationToken = default);

    Task<BaseImage> UpdateBaseImageAsync(
        BaseImage image,
        CancellationToken cancellationToken = default
    );

    Task DeleteBaseImageAsync(string id, CancellationToken cancellationToken = default);

    Task<ImageArtifact?> GetArtifactAsync(string id, CancellationToken cancellationToken = default);

    Task<List<ImageArtifact>> GetAllArtifactsAsync(CancellationToken cancellationToken = default);

    Task DeleteArtifactAsync(string id, CancellationToken cancellationToken = default);

    Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken cancellationToken = default);

    Task<List<BaseImage>> GetAllMergedAsync(CancellationToken cancellationToken = default);

    Task<BaseImage> PromoteArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default
    );
}
