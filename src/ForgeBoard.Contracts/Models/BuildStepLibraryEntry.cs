namespace ForgeBoard.Contracts.Models;

public sealed class BuildStepLibraryEntry
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public BuildStepType StepType { get; set; } = BuildStepType.PowerShell;

    public string Content { get; set; } = string.Empty;

    public int DefaultTimeoutSeconds { get; set; } = BuildDefaults.DefaultTimeoutSeconds;

    public bool ExpectReboot { get; set; } = false;

    public bool UsePacker { get; set; } = false;

    public List<string> Tags { get; set; } = new List<string>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ModifiedAt { get; set; }
}
