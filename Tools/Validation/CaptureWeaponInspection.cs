using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
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
                bool reviewModulators = OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_MODULATORS") == "1";
                if (reviewModulators)
                {
                    string[] parts = shape switch
                    {
                        "square" => new[] { "sharpened_form", "split_chamber", "burstline_converter" },
                        "pyramid" => new[] { "extra_output", "guiding_bias" },
                        "klein" => new[] { "quickened_core", "blood_price_reactor", "recovery_circuit" },
                        "boys" => new[] { "primary_mimic", "focused_edge", "sharpened_form" },
                        _ => new[] { "focused_edge" },
                    };
                    for (int index = 0; index < parts.Length; index++)
                        if (!_map.TryAddArenaDebugModulator(0, parts[index], new[] { "broken", "enhanced", "flawless" }[index % 3]))
                            throw new InvalidOperationException($"Could not install {parts[index]} for {shape} inspection capture.");
                }
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
                await Frames(50);
                await Capture($"Inspector-{shape}");
                if (reviewModulators)
                {
                    var arsenal = _map.GetInventoryShapes();
                    Nodes(_map).OfType<FpsWeaponInspectionUI>().Single().SelectModulatorDetails(arsenal[0].Modulators[^1]);
                    ScrollContainer details = Nodes(_map).OfType<ScrollContainer>().Single(node => node.Name == "WeaponDetailsScroll");
                    details.ScrollVertical = (int)details.GetVScrollBar().MaxValue;
                    await Capture($"ModulatorDetails-{shape}");
                    details.ScrollVertical = 0;
                }
                if (OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_ALL") == "1") await Capture($"Shape-{shape}");
                if (shape == "square")
                {
                    await Capture("InspectorSquare");
                    continue;
                }
                Node3D preview = Nodes(_map).OfType<Node3D>().First(node => node.Name.ToString() == "InspectedWeaponModel" &&
                    node.GetParent() != null && HasPreviewAncestor(node));
                AnimationPlayer mechanism = Nodes(preview).OfType<AnimationPlayer>().Single(player => player.HasAnimation("Fire"));
                FpsWeaponInspectionUI inspector = Nodes(_map).OfType<FpsWeaponInspectionUI>().Single();
                inspector.SetProcess(false);
                mechanism.Play("Fire");
                mechanism.Seek(mechanism.GetAnimation("Fire").Length * .48, true);
                mechanism.Pause();
                await Capture($"Mechanism-{shape}");
                inspector.SetProcess(true);
            }
            if (OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_WORKBENCH") == "1") await CaptureWorkbench();
            if (OS.GetEnvironment("WEAPON_INSPECTION_CAPTURE_WORKSHOP") == "1") await CaptureQualityWorkshop();
            string manifestPath = ProjectSettings.GlobalizePath("res://.tmp/weapon-inspection-capture/completed.json");
            System.IO.File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { shapes, captures = _captured,
                window = new { width = DisplayServer.WindowGetSize().X, height = DisplayServer.WindowGetSize().Y }, completed_utc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("Weapon inspection capture passed.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task CaptureWorkbench()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        _map.CloseWeaponInspection();
        var arsenal = (List<EquipmentItem>)typeof(FpsVerticalSlice).GetField("_arsenal", flags)!.GetValue(_map)!;
        var storage = (List<WeaponModulatorData>)typeof(FpsVerticalSlice).GetField("_recoveredModulators", flags)!.GetValue(_map)!;
        arsenal.Clear(); storage.Clear();
        foreach (string shape in new[] { "square", "circle", "triangle", "hexagon", "pyramid", "cube", "klein", "boys" })
        {
            EquipmentItem item = new() { Shape = EquipmentCatalogDefaults.CreateDefaultShapes().Single(value => value.Key == shape),
                Rarity = EquipmentCatalogDefaults.CreateRarityByKey("divine"), Level = EquipmentCatalogDefaults.MaxWeaponLevel };
            StatGenerator.RecalculateDerivedStats(item);
            arsenal.Add(item);
        }
        foreach (var (key, quality) in new[] { ("sharpened_form", WeaponModulatorTier.Flawed), ("quickened_core", WeaponModulatorTier.Baseline),
            ("extra_output", WeaponModulatorTier.Enhanced), ("sharpened_form", WeaponModulatorTier.Flawless),
            ("heavy_payload", WeaponModulatorTier.Flawed), ("return_arc", WeaponModulatorTier.Enhanced),
            ("ballistic_tuning", WeaponModulatorTier.Baseline), ("prism_refraction", WeaponModulatorTier.Flawless),
            ("wing_man", WeaponModulatorTier.Enhanced), ("blood_price_reactor", WeaponModulatorTier.Flawed) })
            storage.Add(ModulatorCatalog.CreateByKey(key, quality));
        typeof(FpsVerticalSlice).GetMethod("EquipWeapon", flags)!.Invoke(_map, new object[] { 0, false });
        _map.OpenWeaponInspection();
        _map.PreviewInspectedWeapon(0);
        WeaponModulatorData installed = storage[3];
        if (!_map.TryInstallInventoryModulator(installed, 2, out string reason)) throw new InvalidOperationException(reason);
        await Frames(50);
        string resolution = $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}";
        Control Find(string name) => Nodes(_map).OfType<Control>().Single(control => control.Name == name);
        await Capture("WorkbenchGrid-" + resolution);
        await WorkbenchInputFixture.Drag(this, Find("ShapeChoice1"), Find("WeaponDropTarget"), true);
        await Capture("WorkbenchShapeDrag-" + resolution);
        WorkbenchInputFixture.Release(Find("WeaponDropTarget").GetGlobalRect().GetCenter());
        await Frames(8);
        await Capture("WorkbenchDisassemble-" + resolution);
        await Frames(35);
        await Capture("WorkbenchAssemble-" + resolution);
        await Frames(50);
        if (_map.EquippedWeaponIndex != 1) throw new InvalidOperationException("Actual rich-fixture shape drag did not equip Circle");
        Nodes(_map).OfType<FpsWeaponInspectionUI>().Single().ShowModulatorInventory(true);
        await Frames(5);
        await Capture("WorkbenchModulatorGrid-" + resolution);
        int before = storage.Count;
        WeaponModulatorData part = storage[0];
        await WorkbenchInputFixture.Drag(this, Find("StoredModulator0"), Find("SocketDock1"));
        if (!ReferenceEquals(_map.GetInventoryWeaponSlots(1)[1], part) || storage.Count != before - 1)
            throw new InvalidOperationException("Actual rich-fixture modulator drag did not transfer one exact reference");
        await Capture("WorkbenchSocketSeat-" + resolution);
        await Frames(30);
        Nodes(_map).OfType<FpsWeaponInspectionUI>().Single().SelectModulatorDetails(part);
        await Capture("WorkbenchSocketInstalled-" + resolution);
        await WorkbenchInputFixture.Drag(this, Find("SocketDock1"), Find("InventoryDropTarget"));
        if (_map.GetInventoryWeaponSlots(1)[1] != null || storage.Count != before)
            throw new InvalidOperationException("Actual rich-fixture removal drag did not restore the loose inventory count");
        await Capture("WorkbenchSocketRemoved-" + resolution);
    }

    private async Task CaptureQualityWorkshop()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo Field(string name) => typeof(FpsVerticalSlice).GetField(name, flags)!;
        object? Invoke(string name) => typeof(FpsVerticalSlice).GetMethod(name, flags)!.Invoke(_map, null);
        _map.CloseWeaponInspection();
        if (!_map.TrySetArenaDebugLoadoutItem(0, "square", "divine", EquipmentCatalogDefaults.MaxWeaponLevel) ||
            !_map.TryAddArenaDebugModulator(0, "sharpened_form", "broken"))
            throw new InvalidOperationException("Cannot prepare quality workshop loadout.");
        var storage = (List<WeaponModulatorData>)Field("_recoveredModulators").GetValue(_map)!;
        var stock = (List<WeaponModulatorData>)Field("_shopStock").GetValue(_map)!;
        storage.Clear(); stock.Clear();
        storage.Add(ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Flawed));
        storage.Add(ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Flawed));
        storage.Add(ModulatorCatalog.CreateByKey("quickened_core", WeaponModulatorTier.Enhanced));
        storage.Add(ModulatorCatalog.CreateByKey("extra_output", WeaponModulatorTier.Flawless));
        stock.Add(ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Enhanced));
        stock.Add(ModulatorCatalog.CreateByKey("burstline_converter", WeaponModulatorTier.Flawed));
        ((List<EquipmentItem>)Field("_shopShapes").GetValue(_map)!).Clear();
        Field("_defenseCoins").SetValue(_map, 240);
        Field("_waitingForNextWave").SetValue(_map, true);
        Field("_lastWaveModulatorReward").SetValue(_map, "Broken Sharpened Form");
        FpsPlayerController player = (FpsPlayerController)Field("_player").GetValue(_map)!;
        player.GlobalPosition = _map.DefenseSettings!.ShopPosition + Vector3.Up;
        Invoke("OpenDefenseShop");
        if (!(bool)Field("_shopOpen").GetValue(_map)!) throw new InvalidOperationException("Quality workshop did not open.");
        ScrollContainer scroll = Nodes(_map).OfType<ScrollContainer>().Single(node => node.Name == "DefenseShopScroll");
        await Capture("QualityShop");
        scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
        await Capture("QualityFusionBefore");
        Button fuse = Nodes(_map).OfType<Button>().Single(button => button.Text == "Fuse / 20 coins");
        if (fuse.Disabled) throw new InvalidOperationException("Valid fusion button is disabled.");
        fuse.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(5);
        if ((int)Field("_defenseCoins").GetValue(_map)! != 220 || storage.Count != 3 ||
            !storage.Any(part => part.NormalizedKey == "sharpened_form" && part.Tier == WeaponModulatorTier.Baseline))
            throw new InvalidOperationException("Rendered fusion did not consume exactly two Broken parts and 20 coins.");
        scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
        await Capture("QualityFusionAfter");
        Field("_victory").SetValue(_map, true);
        Invoke("CloseDefenseShop");
        await Capture("QualityVictory");
        Button workshop = Nodes(_map).OfType<Button>().Single(button => button.Name == "InventoryButton");
        workshop.EmitSignal(BaseButton.SignalName.Pressed);
        scroll.ScrollVertical = 0;
        await Capture("QualityVictoryShop");
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
