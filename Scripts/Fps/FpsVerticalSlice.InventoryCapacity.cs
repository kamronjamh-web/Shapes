using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    public const int EquippedShapeCapacity = 5;
    public const int ReserveShapeCapacity = 2;
    public const int StoredModulatorCapacity = 100;
    private readonly List<EquipmentItem> _reserveShapes = new();
    public int EquippedShapeCount => _arsenal.Count;
    public int ReserveShapeCount => _reserveShapes.Count;

    private EquipmentItem? GetInventoryShape(int index)
    {
        if (index < 0) return null;
        if (index < _arsenal.Count) return _arsenal[index];
        index -= _arsenal.Count;
        return index < _reserveShapes.Count ? _reserveShapes[index] : null;
    }

    private bool IsInventoryPartInstalled(WeaponModulatorData part)
    {
        foreach (EquipmentItem shape in GetInventoryShapes())
            if (shape.Modulators.Exists(candidate => ReferenceEquals(candidate, part))) return true;
        return false;
    }

    public bool TryStoreRunShape(EquipmentItem shape, out string reason)
    {
        reason = string.Empty;
        if (shape?.Shape == null || shape.Rarity == null)
            reason = "This shape is unavailable.";
        else if (_arsenal.Contains(shape) || _reserveShapes.Contains(shape))
            reason = "This shape is already in your inventory.";
        else if (_arsenal.Count < EquippedShapeCapacity && CanSwapReserveShape(shape, _arsenal.Count, out reason))
            _arsenal.Add(shape);
        else if (_reserveShapes.Count < ReserveShapeCapacity)
        {
            _reserveShapes.Add(shape);
            reason = string.Empty;
        }
        else if (reason.Length == 0) reason = "Shape inventory is full (5 equipped + 2 reserve). Drop a shape first.";
        if (reason.Length == 0 && _weaponInspectionOpen) RefreshInventoryInspection();
        return reason.Length == 0;
    }

    public bool TryStoreRunModulator(WeaponModulatorData part, out string reason)
    {
        reason = string.Empty;
        if (part == null) reason = "This part is unavailable.";
        else if (_recoveredModulators.Exists(candidate => ReferenceEquals(candidate, part)) || IsInventoryPartInstalled(part))
            reason = "This part is already in your inventory.";
        else if (_recoveredModulators.Count >= StoredModulatorCapacity)
            reason = "Modulator storage is full (100/100). Drop or install a stored part first.";
        else _recoveredModulators.Add(part);
        if (reason.Length == 0 && _weaponInspectionOpen) RefreshInventoryInspection();
        return reason.Length == 0;
    }

    private bool CanSwapReserveShape(EquipmentItem incoming, int activeIndex, out string reason)
    {
        reason = string.Empty;
        bool addingActive = activeIndex == _arsenal.Count && _arsenal.Count < EquippedShapeCapacity;
        if (activeIndex < 0 || (activeIndex >= _arsenal.Count && !addingActive))
        {
            reason = "Select an equipped shape before swapping a reserve.";
            return false;
        }
        HashSet<string> wings = new(StringComparer.Ordinal);
        for (int slot = 0; slot < _arsenal.Count + (addingActive ? 1 : 0); slot++)
        {
            EquipmentItem weapon = slot == activeIndex ? incoming : _arsenal[slot];
            int wingCount = 0;
            foreach (WeaponModulatorData part in weapon.Modulators)
            {
                if (!part.IsWingModulator) continue;
                if (slot == 0 || ++wingCount > 1 || !wings.Add(part.NormalizedKey))
                {
                    reason = "This swap would conflict with equipped Wings. Remove the conflicting Wing first.";
                    return false;
                }
            }
        }
        return true;
    }

    public bool TryDropInventoryShape(int index, out string reason)
    {
        EquipmentItem? shape = GetInventoryShape(index);
        if (!CanEditInventory(InventoryTarget, out reason)) return false;
        if (shape == null) { reason = "This shape is no longer in your inventory."; return false; }
        if (index < _arsenal.Count && _arsenal.Count <= 1)
        {
            reason = "Keep at least one equipped shape.";
            return false;
        }
        if (index == 0 && _arsenal[1].Modulators.Exists(part => part.IsWingModulator))
        {
            reason = "Remove the next shape's Wing before moving it into the primary slot.";
            return false;
        }
        EquipmentItem? preview = InventoryTarget;
        if (index < _arsenal.Count)
        {
            _arsenal.RemoveAt(index);
            if (_selectedWeaponIndex > index) _selectedWeaponIndex--;
            _selectedWeaponIndex = Math.Clamp(_selectedWeaponIndex, 0, _arsenal.Count - 1);
            ResetWeaponEffectRuntimeState();
            EquipWeapon(_selectedWeaponIndex, false);
        }
        else _reserveShapes.RemoveAt(index - _arsenal.Count);
        _inspectionWeaponIndex = _selectedWeaponIndex;
        if (preview != null && !ReferenceEquals(preview, shape))
        {
            int previewIndex = _arsenal.IndexOf(preview);
            if (previewIndex < 0) previewIndex = _arsenal.Count + _reserveShapes.IndexOf(preview);
            _inspectionWeaponIndex = previewIndex;
        }
        SpawnGroundShape(shape);
        RefreshInventoryInspection();
        return true;
    }

    public bool TryDropInventoryModulator(WeaponModulatorData part, out string reason)
    {
        if (!CanEditInventory(InventoryTarget, out reason)) return false;
        int index = _recoveredModulators.FindIndex(candidate => ReferenceEquals(candidate, part));
        if (index < 0) { reason = "This part is no longer in storage."; return false; }
        _recoveredModulators.RemoveAt(index);
        SpawnGroundModulator(part);
        RefreshInventoryInspection();
        return true;
    }
}
