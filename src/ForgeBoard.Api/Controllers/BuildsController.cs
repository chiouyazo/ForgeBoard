using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services.Build;
using Microsoft.AspNetCore.Mvc;

namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Manages Packer build definitions, executions, and build logs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class BuildsController : ControllerBase
{
    private readonly ForgeBoardDatabase _db;
    private readonly IBuildOrchestrator _orchestrator;
    private readonly IPackerTemplateGenerator _templateGenerator;
    private readonly IImageManager _imageManager;
    private readonly BuildReadinessChecker _readinessChecker;
    private readonly ILogger<BuildsController> _logger;

    public BuildsController(
        ForgeBoardDatabase db,
        IBuildOrchestrator orchestrator,
        IPackerTemplateGenerator templateGenerator,
        IImageManager imageManager,
        BuildReadinessChecker readinessChecker,
        ILogger<BuildsController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(templateGenerator);
        ArgumentNullException.ThrowIfNull(imageManager);
        ArgumentNullException.ThrowIfNull(readinessChecker);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _orchestrator = orchestrator;
        _templateGenerator = templateGenerator;
        _imageManager = imageManager;
        _readinessChecker = readinessChecker;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether a build definition is ready to execute.
    /// </summary>
    /// <param name="id">The build definition identifier.</param>
    /// <returns>A readiness result indicating any blocking issues.</returns>
    /// <response code="200">Readiness check completed successfully.</response>
    /// <response code="500">An internal error occurred during the readiness check.</response>
    [HttpPost("definitions/{id}/check-readiness")]
    [ProducesResponseType(typeof(BuildReadinessResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildReadinessResult> CheckReadiness(string id)
    {
        try
        {
            BuildReadinessResult result = _readinessChecker.Check(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check build readiness for {DefinitionId}", id);
            return Problem("Failed to check build readiness", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves all build definitions.
    /// </summary>
    /// <returns>A list of all build definitions.</returns>
    /// <response code="200">Returns the list of build definitions.</response>
    /// <response code="500">An internal error occurred while retrieving build definitions.</response>
    [HttpGet("definitions")]
    [ProducesResponseType(typeof(List<BuildDefinition>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<List<BuildDefinition>> GetDefinitions()
    {
        try
        {
            List<BuildDefinition> definitions = _db.BuildDefinitions.FindAll().ToList();
            return Ok(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get build definitions");
            return Problem("Failed to retrieve build definitions", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single build definition by identifier.
    /// </summary>
    /// <param name="id">The build definition identifier.</param>
    /// <returns>The matching build definition.</returns>
    /// <response code="200">Returns the build definition.</response>
    /// <response code="404">No build definition with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the build definition.</response>
    [HttpGet("definitions/{id}")]
    [ProducesResponseType(typeof(BuildDefinition), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildDefinition> GetDefinition(string id)
    {
        try
        {
            BuildDefinition? definition = _db.BuildDefinitions.FindById(id);
            if (definition is null)
            {
                return NotFound();
            }
            return Ok(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get build definition {Id}", id);
            return Problem($"Failed to retrieve build definition {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new build definition.
    /// </summary>
    /// <param name="definition">The build definition to create.</param>
    /// <returns>The newly created build definition.</returns>
    /// <response code="201">The build definition was created successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="500">An internal error occurred while creating the build definition.</response>
    [HttpPost("definitions")]
    [ProducesResponseType(typeof(BuildDefinition), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildDefinition> CreateDefinition([FromBody] BuildDefinition definition)
    {
        if (definition is null)
        {
            return BadRequest("Build definition is required");
        }

        if (string.IsNullOrEmpty(definition.Name))
        {
            return BadRequest("Build definition name is required");
        }

        try
        {
            if (string.IsNullOrEmpty(definition.Id))
            {
                definition.Id = Guid.NewGuid().ToString("N");
            }
            definition.CreatedAt = DateTimeOffset.UtcNow;
            EnsureStepIds(definition);

            _db.BuildDefinitions.Insert(definition);

            return CreatedAtAction(nameof(GetDefinition), new { id = definition.Id }, definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create build definition");
            return Problem("Failed to create build definition", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates an existing build definition.
    /// </summary>
    /// <param name="id">The build definition identifier.</param>
    /// <param name="definition">The updated build definition.</param>
    /// <returns>The updated build definition.</returns>
    /// <response code="200">The build definition was updated successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="404">No build definition with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while updating the build definition.</response>
    [HttpPut("definitions/{id}")]
    [ProducesResponseType(typeof(BuildDefinition), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildDefinition> UpdateDefinition(
        string id,
        [FromBody] BuildDefinition definition
    )
    {
        if (definition is null)
        {
            return BadRequest("Build definition is required");
        }

        try
        {
            BuildDefinition? existing = _db.BuildDefinitions.FindById(id);
            if (existing is null)
            {
                return NotFound();
            }

            definition.Id = id;
            definition.ModifiedAt = DateTimeOffset.UtcNow;
            EnsureStepIds(definition);

            _db.BuildDefinitions.Update(definition);

            return Ok(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update build definition {Id}", id);
            return Problem($"Failed to update build definition {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a build definition and its associated data.
    /// </summary>
    /// <param name="id">The build definition identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The build definition was deleted successfully.</response>
    /// <response code="404">No build definition with the specified identifier was found.</response>
    /// <response code="409">The build definition cannot be deleted because it is in use.</response>
    /// <response code="500">An internal error occurred while deleting the build definition.</response>
    [HttpDelete("definitions/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteDefinition(string id, CancellationToken cancellationToken)
    {
        try
        {
            BuildDefinition? definition = _db.BuildDefinitions.FindById(id);
            if (definition is null)
            {
                return NotFound();
            }

            await _orchestrator.DeleteDefinitionAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete build definition {Id}", id);
            return Problem($"Failed to delete build definition {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves build executions, optionally filtered by definition identifier.
    /// </summary>
    /// <param name="definitionId">Optional build definition identifier to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of build executions.</returns>
    /// <response code="200">Returns the list of build executions.</response>
    /// <response code="500">An internal error occurred while retrieving build executions.</response>
    [HttpGet("executions")]
    [ProducesResponseType(typeof(List<BuildExecution>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<BuildExecution>>> GetExecutions(
        [FromQuery] string? definitionId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<BuildExecution> executions = await _orchestrator.GetExecutionsAsync(
                definitionId,
                cancellationToken
            );
            return Ok(executions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get build executions");
            return Problem("Failed to retrieve build executions", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single build execution by identifier.
    /// </summary>
    /// <param name="id">The build execution identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching build execution.</returns>
    /// <response code="200">Returns the build execution.</response>
    /// <response code="404">No build execution with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the build execution.</response>
    [HttpGet("executions/{id}")]
    [ProducesResponseType(typeof(BuildExecution), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BuildExecution>> GetExecution(
        string id,
        CancellationToken cancellationToken
    )
    {
        try
        {
            BuildExecution? execution = await _orchestrator.GetExecutionAsync(
                id,
                cancellationToken
            );
            if (execution is null)
            {
                return NotFound();
            }
            return Ok(execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get build execution {Id}", id);
            return Problem($"Failed to retrieve build execution {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Starts a new build execution for the specified definition.
    /// </summary>
    /// <param name="definitionId">The build definition identifier to start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created build execution.</returns>
    /// <response code="201">The build was started successfully.</response>
    /// <response code="400">The build cannot be started due to a precondition failure.</response>
    /// <response code="404">No build definition with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while starting the build.</response>
    [HttpPost("executions/{definitionId}/start")]
    [ProducesResponseType(typeof(BuildExecution), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BuildExecution>> StartBuild(
        string definitionId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            BuildDefinition? definition = _db.BuildDefinitions.FindById(definitionId);
            if (definition is null)
            {
                return NotFound();
            }

            BuildExecution execution = await _orchestrator.StartBuildAsync(
                definitionId,
                cancellationToken
            );
            return CreatedAtAction(nameof(GetExecution), new { id = execution.Id }, execution);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Cannot start build for definition {DefinitionId}",
                definitionId
            );
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start build for definition {DefinitionId}",
                definitionId
            );
            return Problem($"Failed to start build for definition {definitionId}", statusCode: 500);
        }
    }

    /// <summary>
    /// Cancels a running build execution.
    /// </summary>
    /// <param name="id">The build execution identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The build was cancelled successfully.</response>
    /// <response code="404">No build execution with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while cancelling the build.</response>
    [HttpPost("executions/{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CancelBuild(string id, CancellationToken cancellationToken)
    {
        try
        {
            BuildExecution? execution = await _orchestrator.GetExecutionAsync(
                id,
                cancellationToken
            );
            if (execution is null)
            {
                return NotFound();
            }

            await _orchestrator.CancelBuildAsync(id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel build execution {Id}", id);
            return Problem($"Failed to cancel build execution {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a build execution record.
    /// </summary>
    /// <param name="id">The build execution identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The build execution was deleted successfully.</response>
    /// <response code="404">No build execution with the specified identifier was found.</response>
    /// <response code="409">The build execution cannot be deleted because it is still running.</response>
    /// <response code="500">An internal error occurred while deleting the build execution.</response>
    [HttpDelete("executions/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteExecution(string id, CancellationToken cancellationToken)
    {
        try
        {
            BuildExecution? execution = await _orchestrator.GetExecutionAsync(
                id,
                cancellationToken
            );
            if (execution is null)
            {
                return NotFound();
            }

            await _orchestrator.DeleteExecutionAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete build execution {Id}", id);
            return Problem($"Failed to delete build execution {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves log entries for a build execution.
    /// </summary>
    /// <param name="id">The build execution identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of log entries for the build execution.</returns>
    /// <response code="200">Returns the build log entries.</response>
    /// <response code="404">No build execution with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the logs.</response>
    [HttpGet("executions/{id}/logs")]
    [ProducesResponseType(typeof(List<BuildLogEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<BuildLogEntry>>> GetLogs(
        string id,
        CancellationToken cancellationToken
    )
    {
        try
        {
            BuildExecution? execution = await _orchestrator.GetExecutionAsync(
                id,
                cancellationToken
            );
            if (execution is null)
            {
                return NotFound();
            }

            List<BuildLogEntry> logs = await _orchestrator.GetLogsAsync(id, cancellationToken);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get logs for build execution {Id}", id);
            return Problem($"Failed to retrieve logs for build execution {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Generates a preview of the Packer HCL template and step descriptions for a build definition.
    /// </summary>
    /// <param name="definition">The build definition to preview.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An object containing the generated HCL template and step descriptions.</returns>
    /// <response code="200">Returns the build preview with HCL and step descriptions.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="404">The referenced base image or artifact was not found.</response>
    /// <response code="500">An internal error occurred while generating the preview.</response>
    [HttpPost("preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<object>> PreviewBuild(
        [FromBody] BuildDefinition definition,
        CancellationToken cancellationToken
    )
    {
        if (definition is null)
        {
            return BadRequest("Build definition is required");
        }

        try
        {
            BaseImage? baseImage;
            if (definition.BaseImageId.StartsWith(BaseImagePrefixes.BuildChain))
            {
                string chainedDefId = definition.BaseImageId[BaseImagePrefixes.BuildChain.Length..];
                BuildDefinition? chainedDef = _db.BuildDefinitions.FindById(chainedDefId);
                baseImage = new BaseImage
                {
                    Id = definition.BaseImageId,
                    Name = chainedDef is not null
                        ? $"[Build] {chainedDef.Name}"
                        : "Chained build output",
                    FileName = "chained-output.vhdx",
                    ImageFormat = "vhdx",
                    LocalCachePath = "chained-output.vhdx",
                    IsCached = false,
                };
            }
            else if (definition.BaseImageId.StartsWith(BaseImagePrefixes.Artifact))
            {
                string artifactId = definition.BaseImageId[BaseImagePrefixes.Artifact.Length..];
                ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
                if (artifact is null)
                {
                    return NotFound($"Artifact {artifactId} not found");
                }
                baseImage = new BaseImage
                {
                    Id = definition.BaseImageId,
                    Name = artifact.Name,
                    FileName = Path.GetFileName(artifact.FilePath),
                    ImageFormat = artifact.Format,
                    LocalCachePath = artifact.FilePath,
                    IsCached = true,
                };
            }
            else
            {
                baseImage = await _imageManager.GetBaseImageAsync(
                    definition.BaseImageId,
                    cancellationToken
                );
                if (baseImage is null)
                {
                    return NotFound($"Base image {definition.BaseImageId} not found");
                }
            }

            string hcl = _templateGenerator.GenerateHcl(
                definition,
                baseImage,
                "<output_directory>"
            );
            List<string> steps = _templateGenerator.GenerateStepDescription(definition, baseImage);

            return Ok(new { hcl, steps });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate build preview");
            return Problem("Failed to generate build preview", statusCode: 500);
        }
    }

    private static void EnsureStepIds(BuildDefinition definition)
    {
        foreach (BuildStep step in definition.Steps)
        {
            if (string.IsNullOrEmpty(step.Id))
            {
                step.Id = Guid.NewGuid().ToString("N");
            }
            step.BuildDefinitionId = definition.Id;
        }
    }
}
