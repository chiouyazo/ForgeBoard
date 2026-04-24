namespace ForgeBoard.Contracts.Models;

public sealed class BuildDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string BaseImageId { get; set; } = string.Empty;

    public PackerBuilder Builder { get; set; } = PackerBuilder.Qemu;

    public string PackerRunnerConfigId { get; set; } = string.Empty;

    public int MemoryMb { get; set; } = BuildDefaults.DefaultMemoryMb;

    public int CpuCount { get; set; } = BuildDefaults.DefaultCpuCount;

    public long DiskSizeMb { get; set; } = BuildDefaults.DefaultDiskSizeMb;

    public string OutputFormat { get; set; } = "qcow2";

    public string Version { get; set; } = "1.0";

    public string? UnattendPath { get; set; }

    public List<BuildStep> Steps { get; set; } = new List<BuildStep>();

    public List<string> Tags { get; set; } = new List<string>();

    public List<string> PostProcessors { get; set; } = new List<string>();

    public List<VmNetworkAdapter>? Networks { get; set; }

    public string? NetworkFeedId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ModifiedAt { get; set; }
}
