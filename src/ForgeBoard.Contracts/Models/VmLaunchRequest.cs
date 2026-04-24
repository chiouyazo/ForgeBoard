namespace ForgeBoard.Contracts.Models;

public sealed class VmLaunchRequest
{
    public string? VmName { get; set; }

    public int MemoryMb { get; set; } = 4096;

    public int CpuCount { get; set; } = 4;

    public List<VmNetworkAdapter>? Networks { get; set; }
}
