using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using LiteDB;

namespace ForgeBoard.Core.Data;

public sealed class ForgeBoardDatabase : IDisposable
{
    private readonly LiteDatabase _db;

    public ForgeBoardDatabase(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string dbPath = Path.Combine(paths.DataDirectory, "forgeboard.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
        EnsureIndexes();
    }

    public ILiteCollection<Feed> Feeds => _db.GetCollection<Feed>("feeds");
    public ILiteCollection<BaseImage> BaseImages => _db.GetCollection<BaseImage>("base_images");
    public ILiteCollection<BuildStepLibraryEntry> StepLibrary =>
        _db.GetCollection<BuildStepLibraryEntry>("step_library");
    public ILiteCollection<BuildDefinition> BuildDefinitions =>
        _db.GetCollection<BuildDefinition>("build_definitions");
    public ILiteCollection<BuildExecution> BuildExecutions =>
        _db.GetCollection<BuildExecution>("build_executions");
    public ILiteCollection<BuildLogEntry> BuildLogs =>
        _db.GetCollection<BuildLogEntry>("build_logs");
    public ILiteCollection<ImageArtifact> ImageArtifacts =>
        _db.GetCollection<ImageArtifact>("image_artifacts");
    public ILiteCollection<PackerRunnerConfig> PackerRunners =>
        _db.GetCollection<PackerRunnerConfig>("packer_runners");
    public ILiteCollection<AppSettings> AppSettings =>
        _db.GetCollection<AppSettings>("app_settings");

    private void EnsureIndexes()
    {
        BuildLogs.EnsureIndex(x => x.BuildExecutionId);
        BuildLogs.EnsureIndex(x => x.Timestamp);
        BuildExecutions.EnsureIndex(x => x.BuildDefinitionId);
        BuildExecutions.EnsureIndex(x => x.Status);
        ImageArtifacts.EnsureIndex(x => x.BuildExecutionId);
        BaseImages.EnsureIndex(x => x.SourceId);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
