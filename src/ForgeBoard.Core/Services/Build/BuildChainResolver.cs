using System.Collections.Concurrent;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildChainResolver
{
    private readonly ForgeBoardDatabase _db;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _pendingChains =
        new ConcurrentDictionary<string, HashSet<string>>();

    public BuildChainResolver(ForgeBoardDatabase db, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _logger = logger;
    }

    public async Task<string?> ResolveBaseImageAsync(
        string executionId,
        BaseImage baseImage,
        Feed feed,
        Func<string, CancellationToken, Task<BuildExecution>> startBuildFunc,
        Action<string, Contracts.Models.LogLevel, string> addLog,
        CancellationToken cancellationToken
    )
    {
        string sourceBuildDefId = feed.ConnectionString;

        if (string.IsNullOrEmpty(sourceBuildDefId))
        {
            return null;
        }

        DetectCircularDependency(executionId, sourceBuildDefId);

        string? recentPath = CheckForRecentArtifact(executionId, sourceBuildDefId, addLog);
        if (recentPath is not null)
        {
            return recentPath;
        }

        addLog(
            executionId,
            Contracts.Models.LogLevel.Info,
            $"No existing artifact found, triggering chained build for definition {sourceBuildDefId}"
        );

        HashSet<string> chainSet = _pendingChains.GetOrAdd(executionId, _ => new HashSet<string>());
        chainSet.Add(sourceBuildDefId);

        try
        {
            BuildExecution chainedExecution = await startBuildFunc(
                sourceBuildDefId,
                cancellationToken
            );

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                BuildExecution? chainedStatus = _db.BuildExecutions.FindById(chainedExecution.Id);
                if (chainedStatus is null)
                {
                    throw new InvalidOperationException(
                        $"Chained build execution {chainedExecution.Id} disappeared"
                    );
                }

                if (chainedStatus.Status == BuildStatus.Succeeded)
                {
                    if (chainedStatus.ArtifactId is not null)
                    {
                        ImageArtifact? artifact = _db.ImageArtifacts.FindById(
                            chainedStatus.ArtifactId
                        );
                        if (artifact is not null && File.Exists(artifact.FilePath))
                        {
                            return artifact.FilePath;
                        }
                    }
                    break;
                }

                if (
                    chainedStatus.Status == BuildStatus.Failed
                    || chainedStatus.Status == BuildStatus.Cancelled
                )
                {
                    throw new InvalidOperationException(
                        $"Chained build {chainedExecution.Id} ended with status {chainedStatus.Status}: {chainedStatus.ErrorMessage}"
                    );
                }
            }
        }
        finally
        {
            chainSet.Remove(sourceBuildDefId);
        }

        return null;
    }

    private void DetectCircularDependency(string executionId, string sourceBuildDefId)
    {
        if (
            _pendingChains.TryGetValue(executionId, out HashSet<string>? visited)
            && visited.Contains(sourceBuildDefId)
        )
        {
            throw new InvalidOperationException(
                $"Circular dependency detected: build definition {sourceBuildDefId} is part of the build chain for execution {executionId}"
            );
        }
    }

    private string? CheckForRecentArtifact(
        string executionId,
        string sourceBuildDefId,
        Action<string, Contracts.Models.LogLevel, string> addLog
    )
    {
        ImageArtifact? existingArtifact = _db
            .ImageArtifacts.Find(a => a.BuildDefinitionId == sourceBuildDefId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

        if (existingArtifact is not null && File.Exists(existingArtifact.FilePath))
        {
            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Found existing artifact from definition {sourceBuildDefId}: {existingArtifact.FilePath}"
            );
            return existingArtifact.FilePath;
        }

        return null;
    }
}
