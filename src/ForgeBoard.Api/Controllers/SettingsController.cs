using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Manages application settings including Packer path configuration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class SettingsController : ControllerBase
{
    private readonly ForgeBoardDatabase _db;
    private readonly IPackerService _packerService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ForgeBoardDatabase db,
        IPackerService packerService,
        ILogger<SettingsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(packerService);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _packerService = packerService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the current application settings.
    /// </summary>
    /// <returns>The application settings, creating defaults if none exist.</returns>
    /// <response code="200">Returns the application settings.</response>
    /// <response code="500">An internal error occurred while retrieving settings.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AppSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<AppSettings> Get()
    {
        try
        {
            AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            if (settings is null)
            {
                settings = new AppSettings();
                _db.AppSettings.Insert(settings);
            }
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get application settings");
            return Problem("Failed to retrieve application settings", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves the version of Packer at the specified path.
    /// </summary>
    /// <param name="path">The file system path to the Packer executable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Packer version string.</returns>
    /// <response code="200">Returns the Packer version as plain text.</response>
    /// <response code="400">The Packer path was not provided.</response>
    /// <response code="500">An internal error occurred while querying the Packer version.</response>
    [HttpGet("packer-version")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<string>> GetPackerVersion(
        [FromQuery] string path,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("Packer path is required");
        }

        try
        {
            PackerRunnerConfig tempConfig = new PackerRunnerConfig { PackerPath = path };

            string version = await _packerService.GetVersionAsync(tempConfig, cancellationToken);
            return Content(version, "text/plain");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get packer version at {Path}", path);
            return Problem($"Failed to get packer version: {ex.Message}", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates the application settings.
    /// </summary>
    /// <param name="settings">The updated application settings.</param>
    /// <returns>The updated application settings.</returns>
    /// <response code="200">The settings were updated successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="500">An internal error occurred while updating settings.</response>
    [HttpPut]
    [ProducesResponseType(typeof(AppSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<AppSettings> Update([FromBody] AppSettings settings)
    {
        if (settings is null)
        {
            return BadRequest("Settings are required");
        }

        try
        {
            settings.Id = KnownIds.DefaultSettings;
            settings.ModifiedAt = DateTimeOffset.UtcNow;

            AppSettings? existing = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            if (existing is null)
            {
                _db.AppSettings.Insert(settings);
            }
            else
            {
                _db.AppSettings.Update(settings);
            }

            EnsureAutoLocalRunner(settings);

            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update application settings");
            return Problem("Failed to update application settings", statusCode: 500);
        }
    }

    private void EnsureAutoLocalRunner(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PackerPath))
        {
            return;
        }

        PackerRunnerConfig? existing = _db.PackerRunners.FindById(KnownIds.AutoLocalRunner);
        if (existing is null)
        {
            PackerRunnerConfig runner = new PackerRunnerConfig
            {
                Id = KnownIds.AutoLocalRunner,
                Name = "Local Packer",
                PackerPath = settings.PackerPath,
            };
            _db.PackerRunners.Insert(runner);
            _logger.LogInformation(
                "Created auto-local Packer runner with path {Path}",
                settings.PackerPath
            );
        }
        else
        {
            existing.PackerPath = settings.PackerPath;
            _db.PackerRunners.Update(existing);
            _logger.LogInformation(
                "Updated auto-local Packer runner with path {Path}",
                settings.PackerPath
            );
        }
    }
}
