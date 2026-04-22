namespace ForgeBoard.Contracts.Models;

public sealed class AppSettings
{
    public string Id { get; set; } = "default";

    public string DefaultPackerRunnerConfigId { get; set; } = string.Empty;

    public PackerBuilder DefaultBuilder { get; set; } = PackerBuilder.Qemu;

    public int MaxConcurrentBuilds { get; set; } = 1;

    public long MaxCacheSizeBytes { get; set; } = 107374182400;

    public string? DefaultUnattendPath { get; set; }

    public string? ProxyUrl { get; set; }

    public string? PackerPath { get; set; }

    public string WinrmUsername { get; set; } = "Administrator";

    public string WinrmPassword { get; set; } = "Admin123!";

    public string HyperVSwitch { get; set; } = "Default Switch";

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}
