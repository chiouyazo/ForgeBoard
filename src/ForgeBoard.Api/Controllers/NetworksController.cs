using System.Text;
using System.Text.Json;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.AspNetCore.Mvc;

namespace ForgeBoard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class NetworksController : ControllerBase
{
    private readonly ForgeBoardDatabase _db;
    private readonly IEnumerable<IFeedAdapter> _adapters;
    private readonly ILogger<NetworksController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public NetworksController(
        ForgeBoardDatabase db,
        IEnumerable<IFeedAdapter> adapters,
        ILogger<NetworksController> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _adapters = adapters;
        _logger = logger;
    }

    [HttpGet("{feedId}")]
    public async Task<ActionResult<List<NetworkDefinition>>> GetNetworks(
        string feedId,
        [FromQuery] string? repository,
        CancellationToken cancellationToken
    )
    {
        try
        {
            (Feed feed, IFeedAdapter adapter) = ResolveFeed(feedId, repository);
            List<NetworkDefinition> networks = await LoadNetworksAsync(
                feed,
                adapter,
                cancellationToken
            );
            return Ok(networks);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load networks from feed {FeedId}", feedId);
            return Problem($"Failed to load networks: {ex.Message}", statusCode: 500);
        }
    }

    [HttpPost("{feedId}")]
    public async Task<ActionResult<NetworkDefinition>> CreateNetwork(
        string feedId,
        [FromQuery] string? repository,
        [FromBody] NetworkDefinition network,
        CancellationToken cancellationToken
    )
    {
        if (network is null)
        {
            return BadRequest("Network definition is required");
        }

        try
        {
            (Feed feed, IFeedAdapter adapter) = ResolveFeed(feedId, repository);
            List<NetworkDefinition> networks = await LoadNetworksAsync(
                feed,
                adapter,
                cancellationToken
            );

            if (string.IsNullOrEmpty(network.Id))
            {
                network.Id = network.Name.ToLowerInvariant().Replace(" ", "-");
            }

            if (networks.Any(n => n.Id == network.Id))
            {
                return Conflict($"A network with ID '{network.Id}' already exists");
            }

            networks.Add(network);
            await SaveNetworksAsync(feed, adapter, networks, cancellationToken);

            return CreatedAtAction(nameof(GetNetworks), new { feedId }, network);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create network in feed {FeedId}", feedId);
            return Problem($"Failed to create network: {ex.Message}", statusCode: 500);
        }
    }

    [HttpPut("{feedId}/{networkId}")]
    public async Task<ActionResult<NetworkDefinition>> UpdateNetwork(
        string feedId,
        string networkId,
        [FromQuery] string? repository,
        [FromBody] NetworkDefinition network,
        CancellationToken cancellationToken
    )
    {
        if (network is null)
        {
            return BadRequest("Network definition is required");
        }

        try
        {
            (Feed feed, IFeedAdapter adapter) = ResolveFeed(feedId, repository);
            List<NetworkDefinition> networks = await LoadNetworksAsync(
                feed,
                adapter,
                cancellationToken
            );

            int index = networks.FindIndex(n => n.Id == networkId);
            if (index < 0)
            {
                return NotFound($"Network '{networkId}' not found");
            }

            network.Id = networkId;
            networks[index] = network;
            await SaveNetworksAsync(feed, adapter, networks, cancellationToken);

            return Ok(network);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update network {NetworkId} in feed {FeedId}",
                networkId,
                feedId
            );
            return Problem($"Failed to update network: {ex.Message}", statusCode: 500);
        }
    }

    [HttpDelete("{feedId}/{networkId}")]
    public async Task<ActionResult> DeleteNetwork(
        string feedId,
        string networkId,
        [FromQuery] string? repository,
        CancellationToken cancellationToken
    )
    {
        try
        {
            (Feed feed, IFeedAdapter adapter) = ResolveFeed(feedId, repository);
            List<NetworkDefinition> networks = await LoadNetworksAsync(
                feed,
                adapter,
                cancellationToken
            );

            int removed = networks.RemoveAll(n => n.Id == networkId);
            if (removed == 0)
            {
                return NotFound($"Network '{networkId}' not found");
            }

            await SaveNetworksAsync(feed, adapter, networks, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete network {NetworkId} from feed {FeedId}",
                networkId,
                feedId
            );
            return Problem($"Failed to delete network: {ex.Message}", statusCode: 500);
        }
    }

    private (Feed Feed, IFeedAdapter Adapter) ResolveFeed(string feedId, string? repository)
    {
        Feed? feed = _db.Feeds.FindById(feedId);
        if (feed is null)
        {
            throw new InvalidOperationException($"Feed {feedId} not found");
        }

        if (!string.IsNullOrWhiteSpace(repository))
        {
            feed.Repository = repository;
        }

        IFeedAdapter? adapter = _adapters.FirstOrDefault(a => a.SourceType == feed.SourceType);
        if (adapter is null)
        {
            throw new InvalidOperationException(
                $"No adapter available for feed type {feed.SourceType}"
            );
        }

        return (feed, adapter);
    }

    private async Task<List<NetworkDefinition>> LoadNetworksAsync(
        Feed feed,
        IFeedAdapter adapter,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using Stream stream = await adapter.PullAsync(
                feed,
                "networks.json",
                null,
                cancellationToken
            );
            using StreamReader reader = new StreamReader(stream);
            string json = await reader.ReadToEndAsync(cancellationToken);

            NetworksManifest? manifest = JsonSerializer.Deserialize<NetworksManifest>(
                json,
                JsonOptions
            );
            return manifest?.Networks ?? new List<NetworkDefinition>();
        }
        catch (HttpRequestException)
        {
            return new List<NetworkDefinition>();
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("not found"))
        {
            return new List<NetworkDefinition>();
        }
    }

    private async Task SaveNetworksAsync(
        Feed feed,
        IFeedAdapter adapter,
        List<NetworkDefinition> networks,
        CancellationToken cancellationToken
    )
    {
        NetworksManifest manifest = new NetworksManifest { Version = 1, Networks = networks };

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using MemoryStream stream = new MemoryStream(bytes);

        await adapter.PushAsync(feed, "networks.json", stream, cancellationToken);
        _logger.LogInformation(
            "Saved {Count} network definitions to feed {FeedId}",
            networks.Count,
            feed.Id
        );
    }

    private sealed class NetworksManifest
    {
        public int Version { get; set; } = 1;
        public List<NetworkDefinition> Networks { get; set; } = new List<NetworkDefinition>();
    }
}
