using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;
using ForgeBoard.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ForgeBoard.Views.Builds;

public sealed partial class BuildWizardPage : Page
{
    private readonly BuildWizardViewModel _viewModel;

    private string? _editDefinitionId;

    public BuildWizardPage()
    {
        this.InitializeComponent();
        _viewModel = new BuildWizardViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += OnPageLoaded;

        _viewModel.NavigateToBuildDetail += OnNavigateToBuildDetail;
        _viewModel.NavigateToBuildList += OnNavigateToBuildList;
        _viewModel.ShowPreviewDialog += OnShowPreviewDialog;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string definitionId && !string.IsNullOrEmpty(definitionId))
        {
            _editDefinitionId = definitionId;
        }
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
        if (!string.IsNullOrEmpty(_editDefinitionId))
        {
            await _viewModel.LoadDefinitionAsync(_editDefinitionId);
        }
    }

    private void OnNavigateToBuildDetail(string executionId)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildDetailPage), executionId);
        });
    }

    private void OnNavigateToBuildList()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(BuildListPage));
        });
    }

    private void OnShowPreviewDialog(string previewHcl, List<string> stepFlow)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            Grid previewContent = new Grid { MinHeight = 400 };
            previewContent.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );
            previewContent.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) }
            );
            previewContent.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            );

            // LEFT panel: HCL in a monospace read-only TextBox with dark background
            Border hclBorder = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)
                ),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0),
            };
            TextBox hclBox = new TextBox
            {
                Text = previewHcl,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "Cascadia Code, Consolas, Courier New"
                ),
                FontSize = 11,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 220, 220)
                ),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)
                ),
                BorderThickness = new Thickness(0),
                MaxHeight = 450,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            ScrollViewer.SetVerticalScrollBarVisibility(hclBox, ScrollBarVisibility.Auto);
            hclBorder.Child = hclBox;
            Grid.SetColumn(hclBorder, 0);
            previewContent.Children.Add(hclBorder);

            // RIGHT panel: numbered list of steps
            StackPanel flowPanel = new StackPanel { Spacing = 6 };
            TextBlock flowHeader = new TextBlock
            {
                Text = "Build Steps",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)
                    Application.Current.Resources["TextBrush"],
                Margin = new Thickness(0, 0, 0, 8),
            };
            flowPanel.Children.Add(flowHeader);

            for (int i = 0; i < stepFlow.Count; i++)
            {
                TextBlock stepText = new TextBlock
                {
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["TextBrush"],
                    TextWrapping = TextWrapping.Wrap,
                };
                stepText.Inlines.Add(
                    new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = $"{i + 1}. ",
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    }
                );
                stepText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = stepFlow[i] });
                flowPanel.Children.Add(stepText);
            }
            ScrollViewer flowScroll = new ScrollViewer { Content = flowPanel, MaxHeight = 450 };
            Grid.SetColumn(flowScroll, 2);
            previewContent.Children.Add(flowScroll);

            ContentDialog dialog = new ContentDialog
            {
                Title = "Build Preview",
                Content = previewContent,
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
                MinWidth = 800,
            };

            await dialog.ShowAsync();
        });
    }

    private void AddStepFromLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            _viewModel.AddStepFromLibraryCommand.Execute(id);
        }
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            _viewModel.RemoveStepCommand.Execute(id);
        }
    }

    private async void ToggleStepEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            BuildStep? step = _viewModel.BuildSteps.FirstOrDefault(s => s.Id == id);
            if (step is null)
                return;

            _viewModel.SelectedStepForEdit = step;
            _viewModel.NotifyStepTypeChanged();

            ContentDialog dialog = BuildStepEditDialog();
            ContentDialogResult result = await dialog.ShowAsync();
            _viewModel.SelectedStepForEdit = null;
            RefreshStepList();
        }
    }

    private ContentDialog BuildStepEditDialog()
    {
        StackPanel content = new StackPanel { Spacing = 10, MinWidth = 500 };

        TextBox nameBox = new TextBox { Header = "Name", FontSize = 12 };
        nameBox.SetBinding(
            TextBox.TextProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("SelectedStepForEdit.Name"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            }
        );
        content.Children.Add(nameBox);

        ComboBox typeBox = new ComboBox
        {
            Header = "Type",
            ItemsSource = _viewModel.AvailableStepTypes,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        typeBox.SetBinding(
            ComboBox.SelectedItemProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("SelectedStepForEdit.StepType"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            }
        );
        typeBox.SelectionChanged += (s, e) => _viewModel.NotifyStepTypeChanged();
        content.Children.Add(typeBox);

        TextBox contentBox = new TextBox
        {
            Header = "Content",
            AcceptsReturn = true,
            MaxHeight = 300,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        contentBox.SetBinding(
            TextBox.TextProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("SelectedStepForEdit.Content"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            }
        );
        content.Children.Add(contentBox);

        Grid optionsGrid = new Grid();
        optionsGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) }
        );
        optionsGrid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );

        CheckBox packerCheck = new CheckBox
        {
            Content = "Run via Packer",
            Margin = new Thickness(0, 0, 8, 0),
        };
        packerCheck.SetBinding(
            CheckBox.IsCheckedProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("SelectedStepForEdit.UsePacker"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            }
        );
        Grid.SetColumn(packerCheck, 0);
        optionsGrid.Children.Add(packerCheck);

        TextBox timeoutBox = new TextBox { Header = "Timeout (sec)", FontSize = 12 };
        timeoutBox.SetBinding(
            TextBox.TextProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("SelectedStepForEdit.TimeoutSeconds"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay,
            }
        );
        Grid.SetColumn(timeoutBox, 1);
        optionsGrid.Children.Add(timeoutBox);

        content.Children.Add(optionsGrid);

        return new ContentDialog
        {
            Title = "Edit Step",
            Content = content,
            PrimaryButtonText = "Done",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            DataContext = _viewModel,
        };
    }

    private void RefreshStepList()
    {
        List<BuildStep> steps = _viewModel.BuildSteps.ToList();
        _viewModel.BuildSteps.Clear();
        foreach (BuildStep step in steps)
        {
            _viewModel.BuildSteps.Add(step);
        }
        _viewModel.RefreshGroupedSteps();
    }

    private async void SaveStepToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.SaveStepToLibraryCommand.ExecuteAsync(id);
        }
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            int index = -1;
            for (int i = 0; i < _viewModel.BuildSteps.Count; i++)
            {
                if (_viewModel.BuildSteps[i].Id == id)
                {
                    index = i;
                    break;
                }
            }

            if (index <= 0)
                return;

            _viewModel.BuildSteps.Move(index, index - 1);
            for (int i = 0; i < _viewModel.BuildSteps.Count; i++)
            {
                _viewModel.BuildSteps[i].Order = i;
            }
        }
    }

    private void MoveStepDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            int index = -1;
            for (int i = 0; i < _viewModel.BuildSteps.Count; i++)
            {
                if (_viewModel.BuildSteps[i].Id == id)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0 || index >= _viewModel.BuildSteps.Count - 1)
                return;

            _viewModel.BuildSteps.Move(index, index + 1);
            for (int i = 0; i < _viewModel.BuildSteps.Count; i++)
            {
                _viewModel.BuildSteps[i].Order = i;
            }
        }
    }
}
