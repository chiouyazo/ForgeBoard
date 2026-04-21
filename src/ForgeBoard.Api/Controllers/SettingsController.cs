using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
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
    private readonly IAppPaths _appPaths;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ForgeBoardDatabase db,
        IPackerService packerService,
        IAppPaths appPaths,
        IWebHostEnvironment hostEnvironment,
        IHostApplicationLifetime lifetime,
        ILogger<SettingsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(packerService);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _packerService = packerService;
        _appPaths = appPaths;
        _hostEnvironment = hostEnvironment;
        _lifetime = lifetime;
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

    /// <summary>
    /// Retrieves the resolved storage paths the API is currently using.
    /// </summary>
    [HttpGet("storage")]
    [ProducesResponseType(typeof(StoragePaths), StatusCodes.Status200OK)]
    public ActionResult<StoragePaths> GetStorage()
    {
        return Ok(
            new StoragePaths
            {
                DataDirectory = _appPaths.DataDirectory,
                TempDirectory = _appPaths.TempDirectory,
                CacheDirectory = _appPaths.CacheDirectory,
                ArtifactsDirectory = _appPaths.ArtifactsDirectory,
                WorkingDirectory = _appPaths.WorkingDirectory,
                LogsDirectory = _appPaths.LogsDirectory,
                DatabasePath = _appPaths.DatabasePath,
            }
        );
    }

    /// <summary>
    /// Updates the configured storage paths in appsettings.json. Requires an API restart to take effect.
    /// </summary>
    [HttpPut("storage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult UpdateStorage([FromBody] StoragePathsUpdateRequest request)
    {
        if (request is null)
        {
            return BadRequest("Request body is required");
        }

        string dataDir = request.DataDirectory.Trim();
        string tempDir = request.TempDirectory.Trim();

        if (!string.IsNullOrEmpty(dataDir) && !Path.IsPathFullyQualified(dataDir))
        {
            return BadRequest("DataDirectory must be an absolute path");
        }

        if (!string.IsNullOrEmpty(tempDir) && !Path.IsPathFullyQualified(tempDir))
        {
            return BadRequest("TempDirectory must be an absolute path");
        }

        try
        {
            string settingsFile = Path.Combine(
                _hostEnvironment.ContentRootPath,
                "appsettings.json"
            );
            JsonNode root;

            if (System.IO.File.Exists(settingsFile))
            {
                string existingJson = System.IO.File.ReadAllText(settingsFile);
                root = JsonNode.Parse(existingJson) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            JsonNode? section = root["ForgeBoard"];
            if (section is not JsonObject forgeBoardSection)
            {
                forgeBoardSection = new JsonObject();
                root["ForgeBoard"] = forgeBoardSection;
            }

            forgeBoardSection["DataDirectory"] = dataDir;
            forgeBoardSection["TempDirectory"] = tempDir;

            JsonSerializerOptions writeOptions = new JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(settingsFile, root.ToJsonString(writeOptions));

            _logger.LogInformation(
                "Updated storage paths in appsettings.json: DataDirectory={DataDir}, TempDirectory={TempDir}",
                string.IsNullOrEmpty(dataDir) ? "(default)" : dataDir,
                string.IsNullOrEmpty(tempDir) ? "(default)" : tempDir
            );

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update storage paths");
            return Problem("Failed to update storage paths: " + ex.Message, statusCode: 500);
        }
    }

    /// <summary>
    /// Searches for the Packer executable on the server.
    /// </summary>
    [HttpGet("detect-packer")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<string> DetectPacker()
    {
        string packerExe = "packer.exe";

        string? pathResult = SearchInPath(packerExe);
        if (pathResult is not null)
        {
            return Ok(pathResult);
        }

        List<string> candidates = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Packer",
                packerExe
            ),
            Path.Combine("C:\\HashiCorp\\Packer", packerExe),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "packer",
                packerExe
            ),
        };

        foreach (string candidate in candidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return Ok(candidate);
            }
        }

        return NotFound("Packer not found on server");
    }

    private static string? SearchInPath(string executable)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        string[] directories = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string directory in directories)
        {
            string fullPath = Path.Combine(directory, executable);
            if (System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Restarts the API process. Spawns a detached relauncher and shuts down gracefully.
    /// </summary>
    [HttpPost("restart")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult Restart()
    {
        try
        {
            ScheduleRestart();
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule API restart");
            return Problem("Failed to schedule API restart: " + ex.Message, statusCode: 500);
        }
    }

    private void ScheduleRestart()
    {
        if (WindowsServiceHelpers.IsWindowsService())
        {
            _logger.LogInformation("Restart requested - Windows service will be restarted by SCM");
            string serviceName = "ForgeBoard";
            string command =
                $"/c timeout /t 2 /nobreak >nul && sc.exe stop \"{serviceName}\" >nul && sc.exe start \"{serviceName}\" >nul";
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = command,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            return;
        }

        Process current = Process.GetCurrentProcess();
        string? exePath = current.MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("Cannot determine current process executable");
        }

        string workingDir = _hostEnvironment.ContentRootPath;
        string relauncherCommand;

        if (
            Path.GetFileName(exePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        )
        {
            string dllPath = Path.Combine(workingDir, "ForgeBoard.Api.dll");
            relauncherCommand =
                $"/c timeout /t 2 /nobreak >nul && start \"\" \"{exePath}\" \"{dllPath}\"";
        }
        else
        {
            relauncherCommand = $"/c timeout /t 2 /nobreak >nul && start \"\" \"{exePath}\"";
        }

        _logger.LogInformation("Restart requested - spawning relauncher and stopping");

        Process.Start(
            new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = relauncherCommand,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            _lifetime.StopApplication();
        });
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
