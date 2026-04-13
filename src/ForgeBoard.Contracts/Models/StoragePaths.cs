namespace ForgeBoard.Contracts.Models;

public sealed class StoragePaths
{
    public string DataDirectory { get; set; } = string.Empty;

    public string TempDirectory { get; set; } = string.Empty;

    public string CacheDirectory { get; set; } = string.Empty;

    public string ArtifactsDirectory { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public string LogsDirectory { get; set; } = string.Empty;

    public string DatabasePath { get; set; } = string.Empty;
}
