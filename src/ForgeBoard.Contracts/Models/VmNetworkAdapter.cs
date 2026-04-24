namespace ForgeBoard.Contracts.Models;

public sealed class VmNetworkAdapter
{
    public string NetworkId { get; set; } = string.Empty;

    public string? StaticIp { get; set; }

    public string? Gateway { get; set; }

    public string? DnsServers { get; set; }

    public string? MacAddress { get; set; }

    public int? VlanId { get; set; }
}
