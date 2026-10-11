using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateHordeRenderBuffers : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private FpsHordeManager _crowd = null!;
    private MultiMesh _cubes = null!, _pyramids = null!;
    private bool _headless;
    private int _checks, _instances;
    private readonly List<FpsHordeHandle> _handles = new();
    private readonly record struct Expected(Transform3D Pose, Color Color, Color Custom);

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/horde-render-validation/"), "Save data isolated");
            _headless = DisplayServer.GetName() == "headless";
            _crowd = new FpsHordeManager
            {
                ProcessMode = ProcessModeEnum.Disabled,
                Settings = new FpsHordeSettings { Capacity = 4096, RollingEnabled = true, GpuCrowdRenderingEnabled = true },
                SurfaceHeight = _ => 0f,
                // A known low lintel in one region exercises the old visual lift cap.
                MaxFootHeight = (feet, radius, height) => feet.X > 5f && feet.X < 15f ? .35f - height : float.PositiveInfinity,
            };
            AddChild(_crowd);
            _crowd.Transform = new Transform3D(Basis.FromEuler(new Vector3(.16f, .47f, -.11f)) * Basis.FromScale(new Vector3(1.3f, .8f, 1.7f)), new Vector3(7f, 3f, -9f));
            _cubes = _crowd.GetNode<MultiMeshInstance3D>("CrowdCubes").Multimesh;
            _pyramids = _crowd.GetNode<MultiMeshInstance3D>("CrowdPyramids").Multimesh;
            Check(_cubes.InstanceCount == 256 && _pyramids.InstanceCount == 256, "Empty roles start with 256 instances instead of reserved capacity");
            Spawn(9);
            StageMotion();
            SetAccumulator(0d);
            await ValidateOutput("blend zero", .015f);
            SetAccumulator(.5 / _crowd.Settings.SimulationHz);
            await ValidateOutput("half interpolation with roll and local transform", .025f);
            _crowd.SetCombatEnabled(false);
            await ValidateOutput("paused uses current pose and visual ceiling", .01f);
            _crowd.SetCombatEnabled(true);
            _crowd.Settings.Enabled = false;
            await ValidateOutput("disabled simulation uses current pose", .01f);
            _crowd.Settings.Enabled = true;

            Spawn(1191);
            StageMotion();
            SetAccumulator(.5 / _crowd.Settings.SimulationHz);
            await ValidateOutput("mixed roles grow buffers", .01f);
            int grownCubeCapacity = _cubes.InstanceCount, grownPyramidCapacity = _pyramids.InstanceCount;
            Check(grownCubeCapacity == 1024 && grownPyramidCapacity == 1024, "600 bodies per role grow to 1024 instances");
            for (int i = 0; i < _handles.Count; i++) if (i % 7 == 0) _crowd.ApplyDamage(_handles[i], 100000f);
            _crowd.FlushDeathEvents();
            await ValidateOutput("death holes retain ascending slot publication", .01f);
            var oldHandle = _handles[0];
            Spawn(80);
            Check(!_crowd.IsAlive(oldHandle), "Reused slot invalidates old render handle");
            StageMotion();
            await ValidateOutput("slot reuse and mixed role publication", .01f);

            var cube = FpsEnemyArchetypeSettings.CreateBruiserDefaults();
            cube.CollisionRadius = .83f; cube.CollisionHeight = 2.43f; cube.BodyColor = new Color(.17f, .43f, .79f, .61f);
            var pyramid = FpsEnemyArchetypeSettings.CreateStrikerDefaults();
            pyramid.CollisionRadius = .34f; pyramid.CollisionHeight = 1.67f; pyramid.BodyColor = new Color(.81f, .32f, .11f, .73f);
            _crowd.ApplyArchetypes(cube, pyramid);
            StageMotion();
            await ValidateOutput("live colors radii and nonuniform heights retune", .02f);
            // Save/restore is used by production investigation fixtures; don't restore a snapshot
            // across spawn/death mutations because its helper captures enemy structs only.
            object current = _crowd.SaveGpuProbeState();
            StageMotion();
            _crowd.RestoreGpuProbeState(current);
            await ValidateOutput("same-population state restoration", 0f);

            int retained = 0;
            foreach (var handle in _handles) if (_crowd.IsAlive(handle) && retained++ >= 100) _crowd.ApplyDamage(handle, 100000f);
            _crowd.FlushDeathEvents();
            await ValidateOutput("nonempty quarter-threshold shrink", 0f);
            Check(_crowd.ActiveCount == 100 && _cubes.InstanceCount == 256 && _pyramids.InstanceCount == 256,
                "Both populated role buffers shrink after occupancy drops below one quarter");

            _crowd.Clear();
            await ValidateOutput("clear removes every visible instance", 0f);
            Check(_cubes.InstanceCount == 256 && _pyramids.InstanceCount == 256, "Clear shrinks empty role buffers to minimum");
            _handles.Clear();
            Spawn(4096, FpsEnemyRole.Bruiser);
            StageMotion();
            await ValidateOutput("one role fills reserved manager capacity", 0f);
            Check(_cubes.InstanceCount == _crowd.Capacity && _pyramids.VisibleInstanceCount == 0 && _pyramids.InstanceCount == 256,
                "Full cube capacity leaves the empty pyramid role at minimum allocation");
            _crowd.Clear();
            await ValidateOutput("clear after full role capacity", 0f);
            _handles.Clear();
            Spawn(2);
            StageMotion();
            await ValidateOutput("fresh publication after clear", 0f);
            Check(_crowd.ActiveCount == 2 && _crowd.RenderedInstanceCount == 2, "Respawn has exactly two authoritative render instances");
            Check(!string.IsNullOrWhiteSpace(_crowd.GpuCrowdRenderFallbackReason), "Unsupported custom ceiling reports CPU fallback reason");
            await ValidateGpuWorldRendering();
            await ValidateRenderedGpuImages();
            // Exercise native wrapper finalizers while the rendering server is
            // still alive, including resources released by the temporary viewport.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_headless) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GD.Print($"Horde render validation passed: {_checks} checks; {_instances} native instance readbacks; mode={(_headless ? "headless structural only" : "native")}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Spawn(int count, FpsEnemyRole? forcedRole = null)
    {
        var cube = FpsEnemyArchetypeSettings.CreateBruiserDefaults();
        var pyramid = FpsEnemyArchetypeSettings.CreateStrikerDefaults();
        for (int i = 0; i < count; i++)
        {
            int index = _handles.Count;
            FpsEnemyRole role = forcedRole ?? (index % 2 == 0 ? FpsEnemyRole.Bruiser : FpsEnemyRole.Striker);
            var handle = _crowd.Spawn(role == FpsEnemyRole.Bruiser ? cube : pyramid, role, new Vector3(index % 40 * 3f, 0, index / 40 * 3f));
            if (handle == FpsHordeHandle.Invalid) throw new InvalidOperationException("Fixture exceeded its reserved capacity");
            _handles.Add(handle);
        }
    }

    private Array Enemies() => (Array)typeof(FpsHordeManager).GetField("_enemies", Private)!.GetValue(_crowd)!;
    private static T Read<T>(object body, string name) => (T)body.GetType().GetField(name)!.GetValue(body)!;
    private static void Write(object body, string name, object value) => body.GetType().GetField(name)!.SetValue(body, value);
    private void SetAccumulator(double value) => typeof(FpsHordeManager).GetField("_accumulator", Private)!.SetValue(_crowd, value);
    private void StageMotion()
    {
        Array enemies = Enemies();
        for (int i = 0; i < enemies.Length; i++)
        {
            object body = enemies.GetValue(i)!;
            if (!Read<bool>(body, "Alive")) continue;
            Vector3 position = Read<Vector3>(body, "Position");
            Write(body, "PreviousPosition", position + new Vector3(-.8f, .13f, .4f));
            Write(body, "Yaw", .13f + i * .017f);
            Write(body, "PreviousRollOrientation", new Quaternion(Vector3.Right, .23f));
            Write(body, "RollOrientation", new Quaternion(new Vector3(1, 0, 1).Normalized(), .77f));
            Write(body, "Flash", i % 3 == 0 ? .04f : 0f);
            enemies.SetValue(body, i);
        }
        _crowd.MarkRenderStateDirty();
    }

    // Independent preserved old per-instance pose math, including rolling support and ceiling.
    private Expected Reference(object body, float blend, float delta)
    {
        Vector3 feet = Read<Vector3>(body, "PreviousPosition").Lerp(Read<Vector3>(body, "Position"), blend);
        Basis orientation = new(Vector3.Up, Read<float>(body, "Yaw"));
        if (_crowd.Settings.RollingEnabled && Read<FpsEnemyRole>(body, "Role") == FpsEnemyRole.Bruiser)
            orientation = new Basis(Read<Quaternion>(body, "PreviousRollOrientation").Slerp(Read<Quaternion>(body, "RollOrientation"), blend)) * orientation;
        float radius = Read<float>(body, "Radius"), height = Read<float>(body, "Height");
        Basis scaled = orientation * Basis.FromScale(new Vector3(radius * 2f, height, radius * 2f));
        float halfHeight = (Mathf.Abs(scaled.X.Y) + Mathf.Abs(scaled.Y.Y) + Mathf.Abs(scaled.Z.Y)) * .5f;
        float visualRadius = .5f * Mathf.Max(Mathf.Abs(scaled.X.X) + Mathf.Abs(scaled.Y.X) + Mathf.Abs(scaled.Z.X), Mathf.Abs(scaled.X.Z) + Mathf.Abs(scaled.Y.Z) + Mathf.Abs(scaled.Z.Z));
        feet.Y = Mathf.Min(feet.Y, _crowd.MaxFootHeight?.Invoke(feet, visualRadius, halfHeight * 2f) ?? float.PositiveInfinity);
        Transform3D pose = _crowd.GlobalTransform.AffineInverse() * new Transform3D(scaled, feet + Vector3.Up * halfHeight);
        float flash = Mathf.Max(0f, Read<float>(body, "Flash") - delta);
        return new Expected(pose, Read<Color>(body, "Color"), new Color(flash > 0 ? 1 : 0, 0, 0, 1));
    }

    private async Task ValidateOutput(string name, float delta, string expectedBackend = "CPU")
    {
        float blend = _crowd.CombatEnabled && _crowd.Settings.Enabled
            ? Mathf.Clamp((float)(double)typeof(FpsHordeManager).GetField("_accumulator", Private)!.GetValue(_crowd)! * Mathf.Clamp(_crowd.Settings.SimulationHz, 5f, 60f), 0f, 1f) : 1f;
        List<Expected> cubes = new(), pyramids = new();
        List<(int Slot, float Flash)> flashes = new();
        Array enemies = Enemies();
        for (int slot = 0; slot < enemies.Length; slot++)
        {
            object body = enemies.GetValue(slot)!;
            if (!Read<bool>(body, "Alive")) continue;
            (Read<FpsEnemyRole>(body, "Role") == FpsEnemyRole.Striker ? pyramids : cubes).Add(Reference(body, blend, delta));
            flashes.Add((slot, Mathf.Max(0f, Read<float>(body, "Flash") - delta)));
        }
        _crowd._Process(delta);
        Check(_crowd.LastCrowdRenderBackend == expectedBackend,
            $"{name}: expected {expectedBackend} renderer; actual={_crowd.LastCrowdRenderBackend}; reason={_crowd.GpuCrowdRenderFallbackReason}");
        foreach (var flash in flashes)
            if (Mathf.Abs(Read<float>(enemies.GetValue(flash.Slot)!, "Flash") - flash.Flash) > .000001f)
                throw new InvalidOperationException($"{name}: authoritative flash decay differs at slot {flash.Slot}");
        Check(true, $"{name}: authoritative flash timers decay exactly once");
        Check(_cubes.VisibleInstanceCount == cubes.Count && _pyramids.VisibleInstanceCount == pyramids.Count
            && _crowd.RenderedInstanceCount == cubes.Count + pyramids.Count && _crowd.ActiveCount == cubes.Count + pyramids.Count,
            $"{name}: both visible role counts equal authoritative live slots");
        Check(_cubes.InstanceCount >= cubes.Count && _pyramids.InstanceCount >= pyramids.Count
            && _cubes.InstanceCount <= _crowd.Capacity && _pyramids.InstanceCount <= _crowd.Capacity,
            $"{name}: role allocations cover visible instances within reserved capacity");
        if (_headless) { GD.Print($"LIMIT: {name}: headless skips actual MultiMesh transform/color/custom readback."); return; }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (expectedBackend == "GPU")
        {
            await CompareGpuBuffers(cubes, pyramids, name);
            return;
        }
        Compare(_cubes, cubes, name + "/cube");
        Compare(_pyramids, pyramids, name + "/pyramid");
    }

    private void Compare(MultiMesh mesh, List<Expected> expected, string name)
    {
        for (int i = 0; i < expected.Count; i++)
        {
            Transform3D actual = mesh.GetInstanceTransform(i), pose = expected[i].Pose;
            float error = Mathf.Max(actual.Origin.DistanceTo(pose.Origin), Mathf.Max(actual.Basis.X.DistanceTo(pose.Basis.X), Mathf.Max(actual.Basis.Y.DistanceTo(pose.Basis.Y), actual.Basis.Z.DistanceTo(pose.Basis.Z))));
            if (!float.IsFinite(error) || error > .0001f || ColorError(mesh.GetInstanceColor(i), expected[i].Color) > .001f || ColorError(mesh.GetInstanceCustomData(i), expected[i].Custom) > .001f)
                throw new InvalidOperationException($"{name} instance {i}: pose error={error}; actual={actual}; expected={pose}; color={mesh.GetInstanceColor(i)}/{expected[i].Color}; custom={mesh.GetInstanceCustomData(i)}/{expected[i].Custom}");
            _instances++;
        }
        Check(true, $"{name}: every native transform/color/custom matches independent per-instance reference ({expected.Count})");
    }
    private static float ColorError(Color a, Color b) => Mathf.Max(Mathf.Max(Mathf.Abs(a.R - b.R), Mathf.Abs(a.G - b.G)), Mathf.Max(Mathf.Abs(a.B - b.B), Mathf.Abs(a.A - b.A)));
    private void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; GD.Print($"PASS: {message}"); }
}
