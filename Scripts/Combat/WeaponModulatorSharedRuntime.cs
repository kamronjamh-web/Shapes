using System;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Combat;

public static class WeaponModulatorSharedRuntime
{
    public readonly record struct OnCritChainProcConfig(float DamageScale, float Range, int ArcCount, float CooldownSeconds);

    public static void ForEachResolvedEffect(
        EquipmentItem? sourceItem,
        ModulatorEffectTrigger trigger,
        Action<WeaponModulatorEffectData> effectVisitor)
    {
        if (sourceItem?.Modulators == null || sourceItem.Modulators.Count == 0)
        {
            return;
        }

        for (int modulatorIndex = 0; modulatorIndex < sourceItem.Modulators.Count; modulatorIndex++)
        {
            WeaponModulatorData modulator = sourceItem.Modulators[modulatorIndex];
            if (modulator == null)
            {
                continue;
            }

            var effects = modulator.GetResolvedEffects();
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                WeaponModulatorEffectData effect = effects[effectIndex];
                if (effect == null || effect.Trigger != trigger || effect.Type == ModulatorEffectType.None)
                {
                    continue;
                }

                effectVisitor(effect);
            }
        }
    }

    public static float GetWeaponItemRangeMultiplier(EquipmentItem item)
    {
        return Mathf.Clamp(
            RuntimeMath.FiniteOr(1f + StatGenerator.GetTotalSubstatValue(item, SubstatType.Range) / 100f, 1f),
            0.1f,
            12f);
    }

    public static float GetWeaponItemCritChancePercent(EquipmentItem item)
    {
        return Mathf.Clamp(StatGenerator.GetTotalSubstatValue(item, SubstatType.CritChance), 0f, 100f);
    }

    public static float GetWeaponItemCritDamagePercent(EquipmentItem item)
    {
        return Mathf.Max(0f, StatGenerator.GetTotalSubstatValue(item, SubstatType.CritDamage));
    }

    public static int GetWeaponItemProjectileCount(EquipmentItem item)
    {
        return Mathf.Max(0, Mathf.RoundToInt(StatGenerator.GetTotalSubstatValue(item, SubstatType.ProjectileCount)));
    }

    public static float GetWeaponItemProjectileSpeedMultiplier(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.ProjectileSpeedMultiplier);
    }

    public static float GetWeaponItemDurationMultiplier(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.DurationMultiplier);
    }

    public static float GetWeaponItemHomingStrengthMultiplier(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.HomingStrengthMultiplier);
    }

    public static float GetWeaponItemKnockbackMultiplier(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.KnockbackMultiplier);
    }

    public static float GetWeaponItemPullStrengthMultiplier(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.PullStrengthMultiplier);
    }

    public static float GetWeaponItemCloneOrEchoPowerMultiplier(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.CloneOrEchoPowerMultiplier);
    }

    public static float GetWeaponItemOrbitRadiusScalar(EquipmentItem item)
    {
        return GetWeaponItemPassiveMultiplier(item, ModulatorEffectType.NewOrbitRadiusScalar);
    }

    public static int GetWeaponItemChainCountBonus(EquipmentItem item)
    {
        return GetWeaponItemPassiveCount(item, ModulatorEffectType.ChainCountAdd);
    }

    public static bool HasWeaponItemPassiveFlag(EquipmentItem item, ModulatorEffectType type)
    {
        bool hasFlag = false;
        ForEachResolvedEffect(item, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type != type)
            {
                return;
            }

            if (effect.Count > 0 || effect.Magnitude > 0f || effect.MagnitudeB > 0f || effect.MagnitudeC > 0f || !string.IsNullOrWhiteSpace(effect.Key))
            {
                hasFlag = true;
            }
        });

        return hasFlag;
    }

    public static float GetWeaponItemPassiveMaxMagnitude(EquipmentItem item, ModulatorEffectType type, float fallback)
    {
        float best = fallback;
        ForEachResolvedEffect(item, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == type)
            {
                best = Mathf.Max(best, effect.Magnitude);
            }
        });

        return best;
    }

    public static int GetWeaponItemPierceCount(EquipmentItem item)
    {
        return GetWeaponItemPassiveCount(item, ModulatorEffectType.PierceAdd);
    }

    public static float GetWeaponItemPassiveMultiplier(EquipmentItem item, ModulatorEffectType type)
    {
        float multiplier = 1f;
        float apexMultiplier = GetApexDoctrineMultiplier(item);
        ForEachResolvedEffect(item, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == type && effect.Magnitude > 0f)
            {
                float scaledMagnitude = effect.Magnitude;
                if (apexMultiplier > 1f && IsApexAffectableEffect(effect.Type))
                {
                    scaledMagnitude = 1f + (effect.Magnitude - 1f) * apexMultiplier;
                }

                multiplier *= scaledMagnitude;
            }
        });

        return Mathf.Clamp(RuntimeMath.FiniteOr(multiplier, 1f), 0.1f, 12f);
    }

    public static int GetWeaponItemPassiveCount(EquipmentItem item, ModulatorEffectType type)
    {
        int total = 0;
        float apexMultiplier = GetApexDoctrineMultiplier(item);
        ForEachResolvedEffect(item, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == type)
            {
                int baseCount = effect.Count > 0 ? effect.Count : Mathf.RoundToInt(effect.Magnitude);
                if (baseCount > 0 && apexMultiplier > 1f && IsApexAffectableEffect(effect.Type))
                {
                    baseCount = Mathf.Max(baseCount, Mathf.RoundToInt(baseCount * apexMultiplier));
                }

                total += baseCount;
            }
        });

        return Mathf.Max(0, total);
    }

    public static bool IsOnFireEchoEffect(WeaponModulatorEffectData effect)
    {
        return effect.Trigger == ModulatorEffectTrigger.OnFire
            && effect.Type == ModulatorEffectType.EchoAfterDelay;
    }

    public static bool IsOnHitApplyMark(WeaponModulatorEffectData effect)
    {
        return effect.Trigger == ModulatorEffectTrigger.OnHit
            && effect.Type == ModulatorEffectType.ApplyMark;
    }

    public static bool IsOnHitChainToNearby(WeaponModulatorEffectData effect)
    {
        return effect.Trigger == ModulatorEffectTrigger.OnHit
            && effect.Type == ModulatorEffectType.ChainToNearby;
    }

    public static bool IsOnHitLingeringZone(WeaponModulatorEffectData effect)
    {
        return effect.Trigger == ModulatorEffectTrigger.OnHit
            && effect.Type == ModulatorEffectType.LingeringZone;
    }

    public static bool IsOnHitExecuteLowHealth(WeaponModulatorEffectData effect)
    {
        return effect.Trigger == ModulatorEffectTrigger.OnHit
            && effect.Type == ModulatorEffectType.ExecuteLowHealth;
    }

    public static bool IsOnHitEliteBossHunter(WeaponModulatorEffectData effect)
    {
        return effect.Trigger == ModulatorEffectTrigger.OnHit
            && effect.Type == ModulatorEffectType.EliteBossHunter;
    }

    public static float ResolveEchoDelaySeconds(WeaponModulatorEffectData effect)
    {
        return effect.Magnitude > 0f ? effect.Magnitude : 0.45f;
    }

    public static float ResolveEchoDamageScale(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeB > 0f ? effect.MagnitudeB : 0.55f;
    }

    public static int ResolveEchoCount(WeaponModulatorEffectData effect)
    {
        return Mathf.Max(1, effect.Count);
    }

    public static float ResolveMarkDamageMultiplier(WeaponModulatorEffectData effect)
    {
        return effect.Magnitude > 1f ? effect.Magnitude : 1.18f;
    }

    public static float ResolveMarkDurationSeconds(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeB > 0f ? effect.MagnitudeB : 4f;
    }

    public static float ResolveExecuteThreshold(WeaponModulatorEffectData effect)
    {
        return effect.Magnitude > 0f ? effect.Magnitude : 0.15f;
    }

    public static float ResolveExecuteDamageMultiplier(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeB > 0f ? effect.MagnitudeB : 2f;
    }

    public static float ResolveExecuteBossCapMultiplier(WeaponModulatorEffectData effect, float executeDamageMultiplier)
    {
        return effect.MagnitudeC > 0f ? effect.MagnitudeC : executeDamageMultiplier;
    }

    public static float ResolveEliteBossHunterEliteMultiplier(WeaponModulatorEffectData effect)
    {
        return effect.Magnitude > 0f ? effect.Magnitude : 1.35f;
    }

    public static float ResolveEliteBossHunterNormalMultiplier(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeB > 0f ? effect.MagnitudeB : 1f;
    }

    public static int ResolveChainCount(WeaponModulatorEffectData effect)
    {
        return Mathf.Max(1, effect.Count);
    }

    public static float ResolveChainRange(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeB > 0f ? effect.MagnitudeB : 18f;
    }

    public static float ResolveChainDamageScale(WeaponModulatorEffectData effect)
    {
        return effect.Magnitude > 0f ? effect.Magnitude : 0.45f;
    }

    public static float ResolveLingeringZoneDamageScale(WeaponModulatorEffectData effect)
    {
        return effect.Magnitude > 0f ? effect.Magnitude : 0.35f;
    }

    public static float ResolveLingeringZoneRadius(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeB > 0f ? effect.MagnitudeB : 2.4f;
    }

    public static float ResolveLingeringZoneDurationSeconds(WeaponModulatorEffectData effect)
    {
        return effect.MagnitudeC > 0f ? effect.MagnitudeC : 2.8f;
    }

    public static bool HasOnCritChainProc(EquipmentItem sourceItem)
    {
        return TryGetOnCritChainProc(sourceItem, out _);
    }

    public static bool TryGetOnCritChainProc(EquipmentItem sourceItem, out OnCritChainProcConfig config)
    {
        float damageScale = 0.35f;
        float range = 10f;
        int arcCount = 1;
        float cooldown = 0.35f;
        bool found = false;

        ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == ModulatorEffectType.NewOnCritChainProc)
            {
                found = true;
                if (effect.Magnitude > 0f)
                {
                    damageScale = effect.Magnitude;
                }

                if (effect.MagnitudeB > 0f)
                {
                    range = effect.MagnitudeB;
                }

                if (effect.Count > 0)
                {
                    arcCount = effect.Count;
                }

                if (effect.MagnitudeC > 0f)
                {
                    cooldown = effect.MagnitudeC;
                }
            }
        });

        config = new OnCritChainProcConfig(
            Mathf.Clamp(RuntimeMath.FiniteOr(damageScale, 0.35f), 0.01f, 12f),
            Mathf.Clamp(RuntimeMath.FiniteOr(range, 10f), 1f, 120f),
            Mathf.Clamp(arcCount, 1, 6),
            Mathf.Clamp(RuntimeMath.FiniteOr(cooldown, 0.35f), 0.05f, 10f));
        return found;
    }

    public static bool HasWeaponTransform(EquipmentItem sourceItem)
    {
        bool found = false;
        ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == ModulatorEffectType.NewWeaponTransform)
            {
                found = true;
            }
        });

        return found;
    }

    private static bool IsApexAffectableEffect(ModulatorEffectType type)
    {
        return type switch
        {
            ModulatorEffectType.None => false,
            ModulatorEffectType.NewCapstoneRule => false,
            ModulatorEffectType.NewCapstoneProc => false,
            ModulatorEffectType.NewWeaponTransform => false,
            _ => true,
        };
    }

    public static bool ShouldApexDoctrineScaleEffect(WeaponModulatorEffectData effect) => IsApexAffectableEffect(effect.Type);

    public static float GetApexDoctrineMultiplier(EquipmentItem sourceItem)
    {
        float multiplier = 1f;
        bool hasApexDoctrine = false;
        int tier3Count = 0;

        if (sourceItem?.Modulators != null)
        {
            for (int modulatorIndex = 0; modulatorIndex < sourceItem.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData? modulator = sourceItem.Modulators[modulatorIndex];
                if (modulator != null && modulator.PowerTier == ModulatorPowerTier.Tier3)
                {
                    tier3Count++;
                }
            }
        }

        ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == ModulatorEffectType.NewCapstoneRule)
            {
                hasApexDoctrine = true;
                multiplier = Mathf.Max(multiplier, effect.Magnitude > 0f ? effect.Magnitude : 1.35f);
            }
        });

        // Apex Doctrine only activates when it is the sole Tier3 commitment on the weapon.
        if (!hasApexDoctrine || tier3Count != 1)
        {
            return 1f;
        }

        return Mathf.Clamp(RuntimeMath.FiniteOr(multiplier, 1f), 0.1f, 12f);
    }

    public readonly record struct OmniFeedbackConfig(float DamageScale, float SearchRange, int HitCadence);

    public static bool TryGetOmniFeedbackConfig(EquipmentItem sourceItem, out OmniFeedbackConfig config)
    {
        float damageScale = 1f;
        float range = 12f;
        int cadence = 8;
        bool found = false;

        ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type != ModulatorEffectType.NewCapstoneProc)
            {
                return;
            }

            found = true;
            if (effect.Magnitude > 0f)
            {
                damageScale = effect.Magnitude;
            }

            if (effect.MagnitudeB > 0f)
            {
                range = effect.MagnitudeB;
            }

            if (effect.Count > 0)
            {
                cadence = effect.Count;
            }
        });

        config = new OmniFeedbackConfig(
            Mathf.Clamp(RuntimeMath.FiniteOr(damageScale, 1f), 0.01f, 12f),
            Mathf.Clamp(RuntimeMath.FiniteOr(range, 12f), 1f, 120f),
            Mathf.Clamp(cadence, 1, 64));
        return found;
    }
}
