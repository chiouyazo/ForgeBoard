using System.Threading.Channels;
using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services.Build;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class BuildOrchestrator : IBuildOrchestrator, IDisposable
{
    private readonly ForgeBoardDatabase _db;
    private readonly PackerService _packerService;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<BuildOrchestrator> _logger;
    private readonly BuildExecutionWorker _worker;
    private readonly BuildWorkspaceManager _workspaceManager;
    private readonly Channel<string> _buildQueue;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly Task _workerTask;

    public event Action<string, BuildLogEntry>? OnBuildLogReceived;
    public event Action<string, BuildStatus>? OnBuildStatusChanged;

    public BuildOrchestrator(
        ForgeBoardDatabase db,
        PackerService packerService,
        IPackerTemplateGenerator templateGenerator,
        ICacheService cacheService,
        IAppPaths appPaths,
        IEnumerable<IPostProcessor> postProcessors,
        BuildFileServer fileServer,
        DirectBuildEngine directEngine,
        PackerBuildEngine packerEngine,
        BuildPhaseExecutor phaseExecutor,
        IHostApplicationLifetime lifetime,
        ILogger<BuildOrchestrator> logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(packerService);
        ArgumentNullException.ThrowIfNull(templateGenerator);
        ArgumentNullException.ThrowIfNull(cacheService);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(postProcessors);
        ArgumentNullException.ThrowIfNull(fileServer);
        ArgumentNullException.ThrowIfNull(directEngine);
        ArgumentNullException.ThrowIfNull(packerEngine);
        ArgumentNullException.ThrowIfNull(phaseExecutor);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _packerService = packerService;
        _appPaths = appPaths;
        _logger = logger;

        Dictionary<string, IPostProcessor> postProcessorMap = postProcessors.ToDictionary(
            p => p.Name,
            StringComparer.OrdinalIgnoreCase
        );

        BuildChainResolver chainResolver = new BuildChainResolver(db, logger);
        _workspaceManager = new BuildWorkspaceManager(logger);

        _worker = new BuildExecutionWorker(
            db,
            phaseExecutor,
            cacheService,
            appPaths,
            postProcessorMap,
            chainResolver,
            _workspaceManager,
            logger
        );

        _buildQueue = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );

        _shutdownCts = new CancellationTokenSource();

        RecoverFromPreviousShutdown();

        lifetime.ApplicationStopping.Register(OnApplicationStopping);

        _workerTask = Task
            .Factory.StartNew(
                () => ProcessBuildQueueAsync(_shutdownCts.Token),
                _shutdownCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            )
            .Unwrap();
    }

    public async Task<BuildExecution> StartBuildAsync(
        string buildDefinitionId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(buildDefinitionId);

        BuildDefinition? definition = _db.BuildDefinitions.FindById(buildDefinitionId);
        if (definition is null)
        {
            throw new InvalidOperationException($"Build definition {buildDefinitionId} not found");
        }

        if (
            !definition.BaseImageId.StartsWith(BaseImagePrefixes.BuildChain)
            && !definition.BaseImageId.StartsWith(BaseImagePrefixes.Artifact)
        )
        {
            BaseImage? baseImage = _db.BaseImages.FindById(definition.BaseImageId);
            if (baseImage is null)
            {
                throw new InvalidOperationException(
                    $"Base image {definition.BaseImageId} not found"
                );
            }
        }

        BuildExecution execution = new BuildExecution
        {
            Id = Guid.NewGuid().ToString("N"),
            BuildDefinitionId = buildDefinitionId,
            Status = BuildStatus.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        _db.BuildExecutions.Insert(execution);

        _logger.LogInformation(
            "Queued build execution {ExecutionId} for definition {DefinitionId}",
            execution.Id,
            buildDefinitionId
        );

        await _buildQueue.Writer.WriteAsync(execution.Id, cancellationToken);

        return execution;
    }

    public async Task CancelBuildAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);

        BuildExecution? execution = _db.BuildExecutions.FindById(executionId);
        if (execution is null)
        {
            return;
        }

        await _packerService.CancelBuildAsync(executionId, cancellationToken);

        execution.Status = BuildStatus.Cancelled;
        execution.CompletedAt = DateTimeOffset.UtcNow;
        _db.BuildExecutions.Update(execution);
        OnBuildStatusChanged?.Invoke(executionId, BuildStatus.Cancelled);

        AddLog(executionId, Contracts.Models.LogLevel.Warning, "Build cancelled by user");
        _logger.LogInformation("Cancelled build execution {ExecutionId}", executionId);
    }

    public Task<BuildExecution?> GetExecutionAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);
        BuildExecution? result = _db.BuildExecutions.FindById(executionId);
        return Task.FromResult<BuildExecution?>(result);
    }

    public Task<List<BuildExecution>> GetExecutionsAsync(
        string? buildDefinitionId = null,
        CancellationToken cancellationToken = default
    )
    {
        List<BuildExecution> result;

        if (buildDefinitionId is not null)
        {
            result = _db
                .BuildExecutions.Find(e => e.BuildDefinitionId == buildDefinitionId)
                .OrderByDescending(e => e.QueuedAt)
                .ToList();
        }
        else
        {
            result = _db.BuildExecutions.FindAll().OrderByDescending(e => e.QueuedAt).ToList();
        }

        return Task.FromResult(result);
    }

    public Task<List<BuildLogEntry>> GetLogsAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);

        List<BuildLogEntry> result = _db
            .BuildLogs.Find(l => l.BuildExecutionId == executionId)
            .OrderBy(l => l.Timestamp)
            .ToList();

        return Task.FromResult(result);
    }

    public Task DeleteDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        bool hasRunningExecution = _db
            .BuildExecutions.Find(e => e.BuildDefinitionId == id)
            .Any(e =>
                e.Status == BuildStatus.Running
                || e.Status == BuildStatus.Preparing
                || e.Status == BuildStatus.WaitingForChain
                || e.Status == BuildStatus.Queued
            );

        if (hasRunningExecution)
        {
            throw new InvalidOperationException(
                $"Cannot delete build definition {id} because it has a running or queued execution"
            );
        }

        _db.BuildDefinitions.Delete(id);
        _logger.LogInformation("Deleted build definition {Id}", id);

        return Task.CompletedTask;
    }

    public Task DeleteExecutionAsync(
        string executionId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);

        BuildExecution? execution = _db.BuildExecutions.FindById(executionId);
        if (execution is null)
        {
            return Task.CompletedTask;
        }

        if (execution.Status == BuildStatus.Running || execution.Status == BuildStatus.Preparing)
        {
            throw new InvalidOperationException(
                $"Cannot delete execution {executionId} because it is still active"
            );
        }

        List<BuildLogEntry> logs = _db
            .BuildLogs.Find(l => l.BuildExecutionId == executionId)
            .ToList();

        foreach (BuildLogEntry log in logs)
        {
            _db.BuildLogs.Delete(log.Id);
        }

        _db.BuildExecutions.Delete(executionId);
        _logger.LogInformation(
            "Deleted execution {ExecutionId} and {LogCount} associated log entries",
            executionId,
            logs.Count
        );

        return Task.CompletedTask;
    }

    private void RecoverFromPreviousShutdown()
    {
        List<BuildExecution> interrupted = _db
            .BuildExecutions.Find(e =>
                e.Status == BuildStatus.Running
                || e.Status == BuildStatus.Preparing
                || e.Status == BuildStatus.WaitingForChain
            )
            .ToList();

        foreach (BuildExecution execution in interrupted)
        {
            execution.Status = BuildStatus.Failed;
            execution.ErrorMessage = "Interrupted by previous shutdown";
            execution.CompletedAt = DateTimeOffset.UtcNow;
            _db.BuildExecutions.Update(execution);

            _logger.LogWarning(
                "Marked interrupted execution {ExecutionId} as Failed",
                execution.Id
            );
        }

        CleanOrphanedWorkspaces();
    }

    private void OnApplicationStopping()
    {
        _logger.LogInformation("Application stopping, cancelling all running builds...");

        _buildQueue.Writer.TryComplete();
        _shutdownCts.Cancel();

        List<BuildExecution> activeExecutions = _db
            .BuildExecutions.Find(e =>
                e.Status == BuildStatus.Running
                || e.Status == BuildStatus.Preparing
                || e.Status == BuildStatus.WaitingForChain
                || e.Status == BuildStatus.Queued
            )
            .ToList();

        foreach (BuildExecution execution in activeExecutions)
        {
            _packerService
                .CancelBuildAsync(execution.Id, CancellationToken.None)
                .Wait(TimeSpan.FromSeconds(5));

            execution.Status = BuildStatus.Cancelled;
            execution.CompletedAt = DateTimeOffset.UtcNow;
            execution.ErrorMessage = "Cancelled due to application shutdown";
            _db.BuildExecutions.Update(execution);

            if (execution.WorkingDirectory is not null)
            {
                _workspaceManager.CleanupWorkspace(execution.WorkingDirectory);
            }
        }

        _logger.LogInformation(
            "Cancelled {Count} active builds during shutdown",
            activeExecutions.Count
        );
    }

    private void CleanOrphanedWorkspaces()
    {
        if (!Directory.Exists(_appPaths.WorkingDirectory))
        {
            return;
        }

        try
        {
            foreach (string dir in Directory.GetDirectories(_appPaths.WorkingDirectory))
            {
                _workspaceManager.CleanupWorkspace(dir);
            }

            _logger.LogInformation("Cleaned orphaned workspace directories");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean orphaned workspace directories");
        }
    }

    private async Task<BuildExecution> StartChainedBuildDirectAsync(
        string buildDefinitionId,
        CancellationToken cancellationToken
    )
    {
        BuildDefinition? definition = _db.BuildDefinitions.FindById(buildDefinitionId);
        if (definition is null)
        {
            throw new InvalidOperationException($"Build definition {buildDefinitionId} not found");
        }

        BuildExecution execution = new BuildExecution
        {
            Id = Guid.NewGuid().ToString("N"),
            BuildDefinitionId = buildDefinitionId,
            Status = BuildStatus.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        _db.BuildExecutions.Insert(execution);
        _logger.LogInformation(
            "Starting chained build {ExecutionId} for definition {DefinitionId} (direct, bypassing queue)",
            execution.Id,
            buildDefinitionId
        );

        // Run the chained build directly instead of queuing it
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _worker.ExecuteBuildAsync(
                        execution.Id,
                        StartChainedBuildDirectAsync,
                        AddLog,
                        (id, status) => OnBuildStatusChanged?.Invoke(id, status),
                        cancellationToken
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chained build {ExecutionId} failed", execution.Id);
                }
            },
            cancellationToken
        );

        return execution;
    }

    private async Task ProcessBuildQueueAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Build queue worker started");

        try
        {
            await foreach (string executionId in _buildQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await _worker.ExecuteBuildAsync(
                        executionId,
                        StartChainedBuildDirectAsync,
                        AddLog,
                        (id, status) => OnBuildStatusChanged?.Invoke(id, status),
                        cancellationToken
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unhandled error processing build execution {ExecutionId}",
                        executionId
                    );
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Build queue worker shutting down");
        }
    }

    private void AddLog(string executionId, Contracts.Models.LogLevel level, string message)
    {
        BuildLogEntry logEntry = new BuildLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            BuildExecutionId = executionId,
            Timestamp = DateTimeOffset.UtcNow,
            Level = level,
            Message = message,
        };

        _db.BuildLogs.Insert(logEntry);
        OnBuildLogReceived?.Invoke(executionId, logEntry);
    }

    public void Dispose()
    {
        _buildQueue.Writer.TryComplete();
        _shutdownCts.Cancel();

        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException) { }

        _shutdownCts.Dispose();
    }
}
