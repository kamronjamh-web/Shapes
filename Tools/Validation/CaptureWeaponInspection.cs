using System;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class CaptureWeaponInspection : Node
{
    private FpsVerticalSlice _map = null!;
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
            _map.TrySetArenaDebugLoadoutItem(0, "square", "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
            await Capture("SquareFPS");
            _map.OpenWeaponInspection();
            await Capture("InspectorSquare");
            foreach (string shape in new[] { "circle", "triangle" })
            {
                _map.CloseWeaponInspection();
                _map.TrySetArenaDebugLoadoutItem(1, shape, "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
                _map.OpenWeaponInspection();
                _map.PreviewInspectedWeapon(1);
                await Capture(shape == "circle" ? "InspectorCircle" : "InspectorTriangle");
            }
            if (OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_ALL") == "1")
                foreach (ShapeData shape in EquipmentCatalogDefaults.CreateDefaultShapes())
                {
                    _map.CloseWeaponInspection();
                    _map.TrySetArenaDebugLoadoutItem(1, shape.Key, "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
                    _map.OpenWeaponInspection();
                    _map.PreviewInspectedWeapon(1);
                    await Capture($"Shape-{shape.Key}");
                }
            GD.Print("Weapon inspection capture passed.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Capture(string name)
    {
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = ProjectSettings.GlobalizePath($"res://.tmp/weapon-inspection-capture/{name}.png");
        Error result = GetViewport().GetTexture().GetImage().SavePng(path);
        if (result != Error.Ok) throw new InvalidOperationException($"Could not save {path}: {result}");
        GD.Print($"CAPTURE: {path}");
    }
    private async Task Frames(int count) { for (int index = 0; index < count; index++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
