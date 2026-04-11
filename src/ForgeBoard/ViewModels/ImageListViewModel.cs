using System.Collections.ObjectModel;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class ImageListViewModel : ObservableObject
{
    private readonly ApiClient _api;

    private readonly ObservableCollection<ImageListItem> _allImages =
        new ObservableCollection<ImageListItem>();

    public ObservableCollection<ImageListItem> FilteredImages { get; } =
        new ObservableCollection<ImageListItem>();
    public ObservableCollection<Feed> Feeds { get; } = new ObservableCollection<Feed>();

    public List<string> TypeFilterOptions { get; } =
        new List<string> { "All", "ISO", "VHDX", "BOX", "QCOW2" };
    public List<string> OriginFilterOptions { get; } =
        new List<string> { "All", "Local", "Imported", "Built", "BuildChain" };

    [ObservableProperty]
    private DiskUsageInfo? _diskUsage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _localImagePath = string.Empty;

    [ObservableProperty]
    private string _localImageName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredImages))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedTypeFilter = "All";

    [ObservableProperty]
    private string _selectedOriginFilter = "All";

    [ObservableProperty]
    private bool _showFeedsPanel;

    public ImageListViewModel(ApiClient api)
    {
        _api = api;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedTypeFilterChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedOriginFilterChanged(string value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        FilteredImages.Clear();
        foreach (ImageListItem image in _allImages)
        {
            bool matchesSearch =
                string.IsNullOrWhiteSpace(SearchText)
                || image.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || image.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || image.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            bool matchesType =
                SelectedTypeFilter == "All"
                || image.Format.Equals(SelectedTypeFilter, StringComparison.OrdinalIgnoreCase);

            bool matchesOrigin =
                SelectedOriginFilter == "All"
                || image.Origin.Equals(SelectedOriginFilter, StringComparison.OrdinalIgnoreCase);

            if (matchesSearch && matchesType && matchesOrigin)
            {
                FilteredImages.Add(image);
            }
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await LoadImagesAsync();
            ApplyFilters();
            await LoadFeedsAsync();
            DiskUsage = await _api.GetDiskUsageAsync();
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

    private async Task LoadImagesAsync()
    {
        List<BaseImage> baseImages = await _api.GetBaseImagesAsync();
        List<ImageArtifact> artifacts = await _api.GetArtifactsAsync();
        List<BuildExecution> executions = await _api.GetBuildHistoryAsync();
        Dictionary<string, PublishProgress> activePublishes = await _api.GetActivePublishesAsync();

        HashSet<string> inUseBaseImageIds = new HashSet<string>();
        HashSet<string> inUseArtifactIds = new HashSet<string>();

        foreach (BuildExecution exec in executions)
        {
            if (
                exec.Status == BuildStatus.Running
                || exec.Status == BuildStatus.Preparing
                || exec.Status == BuildStatus.WaitingForChain
                || exec.Status == BuildStatus.Queued
            )
            {
                BuildDefinition? def = null;
                try
                {
                    def = await _api.GetBuildDefinitionAsync(exec.BuildDefinitionId);
                }
                catch { }
                if (def is not null)
                {
                    inUseBaseImageIds.Add(def.BaseImageId);
                }
            }
        }

        foreach (KeyValuePair<string, PublishProgress> publish in activePublishes)
        {
            if (!publish.Value.IsComplete)
            {
                inUseArtifactIds.Add(publish.Key);
            }
        }

        _allImages.Clear();

        Microsoft.UI.Xaml.Media.SolidColorBrush greenDot =
            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 76, 175, 80)
            );
        Microsoft.UI.Xaml.Media.SolidColorBrush grayDot =
            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 158, 158, 158)
            );

        foreach (BaseImage image in baseImages)
        {
            string extension = string.Empty;
            if (!string.IsNullOrEmpty(image.FileName))
            {
                extension = System
                    .IO.Path.GetExtension(image.FileName)
                    .TrimStart('.')
                    .ToUpperInvariant();
            }

            string origin = image.Origin switch
            {
                ImageOrigin.Local => "Local",
                ImageOrigin.Imported => "Imported",
                ImageOrigin.Built => "Built",
                ImageOrigin.BuildChain => "BuildChain",
                _ => string.IsNullOrEmpty(image.SourceId) ? "Local" : "Imported",
            };

            bool isImported = origin == "Imported";

            _allImages.Add(
                new ImageListItem
                {
                    Id = image.Id,
                    Name = image.Name,
                    Description = image.Description,
                    FileName = image.FileName,
                    Format = string.IsNullOrEmpty(extension) ? "Unknown" : extension,
                    FileSizeBytes = image.FileSizeBytes,
                    Origin = origin,
                    CreatedAt = image.CreatedAt,
                    IsBaseImage = true,
                    IsCached = image.IsCached,
                    ShowCacheIndicator = isImported,
                    CacheLabel = image.IsCached ? "cached" : "not cached",
                    CacheBrush = image.IsCached ? greenDot : grayDot,
                    CanDelete = !inUseBaseImageIds.Contains(image.Id),
                }
            );
        }

        foreach (ImageArtifact artifact in artifacts)
        {
            _allImages.Add(
                new ImageListItem
                {
                    Id = artifact.Id,
                    Name = artifact.Name,
                    Description = $"Build artifact ({artifact.Format})",
                    FileName = System.IO.Path.GetFileName(artifact.FilePath),
                    Format = artifact.Format.ToUpperInvariant(),
                    FileSizeBytes = artifact.FileSizeBytes,
                    Origin = "Built",
                    CreatedAt = artifact.CreatedAt,
                    IsBaseImage = false,
                    IsBuilt = true,
                    ArtifactId = artifact.Id,
                    BuildDefinitionId = artifact.BuildDefinitionId,
                    CanDelete = !inUseArtifactIds.Contains(artifact.Id),
                }
            );
        }
    }

    private async Task LoadFeedsAsync()
    {
        List<Feed> feeds = await _api.GetFeedsAsync();
        Feeds.Clear();
        foreach (Feed feed in feeds)
        {
            Feeds.Add(feed);
        }
    }

    [RelayCommand]
    private async Task AddLocalImageAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalImagePath))
        {
            ErrorMessage = "Please enter a file path.";
            return;
        }

        bool pathValid = await _api.ValidatePathAsync(LocalImagePath);
        if (!pathValid)
        {
            ErrorMessage = $"File not found: {LocalImagePath}";
            return;
        }

        ErrorMessage = null;
        try
        {
            string fileName = System.IO.Path.GetFileName(LocalImagePath);
            string name = string.IsNullOrWhiteSpace(LocalImageName)
                ? System.IO.Path.GetFileNameWithoutExtension(LocalImagePath)
                : LocalImageName;

            string extension = System.IO.Path.GetExtension(LocalImagePath).ToLowerInvariant();
            string format = extension switch
            {
                ".box" => "box",
                ".iso" => "iso",
                ".vhdx" => "vhdx",
                ".qcow2" => "qcow2",
                ".vmdk" => "vmdk",
                _ => extension.TrimStart('.'),
            };

            BaseImage image = new BaseImage
            {
                Name = name,
                FileName = fileName,
                LocalCachePath = LocalImagePath,
                IsCached = true,
                ImageFormat = format,
                Description = $"Locally added {format} image",
            };

            BaseImage created = await _api.CreateBaseImageAsync(image);

            _allImages.Add(
                new ImageListItem
                {
                    Id = created.Id,
                    Name = created.Name,
                    Description = created.Description,
                    FileName = created.FileName,
                    Format = format.ToUpperInvariant(),
                    FileSizeBytes = created.FileSizeBytes,
                    Origin = "Local",
                    CreatedAt = created.CreatedAt,
                    IsBaseImage = true,
                }
            );

            ApplyFilters();
            LocalImagePath = string.Empty;
            LocalImageName = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportFromFeedAsync(ImportRequest request)
    {
        ErrorMessage = null;
        try
        {
            BaseImage imported = await _api.ImportBaseImageAsync(
                request.FeedId,
                request.RemotePath
            );

            string extension = string.Empty;
            if (!string.IsNullOrEmpty(imported.FileName))
            {
                extension = System
                    .IO.Path.GetExtension(imported.FileName)
                    .TrimStart('.')
                    .ToUpperInvariant();
            }

            _allImages.Add(
                new ImageListItem
                {
                    Id = imported.Id,
                    Name = imported.Name,
                    Description = imported.Description,
                    FileName = imported.FileName,
                    Format = string.IsNullOrEmpty(extension) ? "Unknown" : extension,
                    FileSizeBytes = imported.FileSizeBytes,
                    Origin = "Imported",
                    CreatedAt = imported.CreatedAt,
                    IsBaseImage = true,
                }
            );

            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteImageAsync(string id)
    {
        try
        {
            ImageListItem? image = _allImages.FirstOrDefault(i => i.Id == id);
            if (image is null)
                return;

            if (image.IsBaseImage)
            {
                await _api.DeleteBaseImageAsync(id);
            }
            else
            {
                await _api.DeleteArtifactAsync(id);
            }

            _allImages.Remove(image);
            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddFeedAsync(Feed feed)
    {
        ErrorMessage = null;
        try
        {
            Feed created = await _api.CreateFeedAsync(feed);
            Feeds.Add(created);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task EditFeedAsync(Feed feed)
    {
        ErrorMessage = null;
        try
        {
            Feed updated = await _api.UpdateFeedAsync(feed.Id, feed);
            int index = -1;
            for (int i = 0; i < Feeds.Count; i++)
            {
                if (Feeds[i].Id == feed.Id)
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0)
            {
                Feeds[index] = updated;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteFeedAsync(string id)
    {
        try
        {
            await _api.DeleteFeedAsync(id);
            Feed? feed = Feeds.FirstOrDefault(f => f.Id == id);
            if (feed is not null)
            {
                Feeds.Remove(feed);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    private readonly Dictionary<string, string> _feedTestStatus = new Dictionary<string, string>();

    public string GetFeedTestStatus(string feedId)
    {
        if (_feedTestStatus.TryGetValue(feedId, out string? status))
        {
            return status;
        }
        return string.Empty;
    }

    public event Action<string, string, bool>? FeedTestCompleted;

    [RelayCommand]
    private async Task TestFeedAsync(string id)
    {
        ErrorMessage = null;
        _feedTestStatus[id] = "Testing...";
        FeedTestCompleted?.Invoke(id, "Testing...", false);
        try
        {
            bool success = await _api.TestFeedConnectivityAsync(id);
            if (success)
            {
                _feedTestStatus[id] = "Connected";
                FeedTestCompleted?.Invoke(id, "Connected", true);
            }
            else
            {
                _feedTestStatus[id] = "Failed";
                FeedTestCompleted?.Invoke(id, "Failed", false);
            }
        }
        catch (Exception ex)
        {
            _feedTestStatus[id] = "Failed";
            FeedTestCompleted?.Invoke(id, "Failed", false);
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }
}
