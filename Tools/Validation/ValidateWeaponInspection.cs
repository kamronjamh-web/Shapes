using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Runs against the real North Gate scene and imported GLB in an isolated user directory.
public partial class ValidateWeaponInspection : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private FpsVerticalSlice _map = null!;
    private int _checks;
    private readonly Dictionary<string, byte[]?> _fixtureOriginals = new();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/weapon-inspection-validation/"), "Save directory is isolated");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            LegacyHillFixture.Configure(_map);
            AddChild(_map);
            await Frames(4);
            foreach (string name in new[] { "inventory_state.cfg", "game_state.cfg", "currency_state.cfg", "save_manager.cfg" })
            {
                string path = ProjectSettings.GlobalizePath($"user://{name}");
                _fixtureOriginals[path] = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
                ConfigFile save = new();
                _ = save.Load($"user://{name}");
                save.SetValue("weapon_inspection_validation", "sentinel", "hub state must survive preview and run-only equip");
                Check(save.Save($"user://{name}") == Error.Ok, $"Isolated hub save fixture {name} is present");
            }
            Dictionary<string, byte[]> savesBefore = SaveSnapshot();
            await ValidateModels();
            await ValidateWorkflow();
            Dictionary<string, byte[]> savesAfter = SaveSnapshot();
            Check(savesBefore.Count == savesAfter.Count && savesBefore.All(entry => savesAfter.TryGetValue(entry.Key, out byte[]? bytes) && entry.Value.SequenceEqual(bytes)), "Inspection and equipping leave all hub save files unchanged");
            GD.Print($"Weapon inspection validation passed: {_checks} checks.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally
        {
            // Restore only the four exact fixture files, including after a failed assertion.
            foreach ((string path, byte[]? bytes) in _fixtureOriginals)
                if (bytes == null) System.IO.File.Delete(path);
                else System.IO.File.WriteAllBytes(path, bytes);
        }
    }

    private async Task ValidateModels()
    {
        var shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
        Check(shapes.Count == 16, "Factory coverage uses all sixteen catalog shapes");
        foreach (ShapeData shape in shapes)
        {
            EquipmentItem item = Item(shape.Key, "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
            Node3D model = FpsWeaponVisualFactory.Create(item);
            AddChild(model);
            await Frames(1);
            Check(model.GetMeta("shape_key").AsString() == shape.Key, $"{shape.Key}: model retains actual item identity");
            Check(Nodes(model).OfType<MeshInstance3D>().Any(mesh => mesh.Mesh != null), $"{shape.Key}: imported model contains runtime geometry");
            if (shape.Key != "square")
            {
                Node3D chamber = Find<Node3D>(model, "ShapeUpperChamber");
                Node3D insert = Find<Node3D>(model, "EquippedShapeInsert");
                Check(chamber.GetMeta("shape_key").AsString() == shape.Key && insert.GetMeta("shape_key").AsString() == shape.Key &&
                    HasRenderableGeometry(chamber) && HasRenderableGeometry(insert),
                    $"{shape.Key}: chamber and insert both contain the selected shape geometry");
            }
            for (int slot = 0; slot < 10; slot++)
                Check(Find<Node3D>(model, $"ModulatorSocket{slot + 1:00}").GetMeta("state").AsString() == "empty", $"{shape.Key}: bay {slot + 1} has no baked example part");
            model.Free();
        }

        EquipmentItem common = Item("square", "common", 1);
        EquipmentItem full = Item("triangle", "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
        full.Modulators = ModulatorCatalog.CreateAll().Take(10).ToList();
        Check(full.UnlockedModulatorSlots == 10, "Test item unlocks all ten modulator bays");
        Node3D baseline = FpsWeaponVisualFactory.Create(common);
        Node3D installed = FpsWeaponVisualFactory.Create(full);
        Node3D independent = FpsWeaponVisualFactory.Create(full);
        AddChild(baseline); AddChild(installed); AddChild(independent);
        await Frames(1);
        Check(baseline.GetMeta("rarity_color").AsColor() == common.Rarity!.UiColor && installed.GetMeta("rarity_color").AsColor() == full.Rarity!.UiColor, "Visuals use actual item rarity colors");
        for (int slot = 0; slot < 10; slot++)
        {
            Node3D locked = Find<Node3D>(baseline, $"ModulatorSocket{slot + 1:00}");
            Node3D bay = Find<Node3D>(installed, $"ModulatorSocket{slot + 1:00}");
            Check(locked.GetMeta("state").AsString() == (slot < common.UnlockedModulatorSlots ? "empty" : "locked"), $"Bay {slot + 1}: actual rarity/level controls lock state");
            Check(bay.GetMeta("state").AsString() == "installed" && bay.GetMeta("modulator_key").AsString() == full.Modulators[slot].Key, $"Bay {slot + 1}: actual installed modulator identity");
            Check(Nodes(bay).OfType<Node3D>().Any(part => part.Name.ToString().StartsWith("Cartridge", StringComparison.Ordinal) && part.Visible) &&
                Nodes(bay).OfType<MeshInstance3D>().Any(mesh => mesh.MaterialOverride is StandardMaterial3D color && color.AlbedoColor == full.Modulators[slot].UiColor),
                $"Bay {slot + 1}: installed cartridge is visible with the actual part color");
        }
        var firstMaterials = Materials(installed).ToList();
        var secondMaterials = Materials(independent).ToList();
        StandardMaterial3D accent = firstMaterials.First(material => material.AlbedoColor == full.Rarity!.UiColor);
        Check(!secondMaterials.Any(material => ReferenceEquals(material, accent)), "Independent weapon instances own independent rarity materials");
        accent.AlbedoColor = Colors.Magenta;
        Check(secondMaterials.Any(material => material.AlbedoColor == full.Rarity!.UiColor), "Changing one weapon accent preserves the other instance");
        MeshInstance3D cartridge = Nodes(Find<Node3D>(installed, "ModulatorSocket01")).OfType<MeshInstance3D>().First(mesh => mesh.MaterialOverride is StandardMaterial3D);
        MeshInstance3D otherCartridge = Nodes(Find<Node3D>(independent, "ModulatorSocket01")).OfType<MeshInstance3D>().First(mesh => mesh.MaterialOverride is StandardMaterial3D);
        Check(!ReferenceEquals(cartridge.MaterialOverride, otherCartridge.MaterialOverride), "Independent weapon instances own independent modulator materials");
        baseline.Free(); installed.Free(); independent.Free();
    }

    private async Task ValidateWorkflow()
    {
        Check(_map.TrySetArenaDebugLoadoutItem(0, "square", "common", 1) && _map.TrySetArenaDebugLoadoutItem(1, "triangle", "divine", EquipmentCatalogDefaults.MaxWeaponLevel), "Actual run arsenal contains two distinct shapes");
        // Make the health retune deterministic rather than depending on generated substat rolls.
        Field<List<EquipmentItem>>("_arsenal")[1].Substats = new() { new(SubstatType.HealthPercent, 50f) };
        FpsPlayerController player = Field<FpsPlayerController>("_player");
        player.SetDebugHealth(player.MaxHealth, player.MaxHealth * 0.37f);
        float healthFraction = player.CurrentHealth / player.MaxHealth;
        float shotDamage = player.ShotDamage;
        float maxHealthBefore = player.MaxHealth;
        Node3D equippedBefore = Find<Node3D>(player, "EquippedHandgun");
        _map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Check(Field<bool>("_pauseRequested"), "Escape opens the real pause menu");
        Button inventory = Find<Button>(_map, "InventoryButton");
        Check(inventory.IsVisibleInTree() && !inventory.Disabled, "Pause menu exposes an enabled Inventory button");
        inventory.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(_map.IsWeaponInspectionOpen && !CombatEnabled(player), "Inventory button opens inspection and disables combat");
        Find<Button>(_map, "ShapeChoice1").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(_map.InspectedWeaponIndex == 1 && _map.EquippedWeaponIndex == 0 && player.ShotDamage == shotDamage && ReferenceEquals(Find<Node3D>(player, "EquippedHandgun"), equippedBefore), "Preview changes neither equipped geometry nor active weapon stats");
        Check(Find<Node3D>(Find<Control>(_map, "HandgunPreview"), "EquippedHandgun").GetMeta("shape_key").AsString() == "triangle", "Inspection viewport displays the selected triangle while first person retains Square");
        Find<Button>(_map, "CloseWeaponInspection").EmitSignal(BaseButton.SignalName.Pressed);
        Check(!_map.IsWeaponInspectionOpen && _map.EquippedWeaponIndex == 0 && Field<bool>("_pauseRequested") && !CombatEnabled(player), "Close without equip preserves weapon and restores the previous paused state");
        _map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Check(!Field<bool>("_pauseRequested") && CombatEnabled(player), "Escape resumes combat after inspection closes");

        _map.OpenWeaponInspection();
        await Frames(1);
        Check(!CombatEnabled(player) && !(bool)Invoke("IsCombatRunning")!, "Opening inspection from combat pauses the encounter");
        _map.PreviewInspectedWeapon(1);
        Find<Button>(_map, "EquipInspectedShape").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(_map.EquippedWeaponIndex == 1 && Find<Node3D>(player, "EquippedHandgun").GetMeta("shape_key").AsString() == "triangle", "Explicit Equip shape button applies the inspected weapon to first person");
        Check(player.MaxHealth > maxHealthBefore && Mathf.Abs(player.CurrentHealth / player.MaxHealth - healthFraction) < 0.0001f, "Equipping with a different maximum health preserves the player's existing health fraction");
        if (_map.IsWeaponInspectionOpen) _map.CloseWeaponInspection();
        Check(!Field<bool>("_pauseRequested") && CombatEnabled(player), "Closing inspection restores previously running combat");
        _map.OpenWeaponInspection();
        _map.PreviewInspectedWeapon(0);
        Find<Button>(_map, "EquipInspectedShape").GrabFocus();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(1);
        Check(!_map.IsWeaponInspectionOpen && _map.EquippedWeaponIndex == 1 && CombatEnabled(player), "Escape dismisses a preview without changing the equipped weapon");

        player.GlobalPosition = _map.DefenseSettings!.ShopPosition;
        _map._UnhandledInput(new InputEventKey { Keycode = Key.F, Pressed = true });
        Check(Field<bool>("_shopOpen"), "F opens the actual field shop near its station");
        Find<Button>(_map, "InspectWeaponButton").EmitSignal(BaseButton.SignalName.Pressed);
        Check(_map.IsWeaponInspectionOpen && !Field<bool>("_shopOpen") && !(bool)Invoke("IsCombatRunning")!, "Shop inspection button pauses combat and replaces the shop overlay");
        _map.CloseWeaponInspection();
        Check(Field<bool>("_shopOpen") && !_map.IsWeaponInspectionOpen && !Field<bool>("_pauseRequested") && !CombatEnabled(player), "Closing shop inspection restores the shop and its input state");
        _map._UnhandledInput(new InputEventKey { Keycode = Key.F, Pressed = true });
        _map.OpenWeaponInspection();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Quoteleft, PhysicalKeycode = Key.Quoteleft, Pressed = true });
        await Frames(1);
        Check(!_map.IsWeaponInspectionOpen && Field<bool>("_gameplayTuningVisible"), "Grave switches inspection to the existing gameplay tuning menu");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(1);
        Check(!Field<bool>("_gameplayTuningVisible") && !_map.IsWeaponInspectionOpen, "Escape closes tuning without reopening inspection");
    }

    private static EquipmentItem Item(string shape, string rarity, int level) => new()
    { Shape = EquipmentCatalogDefaults.CreateShapeByKey(shape), Rarity = EquipmentCatalogDefaults.CreateRarityByKey(rarity), Level = level, Damage = 10, Health = 10 };
    private static IEnumerable<Node> Nodes(Node node)
    {
        foreach (Node child in node.GetChildren()) { yield return child; foreach (Node descendant in Nodes(child)) yield return descendant; }
    }
    private static T Find<T>(Node root, string name) where T : Node => Nodes(root).OfType<T>().First(node => node.Name.ToString() == name);
    private static bool HasRenderableGeometry(Node3D root)
    {
        // Hyperphere uses a canonical MultiMesh point cloud. RenderingServer AABBs may be
        // empty in headless mode; the factory computes bounds from CPU meshes/transforms.
        bool populated = Nodes(root).OfType<VisualInstance3D>().Any(visual => visual switch
        {
            MeshInstance3D mesh => mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 0,
            MultiMeshInstance3D cloud => cloud.Multimesh?.Mesh != null && cloud.Multimesh.InstanceCount > 0 && cloud.Multimesh.VisibleInstanceCount != 0,
            _ => false,
        });
        Aabb? bounds = FpsWeaponVisualFactory.GetVisualBounds(root);
        return populated && bounds.HasValue && bounds.Value.Position.IsFinite() && bounds.Value.Size.IsFinite() && bounds.Value.Size.LengthSquared() > 0f;
    }
    private static IEnumerable<StandardMaterial3D> Materials(Node3D model)
    {
        foreach (MeshInstance3D mesh in Nodes(model).OfType<MeshInstance3D>())
        {
            if (mesh.MaterialOverride is StandardMaterial3D material) yield return material;
            if (mesh.Mesh == null) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetSurfaceOverrideMaterial(surface) is StandardMaterial3D accent) yield return accent;
        }
    }
    private static bool CombatEnabled(FpsPlayerController player) => (bool)typeof(FpsPlayerController).GetField("_combatEnabled", Private)!.GetValue(player)!;
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
    private object? Invoke(string name) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, null);
    private static Dictionary<string, byte[]> SaveSnapshot() => Directory.GetFiles(OS.GetUserDataDir(), "*.cfg", SearchOption.AllDirectories).ToDictionary(path => path, System.IO.File.ReadAllBytes);
    private async Task Frames(int count) { for (int index = 0; index < count; index++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); _checks++; GD.Print($"PASS: {description}"); }
}
