using ForgeBoard.Api.Hubs;
using ForgeBoard.Api.Services;
using ForgeBoard.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Uno.UI.Hosting;

namespace ForgeBoard;

internal class Program
{
    private static CancellationTokenSource? _apiShutdownCts;
    private static readonly ManualResetEventSlim ApiReady = new ManualResetEventSlim(false);

    [STAThread]
    public static async Task Main(string[] args)
    {
        App.InitializeLogging();

        _apiShutdownCts = new CancellationTokenSource();
        Thread apiThread = new Thread(() => StartApiHost(_apiShutdownCts.Token))
        {
            IsBackground = true,
            Name = "ForgeBoard-API",
        };
        apiThread.Start();

        WaitForApiReady();

        UnoPlatformHost host = UnoPlatformHostBuilder
            .Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        await host.RunAsync();

        _apiShutdownCts.Cancel();
        _apiShutdownCts.Dispose();
    }

    private static void WaitForApiReady()
    {
        using (HttpClient healthCheck = new HttpClient())
        {
            healthCheck.Timeout = TimeSpan.FromSeconds(2);

            for (int i = 0; i < 30; i++)
            {
                try
                {
                    HttpResponseMessage resp = healthCheck
                        .GetAsync("http://localhost:5050/health")
                        .GetAwaiter()
                        .GetResult();
                    if (resp.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine("API health check passed");
                        return;
                    }
                }
                catch (Exception) { }
                Thread.Sleep(500);
            }

            System.Diagnostics.Debug.WriteLine(
                "API health check timed out after 15 seconds, proceeding anyway"
            );
        }
    }

    private static void StartApiHost(CancellationToken cancellationToken)
    {
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(Array.Empty<string>());

            builder.WebHost.UseUrls("http://localhost:5050");

            builder.Services.AddForgeBoardCore();
            builder
                .Services.AddControllers()
                .AddApplicationPart(typeof(ForgeBoard.Api.Controllers.FeedsController).Assembly)
                .ConfigureApiBehaviorOptions(options =>
                {
                    options.SuppressModelStateInvalidFilter = true;
                });
            builder.Services.AddSignalR();
            builder.Services.AddHealthChecks();
            builder.Services.AddHostedService<BuildLogBroadcastService>();

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy
                        .SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            });

            WebApplication app = builder.Build();

            ForgeBoard.Core.Services.StepLibrarySeeder.SeedIfEmpty(
                app.Services.GetRequiredService<ForgeBoard.Core.Data.ForgeBoardDatabase>()
            );

            app.UseCors();
            app.MapControllers();
            app.MapHub<BuildLogHub>("/hubs/buildlog");
            app.MapHealthChecks("/health");

            app.Lifetime.ApplicationStopping.Register(() => { });
            Task runTask = app.RunAsync();
            cancellationToken.Register(() => app.StopAsync().GetAwaiter().GetResult());
            runTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API host failed: {ex.Message}");
        }
    }
}
