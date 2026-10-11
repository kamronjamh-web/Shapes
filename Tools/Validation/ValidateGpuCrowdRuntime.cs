using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps.Gpu;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateGpuCrowdRuntime : Node
{
    private const float Dt = 1f / 30f;
    private int _checks;
    private bool _headless;
    private PackedScene _mapScene = null!;
    private readonly List<FpsHordeEnemySnapshot> _alive = new();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/gpu-crowd-runtime-validation/"), "Save data is isolated");
            Check(GD.Load<FpsHordeSettings>("res://Resources/Fps/HillFort_Horde.tres").GpuCrowdContactsEnabled,
                "The shipped fort resource enables GPU contacts");
            _headless = DisplayServer.GetName() == "headless";
            // Keep the imported scene and its managed Resource wrappers alive while
            // repeated parity snapshots exercise GC between map recreations.
            _mapScene = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn");
            if (!_headless) Check(RenderingServer.GetCurrentRenderingDriverName() == "vulkan", "Native validation uses Vulkan");
            await ValidateMovementParity();
            await ValidateDriveParity();
            await ValidateRollingPrecisionParity();
            await ValidateBucketPrefixParity();
            foreach (int count in new[] { 200, 2000 })
            {
                FpsVerticalSlice map = await CreateMap();
                FpsHordeManager crowd = map.HordeManager!;
                Check(map.DebugSpawnHorde(count, true) == count, $"Registered {count} bodies");
                StagePressure(map);
                ValidateIndependentPacking(crowd);
                double contactMs = 0;
                for (int tick = 0; tick < 120; tick++)
                {
                    if (tick == 60)
                    {
                        FpsDefenseGate gate = map.DefenseGates[0];
                        gate.DebugInvulnerable = false;
                        gate.ApplyDamage(1000000f);
                        Check(!gate.IsAlive && map.CaptureGpuCrowdWorld()!.Gates[0].Center.W == 0f, "Breach updates cached gate state");
                    }
                    if (tick == 100)
                    {
                        map.DebugResetDefenseGates();
                        foreach (FpsDefenseGate gate in map.DefenseGates) gate.DebugInvulnerable = true;
                        Check(map.CaptureGpuCrowdWorld()!.Gates[0].Center.W == 1f, "Reset updates cached gate state");
                    }
                    Tick(crowd, _headless ? "CPU" : "GPU");
                    contactMs += crowd.LastContactMilliseconds;
                    if (tick % 8 == 0)
                    {
                        ValidatePositions(map, count);
                        await Frames(1);
                    }
                }
                ValidatePositions(map, count);
                GD.Print($"Runtime {count} / {(_headless ? "headless CPU fallback" : "GPU contacts + fort world")}: 120 evolving ticks; average contacts+rolling {contactMs / 120:F4} ms. This is not whole-game FPS.");
                ValidateAuthoritativeHandles(map);
                ValidateFallbacks(map);
                await ValidateRetune(map);
                if (!_headless && count == 2000)
                {
                    long before = crowd.SimulationSteps;
                    map.ProcessMode = ProcessModeEnum.Inherit;
                    // Uncapped/disabled rendering can finish dozens of process
                    // frames before the next fixed physics tick is due. Wait for
                    // real simulation progress, bounded by wall time.
                    ulong deadline = Time.GetTicksMsec() + 5000;
                    int liveFrames = 0;
                    do
                    {
                        await Frames(1);
                        liveFrames++;
                    }
                    while ((crowd.SimulationSteps < before + 2 || crowd.RenderedInstanceCount != crowd.ActiveCount)
                        && Time.GetTicksMsec() < deadline);
                    Check(crowd.SimulationSteps > before && crowd.LastCrowdContactBackend == "GPU",
                        "The live scene advances GPU contacts through its normal physics loop");
                    Check(crowd.RenderedInstanceCount == crowd.ActiveCount,
                        "The live scene publishes every authoritative body to shared rendering");
                    GD.Print($"Live physics publication: {crowd.SimulationSteps - before} steps / {liveFrames} process frames within the 5s timeout.");
                    map.ProcessMode = ProcessModeEnum.Disabled;
                    ValidatePositions(map, count);
                }
                map.QueueFree();
                await Frames(2);
                Check(!GodotObject.IsInstanceValid(map), "Map teardown completed before device recreation");
                // Release managed owners of native Resource wrappers as well as
                // the nodes. Async locals otherwise retain the last freed map.
                map = null!;
                crowd = null!;
            }
            // Corpse and rebuilt gate shapes have managed wrappers. Finalize
            // them before shutting down the physics server, then drain queued
            // native frees; fast process frames alone need not advance physics.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await Frames(2);
            GD.Print($"GPU crowd runtime validation passed: {_checks} checks; mode={(_headless ? "headless fallback" : "native Vulkan")}.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task<FpsVerticalSlice> CreateMap(int? capacity = null)
    {
        FpsVerticalSlice map = _mapScene.Instantiate<FpsVerticalSlice>();
        map.ProcessMode = ProcessModeEnum.Disabled;
        map.HordeSettings = (FpsHordeSettings)map.HordeSettings!.Duplicate(true);
        if (capacity.HasValue) map.HordeSettings.Capacity = capacity.Value;
        map.HordeSettings.GpuCrowdContactsEnabled = true;
        AddChild(map);
        map.DebugPlayerInvulnerable = map.DebugCoreInvulnerable = true;
        foreach (FpsDefenseGate gate in map.DefenseGates) gate.DebugInvulnerable = true;
        map.HordeManager!.SetCombatEnabled(true);
        await Frames(2);
        return map;
    }

    private void Tick(FpsHordeManager crowd, string backend)
    {
        long steps = crowd.SimulationSteps;
        crowd.TickSimulation(Dt);
        Check(crowd.SimulationSteps == steps + 1, "Exactly one authoritative simulation step advanced");
        Check(crowd.LastCrowdContactBackend == backend, $"Expected {backend}, got {crowd.LastCrowdContactBackend}: {crowd.GpuCrowdContactFallbackReason}");
        if (_headless && crowd.Settings.GpuCrowdContactsEnabled && crowd.Settings.SoftCollisionEnabled && crowd.Settings.ContactIterations == 1)
            Check(crowd.GpuCrowdContactFallbackReason.Contains("Vulkan"), "Headless runtime deliberately uses CPU fallback");
    }

    private void StagePressure(FpsVerticalSlice map)
    {
        FpsHordeManager crowd = map.HordeManager!;
        crowd.CopyAlive(_alive);
        for (int i = 0; i < _alive.Count; i++)
        {
            Vector3 outward = (i % 4) switch { 0 => Vector3.Forward, 1 => Vector3.Right, 2 => Vector3.Back, _ => Vector3.Left };
            Vector3 across = new(-outward.Z, 0, outward.X);
            int row = i / 4;
            Vector3 position = outward * (map.HillMapSettings!.RoomHalfSize + 3f + row / 20 * .8f) + across * ((row % 20 - 9.5f) * .8f);
            position.Y = map.SampleHillSurfaceHeight(position);
            Check(crowd.TrySetPosition(_alive[i].Handle, position), "Staged live handle remains valid");
        }
    }

    private void ValidatePositions(FpsVerticalSlice map, int count)
    {
        FpsHordeManager crowd = map.HordeManager!;
        crowd.CopyAlive(_alive);
        Check(crowd.ActiveCount == count && map.ActiveEnemyCount == count && _alive.Count == count, "CPU registration and live slots agree");
        float half = map.HillMapSettings!.PlayableSize * .5f;
        foreach (FpsHordeEnemySnapshot body in _alive)
        {
            Vector3 position = body.Position;
            Check(position.IsFinite() && Mathf.Abs(position.X) <= half - body.Radius + .001f && Mathf.Abs(position.Z) <= half - body.Radius + .001f,
                "Authoritative GPU/CPU positions remain finite and inside map bounds");
            Check(position.Y >= map.SampleHillSurfaceHeight(position) - .001f, "Bodies remain above terrain");
        }
    }

    private void ValidateIndependentPacking(FpsHordeManager crowd)
    {
        var first = new GpuCrowdBody[crowd.Capacity];
        var second = new GpuCrowdBody[crowd.Capacity];
        crowd.PackGpuCrowd(first, (float)Dt, worldResolved: true);
        crowd.PackGpuCrowd(second, (float)Dt, worldResolved: true);
        crowd.CopyAlive(_alive);
        FpsHordeEnemySnapshot body = _alive[0];
        first[body.Handle.Slot].PositionRadius.X += 100f;
        Check(second[body.Handle.Slot].PositionRadius.X == body.Position.X && crowd.TryGet(body.Handle, out var current) && current.Position == body.Position,
            "GPU snapshots are independent storage and do not mutate CPU authority");
    }

    private void ValidateAuthoritativeHandles(FpsVerticalSlice map)
    {
        FpsHordeManager crowd = map.HordeManager!;
        crowd.CopyAlive(_alive);
        FpsHordeEnemySnapshot victim = _alive[0];
        int count = crowd.ActiveCount;
        var query = new List<FpsHordeEnemySnapshot>();
        crowd.QueryRadius(victim.Position, 0f, query);
        Check(query.Exists(body => body.Handle == victim.Handle), "Weapon broad phase sees current authoritative position");
        Check(crowd.ApplyDamage(victim.Handle, 1000000f) && !crowd.TryGet(victim.Handle, out _), "Damage invalidates a live handle immediately");
        crowd.FlushDeathEvents();
        Check(crowd.ActiveCount == count - 1 && map.ActiveEnemyCount == count - 1, "Death events remove combat registration");
        crowd.QueryRadius(victim.Position, 0f, query);
        Check(!query.Exists(body => body.Handle == victim.Handle), "Weapon broad phase excludes dead slots");
        Check(map.DebugSpawnHorde(1, false) == 1, "Production spawn replaces the removed registration");
        crowd.CopyAlive(_alive);
        FpsHordeEnemySnapshot replacement = _alive.Find(body => body.Handle.Slot == victim.Handle.Slot);
        Check(replacement.Handle.Generation != victim.Handle.Generation && crowd.TryGet(replacement.Handle, out _) && !crowd.ApplyDamage(victim.Handle, 1f),
            "Recycled GPU slot has a new generation and rejects the stale handle");
        Tick(crowd, _headless ? "CPU" : "GPU");
    }

    private void ValidateFallbacks(FpsVerticalSlice map)
    {
        FpsHordeManager crowd = map.HordeManager!;
        crowd.Settings.ContactIterations = 2;
        Tick(crowd, "CPU");
        Check(crowd.GpuCrowdContactFallbackReason.Contains("iteration"), "Multiple contact passes use CPU");
        crowd.Settings.ContactIterations = 1;
        crowd.Settings.SoftCollisionEnabled = false;
        Tick(crowd, "CPU");
        crowd.Settings.SoftCollisionEnabled = true;
        Func<GpuCrowdWorld?>? provider = crowd.GpuCrowdWorldProvider;
        crowd.GpuCrowdWorldProvider = () => null;
        Tick(crowd, "CPU");
        if (!_headless) Check(crowd.GpuCrowdContactFallbackReason.Contains("world"), "Unsupported world uses CPU");
        crowd.GpuCrowdWorldProvider = provider;
        float extent = crowd.Settings.MapExtent;
        crowd.Settings.MapExtent = extent - 100f;
        Tick(crowd, "CPU");
        if (!_headless) Check(crowd.GpuCrowdContactFallbackReason.Contains("bounds"), "Mismatched crowd and fort bounds use CPU");
        crowd.Settings.MapExtent = extent;
        if (!_headless)
        {
            int attempts = 0;
            crowd.GpuCrowdWorldProvider = () => { attempts++; throw new InvalidOperationException("Intentional validation failure before state mutation"); };
            Tick(crowd, "CPU");
            Tick(crowd, "CPU");
            Check(attempts == 1, "Execution failure is latched instead of retried every frame");
            crowd.GpuCrowdWorldProvider = provider;
            Tick(crowd, "CPU");

            // Real menu signals must recover without an intervening physics tick,
            // including when the game is paused and never observes the off state.
            CheckBox toggle = FindGpuToggle(map) ?? throw new InvalidOperationException("GPU tuning toggle missing.");
            long steps = crowd.SimulationSteps;
            toggle.ButtonPressed = false;
            Check(!crowd.Settings.GpuCrowdContactsEnabled && crowd.GpuCrowdContactFallbackReason.Length == 0,
                "Disabling the menu toggle immediately clears failure while simulation is stopped");
            toggle.ButtonPressed = true;
            Check(crowd.Settings.GpuCrowdContactsEnabled && crowd.SimulationSteps == steps,
                "Off/on menu recovery does not require a simulation tick");
            Tick(crowd, "GPU");
        }
        crowd.Settings.GpuCrowdContactsEnabled = false;
        Tick(crowd, "CPU");
        Check(crowd.GpuCrowdContactFallbackReason.Length == 0, "Opting out clears the fallback latch");
        crowd.Settings.GpuCrowdContactsEnabled = true;
        Tick(crowd, _headless ? "CPU" : "GPU");
        GD.Print("PASS: runtime eligibility, failure latch, and explicit off/on recovery.");
    }

    private async Task ValidateRetune(FpsVerticalSlice map)
    {
        GpuCrowdWorld oldWorld = map.CaptureGpuCrowdWorld()!;
        int oldVersion = map.FortSpatialIndexVersion;
        map.HillMapSettings!.RoomWallHeight += 1f;
        MethodInfo apply = typeof(FpsVerticalSlice).GetMethod("ApplyPendingLiveTuningChanges", BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(map, null);
        await Frames(2);
        GpuCrowdWorld newWorld = map.CaptureGpuCrowdWorld()!;
        Check(map.FortSpatialIndexVersion > oldVersion && !ReferenceEquals(oldWorld, newWorld), "Live geometry retuning replaces GPU static world data");
        Tick(map.HordeManager!, _headless ? "CPU" : "GPU");
        ValidatePositions(map, map.HordeManager!.ActiveCount);
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static CheckBox? FindGpuToggle(Node node)
    {
        if (node is CheckBox check && check.Text == "GPU crowd contacts") return check;
        foreach (Node child in node.GetChildren())
            if (FindGpuToggle(child) is CheckBox result) return result;
        return null;
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
