using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddCoreTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTemplate(
            "power_coil",
            "Power Coil",
            "Stacks raw damage into the weapon core.",
            WeaponModulatorCategory.Core,
            12f,
            new Color(0.9608f, 0.3373f, 0.2471f),
            Tags("damage", "offense", "core"),
            new SubstatRoll(SubstatType.DamagePercent, 22f)));

        output.Add(CreateTemplate(
            "accelerator_fins",
            "Accelerator Fins",
            "Speeds up attacks and tightens crit windows.",
            WeaponModulatorCategory.Core,
            10f,
            new Color(1.0000f, 0.7922f, 0.1961f),
            Tags("attack_speed", "crit", "offense"),
            new SubstatRoll(SubstatType.AttackSpeed, 18f),
            new SubstatRoll(SubstatType.CritChance, 6f)));

        output.Add(CreateTemplate(
            "long_barrel",
            "Long Barrel",
            "Pushes range and crit damage at the same time.",
            WeaponModulatorCategory.Core,
            10f,
            new Color(0.3922f, 0.9255f, 0.6667f),
            Tags("range", "crit", "damage"),
            new SubstatRoll(SubstatType.Range, 10f),
            new SubstatRoll(SubstatType.CritDamage, 14f)));

        output.Add(CreateTemplate(
            "siege_core",
            "Siege Core",
            "Bulks up projectile impact and screen presence.",
            WeaponModulatorCategory.Core,
            7f,
            new Color(0.9725f, 0.6039f, 0.2039f),
            Tags("damage", "size", "impact"),
            new SubstatRoll(SubstatType.DamagePercent, 28f),
            new SubstatRoll(SubstatType.Size, 10f)));
    }
}