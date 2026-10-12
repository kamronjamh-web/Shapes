using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using BitOperations = System.Numerics.BitOperations;

namespace SuperGatchaRebuild.Validation;

// Compare the indexed callbacks with their original full-scan implementation,
// then assert independent scene contracts so two wrong answers cannot pass.
public partial class ValidateFortSpatialIndex : Node
{
    private const float Tolerance = 0.00001f;
    private FpsVerticalSlice _map = null!;
    private int _checks, _queries;
    private readonly Random _random = new(731902);

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/fort-spatial-index-validation/"), "Save data is isolated");
            FpsHillMapSettings cached = GD.Load<FpsHillMapSettings>("res://Resources/Fps/HillFort_Hill.tres");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            LegacyExpandedFortFixture.Configure(_map);
            _map.ProcessMode = ProcessModeEnum.Disabled;
            AddChild(_map);
            Check(!ReferenceEquals(cached, _map.HillMapSettings), "Live geometry settings are separate from cached defaults");
            Check(_map.FortSpatialQueriesEnabled, "Production uses the nearby structure index by default");
            ValidateContracts();
            ValidateCandidateReduction();
            ValidateDifferential("default", 5000);
            ValidateCombinedWorldResolver("default", 3000);
            ValidateCellBoundaries();
            await ValidateRebuild();
            Check(cached.RoomHalfSize == 100f && cached.RoomWallHeight == 6.096f && cached.FortificationsEnabled,
                "Geometry retuning leaves the cached production resource untouched");
            _map.FortSpatialQueriesEnabled = true;
            GD.Print($"Fort spatial index validation passed: {_checks} checks; {_queries} differential query triples.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void ValidateContracts()
    {
        FpsHillMapSettings settings = _map.HillMapSettings!;
        float top = settings.SummitHeight + settings.RoomWallHeight;
        for (int side = 0; side < 4; side++)
        {
            Vector3 outward = Outward(side), across = new(-outward.Z, 0, outward.X);
            Vector3 deck = outward * 100.5f + across * 30f + Vector3.Up * 30f;
            foreach (bool indexed in new[] { false, true })
            {
                _map.FortSpatialQueriesEnabled = indexed;
                Close(_map.SampleHillCrowdSupportHeight(deck, deck, .55f, 1.2f), top, $"side {side} deck support / indexed={indexed}");
                Vector3 below = new(deck.X, settings.SummitHeight, deck.Z);
                Close(_map.SampleHillCrowdMaxFootHeight(below, .55f, 1.2f), top - settings.FortDeckThickness - 1.2f,
                    $"side {side} deck underside clearance / indexed={indexed}");
                Vector3 from = outward * 104f + across * 50f + Vector3.Up * settings.SummitHeight;
                Vector3 desired = outward * 98f + across * 50f + Vector3.Up * settings.SummitHeight;
                Vector3 blocked = _map.ConstrainHillCrowdBodyMovement(from, desired, .55f, 1.2f);
                Check(blocked.Dot(outward) >= 102f + .55f - .001f, $"side {side} wall blocks a body crossing / indexed={indexed}");
                Vector3 ramp = outward * 89.856f + across * 6.786f + Vector3.Up * 30f;
                Close(_map.SampleHillCrowdSupportHeight(ramp, ramp, .1f, .2f), 11.048f,
                    $"side {side} ramp has intermediate support / indexed={indexed}");
            }
            // Both endpoints lie away from the wall cells. Swept movement must
            // still agree when the segment crosses the entire fort.
            foreach (float radius in new[] { 0f, .01f, .55f, 3f, 12f })
                Compare(outward * 150f + across * 50f + Vector3.Up * 8f,
                    -outward * 150f + across * 50f + Vector3.Up * 8f, radius, 1.2f, $"fast wall sweep side {side}");
        }
        _map.FortSpatialQueriesEnabled = true;
        Vector3 outside = new(700, 30, -700);
        Close(_map.SampleHillCrowdSupportHeight(outside, outside, .55f, 1.2f), 0f, "Far terrain has no phantom fort support");
        Check(float.IsPositiveInfinity(_map.SampleHillCrowdMaxFootHeight(outside, .55f, 1.2f)), "Far terrain has no phantom ceiling");
    }

    private void ValidateDifferential(string phase, int samples)
    {
        float room = _map.HillMapSettings!.RoomHalfSize;
        for (int i = 0; i < samples; i++)
        {
            Vector3 from;
            if (i % 3 == 0)
            {
                Vector3 outward = Outward(i % 4), across = new(-outward.Z, 0, outward.X);
                from = outward * (room + Range(-25, 15)) + across * Range(-16, 16) + Vector3.Up * Range(-2, 35);
            }
            else from = new Vector3(Range(-room - 35, room + 35), Range(-2, 35), Range(-room - 35, room + 35));
            Vector3 desired = i % 7 == 0
                ? new Vector3(Range(-350, 350), Range(-2, 35), Range(-350, 350))
                : from + new Vector3(Range(-5, 5), Range(-8, 8), Range(-5, 5));
            float radius = (i % 10) switch { 0 => 0f, 1 => .001f, 2 => 8f, 3 => 24f, _ => Range(.05f, 3f) };
            float height = (i % 8) switch { 0 => .01f, 1 => 12f, _ => Range(.1f, 6f) };
            Compare(from, desired, radius, height, $"{phase} deterministic sample {i}");
        }
        GD.Print($"PASS: {phase} {_queries} cumulative differential query triples (support, ceiling, constrained movement).");
    }

    private void ValidateCellBoundaries()
    {
        // Dense coordinate sweeps cover both signs and exact integer boundaries;
        // the actual index cell size is also included by the diagnostics check.
        foreach (float axis in new[] { -112f, -104f, -102f, -100f, -96f, -80f, 0f, 80f, 96f, 100f, 102f, 104f, 112f })
        foreach (float epsilon in new[] { -.0001f, 0f, .0001f })
        foreach (float radius in new[] { .001f, .55f, 6f })
        foreach (int side in Enumerable.Range(0, 4))
        {
            Vector3 outward = Outward(side), across = new(-outward.Z, 0, outward.X);
            Vector3 from = outward * (axis + epsilon) + across * 6.786f + Vector3.Up * 14.096f;
            Compare(from, from + outward * .05f, radius, 1.2f, $"signed boundary {axis}/{epsilon}/{side}");
        }
        FpsFortSpatialIndex index = _map._fortSpatialIndex!;
        for (int cell = 0; cell <= Math.Max(index.Width, index.Height); cell++)
        foreach (float epsilon in new[] { -.0001f, 0f, .0001f })
        foreach (float radius in new[] { 0f, .55f, 8f })
        {
            Vector3 xEdge = new(index.OriginX + cell * index.CellSize + epsilon, 14.096f, -100.5f);
            Vector3 zEdge = new(100.5f, 14.096f, index.OriginZ + cell * index.CellSize + epsilon);
            Compare(xEdge, xEdge + Vector3.Right * .05f, radius, 1.2f, $"actual X cell boundary {cell}/{epsilon}");
            Compare(zEdge, zEdge + Vector3.Back * .05f, radius, 1.2f, $"actual Z cell boundary {cell}/{epsilon}");
        }
    }

    private void ValidateCandidateReduction()
    {
        FpsFortSpatialIndex index = _map._fortSpatialIndex ?? throw new InvalidOperationException("Default fort has no index");
        Vector3 nearWall = new(50, 8, -101);
        var wall = index.Query(nearWall, nearWall, .65f);
        int solidCandidates = BitOperations.PopCount(wall.Solids), rampCandidates = BitOperations.PopCount(wall.Ramps);
        Check(solidCandidates > 0 && solidCandidates <= 8 && solidCandidates < _map.FortSolidCount / 2 && rampCandidates == 0,
            $"Wall query narrows {_map.FortSolidCount} solids/{_map.FortRampCount} ramps to {solidCandidates}/{rampCandidates}");
        Vector3 nearRamp = new(6.786f, 11, -89.856f);
        var ramp = index.Query(nearRamp, nearRamp, .65f);
        Check(BitOperations.PopCount(ramp.Ramps) > 0 && BitOperations.PopCount(ramp.Ramps) <= 2,
            "Ramp query retains nearby ramps without scanning all eight approaches");
        Vector3 far = new(700, 0, -700);
        var empty = index.Query(far, far, .65f);
        Check(empty.Solids == 0 && empty.Ramps == 0, "Far query has no structure candidates");
        Vector3 from = new(150, 8, -50), desired = new(-150, 8, -50);
        var swept = index.Query(from, desired, .65f);
        Check(BitOperations.PopCount(swept.Solids) > 0, "Sweep with both endpoints outside finds intervening structure candidates");
    }

    private async Task ValidateRebuild()
    {
        FpsHillMapSettings settings = _map.HillMapSettings!;
        Vector3 oldDeck = new(30, 30, -100.5f);
        int originalVersion = _map.FortSpatialIndexVersion;
        float oldSupport = _map.SampleHillCrowdSupportHeight(oldDeck, oldDeck, .1f, .2f);
        settings.RoomHalfSize = 70f;
        settings.RoomWallHeight = 8f;
        settings.FortPlatformDepth = 10f;
        settings.FortAccessRampRun = 30f;
        settings.FortRailHeight = 0f;
        // Exercise the actual Changed signal / dirty flag / live rebuild path,
        // without turning on gameplay or advancing an uncontrolled simulation.
        MethodInfo apply = typeof(FpsVerticalSlice).GetMethod("ApplyPendingLiveTuningChanges", BindingFlags.NonPublic | BindingFlags.Instance)!;
        apply.Invoke(_map, null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_map.FortSpatialIndexVersion > originalVersion, "Live settings rebuild replaces the spatial index");
        _map.FortSpatialQueriesEnabled = true;
        float removedSupport = _map.SampleHillCrowdSupportHeight(oldDeck, oldDeck, .1f, .2f);
        Close(removedSupport, _map.SampleHillSurfaceHeight(oldDeck), "Rebuild removes support at the old wallwalk");
        Check(oldSupport > removedSupport + 1f, "Retuning changes actual support geometry");
        Vector3 newDeck = new(30, 30, -70.5f);
        Close(_map.SampleHillCrowdSupportHeight(newDeck, newDeck, .1f, .2f), 16f, "Rebuild indexes the moved and raised wallwalk");
        ValidateDifferential("retuned", 5000);
        ValidateCombinedWorldResolver("retuned", 1000);
        settings.FortificationsEnabled = false;
        apply.Invoke(_map, null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Compare(newDeck, newDeck, .55f, 1.2f, "fort disabled after rebuild");
        settings.FortificationsEnabled = true;
        settings.RoomHalfSize = 100f;
        settings.RoomWallHeight = 6.096f;
        settings.FortPlatformDepth = 6f;
        settings.FortAccessRampRun = 18.288f;
        settings.FortRailHeight = .9f;
        apply.Invoke(_map, null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ValidateContracts();
        ValidateDifferential("restored", 2000);
    }

    private void Compare(Vector3 from, Vector3 desired, float radius, float height, string name)
    {
        _map.FortSpatialQueriesEnabled = false;
        float support = _map.SampleHillCrowdSupportHeight(from, desired, radius, height);
        float ceiling = _map.SampleHillCrowdMaxFootHeight(desired, radius, height);
        Vector3 constrained = _map.ConstrainHillCrowdBodyMovement(from, desired, radius, height);
        _map.FortSpatialQueriesEnabled = true;
        Close(_map.SampleHillCrowdSupportHeight(from, desired, radius, height), support, name + " support");
        Close(_map.SampleHillCrowdMaxFootHeight(desired, radius, height), ceiling, name + " ceiling");
        Vector3 actual = _map.ConstrainHillCrowdBodyMovement(from, desired, radius, height);
        if (!actual.IsFinite() || actual.DistanceTo(constrained) > Tolerance)
            throw new InvalidOperationException($"{name} movement: indexed {actual}, original {constrained}; from={from}, desired={desired}, r={radius}, h={height}");
        _checks++; _queries++;
    }

    private void Close(float actual, float expected, string name)
    {
        if (actual != expected && (!float.IsFinite(actual) || !float.IsFinite(expected) || Mathf.Abs(actual - expected) > Tolerance))
            throw new InvalidOperationException($"{name}: indexed/actual {actual}, original/expected {expected}");
        _checks++;
    }
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++; GD.Print($"PASS: {name}");
    }
    private float Range(float min, float max) => min + (max - min) * (float)_random.NextDouble();
    private static Vector3 Outward(int side) => side switch { 0 => Vector3.Forward, 1 => Vector3.Right, 2 => Vector3.Back, _ => Vector3.Left };
}
