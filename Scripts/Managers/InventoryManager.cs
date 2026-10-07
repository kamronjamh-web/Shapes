using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Hub;

namespace SuperGatchaRebuild.Managers;

// Lightweight inventory/equipment service used by hub and debug systems.
public partial class InventoryManager : Node, IEquipmentInventoryService, IInventoryDebugCommands, IHubEquipmentProvider
{
    [Signal]
    public delegate void InventoryChangedEventHandler();

    [Signal]
    public delegate void EquippedChangedEventHandler();

    [Export] public int MaxInventorySize = 400;
    [Export] public string SaveFilePath = "user://inventory_state.cfg";

    private const string MetaSection = "inventory_meta";
    private const string SchemaVersionKey = "schema_version";
    private const int CurrentSchemaVersion = 6;
    private const string EquippedSection = "inventory_equipped";
    private const string ItemSectionPrefix = "inventory_item_";
    private const string StoredEssenceSection = "inventory_essences";
    private const string StoredModulatorSection = "inventory_modulators";
    private const string DefaultShapeKey = "square";
    private const string DefaultRarityKey = "common";

    private readonly List<EquipmentItem> _items = new();
    private readonly List<ModifierData> _storedEssences = new();
    private readonly List<WeaponModulatorData> _storedModulators = new();
    private readonly EquipmentItem?[] _equipped = new EquipmentItem?[5];
    private bool _isRestoringState;

    public event Action? EquipmentChanged;

    public bool IsInventoryFull => _items.Count >= MaxInventorySize;
    public IReadOnlyList<EquipmentItem> Items => _items;
    public IReadOnlyList<ModifierData> StoredEssences => _storedEssences;
    public IReadOnlyList<WeaponModulatorData> StoredModulators => _storedModulators;

    public override void _Ready()
    {
        LoadState();
    }

    public bool TryAddItem(EquipmentItem item)
    {
        if (IsInventoryFull)
        {
            return false;
        }

        EnsureItemCoreData(item);

        if (FindItemByInstanceId(item.InstanceId) != null)
        {
            item.InstanceId = Guid.NewGuid().ToString("N");
        }

        _items.Add(item);
        AutoEquipIfPossible(item);
        EmitInventorySignals();
        return true;
    }

    public void NotifyInventoryChanged()
    {
        EmitInventorySignals();
    }

    public EquipmentItem? GetEquippedItemAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _equipped.Length)
        {
            return null;
        }

        return _equipped[slotIndex];
    }

    public bool TryEquip(EquipmentItem item, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _equipped.Length)
        {
            return false;
        }

        EnsureItemCoreData(item);
        if (!CanEquipItemInSlot(item, slotIndex))
        {
            return false;
        }

        for (int i = 0; i < _equipped.Length; i++)
        {
            if (i != slotIndex && ReferenceEquals(_equipped[i], item))
            {
                _equipped[i] = null;
            }
        }

        _equipped[slotIndex] = item;
        EmitInventorySignals();
        return true;
    }

    public void Unequip(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _equipped.Length)
        {
            return;
        }

        _equipped[slotIndex] = null;
        EmitInventorySignals();
    }

    public bool TryDeleteItem(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        int itemIndex = FindItemIndexByInstanceId(instanceId);
        if (itemIndex < 0)
        {
            return false;
        }

        EquipmentItem item = _items[itemIndex];
        UnequipItemReferences(item);
        _items.RemoveAt(itemIndex);
        EmitInventorySignals();
        return true;
    }

    public int DeleteItems(IEnumerable<string> instanceIds)
    {
        HashSet<string> idsToDelete = new(StringComparer.Ordinal);
        foreach (string instanceId in instanceIds)
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                idsToDelete.Add(instanceId);
            }
        }

        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        int removed = 0;
        for (int itemIndex = _items.Count - 1; itemIndex >= 0; itemIndex--)
        {
            EquipmentItem item = _items[itemIndex];
            if (!idsToDelete.Contains(item.InstanceId))
            {
                continue;
            }

            UnequipItemReferences(item);
            _items.RemoveAt(itemIndex);
            removed++;
        }

        if (removed > 0)
        {
            EmitInventorySignals();
        }

        return removed;
    }

    public bool TrySetItemLevel(string? instanceId, int targetLevel)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null)
        {
            return false;
        }

        int clampedLevel = StatGenerator.ClampWeaponLevel(targetLevel);
        if (item.Level == clampedLevel)
        {
            return false;
        }

        item.Level = clampedLevel;
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool TrySetItemWeaponProgress(string? instanceId, int targetLevel, float targetXpProgress, bool emitSignals = true)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null)
        {
            return false;
        }

        int clampedLevel = StatGenerator.ClampWeaponLevel(targetLevel);
        float clampedProgress = Mathf.Max(0f, targetXpProgress);
        if (clampedLevel >= EquipmentCatalogDefaults.MaxWeaponLevel)
        {
            clampedProgress = 0f;
        }

        if (item.Level == clampedLevel && Mathf.IsEqualApprox(item.WeaponXpProgress, clampedProgress))
        {
            return false;
        }

        item.Level = clampedLevel;
        item.WeaponXpProgress = clampedProgress;
        EnsureItemCoreData(item);
        if (emitSignals)
        {
            EmitInventorySignals();
        }

        return true;
    }

    public bool TryLevelUpItem(string? instanceId)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel)
        {
            return false;
        }

        return TrySetItemLevel(instanceId, item.Level + 1);
    }

    public bool TryInstallEssence(string? instanceId, ModifierData? essence, bool replaceExisting = false)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || essence == null)
        {
            return false;
        }

        EnsureItemCoreData(item);
        if (!item.HasEssenceSlotUnlocked)
        {
            return false;
        }

        if (item.HasEquippedEssence && !replaceExisting)
        {
            return false;
        }

        item.Essence = essence.Key == ModifierKey.Normal ? null : CloneModifier(essence);
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool TryClearEssence(string? instanceId)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || !item.HasEquippedEssence)
        {
            return false;
        }

        item.Essence = null;
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool TryAddModulator(string? instanceId, WeaponModulatorData? modulator)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || modulator == null)
        {
            return false;
        }

        EnsureItemCoreData(item);
        if (item.AvailableModulatorSlots <= 0)
        {
            return false;
        }

        if (!CanInstallModulator(item, modulator))
        {
            return false;
        }

        item.Modulators.Add(CloneModulator(modulator));
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool TryRemoveModulator(string? instanceId, int modulatorIndex)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || item.Modulators == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count)
        {
            return false;
        }

        item.Modulators.RemoveAt(modulatorIndex);
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool StoreEssence(ModifierData? essence)
    {
        if (essence == null || essence.Key == ModifierKey.Normal)
        {
            return false;
        }

        _storedEssences.Add(CloneModifier(essence));
        EmitInventorySignals();
        return true;
    }

    public bool StoreModulator(WeaponModulatorData? modulator)
    {
        if (modulator == null)
        {
            return false;
        }

        _storedModulators.Add(CloneModulator(modulator));
        EmitInventorySignals();
        return true;
    }

    public bool TryInstallStoredEssence(string? instanceId, int storedEssenceIndex, bool replaceExisting = true)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || storedEssenceIndex < 0 || storedEssenceIndex >= _storedEssences.Count)
        {
            return false;
        }

        EnsureItemCoreData(item);
        if (!item.HasEssenceSlotUnlocked)
        {
            return false;
        }

        if (item.HasEquippedEssence && !replaceExisting)
        {
            return false;
        }

        ModifierData incomingEssence = CloneModifier(_storedEssences[storedEssenceIndex]);
        ModifierData? replacedEssence = item.HasEquippedEssence ? CloneModifier(item.Essence!) : null;

        _storedEssences.RemoveAt(storedEssenceIndex);
        if (replacedEssence != null)
        {
            _storedEssences.Add(replacedEssence);
        }

        item.Essence = incomingEssence;
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool TryInstallStoredModulator(string? instanceId, int storedModulatorIndex)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || storedModulatorIndex < 0 || storedModulatorIndex >= _storedModulators.Count)
        {
            return false;
        }

        EnsureItemCoreData(item);
        if (item.AvailableModulatorSlots <= 0)
        {
            return false;
        }

        WeaponModulatorData modulator = CloneModulator(_storedModulators[storedModulatorIndex]);
        if (!CanInstallModulator(item, modulator))
        {
            return false;
        }

        _storedModulators.RemoveAt(storedModulatorIndex);
        item.Modulators.Add(modulator);
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool CanInstallStoredModulator(string? instanceId, int storedModulatorIndex, int replacingModulatorIndex = -1)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || storedModulatorIndex < 0 || storedModulatorIndex >= _storedModulators.Count)
        {
            return false;
        }

        EnsureItemCoreData(item);
        if (replacingModulatorIndex >= 0)
        {
            if (item.Modulators == null || replacingModulatorIndex >= item.Modulators.Count)
            {
                return false;
            }
        }
        else if (item.AvailableModulatorSlots <= 0)
        {
            return false;
        }

        return CanInstallModulator(item, _storedModulators[storedModulatorIndex], replacingModulatorIndex);
    }

    public bool TryReplaceStoredModulator(string? instanceId, int modulatorIndex, int storedModulatorIndex)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || item.Modulators == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count
            || storedModulatorIndex < 0 || storedModulatorIndex >= _storedModulators.Count)
        {
            return false;
        }

        EnsureItemCoreData(item);
        WeaponModulatorData incomingModulator = CloneModulator(_storedModulators[storedModulatorIndex]);
        if (!CanInstallModulator(item, incomingModulator, modulatorIndex))
        {
            return false;
        }

        WeaponModulatorData replacedModulator = CloneModulator(item.Modulators[modulatorIndex]);
        _storedModulators.RemoveAt(storedModulatorIndex);
        item.Modulators[modulatorIndex] = incomingModulator;
        _storedModulators.Add(replacedModulator);
        EnsureItemCoreData(item);
        EmitInventorySignals();
        return true;
    }

    public bool TryReturnEssenceToStorage(string? instanceId)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || !item.HasEquippedEssence)
        {
            return false;
        }

        ModifierData essence = CloneModifier(item.Essence!);
        item.Essence = null;
        EnsureItemCoreData(item);
        _storedEssences.Add(essence);
        EmitInventorySignals();
        return true;
    }

    public bool TryReturnModulatorToStorage(string? instanceId, int modulatorIndex)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null || item.Modulators == null || modulatorIndex < 0 || modulatorIndex >= item.Modulators.Count)
        {
            return false;
        }

        WeaponModulatorData modulator = CloneModulator(item.Modulators[modulatorIndex]);
        item.Modulators.RemoveAt(modulatorIndex);
        EnsureItemCoreData(item);
        _storedModulators.Add(modulator);
        EmitInventorySignals();
        return true;
    }

    public int AddAllShapesToInventory()
    {
        int added = 0;

        RandomNumberGenerator rng = new();
        rng.Randomize();

        StatGenerator generator = new(rng);

        List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
        RarityData rarity = EquipmentCatalogDefaults.CreateRarityByKey("mythic");
        ModifierData modifier = EquipmentCatalogDefaults.CreateModifierByKey(ModifierKey.Normal, includeBaselineNormal: true);

        for (int i = 0; i < shapes.Count; i++)
        {
            if (IsInventoryFull)
            {
                break;
            }

            ShapeData shape = shapes[i];
            EquipmentItem item = generator.BuildItem(shape, rarity, modifier, 1);

            if (TryAddItem(item))
            {
                added++;
            }
        }

        return added;
    }

    public int AddAllModulatorsToInventory(bool includeAllTiers = true)
    {
        List<WeaponModulatorData> templates = EquipmentCatalogDefaults.CreateDefaultModulators();
        if (templates.Count == 0)
        {
            return 0;
        }

        HashSet<string> seenSignatures = new(StringComparer.Ordinal);
        for (int modulatorIndex = 0; modulatorIndex < _storedModulators.Count; modulatorIndex++)
        {
            WeaponModulatorData stored = _storedModulators[modulatorIndex];
            string normalizedKey = stored.NormalizedKey;
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                continue;
            }

            WeaponModulatorTier tier = stored.UsesTierPresentation ? stored.Tier : WeaponModulatorTier.Baseline;
            seenSignatures.Add(BuildStoredModulatorSignature(normalizedKey, tier));
        }

        WeaponModulatorTier[] tiers =
        {
            WeaponModulatorTier.Flawed,
            WeaponModulatorTier.Baseline,
            WeaponModulatorTier.Enhanced,
            WeaponModulatorTier.Flawless,
        };

        int added = 0;
        for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
        {
            WeaponModulatorData template = templates[templateIndex];
            string normalizedKey = template.NormalizedKey;
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                continue;
            }

            bool useTierVariants = includeAllTiers && template.UsesTierPresentation;
            if (useTierVariants)
            {
                for (int tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
                {
                    if (!EquipmentCatalogDefaults.TryCreateModulatorByKey(normalizedKey, tiers[tierIndex], out WeaponModulatorData modulator))
                    {
                        continue;
                    }

                    if (TryAddStoredModulatorUnique(modulator, seenSignatures))
                    {
                        added++;
                    }
                }

                continue;
            }

            if (!EquipmentCatalogDefaults.TryCreateModulatorByKey(normalizedKey, WeaponModulatorTier.Baseline, out WeaponModulatorData baselineModulator))
            {
                continue;
            }

            if (TryAddStoredModulatorUnique(baselineModulator, seenSignatures))
            {
                added++;
            }
        }

        if (added > 0)
        {
            EmitInventorySignals();
        }

        return added;
    }

    public void GrantAllItemsX1()
    {
        _ = AddAllShapesToInventory();
    }

    public void ClearAllItems()
    {
        _items.Clear();
        _storedEssences.Clear();
        _storedModulators.Clear();

        for (int i = 0; i < _equipped.Length; i++)
        {
            _equipped[i] = null;
        }

        EmitInventorySignals();
    }

    private void AutoEquipIfPossible(EquipmentItem item)
    {
        for (int i = 0; i < _equipped.Length; i++)
        {
            if (_equipped[i] == null)
            {
                _equipped[i] = item;
                return;
            }
        }
    }

    private void UnequipItemReferences(EquipmentItem item)
    {
        for (int slotIndex = 0; slotIndex < _equipped.Length; slotIndex++)
        {
            if (ReferenceEquals(_equipped[slotIndex], item)
                || string.Equals(_equipped[slotIndex]?.InstanceId, item.InstanceId, StringComparison.Ordinal))
            {
                _equipped[slotIndex] = null;
            }
        }
    }

    private int FindItemIndexByInstanceId(string instanceId)
    {
        for (int itemIndex = 0; itemIndex < _items.Count; itemIndex++)
        {
            if (string.Equals(_items[itemIndex].InstanceId, instanceId, StringComparison.Ordinal))
            {
                return itemIndex;
            }
        }

        return -1;
    }

    private void EmitInventorySignals()
    {
        if (!_isRestoringState)
        {
            SaveState();
        }

        EmitSignal(SignalName.InventoryChanged);
        EmitSignal(SignalName.EquippedChanged);
        EquipmentChanged?.Invoke();
    }

    private void EnsureItemCoreData(EquipmentItem item)
    {
        item.Shape ??= CreateFallbackShape(DefaultShapeKey, ToDisplayName(DefaultShapeKey));
        item.Rarity ??= CreateFallbackRarity(DefaultRarityKey, ToDisplayName(DefaultRarityKey));

        item.Level = StatGenerator.ClampWeaponLevel(item.Level);
        item.WeaponXpProgress = item.Level >= EquipmentCatalogDefaults.MaxWeaponLevel
            ? 0f
            : Mathf.Max(0f, item.WeaponXpProgress);
        item.Stars = Math.Max(1, item.Stars);
        item.Substats ??= new List<SubstatRoll>();
        item.Modulators ??= new List<WeaponModulatorData>();

        if (string.IsNullOrWhiteSpace(item.InstanceId))
        {
            item.InstanceId = Guid.NewGuid().ToString("N");
        }

        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            item.Modulators[modulatorIndex].RefreshPresentation();
        }

        StatGenerator.RecalculateDerivedStats(item);
    }

    private bool CanEquipItemInSlot(EquipmentItem item, int slotIndex)
    {
        if (!HasWingModulator(item))
        {
            return true;
        }

        if (slotIndex == 0)
        {
            return false;
        }

        return !HasConflictingEquippedWingModulator(item);
    }

    private bool CanInstallModulator(EquipmentItem item, WeaponModulatorData modulator, int replacingModulatorIndex = -1)
    {
        if (!modulator.CanApplyToWeapon(WeaponModulatorCompatibility.BuildWeaponTags(item)))
        {
            return false;
        }

        if (!modulator.IsWingModulator)
        {
            return true;
        }

        int equippedSlot = GetEquippedSlotIndex(item);
        if (equippedSlot <= 0)
        {
            return false;
        }

        if (HasWingModulator(item, replacingModulatorIndex))
        {
            return false;
        }

        return !HasConflictingEquippedWingModulator(item, modulator);
    }

    private int GetEquippedSlotIndex(EquipmentItem item)
    {
        for (int slotIndex = 0; slotIndex < _equipped.Length; slotIndex++)
        {
            if (ReferenceEquals(_equipped[slotIndex], item))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private bool HasConflictingEquippedWingModulator(EquipmentItem item, WeaponModulatorData? pendingModulator = null)
    {
        if (pendingModulator != null)
        {
            string pendingWingKey = WeaponModulatorData.NormalizeKey(pendingModulator.Key);
            if (string.IsNullOrWhiteSpace(pendingWingKey))
            {
                return false;
            }

            for (int slotIndex = 1; slotIndex < _equipped.Length; slotIndex++)
            {
                EquipmentItem? equippedItem = _equipped[slotIndex];
                if (equippedItem == null || ReferenceEquals(equippedItem, item))
                {
                    continue;
                }

                for (int modulatorIndex = 0; modulatorIndex < equippedItem.Modulators.Count; modulatorIndex++)
                {
                    WeaponModulatorData equippedModulator = equippedItem.Modulators[modulatorIndex];
                    if (equippedModulator.IsWingModulator && equippedModulator.NormalizedKey == pendingWingKey)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        for (int slotIndex = 1; slotIndex < _equipped.Length; slotIndex++)
        {
            EquipmentItem? equippedItem = _equipped[slotIndex];
            if (equippedItem == null || ReferenceEquals(equippedItem, item))
            {
                continue;
            }

            for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData itemModulator = item.Modulators[modulatorIndex];
                if (!itemModulator.IsWingModulator)
                {
                    continue;
                }

                string wingKey = itemModulator.NormalizedKey;
                for (int equippedModulatorIndex = 0; equippedModulatorIndex < equippedItem.Modulators.Count; equippedModulatorIndex++)
                {
                    WeaponModulatorData equippedModulator = equippedItem.Modulators[equippedModulatorIndex];
                    if (equippedModulator.IsWingModulator && equippedModulator.NormalizedKey == wingKey)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasWingModulator(EquipmentItem item, int excludedModulatorIndex = -1)
    {
        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            if (modulatorIndex == excludedModulatorIndex)
            {
                continue;
            }

            if (item.Modulators[modulatorIndex].IsWingModulator)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryAddStoredModulatorUnique(WeaponModulatorData modulator, HashSet<string> seenSignatures)
    {
        string normalizedKey = modulator.NormalizedKey;
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        WeaponModulatorTier tier = modulator.UsesTierPresentation ? modulator.Tier : WeaponModulatorTier.Baseline;
        string signature = BuildStoredModulatorSignature(normalizedKey, tier);
        if (!seenSignatures.Add(signature))
        {
            return false;
        }

        _storedModulators.Add(CloneModulator(modulator));
        return true;
    }

    private static string BuildStoredModulatorSignature(string normalizedKey, WeaponModulatorTier tier)
    {
        return $"{normalizedKey}|{(int)tier}";
    }

    private void LoadState()
    {
        _isRestoringState = true;

        _items.Clear();
        _storedEssences.Clear();
        _storedModulators.Clear();
        Array.Clear(_equipped, 0, _equipped.Length);

        ConfigFile cfg = new();
        if (cfg.Load(SaveFilePath) != Error.Ok)
        {
            _isRestoringState = false;
            return;
        }

        bool migrated = EnsureSchema(cfg);

        int itemCount = Mathf.Max(0, cfg.GetValue(MetaSection, "count", 0).AsInt32());
        HashSet<string> seenInstanceIds = new(StringComparer.Ordinal);

        for (int i = 0; i < itemCount; i++)
        {
            EquipmentItem? item = LoadItem(cfg, i);
            if (item == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.InstanceId) || seenInstanceIds.Contains(item.InstanceId))
            {
                item.InstanceId = Guid.NewGuid().ToString("N");
            }

            seenInstanceIds.Add(item.InstanceId);
            _items.Add(item);
        }

        if (cfg.HasSection(EquippedSection))
        {
            for (int i = 0; i < _equipped.Length; i++)
            {
                string instanceId = cfg.GetValue(EquippedSection, $"slot_{i}", string.Empty).AsString();
                _equipped[i] = FindItemByInstanceId(instanceId);
            }
        }
        else
        {
            AutoEquipFromLoadedItems();
        }

        _storedEssences.AddRange(LoadStoredEssences(cfg));
        _storedModulators.AddRange(LoadModulators(cfg, StoredModulatorSection));

        _isRestoringState = false;

        if (migrated)
        {
            SaveState();
        }
    }

    private void SaveState()
    {
        ConfigFile cfg = new();
        _ = cfg.Load(SaveFilePath);

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        int previousCount = Mathf.Max(0, cfg.GetValue(MetaSection, "count", 0).AsInt32());
        for (int i = 0; i < previousCount; i++)
        {
            EraseSectionIfPresent(cfg, GetItemSectionName(i));
        }

        cfg.SetValue(MetaSection, "count", _items.Count);

        for (int i = 0; i < _items.Count; i++)
        {
            SaveItem(cfg, _items[i], i);
        }

        EraseSectionIfPresent(cfg, StoredEssenceSection);
        SaveStoredEssences(cfg, _storedEssences);

        EraseSectionIfPresent(cfg, StoredModulatorSection);
        cfg.SetValue(StoredModulatorSection, "modulator_count", _storedModulators.Count);
        for (int i = 0; i < _storedModulators.Count; i++)
        {
            SaveModulator(cfg, StoredModulatorSection, i, _storedModulators[i]);
        }

        EraseSectionIfPresent(cfg, EquippedSection);
        for (int i = 0; i < _equipped.Length; i++)
        {
            cfg.SetValue(EquippedSection, $"slot_{i}", _equipped[i]?.InstanceId ?? string.Empty);
        }

        Error err = cfg.Save(SaveFilePath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Failed saving inventory state: {err}");
        }
    }

    private EquipmentItem? LoadItem(ConfigFile cfg, int index)
    {
        string section = GetItemSectionName(index);
        if (!cfg.HasSection(section))
        {
            return null;
        }

        ShapeData shape = LoadShape(cfg, section);
        RarityData rarity = LoadRarity(cfg, section);
        ModifierData? essence = LoadEssence(cfg, section);

        EquipmentItem item = new()
        {
            InstanceId = cfg.GetValue(section, "instance_id", Guid.NewGuid().ToString("N")).AsString(),
            Shape = shape,
            Rarity = rarity,
            Essence = essence,
            Level = StatGenerator.ClampWeaponLevel(cfg.GetValue(section, "level", 1).AsInt32()),
            WeaponXpProgress = cfg.GetValue(section, "weapon_xp_progress", 0f).AsSingle(),
            Stars = Math.Max(1, cfg.GetValue(section, "stars", Math.Max(1, shape.Stars)).AsInt32()),
            Health = cfg.GetValue(section, "health", 0).AsInt32(),
            Damage = cfg.GetValue(section, "damage", 0).AsInt32(),
            Substats = LoadSubstats(cfg, section),
            Modulators = LoadModulators(cfg, section),
        };

        EnsureItemCoreData(item);

        return item;
    }

    private static List<SubstatRoll> LoadSubstats(ConfigFile cfg, string section)
    {
        List<SubstatRoll> substats = new();
        int count = Mathf.Max(0, cfg.GetValue(section, "substat_count", 0).AsInt32());

        for (int i = 0; i < count; i++)
        {
            int rawType = cfg.GetValue(section, $"substat_{i}_type", 0).AsInt32();
            if (!Enum.IsDefined(typeof(SubstatType), rawType))
            {
                continue;
            }

            SubstatType type = (SubstatType)rawType;
            if (type == SubstatType.None)
            {
                continue;
            }

            float value = cfg.GetValue(section, $"substat_{i}_value", 0f).AsSingle();
            substats.Add(new SubstatRoll(type, value));
        }

        return substats;
    }

    private void SaveItem(ConfigFile cfg, EquipmentItem item, int index)
    {
        EnsureItemCoreData(item);

        ShapeData shape = item.Shape!;
        RarityData rarity = item.Rarity!;
        ModifierData? essence = item.Essence;

        string section = GetItemSectionName(index);
        EraseSectionIfPresent(cfg, section);

        cfg.SetValue(section, "instance_id", item.InstanceId);
        cfg.SetValue(section, "level", item.Level);
        cfg.SetValue(section, "weapon_xp_progress", item.WeaponXpProgress);
        cfg.SetValue(section, "stars", item.Stars);
        cfg.SetValue(section, "health", item.Health);
        cfg.SetValue(section, "damage", item.Damage);

        cfg.SetValue(section, "shape_key", shape.Key);
        cfg.SetValue(section, "shape_display", shape.DisplayName);
        cfg.SetValue(section, "shape_group", (int)shape.ShapeGroup);
        cfg.SetValue(section, "shape_weight", shape.Weight);
        cfg.SetValue(section, "shape_stars", shape.Stars);
        cfg.SetValue(section, "shape_attack_type", (int)shape.AttackType);
        cfg.SetValue(section, "shape_attack_name", shape.AttackName);
        cfg.SetValue(section, "shape_hits", shape.Hits);
        cfg.SetValue(section, "shape_damage_per_hit", shape.DamagePerHit);
        cfg.SetValue(section, "shape_total_damage", shape.TotalDamage);
        cfg.SetValue(section, "shape_cooldown", shape.Cooldown);
        cfg.SetValue(section, "shape_special_description", shape.SpecialDescription);
        cfg.SetValue(section, "shape_description", shape.Description);

        cfg.SetValue(section, "rarity_key", rarity.Key);
        cfg.SetValue(section, "rarity_display", rarity.DisplayName);
        cfg.SetValue(section, "rarity_weight", rarity.Weight);
        cfg.SetValue(section, "rarity_stat_multiplier", rarity.StatMultiplier);
        cfg.SetValue(section, "rarity_substat_count", rarity.SubstatCount);
        cfg.SetValue(section, "rarity_xp_value", rarity.XpValue);
        SaveColor(cfg, section, "rarity_color", rarity.UiColor);

        cfg.SetValue(section, "essence_present", essence != null);
        if (essence != null)
        {
            SaveModifier(cfg, section, "essence", essence);
        }

        List<SubstatRoll> substats = item.Substats ?? new List<SubstatRoll>();
        int substatCount = substats.Count;
        cfg.SetValue(section, "substat_count", substatCount);
        for (int i = 0; i < substatCount; i++)
        {
            SubstatRoll substat = substats[i];
            cfg.SetValue(section, $"substat_{i}_type", (int)substat.Type);
            cfg.SetValue(section, $"substat_{i}_value", substat.Value);
        }

        List<WeaponModulatorData> modulators = item.Modulators ?? new List<WeaponModulatorData>();
        cfg.SetValue(section, "modulator_count", modulators.Count);
        for (int i = 0; i < modulators.Count; i++)
        {
            SaveModulator(cfg, section, i, modulators[i]);
        }
    }

    private static void EraseSectionIfPresent(ConfigFile cfg, string section)
    {
        if (cfg.HasSection(section))
        {
            cfg.EraseSection(section);
        }
    }

    private static ShapeData LoadShape(ConfigFile cfg, string section)
    {
        int shapeGroupRaw = cfg.GetValue(section, "shape_group", (int)ShapeGroup.TwoD).AsInt32();
        ShapeGroup shapeGroup = Enum.IsDefined(typeof(ShapeGroup), shapeGroupRaw)
            ? (ShapeGroup)shapeGroupRaw
            : ShapeGroup.TwoD;

        int attackTypeRaw = cfg.GetValue(section, "shape_attack_type", (int)WeaponAttackType.Single).AsInt32();
        WeaponAttackType attackType = Enum.IsDefined(typeof(WeaponAttackType), attackTypeRaw)
            ? (WeaponAttackType)attackTypeRaw
            : WeaponAttackType.Single;

        string key = cfg.GetValue(section, "shape_key", DefaultShapeKey).AsString();
        string display = cfg.GetValue(section, "shape_display", ToDisplayName(key)).AsString();

        return new ShapeData
        {
            Key = key,
            DisplayName = string.IsNullOrWhiteSpace(display) ? ToDisplayName(key) : display,
            ShapeGroup = shapeGroup,
            Weight = cfg.GetValue(section, "shape_weight", 1f).AsSingle(),
            Stars = Math.Max(1, cfg.GetValue(section, "shape_stars", 1).AsInt32()),
            AttackType = attackType,
            AttackName = cfg.GetValue(section, "shape_attack_name", "Flat Strike").AsString(),
            Hits = Math.Max(1, cfg.GetValue(section, "shape_hits", 1).AsInt32()),
            DamagePerHit = Math.Max(0f, cfg.GetValue(section, "shape_damage_per_hit", 80f).AsSingle()),
            TotalDamage = Math.Max(0f, cfg.GetValue(section, "shape_total_damage", 80f).AsSingle()),
            Cooldown = Math.Max(0.01f, cfg.GetValue(section, "shape_cooldown", 1f).AsSingle()),
            SpecialDescription = cfg.GetValue(section, "shape_special_description", string.Empty).AsString(),
            Description = cfg.GetValue(section, "shape_description", string.Empty).AsString(),
        };
    }

    private static RarityData LoadRarity(ConfigFile cfg, string section)
    {
        string key = EquipmentCatalogDefaults.NormalizeRarityKey(cfg.GetValue(section, "rarity_key", DefaultRarityKey).AsString());
        return EquipmentCatalogDefaults.CreateRarityByKey(key);
    }

    private static ModifierData? LoadEssence(ConfigFile cfg, string section)
    {
        if (cfg.HasSectionKey(section, "essence_key") || cfg.GetValue(section, "essence_present", false).AsBool())
        {
            ModifierData essence = LoadModifier(cfg, section, "essence");
            return essence.Key == ModifierKey.Normal ? null : essence;
        }

        if (!cfg.HasSectionKey(section, "modifier_key") && !cfg.HasSectionKey(section, "modifier_key_name"))
        {
            return null;
        }

        ModifierData legacyModifier = LoadModifier(cfg, section, "modifier");
        return legacyModifier.Key == ModifierKey.Normal ? null : legacyModifier;
    }

    private static List<ModifierData> LoadStoredEssences(ConfigFile cfg)
    {
        List<ModifierData> essences = new();
        if (!cfg.HasSection(StoredEssenceSection))
        {
            return essences;
        }

        int count = Mathf.Max(0, cfg.GetValue(StoredEssenceSection, "essence_count", 0).AsInt32());
        for (int i = 0; i < count; i++)
        {
            ModifierData essence = LoadModifier(cfg, StoredEssenceSection, $"essence_{i}");
            if (essence.Key == ModifierKey.Normal)
            {
                continue;
            }

            essences.Add(essence);
        }

        return essences;
    }

    private static List<WeaponModulatorData> LoadModulators(ConfigFile cfg, string section)
    {
        List<WeaponModulatorData> modulators = new();
        int count = Mathf.Max(0, cfg.GetValue(section, "modulator_count", 0).AsInt32());
        for (int i = 0; i < count; i++)
        {
            string key = cfg.GetValue(section, $"modulator_{i}_key", string.Empty).AsString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            int rawTier = cfg.GetValue(section, $"modulator_{i}_tier", (int)WeaponModulatorTier.Baseline).AsInt32();
            WeaponModulatorTier tier = Enum.IsDefined(typeof(WeaponModulatorTier), rawTier)
                ? (WeaponModulatorTier)rawTier
                : WeaponModulatorTier.Baseline;

            if (ModulatorCatalog.TryCreateByKey(key, tier, out WeaponModulatorData defaultModulator))
            {
                modulators.Add(defaultModulator);
                continue;
            }

            WeaponModulatorData modulator = new()
            {
                Key = key,
                BaseDisplayName = cfg.GetValue(
                    section,
                    $"modulator_{i}_base_display",
                    cfg.GetValue(section, $"modulator_{i}_display", ToDisplayName(key)).AsString()).AsString(),
                DisplayName = cfg.GetValue(section, $"modulator_{i}_display", ToDisplayName(key)).AsString(),
                BaseDescription = cfg.GetValue(
                    section,
                    $"modulator_{i}_base_description",
                    cfg.GetValue(section, $"modulator_{i}_description", string.Empty).AsString()).AsString(),
                Description = cfg.GetValue(section, $"modulator_{i}_description", string.Empty).AsString(),
                PowerTier = Enum.IsDefined(typeof(ModulatorPowerTier), cfg.GetValue(section, $"modulator_{i}_power_tier", (int)ModulatorPowerTier.Tier1).AsInt32())
                    ? (ModulatorPowerTier)cfg.GetValue(section, $"modulator_{i}_power_tier", (int)ModulatorPowerTier.Tier1).AsInt32()
                    : ModulatorPowerTier.Tier1,
                Category = Enum.IsDefined(typeof(WeaponModulatorCategory), cfg.GetValue(section, $"modulator_{i}_category", (int)WeaponModulatorCategory.None).AsInt32())
                    ? (WeaponModulatorCategory)cfg.GetValue(section, $"modulator_{i}_category", (int)WeaponModulatorCategory.None).AsInt32()
                    : WeaponModulatorCategory.None,
                Weight = Mathf.Max(0.0001f, cfg.GetValue(section, $"modulator_{i}_weight", 1f).AsSingle()),
                UiColor = LoadColor(cfg, section, $"modulator_{i}_color", Colors.White),
                Substats = new List<SubstatRoll>(),
                Effects = new List<WeaponModulatorEffectData>(),
                Tags = LoadNormalizedTagList(cfg, section, $"modulator_{i}_tag"),
                AllowedWeaponTags = LoadNormalizedTagList(cfg, section, $"modulator_{i}_allowed_weapon_tag"),
                BlockedWeaponTags = LoadNormalizedTagList(cfg, section, $"modulator_{i}_blocked_weapon_tag"),
                Tier = tier,
            };

            int substatCount = Mathf.Max(0, cfg.GetValue(section, $"modulator_{i}_substat_count", 0).AsInt32());
            for (int substatIndex = 0; substatIndex < substatCount; substatIndex++)
            {
                int rawType = cfg.GetValue(section, $"modulator_{i}_substat_{substatIndex}_type", 0).AsInt32();
                if (!Enum.IsDefined(typeof(SubstatType), rawType))
                {
                    continue;
                }

                SubstatType type = (SubstatType)rawType;
                if (type == SubstatType.None)
                {
                    continue;
                }

                float value = cfg.GetValue(section, $"modulator_{i}_substat_{substatIndex}_value", 0f).AsSingle();
                modulator.Substats.Add(new SubstatRoll(type, value));
            }

            int effectCount = Mathf.Max(0, cfg.GetValue(section, $"modulator_{i}_effect_count", 0).AsInt32());
            for (int effectIndex = 0; effectIndex < effectCount; effectIndex++)
            {
                int rawEffectType = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_type", 0).AsInt32();
                if (!Enum.IsDefined(typeof(ModulatorEffectType), rawEffectType))
                {
                    continue;
                }

                int rawTrigger = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_trigger", (int)ModulatorEffectTrigger.Passive).AsInt32();
                ModulatorEffectTrigger trigger = Enum.IsDefined(typeof(ModulatorEffectTrigger), rawTrigger)
                    ? (ModulatorEffectTrigger)rawTrigger
                    : ModulatorEffectTrigger.Passive;

                modulator.Effects.Add(new WeaponModulatorEffectData
                {
                    Type = (ModulatorEffectType)rawEffectType,
                    Trigger = trigger,
                    Magnitude = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_magnitude", 0f).AsSingle(),
                    MagnitudeB = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_magnitude_b", 0f).AsSingle(),
                    MagnitudeC = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_magnitude_c", 0f).AsSingle(),
                    Count = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_count", 0).AsInt32(),
                    CountB = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_count_b", 0).AsInt32(),
                    Key = cfg.GetValue(section, $"modulator_{i}_effect_{effectIndex}_key", string.Empty).AsString(),
                });
            }

            if (modulator.Effects.Count == 0 && modulator.Substats.Count > 0)
            {
                modulator.Effects.AddRange(WeaponModulatorEffectData.FromSubstats(modulator.Substats));
            }

            modulator.RefreshPresentation();

            modulators.Add(modulator);
        }

        return modulators;
    }

    private static ModifierData LoadModifier(ConfigFile cfg, string section, string prefix)
    {
        ModifierKey key = ModifierKey.Normal;
        int rawKey = cfg.GetValue(section, $"{prefix}_key", (int)ModifierKey.Normal).AsInt32();
        if (Enum.IsDefined(typeof(ModifierKey), rawKey))
        {
            key = (ModifierKey)rawKey;
        }
        else
        {
            string fallbackName = cfg.GetValue(section, $"{prefix}_key_name", ModifierKey.Normal.ToString()).AsString();
            if (!Enum.TryParse(fallbackName, true, out key))
            {
                key = string.Equals(fallbackName, "glacial", StringComparison.OrdinalIgnoreCase)
                    ? ModifierKey.Cursed
                    : ModifierKey.Normal;
            }
        }

        int guaranteedSubstatRaw = cfg.GetValue(section, $"{prefix}_guaranteed_substat", (int)SubstatType.None).AsInt32();
        SubstatType guaranteedSubstat = Enum.IsDefined(typeof(SubstatType), guaranteedSubstatRaw)
            ? (SubstatType)guaranteedSubstatRaw
            : SubstatType.None;

        string display = cfg.GetValue(section, $"{prefix}_display", key.ToString()).AsString();

        return new ModifierData
        {
            Key = key,
            DisplayName = string.IsNullOrWhiteSpace(display) ? key.ToString() : display,
            Weight = cfg.GetValue(section, $"{prefix}_weight", 1f).AsSingle(),
            UiColor = LoadColor(cfg, section, $"{prefix}_color", Colors.White),
            HpMultiplier = Math.Max(0.01f, cfg.GetValue(section, $"{prefix}_hp_multiplier", 1f).AsSingle()),
            DamageMultiplier = Math.Max(0.01f, cfg.GetValue(section, $"{prefix}_damage_multiplier", 1f).AsSingle()),
            XpMultiplierBonus = cfg.GetValue(section, $"{prefix}_xp_multiplier_bonus", 0f).AsSingle(),
            PrimaryEffectKey = cfg.GetValue(section, $"{prefix}_primary_effect_key", "none").AsString(),
            PrimaryEffectDescription = cfg.GetValue(section, $"{prefix}_primary_effect_description", "No special effect").AsString(),
            PassiveName = cfg.GetValue(section, $"{prefix}_passive_name", string.Empty).AsString(),
            PassiveDescription = cfg.GetValue(section, $"{prefix}_passive_description", string.Empty).AsString(),
            SynergyName = cfg.GetValue(section, $"{prefix}_synergy_name", string.Empty).AsString(),
            SynergyDescription = cfg.GetValue(section, $"{prefix}_synergy_description", string.Empty).AsString(),
            SuperSynergyName = cfg.GetValue(section, $"{prefix}_super_synergy_name", string.Empty).AsString(),
            SuperSynergyDescription = cfg.GetValue(section, $"{prefix}_super_synergy_description", string.Empty).AsString(),
            GuaranteedSubstat = guaranteedSubstat,
            GrantsAllSubstats = cfg.GetValue(section, $"{prefix}_grants_all_substats", false).AsBool(),
            IsWildcard = cfg.GetValue(section, $"{prefix}_is_wildcard", false).AsBool(),
        };
    }

    private static void SaveModifier(ConfigFile cfg, string section, string prefix, ModifierData modifier)
    {
        cfg.SetValue(section, $"{prefix}_key", (int)modifier.Key);
        cfg.SetValue(section, $"{prefix}_key_name", modifier.Key.ToString());
        cfg.SetValue(section, $"{prefix}_display", modifier.DisplayName);
        cfg.SetValue(section, $"{prefix}_weight", modifier.Weight);
        SaveColor(cfg, section, $"{prefix}_color", modifier.UiColor);
        cfg.SetValue(section, $"{prefix}_hp_multiplier", modifier.HpMultiplier);
        cfg.SetValue(section, $"{prefix}_damage_multiplier", modifier.DamageMultiplier);
        cfg.SetValue(section, $"{prefix}_xp_multiplier_bonus", modifier.XpMultiplierBonus);
        cfg.SetValue(section, $"{prefix}_primary_effect_key", modifier.PrimaryEffectKey);
        cfg.SetValue(section, $"{prefix}_primary_effect_description", modifier.PrimaryEffectDescription);
        cfg.SetValue(section, $"{prefix}_passive_name", modifier.PassiveName);
        cfg.SetValue(section, $"{prefix}_passive_description", modifier.PassiveDescription);
        cfg.SetValue(section, $"{prefix}_synergy_name", modifier.SynergyName);
        cfg.SetValue(section, $"{prefix}_synergy_description", modifier.SynergyDescription);
        cfg.SetValue(section, $"{prefix}_super_synergy_name", modifier.SuperSynergyName);
        cfg.SetValue(section, $"{prefix}_super_synergy_description", modifier.SuperSynergyDescription);
        cfg.SetValue(section, $"{prefix}_guaranteed_substat", (int)modifier.GuaranteedSubstat);
        cfg.SetValue(section, $"{prefix}_grants_all_substats", modifier.GrantsAllSubstats);
        cfg.SetValue(section, $"{prefix}_is_wildcard", modifier.IsWildcard);
    }

    private static void SaveStoredEssences(ConfigFile cfg, IReadOnlyList<ModifierData> essences)
    {
        cfg.SetValue(StoredEssenceSection, "essence_count", essences.Count);
        for (int i = 0; i < essences.Count; i++)
        {
            SaveModifier(cfg, StoredEssenceSection, $"essence_{i}", essences[i]);
        }
    }

    private static void SaveModulator(ConfigFile cfg, string section, int index, WeaponModulatorData modulator)
    {
        modulator.RefreshPresentation();
        cfg.SetValue(section, $"modulator_{index}_key", modulator.Key);
        cfg.SetValue(section, $"modulator_{index}_tier", (int)modulator.Tier);
        cfg.SetValue(section, $"modulator_{index}_power_tier", (int)modulator.PowerTier);
        cfg.SetValue(section, $"modulator_{index}_category", (int)modulator.Category);
        cfg.SetValue(section, $"modulator_{index}_base_display", modulator.BaseDisplayName);
        cfg.SetValue(section, $"modulator_{index}_display", modulator.DisplayName);
        cfg.SetValue(section, $"modulator_{index}_base_description", modulator.BaseDescription);
        cfg.SetValue(section, $"modulator_{index}_description", modulator.Description);
        cfg.SetValue(section, $"modulator_{index}_weight", modulator.Weight);
        SaveColor(cfg, section, $"modulator_{index}_color", modulator.UiColor);
        SaveNormalizedTagList(cfg, section, $"modulator_{index}_tag", modulator.Tags);
        SaveNormalizedTagList(cfg, section, $"modulator_{index}_allowed_weapon_tag", modulator.AllowedWeaponTags);
        SaveNormalizedTagList(cfg, section, $"modulator_{index}_blocked_weapon_tag", modulator.BlockedWeaponTags);
        cfg.SetValue(section, $"modulator_{index}_substat_count", modulator.Substats.Count);
        for (int substatIndex = 0; substatIndex < modulator.Substats.Count; substatIndex++)
        {
            SubstatRoll substat = modulator.Substats[substatIndex];
            cfg.SetValue(section, $"modulator_{index}_substat_{substatIndex}_type", (int)substat.Type);
            cfg.SetValue(section, $"modulator_{index}_substat_{substatIndex}_value", substat.Value);
        }

        cfg.SetValue(section, $"modulator_{index}_effect_count", modulator.Effects.Count);
        for (int effectIndex = 0; effectIndex < modulator.Effects.Count; effectIndex++)
        {
            WeaponModulatorEffectData effect = modulator.Effects[effectIndex];
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_type", (int)effect.Type);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_trigger", (int)effect.Trigger);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_magnitude", effect.Magnitude);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_magnitude_b", effect.MagnitudeB);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_magnitude_c", effect.MagnitudeC);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_count", effect.Count);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_count_b", effect.CountB);
            cfg.SetValue(section, $"modulator_{index}_effect_{effectIndex}_key", effect.Key);
        }
    }

    private static List<string> LoadNormalizedTagList(ConfigFile cfg, string section, string prefix)
    {
        List<string> tags = new();
        int count = Mathf.Max(0, cfg.GetValue(section, $"{prefix}_count", 0).AsInt32());
        for (int i = 0; i < count; i++)
        {
            string tag = cfg.GetValue(section, $"{prefix}_{i}", string.Empty).AsString();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static void SaveNormalizedTagList(ConfigFile cfg, string section, string prefix, IReadOnlyList<string> tags)
    {
        cfg.SetValue(section, $"{prefix}_count", tags.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            cfg.SetValue(section, $"{prefix}_{i}", tags[i]);
        }
    }

    private static void SaveColor(ConfigFile cfg, string section, string prefix, Color color)
    {
        cfg.SetValue(section, $"{prefix}_r", color.R);
        cfg.SetValue(section, $"{prefix}_g", color.G);
        cfg.SetValue(section, $"{prefix}_b", color.B);
        cfg.SetValue(section, $"{prefix}_a", color.A);
    }

    private static Color LoadColor(ConfigFile cfg, string section, string prefix, Color fallback)
    {
        float r = cfg.GetValue(section, $"{prefix}_r", fallback.R).AsSingle();
        float g = cfg.GetValue(section, $"{prefix}_g", fallback.G).AsSingle();
        float b = cfg.GetValue(section, $"{prefix}_b", fallback.B).AsSingle();
        float a = cfg.GetValue(section, $"{prefix}_a", fallback.A).AsSingle();
        return new Color(r, g, b, a);
    }

    private void AutoEquipFromLoadedItems()
    {
        for (int i = 0; i < _equipped.Length; i++)
        {
            _equipped[i] = i < _items.Count ? _items[i] : null;
        }
    }

    private EquipmentItem? FindItemByInstanceId(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].InstanceId, instanceId, StringComparison.Ordinal))
            {
                return _items[i];
            }
        }

        return null;
    }

    private static ShapeData CreateFallbackShape(string key, string displayName)
    {
        ShapeData shape = EquipmentCatalogDefaults.CreateShapeByKey(key);
        shape.DisplayName = string.IsNullOrWhiteSpace(displayName) ? shape.DisplayName : displayName;
        return shape;
    }

    private static RarityData CreateFallbackRarity(string key, string displayName)
    {
        RarityData rarity = EquipmentCatalogDefaults.CreateRarityByKey(key);
        rarity.DisplayName = string.IsNullOrWhiteSpace(displayName) ? rarity.DisplayName : displayName;
        return rarity;
    }

    private static ModifierData CreateFallbackModifier(ModifierKey key, string displayName)
    {
        ModifierData modifier = EquipmentCatalogDefaults.CreateModifierByKey(key, includeBaselineNormal: true);
        modifier.DisplayName = string.IsNullOrWhiteSpace(displayName) ? modifier.DisplayName : displayName;
        return modifier;
    }

    private static ModifierData CloneModifier(ModifierData modifier)
    {
        return new ModifierData
        {
            Key = modifier.Key,
            DisplayName = modifier.DisplayName,
            Weight = modifier.Weight,
            UiColor = modifier.UiColor,
            HpMultiplier = modifier.HpMultiplier,
            DamageMultiplier = modifier.DamageMultiplier,
            XpMultiplierBonus = modifier.XpMultiplierBonus,
            PrimaryEffectKey = modifier.PrimaryEffectKey,
            PrimaryEffectDescription = modifier.PrimaryEffectDescription,
            PassiveName = modifier.PassiveName,
            PassiveDescription = modifier.PassiveDescription,
            SynergyName = modifier.SynergyName,
            SynergyDescription = modifier.SynergyDescription,
            SuperSynergyName = modifier.SuperSynergyName,
            SuperSynergyDescription = modifier.SuperSynergyDescription,
            GuaranteedSubstat = modifier.GuaranteedSubstat,
            GrantsAllSubstats = modifier.GrantsAllSubstats,
            IsWildcard = modifier.IsWildcard,
        };
    }

    private static WeaponModulatorData CloneModulator(WeaponModulatorData modulator)
    {
        List<SubstatRoll> rolls = new(modulator.Substats.Count);
        for (int i = 0; i < modulator.Substats.Count; i++)
        {
            SubstatRoll roll = modulator.Substats[i];
            rolls.Add(new SubstatRoll(roll.Type, roll.Value));
        }

        List<WeaponModulatorEffectData> effects = new(modulator.Effects.Count);
        for (int i = 0; i < modulator.Effects.Count; i++)
        {
            WeaponModulatorEffectData effect = modulator.Effects[i];
            effects.Add(new WeaponModulatorEffectData
            {
                Type = effect.Type,
                Trigger = effect.Trigger,
                Magnitude = effect.Magnitude,
                MagnitudeB = effect.MagnitudeB,
                MagnitudeC = effect.MagnitudeC,
                Count = effect.Count,
                CountB = effect.CountB,
                Key = effect.Key,
            });
        }

        WeaponModulatorData clone = new()
        {
            Key = modulator.Key,
            BaseDisplayName = string.IsNullOrWhiteSpace(modulator.BaseDisplayName) ? modulator.DisplayName : modulator.BaseDisplayName,
            DisplayName = modulator.DisplayName,
            BaseDescription = string.IsNullOrWhiteSpace(modulator.BaseDescription) ? modulator.Description : modulator.BaseDescription,
            Description = modulator.Description,
            PowerTier = modulator.PowerTier,
            Category = modulator.Category,
            Tags = new List<string>(modulator.Tags),
            AllowedWeaponTags = new List<string>(modulator.AllowedWeaponTags),
            BlockedWeaponTags = new List<string>(modulator.BlockedWeaponTags),
            SupportsTier = modulator.SupportsTier,
            Weight = modulator.Weight,
            UiColor = modulator.UiColor,
            Substats = rolls,
            Effects = effects,
            Tier = modulator.Tier,
        };

        clone.RefreshPresentation();
        return clone;
    }

    private static string GetItemSectionName(int index)
    {
        return $"{ItemSectionPrefix}{index}";
    }

    private static bool EnsureSchema(ConfigFile cfg)
    {
        int version = Mathf.Max(0, cfg.GetValue(MetaSection, SchemaVersionKey, 0).AsInt32());
        if (version == CurrentSchemaVersion)
        {
            return false;
        }

        if (version < 5)
        {
            MigrateLegacyRarityLadder(cfg);
        }

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        return true;
    }

    private static void MigrateLegacyRarityLadder(ConfigFile cfg)
    {
        int itemCount = Mathf.Max(0, cfg.GetValue(MetaSection, "count", 0).AsInt32());
        for (int i = 0; i < itemCount; i++)
        {
            string section = GetItemSectionName(i);
            if (!cfg.HasSection(section) || !cfg.HasSectionKey(section, "rarity_key"))
            {
                continue;
            }

            string remappedKey = EquipmentCatalogDefaults.RemapLegacyRarityKey(cfg.GetValue(section, "rarity_key", DefaultRarityKey).AsString());
            cfg.SetValue(section, "rarity_key", remappedKey);
        }
    }

    private static string ToDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        return char.ToUpperInvariant(key[0]) + key[1..];
    }
}
