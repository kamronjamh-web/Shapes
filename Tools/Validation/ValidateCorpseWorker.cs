using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Fps.Physics;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateCorpseWorker : Node
{
    private const float Dt = 1f / 60f;
    private int _checks;
    private FpsEnemyCorpsePool? _pool;
    private FpsVerticalSlice? _map;
    private readonly List<FpsEnemyCorpseSnapshot> _snapshots = new();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/corpse-worker-validation/"), "Worker validation isolates save data");
            Engine.MaxFps = 120;
            bool missing = bool.Parse(System.Environment.GetEnvironmentVariable("SHAPES_CORPSE_WORKER_MISSING") ?? "false");
            string mode = System.Environment.GetEnvironmentVariable("SHAPES_CORPSE_WORKER_MODE") ?? "full";
            if (missing) await ValidateMissingNative();
            else
            {
                if (mode != "world") { await ValidatePackets(); await ValidatePool(); }
                if (mode != "flat") await ValidateFortWorld();
            }
            await Cleanup();
            GD.Print($"Corpse worker validation passed: {_checks} checks; missingNative={missing}.");
            GetTree().Quit();
        }
        catch (Exception error) { await Cleanup(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static CorpseWorldSnapshot Floor(int version, float x = 0f) => new(version,
        new[] { new CorpseStaticShape(CorpseStaticShapeKind.Box, new Vector3(x, -.5f, 0), Quaternion.Identity,
            new Vector3(100f, 1f, 100f), Array.Empty<Vector3>()) });
    private static FpsHordeDeath Death(int identity, Vector3 center)
    {
        bool pyramid = (identity & 1) != 0;
        Vector3 size = pyramid ? new Vector3(.9f, 1f, .9f) : Vector3.One;
        FpsHordeEnemySnapshot enemy = new(new(identity, 1), center, center, size.X * .5f, 0, 40,
            pyramid ? FpsEnemyRole.Striker : FpsEnemyRole.Bruiser, size.Y);
        return new(enemy, pyramid ? Colors.Orange : Colors.CornflowerBlue,
            new Transform3D(Basis.FromScale(size), center), Vector3.Back, 50f, 10f);
    }

    private async Task ValidatePackets()
    {
        Check(Box3dCorpseWorker.TryCreate(Vector3.Zero, 4, out var created, out string reason), "Native worker starts: " + reason);
        using Box3dCorpseWorker worker = created!;
        CorpsePose[] poses = new CorpsePose[4];
        worker.ReplaceWorld(Floor(1));
        CorpseSpawn spawn = new(false, new Vector3(0, 2, 0), Quaternion.Identity, Vector3.One, Vector3.Zero, Vector3.Zero);
        await Until(() => worker.TrySpawn(0, 1, spawn), "First worker spawn command is accepted");
        await Until(() => worker.TrySpawn(1, 1, spawn with { Pyramid = true }), "Overlapping pyramid command is accepted");
        worker.Advance(.1f, true);
        await Until(() => worker.TryCopyCompleted(poses, out int count, out var info) && count == 2 && info.GeometryVersion == 1,
            "Completed packets contain both exact generations and the installed world version");
        Check(poses[0].Position == spawn.Position && poses[1].Position == spawn.Position,
            "Worker corpses retain static-only filtering and do not resolve their mutual overlap");
        await Until(() => worker.TrySpawn(0, 2, spawn with { Position = new Vector3(10, 2, 0) }), "Recycled generation is accepted");
        if (worker.TryCopyCompleted(poses, out int immediate, out _))
            Check(poses.Take(immediate).All(p => p.Slot != 0 || p.Generation == 2), "Older in-flight generation is filtered immediately");
        await Until(() => worker.TryCopyCompleted(poses, out int count, out _) && poses.Take(count).Any(p => p.Slot == 0 && p.Generation == 2 && p.Position.X == 10),
            "Recycling publishes the replacement pose without old-slot teleportation");
        Check(!worker.TryRemove(0, 1), "Stale generation cannot remove a newer corpse");
        await Until(() => worker.TrySpawn(0, 3, spawn), "New generation queued before removal");
        await Until(() => worker.TryRemove(0, 3), "Newest generation removal is accepted");
        await Until(() => worker.TryCopyCompleted(poses, out int count, out _) && poses.Take(count).All(p => p.Slot != 0),
            "Spawn/remove coalescing leaves a desired-empty slot even with an older native body");
        int epoch = worker.Clear();
        if (worker.TryCopyCompleted(poses, out _, out var afterClear)) Check(afterClear.Epoch == epoch, "Clear cannot expose an old-epoch packet");
        await Until(() => worker.TrySpawn(0, 4, spawn with { LinearVelocity = Vector3.Right }), "Spawn after clear uses a fresh epoch");
        worker.Advance(.05f, true);
        await Until(() => worker.TryCopyCompleted(poses, out int count, out var info) && count == 1 && info.Epoch == epoch && poses[0].Generation == 4 && poses[0].Position.X > 0,
            "Post-clear completion preserves the new epoch and motion");
        worker.Advance(0, false);
        await Until(() => worker.TryCopyCompleted(poses, out _, out var info) && !info.Running, "Worker publishes paused state");
        Vector3 paused = poses[0].Position;
        for (int i = 0; i < 8; i++) { worker.Advance(Dt, false); await Frame(); }
        Check(worker.TryCopyCompleted(poses, out int pausedCount, out _) && pausedCount == 1 && poses[0].Position == paused,
            "Paused worker advances neither native poses nor pending simulation time");
        worker.Advance(10f, true);
        await Until(() => worker.TryCopyCompleted(poses, out _, out var info) && info.DroppedSeconds >= 9.75 && info.PendingSeconds <= .250001,
            "Worker backlog is bounded and dropped cosmetic time is explicit");
        worker.Dispose();
        Check(worker.IsStopped, "Worker shutdown joins its native owner before releasing resources");
    }

    private async Task ValidatePool()
    {
        CorpseWorldSnapshot world = Floor(1);
        _pool = new FpsEnemyCorpsePool { Settings = new FpsHordeSettings { CorpseWorkerEnabled = true,
            CorpseCapacity = 2056, CorpseLifetimeSeconds = 15f, CorpseBaseLaunchSpeed = 0,
            CorpseOverkillSpeedScale = 0, CorpseUpwardSpeed = 0, CorpseAngularSpeed = 0 }, WorkerWorldProvider = () => world };
        AddChild(_pool);
        _pool.SpawnCorpse(Death(0, new Vector3(0, .6f, 0)));
        await Until(() => _pool.WorkerReady && _pool.SleepingCorpseCount == 1, "Flat-floor worker corpse settles and sleeps", () => _pool.Tick(Dt, true));
        Check(_pool.UsingWorker && Bodies(_pool) == 0, "Worker corpse creates no Godot rigid-body node");
        long uploads = _pool.CorpseRenderUploadCount;
        for (int i = 0; i < 8; i++) { _pool.Tick(Dt, true); await Frame(); }
        Check(_pool.CorpseRenderUploadCount == uploads, "Settled worker corpses stabilize batched render uploads");
        SnapshotPool(); Vector3 rested = _snapshots.Single().WorldTransform.Origin;
        world = Floor(2, 200f);
        await Until(() => _pool.WorkerFrame.GeometryVersion == 2 && _pool.SleepingCorpseCount == 0
            && PoolY() < rested.Y - .05f, "Replacing supporting geometry wakes the sleeping corpse", () => _pool.Tick(Dt, true));

        _pool.Clear(); world = Floor(3);
        for (int i = 0; i < 2056; i++) _pool.SpawnCorpse(Death(100 + i, new Vector3((i % 32 - 16) * .5f, 5f, (i / 32 - 32) * .5f)));
        await Until(() => _pool.WorkerReady && _pool.WorkerFrame.GeometryVersion == 3
            && _pool.WorkerPendingCommandCount == 0 && _pool.WorkerPublishedCount == 2056,
            "2056-body death burst queues every native command", () => _pool.Tick(Dt, true));
        SnapshotPool();
        Check(_pool.ActiveCount == 2056 && _snapshots.Count == 2056 && Bodies(_pool) == 0,
            "2056 active corpses use bounded managed slots and zero Godot rigid bodies");
        Check(_snapshots.All(c => c.WorldTransform.Origin.IsFinite() && c.LinearVelocity.IsFinite() && c.AngularVelocity.IsFinite()),
            "Mass worker poses and velocities remain finite");
        _pool.Tick(0, false); SnapshotPool(); var before = _snapshots.ToArray();
        for (int i = 0; i < 8; i++) { _pool.Tick(.1f, false); await Frame(); }
        SnapshotPool();
        Check(_snapshots.Zip(before).All(pair => pair.First.Frozen && pair.First.WorldTransform == pair.Second.WorldTransform
            && pair.First.RemainingLifetime == pair.Second.RemainingLifetime), "2056-body pause freezes presentation and lifetime");
        _pool.SpawnCorpse(Death(9000, new Vector3(8, 7, 8))); SnapshotPool();
        Check(_snapshots.Single(c => c.Slot == 0).EnemyHandle.Slot == 9000 && _pool.ActiveCount == 2056,
            "Worker pool recycles the same oldest identity while paused");
        _pool.Settings.CorpseCapacity = 64; _pool.Tick(0, false);
        Check(_pool.ActiveCount == 64, "Worker capacity shrink retires high slots");
        _pool.Settings.CorpseCapacity = 2056;
        for (int i = 0; i < 100; i++) _pool.SpawnCorpse(Death(10000 + i, new Vector3(2, 6, 2)));
        Check(_pool.ActiveCount == 164, "Worker capacity regrowth reuses valid slots and generations");
        _pool.Tick(Dt, true); int previousEpoch = _pool.WorkerFrame.Epoch; _pool.Clear();
        _pool.SpawnCorpse(Death(20000, new Vector3(12, 9, 12)));
        await Until(() => _pool.WorkerPendingCommandCount == 0 && _pool.WorkerPublishedCount == 1
            && _pool.WorkerFrame.Epoch > previousEpoch && PoolIdentity() == 20000 && PoolNewPosition(),
            "Clear during worker activity prevents stale identities after reuse", () => _pool.Tick(Dt, true));
        _pool.Tick(15f, true); SnapshotPool();
        Check(_pool.ActiveCount == 0 && _snapshots.Count == 0, "Worker lifetime expiration retires all cosmetic identities");

        _pool.SpawnCorpse(Death(20001, new Vector3(0, 4, 0)));
        _pool.Tick(0, false); SnapshotPool(); FpsEnemyCorpseSnapshot preserved = _snapshots.Single();
        _pool.Settings.CorpseWorkerEnabled = false; _pool.Tick(0, false); SnapshotPool();
        FpsEnemyCorpseSnapshot fallback = _snapshots.Single();
        Check(!_pool.UsingWorker && Bodies(_pool) == 1 && fallback.WorldTransform.IsEqualApprox(preserved.WorldTransform)
            && fallback.LinearVelocity == preserved.LinearVelocity && fallback.AngularVelocity == preserved.AngularVelocity
            && fallback.RemainingLifetime == preserved.RemainingLifetime,
            "Godot fallback restores a paused worker corpse with its pose, velocities and remaining lifetime");
        _pool.Settings.CorpseWorkerEnabled = true; _pool.Tick(0, false);
        await Until(() => _pool.WorkerReady, "Worker can recover from a live backend toggle", () => _pool.Tick(0, false));
        await Frame();
        Check(Bodies(_pool) == 0 && _pool.ActiveCount == 1, "Recovering worker releases fallback body nodes without losing identity");
        _pool.Clear(); _pool.QueueFree(); _pool = null; await Frame();
    }

    private async Task ValidateFortWorld()
    {
        _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
        _map.HordeSettings!.GpuCrowdContactsEnabled = false; _map.HordeSettings.Enabled = false;
        AddChild(_map); await Frame(); _map.SetPhysicsProcess(false);
        _map.HordeManager!.SetProcess(false);
        CorpseWorldSnapshot closed = _map.CaptureCorpseWorld() ?? throw new InvalidOperationException(_map.CorpseWorldUnavailableReason);
        Check(closed.Shapes.Any(s => s.Kind == CorpseStaticShapeKind.TriangleMesh) && closed.Shapes.Any(s => s.Kind == CorpseStaticShapeKind.Box),
            "Actual fort snapshot contains terrain mesh and solid collision geometry");
        FpsDefenseGate gate = Descendants(_map).OfType<FpsDefenseGate>().First(g => g.IsAlive);
        gate.ApplyDamage(float.MaxValue);
        CorpseWorldSnapshot breached = _map.CaptureCorpseWorld()!;
        Check(breached.Version > closed.Version && breached.Shapes.Length == closed.Shapes.Length - 1,
            "Gate breach removes its worker collider immediately before deferred physics toggles");
        gate.ResetHealth(); CorpseWorldSnapshot restored = _map.CaptureCorpseWorld()!;
        Check(restored.Version > breached.Version && restored.Shapes.Length == closed.Shapes.Length,
            "Gate restore reinstalls its worker collider in a new world version");
        await ValidateFortContacts(closed, breached, restored, gate.GlobalPosition + Vector3.Up * gate.Dimensions.Y * .5f);
        gate.Dimensions += Vector3.Up * .25f;
        CorpseWorldSnapshot retuned = _map.CaptureCorpseWorld()!;
        Check(retuned.Version > restored.Version, "Live gate geometry retuning refreshes the immutable worker world");
        _map.CorpsePool!.Settings.CorpseWorkerEnabled = true;
        _map.CorpsePool.Tick(0, true);
        await Until(() => _map.CorpsePool.WorkerReady && _map.CorpsePool.WorkerFrame.GeometryVersion == retuned.Version,
            "Actual map pool installs the latest fort collision snapshot", () => _map.CorpsePool.Tick(0, true));
        typeof(FpsVerticalSlice).GetMethod("BuildArena", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_map, null);
        CorpseWorldSnapshot rebuilt = _map.CaptureCorpseWorld()!;
        await Until(() => _map.CorpsePool.WorkerFrame.GeometryVersion == rebuilt.Version,
            "Arena rebuild replaces static geometry on the native owner", () => _map.CorpsePool.Tick(0, true));
        Check(rebuilt.Version > retuned.Version && _map.CorpsePool.UsingWorker, "Rebuild preserves the active worker backend");
        _map.QueueFree(); _map = null; await Frame();
    }

    private async Task ValidateMissingNative()
    {
        _pool = new FpsEnemyCorpsePool { Settings = new FpsHordeSettings { CorpseCapacity = 8, CorpseWorkerEnabled = true },
            WorkerWorldProvider = () => Floor(1) };
        AddChild(_pool); _pool.SpawnCorpse(Death(1, new Vector3(0, 3, 0))); _pool.Tick(0, false);
        Check(!_pool.UsingWorker && _pool.CorpseBackendStatus.Contains("absent", StringComparison.OrdinalIgnoreCase),
            "Missing native library chooses an explicit Godot fallback");
        Check(_pool.ActiveCount == 1 && Bodies(_pool) == 1, "Missing-library fallback retains cosmetic identity and physics");
        await Frame();
    }

    private float PoolY() { SnapshotPool(); return _snapshots.Single().WorldTransform.Origin.Y; }
    private int PoolIdentity() { SnapshotPool(); return _snapshots.Single().EnemyHandle.Slot; }
    private bool PoolNewPosition()
    {
        Vector3 position = _snapshots.Single().WorldTransform.Origin;
        return Math.Abs(position.X - 12f) < .001f && Math.Abs(position.Z - 12f) < .001f && position.Y > 8f && position.Y <= 9f;
    }
    private void SnapshotPool() => _pool!.CopyActiveSnapshots(_snapshots);
    private static int Bodies(Node node) => Descendants(node).OfType<RigidBody3D>().Count();
    private static IEnumerable<Node> Descendants(Node root)
    {
        var children = root.GetChildren();
        using Godot.Collections.Array childrenOwner = (Godot.Collections.Array)children;
        foreach (Node child in children) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
    }
    private async Task Until(Func<bool> condition, string description, Action? pump = null, bool record = true)
    {
        long start = Stopwatch.GetTimestamp();
        do { pump?.Invoke(); if (condition()) { if (record) Check(true, description); return; } await Frame(); }
        while (Stopwatch.GetElapsedTime(start).TotalSeconds < 8);
        throw new InvalidOperationException(description + " timed out. " + _pool?.CorpseBackendStatus);
    }
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Cleanup()
    {
        _pool?.Clear(); _pool?.QueueFree(); _pool = null;
        _map?.QueueFree(); _map = null; _snapshots.Clear();
        for (int i = 0; i < 3; i++) await Frame();
        GC.Collect(); GC.WaitForPendingFinalizers();
        for (int i = 0; i < 3; i++) await Frame();
    }
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++; GD.Print("PASS: " + description);
    }
}
