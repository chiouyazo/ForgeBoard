using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildReadinessChecker
{
    private readonly ForgeBoardDatabase _db;
    private readonly IAppPaths _appPaths;

    public BuildReadinessChecker(ForgeBoardDatabase db, IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(appPaths);
        _db = db;
        _appPaths = appPaths;
    }

    public BuildReadinessResult Check(string definitionId)
    {
        ArgumentNullException.ThrowIfNull(definitionId);

        BuildReadinessResult result = new BuildReadinessResult();

        BuildDefinition? definition = _db.BuildDefinitions.FindById(definitionId);
        if (definition is null)
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Definition",
                    Message = "Build definition not found",
                    Severity = IssueSeverity.Error,
                }
            );
            result.IsReady = false;
            return result;
        }

        CheckPackerInstalled(result);
        CheckBuilderAvailable(definition, result);
        CheckBaseImage(definition, result);
        CheckSteps(definition, result);
        CheckDiskSpace(definition, result);
        CheckNoConflictingExecution(definition, result);

        result.IsReady = !result.Issues.Any(i => i.Severity == IssueSeverity.Error);
        return result;
    }

    private void CheckPackerInstalled(BuildReadinessResult result)
    {
        PackerRunnerConfig? runner = _db.PackerRunners.FindAll().FirstOrDefault();
        if (runner is null)
        {
            AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
            if (settings is null || string.IsNullOrEmpty(settings.PackerPath))
            {
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Packer",
                        Message =
                            "No Packer path configured. Go to Settings and set the Packer executable path.",
                        Severity = IssueSeverity.Error,
                    }
                );
                return;
            }

            if (!File.Exists(settings.PackerPath))
            {
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Packer",
                        Message = $"Packer executable not found at: {settings.PackerPath}",
                        Severity = IssueSeverity.Error,
                    }
                );
            }
            return;
        }

        if (!string.IsNullOrEmpty(runner.PackerPath) && !File.Exists(runner.PackerPath))
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Packer",
                    Message = $"Packer executable not found at: {runner.PackerPath}",
                    Severity = IssueSeverity.Error,
                }
            );
        }
    }

    private static void CheckBuilderAvailable(
        BuildDefinition definition,
        BuildReadinessResult result
    )
    {
        if (definition.Builder == PackerBuilder.Qemu)
        {
            string executable = OperatingSystem.IsWindows()
                ? "qemu-system-x86_64.exe"
                : "qemu-system-x86_64";
            bool found = false;
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                char separator = OperatingSystem.IsWindows() ? ';' : ':';
                foreach (
                    string directory in pathEnv.Split(
                        separator,
                        StringSplitOptions.RemoveEmptyEntries
                    )
                )
                {
                    if (File.Exists(Path.Combine(directory, executable)))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Builder",
                        Message =
                            $"QEMU ({executable}) not found in PATH. Install QEMU or switch to Hyper-V.",
                        Severity = IssueSeverity.Error,
                    }
                );
            }
        }
        else if (definition.Builder == PackerBuilder.HyperV)
        {
            if (!OperatingSystem.IsWindows())
            {
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Builder",
                        Message = "Hyper-V is only available on Windows",
                        Severity = IssueSeverity.Error,
                    }
                );
                return;
            }

            try
            {
                using Microsoft.Win32.RegistryKey? key =
                    Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization"
                    );
                if (key is null)
                {
                    result.Issues.Add(
                        new BuildReadinessIssue
                        {
                            Category = "Builder",
                            Message =
                                "Hyper-V feature is not installed. Enable it in Windows Features.",
                            Severity = IssueSeverity.Error,
                        }
                    );
                    return;
                }

                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                bool isAdmin = principal.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator
                );

                bool isHyperVAdmin = false;
                try
                {
                    isHyperVAdmin = principal.IsInRole(
                        new System.Security.Principal.SecurityIdentifier("S-1-5-32-578")
                    );
                }
                catch { }

                if (!isHyperVAdmin && identity.Groups is not null)
                {
                    System.Security.Principal.SecurityIdentifier hyperVSid =
                        new System.Security.Principal.SecurityIdentifier("S-1-5-32-578");
                    foreach (System.Security.Principal.IdentityReference group in identity.Groups)
                    {
                        try
                        {
                            if (
                                group.Translate(
                                    typeof(System.Security.Principal.SecurityIdentifier)
                                )
                                    is System.Security.Principal.SecurityIdentifier sid
                                && sid == hyperVSid
                            )
                            {
                                isHyperVAdmin = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (!isAdmin && !isHyperVAdmin)
                {
                    result.Issues.Add(
                        new BuildReadinessIssue
                        {
                            Category = "Builder",
                            Message =
                                "Current user is not in Administrators or Hyper-V Administrators group. Run as administrator or add user to Hyper-V Administrators.",
                            Severity = IssueSeverity.Error,
                        }
                    );
                }
            }
            catch (Exception)
            {
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Builder",
                        Message = "Could not verify Hyper-V availability",
                        Severity = IssueSeverity.Warning,
                    }
                );
            }
        }
    }

    private void CheckBaseImage(BuildDefinition definition, BuildReadinessResult result)
    {
        string baseImageId = definition.BaseImageId;

        if (string.IsNullOrEmpty(baseImageId))
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Base Image",
                    Message = "No base image selected",
                    Severity = IssueSeverity.Error,
                }
            );
            return;
        }

        if (baseImageId.StartsWith(BaseImagePrefixes.BuildChain))
        {
            return;
        }

        if (baseImageId.StartsWith(BaseImagePrefixes.Artifact))
        {
            string artifactId = baseImageId[BaseImagePrefixes.Artifact.Length..];
            ImageArtifact? artifact = _db.ImageArtifacts.FindById(artifactId);
            if (artifact is null || !File.Exists(artifact.FilePath))
            {
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Base Image",
                        Message = $"Build artifact file not found on disk",
                        Severity = IssueSeverity.Error,
                    }
                );
            }
            return;
        }

        BaseImage? image = _db.BaseImages.FindById(baseImageId);
        if (image is null)
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Base Image",
                    Message = "Selected base image not found in database",
                    Severity = IssueSeverity.Error,
                }
            );
            return;
        }

        if (!string.IsNullOrEmpty(image.LocalCachePath) && !File.Exists(image.LocalCachePath))
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Base Image",
                    Message = $"Base image file not found at: {image.LocalCachePath}",
                    Severity = IssueSeverity.Error,
                }
            );
        }

        if (string.IsNullOrEmpty(image.Checksum))
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Base Image",
                    Message =
                        "No checksum specified for the base image. Packer will show a warning.",
                    Severity = IssueSeverity.Warning,
                }
            );
        }
    }

    private static void CheckSteps(BuildDefinition definition, BuildReadinessResult result)
    {
        if (definition.Steps.Count == 0)
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Steps",
                    Message = "No build steps defined",
                    Severity = IssueSeverity.Warning,
                }
            );
        }
    }

    private void CheckDiskSpace(BuildDefinition definition, BuildReadinessResult result)
    {
        try
        {
            string artifactsDir = _appPaths.ArtifactsDirectory;
            string? root = Path.GetPathRoot(artifactsDir);
            if (string.IsNullOrEmpty(root))
                return;

            DriveInfo drive = new DriveInfo(root);
            long requiredBytes = definition.DiskSizeMb * 1024 * 1024;
            long bufferBytes = 2L * 1024 * 1024 * 1024;

            if (drive.AvailableFreeSpace < requiredBytes + bufferBytes)
            {
                long freeGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                long requiredGb = (requiredBytes + bufferBytes) / (1024 * 1024 * 1024);
                result.Issues.Add(
                    new BuildReadinessIssue
                    {
                        Category = "Disk Space",
                        Message =
                            $"Low disk space: {freeGb} GB free, need at least {requiredGb} GB (disk image + 2 GB buffer)",
                        Severity = IssueSeverity.Error,
                    }
                );
            }
        }
        catch (Exception)
        {
            // Drive info may fail on some platforms
        }
    }

    private void CheckNoConflictingExecution(
        BuildDefinition definition,
        BuildReadinessResult result
    )
    {
        bool hasRunning = _db
            .BuildExecutions.Find(e =>
                e.BuildDefinitionId == definition.Id
                && (
                    e.Status == BuildStatus.Running
                    || e.Status == BuildStatus.Preparing
                    || e.Status == BuildStatus.Queued
                )
            )
            .Any();

        if (hasRunning)
        {
            result.Issues.Add(
                new BuildReadinessIssue
                {
                    Category = "Execution",
                    Message = "A build for this definition is already running or queued",
                    Severity = IssueSeverity.Error,
                }
            );
        }
    }
}
