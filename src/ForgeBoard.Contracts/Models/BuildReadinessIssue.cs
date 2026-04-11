namespace ForgeBoard.Contracts.Models;

public sealed class BuildReadinessIssue
{
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public IssueSeverity Severity { get; set; }
}
