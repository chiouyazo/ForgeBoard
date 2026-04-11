using ForgeBoard.Api.Hubs;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.AspNetCore.SignalR;

namespace ForgeBoard.Api.Services;

public sealed class BuildLogBroadcastService : IHostedService
{
    private readonly IBuildOrchestrator _orchestrator;
    private readonly IHubContext<BuildLogHub> _hubContext;
    private readonly ILogger<BuildLogBroadcastService> _logger;

    public BuildLogBroadcastService(
        IBuildOrchestrator orchestrator,
        IHubContext<BuildLogHub> hubContext,
        ILogger<BuildLogBroadcastService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(hubContext);
        ArgumentNullException.ThrowIfNull(logger);

        _orchestrator = orchestrator;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _orchestrator.OnBuildLogReceived += HandleBuildLogReceived;
        _orchestrator.OnBuildStatusChanged += HandleBuildStatusChanged;
        _logger.LogInformation("Build log broadcast service started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _orchestrator.OnBuildLogReceived -= HandleBuildLogReceived;
        _orchestrator.OnBuildStatusChanged -= HandleBuildStatusChanged;
        _logger.LogInformation("Build log broadcast service stopped");
        return Task.CompletedTask;
    }

    private void HandleBuildLogReceived(string executionId, BuildLogEntry logEntry)
    {
        _ = BroadcastLogAsync(executionId, logEntry);
    }

    private void HandleBuildStatusChanged(string executionId, BuildStatus status)
    {
        _ = BroadcastStatusAsync(executionId, status);
    }

    private async Task BroadcastLogAsync(string executionId, BuildLogEntry logEntry)
    {
        try
        {
            await _hubContext.Clients.Group(executionId).SendAsync("BuildLogReceived", logEntry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast build log for execution {ExecutionId}",
                executionId
            );
        }
    }

    private async Task BroadcastStatusAsync(string executionId, BuildStatus status)
    {
        try
        {
            await _hubContext
                .Clients.Group(executionId)
                .SendAsync("BuildStatusChanged", executionId, status.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast build status for execution {ExecutionId}",
                executionId
            );
        }
    }
}
