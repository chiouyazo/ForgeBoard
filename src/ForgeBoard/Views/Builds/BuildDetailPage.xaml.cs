using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;
using ForgeBoard.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ForgeBoard.Views.Builds;

public sealed partial class BuildDetailPage : Page
{
    private readonly BuildDetailViewModel _viewModel;
    private string? _currentExecutionId;
    private DispatcherTimer? _pollTimer;
    private bool _isPolling;

    public BuildDetailPage()
    {
        this.InitializeComponent();
        _viewModel = new BuildDetailViewModel(App.ApiClient);
        this.DataContext = _viewModel;

        _viewModel.NavigateToExecution += OnNavigateToExecution;
    }

    private void OnNavigateToExecution(string executionId)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildDetailPage), executionId);
            Shell.Current?.UpdateBrowserUrl($"builds/detail?id={executionId}");
        });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string executionId)
        {
            _currentExecutionId = executionId;
            await _viewModel.LoadCommand.ExecuteAsync(executionId);
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
            StartPolling();
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopPolling();
        try
        {
            await App.ApiClient.DisconnectLogStreamAsync();
        }
        catch { }
        _currentExecutionId = null;
    }

    private void StartPolling()
    {
        StopPolling();
        _pollTimer = new DispatcherTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(2);
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        if (_pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= PollTimer_Tick;
            _pollTimer = null;
        }
    }

    private async void PollTimer_Tick(object? sender, object e)
    {
        if (_isPolling || _currentExecutionId is null)
            return;

        _isPolling = true;
        try
        {
            string executionId = _currentExecutionId;
            BuildExecution execution = await App.ApiClient.GetBuildExecutionAsync(executionId);
            List<BuildLogEntry> logs = await App.ApiClient.GetBuildLogsAsync(executionId);

            if (
                _viewModel.Execution is null
                || _viewModel.Execution.Status != execution.Status
                || _viewModel.Execution.ErrorMessage != execution.ErrorMessage
            )
            {
                _viewModel.Execution = execution;
            }

            if (logs.Count > _viewModel.LogEntries.Count)
            {
                for (int i = _viewModel.LogEntries.Count; i < logs.Count; i++)
                {
                    _viewModel.LogEntries.Add(logs[i]);
                }
                _viewModel.RebuildLogText();
                LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
            }

            _viewModel.UpdateExecutionStatus(execution.Status);

            if (
                execution.Status == BuildStatus.Succeeded
                || execution.Status == BuildStatus.Failed
                || execution.Status == BuildStatus.Cancelled
            )
            {
                StopPolling();

                if (string.IsNullOrEmpty(_viewModel.BuildName))
                {
                    await _viewModel.LoadCommand.ExecuteAsync(executionId);
                }
            }
        }
        catch { }
        finally
        {
            _isPolling = false;
        }
    }

    private async void CancelBuild_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = "Confirm Cancel",
            Content = "Cancel this build? This cannot be undone.",
            PrimaryButtonText = "Cancel Build",
            CloseButtonText = "Keep Running",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        if (_viewModel.CancelCommand.CanExecute(null))
        {
            await _viewModel.CancelCommand.ExecuteAsync(null);
        }
    }
}
