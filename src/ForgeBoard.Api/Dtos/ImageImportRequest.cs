namespace ForgeBoard.Api.Dtos;

public sealed class ImageImportRequest
{
    public string SourceId { get; set; } = string.Empty;

    public string RemotePath { get; set; } = string.Empty;
}
