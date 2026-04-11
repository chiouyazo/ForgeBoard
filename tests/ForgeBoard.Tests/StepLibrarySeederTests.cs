using ForgeBoard.Contracts.Models;
using ForgeBoard.Core.Data;
using ForgeBoard.Core.Services;

namespace ForgeBoard.Tests;

[TestFixture]
public class StepLibrarySeederTests
{
    private string _tempDir = null!;
    private TestAppPaths _paths = null!;
    private ForgeBoardDatabase _db = null!;

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
    public void SeedIfEmpty_PopulatesStepLibrary()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        int count = _db.StepLibrary.Count();
        count.Should().BeGreaterThan(0);
    }

    [Test]
    public void SeedIfEmpty_IncludesGenericSteps()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        List<BuildStepLibraryEntry> steps = _db.StepLibrary.FindAll().ToList();
        steps.Should().Contain(s => s.Name == "Install Chocolatey Package");
        steps.Should().Contain(s => s.Name == "Windows Restart");
        steps.Should().Contain(s => s.Name == "Run Inline PowerShell");
        steps.Should().Contain(s => s.Name == "Run Inline Shell");
    }

    [Test]
    public void SeedIfEmpty_Seeds9GenericEntries()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        int count = _db.StepLibrary.Count();
        count.Should().Be(9);
    }

    [Test]
    public void SeedIfEmpty_DoesNotDuplicateOnSecondCall()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);
        int firstCount = _db.StepLibrary.Count();

        StepLibrarySeeder.SeedIfEmpty(_db);
        int secondCount = _db.StepLibrary.Count();

        secondCount.Should().Be(firstCount);
    }

    [Test]
    public void SeedIfEmpty_AllEntriesHaveUniqueIds()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        List<BuildStepLibraryEntry> steps = _db.StepLibrary.FindAll().ToList();
        List<string> ids = steps.Select(s => s.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void SeedIfEmpty_AllEntriesHaveCreatedAt()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        List<BuildStepLibraryEntry> steps = _db.StepLibrary.FindAll().ToList();
        foreach (BuildStepLibraryEntry step in steps)
        {
            step.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    [Test]
    public void SeedIfEmpty_WindowsRestartHasExpectReboot()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        BuildStepLibraryEntry? restart = _db
            .StepLibrary.FindAll()
            .FirstOrDefault(s => s.StepType == BuildStepType.WindowsRestart);

        restart.Should().NotBeNull();
        restart!.ExpectReboot.Should().BeTrue();
    }

    [Test]
    public void SeedIfEmpty_AllEntriesHaveTags()
    {
        StepLibrarySeeder.SeedIfEmpty(_db);

        List<BuildStepLibraryEntry> steps = _db.StepLibrary.FindAll().ToList();
        foreach (BuildStepLibraryEntry step in steps)
        {
            step.Tags.Should().NotBeEmpty();
        }
    }
}
