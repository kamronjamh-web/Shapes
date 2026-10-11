using System;
using Godot;
using SuperGatchaRebuild.Fps.Physics;

namespace SuperGatchaRebuild.Fps;

public partial class FpsEnemyCorpsePool
{
    internal Func<CorpseWorldSnapshot?>? WorkerWorldProvider { get; set; }
    public bool UsingWorker => _corpseWorker != null;
    internal bool WorkerReady => _corpseWorker?.IsReady == true;
    internal CorpseFrameInfo WorkerFrame { get; private set; }
    internal int WorkerPublishedCount { get; private set; }
    internal string CorpseBackendStatus { get; private set; } = "Godot physics";
    internal int SleepingCorpseCount
    {
        get { int count = 0; foreach (Corpse corpse in _slots) if (corpse.Active && corpse.WasSleeping) count++; return count; }
    }
    internal int WorkerPendingCommandCount
    {
        get { int count = 0; foreach (bool pending in _workerDirty) if (pending) count++; return count; }
    }
    private Box3dCorpseWorker? _corpseWorker;
    private bool _backendInitialized, _workerFailed;
    private int _workerWorldVersion = -1;
    private int _workerAppliedGeometryVersion = -1;
    // Retain generations and removals across trimming/regrowing the object pool.
    // The newest command wins, but no failed nonblocking hand-off loses a death.
    private readonly uint[] _workerGeneration = new uint[FpsHordeSettings.MaximumCorpseCapacity];
    private readonly uint[] _workerAccepted = new uint[FpsHordeSettings.MaximumCorpseCapacity];
    private readonly bool[] _workerDirty = new bool[FpsHordeSettings.MaximumCorpseCapacity];
    private readonly CorpsePose[] _workerPoses = new CorpsePose[FpsHordeSettings.MaximumCorpseCapacity];

    private void EnsureCorpseBackend()
    {
        _backendInitialized = true;
        if (!Settings.CorpseWorkerEnabled)
        {
            StopCorpseWorker("Godot physics");
            _workerFailed = false;
            return;
        }
        if (_corpseWorker?.FailureReason.Length > 0)
        {
            string reason = _corpseWorker.FailureReason;
            StopCorpseWorker(reason);
            _workerFailed = true;
        }
        if (_workerFailed) return;
        CorpseWorldSnapshot? world;
        try { world = WorkerWorldProvider?.Invoke(); }
        catch (Exception error) { StopCorpseWorker($"Godot fallback: {error.Message}"); return; }
        if (world == null)
        {
            StopCorpseWorker("Godot fallback: collision world unavailable");
            return;
        }
        if (_corpseWorker == null)
        {
            Vector3 gravity = (Vector3)ProjectSettings.GetSetting("physics/3d/default_gravity_vector", Vector3.Down);
            gravity *= (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
            string nativePath = ProjectSettings.GlobalizePath("res://Native/Box3D/win-x64/box3d_corpses.dll");
            if (!Box3dCorpseWorker.TryCreate(gravity, FpsHordeSettings.MaximumCorpseCapacity, out _corpseWorker, out string reason, nativePath))
            {
                _workerFailed = true;
                CorpseBackendStatus = $"Godot fallback: {reason}";
                return;
            }
            try { _corpseWorker!.ReplaceWorld(world); }
            catch (Exception error)
            {
                StopCorpseWorker($"Godot fallback: {error.Message}"); _workerFailed = true; return;
            }
            _workerWorldVersion = world.Version;
            _corpseWorker!.Advance(0, _running);
            foreach (Corpse corpse in _slots)
            {
                if (corpse.Active && corpse.Body != null)
                {
                    SyncGodotPose(corpse, true);
                    if (_running)
                    {
                        corpse.SavedLinearVelocity = corpse.Body.LinearVelocity;
                        corpse.SavedAngularVelocity = corpse.Body.AngularVelocity;
                    }
                }
                if (corpse.Body != null)
                {
                    corpse.Body.Freeze = true;
                    corpse.Body.CollisionMask = 0;
                    corpse.Collider!.Disabled = true;
                    corpse.Body.QueueFree();
                    corpse.Body = null; corpse.Collider = null; corpse.Shape = null;
                }
                if (corpse.Active) QueueWorkerSpawn(corpse);
            }
            _shapes.Clear();
            CorpseBackendStatus = "Box3D on background CPU thread";
        }
        else if (_workerWorldVersion != world.Version)
        {
            try { _corpseWorker.ReplaceWorld(world); _workerWorldVersion = world.Version; }
            catch (Exception error) { StopCorpseWorker($"Godot fallback: {error.Message}"); _workerFailed = true; }
        }
    }

    private void QueueWorkerSpawn(Corpse corpse)
    {
        // Exhaustion cannot silently alias an old packet. Restarting the backend
        // is safe because no packet from the disposed worker can be read again.
        if (_workerGeneration[corpse.Slot] == uint.MaxValue)
        {
            StopCorpseWorker("Godot fallback: corpse generation exhausted");
            _workerFailed = true;
            return;
        }
        _workerGeneration[corpse.Slot]++;
        _workerDirty[corpse.Slot] = true;
    }

    private void FlushWorkerCommands()
    {
        if (_corpseWorker == null) return;
        try { FlushWorkerCommandsCore(); }
        catch (Exception error)
        {
            StopCorpseWorker($"Godot fallback: {error.Message}");
            _workerFailed = true;
        }
    }

    private void FlushWorkerCommandsCore()
    {
        if (_corpseWorker == null) return;
        float linearDamping = .15f + (float)ProjectSettings.GetSetting("physics/3d/default_linear_damp", .1f);
        float angularDamping = .35f + (float)ProjectSettings.GetSetting("physics/3d/default_angular_damp", .1f);
        for (int slot = 0; slot < _workerDirty.Length; slot++)
        {
            if (!_workerDirty[slot]) continue;
            if (slot < _slots.Count && _slots[slot].Active)
            {
                Corpse corpse = _slots[slot];
                CorpseSpawn spawn = new(corpse.Role == FpsEnemyRole.Striker, corpse.Pose.Origin,
                    corpse.Pose.Basis.GetRotationQuaternion(), corpse.Size,
                    corpse.SavedLinearVelocity, corpse.SavedAngularVelocity, linearDamping, angularDamping);
                if (!_corpseWorker.TrySpawn(slot, _workerGeneration[slot], spawn)) continue;
                _workerAccepted[slot] = _workerGeneration[slot];
            }
            else
            {
                if (_workerAccepted[slot] != 0 && !_corpseWorker.TryRemove(slot, _workerAccepted[slot])) continue;
                _workerAccepted[slot] = 0;
            }
            _workerDirty[slot] = false;
        }
    }

    private void ReadWorkerPoses()
    {
        if (_corpseWorker == null
            || !_corpseWorker.TryCopyCompleted(_workerPoses, out int count, out CorpseFrameInfo info)
            || info.Sequence <= WorkerFrame.Sequence || info.Epoch != _corpseWorker.Epoch) return;
        bool geometryChanged = info.GeometryVersion != _workerAppliedGeometryVersion;
        WorkerFrame = info;
        WorkerPublishedCount = count;
        if (!_running || info.GeometryVersion != _workerWorldVersion) return;
        _workerAppliedGeometryVersion = info.GeometryVersion;
        for (int i = 0; i < count; i++)
        {
            CorpsePose state = _workerPoses[i];
            if ((uint)state.Slot >= (uint)_slots.Count || state.Epoch != info.Epoch) continue;
            Corpse corpse = _slots[state.Slot];
            if (!corpse.Active || state.Generation != _workerGeneration[state.Slot] || _workerDirty[state.Slot]) continue;
            // The transition into sleep already captured the final pose. Geometry
            // changes wake native bodies, so support removal still refreshes it.
            if (state.Sleeping && corpse.WasSleeping && !geometryChanged) continue;
            Transform3D pose = new(new Basis(state.Rotation), state.Position);
            if (pose != corpse.Pose) { corpse.Pose = pose; MarkVisualDirty(); }
            corpse.SavedLinearVelocity = state.LinearVelocity;
            corpse.SavedAngularVelocity = state.AngularVelocity;
            corpse.WasSleeping = state.Sleeping;
        }
    }

    private void ClearWorkerCorpses()
    {
        if (_corpseWorker == null) return;
        _corpseWorker.Clear();
        Array.Clear(_workerAccepted); Array.Clear(_workerDirty);
        WorkerFrame = default;
        WorkerPublishedCount = 0;
        _workerAppliedGeometryVersion = -1;
    }

    private void StopCorpseWorker(string status)
    {
        if (_corpseWorker != null)
        {
            // Join only when changing backend or leaving the tree, never per tick.
            _corpseWorker.Dispose(); _corpseWorker = null;
            Array.Clear(_workerAccepted); Array.Clear(_workerDirty);
            WorkerFrame = default; WorkerPublishedCount = 0; _workerWorldVersion = -1;
            _workerAppliedGeometryVersion = -1;
            foreach (Corpse corpse in _slots)
                if (corpse.Active) ActivateGodotBody(corpse);
        }
        CorpseBackendStatus = status;
    }

    public override void _ExitTree()
    {
        _corpseWorker?.Dispose();
        _corpseWorker = null;
    }
}
