namespace ForgeBoard.Contracts.Models;

public sealed class DiskUsageInfo
{
    public long CacheSizeBytes { get; set; } = 0;

    public long ArtifactSizeBytes { get; set; } = 0;

    public long WorkingSizeBytes { get; set; } = 0;

    public long TotalSizeBytes { get; set; } = 0;

    public int CachedImageCount { get; set; } = 0;

    public int ArtifactCount { get; set; } = 0;

    public long DriveTotalBytes { get; set; } = 0;

    public long DriveFreeBytes { get; set; } = 0;
}
