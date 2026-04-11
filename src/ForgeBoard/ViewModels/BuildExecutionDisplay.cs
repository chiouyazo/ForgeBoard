using ForgeBoard.Contracts.Models;

namespace ForgeBoard.ViewModels;

public sealed class BuildExecutionDisplay
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = string.Empty;
    public BuildStatus Status { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public string DisplayTitle => $"{Name} ({ExecutionId[..Math.Min(8, ExecutionId.Length)]})";
}
