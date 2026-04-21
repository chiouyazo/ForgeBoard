using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Build;

public sealed class PackerBuildEngine
{
    private readonly PackerService _packerService;
    private readonly IPackerTemplateGenerator _templateGenerator;
    private readonly BuildFileServer _fileServer;
    private readonly ILogger<PackerBuildEngine> _logger;

    public PackerBuildEngine(
        PackerService packerService,
        IPackerTemplateGenerator templateGenerator,
        BuildFileServer fileServer,
        ILogger<PackerBuildEngine> logger
    )
    {
        ArgumentNullException.ThrowIfNull(packerService);
        ArgumentNullException.ThrowIfNull(templateGenerator);
        ArgumentNullException.ThrowIfNull(fileServer);
        ArgumentNullException.ThrowIfNull(logger);

        _packerService = packerService;
        _templateGenerator = templateGenerator;
        _fileServer = fileServer;
        _logger = logger;
    }

    public async Task<BuildEngineResult> ExecuteAsync(
        string executionId,
        BuildDefinition definition,
        List<BuildStep> steps,
        BaseImage baseImage,
        string workingDirectory,
        string outputDirectory,
        PackerRunnerConfig runnerConfig,
        Action<string, Contracts.Models.LogLevel, string> addLog,
        Action<string, BuildStatus> notifyStatusChanged,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(runnerConfig);
        ArgumentNullException.ThrowIfNull(addLog);
        ArgumentNullException.ThrowIfNull(notifyStatusChanged);

        string? tempVmName = null;

        try
        {
            string resolvedImagePath = baseImage.LocalCachePath ?? baseImage.FileName;
            string resolvedExt = Path.GetExtension(resolvedImagePath).ToLowerInvariant();

            if (definition.Builder == PackerBuilder.HyperV && resolvedExt is ".vhdx" or ".vhd")
            {
                tempVmName = $"forgeboard-temp-{executionId[..8]}";
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    $"Creating temporary VM '{tempVmName}' with differencing disk..."
                );

                await RunPowerShellAsync(
                    "Get-VM | Where-Object { $_.Name -like 'forgeboard-temp-*' } | ForEach-Object { "
                        + "Stop-VM -Name $_.Name -Force -TurnOff -ErrorAction SilentlyContinue; "
                        + "Remove-VM -Name $_.Name -Force -ErrorAction SilentlyContinue }",
                    cancellationToken
                );

                string vmDir = Path.Combine(workingDirectory, tempVmName);
                Directory.CreateDirectory(vmDir);
                string diffVhdPath = Path.Combine(vmDir, $"{tempVmName}.vhdx");

                string createScript =
                    $"New-VHD -Path '{diffVhdPath}' -ParentPath '{resolvedImagePath}' -Differencing | Out-Null; "
                    + $"$vm = New-VM -Name '{tempVmName}' -Generation 2 -VHDPath '{diffVhdPath}' "
                    + $"-MemoryStartupBytes {definition.MemoryMb * 1024L * 1024L} -Path '{vmDir}'; "
                    + $"Set-VMProcessor -VMName '{tempVmName}' -Count {definition.CpuCount}; "
                    + $"Set-VMMemory -VMName '{tempVmName}' -DynamicMemoryEnabled $false; "
                    + $"Set-VMFirmware -VMName '{tempVmName}' -EnableSecureBoot Off; "
                    + "$sw = Get-VMSwitch -Name 'Default Switch' -ErrorAction SilentlyContinue; "
                    + $"if ($sw) {{ Add-VMNetworkAdapter -VMName '{tempVmName}' -SwitchName $sw.Name }}";

                (int exitCodeVm, string errorVm) = await RunPowerShellAsync(
                    createScript,
                    cancellationToken
                );

                if (exitCodeVm != 0)
                {
                    throw new InvalidOperationException($"Failed to create temp VM: {errorVm}");
                }

                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    $"Temporary VM '{tempVmName}' created with differencing disk"
                );

                baseImage.LocalCachePath = tempVmName;
            }

            BuildDefinition packerDefinition = new BuildDefinition
            {
                Id = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                BaseImageId = definition.BaseImageId,
                Builder = definition.Builder,
                PackerRunnerConfigId = definition.PackerRunnerConfigId,
                MemoryMb = definition.MemoryMb,
                CpuCount = definition.CpuCount,
                DiskSizeMb = definition.DiskSizeMb,
                OutputFormat = definition.OutputFormat,
                Version = definition.Version,
                UnattendPath = definition.UnattendPath,
                Steps = steps,
                Tags = definition.Tags,
                PostProcessors = definition.PostProcessors,
                CreatedAt = definition.CreatedAt,
                ModifiedAt = definition.ModifiedAt,
            };

            if (
                !string.IsNullOrEmpty(definition.UnattendPath)
                && definition.Builder == PackerBuilder.HyperV
                && IsIsoBaseImage(baseImage)
            )
            {
                string oscdimgExe = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe"
                );

                if (!File.Exists(oscdimgExe))
                {
                    addLog(
                        executionId,
                        Contracts.Models.LogLevel.Error,
                        "oscdimg.exe not found. Required to prepare the Windows ISO with Autounattend."
                    );
                    addLog(
                        executionId,
                        Contracts.Models.LogLevel.Error,
                        "Install Windows ADK Deployment Tools:"
                    );
                    addLog(
                        executionId,
                        Contracts.Models.LogLevel.Error,
                        "  1. Download from https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install"
                    );
                    addLog(
                        executionId,
                        Contracts.Models.LogLevel.Error,
                        "  2. Run: adksetup.exe /quiet /features OptionId.DeploymentTools"
                    );
                    addLog(
                        executionId,
                        Contracts.Models.LogLevel.Error,
                        "  3. Restart ForgeBoard and retry the build."
                    );
                    throw new InvalidOperationException(
                        "Windows ADK Deployment Tools not installed."
                    );
                }

                string isoPath = baseImage.LocalCachePath ?? baseImage.FileName;
                string preparedIso = Path.Combine(workingDirectory, "prepared.iso");

                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    "Preparing Windows ISO with Autounattend.xml..."
                );

                string prepareScript =
                    "$ErrorActionPreference = 'Stop'; "
                    + $"$mount = Mount-DiskImage -ImagePath '{isoPath}' -PassThru; "
                    + "$driveLetter = ($mount | Get-Volume).DriveLetter; "
                    + $"$tempDir = '{Path.Combine(workingDirectory, "iso_contents")}'; "
                    + "Copy-Item -Path \"${driveLetter}:\\\" -Destination $tempDir -Recurse -Force; "
                    + $"Dismount-DiskImage -ImagePath '{isoPath}' | Out-Null; "
                    + $"Copy-Item -Path '{definition.UnattendPath}' -Destination (Join-Path $tempDir 'Autounattend.xml') -Force; "
                    + "$efiBoot = Get-ChildItem (Join-Path $tempDir 'efi\\microsoft\\boot\\efisys_noprompt.bin') -ErrorAction SilentlyContinue; "
                    + "if (-not $efiBoot) { $efiBoot = Get-ChildItem (Join-Path $tempDir 'efi\\microsoft\\boot\\efisys.bin') -ErrorAction SilentlyContinue }; "
                    + "if (-not $efiBoot) { throw 'Could not find efisys boot file in ISO' }; "
                    + "$etfsBoot = Join-Path $tempDir 'boot\\etfsboot.com'; "
                    + "$bootData = \"2#p0,e,b$etfsBoot#pEF,e,b$($efiBoot.FullName)\"; "
                    + $"& '{oscdimgExe}' -m -o -u2 -udfver102 \"-bootdata:$bootData\" $tempDir '{preparedIso}'; "
                    + "if ($LASTEXITCODE -ne 0) { throw \"oscdimg failed with exit code $LASTEXITCODE\" }; "
                    + "Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue";

                (int prepExit, string prepError) = await RunPowerShellAsync(
                    prepareScript,
                    cancellationToken
                );
                if (prepExit != 0)
                {
                    throw new InvalidOperationException($"Failed to prepare ISO: {prepError}");
                }

                addLog(executionId, Contracts.Models.LogLevel.Info, "Prepared ISO created");
                baseImage.LocalCachePath = preparedIso;
            }

            addLog(executionId, Contracts.Models.LogLevel.Info, "Generating Packer template...");
            string hcl = _templateGenerator.GenerateHcl(
                packerDefinition,
                baseImage,
                outputDirectory
            );
            string templatePath = Path.Combine(workingDirectory, "template.pkr.hcl");
            await File.WriteAllTextAsync(templatePath, hcl, cancellationToken);

            addLog(executionId, Contracts.Models.LogLevel.Info, "Initializing Packer plugins...");
            (bool initSuccess, string initOutput) = await _packerService.InitTemplateAsync(
                templatePath,
                runnerConfig,
                cancellationToken
            );

            if (!initSuccess)
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Warning,
                    $"Packer init warning: {initOutput}"
                );
            }

            addLog(executionId, Contracts.Models.LogLevel.Info, "Validating Packer template...");
            (bool validationSuccess, string validationOutput) =
                await _packerService.ValidateTemplateAsync(
                    templatePath,
                    runnerConfig,
                    cancellationToken
                );

            if (!validationSuccess)
            {
                throw new InvalidOperationException(
                    $"Template validation failed: {validationOutput}"
                );
            }

            addLog(executionId, Contracts.Models.LogLevel.Info, "Template validated successfully");

            Directory.CreateDirectory(Path.GetDirectoryName(outputDirectory)!);
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    "Cleaned up stale output directory from previous run"
                );
            }

            string? fileServerPort = _fileServer.Start(executionId, packerDefinition);

            notifyStatusChanged(executionId, BuildStatus.Running);
            addLog(executionId, Contracts.Models.LogLevel.Info, "Starting Packer build...");

            Dictionary<string, string> extraEnv = new Dictionary<string, string>();
            if (fileServerPort is not null)
            {
                extraEnv["FORGEBOARD_FILE_PORT"] = fileServerPort;
            }

            int exitCode = await _packerService.RunBuildAsync(
                executionId,
                templatePath,
                runnerConfig,
                extraEnv,
                line => addLog(executionId, Contracts.Models.LogLevel.Info, line),
                line => addLog(executionId, Contracts.Models.LogLevel.Error, line),
                cancellationToken
            );

            if (exitCode != 0)
            {
                return new BuildEngineResult
                {
                    Success = false,
                    ErrorMessage = $"Packer exited with code {exitCode}",
                };
            }

            string? outputVhdxPath = FindOutputVhdx(outputDirectory);

            return new BuildEngineResult { Success = true, OutputVhdxPath = outputVhdxPath };
        }
        finally
        {
            if (tempVmName is not null)
            {
                await PowerShellRunner.RunFireAndForgetAsync(
                    $"Remove-VM -Name '{tempVmName}' -Force -ErrorAction SilentlyContinue"
                );
                _logger.LogInformation("Cleaned up temporary VM {VmName}", tempVmName);
            }

            _fileServer.Stop(executionId);
        }
    }

    private static string? FindOutputVhdx(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        string[] vhdxFiles = Directory.GetFiles(
            outputDirectory,
            "*.vhdx",
            SearchOption.AllDirectories
        );
        if (vhdxFiles.Length > 0)
        {
            return vhdxFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        string[] vhdFiles = Directory.GetFiles(
            outputDirectory,
            "*.vhd",
            SearchOption.AllDirectories
        );
        if (vhdFiles.Length > 0)
        {
            return vhdFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        string[] allFiles = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories);
        if (allFiles.Length > 0)
        {
            return allFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        return null;
    }

    private static async Task<(int ExitCode, string Error)> RunPowerShellAsync(
        string script,
        CancellationToken cancellationToken
    )
    {
        (int exitCode, string _, string error) = await PowerShellRunner.RunAsync(
            script,
            cancellationToken
        );
        return (exitCode, error);
    }

    private static bool IsIsoBaseImage(BaseImage baseImage)
    {
        string imagePath = baseImage.LocalCachePath ?? baseImage.FileName;
        return Path.GetExtension(imagePath).Equals(".iso", StringComparison.OrdinalIgnoreCase);
    }
}
