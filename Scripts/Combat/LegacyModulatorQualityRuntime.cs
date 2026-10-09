using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
    private sealed class LegacyCountCarry { public readonly Dictionary<string, float> Values = new(); }
    // Sources can disappear without retaining their items or creating an unbounded strong-key cache.
    private readonly ConditionalWeakTable<EquipmentItem, LegacyCountCarry> _legacyQualityCounts = new();
    private readonly LegacyCountCarry _legacyDefaultCounts = new();

    private int ResolveLegacyQualityCount(EquipmentItem? source, string purpose, float desired)
    {
        LegacyCountCarry state = source == null ? _legacyDefaultCounts : _legacyQualityCounts.GetValue(source, static _ => new LegacyCountCarry());
        desired = float.IsFinite(desired) ? Mathf.Clamp(desired, 0f, 1024f) : 0f;
        if (desired <= 0f) { state.Values.Remove(purpose); return 0; }
        state.Values.TryGetValue(purpose, out float carry);
        float accumulated = desired + carry;
        int count = Mathf.FloorToInt(accumulated + .000001f);
        state.Values[purpose] = Mathf.Max(0f, accumulated - count);
        return count;
    }

    private void ResetLegacyQualityCounts()
    {
        _legacyQualityCounts.Clear();
        _legacyDefaultCounts.Values.Clear();
    }

    private static WeaponModulatorEffectData? StrongestLegacyEffect(EquipmentItem? source, ModulatorEffectType type, string? key = null)
    {
        WeaponModulatorEffectData? result = null;
        SuperGatchaRebuild.Combat.WeaponModulatorSharedRuntime.ForEachResolvedEffect(source, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == type && (key == null || WeaponModulatorData.NormalizeKey(effect.Key) == key)
                && (result == null || effect.QualityStrength >= result.QualityStrength)) result = effect;
        });
        return result;
    }
}
