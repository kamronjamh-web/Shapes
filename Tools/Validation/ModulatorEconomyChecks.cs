using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Validation;

internal static class ModulatorEconomyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var boundaries = new[] { (0f, WeaponModulatorTier.Flawed), (.59999f, WeaponModulatorTier.Flawed),
            (.6f, WeaponModulatorTier.Baseline), (.89999f, WeaponModulatorTier.Baseline),
            (.9f, WeaponModulatorTier.Enhanced), (.98999f, WeaponModulatorTier.Enhanced),
            (.99f, WeaponModulatorTier.Flawless), (1f, WeaponModulatorTier.Flawless) };
        check(boundaries.All(pair => ModulatorLoot.RollQuality(pair.Item1) == pair.Item2),
            "Quality roll boundaries implement the approved Broken-heavy 60/30/9/1 distribution");
        List<WeaponModulatorData> pool = ModulatorCatalog.CreateAll();
        List<WeaponModulatorData> distorted = pool.Select(part => ModulatorCatalog.Clone(part)).ToList();
        foreach (WeaponModulatorData part in distorted)
            part.Weight *= part.PowerTier == ModulatorPowerTier.Tier1 ? .001f : part.PowerTier == ModulatorPowerTier.Tier2 ? 1000f : 50f;
        using RandomNumberGenerator first = new() { Seed = 9341 }, second = new() { Seed = 9341 };
        int[] tiers = new int[3], qualities = new int[4];
        for (int roll = 0; roll < 10000; roll++)
        {
            WeaponModulatorData actual = ModulatorLoot.Roll(pool, first) ?? throw new InvalidOperationException("Active catalog roll was empty");
            WeaponModulatorData scaled = ModulatorLoot.Roll(distorted, second) ?? throw new InvalidOperationException("Distorted catalog roll was empty");
            if (actual.IsRetired || actual.PowerTier != scaled.PowerTier || actual.Tier != scaled.Tier)
                throw new InvalidOperationException("Tier-first or independent quality rolling depends on summed part weights");
            tiers[(int)actual.PowerTier]++; qualities[(int)actual.Tier]++;
        }
        check(Math.Abs(tiers[0] - 7000) < 300 && Math.Abs(tiers[1] - 2500) < 250 && Math.Abs(tiers[2] - 500) < 150 &&
            Math.Abs(qualities[0] - 6000) < 300 && Math.Abs(qualities[1] - 3000) < 300 &&
            Math.Abs(qualities[2] - 900) < 180 && Math.Abs(qualities[3] - 100) < 60,
            "Seeded rolls honor tier-first 70/25/5 and independent quality weights despite large within-tier weight differences");
        check(ModulatorLoot.Roll(new[] { ModulatorCatalog.CreateByKey("apex_doctrine") }, first) == null &&
            ModulatorLoot.Roll(Array.Empty<WeaponModulatorData>(), first) == null,
            "Empty and retired-only drop pools produce no installable part");

        foreach (ModulatorPowerTier tier in Enum.GetValues<ModulatorPowerTier>())
        foreach (WeaponModulatorTier quality in new[] { WeaponModulatorTier.Flawed, WeaponModulatorTier.Baseline, WeaponModulatorTier.Enhanced })
        {
            WeaponModulatorData input = ModulatorCatalog.Clone(pool.First(part => part.PowerTier == tier && !part.IsRetired), quality);
            WeaponModulatorData partner = ModulatorCatalog.Clone(input), unrelated = ModulatorCatalog.CreateByKey("wing_man");
            List<WeaponModulatorData> inventory = new() { partner, unrelated, input };
            int coins = 1000, cost = 20 * ((int)tier + 1) * ((int)quality + 1);
            string baseline = System.Text.Json.JsonSerializer.Serialize(input.Effects);
            check(ModulatorFusion.GetCost(input) == cost && ModulatorFusion.TryFuse(inventory, input, ref coins, 20, out WeaponModulatorData? result, out string reason) &&
                result != null && result.Tier == quality + 1 && result.Key == input.Key && coins == 1000 - cost && inventory.Count == 2 &&
                inventory.Contains(unrelated) && inventory.Contains(result) && !inventory.Contains(input) && !inventory.Contains(partner) &&
                System.Text.Json.JsonSerializer.Serialize(result.Effects) == baseline && string.IsNullOrEmpty(reason),
                $"{tier}/{quality}: fusion consumes exactly two loose parts and one coin charge while preserving the raw baseline");
        }

        void Rejected(List<WeaponModulatorData> inventory, WeaponModulatorData input, int balance, string description)
        {
            WeaponModulatorData[] before = inventory.ToArray();
            int coins = balance;
            bool success = ModulatorFusion.TryFuse(inventory, input, ref coins, 20, out WeaponModulatorData? result, out string reason);
            check(!success && result == null && !string.IsNullOrWhiteSpace(reason) && coins == balance &&
                inventory.Count == before.Length && inventory.Zip(before).All(pair => ReferenceEquals(pair.First, pair.Second)),
                $"Rejected fusion preserves exact inventory references and coins: {description}");
        }
        WeaponModulatorData basic = ModulatorCatalog.CreateByKey("sharpened_form");
        Rejected(new() { basic, ModulatorCatalog.Clone(basic) }, basic, 39, "insufficient coins");
        Rejected(new() { basic, ModulatorCatalog.Clone(basic, WeaponModulatorTier.Enhanced) }, basic, 1000, "different qualities");
        Rejected(new() { basic, basic }, basic, 1000, "same object stored twice");
        Rejected(new() { ModulatorCatalog.Clone(basic), ModulatorCatalog.Clone(basic) }, basic, 1000, "stale selected reference");
        WeaponModulatorData maximum = ModulatorCatalog.Clone(basic, WeaponModulatorTier.Flawless);
        Rejected(new() { maximum, ModulatorCatalog.Clone(maximum) }, maximum, 1000, "highest quality");
        WeaponModulatorData retired = ModulatorCatalog.CreateByKey("apex_doctrine");
        Rejected(new() { retired, ModulatorCatalog.Clone(retired) }, retired, 1000, "archived key");
        WeaponModulatorData fractional = ModulatorCatalog.CreateByKey("extra_output");
        fractional.Effects[0].CountValue = .5f;
        WeaponModulatorData distinct = ModulatorCatalog.Clone(fractional);
        distinct.Effects[0].CountValue = .75f;
        Rejected(new() { fractional, distinct }, fractional, 1000, "different saved fractional raw payloads");
    }
}
