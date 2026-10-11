using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateHordeRenderBuffers
{
    private async Task ValidateGpuWorldRendering()
    {
        if (_headless)
        {
            GD.Print("LIMIT: headless validates CPU fallback and buffer structure; GPU rendering requires native Vulkan.");
            return;
        }
        Check(RenderingServer.GetCurrentRenderingDriverName() == "vulkan", "GPU renderer validation uses Vulkan");
        _crowd.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
        map.ProcessMode = ProcessModeEnum.Disabled;
        map.HordeSettings = new FpsHordeSettings { Capacity = 4096, RollingEnabled = true, GpuCrowdRenderingEnabled = true };
        AddChild(map);
        _crowd = map.HordeManager!;
        _crowd.SetProcess(false);
        _crowd.Clear();
        _crowd.SetCombatEnabled(true);
        _crowd.Transform = new Transform3D(Basis.FromEuler(new Vector3(.16f, .47f, -.11f))
            * Basis.FromScale(new Vector3(1.3f, .8f, 1.7f)), new Vector3(7f, 3f, -9f));
        _cubes = _crowd.GetNode<MultiMeshInstance3D>("CrowdCubes").Multimesh;
        _pyramids = _crowd.GetNode<MultiMeshInstance3D>("CrowdPyramids").Multimesh;
        _handles.Clear();
        Spawn(9);
        StageMotion();
        SetAccumulator(0d);
        await ValidateOutput("GPU blend zero", .015f, "GPU");
        long uploads = _crowd.GpuRenderUploadCount;
        SetAccumulator(.5 / _crowd.Settings.SimulationHz);
        await ValidateOutput("GPU half interpolation / rolling / transformed manager", .025f, "GPU");
        Check(_crowd.GpuRenderUploadCount == uploads, "Interpolation-only GPU publication reuses uploaded body data");
        StageMotion();
        await ValidateOutput("GPU flash visible after dirty upload", .01f, "GPU");
        uploads = _crowd.GpuRenderUploadCount;
        await ValidateOutput("GPU flash expires without simulation", .1f, "GPU");
        Check(_crowd.GpuRenderUploadCount == uploads, "Flash-only GPU publication expires highlight without a body upload");
        Check(_crowd.ApplyDamage(_handles[0], 1f), "Damage fixture retains a live body");
        await ValidateOutput("GPU damage updates cached body flash", .01f, "GPU");
        Check(_crowd.GpuRenderUploadCount > uploads, "Damage invalidates GPU body upload cache");
        uploads = _crowd.GpuRenderUploadCount;
        Spawn(1);
        await ValidateOutput("GPU spawn updates cached body population", .01f, "GPU");
        Check(_crowd.GpuRenderUploadCount > uploads, "Spawn invalidates GPU body upload cache without fixture MarkDirty");
        uploads = _crowd.GpuRenderUploadCount;
        _crowd.RestoreGpuProbeState(_crowd.SaveGpuProbeState());
        await ValidateOutput("GPU restore invalidates cache without other mutations", 0f, "GPU");
        Check(_crowd.GpuRenderUploadCount > uploads, "State restoration invalidates GPU body upload cache");
        _crowd.SetCombatEnabled(false);
        StageMotion();
        await ValidateOutput("GPU combat pause current pose", .01f, "GPU");
        _crowd.Settings.Enabled = false;
        _crowd.SetCombatEnabled(true);
        await ValidateOutput("GPU disabled simulation current pose", .01f, "GPU");
        _crowd.Settings.Enabled = true;

        Spawn(1190);
        StageMotion();
        SetAccumulator(.5 / _crowd.Settings.SimulationHz);
        await ValidateOutput("GPU both role buffers grow", .01f, "GPU");
        Check(_cubes.InstanceCount == 1024 && _pyramids.InstanceCount == 1024, "GPU 600 instances per role allocate 1024 each");
        for (int i = 0; i < _handles.Count; i++) if (i % 7 == 0) _crowd.ApplyDamage(_handles[i], 100000f);
        _crowd.FlushDeathEvents();
        await ValidateOutput("GPU ascending slot publication after death holes", .01f, "GPU");
        var dead = _handles[0];
        Spawn(80);
        Check(!_crowd.IsAlive(dead), "GPU reused slot preserves handle generation");
        StageMotion();
        await ValidateOutput("GPU reuse preserves per-role ordering", .01f, "GPU");

        var cube = FpsEnemyArchetypeSettings.CreateBruiserDefaults();
        cube.CollisionRadius = .83f; cube.CollisionHeight = 2.43f; cube.BodyColor = new Color(.17f, .43f, .79f, .61f);
        var pyramid = FpsEnemyArchetypeSettings.CreateStrikerDefaults();
        pyramid.CollisionRadius = .34f; pyramid.CollisionHeight = 1.67f; pyramid.BodyColor = new Color(.81f, .32f, .11f, .73f);
        _crowd.ApplyArchetypes(cube, pyramid);
        StageMotion();
        await ValidateOutput("GPU archetype dimensions and alpha retune", .02f, "GPU");
        _crowd.Settings.RollingEnabled = false;
        await ValidateOutput("GPU rolling toggle off", .01f, "GPU");
        _crowd.Settings.RollingEnabled = true;
        object snapshot = _crowd.SaveGpuProbeState();
        StageMotion();
        _crowd.RestoreGpuProbeState(snapshot);
        _crowd.MarkRenderStateDirty();
        await ValidateOutput("GPU same-population snapshot restoration", 0f, "GPU");

        // Real deck undersides constrain the rotated visual bounds, independently
        // of the body's collision dimensions or manager's nonuniform transform.
        int moved = 0;
        foreach (var handle in _handles)
            if (_crowd.IsAlive(handle) && moved++ < 4)
                _crowd.TrySetPosition(handle, new Vector3(30f + moved, map.HillMapSettings!.SummitHeight, -100.5f));
        StageMotion();
        Check(float.IsFinite(_crowd.MaxFootHeight!(new Vector3(31f, map.HillMapSettings!.SummitHeight, -100.5f), 1f, 2f)),
            "GPU real fort fixture includes a finite deck ceiling");
        await ValidateOutput("GPU fort ceiling clips rolling visual lift", .01f, "GPU");

        map.HillMapSettings!.SummitHeight += 2f;
        map.HillMapSettings.DoorWidth += .5f;
        await ValidateOutput("GPU live world headers before geometry rebuild", .01f, "GPU");
        map.HillMapSettings.RoomWallHeight += 2f;
        typeof(FpsVerticalSlice).GetMethod("ApplyPendingLiveTuningChanges", Private)!.Invoke(map, null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ValidateOutput("GPU world geometry rebuild refreshes ceiling buffers", .01f, "GPU");

        _crowd.Settings.GpuCrowdRenderingEnabled = false;
        await ValidateOutput("GPU option off selects CPU while paused", .01f);
        _crowd.Settings.GpuCrowdRenderingEnabled = true;
        await ValidateOutput("GPU option on recovers without a simulation tick", .01f, "GPU");
        // The CPU comparison above materializes Godot's instance data cache.
        // Growing visibility inside the same allocation must not upload that
        // stale cache over the compute-written MultiMesh buffer.
        int cachedCubeCapacity = _cubes.InstanceCount, cachedPyramidCapacity = _pyramids.InstanceCount;
        Spawn(2);
        StageMotion();
        await ValidateOutput("GPU visibility growth after native CPU instance cache", .01f, "GPU");
        Check(_cubes.InstanceCount == cachedCubeCapacity && _pyramids.InstanceCount == cachedPyramidCapacity,
            "CPU-to-GPU cache regression grows visible roles without reallocating");
        Viewport viewport = GetViewport();
        bool savedTaa = viewport.UseTaa;
        var savedDebugDraw = viewport.DebugDraw;
        try
        {
            viewport.UseTaa = true;
            await ValidateOutput("GPU temporal antialiasing requires CPU renderer", .01f);
            Check(_crowd.GpuCrowdRenderFallbackReason.Contains("motion vectors", StringComparison.OrdinalIgnoreCase),
                "TAA fallback explains unsupported motion-vector layout");
            viewport.UseTaa = false;
            await ValidateOutput("GPU recovery after disabling TAA", .01f, "GPU");
            viewport.DebugDraw = Viewport.DebugDrawEnum.MotionVectors;
            await ValidateOutput("GPU motion-vector debug view requires CPU renderer", .01f);
            Check(_crowd.GpuCrowdRenderFallbackReason.Contains("motion vectors", StringComparison.OrdinalIgnoreCase),
                "Motion-vector debug fallback reports its reason");
            viewport.DebugDraw = savedDebugDraw;
            await ValidateOutput("GPU recovery after motion-vector debug view", .01f, "GPU");
        }
        finally
        {
            viewport.UseTaa = savedTaa;
            viewport.DebugDraw = savedDebugDraw;
        }
        map.HillMapSettings.FortificationsEnabled = false;
        await ValidateOutput("GPU unsupported legacy world selects CPU", .01f);
        Check(!string.IsNullOrWhiteSpace(_crowd.GpuCrowdRenderFallbackReason), "Unsupported world explains rendering fallback");
        map.HillMapSettings.FortificationsEnabled = true;
        await ValidateOutput("GPU supported fort restores renderer", .01f, "GPU");

        int retained = 0;
        foreach (var handle in _handles) if (_crowd.IsAlive(handle) && retained++ >= 100) _crowd.ApplyDamage(handle, 100000f);
        _crowd.FlushDeathEvents();
        await ValidateOutput("GPU populated buffers shrink", 0f, "GPU");
        Check(_cubes.InstanceCount == 256 && _pyramids.InstanceCount == 256, "GPU shrink returns both small roles to 256");
        _crowd.Clear();
        await ValidateOutput("GPU clear publishes zero roles", 0f, "GPU");
        _handles.Clear();
        Spawn(4096, FpsEnemyRole.Bruiser);
        StageMotion();
        await ValidateOutput("GPU full capacity cube / empty pyramid", 0f, "GPU");
        Check(_cubes.InstanceCount == 4096 && _pyramids.VisibleInstanceCount == 0 && _pyramids.InstanceCount == 256,
            "GPU one-role capacity leaves zero role at minimum allocation");
        _crowd.Clear();
        await ValidateOutput("GPU clear after maximum role buffer", 0f, "GPU");
        _handles.Clear();
        Spawn(2);
        StageMotion();
        await ValidateOutput("GPU clear and respawn publication", 0f, "GPU");
        map.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CompareGpuBuffers(List<Expected> cubes, List<Expected> pyramids, string name)
    {
        // The debug hook queues actual main-device MultiMesh buffer readback after
        // publication; no normal frame ever takes this synchronization path.
        var buffers = await _crowd.DebugReadbackGpuCrowdRenderBuffersAsync();
        CompareGpuBuffer(buffers.Cubes, _cubes.InstanceCount, cubes, name + "/cube");
        CompareGpuBuffer(buffers.Pyramids, _pyramids.InstanceCount, pyramids, name + "/pyramid");
    }

    private void CompareGpuBuffer(byte[] buffer, int capacity, List<Expected> expected, string name)
    {
        Check(buffer.Length == capacity * 80, $"{name}: actual GPU buffer matches 20-float instance allocation");
        float At(int offset) => BitConverter.ToSingle(buffer, offset * sizeof(float));
        for (int i = 0; i < expected.Count; i++)
        {
            int k = i * 20;
            for (int j = 0; j < 20; j++)
                if (!float.IsFinite(At(k + j))) throw new InvalidOperationException($"{name}: nonfinite GPU instance {i} component {j}");
            Transform3D actual = new(new Basis(new Vector3(At(k), At(k + 4), At(k + 8)),
                new Vector3(At(k + 1), At(k + 5), At(k + 9)), new Vector3(At(k + 2), At(k + 6), At(k + 10))),
                new Vector3(At(k + 3), At(k + 7), At(k + 11)));
            Transform3D pose = expected[i].Pose;
            float error = Mathf.Max(actual.Origin.DistanceTo(pose.Origin), Mathf.Max(actual.Basis.X.DistanceTo(pose.Basis.X),
                Mathf.Max(actual.Basis.Y.DistanceTo(pose.Basis.Y), actual.Basis.Z.DistanceTo(pose.Basis.Z))));
            Color color = new(At(k + 12), At(k + 13), At(k + 14), At(k + 15));
            Color custom = new(At(k + 16), At(k + 17), At(k + 18), At(k + 19));
            if (error > .0001f || ColorError(color, expected[i].Color) > .001f || ColorError(custom, expected[i].Custom) > .001f)
                throw new InvalidOperationException($"{name} GPU instance {i}: pose error={error}; actual={actual}; expected={pose}; color={color}/{expected[i].Color}; custom={custom}/{expected[i].Custom}");
            _instances++;
        }
        Check(true, $"{name}: actual GPU transform/color/custom matches independent old pose math ({expected.Count})");
    }
}
