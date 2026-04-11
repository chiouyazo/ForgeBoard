using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.AspNetCore.Mvc;

namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Validates file paths, feed connectivity, Packer installations, and available VM builders.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ValidationController : ControllerBase
{
    private readonly IEnumerable<IFeedAdapter> _adapters;
    private readonly IPackerService _packerService;
    private readonly ILogger<ValidationController> _logger;

    public ValidationController(
        IEnumerable<IFeedAdapter> adapters,
        IPackerService packerService,
        ILogger<ValidationController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(packerService);
        ArgumentNullException.ThrowIfNull(logger);
        _adapters = adapters;
        _packerService = packerService;
        _logger = logger;
    }

    /// <summary>
    /// Validates whether a file system path exists and returns metadata about it.
    /// </summary>
    /// <param name="request">The path validation request containing the path to check.</param>
    /// <returns>An object with existence, type, and size information.</returns>
    /// <response code="200">Returns path validation results.</response>
    /// <response code="400">The path was not provided.</response>
    /// <response code="500">An internal error occurred while validating the path.</response>
    [HttpPost("path")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<object> ValidatePath([FromBody] PathValidationRequest request)
    {
        if (request is null || string.IsNullOrEmpty(request.Path))
        {
            return BadRequest("Path is required");
        }

        try
        {
            bool isDirectory = Directory.Exists(request.Path);
            bool isFile = System.IO.File.Exists(request.Path);
            bool exists = isDirectory || isFile;
            long sizeBytes = 0;

            if (isFile)
            {
                FileInfo info = new FileInfo(request.Path);
                sizeBytes = info.Length;
            }
            else if (isDirectory)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(request.Path);
                sizeBytes = dirInfo
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }

            return Ok(
                new
                {
                    exists,
                    isDirectory,
                    sizeBytes,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate path {Path}", request.Path);
            return Problem("Failed to validate path", statusCode: 500);
        }
    }

    /// <summary>
    /// Validates connectivity to a feed by testing its connection settings.
    /// </summary>
    /// <param name="feed">The feed to test connectivity for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the feed is reachable; otherwise false.</returns>
    /// <response code="200">Returns the connectivity test result.</response>
    /// <response code="400">The feed is missing or has an unsupported source type.</response>
    /// <response code="500">An internal error occurred while validating feed connectivity.</response>
    [HttpPost("feed")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<bool>> ValidateFeed(
        [FromBody] Feed feed,
        CancellationToken cancellationToken
    )
    {
        if (feed is null)
        {
            return BadRequest("Feed is required");
        }

        try
        {
            IFeedAdapter? adapter = _adapters.FirstOrDefault(a => a.SourceType == feed.SourceType);
            if (adapter is null)
            {
                return BadRequest($"No adapter found for feed type {feed.SourceType}");
            }

            bool result = await adapter.TestConnectivityAsync(feed, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate feed connectivity");
            return Problem("Failed to validate feed connectivity", statusCode: 500);
        }
    }

    /// <summary>
    /// Validates a Packer executable by checking its path and querying its version.
    /// </summary>
    /// <param name="request">The Packer validation request containing the executable path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A validation response indicating whether Packer is valid and its version.</returns>
    /// <response code="200">Returns the Packer validation result.</response>
    /// <response code="400">The Packer path was not provided.</response>
    [HttpPost("packer")]
    [ProducesResponseType(typeof(PackerValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PackerValidationResponse>> ValidatePacker(
        [FromBody] PackerValidationRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest("Packer path is required");
        }

        try
        {
            if (!System.IO.File.Exists(request.Path))
            {
                return Ok(
                    new PackerValidationResponse
                    {
                        Valid = false,
                        Error = $"File not found: {request.Path}",
                    }
                );
            }

            PackerRunnerConfig tempConfig = new PackerRunnerConfig { PackerPath = request.Path };

            string version = await _packerService.GetVersionAsync(tempConfig, cancellationToken);

            return Ok(new PackerValidationResponse { Valid = true, Version = version });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Packer validation failed for path {Path}", request.Path);

            return Ok(new PackerValidationResponse { Valid = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves the list of available VM builders (QEMU, Hyper-V) and their availability status.
    /// </summary>
    /// <returns>A list of available builders with their status and reason.</returns>
    /// <response code="200">Returns the list of available builders.</response>
    [HttpGet("available-builders")]
    [ProducesResponseType(typeof(List<AvailableBuilder>), StatusCodes.Status200OK)]
    public ActionResult<List<AvailableBuilder>> GetAvailableBuilders()
    {
        List<AvailableBuilder> builders = new List<AvailableBuilder>();

        builders.Add(CheckQemuAvailability());
        builders.Add(CheckHyperVAvailability());

        return Ok(builders);
    }

    private static AvailableBuilder CheckQemuAvailability()
    {
        string executable = OperatingSystem.IsWindows()
            ? "qemu-system-x86_64.exe"
            : "qemu-system-x86_64";

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            char separator = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (
                string directory in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries)
            )
            {
                string fullPath = Path.Combine(directory, executable);
                if (System.IO.File.Exists(fullPath))
                {
                    return new AvailableBuilder
                    {
                        Builder = PackerBuilder.Qemu,
                        IsAvailable = true,
                        Reason = $"Found at {fullPath}",
                    };
                }
            }
        }

        return new AvailableBuilder
        {
            Builder = PackerBuilder.Qemu,
            IsAvailable = false,
            Reason = $"{executable} not found in PATH",
        };
    }

    private static AvailableBuilder CheckHyperVAvailability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AvailableBuilder
            {
                Builder = PackerBuilder.HyperV,
                IsAvailable = false,
                Reason = "Hyper-V is only available on Windows",
            };
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization"
                );
            if (key is null)
            {
                return new AvailableBuilder
                {
                    Builder = PackerBuilder.HyperV,
                    IsAvailable = false,
                    Reason = "Hyper-V feature is not installed",
                };
            }

            System.Security.Principal.WindowsIdentity identity =
                System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal =
                new System.Security.Principal.WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(
                System.Security.Principal.WindowsBuiltInRole.Administrator
            );

            bool isHyperVAdmin = false;
            try
            {
                isHyperVAdmin = principal.IsInRole(
                    new System.Security.Principal.SecurityIdentifier("S-1-5-32-578")
                );
            }
            catch { }

            if (!isHyperVAdmin && identity.Groups is not null)
            {
                System.Security.Principal.SecurityIdentifier hyperVSid =
                    new System.Security.Principal.SecurityIdentifier("S-1-5-32-578");
                foreach (System.Security.Principal.IdentityReference group in identity.Groups)
                {
                    try
                    {
                        if (
                            group.Translate(typeof(System.Security.Principal.SecurityIdentifier))
                                is System.Security.Principal.SecurityIdentifier sid
                            && sid == hyperVSid
                        )
                        {
                            isHyperVAdmin = true;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (!isAdmin && !isHyperVAdmin)
            {
                return new AvailableBuilder
                {
                    Builder = PackerBuilder.HyperV,
                    IsAvailable = false,
                    Reason =
                        "Current user is not a member of Administrators or Hyper-V Administrators",
                };
            }

            return new AvailableBuilder
            {
                Builder = PackerBuilder.HyperV,
                IsAvailable = true,
                Reason = "Hyper-V installed and user has required permissions",
            };
        }
        catch (Exception)
        {
            return new AvailableBuilder
            {
                Builder = PackerBuilder.HyperV,
                IsAvailable = false,
                Reason = "Failed to detect Hyper-V status",
            };
        }
    }
}
