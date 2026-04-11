using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;
using ForgeBoard.ViewModels;

namespace ForgeBoard.Views.Builds;

public sealed partial class BuildListPage : Page
{
    private readonly BuildListViewModel _viewModel;

    public BuildListPage()
    {
        this.InitializeComponent();
        _viewModel = new BuildListViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += async (s, e) => await _viewModel.LoadCommand.ExecuteAsync(null);

        _viewModel.NavigateToBuildDetail += OnNavigateToBuildDetail;
        _viewModel.ShowReadinessResult += OnShowReadinessResult;
    }

    private void OnNavigateToBuildDetail(string executionId)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildDetailPage), executionId);
            Shell.Current?.UpdateBrowserUrl($"builds/detail?id={executionId}");
        });
    }

    private void OnShowReadinessResult(string definitionId, BuildReadinessResult readiness)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            StackPanel issuePanel = new StackPanel { Spacing = 8 };

            foreach (BuildReadinessIssue issue in readiness.Issues)
            {
                string prefix = issue.Severity == IssueSeverity.Error ? "X" : "!";
                Microsoft.UI.Xaml.Media.Brush foreground =
                    issue.Severity == IssueSeverity.Error
                        ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"]
                        : (Microsoft.UI.Xaml.Media.Brush)
                            Application.Current.Resources["WarningBrush"];

                TextBlock issueText = new TextBlock
                {
                    Text = $"[{prefix}] [{issue.Category}] {issue.Message}",
                    FontSize = 12,
                    Foreground = foreground,
                    TextWrapping = TextWrapping.Wrap,
                };
                issuePanel.Children.Add(issueText);
            }

            bool hasErrors = readiness.Issues.Any(i => i.Severity == IssueSeverity.Error);

            ContentDialog dialog = new ContentDialog
            {
                Title = hasErrors ? "Build Cannot Start" : "Build Warnings",
                Content = issuePanel,
                CloseButtonText = hasErrors ? "OK" : "Cancel",
                XamlRoot = this.XamlRoot,
            };

            if (!hasErrors)
            {
                dialog.PrimaryButtonText = "Start Anyway";
            }

            ContentDialogResult result = await dialog.ShowAsync();
            if (!hasErrors && result == ContentDialogResult.Primary)
            {
                try
                {
                    BuildExecution execution = await App.ApiClient.StartBuildAsync(definitionId);
                    Frame? frame = this.Frame;
                    frame?.Navigate(typeof(BuildDetailPage), execution.Id);
                }
                catch (Exception ex)
                {
                    _viewModel.ErrorMessage = ex.Message;
                }
            }
        });
    }

    private void NewBuild_Click(object sender, RoutedEventArgs e)
    {
        Frame? frame = this.Frame;
        frame?.Navigate(typeof(BuildWizardPage));
        Shell.Current?.UpdateBrowserUrl("builds/new");
    }

    private async void RunBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.RunBuildCommand.ExecuteAsync(id);
        }
    }

    private void EditBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string definitionId)
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildWizardPage), definitionId);
            Shell.Current?.UpdateBrowserUrl($"builds/edit?id={definitionId}");
        }
    }

    private async void CloneBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.CloneCommand.ExecuteAsync(id);
        }
    }

    private async void DeleteBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            BuildDefinition? definition = _viewModel.Definitions.FirstOrDefault(d => d.Id == id);
            string name = definition?.Name ?? id;

            ContentDialog dialog = new ContentDialog
            {
                Title = "Confirm Delete",
                Content = $"Delete '{name}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await _viewModel.DeleteCommand.ExecuteAsync(id);
        }
    }
}
