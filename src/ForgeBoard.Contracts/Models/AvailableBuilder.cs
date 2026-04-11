namespace ForgeBoard.Contracts.Models;

public sealed class AvailableBuilder
{
    public PackerBuilder Builder { get; set; }

    public bool IsAvailable { get; set; }

    public string? Reason { get; set; }
}
