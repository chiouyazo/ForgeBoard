using ForgeBoard.Contracts.Models;

namespace ForgeBoard.Core.Services.Build;

public sealed class BuildPhase
{
    public BuildMode Mode { get; set; }

    public List<BuildStep> Steps { get; set; } = new List<BuildStep>();

    public static List<BuildPhase> SplitPhases(List<BuildStep> steps, bool forceAllPacker)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<BuildPhase> phases = new List<BuildPhase>();

        if (forceAllPacker)
        {
            BuildPhase packerPhase = new BuildPhase
            {
                Mode = BuildMode.Packer,
                Steps = new List<BuildStep>(steps),
            };
            phases.Add(packerPhase);
            return phases;
        }

        List<BuildStep> packerSteps = new List<BuildStep>();
        List<BuildStep> directSteps = new List<BuildStep>();

        foreach (BuildStep step in steps)
        {
            if (step.UsePacker)
            {
                packerSteps.Add(step);
            }
            else
            {
                directSteps.Add(step);
            }
        }

        if (packerSteps.Count > 0)
        {
            phases.Add(new BuildPhase { Mode = BuildMode.Packer, Steps = packerSteps });
        }

        if (directSteps.Count > 0)
        {
            phases.Add(new BuildPhase { Mode = BuildMode.Direct, Steps = directSteps });
        }

        return phases;
    }
}
