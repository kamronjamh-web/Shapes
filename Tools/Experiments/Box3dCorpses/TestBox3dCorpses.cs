using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Experiments.Box3dCorpses;

// Isolated experiment; the production corpse backend and pool are unchanged.
public partial class TestBox3dCorpses : Node3D
{
    private const float Dt = 1f / 60f;
    private static readonly Vector3 Left = new(-16f, 0f, 0f), Right = new(16f, 0f, 0f);
    private readonly List<FpsEnemyCorpsePool> _pools = new();
    private readonly List<FpsEnemyCorpseSnapshot> _snapshots = new(), _scratch = new();
    private readonly List<int> _ids = new();
    private readonly List<MeshInstance3D> _visuals = new();
    private readonly List<Vector3> _sizes = new();
    private readonly StringBuilder _report = new();
    private readonly StringBuilder _csv = new("count,backend,sample,physics_tick,engine_physics_recent_window_max_ms,box3d_step_ms,box3d_read_publish_ms\n");
    private Box3dWorld? _world;
    private string _directory = "", _backend = "idle";
    private float _linearDamp, _angularDamp;
    private float _nativeRemaining;
    private double _stepMs, _publishMs;
    private long _physicsTick;
    private int _checks, _capacity;
    private bool _running = true, _demo, _canSave, _respawning;
    private Mesh _cube = null!, _pyramid = null!;

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/box3d-corpses/"))
                throw new InvalidOperationException("Use RunBox3dCorpses.ps1 for isolated save data.");
            string label = System.Environment.GetEnvironmentVariable("SHAPES_BOX3D_LABEL") ?? "latest";
            if (label.Length == 0 || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidOperationException("Invalid run label.");
            _directory = ProjectSettings.GlobalizePath("res://.tmp/box3d-corpses/" + label);
            Directory.CreateDirectory(_directory);
            if (File.Exists(Path.Combine(_directory, "report.txt"))) throw new InvalidOperationException("Choose a new label; existing reports are preserved.");
            _canSave = true;
            _demo = bool.Parse(System.Environment.GetEnvironmentVariable("SHAPES_BOX3D_DEMO") ?? "false");
            int[] counts = (System.Environment.GetEnvironmentVariable("SHAPES_BOX3D_COUNTS") ?? "64,256,2056")
                .Split(',').Select(int.Parse).ToArray();
            if (counts.Length == 0 || counts.Any(c => c != 64 && c != 256 && c != 1024 && c != 2056))
                throw new InvalidOperationException("Counts must contain 64, 256, 1024, or 2056.");
            Engine.PhysicsTicksPerSecond = 60;
            Engine.TimeScale = 1d;
            Engine.MaxFps = 60;
            if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _linearDamp = .15f + (float)ProjectSettings.GetSetting("physics/3d/default_linear_damp", .1f).AsDouble();
            _angularDamp = .35f + (float)ProjectSettings.GetSetting("physics/3d/default_angular_damp", .1f).AsDouble();
            Vector3 gravity = ProjectSettings.GetSetting("physics/3d/default_gravity_vector", Vector3.Down).AsVector3()
                * (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
            _world = new Box3dWorld(gravity);
            _cube = new BoxMesh { Size = Vector3.One };
            _pyramid = PyramidMesh();
            BuildScene();
            Line($"Box3D cosmetic corpse experiment; {DateTime.UtcNow:O}; CPU={OS.GetProcessorName()}; Godot={Engine.GetVersionInfo()["string"]}; physics setting={ProjectSettings.GetSetting("physics/3d/physics_engine", "DEFAULT")}; server={PhysicsServer3D.Singleton.GetClass()}; display={DisplayServer.GetName()}");
            Line($"Actual FpsEnemyCorpsePool baseline: mass 1, friction .65, bounce .12, CCD enabled, linear/angular damping .15/.35 plus project defaults. Box3D nominal effective damping={_linearDamp}/{_angularDamp}, gravity={gravity}; dt=1/60, Box3D substeps 4. Bodies collide with static layer 1 only; corpses never collide with one another. Lifetime 15 s is a diagnostic override of production 4 s. Each count uses one bounded production pool, including the current 2056-body ceiling; 1024 remains an optional intermediate count.");
            Line("Different solvers need not produce matching trajectories. Box3D's default world enables continuous collision for dynamic/static contacts; this bridge retains that default. Four substeps are a separate setting. Godot corpse ContinuousCd=true and Box3D's defaults do not establish exact CCD equivalence; the extra Box3D bullet mode is unset. Damping integration, contact material mixing, sleep and pyramid mass properties can differ despite matching nominal inputs.");
            Line("Experiment limitation: active native bodies are bounded, but removed handle-table entries are retained until world disposal. Repeated respawn/clear can grow that table; this bridge is not a production memory-recycling implementation.");
            Line("Both backends observe Performance.TimePhysicsProcess in milliseconds: engine physics-frame CPU work, including synchronization and this harness. Godot main.cpp publishes the maximum since the previous roughly one-second update; CSV values repeat between updates and are NOT independent per-step timings. Three observations spaced60 ticks apart are reported after66 warmup ticks; update windows need not align exactly with the samples. Box3D additionally reports direct Step CPU wall time and managed read/visual publication separately. Never compare Box3D Step alone against Godot engine monitor as a solver speedup. The inactive Godot population remains frozen/hidden during the Box3D control; static scene/camera remain the same. No GPU or whole-game FPS claim.");
            Line("Sources: https://docs.godotengine.org/en/stable/classes/class_rigidbody3d.html ; https://docs.godotengine.org/en/stable/classes/class_performance.html ; https://github.com/godotengine/godot/blob/4.7-stable/main/main.cpp (physics_process_max publication); https://github.com/erincatto/box3d/blob/5d83df83ab47172c2c745b44315e7538ead02397/src/types.c (enableContinuous default); actual production baseline Scripts/Fps/FpsEnemyCorpsePool.cs.");
            ValidateNativeWorld(gravity);
            foreach (int count in counts)
            {
                await SpawnMatched(count);
                await ValidatePauseAndRecycle();
                await SpawnMatched(count);
                if (_demo)
                {
                    SetBackend("both");
                    Line($"Interactive comparison: {count} corpses per side. P pause/resume; R respawn/recycle; C clear; Escape quit. Godot left, Box3D right.");
                    Save();
                    return;
                }
                await Measure(count, "Godot");
                await SpawnMatched(count);
                await Measure(count, "Box3D");
                await SpawnMatched(count);
                SetBackend("both");
                await PhysicsTicks(90);
                ValidateFinite();
                await Capture(count);
                ClearAll();
                Check(_world.ActiveDynamicCount == 0 && _pools.Sum(p => p.ActiveCount) == 0, "Clear retires both bounded populations");
            }
            await Cleanup();
            Line($"Box3D corpse validation passed: {_checks} checks.");
            Save();
            GetTree().Quit();
        }
        catch (Exception error)
        {
            Line("FAILED: " + error);
            Save();
            _world?.Dispose(); _world = null;
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _physicsTick++;
        _stepMs = _publishMs = 0d;
        bool godot = _backend is "Godot" or "both";
        foreach (var pool in _pools) pool.Tick((float)delta, _running && godot);
        if (!_running || _world == null || _backend is not ("Box3D" or "both")) return;
        AdvanceNativeLifetime((float)delta);
        if (_ids.Count == 0) return;
        if (Math.Abs(delta - Dt) > .00001) throw new InvalidOperationException("Experiment physics cadence changed.");
        long start = Stopwatch.GetTimestamp();
        _world.Step(Dt, 4);
        _stepMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < _ids.Count; i++)
        {
            var state = _world.Read(_ids[i]);
            _visuals[i].Transform = new Transform3D(new Basis(state.Rotation) * Basis.FromScale(_sizes[i]), state.Position + Right);
        }
        _publishMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void BuildScene()
    {
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55f, -25f, 0f), LightEnergy = 1.3f });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(.06f, .08f, .12f), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White, AmbientLightEnergy = .45f } });
        Camera3D camera = new() { Position = new Vector3(0f, 25f, 39f), Fov = 65f, Far = 200f };
        AddChild(camera); camera.LookAt(new Vector3(0f, 2f, 0f)); camera.Current = true;
        foreach (Vector3 offset in new[] { Left, Right })
        {
            StaticVisual(new Vector3(0f, -.5f, 0f), Quaternion.Identity, new Vector3(24f, 1f, 24f), offset, "Floor");
            StaticVisual(new Vector3(0f, 2f, 9f), Quaternion.Identity, new Vector3(24f, 4f, .6f), offset, "Wall");
            StaticVisual(new Vector3(-6f, 1f, 0f), new Quaternion(Vector3.Right, Mathf.DegToRad(-20f)), new Vector3(8f, .6f, 5f), offset, "Ramp");
            AddChild(new Label3D { Text = offset == Left ? "GODOT CORPSE POOL" : "BOX3D LOCAL WORLD", Position = offset + new Vector3(0f, 6f, -10f), FontSize = 64, PixelSize = .025f });
        }
    }

    private void StaticVisual(Vector3 center, Quaternion rotation, Vector3 size, Vector3 offset, string name)
    {
        var visual = new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size },
            Transform = new Transform3D(new Basis(rotation), center + offset),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.25f, .3f, .38f), Roughness = .8f } };
        AddChild(visual);
        if (offset == Left)
        {
            var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, Transform = visual.Transform,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = .8f, Bounce = 0f } };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            AddChild(body);
        }
        else _world!.AddStaticBox(center, rotation, size, .8f);
    }

    private static FpsHordeDeath Death(int index)
    {
        bool pyramid = index % 2 != 0;
        float radius = pyramid ? .46f : .55f, height = pyramid ? 1f : 1.2f;
        Vector3 center = new((index % 16 - 7.5f) * 1.1f, 3f + index / 192 * 1.5f, -9f + index / 16 % 12 * 1.15f);
        Quaternion rotation = new(Vector3.Right, (index % 5 - 2) * .1f);
        Basis basis = new Basis(rotation) * Basis.FromScale(new Vector3(radius * 2f, height, radius * 2f));
        var enemy = new FpsHordeEnemySnapshot(new(index, 1), center, center, radius, 0f, 40f,
            pyramid ? FpsEnemyRole.Striker : FpsEnemyRole.Bruiser, height);
        return new FpsHordeDeath(enemy, pyramid ? new Color(.85f, .38f, .22f) : new Color(.2f, .65f, .9f),
            new Transform3D(basis, center + Left), new Vector3((index % 5 - 2) * .15f, 0f, 1f), 50f, index % 8 * 4f);
    }

    private async Task SpawnMatched(int count)
    {
        ClearAll();
        foreach (var previousPool in _pools) previousPool.QueueFree();
        _pools.Clear();
        foreach (var visual in _visuals) visual.QueueFree();
        _visuals.Clear(); _sizes.Clear();
        await PhysicsTicks(2);
        _capacity = count;
        var pool = new FpsEnemyCorpsePool { Settings = new FpsHordeSettings { CorpseCapacity = count,
            CorpseLifetimeSeconds = 15f, CorpseEnabled = true } };
        AddChild(pool); _pools.Add(pool);
        Check(pool.Capacity == count, "Single production pool supports the requested bounded capacity");
        for (int i = 0; i < count; i++) pool.SpawnCorpse(Death(i));
        CopyGodot();
        foreach (var corpse in _snapshots)
        {
            FpsHordeDeath death = Death(corpse.EnemyHandle.Slot);
            Vector3 size = death.VisualTransform.Basis.Scale.Abs();
            Quaternion rotation = corpse.WorldTransform.Basis.GetRotationQuaternion();
            Vector3 center = corpse.WorldTransform.Origin - Left;
            int id = corpse.Role == FpsEnemyRole.Striker
                ? _world!.SpawnPyramid(center, rotation, size, corpse.InitialLaunchVelocity, corpse.AngularVelocity, _linearDamp, _angularDamp, .65f, .12f)
                : _world!.SpawnBox(center, rotation, size, corpse.InitialLaunchVelocity, corpse.AngularVelocity, _linearDamp, _angularDamp, .65f, .12f);
            _ids.Add(id);
            var visual = new MeshInstance3D { Mesh = corpse.Role == FpsEnemyRole.Striker ? _pyramid : _cube,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = death.BodyColor, Roughness = .7f,
                    EmissionEnabled = true, Emission = death.BodyColor, EmissionEnergyMultiplier = .08f },
                Transform = new Transform3D(new Basis(rotation), center + Right), Scale = size };
            AddChild(visual); _visuals.Add(visual); _sizes.Add(size);
        }
        SetBackend("idle");
        _nativeRemaining = 15f;
        Check(_snapshots.Count == count && _world!.ActiveDynamicCount == count, "Matched real-pool initial transforms and launch velocities");
    }

    private void SetBackend(string backend)
    {
        _backend = backend;
        foreach (var pool in _pools) { pool.Tick(0f, _running && (backend is "Godot" or "both")); pool.Visible = backend is "Godot" or "both"; }
        for (int i = 0; i < _visuals.Count; i++)
            _visuals[i].Visible = i < _ids.Count && (backend is "Box3D" or "both");
    }

    // Each diagnostic population is spawned as one cohort, with the same 15 s
    // lifetime as the production pool override. Pause/inactive controls preserve it.
    private void AdvanceNativeLifetime(float dt)
    {
        if (!_running || _backend is not ("Box3D" or "both") || _ids.Count == 0) return;
        _nativeRemaining = Mathf.Max(0f, _nativeRemaining - dt);
        if (_nativeRemaining > 0f) return;
        _world!.ClearDynamics(); _ids.Clear();
        foreach (var visual in _visuals) visual.Visible = false;
    }

    private void CopyGodot()
    {
        _snapshots.Clear();
        foreach (var pool in _pools) { pool.CopyActiveSnapshots(_scratch); _snapshots.AddRange(_scratch); }
    }

    private async Task Measure(int count, string backend)
    {
        SetBackend(backend);
        await PhysicsTicks(66);
        List<double> engine = new(), step = new(), publish = new();
        long lastTick = _physicsTick;
        for (int i = 0; i < 180; i++)
        {
            do { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            while (_physicsTick == lastTick);
            lastTick = _physicsTick;
            double physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000d;
            if ((i + 1) % 60 == 0) engine.Add(physics);
            step.Add(_stepMs); publish.Add(_publishMs);
            _csv.AppendLine(FormattableString.Invariant($"{count},{backend},{i},{lastTick},{physics:F6},{_stepMs:F6},{_publishMs:F6}"));
        }
        ValidateFinite();
        Check(_world!.ActiveDynamicCount == count && _pools.Sum(p => p.ActiveCount) == count, "Fixed-step benchmark retains the requested population");
        Line($"{count}/{backend}:180 distinct observed60Hz physics ticks after66 warmup ticks; engine physics recent-window maxima sampled60 ticks apart=[{string.Join(",", engine.Select(v => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)))}]ms; Box3D direct Step {Describe(step)}; Box3D read+visual publication {Describe(publish)}. Render-frame timing is not included; zero Box3D fields in Godot rows mean inactive. The timed trajectory progresses from age1.1s to4.1s, including eventual sleep.");
        Save();
    }

    private async Task ValidatePauseAndRecycle()
    {
        SetBackend("both"); await PhysicsTicks(8);
        _running = false; SetBackend("both");
        float remaining = _nativeRemaining;
        AdvanceNativeLifetime(15f);
        Check(_nativeRemaining == remaining && _world!.ActiveDynamicCount == _capacity,
            "Paused native lifetime does not advance");
        CopyGodot();
        var before = _snapshots.Select(s => s.WorldTransform).ToArray();
        var native = _ids.Select(id => _world!.Read(id).Position).ToArray();
        await PhysicsTicks(8); CopyGodot();
        Check(before.Length == _snapshots.Count && before.Zip(_snapshots).All(pair => pair.First.IsEqualApprox(pair.Second.WorldTransform)), "Godot pause freezes all active corpse transforms");
        Check(_ids.Select((id, i) => _world!.Read(id).Position == native[i]).All(equal => equal), "Box3D pause omits native world steps");
        _running = true; SetBackend("both"); await PhysicsTicks(2); ValidateFinite();
        var pool = _pools[0];
        pool.SpawnCorpse(Death(10000)); pool.CopyActiveSnapshots(_scratch);
        Check(pool.ActiveCount == pool.Capacity && _scratch.Any(s => s.EnemyHandle.Slot == 10000), "Production pool recycles oldest corpse at its bound");
        int old = _ids[0]; _world!.Remove(old); _ids.RemoveAt(0);
        int replacement = _world.SpawnBox(new Vector3(0f, 6f, 0f), Quaternion.Identity, Vector3.One, Vector3.Zero, Vector3.Zero, _linearDamp, _angularDamp, .65f, .12f);
        _ids.Insert(0, replacement);
        _sizes[0] = Vector3.One;
        Check(_world.ActiveDynamicCount == _capacity, "Native remove/spawn recycles the bounded population");
        bool staleRejected = false;
        try { _world.Read(old); } catch (ArgumentException) { staleRejected = true; }
        Check(staleRejected, "Removed native body ID remains invalid after replacement");
        AdvanceNativeLifetime(15f);
        Check(_world.ActiveDynamicCount == 0 && _ids.Count == 0 && _visuals.All(v => !v.Visible),
            "Native cohort expiration removes bodies and hides visuals without stale read handles");
        SetBackend("both");
        Check(_visuals.All(v => !v.Visible), "Resuming an expired native cohort keeps retired visuals hidden");
    }

    private void ValidateNativeWorld(Vector3 gravity)
    {
        using (var free = new Box3dWorld(Vector3.Zero))
        {
            int a = free.SpawnBox(Vector3.Zero, Quaternion.Identity, Vector3.One, Vector3.Zero, Vector3.Zero, 0f, 0f, .65f, .12f);
            int b = free.SpawnPyramid(Vector3.Zero, Quaternion.Identity, Vector3.One, Vector3.Zero, Vector3.Zero, 0f, 0f, .65f, .12f);
            for (int i = 0; i < 30; i++) free.Step(Dt);
            Check(free.Read(a).Position.Length() < .00001f && free.Read(b).Position.Length() < .00001f,
                "Overlapping native corpses do not collide with one another");
            free.ClearDynamics(); Check(free.ActiveDynamicCount == 0, "Native clear removes all dynamic bodies");
            bool rejected = false; try { free.Read(b); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Clear invalidates native IDs");
        }
        using (var continuous = new Box3dWorld(Vector3.Zero))
        {
            continuous.AddStaticBox(new Vector3(0f, 0f, 1f), Quaternion.Identity, new Vector3(4f, 4f, .03f));
            int projectile = continuous.SpawnBox(Vector3.Zero, Quaternion.Identity, new Vector3(.1f, .1f, .1f),
                Vector3.Back * 24f, Vector3.Zero, 0f, 0f, .65f, .12f);
            float maximumZ = 0f;
            for (int i = 0; i < 30; i++)
            {
                continuous.Step(Dt, 4);
                var state = continuous.Read(projectile); CheckFinite(state);
                maximumZ = Mathf.Max(maximumZ, state.Position.Z);
            }
            Check(maximumZ < 1f, "Native continuous collision blocks a .1m cube at24m/s against a .03m static wall; this is one tested case, not exact solver CCD parity");
        }
        using var world = new Box3dWorld(gravity);
        world.AddStaticBox(new Vector3(0f, -.5f, 0f), Quaternion.Identity, new Vector3(24f, 1f, 24f));
        world.AddStaticBox(new Vector3(0f, 2f, 9f), Quaternion.Identity, new Vector3(24f, 4f, .6f));
        world.AddStaticBox(new Vector3(-6f, 1f, 0f), new Quaternion(Vector3.Right, Mathf.DegToRad(-20f)), new Vector3(8f, .6f, 5f));
        int falling = world.SpawnPyramid(new Vector3(6f, 9f, 0f), Quaternion.Identity, Vector3.One, Vector3.Zero, Vector3.Zero, _linearDamp, _angularDamp, .65f, .12f);
        int wall = world.SpawnBox(new Vector3(0f, 1f, 6f), Quaternion.Identity, Vector3.One, Vector3.Back * 8f, Vector3.Zero, _linearDamp, _angularDamp, .65f, .12f);
        int ramp = world.SpawnBox(new Vector3(-6f, 5f, 0f), Quaternion.Identity, Vector3.One, Vector3.Zero, Vector3.Zero, _linearDamp, _angularDamp, .65f, .12f);
        for (int i = 0; i < 60; i++) world.Step(Dt);
        Check(world.Read(falling).Position.Y < 9f && world.Read(falling).LinearVelocity.Y < 0f, "Native gravity accelerates a free falling pyramid");
        for (int i = 0; i < 180; i++) world.Step(Dt);
        foreach (int id in new[] { falling, wall, ramp }) CheckFinite(world.Read(id));
        Check(world.Read(falling).Position.Y >= -.6f, "Native pyramid collides with the floor");
        Check(world.Read(wall).Position.Z < 9.6f, "Native cube is blocked by a static wall at representative corpse speed");
        Check(world.Read(ramp).Position.Y > .7f, "Native cube remains supported by the tilted static ramp");
    }

    private void ValidateFinite()
    {
        CopyGodot();
        foreach (var corpse in _snapshots)
            if (!corpse.WorldTransform.Origin.IsFinite() || !corpse.LinearVelocity.IsFinite() || !corpse.AngularVelocity.IsFinite())
                throw new InvalidOperationException("Godot corpse became nonfinite.");
        foreach (int id in _ids) CheckFinite(_world!.Read(id));
        Check(true, "Both corpse backends retain finite poses and velocities");
    }
    private static void CheckFinite(Box3dBodyState state)
    {
        if (!state.Position.IsFinite() || !state.LinearVelocity.IsFinite() || !state.AngularVelocity.IsFinite()
            || !float.IsFinite(state.Rotation.LengthSquared()) || state.Rotation.LengthSquared() < .5f)
            throw new InvalidOperationException("Native corpse became nonfinite.");
    }

    private void ClearAll()
    {
        SetBackend("idle");
        foreach (var pool in _pools) pool.Clear();
        _world?.ClearDynamics(); _ids.Clear(); _nativeRemaining = 0f;
        foreach (var visual in _visuals) visual.Visible = false;
    }
    private async Task Cleanup()
    {
        ClearAll(); _world?.Dispose(); _world = null;
        foreach (var pool in _pools) pool.QueueFree(); _pools.Clear();
        foreach (var visual in _visuals) visual.QueueFree(); _visuals.Clear(); _sizes.Clear();
        _snapshots.Clear(); _scratch.Clear();
        await PhysicsTicks(2);
        GC.Collect(); GC.WaitForPendingFinalizers(); await PhysicsTicks(2);
    }
    private async Task Capture(int count)
    {
        if (DisplayServer.GetName() == "headless") { Line("LIMIT: headless skips side-by-side image capture."); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(_directory, $"corpses-{count}.png")) != Error.Ok) throw new InvalidOperationException("Scene capture failed.");
    }
    private async Task PhysicsTicks(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
    private void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++; Line("PASS: " + description);
    }
    private static string Describe(List<double> values)
    {
        double[] ordered = values.OrderBy(x => x).ToArray();
        return $"mean={values.Average():F4}, median={ordered[ordered.Length / 2]:F4}, p95={ordered[(int)Math.Ceiling(ordered.Length * .95) - 1]:F4}, max={ordered[^1]:F4}ms";
    }
    private void Line(string text) { _report.AppendLine(text); GD.Print(text); }
    private void Save()
    {
        if (!_canSave) return;
        File.WriteAllText(Path.Combine(_directory, "report.txt"), _report.ToString());
        File.WriteAllText(Path.Combine(_directory, "samples.csv"), _csv.ToString());
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_demo || _respawning || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.P) { _running = !_running; SetBackend("both"); }
        if (key.Keycode == Key.C) ClearAll();
        if (key.Keycode == Key.R) RespawnDemo();
        if (key.Keycode == Key.Escape) { _world?.Dispose(); _world = null; GetTree().Quit(); }
    }
    private async void RespawnDemo()
    {
        _respawning = true;
        try { await SpawnMatched(_capacity); SetBackend("both"); }
        catch (Exception error) { Line("FAILED: " + error); Save(); GetTree().Quit(1); }
        finally { _respawning = false; }
    }
    public override void _ExitTree() { _world?.Dispose(); _world = null; }
    private static Mesh PyramidMesh()
    {
        using SurfaceTool tool = new(); tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] vertices = { new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(.5f, -.5f, .5f), new(-.5f, -.5f, .5f), new(0f, .5f, 0f) };
        foreach (int index in new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 0, 3, 2, 0, 2, 1 }) tool.AddVertex(vertices[index]);
        tool.GenerateNormals(); return tool.Commit();
    }
}
