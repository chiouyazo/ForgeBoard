using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.AspNetCore.Mvc;

namespace ForgeBoard.Api.Controllers;

/// <summary>
/// Manages image feed connections for browsing and importing VM images.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class FeedsController : ControllerBase
{
    private readonly IFeedManager _feedManager;
    private readonly ILogger<FeedsController> _logger;

    public FeedsController(IFeedManager feedManager, ILogger<FeedsController> logger)
    {
        ArgumentNullException.ThrowIfNull(feedManager);
        ArgumentNullException.ThrowIfNull(logger);
        _feedManager = feedManager;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all configured feeds.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all feeds.</returns>
    /// <response code="200">Returns the list of feeds.</response>
    /// <response code="500">An internal error occurred while retrieving feeds.</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<Feed>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<Feed>>> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            List<Feed> feeds = await _feedManager.GetAllAsync(cancellationToken);
            return Ok(feeds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all feeds");
            return Problem("Failed to retrieve feeds", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single feed by identifier.
    /// </summary>
    /// <param name="id">The feed identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching feed.</returns>
    /// <response code="200">Returns the feed.</response>
    /// <response code="404">No feed with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while retrieving the feed.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Feed), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Feed>> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            Feed? feed = await _feedManager.GetAsync(id, cancellationToken);
            if (feed is null)
            {
                return NotFound();
            }
            return Ok(feed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feed {Id}", id);
            return Problem($"Failed to retrieve feed {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new feed.
    /// </summary>
    /// <param name="feed">The feed to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created feed.</returns>
    /// <response code="201">The feed was created successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="500">An internal error occurred while creating the feed.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Feed), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Feed>> Create(
        [FromBody] Feed feed,
        CancellationToken cancellationToken
    )
    {
        if (feed is null)
        {
            return BadRequest("Feed is required");
        }

        if (string.IsNullOrEmpty(feed.Name))
        {
            return BadRequest("Feed name is required");
        }

        if (string.IsNullOrEmpty(feed.ConnectionString))
        {
            return BadRequest("Connection string is required");
        }

        try
        {
            Feed created = await _feedManager.CreateAsync(feed, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create feed");
            return Problem("Failed to create feed", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates an existing feed.
    /// </summary>
    /// <param name="id">The feed identifier.</param>
    /// <param name="feed">The updated feed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated feed.</returns>
    /// <response code="200">The feed was updated successfully.</response>
    /// <response code="400">The request body is missing or invalid.</response>
    /// <response code="404">No feed with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while updating the feed.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Feed), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Feed>> Update(
        string id,
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
            Feed? existing = await _feedManager.GetAsync(id, cancellationToken);
            if (existing is null)
            {
                return NotFound();
            }

            feed.Id = id;
            Feed updated = await _feedManager.UpdateAsync(feed, cancellationToken);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update feed {Id}", id);
            return Problem($"Failed to update feed {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a feed.
    /// </summary>
    /// <param name="id">The feed identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The feed was deleted successfully.</response>
    /// <response code="404">No feed with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while deleting the feed.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            Feed? existing = await _feedManager.GetAsync(id, cancellationToken);
            if (existing is null)
            {
                return NotFound();
            }

            await _feedManager.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete feed {Id}", id);
            return Problem($"Failed to delete feed {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Browses available images in a feed.
    /// </summary>
    /// <param name="id">The feed identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of images available in the feed.</returns>
    /// <response code="200">Returns the list of images in the feed.</response>
    /// <response code="404">No feed with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while browsing feed images.</response>
    [HttpGet("{id}/browse")]
    [ProducesResponseType(typeof(List<FeedImage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<FeedImage>>> BrowseImages(
        string id,
        CancellationToken cancellationToken
    )
    {
        try
        {
            Feed? feed = await _feedManager.GetAsync(id, cancellationToken);
            if (feed is null)
            {
                return NotFound();
            }

            List<FeedImage> images = await _feedManager.BrowseImagesAsync(id, cancellationToken);
            return Ok(images);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to browse images for feed {Id}", id);
            return Problem($"Failed to browse images for feed {id}", statusCode: 500);
        }
    }

    /// <summary>
    /// Tests connectivity to a feed.
    /// </summary>
    /// <param name="id">The feed identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the feed is reachable; otherwise false.</returns>
    /// <response code="200">Returns the connectivity test result.</response>
    /// <response code="404">No feed with the specified identifier was found.</response>
    /// <response code="500">An internal error occurred while testing connectivity.</response>
    [HttpPost("{id}/test")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<bool>> TestConnectivity(
        string id,
        CancellationToken cancellationToken
    )
    {
        try
        {
            Feed? feed = await _feedManager.GetAsync(id, cancellationToken);
            if (feed is null)
            {
                return NotFound();
            }

            bool result = await _feedManager.TestConnectivityAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connectivity for feed {Id}", id);
            return Problem($"Failed to test connectivity for feed {id}", statusCode: 500);
        }
    }
}
