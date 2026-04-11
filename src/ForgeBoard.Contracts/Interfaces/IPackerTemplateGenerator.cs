using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Contracts.Interfaces;

public interface IPackerTemplateGenerator
{
    string GenerateHcl(BuildDefinition definition, BaseImage baseImage, string outputDirectory);

    List<string> GenerateStepDescription(BuildDefinition definition, BaseImage baseImage);
}
