namespace ForgeBoard.Contracts.Models;

public sealed class VmLaunchProgress
{
    public string Status { get; set; } = string.Empty;

    public string? VmName { get; set; }

    public string? Error { get; set; }

    public bool IsComplete { get; set; }

    public bool IsSuccess { get; set; }
}
