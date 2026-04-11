using ForgeBoard.Services;
using ForgeBoard.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ForgeBoard.Views.Settings;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage()
    {
        this.InitializeComponent();
        _viewModel = new SettingsViewModel(App.ApiClient);
        this.DataContext = _viewModel;
        this.Loaded += async (s, e) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void BrowsePackerPath_Click(object sender, RoutedEventArgs e)
    {
        await ShowFallbackPathDialogAsync();
    }

    private async void PackerPath_LostFocus(object sender, RoutedEventArgs e)
    {
        string path = _viewModel.PackerPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.ValidatePackerPathAsync(path);
        }
    }

    private async Task ShowFallbackPathDialogAsync()
    {
        TextBox pathInput = new TextBox
        {
            PlaceholderText = @"C:\Program Files\Packer\packer.exe",
            Header = "Path to packer executable",
            Text = _viewModel.PackerPath ?? string.Empty,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
        };

        ContentDialog dialog = new ContentDialog
        {
            Title = "Set Packer Path",
            Content = pathInput,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(pathInput.Text))
        {
            _viewModel.PackerPath = pathInput.Text.Trim();
            await _viewModel.ValidatePackerPathAsync(_viewModel.PackerPath);
        }
    }
}
