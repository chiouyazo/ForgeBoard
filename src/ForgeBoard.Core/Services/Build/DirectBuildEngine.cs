using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;
using ContractLogLevel = ForgeBoard.Contracts.Models.LogLevel;

namespace ForgeBoard.Core.Services.Build;

public sealed class DirectBuildEngine
{
    private readonly ForgeBoardDatabase _db;
    private readonly ILogger<DirectBuildEngine> _logger;

    private const string TempScriptPath = @"C:\Windows\Temp\forgeboard-script.ps1";
    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMinutes(5);

    public DirectBuildEngine(ForgeBoardDatabase db, ILogger<DirectBuildEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        _db = db;
        _logger = logger;
    }

    private string SessionUser
    {
        get
        {
            AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            return settings?.WinrmUsername ?? "Administrator";
        }
    }

    private string SessionPassword
    {
        get
        {
            AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            return settings?.WinrmPassword ?? "Admin123!";
        }
    }

    public async Task<BuildEngineResult> ExecuteAsync(
        string executionId,
        List<BuildStep> steps,
        string baseVhdxPath,
        string outputDirectory,
        int memoryMb,
        int cpuCount,
        Action<string, ContractLogLevel, string> addLog,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(baseVhdxPath);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(addLog);

        string vmName = $"forgeboard-{executionId[..8]}";
        string vmDir = Path.Combine(outputDirectory, vmName);
        string diffVhdPath = Path.Combine(vmDir, $"{vmName}.vhdx");
        string outputPath = Path.Combine(outputDirectory, "disk.vhdx");

        try
        {
            Directory.CreateDirectory(vmDir);

            addLog(
                executionId,
                ContractLogLevel.Info,
                $"Creating differencing disk from {baseVhdxPath}"
            );
            (int exitCode, string output, string error) = await PowerShellRunner.RunAsync(
                $"New-VHD -Path '{diffVhdPath}' -ParentPath '{baseVhdxPath}' -Differencing",
                cancellationToken
            );

            if (exitCode != 0)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to create differencing disk: {error}",
                };
            }

            addLog(executionId, ContractLogLevel.Info, $"Creating Gen2 VM '{vmName}'");
            long memoryBytes = (long)memoryMb * 1024 * 1024;
            string createVmScript =
                $"$vm = New-VM -Name '{vmName}' -Generation 2 -VHDPath '{diffVhdPath}' -MemoryStartupBytes {memoryBytes} -Path '{vmDir}'\n"
                + $"Set-VMProcessor -VMName '{vmName}' -Count {cpuCount}\n"
                + $"Set-VMMemory -VMName '{vmName}' -DynamicMemoryEnabled $false\n"
                + $"Set-VMFirmware -VMName '{vmName}' -EnableSecureBoot Off\n"
                + $"Set-VMKeyProtector -VMName '{vmName}' -NewLocalKeyProtector\n"
                + $"Enable-VMTPM -VMName '{vmName}'\n"
                + $"$sw = Get-VMSwitch -Name 'Default Switch' -ErrorAction SilentlyContinue\n"
                + $"if ($sw) {{ Add-VMNetworkAdapter -VMName '{vmName}' -SwitchName $sw.Name }}\n"
                + $"Start-VM -Name '{vmName}'";

            (exitCode, output, error) = await PowerShellRunner.RunAsync(
                createVmScript,
                cancellationToken
            );
            if (exitCode != 0)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to create VM: {error}",
                };
            }

            addLog(executionId, ContractLogLevel.Info, "Waiting for VM IP address...");
            string? vmIp = await ResolveVmIpAsync(vmName, addLog, executionId, cancellationToken);
            if (vmIp is null)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ErrorMessage = "Failed to get VM IP address within timeout",
                };
            }
            addLog(executionId, ContractLogLevel.Info, $"VM IP: {vmIp}");

            addLog(executionId, ContractLogLevel.Info, "Waiting for WinRM connectivity...");
            bool connected = await WaitForSessionAsync(
                vmIp,
                addLog,
                executionId,
                cancellationToken
            );
            if (!connected)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ErrorMessage = "Failed to establish WinRM session within timeout",
                };
            }

            foreach (BuildStep step in steps.OrderBy(s => s.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();

                addLog(executionId, ContractLogLevel.Info, $"[Step {step.Order}] {step.Name}");

                using CancellationTokenSource stepCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stepCts.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds));
                CancellationToken stepToken = stepCts.Token;

                Action<string> logLine = line =>
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        addLog(executionId, ContractLogLevel.Info, $"  {line}");
                    }
                };

                BuildEngineResult? stepResult = step.StepType switch
                {
                    BuildStepType.PowerShell => await ExecutePowerShellAsync(
                        vmIp,
                        step,
                        logLine,
                        stepToken
                    ),
                    BuildStepType.PowerShellFile => await ExecutePowerShellFileAsync(
                        vmIp,
                        step,
                        logLine,
                        stepToken
                    ),
                    BuildStepType.FileUpload => await ExecuteFileUploadAsync(
                        vmIp,
                        step,
                        logLine,
                        stepToken
                    ),
                    BuildStepType.WindowsRestart => await ExecuteWindowsRestartAsync(
                        vmName,
                        vmIp,
                        addLog,
                        executionId,
                        stepToken
                    ),
                    BuildStepType.Shell => await ExecuteShellAsync(vmIp, step, logLine, stepToken),
                    BuildStepType.ShellFile => await ExecuteShellFileAsync(
                        vmIp,
                        step,
                        logLine,
                        stepToken
                    ),
                    BuildStepType.Custom => await ExecutePowerShellAsync(
                        vmIp,
                        step,
                        logLine,
                        stepToken
                    ),
                    _ => new BuildEngineResult
                    {
                        Success = false,
                        ErrorMessage = $"Unknown step type: {step.StepType}",
                    },
                };

                if (stepResult is not null)
                {
                    addLog(
                        executionId,
                        ContractLogLevel.Error,
                        $"Step {step.Order} failed: {stepResult.ErrorMessage}"
                    );
                    return stepResult;
                }

                addLog(executionId, ContractLogLevel.Info, $"[Step {step.Order}] Completed");
            }

            addLog(executionId, ContractLogLevel.Info, "Shutting down VM...");
            await PowerShellRunner.RunWithSessionAsync(
                vmIp,
                SessionUser,
                SessionPassword,
                "shutdown /s /t 5 /f",
                cancellationToken
            );

            bool shutdown = await WaitForVmStateAsync(
                vmName,
                "Off",
                ShutdownTimeout,
                cancellationToken
            );
            if (!shutdown)
            {
                addLog(
                    executionId,
                    ContractLogLevel.Warning,
                    "VM did not shut down gracefully, forcing stop"
                );
                await PowerShellRunner.RunAsync(
                    $"Stop-VM -Name '{vmName}' -Force -TurnOff",
                    cancellationToken
                );
            }

            addLog(executionId, ContractLogLevel.Info, "Removing VM definition...");
            await PowerShellRunner.RunAsync(
                $"Remove-VM -Name '{vmName}' -Force",
                cancellationToken
            );

            addLog(
                executionId,
                ContractLogLevel.Info,
                "Converting differencing disk to standalone VHDX..."
            );
            (exitCode, output, error) = await PowerShellRunner.RunAsync(
                $"Convert-VHD -Path '{diffVhdPath}' -DestinationPath '{outputPath}' -VHDType Dynamic",
                cancellationToken
            );

            if (exitCode != 0)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to convert VHD: {error}",
                };
            }

            CleanupDirectory(vmDir);

            addLog(executionId, ContractLogLevel.Info, "Build completed successfully");
            return new BuildEngineResult { Success = true, OutputVhdxPath = outputPath };
        }
        catch (OperationCanceledException)
        {
            addLog(executionId, ContractLogLevel.Warning, "Build was cancelled");
            return new BuildEngineResult { Success = false, ErrorMessage = "Build was cancelled" };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DirectBuildEngine failed for execution {ExecutionId}",
                executionId
            );
            addLog(executionId, ContractLogLevel.Error, $"Build failed: {ex.Message}");
            return new BuildEngineResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            await CleanupVmAsync(vmName);
            CleanupDirectory(vmDir);
        }
    }

    private async Task<string?> ResolveVmIpAsync(
        string vmName,
        Action<string, ContractLogLevel, string> addLog,
        string executionId,
        CancellationToken cancellationToken
    )
    {
        DateTime deadline = DateTime.UtcNow + SessionTimeout;
        int attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            (int exitCode, string output, string error) = await PowerShellRunner.RunAsync(
                $"(Get-VMNetworkAdapter -VMName '{vmName}').IPAddresses | Where-Object {{ $_ -match '^\\d+\\.\\d+\\.\\d+\\.\\d+$' -and $_ -notmatch '^169\\.254\\.' }} | Select-Object -First 1",
                cancellationToken
            );

            string ip = output.Trim();
            if (
                exitCode == 0
                && !string.IsNullOrEmpty(ip)
                && ip.Contains('.')
                && !ip.StartsWith("169.254.")
            )
            {
                return ip;
            }

            if (attempt % 6 == 0)
            {
                addLog(
                    executionId,
                    ContractLogLevel.Info,
                    $"Waiting for VM IP... ({attempt * 10}s)"
                );
            }

            await Task.Delay(SessionPollInterval, cancellationToken);
        }

        return null;
    }

    private async Task<bool> WaitForSessionAsync(
        string vmIp,
        Action<string, ContractLogLevel, string> addLog,
        string executionId,
        CancellationToken cancellationToken
    )
    {
        DateTime deadline = DateTime.UtcNow + SessionTimeout;
        int attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                (int exitCode, string output, string error) =
                    await PowerShellRunner.RunWithSessionAsync(
                        vmIp,
                        SessionUser,
                        SessionPassword,
                        "Write-Host 'connected'",
                        cancellationToken
                    );

                if (exitCode == 0 && output.Contains("connected"))
                {
                    addLog(
                        executionId,
                        ContractLogLevel.Info,
                        $"WinRM connected ({vmIp}) on attempt {attempt}"
                    );
                    return true;
                }

                if (attempt <= 3 || attempt % 10 == 0)
                {
                    string errorMsg = !string.IsNullOrEmpty(error) ? error.Trim() : output.Trim();
                    if (errorMsg.Length > 200)
                        errorMsg = errorMsg[..200];
                    addLog(
                        executionId,
                        ContractLogLevel.Info,
                        $"WinRM attempt {attempt} (exit {exitCode}): {errorMsg}"
                    );
                }
            }
            catch (Exception ex)
            {
                if (attempt <= 3)
                {
                    addLog(
                        executionId,
                        ContractLogLevel.Info,
                        $"WinRM attempt {attempt} exception: {ex.Message}"
                    );
                }
            }

            await Task.Delay(SessionPollInterval, cancellationToken);
        }

        return false;
    }

    private async Task<bool> WaitForVmStateAsync(
        string vmName,
        string targetState,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int exitCode, string output, string _) = await PowerShellRunner.RunAsync(
                $"(Get-VM -Name '{vmName}').State",
                cancellationToken
            );

            if (
                exitCode == 0
                && output.Trim().Equals(targetState, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        return false;
    }

    private async Task<BuildEngineResult?> ExecutePowerShellAsync(
        string host,
        BuildStep step,
        Action<string> logLine,
        CancellationToken cancellationToken
    )
    {
        (int exitCode, string output, string error) = await PowerShellRunner.RunWithSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            step.Content,
            cancellationToken,
            logLine
        );

        if (exitCode != 0)
        {
            foreach (string line in error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                logLine(line.TrimEnd('\r'));
            }
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"PowerShell step failed (exit {exitCode}): {error.Trim()}",
            };
        }

        return null;
    }

    private async Task<BuildEngineResult?> ExecutePowerShellFileAsync(
        string host,
        BuildStep step,
        Action<string> logLine,
        CancellationToken cancellationToken
    )
    {
        string hostPath = step.Content.Split('\n')[0].Trim();

        (int exitCode, string output, string error) = await PowerShellRunner.CopyToSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            hostPath,
            TempScriptPath,
            cancellationToken
        );

        if (exitCode != 0)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"Failed to copy script to VM: {error}",
            };
        }

        (exitCode, output, error) = await PowerShellRunner.RunWithSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            $"& '{TempScriptPath}'",
            cancellationToken,
            logLine
        );

        if (exitCode != 0)
        {
            foreach (string line in error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                logLine(line.TrimEnd('\r'));
            }
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"PowerShellFile step failed (exit {exitCode}): {error.Trim()}",
            };
        }

        return null;
    }

    private async Task<BuildEngineResult?> ExecuteFileUploadAsync(
        string host,
        BuildStep step,
        Action<string> logLine,
        CancellationToken cancellationToken
    )
    {
        string[] lines = step.Content.Split('\n');
        string source = lines[0].Trim();
        string destination =
            lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])
                ? lines[1].Trim()
                : $@"C:\{Path.GetFileName(source)}";

        logLine($"{source} -> {destination}");

        (int exitCode, string _, string error) = await PowerShellRunner.CopyToSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            source,
            destination,
            cancellationToken
        );

        if (exitCode != 0)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"FileUpload step failed: {error}",
            };
        }

        return null;
    }

    private async Task<BuildEngineResult?> ExecuteWindowsRestartAsync(
        string vmName,
        string vmIp,
        Action<string, ContractLogLevel, string> addLog,
        string executionId,
        CancellationToken cancellationToken
    )
    {
        await PowerShellRunner.RunWithSessionAsync(
            vmIp,
            SessionUser,
            SessionPassword,
            "Restart-Computer -Force",
            cancellationToken
        );

        addLog(executionId, ContractLogLevel.Info, "Waiting for VM to restart...");
        bool reachedOff = await WaitForVmStateAsync(
            vmName,
            "Off",
            SessionTimeout,
            cancellationToken
        );
        if (!reachedOff)
        {
            _logger.LogDebug("VM did not reach Off state, may have rebooted quickly");
        }

        bool reachedRunning = await WaitForVmStateAsync(
            vmName,
            "Running",
            SessionTimeout,
            cancellationToken
        );
        if (!reachedRunning)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = "VM did not return to Running state after restart",
            };
        }

        addLog(executionId, ContractLogLevel.Info, "Waiting for WinRM after restart...");
        bool reconnected = await WaitForSessionAsync(vmIp, addLog, executionId, cancellationToken);
        if (!reconnected)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = "Failed to re-establish PSSession after restart",
            };
        }

        return null;
    }

    private async Task<BuildEngineResult?> ExecuteShellAsync(
        string host,
        BuildStep step,
        Action<string> logLine,
        CancellationToken cancellationToken
    )
    {
        string command = "cmd /c \"" + step.Content + "\"";

        (int exitCode, string output, string error) = await PowerShellRunner.RunWithSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            command,
            cancellationToken,
            logLine
        );

        if (exitCode != 0)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"Shell step failed (exit {exitCode}): {error}{output}",
            };
        }

        return null;
    }

    private async Task<BuildEngineResult?> ExecuteShellFileAsync(
        string host,
        BuildStep step,
        Action<string> logLine,
        CancellationToken cancellationToken
    )
    {
        string hostPath = step.Content.Split('\n')[0].Trim();
        string remotePath = @"C:\Windows\Temp\forgeboard-script.cmd";

        (int exitCode, string _, string error) = await PowerShellRunner.CopyToSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            hostPath,
            remotePath,
            cancellationToken
        );

        if (exitCode != 0)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"Failed to copy shell script to VM: {error}",
            };
        }

        (exitCode, string output, error) = await PowerShellRunner.RunWithSessionAsync(
            host,
            SessionUser,
            SessionPassword,
            $"cmd /c \"{remotePath}\"",
            cancellationToken,
            logLine
        );

        if (exitCode != 0)
        {
            return new BuildEngineResult
            {
                Success = false,
                ErrorMessage = $"ShellFile step failed (exit {exitCode}): {error}{output}",
            };
        }

        return null;
    }

    private async Task CleanupVmAsync(string vmName)
    {
        try
        {
            await PowerShellRunner.RunAsync(
                $"Stop-VM -Name '{vmName}' -Force -TurnOff -ErrorAction SilentlyContinue",
                CancellationToken.None
            );
            await PowerShellRunner.RunAsync(
                $"Remove-VM -Name '{vmName}' -Force -ErrorAction SilentlyContinue",
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up VM {VmName}", vmName);
        }
    }

    private void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up directory {Path}", path);
        }
    }
}
