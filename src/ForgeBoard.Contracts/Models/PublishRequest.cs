namespace ForgeBoard.Contracts.Models;

public sealed class PublishRequest
{
    public string FeedId { get; set; } = string.Empty;

    public string? Repository { get; set; }

    public string Version { get; set; } = "1.0.0";

    public string? ReleaseNotes { get; set; }

    public string ImageType { get; set; } = "Windows";

    public List<string> Features { get; set; } = new List<string>();

    public string? BuildSummary { get; set; }

    public string? ConvertFormat { get; set; }
}
