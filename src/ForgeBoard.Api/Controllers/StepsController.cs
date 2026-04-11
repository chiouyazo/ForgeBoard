using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.AspNetCore.Mvc;

namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Manages the build step library for reusable provisioning steps.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class StepsController : ControllerBase
{
    private readonly ForgeBoardDatabase _db;
    private readonly ILogger<StepsController> _logger;

    public StepsController(ForgeBoardDatabase db, ILogger<StepsController> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all step library entries.
    /// </summary>
    /// <returns>A list of all step library entries.</returns>
    /// <response code="200">Returns the list of step library entries.</response>
    /// <response code="500">An internal error occurred while retrieving step library entries.</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<BuildStepLibraryEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<List<BuildStepLibraryEntry>> GetAll()
    {
        try
        {
            List<BuildStepLibraryEntry> entries = _db.StepLibrary.FindAll().ToList();
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get step library entries");
            return Problem("Failed to retrieve step library entries", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single step library entry by identifier.
    /// </summary>
    /// <param name="id">The step library entry identifier.</param>
    /// <returns>The matching step library entry.</returns>
    /// <response code="200">Returns the step library entry.</response>
    /// <response code="404">No step library entry with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the step library entry.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BuildStepLibraryEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildStepLibraryEntry> Get(string id)
    {
        try
        {
            BuildStepLibraryEntry? entry = _db.StepLibrary.FindById(id);
            if (entry is null)
            {
                return NotFound();
            }
            return Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get step library entry {Id}", id);
            return Problem($"Failed to retrieve step library entry {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new step library entry.
    /// </summary>
    /// <param name="entry">The step library entry to create.</param>
    /// <returns>The newly created step library entry.</returns>
    /// <response code="201">The step library entry was created successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="500">An internal error occurred while creating the step library entry.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BuildStepLibraryEntry), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildStepLibraryEntry> Create([FromBody] BuildStepLibraryEntry entry)
    {
        if (entry is null)
        {
            return BadRequest("Step library entry is required");
        }

        if (string.IsNullOrEmpty(entry.Name))
        {
            return BadRequest("Step name is required");
        }

        try
        {
            if (string.IsNullOrEmpty(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString("N");
            }
            entry.CreatedAt = DateTimeOffset.UtcNow;

            _db.StepLibrary.Insert(entry);

            return CreatedAtAction(nameof(Get), new { id = entry.Id }, entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create step library entry");
            return Problem("Failed to create step library entry", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates an existing step library entry.
    /// </summary>
    /// <param name="id">The step library entry identifier.</param>
    /// <param name="entry">The updated step library entry.</param>
    /// <returns>The updated step library entry.</returns>
    /// <response code="200">The step library entry was updated successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="404">No step library entry with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while updating the step library entry.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(BuildStepLibraryEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildStepLibraryEntry> Update(
        string id,
        [FromBody] BuildStepLibraryEntry entry
    )
    {
        if (entry is null)
        {
            return BadRequest("Step library entry is required");
        }

        try
        {
            BuildStepLibraryEntry? existing = _db.StepLibrary.FindById(id);
            if (existing is null)
            {
                return NotFound();
            }

            entry.Id = id;
            entry.ModifiedAt = DateTimeOffset.UtcNow;

            _db.StepLibrary.Update(entry);

            return Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update step library entry {Id}", id);
            return Problem($"Failed to update step library entry {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Exports all step library entries for backup or sharing.
    /// </summary>
    /// <returns>A list of all step library entries.</returns>
    /// <response code="200">Returns all step library entries for export.</response>
    /// <response code="500">An internal error occurred while exporting the step library.</response>
    [HttpGet("export")]
    [ProducesResponseType(typeof(List<BuildStepLibraryEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<List<BuildStepLibraryEntry>> ExportAll()
    {
        try
        {
            List<BuildStepLibraryEntry> entries = _db.StepLibrary.FindAll().ToList();
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export step library");
            return Problem("Failed to export step library", statusCode: 500);
        }
    }

    /// <summary>
    /// Exports a single step library entry for backup or sharing.
    /// </summary>
    /// <param name="id">The step library entry identifier.</param>
    /// <returns>The step library entry for export.</returns>
    /// <response code="200">Returns the step library entry for export.</response>
    /// <response code="404">No step library entry with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while exporting the step.</response>
    [HttpGet("{id}/export")]
    [ProducesResponseType(typeof(BuildStepLibraryEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildStepLibraryEntry> ExportSingle(string id)
    {
        try
        {
            BuildStepLibraryEntry? entry = _db.StepLibrary.FindById(id);
            if (entry is null)
            {
                return NotFound();
            }
            return Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export step {Id}", id);
            return Problem($"Failed to export step {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Imports step library entries from a previously exported list.
    /// </summary>
    /// <param name="entries">The list of step library entries to import.</param>
    /// <returns>The list of imported step library entries with new identifiers.</returns>
    /// <response code="200">The step library entries were imported successfully.</response>
    /// <response code="400">The request body is missing or empty.</response>
    /// <response code="500">An internal error occurred while importing step library entries.</response>
    [HttpPost("import")]
    [ProducesResponseType(typeof(List<BuildStepLibraryEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<List<BuildStepLibraryEntry>> Import(
        [FromBody] List<BuildStepLibraryEntry> entries
    )
    {
        if (entries is null || entries.Count == 0)
        {
            return BadRequest("At least one step entry is required");
        }

        try
        {
            List<BuildStepLibraryEntry> imported = new List<BuildStepLibraryEntry>();
            foreach (BuildStepLibraryEntry entry in entries)
            {
                entry.Id = Guid.NewGuid().ToString("N");
                entry.CreatedAt = DateTimeOffset.UtcNow;
                entry.ModifiedAt = null;
                _db.StepLibrary.Insert(entry);
                imported.Add(entry);
            }

            _logger.LogInformation("Imported {Count} step library entries", imported.Count);
            return Ok(imported);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import step library entries");
            return Problem("Failed to import step library entries", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a duplicate of an existing step library entry.
    /// </summary>
    /// <param name="id">The step library entry identifier to duplicate.</param>
    /// <returns>The newly created duplicate step library entry.</returns>
    /// <response code="201">The step library entry was duplicated successfully.</response>
    /// <response code="404">No step library entry with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while duplicating the step.</response>
    [HttpPost("{id}/duplicate")]
    [ProducesResponseType(typeof(BuildStepLibraryEntry), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<BuildStepLibraryEntry> Duplicate(string id)
    {
        try
        {
            BuildStepLibraryEntry? source = _db.StepLibrary.FindById(id);
            if (source is null)
            {
                return NotFound();
            }

            BuildStepLibraryEntry duplicate = new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"{source.Name} (Copy)",
                Description = source.Description,
                StepType = source.StepType,
                Content = source.Content,
                DefaultTimeoutSeconds = source.DefaultTimeoutSeconds,
                ExpectReboot = source.ExpectReboot,
                Tags = new List<string>(source.Tags),
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _db.StepLibrary.Insert(duplicate);
            return CreatedAtAction(nameof(Get), new { id = duplicate.Id }, duplicate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to duplicate step {Id}", id);
            return Problem($"Failed to duplicate step {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a step library entry.
    /// </summary>
    /// <param name="id">The step library entry identifier.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The step library entry was deleted successfully.</response>
    /// <response code="404">No step library entry with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while deleting the step library entry.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult Delete(string id)
    {
        try
        {
            BuildStepLibraryEntry? entry = _db.StepLibrary.FindById(id);
            if (entry is null)
            {
                return NotFound();
            }

            _db.StepLibrary.Delete(id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete step library entry {Id}", id);
            return Problem($"Failed to delete step library entry {id}", statusCode: 500);
        }
    }
}
