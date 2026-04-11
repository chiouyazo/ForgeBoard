namespace ForgeBoard.Contracts.Models;

public sealed class PublishProgress
{
    public string Status { get; set; } = "Idle";

    public double PercentComplete { get; set; }

    public string? Error { get; set; }

    public bool IsComplete { get; set; }
}
