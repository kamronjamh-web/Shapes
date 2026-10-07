using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddProcTemplates(List<WeaponModulatorData> output)
    {
        WeaponModulatorData volitile = CreateTemplate(
            "volitile",
            "Volitile",
            "All player-caused damage detonates for 50% damage. Tier sets the base radius to 1, 2, 3, or 4, and extra Volitile copies only extend radius further without raising damage.",
            WeaponModulatorCategory.Proc,
            2f,
            new Color(1.0000f, 0.4863f, 0.2471f),
            Tags("explosion", "aoe", "proc", "radius"));
        volitile.PowerTier = ModulatorPowerTier.Tier3;
        volitile.SupportsTier = true;
        volitile.RefreshPresentation();
        output.Add(volitile);
    }
}