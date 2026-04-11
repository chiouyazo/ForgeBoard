using Microsoft.AspNetCore.SignalR;

namespace ForgeBoard.Api.Hubs;

public sealed class BuildLogHub : Hub
{
    private readonly ILogger<BuildLogHub> _logger;

    public BuildLogHub(ILogger<BuildLogHub> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task JoinBuild(string executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, executionId);
        _logger.LogInformation(
            "Client {ConnectionId} joined build group {ExecutionId}",
            Context.ConnectionId,
            executionId
        );
    }

    public async Task LeaveBuild(string executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, executionId);
        _logger.LogInformation(
            "Client {ConnectionId} left build group {ExecutionId}",
            Context.ConnectionId,
            executionId
        );
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
