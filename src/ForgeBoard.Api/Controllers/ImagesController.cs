using ForgeBoard.Api.Dtos;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Manages base images, build artifacts, image publishing, and disk usage.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ImagesController : ControllerBase
{
    private readonly IImageManager _imageManager;
    private readonly ICacheService _cacheService;
    private readonly ArtifactPublisher _publisher;
    private readonly VmLauncher _vmLauncher;
    private readonly ForgeBoardDatabase _db;
    private readonly ILogger<ImagesController> _logger;

    public ImagesController(
        IImageManager imageManager,
        ICacheService cacheService,
        ArtifactPublisher publisher,
        VmLauncher vmLauncher,
        ForgeBoardDatabase db,
        ILogger<ImagesController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(imageManager);
        ArgumentNullException.ThrowIfNull(cacheService);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(vmLauncher);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        _imageManager = imageManager;
        _cacheService = cacheService;
        _publisher = publisher;
        _vmLauncher = vmLauncher;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Starts publishing an artifact to a feed as a background task.
    /// </summary>
    /// <param name="id">The artifact identifier to publish.</param>
    /// <param name="request">The publish request containing target feed details.</param>
    /// <returns>An accepted response with a message indicating that publishing has started.</returns>
    /// <response code="202">The publish operation was accepted and started in the background.</response>
    [HttpPost("artifacts/{id}/publish")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public ActionResult PublishArtifact(string id, [FromBody] PublishRequest request)
    {
        // Run as fire-and-forget background task - not tied to HTTP request lifecycle
        _ = Task.Run(async () =>
        {
            try
            {
                await _publisher.PublishAsync(id, request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background publish failed for artifact {ArtifactId}", id);
            }
        });

        return Accepted(
            new
            {
                message = "Publish started. Check progress via GET artifacts/{id}/publish-progress",
            }
        );
    }

    /// <summary>
    /// Retrieves the publish progress for an artifact.
    /// </summary>
    /// <param name="id">The artifact identifier.</param>
    /// <returns>The current publish progress status.</returns>
    /// <response code="200">Returns the publish progress.</response>
    [HttpGet("artifacts/{id}/publish-progress")]
    [ProducesResponseType(typeof(PublishProgress), StatusCodes.Status200OK)]
    public ActionResult<PublishProgress> GetPublishProgress(string id)
    {
        PublishProgress? progress = _publisher.GetProgress(id);
        if (progress is null)
        {
            return Ok(new PublishProgress { Status = "No active publish", IsComplete = true });
        }
        return Ok(progress);
    }

    /// <summary>
    /// Retrieves all currently active publish operations.
    /// </summary>
    /// <returns>A dictionary of artifact identifiers to their publish progress.</returns>
    /// <response code="200">Returns all active publish operations.</response>
    [HttpGet("artifacts/active-publishes")]
    [ProducesResponseType(typeof(Dictionary<string, PublishProgress>), StatusCodes.Status200OK)]
    public ActionResult<Dictionary<string, PublishProgress>> GetActivePublishes()
    {
        return Ok(_publisher.GetAllPublishes());
    }

    /// <summary>
    /// Requests cancellation of an active publish operation.
    /// </summary>
    /// <param name="id">The artifact identifier whose publish should be cancelled.</param>
    /// <returns>A message confirming the cancellation was requested.</returns>
    /// <response code="200">The cancellation was requested successfully.</response>
    [HttpPost("artifacts/{id}/cancel-publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult CancelPublish(string id)
    {
        _publisher.CancelPublish(id);
        return Ok(new { message = "Cancel requested" });
    }

    /// <summary>
    /// Dismisses a completed or failed publish operation from the active list.
    /// </summary>
    /// <param name="id">The artifact identifier to dismiss.</param>
    /// <returns>A message confirming the dismissal.</returns>
    /// <response code="200">The publish operation was dismissed.</response>
    [HttpPost("artifacts/{id}/dismiss-publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult DismissPublish(string id)
    {
        _publisher.DismissPublish(id);
        return Ok(new { message = "Dismissed" });
    }

    /// <summary>
    /// Retrieves the list of repositories available in a Nexus feed.
    /// </summary>
    /// <param name="feedId">The feed identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of repository names.</returns>
    /// <response code="200">Returns the repository names.</response>
    /// <response code="404">No feed with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while listing repositories.</response>
    [HttpGet("feeds/{feedId}/repositories")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<string>>> GetFeedRepositories(
        string feedId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            Feed? feed = _db.Feeds.FindById(feedId);
            if (feed is null)
                return NotFound();

            if (feed.SourceType != FeedType.Nexus)
            {
                return Ok(new List<string>());
            }

            ForgeBoard.Core.Services.Sources.NexusFeedAdapter? nexusAdapter = null;
            foreach (
                object adapter in HttpContext.RequestServices.GetServices<ForgeBoard.Contracts.Interfaces.IFeedAdapter>()
            )
            {
                if (adapter is ForgeBoard.Core.Services.Sources.NexusFeedAdapter na)
                {
                    nexusAdapter = na;
                    break;
                }
            }

            if (nexusAdapter is null)
                return Ok(new List<string>());

            List<string> repos = await nexusAdapter.ListRepositoriesAsync(feed, cancellationToken);
            return Ok(repos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list repositories for feed {FeedId}", feedId);
            return Problem("Failed to list repositories", statusCode: 500);
        }
    }

    /// <summary>
    /// Imports a base image from a feed source.
    /// </summary>
    /// <param name="request">The import request containing source and path details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created base image.</returns>
    /// <response code="201">The base image was imported successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="404">The specified feed source was not found.</response>
    /// <response code="500">An internal error occurred while importing the image.</response>
    [HttpPost("base/import")]
    [ProducesResponseType(typeof(BaseImage), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BaseImage>> ImportBaseImage(
        [FromBody] ImageImportRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request is null)
        {
            return BadRequest("Import request is required");
        }

        try
        {
            Feed? feed = _db.Feeds.FindById(request.SourceId);
            if (feed is null)
            {
                return NotFound($"Feed {request.SourceId} not found");
            }

            BaseImage image = new BaseImage
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Path.GetFileNameWithoutExtension(request.RemotePath),
                FileName = Path.GetFileName(request.RemotePath),
                SourceId = request.SourceId,
                Origin = ImageOrigin.Imported,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            BaseImage created = await _imageManager.CreateBaseImageAsync(image, cancellationToken);

            try
            {
                await _cacheService.EnsureCachedAsync(created, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to cache imported image {ImageId}, it will be cached on first use",
                    created.Id
                );
            }

            return CreatedAtAction(nameof(GetBaseImage), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import base image");
            return Problem("Failed to import base image", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves all base images.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all base images.</returns>
    /// <response code="200">Returns the list of base images.</response>
    /// <response code="500">An internal error occurred while retrieving base images.</response>
    [HttpGet("base")]
    [ProducesResponseType(typeof(List<BaseImage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<BaseImage>>> GetBaseImages(
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<BaseImage> images = await _imageManager.GetAllBaseImagesAsync(cancellationToken);
            return Ok(images);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get base images");
            return Problem("Failed to retrieve base images", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single base image by identifier.
    /// </summary>
    /// <param name="id">The base image identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching base image.</returns>
    /// <response code="200">Returns the base image.</response>
    /// <response code="404">No base image with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the base image.</response>
    [HttpGet("base/{id}")]
    [ProducesResponseType(typeof(BaseImage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BaseImage>> GetBaseImage(
        string id,
        CancellationToken cancellationToken
    )
    {
        try
        {
            BaseImage? image = await _imageManager.GetBaseImageAsync(id, cancellationToken);
            if (image is null)
            {
                return NotFound();
            }
            return Ok(image);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get base image {Id}", id);
            return Problem($"Failed to retrieve base image {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new base image record.
    /// </summary>
    /// <param name="image">The base image to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created base image.</returns>
    /// <response code="201">The base image was created successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="500">An internal error occurred while creating the base image.</response>
    [HttpPost("base")]
    [ProducesResponseType(typeof(BaseImage), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BaseImage>> CreateBaseImage(
        [FromBody] BaseImage image,
        CancellationToken cancellationToken
    )
    {
        if (image is null)
        {
            return BadRequest("Image is required");
        }

        if (string.IsNullOrEmpty(image.Name))
        {
            return BadRequest("Image name is required");
        }

        try
        {
            BaseImage created = await _imageManager.CreateBaseImageAsync(image, cancellationToken);
            return CreatedAtAction(nameof(GetBaseImage), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create base image");
            return Problem("Failed to create base image", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates an existing base image.
    /// </summary>
    /// <param name="id">The base image identifier.</param>
    /// <param name="image">The updated base image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated base image.</returns>
    /// <response code="200">The base image was updated successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="404">No base image with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while updating the base image.</response>
    [HttpPut("base/{id}")]
    [ProducesResponseType(typeof(BaseImage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BaseImage>> UpdateBaseImage(
        string id,
        [FromBody] BaseImage image,
        CancellationToken cancellationToken
    )
    {
        if (image is null)
        {
            return BadRequest("Image is required");
        }

        try
        {
            BaseImage? existing = await _imageManager.GetBaseImageAsync(id, cancellationToken);
            if (existing is null)
            {
                return NotFound();
            }

            image.Id = id;
            BaseImage updated = await _imageManager.UpdateBaseImageAsync(image, cancellationToken);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update base image {Id}", id);
            return Problem($"Failed to update base image {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a base image.
    /// </summary>
    /// <param name="id">The base image identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The base image was deleted successfully.</response>
    /// <response code="404">No base image with the specified identifier was found.</response>
    /// <response code="409">The base image cannot be deleted because it is in use by a build definition.</response>
    /// <response code="500">An internal error occurred while deleting the base image.</response>
    [HttpDelete("base/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteBaseImage(string id, CancellationToken cancellationToken)
    {
        try
        {
            BaseImage? existing = await _imageManager.GetBaseImageAsync(id, cancellationToken);
            if (existing is null)
            {
                return NotFound();
            }

            await _imageManager.DeleteBaseImageAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete base image {Id}", id);
            return Problem($"Failed to delete base image {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves all build artifacts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all image artifacts.</returns>
    /// <response code="200">Returns the list of artifacts.</response>
    /// <response code="500">An internal error occurred while retrieving artifacts.</response>
    [HttpGet("artifacts")]
    [ProducesResponseType(typeof(List<ImageArtifact>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ImageArtifact>>> GetArtifacts(
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<ImageArtifact> artifacts = await _imageManager.GetAllArtifactsAsync(
                cancellationToken
            );
            return Ok(artifacts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get artifacts");
            return Problem("Failed to retrieve artifacts", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single build artifact by identifier.
    /// </summary>
    /// <param name="id">The artifact identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching artifact.</returns>
    /// <response code="200">Returns the artifact.</response>
    /// <response code="404">No artifact with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the artifact.</response>
    [HttpGet("artifacts/{id}")]
    [ProducesResponseType(typeof(ImageArtifact), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ImageArtifact>> GetArtifact(
        string id,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ImageArtifact? artifact = await _imageManager.GetArtifactAsync(id, cancellationToken);
            if (artifact is null)
            {
                return NotFound();
            }
            return Ok(artifact);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get artifact {Id}", id);
            return Problem($"Failed to retrieve artifact {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a build artifact.
    /// </summary>
    /// <param name="id">The artifact identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The artifact was deleted successfully.</response>
    /// <response code="404">No artifact with the specified identifier was found.</response>
    /// <response code="409">The artifact cannot be deleted because it is in use.</response>
    /// <response code="500">An internal error occurred while deleting the artifact.</response>
    [HttpDelete("artifacts/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteArtifact(string id, CancellationToken cancellationToken)
    {
        try
        {
            ImageArtifact? existing = await _imageManager.GetArtifactAsync(id, cancellationToken);
            if (existing is null)
            {
                return NotFound();
            }

            await _imageManager.DeleteArtifactAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete artifact {Id}", id);
            return Problem($"Failed to delete artifact {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Starts launching a Hyper-V VM from an artifact in the background. Returns immediately.
    /// </summary>
    [HttpPost("artifacts/{id}/launch-vm")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult LaunchVm(string id, [FromBody] VmLaunchRequest? request)
    {
        try
        {
            _vmLauncher.StartLaunch(id, request);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch VM from artifact {Id}", id);
            return Problem($"Failed to launch VM: {ex.Message}", statusCode: 500);
        }
    }

    /// <summary>
    /// Gets the progress of a VM launch.
    /// </summary>
    [HttpGet("artifacts/{id}/launch-progress")]
    [ProducesResponseType(typeof(VmLaunchProgress), StatusCodes.Status200OK)]
    public ActionResult<VmLaunchProgress> GetLaunchProgress(string id)
    {
        VmLaunchProgress? progress = _vmLauncher.GetProgress(id);
        return Ok(
            progress ?? new VmLaunchProgress { Status = "No launch in progress", IsComplete = true }
        );
    }

    /// <summary>
    /// Gets all active and recent VM launches.
    /// </summary>
    [HttpGet("artifacts/active-launches")]
    [ProducesResponseType(typeof(Dictionary<string, VmLaunchProgress>), StatusCodes.Status200OK)]
    public ActionResult<Dictionary<string, VmLaunchProgress>> GetActiveLaunches()
    {
        return Ok(_vmLauncher.GetAllLaunches());
    }

    /// <summary>
    /// Dismisses a completed VM launch notification.
    /// </summary>
    [HttpPost("artifacts/{id}/dismiss-launch")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult DismissLaunch(string id)
    {
        _vmLauncher.DismissLaunch(id);
        return NoContent();
    }

    /// <summary>
    /// Retrieves all images (base images and artifacts) merged into a single list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A merged list of all available images.</returns>
    /// <response code="200">Returns the merged image list.</response>
    /// <response code="500">An internal error occurred while retrieving the merged list.</response>
    [HttpGet("all")]
    [ProducesResponseType(typeof(List<BaseImage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<BaseImage>>> GetAllMerged(
        CancellationToken cancellationToken
    )
    {
        try
        {
            List<BaseImage> merged = await _imageManager.GetAllMergedAsync(cancellationToken);
            return Ok(merged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get merged image list");
            return Problem("Failed to retrieve merged image list", statusCode: 500);
        }
    }

    /// <summary>
    /// Promotes a build artifact to a base image so it can be used in subsequent builds.
    /// </summary>
    /// <param name="artifactId">The artifact identifier to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created base image from the promoted artifact.</returns>
    /// <response code="201">The artifact was promoted to a base image successfully.</response>
    /// <response code="404">No artifact with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while promoting the artifact.</response>
    [HttpPost("promote/{artifactId}")]
    [ProducesResponseType(typeof(BaseImage), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BaseImage>> PromoteArtifact(
        string artifactId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            BaseImage promoted = await _imageManager.PromoteArtifactAsync(
                artifactId,
                cancellationToken
            );
            return CreatedAtAction(nameof(GetBaseImage), new { id = promoted.Id }, promoted);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to promote artifact {ArtifactId}", artifactId);
            return Problem($"Failed to promote artifact {artifactId}", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves disk usage information for cached images and artifacts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Disk usage statistics.</returns>
    /// <response code="200">Returns the disk usage information.</response>
    /// <response code="500">An internal error occurred while retrieving disk usage.</response>
    [HttpGet("disk-usage")]
    [ProducesResponseType(typeof(DiskUsageInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<DiskUsageInfo>> GetDiskUsage(CancellationToken cancellationToken)
    {
        try
        {
            DiskUsageInfo usage = await _imageManager.GetDiskUsageAsync(cancellationToken);
            return Ok(usage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get disk usage");
            return Problem("Failed to retrieve disk usage information", statusCode: 500);
        }
    }

    private void RegisterWithVmManager(string vmName, ImageArtifact artifact)
    {
        try
        {
            string vmManagerDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VmManager"
            );
            string managedVmsPath = Path.Combine(vmManagerDir, "managed-vms.json");
            Directory.CreateDirectory(vmManagerDir);

            List<Dictionary<string, object?>> entries = new List<Dictionary<string, object?>>();
            if (System.IO.File.Exists(managedVmsPath))
            {
                string existing = System.IO.File.ReadAllText(managedVmsPath);
                entries =
                    System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
                        existing
                    )
                    ?? new List<Dictionary<string, object?>>();
            }

            BuildDefinition? def = _db.BuildDefinitions.FindById(artifact.BuildDefinitionId);
            Dictionary<string, object?> origin = new Dictionary<string, object?>
            {
                ["ImageId"] = artifact.BuildDefinitionId,
                ["ImageName"] = def?.Name ?? artifact.Name,
                ["Version"] = def?.Version ?? "1.0",
                ["FeedId"] = null,
                ["FeedUrl"] = null,
                ["Repository"] = null,
            };

            entries.Add(new Dictionary<string, object?> { ["Name"] = vmName, ["Origin"] = origin });

            string json = System.Text.Json.JsonSerializer.Serialize(
                entries,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );
            System.IO.File.WriteAllText(managedVmsPath, json);

            _logger.LogInformation("Registered VM {VmName} with VmManager", vmName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to register VM {VmName} with VmManager (VmManager may not be installed)",
                vmName
            );
        }
    }
}
