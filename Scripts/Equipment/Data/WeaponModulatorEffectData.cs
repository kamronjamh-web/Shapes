using System;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public enum ModulatorPowerTier
{
    Tier1,
    Tier2,
    Tier3,
}

public enum ModulatorEffectTrigger
{
    Passive,
    OnFire,
    OnHit,
}

public enum ModulatorEffectType
{
    None = 0,
    DamageMultiplier,
    HealthMultiplier,
    CooldownMultiplier,
    AreaMultiplier,
    RangeMultiplier,
    ProjectileSpeedMultiplier,
    DurationMultiplier,
    CritChanceAdd,
    CritDamageMultiplier,
    DodgeChanceAdd,
    LifeStealAdd,
    HealthRegenAdd,
    MovementSpeedMultiplier,
    RicochetAdd,
    BounceTimesAdd,
    ProjectileCountAdd,
    PierceAdd,
    KnockbackMultiplier,
    HomingStrengthMultiplier,
    ChainCountAdd,
    TickRateMultiplier,
    PullStrengthMultiplier,
    CloneOrEchoPowerMultiplier,
    NewOrbitRadiusScalar,
    NewSawReturn,
    NewSecondPulse,
    NewTargetLockBias,
    NewOnCritChainProc,
    NewWeaponTransform,
    NewCapstoneRule,
    NewCapstoneProc,
    EchoAfterDelay,
    ChainToNearby,
    LingeringZone,
    ApplyMark,
    ExecuteLowHealth,
    EliteBossHunter,
    LuckAdd,
}

public sealed class WeaponModulatorEffectData
{
    public ModulatorEffectType Type { get; set; } = ModulatorEffectType.None;
    public ModulatorEffectTrigger Trigger { get; set; } = ModulatorEffectTrigger.Passive;
    public float Magnitude { get; set; }
    public float MagnitudeB { get; set; }
    public float MagnitudeC { get; set; }
    public int Count { get; set; }
    public int CountB { get; set; }
    // Fractional bonus channel. Raw catalog topology remains in Count.
    public float? CountValue { get; set; }
    // Resolution metadata, never a saved baseline strength.
    public float QualityStrength { get; set; } = 1f;
    public float ResolvedCountValue => CountValue ?? (Count != 0 ? Count : Magnitude);
    public string Key { get; set; } = string.Empty;

    public bool IsPassive => Trigger == ModulatorEffectTrigger.Passive;

    public static WeaponModulatorEffectData FromSubstatRoll(SubstatRoll roll)
    {
        return roll.Type switch
        {
            SubstatType.Luck => new WeaponModulatorEffectData { Type = ModulatorEffectType.LuckAdd, Magnitude = roll.Value },
            SubstatType.DamagePercent => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f + roll.Value / 100f,
            },
            SubstatType.HealthPercent => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.HealthMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f + roll.Value / 100f,
            },
            SubstatType.DodgeChance => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DodgeChanceAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = roll.Value,
            },
            SubstatType.LifeStealPercent => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.LifeStealAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = roll.Value,
            },
            SubstatType.HealthRegen => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.HealthRegenAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = roll.Value,
            },
            SubstatType.CritDamage => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CritDamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f + roll.Value / 100f,
            },
            SubstatType.CritChance => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CritChanceAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = roll.Value,
            },
            SubstatType.MovementSpeed => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.MovementSpeedMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f + roll.Value / 100f,
            },
            SubstatType.Range => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.RangeMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f + roll.Value / 100f,
            },
            SubstatType.Size => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.AreaMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f + roll.Value / 100f,
            },
            SubstatType.AttackSpeed => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CooldownMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1f / Math.Max(0.01f, 1f + roll.Value / 100f),
            },
            SubstatType.Ricochet => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.RicochetAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Count = Math.Max(0, (int)Math.Round(roll.Value)),
                CountValue = roll.Value,
            },
            SubstatType.BounceTimes => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.BounceTimesAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Count = Math.Max(0, (int)Math.Round(roll.Value)),
                CountValue = roll.Value,
            },
            SubstatType.ProjectileCount => new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ProjectileCountAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Count = Math.Max(0, (int)Math.Round(roll.Value)),
                CountValue = roll.Value,
            },
            _ => new WeaponModulatorEffectData(),
        };
    }

    public bool TryConvertToSubstatRoll(out SubstatRoll roll)
    {
        roll = Type switch
        {
            ModulatorEffectType.DamageMultiplier => new SubstatRoll(SubstatType.DamagePercent, (Magnitude - 1f) * 100f),
            ModulatorEffectType.HealthMultiplier => new SubstatRoll(SubstatType.HealthPercent, (Magnitude - 1f) * 100f),
            ModulatorEffectType.DodgeChanceAdd => new SubstatRoll(SubstatType.DodgeChance, Magnitude),
            ModulatorEffectType.LifeStealAdd => new SubstatRoll(SubstatType.LifeStealPercent, Magnitude),
            ModulatorEffectType.HealthRegenAdd => new SubstatRoll(SubstatType.HealthRegen, Magnitude),
            ModulatorEffectType.CritDamageMultiplier => new SubstatRoll(SubstatType.CritDamage, (Magnitude - 1f) * 100f),
            ModulatorEffectType.CritChanceAdd => new SubstatRoll(SubstatType.CritChance, Magnitude),
            ModulatorEffectType.MovementSpeedMultiplier => new SubstatRoll(SubstatType.MovementSpeed, (Magnitude - 1f) * 100f),
            ModulatorEffectType.RangeMultiplier => new SubstatRoll(SubstatType.Range, (Magnitude - 1f) * 100f),
            ModulatorEffectType.AreaMultiplier => new SubstatRoll(SubstatType.Size, (Magnitude - 1f) * 100f),
            ModulatorEffectType.CooldownMultiplier => new SubstatRoll(SubstatType.AttackSpeed, ((1f / Math.Max(0.01f, Magnitude)) - 1f) * 100f),
            ModulatorEffectType.LuckAdd => new SubstatRoll(SubstatType.Luck, Magnitude),
            ModulatorEffectType.RicochetAdd => new SubstatRoll(SubstatType.Ricochet, ResolvedCountValue),
            ModulatorEffectType.BounceTimesAdd => new SubstatRoll(SubstatType.BounceTimes, ResolvedCountValue),
            ModulatorEffectType.ProjectileCountAdd => new SubstatRoll(SubstatType.ProjectileCount, ResolvedCountValue),
            _ => new SubstatRoll(SubstatType.None, 0f),
        };

        return roll.Type != SubstatType.None;
    }

    public static List<WeaponModulatorEffectData> FromSubstats(IReadOnlyList<SubstatRoll>? substats)
    {
        List<WeaponModulatorEffectData> effects = new();
        if (substats == null)
        {
            return effects;
        }

        for (int i = 0; i < substats.Count; i++)
        {
            WeaponModulatorEffectData effect = FromSubstatRoll(substats[i]);
            if (effect.Type != ModulatorEffectType.None)
            {
                effects.Add(effect);
            }
        }

        return effects;
    }
}
