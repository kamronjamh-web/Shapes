using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.DebugTools;

// Builds custom items for debug forge and pushes them through inventory.
public partial class DebugForgeService : Node
{
    [Export] public NodePath InventoryServicePath = new NodePath();

    private readonly RandomNumberGenerator _rng = new();

    private StatGenerator? _statGenerator;
    private IEquipmentInventoryService? _inventoryService;

    public override void _Ready()
    {
        _rng.Randomize();
        _statGenerator = new StatGenerator(_rng);
        ResolveDependencies();
    }

    public bool TryForgeItem(
        ShapeData? shape,
        RarityData? rarity,
        ModifierData? modifier,
        int level,
        IList<SubstatRoll> requestedSubstats,
        out EquipmentItem? forgedItem,
        out string feedback)
    {
        forgedItem = null;

        if (shape == null || rarity == null || modifier == null)
        {
            feedback = "Missing shape, rarity, or essence selection.";
            return false;
        }

        if (level < 1 || level > EquipmentCatalogDefaults.MaxWeaponLevel)
        {
            feedback = $"Weapon level must be between 1 and {EquipmentCatalogDefaults.MaxWeaponLevel}.";
            return false;
        }

        if (!ValidateSubstats(requestedSubstats, out string substatError))
        {
            feedback = substatError;
            return false;
        }

        _statGenerator ??= new StatGenerator(_rng);

        forgedItem = _statGenerator.BuildItem(shape, rarity, modifier, level);
        if (requestedSubstats.Count > 0)
        {
            List<SubstatRoll> copy = new(requestedSubstats.Count);
            for (int i = 0; i < requestedSubstats.Count; i++)
            {
                SubstatRoll roll = requestedSubstats[i];
                copy.Add(new SubstatRoll(roll.Type, roll.Value));
            }

            forgedItem.Substats = copy;
        }

        if (_inventoryService == null)
        {
            feedback = "Inventory service not connected.";
            return false;
        }

        if (_inventoryService.IsInventoryFull)
        {
            feedback = "Inventory full.";
            return false;
        }

        if (!_inventoryService.TryAddItem(forgedItem))
        {
            feedback = "Failed to add item to inventory.";
            return false;
        }

        _inventoryService.NotifyInventoryChanged();

        feedback = $"Forged {rarity.DisplayName} {shape.DisplayName} ({modifier.DisplayName} essence) L{level}.";
        return true;
    }

    public void CalculatePreview(
        ShapeData? shape,
        RarityData? rarity,
        ModifierData? modifier,
        int level,
        out int hp,
        out int damage)
    {
        hp = 0;
        damage = 0;

        if (shape == null || rarity == null || modifier == null)
        {
            return;
        }

        _statGenerator ??= new StatGenerator(_rng);

        hp = _statGenerator.CalculateHealth(level, shape, rarity, modifier);
        damage = _statGenerator.CalculateDamage(level, shape, rarity, modifier);
    }

    public bool GetSubstatRange(SubstatType type, RarityData? rarity, out float min, out float max)
    {
        return StatGenerator.TryGetSubstatRange(type, rarity, out min, out max);
    }

    public bool TryGrantModulator(
        string? modulatorKey,
        WeaponModulatorTier tier,
        out WeaponModulatorData? grantedModulator,
        out string feedback)
    {
        grantedModulator = null;

        if (string.IsNullOrWhiteSpace(modulatorKey))
        {
            feedback = "Choose a modulator first.";
            return false;
        }

        ResolveDependencies();
        if (_inventoryService == null)
        {
            feedback = "Inventory service not connected.";
            return false;
        }

        grantedModulator = ModulatorCatalog.CreateByKey(modulatorKey, tier);
        if (!_inventoryService.StoreModulator(grantedModulator))
        {
            feedback = "Failed to add modulator to storage.";
            grantedModulator = null;
            return false;
        }

        feedback = $"Added {grantedModulator.DisplayName} to stored modulators.";
        return true;
    }

    public int AddAllShapes(
        IEnumerable<ShapeData> shapePool,
        RarityData rarity,
        ModifierData modifier,
        int level,
        out int failed)
    {
        failed = 0;
        int added = 0;

        foreach (ShapeData shape in shapePool)
        {
            bool success = TryForgeItem(
                shape,
                rarity,
                modifier,
                level,
                Array.Empty<SubstatRoll>(),
                out _,
                out _);

            if (success)
            {
                added++;
            }
            else
            {
                failed++;
                if (_inventoryService?.IsInventoryFull == true)
                {
                    break;
                }
            }
        }

        return added;
    }

    private void ResolveDependencies()
    {
        if (!InventoryServicePath.IsEmpty)
        {
            Node? node = GetNodeOrNull(InventoryServicePath);
            if (node is IEquipmentInventoryService typed)
            {
                _inventoryService = typed;
            }
        }

        _inventoryService ??= GetNodeOrNull("/root/InventoryManager") as IEquipmentInventoryService;
    }

    private static bool ValidateSubstats(IList<SubstatRoll> substats, out string error)
    {
        error = string.Empty;

        HashSet<SubstatType> seen = new();
        for (int i = 0; i < substats.Count; i++)
        {
            SubstatRoll roll = substats[i];

            if (roll.Type == SubstatType.None)
            {
                error = "Substat type cannot be None.";
                return false;
            }

            if (!seen.Add(roll.Type))
            {
                error = "Duplicate substats are not allowed.";
                return false;
            }
        }

        return true;
    }
}

// Placeholder inventory contract for forge/debug services.
public interface IEquipmentInventoryService
{
    bool IsInventoryFull { get; }
    bool TryAddItem(EquipmentItem item);
    bool StoreModulator(WeaponModulatorData? modulator);
    void NotifyInventoryChanged();
}
