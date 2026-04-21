using System.Text;
using ForgeBoard.Contracts;
using ForgeBoard.Contracts.Interfaces;
using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;

namespace ForgeBoard.Core.Services;

public sealed class PackerTemplateGenerator : IPackerTemplateGenerator
{
    private readonly ForgeBoardDatabase _db;

    public PackerTemplateGenerator(ForgeBoardDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    private (string Username, string Password) GetWinrmCredentials()
    {
        AppSettings? settings = _db.AppSettings.FindById(KnownIds.DefaultSettings);
        string username = settings?.WinrmUsername ?? "Administrator";
        string password = settings?.WinrmPassword ?? "Admin123!";
        return (username, password);
    }

    public string GenerateHcl(
        BuildDefinition definition,
        BaseImage baseImage,
        string outputDirectory
    )
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        string imagePath = baseImage.LocalCachePath ?? baseImage.FileName;
        bool isDisk = IsDiskImage(imagePath) || IsVagrantBox(imagePath) || IsVmName(imagePath);
        string builderType = ResolveBuilderType(definition.Builder, isDisk);

        StringBuilder hcl = new StringBuilder();

        AppendPluginBlock(hcl, definition.Builder);
        hcl.AppendLine();
        AppendSourceBlock(hcl, definition, baseImage, outputDirectory);
        hcl.AppendLine();
        AppendBuildBlock(hcl, definition, builderType);

        return hcl.ToString();
    }

    private static string ResolveBuilderType(PackerBuilder builder, bool isDiskImage)
    {
        if (builder == PackerBuilder.Qemu)
        {
            return "qemu";
        }
        return isDiskImage ? "hyperv-vmcx" : "hyperv-iso";
    }

    public List<string> GenerateStepDescription(BuildDefinition definition, BaseImage baseImage)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(baseImage);

        List<string> steps = new List<string>();

        string builderName = definition.Builder == PackerBuilder.Qemu ? "QEMU" : "Hyper-V";
        steps.Add(
            $"Boot VM from {baseImage.Name} ({Path.GetExtension(baseImage.FileName).TrimStart('.')}) using {builderName}"
        );
        steps.Add(
            $"Allocate {definition.MemoryMb} MB RAM, {definition.CpuCount} CPUs, {definition.DiskSizeMb} MB disk"
        );
        steps.Add("Wait for WinRM connectivity");

        foreach (BuildStep step in definition.Steps.OrderBy(s => s.Order))
        {
            string description = step.StepType switch
            {
                BuildStepType.PowerShell => $"Run PowerShell: {step.Name}",
                BuildStepType.Shell => $"Run shell command: {step.Name}",
                BuildStepType.PowerShellFile => $"Execute PowerShell script: {step.Name}",
                BuildStepType.ShellFile => $"Execute shell script: {step.Name}",
                BuildStepType.FileUpload => $"Upload file: {step.Content} to {step.Name}",
                BuildStepType.WindowsRestart =>
                    $"Restart Windows (timeout: {step.TimeoutSeconds}s)",
                BuildStepType.Custom => $"Custom provisioner: {step.Name}",
                _ => $"Step: {step.Name}",
            };
            steps.Add(description);
        }

        steps.Add("Shutdown VM");

        if (definition.PostProcessors.Count > 0)
        {
            steps.Add($"Run post-processors: {string.Join(", ", definition.PostProcessors)}");
        }

        return steps;
    }

    private static void AppendPluginBlock(StringBuilder hcl, PackerBuilder builder)
    {
        hcl.AppendLine("packer {");
        hcl.AppendLine("  required_plugins {");

        if (builder == PackerBuilder.Qemu)
        {
            hcl.AppendLine("    qemu = {");
            hcl.AppendLine("      source  = \"github.com/hashicorp/qemu\"");
            hcl.AppendLine("      version = \"~> 1\"");
            hcl.AppendLine("    }");
        }
        else
        {
            hcl.AppendLine("    hyperv = {");
            hcl.AppendLine("      source  = \"github.com/hashicorp/hyperv\"");
            hcl.AppendLine("      version = \"~> 1\"");
            hcl.AppendLine("    }");
        }

        hcl.AppendLine("  }");
        hcl.AppendLine("}");
    }

    private static bool IsIsoImage(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".iso";
    }

    private static bool IsDiskImage(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".vhdx" or ".vhd" or ".vmcx" or ".qcow2" or ".vmdk" or ".raw" or ".img";
    }

    private static bool IsVagrantBox(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".box";
    }

    private static bool IsVmName(string path)
    {
        return !string.IsNullOrEmpty(path)
            && !path.Contains(Path.DirectorySeparatorChar)
            && !path.Contains(Path.AltDirectorySeparatorChar)
            && string.IsNullOrEmpty(Path.GetExtension(path));
    }

    private void AppendSourceBlock(
        StringBuilder hcl,
        BuildDefinition definition,
        BaseImage baseImage,
        string outputDirectory
    )
    {
        string imagePath = baseImage.LocalCachePath ?? baseImage.FileName;
        string checksum = FormatChecksum(baseImage.Checksum, baseImage.FileName);
        bool isDisk = IsDiskImage(imagePath) || IsVagrantBox(imagePath) || IsVmName(imagePath);

        if (definition.Builder == PackerBuilder.Qemu)
        {
            AppendQemuSource(hcl, definition, imagePath, checksum, outputDirectory, isDisk);
        }
        else
        {
            if (isDisk)
            {
                AppendHyperVVmcxSource(hcl, definition, imagePath, outputDirectory);
            }
            else
            {
                AppendHyperVIsoSource(hcl, definition, imagePath, checksum, outputDirectory);
            }
        }
    }

    private static string GenerateVmName(BuildDefinition definition)
    {
        string safeName = System
            .Text.RegularExpressions.Regex.Replace(definition.Name, @"[^a-zA-Z0-9\-_]", "-")
            .Trim('-');
        if (string.IsNullOrEmpty(safeName))
            safeName = "forgeboard";
        string shortId = definition.Id.Length > 8 ? definition.Id[..8] : definition.Id;
        return $"{safeName}-{shortId}";
    }

    private void AppendQemuSource(
        StringBuilder hcl,
        BuildDefinition definition,
        string imagePath,
        string checksum,
        string outputDirectory,
        bool isDiskImage
    )
    {
        hcl.AppendLine("source \"qemu\" \"build\" {");
        hcl.AppendLine($"  vm_name           = \"{EscapeHcl(GenerateVmName(definition))}\"");

        if (isDiskImage)
        {
            hcl.AppendLine($"  iso_url           = \"{EscapeHcl(imagePath)}\"");
            hcl.AppendLine($"  iso_checksum      = \"{EscapeHcl(checksum)}\"");
            hcl.AppendLine("  disk_image        = true");
        }
        else
        {
            hcl.AppendLine($"  iso_url           = \"{EscapeHcl(imagePath)}\"");
            hcl.AppendLine($"  iso_checksum      = \"{EscapeHcl(checksum)}\"");
            hcl.AppendLine($"  disk_size         = \"{definition.DiskSizeMb}M\"");
        }

        hcl.AppendLine($"  output_directory  = \"{EscapeHcl(outputDirectory)}\"");

        if (!string.IsNullOrEmpty(definition.OutputFormat))
        {
            hcl.AppendLine($"  format            = \"{EscapeHcl(definition.OutputFormat)}\"");
        }

        hcl.AppendLine(
            "  shutdown_command   = \"shutdown /s /t 10 /f /d p:4:1 /c \\\"Packer shutdown\\\"\""
        );
        hcl.AppendLine($"  memory            = {definition.MemoryMb}");
        hcl.AppendLine($"  cpus              = {definition.CpuCount}");
        hcl.AppendLine("  accelerator       = \"tcg\"");
        hcl.AppendLine("  communicator      = \"winrm\"");
        (string winrmUser, string winrmPass) = GetWinrmCredentials();
        hcl.AppendLine($"  winrm_username    = \"{EscapeHcl(winrmUser)}\"");
        hcl.AppendLine($"  winrm_password    = \"{EscapeHcl(winrmPass)}\"");
        hcl.AppendLine("  winrm_timeout     = \"30m\"");

        if (definition.UnattendPath is not null)
        {
            hcl.AppendLine($"  floppy_files      = [\"{EscapeHcl(definition.UnattendPath)}\"]");
        }

        hcl.AppendLine("}");
    }

    private void AppendHyperVIsoSource(
        StringBuilder hcl,
        BuildDefinition definition,
        string isoPath,
        string checksum,
        string outputDirectory
    )
    {
        hcl.AppendLine("source \"hyperv-iso\" \"build\" {");
        hcl.AppendLine($"  vm_name           = \"{EscapeHcl(GenerateVmName(definition))}\"");
        hcl.AppendLine($"  iso_url           = \"{EscapeHcl(isoPath)}\"");
        hcl.AppendLine($"  iso_checksum      = \"{EscapeHcl(checksum)}\"");
        hcl.AppendLine($"  output_directory  = \"{EscapeHcl(outputDirectory)}\"");
        hcl.AppendLine("  shutdown_command   = \"shutdown /s /t 10 /f\"");
        hcl.AppendLine($"  disk_size         = {definition.DiskSizeMb}");
        hcl.AppendLine($"  memory            = {definition.MemoryMb}");
        hcl.AppendLine($"  cpus              = {definition.CpuCount}");
        hcl.AppendLine("  generation        = 2");
        hcl.AppendLine("  enable_secure_boot = false");
        hcl.AppendLine("  headless          = true");
        hcl.AppendLine("  switch_name       = \"Default Switch\"");
        hcl.AppendLine("  communicator      = \"winrm\"");
        (string winrmUser, string winrmPass) = GetWinrmCredentials();
        hcl.AppendLine($"  winrm_username    = \"{EscapeHcl(winrmUser)}\"");
        hcl.AppendLine($"  winrm_password    = \"{EscapeHcl(winrmPass)}\"");
        hcl.AppendLine("  winrm_timeout     = \"2h\"");

        hcl.AppendLine("}");
    }

    private void AppendHyperVVmcxSource(
        StringBuilder hcl,
        BuildDefinition definition,
        string imagePath,
        string outputDirectory
    )
    {
        string extension = Path.GetExtension(imagePath).ToLowerInvariant();

        hcl.AppendLine("source \"hyperv-vmcx\" \"build\" {");
        hcl.AppendLine($"  vm_name           = \"{EscapeHcl(GenerateVmName(definition))}\"");

        if (extension is ".vmcx")
        {
            hcl.AppendLine($"  clone_from_vmcx_path = \"{EscapeHcl(imagePath)}\"");
        }
        else
        {
            // For VHDX/VHD files, the build worker registers a temp VM and passes the name
            // The imagePath here will be the VM name set by the worker
            hcl.AppendLine($"  clone_from_vm_name = \"{EscapeHcl(imagePath)}\"");
        }

        hcl.AppendLine($"  output_directory  = \"{EscapeHcl(outputDirectory)}\"");
        hcl.AppendLine("  shutdown_command   = \"shutdown /s /t 10 /f\"");
        hcl.AppendLine($"  memory            = {definition.MemoryMb}");
        hcl.AppendLine($"  cpus              = {definition.CpuCount}");
        hcl.AppendLine("  generation        = 2");
        hcl.AppendLine("  headless          = true");
        hcl.AppendLine("  switch_name       = \"Default Switch\"");
        hcl.AppendLine("  communicator      = \"winrm\"");
        (string winrmUser, string winrmPass) = GetWinrmCredentials();
        hcl.AppendLine($"  winrm_username    = \"{EscapeHcl(winrmUser)}\"");
        hcl.AppendLine($"  winrm_password    = \"{EscapeHcl(winrmPass)}\"");
        hcl.AppendLine("  winrm_timeout     = \"30m\"");
        hcl.AppendLine("}");
    }

    private static void AppendBuildBlock(
        StringBuilder hcl,
        BuildDefinition definition,
        string builderType
    )
    {
        hcl.AppendLine("build {");
        hcl.AppendLine($"  sources = [\"source.{builderType}.build\"]");
        hcl.AppendLine();

        List<BuildStep> orderedSteps = definition.Steps.OrderBy(s => s.Order).ToList();
        foreach (BuildStep step in orderedSteps)
        {
            AppendProvisionerBlock(hcl, step);
        }

        hcl.AppendLine("}");
    }

    private static void AppendProvisionerBlock(StringBuilder hcl, BuildStep step)
    {
        switch (step.StepType)
        {
            case BuildStepType.PowerShell:
                AppendInlineProvisioner(hcl, "powershell", step);
                break;

            case BuildStepType.Shell:
                AppendInlineProvisioner(hcl, "shell", step);
                break;

            case BuildStepType.PowerShellFile:
                AppendScriptProvisioner(hcl, "powershell", step);
                break;

            case BuildStepType.ShellFile:
                AppendScriptProvisioner(hcl, "shell", step);
                break;

            case BuildStepType.FileUpload:
                AppendFileProvisioner(hcl, step);
                break;

            case BuildStepType.WindowsRestart:
                AppendWindowsRestartProvisioner(hcl, step);
                break;

            case BuildStepType.Custom:
                hcl.AppendLine(step.Content);
                hcl.AppendLine();
                break;
        }
    }

    private static void AppendInlineProvisioner(
        StringBuilder hcl,
        string provisionerType,
        BuildStep step
    )
    {
        string[] lines = step.Content.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries
        );
        string escapedLines = string.Join(", ", lines.Select(l => $"\"{EscapeHcl(l.Trim())}\""));

        hcl.AppendLine($"  provisioner \"{provisionerType}\" {{");
        hcl.AppendLine($"    inline  = [{escapedLines}]");
        hcl.AppendLine($"    timeout = \"{step.TimeoutSeconds}s\"");
        hcl.AppendLine("  }");
        hcl.AppendLine();
    }

    private static void AppendScriptProvisioner(
        StringBuilder hcl,
        string provisionerType,
        BuildStep step
    )
    {
        hcl.AppendLine($"  provisioner \"{provisionerType}\" {{");
        hcl.AppendLine($"    script  = \"{EscapeHcl(step.Content)}\"");
        hcl.AppendLine($"    timeout = \"{step.TimeoutSeconds}s\"");
        hcl.AppendLine("  }");
        hcl.AppendLine();
    }

    private static void AppendFileProvisioner(StringBuilder hcl, BuildStep step)
    {
        // Content format: "source_path" or "source_path\ndestination_path"
        string[] parts = step.Content.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries);
        string source = parts[0].Trim();
        string destination = parts.Length > 1 ? parts[1].Trim() : "C:\\" + Path.GetFileName(source);

        hcl.AppendLine("  provisioner \"file\" {");
        hcl.AppendLine($"    source      = \"{EscapeHcl(source)}\"");
        hcl.AppendLine($"    destination = \"{EscapeHcl(destination)}\"");
        hcl.AppendLine("  }");
        hcl.AppendLine();
    }

    private static void AppendWindowsRestartProvisioner(StringBuilder hcl, BuildStep step)
    {
        hcl.AppendLine("  provisioner \"windows-restart\" {");
        hcl.AppendLine($"    restart_timeout = \"{step.TimeoutSeconds}s\"");
        hcl.AppendLine("  }");
        hcl.AppendLine();
    }

    private static string FormatChecksum(string? checksum, string fileName)
    {
        if (!string.IsNullOrEmpty(checksum) && checksum != "none")
        {
            bool looksLikeSha256 =
                checksum.Length == 64 && checksum.All(c => "0123456789abcdefABCDEF".Contains(c));
            if (looksLikeSha256)
            {
                return $"sha256:{checksum}";
            }
            if (checksum.Contains(':'))
            {
                return checksum;
            }
            return checksum;
        }

        return "none";
    }

    private static string EscapeHcl(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("${", "$${");
    }
}
