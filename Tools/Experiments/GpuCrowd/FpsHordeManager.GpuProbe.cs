using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

// State capture and fixture helpers for GPU validation and benchmark scenes.
// Production packing and result application live in FpsHordeManager.GpuPacking.cs.
public partial class FpsHordeManager
{
    internal object SaveGpuProbeState() => _enemies.Clone();

    internal void RestoreGpuProbeState(object state)
    {
        ((Enemy[])state).CopyTo(_enemies, 0);
        RebuildAliveSlotBookkeeping();
        _gridDirty = true;
        MarkRenderStateDirty();
    }

    internal void RunCpuProbeContacts(float dt) { SolveCrowdContacts(dt); MarkRenderStateDirty(); }

    internal Vector3 ResolveGpuProbeWorld(Vector3 from, Vector3 desired, float radius, float height)
        => ResolveBodyWorldPosition(from, desired, radius, height);

    internal void SetGpuProbeMotion(FpsHordeHandle handle, Vector3 previous, Vector3 drive, float pressure = 0f, float verticalVelocity = 0f)
    {
        if (!IsAlive(handle)) throw new ArgumentException("Probe motion requires a live handle.");
        ref Enemy e = ref _enemies[handle.Slot];
        e.PreviousPosition = previous;
        e.DesiredVelocity = drive;
        e.ContactPressure = pressure;
        e.VerticalVelocity = verticalVelocity;
        MarkRenderStateDirty();
    }

    internal void AdvanceGpuProbePreviousPositions()
    {
        foreach (int i in EnumerateAliveSlots())
            if (_enemies[i].Alive) _enemies[i].PreviousPosition = _enemies[i].Position;
        MarkRenderStateDirty();
    }

    internal (int Compared, double MaxError) CompareGpuProbeState(object expected, float tolerance = 0.001f)
    {
        Enemy[] reference = (Enemy[])expected;
        double maxError = 0;
        int compared = 0;
        for (int slot = 0; slot < _enemies.Length; slot++)
        {
            ref Enemy e = ref _enemies[slot];
            ref Enemy other = ref reference[slot];
            float error = Mathf.Max(e.Position.DistanceTo(other.Position),
                Mathf.Max(Mathf.Abs(e.ContactPressure - other.ContactPressure), Mathf.Abs(e.VerticalVelocity - other.VerticalVelocity)));
            if (!float.IsFinite(error) || error > tolerance)
                throw new InvalidOperationException($"GPU/CPU mismatch slot {slot}: error={error}, GPU={e.Position}, CPU={other.Position}; pressure {e.ContactPressure}/{other.ContactPressure}; vertical {e.VerticalVelocity}/{other.VerticalVelocity}.");
            maxError = Math.Max(maxError, error);
            if (e.Alive) compared++;
        }
        return (compared, maxError);
    }
}
