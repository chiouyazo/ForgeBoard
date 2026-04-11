namespace ForgeBoard.Tests;

public class SmokeTests
{
    [Test]
    public void ContractsModels_CanBeInstantiated()
    {
        ForgeBoard.Contracts.Models.BuildDefinition definition =
            new ForgeBoard.Contracts.Models.BuildDefinition();
        definition.Name.Should().BeEmpty();
        definition.Steps.Should().NotBeNull();
        definition.Tags.Should().NotBeNull();
    }

    [Test]
    public void BaseImage_HasDefaultValues()
    {
        ForgeBoard.Contracts.Models.BaseImage image = new ForgeBoard.Contracts.Models.BaseImage();
        image.Id.Should().BeEmpty();
        image.ImageFormat.Should().Be("box");
        image.Origin.Should().Be(ForgeBoard.Contracts.Models.ImageOrigin.Local);
    }
}
