using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Equipment;

// Runtime generated item instance.
// Keep this as plain C# data, separate from reusable Resource definitions.
public sealed class EquipmentItem
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public ShapeData? Shape { get; set; }
    public RarityData? Rarity { get; set; }
    public ModifierData? Essence { get; set; }

    public ModifierData? Modifier
    {
        get => Essence;
        set => Essence = value;
    }

    public int Level { get; set; } = 1;
    public float WeaponXpProgress { get; set; }
    // Derived from shape definition at generation time.
    public int Stars { get; set; } = 1;

    // Main stats after rarity/modifier scaling.
    public int Health { get; set; }
    public int Damage { get; set; }

    public List<SubstatRoll> Substats { get; set; } = new();
    public List<WeaponModulatorData> Modulators { get; set; } = new();

    public bool HasEssenceSlotUnlocked => EquipmentCatalogDefaults.HasEssenceSlot(Rarity?.Key, Level);
    public int UnlockedModulatorSlots => EquipmentCatalogDefaults.GetUnlockedModulatorSlots(Rarity?.Key, Level);
    public int AvailableModulatorSlots => Math.Max(0, UnlockedModulatorSlots - (Modulators?.Count ?? 0));
    public bool HasEquippedEssence => Essence != null && Essence.Key != ModifierKey.Normal;

    public bool IsValid => Shape != null && Rarity != null;
}
