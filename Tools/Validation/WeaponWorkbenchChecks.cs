using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

internal static class WeaponWorkbenchChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private delegate bool Attempt(out string reason);

    public static async Task Run(Node host, FpsVerticalSlice map, Action<bool, string> check)
    {
        T Field<T>(string key) => (T)typeof(FpsVerticalSlice).GetField(key, Private)!.GetValue(map)!;
        void Set(string key, object value) => typeof(FpsVerticalSlice).GetField(key, Private)!.SetValue(map, value);
        List<EquipmentItem> arsenal = Field<List<EquipmentItem>>("_arsenal");
        List<WeaponModulatorData> storage = Field<List<WeaponModulatorData>>("_recoveredModulators");
        EquipmentItem[] originalShapes = arsenal.ToArray();
        WeaponModulatorData[] originalStorage = storage.ToArray();
        int originalEquipped = map.EquippedWeaponIndex;
        EquipmentItem Item(string shape, bool locked = false)
        {
            EquipmentItem item = new()
            {
                Shape = EquipmentCatalogDefaults.CreateDefaultShapes().Single(value => value.Key == shape),
                Rarity = EquipmentCatalogDefaults.CreateRarityByKey(locked ? "common" : "divine"),
                Level = locked ? 1 : EquipmentCatalogDefaults.MaxWeaponLevel,
                Substats = new() { new(SubstatType.CritChance, -100f) },
            };
            StatGenerator.RecalculateDerivedStats(item);
            return item;
        }
        EquipmentItem square = Item("square"), triangle = Item("triangle"), locked = Item("square", true);
        WeaponModulatorData damage = ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Flawed);
        WeaponModulatorData cadence = ModulatorCatalog.CreateByKey("quickened_core", WeaponModulatorTier.Enhanced);
        WeaponModulatorData ballistic = ModulatorCatalog.CreateByKey("ballistic_tuning");
        WeaponModulatorData foreign = ModulatorCatalog.Clone(damage);
        string Snapshot() => JsonSerializer.Serialize(new
        {
            Equipped = map.EquippedWeaponIndex, Inspected = map.InspectedWeaponIndex,
            Stored = storage.Select(RuntimeHelpers.GetHashCode).ToArray(),
            Dense = arsenal.Select(item => item.Modulators.Select(RuntimeHelpers.GetHashCode).ToArray()).ToArray(),
            Slots = Enumerable.Range(0, arsenal.Count).Select(index => map.GetInventoryWeaponSlots(index)
                .Select(part => part == null ? 0 : RuntimeHelpers.GetHashCode(part)).ToArray()).ToArray(),
            Stats = Enumerable.Range(0, arsenal.Count).Select(map.GetInventoryWeaponStats).ToArray(),
            Controller = new { Field<FpsPlayerController>("_player").ShotDamage, Field<FpsPlayerController>("_player").MaxHealth,
                Field<FpsPlayerController>("_player").CurrentHealth },
        });
        void Reject(Attempt attempt, string description)
        {
            string before = Snapshot();
            bool success = attempt(out string reason);
            check(!success && !string.IsNullOrWhiteSpace(reason) && Snapshot() == before, "Atomic workbench rejection: " + description);
        }
        try
        {
            map.CloseWeaponInspection();
            arsenal.Clear(); arsenal.AddRange(new[] { square, triangle, locked });
            storage.Clear(); storage.AddRange(new[] { damage, cadence, ballistic });
            Set("_selectedWeaponIndex", 0);
            map._Input(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = true });
            check(map.IsWeaponInspectionOpen && Field<bool>("_pauseRequested"), "Tab opens the paused workbench");
            check(map.GetInventoryWeaponSlots(0).Count == 10 && map.GetInventoryWeaponSlots(-1).Count == 0,
                "Workbench exposes ten stable physical sockets and no slots for foreign shape indices");
            FpsInventoryWeaponStats baseline = map.GetInventoryWeaponStats(0)!;
            check(map.TryInstallInventoryModulator(square, damage, 2, out _) && square.Modulators.Count == 1 && storage.Count == 2 &&
                ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage), "Installing a loose Broken part transfers its exact reference to physical socket three");
            check(map.GetInventoryWeaponStats(0)!.Damage == StatGenerator.GetEffectiveDamage(square) &&
                map.GetInventoryWeaponStats(0)!.Damage > baseline.Damage && Field<FpsPlayerController>("_player").ShotDamage == map.GetInventoryWeaponStats(0)!.Damage,
                "Workbench effective damage agrees with the actual equipped controller after installation");
            Reject((out string reason) => map.TryInstallInventoryModulator(square, cadence, 2, out reason), "occupied socket");
            Reject((out string reason) => map.TryInstallInventoryModulator(square, foreign, 0, out reason), "foreign part with matching key and quality");
            Reject((out string reason) => map.TryInstallInventoryModulator(square, damage, 0, out reason), "stale already-transferred part");
            Reject((out string reason) => map.TryInstallInventoryModulator(square, cadence, 10, out reason), "out-of-range socket");
            check(map.TryInstallInventoryModulator(square, cadence, 0, out _) && storage.Count == 1,
                "A second loose part installs into an earlier empty physical socket");
            check(map.TryRemoveInventoryModulator(square, 0, out _) && storage.Count == 2 &&
                ReferenceEquals(storage[^1], cadence) && ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage) && map.GetInventoryWeaponSlots(0)[0] == null,
                "Removing an earlier socket returns its exact part and preserves a later socket's identity");
            check(Field<FpsPlayerController>("_player").FindChild("ModulatorSocket03", true, false)!.GetMeta("modulator_key").AsString() == damage.Key,
                "First-person factory retains the later installed cartridge in its physical socket after dense-list removal");
            Reject((out string reason) => map.TryRemoveInventoryModulator(square, 0, out reason), "empty socket removal");
            check(map.TryEquipInventoryShape(1), "Workbench immediately equips an owned alternate shape");
            Reject((out string reason) => map.TryInstallInventoryModulator(square, cadence, 1, out reason), "shape changed during an installation drag");
            Reject((out string reason) => map.TryRemoveInventoryModulator(square, 2, out reason), "shape changed during a removal drag");
            Reject((out string reason) => map.TryInstallInventoryModulator(triangle, ballistic, 0, out reason), "projectile-only part on a beam");
            check(map.TryEquipInventoryShape(2), "Workbench can select an owned locked-socket shape");
            Reject((out string reason) => map.TryInstallInventoryModulator(locked, cadence, 9, out reason), "locked physical socket");
            check(map.TryEquipInventoryShape(0), "Workbench restores the original owned shape");
            EquipmentItem foreignWeapon = Item("square"); foreignWeapon.InstanceId = square.InstanceId;
            Reject((out string reason) => map.TryInstallInventoryModulator(foreignWeapon, cadence, 1, out reason),
                "foreign weapon reference even with an identical saved instance ID");
            await WorkbenchInputFixture.Frames(host, 100);
            Control Find(string name) => host.GetTree().Root.FindChild(name, true, false) as Control
                ?? throw new InvalidOperationException("Workbench control is missing: " + name);
            FpsWeaponInspectionUI ui = Field<FpsWeaponInspectionUI>("_weaponInspectionUi");
            T Preview<T>(string name) => (T)typeof(FpsWeaponInspectionUI).GetField(name, Private)!.GetValue(ui)!;
            map.PreviewInspectedWeapon(1);
            await WorkbenchInputFixture.Frames(host, 100);
            check(map.EquippedWeaponIndex == 0 && map.InspectedWeaponIndex == 1 && !ui.IsTransforming,
                "An alternate shape can finish previewing without changing the equipped weapon");
            check(map.TryEquipInventoryShape(1) && ui.IsTransforming,
                "Equipping an already-previewed alternate shape still starts authored disassembly and assembly");
            await WorkbenchInputFixture.Frames(host, 100);
            check(!ui.IsTransforming && map.EquippedWeaponIndex == 1,
                "Same-preview equip finishes its authored transition with the alternate shape equipped");
            check(map.TryEquipInventoryShape(0), "Workbench restores Square after same-preview equip regression");
            await WorkbenchInputFixture.Frames(host, 100);
            FpsInventoryTile[] markers = Descendants(ui).OfType<FpsInventoryTile>().Where(control => control.Name.ToString().StartsWith("SocketDropTarget") && control.Visible).ToArray();
            check(markers.Length > 0 && markers.All(marker =>
            {
                int index = marker.GetMeta("slot_index").AsInt32();
                Node3D anchor = (Node3D)Preview<Node3D>("_gunVisual").FindChild($"ModulatorSocket{index + 1:00}", true, false)!;
                Camera3D camera = Preview<Camera3D>("_inspectionCamera");
                Vector2 expected = ((Control)marker.GetParent()).GetGlobalTransformWithCanvas().AffineInverse() *
                    camera.UnprojectPosition(anchor.GlobalPosition);
                return marker.GetRect().GetCenter().DistanceTo(expected) < 1f && marker.GetMeta("socket_state").AsString() == (map.GetInventoryWeaponSlots(0)[index] == null ? "empty" : "installed");
            }), "Visible world-model drop targets project their exact physical socket anchors through the player camera and retain sparse installed state");
            check(ReferenceEquals(Preview<Node3D>("_gunPivot").GetParent(), Preview<Camera3D>("_inspectionCamera")) &&
                Preview<SubViewport>("_gunViewport").RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
                "World inspection uses the player camera and disables the separate weapon preview render target");
            await WorkbenchInputFixture.Click(host, Find("SocketDock1"));
            Button socketAction = (Button)Find("SocketAction");
            check(Find("StoredModulator" + storage.FindIndex(part => ReferenceEquals(part, cadence))).IsVisibleInTree() && socketAction.Disabled,
                "Clicking an empty socket opens loose parts and keeps removal disabled");
            await WorkbenchInputFixture.Click(host, Find("StoredModulator" + storage.FindIndex(part => ReferenceEquals(part, cadence))));
            check(ReferenceEquals(map.GetInventoryWeaponSlots(0)[1], cadence) && storage.Count == 1 &&
                ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage),
                "Clicking a compatible loose part installs its exact reference into the selected physical socket without moving its neighbor");
            await WorkbenchInputFixture.Frames(host, 30);
            string beforeOccupiedClick = Snapshot();
            await WorkbenchInputFixture.Click(host, Find("StoredModulator" + storage.FindIndex(part => ReferenceEquals(part, ballistic))));
            check(Snapshot() == beforeOccupiedClick && !socketAction.Disabled,
                "Clicking another loose part cannot overwrite an occupied socket and exposes removal after seating");
            await WorkbenchInputFixture.Click(host, socketAction);
            check(map.GetInventoryWeaponSlots(0)[1] == null && storage.Count == 2 && ReferenceEquals(storage[^1], cadence) &&
                ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage),
                "The remove button returns the exact clicked part to storage and preserves the neighboring socket");
            await WorkbenchInputFixture.Frames(host, 30);
            ui.ShowModulatorInventory(true);
            await WorkbenchInputFixture.Frames(host, 2);
            FpsInventoryTile tile = (FpsInventoryTile)Find("StoredModulator" + storage.FindIndex(part => ReferenceEquals(part, cadence)));
            FpsInventoryDragData valid = tile.CreatePayload?.Invoke() ?? throw new InvalidOperationException("Workbench tile supplied no payload");
            Control socket = Find("SocketDock1");
            Variant validData = valid;
            check(socket._CanDropData(Vector2.Zero, validData), "Actual socket hover accepts the owned compatible storage payload");
            FpsWeaponInspectionUI otherUi = new();
            try
            {
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    using FpsInventoryDragData forged = new()
                    {
                        Owner = mode == 0 ? otherUi : valid.Owner, Session = mode == 1 ? valid.Session - 1 : valid.Session,
                        Weapon = valid.Weapon, Part = mode == 2 ? foreign : valid.Part, ArsenalIndex = valid.ArsenalIndex, SourceSlot = valid.SourceSlot,
                    };
                    Variant data = forged;
                    string before = Snapshot();
                    check(!socket._CanDropData(Vector2.Zero, data), "Socket hover rejects foreign owner, stale session or unowned part payload");
                    socket._DropData(Vector2.Zero, data);
                    check(Snapshot() == before, "Forged GUI drop leaves exact part references, physical slots and effective stats untouched");
                }
            }
            finally { otherUi.Free(); }
            if (DisplayServer.GetName() == "headless")
            {
                GD.Print("SKIP: OS hover and real GUI drag route require RunWeaponInspectionValidation.ps1 -Native; headless validates drop callbacks and exact transfers.");
                socket._DropData(Vector2.Zero, validData);
                check(ReferenceEquals(map.GetInventoryWeaponSlots(0)[1], cadence) && storage.Count == 1 &&
                    ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage),
                    "Headless owned GUI drop callback transfers exactly one part into its physical socket");
                await WorkbenchInputFixture.Frames(host, 30);
                FpsInventoryDragData removal = ((FpsInventoryTile)Find("SocketDock1")).CreatePayload?.Invoke()
                    ?? throw new InvalidOperationException("Installed socket supplied no removal payload");
                Control destination = Find("InventoryDropTarget");
                Variant removalData = removal;
                check(destination._CanDropData(Vector2.Zero, removalData), "Headless storage hover contract accepts an owned installed part");
                destination._DropData(Vector2.Zero, removalData);
                check(map.GetInventoryWeaponSlots(0)[1] == null && storage.Count == 2 && ReferenceEquals(storage[^1], cadence) &&
                    ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage),
                    "Headless removal callback restores the exact part without moving its neighbor");
            }
            else
            {
            string beforeCancel = Snapshot();
            await WorkbenchInputFixture.Drag(host, Find("StoredModulator" + storage.FindIndex(part => ReferenceEquals(part, cadence))), Find("SocketDock1"), true);
            ((LineEdit)Find("InventorySearch")).GrabFocus();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = true });
            Input.FlushBufferedEvents();
            WorkbenchInputFixture.Release(socket.GetGlobalRect().GetCenter(), host);
            await WorkbenchInputFixture.Frames(host, 2);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = false });
            Input.FlushBufferedEvents();
            check(!map.IsWeaponInspectionOpen && !host.GetViewport().GuiIsDragging() && Snapshot() == beforeCancel,
                "Tab closes a focused workbench during an actual drag and cancels it without transferring an item");
            map._Input(new InputEventKey { Keycode = Key.Tab, PhysicalKeycode = Key.Tab, Pressed = true });
            // Let the .28-second inspection raise finish before measuring an assembly's fixed core.
            await WorkbenchInputFixture.Frames(host, 20);
            string beforeStaleSession = Snapshot();
            socket._DropData(Vector2.Zero, validData);
            check(Snapshot() == beforeStaleSession, "Reopening the workbench rejects a payload from the previous actual session");
            ui.ShowModulatorInventory(false);
            await WorkbenchInputFixture.Frames(host, 2);
            await WorkbenchInputFixture.Drag(host, Find("ShapeChoice1"), Find("WeaponDropTarget"),
                diagnosticContext: () => $"equipped={map.EquippedWeaponIndex} inspected={map.InspectedWeaponIndex} transforming={ui.IsTransforming} session={ui.DragSession}");
            check(map.EquippedWeaponIndex == 1 && Field<FpsPlayerController>("_player").FindChild("EquippedHandgun", true, false)!.GetMeta("shape_key").AsString() == "triangle",
                "Actual GUI shape drag equips and updates the first-person imported model");
            Vector2 CoreScreenPoint()
            {
                Node3D core = (Node3D)Preview<Node3D>("_gunVisual").FindChild("ShapeSocket", true, false)!;
                return Preview<Camera3D>("_inspectionCamera").UnprojectPosition(core.GlobalPosition);
            }
            Vector2 fixedCore = CoreScreenPoint();
            float maxDrift = 0f;
            string CoreDiagnostic() => $"screen={CoreScreenPoint()} pivot={Preview<Node3D>("_gunPivot").Position} fov={Preview<Camera3D>("_inspectionCamera").Fov} raise={Preview<float>("_raiseProgress")} shape={Preview<Node3D>("_gunVisual").GetMeta("shape_key").AsString()}";
            string driftDiagnostic = CoreDiagnostic();
            for (int sample = 0; sample < 9; sample++)
            {
                await WorkbenchInputFixture.Frames(host, 10);
                float drift = CoreScreenPoint().DistanceTo(fixedCore);
                if (drift > maxDrift) { maxDrift = drift; driftDiagnostic += $"; sample {sample}: {CoreDiagnostic()}"; }
            }
            check(maxDrift < 2f, $"Core projection remains stable throughout actual disassembly, model replacement and assembly (maximum drift {maxDrift:F3}px; {driftDiagnostic})");
            await WorkbenchInputFixture.Frames(host, 100);
            await WorkbenchInputFixture.Drag(host, Find("ShapeChoice0"), Find("WeaponDropTarget"));
            await WorkbenchInputFixture.Frames(host, 100);
            ui.ShowModulatorInventory(true);
            await WorkbenchInputFixture.Frames(host, 2);
            int cadenceIndex = storage.FindIndex(part => ReferenceEquals(part, cadence));
            await WorkbenchInputFixture.Drag(host, Find("StoredModulator" + cadenceIndex), Find("SocketDock1"));
            check(ReferenceEquals(map.GetInventoryWeaponSlots(0)[1], cadence) && ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage) && storage.Count == 1,
                "Actual GUI modulator drag installs into the empty physical socket without moving its neighbor");
            await WorkbenchInputFixture.Frames(host, 30);
            await WorkbenchInputFixture.Drag(host, Find("SocketDock1"), Find("InventoryDropTarget"));
            check(map.GetInventoryWeaponSlots(0)[1] == null && ReferenceEquals(map.GetInventoryWeaponSlots(0)[2], damage) && storage.Count == 2,
                "Actual GUI socket-to-storage drag returns one part and leaves other sockets stable");
            }
            map.CloseWeaponInspection();
            Reject((out string reason) => map.TryInstallInventoryModulator(square, cadence, 1, out reason), "closed inventory installation");
            Reject((out string reason) => map.TryRemoveInventoryModulator(square, 2, out reason), "closed inventory removal");
            check(!map.TryEquipInventoryShape(1), "Closed inventory cannot equip a shape through a stale callback");
            check(!Preview<Node3D>("_gunPivot").IsVisibleInTree(), "Closing inventory hides the camera-attached inspection geometry");
            foreach (SubViewport viewport in Descendants(Field<Node>("_weaponInspectionUi")).OfType<SubViewport>())
                check(viewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled, "Closing inventory disables its viewport rendering");
        }
        finally
        {
            map.CloseWeaponInspection();
            arsenal.Clear(); arsenal.AddRange(originalShapes);
            storage.Clear(); storage.AddRange(originalStorage);
            Set("_selectedWeaponIndex", originalEquipped);
            typeof(FpsVerticalSlice).GetMethod("EquipWeapon", Private)!.Invoke(map, new object[] { originalEquipped, false });
        }
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
    }
}
