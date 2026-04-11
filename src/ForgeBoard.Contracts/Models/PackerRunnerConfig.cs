namespace ForgeBoard.Contracts.Models;

public sealed class PackerRunnerConfig
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PackerRunnerType RunnerType { get; set; } = PackerRunnerType.Local;

    public string? PackerPath { get; set; }

    public string? DockerImage { get; set; }

    public string? RemoteHost { get; set; }

    public int? RemotePort { get; set; }

    public string? WorkingDirectory { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
