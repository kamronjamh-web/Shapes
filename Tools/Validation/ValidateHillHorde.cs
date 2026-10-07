using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Exercises the real scene and physics; never writes the player's normal save directory.
public partial class ValidateHillHorde : Node
{
	private int _checks;
	private FpsVerticalSlice _map = null!;
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/hill-horde-validation/"), "Validation uses isolated save data");
			await Run();
			await Unload();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			await PhysicsFrames(2);
			GD.Print($"Hill horde validation passed: {_checks} checks.");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}

	private async Task Run()
	{
		await Load("res://Scenes/Fps/NorthGate.tscn");
		FpsDefenseSettings defense = _map.DefenseSettings!;
		FpsHillMapSettings hill = _map.HillMapSettings!;
		FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		Check(_map.IsHillMap && Mathf.IsEqualApprox(hill.PlayableSize, 200f), "North Gate uses the 200 metre hill map");
		Check(Mathf.IsEqualApprox(hill.SummitHeight, 8f) && Mathf.IsEqualApprox(defense.CorePosition.Y, 8f) && Mathf.IsEqualApprox(defense.ShopPosition.Y, 8f), "Core and shop occupy the eight metre summit");
		Check(Mathf.IsEqualApprox(core.Position.Y, 9.6f), "The physical core rests above its summit floor");
		Check(defense.Lanes.Count(lane => lane.Enabled) == 1 && defense.Lanes.First(lane => lane.Enabled).Direction == FpsLaneDirection.North, "Only the north approach is enabled");
		Check(core.CurrentHealth == core.MaxHealth && Field<bool>("_waitingForNextWave"), "Preparation starts with a healthy core");
		Vector3 northFoot = new(0f, 0f, -hill.HillFootHalfSize - 4f);
		Check(Mathf.Abs(FloorHeight(northFoot)) < 0.05f && Mathf.Abs(FloorHeight(new Vector3(14f, 0f, 14f)) - 8f) < 0.05f, "Real terrain collision reaches ground at the foot and eight metres at the summit");
		float room = hill.RoomHalfSize;
		Check(!RayHits(new Vector3(0f, 10f, -room - 2f), new Vector3(0f, 10f, -room + 2f)), "The north room doorway admits a physical ray");
		Check(RayHits(new Vector3(room - 2f, 10f, -room - 2f), new Vector3(room - 2f, 10f, -room + 2f)), "The room wall blocks a physical ray beside the doorway");
		Check(RayHits(new Vector3(98f, 2f, 70f), new Vector3(102f, 2f, 70f)), "Playable terrain has physical boundary walls");
		player.Position = defense.ShopPosition;
		KeyPress(Key.F);
		Check(Field<bool>("_shopOpen"), "The summit shop opens with F");
		float timer = Field<float>("_nextWaveDelayTimer");
		await PhysicsFrames(5);
		Check(Mathf.IsEqualApprox(timer, Field<float>("_nextWaveDelayTimer")), "Preparation shopping holds the wave clock");
		KeyPress(Key.Escape);
		await ValidateCrowd(defense, hill, core, player);
		await ValidateFallback();
	}

	private async Task ValidateCrowd(FpsDefenseSettings defense, FpsHillMapSettings hill, FpsDefenseCore core, FpsPlayerController player)
	{
		float originalWeaponDrop = defense.WeaponDropChance, originalModulatorDrop = defense.ModulatorDropChance;
		int originalPickupLimit = defense.MaxLoosePickups;
		FpsHordeManager crowd = _map.HordeManager ?? throw new InvalidOperationException("Missing custom crowd manager");
		Check(crowd.ActiveCount == 0, "Preparation owns an empty custom crowd manager");
		int deaths = 0;
		HashSet<FpsHordeHandle> died = new();
		crowd.EnemyDied += enemy => { deaths++; died.Add(enemy.Handle); };
		KeyPress(Key.N);
		await PhysicsFrames(3);
		List<FpsHordeEnemySnapshot> enemies = Alive(crowd);
		Check(enemies.Count == 100 && crowd.ActiveCount == 100 && crowd.RenderedInstanceCount == 100, "The wave spawns and renders exactly one hundred live shapes");
		Check(Field<List<FpsSliceEnemy>>("_enemies").Count == 0 && Descendants(_map).OfType<CharacterBody3D>().Count() == 1, "The defense wave creates no per-enemy CharacterBody nodes");
		Check(Descendants(crowd).OfType<MultiMeshInstance3D>().Any(), "Crowd rendering uses MultiMesh instances");
		Check(enemies.All(enemy => enemy.Position.Z < -70f && Mathf.Abs(enemy.Position.X) < 40f && enemy.Position.Y < 1f), "All one hundred shapes spawn at the north hill foot");
		FpsHordeEnemySnapshot tracked = enemies.First();
		Vector3 initial = tracked.Position;
		var sample = Stopwatch.StartNew();
		await PhysicsFrames(120);
		sample.Stop();
		GD.Print($"Horde diagnostic: 120 headless physics frames with {crowd.ActiveCount} live shapes took {sample.Elapsed.TotalMilliseconds:0.0} ms wall time; this is not a rendered FPS measurement.");
		Check(crowd.TryGet(tracked.Handle, out tracked) && tracked.Position.Z > initial.Z + 1f, "Live crowd simulation advances toward the summit");
		for (int block = 0; block < 30 && !Alive(crowd).Any(enemy => enemy.Position.Y > 2f); block++) await PhysicsFrames(60);
		Check(Alive(crowd).Any(enemy => enemy.Position.Y > 2f), "Shapes physically progress up the hill surface");
		Check(Alive(crowd).All(enemy => Mathf.Abs(enemy.Position.Y - _map.SampleHillSurfaceHeight(enemy.Position)) < 0.1f), "Crowd feet remain on the hill height surface");
		KeyPress(Key.Escape);
		Dictionary<FpsHordeHandle, FpsHordeEnemySnapshot> paused = Alive(crowd).ToDictionary(enemy => enemy.Handle);
		await PhysicsFrames(90);
		Check(Field<bool>("_pauseRequested") && Alive(crowd).All(enemy => paused[enemy.Handle].Position == enemy.Position), "Pausing with Escape freezes every live crowd position");
		float originalSummit = hill.SummitHeight;
		float coreBeforeTuning = core.CurrentHealth;
		try
		{
			hill.SummitHeight = 9f;
			await PhysicsFrames(5);
			Check(crowd.ActiveCount == 100 && Alive(crowd).All(enemy => paused.TryGetValue(enemy.Handle, out FpsHordeEnemySnapshot before) && before.CurrentHealth == enemy.CurrentHealth) && core.CurrentHealth == coreBeforeTuning, "Live hill height tuning preserves every enemy identity and health plus core health");
			Check(Mathf.IsEqualApprox(core.Position.Y, 10.6f) && Mathf.IsEqualApprox(Field<Node3D>("_shopMarker").Position.Y, 9f) && player.Position.Y >= 9f && player.Position.Y < 9.2f, "Live summit tuning raises the core, shop, and defender onto the new floor");
		}
		finally { hill.SummitHeight = originalSummit; }
		await PhysicsFrames(5);
		KeyPress(Key.Escape);
		await PhysicsFrames(5);
		bool InsideRoom(FpsHordeEnemySnapshot enemy) => Mathf.Abs(enemy.Position.X) < hill.RoomHalfSize - 1f && enemy.Position.Z > -hill.RoomHalfSize + 1f;
		for (int block = 0; block < 90 && !Alive(crowd).Any(InsideRoom); block++) await PhysicsFrames(60);
		Check(Alive(crowd).Any(InsideRoom), "Unmodified north spawns naturally climb and enter the summit room");
		Vector3 doorFrom = new(0f, 8f, -hill.RoomHalfSize - 2f), doorTo = new(0f, 8f, -hill.RoomHalfSize + 2f);
		Check(_map.ConstrainHillCrowdMovement(doorFrom, doorTo, 0.5f).DistanceTo(doorTo) < 0.05f, "Custom movement allows passage through the north doorway");
		Vector3 wallFrom = new(hill.RoomHalfSize - 2f, 8f, -hill.RoomHalfSize - 2f), wallTo = new(hill.RoomHalfSize - 2f, 8f, -hill.RoomHalfSize + 2f);
		Check(_map.ConstrainHillCrowdMovement(wallFrom, wallTo, 0.5f).DistanceTo(wallTo) > 0.5f, "Custom movement blocks crossing a solid room wall");

		// Relocate two real records onto a clear flat test firing line; no mocked targets.
		enemies = Alive(crowd);
		FpsHordeHandle front = enemies[0].Handle, rear = enemies[1].Handle;
		void PlaceOnFiringLine(FpsHordeHandle handle, Vector3 destination)
		{
			// Naturally piled records may be inside the room. Stage over its roof so the
			// wall-constrained relocation API does not leave the firing fixture at a wall.
			crowd.TryGet(handle, out FpsHordeEnemySnapshot current);
			float aboveRoof = hill.SummitHeight + hill.RoomWallHeight + 3f;
			crowd.TrySetPosition(handle, new Vector3(current.Position.X, aboveRoof, current.Position.Z));
			crowd.TrySetPosition(handle, new Vector3(destination.X, aboveRoof, destination.Z));
			crowd.TrySetPosition(handle, destination);
		}
		PlaceOnFiringLine(front, new Vector3(60f, 0f, -76f));
		PlaceOnFiringLine(rear, new Vector3(60f, 0f, -82f));
		crowd.TryGet(front, out FpsHordeEnemySnapshot frontBefore);
		crowd.TryGet(rear, out FpsHordeEnemySnapshot rearBefore);
		EquipmentItem weapon = (EquipmentItem)Invoke("GetCurrentWeapon")!;
		weapon.Modulators.Clear();
		weapon.Modulators.Add(ModulatorCatalog.CreateByKey("piercing_edge"));
		Vector3 origin = new(60f, frontBefore.HeadPosition.Y, -70f);
		Invoke("FireSingleShot", weapon, origin, Vector3.Forward, 0.05f);
		Check(crowd.TryGet(front, out FpsHordeEnemySnapshot frontAfter) && frontAfter.CurrentHealth < frontBefore.CurrentHealth && crowd.TryGet(rear, out FpsHordeEnemySnapshot rearAfter) && rearAfter.CurrentHealth < rearBefore.CurrentHealth, "Square piercing damages two real custom crowd records");
		float beforeArea = frontAfter.CurrentHealth;
		Invoke("DamageEnemiesInRadius", frontAfter.Position, 1f, 1f, weapon, false);
		crowd.TryGet(front, out frontAfter);
		Check(frontAfter.CurrentHealth < beforeArea, "Weapon area damage reaches custom crowd enemies");
		// A world wall must stop the director's custom ray before reaching the target.
		crowd.TrySetPosition(front, doorFrom);
		crowd.TrySetPosition(front, doorTo);
		crowd.TrySetPosition(front, new Vector3(hill.RoomHalfSize - 2f, 8f, -hill.RoomHalfSize + 2f));
		crowd.TryGet(front, out frontBefore);
		origin = new Vector3(hill.RoomHalfSize - 2f, frontBefore.HeadPosition.Y, -hill.RoomHalfSize - 2f);
		Invoke("FireSingleShot", weapon, origin, Vector3.Back, 0.05f);
		crowd.TryGet(front, out frontAfter);
		Check(frontAfter.CurrentHealth == frontBefore.CurrentHealth, "Custom hitscan respects room wall occlusion");

		defense.WeaponDropChance = 1f;
		defense.ModulatorDropChance = 1f;
		defense.MaxLoosePickups = 8;
		foreach (FpsHordeEnemySnapshot enemy in Alive(crowd).Take(8)) crowd.ApplyDamage(enemy.Handle, 100000f);
		crowd.FlushDeathEvents();
		Invoke("SweepDefenseLoot");
		Check(deaths == 8 && died.Count == 8 && Field<int>("_defenseCoins") == 8 * defense.CoinsPerEnemy, "Eight custom deaths award currency once and notify once");
		Check(Field<List<EquipmentItem>>("_arsenal").Count == 9 && Field<List<WeaponModulatorData>>("_recoveredModulators").Count == 8, "Custom crowd kills produce usable weapon and modulator drops");
		Check(Field<float>("_combatShopRemaining") > 0f, "Custom crowd kills open a combat shop window");
		player.Position = defense.ShopPosition;
		KeyPress(Key.F);
		tracked = Alive(crowd).First();
		initial = tracked.Position;
		await PhysicsFrames(20);
		crowd.TryGet(tracked.Handle, out tracked);
		Check(Field<bool>("_shopOpen") && tracked.Position.DistanceSquaredTo(initial) > 0.01f, "Crowd advances while the combat shop is open");
		Click("Upgrade current shape");
		Check(weapon.Level == 2 && Field<int>("_defenseCoins") == 8 * defense.CoinsPerEnemy - defense.WeaponUpgradePrice, "Combat shopping upgrades the run weapon and spends currency");
		KeyPress(Key.Escape);
		defense.WeaponDropChance = 0f;
		defense.ModulatorDropChance = 0f;
		tracked = Alive(crowd).First();
		crowd.TrySetPosition(tracked.Handle, doorFrom);
		crowd.TrySetPosition(tracked.Handle, doorTo);
		crowd.TrySetPosition(tracked.Handle, defense.CorePosition + Vector3.Forward * 1.8f);
		float coreHealth = core.CurrentHealth;
		await PhysicsFrames(90);
		Check(core.CurrentHealth < coreHealth, "A live custom crowd enemy threatens and damages the core");
		foreach (FpsHordeEnemySnapshot enemy in Alive(crowd)) crowd.ApplyDamage(enemy.Handle, 100000f);
		crowd.FlushDeathEvents();
		await PhysicsFrames(3);
		Check(deaths == 100 && died.Count == 100 && crowd.ActiveCount == 0 && crowd.RenderedInstanceCount == 0, "All one hundred deaths notify once and remove every live render slot");
		Check(Field<int>("_defenseCoins") == 100 * defense.CoinsPerEnemy - defense.WeaponUpgradePrice, "Complete horde currency accounts for all one hundred deaths exactly once");
		Check(Field<bool>("_victory") && !Field<bool>("_defeat") && core.IsAlive, "Clearing the single horde wave wins with the core alive");
		crowd.ApplyDamage(front, 100000f);
		crowd.FlushDeathEvents();
		Check(deaths == 100, "Stale crowd handles cannot repeat death notifications");
		defense.WeaponDropChance = originalWeaponDrop;
		defense.ModulatorDropChance = originalModulatorDrop;
		defense.MaxLoosePickups = originalPickupLimit;
		await ValidateDenseAreaDeath();
		await Load("res://Scenes/Fps/NorthGate.tscn");
		core = Field<FpsDefenseCore>("_defenseCore");
		core.ApplyDamage(core.MaxHealth + 1f);
		Check(Field<bool>("_defeat") && !Field<bool>("_victory"), "Core destruction still ends the hill defense in defeat");
	}

	private async Task ValidateDenseAreaDeath()
	{
		await Load("res://Scenes/Fps/NorthGate.tscn");
		FpsHordeManager crowd = _map.HordeManager!;
		int deaths = 0;
		HashSet<FpsHordeHandle> handles = new();
		crowd.EnemyDied += enemy => { deaths++; handles.Add(enemy.Handle); };
		KeyPress(Key.N);
		await PhysicsFrames(3);
		Check(crowd.ActiveCount == 100, "Dense area regression begins with a fresh hundred-enemy wave");
		int hitCount = (int)Invoke("DamageEnemiesInRadius", Vector3.Zero, 150f, 100000f, null!, false)!;
		crowd.FlushDeathEvents();
		await PhysicsFrames(3);
		Check(hitCount == 100 && deaths == 100 && handles.Count == 100 && crowd.ActiveCount == 0 && crowd.RenderedInstanceCount == 0, "One area attack damages and retires all hundred targets without mutation skips");
		Check(Field<int>("_defenseCoins") == 100 * _map.DefenseSettings!.CoinsPerEnemy && Field<bool>("_victory"), "A single area clear awards all configured currency and wins the wave");
	}

	private static List<FpsHordeEnemySnapshot> Alive(FpsHordeManager manager)
	{
		List<FpsHordeEnemySnapshot> enemies = new();
		manager.CopyAlive(enemies);
		return enemies;
	}
	private static IEnumerable<Node> Descendants(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			yield return child;
			foreach (Node descendant in Descendants(child)) yield return descendant;
		}
	}

	private async Task ValidateFallback()
	{
		await Load("res://Scenes/Fps/FpsVerticalSlice.tscn");
		List<FpsSliceEnemy> enemies = Field<List<FpsSliceEnemy>>("_enemies");
		Check(!_map.IsDefenseMap && enemies.Count == 3, "The original FPS sandbox retains its three body enemies");
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		FpsSliceEnemy front = enemies[0], rear = enemies[1];
		front.Position = player.Position + Vector3.Forward * 8f;
		rear.Position = player.Position + Vector3.Forward * 14f;
		await PhysicsFrames(3);
		EquipmentItem weapon = (EquipmentItem)Invoke("GetCurrentWeapon")!;
		weapon.Modulators.Clear();
		weapon.Modulators.Add(ModulatorCatalog.CreateByKey("piercing_edge"));
		float frontHealth = front.CurrentHealth, rearHealth = rear.CurrentHealth;
		Invoke("FireSingleShot", weapon, player.HeadPosition, (front.HeadPosition - player.HeadPosition).Normalized(), 1f);
		GD.Print($"Legacy ray diagnostic: shape {weapon.Shape?.Key}, front {frontHealth}->{front.CurrentHealth} at {front.HeadPosition}, rear {rearHealth}->{rear.CurrentHealth} at {rear.HeadPosition}, origin {player.HeadPosition}");
		Check(front.CurrentHealth < frontHealth && rear.CurrentHealth < rearHealth, "Legacy Square piercing still damages both body enemies");
		await Unload();
	}

	private float FloorHeight(Vector3 point)
	{
		var hit = _map.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(point.X, 15f, point.Z), new Vector3(point.X, -2f, point.Z), 1));
		Check(hit.Count > 0, $"Terrain collision exists at {point.X}, {point.Z}");
		GD.Print($"Terrain ray {point}: height {((Vector3)hit["position"]).Y}, collider {hit["collider"]}");
		return ((Vector3)hit["position"]).Y;
	}
	private bool RayHits(Vector3 start, Vector3 end) => _map.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(start, end, 1)).Count > 0;
	private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, PrivateInstance)!.GetValue(_map)!;
	private object? Invoke(string name, params object[] arguments) => typeof(FpsVerticalSlice).GetMethod(name, PrivateInstance)!.Invoke(_map, arguments);
	private void KeyPress(Key key) => _map._UnhandledInput(new InputEventKey { Keycode = key, Pressed = true });
	private void Click(string prefix)
	{
		Button button = Field<VBoxContainer>("_shopContent").GetChildren().OfType<Button>().First(button => button.Text.StartsWith(prefix, StringComparison.Ordinal));
		Check(!button.Disabled, $"Shop action available: {prefix}");
		button.EmitSignal(Button.SignalName.Pressed);
	}
	private async Task Load(string path)
	{
		if (_map != null) await Unload();
		_map = GD.Load<PackedScene>(path).Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(_map);
		AddChild(_map);
		await PhysicsFrames(4);
	}
	private async Task Unload()
	{
		FpsVerticalSlice? map = _map;
		_map = null!;
		if (!GodotObject.IsInstanceValid(map)) return;
		if (map!.GetParent() == this) RemoveChild(map);
		map.QueueFree();
		await PhysicsFrames(3);
	}
	private async Task PhysicsFrames(int count)
	{
		for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
	private void Check(bool condition, string description)
	{
		if (!condition) throw new InvalidOperationException(description);
		_checks++;
		GD.Print($"PASS: {description}");
	}
}
