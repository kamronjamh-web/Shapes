using System;

namespace SuperGatchaRebuild.Equipment;

public enum EquipmentSlotType
{
    Primary = 0,
    Support = 1,
}

public sealed class EquipmentSlot
{
    public int SlotIndex { get; }
    public EquipmentSlotType SlotType { get; }

    public EquipmentItem? Item { get; private set; }
    public bool IsEmpty => Item == null;

    public event Action<EquipmentSlot>? Changed;

    public EquipmentSlot(int slotIndex, EquipmentSlotType slotType)
    {
        SlotIndex = slotIndex;
        SlotType = slotType;
    }

    public void Equip(EquipmentItem item)
    {
        Item = item;
        Changed?.Invoke(this);
    }

    public EquipmentItem? Unequip()
    {
        EquipmentItem? old = Item;
        Item = null;
        Changed?.Invoke(this);
        return old;
    }

    public override string ToString() => $"Slot {SlotIndex} [{SlotType}]";
}
