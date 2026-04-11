namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Request model for validating a file system path.
/// </summary>
public sealed class PathValidationRequest
{
    /// <summary>
    /// The file system path to validate.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
