namespace ForgeBoard.Contracts.Interfaces;

public interface IAppPaths
{
    string DataDirectory { get; }

    string CacheDirectory { get; }

    string ArtifactsDirectory { get; }

    string WorkingDirectory { get; }

    string LogsDirectory { get; }

    string DatabasePath { get; }

    string TempDirectory { get; }

    void EnsureDirectoriesExist();
}
