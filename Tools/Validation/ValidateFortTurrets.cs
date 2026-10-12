using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateFortTurrets : Node
{
    private FpsVerticalSlice _map = null!;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int _checks;
    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\','/').Contains("/.tmp/fort-turret-validation/"), "Isolated turret validation save directory");
            _map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            AddChild(_map); await Frames(5);
            _map.SetProcess(false); _map.SetPhysicsProcess(false);
            FpsPlayerController player = Field<FpsPlayerController>("_player");
            Set("_defenseCoins", 2000);
            player.GlobalPosition = (Vector3)Call("ResolveDefenseGroundPosition", _map.DefenseSettings!.ShopPosition)!;
            Call("OpenDefenseShop");
            Check(Field<bool>("_shopOpen"), "Real field shop opens at its raised anchor");
            List<EquipmentItem> offers = Field<List<EquipmentItem>>("_shopShapes");
            EquipmentItem square = offers.First(shape => shape.Shape!.AttackType == WeaponAttackType.Single);
            Check(offers.Contains(square), "Every stronghold restock supplies a Square");
            Press("Buy Square");
            Check(_map.GetInventoryShapes().Any(item => ReferenceEquals(item,square)) && !offers.Contains(square), "Actual shape purchase transfers the offered item into run ownership");
            Press("Unlock North mount");
            Check(_map.GetTurretHealth(0) == _map.TurretMaximumHealth, "Actual shop unlock initializes independent turret health");
            WeaponModulatorData part = new() { Key = "turret-test-damage", DisplayName = "Turret damage fixture", Effects = new() { new() { Type = ModulatorEffectType.DamageMultiplier, Magnitude = 1.5f } } };
            square.Modulators.Add(part); StatGenerator.RecalculateDerivedStats(square);
            Check(_map.TryMountTurretShape(0,square,out _), "Owned Square with a supported installed modulator mounts");
            Check(ReferenceEquals(_map.GetMountedTurretShape(0),square) && ReferenceEquals(square.Modulators[0],part) && !_map.GetInventoryShapes().Contains(square), "Mount transfers exact shape and installed modulator references without duplication");
            Check(!_map.TryStoreRunShape(square,out _) && !_map.TryStoreRunModulator(part,out _), "Mounted shape and installed part cannot also enter player storage");
            Check(_map.EquippedShapeCount >= 1, "Mount retains a player weapon");
            Set("_pauseRequested",true); _map._PhysicsProcess(1f/60f);
            object pausedTurret = Field<Array>("_fortTurrets").GetValue(0)!;
            Check(pausedTurret.GetType().GetField("ShapeVisual")!.GetValue(pausedTurret) is Node3D && Field<System.Collections.ICollection>("_fortTurretBullets").Count==0,"Paused physics updates the mounted model without firing turret bullets");
            Set("_pauseRequested",false);
            if (OS.GetCmdlineUserArgs().Contains("--capture")) await Capture();
            List<EquipmentItem> active = Field<List<EquipmentItem>>("_arsenal");
            if (active.Count == 1) Check(!_map.TryMountTurretShape(1,active[0],out _), "Player's last held shape cannot be transferred");
            square.Modulators.Add(new() { Key = "turret-test-proc", DisplayName = "Unsupported proc", Effects = new() { new() { Type = ModulatorEffectType.EchoAfterDelay, Trigger = ModulatorEffectTrigger.OnFire } } });
            Check(!FpsVerticalSlice.IsTurretShapeSupported(square,out string why) && why.Contains("EchoAfterDelay"), "Unsupported installed proc is explicitly rejected");
            square.Modulators.RemoveAt(1);
            Set("_combatShopRemaining",100f);
            Check(_map.DebugSpawnHorde(1,true) == 1, "Real horde target spawned for autonomous turret projectile test");
            List<FpsHordeEnemySnapshot> snapshots = new(); _map.HordeManager!.CopyAlive(snapshots);
            _map.HordeManager.TrySetPosition(snapshots[0].Handle,new Vector3(28f,8f,-130f));
            _map.HordeManager.TryGet(snapshots[0].Handle,out FpsHordeEnemySnapshot before);
            float healthBefore = before.CurrentHealth, playerBefore = player.CurrentHealth;
            Array turrets = Field<Array>("_fortTurrets"); object turret = turrets.GetValue(0)!;
            turret.GetType().GetField("Cooldown")!.SetValue(turret,0f);
            turret.GetType().GetField("Acquire")!.SetValue(turret,0f);
            turret.GetType().GetField("QueryCursor")!.SetValue(turret,0);
            Call("TickFortTurrets",.01f);
            System.Collections.ICollection turretBullets = Field<System.Collections.ICollection>("_fortTurretBullets");
            int bulletsBeforeReset = turretBullets.Count;
            Call("ResetWeaponEffectRuntimeState");
            Check(bulletsBeforeReset > 0 && turretBullets.Count == bulletsBeforeReset, "Player weapon effect reset preserves independent turret bullets in flight");
            for (int step=0;step<120;step++) Call("TickFortTurrets",1f/60f);
            bool alive = _map.HordeManager.TryGet(before.Handle,out FpsHordeEnemySnapshot after);
            Check(!alive || after.CurrentHealth < healthBefore, "North turret acquires a real sector target and damages it with independent moving bullets");
            Check(Mathf.IsEqualApprox(playerBefore,player.CurrentHealth), "Turret firing does not heal or damage the player");
            _map.HillMapSettings!.SteppedStrongholdEnabled = false; Call("TickFortTurrets",1f/60f);
            Node3D visual = (Node3D)turret.GetType().GetField("Visual")!.GetValue(turret)!;
            Check(!visual.Visible && turretBullets.Count==0 && ReferenceEquals(_map.GetMountedTurretShape(0),square), "Disabling the stepped layout hides turret visuals and clears bullets while preserving equipment");
            _map.HillMapSettings.SteppedStrongholdEnabled = true; Call("TickFortTurrets",1f/60f);
            Check(visual.Visible && ReferenceEquals(_map.GetMountedTurretShape(0),square), "Reenabling the layout restores the original turret equipment and visual");
            turret.GetType().GetField("Health")!.SetValue(turret,123f);
            Call("BuildArena"); await Frames(2);
            Check(ReferenceEquals(_map.GetMountedTurretShape(0),square) && Mathf.IsEqualApprox(_map.GetTurretHealth(0),123f), "Live fort geometry rebuild preserves mounted ownership and wounded health");
            _map.DebugSpawnHorde(1,true); snapshots.Clear(); _map.HordeManager.CopyAlive(snapshots);
            _map.HordeManager.TrySetPosition(snapshots[0].Handle,new Vector3(28f,16f,-130f));
            _map.HordeManager.TrySetPosition(snapshots[0].Handle,new Vector3(28f,16f,-107f));
            _map.HordeManager.TrySetPosition(snapshots[0].Handle,new Vector3(28f,14.096f,-107f));
            turret.GetType().GetField("Health")!.SetValue(turret,1f);
            turret.GetType().GetField("Acquire")!.SetValue(turret,0f);
            Call("TickFortTurrets",1f/60f);
            Check(_map.GetTurretHealth(0)==0f && ReferenceEquals(_map.GetMountedTurretShape(0),square), "Nearby real enemy disables the mount while preserving its exact equipment");
            Call("RebuildDefenseShopMenu"); Press("Repair mount to full");
            Check(_map.GetTurretHealth(0)==_map.TurretMaximumHealth && ReferenceEquals(_map.GetMountedTurretShape(0),square), "Actual repair shop button reactivates the same mounted shape");
            List<EquipmentItem> stored = Field<List<EquipmentItem>>("_reserveShapes");
            while (active.Count < FpsVerticalSlice.EquippedShapeCapacity) active.Add(Shape());
            while (stored.Count < FpsVerticalSlice.ReserveShapeCapacity) stored.Add(Shape());
            Check(!_map.TryReturnTurretShape(0,out why) && why.Contains("full") && ReferenceEquals(_map.GetMountedTurretShape(0),square), "Full storage blocks return atomically and retains exact installed equipment");
            stored.RemoveAt(stored.Count-1);
            Check(_map.TryReturnTurretShape(0,out _) && _map.GetMountedTurretShape(0)==null && _map.GetInventoryShapes().Contains(square) && ReferenceEquals(square.Modulators[0],part), "Free storage returns the original shape with all installed parts");
            Set("_pauseRequested",true); _map._PhysicsProcess(1f/60f);
            Check(turret.GetType().GetField("ShapeVisual")!.GetValue(turret)==null,"Returning a shape removes its mounted model during paused physics");
            Call("ResetFortTurrets");
            Check(_map.GetMountedTurretShape(0)==null && _map.GetTurretHealth(0)==0f && !(bool)turret.GetType().GetField("Unlocked")!.GetValue(turret)!, "Run reset clears unlocks, health and mounted equipment");
            GD.Print($"Fort turret validation passed: {_checks} checks.");
            _map.QueueFree(); await Frames(3); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(2); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); _map?.QueueFree(); await Frames(3); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(2); GetTree().Quit(1); }
    }
    private EquipmentItem Shape()
    { EquipmentItem item = new() { Shape = EquipmentCatalogDefaults.CreateDefaultShapes().First(s=>s.AttackType==WeaponAttackType.Single), Rarity = EquipmentCatalogDefaults.CreateRarityByKey("common"), Level=1 }; StatGenerator.RecalculateDerivedStats(item); return item; }
    private void Press(string prefix)
    {
        Button button = Descendants(_map).OfType<Button>().First(b => b.Text.StartsWith(prefix,StringComparison.Ordinal) && !b.IsQueuedForDeletion());
        Check(!button.Disabled,$"Shop button {prefix} is enabled"); button.EmitSignal(Button.SignalName.Pressed);
    }
    private static IEnumerable<Node> Descendants(Node node)
    { foreach(Node child in node.GetChildren()) { yield return child; foreach(Node nested in Descendants(child)) yield return nested; } }
    private object? Call(string name,params object[] args) => typeof(FpsVerticalSlice).GetMethod(name,Private)!.Invoke(_map,args);
    private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name,Private)!.GetValue(_map)!;
    private void Set(string name,object value) => typeof(FpsVerticalSlice).GetField(name,Private)!.SetValue(_map,value);
    private void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); _checks++; GD.Print($"PASS {message}"); }
    private async Task Capture()
    {
        Call("TickFortTurrets",1f/60f);
        Call("CloseDefenseShop");
        Camera3D camera = new() { Fov=65f, Far=1000f };
        _map.AddChild(camera); camera.Current=true;
        camera.GlobalPosition = new Vector3(35f,19f,-117f); camera.LookAt(new Vector3(28f,16f,-107f));
        await Frames(4); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        string output = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),".tmp/fort-turret-validation/native/capture");
        System.IO.Directory.CreateDirectory(output);
        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"north-mounted-square.png"))==Error.Ok,"Native mounted shape visual captured");
        Call("OpenDefenseShop"); await Frames(3);
        ScrollContainer scroll = Descendants(_map).OfType<ScrollContainer>().Single(n=>n.Name=="DefenseShopScroll");
        Label label = Descendants(scroll).OfType<Label>().First(n=>n.Text.StartsWith("FORT TURRETS"));
        scroll.ScrollVertical = Mathf.Max(0,(int)(label.GlobalPosition.Y - scroll.GlobalPosition.Y) + scroll.ScrollVertical);
        await Frames(3); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"fort-turret-shop.png"))==Error.Ok,"Native real turret shop captured");
        camera.QueueFree(); Field<FpsPlayerController>("_player").Camera.Current=true;
    }
    private async Task Frames(int count) { for(int i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame); }
}
