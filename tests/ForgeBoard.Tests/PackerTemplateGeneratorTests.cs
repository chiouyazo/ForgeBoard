using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Services;

namespace ForgeBoard.Tests;

[TestFixture]
public class PackerTemplateGeneratorTests
{
    private PackerTemplateGenerator _generator = null!;

    [SetUp]
    public void Setup()
    {
        _generator = new PackerTemplateGenerator();
    }

    [Test]
    public void GenerateHcl_QemuBuilder_ContainsQemuSource()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("source \"qemu\" \"build\"");
        hcl.Should().Contain("github.com/hashicorp/qemu");
        hcl.Should().Contain("communicator      = \"winrm\"");
    }

    [Test]
    public void GenerateHcl_HyperVBuilder_ContainsHyperVSource()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.HyperV);
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("source \"hyperv-iso\" \"build\"");
        hcl.Should().Contain("github.com/hashicorp/hyperv");
        hcl.Should().Contain("generation        = 2");
    }

    [Test]
    public void GenerateHcl_WithPowerShellStep_ContainsInlineProvisioner()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step1",
                Order = 0,
                Name = "Test Step",
                StepType = BuildStepType.PowerShell,
                Content = "Write-Host \"Hello\"",
                TimeoutSeconds = 300,
            }
        );
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("provisioner \"powershell\"");
        hcl.Should().Contain("Write-Host");
    }

    [Test]
    public void GenerateHcl_WithWindowsRestartStep_ContainsRestartProvisioner()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step1",
                Order = 0,
                Name = "Restart",
                StepType = BuildStepType.WindowsRestart,
                TimeoutSeconds = 600,
                ExpectReboot = true,
            }
        );
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("provisioner \"windows-restart\"");
        hcl.Should().Contain("restart_timeout = \"600s\"");
    }

    [Test]
    public void GenerateHcl_WithFileUploadStep_ContainsFileProvisioner()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step1",
                Order = 0,
                Name = "C:\\destination\\file.txt",
                StepType = BuildStepType.FileUpload,
                Content = "C:\\source\\file.txt",
            }
        );
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("provisioner \"file\"");
        hcl.Should().Contain("source");
        hcl.Should().Contain("destination");
    }

    [Test]
    public void GenerateHcl_WithChecksumSha256_FormatsCorrectly()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        BaseImage baseImage = CreateTestBaseImage();
        baseImage.Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("sha256:");
    }

    [Test]
    public void GenerateHcl_WithNoChecksum_UsesNone()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        BaseImage baseImage = CreateTestBaseImage();
        baseImage.Checksum = null;

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("iso_checksum      = \"none\"");
    }

    [Test]
    public void GenerateHcl_WithUnattendPath_ContainsFloppyFiles()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.UnattendPath = "C:\\unattend\\autounattend.xml";
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("floppy_files");
        hcl.Should().Contain("autounattend.xml");
    }

    [Test]
    public void GenerateHcl_IncludesMemoryAndCpu()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.MemoryMb = 8192;
        definition.CpuCount = 4;
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        hcl.Should().Contain("memory            = 8192");
        hcl.Should().Contain("cpus              = 4");
    }

    [Test]
    public void GenerateStepDescription_ProducesCorrectStepList()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step1",
                Order = 0,
                Name = "Install SQL",
                StepType = BuildStepType.PowerShell,
                TimeoutSeconds = 300,
            }
        );
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step2",
                Order = 1,
                Name = "Restart",
                StepType = BuildStepType.WindowsRestart,
                TimeoutSeconds = 600,
            }
        );
        definition.PostProcessors = new List<string> { "ConvertVHD", "Checksum" };
        BaseImage baseImage = CreateTestBaseImage();

        List<string> steps = _generator.GenerateStepDescription(definition, baseImage);

        steps.Should().Contain(s => s.Contains("Boot VM from"));
        steps.Should().Contain(s => s.Contains("Run PowerShell: Install SQL"));
        steps.Should().Contain(s => s.Contains("Restart Windows"));
        steps.Should().Contain(s => s.Contains("Shutdown VM"));
        steps.Should().Contain(s => s.Contains("ConvertVHD"));
    }

    [Test]
    public void GenerateHcl_EscapesBackslashesInPaths()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        BaseImage baseImage = CreateTestBaseImage();
        baseImage.LocalCachePath = "C:\\Users\\test\\images\\win2022.iso";

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output\\dir");

        hcl.Should().Contain("C:\\\\Users\\\\test\\\\images\\\\win2022.iso");
    }

    [Test]
    public void GenerateHcl_OrdersStepsByOrder()
    {
        BuildDefinition definition = CreateTestDefinition(PackerBuilder.Qemu);
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step2",
                Order = 1,
                Name = "Second Step",
                StepType = BuildStepType.PowerShell,
                Content = "Write-Host Second",
                TimeoutSeconds = 60,
            }
        );
        definition.Steps.Add(
            new BuildStep
            {
                Id = "step1",
                Order = 0,
                Name = "First Step",
                StepType = BuildStepType.PowerShell,
                Content = "Write-Host First",
                TimeoutSeconds = 60,
            }
        );
        BaseImage baseImage = CreateTestBaseImage();

        string hcl = _generator.GenerateHcl(definition, baseImage, "C:\\output");

        int firstIndex = hcl.IndexOf("Write-Host First", StringComparison.Ordinal);
        int secondIndex = hcl.IndexOf("Write-Host Second", StringComparison.Ordinal);
        firstIndex.Should().BeLessThan(secondIndex);
    }

    private static BuildDefinition CreateTestDefinition(PackerBuilder builder)
    {
        return new BuildDefinition
        {
            Id = "test-def-1",
            Name = "Test Build",
            BaseImageId = "base-1",
            Builder = builder,
            MemoryMb = 4096,
            CpuCount = 2,
            DiskSizeMb = 40960,
            Steps = new List<BuildStep>(),
            Tags = new List<string>(),
            PostProcessors = new List<string>(),
        };
    }

    private static BaseImage CreateTestBaseImage()
    {
        return new BaseImage
        {
            Id = "base-1",
            Name = "Windows Server 2022",
            FileName = "win2022.iso",
            LocalCachePath = "C:\\images\\win2022.iso",
            Checksum = null,
        };
    }
}
