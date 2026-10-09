using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class CaptureWeaponPolish : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private FpsVerticalSlice _map = null!;
    private FpsPlayerController _player = null!;
    private FpsWeaponPolishEffects Effects => _map.WeaponEffects ?? throw new InvalidOperationException("Scene cosmetic helper was not initialized.");
    private readonly List<string> _captures = new();
    private readonly List<object> _timings = new();
    private static readonly string[] Shapes = EquipmentCatalogDefaults.CreateDefaultShapes().Select(shape => shape.Key).ToArray();
    private string Root => ProjectSettings.GlobalizePath("res://.tmp/weapon-polish-capture");

    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/weapon-polish-capture/") || DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("Capture requires native graphics and isolated saves.");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            LegacyHillFixture.Configure(_map);
            _map.BruiserArchetypeSettings = (FpsEnemyArchetypeSettings)_map.BruiserArchetypeSettings!.Duplicate(true);
            _map.BruiserArchetypeSettings.MaxHealth = 1000000f;
            AddChild(_map);
            await Frames(5);
            ValidateWeaponPolish.CheckRenderedBeam(Effects, (passed, description) =>
            {
                if (!passed) throw new InvalidOperationException(description);
                GD.Print($"PASS: {description}");
            });
            _map.DebugPlayerInvulnerable = _map.DebugCoreInvulnerable = true;
            _map.SetPhysicsProcess(false);
            _map.SetProcess(false);
            _player = Field<FpsPlayerController>("_player");
            _player.SetPhysicsProcess(false);
            _player.SetProcess(false);
            _player.GlobalPosition = new Vector3(60f, 0.1f, -65f);
            _player.Rotation = Vector3.Zero;
            _player.Camera.Current = true;
            foreach (string shape in Shapes)
            {
                Prepare(shape, 100);
                _player.Camera.Current = true;
                await Frames(3);
                await Save($"Idle-{shape}");
                // Route one actual input event through the controller and current equipped item.
                if (shape != "klein")
                {
                    typeof(FpsPlayerController).GetField("_fireCooldown", Private)!.SetValue(_player, 0f);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    Input.FlushBufferedEvents();
                    try
                    {
                        await Frames(1);
                        _map._Process(1d / 60d);
                    }
                    finally
                    {
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                        Input.FlushBufferedEvents();
                    }
                    if ((float)typeof(FpsPlayerController).GetField("_fireCooldown", Private)!.GetValue(_player)! <= 0f)
                        throw new InvalidOperationException($"{shape}: actual input did not fire; combat={(bool)typeof(FpsPlayerController).GetField("_combatEnabled", Private)!.GetValue(_player)!}.");
                }
                foreach (AnimationPlayer animation in Descendants(_player).OfType<AnimationPlayer>().Where(animation => animation.IsPlaying()))
                {
                    animation.Advance(0.025);
                    animation.Pause();
                }
                if (shape == "hexagon") PlaceMineTriggerTarget();
                TickAttack(1f / 60f);
                await Save($"Fire-{shape}");
                float worldTime = WorldTime(shape);
                for (int i = 1; i < Mathf.RoundToInt(worldTime * 60f); i++) TickAttack(1f / 60f);
                Camera3D overview = new() { Fov = 65f };
                _map.AddChild(overview);
                bool localEffect = shape is "sphere" or "cube" or "klein" or "boys";
                overview.GlobalPosition = localEffect ? new Vector3(68f, 6f, -59f) : new Vector3(65f, 5f, -66f);
                overview.LookAt(localEffect ? new Vector3(60f, 1.1f, -66f) : new Vector3(60f, 1.1f, -78f));
                Vector3? mortarTarget = null;
                if (shape == "hyperphere")
                {
                    IList mortars = Field<IList>("_playerMortars");
                    if (mortars.Count == 0) throw new InvalidOperationException("Mortar World capture lost its in-flight shell.");
                    object mortar = mortars[0]!;
                    Vector3 target = (Vector3)mortar.GetType().GetField("Target")!.GetValue(mortar)!;
                    MeshInstance3D shell = (MeshInstance3D)mortar.GetType().GetField("Mesh")!.GetValue(mortar)!;
                    mortarTarget = target;
                    FrameMortar(overview, shell.GlobalPosition, target);
                    AssertInFrame(overview, shell.GlobalPosition, "Mortar shell");
                    AssertInFrame(overview, target, "Mortar ground target");
                }
                overview.Current = true;
                await Save($"World-{shape}");
                if (shape is "hexagon" or "pyramid" or "hyperphere" or "mobius" or "boys")
                {
                    float resolveTime = shape switch { "hexagon" => .40f, "hyperphere" => .98f, "pyramid" => .52f, "mobius" => .57f, _ => .8f };
                    for (int i = Mathf.RoundToInt(worldTime * 60f); i < Mathf.RoundToInt(resolveTime * 60f); i++) TickAttack(1f / 60f);
                    if (mortarTarget is Vector3 target)
                    {
                        overview.GlobalPosition = target + new Vector3(7f, 5f, 7f);
                        overview.LookAt(target + Vector3.Up);
                        AssertInFrame(overview, target, "Resolved mortar target");
                    }
                    await Save($"Resolve-{shape}");
                }
                overview.QueueFree();
                await Frames(1);
            }
            // Identical staged initial crowd/camera and scripted shot cadence for each budget.
            // The map still runs its real physics path; weapon firing is fixed to this benchmark route.
            _player.Camera.Current = true;
            Prepare("triangle", 2000);
            // Fire from the crowded north formation so local fields and seeking/chain attacks
            // exercise populated targets instead of empty terrain off the edge of the crowd.
            _player.GlobalPosition = new Vector3(0f, .1f, -76f);
            FpsHordeManager timingCrowd = _map.HordeManager!;
            FieldInfo records = typeof(FpsHordeManager).GetField("_enemies", Private)!;
            Array initialRecords = (Array)((Array)records.GetValue(timingCrowd)!).Clone();
            FieldInfo random = typeof(FpsHordeManager).GetField("_random", Private)!;
            object initialRandom = random.GetValue(timingCrowd)!;
            FieldInfo accumulator = typeof(FpsHordeManager).GetField("_accumulator", Private)!;
            object initialAccumulator = accumulator.GetValue(timingCrowd)!;
            foreach (bool enabled in new[] { false, true })
            {
                // Restore exact records, including generation-dependent rolling phases and RNG.
                // No deaths are possible with this million-health fixture, so handles/free slots stay valid.
                Invoke("ResetWeaponEffectRuntimeState");
                records.SetValue(timingCrowd, initialRecords.Clone());
                random.SetValue(timingCrowd, initialRandom);
                accumulator.SetValue(timingCrowd, initialAccumulator);
                typeof(FpsHordeManager).GetField("_gridDirty", Private)!.SetValue(timingCrowd, true);
                timingCrowd._Process(0d);
                Field<RandomNumberGenerator>("_weaponRng").Seed = 413;
                _map.WeaponPolishEnabled = Effects.Enabled = enabled;
                Camera3D overview = new() { Fov = 78f };
                _map.AddChild(overview);
                overview.GlobalPosition = new Vector3(85f, 55f, -135f);
                overview.LookAt(new Vector3(0f, 2f, -65f));
                overview.Current = true;
                List<double> intervals = new();
                long previous = Stopwatch.GetTimestamp();
                int maxTransient = 0, maxSegments = 0, peakSolid = 0, peakBurst = 0;
                for (int frame = 0; frame < 540; frame++)
                {
                    if (frame % 15 == 0)
                    {
                        string shape = Shapes[(frame / 15) % Shapes.Length];
                        // This cadence is a staged all-shape stress workload, not normal weapon cooldowns.
                        Field<List<EquipmentItem>>("_arsenal")[0] = Item(shape);
                        if (shape != "klein") Invoke("FireWeaponAttack", Item(shape), _player.HeadPosition, Vector3.Forward, 1f, false);
                    }
                    _map._PhysicsProcess(1d / 60d);
                    await Frames(1);
                    long now = Stopwatch.GetTimestamp();
                    if (frame >= 60) intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
                    previous = now;
                    maxTransient = Math.Max(maxTransient, Effects.ActiveTransientCount);
                    maxSegments = Math.Max(maxSegments, Effects.VisibleSegmentCount);
                    peakSolid = Math.Max(peakSolid, Effects.VisibleSolidCount);
                    peakBurst = Math.Max(peakBurst, Effects.ActiveCosmeticBurstCount);
                }
                intervals.Sort();
                if (_map.HordeManager!.ActiveCount != 2000)
                    throw new InvalidOperationException("Timing crowd count changed; paired samples are invalid.");
                if (maxTransient > FpsWeaponPolishEffects.MaxTransientInstances || maxSegments > FpsWeaponPolishEffects.MaxPersistentSegments ||
                    peakSolid > FpsWeaponPolishEffects.MaxSolidInstances || peakBurst > FpsWeaponPolishEffects.MaxCosmeticBursts)
                    throw new InvalidOperationException("Rendered combat exceeded the cosmetic capacity.");
                if (!enabled && (maxTransient != 0 || maxSegments != 0 || peakSolid != 0 || peakBurst != 0))
                    throw new InvalidOperationException("Zero-budget sample rendered cosmetic instances.");
                if (enabled && maxTransient == 0 && maxSegments == 0)
                    throw new InvalidOperationException("Normal-budget sample failed to render cosmetics.");
                string budget = enabled ? "normal" : "zero";
                await Save($"Crowd-2000-{budget}");
                _timings.Add(new { budget, requested_crowd = 2000, final_crowd = _map.HordeManager!.ActiveCount, samples = intervals.Count,
                    median_ms = intervals[intervals.Count / 2], p95_ms = intervals[(int)Math.Ceiling(intervals.Count * 0.95) - 1],
                    max_ms = intervals[^1], max_transient = maxTransient, max_segments = maxSegments, peak_solid = peakSolid, peak_burst = peakBurst });
                overview.QueueFree();
                await Frames(1);
            }
            System.IO.File.WriteAllText($"{Root}/completed.json", JsonSerializer.Serialize(new
            {
                shapes = Shapes, captures = _captures, timings = _timings, completed_utc = DateTime.UtcNow,
                godot = Engine.GetVersionInfo()["string"].AsString(), gpu = RenderingServer.GetVideoAdapterName(),
                viewport = GetViewport().GetVisibleRect().Size.ToString(), vsync = DisplayServer.WindowGetVsyncMode().ToString(),
                sample_scope = "Process-frame wall intervals include fixed 60Hz manual combat simulation, rendering, pacing, and scheduling. Same initial crowd record snapshot/RNG, staged camera, 2000 crowd, all sixteen shapes every 15 frames (Klein passive); 60 warmup then 480 sampled frames. Forced stress cadence exceeds normal weapon cooldowns. Sequential short samples are diagnostic, not GPU execution time or sustained gameplay FPS. Screenshots freeze effect age for the draw; near-player attacks stage nearby targets, others use an eleven-meter firing lane.",
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("Weapon polish capture passed.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Prepare(string shape, int count)
    {
        Invoke("ResetWeaponEffectRuntimeState");
        if (_map.DebugSpawnHorde(count, true) != count || !_map.TrySetArenaDebugLoadoutItem(0, shape, "divine", 5))
            throw new InvalidOperationException($"Could not prepare {shape}/{count} fixture.");
        // Debug generation rolls output/area substats; capture the deterministic base shape instead.
        Field<List<EquipmentItem>>("_arsenal")[0] = Item(shape);
        Invoke("EquipWeapon", 0, false);
        _player.SetDebugHealth(1000f, 500f);
        Field<RandomNumberGenerator>("_weaponRng").Seed = 413;
        if (count == 100)
        {
            List<IFpsCombatEnemy> targets = Field<List<IFpsCombatEnemy>>("_combatEnemies");
            var crowd = _map.HordeManager!;
            var constrain = crowd.ConstrainMovement;
            crowd.ConstrainMovement = null;
            try
            {
                for (int i = 0; i < targets.Count; i++) targets[i].GlobalPosition = new Vector3(60f + (i % 10 - 4) * 1.3f, 0f, -76f - i / 10 * 1.8f);
                targets[0].GlobalPosition = new Vector3(60f, 0f, -76f);
                if (shape is "sphere" or "cube" or "klein")
                {
                    targets[0].GlobalPosition = new Vector3(60f, 0f, -68f);
                    targets[1].GlobalPosition = new Vector3(63f, 0f, -65f);
                    targets[2].GlobalPosition = new Vector3(57f, 0f, -65f);
                }
                _player.Camera.LookAt(targets[0].HeadPosition);
            }
            finally { crowd.ConstrainMovement = constrain; }
            // Repositioning records invalidates spatial queries, but the GPU buffer still needs upload.
            crowd._Process(0d);
        }
    }
    private void TickAttack(float dt)
    {
        Effects.BeginFrame(dt, _player.HeadPosition);
        Invoke("TickPlayerProjectiles", dt);
        Invoke("TickMines", dt);
        Invoke("TickZones", dt);
        Invoke("TickOrbitals", dt);
        Invoke("TickMortars", dt);
        Invoke("TickEchoes", dt);
        Invoke("TickClones", dt);
        Invoke("TickWrathAura", dt);
        Effects.EndFrame();
    }
    private void PlaceMineTriggerTarget()
    {
        IList mines = Field<IList>("_playerMines");
        if (mines.Count == 0) throw new InvalidOperationException("Mine capture did not deploy any mines.");
        object mine = mines[0]!;
        Vector3 point = (Vector3)mine.GetType().GetField("Position")!.GetValue(mine)!;
        var crowd = _map.HordeManager!;
        var previous = crowd.ConstrainMovement;
        crowd.ConstrainMovement = null;
        try { Field<List<IFpsCombatEnemy>>("_combatEnemies")[0].GlobalPosition = point - Vector3.Up * .3f; }
        finally { crowd.ConstrainMovement = previous; }
        crowd._Process(0d);
    }
    private static float WorldTime(string shape) => shape switch
    {
        "sphere" or "cone" => .1f,
        "pentachoron" or "mobius" => .05f,
        "cube" or "klein" => .3f,
        "hyperphere" => .4f,
        "boys" => .22f,
        _ => .2f,
    };
    private static void FrameMortar(Camera3D camera, Vector3 shell, Vector3 target)
    {
        Vector3 center = (shell + target) * .5f;
        float distance = Mathf.Max(12f, shell.DistanceTo(target) * 1.35f);
        camera.GlobalPosition = center + new Vector3(1f, .65f, 1f).Normalized() * distance;
        camera.LookAt(center);
    }
    private void AssertInFrame(Camera3D camera, Vector3 point, string description)
    {
        Vector2 projected = camera.UnprojectPosition(point);
        Rect2 viewport = GetViewport().GetVisibleRect();
        Rect2 inset = new(viewport.Position + viewport.Size * .04f, viewport.Size * .92f);
        if (camera.IsPositionBehind(point) || !projected.IsFinite() || !inset.HasPoint(projected))
            throw new InvalidOperationException($"{description} is outside the useful capture framing.");
    }
    private static EquipmentItem Item(string shape) => new()
    {
        Shape = EquipmentCatalogDefaults.CreateShapeByKey(shape), Rarity = EquipmentCatalogDefaults.CreateRarityByKey("divine"),
        Level = 5, Damage = 100, Health = 100, Substats = new() { new(SubstatType.CritChance, -100f) },
    };
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node descendant in Descendants(child)) yield return descendant; }
    }
    private object? Invoke(string name, params object[] args) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, args);
    private async Task Save(string name)
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error result = GetViewport().GetTexture().GetImage().SavePng($"{Root}/{name}.png");
        if (result != Error.Ok) throw new InvalidOperationException($"Screenshot {name}: {result}");
        _captures.Add(name);
        GD.Print($"CAPTURE: {Root}/{name}.png");
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
