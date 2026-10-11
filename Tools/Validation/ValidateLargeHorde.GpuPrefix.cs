using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateLargeHorde
{
    private void ValidateGpuPrefix()
    {
        const int reserved = 4096, prefix = 1024;
        const float dt = 1f / 30f;
        FpsHordeManager fixture = new()
        {
            ProcessMode = ProcessModeEnum.Disabled,
            Settings = new FpsHordeSettings
            {
                Capacity = reserved, SoftCollisionEnabled = true, ContactIterations = 1,
                ContactNeighborBudget = 8, StackingEnabled = true, MaxPileHeight = 12f,
                RollingEnabled = false,
            },
        };
        AddChild(fixture);
        try
        {
            FpsEnemyArchetypeSettings archetype = new() { CollisionRadius = .45f, CollisionHeight = 1f };
            List<FpsHordeHandle> handles = new(700);
            for (int i = 0; i < 700; i++)
            {
                Vector3 position = new((i % 8) * .18f, (i % 3) * .2f, ((i / 8) % 8) * .18f);
                FpsHordeHandle handle = fixture.Spawn(archetype, FpsEnemyRole.Bruiser, position);
                handles.Add(handle);
                fixture.SetGpuProbeMotion(handle, position + Vector3.Up * .1f, Vector3.Right, .15f, -.5f);
            }
            Check(handles.TrueForAll(fixture.IsAlive), "Prefix fixture creates stable slot handles");
            bool removed = true;
            for (int i = 0; i < handles.Count; i++)
            {
                // Retain low slots, holes and a live slot at the far end. Active
                // count deliberately does not describe the required buffer span.
                if (i == 699 || i < 4 || (i >= 640 && i % 2 == 0)) continue;
                fixture.TryGet(handles[i], out FpsHordeEnemySnapshot body);
                removed &= fixture.ApplyDamage(handles[i], body.MaxHealth + 1f);
            }
            Check(removed, "Prefix fixture releases interior slots");
            Check(fixture.HighestLiveSlot == 699 && fixture.ActiveCount < 64,
                "Live slot prefix retains high handles despite a small active count and holes");
            Check(!fixture.IsAlive(handles[500]) && fixture.IsAlive(handles[699]), "Interior hole does not move a high live slot");
            object original = fixture.SaveGpuProbeState();
            int stacked = fixture.StackedEnemyCount;
            float highest = fixture.HighestPileFeet;

            void Reject(Action action, string description, bool nonfinite = false)
            {
                bool rejected = false;
                try { action(); }
                catch (ArgumentException) { rejected = true; }
                catch (InvalidOperationException) when (nonfinite) { rejected = true; }
                Check(rejected, description);
                Check(fixture.CompareGpuProbeState(original, 0f).MaxError == 0
                    && fixture.StackedEnemyCount == stacked && fixture.HighestPileFeet == highest,
                    description + " preserves authoritative positions, pressure, velocity and diagnostics");
            }

            Reject(() => fixture.PackGpuCrowd(new GpuCrowdBody[fixture.ActiveCount], dt),
                "Active-count-sized packing cannot omit a high live slot");
            Reject(() => fixture.PackGpuCrowd(new GpuCrowdBody[699], dt),
                "Packing rejects a prefix ending just before the highest live slot");
            Reject(() => fixture.PackGpuCrowd(new GpuCrowdBody[reserved + 1], dt),
                "Packing rejects buffers beyond reserved capacity");
            Reject(() => fixture.ApplyGpuCrowdResults(new byte[699 * 32]),
                "Result application rejects omitted high live slots before mutation");
            Reject(() => fixture.ApplyGpuCrowdResults(new byte[(reserved + 1) * 32]),
                "Result application rejects buffers beyond reserved capacity");
            Reject(() => fixture.ApplyGpuCrowdResults(new byte[prefix * 32 + 1]),
                "Result application rejects a trailing incomplete result");
            byte[] corrupt = new byte[prefix * 32];
            MemoryMarshal.Cast<byte, GpuCrowdResult>(corrupt.AsSpan())[699].PositionPressure.X = float.NaN;
            Reject(() => fixture.ApplyGpuCrowdResults(corrupt),
                "A nonfinite high-slot result rejects the complete packet before applying earlier live results", nonfinite: true);
            GpuCrowdBody[] prefixBodies = new GpuCrowdBody[prefix];
            fixture.PackGpuCrowd(prefixBodies, dt);
            Check(prefixBodies[699].WorldVelocityAlive.W == 1f && prefixBodies[500].WorldVelocityAlive.W == 0f,
                "Valid prefix preserves live high slots and interior dead flags");
            if (_headless) return;

            fixture.RunCpuProbeContacts(dt);
            object expected = fixture.SaveGpuProbeState();
            fixture.RestoreGpuProbeState(original);
            using GpuCrowdKernel shortKernel = new(prefix, gridCapacity: reserved);
            using GpuCrowdKernel fullKernel = new(reserved);
            Check(shortKernel.Capacity == prefix && shortKernel.Buckets == fullKernel.Buckets
                && shortKernel.Buckets == 16384,
                "Prefix kernel retains the reserved CPU hash grid and bounded-neighbor traversal");
            GpuCrowdParameters parameters = fixture.PackGpuCrowd(prefixBodies, dt);
            shortKernel.Upload(prefixBodies);
            byte[] prefixOutput = shortKernel.Run(parameters).Output;
            fixture.ApplyGpuCrowdResults(prefixOutput);
            Check(fixture.CompareGpuProbeState(expected).Compared == fixture.ActiveCount,
                "Prefix GPU contacts match CPU contacts with high slots and holes");
            object prefixExpected = fixture.SaveGpuProbeState();
            fixture.RestoreGpuProbeState(original);
            GpuCrowdBody[] fullBodies = new GpuCrowdBody[reserved];
            parameters = fixture.PackGpuCrowd(fullBodies, dt);
            fullKernel.Upload(fullBodies);
            fixture.ApplyGpuCrowdResults(fullKernel.Run(parameters).Output);
            Check(fixture.CompareGpuProbeState(expected).Compared == fixture.ActiveCount,
                "Full GPU contacts match the same reserved-grid CPU contacts");
            Check(fixture.CompareGpuProbeState(prefixExpected, 0f).MaxError == 0,
                "Prefix and full GPU buffers produce identical live results under the same hash grid");
        }
        finally
        {
            RemoveChild(fixture);
            fixture.Free();
        }
    }
}
