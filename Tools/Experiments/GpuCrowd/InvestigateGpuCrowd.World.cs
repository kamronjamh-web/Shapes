using SuperGatchaRebuild.Fps.Gpu;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Experiments;

public partial class InvestigateGpuCrowd
{
    private readonly record struct WorldQuery(Vector3 From, Vector3 Desired, float Radius, float Height, string Name);
    private int _worldQueryCount;
    private double _worldQueryMaxError;

    private async Task ValidateGpuWorldQueries()
    {
        FpsVerticalSlice map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
        map.ProcessMode = ProcessModeEnum.Disabled;
        map.HordeSettings = (FpsHordeSettings)map.HordeSettings!.Duplicate(true);
        map.HordeSettings.GpuCrowdContactsEnabled = false;
        AddChild(map);
        using GpuCrowdKernel kernel = new(2048);
        GpuCrowdWorld initial = map.CaptureGpuCrowdWorld() ?? throw new InvalidOperationException("Fort world snapshot missing.");
        if (!ReferenceEquals(initial, map.CaptureGpuCrowdWorld())) throw new InvalidOperationException("Unchanged world must reuse its snapshot.");
        ValidateWorldBatch(map, kernel, BuildWorldQueries(initial, "intact"));
        ValidateGateContracts(map, kernel, breached: false);
        foreach (FpsDefenseGate gate in map.DefenseGates) gate.ApplyEnemyDamage(float.MaxValue);
        if (!ReferenceEquals(initial, map.CaptureGpuCrowdWorld())) throw new InvalidOperationException("A breach must only refresh dynamic gate data.");
        ValidateGateContracts(map, kernel, breached: true);
        ValidateWorldBatch(map, kernel, BuildWorldQueries(initial, "breached", randomCount: 600));
        map.DebugResetDefenseGates();
        ValidateGateContracts(map, kernel, breached: false);

        // CPU callbacks read live terrain/gate settings even before (or without)
        // a geometry rebuild. The GPU header must follow those values as well.
        FpsHillMapSettings settings = map.HillMapSettings!;
        float oldHeight = settings.SummitHeight, oldFoot = settings.HillFootHalfSize;
        float oldWidth = settings.DoorWidth, oldThickness = settings.FortWallThickness;
        int oldVersion = map.FortSpatialIndexVersion;
        map.LiveApplyDefenseChanges = false;
        settings.SummitHeight += 1.25f;
        settings.HillFootHalfSize += 7f;
        settings.DoorWidth += 1f;
        settings.FortWallThickness += .5f;
        GpuCrowdWorld live = map.CaptureGpuCrowdWorld()!;
        if (ReferenceEquals(initial, live) || live.Version != oldVersion || live.Header.Terrain.W != settings.SummitHeight)
            throw new InvalidOperationException("Live terrain/gate settings must refresh GPU data without requiring a fort rebuild.");
        ValidateWorldBatch(map, kernel, BuildWorldQueries(live, "live-settings-without-rebuild", randomCount: 600));
        settings.SummitHeight = oldHeight; settings.HillFootHalfSize = oldFoot;
        settings.DoorWidth = oldWidth; settings.FortWallThickness = oldThickness;
        map.LiveApplyDefenseChanges = true;

        // Reuse the same kernel across actual map rebuilds, including changes in
        // buffer sizes, so stale geometry and stale uniform sets cannot pass.
        settings.RoomHalfSize = 70f;
        settings.RoomWallHeight = 8f;
        settings.FortRailHeight = 0f;
        settings.FortPlatformDepth = 10f;
        settings.FortAccessRampRun = 30f;
        MethodInfo rebuild = typeof(FpsVerticalSlice).GetMethod("ApplyPendingLiveTuningChanges", BindingFlags.NonPublic | BindingFlags.Instance)!;
        rebuild.Invoke(map, null);
        await Frames(1);
        GpuCrowdWorld rebuilt = map.CaptureGpuCrowdWorld() ?? throw new InvalidOperationException("Rebuilt world snapshot missing.");
        if (ReferenceEquals(initial, rebuilt) || initial.Version == rebuilt.Version)
            throw new InvalidOperationException("Fort rebuild must invalidate the uploaded geometry.");
        ValidateWorldBatch(map, kernel, BuildWorldQueries(rebuilt, "rebuilt"));
        ValidateGateContracts(map, kernel, breached: false);
        settings.FortificationsEnabled = false;
        rebuild.Invoke(map, null);
        await Frames(1);
        if (map.CaptureGpuCrowdWorld() != null) throw new InvalidOperationException("Legacy map must not export stale fort data.");
        kernel.SetWorld(null);
        settings.FortificationsEnabled = true;
        rebuild.Invoke(map, null);
        await Frames(1);
        ValidateWorldBatch(map, kernel, BuildWorldQueries(map.CaptureGpuCrowdWorld()!, "restored", randomCount: 600));

        // Real crowd contacts alongside the uploaded geometry, with positions at
        // all four gates, ramps, wall tops, and platform undersides.
        FpsHordeManager crowd = map.HordeManager!;
        GpuCrowdWorld world = map.CaptureGpuCrowdWorld()!;
        List<Vector3> positions = new();
        foreach (GpuCrowdWorldGate gate in world.Gates)
        {
            Vector3 outward = Xyz(gate.Outward), across = new(-outward.Z, 0, outward.X);
            for (int i = 0; i < 24; i++) positions.Add(Xyz(gate.Center) + outward * (1.6f + i / 6 * .65f) + across * ((i % 6 - 2.5f) * .6f));
        }
        foreach (GpuCrowdWorldRamp ramp in world.Ramps)
        {
            Vector3 outward = Xyz(ramp.Outward), across = Xyz(ramp.Across);
            for (int i = 0; i < 8; i++)
            {
                float along = ramp.Slope.Y - .8f + i * .14f;
                float y = Mathf.Lerp(ramp.Slope.Z, ramp.Slope.W, Mathf.Clamp((along - ramp.Slope.X) / (ramp.Slope.Y - ramp.Slope.X), 0, 1));
                positions.Add(outward * along + across * ramp.Outward.W + Vector3.Up * y);
            }
        }
        foreach (GpuCrowdWorldSolid solid in world.Solids)
            for (int i = 0; i < 3; i++) positions.Add(new Vector3(solid.Center.X + i * .3f, solid.Size.W, solid.Center.Z));
        foreach (Vector3 position in positions)
        {
            FpsHordeHandle handle = crowd.Spawn(map.BruiserArchetypeSettings!, FpsEnemyRole.Bruiser, position);
            crowd.SetGpuProbeMotion(handle, position, -new Vector3(position.X, 0, position.Z).Normalized(), .5f);
        }
        using GpuCrowdKernel contactKernel = new(crowd.Capacity);
        GpuCrowdBody[] bodies = new GpuCrowdBody[crowd.Capacity];
        CheckParity(crowd, contactKernel, crowd.SaveGpuProbeState(), bodies, "fort-ramps-decks-and-restored-gates", map);
        Line($"PASS GPU world: {_worldQueryCount} direct full-scan CPU/GPU query comparisons; maximum resolve/ground/support/ceiling error {_worldQueryMaxError:G6}. Includes actual gate breaches/restoration, geometry rebuilds, tiny/large radii, ceilings, ramps, cell boundaries, and long sweeps.");
        map.QueueFree();
        await Frames(2);
    }

    private List<WorldQuery> BuildWorldQueries(GpuCrowdWorld world, string label, int randomCount = 4000)
    {
        List<WorldQuery> queries = new();
        Random random = new(92873);
        float Range(float low, float high) => low + (high - low) * (float)random.NextDouble();
        for (int i = 0; i < randomCount; i++)
        {
            GpuCrowdWorldSolid solid = world.Solids[i % world.Solids.Length];
            Vector3 from = i % 3 == 0
                ? new Vector3(Range(-400, 400), Range(-3, 32), Range(-400, 400))
                : Xyz(solid.Center) + new Vector3(Range(-8, 8), Range(-8, 8), Range(-8, 8));
            Vector3 desired = i % 7 == 0 ? new Vector3(Range(-400, 400), Range(-3, 32), Range(-400, 400))
                : from + new Vector3(Range(-3, 3), Range(-6, 6), Range(-3, 3));
            float radius = i % 5 == 0 ? 12f : i % 5 == 1 ? .001f : Range(.05f, 2.4f);
            queries.Add(new(from, desired, radius, i % 6 == 0 ? 12f : Range(.1f, 4f), $"{label}-random-{i}"));
        }
        foreach (GpuCrowdWorldSolid solid in world.Solids)
        foreach (float radius in new[] { 0f, .001f, .55f, 4f })
        foreach (float epsilon in new[] { -.0001f, 0f, .0001f })
        {
            Vector3 top = new(solid.Center.X, solid.Size.W + epsilon, solid.Center.Z);
            Vector3 bottom = new(solid.Center.X, solid.Center.W - .01f, solid.Center.Z);
            queries.Add(new(top, top + Vector3.Down, radius, 1.2f, $"{label}-solid-top"));
            queries.Add(new(bottom, bottom + Vector3.Up * 2, radius, 12f, $"{label}-solid-low-clearance"));
            foreach (Vector3 axis in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
            {
                float half = Math.Abs(axis.X) * solid.Size.X * .5f + Math.Abs(axis.Z) * solid.Size.Z * .5f;
                Vector3 edge = Xyz(solid.Center) + axis * (half + radius + epsilon);
                queries.Add(new(edge + axis * 2, edge - axis * 2, radius, 1.2f, $"{label}-solid-side"));
            }
        }
        foreach (GpuCrowdWorldRamp ramp in world.Ramps)
        foreach (float radius in new[] { 0f, .001f, .55f, 3f })
        foreach (float epsilon in new[] { -.0001f, 0f, .0001f })
        {
            Vector3 outward = Xyz(ramp.Outward), across = Xyz(ramp.Across);
            Vector3 start = outward * ramp.Slope.X + across * ramp.Outward.W + Vector3.Up * ramp.Slope.Z;
            Vector3 end = outward * ramp.Slope.Y + across * ramp.Outward.W + Vector3.Up * ramp.Slope.W;
            queries.Add(new(start - outward * .05f, start + outward * .2f, radius, 1.2f, $"{label}-ramp-entry"));
            queries.Add(new(end - outward * .1f - Vector3.Up * .02f, end + outward * (radius + .1f + epsilon), radius, 1.2f, $"{label}-ramp-landing"));
            queries.Add(new(end + across * (ramp.Across.W * .5f + .05f + epsilon), end - outward * .5f, radius, 1.2f, $"{label}-ramp-previous-margin"));
        }
        for (int x = 0; x <= world.Header.GridWidth; x++)
        foreach (float radius in new[] { .001f, .55f, 4f })
        foreach (float epsilon in new[] { -.0001f, 0f, .0001f })
        {
            float edge = world.Header.Grid.X + x * world.Header.Grid.Z;
            for (int z = 0; z <= world.Header.GridHeight; z += 3)
            {
                float edgeZ = world.Header.Grid.Y + z * world.Header.Grid.Z;
                Vector3 desired = new(edge - radius + epsilon, world.Header.Terrain.W, edgeZ + radius + epsilon);
                queries.Add(new(desired + new Vector3(-2, 9, -2), desired, radius, 12f, $"{label}-grid-boundary"));
            }
        }
        foreach (Vector3 axis in new[] { Vector3.Right, Vector3.Forward })
        {
            Vector3 across = new(-axis.Z, 0, axis.X);
            Vector3 from = axis * 350 + across * 50 + Vector3.Up * world.Header.Terrain.W;
            queries.Add(new(from, from - axis * 700, .55f, 1.2f, $"{label}-fast-sweep"));
        }
        return queries;
    }

    private void ValidateGateContracts(FpsVerticalSlice map, GpuCrowdKernel kernel, bool breached)
    {
        GpuCrowdWorld world = map.CaptureGpuCrowdWorld()!;
        List<WorldQuery> queries = new();
        foreach (GpuCrowdWorldGate gate in world.Gates)
        {
            Vector3 center = Xyz(gate.Center), outward = Xyz(gate.Outward);
            queries.Add(new(center + outward * 4, center - outward * 4, .55f, 1.2f, "gate-crossing"));
            queries.Add(new(center + outward * .2f, center - outward * .2f, .55f, 1.2f, "restored-gate-outside"));
            queries.Add(new(center - outward * .2f, center + outward * .2f, .55f, 1.2f, "restored-gate-inside"));
        }
        GpuCrowdResult[] results = ValidateWorldBatch(map, kernel, queries);
        for (int i = 0; i < world.Gates.Length; i++)
        {
            ref GpuCrowdWorldGate gate = ref world.Gates[i];
            Vector3 outward = Xyz(gate.Outward), center = Xyz(gate.Center);
            float face = world.Header.Limits.Y * .5f + .55f;
            float crossing = (Xyz(results[i * 3].PositionPressure) - center).Dot(outward);
            if (breached ? crossing > -3.99f : crossing < face - .001f)
                throw new InvalidOperationException($"GPU gate crossing contract failed: breached={breached}, offset={crossing}.");
            if (!breached)
            {
                float outside = (Xyz(results[i * 3 + 1].PositionPressure) - center).Dot(outward);
                float inside = (Xyz(results[i * 3 + 2].PositionPressure) - center).Dot(outward);
                if (outside < face || inside > -face) throw new InvalidOperationException("Restored gate must push an overlapping body to its original side.");
            }
        }
    }

    private GpuCrowdResult[] ValidateWorldBatch(FpsVerticalSlice map, GpuCrowdKernel kernel, List<WorldQuery> queries)
    {
        GpuCrowdBody[] bodies = new GpuCrowdBody[kernel.Capacity];
        GpuCrowdResult[] actual = new GpuCrowdResult[queries.Count];
        GpuCrowdResult[] expected = new GpuCrowdResult[queries.Count];
        bool wasIndexed = map.FortSpatialQueriesEnabled;
        try
        {
            map.FortSpatialQueriesEnabled = false;
            for (int i = 0; i < queries.Count; i++)
            {
                WorldQuery q = queries[i];
                Vector3 resolved = map.HordeManager!.ResolveGpuProbeWorld(q.From, q.Desired, q.Radius, q.Height);
                expected[i].PositionPressure = new(resolved.X, resolved.Y, resolved.Z, map.SampleHillSurfaceHeight(q.Desired));
                expected[i].VelocityCounts = new(map.SampleHillCrowdSupportHeight(q.From, q.Desired, q.Radius, q.Height),
                    map.SampleHillCrowdMaxFootHeight(q.Desired, q.Radius, q.Height), 0, 0);
            }
        }
        finally { map.FortSpatialQueriesEnabled = wasIndexed; }
        kernel.SetWorld(map.CaptureGpuCrowdWorld());
        for (int start = 0; start < queries.Count; start += kernel.Capacity)
        {
            int count = Math.Min(kernel.Capacity, queries.Count - start);
            for (int i = 0; i < count; i++)
            {
                WorldQuery q = queries[start + i];
                bodies[i].PositionRadius = new(q.Desired.X, q.Desired.Y, q.Desired.Z, q.Radius);
                bodies[i].PreviousHeight = new(q.From.X, q.From.Y, q.From.Z, q.Height);
            }
            kernel.Upload(bodies);
            ReadOnlySpan<GpuCrowdResult> output = MemoryMarshal.Cast<byte, GpuCrowdResult>(kernel.Run(default, worldQueries: true).Output);
            output[..count].CopyTo(actual.AsSpan(start, count));
        }
        for (int i = 0; i < queries.Count; i++)
        {
            float error = Xyz(actual[i].PositionPressure).DistanceTo(Xyz(expected[i].PositionPressure));
            error = Math.Max(error, ScalarError(actual[i].PositionPressure.W, expected[i].PositionPressure.W));
            error = Math.Max(error, ScalarError(actual[i].VelocityCounts.X, expected[i].VelocityCounts.X));
            error = Math.Max(error, ScalarError(actual[i].VelocityCounts.Y, expected[i].VelocityCounts.Y));
            if (!float.IsFinite(error) || error > .001f)
                throw new InvalidOperationException($"GPU world mismatch {queries[i]}: error={error}; GPU position/ground={actual[i].PositionPressure} support/ceiling={actual[i].VelocityCounts}; CPU={expected[i].PositionPressure}/{expected[i].VelocityCounts}.");
            _worldQueryMaxError = Math.Max(_worldQueryMaxError, error);
            _worldQueryCount++;
        }
        return actual;
    }

    private static float ScalarError(float actual, float expected) => actual == expected ? 0f : Math.Abs(actual - expected);
    private static Vector3 Xyz(Vector4 value) => new(value.X, value.Y, value.Z);
}
