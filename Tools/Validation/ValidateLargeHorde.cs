using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Deliberately freezes the real map: population, publication and simulation are measured separately.
public partial class ValidateLargeHorde : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private FpsVerticalSlice _map = null!;
    private FpsHordeManager _crowd = null!;
    private readonly List<FpsHordeEnemySnapshot> _alive = new(100000);
    private Control _menu = null!;
    private SpinBox _count = null!;
    private OptionButton _role = null!;
    private int _checks;
    private bool _headless;

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/large-horde-validation/"), "Isolated save directory");
            _headless = DisplayServer.GetName() == "headless";
            if (!_headless) Check(RenderingServer.GetCurrentRenderingDriverName() == "vulkan", "Native Vulkan device");
            GD.Print($"Hardware: {OS.GetProcessorName()}; logical processors={OS.GetProcessorCount()}; renderer={RenderingServer.GetVideoAdapterName()}; driver={RenderingServer.GetCurrentRenderingDriverName()}; mode={DisplayServer.GetName()}");
            FpsHordeSettings cached = GD.Load<FpsHordeSettings>("res://Resources/Fps/HillFort_Horde.tres");
            int cachedCapacity = cached.Capacity;
            Check(cachedCapacity == 100000, "Shipped fort capacity is 100k");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            _map.ProcessMode = ProcessModeEnum.Disabled;
            Check(_map.HordeSettings!.Capacity == 100000, "Default scene capacity is 100k before run-local overrides");
            _map.HordeSettings = (FpsHordeSettings)_map.HordeSettings!.Duplicate(true);
            _map.HordeSettings.Capacity = 100000;
            _map.HordeSettings.GpuCrowdContactsEnabled = true;
            AddChild(_map);
            await Frames(2);
            _map.DebugPlayerInvulnerable = _map.DebugCoreInvulnerable = true;
            foreach (FpsDefenseGate gate in _map.DefenseGates) gate.DebugInvulnerable = true;
            _crowd = _map.HordeManager!;
            _crowd.SetCombatEnabled(true);
            Check(_crowd.Capacity == 100000 && _crowd.ActiveCount == 0, "100k capacity is available without automatic population growth");
            Check(_map.EncounterSettings!.Waves[0].EnemySpawns.Sum(s => s.Count) == 100, "Configured first wave remains 100");
            Check((bool)typeof(FpsVerticalSlice).GetMethod("TrySpawnNextWave", Private)!.Invoke(_map, null)!, "Real director starts its first wave");
            ValidatePopulation(100, true, "default first wave");
            Tick(1);
            Check(_headless || _crowd.GpuContactBufferCapacity == 256, "100 live bodies use a 256-slot GPU prefix rather than reserved 100k slots");
            ValidateGpuPrefix();
            ValidateActiveSlots();
            _map.DebugClearHorde();

            _map._Input(new InputEventKey { Keycode = Key.Quoteleft, Pressed = true });
            _menu = Field<Control>("_gameplayTuningMenu");
            _count = Field<SpinBox>("_gameplayTuningSpawnCount");
            _role = Descendants(_menu).OfType<OptionButton>().First(o => o.ItemCount == 2 && o.GetItemText(0) == "Cube / melee");
            Check(_count.MaxValue == 100000, "Real menu accepts 100k");
            foreach (int preset in new[] { 10000, 25000, 50000, 100000 })
            {
                Button(preset.ToString()).EmitSignal(Godot.Button.SignalName.Pressed);
                Check(_count.Value == preset && _crowd.ActiveCount == 0, $"Preset {preset} updates count without spawning");
            }
            SelectRole(FpsEnemyRole.Bruiser);
            SpawnMenu(100000, "Replace");
            ValidatePopulation(100000, true, "100k cube Replace");
            Check(_alive.All(b => b.Role == FpsEnemyRole.Bruiser), "All 100k replaced bodies are cubes");
            FpsHordeHandle stale = _alive[0].Handle;
            Vector3 first = _alive[0].Position;
            SpawnMenu(100000, "Add");
            Check(_crowd.ActiveCount == 100000 && _map.ActiveEnemyCount == 100000 && _crowd.TryGet(stale, out var unchanged) && unchanged.Position == first,
                "Add at full capacity rejects new bodies and preserves existing handles");
            Check(_crowd.ApplyDamage(stale, 1000000f), "One full-population slot can be released");
            SpawnMenu(1, "Add");
            ValidatePopulation(100000, true, "near-full Add refills one slot");
            Check(!_crowd.IsAlive(stale), "Refilling a slot invalidates its prior generation");
            Tick(_headless ? 1 : 3);
            ValidatePopulation(100000, false, "evolving 100k");
            FpsHordeHandle clearStale = _alive[^1].Handle;
            Check(_headless || _crowd.GpuContactBufferCapacity == 100000, "100k simulation covers all live slots");
            if (!_headless)
            {
                long frozenSteps = _crowd.SimulationSteps;
                await Frames(4);
                Check(_crowd.SimulationSteps == frozenSteps && _crowd.RenderedInstanceCount == 100000,
                    "Native rendering advances frames with 100k published bodies while simulation stays frozen");
            }
            Button("Clear").EmitSignal(Godot.Button.SignalName.Pressed);
            ValidatePopulation(0, false, "100k Clear");
            await Frames(3);
            ValidatePopulation(0, false, "Clear remains stable on subsequent frames");
            SpawnMenu(100, "Replace");
            Tick(1);
            ValidatePopulation(100, false, "100-body respawn");
            Check(!_crowd.IsAlive(clearStale) && !_crowd.ApplyDamage(clearStale, 1000000f), "Cleared live handle cannot affect respawned bodies");
            Check(_headless || _crowd.GpuContactBufferCapacity == 256, "GPU allocation shrinks after 100k Clear and 100-body respawn");

            SelectRole(FpsEnemyRole.Striker);
            SpawnMenu(10000, "Replace");
            ValidatePopulation(10000, true, "10k pyramid Replace");
            Check(_alive.All(b => b.Role == FpsEnemyRole.Striker), "Pyramid selector creates real ranged records");
            SelectRole(FpsEnemyRole.Bruiser);
            SpawnMenu(50000, "Replace");
            ValidatePopulation(50000, true, "first 50k cubes");
            _crowd.CopyAlive(_alive);
            FpsHordeHandle retained = _alive[0].Handle;
            Vector3 retainedPosition = _alive[0].Position;
            SelectRole(FpsEnemyRole.Striker);
            SpawnMenu(50000, "Add");
            ValidatePopulation(100000, true, "50k cubes plus 50k pyramids Add");
            Check(_alive.Count(b => b.Role == FpsEnemyRole.Bruiser) == 50000 && _alive.Count(b => b.Role == FpsEnemyRole.Striker) == 50000,
                "Mixed Add preserves 50k records of each role");
            Check(_crowd.TryGet(retained, out var retainedBody) && retainedBody.Position == retainedPosition, "Add preserves existing cube placement and handles");
            Tick(_headless ? 1 : 2);
            ValidatePopulation(100000, false, "evolving mixed 100k");
            if (!_headless)
            {
                _map._Input(new InputEventKey { Keycode = Key.Quoteleft, Pressed = true });
                Check(!Field<bool>("_gameplayTuningVisible"), "Tuning menu closes before live rendered smoke");
                long beforeLive = _crowd.SimulationSteps;
                _map.ProcessMode = ProcessModeEnum.Inherit;
                await Frames(8);
                _map.ProcessMode = ProcessModeEnum.Disabled;
                Check(_crowd.SimulationSteps > beforeLive && _crowd.LastCrowdContactBackend == "GPU",
                    "Live rendered 100k map advances authoritative GPU simulation through normal physics processing");
                ValidatePopulation(100000, false, "live rendered mixed 100k smoke");
            }
            Check(cached.Capacity == cachedCapacity && GD.Load<FpsHordeSettings>("res://Resources/Fps/HillFort_Horde.tres").Capacity == cachedCapacity,
                "Run-local setup leaves cached capacity unchanged");
            Check(_map.EncounterSettings.Waves[0].EnemySpawns.Sum(s => s.Count) == 100, "Manual spawning leaves default first-wave settings unchanged");
            Check(GD.Load<FpsEncounterSettings>("res://Resources/Fps/HillFort_Encounter.tres").Waves[0].EnemySpawns.Sum(s => s.Count) == 100,
                "Cached first-wave resource remains unchanged");
            ReportMemory("final populated map");
            _map.QueueFree();
            await Frames(3);
            Check(!GodotObject.IsInstanceValid(_map), "Large map and GPU device teardown completes");
            GD.Print($"Large horde validation passed: {_checks} checks; mode={(_headless ? "headless CPU fallback" : "native Vulkan")}. Timings are isolated operations, not FPS.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void SpawnMenu(int count, string action)
    {
        _count.Value = count;
        Stopwatch watch = Stopwatch.StartNew();
        Button(action).EmitSignal(Godot.Button.SignalName.Pressed);
        GD.Print($"Menu {action} {count}: {watch.Elapsed.TotalMilliseconds:F3} ms; actual={_crowd.ActiveCount}");
    }

    private void Tick(int count)
    {
        Stopwatch watch = Stopwatch.StartNew();
        long before = _crowd.SimulationSteps;
        for (int i = 0; i < count; i++) _crowd.TickSimulation(1f / 30f);
        Check(_crowd.SimulationSteps == before + count, $"Advanced exactly {count} deliberate simulation steps");
        Check(_crowd.LastCrowdContactBackend == (_headless ? "CPU" : "GPU"), $"Expected contact backend; actual={_crowd.LastCrowdContactBackend}; fallback={_crowd.GpuCrowdContactFallbackReason}");
        if (_headless) Check(_crowd.GpuCrowdContactFallbackReason.Contains("Vulkan"), "Headless deliberately falls back without a GPU device");
        GD.Print($"Simulation {_crowd.ActiveCount} bodies / {count} steps: {watch.Elapsed.TotalMilliseconds:F3} ms total; last contacts+rolling={_crowd.LastContactMilliseconds:F3} ms; no FPS assertion");
    }

    private void ValidatePopulation(int expected, bool separated, string name)
    {
        _crowd.CopyAlive(_alive);
        Check(_alive.Count == expected && _crowd.ActiveCount == expected && _map.ActiveEnemyCount == expected && Field<IDictionary>("_hordeTargets").Count == expected,
            $"{name}: live slots, combat registrations and target dictionary agree ({expected})");
        Stopwatch watch = Stopwatch.StartNew();
        _crowd._Process(0);
        int visible = Descendants(_crowd).OfType<MultiMeshInstance3D>().Sum(n => n.Multimesh.VisibleInstanceCount);
        Check(_crowd.RenderedInstanceCount == expected && visible == expected, $"{name}: actual shared render instances agree");
        var renderNodes = Descendants(_crowd).OfType<MultiMeshInstance3D>().ToArray();
        Check(renderNodes.First(n => n.Name == "CrowdCubes").Multimesh.VisibleInstanceCount == _alive.Count(b => b.Role == FpsEnemyRole.Bruiser)
            && renderNodes.First(n => n.Name == "CrowdPyramids").Multimesh.VisibleInstanceCount == _alive.Count(b => b.Role == FpsEnemyRole.Striker),
            $"{name}: cube and pyramid render instances match their authoritative roles");
        GD.Print($"Render publication {expected}: {watch.Elapsed.TotalMilliseconds:F3} ms (GPU drawing excluded)");
        float half = _map.HillMapSettings!.PlayableSize * .5f;
        bool finite = true, terrain = true;
        foreach (var body in _alive)
        {
            finite &= body.Position.IsFinite() && body.HeadPosition.IsFinite() && float.IsFinite(body.CurrentHealth)
                && Mathf.Abs(body.Position.X) <= half - body.Radius + .001f && Mathf.Abs(body.Position.Z) <= half - body.Radius + .001f;
            terrain &= body.Position.Y >= _map.SampleHillSurfaceHeight(body.Position) - .002f;
        }
        Check(finite && terrain, $"{name}: finite bodies inside physical map bounds and above terrain");
        if (separated) ValidateSeparation(name);
    }

    private void ValidateSeparation(string name)
    {
        float cell = _alive.Count == 0 ? 1f : _alive.Max(b => b.Radius) * 2f + .001f;
        var bins = new Dictionary<(int, int), List<int>>(_alive.Count);
        long comparisons = 0;
        for (int i = 0; i < _alive.Count; i++)
        {
            var body = _alive[i];
            int x = (int)MathF.Floor(body.Position.X / cell), z = (int)MathF.Floor(body.Position.Z / cell);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                if (bins.TryGetValue((x + dx, z + dz), out var neighbors)) foreach (int j in neighbors)
                {
                    var other = _alive[j];
                    float distance = new Vector2(body.Position.X - other.Position.X, body.Position.Z - other.Position.Z).LengthSquared();
                    float radius = body.Radius + other.Radius;
                    comparisons++;
                    if (distance + .0001f < radius * radius) throw new InvalidOperationException($"{name}: overlapping footprints {i}/{j}: {body.Position} vs {other.Position}");
                }
            if (!bins.TryGetValue((x, z), out var bucket)) bins.Add((x, z), bucket = new List<int>(2));
            bucket.Add(i);
        }
        Check(true, $"{name}: physical footprints do not overlap ({comparisons} neighboring pairs)");
    }

    private void SelectRole(FpsEnemyRole role)
    {
        int index = role == FpsEnemyRole.Striker ? 1 : 0;
        _role.Select(index);
        _role.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        Check(_map.DebugSpawnRole == role, $"Real selector chooses {role}");
    }
    private void ReportMemory(string label)
    {
        using Process process = Process.GetCurrentProcess();
        GD.Print($"Memory ({label}): working set={process.WorkingSet64 / 1048576d:F1} MiB; private={process.PrivateMemorySize64 / 1048576d:F1} MiB; managed={GC.GetTotalMemory(false) / 1048576d:F1} MiB");
    }
    private Godot.Button Button(string text) => Descendants(_menu).OfType<Godot.Button>().First(b => b.Text == text);
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        GD.Print($"PASS: {message}");
    }
}
