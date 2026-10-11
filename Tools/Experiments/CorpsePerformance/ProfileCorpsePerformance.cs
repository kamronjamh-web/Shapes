using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ProfileCorpsePerformance : Node
{
    private const int Count = 2056;
    private FpsVerticalSlice _map = null!;
    private FpsEnemyCorpsePool _pool = null!;
    private FpsHordeDeath[] _deaths = null!;
    private bool _tick;
    private bool _running = true;
    private bool _workerRequested;
    private double _tickMs;
    private long _ticks;
    private string _directory = "";
    private readonly StringBuilder _report = new();
    private readonly StringBuilder _csv = new("mode,sample,frame_ms,pool_tick_cpu_ms,physics_calls,viewport_gpu_ms,viewport_render_cpu_ms,render_setup_cpu_ms,visible_draws,visible_primitives,active_corpses,sleeping_corpses,engine_physics_monitor_seconds,worker_requested,worker_active,worker_ready,worker_epoch,worker_frame_sequence,worker_simulation_batch_ms,worker_pending_seconds,worker_dropped_seconds,worker_pose_readback_ms\n");

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/corpse-performance/")) throw new InvalidOperationException("Isolated saves required");
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Native rendering required");
            string label = System.Environment.GetEnvironmentVariable("SHAPES_CORPSE_PROFILE_LABEL") ?? "baseline";
            if (label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new InvalidOperationException("Unsafe run label");
            _directory = ProjectSettings.GlobalizePath($"res://.tmp/corpse-performance/{label}");
            Directory.CreateDirectory(_directory);
            if (File.Exists(Path.Combine(_directory, "report.txt"))) throw new InvalidOperationException("Report already exists");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            _workerRequested = OS.GetCmdlineUserArgs().Contains("--worker")
                || string.Equals(System.Environment.GetEnvironmentVariable("SHAPES_CORPSE_PROFILE_WORKER"), "true", StringComparison.OrdinalIgnoreCase);
            _map.HordeSettings = new FpsHordeSettings { Capacity = 2000, CorpseCapacity = Count, CorpseLifetimeSeconds = 15f,
                CorpseWorkerEnabled = _workerRequested };
            AddChild(_map);
            _map.SetProcess(false); _map.SetPhysicsProcess(false);
            _map.HordeManager!.SetProcess(false); _map.HordeManager.SetPhysicsProcess(false);
            foreach (string path in new[] { "Player", "PlayerController" })
                if (_map.GetNodeOrNull<Node>(path) is Node player) { player.SetProcess(false); player.SetPhysicsProcess(false); }
            _map.HordeManager!.Clear(); _map.HordeManager._Process(0);
            _pool = _map.CorpsePool!;
            ProcessMode = ProcessModeEnum.Always;
            Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            if (GetViewport().GetVisibleRect().Size != new Vector2(1920, 1080)) throw new InvalidOperationException("1920x1080 required");
            var camera = new Camera3D { Fov = 70, Far = 1000 };
            AddChild(camera); camera.GlobalPosition = new Vector3(70, 95, -240); camera.LookAt(new Vector3(0, 0, -145)); camera.MakeCurrent();
            Rid viewport = GetViewport().GetViewportRid(); RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
            _deaths = CreateDeaths();
            string assemblyPath = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/SuperGatchaRebuild.dll");
            string assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
            Line($"Corpse profile {label}; {DateTime.UtcNow:O}; CPU={OS.GetProcessorName()}; GPU={RenderingServer.GetVideoAdapterName()}; Godot={Engine.GetVersionInfo()["string"]}; assemblySHA256={assemblyHash}");
            Line("Real NorthGate collision geometry stays enabled; only director/livingcrowd/player callbacks stopped. Seed1337 fixed2056 mixed cube/pyramid deaths in46x45 grid, spacing1.8m, ground+4m, launch/tumble. Each mode respawns identical deaths; physics evolves in wall time. Lifetimes15s prevent expiry during3s sample. No combat/FPS guarantee. Freeze uses public pool.Tick(...,false), pausing whole corpse physics/lifetime bookkeeping; hide disables visual drawing only. Viewport timings/drawcalls include the fort/background. Engine physics monitor is a periodically refreshed monitor (potential1s maximum), NOT a per-tick sample; do not use its median as physics cost. Frame modes are ablations, not additive component timings.");
            Line($"Worker requested={_workerRequested}. Worker frame sequence counts published pose packets, NOT physics steps. SimulationBatchMs is the latest completed worker simulation loop (up to4 steps), excluding command application/world rebuild/pose readback. PendingSeconds is queued cosmetic simulation time, not full end-to-end pose latency. Samples arrive asynchronously; CSV rows can repeat the same packet. Summary records each new packet once; do not add worker batch time to main-thread CPU or viewportGPU scopes.");
            await Frames(3);
            _pool.Tick(0, true);
            if (_workerRequested)
            {
                Stopwatch readyWait = Stopwatch.StartNew();
                while (!_pool.WorkerReady && readyWait.Elapsed.TotalSeconds < 10) await Frames(1);
                if (!_pool.UsingWorker || !_pool.WorkerReady)
                    throw new InvalidOperationException($"Requested worker startup failed: {_pool.CorpseBackendStatus}");
                Line($"Worker startup ready after {readyWait.Elapsed.TotalMilliseconds:F4}ms before cold enqueue timing; status={_pool.CorpseBackendStatus}. Cold enqueue excludes DLL/world startup and asynchronous native body creation; it is NOT complete physics-ready latency.");
            }
            TimeSpawn("cold_create_2056", clear: true);
            if (_workerRequested) DumpWorkerReproduction();
            await Frames(3);
            TimeSpawn("warm_clear_refill_2056", clear: true);
            for (int i = 0; i < 5; i++) { await Frames(1); TimeSpawn($"warm_active_recycle_2056_{i}", clear: false); }
            foreach (string mode in new[] { "normal", "frozen-physics", "hidden-visuals", "frozen-hidden", "normal-repeat" })
            {
                _tick = false; _pool.Clear(); _pool.Visible = true; _pool.Tick(0, true);
                foreach (var death in _deaths) _pool.SpawnCorpse(death);
                _running = !mode.Contains("frozen"); _pool.Tick(0, _running);
                _pool.Visible = !mode.Contains("hidden"); _tick = true;
                if (_workerRequested) await WaitForWorkerPopulation(mode);
                if (_running) await AssertPhysicsMovement(mode);
                await Measure(mode, viewport);
                _tick = false;
                Save();
            }
            _pool.Clear(); _map.QueueFree(); await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Save(); GD.Print("Corpse performance profile complete."); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); if (_directory.Length > 0) Save(); GetTree().Quit(1); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_tick) return;
        long start = Stopwatch.GetTimestamp(); _pool.Tick((float)delta, _running);
        _tickMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds; _ticks++;
    }

    private async Task AssertPhysicsMovement(string mode)
    {
        var before = new List<FpsEnemyCorpseSnapshot>(); _pool.CopyActiveSnapshots(before);
        for (int i = 0; i < 3; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await Frames(1);
        }
        var after = new List<FpsEnemyCorpseSnapshot>(); _pool.CopyActiveSnapshots(after);
        int moved = 0;
        for (int i = 0; i < before.Count; i++)
            if (before[i].WorldTransform.Origin.DistanceSquaredTo(after[i].WorldTransform.Origin) > .000001f) moved++;
        if (moved < Count / 2) throw new InvalidOperationException($"{mode}: actualphysicsfailedtoadvance enough corpse poses ({moved}/{Count})");
        Line($"{mode}: actualphysicsmotion guard passed ({moved}/{Count} corpseposes moved across3physicssignals)");
    }

    private async Task WaitForWorkerPopulation(string mode)
    {
        Stopwatch wait = Stopwatch.StartNew();
        while (_pool.WorkerPublishedCount != Count || _pool.WorkerPendingCommandCount != 0)
        {
            if (!_pool.UsingWorker || !_pool.WorkerReady || wait.Elapsed.TotalSeconds > 10)
                throw new InvalidOperationException($"{mode}: worker population not ready after {wait.Elapsed.TotalSeconds:F3}s: published={_pool.WorkerPublishedCount}, pending={_pool.WorkerPendingCommandCount}, status={_pool.CorpseBackendStatus}");
            await Frames(1);
        }
        Line($"{mode}: complete worker packet ({_pool.WorkerPublishedCount}) and pending commands=0 after {wait.Elapsed.TotalMilliseconds:F4}ms, outside measured interval.");
    }

    private void DumpWorkerReproduction()
    {
        var world = _map.CaptureCorpseWorld() ?? throw new InvalidOperationException(_map.CorpseWorldUnavailableReason);
        var snapshots = new List<FpsEnemyCorpseSnapshot>(); _pool.CopyActiveSnapshots(snapshots);
        static float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
        static float[] Q(Quaternion value) => new[] { value.X, value.Y, value.Z, value.W };
        Vector3 gravity = (Vector3)ProjectSettings.GetSetting("physics/3d/default_gravity_vector", Vector3.Down);
        gravity *= (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
        var data = new
        {
            seed = 1337, capacity = Count, gravity = V(gravity), fixedStepSeconds = 1.0 / 60,
            geometryVersion = world.Version,
            statics = world.Shapes.Select(shape => new { kind = shape.Kind.ToString(), position = V(shape.Position),
                rotation = Q(shape.Rotation), size = V(shape.Size), vertices = shape.Vertices.Select(V).ToArray(),
                friction = shape.Friction, restitution = shape.Restitution }).ToArray(),
            bodies = snapshots.Select(s => new { slot = s.Slot, pyramid = s.Role == FpsEnemyRole.Striker,
                position = V(s.WorldTransform.Origin), rotation = Q(s.WorldTransform.Basis.GetRotationQuaternion()),
                size = V(_deaths[s.Slot].VisualTransform.Basis.Scale.Abs()), linearVelocity = V(s.LinearVelocity),
                angularVelocity = V(s.AngularVelocity),
                linearDamping = .15f + (float)ProjectSettings.GetSetting("physics/3d/default_linear_damp", .1f),
                angularDamping = .35f + (float)ProjectSettings.GetSetting("physics/3d/default_angular_damp", .1f),
                friction = .65f, restitution = .12f, mass = 1f }).ToArray()
        };
        File.WriteAllText(Path.Combine(_directory, "worker-reproduction.json"), JsonSerializer.Serialize(data));
        Line($"Saved worker-reproduction.json before native stepping: {world.Shapes.Length} static shapes and {snapshots.Count} actual queued body definitions.");
    }

    private FpsHordeDeath[] CreateDeaths()
    {
        Random random = new(1337); var result = new FpsHordeDeath[Count];
        for (int i = 0; i < Count; i++)
        {
            Vector3 feet = new((i % 46 - 22.5f) * 1.8f, 0, -185 + i / 46 * 1.8f);
            feet.Y = _map.SampleHillSurfaceHeight(feet) + 4f;
            bool pyramid = i % 2 != 0; float height = pyramid ? 1.35f : 1.2f;
            var enemy = new FpsHordeEnemySnapshot(new FpsHordeHandle(i, 1), feet, feet + Vector3.Up * height, .45f, 0, 30,
                pyramid ? FpsEnemyRole.Striker : FpsEnemyRole.Bruiser, height);
            Vector3 direction = new((float)random.NextDouble() - .5f, .1f, (float)random.NextDouble() - .5f);
            result[i] = new(enemy, pyramid ? new Color(.9f, .3f, .1f) : new Color(.15f, .5f, .9f),
                new Transform3D(Basis.FromScale(new Vector3(.9f, height, .9f)), feet + Vector3.Up * height * .5f), direction, 60, 30);
        }
        return result;
    }

    private void TimeSpawn(string name, bool clear)
    {
        long start = Stopwatch.GetTimestamp(); if (clear) _pool.Clear();
        foreach (var death in _deaths) _pool.SpawnCorpse(death);
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (_pool.ActiveCount != Count || _pool.Capacity != Count) throw new InvalidOperationException("Exact corpse count unavailable");
        Line($"{name}: main-thread enqueue/wall={ms:F4}ms; count={_pool.ActiveCount}; managedBytes={GC.GetTotalMemory(false)}; processWorkingSet={System.Diagnostics.Process.GetCurrentProcess().WorkingSet64}. Deferred renderer/physics allocation work may occur in later frames and is NOT included.");
        File.AppendAllText(Path.Combine(_directory, "spawn.csv"), FormattableString.Invariant($"{name},{Count},{ms:F6}\n"));
    }

    private async Task Measure(string mode, Rid viewport)
    {
        Stopwatch warmup = Stopwatch.StartNew(); for (int i = 0; i < 24 || warmup.Elapsed.TotalSeconds < .5; i++) await Frames(1);
        if (_workerRequested && mode == "normal")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(Path.Combine(_directory, "worker-normal.png")) != Error.Ok)
                throw new InvalidOperationException("Worker viewport image save failed");
            Line("Saved worker-normal.png after warmup, outside measured frame interval.");
        }
        _tickMs = 0; _ticks = 0;
        var frame = new List<double>(); var ticks = new List<double>(); var gpu = new List<double>(); var cpu = new List<double>(); var draws = new List<double>();
        Stopwatch measurement = Stopwatch.StartNew(); long previous = Stopwatch.GetTimestamp();
        var snapshots = new List<FpsEnemyCorpseSnapshot>();
        double totalTickMs = 0; long totalTicks = 0;
        var workerBatches = new List<double>(); var workerPending = new List<double>(); var workerReads = new List<double>();
        var latest = _pool.WorkerFrame;
        long lastSequence = latest.Sequence;
        double initialDropped = latest.DroppedSeconds;
        for (int i = 0; i < 90 || measurement.Elapsed.TotalSeconds < 3; i++)
        {
            await Frames(1); long now = Stopwatch.GetTimestamp(); double elapsed = Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds; previous = now;
            if (_pool.ActiveCount != Count) throw new InvalidOperationException("Population expired during measurement");
            double g = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport), c = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
            int draw = RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
            int primitives = RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.PrimitivesInFrame);
            int sleeping = -1; // Avoid per-body native readback contaminating every measured frame.
            var worker = _pool.WorkerFrame;
            if (_workerRequested && (!_pool.UsingWorker || !_pool.WorkerReady))
                throw new InvalidOperationException($"Requested worker unavailable during {mode}: {_pool.CorpseBackendStatus}");
            if (_workerRequested && worker.Sequence != lastSequence)
            {
                lastSequence = worker.Sequence;
                workerBatches.Add(worker.StepMs); workerPending.Add(worker.PendingSeconds); workerReads.Add(worker.ReadMs);
            }
            _csv.AppendLine(FormattableString.Invariant($"{mode},{i},{elapsed:F6},{_tickMs:F6},{_ticks},{g:F6},{c:F6},{RenderingServer.GetFrameSetupTimeCpu():F6},{draw},{primitives},{_pool.ActiveCount},{sleeping},{Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess):F9},{_workerRequested},{_pool.UsingWorker},{_pool.WorkerReady},{worker.Epoch},{worker.Sequence},{worker.StepMs:F6},{worker.PendingSeconds:F9},{worker.DroppedSeconds:F9},{worker.ReadMs:F6}"));
            totalTickMs += _tickMs; totalTicks += _ticks;
            frame.Add(elapsed); ticks.Add(_tickMs); gpu.Add(g); cpu.Add(c); draws.Add(draw); _tickMs = 0; _ticks = 0;
        }
        _pool.CopyActiveSnapshots(snapshots);
        if (snapshots.Any(s => !s.WorldTransform.Origin.IsFinite() || !s.LinearVelocity.IsFinite())) throw new InvalidOperationException("Nonfinite corpse state");
        Line($"{mode}: frames={frame.Count}, wall={measurement.Elapsed.TotalSeconds:F3}s; frame median/p95={Median(frame):F4}/{P95(frame):F4}ms; frame mean={frame.Average():F4}ms; poolTickCPU/frame={Median(ticks):F4}ms; physicsCalls={totalTicks}; poolTickCPU/call={totalTickMs / Math.Max(1, totalTicks):F4}ms; viewportGPU median={Median(gpu):F4}ms; viewportRenderCPU median={Median(cpu):F4}ms; viewportDraws median={Median(draws):F0}; corpses={snapshots.Count}; frozen={snapshots.Count(s => s.Frozen)}.");
        Line($"{mode}: workerActive={_pool.UsingWorker}; ready={_pool.WorkerReady}; status={_pool.CorpseBackendStatus}; distinctPackets={workerBatches.Count}; batch median/p95={(workerBatches.Count > 0 ? Median(workerBatches) : double.NaN):F4}/{(workerBatches.Count > 0 ? P95(workerBatches) : double.NaN):F4}ms; pending max={(workerPending.Count > 0 ? workerPending.Max() : 0):F6}s; dropped interval={_pool.WorkerFrame.DroppedSeconds - initialDropped:F6}s.");
        Line($"{mode}: native pose-readback median/p95={(workerReads.Count > 0 ? Median(workerReads) : double.NaN):F4}/{(workerReads.Count > 0 ? P95(workerReads) : double.NaN):F4}ms, sampled once per distinct packet and separate from simulation batch.");
    }
    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
    private static double P95(List<double> values) => values.Order().ElementAt(Math.Min(values.Count - 1, (int)(values.Count * .95)));
    private void Line(string value) { _report.AppendLine(value); GD.Print(value); }
    private void Save() { File.WriteAllText(Path.Combine(_directory, "report.txt"), _report.ToString()); File.WriteAllText(Path.Combine(_directory, "frames.csv"), _csv.ToString()); }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
