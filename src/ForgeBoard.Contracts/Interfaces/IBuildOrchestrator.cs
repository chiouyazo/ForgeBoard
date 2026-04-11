using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface IBuildOrchestrator
{
    event Action<string, BuildLogEntry>? OnBuildLogReceived;

    event Action<string, BuildStatus>? OnBuildStatusChanged;

    Task<BuildExecution> StartBuildAsync(
        string buildDefinitionId,
        CancellationToken cancellationToken = default
    );

    Task CancelBuildAsync(string executionId, CancellationToken cancellationToken = default);

    Task<BuildExecution?> GetExecutionAsync(
        string executionId,
        CancellationToken cancellationToken = default
    );

    Task<List<BuildExecution>> GetExecutionsAsync(
        string? buildDefinitionId = null,
        CancellationToken cancellationToken = default
    );

    Task<List<BuildLogEntry>> GetLogsAsync(
        string executionId,
        CancellationToken cancellationToken = default
    );

    Task DeleteDefinitionAsync(string id, CancellationToken cancellationToken = default);

    Task DeleteExecutionAsync(string executionId, CancellationToken cancellationToken = default);
}
