using System;
using System.Linq;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsWeaponInspectionUI
{
    private int ActiveShapeCount => _capacitySet ? _equippedCount : Math.Min(_weapons.Count, 5);
    private int ReserveShapeCount => _capacitySet ? _reserveCount : Math.Max(0, _weapons.Count - ActiveShapeCount);
    private bool CanUseInventoryActions => _inspectionVisible && _motionPhase == "";

    public void SetCapacityState(int equippedCount, int reserveCount, int storedCount)
    {
        EnsureBuilt();
        _capacitySet = true;
        _equippedCount = Math.Max(0, equippedCount);
        _reserveCount = Math.Max(0, reserveCount);
        _storedCount = Math.Max(0, storedCount);
        RebuildInventory();
        if (InspectedItem != null)
            _rarity.Text = $"{InspectedItem.Rarity?.DisplayName ?? "Common"}  /  LEVEL {InspectedItem.Level}  /  {ShapeLocation(_selected)}";
        RefreshInventoryActions();
    }

    private string ShapeLocation(int index) => index >= ActiveShapeCount
        ? $"RESERVE {index - ActiveShapeCount + 1}"
        : $"ACTIVE SLOT {index + 1}{(index == _equipped ? " / HELD" : "")}";

    private void RefreshCapacityText()
    {
        if (_capacity != null)
            _capacity.Text = $"Shapes: {ActiveShapeCount}/5 equipped + {ReserveShapeCount}/2 reserve  |  Loose parts: {(_capacitySet ? _storedCount : _stored.Count)}/100";
    }

    private void RefreshInventoryActions()
    {
        if (_equip == null) return;
        _equip.Disabled = !CanUseInventoryActions || InspectedItem == null || _selected == _equipped;
        _equip.Text = _selected == _equipped ? "EQUIPPED"
            : _selected >= ActiveShapeCount && InspectedItem != null ? "Swap with held shape" : "Equip this shape";
        if (_dropShape != null) _dropShape.Disabled = !CanUseInventoryActions || InspectedItem == null;
        if (_dropStoredPart != null)
            _dropStoredPart.Disabled = !CanUseInventoryActions || !_detailPartIsStored || _detailPart == null || !_stored.Contains(_detailPart);
    }

    private void DropSelectedShape()
    {
        if (!CanUseInventoryActions || InspectedItem == null) return;
        int ownedIndex = _selected;
        ShowWeaponDetails();
        ShapeDropRequested?.Invoke(ownedIndex);
        RefreshInventoryActions();
    }

    private void DropSelectedStoredPart()
    {
        if (!CanUseInventoryActions || !_detailPartIsStored || _detailPart is not { } part || !_stored.Contains(part)) return;
        ShowWeaponDetails();
        StoredModulatorDropRequested?.Invoke(part);
        RefreshInventoryActions();
    }
}
