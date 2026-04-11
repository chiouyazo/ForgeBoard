using System.Runtime.InteropServices;
using ForgeBoard.Contracts.Interfaces;

namespace ForgeBoard.Core.Services;

public sealed class AppPaths : IAppPaths
{
    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string ArtifactsDirectory { get; }
    public string WorkingDirectory { get; }
    public string LogsDirectory { get; }
    public string DatabasePath { get; }
    public string TempDirectory { get; }

    public AppPaths()
        : this(null, null) { }

    public AppPaths(string? baseDirectory, string? tempDirectory = null)
    {
        DataDirectory =
            !string.IsNullOrEmpty(baseDirectory) ? baseDirectory
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ForgeBoard"
                )
            : "/data/forgeboard";

        CacheDirectory = Path.Combine(DataDirectory, "cache");
        ArtifactsDirectory = Path.Combine(DataDirectory, "artifacts");
        WorkingDirectory = Path.Combine(DataDirectory, "workspace");
        LogsDirectory = Path.Combine(DataDirectory, "logs");
        DatabasePath = Path.Combine(DataDirectory, "forgeboard.db");
        TempDirectory = !string.IsNullOrEmpty(tempDirectory)
            ? tempDirectory
            : Path.Combine(DataDirectory, "temp");
    }

    public void EnsureDirectoriesExist()
    {
        string root = Path.GetPathRoot(DataDirectory) ?? string.Empty;
        if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"The drive '{root}' is not available. Check that the drive is mounted and accessible, "
                    + $"or update ForgeBoard:DataDirectory in appsettings.json to use a valid path."
            );
        }

        string tempRoot = Path.GetPathRoot(TempDirectory) ?? string.Empty;
        if (!string.IsNullOrEmpty(tempRoot) && tempRoot != root && !Directory.Exists(tempRoot))
        {
            throw new InvalidOperationException(
                $"The drive '{tempRoot}' is not available. Check that the drive is mounted and accessible, "
                    + $"or update ForgeBoard:TempDirectory in appsettings.json to use a valid path."
            );
        }

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(ArtifactsDirectory);
        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TempDirectory);
    }
}
