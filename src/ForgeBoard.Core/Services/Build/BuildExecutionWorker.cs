using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using Microsoft.Extensions.Logging;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildExecutionWorker
{
    private readonly ForgeBoardDatabase _db;
    private readonly BuildPhaseExecutor _phaseExecutor;
    private readonly ICacheService _cacheService;
    private readonly IAppPaths _appPaths;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IPostProcessor> _postProcessors;
    private readonly BuildChainResolver _chainResolver;
    private readonly BuildWorkspaceManager _workspaceManager;

    public BuildExecutionWorker(
        ForgeBoardDatabase db,
        BuildPhaseExecutor phaseExecutor,
        ICacheService cacheService,
        IAppPaths appPaths,
        Dictionary<string, IPostProcessor> postProcessors,
        BuildChainResolver chainResolver,
        BuildWorkspaceManager workspaceManager,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(phaseExecutor);
        ArgumentNullException.ThrowIfNull(cacheService);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(postProcessors);
        ArgumentNullException.ThrowIfNull(chainResolver);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _phaseExecutor = phaseExecutor;
        _cacheService = cacheService;
        _appPaths = appPaths;
        _postProcessors = postProcessors;
        _chainResolver = chainResolver;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task ExecuteBuildAsync(
        string executionId,
        Func<string, CancellationToken, Task<BuildExecution>> startBuildFunc,
        Action<string, Contracts.Models.LogLevel, string> addLog,
        Action<string, BuildStatus> notifyStatusChanged,
        CancellationToken cancellationToken
    )
    {
        BuildExecution? execution = _db.BuildExecutions.FindById(executionId);
        if (execution is null)
        {
            _logger.LogWarning("Execution {ExecutionId} not found, skipping", executionId);
            return;
        }

        if (execution.Status == BuildStatus.Cancelled)
        {
            _logger.LogInformation(
                "Execution {ExecutionId} was already cancelled, skipping",
                executionId
            );
            return;
        }

        string? workDir = null;
        string? outputDir = null;
        bool buildSucceeded = false;

        try
        {
            execution.Status = BuildStatus.Preparing;
            execution.StartedAt = DateTimeOffset.UtcNow;
            _db.BuildExecutions.Update(execution);
            notifyStatusChanged(executionId, BuildStatus.Preparing);
            addLog(executionId, Contracts.Models.LogLevel.Info, "Preparing build...");

            BuildDefinition? definition = _db.BuildDefinitions.FindById(
                execution.BuildDefinitionId
            );
            if (definition is null)
            {
                throw new InvalidOperationException(
                    $"Build definition {execution.BuildDefinitionId} not found"
                );
            }

            PackerRunnerConfig runnerConfig = ResolveRunnerConfig(definition);

            workDir = _workspaceManager.CreateWorkspace(_appPaths.WorkingDirectory, executionId);
            execution.WorkingDirectory = workDir;
            _db.BuildExecutions.Update(execution);

            await PrepareBuildAsync(
                executionId,
                definition,
                startBuildFunc,
                addLog,
                cancellationToken
            );

            outputDir = Path.Combine(_appPaths.ArtifactsDirectory, executionId);

            BaseImage baseImage = ResolveBaseImageForTemplate(definition.BaseImageId);

            string baseImagePath = baseImage.LocalCachePath ?? baseImage.FileName;
            if (Path.GetExtension(baseImagePath).Equals(".box", StringComparison.OrdinalIgnoreCase))
            {
                long boxSizeBytes = new FileInfo(baseImagePath).Length;
                string boxSizeDisplay =
                    boxSizeBytes > 1024 * 1024 * 1024
                        ? $"{boxSizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
                        : $"{boxSizeBytes / (1024.0 * 1024.0):F0} MB";

                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    $"Extracting Vagrant box ({boxSizeDisplay}). This may take a while..."
                );

                string extractDir = Path.Combine(workDir, "box_extract");
                Directory.CreateDirectory(extractDir);

                cancellationToken.ThrowIfCancellationRequested();

                (int tarExitCode, string _, string tarError) =
                    await PowerShellRunner.RunCommandAsync(
                        "tar",
                        $"xf \"{baseImagePath}\" -C \"{extractDir}\"",
                        cancellationToken
                    );
                if (tarExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to extract .box file: {tarError}");
                }

                addLog(executionId, Contracts.Models.LogLevel.Info, "Box extracted successfully");

                string? vhdxPath =
                    Directory
                        .GetFiles(extractDir, "*.vhdx", SearchOption.AllDirectories)
                        .FirstOrDefault()
                    ?? Directory
                        .GetFiles(extractDir, "*.vhd", SearchOption.AllDirectories)
                        .FirstOrDefault();

                if (vhdxPath is null)
                {
                    string[] extractedFiles = Directory.GetFiles(
                        extractDir,
                        "*",
                        SearchOption.AllDirectories
                    );
                    string fileList = string.Join(", ", extractedFiles.Select(Path.GetFileName));
                    throw new InvalidOperationException(
                        $"No VHDX/VHD file found inside the Vagrant box. Found: {fileList}"
                    );
                }

                long vhdxSize = new FileInfo(vhdxPath).Length;
                string vhdxSizeDisplay =
                    vhdxSize > 1024 * 1024 * 1024
                        ? $"{vhdxSize / (1024.0 * 1024.0 * 1024.0):F1} GB"
                        : $"{vhdxSize / (1024.0 * 1024.0):F0} MB";

                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    $"Found disk image: {Path.GetFileName(vhdxPath)} ({vhdxSizeDisplay})"
                );
                baseImage.LocalCachePath = vhdxPath;
            }

            Directory.CreateDirectory(_appPaths.ArtifactsDirectory);

            execution.Status = BuildStatus.Running;
            _db.BuildExecutions.Update(execution);
            notifyStatusChanged(executionId, BuildStatus.Running);
            addLog(executionId, Contracts.Models.LogLevel.Info, "Starting build phases...");

            string? finalOutputPath = await _phaseExecutor.ExecuteAsync(
                executionId,
                definition,
                baseImage,
                workDir,
                outputDir,
                runnerConfig,
                addLog,
                notifyStatusChanged,
                cancellationToken
            );

            execution = _db.BuildExecutions.FindById(executionId)!;

            if (execution.Status == BuildStatus.Cancelled)
            {
                addLog(executionId, Contracts.Models.LogLevel.Warning, "Build was cancelled");
                return;
            }

            if (finalOutputPath is not null)
            {
                addLog(executionId, Contracts.Models.LogLevel.Info, "Build completed successfully");

                await RunPostProcessorsAsync(
                    executionId,
                    definition,
                    outputDir,
                    addLog,
                    cancellationToken
                );

                execution.Status = BuildStatus.Succeeded;
                notifyStatusChanged(executionId, BuildStatus.Succeeded);

                FlattenArtifactDirectory(outputDir);
                string flattenedPath = Path.Combine(outputDir, Path.GetFileName(finalOutputPath));
                string resolvedOutputPath = File.Exists(flattenedPath)
                    ? flattenedPath
                    : finalOutputPath;
                string? artifactId = RegisterArtifact(
                    executionId,
                    definition,
                    outputDir,
                    resolvedOutputPath,
                    addLog
                );
                if (artifactId is not null)
                {
                    execution.ArtifactId = artifactId;
                    ApplyChecksumToArtifact(artifactId, outputDir);
                }

                CleanupRawExportFiles(outputDir, addLog, executionId);
                CleanupIntermediateArtifactFiles(executionId, artifactId, outputDir, addLog);
                buildSucceeded = true;
            }
            else
            {
                execution.Status = BuildStatus.Failed;
                execution.ErrorMessage = "Build phases failed";
                notifyStatusChanged(executionId, BuildStatus.Failed);
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Error,
                    "Build failed during phase execution"
                );
            }

            execution.CompletedAt = DateTimeOffset.UtcNow;
            _db.BuildExecutions.Update(execution);
        }
        catch (OperationCanceledException)
        {
            execution = _db.BuildExecutions.FindById(executionId);
            if (execution is not null && execution.Status != BuildStatus.Cancelled)
            {
                execution.Status = BuildStatus.Cancelled;
                execution.CompletedAt = DateTimeOffset.UtcNow;
                _db.BuildExecutions.Update(execution);
                notifyStatusChanged(executionId, BuildStatus.Cancelled);
            }

            addLog(executionId, Contracts.Models.LogLevel.Warning, "Build was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build execution {ExecutionId} failed", executionId);

            execution = _db.BuildExecutions.FindById(executionId);
            if (execution is not null)
            {
                execution.Status = BuildStatus.Failed;
                execution.ErrorMessage = ex.Message;
                execution.CompletedAt = DateTimeOffset.UtcNow;
                _db.BuildExecutions.Update(execution);
                notifyStatusChanged(executionId, BuildStatus.Failed);
            }

            addLog(executionId, Contracts.Models.LogLevel.Error, $"Build failed: {ex.Message}");
        }
        finally
        {
            if (workDir is not null)
            {
                _workspaceManager.CleanupWorkspace(workDir);
            }

            if (!buildSucceeded && outputDir is not null)
            {
                CleanupOutputDirectory(outputDir);
            }
        }
    }

    private async Task PrepareBuildAsync(
        string executionId,
        BuildDefinition definition,
        Func<string, CancellationToken, Task<BuildExecution>> startBuildFunc,
        Action<string, Contracts.Models.LogLevel, string> addLog,
        CancellationToken cancellationToken
    )
    {
        string baseImageId = definition.BaseImageId;

        if (baseImageId.StartsWith(BaseImagePrefixes.BuildChain))
        {
            string chainedDefinitionId = baseImageId[BaseImagePrefixes.BuildChain.Length..];
            BuildDefinition? chainedDefinition = _db.BuildDefinitions.FindById(chainedDefinitionId);
            if (chainedDefinition is null)
            {
                throw new InvalidOperationException(
                    $"Chained build definition {chainedDefinitionId} not found"
                );
            }

            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Base image is a build chain to '{chainedDefinition.Name}' v{chainedDefinition.Version}"
            );

            ImageArtifact? existingArtifact = _db
                .ImageArtifacts.Find(a => a.BuildDefinitionId == chainedDefinitionId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            bool canReuse =
                existingArtifact is not null
                && File.Exists(existingArtifact.FilePath)
                && existingArtifact.Version == chainedDefinition.Version;

            if (canReuse)
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    $"Using existing artifact v{existingArtifact!.Version}: {existingArtifact.FilePath}"
                );
                return;
            }

            if (
                existingArtifact is not null
                && existingArtifact.Version != chainedDefinition.Version
            )
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    $"Existing artifact is v{existingArtifact.Version} but definition is v{chainedDefinition.Version}, rebuilding..."
                );
            }
            else if (existingArtifact is not null && !File.Exists(existingArtifact.FilePath))
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Warning,
                    $"Artifact file missing: {existingArtifact.FilePath}, rebuilding..."
                );
            }
            else
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Info,
                    "No existing artifact found, triggering chained build..."
                );
            }

            BuildExecution? currentExecution = _db.BuildExecutions.FindById(executionId);
            if (currentExecution is not null)
            {
                currentExecution.Status = BuildStatus.WaitingForChain;
                _db.BuildExecutions.Update(currentExecution);
            }

            BuildExecution chainedExecution = await startBuildFunc(
                chainedDefinitionId,
                cancellationToken
            );
            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Waiting for chained build {chainedExecution.Id}..."
            );

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                BuildExecution? chainedStatus = _db.BuildExecutions.FindById(chainedExecution.Id);
                if (chainedStatus is null)
                    throw new InvalidOperationException(
                        $"Chained build execution {chainedExecution.Id} disappeared"
                    );

                if (chainedStatus.Status == BuildStatus.Succeeded)
                {
                    addLog(
                        executionId,
                        Contracts.Models.LogLevel.Info,
                        "Chained build succeeded, continuing..."
                    );

                    currentExecution = _db.BuildExecutions.FindById(executionId);
                    if (currentExecution is not null)
                    {
                        currentExecution.Status = BuildStatus.Preparing;
                        _db.BuildExecutions.Update(currentExecution);
                    }
                    return;
                }

                if (
                    chainedStatus.Status == BuildStatus.Failed
                    || chainedStatus.Status == BuildStatus.Cancelled
                )
                {
                    throw new InvalidOperationException(
                        $"Chained build failed: {chainedStatus.ErrorMessage}"
                    );
                }
            }
        }

        if (baseImageId.StartsWith(BaseImagePrefixes.Artifact))
        {
            string artifactId = baseImageId[BaseImagePrefixes.Artifact.Length..];
            ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
            if (artifact is null || !File.Exists(artifact.FilePath))
                throw new InvalidOperationException(
                    $"Build artifact {artifactId} not found or file missing"
                );
            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Using build artifact: {artifact.FilePath}"
            );
            return;
        }

        BaseImage? baseImage = _db.BaseImages.FindById(baseImageId);
        if (baseImage is null)
        {
            throw new InvalidOperationException($"Base image {baseImageId} not found");
        }

        addLog(executionId, Contracts.Models.LogLevel.Info, "Ensuring base image is cached...");
        await _cacheService.EnsureCachedAsync(baseImage, null, cancellationToken);
    }

    private async Task RunPostProcessorsAsync(
        string executionId,
        BuildDefinition definition,
        string outputDir,
        Action<string, Contracts.Models.LogLevel, string> addLog,
        CancellationToken cancellationToken
    )
    {
        if (definition.PostProcessors.Count == 0)
        {
            return;
        }

        string[] outputFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
        if (outputFiles.Length == 0)
        {
            addLog(
                executionId,
                Contracts.Models.LogLevel.Warning,
                "No output files found for post-processing"
            );
            return;
        }

        string primaryArtifact = outputFiles.OrderByDescending(f => new FileInfo(f).Length).First();

        addLog(
            executionId,
            Contracts.Models.LogLevel.Info,
            $"Running {definition.PostProcessors.Count} post-processor(s)..."
        );

        string currentInput = primaryArtifact;

        foreach (string processorName in definition.PostProcessors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_postProcessors.TryGetValue(processorName, out IPostProcessor? processor))
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Warning,
                    $"Post-processor '{processorName}' not found, skipping"
                );
                continue;
            }

            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Running post-processor: {processor.Name}"
            );

            string extension = Path.GetExtension(currentInput);
            if (
                processorName.Equals("ConvertVhd", StringComparison.OrdinalIgnoreCase)
                && extension is not ".vhdx" and not ".vhd"
            )
            {
                extension = ".vhdx";
            }
            string outputPath = Path.Combine(
                outputDir,
                $"{Path.GetFileNameWithoutExtension(currentInput)}-{processorName.ToLowerInvariant()}{extension}"
            );

            try
            {
                await processor.ProcessAsync(
                    currentInput,
                    outputPath,
                    message =>
                        addLog(
                            executionId,
                            Contracts.Models.LogLevel.Info,
                            $"[{processor.Name}] {message}"
                        ),
                    cancellationToken
                );

                if (File.Exists(outputPath))
                {
                    currentInput = outputPath;
                }
            }
            catch (Exception ex)
            {
                addLog(
                    executionId,
                    Contracts.Models.LogLevel.Error,
                    $"Post-processor '{processor.Name}' failed: {ex.Message}"
                );
                _logger.LogError(
                    ex,
                    "Post-processor {Processor} failed for execution {ExecutionId}",
                    processor.Name,
                    executionId
                );
            }
        }
    }

    private string? RegisterArtifact(
        string executionId,
        BuildDefinition definition,
        string outputDir,
        string outputFilePath,
        Action<string, Contracts.Models.LogLevel, string> addLog
    )
    {
        if (!File.Exists(outputFilePath))
        {
            _logger.LogWarning("Build output file {Path} does not exist", outputFilePath);
            return null;
        }

        FileInfo artifactInfo = new FileInfo(outputFilePath);
        string primaryArtifact = outputFilePath;

        ImageArtifact artifact = new ImageArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            BuildExecutionId = executionId,
            BuildDefinitionId = definition.Id,
            Name = $"{definition.Name} - {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}",
            FilePath = primaryArtifact,
            FileSizeBytes = artifactInfo.Length,
            Format = artifactInfo.Extension.TrimStart('.'),
            Version = definition.Version,
            CreatedAt = DateTimeOffset.UtcNow,
            Tags = new List<string>(definition.Tags),
        };

        _db.ImageArtifacts.Insert(artifact);
        addLog(
            executionId,
            Contracts.Models.LogLevel.Info,
            $"Registered artifact: {artifact.Name} ({artifact.FileSizeBytes} bytes)"
        );

        _logger.LogInformation(
            "Registered artifact {ArtifactId} at {Path}",
            artifact.Id,
            primaryArtifact
        );
        return artifact.Id;
    }

    private BaseImage ResolveBaseImageForTemplate(string baseImageId)
    {
        if (baseImageId.StartsWith(BaseImagePrefixes.BuildChain))
        {
            string defId = baseImageId[BaseImagePrefixes.BuildChain.Length..];
            BuildDefinition? chainedDef = _db.BuildDefinitions.FindById(defId);
            string requiredVersion = chainedDef?.Version ?? string.Empty;

            ImageArtifact? artifact = _db
                .ImageArtifacts.Find(a =>
                    a.BuildDefinitionId == defId && a.Version == requiredVersion
                )
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            if (artifact is null)
            {
                artifact = _db
                    .ImageArtifacts.Find(a => a.BuildDefinitionId == defId)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefault();
            }

            if (artifact is not null && File.Exists(artifact.FilePath))
            {
                return new BaseImage
                {
                    Id = baseImageId,
                    Name = chainedDef is not null
                        ? $"{chainedDef.Name} v{artifact.Version}"
                        : "Chained build output",
                    LocalCachePath = artifact.FilePath,
                    ImageFormat = artifact.Format,
                    IsCached = true,
                };
            }
            throw new InvalidOperationException(
                $"No artifact available for chained build definition {defId}"
            );
        }

        if (baseImageId.StartsWith(BaseImagePrefixes.Artifact))
        {
            string artId = baseImageId[BaseImagePrefixes.Artifact.Length..];
            ImageArtifact? artifact = _db.ImageArtifacts.FindById(artId);
            if (artifact is not null && File.Exists(artifact.FilePath))
            {
                return new BaseImage
                {
                    Id = baseImageId,
                    Name = artifact.Name,
                    LocalCachePath = artifact.FilePath,
                    ImageFormat = artifact.Format,
                    IsCached = true,
                };
            }
            throw new InvalidOperationException($"Artifact {artId} not found");
        }

        BaseImage? image = _db.BaseImages.FindById(baseImageId);
        if (image is null)
            throw new InvalidOperationException($"Base image {baseImageId} not found");
        return image;
    }

    private void ApplyChecksumToArtifact(string artifactId, string outputDir)
    {
        ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
        if (artifact is null)
        {
            return;
        }

        string checksumFile = artifact.FilePath + ".sha256";
        if (File.Exists(checksumFile))
        {
            string content = File.ReadAllText(checksumFile).Trim();
            int spaceIndex = content.IndexOf(' ');
            artifact.Checksum = spaceIndex > 0 ? content.Substring(0, spaceIndex) : content;
            _db.ImageArtifacts.Update(artifact);
            _logger.LogInformation(
                "Applied checksum to artifact {ArtifactId}: {Checksum}",
                artifactId,
                artifact.Checksum
            );
        }

        string[] allFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
        string? largestFile = allFiles
            .Where(f => !f.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();

        if (largestFile is not null && largestFile != artifact.FilePath)
        {
            artifact.FilePath = largestFile;
            artifact.FileSizeBytes = new FileInfo(largestFile).Length;
            artifact.Format = Path.GetExtension(largestFile).TrimStart('.');
            _db.ImageArtifacts.Update(artifact);
            _logger.LogInformation(
                "Updated artifact path to post-processed output: {Path}",
                largestFile
            );
        }
    }

    private PackerRunnerConfig ResolveRunnerConfig(BuildDefinition definition)
    {
        PackerRunnerConfig? runnerConfig = null;

        if (!string.IsNullOrEmpty(definition.PackerRunnerConfigId))
        {
            runnerConfig = _db.PackerRunners.FindById(definition.PackerRunnerConfigId);
        }

        if (runnerConfig is null)
        {
            AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            if (settings is not null && !string.IsNullOrEmpty(settings.DefaultPackerRunnerConfigId))
            {
                runnerConfig = _db.PackerRunners.FindById(settings.DefaultPackerRunnerConfigId);
            }
        }

        if (runnerConfig is null)
        {
            runnerConfig = _db.PackerRunners.FindAll().FirstOrDefault();
        }

        if (runnerConfig is null)
        {
            AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            if (settings is not null && !string.IsNullOrEmpty(settings.PackerPath))
            {
                runnerConfig = new PackerRunnerConfig
                {
                    Id = KnownIds.AutoLocalRunner,
                    Name = "Local Packer",
                    PackerPath = settings.PackerPath,
                };
                _db.PackerRunners.Upsert(runnerConfig);
            }
        }

        if (runnerConfig is null)
        {
            throw new InvalidOperationException(
                "No Packer runner configuration found. Set a Packer path in Settings first."
            );
        }

        return runnerConfig;
    }

    private void FlattenArtifactDirectory(string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            return;
        }

        string[] vhdxFiles = Directory
            .GetFiles(outputDir, "*.vhdx", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(outputDir, "*.vhd", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(outputDir, "*.qcow2", SearchOption.AllDirectories))
            .ToArray();

        foreach (string file in vhdxFiles)
        {
            string destPath = Path.Combine(outputDir, Path.GetFileName(file));
            if (file != destPath && !File.Exists(destPath))
            {
                File.Move(file, destPath);
                _logger.LogInformation("Moved artifact {Source} to {Dest}", file, destPath);
            }
        }

        string[] checksumFiles = Directory.GetFiles(
            outputDir,
            "*.sha256",
            SearchOption.AllDirectories
        );
        foreach (string file in checksumFiles)
        {
            string destPath = Path.Combine(outputDir, Path.GetFileName(file));
            if (file != destPath && !File.Exists(destPath))
            {
                File.Move(file, destPath);
            }
        }
    }

    private void CleanupRawExportFiles(
        string outputDir,
        Action<string, Contracts.Models.LogLevel, string> addLog,
        string executionId
    )
    {
        string[] exportSubDirs = { "Virtual Hard Disks", "Virtual Machines", "Snapshots" };
        long freedBytes = 0;

        foreach (string subDir in exportSubDirs)
        {
            string path = Path.Combine(outputDir, subDir);
            if (!Directory.Exists(path))
            {
                continue;
            }

            try
            {
                long dirSize = Directory
                    .GetFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                Directory.Delete(path, true);
                freedBytes += dirSize;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up {Path}", path);
            }
        }

        if (freedBytes > 0)
        {
            string freedDisplay =
                freedBytes > 1024 * 1024 * 1024
                    ? $"{freedBytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
                    : $"{freedBytes / (1024.0 * 1024.0):F0} MB";
            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Cleaned up raw export files, freed {freedDisplay}"
            );
        }
    }

    private void CleanupOutputDirectory(string outputDir)
    {
        try
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
                _logger.LogInformation("Cleaned up build output at {Path}", outputDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up failed build output at {Path}", outputDir);
        }
    }

    private void CleanupIntermediateArtifactFiles(
        string executionId,
        string? artifactId,
        string outputDir,
        Action<string, Contracts.Models.LogLevel, string> addLog
    )
    {
        if (artifactId is null || !Directory.Exists(outputDir))
        {
            return;
        }

        ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
        if (artifact is null)
        {
            return;
        }

        string keepPath = Path.GetFullPath(artifact.FilePath);
        string keepChecksum = keepPath + ".sha256";
        long freedBytes = 0;

        foreach (string file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(file);
            if (
                fullPath.Equals(keepPath, StringComparison.OrdinalIgnoreCase)
                || fullPath.Equals(keepChecksum, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            try
            {
                long size = new FileInfo(file).Length;
                File.Delete(file);
                freedBytes += size;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete intermediate file {Path}", file);
            }
        }

        if (freedBytes > 0)
        {
            string freedDisplay =
                freedBytes > 1024 * 1024 * 1024
                    ? $"{freedBytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
                    : $"{freedBytes / (1024.0 * 1024.0):F0} MB";
            addLog(
                executionId,
                Contracts.Models.LogLevel.Info,
                $"Cleaned up intermediate files, freed {freedDisplay}"
            );
        }
    }
}
