namespace ForgeBoard.Core.Services.Build;

public sealed class BuildEngineResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OutputVhdxPath { get; init; }
}
