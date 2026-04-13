namespace ForgeBoard.Contracts.Models;

public sealed class StoragePathsUpdateRequest
{
    public string DataDirectory { get; set; } = string.Empty;

    public string TempDirectory { get; set; } = string.Empty;
}
