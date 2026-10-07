using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddUtilityTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTemplate(
            "lucky_prism",
            "Lucky Prism",
            "Biases the weapon toward drops and critical hits.",
            WeaponModulatorCategory.Utility,
            6f,
            new Color(0.9961f, 0.8627f, 0.2745f),
            Tags("luck", "drops", "crit", "utility"),
            new SubstatRoll(SubstatType.Luck, 18f),
            new SubstatRoll(SubstatType.CritChance, 8f)));
    }
}