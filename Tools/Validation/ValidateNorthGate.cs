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

// Integration checks run against the actual Godot scene, physics, input and shop controls.
public partial class ValidateNorthGate : Node
{
	private int _checks;
	private FpsVerticalSlice _map = null!;
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/north-gate-validation/"), "Validation uses isolated save data");
			await Run();
			GD.Print($"North Gate validation passed: {_checks} checks.");
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
		_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGateLaneSandbox.tscn").Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(_map);
		AddChild(_map);
		await Frames(3);
		FpsDefenseSettings settings = _map.DefenseSettings!;
		Check(settings.Lanes.Count == 4 && settings.Lanes.Count(lane => lane.Enabled) == 1 && settings.Lanes[0].Direction == FpsLaneDirection.North, "Map starts with only the north lane active");
		FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		Check(core.CurrentHealth == settings.CoreMaxHealth && Enemies().Count == 0, "Preparation begins with a full core and no threats");
		Check(_map.GetNode("ArenaGeometry/NorthLaneFloor") != null && _map.GetNode("ArenaGeometry/EastSealedWall") != null, "North approach and closed cardinal gates are instantiated");

		player.Position = settings.ShopPosition;
		KeyPress(Key.F);
		Check(Field<bool>("_shopOpen"), "Preparation shop opens through the interaction key");
		float timer = Field<float>("_nextWaveDelayTimer");
		await Frames(5);
		Check(Mathf.IsEqualApprox(timer, Field<float>("_nextWaveDelayTimer")), "Shopping during preparation holds the wave timer");
		KeyPress(Key.Escape);
		Check(!Field<bool>("_shopOpen") && !Field<bool>("_pauseRequested"), "Escape closes the shop and returns to defense");
		KeyPress(Key.N);
		await Frames(3);
		Check(Enemies().Count == 12 && Field<int>("_currentWaveNumber") == 1, "First wave starts with twelve threats");
		Check(Enemies().All(enemy => enemy.Position.Z < -24f && Mathf.Abs(enemy.Position.X) < 10f), "All first-wave enemies spawn inside the north lane");
		FpsSliceEnemy pyramid = Enemies().First(enemy => enemy.Role == FpsEnemyRole.Striker);
		CylinderMesh pyramidMesh = (CylinderMesh)pyramid.GetChildren().OfType<MeshInstance3D>().First().Mesh;
		Check(pyramidMesh.RadialSegments == 4 && pyramidMesh.TopRadius == 0f, "Ranged enemies use a pyramid silhouette");
		_map.TryGetDefenseTarget(new Vector3(0f, 0f, -55f), out _, out bool targetsCore);
		Check(targetsCore, "Distant enemies advance toward the core");
		_map.TryGetDefenseTarget(player.Position + Vector3.Right, out _, out targetsCore);
		Check(!targetsCore, "Nearby defenders can draw enemy attention");

		float coreHealth = core.CurrentHealth;
		_map.SpawnEnemyProjectile(core.GlobalPosition + Vector3.Forward * 4f, Vector3.Back, 23f, 7f, pyramid.GetRid());
		await Frames(20);
		Check(core.CurrentHealth < coreHealth, "Enemy projectiles collide with and damage the core");
		FpsSliceEnemy cube = Enemies().First(enemy => enemy.Role == FpsEnemyRole.Bruiser);
		Vector3 cubeSpawn = cube.Position;
		cube.Position = settings.CorePosition + Vector3.Forward * 1.9f;
		coreHealth = core.CurrentHealth;
		await Frames(5);
		Check(core.CurrentHealth < coreHealth, "Melee shapes can reach and damage the core");
		cube.Position = cubeSpawn;

		// Guarantee both rare drop paths so the economy test is repeatable.
		settings.WeaponDropChance = 1f;
		settings.ModulatorDropChance = 1f;
		settings.MaxLoosePickups = 8;
		foreach (FpsSliceEnemy enemy in Enemies().Take(8).ToArray()) enemy.ApplyDamage(100000f);
		Check(Field<float>("_combatShopRemaining") > 0f && !Field<bool>("_waitingForNextWave"), "A shop appears during an active wave after enough kills");
		Invoke("SweepDefenseLoot");
		Check(Field<int>("_defenseCoins") == 8 * settings.CoinsPerEnemy, "Enemy deaths award the configured currency exactly once");
		Check(Field<List<EquipmentItem>>("_arsenal").Count == 9 && Field<List<WeaponModulatorData>>("_recoveredModulators").Count == 8, "Weapon and modulator drops become usable run equipment");
		KeyPress(Key.F);
		FpsSliceEnemy advancing = Enemies()[0];
		Vector3 before = advancing.Position;
		await Frames(15);
		Check(Field<bool>("_shopOpen") && advancing.Position.DistanceSquaredTo(before) > 0.01f, "Enemies keep moving while the combat shop is open");
		Check(!(bool)Invoke("TrySpendDefenseCoins", Field<int>("_defenseCoins") + 1)!, "Unaffordable purchases are rejected");

		Click("Upgrade current shape");
		EquipmentItem weapon = (EquipmentItem)Invoke("GetCurrentWeapon")!;
		Check(weapon.Level == 2 && Field<int>("_defenseCoins") == 0, "Purchasing a shape upgrade spends currency and unlocks capacity");
		var recovered = Field<List<WeaponModulatorData>>("_recoveredModulators");
		if (!recovered.Any(part => part.Key == "quickened_core")) recovered.Add(ModulatorCatalog.CreateByKey("quickened_core"));
		Invoke("RebuildDefenseShopMenu");
		float cooldown = (float)Invoke("GetCurrentWeaponCooldownSeconds")!;
		Click("Install Quickened Core", recovered: true);
		Check(weapon.Modulators.Any(part => part.Key == "quickened_core") && (float)Invoke("GetCurrentWeaponCooldownSeconds")! < cooldown, "Installing a recovered cadence modulator changes actual firing cooldown");
		KeyPress(Key.Escape);
		foreach (FpsSliceEnemy enemy in Enemies().ToArray()) enemy.ApplyDamage(100000f);
		await Frames(2);
		Check(Field<bool>("_waitingForNextWave") && Field<int>("_defenseCoins") == 4 * settings.CoinsPerEnemy, "Wave clear collects remaining loot and enters preparation");

		float damagedHealth = core.CurrentHealth;
		int coins = Field<int>("_defenseCoins");
		settings.Lanes[0].Width = 24f;
		await Frames(3);
		Check(core.CurrentHealth == damagedHealth && Field<int>("_defenseCoins") == coins && weapon.Level == 2, "Live lane changes preserve core health, currency and equipment");
		KeyPress(Key.N);
		await Frames(3);
		Check(Enemies().Count == 24, "Second wave has twenty-four threats");
		foreach (FpsSliceEnemy enemy in Enemies().ToArray()) enemy.ApplyDamage(100000f);
		int balance = Field<int>("_defenseCoins");
		KeyPress(Key.F);
		ShapeData offered = Field<List<EquipmentItem>>("_shopShapes")[0].Shape!;
		Click($"Buy {offered.DisplayName}");
		Check(((EquipmentItem)Invoke("GetCurrentWeapon")!).Shape!.Key == offered.Key && Field<int>("_defenseCoins") == balance - settings.ShapePrice, "Buying a rotating base shape equips its weapon identity and charges its price");
		Click("Repair core");
		Check(core.CurrentHealth == core.MaxHealth, "Core repairs restore health without exceeding maximum health");
		KeyPress(Key.Escape);
		KeyPress(Key.N);
		await Frames(3);
		Check(Enemies().Count == 40, "Third wave has forty threats");
		foreach (FpsSliceEnemy enemy in Enemies().ToArray()) enemy.ApplyDamage(100000f);
		await Frames(3);
		Check(Field<bool>("_victory") && core.IsAlive && !Field<bool>("_defeat"), "Victory requires clearing every wave with the core alive");

		RemoveChild(_map);
		_map.QueueFree();
		await Frames(3);
		_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(_map);
		AddChild(_map);
		await Frames(3);
		core = Field<FpsDefenseCore>("_defenseCore");
		_map.DefenseSettings!.Lanes[0].Enabled = false;
		await Frames(3);
		KeyPress(Key.N);
		await Frames(3);
		Check(Enemies().Count == 0 && Field<bool>("_victory"), "Closed lanes reject every spawn group without legacy fallback spawns");
		_map.DefenseSettings.Lanes[0].Enabled = true;
		Invoke("RebuildEncounterFromSettings");
		await Frames(3);
		core.ApplyDamage(core.MaxHealth + 1f);
		Check(Field<bool>("_defeat") && !Field<bool>("_victory"), "Core destruction ends the defense in defeat");
		Invoke("RebuildEncounterFromSettings");
		Check(Field<bool>("_defeat"), "Live encounter edits cannot resume a defense with a destroyed core");
		RemoveChild(_map);
		_map.QueueFree();
		await Frames(3);
		_map = GD.Load<PackedScene>("res://Scenes/Fps/FpsVerticalSlice.tscn").Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(_map);
		AddChild(_map);
		await Frames(3);
		Check(!_map.IsDefenseMap && Enemies().Count == 3 && Field<int>("_currentWaveNumber") == 1, "The original FPS sandbox still starts its existing encounter");
		player = Field<FpsPlayerController>("_player");
		FpsSliceEnemy front = Enemies()[0];
		FpsSliceEnemy rear = Enemies()[1];
		front.Position = player.Position + Vector3.Forward * 8f;
		rear.Position = player.Position + Vector3.Forward * 14f;
		await Frames(3);
		weapon = (EquipmentItem)Invoke("GetCurrentWeapon")!;
		weapon.Modulators.Clear();
		weapon.Modulators.Add(ModulatorCatalog.CreateByKey("piercing_edge"));
		float frontHealth = front.CurrentHealth;
		float rearHealth = rear.CurrentHealth;
		Invoke("FireSingleShot", weapon, player.HeadPosition, (front.HeadPosition - player.HeadPosition).Normalized(), 1f);
		Check(front.CurrentHealth < frontHealth && rear.CurrentHealth < rearHealth, "A piercing Square shot damages both enemies in its firing line");
		RemoveChild(_map);
		_map.QueueFree();
		await Frames(3);
	}

	private List<FpsSliceEnemy> Enemies() => Field<List<FpsSliceEnemy>>("_enemies");
	private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, PrivateInstance)!.GetValue(_map)!;
	private object? Invoke(string name, params object[] arguments) => typeof(FpsVerticalSlice).GetMethod(name, PrivateInstance)!.Invoke(_map, arguments);
	private void KeyPress(Key key) => _map._UnhandledInput(new InputEventKey { Keycode = key, Pressed = true });
	private void Click(string prefix, bool recovered = false)
	{
		Button button = Field<VBoxContainer>("_shopContent").GetChildren().OfType<Button>().First(button => button.Text.StartsWith(prefix, StringComparison.Ordinal) && (!recovered || !button.Text.Contains("coins")));
		Check(!button.Disabled, $"Shop action available: {prefix}");
		button.EmitSignal(Button.SignalName.Pressed);
	}
	private async Task Frames(int count)
	{
		for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private void Check(bool condition, string description)
	{
		if (!condition) throw new InvalidOperationException(description);
		_checks++;
		GD.Print($"PASS: {description}");
	}
}
