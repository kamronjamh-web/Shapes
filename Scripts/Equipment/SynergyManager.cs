using System;
using System.Collections.Generic;
using System.Linq;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Equipment;

public sealed class ModifierSynergyState
{
    public ModifierKey ModifierKey { get; init; }
    public int RealCount { get; set; }
    public int EffectiveCount { get; set; }
    public bool PassiveActive { get; set; }
    public bool SynergyActive { get; set; }
    public bool SuperSynergyActive { get; set; }
}

public sealed class SynergySnapshot
{
    public Dictionary<ModifierKey, ModifierSynergyState> States { get; } = new();

    public bool PrismaticPassiveActive { get; set; }
    public bool PrismaticSynergyActive { get; set; }
    public bool PrismaticSuperSynergyActive { get; set; }

    public bool FullSpectrumActive { get; set; }

    public ModifierSynergyState GetState(ModifierKey key)
    {
        if (!States.TryGetValue(key, out ModifierSynergyState? state))
        {
            state = new ModifierSynergyState { ModifierKey = key };
            States[key] = state;
        }

        return state;
    }
}

// Evaluates passive/synergy/super-synergy states from support slots only.
public sealed class SynergyManager
{
    public SynergySnapshot EvaluateSupportSynergies(IReadOnlyList<EquipmentSlot> supportSlots)
    {
        SynergySnapshot snapshot = new();

        Dictionary<ModifierKey, int> realCounts = new();
        int prismaticCount = 0;
        int equippedSupportCount = 0;

        for (int i = 0; i < supportSlots.Count; i++)
        {
            EquipmentItem? item = supportSlots[i].Item;
            if (item?.Modifier == null)
            {
                continue;
            }

            equippedSupportCount++;
            ModifierKey key = item.Modifier.Key;

            if (key == ModifierKey.Prismatic)
            {
                prismaticCount++;
                continue;
            }

            realCounts[key] = realCounts.TryGetValue(key, out int cnt) ? cnt + 1 : 1;
        }

        snapshot.PrismaticPassiveActive = prismaticCount >= 1;
        snapshot.PrismaticSynergyActive = prismaticCount >= 2;

        bool allSupportSlotsFilled = supportSlots.Count >= 4 && supportSlots.Take(4).All(s => s.Item != null);
        bool firstFourAllPrismatic = supportSlots.Count >= 4 && supportSlots.Take(4).All(s => s.Item?.Modifier?.Key == ModifierKey.Prismatic);
        snapshot.FullSpectrumActive = allSupportSlotsFilled && firstFourAllPrismatic;
        snapshot.PrismaticSuperSynergyActive = snapshot.FullSpectrumActive || prismaticCount >= 4;

        foreach (ModifierKey key in Enum.GetValues<ModifierKey>())
        {
            if (key == ModifierKey.Normal || key == ModifierKey.Prismatic)
            {
                continue;
            }

            int real = realCounts.TryGetValue(key, out int count) ? count : 0;
            int effective = 0;
            if (snapshot.FullSpectrumActive)
            {
                effective = 4;
            }
            else if (real > 0)
            {
                effective = real >= 4 ? 4 : Math.Min(real + prismaticCount, 3);
            }

            ModifierSynergyState state = snapshot.GetState(key);
            state.RealCount = real;
            state.EffectiveCount = effective;

            // Passive requires real matching pieces only.
            state.PassiveActive = real >= 1;

            // Prismatic wildcards can reach synergy, but not super, unless Full Spectrum is active.
            state.SynergyActive = effective >= 2;
            state.SuperSynergyActive = snapshot.FullSpectrumActive || real >= 4;
        }

        // Full Spectrum: activate all modifier synergies/supers.
        if (snapshot.FullSpectrumActive)
        {
            foreach (ModifierKey key in Enum.GetValues<ModifierKey>())
            {
                if (key == ModifierKey.Normal || key == ModifierKey.Prismatic)
                {
                    continue;
                }

                ModifierSynergyState state = snapshot.GetState(key);
                state.PassiveActive = true;
                state.EffectiveCount = Math.Max(state.EffectiveCount, 4);
                state.SynergyActive = true;
                state.SuperSynergyActive = true;
            }
        }

        return snapshot;
    }
}
