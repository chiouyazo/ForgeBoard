namespace ForgeBoard.ViewModels;

public sealed class BuildPreviewResult
{
    public string Hcl { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new List<string>();
}
