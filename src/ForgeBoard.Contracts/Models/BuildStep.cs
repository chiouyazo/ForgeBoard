namespace ForgeBoard.Contracts.Models;

public sealed class BuildStep
{
    public string Id { get; set; } = string.Empty;

    public string BuildDefinitionId { get; set; } = string.Empty;

    public int Order { get; set; } = 0;

    public string Name { get; set; } = string.Empty;

    public BuildStepType StepType { get; set; } = BuildStepType.PowerShell;

    public string Content { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = BuildDefaults.DefaultTimeoutSeconds;

    public bool ExpectReboot { get; set; } = false;

    public bool UsePacker { get; set; } = false;

    public string? LibraryEntryId { get; set; }
}
