using ForgeBoard.Services;
using ForgeBoard.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ForgeBoard.Views.StepLibrary;

public sealed partial class StepListPage : Page
{
    private readonly StepListViewModel _viewModel;

    public StepListPage()
    {
        this.InitializeComponent();
        _viewModel = new StepListViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += async (s, e) => await _viewModel.LoadCommand.ExecuteAsync(null);

        _viewModel.ExportReady += OnExportReady;
    }

    private void OnExportReady(string json)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
#if HAS_UNO_SKIA
            try
            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("JSON Files", new List<string> { ".json" });
                picker.SuggestedFileName = "forgeboard-steps";

                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow)
                );

                StorageFile? file = await picker.PickSaveFileAsync();
                if (file is not null)
                {
                    await FileIO.WriteTextAsync(file, json);
                }
            }
            catch (Exception)
            {
                ShowExportFallbackDialog(json);
            }
#else
            ShowExportFallbackDialog(json);
#endif
        });
    }

    private void ShowExportFallbackDialog(string json)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            TextBox jsonBox = new TextBox
            {
                Text = json,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 11,
                MaxHeight = 400,
            };
            ScrollViewer.SetVerticalScrollBarVisibility(jsonBox, ScrollBarVisibility.Auto);

            ContentDialog dialog = new ContentDialog
            {
                Title = "Exported Steps JSON",
                Content = jsonBox,
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot,
                MinWidth = 600,
            };
            await dialog.ShowAsync();
        });
    }

    private void NewStep_Click(object sender, RoutedEventArgs e)
    {
        Frame? frame = this.Frame;
        frame?.Navigate(typeof(StepEditorPage));
    }

    private void EditStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            Frame? frame = this.Frame;
            frame?.Navigate(typeof(StepEditorPage), id);
        }
    }

    private async void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            ForgeBoard.Contracts.Models.BuildStepLibraryEntry? step =
                _viewModel.Steps.FirstOrDefault(s => s.Id == id);
            string name = step?.Name ?? id;

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

    private async void DuplicateStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.DuplicateCommand.ExecuteAsync(id);
        }
    }

    private async void ExportStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            await _viewModel.ExportSingleCommand.ExecuteAsync(id);
        }
    }

    private async void ExportAllSteps_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ExportAllCommand.ExecuteAsync(null);
    }

    private async void ImportSteps_Click(object sender, RoutedEventArgs e)
    {
#if HAS_UNO_SKIA
        try
        {
            FileOpenPicker picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");

            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow)
            );

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                string json = await FileIO.ReadTextAsync(file);
                await _viewModel.ImportFromJsonAsync(json);
            }
        }
        catch (Exception)
        {
            await ShowImportFallbackDialogAsync();
        }
#else
        await ShowImportFallbackDialogAsync();
#endif
    }

    private async Task ShowImportFallbackDialogAsync()
    {
        TextBox jsonBox = new TextBox
        {
            PlaceholderText = "Paste JSON here...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 11,
            MaxHeight = 400,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(jsonBox, ScrollBarVisibility.Auto);

        ContentDialog dialog = new ContentDialog
        {
            Title = "Import Steps from JSON",
            Content = jsonBox,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            MinWidth = 600,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(jsonBox.Text))
        {
            await _viewModel.ImportFromJsonAsync(jsonBox.Text);
        }
    }
}
