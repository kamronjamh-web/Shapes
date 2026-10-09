using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

/// <summary>Immutable roll weights; later skill bonuses can supply a modified copy per player.</summary>
public readonly record struct ModulatorLootProfile(float Tier1, float Tier2, float Tier3,
    float Broken, float Baseline, float Enhanced, float Flawless)
{
    public static ModulatorLootProfile Default => new(70, 25, 5, 60, 30, 9, 1);
}

public static class ModulatorLoot
{
    public static WeaponModulatorData? Roll(IReadOnlyList<WeaponModulatorData> pool, RandomNumberGenerator rng,
        ModulatorLootProfile? profile = null)
    {
        ModulatorLootProfile weights = profile ?? ModulatorLootProfile.Default;
        var eligible = pool.Where(part => !part.IsRetired && float.IsFinite(part.Weight) && part.Weight > 0).ToList();
        if (eligible.Count == 0) return null;
        var tiers = new List<ModulatorPowerTier>();
        var tierWeights = new List<float>();
        foreach (ModulatorPowerTier tier in Enum.GetValues<ModulatorPowerTier>())
        {
            float weight = tier switch { ModulatorPowerTier.Tier1 => weights.Tier1, ModulatorPowerTier.Tier2 => weights.Tier2, _ => weights.Tier3 };
            if (ValidWeight(weight) <= 0 || !eligible.Any(part => part.PowerTier == tier)) continue;
            tiers.Add(tier); tierWeights.Add(weight);
        }
        if (tiers.Count == 0) return null;
        ModulatorPowerTier selectedTier = tiers[PickIndex(tierWeights, rng.Randf())];
        var candidates = eligible.Where(part => part.PowerTier == selectedTier).ToList();
        int index = PickIndex(candidates.Select(part => part.Weight).ToArray(), rng.Randf());
        return ModulatorCatalog.Clone(candidates[index], RollQuality(rng.Randf(), weights));
    }

    public static WeaponModulatorTier RollQuality(float unitRoll, ModulatorLootProfile? profile = null)
    {
        ModulatorLootProfile weights = profile ?? ModulatorLootProfile.Default;
        float[] qualityWeights = { weights.Broken, weights.Baseline, weights.Enhanced, weights.Flawless };
        if (qualityWeights.All(weight => ValidWeight(weight) == 0)) return WeaponModulatorTier.Baseline;
        return (WeaponModulatorTier)PickIndex(qualityWeights, unitRoll);
    }

    private static int PickIndex(IReadOnlyList<float> weights, float unitRoll)
    {
        float total = 0;
        foreach (float weight in weights) total += ValidWeight(weight);
        float roll = Mathf.Clamp(float.IsFinite(unitRoll) ? unitRoll : 0, 0, 0.99999994f) * total;
        int last = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            float weight = ValidWeight(weights[i]);
            if (weight <= 0) continue;
            last = i;
            if (roll < weight) return i;
            roll -= weight;
        }
        return last;
    }

    private static float ValidWeight(float weight) => float.IsFinite(weight) ? Mathf.Clamp(weight, 0, 1000000) : 0;

    public static Color GetTierColor(ModulatorPowerTier tier) => tier switch
    {
        ModulatorPowerTier.Tier2 => new Color(0.35f, 0.70f, 1f),
        ModulatorPowerTier.Tier3 => new Color(0.80f, 0.48f, 1f),
        _ => new Color(0.78f, 0.82f, 0.86f),
    };
}
