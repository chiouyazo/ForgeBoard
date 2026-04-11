using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface IFeedAdapter
{
    FeedType SourceType { get; }

    Task<bool> TestConnectivityAsync(Feed feed, CancellationToken cancellationToken = default);

    Task<Stream> PullAsync(
        Feed feed,
        string fileName,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task PushAsync(
        Feed feed,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default
    );

    Task<List<string>> ListFilesAsync(
        Feed feed,
        string? prefix = null,
        CancellationToken cancellationToken = default
    );

    Task<List<FeedImage>> BrowseImagesAsync(
        Feed feed,
        CancellationToken cancellationToken = default
    );
}
