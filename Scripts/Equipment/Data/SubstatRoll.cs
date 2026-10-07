using System;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public enum SubstatType
{
    None = 0,
    DamagePercent = 1,
    HealthPercent = 2,
    DodgeChance = 3,
    LifeStealPercent = 4,
    HealthRegen = 5,
    CritDamage = 6,
    CritChance = 7,
    MovementSpeed = 8,
    Range = 9,
    Luck = 10,
    Size = 11,
    AttackSpeed = 12,
    Ricochet = 13,
    BounceTimes = 14,
    ProjectileCount = 15,
}

public sealed class SubstatRoll
{
    private static readonly HashSet<SubstatType> FlatDisplayTypes = new()
    {
        SubstatType.Range,
        SubstatType.Luck,
        SubstatType.Ricochet,
        SubstatType.BounceTimes,
        SubstatType.ProjectileCount,
        SubstatType.HealthRegen,
    };

    public SubstatType Type { get; set; } = SubstatType.None;
    public float Value { get; set; }

    public SubstatRoll()
    {
    }

    public SubstatRoll(SubstatType type, float value)
    {
        Type = type;
        Value = value;
    }

    public bool IsPercent => !UsesFlatDisplay(Type);

    public static bool UsesFlatDisplay(SubstatType type) => FlatDisplayTypes.Contains(type);

    public string ToDisplayString()
    {
        string label = ToDisplayName(Type);
        if (IsPercent)
        {
            return $"{label} +{Value:0.00}%";
        }

        return $"{label} +{Value:0.0}";
    }

    public static string ToDisplayName(SubstatType type)
    {
        return type switch
        {
            SubstatType.DamagePercent => "Damage Percent",
            SubstatType.HealthPercent => "Health Percent",
            SubstatType.DodgeChance => "Dodge Chance",
            SubstatType.LifeStealPercent => "Life Steal Percent",
            SubstatType.HealthRegen => "Health Regen",
            SubstatType.CritDamage => "Crit Damage",
            SubstatType.CritChance => "Crit Chance",
            SubstatType.MovementSpeed => "Movement Speed",
            SubstatType.Range => "Range",
            SubstatType.Luck => "Luck",
            SubstatType.Size => "Size",
            SubstatType.AttackSpeed => "Attack Speed",
            SubstatType.Ricochet => "Ricochet",
            SubstatType.BounceTimes => "Bounce Times",
            SubstatType.ProjectileCount => "Projectile Count",
            _ => "Unknown",
        };
    }
}
