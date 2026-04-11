using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForgeBoard.Tests;

[TestFixture]
public class ImageManagerTests
{
    private string _tempDir = null!;
    private TestAppPaths _paths = null!;
    private ForgeBoardDatabase _db = null!;
    private ImageManager _imageManager = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "forgeboard_test_" + Guid.NewGuid().ToString("N")
        );
        _paths = new TestAppPaths(_tempDir);
        _paths.EnsureDirectoriesExist();
        _db = new ForgeBoardDatabase(_paths);
        ILogger<ImageManager> logger = NullLogger<ImageManager>.Instance;
        _imageManager = new ImageManager(_db, _paths, logger);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public async Task CreateBaseImageAsync_AssignsId()
    {
        BaseImage image = new BaseImage { Name = "Test Image", FileName = "test.iso" };

        BaseImage result = await _imageManager.CreateBaseImageAsync(image);

        result.Id.Should().NotBeNullOrEmpty();
        result.Name.Should().Be("Test Image");
    }

    [Test]
    public async Task CreateBaseImageAsync_SetsCreatedAt()
    {
        BaseImage image = new BaseImage { Name = "Test Image", FileName = "test.iso" };

        BaseImage result = await _imageManager.CreateBaseImageAsync(image);

        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task GetBaseImageAsync_ReturnsCreatedImage()
    {
        BaseImage image = new BaseImage { Name = "Test Image", FileName = "test.iso" };
        BaseImage created = await _imageManager.CreateBaseImageAsync(image);

        BaseImage? retrieved = await _imageManager.GetBaseImageAsync(created.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Image");
    }

    [Test]
    public async Task GetBaseImageAsync_ReturnsNull_WhenNotFound()
    {
        BaseImage? result = await _imageManager.GetBaseImageAsync("nonexistent");

        result.Should().BeNull();
    }

    [Test]
    public async Task GetAllBaseImagesAsync_ReturnsAllImages()
    {
        await _imageManager.CreateBaseImageAsync(
            new BaseImage { Name = "Image 1", FileName = "a.iso" }
        );
        await _imageManager.CreateBaseImageAsync(
            new BaseImage { Name = "Image 2", FileName = "b.iso" }
        );

        List<BaseImage> result = await _imageManager.GetAllBaseImagesAsync();

        result.Should().HaveCount(2);
    }

    [Test]
    public async Task UpdateBaseImageAsync_UpdatesExistingImage()
    {
        BaseImage image = await _imageManager.CreateBaseImageAsync(
            new BaseImage { Name = "Original Name", FileName = "test.iso" }
        );

        image.Name = "Updated Name";
        await _imageManager.UpdateBaseImageAsync(image);

        BaseImage? retrieved = await _imageManager.GetBaseImageAsync(image.Id);
        retrieved!.Name.Should().Be("Updated Name");
    }

    [Test]
    public async Task DeleteBaseImageAsync_RemovesImage()
    {
        BaseImage image = await _imageManager.CreateBaseImageAsync(
            new BaseImage { Name = "To Delete", FileName = "test.iso" }
        );

        await _imageManager.DeleteBaseImageAsync(image.Id);

        BaseImage? retrieved = await _imageManager.GetBaseImageAsync(image.Id);
        retrieved.Should().BeNull();
    }

    [Test]
    public void DeleteBaseImageAsync_ThrowsWhenUsedByRunningBuild()
    {
        BaseImage image = new BaseImage
        {
            Id = "base-running",
            Name = "Running Base",
            FileName = "test.iso",
        };
        _db.BaseImages.Insert(image);

        BuildDefinition definition = new BuildDefinition
        {
            Id = "def-1",
            Name = "Test Build",
            BaseImageId = "base-running",
        };
        _db.BuildDefinitions.Insert(definition);

        BuildExecution execution = new BuildExecution
        {
            Id = "exec-1",
            BuildDefinitionId = "def-1",
            Status = BuildStatus.Running,
        };
        _db.BuildExecutions.Insert(execution);

        Func<Task> act = async () => await _imageManager.DeleteBaseImageAsync("base-running");

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*running build*");
    }

    [Test]
    public async Task GetAllMergedAsync_IncludesBaseImagesAndArtifactsAndChains()
    {
        _db.BaseImages.Insert(
            new BaseImage
            {
                Id = "base-1",
                Name = "Base Image",
                FileName = "base.iso",
            }
        );

        _db.ImageArtifacts.Insert(
            new ImageArtifact
            {
                Id = "artifact-1",
                Name = "Artifact Image",
                FilePath = "C:\\nonexistent\\artifact.qcow2",
                BuildExecutionId = "exec-1",
            }
        );

        _db.BuildDefinitions.Insert(new BuildDefinition { Id = "def-1", Name = "Chain Build" });

        List<BaseImage> merged = await _imageManager.GetAllMergedAsync();

        merged.Should().HaveCount(3);
        merged.Should().Contain(i => i.Name == "Base Image");
        merged.Should().Contain(i => i.Name == "Artifact Image");
        merged.Should().Contain(i => i.Name == "[Build] Chain Build");
    }

    [Test]
    public async Task GetAllMergedAsync_ChainImageHasCorrectOrigin()
    {
        _db.BuildDefinitions.Insert(new BuildDefinition { Id = "def-1", Name = "My Build" });

        List<BaseImage> merged = await _imageManager.GetAllMergedAsync();

        BaseImage? chainImage = merged.FirstOrDefault(i => i.Origin == ImageOrigin.BuildChain);
        chainImage.Should().NotBeNull();
        chainImage!.LinkedBuildDefinitionId.Should().Be("def-1");
        chainImage.Id.Should().Be("buildchain_def-1");
    }

    [Test]
    public async Task PromoteArtifactAsync_CreatesBaseImage()
    {
        _db.ImageArtifacts.Insert(
            new ImageArtifact
            {
                Id = "artifact-1",
                Name = "Build Output",
                FilePath = "C:\\nonexistent\\output.qcow2",
                Checksum = "abc123",
                FileSizeBytes = 1024,
            }
        );

        BaseImage promoted = await _imageManager.PromoteArtifactAsync("artifact-1");

        promoted.Should().NotBeNull();
        promoted.Name.Should().Be("Build Output");
        promoted.Origin.Should().Be(ImageOrigin.Built);
    }

    [Test]
    public void PromoteArtifactAsync_ThrowsWhenArtifactNotFound()
    {
        Func<Task> act = async () => await _imageManager.PromoteArtifactAsync("nonexistent");

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public async Task GetDiskUsageAsync_ReturnsUsageInfo()
    {
        DiskUsageInfo usage = await _imageManager.GetDiskUsageAsync();

        usage.Should().NotBeNull();
        usage.DriveTotalBytes.Should().BeGreaterThan(0);
    }
}
