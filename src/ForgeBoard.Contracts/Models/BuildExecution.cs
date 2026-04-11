namespace ForgeBoard.Contracts.Models;

public sealed class BuildExecution
{
    public string Id { get; set; } = string.Empty;

    public string BuildDefinitionId { get; set; } = string.Empty;

    public BuildStatus Status { get; set; } = BuildStatus.Queued;

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ArtifactId { get; set; }

    public string? WorkingDirectory { get; set; }
}
