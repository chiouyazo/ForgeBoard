using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface IFeedManager
{
    Task<Feed> CreateAsync(Feed feed, CancellationToken cancellationToken = default);

    Task<Feed?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<List<Feed>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Feed> UpdateAsync(Feed feed, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> TestConnectivityAsync(string id, CancellationToken cancellationToken = default);

    Task<List<FeedImage>> BrowseImagesAsync(
        string id,
        CancellationToken cancellationToken = default
    );
}
