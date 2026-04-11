using System.Collections.ObjectModel;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;

namespace ForgeBoard.ViewModels;

public partial class BuildDetailViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailureDisplayMessage))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private BuildExecution? _execution;

    [ObservableProperty]
    private string _buildName = string.Empty;

    public string Title =>
        !string.IsNullOrEmpty(BuildName) ? BuildName : Execution?.BuildDefinitionId ?? "Build";
    public string StatusText => Execution?.Status.ToString() ?? "Unknown";

    public ObservableCollection<BuildLogEntry> LogEntries { get; } =
        new ObservableCollection<BuildLogEntry>();

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public string FailureDisplayMessage
    {
        get
        {
            if (Execution is null || Execution.Status != BuildStatus.Failed)
            {
                return string.Empty;
            }

            string errorText = !string.IsNullOrWhiteSpace(Execution.ErrorMessage)
                ? Execution.ErrorMessage
                : "Unknown error";

            if (LogEntries.Count == 0)
            {
                return $"Build failed before logging started: {errorText}";
            }

            return errorText;
        }
    }

    public bool CanCancel =>
        Execution is not null
        && (
            Execution.Status == BuildStatus.Running
            || Execution.Status == BuildStatus.Queued
            || Execution.Status == BuildStatus.Preparing
        );

    public bool CanRebuild => !CanCancel;

    public event Action<string>? NavigateToExecution;

    public BuildDetailViewModel(ApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    private async Task LoadAsync(string executionId)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Execution = await _api.GetBuildExecutionAsync(executionId);

            try
            {
                BuildDefinition definition = await _api.GetBuildDefinitionAsync(
                    Execution.BuildDefinitionId
                );
                BuildName = definition.Name;
            }
            catch
            {
                BuildName = Execution.BuildDefinitionId;
            }

            List<BuildLogEntry> logs = await _api.GetBuildLogsAsync(executionId);
            LogEntries.Clear();
            foreach (BuildLogEntry entry in logs)
            {
                LogEntries.Add(entry);
            }
            RebuildLogText();
            OnPropertyChanged(nameof(FailureDisplayMessage));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(StatusText));
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
    private async Task CancelAsync()
    {
        if (Execution is null)
            return;
        try
        {
            await _api.CancelBuildAsync(Execution.Id);
            await LoadAsync(Execution.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task RebuildAsync()
    {
        if (Execution is null)
            return;
        try
        {
            BuildExecution newExecution = await _api.StartBuildAsync(Execution.BuildDefinitionId);
            NavigateToExecution?.Invoke(newExecution.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    public void AddLogEntry(BuildLogEntry entry)
    {
        LogEntries.Add(entry);
    }

    public void RebuildLogText()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (BuildLogEntry entry in LogEntries)
        {
            sb.AppendLine($"[{entry.TimestampDisplay}] {entry.Message}");
        }
        LogText = sb.ToString();
    }

    public void UpdateExecutionStatus(BuildStatus status)
    {
        if (Execution is null)
            return;
        Execution.Status = status;
        OnPropertyChanged(nameof(Execution));
        OnPropertyChanged(nameof(FailureDisplayMessage));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRebuild));
        OnPropertyChanged(nameof(StatusText));
    }
}
