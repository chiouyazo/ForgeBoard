using ForgeBoard.Views.Builds;
using ForgeBoard.Views.Dashboard;
using ForgeBoard.Views.Images;
using ForgeBoard.Views.Settings;
using ForgeBoard.Views.StepLibrary;

namespace ForgeBoard.Views;

public sealed partial class Shell : Page
{
    public static Shell? Current { get; private set; }
    private Button? _activeNavButton;

    public Shell()
    {
        this.InitializeComponent();
        Current = this;
        _activeNavButton = NavDashboard;
        this.Loaded += Shell_Loaded;
    }

    private void Shell_Loaded(object sender, RoutedEventArgs e)
    {
        string route = GetCurrentRoute();
        HandleRoute(route);
    }

    public void NavigateTo(Type pageType, Button navButton, object? parameter = null)
    {
        if (_activeNavButton != null)
        {
            _activeNavButton.Style = (Style)Application.Current.Resources["NavButtonStyle"];
        }
        navButton.Style = (Style)Application.Current.Resources["NavButtonActiveStyle"];
        _activeNavButton = navButton;

        if (parameter is not null)
        {
            ContentFrame.Navigate(pageType, parameter);
        }
        else
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void HandleRoute(string route)
    {
        string path = route.Split('?')[0].Trim('/').ToLowerInvariant();
        Dictionary<string, string> query = ParseQuery(route);

        switch (path)
        {
            case "builds":
                NavigateTo(typeof(BuildListPage), NavBuilds);
                break;

            case "builds/new":
                NavigateTo(typeof(BuildWizardPage), NavBuilds);
                break;

            case "builds/edit" when query.TryGetValue("id", out string? defId):
                NavigateTo(typeof(BuildWizardPage), NavBuilds, defId);
                break;

            case "builds/detail" when query.TryGetValue("id", out string? execId):
                NavigateTo(typeof(BuildDetailPage), NavBuilds, execId);
                break;

            case "images":
                NavigateTo(typeof(ImageListPage), NavImages);
                break;

            case "steps":
                NavigateTo(typeof(StepListPage), NavSteps);
                break;

            case "settings":
                NavigateTo(typeof(SettingsPage), NavSettings);
                break;

            case "dashboard" when query.TryGetValue("build", out string? buildExecId):
                NavigateTo(typeof(BuildDetailPage), NavBuilds, buildExecId);
                break;

            default:
                NavigateTo(typeof(DashboardPage), NavDashboard);
                break;
        }
    }

    public void UpdateBrowserUrl(string route)
    {
#if __WASM__
        try
        {
            Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                $"window.history.pushState(null, '', '/{route}')"
            );
        }
        catch { }
#endif
    }

    private static string GetCurrentRoute()
    {
#if __WASM__
        try
        {
            string url = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "window.location.pathname + window.location.search"
            );
            return url ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
#else
        return string.Empty;
#endif
    }

    private static Dictionary<string, string> ParseQuery(string route)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        int qIndex = route.IndexOf('?');
        if (qIndex < 0)
            return result;

        string queryString = route[(qIndex + 1)..];
        foreach (string pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
        }
        return result;
    }

    private DispatcherTimer? _notificationTimer;

    public void ShowNotification(string message, int durationMs = 4000)
    {
        ShowNotificationInternal(message, "AccentBrush", durationMs);
    }

    public void ShowError(string message, int durationMs = 6000)
    {
        ShowNotificationInternal(message, "ErrorBrush", durationMs);
    }

    public void ShowWarning(string message, int durationMs = 5000)
    {
        ShowNotificationInternal(message, "WarningBrush", durationMs);
    }

    private void ShowNotificationInternal(string message, string brushKey, int durationMs)
    {
        _notificationTimer?.Stop();

        NotificationBar.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources[brushKey];
        NotificationText.Text = message;
        NotificationBar.Visibility = Visibility.Visible;

        StatusText.Text = message;

        _notificationTimer = new DispatcherTimer();
        _notificationTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _notificationTimer.Tick += (s, e) =>
        {
            NotificationBar.Visibility = Visibility.Collapsed;
            StatusText.Text = "Ready";
            _notificationTimer?.Stop();
        };
        _notificationTimer.Start();
    }

    private void DismissNotification_Click(object sender, RoutedEventArgs e)
    {
        _notificationTimer?.Stop();
        NotificationBar.Visibility = Visibility.Collapsed;
        StatusText.Text = "Ready";
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(DashboardPage), NavDashboard);
        UpdateBrowserUrl("dashboard");
    }

    private void NavBuilds_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(BuildListPage), NavBuilds);
        UpdateBrowserUrl("builds");
    }

    private void NavImages_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(ImageListPage), NavImages);
        UpdateBrowserUrl("images");
    }

    private void NavSteps_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(StepListPage), NavSteps);
        UpdateBrowserUrl("steps");
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(SettingsPage), NavSettings);
        UpdateBrowserUrl("settings");
    }
}
