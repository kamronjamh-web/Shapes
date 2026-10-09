using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

/// <summary>Run-local physical socket identity without nullable combat or save lists.</summary>
public static class FpsWeaponSocketLayout
{
    public const int SocketCount = 10;
    private sealed class Layout
    {
        public readonly WeaponModulatorData?[] Slots = new WeaponModulatorData?[SocketCount];
        public readonly ReadOnlyCollection<WeaponModulatorData?> View;
        public Layout() => View = Array.AsReadOnly(Slots);
    }
    private static readonly ConditionalWeakTable<EquipmentItem, Layout> Layouts = new();

    public static IReadOnlyList<WeaponModulatorData?> GetSlots(EquipmentItem weapon)
        => Reconcile(weapon).View;

    private static Layout Reconcile(EquipmentItem weapon)
    {
        Layout layout = Layouts.GetOrCreateValue(weapon);
        for (int slot = 0; slot < SocketCount; slot++)
            if (layout.Slots[slot] is { } part && !weapon.Modulators.Exists(candidate => ReferenceEquals(candidate, part)))
                layout.Slots[slot] = null;
        foreach (WeaponModulatorData part in weapon.Modulators)
        {
            if (Array.Exists(layout.Slots, candidate => ReferenceEquals(candidate, part))) continue;
            int empty = Array.FindIndex(layout.Slots, candidate => candidate == null);
            if (empty >= 0) layout.Slots[empty] = part;
        }
        return layout;
    }

    internal static void Assign(EquipmentItem weapon, int slot, WeaponModulatorData part)
    {
        // Reserve the physical target before adding to the dense gameplay list.
        Reconcile(weapon).Slots[slot] = part;
    }

    internal static void Clear(EquipmentItem weapon, int slot) => Reconcile(weapon).Slots[slot] = null;
}
