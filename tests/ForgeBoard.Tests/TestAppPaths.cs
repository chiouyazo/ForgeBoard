using ForgeBoard.Contracts.Interfaces;

namespace ForgeBoard.Tests;

public sealed class TestAppPaths : IAppPaths
{
    private readonly string _root;

    public TestAppPaths(string root)
    {
        _root = root;
    }

    public string DataDirectory => Path.Combine(_root, "data");
    public string CacheDirectory => Path.Combine(_root, "cache");
    public string ArtifactsDirectory => Path.Combine(_root, "artifacts");
    public string WorkingDirectory => Path.Combine(_root, "working");
    public string LogsDirectory => Path.Combine(_root, "logs");
    public string DatabasePath => Path.Combine(DataDirectory, "forgeboard.db");
    public string TempDirectory => Path.Combine(_root, "temp");

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ArtifactsDirectory);
        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
