namespace ForgeBoard.Contracts.Models;

public sealed class ImageArtifact
{
    public string Id { get; set; } = string.Empty;

    public string BuildExecutionId { get; set; } = string.Empty;

    public string BuildDefinitionId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string? Checksum { get; set; }

    public long FileSizeBytes { get; set; } = 0;

    public string Format { get; set; } = "qcow2";

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<string> Tags { get; set; } = new List<string>();
}
