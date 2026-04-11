using ForgeBoard.Services;
using ForgeBoard.ViewModels;
using ForgeBoard.Views.Builds;

namespace ForgeBoard.Views.Dashboard;

public sealed partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;
    private DispatcherTimer? _refreshTimer;

    public DashboardPage()
    {
        this.InitializeComponent();
        _viewModel = new DashboardViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        StartAutoRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh();
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += async (s, e) =>
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);

            // Stop refreshing if nothing active (active = not completed)
            bool hasActivePublishes = false;
            foreach (ForgeBoard.ViewModels.PublishTaskDisplay p in _viewModel.ActivePublishes)
            {
                if (!p.IsComplete)
                {
                    hasActivePublishes = true;
                    break;
                }
            }
            if (_viewModel.ActiveBuilds.Count == 0 && !hasActivePublishes)
            {
                StopAutoRefresh();
            }
        };
        _refreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private async void CancelPublish_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string artifactId)
        {
            try
            {
                await App.ApiClient.CancelPublishAsync(artifactId);
                Shell.Current?.ShowNotification("Publish cancel requested.");
                await _viewModel.LoadCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                Shell.Current?.ShowError($"Failed to cancel: {ex.Message}");
            }
        }
    }

    private async void DismissPublish_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string artifactId)
        {
            try
            {
                await App.ApiClient.DismissPublishAsync(artifactId);
                await _viewModel.LoadCommand.ExecuteAsync(null);
            }
            catch { }
        }
    }

    private void ActiveBuild_Click(object sender, RoutedEventArgs e)
    {
        if (
            sender is Button button
            && button.Tag is string executionId
            && !string.IsNullOrEmpty(executionId)
        )
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildDetailPage), executionId);
            Views.Shell.Current?.UpdateBrowserUrl($"builds/detail?id={executionId}");
        }
    }

    private void RecentBuild_Click(object sender, RoutedEventArgs e)
    {
        if (
            sender is Button button
            && button.Tag is string executionId
            && !string.IsNullOrEmpty(executionId)
        )
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildDetailPage), executionId);
            Views.Shell.Current?.UpdateBrowserUrl($"builds/detail?id={executionId}");
        }
    }
}
