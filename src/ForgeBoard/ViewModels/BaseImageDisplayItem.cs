using ForgeBoard.Contracts.Models;

namespace ForgeBoard.ViewModels;

public sealed class BaseImageDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Origin { get; set; } = "Local";
    public long FileSizeBytes { get; set; }
    public BaseImage SourceImage { get; set; } = null!;
}
