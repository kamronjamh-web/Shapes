using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddProjectileTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTemplate(
            "rebound_array",
            "Rebound Array",
            "Turns shots into room-clearing ricochet chains.",
            WeaponModulatorCategory.Projectile,
            8f,
            new Color(0.3529f, 0.7647f, 1.0000f),
            Tags("ricochet", "bounce", "projectile"),
            new SubstatRoll(SubstatType.Ricochet, 1f),
            new SubstatRoll(SubstatType.BounceTimes, 1f)));

        output.Add(CreateTemplate(
            "split_chamber",
            "Split Chamber",
            "Adds a second firing lane without changing the weapon base.",
            WeaponModulatorCategory.Projectile,
            8f,
            new Color(0.9176f, 0.5255f, 1.0000f),
            Tags("multishot", "projectile_count", "spread"),
            new SubstatRoll(SubstatType.ProjectileCount, 1f),
            new SubstatRoll(SubstatType.DamagePercent, 10f)));

        output.Add(CreateTemplate(
            "storm_relay",
            "Storm Relay",
            "Adds a fast multishot profile for aggressive builds.",
            WeaponModulatorCategory.Projectile,
            6f,
            new Color(0.3098f, 0.5961f, 1.0000f),
            Tags("multishot", "attack_speed", "range", "projectile"),
            new SubstatRoll(SubstatType.ProjectileCount, 1f),
            new SubstatRoll(SubstatType.AttackSpeed, 14f),
            new SubstatRoll(SubstatType.Range, 6f)));
    }
}