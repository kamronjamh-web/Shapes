using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateSteppedStronghold : Node
{
    private FpsVerticalSlice _map = null!;
    private int _checks;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/stepped-stronghold-validation/"), "Isolated save data");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            AddChild(_map); await Frames(4);
            _map.SetPhysicsProcess(false); _map.SetProcess(false);
            Field<FpsPlayerController>("_player").SetPhysicsProcess(false);
            _map.DebugPlayerInvulnerable = true; _map.DebugCoreInvulnerable = true;
            _map.DebugClearHorde();
            ValidateGeometry();
            await ValidateWalkability();
            // Routing suite exercises CPU and native GPU using the actual imported world.
            _checks += ValidateStrongholdRouting.Run(_map);
            await ValidateRebuild();
            if (DisplayServer.GetName() != "headless") await Capture();
            _map.QueueFree(); await Frames(3);
            GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(2);
            GD.Print($"Stepped stronghold validation passed: {_checks} checks.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void ValidateGeometry()
    {
        Check(_map.HillMapSettings!.SteppedStrongholdEnabled, "Shipped North Gate selects the Blender stronghold");
        Node3D architecture = Descendants(_map).OfType<Node3D>().Single(n => n.Name == "SteppedStrongholdArchitecture");
        Check(architecture.SceneFilePath == FpsVerticalSlice.SteppedStrongholdScenePath && Descendants(architecture).OfType<MeshInstance3D>().Any(), "Live architecture is the exported Blender scene");
        Check(_map.FortSolidCount == 39 && _map.FortRampCount == 16, "Shared crowd geometry has39 solids and16 ramps within GPU limits");
        Check(_map.CaptureGpuCrowdWorld() != null && _map.CaptureCorpseWorld() != null, "Authored physics supports both GPU crowds and corpse worker snapshots");
        Check(_map.DefenseGates.Count == 0, "All four broad entrances are open without gate barriers");
        Check(Mathf.Abs(Field<FpsDefenseCore>("_defenseCore").Position.Y - 15.6f) < .02f, "Core rests on the raised fallback platform");
        Check(Field<FpsPlayerController>("_player").Position.Y >= 14f, "Player starts above the fallback platform");
        Node3D shop = Field<Node3D>("_shopMarker");
        Check(Mathf.Abs(shop.Position.Y - 11f) < .02f && Mathf.Abs(Floor(shop.Position) - 11f) < .02f, "Shop stands on the connected middle terrace");
        for (int side = 0; side < 4; side++)
        {
            Vector3 outward = Outward(side), across = new(-outward.Z, 0, outward.X);
            foreach (float lane in new[] { -10f, 0f, 10f })
            {
                Vector3 offset = across * lane;
                Check(Ray(outward * 110f + offset + Vector3.Up * 9f, outward * 82f + offset + Vector3.Up * 9f).Count == 0, $"Side{side} lane{lane} passes the open entrance");
                foreach (var sample in new[] { (85f, 8f), (70f, 9.5f), (55f, 11f), (40f, 11f), (24f, 12.5f), (10f, 14f) })
                {
                    Vector3 p = outward * sample.Item1 + offset;
                    Check(Mathf.Abs(Floor(p) - sample.Item2) < .035f, $"Side{side} lane{lane} physical floor at{sample.Item1} rises to{sample.Item2}");
                    p.Y = 25f;
                    float indexed = _map.SampleHillCrowdSupportHeight(p, p, .1f, 1f);
                    _map.FortSpatialQueriesEnabled = false;
                    float full = _map.SampleHillCrowdSupportHeight(p, p, .1f, 1f);
                    _map.FortSpatialQueriesEnabled = true;
                    Check(Mathf.Abs(indexed - sample.Item2) < .02f && Mathf.Abs(indexed - full) < .0001f, "Indexed and full crowd support match the physical route");
                }
            }
        }
    }

    private async Task ValidateWalkability()
    {
        CharacterBody3D walker = new() { Name = "StrongholdWalkProbe", CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = .3f, FloorMaxAngle = Mathf.DegToRad(40) };
        walker.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .4f, Height = 1.8f }, Position = Vector3.Up * .9f });
        _map.AddChild(walker);
        for (int side = 0; side < 4; side++)
        {
            Vector3 outward = Outward(side), across = new(-outward.Z, 0, outward.X);
            walker.Position = outward * 84 + across * 7 + Vector3.Up * 8.1f;
            walker.Velocity = Vector3.Zero; await Frames(2);
            bool middleReached = false;
            for (int step = 0; step < 850 && walker.Position.Dot(outward) > 10f; step++)
            {
                walker.Velocity = -outward * 8f + Vector3.Up * (walker.IsOnFloor() ? -.5f : walker.Velocity.Y - 18f / 60f);
                walker.MoveAndSlide();
                if (walker.Position.Dot(outward) < 50f && walker.Position.Y > 10.9f) middleReached = true;
                await Frames(1);
            }
            Check(middleReached && walker.Position.Dot(outward) < 11f && walker.Position.Y > 13.9f && walker.IsOnFloor(), $"Side{side} player capsule climbs both ramps to fallback: {walker.Position}");
        }
        walker.QueueFree(); await Frames(2);
    }

    private async Task ValidateRebuild()
    {
        _map.DebugClearHorde();
        FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
        float health = core.CurrentHealth;
        int version = _map.FortSpatialIndexVersion;
        typeof(FpsVerticalSlice).GetMethod("BuildArena", Private)!.Invoke(_map, null);
        await Frames(3);
        Check(_map.FortSpatialIndexVersion > version && _map.FortSolidCount == 39 && _map.FortRampCount == 16, "Arena rebuild refreshes indexed geometry without duplicate proxies");
        Check(core.CurrentHealth == health && core.Position.Y > 15f, "Rebuild preserves core health and elevated anchor");
        Check(Descendants(_map).Count(n => n.SceneFilePath == FpsVerticalSlice.SteppedStrongholdScenePath && !n.IsQueuedForDeletion()) == 1 && _map.DefenseGates.Count == 0, "Rebuild retains one Blender scene and open approaches");
    }

    private async Task Capture()
    {
        Camera3D camera = new() { Fov = 68, Far = 2500 };
        _map.AddChild(camera); camera.Current = true;
        string path = ProjectSettings.GlobalizePath("res://.tmp/stepped-stronghold-validation/native");
        Directory.CreateDirectory(path);
        foreach (var view in new[] {
            ("overview", new Vector3(190, 175, 210), new Vector3(0, 8, 0)),
            ("approach", new Vector3(8, 10, -110), new Vector3(0, 13, -20)),
            ("terraces", new Vector3(-10, 15.7f, 7), new Vector3(0, 10, -105)),
            ("balcony", new Vector3(36, 20, -115), new Vector3(28, 15, -104)) })
        {
            camera.Position = view.Item2; camera.LookAt(view.Item3); await Frames(3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image capture = GetViewport().GetTexture().GetImage();
            Check(capture.SavePng(Path.Combine(path, view.Item1 + ".png")) == Error.Ok, $"Captured actual runtime {view.Item1}");
        }
        camera.QueueFree();
    }
    private float Floor(Vector3 point)
    {
        var hit = Ray(new Vector3(point.X, 14.2f, point.Z), new Vector3(point.X, -2, point.Z));
        if (hit.Count == 0) throw new InvalidOperationException($"No floor at{point}");
        return ((Vector3)hit["position"]).Y;
    }
    private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to) => _map.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1));
    private static Vector3 Outward(int side) => side switch { 1 => Vector3.Right, 2 => Vector3.Back, 3 => Vector3.Left, _ => Vector3.Forward };
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
    private static IEnumerable<Node> Descendants(Node node) { foreach (Node child in node.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; } }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; GD.Print("PASS: " + message); }
}

