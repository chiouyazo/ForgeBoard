namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Request model for validating a Packer executable.
/// </summary>
public sealed class PackerValidationRequest
{
    /// <summary>
    /// The file system path to the Packer executable.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
