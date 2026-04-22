using ForgeBoard.Contracts.Models;
using Microsoft.Extensions.Logging;
using ContractLogLevel = ForgeBoard.Contracts.Models.LogLevel;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildPhaseExecutor
{
    private readonly PackerBuildEngine _packerEngine;
    private readonly DirectBuildEngine _directEngine;
    private readonly ILogger<BuildPhaseExecutor> _logger;

    public BuildPhaseExecutor(
        PackerBuildEngine packerEngine,
        DirectBuildEngine directEngine,
        ILogger<BuildPhaseExecutor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(packerEngine);
        ArgumentNullException.ThrowIfNull(directEngine);
        ArgumentNullException.ThrowIfNull(logger);

        _packerEngine = packerEngine;
        _directEngine = directEngine;
        _logger = logger;
    }

    public async Task<string?> ExecuteAsync(
        string executionId,
        BuildDefinition definition,
        BaseImage baseImage,
        string workingDirectory,
        string outputDirectory,
        PackerRunnerConfig runnerConfig,
        Action<string, ContractLogLevel, string> addLog,
        Action<string, BuildStatus> notifyStatusChanged,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(executionId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(runnerConfig);
        ArgumentNullException.ThrowIfNull(addLog);
        ArgumentNullException.ThrowIfNull(notifyStatusChanged);

        bool forceAllPacker =
            definition.Builder == PackerBuilder.Qemu
            || !string.IsNullOrEmpty(definition.UnattendPath)
            || IsIsoBaseImage(baseImage);

        List<BuildPhase> phases = BuildPhase.SplitPhases(definition.Steps, forceAllPacker);

        string currentBaseImagePath = baseImage.LocalCachePath ?? baseImage.FileName;

        for (int i = 0; i < phases.Count; i++)
        {
            BuildPhase phase = phases[i];
            cancellationToken.ThrowIfCancellationRequested();

            addLog(
                executionId,
                ContractLogLevel.Info,
                $"Running {phase.Steps.Count} steps in {phase.Mode} mode"
            );

            foreach (BuildStep step in phase.Steps)
            {
                addLog(
                    executionId,
                    ContractLogLevel.Info,
                    $"  {step.Order}. [{step.StepType}] {step.Name} (timeout: {step.TimeoutSeconds}s)"
                );
            }

            BuildEngineResult result;

            if (phase.Mode == BuildMode.Packer)
            {
                bool hasDirectPhaseAfter = i < phases.Count - 1;
                string phaseOutputDir = hasDirectPhaseAfter
                    ? outputDirectory + "-packer"
                    : outputDirectory;

                BaseImage phaseBaseImage = new BaseImage
                {
                    Id = baseImage.Id,
                    Name = baseImage.Name,
                    Description = baseImage.Description,
                    FileName = baseImage.FileName,
                    Checksum = baseImage.Checksum,
                    FileSizeBytes = baseImage.FileSizeBytes,
                    SourceId = baseImage.SourceId,
                    Origin = baseImage.Origin,
                    ImageFormat = baseImage.ImageFormat,
                    LocalCachePath = currentBaseImagePath,
                    IsCached = baseImage.IsCached,
                    LinkedBuildDefinitionId = baseImage.LinkedBuildDefinitionId,
                    CreatedAt = baseImage.CreatedAt,
                    CacheLocally = baseImage.CacheLocally,
                    RepullOnNextBuild = baseImage.RepullOnNextBuild,
                    LastUsedAt = baseImage.LastUsedAt,
                };

                result = await _packerEngine.ExecuteAsync(
                    executionId,
                    definition,
                    phase.Steps,
                    phaseBaseImage,
                    workingDirectory,
                    phaseOutputDir,
                    runnerConfig,
                    addLog,
                    notifyStatusChanged,
                    cancellationToken
                );
            }
            else
            {
                result = await _directEngine.ExecuteAsync(
                    executionId,
                    phase.Steps,
                    currentBaseImagePath,
                    outputDirectory,
                    definition.MemoryMb,
                    definition.CpuCount,
                    addLog,
                    cancellationToken
                );
            }

            if (!result.Success)
            {
                addLog(
                    executionId,
                    ContractLogLevel.Error,
                    $"Phase {i + 1} ({phase.Mode}) failed: {result.ErrorMessage}"
                );
                return null;
            }

            if (result.OutputVhdxPath is not null)
            {
                currentBaseImagePath = result.OutputVhdxPath;
            }
        }

        return currentBaseImagePath;
    }

    private static bool IsIsoBaseImage(BaseImage baseImage)
    {
        string imagePath = baseImage.LocalCachePath ?? baseImage.FileName;
        return Path.GetExtension(imagePath).Equals(".iso", StringComparison.OrdinalIgnoreCase);
    }
}
