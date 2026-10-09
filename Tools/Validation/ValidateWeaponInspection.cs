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
    private static readonly Dictionary<string, (string Asset, string Mechanism)> Authored = new()
    {
        ["square"] = ("SquareHandgun", "Slide___"),
        ["circle"] = ("CircleSawblade", "Circle_Blade_Spin_Pivot"),
        ["triangle"] = ("TriangleBeamCarbine", "TRI_AcceleratorRail_0"),
        ["hexagon"] = ("HexagonMineLauncher", "Hexagon_Drum_Index_Pivot"),
        ["sphere"] = ("SpherePulseCannon", "Sphere_Petal_Pivot_"),
        ["cube"] = ("CubeGuardProjector", "CUBE_SidePanelHinge_"),
        ["pyramid"] = ("PyramidSeekerLauncher", "Pyramid_Internal_Seeker_Magazine"),
        ["cone"] = ("ConeFlameProjector", "Cone_Split_Shield_Pivot_"),
        ["tesseract"] = ("TesseractRiftEngine", "Tesseract_Rift_Pulse"),
        ["hyperphere"] = ("HyperphereMortar", "Hyperphere_Shoulder_Elevation_Pivot"),
        ["pentachoron"] = ("PentachoronArcLattice", "Pentachoron_ArticulatedCapacitor_"),
        ["hypercone"] = ("HyperconeDrill", "Hypercone_Counterrotating_Telescopic_Sleeves"),
        ["impossiblecube"] = ("ImpossibleCubeSingularity", "ImpossibleCube_Suspended_Fragment_"),
        ["mobius"] = ("MobiusEchoWeaver", "Mobius_TravellingTemporalEmitter_"),
        ["klein"] = ("KleinAuraVessel", "Klein_Levitating_Vessel_Root"),
        ["boys"] = ("BoysSurfaceClonePrism", "Boys_ArticulatedInterwovenMirror_"),
    };

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
            await WeaponWorkbenchChecks.Run(this, _map, Check);
            await ValidateModulatorDescriptions();
            await ValidatePresentation();
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
        Check(shapes.All(shape => Authored.ContainsKey(shape.Key)), "All sixteen catalog shapes have authored import expectations");
        foreach (ShapeData shape in shapes)
        {
            EquipmentItem item = Item(shape.Key, "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
            Node3D model = FpsWeaponVisualFactory.Create(item);
            AddChild(model);
            await Frames(1);
            Check(model.GetMeta("shape_key").AsString() == shape.Key, $"{shape.Key}: model retains actual item identity");
            Check(Nodes(model).OfType<MeshInstance3D>().Any(mesh => mesh.Mesh != null), $"{shape.Key}: imported model contains runtime geometry");
            if (Authored.ContainsKey(shape.Key))
            {
                var spec = Authored[shape.Key];
                Check(model.GetMeta("authored").AsBool() && model.GetMeta("visual_source").AsString() == $"res://Resources/Weapons/{spec.Asset}.glb", $"{shape.Key}: uses its approved imported asset");
                Check(Nodes(model).OfType<Node3D>().Any(node => node.Name.ToString().StartsWith(spec.Mechanism, StringComparison.Ordinal)), $"{shape.Key}: distinct authored mechanism survives import");
                Node3D socket = Find<Node3D>(model, "ShapeSocket");
                Check(HasRenderableGeometry(socket) && Nodes(socket).Any(node => node.Name.ToString().Contains("Insert", StringComparison.OrdinalIgnoreCase)), $"{shape.Key}: matching imported shape insert remains visible");
                Check(Find<Node3D>(model, "MuzzleAnchor").Position.IsFinite(), $"{shape.Key}: authored effect anchor survives import");
                Check(Nodes(model).OfType<Node3D>().Count(node => node.Name.ToString().StartsWith("ModulatorSocket", StringComparison.Ordinal)) == 10,
                    $"{shape.Key}: ten uniquely named sockets survive nested authored hierarchies");
                await ValidateAuthored(shape.Key, model);
            }
            else if (shape.Key != "square")
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

    private async Task ValidateAuthored(string shape, Node3D model)
    {
        foreach (RarityData rarity in EquipmentCatalogDefaults.CreateDefaultRarities())
        {
            EquipmentItem item = Item(shape, rarity.Key, EquipmentCatalogDefaults.MaxWeaponLevel);
            Node3D colored = FpsWeaponVisualFactory.Create(item);
            AddChild(colored);
            await Frames(1);
            var accents = RaritySurfaces(colored).ToList();
            Check(accents.Count > 0 && accents.All(material => material is StandardMaterial3D standard && standard.AlbedoColor == rarity.UiColor), $"{shape}: {rarity.Key} colors every imported accent surface");
            Check(Nodes(colored).OfType<MeshInstance3D>().All(mesh => mesh.MaterialOverride is not ShaderMaterial &&
                Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0).All(surface => mesh.GetActiveMaterial(surface) is not ShaderMaterial)),
                $"{shape}: imported surfaces use supported standard materials");
            colored.Free();
        }
        EquipmentItem common = Item(shape, "common", 1);
        EquipmentItem installedItem = Item(shape, "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
        installedItem.Modulators = ModulatorCatalog.CreateAll().Take(3).ToList();
        Node3D lockedModel = FpsWeaponVisualFactory.Create(common);
        Node3D installed = FpsWeaponVisualFactory.Create(installedItem);
        Node3D independent = FpsWeaponVisualFactory.Create(installedItem);
        AddChild(lockedModel); AddChild(installed); AddChild(independent);
        await Frames(1);
        for (int slot = 0; slot < 10; slot++)
        {
            Node3D locked = Find<Node3D>(lockedModel, $"ModulatorSocket{slot + 1:00}");
            Node3D bay = Find<Node3D>(installed, $"ModulatorSocket{slot + 1:00}");
            string state = slot < 3 ? "installed" : "empty";
            Check(locked.GetMeta("state").AsString() == (slot < common.UnlockedModulatorSlots ? "empty" : "locked") &&
                Nodes(locked).OfType<Node3D>().Where(node => node.Name.ToString().StartsWith("Locked", StringComparison.Ordinal)).Any(node => node.Visible) == (slot >= common.UnlockedModulatorSlots),
                $"{shape}: bay {slot + 1} lock geometry follows rarity and level");
            Check(bay.GetMeta("state").AsString() == state &&
                Nodes(bay).OfType<Node3D>().Where(node => node.Name.ToString().StartsWith("Cartridge", StringComparison.Ordinal)).Any(node => node.Visible) == (slot < 3) &&
                Nodes(bay).OfType<Node3D>().Where(node => node.Name.ToString().StartsWith("EmptyContact", StringComparison.Ordinal)).Any(node => node.Visible) == (slot >= 3),
                $"{shape}: bay {slot + 1} installed/empty geometry follows item data");
            if (slot < 3)
                Check(bay.GetMeta("modulator_key").AsString() == installedItem.Modulators[slot].Key &&
                    Materials(bay).Any(mat => mat.AlbedoColor == installedItem.Modulators[slot].UiColor), $"{shape}: bay {slot + 1} preserves modulator identity and color");
        }
        var otherMaterials = Materials(independent).ToList();
        StandardMaterial3D accent = RaritySurfaces(installed).OfType<StandardMaterial3D>().First();
        Check(!otherMaterials.Any(mat => mat.GetInstanceId() == accent.GetInstanceId()), $"{shape}: rarity material belongs to the instance");
        accent.AlbedoColor = Colors.Magenta;
        Check(RaritySurfaces(independent).OfType<StandardMaterial3D>().All(mat => mat.AlbedoColor == installedItem.Rarity!.UiColor), $"{shape}: recoloring cannot bleed into another instance");
        Material ownCartridge = Materials(Find<Node3D>(installed, "ModulatorSocket01")).First();
        Check(!Materials(Find<Node3D>(independent, "ModulatorSocket01")).Any(mat => mat.GetInstanceId() == ownCartridge.GetInstanceId()), $"{shape}: installed modulator material belongs to the instance");
        ValidateWorkbenchAnimations(shape, model, independent);
        if (shape == "square")
        {
            lockedModel.Free(); installed.Free(); independent.Free();
            return;
        }
        var rest = Nodes(model).OfType<Node3D>().ToDictionary(node => node, node => node.Transform);
        var independentRest = Nodes(independent).OfType<Node3D>().ToDictionary(node => node, node => node.Transform);
        AnimationPlayer animation = Nodes(model).OfType<AnimationPlayer>().Single(player => player.HasAnimation("Fire"));
        FpsWeaponVisualFactory.PlayFire(model, .4f);
        animation.Advance(0);
        Check(animation.IsPlaying(), $"{shape}: firing starts the imported mechanism");
        double length = animation.GetAnimation("Fire").Length;
        bool changed = false;
        foreach (double fraction in new[] { .2, .4, .6, .8 })
        {
            animation.Seek(length * fraction, true);
            changed |= rest.Any(entry => !entry.Key.Transform.IsEqualApprox(entry.Value));
        }
        Check(changed, $"{shape}: Fire moves authored transforms");
        Check(independentRest.All(entry => entry.Key.Transform.IsEqualApprox(entry.Value)), $"{shape}: firing cannot animate another instance");
        animation.Seek(0, true);
        animation.Advance(length + .01);
        await Frames(2);
        Check(rest.All(entry => entry.Key.Transform.IsEqualApprox(entry.Value)), $"{shape}: completed firing restores its rest transforms");
        lockedModel.Free(); installed.Free(); independent.Free();
    }

    private void ValidateWorkbenchAnimations(string shape, Node3D model, Node3D independent)
    {
        AnimationPlayer animation = Nodes(model).OfType<AnimationPlayer>().Single(player => player.HasAnimation("Assemble"));
        Check(animation.HasAnimation("Disassemble") && (shape == "square" || animation.HasAnimation("Fire")), $"{shape}: imported workbench clips preserve the existing firing mechanism");
        var rest = Nodes(model).OfType<Node3D>().ToDictionary(node => node, node => node.Transform);
        var independentRest = Nodes(independent).OfType<Node3D>().ToDictionary(node => node, node => node.Transform);
        FpsWeaponWorkbenchAnimations playback = new(model);
        Check(playback.HasClips && playback.PlayDisassemble(), $"{shape}: actual workbench playback starts Disassemble");
        playback.Advance(playback.Duration * .5);
        Check(rest.Any(pair => !pair.Key.Transform.IsEqualApprox(pair.Value)), $"{shape}: Disassemble moves actual imported weapon components");
        playback.Advance(playback.Duration);
        Check(playback.PlayAssemble(), $"{shape}: actual workbench playback starts Assemble");
        playback.Advance(playback.Duration * .5);
        Check(rest.Any(pair => !pair.Key.Transform.IsEqualApprox(pair.Value)), $"{shape}: Assemble presents actual intermediate component transforms");
        playback.Advance(playback.Duration);
        Check(rest.All(pair => pair.Key.Transform.IsEqualApprox(pair.Value)) && independentRest.All(pair => pair.Key.Transform.IsEqualApprox(pair.Value)),
            $"{shape}: assembly restores exact authored rest without animating another instance");
        Check(Enumerable.Range(1, 10).All(slot => animation.HasAnimation($"SocketSeat{slot:00}") && animation.HasAnimation($"SocketRemove{slot:00}")),
            $"{shape}: all ten physical sockets have imported seat and removal clips");
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
        Check(Find<Node3D>(Find<Control>(_map, "HandgunPreview"), "InspectedWeaponModel").GetMeta("shape_key").AsString() == "triangle", "Inspection viewport displays the selected triangle while first person retains Square");
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

    private async Task ValidateModulatorDescriptions()
    {
        Check(_map.TrySetArenaDebugLoadoutItem(0, "square", "divine", EquipmentCatalogDefaults.MaxWeaponLevel) &&
            _map.TrySetArenaDebugLoadoutItem(1, "klein", "divine", EquipmentCatalogDefaults.MaxWeaponLevel), "Modulator inspection fixture prepares real arsenal slots");
        foreach (string key in new[] { "quickened_core", "blood_price_reactor", "recovery_circuit", "wing_man" })
            Check(_map.TryAddArenaDebugModulator(1, key, "baseline"), $"Inspection installs {key} on support Klein");
        _map.OpenWeaponInspection();
        _map.PreviewInspectedWeapon(0);
        _map.EquipInspectedWeapon();
        _map.PreviewInspectedWeapon(1);
        await Frames(1);
        Label details = Find<Label>(_map, "InstalledModulatorDetails");
        string SupportDetails(string key)
        {
            Field<FpsWeaponInspectionUI>("_weaponInspectionUi").SelectModulatorDetails(Field<List<EquipmentItem>>("_arsenal")[1].Modulators.Single(part => part.Key == key));
            return details.Text;
        }
        Check(SupportDetails("quickened_core").Contains("Passive aura tick speed stays the same") && SupportDetails("blood_price_reactor").Contains("cannot charge or vent") &&
            SupportDetails("recovery_circuit").Contains("inactive until") && SupportDetails("wing_man").Contains("Escort fire is active"),
            "Support preview distinguishes manual cadence, inactive reactor/player stats and active Wing");
        _map.EquipInspectedWeapon();
        Check(SupportDetails("blood_price_reactor").Contains("while Klein is equipped") && SupportDetails("recovery_circuit").Contains("Player stat bonuses apply while") &&
            SupportDetails("wing_man").Contains("Escort fire pauses") && !SupportDetails("blood_price_reactor").Contains("cannot charge or vent"),
            "Equipping updates conditional explanations without changing the preview selection");
        _map.CloseWeaponInspection();

        Check(_map.TryAddArenaDebugModulator(0, "sharpened_form", "broken"), "Inspector fixture installs a Broken quality part through the real debug gate");
        _map.OpenWeaponInspection();
        _map.PreviewInspectedWeapon(0);
        Field<FpsWeaponInspectionUI>("_weaponInspectionUi").SelectModulatorDetails(Field<List<EquipmentItem>>("_arsenal")[0].Modulators.Single(part => part.Key == "sharpened_form"));
        Check(Find<Label>(_map, "ModulatorDetailsMeta").Text.Contains("Tier 1 · Broken · 50% power") && details.Text.Contains("Damage: +6%") &&
            !details.Text.Contains("Damage: +12%"), "Actual installed-details UI displays resolved Broken power rather than its raw Baseline bonus");
        _map.CloseWeaponInspection();

        WeaponModulatorData Part(string key) => ModulatorCatalog.CreateAll().Single(part => part.Key == key);
        EquipmentItem clone = Item("boys", "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
        clone.Modulators = new() { Part("primary_mimic"), Part("focused_edge"), Part("apex_doctrine") };
        EquipmentItem primary = Item("mobius", "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
        string cloneText = FpsModulatorPresentation.DescribeInstalled(clone, new[] { primary, clone }, 1, 0);
        Check(cloneText.Contains("do not apply to copied attacks") && cloneText.Contains("Square shots instead") && cloneText.Contains("Retired"),
            "Mimic preview exposes crit suppression, unsupported primary fallback and retired Apex");
        clone.Modulators.RemoveAt(0);
        string restored = FpsModulatorPresentation.DescribeInstalled(clone, new[] { primary, clone }, 1, 0);
        Check(!restored.Contains("do not apply to copied attacks") && restored.Contains("Retired") && !restored.Contains("Active: this is the weapon's only Tier 3"),
            "Removing Mimic restores ordinary clone crit while archived Apex remains inactive");
        EquipmentItem triangle = Item("triangle", "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
        Check(FpsModulatorPresentation.DescribePart(Part("ballistic_tuning"), triangle, true, 0, triangle).Contains("Incompatible:"),
            "Inspector uses the same capability rejection as installation");
        Check(FpsModulatorPresentation.DescribePart(Part("focused_edge"), triangle, true, 0, triangle).Contains("initial hit"),
            "Beam crit explanation distinguishes initial hits from sustained ticks");
        EquipmentItem pyramid = Item("pyramid", "divine", EquipmentCatalogDefaults.MaxWeaponLevel);
        Check(FpsModulatorPresentation.DescribePart(Part("extra_output"), pyramid, true, 0, pyramid).Contains("adds seekers"),
            "Extra Output explains its actual seeker behavior");
        Check(FpsModulatorPresentation.DescribeAttack(primary).Contains("instant shot") &&
            !FpsModulatorPresentation.DescribeAttack(primary).Contains("Passive:"), "Mobius description reflects FPS firing rather than the legacy passive");
    }

    private async Task ValidatePresentation()
    {
        FpsPlayerController player = Field<FpsPlayerController>("_player");
        Camera3D camera = (Camera3D)typeof(FpsPlayerController).GetField("_camera", Private)!.GetValue(player)!;
        Node3D frame = (Node3D)typeof(FpsPlayerController).GetField("_weaponVisualFrame", Private)!.GetValue(player)!;
        async Task Equip(string shape)
        {
            Check(_map.TrySetArenaDebugLoadoutItem(0, shape, "divine", EquipmentCatalogDefaults.MaxWeaponLevel), $"{shape}: presentation fixture uses an actual arsenal item");
            _map.OpenWeaponInspection();
            _map.PreviewInspectedWeapon(0);
            Find<Button>(_map, "EquipInspectedShape").EmitSignal(BaseButton.SignalName.Pressed);
            if (_map.IsWeaponInspectionOpen) _map.CloseWeaponInspection();
            await Frames(4);
            Check(Find<Node3D>(player, "EquippedHandgun").GetMeta("shape_key").AsString() == shape, $"{shape}: actual controller equips authored presentation");
        }
        void CheckClearance(string shape, string pose)
        {
            Node3D visual = Find<Node3D>(player, "EquippedHandgun");
            var points = Nodes(visual).OfType<MeshInstance3D>().Where(mesh => mesh.Mesh != null && mesh.IsVisibleInTree())
                .SelectMany(mesh => Enumerable.Range(0, 8).Select(index =>
                    camera.GlobalTransform.AffineInverse() * (mesh.GlobalTransform * mesh.Mesh!.GetAabb().GetEndpoint(index)))).ToList();
            Check(points.Count > 0 && points.All(point => point.IsFinite() && point.Z < -camera.Near), $"{shape}: {pose} geometry stays ahead of the actual camera near plane");
        }
        await Equip("square");
        Transform3D originalHandgunPose = frame.Transform;
        foreach (string shape in new[] { "tesseract", "hyperphere", "pentachoron", "hypercone", "impossiblecube", "mobius", "klein", "boys" })
        {
            await Equip(shape);
            await Frames(6);
            CheckClearance(shape, "hip");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            try
            {
                await Frames(45);
                Check(player.IsAiming, $"{shape}: aim presentation responds to actual controller input");
                CheckClearance(shape, "aim after processing");
            }
            finally { Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); }
            await Frames(45);
            CheckClearance(shape, "hip after aim release");
        }
        await Equip("square");
        await Frames(6);
        Check(frame.Transform.IsEqualApprox(originalHandgunPose), "Returning to Square restores the original handgun presentation after processing");
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
    private static IEnumerable<Material?> RaritySurfaces(Node3D model)
    {
        foreach (MeshInstance3D mesh in Nodes(model).OfType<MeshInstance3D>())
        {
            if (mesh.Mesh == null) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName == "RarityAccent")
                    yield return mesh.GetSurfaceOverrideMaterial(surface);
        }
    }
    private static bool CombatEnabled(FpsPlayerController player) => (bool)typeof(FpsPlayerController).GetField("_combatEnabled", Private)!.GetValue(player)!;
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
    private object? Invoke(string name) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, null);
    private static Dictionary<string, byte[]> SaveSnapshot() => Directory.GetFiles(OS.GetUserDataDir(), "*.cfg", SearchOption.AllDirectories).ToDictionary(path => path, System.IO.File.ReadAllBytes);
    private async Task Frames(int count) { for (int index = 0; index < count; index++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); _checks++; GD.Print($"PASS: {description}"); }
}
