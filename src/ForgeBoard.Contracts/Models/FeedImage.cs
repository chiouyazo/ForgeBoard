namespace ForgeBoard.Contracts.Models;

public sealed class FeedImage
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? Version { get; set; }

    public List<string> Tags { get; set; } = new List<string>();
}
