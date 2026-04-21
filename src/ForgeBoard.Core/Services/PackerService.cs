using System.Collections.Concurrent;
using CliWrap;
using CliWrap.Buffered;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services;

public sealed class PackerService : IPackerService
{
    private readonly ILogger<PackerService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningBuilds =
        new ConcurrentDictionary<string, CancellationTokenSource>();

    public PackerService(ILogger<PackerService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<bool> ValidateInstallationAsync(
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            string version = await GetVersionAsync(config, cancellationToken);
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Packer installation validation failed");
            return false;
        }
    }

    public async Task<string> GetVersionAsync(
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(config);

        string packerPath = config.PackerPath ?? "packer";

        BufferedCommandResult result = await Cli.Wrap(packerPath)
            .WithArguments("version")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        string output = result.StandardOutput.Trim();

        if (output.StartsWith("Packer v", StringComparison.OrdinalIgnoreCase))
        {
            return output.Substring("Packer v".Length).Trim().Split("\n")[0];
        }

        return output;
    }

    public async Task<int> RunBuildAsync(
        string templatePath,
        PackerRunnerConfig config,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(templatePath);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(onOutput);
        ArgumentNullException.ThrowIfNull(onError);

        string packerPath = config.PackerPath ?? "packer";
        string workingDir = config.WorkingDirectory ?? Path.GetDirectoryName(templatePath) ?? ".";

        CommandResult result = await Cli.Wrap(packerPath)
            .WithArguments(new[] { "build", "-color=false", templatePath })
            .WithWorkingDirectory(workingDir)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(onOutput))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(onError))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken);

        return result.ExitCode;
    }

    public async Task<int> RunBuildAsync(
        string executionId,
        string templatePath,
        PackerRunnerConfig config,
        Dictionary<string, string>? extraEnvironment,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(templatePath);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(onOutput);
        ArgumentNullException.ThrowIfNull(onError);

        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        _runningBuilds[executionId] = linkedCts;

        try
        {
            string packerPath = config.PackerPath ?? "packer";
            string workingDir =
                config.WorkingDirectory ?? Path.GetDirectoryName(templatePath) ?? ".";

            _logger.LogInformation(
                "Starting packer build for execution {ExecutionId}",
                executionId
            );

            CommandResult result = await Cli.Wrap(packerPath)
                .WithArguments(new[] { "build", "-color=false", templatePath })
                .WithWorkingDirectory(workingDir)
                .WithEnvironmentVariables(env =>
                {
                    env.Set("PACKER_POWERSHELL_ARGS", "-NoProfile -NonInteractive");

                    string adkPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        @"Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg"
                    );
                    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    if (
                        Directory.Exists(adkPath)
                        && !currentPath.Contains(adkPath, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        env.Set("PATH", $"{adkPath};{currentPath}");
                    }

                    if (extraEnvironment is not null)
                    {
                        foreach (KeyValuePair<string, string> kvp in extraEnvironment)
                        {
                            env.Set(kvp.Key, kvp.Value);
                        }
                    }
                })
                .WithStandardOutputPipe(PipeTarget.ToDelegate(onOutput))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(onError))
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(linkedCts.Token);

            return result.ExitCode;
        }
        finally
        {
            _runningBuilds.TryRemove(executionId, out CancellationTokenSource? _);
            linkedCts.Dispose();
        }
    }

    public Task CancelBuildAsync(string executionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionId);

        if (_runningBuilds.TryGetValue(executionId, out CancellationTokenSource? cts))
        {
            _logger.LogInformation(
                "Cancelling packer build for execution {ExecutionId}",
                executionId
            );
            cts.Cancel();
        }
        else
        {
            _logger.LogWarning("No running build found for execution {ExecutionId}", executionId);
        }

        return Task.CompletedTask;
    }

    public async Task<(bool Success, string Output)> InitTemplateAsync(
        string templatePath,
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(templatePath);
        ArgumentNullException.ThrowIfNull(config);

        string packerPath = config.PackerPath ?? "packer";
        string workingDir = config.WorkingDirectory ?? Path.GetDirectoryName(templatePath) ?? ".";

        BufferedCommandResult result = await Cli.Wrap(packerPath)
            .WithArguments(new[] { "init", templatePath })
            .WithWorkingDirectory(workingDir)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        string output = $"{result.StandardOutput.Trim()}\n{result.StandardError.Trim()}".Trim();
        return (result.ExitCode == 0, output);
    }

    public async Task<(bool Success, string Output)> ValidateTemplateAsync(
        string templatePath,
        PackerRunnerConfig config,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(templatePath);
        ArgumentNullException.ThrowIfNull(config);

        string packerPath = config.PackerPath ?? "packer";
        string workingDir = config.WorkingDirectory ?? Path.GetDirectoryName(templatePath) ?? ".";

        BufferedCommandResult result = await Cli.Wrap(packerPath)
            .WithArguments(new[] { "validate", templatePath })
            .WithWorkingDirectory(workingDir)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode == 0)
        {
            return (true, result.StandardOutput.Trim());
        }

        string errorOutput = result.StandardError.Trim();
        string standardOutput = result.StandardOutput.Trim();
        string combinedOutput = string.IsNullOrEmpty(errorOutput) ? standardOutput : errorOutput;
        if (!string.IsNullOrEmpty(standardOutput) && !string.IsNullOrEmpty(errorOutput))
        {
            combinedOutput = $"{errorOutput}\n{standardOutput}";
        }

        return (false, combinedOutput);
    }
}
