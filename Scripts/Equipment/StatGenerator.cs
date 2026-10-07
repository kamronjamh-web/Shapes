using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Equipment;

public sealed class StatGenerator
{
    private readonly RandomNumberGenerator _rng;

    private static readonly SubstatType[] AllSubstatTypes =
    {
        SubstatType.DamagePercent,
        SubstatType.HealthPercent,
        SubstatType.DodgeChance,
        SubstatType.LifeStealPercent,
        SubstatType.HealthRegen,
        SubstatType.CritDamage,
        SubstatType.CritChance,
        SubstatType.MovementSpeed,
        SubstatType.Range,
        SubstatType.Luck,
        SubstatType.Size,
        SubstatType.AttackSpeed,
        SubstatType.Ricochet,
        SubstatType.BounceTimes,
        SubstatType.ProjectileCount,
    };

    private static readonly string[] UnstableEffectPool =
    {
        "stat_surge",
        "chaos_crit",
        "stat_swap",
        "volatile",
        "duplicate",
        "mirror",
    };

    public StatGenerator(RandomNumberGenerator rng)
    {
        _rng = rng;
    }

    public EquipmentItem BuildItem(ShapeData shape, RarityData rarity, ModifierData? essence, int level)
    {
        int clampedLevel = ClampWeaponLevel(level);

        EquipmentItem item = new()
        {
            Shape = shape,
            Rarity = rarity,
            Essence = SanitizeEssence(rarity, clampedLevel, essence),
            Level = clampedLevel,
            Stars = Math.Max(1, shape.Stars),
            Modulators = new List<WeaponModulatorData>(),
        };

        item.Health = CalculateHealth(item.Level, shape, rarity, item.Essence);
        item.Damage = CalculateDamage(item.Level, shape, rarity, item.Essence);
        item.Substats = RollSubstats(rarity, item.Essence, out bool mirrorMainStats);

        if (mirrorMainStats)
        {
            int originalHealth = item.Health;
            item.Health = item.Damage;
            item.Damage = originalHealth;
        }

        return item;
    }

    public int CalculateHealth(int level, RarityData rarity, ModifierData? essence)
    {
        return CalculateHealth(level, null, rarity, essence);
    }

    public int CalculateHealth(int level, ShapeData? shape, RarityData rarity, ModifierData? essence)
    {
        return CalculateHealthValue(level, shape, rarity, essence);
    }

    public int CalculateDamage(int level, RarityData rarity, ModifierData? essence)
    {
        return CalculateDamage(level, null, rarity, essence);
    }

    public int CalculateDamage(int level, ShapeData? shape, RarityData rarity, ModifierData? essence)
    {
        return CalculateDamageValue(level, shape, rarity, essence);
    }

    public List<SubstatRoll> RollSubstats(RarityData rarity, ModifierData? essence)
    {
        return RollSubstats(rarity, essence, out _);
    }

    public static int ClampWeaponLevel(int level)
    {
        return Mathf.Clamp(level, 1, EquipmentCatalogDefaults.MaxWeaponLevel);
    }

    public static void RecalculateDerivedStats(EquipmentItem item)
    {
        item.Substats ??= new List<SubstatRoll>();
        item.Modulators ??= new List<WeaponModulatorData>();
        item.Level = ClampWeaponLevel(item.Level);

        if (item.Shape == null || item.Rarity == null)
        {
            item.Essence = null;
            item.Modulators.Clear();
            item.Health = Math.Max(0, item.Health);
            item.Damage = Math.Max(0, item.Damage);
            return;
        }

        item.Essence = SanitizeEssence(item.Rarity, item.Level, item.Essence);

        int unlockedSlots = EquipmentCatalogDefaults.GetUnlockedModulatorSlots(item.Rarity.Key, item.Level);
        if (item.Modulators.Count > unlockedSlots)
        {
            item.Modulators.RemoveRange(unlockedSlots, item.Modulators.Count - unlockedSlots);
        }

        item.Health = CalculateHealthValue(item.Level, item.Shape, item.Rarity, item.Essence);
        item.Damage = CalculateDamageValue(item.Level, item.Shape, item.Rarity, item.Essence);
    }

    public static int GetEffectiveHealth(EquipmentItem item)
    {
        float multiplier = Mathf.Max(0.01f, 1f + GetTotalSubstatValue(item, SubstatType.HealthPercent) / 100f);
        float health = Mathf.Max(0f, item.Health) * multiplier;
        return Mathf.Max(1, Mathf.FloorToInt(health));
    }

    public static int GetEffectiveDamage(EquipmentItem item)
    {
        float multiplier = Mathf.Max(0.01f, 1f + GetTotalSubstatValue(item, SubstatType.DamagePercent) / 100f);
        float damage = Mathf.Max(0f, item.Damage) * multiplier;
        return Mathf.Max(0, Mathf.FloorToInt(damage));
    }

    public static float GetTotalSubstatValue(EquipmentItem item, SubstatType type)
    {
        float total = 0f;

        if (item.Substats != null)
        {
            for (int substatIndex = 0; substatIndex < item.Substats.Count; substatIndex++)
            {
                if (item.Substats[substatIndex].Type == type)
                {
                    total += item.Substats[substatIndex].Value;
                }
            }
        }

        if (item.Modulators != null)
        {
            float apexScale = WeaponModulatorSharedRuntime.GetApexDoctrineMultiplier(item);
            for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData? modulator = item.Modulators[modulatorIndex];
                if (modulator == null)
                {
                    continue;
                }

                IReadOnlyList<WeaponModulatorEffectData> effects = modulator.GetResolvedEffects();
                for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                {
                    WeaponModulatorEffectData effect = effects[effectIndex];
                    if (effect == null || effect.Trigger != ModulatorEffectTrigger.Passive || !effect.TryConvertToSubstatRoll(out SubstatRoll roll))
                    {
                        continue;
                    }

                    if (roll.Type == type)
                    {
                        total += roll.Value * (WeaponModulatorSharedRuntime.ShouldApexDoctrineScaleEffect(effect) ? apexScale : 1f);
                    }
                }
            }
        }

        return total;
    }

    public static bool TryGetSubstatRange(SubstatType type, RarityData? rarity, out float min, out float max)
    {
        min = 0f;
        max = 0f;

        if (type == SubstatType.None)
        {
            return false;
        }

        string rarityKey = EquipmentCatalogDefaults.NormalizeRarityKey(rarity?.Key);
        float cap = EquipmentCatalogDefaults.GetSubstatCap(rarityKey, type, -1f);
        if (cap <= 0f)
        {
            float fallback = EquipmentCatalogDefaults.GetSubstatCap("common", type, 1f);
            float rarityScale = rarity?.StatMultiplier ?? 1f;
            cap = fallback * Mathf.Max(0.01f, rarityScale);
        }

        max = RoundSubstatValue(type, cap);
        min = RoundSubstatValue(type, cap * 0.5f);
        return true;
    }

    private List<SubstatRoll> RollSubstats(RarityData rarity, ModifierData? essence, out bool mirrorMainStats)
    {
        mirrorMainStats = false;

        string rarityKey = EquipmentCatalogDefaults.NormalizeRarityKey(rarity.Key);
        ModifierKey essenceKey = essence?.Key ?? ModifierKey.Normal;

        int substatCount = Math.Max(1, rarity.SubstatCount);
        if ((essence?.GrantsAllSubstats ?? false) || essenceKey == ModifierKey.Prismatic)
        {
            substatCount = AllSubstatTypes.Length;
        }

        List<SubstatRoll> result = new();
        HashSet<SubstatType> used = new();

        if (essenceKey == ModifierKey.Electric)
        {
            AddGuaranteedSubstat(result, used, rarityKey, SubstatType.DamagePercent);
            substatCount--;
        }

        if (essenceKey == ModifierKey.Shielded)
        {
            AddGuaranteedSubstat(result, used, rarityKey, SubstatType.DodgeChance);
            substatCount--;
        }

        for (int i = 0; i < substatCount; i++)
        {
            SubstatType picked = PickUniqueSubstatType(used);
            if (picked == SubstatType.None)
            {
                break;
            }

            used.Add(picked);
            result.Add(RollOneSubstat(picked, rarityKey));
        }

        ApplyModifierBonuses(result, essenceKey);

        if (essenceKey == ModifierKey.Unstable && result.Count > 0)
        {
            ApplyUnstableEffects(result, out mirrorMainStats);
        }

        return result;
    }

    private void AddGuaranteedSubstat(
        List<SubstatRoll> result,
        HashSet<SubstatType> used,
        string rarityKey,
        SubstatType substatType)
    {
        if (substatType == SubstatType.None || used.Contains(substatType))
        {
            return;
        }

        used.Add(substatType);
        result.Add(RollOneSubstat(substatType, rarityKey));
    }

    private SubstatType PickUniqueSubstatType(HashSet<SubstatType> used)
    {
        List<SubstatType> candidates = new(AllSubstatTypes.Length);
        for (int i = 0; i < AllSubstatTypes.Length; i++)
        {
            SubstatType type = AllSubstatTypes[i];
            if (!used.Contains(type))
            {
                candidates.Add(type);
            }
        }

        if (candidates.Count == 0)
        {
            return SubstatType.None;
        }

        int idx = _rng.RandiRange(0, candidates.Count - 1);
        return candidates[idx];
    }

    private SubstatRoll RollOneSubstat(SubstatType type, string rarityKey)
    {
        float maxValue = EquipmentCatalogDefaults.GetSubstatCap(rarityKey, type, 1f);
        float minValue = maxValue * 0.5f;
        float value = _rng.RandfRange(minValue, maxValue);
        value = RoundSubstatValue(type, value);

        return new SubstatRoll(type, value);
    }

    private void ApplyModifierBonuses(List<SubstatRoll> substats, ModifierKey modifierKey)
    {
        float bonusMultiplier = modifierKey switch
        {
            ModifierKey.Shiny => 0.25f,
            ModifierKey.Dark => 0.5f,
            ModifierKey.Prismatic => 0.5f,
            _ => 0f,
        };

        if (bonusMultiplier <= 0f)
        {
            return;
        }

        for (int i = 0; i < substats.Count; i++)
        {
            SubstatRoll substat = substats[i];
            substat.Value = RoundSubstatValue(substat.Type, substat.Value * (1f + bonusMultiplier));
        }
    }

    private void ApplyUnstableEffects(List<SubstatRoll> substats, out bool mirrorMainStats)
    {
        mirrorMainStats = false;

        List<string> pool = new(UnstableEffectPool);
        int effectCount = _rng.RandiRange(1, 3);

        for (int i = 0; i < effectCount && pool.Count > 0; i++)
        {
            int idx = _rng.RandiRange(0, pool.Count - 1);
            string effect = pool[idx];
            pool.RemoveAt(idx);

            switch (effect)
            {
                case "stat_surge":
                    ApplyUnstableStatSurge(substats);
                    break;
                case "chaos_crit":
                    ApplyUnstableChaosCrit(substats);
                    break;
                case "stat_swap":
                    ApplyUnstableStatSwap(substats);
                    break;
                case "duplicate":
                    ApplyUnstableDuplicate(substats);
                    break;
                case "mirror":
                    mirrorMainStats = true;
                    break;
                case "volatile":
                default:
                    break;
            }
        }
    }

    private void ApplyUnstableStatSurge(List<SubstatRoll> substats)
    {
        if (substats.Count == 0)
        {
            return;
        }

        int idx = _rng.RandiRange(0, substats.Count - 1);
        SubstatRoll picked = substats[idx];
        float boost = _rng.RandfRange(0.25f, 0.75f);
        picked.Value = RoundSubstatValue(picked.Type, picked.Value * (1f + boost));
    }

    private void ApplyUnstableChaosCrit(List<SubstatRoll> substats)
    {
        float bonusCrit = _rng.RandfRange(15f, 35f);

        for (int i = 0; i < substats.Count; i++)
        {
            if (substats[i].Type != SubstatType.CritChance)
            {
                continue;
            }

            substats[i].Value = RoundSubstatValue(SubstatType.CritChance, substats[i].Value + bonusCrit);
            return;
        }

        substats.Add(new SubstatRoll(SubstatType.CritChance, RoundSubstatValue(SubstatType.CritChance, bonusCrit)));
    }

    private void ApplyUnstableStatSwap(List<SubstatRoll> substats)
    {
        if (substats.Count < 2)
        {
            return;
        }

        int idxA = _rng.RandiRange(0, substats.Count - 1);
        int idxB = _rng.RandiRange(0, substats.Count - 1);
        while (idxB == idxA)
        {
            idxB = _rng.RandiRange(0, substats.Count - 1);
        }

        float temp = substats[idxA].Value;
        substats[idxA].Value = RoundSubstatValue(substats[idxA].Type, substats[idxB].Value);
        substats[idxB].Value = RoundSubstatValue(substats[idxB].Type, temp);
    }

    private void ApplyUnstableDuplicate(List<SubstatRoll> substats)
    {
        if (substats.Count == 0)
        {
            return;
        }

        int idx = _rng.RandiRange(0, substats.Count - 1);
        SubstatRoll source = substats[idx];
        float duplicated = RoundSubstatValue(source.Type, source.Value * 0.5f);
        substats.Add(new SubstatRoll(source.Type, duplicated));
    }

    private static int CalculateHealthValue(int level, ShapeData? shape, RarityData rarity, ModifierData? essence)
    {
        _ = shape;

        int clampedLevel = ClampWeaponLevel(level);
        float baseHp = 100f + 12f * (clampedLevel - 1);
        float rarityScaledHp = Mathf.Floor(baseHp * rarity.StatMultiplier);
        float finalHp = Mathf.Floor(rarityScaledHp * Mathf.Max(0.01f, essence?.HpMultiplier ?? 1f));
        return Mathf.Max(1, Mathf.FloorToInt(finalHp));
    }

    private static int CalculateDamageValue(int level, ShapeData? shape, RarityData rarity, ModifierData? essence)
    {
        int clampedLevel = ClampWeaponLevel(level);
        float baseDamage = 20f + 2.5f * (clampedLevel - 1);
        float rarityScaledDamage = Mathf.Floor(baseDamage * rarity.StatMultiplier);
        float shapeHitMultiplier = shape != null ? Mathf.Max(0f, shape.DamagePerHit) / 100f : 1f;
        float finalDamage = Mathf.Floor(rarityScaledDamage * shapeHitMultiplier * Mathf.Max(0.01f, essence?.DamageMultiplier ?? 1f));
        return Mathf.Max(0, Mathf.FloorToInt(finalDamage));
    }

    private static ModifierData? SanitizeEssence(RarityData? rarity, int level, ModifierData? essence)
    {
        if (rarity == null || !EquipmentCatalogDefaults.HasEssenceSlot(rarity.Key, level) || essence == null)
        {
            return null;
        }

        return essence.Key == ModifierKey.Normal ? null : essence;
    }

    private static float RoundSubstatValue(SubstatType type, float value)
    {
        if (SubstatRoll.UsesFlatDisplay(type))
        {
            return Mathf.Round(value * 10f) / 10f;
        }

        return Mathf.Round(value * 100f) / 100f;
    }
}
