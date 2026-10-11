using System.Collections.Generic;
using System.Linq;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateLargeHorde
{
    private void ValidateActiveSlots()
    {
        FpsHordeManager fixture = new()
        {
            ProcessMode = ProcessModeEnum.Disabled,
            Settings = new FpsHordeSettings
            {
                Capacity = 256, SimulationHz = 5f, GpuCrowdContactsEnabled = false,
                RollingEnabled = false, SoftCollisionEnabled = true, ContactIterations = 1,
            },
        };
        AddChild(fixture);
        try
        {
            FpsEnemyArchetypeSettings archetype = new()
            {
                CollisionRadius = .1f, CollisionHeight = .2f, MoveSpeed = 0f,
                // Newly spawned callback bodies must already be attack-ready:
                // they missed this step's movement/timer phase by design.
                AttackRange = 1000f, AttackCooldownSeconds = 0f,
            };
            List<FpsHordeHandle> handles = new();
            for (int slot = 0; slot < 194; slot++)
                handles.Add(fixture.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(slot * .3f, 0f, 0f)));
            int[] retained = { 0, 63, 64, 127, 128, 191, 192, 193 };
            for (int slot = 0; slot < handles.Count; slot++)
                if (!retained.Contains(slot)) fixture.ApplyDamage(handles[slot], 1000000f);
            fixture.FlushDeathEvents();
            Check(Slots().SequenceEqual(retained) && fixture.ActiveCount == retained.Length && fixture.HighestLiveSlot == 193,
                "Live occupancy skips sparse holes and enumerates both sides of each 64-bit boundary in ascending order");

            fixture.ApplyDamage(handles[193], 1000000f);
            Check(fixture.HighestLiveSlot == 192, "Highest-slot death retains the adjacent live slot");
            fixture.ApplyDamage(handles[192], 1000000f);
            Check(fixture.HighestLiveSlot == 191, "Highest-slot death crosses an empty word boundary");
            fixture.ApplyDamage(handles[191], 1000000f);
            Check(fixture.HighestLiveSlot == 128, "Highest-slot death skips an empty span of slots");
            FpsHordeHandle reused = fixture.Spawn(archetype, FpsEnemyRole.Striker, new Vector3(60f, 0f, 0f));
            Check(reused.Slot == 191 && fixture.HighestLiveSlot == 191 && !fixture.IsAlive(handles[191]),
                "High slot reuse updates the cache and invalidates its former generation");
            fixture.FlushDeathEvents();

            int[] beforeRestore = Slots().ToArray();
            int countBeforeRestore = fixture.ActiveCount;
            fixture.TryGet(handles[64], out var original);
            object saved = fixture.SaveGpuProbeState();
            fixture.TrySetPosition(handles[64], new Vector3(20f, 0f, 10f));
            fixture.RestoreGpuProbeState(saved);
            Check(Slots().SequenceEqual(beforeRestore) && fixture.ActiveCount == countBeforeRestore
                && fixture.HighestLiveSlot == 191 && fixture.TryGet(handles[64], out var restored) && restored.Position == original.Position,
                "Same-population probe restoration preserves occupancy, cached highest and authoritative positions");

            fixture.ApplyDamage(handles[63], 1000000f);
            fixture.ApplyDamage(handles[64], 1000000f);
            FpsHordeHandle interim = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, Vector3.Zero);
            fixture.FlushDeathEvents();
            Check(interim.Slot == 64 && fixture.ActiveCount == countBeforeRestore - 1,
                "Changed-population snapshot fixture has a missing slot and a recycled generation");
            fixture.RestoreGpuProbeState(saved);
            Check(Slots().SequenceEqual(beforeRestore) && fixture.ActiveCount == countBeforeRestore
                && fixture.HighestLiveSlot == 191 && fixture.IsAlive(handles[63]) && fixture.IsAlive(handles[64]) && !fixture.IsAlive(interim),
                "Changed-population probe restoration reconciles live words, count, highest and generations");
            FpsHordeHandle afterRestore = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, Vector3.Zero);
            Check(!beforeRestore.Contains(afterRestore.Slot) && fixture.IsAlive(handles[63]) && fixture.IsAlive(handles[64])
                && Slots().Count == countBeforeRestore + 1,
                "Restored free stack cannot recycle a resurrected live slot");

            fixture.Clear();
            Check(fixture.ActiveCount == 0 && fixture.HighestLiveSlot == -1 && Slots().Count == 0,
                "Clear empties all live words and cached highest");
            handles.Clear();
            for (int slot = 0; slot < 129; slot++)
                handles.Add(fixture.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(slot * .3f, 0f, 0f)));
            int[] attackSlots = { 0, 63, 64, 127, 128 };
            for (int slot = 0; slot < handles.Count; slot++)
                if (!attackSlots.Contains(slot)) fixture.ApplyDamage(handles[slot], 1000000f);
            fixture.FlushDeathEvents();
            Check(handles[0].Slot == 0 && !fixture.IsAlive(original.Handle) && fixture.HighestLiveSlot == 128,
                "Respawn after Clear rebuilds ascending live occupancy and invalidates cleared handles");

            List<FpsHordeHandle> attacks = new();
            FpsHordeHandle new64 = FpsHordeHandle.Invalid, new128 = FpsHordeHandle.Invalid;
            fixture.Attack += attack =>
            {
                attacks.Add(attack.Handle);
                if (attack.Handle.Slot != 0) return;
                fixture.ApplyDamage(handles[63], 1000000f);
                fixture.ApplyDamage(handles[128], 1000000f);
                fixture.ApplyDamage(handles[64], 1000000f);
                new64 = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, Vector3.Zero);
                new128 = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, Vector3.Zero);
            };
            // The float 1/5 step is slightly larger than the double literal .2.
            fixture.TickSimulation(.21);
            Check(attacks.Select(a => a.Slot).SequenceEqual(new[] { 0, 64, 127, 128 })
                && attacks[1] == new64 && attacks[3] == new128
                && new64.Generation != handles[64].Generation && new128.Generation != handles[128].Generation,
                "Actual attack callbacks skip killed later slots and visit recycled later slots in ascending order across words");
            Check(Slots().SequenceEqual(new[] { 0, 64, 127, 128 }) && fixture.ActiveCount == 4 && fixture.HighestLiveSlot == 128,
                "Reentrant kills and spawns retain consistent final live words, count and highest");

            List<int> Slots()
            {
                List<int> slots = new();
                foreach (int slot in fixture.EnumerateAliveSlots()) slots.Add(slot);
                return slots;
            }
        }
        finally
        {
            RemoveChild(fixture);
            fixture.Free();
        }
    }
}
