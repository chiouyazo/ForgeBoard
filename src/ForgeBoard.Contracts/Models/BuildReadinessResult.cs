namespace ForgeBoard.Contracts.Models;

public sealed class BuildReadinessResult
{
    public bool IsReady { get; set; }

    public List<BuildReadinessIssue> Issues { get; set; } = new List<BuildReadinessIssue>();
}
