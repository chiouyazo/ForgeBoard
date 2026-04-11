using System.Collections.ObjectModel;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<BuildExecutionDisplay> ActiveBuilds { get; } =
        new ObservableCollection<BuildExecutionDisplay>();

    public ObservableCollection<BuildExecutionDisplay> RecentBuilds { get; } =
        new ObservableCollection<BuildExecutionDisplay>();

    public ObservableCollection<PublishTaskDisplay> ActivePublishes { get; } =
        new ObservableCollection<PublishTaskDisplay>();

    [ObservableProperty]
    private DiskUsageInfo? _diskUsage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasNoBuilds;

    public DashboardViewModel(ApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await LoadActiveBuildsAsync();
            HasNoBuilds = ActiveBuilds.Count == 0 && RecentBuilds.Count == 0;
            await LoadActivePublishesAsync();
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

    private async Task LoadActiveBuildsAsync()
    {
        List<BuildExecution> history = await _api.GetBuildHistoryAsync();
        List<BuildDefinition> definitions = await _api.GetBuildDefinitionsAsync();
        Dictionary<string, string> definitionNames = new Dictionary<string, string>();
        foreach (BuildDefinition def in definitions)
        {
            definitionNames[def.Id] = def.Name;
        }

        ActiveBuilds.Clear();
        RecentBuilds.Clear();

        foreach (BuildExecution execution in history)
        {
            string name = definitionNames.TryGetValue(
                execution.BuildDefinitionId,
                out string? defName
            )
                ? defName
                : execution.BuildDefinitionId;

            BuildExecutionDisplay display = new BuildExecutionDisplay
            {
                Id = execution.Id,
                Name = name,
                ExecutionId = execution.Id,
                Status = execution.Status,
                QueuedAt = execution.QueuedAt,
            };

            if (
                execution.Status == BuildStatus.Running
                || execution.Status == BuildStatus.Queued
                || execution.Status == BuildStatus.Preparing
            )
            {
                ActiveBuilds.Add(display);
            }
            else
            {
                RecentBuilds.Add(display);
            }
        }
    }

    private async Task LoadActivePublishesAsync()
    {
        try
        {
            Dictionary<string, PublishProgress> publishes = await _api.GetActivePublishesAsync();

            for (int i = ActivePublishes.Count - 1; i >= 0; i--)
            {
                if (!publishes.ContainsKey(ActivePublishes[i].ArtifactId))
                {
                    ActivePublishes.RemoveAt(i);
                }
            }

            foreach (KeyValuePair<string, PublishProgress> entry in publishes)
            {
                PublishTaskDisplay? existing = null;
                for (int i = 0; i < ActivePublishes.Count; i++)
                {
                    if (ActivePublishes[i].ArtifactId == entry.Key)
                    {
                        existing = ActivePublishes[i];
                        break;
                    }
                }

                if (existing is not null)
                {
                    int idx = ActivePublishes.IndexOf(existing);
                    ActivePublishes[idx] = new PublishTaskDisplay
                    {
                        ArtifactId = entry.Key,
                        Status = entry.Value.Status,
                        PercentComplete = entry.Value.PercentComplete,
                        IsComplete = entry.Value.IsComplete,
                        Error = entry.Value.Error,
                    };
                }
                else
                {
                    ActivePublishes.Add(
                        new PublishTaskDisplay
                        {
                            ArtifactId = entry.Key,
                            Status = entry.Value.Status,
                            PercentComplete = entry.Value.PercentComplete,
                            IsComplete = entry.Value.IsComplete,
                            Error = entry.Value.Error,
                        }
                    );
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task CancelBuildAsync(string executionId)
    {
        try
        {
            await _api.CancelBuildAsync(executionId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }
}
