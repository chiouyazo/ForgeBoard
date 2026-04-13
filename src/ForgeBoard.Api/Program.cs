using System.Reflection;
using ForgeBoard.Api.Hubs;
using ForgeBoard.Api.Services;
using ForgeBoard.Core;
using Microsoft.OpenApi.Models;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "ForgeBoard";
});

string? configuredLogDir = builder.Configuration.GetValue<string>("ForgeBoard:DataDirectory");
string logDir = !string.IsNullOrEmpty(configuredLogDir)
    ? Path.Combine(configuredLogDir, "logs")
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForgeBoard",
        "logs"
    );

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Cors", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDir, "api-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

string? dataDirectory = builder.Configuration.GetValue<string>("ForgeBoard:DataDirectory");
string? tempDirectory = builder.Configuration.GetValue<string>("ForgeBoard:TempDirectory");

if (string.IsNullOrEmpty(dataDirectory))
    dataDirectory = null;
if (string.IsNullOrEmpty(tempDirectory))
    tempDirectory = null;

Log.Information("Data directory: {DataDir}", dataDirectory ?? "(default)");
Log.Information("Temp directory: {TempDir}", tempDirectory ?? "(default)");

builder.Services.AddForgeBoardCore(dataDirectory, tempDirectory);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "ForgeBoard API",
            Description =
                "REST API for managing Packer VM image builds, feeds, steps, and settings.",
            Version = "v1",
        }
    );

    string xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder
    .Services.AddControllers()
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
        policy.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    });
});

int port = builder.Configuration.GetValue<int>("ForgeBoard:Port");
if (port <= 0)
    port = 5050;
builder.WebHost.UseUrls($"http://+:{port}");
Log.Information("Listening on port {Port}", port);

WebApplication app;
try
{
    app = builder.Build();
}
catch (InvalidOperationException ex)
    when (ex.Message.Contains("drive") || ex.Message.Contains("not available"))
{
    Log.Fatal(ex, "Startup failed: {Message}", ex.Message);
    Log.CloseAndFlush();
    return;
}

ForgeBoard.Contracts.Interfaces.IAppPaths resolvedPaths =
    app.Services.GetRequiredService<ForgeBoard.Contracts.Interfaces.IAppPaths>();
ForgeBoard.Core.Services.Build.PowerShellRunner.ConfigureTempBasePath(resolvedPaths.TempDirectory);

try
{
    ForgeBoard.Core.Services.StepLibrarySeeder.SeedIfEmpty(
        app.Services.GetRequiredService<ForgeBoard.Core.Data.ForgeBoardDatabase>()
    );
}
catch (Exception ex)
{
    Log.Fatal(ex, "Failed to initialize database: {Message}", ex.Message);
    Log.CloseAndFlush();
    return;
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

string wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(
        new StaticFileOptions
        {
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        }
    );
    Log.Information("Serving WASM frontend from {Path}", wwwrootPath);
}

app.MapControllers();
app.MapHub<BuildLogHub>("/hubs/buildlog");
app.MapHealthChecks("/health");

if (Directory.Exists(wwwrootPath))
{
    app.MapFallbackToFile("index.html");
}

await app.RunAsync();
