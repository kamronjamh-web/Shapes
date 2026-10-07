using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddSustainTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTemplate(
            "reinforced_shell",
            "Reinforced Shell",
            "Adds survivability and slow regeneration.",
            WeaponModulatorCategory.Sustain,
            12f,
            new Color(0.3725f, 0.7961f, 0.8588f),
            Tags("health", "regen", "defense", "survivability"),
            new SubstatRoll(SubstatType.HealthPercent, 18f),
            new SubstatRoll(SubstatType.HealthRegen, 2f)));

        output.Add(CreateTemplate(
            "hemophage_loop",
            "Hemophage Loop",
            "Leans into sustain and mobility.",
            WeaponModulatorCategory.Sustain,
            8f,
            new Color(0.8784f, 0.2980f, 0.4314f),
            Tags("lifesteal", "mobility", "sustain"),
            new SubstatRoll(SubstatType.LifeStealPercent, 4f),
            new SubstatRoll(SubstatType.MovementSpeed, 8f)));
    }
}