namespace ForgeBoard.Contracts.Models;

public sealed class BuildLogEntry
{
    public string Id { get; set; } = string.Empty;

    public string BuildExecutionId { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public LogLevel Level { get; set; } = LogLevel.Info;

    public string Message { get; set; } = string.Empty;

    public string? StepName { get; set; }
}
