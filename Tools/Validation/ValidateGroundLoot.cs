using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

/// <summary>Real-map regressions for explicit ground equipment and bounded run storage.</summary>
public partial class ValidateGroundLoot : Node
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private FpsVerticalSlice _map = null!;
    private int _checks;
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Fields)!.GetValue(_map)!;
    private void Set(string name, object value) => typeof(FpsVerticalSlice).GetField(name, Fields)!.SetValue(_map, value);
    private object? Call(string name, params object?[] args) => typeof(FpsVerticalSlice).GetMethods(Fields)
        .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(_map, args);
    private static T PickupField<T>(object pickup, string name) => (T)pickup.GetType().GetField(name, Fields)!.GetValue(pickup)!;
    private IList Pickups => Field<IList>("_defensePickups");
    private List<EquipmentItem> Active => Field<List<EquipmentItem>>("_arsenal");
    private List<EquipmentItem> Reserve => Field<List<EquipmentItem>>("_reserveShapes");
    private List<WeaponModulatorData> Stored => Field<List<WeaponModulatorData>>("_recoveredModulators");
    private FpsPlayerController Player => Field<FpsPlayerController>("_player");
    private List<FpsLootPillarInstance> Pillars => Field<List<FpsLootPillarInstance>>("_groundPillarInstances");
    private FpsLootPillarRenderer PillarRenderer => Field<FpsLootPillarRenderer>("_lootPillarRenderer");

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/ground-loot-validation/"), "Validation uses isolated saves");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            LegacyHillFixture.Configure(_map);
            AddChild(_map);
            await Frames(4);
            _map.SetProcess(false);
            _map.SetPhysicsProcess(false);
            Player.SetPhysicsProcess(false);
            ValidateCapacityAndSwaps();
            ValidateWingAcquisition();
            ValidateStoredCapacity();
            ValidateExplicitCollection();
            await ValidateBurstPhysics();
            await ValidatePresentation();
            await ValidateGroundFocusAndPersistence();
            await ValidateVictoryLifecycle();
            GD.Print($"Ground loot validation passed: {_checks} checks.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static EquipmentItem Item(string key = "square", string rarity = "divine", params SubstatRoll[] stats)
    {
        EquipmentItem item = new()
        {
            Shape = EquipmentCatalogDefaults.CreateDefaultShapes().Single(shape => shape.Key == key),
            Rarity = EquipmentCatalogDefaults.CreateRarityByKey(rarity),
            Level = EquipmentCatalogDefaults.MaxWeaponLevel,
            Substats = new List<SubstatRoll>(stats),
        };
        StatGenerator.RecalculateDerivedStats(item);
        return item;
    }
    private static WeaponModulatorData Part() => ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Flawed);
    private void ResetInventory()
    {
        _map.CloseWeaponInspection();
        Active.Clear(); Reserve.Clear(); Stored.Clear();
        Set("_selectedWeaponIndex", 0); Set("_inspectionWeaponIndex", 0);
        Set("_defeat", false); Set("_victory", false); Set("_shopOpen", false); Set("_pauseRequested", false);
    }
    private void OpenInventory()
    {
        _map._Input(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = true });
        Check(_map.IsWeaponInspectionOpen && Field<bool>("_pauseRequested"), "Tab opens paused inventory");
    }
    private void ValidateCapacityAndSwaps()
    {
        ResetInventory();
        EquipmentItem[] shapes = Enumerable.Range(0, 7).Select(_ => Item()).ToArray();
        shapes[5].Substats.Add(new(SubstatType.HealthPercent, 80f));
        shapes[5].Substats.Add(new(SubstatType.MovementSpeed, 25f));
        WeaponModulatorData installed = Part(); shapes[5].Modulators.Add(installed);
        StatGenerator.RecalculateDerivedStats(shapes[5]);
        foreach (EquipmentItem shape in shapes) Check(_map.TryStoreRunShape(shape, out _), "Shape enters available inventory capacity");
        EquipmentItem overflow = Item();
        Check(_map.EquippedShapeCount == 5 && _map.ReserveShapeCount == 2 && _map.GetInventoryShapes().SequenceEqual(shapes), "Inventory holds five active then two reserve shapes");
        Check(!_map.TryStoreRunShape(overflow, out string reason) && reason.Length > 0 && _map.GetInventoryShapes().SequenceEqual(shapes), "Eighth shape rejection leaves exact inventory unchanged");
        Call("EquipWeapon", 0, false);
        float originalMax = Player.MaxHealth, originalMove = Player.MoveSpeed;
        Player.ApplyDamage(Player.CurrentHealth * .35f);
        float ratio = Player.CurrentHealth / Player.MaxHealth;
        Check(Player.MaxHealth == originalMax && Player.MoveSpeed == originalMove, "Reserve health and movement bonuses do not affect the player");
        OpenInventory();
        Button reserveChoice = Descendants(_map).OfType<Button>().Single(button => button.Name == "ShapeChoice5");
        reserveChoice.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_map.InspectedWeaponIndex == 5 && ReferenceEquals(Active[0], shapes[0]) && Player.MaxHealth == originalMax,
            "Selecting reserve row previews it without changing equipped combat stats");
        Check(Descendants(_map).OfType<Button>().Single(button => button.Name == "EquipInspectedShape").Text.Contains("Swap"),
            "Reserve inspection exposes a swap action");
        Check(ReferenceEquals(_map.GetInventoryWeaponSlots(5)[0], installed) && _map.GetInventoryWeaponStats(5)!.MaximumPlayerHealth > originalMax, "Reserve inspection exposes its installed part and own stats");
        Check(_map.TryEquipInventoryShape(5) && ReferenceEquals(Active[0], shapes[5]) && ReferenceEquals(Reserve[0], shapes[0]) && Active.Count == 5 && Reserve.Count == 2, "Reserve equip exchanges exact shape references with held active slot");
        Check(ReferenceEquals(Active[0].Modulators[0], installed) && Player.MaxHealth > originalMax && Player.MoveSpeed > originalMove && Mathf.IsEqualApprox(Player.CurrentHealth / Player.MaxHealth, ratio), "Reserve swap keeps exact installed part, applies passives and preserves health fraction");
        Check(_map.TryEquipInventoryShape(5) && Player.MaxHealth == originalMax && Player.MoveSpeed == originalMove && Mathf.IsEqualApprox(Player.CurrentHealth / Player.MaxHealth, ratio), "Swapping back removes reserve passives without healing");
        int before = Pickups.Count;
        Check(_map.TryDropInventoryShape(6, out _) && Reserve.Count == 1 && Pickups.Count == before + 1 && ReferenceEquals(PickupField<EquipmentItem>(Pickups[Pickups.Count - 1]!, "Weapon"), shapes[6]), "Dropping reserve shape transfers exact item to ground and frees capacity");
        Check(_map.TryStoreRunShape(overflow, out _) && Reserve.Count == 2, "Freed reserve slot accepts another shape");
        _map.CloseWeaponInspection();
    }
    private void ValidateStoredCapacity()
    {
        ResetInventory();
        EquipmentItem shape = Item(); Active.Add(shape); Call("EquipWeapon", 0, false);
        WeaponModulatorData installed = Part(); shape.Modulators.Add(installed);
        StatGenerator.RecalculateDerivedStats(shape);
        foreach (WeaponModulatorData part in Enumerable.Range(0, 100).Select(_ => Part())) Check(_map.TryStoreRunModulator(part, out _), "Loose part fits storage capacity");
        WeaponModulatorData overflow = Part();
        Check(Stored.Count == 100 && !_map.TryStoreRunModulator(overflow, out string reason) && reason.Length > 0 && !Stored.Contains(overflow), "One hundred loose parts excludes installed parts and rejects overflow atomically");
        OpenInventory();
        Check(!_map.TryRemoveInventoryModulator(shape, 0, out _) && ReferenceEquals(shape.Modulators[0], installed) && Stored.Count == 100, "Full storage rejects socket removal and retains exact installed part");
        WeaponModulatorData dropped = Stored[0]; int before = Pickups.Count;
        Check(_map.TryDropInventoryModulator(dropped, out _) && Stored.Count == 99 && Pickups.Count == before + 1 && ReferenceEquals(PickupField<WeaponModulatorData>(Pickups[Pickups.Count - 1]!, "Modulator"), dropped), "Dropping loose part preserves exact reference and frees storage");
        Check(_map.TryRemoveInventoryModulator(shape, 0, out _) && Stored.Count == 100 && ReferenceEquals(Stored[^1], installed), "Freed storage accepts exact removed installed part");
        Check(!_map.TryDropInventoryShape(0, out _) && Active.Count == 1, "Inventory cannot drop its last active shape");
        _map.CloseWeaponInspection();
    }
    private void ValidateWingAcquisition()
    {
        ResetInventory();
        _map.TryStoreRunShape(Item(), out _);
        EquipmentItem wingSupport = Item(); wingSupport.Modulators.Add(ModulatorCatalog.CreateByKey("wing_man"));
        Check(_map.TryStoreRunShape(wingSupport, out _) && Active.Count == 2, "Unique Wing shape enters a support slot");
        EquipmentItem conflicting = Item(); conflicting.Modulators.Add(ModulatorCatalog.CreateByKey("wing_man"));
        Check(_map.TryStoreRunShape(conflicting, out _) && Active.Count == 2 && ReferenceEquals(Reserve[0], conflicting),
            "Conflicting Wing acquisition uses reserve without adding duplicate active support");
        EquipmentItem secondConflict = Item(); secondConflict.Modulators.Add(ModulatorCatalog.CreateByKey("wing_man"));
        _map.TryStoreRunShape(secondConflict, out _);
        EquipmentItem overflow = Item(); overflow.Modulators.Add(ModulatorCatalog.CreateByKey("wing_man"));
        Check(!_map.TryStoreRunShape(overflow, out string reason) && reason.Length > 0 && Active.Count == 2 && Reserve.Count == 2,
            "Conflicting Wing acquisition with full reserves preserves the existing loadout");
    }
    private void ClearGround()
    {
        foreach (object pickup in Pickups.Cast<object>().ToArray())
        {
            if (PickupField<RigidBody3D?>(pickup, "DropBody") != null) Call("SettleGroundLootBody", pickup);
            PickupField<Node3D?>(pickup, "Visual")?.QueueFree();
        }
        Pickups.Clear(); Set("_groundCoinCount", 0); Set("_groundVisualRefresh", 0f);
        Set("_groundPillarsDirty", true);
    }
    private async Task ValidateBurstPhysics()
    {
        ResetInventory(); ClearGround(); Active.Add(Item()); Call("EquipWeapon", 0, false);
        EquipmentItem weapon = Item("triangle");
        Call("SpawnBurstWeaponLoot", Player.GlobalPosition + Vector3.Forward * 2f + Vector3.Up * .5f, weapon);
        object pickup = Pickups[Pickups.Count - 1]!;
        RigidBody3D body = PickupField<RigidBody3D>(pickup, "DropBody");
        Vector3 origin = body.GlobalPosition;
        Call("RefreshGroundLootVisuals");
        Check(Pillars.Count == 1 && !Pillars[0].EmitSparks,
            "Flying burst weapons retain their pillar without restarting GPU spark emitters");
        Check(ReferenceEquals(PickupField<EquipmentItem>(pickup, "Weapon"), weapon) && body.LinearVelocity.Y > 0f &&
            body.CollisionMask == 1 && body.CollisionLayer == 0,
            "Enemy burst launches a cosmetic world-colliding rigid body while preserving the exact weapon data");
        for (int frame = 0; frame < 6; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Call("TickGroundLootPhysics", 1f / 60f);
        }
        Check(body.GlobalPosition.DistanceTo(origin) > .05f && PickupField<RigidBody3D?>(pickup, "DropBody") != null &&
            Active.Count == 1 && Pickups.Contains(pickup), "Burst weapon visibly moves under runtime physics without automatic collection");
        Set("_pauseRequested", true); Call("TickGroundLootPhysics", .02f);
        Vector3 pausedPosition = body.GlobalPosition; float pausedAge = PickupField<float>(pickup, "DropAge");
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Call("TickGroundLootPhysics", .5f);
        Check(body.Freeze && body.GlobalPosition.DistanceTo(pausedPosition) < .001f && PickupField<float>(pickup, "DropAge") == pausedAge,
            "Paused inventory freezes burst movement and preserves the cosmetic flight lifetime");
        if (OS.GetCmdlineUserArgs().Contains("--capture"))
        {
            Transform3D camera = Player.Camera.GlobalTransform;
            Call("RefreshGroundLootVisuals"); Player.Camera.LookAt(body.GlobalPosition, Vector3.Up);
            await Capture("ground-burst-airborne.png"); Player.Camera.GlobalTransform = camera;
        }
        Set("_pauseRequested", false); Call("TickGroundLootPhysics", .02f);
        for (int frame = 0; frame < 165 && PickupField<RigidBody3D?>(pickup, "DropBody") != null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Call("TickGroundLootPhysics", 1f / 60f);
        }
        Call("RefreshGroundLootVisuals");
        Check(PickupField<RigidBody3D?>(pickup, "DropBody") == null && Field<IList>("_airborneGroundLoot").Count == 0 &&
            Pickups.Contains(pickup) && ReferenceEquals(PickupField<EquipmentItem>(pickup, "Weapon"), weapon) &&
            Pillars.Count == 1 && Pillars[0].EmitSparks && Pillars[0].Position == PickupField<Vector3>(pickup, "Position"),
            "Cosmetic flight expires into a stable collectible and matching pillar without losing the weapon");
        Vector3 settled = PickupField<Vector3>(pickup, "Position");
        var supportRay = PhysicsRayQueryParameters3D.Create(settled + Vector3.Up * .1f, settled + Vector3.Down * .2f, 1);
        var support = _map.GetWorld3D().DirectSpaceState.IntersectRay(supportRay);
        Check(support.Count > 0 && ((Vector3)support["normal"]).Y > .25f && ((Vector3)support["position"]).DistanceTo(settled) < .03f &&
            PickupField<Node3D?>(pickup, "Visual") != null,
            "Burst finishes after genuine physics frames on supported ground with a materialized collectible model");
        if (OS.GetCmdlineUserArgs().Contains("--capture"))
        {
            Transform3D camera = Player.Camera.GlobalTransform;
            Player.Camera.LookAt(settled + Vector3.Up * .35f, Vector3.Up);
            await Capture("ground-burst-settled.png"); Player.Camera.GlobalTransform = camera;
        }
        Call("TickGroundLootPhysics", 3f); Call("TickDefenseRun", .016f);
        Check(PickupField<Vector3>(pickup, "Position") == settled && Pickups.Contains(pickup) && Active.Count == 1,
            "Settled burst equipment stays still and waits for explicit pickup");
        for (int index = 1; index < 7; index++) _map.TryStoreRunShape(Item(), out _);
        EquipmentItem second = Item("circle"); Call("SpawnBurstWeaponLoot", Player.GlobalPosition + Vector3.Up * .5f, second);
        object blocked = Pickups[Pickups.Count - 1]!; RigidBody3D blockedBody = PickupField<RigidBody3D>(blocked, "DropBody");
        Call("CollectDefensePickup", blocked);
        Check(Pickups.Contains(blocked) && ReferenceEquals(PickupField<RigidBody3D?>(blocked, "DropBody"), blockedBody) &&
            Field<IList>("_airborneGroundLoot").Contains(blocked), "Full inventory rejection retains the live burst body and exact ground item");
        OpenInventory(); Check(_map.TryDropInventoryShape(6, out _), "Dropping reserve creates room for a burst pickup"); _map.CloseWeaponInspection();
        Call("CollectDefensePickup", blocked); Call("RefreshGroundLootVisuals");
        Check(!Pickups.Contains(blocked) && PickupField<RigidBody3D?>(blocked, "DropBody") == null &&
            !Field<IList>("_airborneGroundLoot").Contains(blocked) && ReferenceEquals(Reserve[^1], second),
            "Successful burst pickup transfers the exact weapon and cleans up its temporary physics binding");
        ClearGround();
        Vector3 actorTestPosition = Player.GlobalPosition;
        Player.GlobalPosition = (Vector3)Call("ResolveSettledLootSupport", Player.GlobalPosition + Vector3.Up * 3f)! + Vector3.Up * .1f;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Call("SpawnBurstWeaponLoot", Player.GlobalPosition + Vector3.Up * 3f, Item());
        object abovePlayer = Pickups[Pickups.Count - 1]!;
        RigidBody3D abovePlayerBody = PickupField<RigidBody3D>(abovePlayer, "DropBody");
        Check(abovePlayerBody.GetCollisionExceptions().Contains(Player),
            "Burst bodies exclude the player from cosmetic collision");
        abovePlayerBody.GlobalPosition = Player.GlobalPosition + Vector3.Up * 3f;
        Call("SettleGroundLootBody", abovePlayer);
        Vector3 belowPlayer = PickupField<Vector3>(abovePlayer, "Position");
        Check(belowPlayer.Y < Player.GlobalPosition.Y + .2f &&
            belowPlayer.DistanceTo((Vector3)Call("ResolveSettledLootSupport", Player.GlobalPosition + Vector3.Up * 3f)!) < .001f,
            "Loot settling above the player reaches world support rather than resting on the player's head");
        Call("RegroundEquipmentLoot");
        Check(PickupField<Vector3>(abovePlayer, "Position").DistanceTo(belowPlayer) < .03f,
            "Regrounding keeps the actor-excluding world support position");
        ClearGround();
        Player.GlobalPosition = actorTestPosition;
        var platform = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3f, .5f, 3f) } });
        _map.AddChild(platform);
        platform.GlobalPosition = Player.GlobalPosition + Vector3.Right * 5f + Vector3.Up * 2f;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Vector3 platformTop = platform.GlobalPosition + Vector3.Up * .25f;
        Call("SpawnBurstWeaponLoot", platformTop + Vector3.Up * .5f, Item());
        object onPlatform = Pickups[Pickups.Count - 1]!;
        PickupField<RigidBody3D>(onPlatform, "DropBody").GlobalPosition = platformTop + Vector3.Up * .5f;
        Call("SettleGroundLootBody", onPlatform);
        Call("RegroundEquipmentLoot");
        Vector3 supportedPlatform = PickupField<Vector3>(onPlatform, "Position");
        Call("RegroundEquipmentLoot"); Call("RefreshGroundLootVisuals");
        Check(supportedPlatform.DistanceTo(platformTop) < .03f &&
            PickupField<Vector3>(onPlatform, "Position").DistanceTo(platformTop) < .03f &&
            Pillars.Count == 1 && Pillars[0].Position.DistanceTo(platformTop) < .03f,
            "Repeated regrounding preserves elevated world-platform support and its matching pillar");
        ClearGround(); platform.QueueFree();
        EquipmentItem[] overflow = Enumerable.Range(0, FpsVerticalSlice.GroundLootPhysicsCapacity + 1).Select(_ => Item()).ToArray();
        foreach (EquipmentItem item in overflow) Call("SpawnBurstWeaponLoot", Player.GlobalPosition + Vector3.Up * .5f, item);
        Check(Pickups.Count == overflow.Length && Field<IList>("_airborneGroundLoot").Count <= FpsVerticalSlice.GroundLootPhysicsCapacity &&
            Pickups.Cast<object>().Select(record => PickupField<EquipmentItem>(record, "Weapon")).SequenceEqual(overflow) &&
            PickupField<RigidBody3D?>(Pickups[0]!, "DropBody") == null,
            "Burst physics capacity settles overflow while retaining every exact authoritative weapon record");
        Call("TickGroundLootPhysics", 3f);
        Check(Field<IList>("_airborneGroundLoot").Count == 0 && Pickups.Count == overflow.Length,
            "All bounded burst bodies finish their flight while every uncollected weapon remains on the ground");
        ClearGround();
    }
    private object AddPickup(EquipmentItem? shape, WeaponModulatorData? part, int coins = 0, Vector3? position = null)
    {
        Call("AddDefensePickup", position ?? Player.GlobalPosition, coins, shape, part);
        return Pickups[Pickups.Count - 1]!;
    }
    private void ValidateExplicitCollection()
    {
        ResetInventory(); ClearGround(); Active.Add(Item()); Call("EquipWeapon", 0, false);
        EquipmentItem shape = Item("triangle"); WeaponModulatorData part = Part();
        object shapePickup = AddPickup(shape, null), partPickup = AddPickup(null, part);
        AddPickup(null, null, 7);
        int coins = Field<int>("_defenseCoins");
        Call("TickDefenseRun", .016f);
        Check(Pickups.Contains(shapePickup) && Pickups.Contains(partPickup) && Active.Count == 1 && Stored.Count == 0 && Field<int>("_defenseCoins") == coins + 7, "Standing on equipment does not collect it; nearby currency still collects");
        AddPickup(null, null, 13, Player.GlobalPosition + Vector3.Right * 30);
        Call("SweepDefenseLoot");
        Check(Pickups.Count == 2 && Field<int>("_defenseCoins") == coins + 20, "Wave loot sweep collects only currency and leaves equipment references");
        Call("CollectDefensePickup", shapePickup); Call("CollectDefensePickup", partPickup);
        Check(Active.Count == 2 && Stored.Count == 1 && ReferenceEquals(Active[1], shape) && ReferenceEquals(Stored[0], part) && Pickups.Count == 0, "Explicit collection transfers exact ground item references");
        for (int index = Active.Count; index < 7; index++) _map.TryStoreRunShape(Item(), out _);
        object rejected = AddPickup(Item(), null);
        Call("CollectDefensePickup", rejected);
        Check(Pickups.Contains(rejected) && Active.Count == 5 && Reserve.Count == 2, "Full shape collection leaves targeted item on ground");
    }
    private async Task ValidatePresentation()
    {
        await ValidateGroundModels();
        List<float> heights = new(); List<Color> colors = new(); List<int> ranks = new();
        foreach (string rarity in new[] { "common", "uncommon", "rare", "legendary", "mythic", "divine" })
        {
            Node3D visual = FpsGroundItemVisual.Create(Item(rarity: rarity), null); AddChild(visual);
            await Frames(1);
            FpsLootPillarInstance pillar = FpsGroundItemVisual.GetPillarInstance(Item(rarity: rarity), null, Vector3.Zero, 0f);
            heights.Add(pillar.Height);
            colors.Add(pillar.Color);
            ranks.Add(pillar.RarityRank);
            Check(visual.FindChild("ItemModel", true, false) != null, $"{rarity} ground shape has its base shape model");
            Check(visual.FindChild("RarityPillar", true, false) == null, $"{rarity} item model does not duplicate the batched billboard pillar");
            visual.QueueFree();
        }
        Check(heights.Zip(heights.Skip(1), (a, b) => b > a).All(value => value) && colors.Distinct().Count() == 6, "Every rarity has a distinct color and strictly taller pillar");
        Check(ranks.SequenceEqual(Enumerable.Range(0, 6)), "Weapon factory passes increasing rarity ranks to GPU spark scaling");
        heights.Clear(); colors.Clear();
        foreach (ModulatorPowerTier rarity in Enum.GetValues<ModulatorPowerTier>())
        {
            WeaponModulatorData part = Part(); part.PowerTier = rarity;
            WeaponModulatorData upgraded = ModulatorCatalog.Clone(part, WeaponModulatorTier.Flawless);
            FpsLootPillarInstance firstPillar = FpsGroundItemVisual.GetPillarInstance(null, part, Vector3.Zero, 0f);
            FpsLootPillarInstance secondPillar = FpsGroundItemVisual.GetPillarInstance(null, upgraded, Vector3.Zero, 0f);
            heights.Add(firstPillar.Height);
            colors.Add(firstPillar.Color);
            Check(firstPillar.Color == secondPillar.Color && firstPillar.Height == secondPillar.Height && firstPillar.Width == secondPillar.Width && firstPillar.RarityRank == secondPillar.RarityRank,
                $"{rarity} modulator pillar represents rarity consistently across quality upgrades");
        }
        Check(heights.Zip(heights.Skip(1), (a, b) => b > a).All(value => value) && colors.Distinct().Count() == 3,
            "Three modulator rarities have distinct colors and increasing pillar height");
        EquipmentItem item = Item(stats: new[] { new SubstatRoll(SubstatType.AttackSpeed, 8f), new SubstatRoll(SubstatType.AttackSpeed, 12f) });
        FpsGroundItemCard card = new(); AddChild(card); card.ShowItem(item, null, "F: pick up shape");
        string text = string.Join("\n", Descendants(card).OfType<Label>().Select(label => label.Text));
        Check(text.Contains("Summed item bonuses") && text.Contains("+20%") && !text.Contains("+8%") && !text.Contains("+12%"), "Ground card sums matching item substat bonuses");
        card.QueueFree();
        if (OS.GetCmdlineUserArgs().Contains("--capture"))
        {
            await LootPillarRenderChecks.Run(this, Check);
            await ValidateFarPillarRendering();
        }
    }
    private async Task ValidateGroundModels()
    {
        foreach (ShapeData shape in EquipmentCatalogDefaults.CreateDefaultShapes())
        {
            Node3D visual = FpsGroundItemVisual.Create(Item(shape.Key), null); AddChild(visual);
            Node3D model = visual.GetNode<Node3D>("ItemModel/GroundShapeModel");
            Aabb? bounds = FpsWeaponVisualFactory.GetVisualBounds(visual);
            Check(model.GetMeta("shape_key").AsString() == shape.Key && model.GetMeta("visual_source").AsString() == "ShapeVisualFactory" &&
                !model.GetMeta("assembled_weapon").AsBool() && model.FindChild("MuzzleAnchor", true, false) == null,
                $"{shape.Key} ground weapon uses its identified base shape rather than an assembled firearm");
            Check(bounds.HasValue && bounds.Value.Size.IsFinite() &&
                Mathf.IsEqualApprox(Longest(bounds.Value.Size), .78f), $"{shape.Key} ground shape has finite geometry normalized to its pickup size");
            visual.QueueFree();
        }
        foreach (ModulatorPowerTier rarity in Enum.GetValues<ModulatorPowerTier>())
        {
            foreach (WeaponModulatorTier quality in new[] { WeaponModulatorTier.Flawed, WeaponModulatorTier.Flawless })
            {
                WeaponModulatorData part = ModulatorCatalog.CreateByKey("sharpened_form", quality); part.PowerTier = rarity;
                EquipmentItem equipped = Item(); equipped.Modulators.Add(part); StatGenerator.RecalculateDerivedStats(equipped);
                Node3D gun = FpsWeaponVisualFactory.Create(equipped); AddChild(gun);
                Node3D bay = (Node3D)gun.FindChild("ModulatorSocket01", true, false)!;
                Aabb? bayBefore = FpsWeaponVisualFactory.GetVisualBounds(bay); Vector3 scaleBefore = bay.Scale;
                Node3D visual = FpsGroundItemVisual.Create(null, part); AddChild(visual);
                Node3D cartridge = (Node3D)visual.GetNode<Node3D>("ItemModel").GetChild(0);
                int expected = (int)rarity + 1;
                MeshInstance3D[] notches = Descendants(cartridge).OfType<MeshInstance3D>()
                    .Where(mesh => mesh.Name.ToString().Replace(" ", "").Contains("IdentityBar", StringComparison.OrdinalIgnoreCase) && mesh.IsVisibleInTree()).ToArray();
                Color color = FpsGroundItemVisual.GetRarityColor(null, part);
                Check(notches.Length == expected && cartridge.GetMeta("modulator_notch_count").AsInt32() == expected &&
                    cartridge.GetMeta("modulator_power_tier").AsInt32() == expected,
                    $"{rarity}/{quality} ground modulator shows exactly {expected} power-tier notches");
                Check(notches.All(mesh => mesh.MaterialOverride is StandardMaterial3D material && material.AlbedoColor == color && material.Emission == color) &&
                    cartridge.GetMeta("modulator_rarity_color").AsColor() == color,
                    $"{rarity}/{quality} cartridge accents match its pillar rarity color");
                Aabb bounds = FpsWeaponVisualFactory.GetVisualBounds(visual)!.Value;
                Check(Mathf.IsEqualApprox(Longest(bounds.Size), .26f), $"{rarity}/{quality} ground modulator is half the former pickup size");
                MeshInstance3D[] installedNotches = Descendants(bay).OfType<MeshInstance3D>()
                    .Where(mesh => mesh.Name.ToString().Replace(" ", "").Contains("IdentityBar", StringComparison.OrdinalIgnoreCase) && mesh.IsVisibleInTree()).ToArray();
                Check(installedNotches.Length == expected && installedNotches.All(mesh => mesh.MaterialOverride is StandardMaterial3D material && material.AlbedoColor == color) &&
                    bay.Scale == scaleBefore && FpsWeaponVisualFactory.GetVisualBounds(bay) == bayBefore,
                    $"{rarity}/{quality} installed cartridge keeps its size and shares power-tier notches and rarity accents");
                visual.QueueFree(); gun.QueueFree();
            }
        }
        await Frames(2);
    }
    private static float Longest(Vector3 size) => Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
    private async Task ValidateGroundFocusAndPersistence()
    {
        ResetInventory(); ClearGround(); Active.Add(Item()); Call("EquipWeapon", 0, false);
        Vector3 playerPosition = Player.GlobalPosition;
        _map.DefenseSettings!.MaxLoosePickups = 8;
        for (int index = 0; index < 30; index++)
            AddPickup(Item(), null, position: playerPosition + new Vector3((index % 6) * .8f - 2, 0, -1.5f - index / 6));
        object[] originals = Pickups.Cast<object>().ToArray();
        Call("RefreshGroundLootVisuals");
        Check(Pickups.Count == 30 && Pickups.Cast<object>().Count(pickup => PickupField<Node3D?>(pickup, "Visual") != null) <= 8,
            "Visual budget retains all thirty authoritative ground equipment records");
        FpsLootPillarInstance[] pillarsBefore = Pillars.ToArray();
        Check(PillarRenderer.PillarCount == 30 && Pillars.Count == 30,
            "Eight-model budget still renders pillars for all thirty equipment records");
        Player.GlobalPosition += Vector3.Right * 150;
        Call("RefreshGroundLootVisuals");
        Check(Pickups.Count == 30 && Pickups.Cast<object>().All(pickup => PickupField<Node3D?>(pickup, "Visual") == null),
            "Distant equipment loses only its visuals, retaining recoverable records");
        Check(PillarRenderer.PillarCount == 30 && Pillars.SequenceEqual(pillarsBefore),
            "Moving beyond the model view distance preserves every pillar and animation phase");
        Player.GlobalPosition = playerPosition;
        Call("RefreshGroundLootVisuals");
        Call("BuildArena");
        await Frames(3);
        Check(Pickups.Cast<object>().SequenceEqual(originals) && originals.All(pickup => PickupField<EquipmentItem>(pickup, "Weapon").Shape != null),
            "Live geometry rebuild preserves all exact equipment records and items");
        Call("RefreshGroundLootVisuals");
        Check(PillarRenderer.PillarCount == 30 && Pillars.Select(pillar => pillar.Position)
            .SequenceEqual(originals.Select(pickup => PickupField<Vector3>(pickup, "Position"))),
            "Live geometry rebuild retains the complete pillar snapshot at regrounded item positions");
        object moved = originals[0]; Vector3 groundedPosition = PickupField<Vector3>(moved, "Position");
        moved.GetType().GetField("Position", Fields)!.SetValue(moved, groundedPosition + Vector3.Up * 30);
        Call("RegroundEquipmentLoot"); Call("RefreshGroundLootVisuals");
        Check(Pillars[0].Position == PickupField<Vector3>(moved, "Position") && Pillars[0].Position.Y < groundedPosition.Y + 30,
            "Regrounding uploads changed terrain support positions to the pillar renderer");
        Call("CollectDefensePickup", originals[0]); Call("RefreshGroundLootVisuals");
        Check(PillarRenderer.PillarCount == 29 && Pillars.SequenceEqual(pillarsBefore.Skip(1)),
            "Explicit collection removes only that item's pillar and preserves survivor phases");

        ClearGround();
        object focused = AddPickup(Item("triangle", "divine", new(SubstatType.DamagePercent, 10f),
            new(SubstatType.DamagePercent, 5f), new(SubstatType.CritChance, 7f)), null,
            position: Player.GlobalPosition + Vector3.Forward * 1.7f);
        Vector3 target = PickupField<Vector3>(focused, "Position") + Vector3.Up * .45f;
        Player.Camera.LookAt(target, Vector3.Up);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Call("TickGroundLoot", .2f);
        Check(ReferenceEquals(Call("FindFocusedGroundPickup"), focused) && Field<FpsGroundItemCard>("_groundItemCard").Visible,
            "A nearby aimed shape becomes focused and displays the ground card");
        Player.Camera.RotateY(Mathf.Pi * .5f);
        Check(Call("FindFocusedGroundPickup") == null && !(bool)Call("TryInteractGroundLoot")! && Pickups.Contains(focused),
            "Looking away prevents F collection and leaves nearby equipment on the ground");
        Player.Camera.LookAt(target, Vector3.Up);
        StaticBody3D wall = new() { CollisionLayer = 1, CollisionMask = 1, Name = "ValidationLootOccluder" };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.5f, 4, .5f) } });
        _map.AddChild(wall); wall.GlobalPosition = (Player.Camera.GlobalPosition + target) * .5f;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check(Call("FindFocusedGroundPickup") == null && !(bool)Call("TryInteractGroundLoot")! && Pickups.Contains(focused),
            "Solid geometry blocks focus and key-press collection through a wall");
        wall.QueueFree(); await Frames(2);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        for (int index = 1; index < 7; index++) _map.TryStoreRunShape(Item(), out _);
        Call("TickGroundLoot", .2f);
        _map._UnhandledInput(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = true });
        Check(Pickups.Contains(focused) && Active.Count == 5 && Reserve.Count == 2 &&
            Descendants(Field<FpsGroundItemCard>("_groundItemCard")).OfType<Label>().Any(label => label.Text.Contains("Inventory full")),
            "F consumes the aimed interaction but full inventory retains ground item and shows capacity hint");
        Check(PillarRenderer.PillarCount == 1 && Pillars[0].Position == PickupField<Vector3>(focused, "Position"),
            "Full inventory rejection preserves the aimed ground item's pillar");
        if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("ground-item-card.png");
        OpenInventory();
        await Frames(30);
        if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture("ground-inventory-capacity.png");
        Check(_map.TryDropInventoryShape(6, out _), "Dropping a reserve frees a slot for aimed ground recovery");
        _map.CloseWeaponInspection();
        Player.Camera.LookAt(target, Vector3.Up);
        _map._UnhandledInput(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = true });
        Check(!Pickups.Contains(focused) && Active.Count == 5 && Reserve.Count == 2,
            "F picks up the aimed shape after inventory space is freed");

        ClearGround(); Stored.Clear();
        for (int index = 0; index < 100; index++) _map.TryStoreRunModulator(Part(), out _);
        object fullPart = AddPickup(null, Part(), position: Player.GlobalPosition + Vector3.Forward * 1.7f);
        Call("CollectDefensePickup", fullPart);
        Check(Pickups.Contains(fullPart) && Stored.Count == 100, "Full loose storage leaves explicit modulator pickup on ground");
        Set("_waitingForNextWave", true); Set("_shopOpen", true);
        EquipmentItem current = Active[_map.EquippedWeaponIndex];
        WeaponModulatorData installed = Part(); current.Modulators.Add(installed);
        Check(!(bool)Call("ReturnDefenseModulator", current, installed)! && current.Modulators.Contains(installed) && Stored.Count == 100,
            "Shop return rejects full loose storage without stripping the installed part");
        List<WeaponModulatorData> stock = Field<List<WeaponModulatorData>>("_shopStock");
        stock.Clear(); WeaponModulatorData offer = Part(); stock.Add(offer);
        Set("_defenseCoins", 10000);
        Call("RebuildDefenseShopMenu");
        Button buy = Descendants(Field<VBoxContainer>("_shopContent")).OfType<Button>()
            .Single(button => button.Text.StartsWith("Buy for storage", StringComparison.Ordinal));
        buy.EmitSignal(BaseButton.SignalName.Pressed);
        Check(stock.Contains(offer) && Stored.Count == 100 && Field<int>("_defenseCoins") == 10000,
            "Full storage shop purchase leaves coins and stock unchanged");
        Set("_shopOpen", false);
    }
    private async Task ValidateVictoryLifecycle()
    {
        _map.CloseWeaponInspection(); ClearGround(); Stored.Clear();
        Set("_pauseRequested", false); Set("_shopOpen", false); Set("_victory", true);
        Set("_lootingAfterVictory", false); Call("RefreshCombatState");
        bool Controls() => (bool)typeof(FpsPlayerController).GetField("_combatEnabled", Fields)!.GetValue(Player)!;
        Check(!(bool)Call("IsCombatRunning")! && !Controls() && Field<ColorRect>("_overlayBackdrop").Visible,
            "Final victory ends combat and shows results with field controls disabled");
        Call("OnResumePressed");
        Check(Field<bool>("_lootingAfterVictory") && Controls() && !(bool)Call("IsCombatRunning")! && !Field<ColorRect>("_overlayBackdrop").Visible,
            "Collect ground loot resumes movement controls while completed combat stays stopped");
        WeaponModulatorData reward = Part();
        object pickup = AddPickup(null, reward, position: Player.GlobalPosition + Vector3.Forward * 1.7f);
        Player.Camera.LookAt(PickupField<Vector3>(pickup, "Position") + Vector3.Up * .45f, Vector3.Up);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _map._UnhandledInput(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, Pressed = true });
        Check(!Pickups.Contains(pickup) && ReferenceEquals(Stored[0], reward) && !(bool)Call("IsCombatRunning")!,
            "Post-victory aimed F recovers the exact part without restarting combat");
        OpenInventory();
        Check(!Controls() && !(bool)Call("IsCombatRunning")!, "Post-victory Tab inspection pauses field controls and combat stays stopped");
        _map.CloseWeaponInspection();
        Check(!Field<bool>("_pauseRequested") && Controls() && Field<bool>("_lootingAfterVictory"),
            "Closing post-victory inspection restores loot movement without a stale pause");
        _map._UnhandledInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        Check(!Field<bool>("_lootingAfterVictory") && !Controls() && !Field<bool>("_pauseRequested") &&
            !_map.IsWeaponInspectionOpen && Field<ColorRect>("_overlayBackdrop").Visible && !(bool)Call("IsCombatRunning")!,
            "Escape returns from final looting to results and disables controls cleanly");
        OpenInventory(); _map.CloseWeaponInspection();
        Check(!Controls() && !_map.IsWeaponInspectionOpen && !Field<bool>("_pauseRequested") &&
            Field<ColorRect>("_overlayBackdrop").Visible, "Inspection opened from final results returns to results with no stale field controls");
    }
    private async Task ValidateFarPillarRendering()
    {
        ClearGround();
        Vector3 originalPosition = Player.GlobalPosition;
        Transform3D originalCamera = Player.Camera.GlobalTransform;
        Label crosshair = Field<Label>("_crosshairLabel");
        bool crosshairVisible = crosshair.Visible; crosshair.Hide();
        Player.GlobalPosition = new Vector3(80, .1f, -80);
        // Keep the full sightline inside the arena boundary and outside the hill footprint.
        Vector3 itemPosition = new(-80, 0, -80);
        Player.Camera.LookAt(itemPosition + Vector3.Up * 1.5f, Vector3.Up);
        Call("RefreshGroundLootVisuals");
        PillarRenderer.SetAnimationTime(0f);
        Set("_noticeTimer", 0f); Field<Label>("_noticeLabel").Hide();
        async Task<Image> Render(string name)
        {
            await Frames(4); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = GetViewport().GetTexture().GetImage(); image.Convert(Image.Format.Rgba8);
            string path = ProjectSettings.GlobalizePath("res://.tmp/ground-loot-validation/native/" + name);
            Check(image.SavePng(path) == Error.Ok, "Distant pillar render saved: " + name);
            return image;
        }
        Image empty = await Render("atlas-far-empty.png");
        object farItem = AddPickup(Item(), null, position: itemPosition);
        Call("RefreshGroundLootVisuals");
        Check(PickupField<Node3D?>(farItem, "Visual") == null && PillarRenderer.PillarCount == 1 &&
            PickupField<Vector3>(farItem, "Position").DistanceTo(Player.GlobalPosition) > 120f,
            "Real-map item beyond one hundred twenty metres retains its pillar while its model stays culled");
        Image far = await Render("atlas-far-160m.png");
        int changed = 0;
        Vector2 projected = Player.Camera.UnprojectPosition(PickupField<Vector3>(farItem, "Position") + Vector3.Up * 1.5f);
        int centerX = Mathf.RoundToInt(projected.X), centerY = Mathf.RoundToInt(projected.Y);
        // Only the target's screen region counts; weapon idle animation is outside this region.
        for (int y = centerY - 24; y <= centerY + 24; y++) for (int x = centerX - 12; x <= centerX + 12; x++)
        {
            Color a = empty.GetPixel(x, y), b = far.GetPixel(x, y);
            if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > .02f) changed++;
        }
        GD.Print($"Far pillar changed target pixels: {changed} at {centerX},{centerY}.");
        Check(changed > 5, "Native real-map rendering visibly draws the far pillar after the item model is culled");
        ClearGround(); Call("RefreshGroundLootVisuals");
        Player.GlobalPosition = originalPosition; Player.Camera.GlobalTransform = originalCamera;
        crosshair.Visible = crosshairVisible;
        PillarRenderer.SetAnimationTime(null);
    }
    private async Task Capture(string name)
    {
        Set("_noticeTimer", 0f); Field<Label>("_noticeLabel").Hide();
        await Frames(3); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://.tmp/ground-loot-validation/native");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        Check(GetViewport().GetTexture().GetImage().SavePng(directory + "/" + name) == Error.Ok, "Native screenshot saved: " + name);
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren()) foreach (Node descendant in Descendants(child)) yield return descendant;
    }
    private async Task Frames(int count) { for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException("Ground loot validation failed: " + description);
        _checks++; GD.Print("PASS " + description);
    }
}

