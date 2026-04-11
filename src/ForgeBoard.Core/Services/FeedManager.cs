using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class FeedManager : IFeedManager
{
    private readonly ForgeBoardDatabase _db;
    private readonly IEnumerable<IFeedAdapter> _adapters;
    private readonly ILogger<FeedManager> _logger;

    public FeedManager(
        ForgeBoardDatabase db,
        IEnumerable<IFeedAdapter> adapters,
        ILogger<FeedManager> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _adapters = adapters;
        _logger = logger;
    }

    public Task<Feed> CreateAsync(Feed feed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);

        feed.AssignDeterministicId();
        feed.CreatedAt = DateTimeOffset.UtcNow;

        _db.Feeds.Insert(feed);

        _logger.LogInformation("Created feed {Id} ({Name})", feed.Id, feed.Name);
        return Task.FromResult(feed);
    }

    public Task<Feed?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        Feed? result = _db.Feeds.FindById(id);
        return Task.FromResult<Feed?>(result);
    }

    public Task<List<Feed>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        List<Feed> result = _db.Feeds.FindAll().ToList();
        return Task.FromResult(result);
    }

    public Task<Feed> UpdateAsync(Feed feed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);

        _db.Feeds.Update(feed);

        _logger.LogInformation("Updated feed {Id}", feed.Id);
        return Task.FromResult(feed);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        Feed? feed = _db.Feeds.FindById(id);
        if (feed is not null)
        {
            _db.Feeds.Delete(id);
            _logger.LogInformation("Deleted feed {Id}", id);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> TestConnectivityAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(id);

        Feed? feed = _db.Feeds.FindById(id);
        if (feed is null)
        {
            return false;
        }

        IFeedAdapter? adapter = _adapters.FirstOrDefault(a => a.SourceType == feed.SourceType);
        if (adapter is null)
        {
            _logger.LogWarning("No adapter found for feed type {SourceType}", feed.SourceType);
            return false;
        }

        return await adapter.TestConnectivityAsync(feed, cancellationToken);
    }

    public async Task<List<FeedImage>> BrowseImagesAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(id);

        Feed? feed = _db.Feeds.FindById(id);
        if (feed is null)
        {
            return new List<FeedImage>();
        }

        IFeedAdapter? adapter = _adapters.FirstOrDefault(a => a.SourceType == feed.SourceType);
        if (adapter is null)
        {
            _logger.LogWarning("No adapter found for feed type {SourceType}", feed.SourceType);
            return new List<FeedImage>();
        }

        return await adapter.BrowseImagesAsync(feed, cancellationToken);
    }
}
