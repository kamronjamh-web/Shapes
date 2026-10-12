using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Preserve gate/wallwalk regression contracts independently of the Blender map.
internal static class LegacyExpandedFortFixture
{
    internal static void Configure(FpsVerticalSlice map)
    {
        map.HillMapSettings = (FpsHillMapSettings)map.HillMapSettings!.Duplicate(true);
        map.HillMapSettings.SteppedStrongholdEnabled = false;
    }
}
