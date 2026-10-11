using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    private readonly HashSet<int> _rewardedDefenseWaves = new();
    private string _lastWaveModulatorReward = string.Empty;

    private bool GrantDefenseWaveModulator()
    {
        if (!IsDefenseMap || _manualHordeTesting || _defeat || _currentWaveNumber <= 0 ||
            ActiveEnemyCount != 0 || _rewardedDefenseWaves.Contains(_currentWaveNumber)) return false;
        WeaponModulatorData? reward = ModulatorLoot.Roll(_defenseModulatorPool, _defenseRng);
        if (reward == null) return false;
        _rewardedDefenseWaves.Add(_currentWaveNumber);
        SpawnGroundModulator(reward);
        _lastWaveModulatorReward = $"{reward.DisplayName} on the ground near you";
        return true;
    }

    private void AddDefenseModulatorOffer(WeaponModulatorData part, bool purchased)
    {
        EquipmentItem? weapon = GetCurrentWeapon();
        Label heading = AddShopLabel($"{part.DisplayName}  /  {ModulatorQualityText.Header(part)}", 17);
        heading.AddThemeColorOverride("font_color", ModulatorLoot.GetTierColor(part.PowerTier));
        Label details = AddShopLabel(ModulatorQualityText.Describe(part), 14);
        details.Name = "ModulatorOfferDetails";
        AddShopLabel(ModulatorQualityText.Compare(part, weapon), 14);
        if (weapon != null && !WeaponModulatorCompatibility.CanApplyToWeapon(part, weapon, out string reason))
            AddShopLabel(reason, 14);

        bool Available() => (purchased ? _shopStock : _recoveredModulators).Contains(part);
        bool Affordable() => !purchased || _defenseCoins >= DefenseSettings!.ModulatorPrice;
        void Install()
        {
            if (!_shopOpen || !Available() || !CanInstallDefenseModulator(part) || !Affordable()) return;
            if (purchased && !TrySpendDefenseCoins(DefenseSettings!.ModulatorPrice)) return;
            (purchased ? _shopStock : _recoveredModulators).Remove(part);
            InstallDefenseModulator(part);
        }
        AddShopButton(purchased ? $"Install {part.DisplayName} / {DefenseSettings!.ModulatorPrice} coins" : $"Install {part.DisplayName} / from storage",
            () => _shopOpen && Available() && Affordable() && CanInstallDefenseModulator(part), Install);
        if (purchased)
            AddShopButton($"Buy for storage / {DefenseSettings!.ModulatorPrice} coins",
                () => _shopOpen && Available() && Affordable() && _recoveredModulators.Count < StoredModulatorCapacity, () =>
                {
                    if (!IsDefenseShopAvailable || !_shopOpen || !Available() || !Affordable() || _recoveredModulators.Count >= StoredModulatorCapacity) return;
                    if (!TrySpendDefenseCoins(DefenseSettings!.ModulatorPrice)) return;
                    if (!TryStoreRunModulator(part, out _)) { _defenseCoins += DefenseSettings!.ModulatorPrice; return; }
                    _shopStock.Remove(part); RebuildDefenseShopMenu();
                });
    }

    private void BuildDefenseModulatorWorkshop(EquipmentItem? weapon)
    {
        AddShopLabel("INSTALLED MODULATORS", 18);
        if (weapon != null)
            foreach (WeaponModulatorData part in weapon.Modulators)
                AddShopButton($"Return {part.DisplayName} to storage", () => weapon.Modulators.Contains(part) && _recoveredModulators.Count < StoredModulatorCapacity,
                    () => ReturnDefenseModulator(weapon, part));

        AddShopLabel("FUSE TWO MATCHING MODULATORS", 18);
        AddShopLabel("Two loose copies of the same effect and quality become one at the next quality. Rarity tier stays the same. Return installed copies to storage first.", 14);
        List<WeaponModulatorData> recipes = new();
        foreach (WeaponModulatorData part in _recoveredModulators)
        {
            if (part.IsRetired || part.Tier >= WeaponModulatorTier.Flawless || recipes.Any(previous => ModulatorFusion.SameRecipe(previous, part))) continue;
            recipes.Add(part);
            int copies = _recoveredModulators.Count(candidate => ModulatorFusion.SameRecipe(candidate, part));
            WeaponModulatorData preview = ModulatorCatalog.Clone(part, part.Tier + 1);
            int cost = ModulatorFusion.GetCost(part, DefenseSettings!.ModulatorFusionBaseCost);
            AddShopLabel($"{part.DisplayName}: {copies}/2 copies  →  {preview.DisplayName}", 16);
            AddShopLabel(ModulatorQualityText.Describe(preview), 14);
            ModulatorFusion.CanFuse(_recoveredModulators, part, _defenseCoins, DefenseSettings.ModulatorFusionBaseCost, out string reason);
            if (!string.IsNullOrEmpty(reason)) AddShopLabel(reason, 13);
            AddShopButton($"Fuse / {cost} coins", () => CanFuseDefenseModulator(part), () => FuseDefenseModulator(part));
        }
        if (recipes.Count == 0) AddShopLabel("Collect matching parts to improve their quality. Flawless is the maximum.", 14);
    }

    private bool CanFuseDefenseModulator(WeaponModulatorData part) => IsDefenseShopAvailable && _shopOpen &&
        ModulatorFusion.CanFuse(_recoveredModulators, part, _defenseCoins, DefenseSettings!.ModulatorFusionBaseCost, out _);

    private bool FuseDefenseModulator(WeaponModulatorData part)
    {
        if (!CanFuseDefenseModulator(part)) return false;
        if (!ModulatorFusion.TryFuse(_recoveredModulators, part, ref _defenseCoins, DefenseSettings!.ModulatorFusionBaseCost,
            out WeaponModulatorData? upgraded, out _)) return false;
        ShowNotice($"Fused {upgraded!.DisplayName}.", 3f);
        RebuildDefenseShopMenu();
        return true;
    }

    private bool ReturnDefenseModulator(EquipmentItem weapon, WeaponModulatorData part)
    {
        if (!IsDefenseShopAvailable || !_shopOpen || !ReferenceEquals(weapon, GetCurrentWeapon())
            || _recoveredModulators.Count >= StoredModulatorCapacity || !weapon.Modulators.Remove(part)) return false;
        TryStoreRunModulator(part, out _);
        RefreshDefenseWeapon(weapon);
        return true;
    }
}
