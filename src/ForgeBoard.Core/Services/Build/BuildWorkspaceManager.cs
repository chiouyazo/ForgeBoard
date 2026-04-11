using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildWorkspaceManager
{
    private readonly ILogger _logger;

    public BuildWorkspaceManager(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string CreateWorkspace(string workingDirectory, string executionId)
    {
        string workDir = Path.Combine(workingDirectory, executionId);
        Directory.CreateDirectory(workDir);
        return workDir;
    }

    public void CleanupWorkspace(string? workDir)
    {
        if (workDir is null || !Directory.Exists(workDir))
        {
            return;
        }

        try
        {
            Directory.Delete(workDir, true);
            _logger.LogDebug("Cleaned up workspace {WorkDir}", workDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up workspace {WorkDir}", workDir);
        }
    }
}
