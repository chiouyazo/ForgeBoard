namespace ForgeBoard.Contracts.Models;

public sealed class BaseImage
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string? Checksum { get; set; }

    public long FileSizeBytes { get; set; } = 0;

    public string SourceId { get; set; } = string.Empty;

    public ImageOrigin Origin { get; set; } = ImageOrigin.Local;

    public string ImageFormat { get; set; } = "box";

    public string? LocalCachePath { get; set; }

    public bool IsCached { get; set; } = false;

    public string? LinkedBuildDefinitionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool CacheLocally { get; set; } = true;

    public bool RepullOnNextBuild { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
