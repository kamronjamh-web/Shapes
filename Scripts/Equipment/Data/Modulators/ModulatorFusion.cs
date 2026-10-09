using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperGatchaRebuild.Equipment.Data;

/// <summary>Fuses loose parts only. Preflight and cloning finish before coins or inputs change.</summary>
public static class ModulatorFusion
{
    public static int GetCost(WeaponModulatorData part, int baseCost = 20)
        => Math.Clamp(baseCost, 1, 500) * (Math.Clamp((int)part.PowerTier, 0, 2) + 1) * (Math.Clamp((int)part.Tier, 0, 3) + 1);

    public static bool CanFuse(IReadOnlyList<WeaponModulatorData> inventory, WeaponModulatorData part,
        int coins, int baseCost, out string reason)
    {
        if (part.IsRetired) { reason = "Retired parts are held for a future replacement."; return false; }
        if ((int)part.Tier < 0 || part.Tier >= WeaponModulatorTier.Flawless)
        { reason = "Flawless is the highest quality."; return false; }
        if (!inventory.Any(candidate => ReferenceEquals(candidate, part)))
        { reason = "This part is no longer in storage."; return false; }
        if (FindPartner(inventory, part) < 0)
        { reason = "Requires two matching loose parts of the same quality. Return installed parts to storage first."; return false; }
        int cost = GetCost(part, baseCost);
        if (coins < cost) { reason = $"Requires {cost} coins."; return false; }
        reason = string.Empty;
        return true;
    }

    public static bool TryFuse(List<WeaponModulatorData> inventory, WeaponModulatorData part, ref int coins,
        int baseCost, out WeaponModulatorData? upgraded, out string reason)
    {
        upgraded = null;
        if (!CanFuse(inventory, part, coins, baseCost, out reason)) return false;
        int first = inventory.IndexOf(part), second = FindPartner(inventory, part);
        WeaponModulatorData result = ModulatorCatalog.Clone(part, part.Tier + 1);
        int cost = GetCost(part, baseCost);
        inventory[first] = result;
        inventory.RemoveAt(second);
        coins -= cost;
        upgraded = result;
        return true;
    }

    private static int FindPartner(IReadOnlyList<WeaponModulatorData> inventory, WeaponModulatorData part)
    {
        for (int i = 0; i < inventory.Count; i++)
            if (!ReferenceEquals(inventory[i], part) && SameRecipe(part, inventory[i])) return i;
        return -1;
    }

    public static bool SameRecipe(WeaponModulatorData a, WeaponModulatorData b)
    {
        if (a.IsRetired || b.IsRetired || a.NormalizedKey != b.NormalizedKey || a.Tier != b.Tier ||
            a.PowerTier != b.PowerTier || a.Category != b.Category || a.Effects.Count != b.Effects.Count ||
            a.Substats.Count != b.Substats.Count || !a.AllowedWeaponTags.SequenceEqual(b.AllowedWeaponTags) ||
            !a.BlockedWeaponTags.SequenceEqual(b.BlockedWeaponTags)) return false;
        for (int i = 0; i < a.Effects.Count; i++)
        {
            WeaponModulatorEffectData x = a.Effects[i], y = b.Effects[i];
            if (x.Type != y.Type || x.Trigger != y.Trigger || x.Key != y.Key || x.Magnitude != y.Magnitude ||
                x.MagnitudeB != y.MagnitudeB || x.MagnitudeC != y.MagnitudeC || x.Count != y.Count || x.CountB != y.CountB || x.CountValue != y.CountValue)
                return false;
        }
        for (int i = 0; i < a.Substats.Count; i++)
            if (a.Substats[i].Type != b.Substats[i].Type || a.Substats[i].Value != b.Substats[i].Value) return false;
        return true;
    }
}
