using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface IPackerService
{
    Task<bool> ValidateInstallationAsync(
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    );

    Task<string> GetVersionAsync(
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    );

    Task<(bool Success, string Output)> InitTemplateAsync(
        string templatePath,
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    );

    Task<(bool Success, string Output)> ValidateTemplateAsync(
        string templatePath,
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    );

    Task<int> RunBuildAsync(
        string templatePath,
        PackerRunnerConfig config,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default
    );

    Task<int> RunBuildAsync(
        string executionId,
        string templatePath,
        PackerRunnerConfig config,
        Dictionary<string, string>? extraEnvironment,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default
    );

    Task CancelBuildAsync(string executionId, CancellationToken cancellationToken = default);
}
