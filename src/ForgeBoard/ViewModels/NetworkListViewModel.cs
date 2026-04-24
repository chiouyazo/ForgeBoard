using System.Collections.ObjectModel;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class NetworkListViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<Feed> Feeds { get; } = new ObservableCollection<Feed>();
    public ObservableCollection<NetworkDefinition> Networks { get; } =
        new ObservableCollection<NetworkDefinition>();

    public ObservableCollection<string> Repositories { get; } = new ObservableCollection<string>();

    [ObservableProperty]
    private Feed? _selectedFeed;

    [ObservableProperty]
    private string? _selectedRepository;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public NetworkListViewModel(ApiClient api)
    {
        _api = api;
    }

    partial void OnSelectedFeedChanged(Feed? value)
    {
        Networks.Clear();
        Repositories.Clear();
        SelectedRepository = null;
        if (value is not null)
        {
            _ = LoadRepositoriesAsync();
        }
    }

    partial void OnSelectedRepositoryChanged(string? value)
    {
        if (value is not null && SelectedFeed is not null)
        {
            _ = LoadNetworksAsync();
        }
        else
        {
            Networks.Clear();
        }
    }

    private async Task LoadRepositoriesAsync()
    {
        if (SelectedFeed is null)
        {
            return;
        }

        try
        {
            List<string> repos = await _api.GetFeedRepositoriesAsync(SelectedFeed.Id);
            Repositories.Clear();
            foreach (string repo in repos)
            {
                Repositories.Add(repo);
            }
            if (Repositories.Count > 0)
            {
                SelectedRepository = SelectedFeed.Repository ?? Repositories[0];
            }
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError($"Failed to load repositories: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            List<Feed> allFeeds = await _api.GetFeedsAsync();
            Feeds.Clear();
            foreach (Feed feed in allFeeds)
            {
                if (feed.SourceType == FeedType.Nexus)
                {
                    Feeds.Add(feed);
                }
            }

            if (SelectedFeed is null && Feeds.Count > 0)
            {
                SelectedFeed = Feeds[0];
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadNetworksAsync()
    {
        if (SelectedFeed is null)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            List<NetworkDefinition> networks = await _api.GetNetworksAsync(
                SelectedFeed.Id,
                SelectedRepository
            );
            Networks.Clear();
            foreach (NetworkDefinition network in networks)
            {
                Networks.Add(network);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateNetworkAsync(NetworkDefinition network)
    {
        if (SelectedFeed is null)
        {
            return;
        }

        try
        {
            await _api.CreateNetworkAsync(
                SelectedFeed.Id,
                SelectedRepository ?? string.Empty,
                network
            );
            await LoadNetworksAsync();
            Views.Shell.Current?.ShowNotification($"Network '{network.Name}' created");
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task UpdateNetworkAsync(NetworkDefinition network)
    {
        if (SelectedFeed is null)
        {
            return;
        }

        try
        {
            await _api.UpdateNetworkAsync(
                SelectedFeed.Id,
                SelectedRepository ?? string.Empty,
                network.Id,
                network
            );
            await LoadNetworksAsync();
            Views.Shell.Current?.ShowNotification($"Network '{network.Name}' updated");
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteNetworkAsync(string networkId)
    {
        if (SelectedFeed is null)
        {
            return;
        }

        try
        {
            await _api.DeleteNetworkAsync(
                SelectedFeed.Id,
                SelectedRepository ?? string.Empty,
                networkId
            );
            await LoadNetworksAsync();
            Views.Shell.Current?.ShowNotification("Network deleted");
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddFeedAsync(Feed feed)
    {
        try
        {
            Feed created = await _api.CreateFeedAsync(feed);
            Feeds.Add(created);
            SelectedFeed = created;
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task TestFeedAsync(string feedId)
    {
        try
        {
            bool success = await _api.TestFeedConnectivityAsync(feedId);
            if (success)
            {
                Views.Shell.Current?.ShowNotification("Feed connection successful");
            }
            else
            {
                Views.Shell.Current?.ShowWarning("Feed connection failed");
            }
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteFeedAsync(string feedId)
    {
        try
        {
            await _api.DeleteFeedAsync(feedId);
            Feed? toRemove = null;
            foreach (Feed feed in Feeds)
            {
                if (feed.Id == feedId)
                {
                    toRemove = feed;
                    break;
                }
            }
            if (toRemove is not null)
            {
                Feeds.Remove(toRemove);
            }
            if (SelectedFeed?.Id == feedId)
            {
                SelectedFeed = Feeds.Count > 0 ? Feeds[0] : null;
            }
        }
        catch (Exception ex)
        {
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }
}
