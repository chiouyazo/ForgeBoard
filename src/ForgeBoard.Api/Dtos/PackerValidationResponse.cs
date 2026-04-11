namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Response model for Packer validation results.
/// </summary>
public sealed class PackerValidationResponse
{
    /// <summary>
    /// Whether the Packer executable is valid and functional.
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    /// The Packer version string, if validation succeeded.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The error message, if validation failed.
    /// </summary>
    public string? Error { get; set; }
}
