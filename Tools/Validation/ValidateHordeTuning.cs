using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

// Runs against the real North Gate director, crowd records, UI and damage path.
public partial class ValidateHordeTuning : Node
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private FpsVerticalSlice _map = null!;
	private int _checks;
	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/horde-tuning-validation/"), "Save data is isolated from normal runs");
			_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
			LegacyHillFixture.Configure(_map);
			AddChild(_map);
			await Frames(4);
			_map.DebugPlayerInvulnerable = true;
			_map.DebugCoreInvulnerable = true;
			FpsHordeManager crowd = _map.HordeManager!;
			Check(crowd.Capacity >= 2000, "North Gate allocates at least 2000 crowd slots");
			ValidateResourceIsolation();
			ValidateInvulnerability();
			await ValidateRangedDamageAndPopulation(crowd);
			foreach (int count in new[] { 100, 500, 1000, 2000 })
			{
				Check(_map.DebugSpawnHorde(count, true) == count && crowd.ActiveCount == count && _map.ActiveEnemyCount == count, $"Debug spawn installs exactly {count} enemies and director records");
				List<FpsHordeEnemySnapshot> alive = Alive(crowd);
				float half = _map.HillMapSettings!.PlayableSize * 0.5f;
				Check(alive.All(e => e.Position.IsFinite() && Mathf.Abs(e.Position.X) <= half - e.Radius && Mathf.Abs(e.Position.Z) <= half - e.Radius), $"{count} spawn positions remain inside the playable boundary");
				Check(alive.All(e => Mathf.Abs(e.Position.Y - _map.SampleHillSurfaceHeight(e.Position)) < 0.05f), $"{count} spawns stand on the terrain surface");
				bool separated = true;
				for (int a = 0; a < alive.Count && separated; a++)
					for (int b = a + 1; b < alive.Count; b++)
					{
						Vector3 delta = alive[a].Position - alive[b].Position; delta.Y = 0f;
						float radius = alive[a].Radius + alive[b].Radius;
						if (delta.LengthSquared() + 0.001f < radius * radius) { separated = false; break; }
					}
				Check(separated, $"{count} initial spawn collision footprints do not overlap");
				await Frames(3);
				Check(crowd.RenderedInstanceCount == count, $"{count} live enemies have render instances");
			}
			FpsHordeHandle stale = Alive(crowd)[0].Handle;
			_map.DebugClearHorde();
			Check(crowd.ActiveCount == 0 && crowd.RenderedInstanceCount == 0 && _map.ActiveEnemyCount == 0, "Clear removes simulation, render and director records");
			_map.DebugSpawnHorde(100, true);
			Check(!crowd.IsAlive(stale) && !crowd.ApplyDamage(stale, 100000f), "A cleared handle cannot damage a recycled slot");
			int added = _map.DebugSpawnHorde(100000, false);
			Check(added == 1900 && crowd.ActiveCount == 2000 && _map.ActiveEnemyCount == 2000, "Over-limit Add fills available slots up to the hard 2000-enemy limit");
			await ValidateMenu(crowd);
			await ValidateLiveSettings(crowd);
			ValidateDamage(crowd);
			await ValidateConfiguredWave(crowd);
			GD.Print($"Horde tuning validation passed: {_checks} checks.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private async Task ValidateMenu(FpsHordeManager crowd)
	{
		KeyPress(Key.Quoteleft);
		await Frames(2);
		Check(Field<bool>("_gameplayTuningVisible") && !Field<bool>("_pauseRequested"), "Grave opens gameplay tuning without pausing the run");
		Check(Input.MouseMode == Input.MouseModeEnum.Visible, "Opening gameplay tuning exposes the cursor");
		Button preset = Descendants(Field<Control>("_gameplayTuningMenu")).OfType<Button>().First(b => b.Text == "500");
		preset.EmitSignal(Button.SignalName.Pressed);
		SpinBox count = Field<SpinBox>("_gameplayTuningSpawnCount");
		Check(count.Value == 500 && crowd.ActiveCount == 2000, "Population preset changes the field without implicitly spawning");
		Button replace = Descendants(Field<Control>("_gameplayTuningMenu")).OfType<Button>().First(b => b.Text == "Replace");
		replace.EmitSignal(Button.SignalName.Pressed);
		Check(crowd.ActiveCount == 500 && _map.ActiveEnemyCount == 500, "Replace button signal creates the selected population through the real UI");
		count.GetLineEdit().GrabFocus();
		Check(count.GetLineEdit().HasFocus(), "Population edit field can own keyboard focus");
		_map._Input(new InputEventKey { PhysicalKeycode = Key.Quoteleft, Pressed = true });
		Check(!Field<bool>("_gameplayTuningVisible"), "Physical grave shortcut closes tuning while a numeric edit owns focus");
		_map._Input(new InputEventKey { Keycode = Key.Quoteleft, Pressed = true });
		Check(Field<bool>("_gameplayTuningVisible"), "Logical grave shortcut opens tuning through early input handling");
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		Check(!(bool)typeof(FpsPlayerController).GetField("_combatEnabled", Private)!.GetValue(player)! && crowd.CombatEnabled, "Tuning blocks player movement and firing while crowd combat remains enabled");
		Vector3 rotation = player.Rotation;
		player._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(100f, 100f) });
		Check(player.Rotation == rotation, "Menu prevents gameplay mouse look");
		long steps = crowd.SimulationSteps;
		await Frames(12);
		Check(crowd.SimulationSteps > steps, "Crowd simulation continues while tuning is open");
		KeyPress(Key.Quoteleft);
		await Frames(2);
		Check(!Field<bool>("_gameplayTuningVisible") && (bool)typeof(FpsPlayerController).GetField("_combatEnabled", Private)!.GetValue(player)!, "Grave closes tuning and restores player controls");
		if (DisplayServer.GetName() != "headless") Check(Input.MouseMode == Input.MouseModeEnum.Captured, "Closing tuning recaptures the mouse on a graphics display");
		else GD.Print("LIMIT: Headless display cannot establish native mouse recapture; rerun with -Rendered to check it.");
		KeyPress(Key.Quoteleft);
		KeyPress(Key.Escape);
		await Frames(2);
		Check(!Field<bool>("_gameplayTuningVisible") && !Field<bool>("_pauseRequested"), "Escape closes tuning first without pausing");
		KeyPress(Key.Escape);
		await Frames(2);
		steps = crowd.SimulationSteps;
		await Frames(12);
		Check(Field<bool>("_pauseRequested") && crowd.SimulationSteps == steps, "Escape still pauses crowd simulation after tuning closes");
		KeyPress(Key.Escape);
		await Frames(2);
	}

	private async Task ValidateLiveSettings(FpsHordeManager crowd)
	{
		FpsHordeHandle tracked = Alive(crowd)[0].Handle;
		float oldHz = crowd.Settings.SimulationHz;
		crowd.Settings.SimulationHz = 10f;
		long steps = crowd.SimulationSteps;
		await Frames(60);
		long slow = crowd.SimulationSteps - steps;
		crowd.Settings.SimulationHz = 60f;
		steps = crowd.SimulationSteps;
		await Frames(60);
		long fast = crowd.SimulationSteps - steps;
		Check(slow >= 8 && slow <= 12 && fast >= 55 && fast <= 65, $"Live simulation Hz changes step frequency (10 Hz: {slow}, 60 Hz: {fast})");
		crowd.Settings.Enabled = false;
		steps = crowd.SimulationSteps;
		await Frames(12);
		Check(crowd.SimulationSteps == steps && crowd.IsAlive(tracked), "Disabling crowd simulation preserves live records");
		crowd.Settings.Enabled = true;
		await Frames(12);
		Check(crowd.SimulationSteps > steps, "Enabling crowd simulation resumes existing records");
		crowd.Settings.SimulationHz = oldHz;
		FpsHordeEnemySnapshot target = Alive(crowd).First(e => e.Role == FpsEnemyRole.Bruiser);
		crowd.ApplyDamage(target.Handle, target.MaxHealth * 0.25f);
		crowd.TryGet(target.Handle, out target);
		float originalHealth = _map.BruiserArchetypeSettings!.MaxHealth;
		_map.BruiserArchetypeSettings.MaxHealth = originalHealth * 2f;
		_map.ApplyHordeArchetypeChangesLive();
		crowd.TryGet(target.Handle, out FpsHordeEnemySnapshot tuned);
		Check(crowd.IsAlive(target.Handle) && Mathf.IsEqualApprox(tuned.MaxHealth, target.MaxHealth * 2f) && Mathf.IsEqualApprox(tuned.CurrentHealth / tuned.MaxHealth, target.CurrentHealth / target.MaxHealth), "Archetype health retune preserves handles and damage fraction");
		_map.BruiserArchetypeSettings.MaxHealth = originalHealth;
		_map.ApplyHordeArchetypeChangesLive();
	}

	private void ValidateDamage(FpsHordeManager crowd)
	{
		_map.DebugSpawnHorde(100, true);
		_map.DefenseSettings!.WeaponDropChance = 0f;
		_map.DefenseSettings.ModulatorDropChance = 0f;
		int count = crowd.ActiveCount, coins = Field<int>("_defenseCoins"), deaths = 0;
		crowd.EnemyDied += _ => deaths++;
		FpsHordeEnemySnapshot target = Alive(crowd)[0];
		Invoke("DamageEnemiesInRadius", target.Position, 0.1f, target.MaxHealth + 1f, null!, false);
		crowd.FlushDeathEvents();
		Invoke("SweepDefenseLoot");
		Check(!crowd.IsAlive(target.Handle) && deaths == 1 && crowd.ActiveCount == count - 1 && _map.ActiveEnemyCount == count - 1, "Real weapon area damage retires exactly one separated crowd record");
		Check(Field<int>("_defenseCoins") == coins + _map.DefenseSettings.CoinsPerEnemy, "Crowd death awards configured coins exactly once");
		crowd.ApplyDamage(target.Handle, 100000f);
		crowd.FlushDeathEvents();
		Check(deaths == 1, "Stale damage cannot duplicate death rewards");
	}
	private void ValidateResourceIsolation()
	{
		FpsHordeSettings saved = GD.Load<FpsHordeSettings>("res://Resources/Fps/HillFort_Horde.tres");
		FpsEncounterSettings savedEncounter = GD.Load<FpsEncounterSettings>("res://Resources/Fps/HillFort_Encounter.tres");
		FpsEnemyArchetypeSettings savedBruiser = GD.Load<FpsEnemyArchetypeSettings>("res://Resources/Fps/NorthGate_Cube.tres");
		Check(!ReferenceEquals(saved, _map.HordeSettings) && !ReferenceEquals(savedEncounter, _map.EncounterSettings) && !ReferenceEquals(savedBruiser, _map.BruiserArchetypeSettings), "Live tuning resources are separate from cached project resources");
		FpsEnemySpawnSettings runSpawn = _map.EncounterSettings!.Waves[0].EnemySpawns[0];
		FpsEnemySpawnSettings cachedSpawn = savedEncounter.Waves[0].EnemySpawns[0];
		int oldCount = runSpawn.Count, cachedCount = cachedSpawn.Count;
		runSpawn.Count = 123;
		Check(!ReferenceEquals(runSpawn, cachedSpawn) && cachedSpawn.Count == cachedCount, "Nested encounter spawn settings are isolated from cached resource data");
		runSpawn.Count = oldCount;
		float runHz = _map.HordeSettings!.SimulationHz, cacheHz = saved.SimulationHz;
		_map.HordeSettings.SimulationHz = 12f;
		Check(saved.SimulationHz == cacheHz, "Run-only simulation tuning does not modify the cached project resource");
		_map.HordeSettings.SimulationHz = runHz;
	}
	private void ValidateInvulnerability()
	{
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
		float playerHp = player.CurrentHealth, coreHp = core.CurrentHealth;
		player.ApplyDamage(1f);
		_map.ApplyDefenseCoreDamage(1f);
		core.ApplyDamage(1f);
		Check(player.CurrentHealth == playerHp && core.CurrentHealth == coreHp, "Stress invulnerability blocks new player and core damage");
		_map.DebugPlayerInvulnerable = false;
		_map.DebugCoreInvulnerable = false;
		player.ApplyDamage(1f);
		_map.ApplyDefenseCoreDamage(1f);
		Check(player.CurrentHealth < playerHp && core.CurrentHealth < coreHp, "Disabling stress invulnerability restores real player and core damage");
		_map.DebugPlayerInvulnerable = true;
		_map.DebugCoreInvulnerable = true;
	}
	private async Task ValidateRangedDamageAndPopulation(FpsHordeManager crowd)
	{
		FpsDefenseCore core = Field<FpsDefenseCore>("_defenseCore");
		Vector3 origin = core.GlobalPosition + Vector3.Right * 4f;
		float health = core.CurrentHealth;
		_map.SpawnEnemyProjectile(origin, Vector3.Left, 40f, 7f, default);
		await Frames(12);
		Check(core.CurrentHealth == health && Field<List<FpsEnemyProjectile>>("_projectiles").Count == 0, "A real enemy projectile is retired without damaging an invulnerable core");
		_map.DebugCoreInvulnerable = false;
		_map.SpawnEnemyProjectile(origin, Vector3.Left, 40f, 7f, default);
		await Frames(12);
		Check(Mathf.IsEqualApprox(core.CurrentHealth, health - 7f), "Disabling core invulnerability restores actual projectile damage");
		_map.DebugCoreInvulnerable = true;
		Control menu = Field<Control>("_gameplayTuningMenu");
		OptionButton selector = Descendants(menu).OfType<OptionButton>().Single();
		selector.Select(1);
		selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
		Check(_map.DebugSpawnRole == FpsEnemyRole.Striker && _map.DebugSpawnHorde(50, true) == 50 && Alive(crowd).All(e => e.Role == FpsEnemyRole.Striker), "Enemy type selection spawns real ranged pyramid records");
		await Frames(3);
		Check(crowd.RenderedInstanceCount == 50, "Ranged pyramid population has matching render instances");
		selector.Select(0);
		selector.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
	}
	private async Task ValidateConfiguredWave(FpsHordeManager crowd)
	{
		_map.EncounterSettings!.Waves[0].EnemySpawns[0].Count = 2000;
		Invoke("RebuildEncounterFromSettings");
		await Frames(3);
		KeyPress(Key.N);
		await Frames(3);
		Check(crowd.ActiveCount == 2000 && _map.ActiveEnemyCount == 2000, "Configured 2000-enemy wave uses the bounded formation path");
		float half = _map.HillMapSettings!.PlayableSize * 0.5f;
		Check(Alive(crowd).All(e => Mathf.Abs(e.Position.X) <= half - e.Radius && Mathf.Abs(e.Position.Z) <= half - e.Radius), "Configured dense wave remains inside the playable map");
	}
	private static IEnumerable<Node> Descendants(Node root)
	{
		foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
	}
	private static List<FpsHordeEnemySnapshot> Alive(FpsHordeManager crowd) { List<FpsHordeEnemySnapshot> result = new(); crowd.CopyAlive(result); return result; }
	private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Private)!.GetValue(_map)!;
	private object? Invoke(string name, params object[] args) => typeof(FpsVerticalSlice).GetMethod(name, Private)!.Invoke(_map, args);
	private void KeyPress(Key key) => _map._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
	private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); _checks++; GD.Print($"PASS: {text}"); }
}
