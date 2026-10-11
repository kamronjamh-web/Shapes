using System;
using System.Runtime.InteropServices;
using Godot;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    private void ValidateGpuCrowdPrefix(int prefixLength)
    {
        if (prefixLength < 0 || prefixLength > _enemies.Length || HighestLiveSlot >= prefixLength)
            throw new ArgumentException("GPU buffer must cover every live slot within the crowd capacity.");
    }

    internal GpuCrowdParameters PackGpuCrowd(GpuCrowdBody[] bodies, float dt, bool worldResolved = false, bool deferredMovement = false)
    {
        if (Settings.ContactIterations != 1 || !Settings.SoftCollisionEnabled)
            throw new InvalidOperationException("GPU contacts require one enabled contact pass.");
        if (deferredMovement && (!worldResolved || _gpuMovementPending.Length != Capacity))
            throw new ArgumentException("Deferred movement packing requires a prepared GPU world.");
        ValidateGpuCrowdPrefix(bodies.Length);
        float maxHeight = 0f;
        for (int i = 0; i < bodies.Length; i++)
        {
            ref Enemy e = ref _enemies[i];
            bool pending = deferredMovement && e.Alive && _gpuMovementPending[i];
            Vector3 position = pending ? _gpuMovementProposals[i] : e.Position;
            float ground = e.Alive && !worldResolved ? SurfaceHeight?.Invoke(e.Position) ?? 0f : 0f;
            float support = e.Alive && !worldResolved ? SurfaceSupportHeight?.Invoke(e.Position, e.Position, e.Radius, e.Height) ?? ground : ground;
            // Write the reusable storage directly. Avoid four constructor calls and
            // a 64-byte temporary/copy per slot in this CPU-to-GPU boundary loop.
            ref GpuCrowdBody packed = ref bodies[i];
            packed.PositionRadius.X = position.X;
            packed.PositionRadius.Y = position.Y;
            packed.PositionRadius.Z = position.Z;
            packed.PositionRadius.W = e.Radius;
            packed.PreviousHeight.X = e.PreviousPosition.X;
            packed.PreviousHeight.Y = e.PreviousPosition.Y;
            packed.PreviousHeight.Z = e.PreviousPosition.Z;
            packed.PreviousHeight.W = e.Height;
            packed.DrivePressure.X = e.DesiredVelocity.X;
            packed.DrivePressure.Y = e.DesiredVelocity.Y;
            packed.DrivePressure.Z = e.DesiredVelocity.Z;
            packed.DrivePressure.W = e.ContactPressure;
            // Phase 5 uses X as a pending-movement marker only with a GPU world.
            packed.WorldVelocityAlive.X = pending ? 1f : ground;
            packed.WorldVelocityAlive.Y = support;
            packed.WorldVelocityAlive.Z = e.VerticalVelocity;
            packed.WorldVelocityAlive.W = e.Alive ? 1f : 0f;
            if (e.Alive) maxHeight = Mathf.Max(maxHeight, e.Height);
        }
        return new GpuCrowdParameters
        {
            HorizontalCell = Mathf.Max(1f, _maxRadius * 2f), VerticalCell = Mathf.Max(1f, maxHeight),
            MaxRadius = _maxRadius, MaxHeight = maxHeight, Dt = dt,
            Stiffness = 1f - Mathf.Exp(-Mathf.Clamp(Settings.ContactStiffness, 0.05f, 1f) * 60f * dt),
            ClimbStep = Mathf.Clamp(Settings.ClimbSpeed, 0f, 8f) * dt,
            MaxPile = Mathf.Clamp(Settings.MaxPileHeight, 0f, 12f),
            Stacking = Settings.StackingEnabled && Settings.MaxPileHeight > 0f ? 1u : 0u,
            Budget = (uint)Math.Clamp(Settings.ContactNeighborBudget, 8, 64), MaxPush = Mathf.Max(0.04f, 6f * dt),
        };
    }

    private void ValidateGpuCrowdResults(byte[] output, bool worldResolved)
    {
        if (output.Length % Marshal.SizeOf<GpuCrowdResult>() != 0)
            throw new ArgumentException("GPU output must contain complete results.");
        ReadOnlySpan<GpuCrowdResult> results = MemoryMarshal.Cast<byte, GpuCrowdResult>(output);
        ValidateGpuCrowdPrefix(results.Length);
        // Validate the complete live result before touching authoritative state.
        // A failed GPU dispatch can then safely fall back to the CPU for this tick.
        foreach (int slot in EnumerateAliveSlots())
        {
            if (!_enemies[slot].Alive) continue;
            ref readonly GpuCrowdResult result = ref results[slot];
            if (!float.IsFinite(result.PositionPressure.X) || !float.IsFinite(result.PositionPressure.Y)
                || !float.IsFinite(result.PositionPressure.Z) || !float.IsFinite(result.PositionPressure.W)
                || !float.IsFinite(result.VelocityCounts.X) || (worldResolved && !float.IsFinite(result.VelocityCounts.W)))
                throw new InvalidOperationException($"Nonfinite GPU result in slot {slot}.");
        }
    }

    internal void ApplyGpuCrowdResults(byte[] output, bool worldResolved = false)
    {
        long started = BeginGpuBoundaryTiming();
        ValidateGpuCrowdResults(output, worldResolved);
        if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { ValidateMs = _gpuBoundaryTiming.ValidateMs + EndGpuBoundaryTiming(started) };
        started = BeginGpuBoundaryTiming();
        ReadOnlySpan<GpuCrowdResult> results = MemoryMarshal.Cast<byte, GpuCrowdResult>(output);
        StackedEnemyCount = 0;
        HighestPileFeet = 0f;
        foreach (int slot in EnumerateAliveSlots())
        {
            // Legacy world callbacks may spawn a later slot while applying a
            // probe packet. Its state belongs to the next contact pass.
            if (slot >= results.Length) break;
            ref Enemy e = ref _enemies[slot];
            if (!e.Alive) continue;
            ref readonly GpuCrowdResult result = ref results[slot];
            Vector3 next = new(result.PositionPressure.X, result.PositionPressure.Y, result.PositionPressure.Z);
            e.ContactPressure = result.PositionPressure.W;
            e.VerticalVelocity = result.VelocityCounts.X;
            if (worldResolved) e.Position = next;
            else if (next != e.Position) e.Position = ResolveBodyWorldPosition(e.Position, next, e.Radius, e.Height);
            float lift = e.Position.Y - (worldResolved ? result.VelocityCounts.W : SurfaceHeight?.Invoke(e.Position) ?? 0f);
            if (lift > 0.2f) StackedEnemyCount++;
            HighestPileFeet = Mathf.Max(HighestPileFeet, lift);
        }
        _gridDirty = true;
        MarkRenderStateDirty();
        if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { ApplyMs = _gpuBoundaryTiming.ApplyMs + EndGpuBoundaryTiming(started) };
    }

    private void PackGpuCrowdDrive(GpuCrowdDriveState[] states, GpuCrowdBody[] bodies)
    {
        foreach (int slot in EnumerateAliveSlots())
        {
            ref Enemy e = ref _enemies[slot];
            ref GpuCrowdDriveState state = ref states[slot];
            state.TargetPlayer.X = e.Target.X; state.TargetPlayer.Y = e.Target.Y; state.TargetPlayer.Z = e.Target.Z; state.TargetPlayer.W = e.TargetsPlayer ? 1f : 0f;
            state.TimersYaw.X = e.AttackTimer; state.TimersYaw.Y = e.DecisionTimer; state.TimersYaw.Z = e.Yaw; state.TimersYaw.W = 0f;
            state.Configuration.X = e.Speed; state.Configuration.Y = e.Range; state.Configuration.Z = e.DesiredRange; state.Configuration.W = e.Role == FpsEnemyRole.Bruiser ? 1f : 0f;
            state.Gait.X = e.RollDistance; state.Gait.Y = e.RollPause; state.Gait.Z = e.RollNextPause; state.Gait.W = e.RollUnusedSeconds;
            state.Orientation.X = e.RollOrientation.X; state.Orientation.Y = e.RollOrientation.Y; state.Orientation.Z = e.RollOrientation.Z; state.Orientation.W = e.RollOrientation.W;
            state.PreviousOrientation.X = e.PreviousRollOrientation.X; state.PreviousOrientation.Y = e.PreviousRollOrientation.Y; state.PreviousOrientation.Z = e.PreviousRollOrientation.Z; state.PreviousOrientation.W = e.PreviousRollOrientation.W;
            state.Velocity.X = e.DesiredVelocity.X; state.Velocity.Y = e.DesiredVelocity.Y; state.Velocity.Z = e.DesiredVelocity.Z; state.Velocity.W = 0f;
            // Full drive owns the previous-position snapshot; CPU state is unchanged
            // until both result packets pass validation.
            bodies[slot].PreviousHeight.X = e.Position.X;
            bodies[slot].PreviousHeight.Y = e.Position.Y;
            bodies[slot].PreviousHeight.Z = e.Position.Z;
        }
    }

    private void ValidateGpuCrowdFullResults(byte[] contacts, byte[] drive)
    {
        if (drive.Length % Marshal.SizeOf<GpuCrowdDriveState>() != 0) throw new ArgumentException("Incomplete GPU drive state.");
        if (contacts.Length % Marshal.SizeOf<GpuCrowdResult>() != 0) throw new ArgumentException("Incomplete GPU contact result.");
        ReadOnlySpan<GpuCrowdDriveState> states = MemoryMarshal.Cast<byte, GpuCrowdDriveState>(drive);
        ReadOnlySpan<GpuCrowdResult> results = MemoryMarshal.Cast<byte, GpuCrowdResult>(contacts);
        ValidateGpuCrowdPrefix(states.Length);
        ValidateGpuCrowdPrefix(results.Length);
        foreach (int slot in EnumerateAliveSlots())
        {
            ref readonly GpuCrowdDriveState state = ref states[slot];
            if (!float.IsFinite(state.TargetPlayer.X) || !float.IsFinite(state.TargetPlayer.Y) || !float.IsFinite(state.TargetPlayer.Z) || !float.IsFinite(state.TargetPlayer.W)
                || !float.IsFinite(state.TimersYaw.X) || !float.IsFinite(state.TimersYaw.Y) || !float.IsFinite(state.TimersYaw.Z) || !float.IsFinite(state.TimersYaw.W)
                || !float.IsFinite(state.Configuration.X) || !float.IsFinite(state.Configuration.Y) || !float.IsFinite(state.Configuration.Z) || !float.IsFinite(state.Configuration.W)
                || !float.IsFinite(state.Gait.X) || !float.IsFinite(state.Gait.Y) || !float.IsFinite(state.Gait.Z) || !float.IsFinite(state.Gait.W)
                || !float.IsFinite(state.Orientation.X) || !float.IsFinite(state.Orientation.Y) || !float.IsFinite(state.Orientation.Z) || !float.IsFinite(state.Orientation.W)
                || !float.IsFinite(state.PreviousOrientation.X) || !float.IsFinite(state.PreviousOrientation.Y) || !float.IsFinite(state.PreviousOrientation.Z) || !float.IsFinite(state.PreviousOrientation.W)
                || !float.IsFinite(state.Velocity.X) || !float.IsFinite(state.Velocity.Y) || !float.IsFinite(state.Velocity.Z) || !float.IsFinite(state.Velocity.W)
                || state.Orientation.X * state.Orientation.X + state.Orientation.Y * state.Orientation.Y + state.Orientation.Z * state.Orientation.Z + state.Orientation.W * state.Orientation.W < .5f
                || state.PreviousOrientation.X * state.PreviousOrientation.X + state.PreviousOrientation.Y * state.PreviousOrientation.Y + state.PreviousOrientation.Z * state.PreviousOrientation.Z + state.PreviousOrientation.W * state.PreviousOrientation.W < .5f)
                throw new InvalidOperationException($"Invalid GPU drive result in slot {slot}.");
            ref readonly GpuCrowdResult result = ref results[slot];
            if (!float.IsFinite(result.PositionPressure.X) || !float.IsFinite(result.PositionPressure.Y)
                || !float.IsFinite(result.PositionPressure.Z) || !float.IsFinite(result.PositionPressure.W)
                || !float.IsFinite(result.VelocityCounts.X) || !float.IsFinite(result.VelocityCounts.W))
                throw new InvalidOperationException($"Nonfinite GPU contact result in slot {slot}.");
        }
    }

    private void ApplyGpuCrowdFullResults(byte[] contacts, byte[] drive)
    {
        long started = BeginGpuBoundaryTiming();
        // Neither authoritative fields nor diagnostics change until every live
        // entry in BOTH packets has passed validation, including late slots.
        ValidateGpuCrowdFullResults(contacts, drive);
        if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { ValidateMs = _gpuBoundaryTiming.ValidateMs + EndGpuBoundaryTiming(started) };
        started = BeginGpuBoundaryTiming();
        ReadOnlySpan<GpuCrowdDriveState> states = MemoryMarshal.Cast<byte, GpuCrowdDriveState>(drive);
        ReadOnlySpan<GpuCrowdResult> results = MemoryMarshal.Cast<byte, GpuCrowdResult>(contacts);
        StackedEnemyCount = 0;
        HighestPileFeet = 0f;
        foreach (int slot in EnumerateAliveSlots())
        {
            ref Enemy e = ref _enemies[slot];
            ref readonly GpuCrowdDriveState state = ref states[slot];
            ref readonly GpuCrowdResult result = ref results[slot];
            e.PreviousPosition.X = e.Position.X; e.PreviousPosition.Y = e.Position.Y; e.PreviousPosition.Z = e.Position.Z;
            e.Target.X = state.TargetPlayer.X; e.Target.Y = state.TargetPlayer.Y; e.Target.Z = state.TargetPlayer.Z;
            e.TargetsPlayer = state.TargetPlayer.W != 0f;
            e.AttackTimer = state.TimersYaw.X; e.DecisionTimer = state.TimersYaw.Y; e.Yaw = state.TimersYaw.Z;
            e.RollDistance = state.Gait.X; e.RollPause = state.Gait.Y; e.RollNextPause = state.Gait.Z; e.RollUnusedSeconds = state.Gait.W;
            e.RollOrientation.X = state.Orientation.X; e.RollOrientation.Y = state.Orientation.Y; e.RollOrientation.Z = state.Orientation.Z; e.RollOrientation.W = state.Orientation.W;
            e.PreviousRollOrientation.X = state.PreviousOrientation.X; e.PreviousRollOrientation.Y = state.PreviousOrientation.Y; e.PreviousRollOrientation.Z = state.PreviousOrientation.Z; e.PreviousRollOrientation.W = state.PreviousOrientation.W;
            e.DesiredVelocity.X = state.Velocity.X; e.DesiredVelocity.Y = state.Velocity.Y; e.DesiredVelocity.Z = state.Velocity.Z;
            e.Position.X = result.PositionPressure.X; e.Position.Y = result.PositionPressure.Y; e.Position.Z = result.PositionPressure.Z;
            e.ContactPressure = result.PositionPressure.W;
            e.VerticalVelocity = result.VelocityCounts.X;
            float lift = e.Position.Y - result.VelocityCounts.W;
            if (lift > .2f) StackedEnemyCount++;
            HighestPileFeet = Math.Max(HighestPileFeet, lift);
        }
        _gridDirty = true;
        MarkRenderStateDirty();
        if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { ApplyMs = _gpuBoundaryTiming.ApplyMs + EndGpuBoundaryTiming(started) };
    }

}
