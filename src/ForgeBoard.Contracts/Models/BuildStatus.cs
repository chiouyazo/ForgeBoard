namespace ForgeBoard.Contracts.Models;

public enum BuildStatus
{
    Queued,
    Preparing,
    WaitingForChain,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
