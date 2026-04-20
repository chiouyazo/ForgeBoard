using System.Reflection;
using System.Runtime.InteropServices;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Services;
using Microsoft.UI.Xaml.Media;

namespace ForgeBoard.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty]
    private AppSettings? _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackerVersionBrush))]
    private string _packerVersion = "Unknown";

    [ObservableProperty]
    private int _maxConcurrentBuilds = 1;

    [ObservableProperty]
    private long _maxCacheSizeGb = 100;

    [ObservableProperty]
    private string _defaultUnattendPath = string.Empty;

    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    [ObservableProperty]
    private PackerBuilder _defaultBuilder = PackerBuilder.Qemu;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    private string _packerPath = string.Empty;

    [ObservableProperty]
    private string _detectedPackerPath = string.Empty;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    [ObservableProperty]
    private bool _packerVersionValid;

    [ObservableProperty]
    private string _winrmUsername = "Administrator";

    [ObservableProperty]
    private string _winrmPassword = "Admin123!";

    [ObservableProperty]
    private string _currentDataDirectory = string.Empty;

    [ObservableProperty]
    private string _currentTempDirectory = string.Empty;

    [ObservableProperty]
    private string _currentDatabasePath = string.Empty;

    [ObservableProperty]
    private string _newDataDirectory = string.Empty;

    [ObservableProperty]
    private string _newTempDirectory = string.Empty;

    [ObservableProperty]
    private bool _storageRestartRequired;

    public Brush PackerVersionBrush
    {
        get
        {
            if (
                PackerVersion.StartsWith("Packer", StringComparison.OrdinalIgnoreCase)
                && !PackerVersion.Contains("not found", StringComparison.OrdinalIgnoreCase)
                && !PackerVersion.Contains("failed", StringComparison.OrdinalIgnoreCase)
            )
            {
                return (Brush)Application.Current.Resources["SuccessBrush"];
            }

            if (PackerVersion == "Unknown" || PackerVersion == "Searching...")
            {
                return (Brush)Application.Current.Resources["MutedBrush"];
            }

            if (
                PackerVersion.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || PackerVersion.Contains("failed", StringComparison.OrdinalIgnoreCase)
            )
            {
                return (Brush)Application.Current.Resources["ErrorBrush"];
            }

            // Assume a version string like "1.x.x" is valid
            return (Brush)Application.Current.Resources["SuccessBrush"];
        }
    }

    public bool IsLocalMode
    {
        get
        {
#if HAS_UNO_SKIA
            return true;
#else
            return false;
#endif
        }
    }

    public string ForgeBoardVersion
    {
        get
        {
            Assembly assembly = typeof(SettingsViewModel).Assembly;
            AssemblyInformationalVersionAttribute? info =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string version =
                info?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";

            int plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version[..plusIndex];
            }

            return $"ForgeBoard v{version}";
        }
    }

    public SettingsViewModel(ApiClient api)
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
            Settings = await _api.GetSettingsAsync();
            MaxConcurrentBuilds = Settings.MaxConcurrentBuilds;
            MaxCacheSizeGb = Settings.MaxCacheSizeBytes / (1024 * 1024 * 1024);
            DefaultUnattendPath = Settings.DefaultUnattendPath ?? string.Empty;
            ProxyUrl = Settings.ProxyUrl ?? string.Empty;
            PackerPath = Settings.PackerPath ?? string.Empty;
            DefaultBuilder = Settings.DefaultBuilder;
            WinrmUsername = Settings.WinrmUsername ?? "Administrator";
            WinrmPassword = Settings.WinrmPassword ?? "Admin123!";

            await LoadStorageAsync();
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

    private async Task LoadStorageAsync()
    {
        StoragePaths paths = await _api.GetStoragePathsAsync();
        CurrentDataDirectory = paths.DataDirectory;
        CurrentTempDirectory = paths.TempDirectory;
        CurrentDatabasePath = paths.DatabasePath;
        NewDataDirectory = paths.DataDirectory;
        NewTempDirectory = paths.TempDirectory;
        StorageRestartRequired = false;
    }

    [RelayCommand]
    private async Task SaveStorageAsync()
    {
        ErrorMessage = null;
        try
        {
            StoragePathsUpdateRequest request = new StoragePathsUpdateRequest
            {
                DataDirectory = NewDataDirectory?.Trim() ?? string.Empty,
                TempDirectory = NewTempDirectory?.Trim() ?? string.Empty,
            };

            await _api.UpdateStoragePathsAsync(request);

            StorageRestartRequired = true;
            Views.Shell.Current?.ShowNotification(
                "Storage paths saved. Restart the API to apply changes."
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task RestartApiAsync()
    {
        ErrorMessage = null;
        try
        {
            await _api.RestartApiAsync();
            Views.Shell.Current?.ShowNotification(
                "Restart triggered. The API will be unavailable for a few seconds."
            );
            StorageRestartRequired = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Settings is null)
            return;

        ErrorMessage = null;
        SuccessMessage = null;
        try
        {
            Settings.MaxConcurrentBuilds = MaxConcurrentBuilds;
            Settings.MaxCacheSizeBytes = MaxCacheSizeGb * 1024 * 1024 * 1024;
            Settings.DefaultUnattendPath = string.IsNullOrWhiteSpace(DefaultUnattendPath)
                ? null
                : DefaultUnattendPath;
            Settings.ProxyUrl = string.IsNullOrWhiteSpace(ProxyUrl) ? null : ProxyUrl;
            Settings.PackerPath = string.IsNullOrWhiteSpace(PackerPath) ? null : PackerPath;
            Settings.DefaultBuilder = DefaultBuilder;
            Settings.WinrmUsername = string.IsNullOrWhiteSpace(WinrmUsername)
                ? "Administrator"
                : WinrmUsername;
            Settings.WinrmPassword = string.IsNullOrWhiteSpace(WinrmPassword)
                ? "Admin123!"
                : WinrmPassword;
            Settings.ModifiedAt = DateTimeOffset.UtcNow;

            await _api.SaveSettingsAsync(Settings);

            if (!string.IsNullOrWhiteSpace(PackerPath))
            {
                await ValidatePackerPathAsync(PackerPath);
            }

            SuccessMessage = "Settings saved.";
            Views.Shell.Current?.ShowNotification("Settings saved.");
            SaveStatus = "Saved";
            _ = ClearSaveStatusAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Views.Shell.Current?.ShowError(ex.Message);
        }
    }

    public async Task ValidatePackerPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            PackerVersion = "Unknown";
            return;
        }

        PackerVersion = "Validating...";
        try
        {
            string version = await _api.GetPackerVersionAsync(path);
            PackerVersion = version;
        }
        catch (Exception ex)
        {
            PackerVersion = $"Validation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DetectPackerAsync()
    {
        ErrorMessage = null;
        PackerVersion = "Searching...";
        DetectedPackerPath = string.Empty;

        string? foundPath = SearchForPacker();

        if (foundPath is null)
        {
            PackerVersion = "Packer not found";
            return;
        }

        DetectedPackerPath = foundPath;
        PackerPath = foundPath;

        try
        {
            string version = await _api.GetPackerVersionAsync(foundPath);
            PackerVersion = version;
        }
        catch (Exception ex)
        {
            PackerVersion = $"Found at {foundPath} but version check failed: {ex.Message}";
        }
    }

    private async Task ClearSaveStatusAsync()
    {
        await Task.Delay(3000);
        SaveStatus = string.Empty;
    }

    private static string? SearchForPacker()
    {
        string packerExecutable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "packer.exe"
            : "packer";

        string? pathResult = SearchInPath(packerExecutable);
        if (pathResult is not null)
        {
            return pathResult;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            List<string> windowsPaths =
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Packer",
                    packerExecutable
                ),
                Path.Combine("C:\\HashiCorp\\Packer", packerExecutable),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "packer",
                    packerExecutable
                ),
            ];

            foreach (string candidate in windowsPaths)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        else
        {
            List<string> linuxPaths = ["/usr/local/bin/packer", "/usr/bin/packer"];

            foreach (string candidate in linuxPaths)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? SearchInPath(string executable)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        char separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        string[] directories = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string directory in directories)
        {
            string fullPath = Path.Combine(directory, executable);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
