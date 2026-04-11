using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;

namespace ForgeBoard.Core.Services;

public static class StepLibrarySeeder
{
    public static void SeedIfEmpty(ForgeBoardDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (db.StepLibrary.Count() > 0)
        {
            return;
        }

        List<BuildStepLibraryEntry> seeds = CreateGenericSteps();

        foreach (BuildStepLibraryEntry entry in seeds)
        {
            entry.CreatedAt = DateTimeOffset.UtcNow;
            db.StepLibrary.Insert(entry);
        }
    }

    private static List<BuildStepLibraryEntry> CreateGenericSteps()
    {
        return new List<BuildStepLibraryEntry>
        {
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Install Chocolatey Package",
                Description = "Installs a package using Chocolatey package manager",
                StepType = BuildStepType.PowerShell,
                Content = "choco install $PackageName -y --no-progress",
                DefaultTimeoutSeconds = 600,
                Tags = new List<string> { "chocolatey", "packages", "windows" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Run SQL Script",
                Description = "Executes a SQL script using sqlcmd",
                StepType = BuildStepType.PowerShell,
                Content = "sqlcmd -S $Server -C -E -i $ScriptPath",
                DefaultTimeoutSeconds = 300,
                Tags = new List<string> { "sql", "database", "windows" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Windows Restart",
                Description = "Restarts the Windows VM and waits for it to come back",
                StepType = BuildStepType.WindowsRestart,
                Content = string.Empty,
                DefaultTimeoutSeconds = 600,
                ExpectReboot = true,
                Tags = new List<string> { "restart", "reboot", "windows" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Configure Windows Service",
                Description = "Configures a Windows service run-as account",
                StepType = BuildStepType.PowerShell,
                Content =
                    "sc.exe config \"$ServiceName\" obj= \".\\$Username\" password= \"$Password\"",
                DefaultTimeoutSeconds = 120,
                Tags = new List<string> { "service", "windows", "configuration" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Set Registry Value",
                Description = "Sets a Windows registry value",
                StepType = BuildStepType.PowerShell,
                Content = "reg add \"$Path\" /v \"$Name\" /t $Type /d \"$Value\" /f",
                DefaultTimeoutSeconds = 60,
                Tags = new List<string> { "registry", "windows", "configuration" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Enable Windows Feature",
                Description = "Enables an optional Windows feature",
                StepType = BuildStepType.PowerShell,
                Content = "Enable-WindowsOptionalFeature -Online -FeatureName $Feature -All",
                DefaultTimeoutSeconds = 600,
                ExpectReboot = true,
                Tags = new List<string> { "features", "windows", "configuration" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Download and Install from URL",
                Description = "Downloads an installer from a URL and runs it",
                StepType = BuildStepType.PowerShell,
                Content =
                    "$installerPath = Join-Path $env:TEMP 'installer.exe'\nInvoke-WebRequest -Uri $Url -OutFile $installerPath -UseBasicParsing\nStart-Process -FilePath $installerPath -ArgumentList $Arguments -Wait -NoNewWindow\nRemove-Item $installerPath -Force",
                DefaultTimeoutSeconds = 900,
                Tags = new List<string> { "download", "install", "windows" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Run Inline PowerShell",
                Description = "Runs custom inline PowerShell script",
                StepType = BuildStepType.PowerShell,
                Content = string.Empty,
                DefaultTimeoutSeconds = 300,
                Tags = new List<string> { "powershell", "custom", "windows" },
            },
            new BuildStepLibraryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Run Inline Shell",
                Description = "Runs custom inline shell script",
                StepType = BuildStepType.Shell,
                Content = string.Empty,
                DefaultTimeoutSeconds = 300,
                Tags = new List<string> { "shell", "custom", "linux" },
            },
        };
    }
}
