using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddWingTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTemplate(
            "wing_man",
            "Wing Man",
            "Support-only. Honest striker wing. Syncs with the primary volley, shares the main target, and adds clean escort damage. Only one Wing Man can be active at a time, but different Wing modulators can be combined as a set.",
            WeaponModulatorCategory.Wing,
            2f,
            new Color(0.5882f, 0.8275f, 1.0000f),
            Tags("support", "sync", "escort", "wing")));
        output[^1].PowerTier = ModulatorPowerTier.Tier3;
        output[^1].RefreshPresentation();

        output.Add(CreateTemplate(
            "wing_woman",
            "Wing Woman",
            "Support-only. Hunter wing. Prioritizes bosses, elites, low-health enemies, and marked targets while leaning into crits and executions. Only one Wing Woman can be active at a time, but different Wing modulators can be combined as a set.",
            WeaponModulatorCategory.Wing,
            2f,
            new Color(1.0000f, 0.6157f, 0.8627f),
            Tags("support", "hunter", "crit", "execute", "wing")));
        output[^1].PowerTier = ModulatorPowerTier.Tier3;
        output[^1].RefreshPresentation();

        output.Add(CreateTemplate(
            "wing_brother",
            "Wing Brother",
            "Support-only. Autonomous wing. Breaks formation, roams for its own angles, and targets enemies independently. Only one Wing Brother can be active at a time, but different Wing modulators can be combined as a set.",
            WeaponModulatorCategory.Wing,
            2f,
            new Color(0.5647f, 0.9373f, 0.6118f),
            Tags("support", "autonomous", "roaming", "wing")));
        output[^1].PowerTier = ModulatorPowerTier.Tier3;
        output[^1].RefreshPresentation();

        output.Add(CreateTemplate(
            "wing_sister",
            "Wing Sister",
            "Support-only. Fire-control wing. Improves target guidance, prediction, and zone placement across the wing network. Only one Wing Sister can be active at a time, but different Wing modulators can be combined as a set.",
            WeaponModulatorCategory.Wing,
            2f,
            new Color(1.0000f, 0.8471f, 0.4549f),
            Tags("support", "guidance", "prediction", "zone", "wing")));
        output[^1].PowerTier = ModulatorPowerTier.Tier3;
        output[^1].RefreshPresentation();
    }
}