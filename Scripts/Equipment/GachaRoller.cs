using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Equipment;

// Rolls rarity, shape, and modifier independently, then builds a runtime item.
public sealed class GachaRoller
{
    private readonly RandomNumberGenerator _rng;
    private readonly StatGenerator _statGenerator;
    private DebugManager? _debugManager;

    public GachaRoller(RandomNumberGenerator rng, DebugManager? debugManager = null)
    {
        _rng = rng;
        _statGenerator = new StatGenerator(rng);
        _debugManager = debugManager;
    }

    public void SetDebugManager(DebugManager? debugManager)
    {
        _debugManager = debugManager;
    }

    public EquipmentItem RollItem(
        IReadOnlyList<ShapeData> shapes,
        IReadOnlyList<RarityData> rarities,
        IReadOnlyList<ModifierData> modifiers,
        int level)
    {
        _ = level;

        ShapeData shape = TryGetForcedShape(shapes) ?? RollShape(shapes);
        RarityData rarity = TryGetForcedRarity(rarities) ?? RollRarity(rarities);
        ModifierData modifier = TryGetForcedModifier(modifiers) ?? RollModifier(modifiers);

        return _statGenerator.BuildItem(shape, rarity, modifier, 1);
    }

    public RarityData RollRarity(IReadOnlyList<RarityData> rarities)
    {
        return WeightedRandom.Pick(rarities, r => r.Weight, _rng);
    }

    public ShapeData RollShape(IReadOnlyList<ShapeData> shapes)
    {
        return WeightedRandom.Pick(shapes, s => s.Weight, _rng);
    }

    public ModifierData RollModifier(IReadOnlyList<ModifierData> modifiers)
    {
        return WeightedRandom.Pick(modifiers, m => m.Weight, _rng);
    }

    private RarityData? TryGetForcedRarity(IReadOnlyList<RarityData> rarities)
    {
        if (_debugManager == null || !_debugManager.HasForcedRarity())
        {
            return null;
        }

        string key = _debugManager.ForcedRarityKey;
        for (int i = 0; i < rarities.Count; i++)
        {
            if (string.Equals(rarities[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return rarities[i];
            }
        }

        return null;
    }

    private ShapeData? TryGetForcedShape(IReadOnlyList<ShapeData> shapes)
    {
        if (_debugManager == null || !_debugManager.HasForcedShape())
        {
            return null;
        }

        string key = _debugManager.ForcedShapeKey;
        for (int i = 0; i < shapes.Count; i++)
        {
            if (string.Equals(shapes[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return shapes[i];
            }
        }

        return null;
    }

    private ModifierData? TryGetForcedModifier(IReadOnlyList<ModifierData> modifiers)
    {
        if (_debugManager == null || !_debugManager.HasForcedModifier())
        {
            return null;
        }

        ModifierKey key = _debugManager.ForcedModifierKey!.Value;
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].Key == key)
            {
                return modifiers[i];
            }
        }

        return null;
    }
}
