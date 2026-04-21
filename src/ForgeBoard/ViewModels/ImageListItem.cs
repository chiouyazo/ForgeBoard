namespace ForgeBoard.ViewModels;

public sealed class ImageListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Origin { get; set; } = "Local";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsBaseImage { get; set; }
    public bool IsBuilt { get; set; }
    public string? ArtifactId { get; set; }
    public string? BuildDefinitionId { get; set; }
    public bool IsCached { get; set; }
    public bool ShowCacheIndicator { get; set; }
    public string CacheLabel { get; set; } = string.Empty;
    public Microsoft.UI.Xaml.Media.SolidColorBrush CacheBrush { get; set; } =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(255, 158, 158, 158)
        );
    public bool CanDelete { get; set; } = true;
    public string DeleteButtonText => IsBaseImage ? "Remove" : "Delete";
}
