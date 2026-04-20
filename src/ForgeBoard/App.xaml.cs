using System;
using ForgeBoard.Services;
using ForgeBoard.Views;
using Microsoft.Extensions.Logging;
using Serilog;
using Uno.Resizetizer;

namespace ForgeBoard;

public partial class App : Application
{
    public static ApiClient ApiClient { get; } = CreateApiClient();

    private static ApiClient CreateApiClient()
    {
        string url = "http://localhost:5050";
#if __WASM__
        try
        {
            string origin = Uno.Foundation.WebAssemblyRuntime.InvokeJS(
                "(() => { return window.location.origin; })()"
            );
            if (!string.IsNullOrEmpty(origin))
            {
                url = origin;
            }
        }
        catch { }
#endif
        return new ApiClient(url);
    }

    public App()
    {
        this.InitializeComponent();
        InitializeSerilog();
    }

    private static void InitializeSerilog()
    {
        LoggerConfiguration config = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console();

#if HAS_UNO_SKIA
        string logDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgeBoard",
            "logs"
        );
        config = config.WriteTo.File(
            System.IO.Path.Combine(logDirectory, "client-.log"),
            rollingInterval: Serilog.RollingInterval.Day
        );
#endif

        Log.Logger = config.CreateLogger();
        Log.Information("ForgeBoard client started");
    }

    public static new Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
#if DEBUG && HAS_UNO_SKIA
        MainWindow.UseStudio();
#endif

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(Shell), args.Arguments);
        }

        MainWindow.SetWindowIcon();
        MainWindow.Activate();
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException(
            $"Failed to load {e.SourcePageType.FullName}: {e.Exception}"
        );
    }

    public static void InitializeLogging()
    {
#if DEBUG
        ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(
                new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider()
            );
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());
            builder.AddConsole();
#else
            builder.AddConsole();
#endif
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
