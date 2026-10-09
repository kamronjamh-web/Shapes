using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    public IReadOnlyList<EquipmentItem> GetInventoryShapes() => _arsenal.AsReadOnly();
    public IReadOnlyList<WeaponModulatorData> GetInventoryStoredModulators() => _recoveredModulators.AsReadOnly();
    public IReadOnlyList<WeaponModulatorData?> GetInventoryWeaponSlots(int arsenalIndex)
        => arsenalIndex >= 0 && arsenalIndex < _arsenal.Count
            ? FpsWeaponSocketLayout.GetSlots(_arsenal[arsenalIndex]) : Array.Empty<WeaponModulatorData?>();

    private EquipmentItem? InventoryTarget => _inspectionWeaponIndex >= 0 && _inspectionWeaponIndex < _arsenal.Count
        ? _arsenal[_inspectionWeaponIndex] : null;

    private bool CanEditInventory(EquipmentItem? expectedWeapon, out string reason)
    {
        reason = string.Empty;
        if (!_weaponInspectionOpen || !_pauseRequested || _defeat || (_victory && !IsDefenseMap))
            reason = "Open the paused inventory to change equipment.";
        else if (expectedWeapon == null || !ReferenceEquals(expectedWeapon, InventoryTarget))
            reason = "The selected shape changed. Drag the part again.";
        return reason.Length == 0;
    }

    public bool TryEquipInventoryShape(int arsenalIndex)
    {
        if (!_weaponInspectionOpen || !_pauseRequested || _defeat || (_victory && !IsDefenseMap)
            || arsenalIndex < 0 || arsenalIndex >= _arsenal.Count) return false;
        bool samePreview = _inspectionWeaponIndex == arsenalIndex;
        bool changedEquipment = _selectedWeaponIndex != arsenalIndex;
        _inspectionWeaponIndex = arsenalIndex;
        if (_selectedWeaponIndex != arsenalIndex) EquipWeapon(arsenalIndex, false);
        RefreshInventoryInspection();
        if (samePreview && changedEquipment) _weaponInspectionUi?.AnimateCurrentEquip();
        return true;
    }

    public bool CanInstallInventoryModulator(WeaponModulatorData part, int slotIndex, out string reason)
        => CanInstallInventoryModulator(InventoryTarget, part, slotIndex, out reason);

    public bool CanInstallInventoryModulator(EquipmentItem? expectedWeapon, WeaponModulatorData part, int slotIndex, out string reason)
    {
        if (!CanEditInventory(expectedWeapon, out reason)) return false;
        EquipmentItem weapon = expectedWeapon!;
        if (part == null || !_recoveredModulators.Exists(candidate => ReferenceEquals(candidate, part)))
            reason = "This part is no longer in run storage.";
        else if (slotIndex < 0 || slotIndex >= FpsWeaponSocketLayout.SocketCount || slotIndex >= weapon.UnlockedModulatorSlots)
            reason = "This socket is locked.";
        else if (FpsWeaponSocketLayout.GetSlots(weapon)[slotIndex] != null)
            reason = "Return the installed part to storage before using this socket.";
        else if (weapon.AvailableModulatorSlots <= 0)
            reason = "This shape has no open sockets.";
        else if (_arsenal.Exists(item => item.Modulators.Exists(candidate => ReferenceEquals(candidate, part))))
            reason = "This part is already installed.";
        else if (!WeaponModulatorCompatibility.CanApplyToWeapon(part, weapon, out reason)) return false;
        else if (!CanInstallModulatorForFpsWeapon(weapon, part))
            reason = "Wings require a support shape, one Wing per shape, and a unique Wing type across the loadout.";
        return reason.Length == 0;
    }

    public bool TryInstallInventoryModulator(WeaponModulatorData part, int slotIndex, out string reason)
        => TryInstallInventoryModulator(InventoryTarget, part, slotIndex, out reason);

    public bool TryInstallInventoryModulator(EquipmentItem? expectedWeapon, WeaponModulatorData part, int slotIndex, out string reason)
    {
        if (!CanInstallInventoryModulator(expectedWeapon, part, slotIndex, out reason)) return false;
        EquipmentItem weapon = expectedWeapon!;
        FpsWeaponSocketLayout.Assign(weapon, slotIndex, part);
        weapon.Modulators.Add(part);
        _recoveredModulators.RemoveAt(_recoveredModulators.FindIndex(candidate => ReferenceEquals(candidate, part)));
        RefreshInventoryWeapon(weapon);
        return true;
    }

    public bool CanRemoveInventoryModulator(int slotIndex, out string reason)
        => CanRemoveInventoryModulator(InventoryTarget, slotIndex, out reason);

    public bool CanRemoveInventoryModulator(EquipmentItem? expectedWeapon, int slotIndex, out string reason)
    {
        if (!CanEditInventory(expectedWeapon, out reason)) return false;
        if (slotIndex < 0 || slotIndex >= FpsWeaponSocketLayout.SocketCount || slotIndex >= expectedWeapon!.UnlockedModulatorSlots)
            reason = "This socket is locked.";
        else if (FpsWeaponSocketLayout.GetSlots(expectedWeapon!)[slotIndex] == null)
            reason = "This socket is empty.";
        return reason.Length == 0;
    }

    public bool TryRemoveInventoryModulator(int slotIndex, out string reason)
        => TryRemoveInventoryModulator(InventoryTarget, slotIndex, out reason);

    public bool TryRemoveInventoryModulator(EquipmentItem? expectedWeapon, int slotIndex, out string reason)
    {
        if (!CanRemoveInventoryModulator(expectedWeapon, slotIndex, out reason)) return false;
        EquipmentItem weapon = expectedWeapon!;
        WeaponModulatorData part = FpsWeaponSocketLayout.GetSlots(weapon)[slotIndex]!;
        weapon.Modulators.RemoveAt(weapon.Modulators.FindIndex(candidate => ReferenceEquals(candidate, part)));
        FpsWeaponSocketLayout.Clear(weapon, slotIndex);
        _recoveredModulators.Add(part);
        RefreshInventoryWeapon(weapon);
        return true;
    }

    private void RefreshInventoryWeapon(EquipmentItem weapon)
    {
        StatGenerator.RecalculateDerivedStats(weapon);
        if (ReferenceEquals(weapon, GetCurrentWeapon()))
        {
            ApplyWeaponHandling(weapon);
            _player.ApplyWeaponItemVisual(weapon);
            UpdateWeaponHud();
        }
        RefreshInventoryInspection();
    }

    private void RefreshInventoryInspection()
    {
        _weaponInspectionUi?.SetWeapons(_arsenal, _inspectionWeaponIndex, _selectedWeaponIndex);
        _weaponInspectionUi?.SetInventory(GetInventoryStoredModulators(), GetInventoryWeaponSlots(_inspectionWeaponIndex),
            GetInventoryWeaponStats(_inspectionWeaponIndex));
    }

    public FpsInventoryWeaponStats? GetInventoryWeaponStats(int arsenalIndex)
    {
        if (arsenalIndex < 0 || arsenalIndex >= _arsenal.Count || _arsenal[arsenalIndex].Shape == null) return null;
        EquipmentItem weapon = _arsenal[arsenalIndex];
        float cadence = Mathf.Max(.1f, 1f + StatGenerator.GetTotalSubstatValue(weapon, SubstatType.AttackSpeed) / 100f);
        float maxHealth = Mathf.Max(1f, _debugBaseMaxHealth) * Mathf.Max(.01f, 1f + GetFpsPassiveSubstat(weapon, SubstatType.HealthPercent) / 100f);
        if (_arenaDebugOverridesActive) maxHealth += _arenaDebugOverrides.MaxHpFlat;
        float healthRatio = _player != null && _player.MaxHealth > 0f ? _player.CurrentHealth / _player.MaxHealth : 1f;
        WeaponAttackType type = weapon.Shape!.AttackType;
        bool hasPiercingOutput = type is WeaponAttackType.Single or WeaponAttackType.Sawblade or WeaponAttackType.Homing
            or WeaponAttackType.Drill or WeaponAttackType.TemporalEcho;
        bool hasWorldBounce = (WeaponModulatorCompatibility.GetCapabilities(type) & WeaponModulatorCapability.WorldBounce) != 0;
        int baseOutputCount = type is WeaponAttackType.Mine or WeaponAttackType.Orbital ? 3
            : type is WeaponAttackType.Homing or WeaponAttackType.ShadowClone ? 2
            : type == WeaponAttackType.ChainLightning ? 2 + weapon.Shape.Hits : 1;
        float damageFactor = type switch
        {
            WeaponAttackType.Sawblade => 1.15f, WeaponAttackType.Laser => 1.25f,
            WeaponAttackType.Mine or WeaponAttackType.Homing or WeaponAttackType.FlameCone => 1.05f,
            WeaponAttackType.Nova => 1.35f, WeaponAttackType.Orbital => .85f,
            WeaponAttackType.Rift => .55f, WeaponAttackType.Mortar => 1.6f,
            WeaponAttackType.Drill => 1.2f, WeaponAttackType.VoidSingularity => .48f,
            WeaponAttackType.TemporalEcho => .75f,
            WeaponAttackType.WrathAura => .6f + (1f - healthRatio) * 1.1f,
            WeaponAttackType.ShadowClone => .65f * WeaponModulatorSharedRuntime.GetWeaponItemCloneOrEchoPowerMultiplier(weapon),
            _ => 1f,
        };
        return new FpsInventoryWeaponStats
        {
            Damage = GetScaledWeaponDamage(weapon) * damageFactor,
            DamageLabel = type is WeaponAttackType.Rift or WeaponAttackType.VoidSingularity ? "Field tick damage"
                : type == WeaponAttackType.ShadowClone ? "Clone shot damage"
                : type == WeaponAttackType.WrathAura ? "Burst damage" : "Impact damage",
            DamageHint = type == WeaponAttackType.WrathAura ? "At current missing health; passive aura ticks are separate. Before critical hits and enemy-specific effects."
                : type == WeaponAttackType.Laser || type == WeaponAttackType.FlameCone ? "Initial hit; lingering ticks and follow-up effects are separate. Before critical hits and enemy-specific effects."
                : type == WeaponAttackType.ChainLightning ? "First target; each next chain retains 78%. Before critical hits and enemy-specific effects."
                : "Before critical hits and enemy-specific effects; follow-up effects are separate.",
            IntervalSeconds = Mathf.Max(.05f, GetWeaponRuntimeCooldown(weapon) / cadence),
            AverageExtraOutput = Mathf.Clamp(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.ProjectileCount), 0f, Mathf.Max(0, 32 - baseOutputCount)),
            Range = GetInventoryRange(weapon),
            AreaScale = GetFpsAreaScale(weapon), DurationScale = GetFpsDurationScale(weapon),
            AveragePierce = hasPiercingOutput ? Mathf.Clamp(WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(weapon), 0f, 32f) : 0f,
            AverageBounces = hasWorldBounce ? Mathf.Clamp(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.BounceTimes), 0f, 12f) : 0f,
            AverageRicochet = Mathf.Clamp(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.Ricochet), 0f, 12f),
            AverageChains = type == WeaponAttackType.ChainLightning
                ? Mathf.Clamp(WeaponModulatorSharedRuntime.GetWeaponItemPassiveCountValue(weapon, ModulatorEffectType.ChainCountAdd), 0f, Mathf.Max(0, 32 - baseOutputCount)) : 0f,
            CritChancePercent = Mathf.Clamp(WeaponModulatorSharedRuntime.GetWeaponItemCritChancePercent(weapon)
                + (_arenaDebugOverridesActive ? _arenaDebugOverrides.CritChancePercent : 0f), 0f, 100f),
            CritDamageMultiplier = 1.5f + WeaponModulatorSharedRuntime.GetWeaponItemCritDamagePercent(weapon) / 100f,
            MaximumPlayerHealth = Mathf.Max(1f, maxHealth), CurrentPlayerHealth = Mathf.Max(1f, maxHealth) * healthRatio,
        };
    }

    private float GetInventoryRange(EquipmentItem weapon)
    {
        float rangeScale = GetFpsRangeScale(weapon);
        float flightScale = WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon) * GetFpsDurationScale(weapon);
        return weapon.Shape!.AttackType switch
        {
            WeaponAttackType.Laser => 78f * rangeScale,
            WeaponAttackType.Sawblade => SawbladeSpeed * SawbladeLife * flightScale * rangeScale,
            WeaponAttackType.Homing => HomingSpeed * 5f * flightScale * rangeScale,
            WeaponAttackType.Drill => DrillSpeed * DrillLife * flightScale * rangeScale,
            WeaponAttackType.Mine => 18f * rangeScale,
            WeaponAttackType.Orbital => 3f * rangeScale * WeaponModulatorSharedRuntime.GetWeaponItemOrbitRadiusScalar(weapon),
            WeaponAttackType.Nova => 8.5f * GetFpsAreaScale(weapon) * rangeScale,
            WeaponAttackType.FlameCone => 13f * rangeScale * WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon),
            WeaponAttackType.WrathAura => 4.6f * GetFpsAreaScale(weapon) * rangeScale,
            WeaponAttackType.ShadowClone => 36f * rangeScale,
            _ => GetWeaponRange(weapon.Shape.AttackType) * rangeScale,
        };
    }
}
