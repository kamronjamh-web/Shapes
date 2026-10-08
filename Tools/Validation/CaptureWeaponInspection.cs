using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class CaptureWeaponInspection : Node
{
    private FpsVerticalSlice _map = null!;
    private readonly List<string> _captured = new();
    public override async void _Ready()
    {
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/weapon-inspection-capture/"))
                throw new InvalidOperationException("Capture requires isolated user data.");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            LegacyHillFixture.Configure(_map);
            AddChild(_map);
            await Frames(8);
            string[] shapes = SelectedShapes();
            foreach (string shape in shapes)
            {
                _map.CloseWeaponInspection();
                if (!_map.TrySetArenaDebugLoadoutItem(0, shape, "divine", EquipmentCatalogDefaults.MaxWeaponLevel))
                    throw new InvalidOperationException($"Could not prepare {shape} capture loadout.");
                await Capture($"FPS-{shape}");
                if (shape == "square") await Capture("SquareFPS");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                try
                {
                    await Frames(45);
                    FpsPlayerController player = Nodes(_map).OfType<FpsPlayerController>().Single();
                    if (!player.IsAiming) throw new InvalidOperationException($"{shape}: scripted aim input did not reach the player.");
                    await Capture($"Aim-{shape}");
                    // Fixture-only reset lets every shape fire without waiting through
                    // the previous item's long cooldown or advancing the arena wave.
                    FieldInfo cooldown = typeof(FpsPlayerController).GetField("_fireCooldown", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException("Player fire cooldown fixture field is missing.");
                    cooldown.SetValue(player, 0f);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    try
                    {
                        await Frames(2);
                        Node3D equipped = Nodes(player).OfType<Node3D>().Single(node => node.Name.ToString() == "EquippedHandgun");
                        AnimationPlayer? firing = Nodes(equipped).OfType<AnimationPlayer>().SingleOrDefault(animation => animation.HasAnimation("Fire"));
                        float remainingCooldown = (float)cooldown.GetValue(player)!;
                        bool playing = firing?.IsPlaying() ?? false;
                        if (remainingCooldown <= 0f || (shape != "square" && !playing))
                        {
                            object? combatEnabled = typeof(FpsPlayerController).GetField("_combatEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(player);
                            throw new InvalidOperationException($"{shape}: actual aimed shot did not start the imported mechanism; cooldown={remainingCooldown}, playing={playing}, inputLeft={Input.IsMouseButtonPressed(MouseButton.Left)}, combatEnabled={combatEnabled}.");
                        }
                        await Capture($"Fire-{shape}");
                    }
                    finally
                    {
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                    }
                }
                finally
                {
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
                }
                await Frames(30);
                _map.OpenWeaponInspection();
                _map.PreviewInspectedWeapon(0);
                await Capture($"Inspector-{shape}");
                if (OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_ALL") == "1") await Capture($"Shape-{shape}");
                if (shape == "square")
                {
                    await Capture("InspectorSquare");
                    continue;
                }
                Node3D preview = Nodes(_map).OfType<Node3D>().First(node => node.Name.ToString() == "EquippedHandgun" &&
                    node.GetParent() != null && HasPreviewAncestor(node));
                AnimationPlayer mechanism = Nodes(preview).OfType<AnimationPlayer>().Single(player => player.HasAnimation("Fire"));
                mechanism.Play("Fire");
                mechanism.Seek(mechanism.GetAnimation("Fire").Length * .48, true);
                mechanism.Pause();
                await Capture($"Mechanism-{shape}");
            }
            string manifestPath = ProjectSettings.GlobalizePath("res://.tmp/weapon-inspection-capture/completed.json");
            System.IO.File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { shapes, captures = _captured, completed_utc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("Weapon inspection capture passed.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static string[] SelectedShapes()
    {
        string[] catalog = EquipmentCatalogDefaults.CreateDefaultShapes().Select(shape => shape.Key).ToArray();
        string filter = OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_SHAPES");
        if (string.IsNullOrWhiteSpace(filter)) return catalog;
        string[] requested = filter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key.Trim().ToLowerInvariant() == "hypersphere" ? "hyperphere" : key.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (requested.Length == 0 || requested.Any(key => !catalog.Contains(key)))
            throw new InvalidOperationException($"Invalid capture shape filter: {filter}");
        return requested;
    }
    private static IEnumerable<Node> Nodes(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node node in Nodes(child)) yield return node; }
    }
    private static bool HasPreviewAncestor(Node node)
    {
        for (Node? parent = node.GetParent(); parent != null; parent = parent.GetParent())
            if (parent.Name.ToString() == "HandgunPreview") return true;
        return false;
    }
    private async Task Capture(string name)
    {
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = ProjectSettings.GlobalizePath($"res://.tmp/weapon-inspection-capture/{name}.png");
        Error result = GetViewport().GetTexture().GetImage().SavePng(path);
        if (result != Error.Ok) throw new InvalidOperationException($"Could not save {path}: {result}");
        _captured.Add(name);
        GD.Print($"CAPTURE: {path}");
    }
    private async Task Frames(int count) { for (int index = 0; index < count; index++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
