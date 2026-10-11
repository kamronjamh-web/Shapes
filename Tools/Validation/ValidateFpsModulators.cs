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

// Real North Gate targets, physics rays, spawned attacks and tick paths. No normal saves.
public partial class ValidateFpsModulators : Node
{
	private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
	private FpsVerticalSlice _map = null!;
	private FpsHordeManager _crowd = null!;
	private List<IFpsCombatEnemy> _targets = null!;
	private int _checks;
	private readonly Vector3 _origin = new(60f, 1.5f, -65f);
	public override async void _Ready()
	{
		try
		{
			Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/fps-modulator-validation/"), "Isolated validation save directory");
			await LoadCrowd();
			WeaponCompatibilityChecks.Run(_map, shape => Weapon(shape), Check);
			ValidateMobiusVolleyReplay();
			ValidateQualityOutputCounts();
			ValidateAdvancedEffects();
			ValidateDamageAndPatterns();
			ValidateRangeDurationAndGuidance();
			ValidateCritsAndCapstone();
			ValidateControlAndSecondaryPatterns();
			ValidateSpecialEffects();
			ValidateObjectTransforms();
			ValidateWings();
			ValidatePlayerPassives();
			ValidateBounceAndLuck();
			ValidateImportedLoadout();
			ValidateQualityWorkshop();
			ValidateCatalogInstallation();
			ValidateQualityWaveReward();
			ValidateFinalWaveWorkshop();
			GD.Print($"FPS modulator validation passed: {_checks} checks.");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private async Task LoadCrowd()
	{
		_map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
		LegacyHillFixture.Configure(_map);
		// The shared combat fixture has one wave. Keep its first hundred targets intact,
		// and add a distinct real terminal wave for normal-clear and victory-workshop checks.
		FpsEncounterWaveSettings finalWave = (FpsEncounterWaveSettings)_map.EncounterSettings!.Waves[0].Duplicate(true);
		finalWave.WaveName = "Quality final-wave workshop fixture";
		finalWave.EnemySpawns[0].Count = 8;
		_map.EncounterSettings.Waves.Add(finalWave);
		// Duplicate tuning before scene initialization; disk resources remain untouched.
		_map.BruiserArchetypeSettings = (FpsEnemyArchetypeSettings)_map.BruiserArchetypeSettings!.Duplicate(true);
		_map.BruiserArchetypeSettings.MaxHealth = 1000000f;
		_map.StrikerArchetypeSettings = (FpsEnemyArchetypeSettings)_map.StrikerArchetypeSettings!.Duplicate(true);
		_map.StrikerArchetypeSettings.MaxHealth = 1000000f;
		_map.DefenseSettings = (FpsDefenseSettings)_map.DefenseSettings!.Duplicate(true);
		AddChild(_map);
		await Frames(4);
		_map._UnhandledInput(new InputEventKey { Keycode = Key.N, Pressed = true });
		await Frames(3);
		_crowd = _map.HordeManager!;
		_targets = Field<List<IFpsCombatEnemy>>("_combatEnemies");
		Check(_crowd.ActiveCount == 100 && _targets.Count == 100, "Tests begin with one hundred real North Gate crowd enemies");
		// Freeze encounter movement only, then explicitly advance actual attack tick methods.
		_map.SetPhysicsProcess(false);
		Field<FpsPlayerController>("_player").SetPhysicsProcess(false);
		Field<RandomNumberGenerator>("_weaponRng").Seed = 413;
		Arrange();
	}

	private void Arrange()
	{
		Invoke("ResetWeaponEffectRuntimeState");
		Func<Vector3, Vector3, float, Vector3>? constrain = _crowd.ConstrainMovement;
		_crowd.ConstrainMovement = null;
		try
		{
			for (int index = 0; index < _targets.Count; index++)
				_targets[index].GlobalPosition = new Vector3(-75f + index % 10, 0f, 65f + index / 10);
			_targets[0].GlobalPosition = new Vector3(60f, 0f, -76f);
			_targets[1].GlobalPosition = new Vector3(64f, 0f, -76f);
			_targets[2].GlobalPosition = new Vector3(68f, 0f, -76f);
		}
		finally { _crowd.ConstrainMovement = constrain; }
		Field<FpsPlayerController>("_player").Position = new Vector3(60f, 0f, -65f);
	}

	private EquipmentItem Weapon(string shape = "square", params string[] parts)
	{
		EquipmentItem weapon = new()
		{
			Shape = EquipmentCatalogDefaults.CreateDefaultShapes().First(item => item.Key == shape),
			Rarity = EquipmentCatalogDefaults.CreateRarityByKey("divine"), Level = 5, Damage = 100, Health = 100,
			Substats = new List<SubstatRoll> { new(SubstatType.CritChance, -100f) },
			Modulators = parts.Select(key => ModulatorCatalog.CreateByKey(key)).ToList(),
		};
		return weapon;
	}
	private void Fire(EquipmentItem weapon)
	{
		Invoke("FireWeaponAttack", weapon, _origin, (_targets[0].HeadPosition - _origin).Normalized(), 1f, false);
		if (weapon.Shape?.AttackType == WeaponAttackType.Single) Invoke("TickPlayerProjectiles", 0.1f);
	}
	private float ShotDamage(EquipmentItem weapon)
	{
		float before = _targets[0].CurrentHealth;
		Fire(weapon);
		return before - _targets[0].CurrentHealth;
	}

	private void ValidateMobiusVolleyReplay()
	{
		foreach (int outputs in new[] { 1, 3 })
		{
			Arrange();
			_targets[0].GlobalPosition = _origin + Vector3.Forward * 2f - Vector3.Up * _origin.Y;
			EquipmentItem weapon = Weapon("mobius");
			weapon.Substats.Add(new(SubstatType.ProjectileCount, outputs - 1));
			float before = _targets[0].CurrentHealth;
			Fire(weapon);
			float initial = before - _targets[0].CurrentHealth;
			IList pending = List("_playerEchoes");
			Check(pending.Count == outputs && pending.Cast<object>().All(echo => ReferenceEquals(Effect<EquipmentItem>(echo, "Weapon"), weapon) &&
				Near(Effect<float>(echo, "Delay"), .55f) && Near(Effect<float>(echo, "DamageScale"), .55f)), $"Mobius {outputs} outputs schedule exactly {outputs} same-source delayed shots");
			Vector3[] directions = pending.Cast<object>().Select(echo => Effect<Vector3>(echo, "Direction")).ToArray();
			Check(directions.All(direction => direction.IsFinite() && Near(direction.Length(), 1f)) && directions.Distinct().Count() == outputs,
				$"Mobius {outputs} delayed records preserve distinct original volley directions");
			Check(Near(initial, outputs * 75f), $"Mobius {outputs} initial hits preserve ordinary output damage");
			Invoke("TickEchoes", .54f);
			Check(pending.Count == outputs && _targets[0].CurrentHealth == before - initial, $"Mobius {outputs} delayed shots respect their actual delay");
			float beforeReplay = _targets[0].CurrentHealth;
			Invoke("TickEchoes", .02f);
			Check(Near(beforeReplay - _targets[0].CurrentHealth, outputs * 75f * .55f) && pending.Count == 0,
				$"Mobius {outputs} outputs replay exactly once each without squared output or recursive queues");
			Invoke("TickEchoes", 2f);
			Check(pending.Count == 0, $"Mobius {outputs} replay never schedules another echo generation");
		}
		Arrange();
		EquipmentItem generic = Weapon("square", "echo_chamber");
		Fire(generic);
		Check(List("_playerEchoes").Count > 0 && List("_playerEchoes").Cast<object>().All(echo => !Effect<bool>(echo, "ReplaySingleTemporalOutput")),
			"Generic on-fire echoes retain full-weapon replay semantics");
	}

	private void ValidateQualityOutputCounts()
	{
		var fixtures = new[] { ("square", "_playerProjectiles", 1), ("hexagon", "_playerMines", 3),
			("cube", "_playerOrbitals", 3), ("pyramid", "_playerProjectiles", 2), ("boys", "_playerClones", 2) };
		foreach (var (shape, list, baseline) in fixtures)
		foreach (var (quality, expectedBonus) in new[] { (WeaponModulatorTier.Flawed, 2), (WeaponModulatorTier.Baseline, 4),
			(WeaponModulatorTier.Enhanced, 6), (WeaponModulatorTier.Flawless, 8) })
		{
			Arrange();
			EquipmentItem weapon = Weapon(shape);
			weapon.Modulators.Add(ModulatorCatalog.CreateByKey("extra_output", quality));
			for (int fire = 0; fire < 4; fire++)
				Invoke("FireWeaponAttack", weapon, _origin, (_targets[0].HeadPosition - _origin).Normalized(), 1f, false);
			Check(List(list).Count == 4 * baseline + expectedBonus && List(list).Cast<object>().All(record =>
				ReferenceEquals(Effect<EquipmentItem>(record, "SourceItem"), weapon)),
				$"{shape}/{quality}: four actual attacks preserve fractional extra outputs and exact source context");
		}
		Arrange();
		EquipmentItem combined = Weapon("square");
		combined.Modulators.Add(ModulatorCatalog.CreateByKey("extra_output", WeaponModulatorTier.Flawed));
		combined.Modulators.Add(ModulatorCatalog.CreateByKey("split_chamber", WeaponModulatorTier.Flawed));
		for (int fire = 0; fire < 4; fire++)
		{
			int before = List("_playerProjectiles").Count;
			Invoke("FireWeaponAttack", combined, _origin, Vector3.Forward, 1f, false);
			Check(List("_playerProjectiles").Count - before == 2, "Two Broken count bonuses combine before resolving each actual Square volley");
		}
		Arrange();
		EquipmentItem first = Weapon("square"), second = Weapon("square");
		first.Modulators.Add(ModulatorCatalog.CreateByKey("extra_output", WeaponModulatorTier.Flawed));
		second.Modulators.Add(ModulatorCatalog.CreateByKey("extra_output", WeaponModulatorTier.Flawed));
		Invoke("FireWeaponAttack", first, _origin, Vector3.Forward, 1f, false);
		Invoke("FireWeaponAttack", second, _origin, Vector3.Forward, 1f, false);
		Check(List("_playerProjectiles").Count == 2, "Fractional count carry stays with each exact weapon source");
		Arrange();
		for (int target = 0; target < 16; target++) _targets[target].GlobalPosition = new Vector3(60f + target * 1.3f, 0f, -76f);
		EquipmentItem lightning = Weapon("pentachoron");
		lightning.Modulators.Add(ModulatorCatalog.CreateByKey("extra_output", WeaponModulatorTier.Flawed));
		lightning.Modulators.Add(ModulatorCatalog.CreateByKey("chain_capacitor", WeaponModulatorTier.Flawed));
		int hits = 0;
		for (int fire = 0; fire < 4; fire++)
		{
			float[] health = _targets.Take(16).Select(target => target.CurrentHealth).ToArray();
			Fire(lightning);
			hits += _targets.Take(16).Select((target, index) => target.CurrentHealth < health[index]).Count(damaged => damaged);
		}
		Check(hits == 4 * (2 + lightning.Shape!.Hits) + 6,
			"Actual lightning aggregates Broken output and chain bonuses before fractional carry, retaining its base chain budget");
		foreach (bool strongestFirst in new[] { true, false })
		{
			EquipmentItem mortar = Weapon("hyperphere");
			WeaponModulatorData strong = ModulatorCatalog.CreateByKey("scorch_crater", WeaponModulatorTier.Flawless);
			WeaponModulatorData weak = ModulatorCatalog.CreateByKey("scorch_crater", WeaponModulatorTier.Flawed);
			mortar.Modulators.AddRange(strongestFirst ? new[] { strong, weak } : new[] { weak, strong });
			WeaponModulatorEffectData config = (WeaponModulatorEffectData)Invoke("GetFpsTransformConfig", mortar, "scorch_crater")!;
			Check(Mathf.Abs(config.QualityStrength - 2f) < .0001f && Mathf.Abs(config.Magnitude - .6f) < .0001f &&
				Mathf.Abs(config.MagnitudeB - 3.5f) < .0001f,
				"Mixed-quality transform copies keep the strongest damage and fixed lifetime independently of installed order");
		}
	}

	private void ValidateAdvancedEffects()
	{
		Arrange();
		EquipmentItem weapon = Weapon("square", "echo_chamber");
		float direct = ShotDamage(weapon), before = _targets[0].CurrentHealth;
		Invoke("TickEchoes", 0.1f);
		Check(_targets[0].CurrentHealth == before, "Echo waits for its configured delay");
		Invoke("TickEchoes", 0.5f);
		Invoke("TickPlayerProjectiles", 0.1f);
		Check(Near(before - _targets[0].CurrentHealth, direct * 0.55f), "Delayed echo damages the real crowd target at reduced power");
		before = _targets[0].CurrentHealth;
		Invoke("TickEchoes", 2f);
		Check(_targets[0].CurrentHealth == before && List("_playerEchoes").Count == 0, "Echo does not recursively schedule itself");

		Arrange();
		weapon = Weapon("square", "predatory_mark");
		float first = ShotDamage(weapon), second = ShotDamage(weapon);
		Check(Near(second, first * 1.22f), "Mark amplifies a subsequent real shot hit");
		Invoke("TickMarkedTargets", 4.1f);
		Check(Near(ShotDamage(weapon), first), "Expired mark restores ordinary hit damage");

		Arrange();
		weapon = Weapon("square", "chain_reaction");
		float nearby = _targets[1].CurrentHealth, further = _targets[2].CurrentHealth;
		direct = ShotDamage(weapon);
		Check(Near(nearby - _targets[1].CurrentHealth, direct * 0.45f) && Near(further - _targets[2].CurrentHealth, direct * 0.45f * 0.85f), "Chain damages two distinct nearby crowd targets with falloff");

		Arrange();
		_targets[1].GlobalPosition = new Vector3(62f, 0f, -76f);
		weapon = Weapon("square", "lingering_scar");
		ShotDamage(weapon);
		before = _targets[0].CurrentHealth;
		nearby = _targets[1].CurrentHealth;
		Invoke("TickModulatorLingeringZones", 0.35f);
		Check(_targets[0].CurrentHealth < before && _targets[1].CurrentHealth < nearby, "Lingering field ticks damage on both real crowd records");
		Check(List("_modulatorLingeringZones").Count == 1, "Lingering ticks do not recursively create more fields");
		Invoke("TickModulatorLingeringZones", 3f);
		before = _targets[0].CurrentHealth;
		Invoke("TickModulatorLingeringZones", 1f);
		Check(_targets[0].CurrentHealth == before && List("_modulatorLingeringZones").Count == 0, "Expired lingering field stops damaging targets");

		Arrange();
		weapon = Weapon("square", "finisher_gate");
		float ordinary = ShotDamage(weapon);
		_targets[0].ApplyDamage(_targets[0].CurrentHealth - _targets[0].MaxHealth * 0.17f);
		Check(Near(ShotDamage(weapon), ordinary * 1.85f), "Execute threshold amplifies low-health crowd damage");
		Arrange();
		weapon = Weapon("square", "boss_geometry");
		Check(Near(ShotDamage(weapon), ordinary * 0.85f), "Hunter applies its rank-and-file damage tradeoff");
		FpsHordeHandle eliteHandle = _crowd.Spawn(_map.StrikerArchetypeSettings!, FpsEnemyRole.Striker, new Vector3(60f, 0f, -76f));
		IFpsCombatEnemy elite = new FpsHordeCombatEnemy(_crowd, eliteHandle);
		float eliteBefore = elite.CurrentHealth;
		Invoke("DealDamageToEnemy", elite, ordinary, elite.HeadPosition, weapon, false, true);
		Check(Near(eliteBefore - elite.CurrentHealth, ordinary * 1.40f), "Hunter amplifies damage on a real elite crowd record");
		elite.ApplyDamage(elite.MaxHealth);
		_crowd.FlushDeathEvents();
	}

	private void Select(EquipmentItem weapon)
	{
		List<EquipmentItem> arsenal = Field<List<EquipmentItem>>("_arsenal");
		arsenal.Clear(); arsenal.Add(weapon);
		SetField("_selectedWeaponIndex", 0);
		Invoke("ApplyWeaponHandling", weapon);
	}

	private void ValidateRangeDurationAndGuidance()
	{
		Arrange();
		Vector3 clearOrigin = new(85f, 1.5f, -65f);
		_targets[0].GlobalPosition = new Vector3(85f, 0f, 20f);
		float before = _targets[0].CurrentHealth;
		Invoke("FireWeaponAttack", Weapon("triangle"), clearOrigin, Vector3.Back, 1f, false);
		Check(_targets[0].CurrentHealth == before, "Ordinary beam does not damage a target beyond its reach");
		Invoke("FireWeaponAttack", Weapon("triangle", "long_reach"), clearOrigin, Vector3.Back, 1f, false);
		Check(_targets[0].CurrentHealth < before, "Long Reach extends actual beam damage to a farther crowd record");

		Arrange();
		for (int index = 0; index < 3; index++) _targets[index].GlobalPosition = new Vector3(-75f + index, 0f, 65f);
		Fire(Weapon("hexagon"));
		Invoke("TickMines", 10.1f);
		Check(List("_playerMines").Count == 0, "Ordinary mines retire at their lifetime");
		Fire(Weapon("hexagon", "lasting_pattern"));
		Invoke("TickMines", 10.1f);
		Check(List("_playerMines").Count > 0, "Lasting Pattern keeps actual mines alive past ordinary expiry");
		Invoke("TickMines", 3f);
		Check(List("_playerMines").Count == 0, "Extended mines still retire after their longer lifetime");

		Arrange();
		_targets[0].GlobalPosition = new Vector3(68f, 0f, -76f);
		Invoke("FireWeaponAttack", Weapon("pyramid"), _origin, Vector3.Forward, 1f, false);
		object baseline = List("_playerProjectiles")[0]!;
		Vector3 targetDirection = (_targets[0].HeadPosition - Effect<Vector3>(baseline, "Position")).Normalized();
		Invoke("TickPlayerProjectiles", 0.016f);
		float ordinaryAngle = Effect<Vector3>(baseline, "Direction").AngleTo(targetDirection);
		Arrange();
		_targets[0].GlobalPosition = new Vector3(68f, 0f, -76f);
		Invoke("FireWeaponAttack", Weapon("pyramid", "guiding_bias"), _origin, Vector3.Forward, 1f, false);
		object guided = List("_playerProjectiles")[0]!;
		targetDirection = (_targets[0].HeadPosition - Effect<Vector3>(guided, "Position")).Normalized();
		Invoke("TickPlayerProjectiles", 0.016f);
		Check(Effect<Vector3>(guided, "Direction").AngleTo(targetDirection) < ordinaryAngle, "Guiding Bias bends a real seeker farther toward its target per tick");

		Arrange();
		Select(Weapon());
		float cooldown = (float)Invoke("GetCurrentWeaponCooldownSeconds")!;
		Select(Weapon("square", "quickened_core"));
		Check((float)Invoke("GetCurrentWeaponCooldownSeconds")! < cooldown, "Quickened Core shortens the cooldown consumed by actual player firing");
		Select(Weapon("square", "longshot_pressure"));
		Check((float)Invoke("GetCurrentWeaponCooldownSeconds")! > cooldown, "Longshot Pressure pays its slower firing cadence tradeoff");
	}

	private void ValidateCritsAndCapstone()
	{
		Arrange();
		float normal = ShotDamage(Weapon());
		EquipmentItem critical = Weapon();
		critical.Substats = new List<SubstatRoll> { new(SubstatType.CritChance, 100f) };
		float crit = ShotDamage(critical);
		Check(crit > normal, "Guaranteed critical chance increases actual crowd hit damage");
		critical.Modulators.Add(ModulatorCatalog.CreateByKey("rupture_crest"));
		Check(ShotDamage(critical) > crit, "Rupture Crest increases actual critical damage");
		EquipmentItem focused = Weapon("square", "focused_edge");
		focused.Substats.Clear();
		RandomNumberGenerator rng = Field<RandomNumberGenerator>("_weaponRng");
		ulong seed = 1;
		for (; seed < 10000; seed++) { rng.Seed = seed; if (rng.Randf() < 0.08f) break; }
		rng.Seed = seed;
		Check(ShotDamage(focused) > normal, "Focused Edge can roll a real critical hit using its catalog chance and a fixed seed");
		Arrange();
		float sharpened = ShotDamage(Weapon("square", "sharpened_form"));
		float apex = ShotDamage(Weapon("square", "sharpened_form", "apex_doctrine"));
		Check(Near(apex, sharpened), "Archived Apex Doctrine adds no amplification to real hit damage");
	}

	private void ValidateControlAndSecondaryPatterns()
	{
		Arrange();
		Vector3 start = _targets[0].GlobalPosition;
		ShotDamage(Weapon());
		float ordinaryPush = start.DistanceTo(_targets[0].GlobalPosition);
		Arrange();
		start = _targets[0].GlobalPosition;
		ShotDamage(Weapon("square", "stability_plating"));
		Check(start.DistanceTo(_targets[0].GlobalPosition) > ordinaryPush, "Stability Plating moves the actual crowd target farther on impact");

		Arrange();
		Fire(Weapon("cube"));
		float ordinaryRadius = Effect<float>(List("_playerOrbitals")[0]!, "OrbitRadius");
		int ordinaryCount = List("_playerOrbitals").Count;
		Arrange();
		Fire(Weapon("cube", "tight_orbit"));
		Check(List("_playerOrbitals").Count == ordinaryCount + 1 && Effect<float>(List("_playerOrbitals")[0]!, "OrbitRadius") < ordinaryRadius, "Tight Orbit creates an extra actual orbital at a shorter orbit radius");

		Arrange();
		for (int index = 0; index < 10; index++) _targets[index].GlobalPosition = new Vector3(60f + index * 3f, 0f, -76f);
		float[] health = _targets.Take(10).Select(target => target.CurrentHealth).ToArray();
		Fire(Weapon("pentachoron"));
		int ordinaryChains = Enumerable.Range(0, 10).Count(index => _targets[index].CurrentHealth < health[index]);
		Arrange();
		for (int index = 0; index < 10; index++) _targets[index].GlobalPosition = new Vector3(60f + index * 3f, 0f, -76f);
		health = _targets.Take(10).Select(target => target.CurrentHealth).ToArray();
		Fire(Weapon("pentachoron", "chain_capacitor"));
		Check(Enumerable.Range(0, 10).Count(index => _targets[index].CurrentHealth < health[index]) > ordinaryChains, "Chain Capacitor damages more distinct real crowd targets");

		Arrange();
		_targets[1].GlobalPosition = new Vector3(62f, 0f, -76f);
		Fire(Weapon("impossiblecube"));
		start = _targets[1].GlobalPosition;
		Invoke("TickZones", 0.1f);
		float ordinaryPull = start.DistanceTo(_targets[1].GlobalPosition);
		Arrange();
		_targets[1].GlobalPosition = new Vector3(62f, 0f, -76f);
		Fire(Weapon("impossiblecube", "void_compression"));
		start = _targets[1].GlobalPosition;
		Invoke("TickZones", 0.1f);
		Check(start.DistanceTo(_targets[1].GlobalPosition) > ordinaryPull, "Void Compression pulls a real crowd record farther per zone tick");

		Arrange();
		Fire(Weapon("boys"));
		float before = _targets[0].CurrentHealth;
		Invoke("TickClones", 0.3f);
		float cloneDamage = before - _targets[0].CurrentHealth;
		Arrange();
		Fire(Weapon("boys", "clone_relay"));
		before = _targets[0].CurrentHealth;
		Invoke("TickClones", 0.3f);
		Check(before - _targets[0].CurrentHealth > cloneDamage, "Clone Relay increases actual clone follow-up damage");
	}

	private void ValidateSpecialEffects()
	{
		foreach ((string shape, string part, float delay) in new[]
		{
			("square", "burstline_converter", 0.5f), ("mobius", "echo_weave", 0.7f),
			("sphere", "double_pulse", 0.4f), ("sphere", "collapse_pulse", 0.5f),
			("pentachoron", "closed_circuit", 0.2f),
		})
		{
			Arrange();
			if (shape == "sphere") _targets[0].GlobalPosition = new Vector3(60f, 0f, -69f);
			EquipmentItem weapon = Weapon(shape, part);
			Select(weapon);
			Fire(weapon);
			float before = _targets[0].CurrentHealth;
			Check(List("_fpsDelayedSpecials").Count > 0, $"{part} schedules its actual delayed follow-up");
			Invoke("TickFpsModulatorSpecials", delay);
			Invoke("TickPlayerProjectiles", 0.1f);
			Check(_targets[0].CurrentHealth < before && List("_fpsDelayedSpecials").Count == 0, $"{part} deals delayed crowd damage without recursive scheduling");
		}

		Arrange();
		_targets[1].GlobalPosition = new Vector3(62.4f, 0f, -76f);
		_targets[2].GlobalPosition = new Vector3(57.6f, 0f, -76f);
		Fire(Weapon("triangle", "prism_refraction"));
		float sideHealth = _targets[1].CurrentHealth;
		Invoke("TickFpsModulatorSpecials", 0.3f);
		Check(_targets[1].CurrentHealth < sideHealth, "Prism Refraction delayed side beam hits an off-axis crowd record");

		Arrange();
		EquipmentItem mortar = Weapon("hyperphere", "scorch_crater");
		Fire(mortar);
		Invoke("TickMortars", 1f);
		Check(List("_playerZones").Count > 0, "Scorch Crater leaves a real field after mortar impact");
		float beforeTick = _targets[0].CurrentHealth;
		Invoke("TickZones", 0.1f);
		Check(_targets[0].CurrentHealth < beforeTick, "Scorch Crater field damages the real impact target");

		Arrange();
		_targets[0].GlobalPosition = new Vector3(60f, 0f, -73f);
		Fire(Weapon("cone", "backdraft_corridor"));
		beforeTick = _targets[0].CurrentHealth;
		Invoke("TickZones", 0.1f);
		Check(List("_playerZones").Count >= 3 && _targets[0].CurrentHealth < beforeTick, "Backdraft Corridor creates burning fields that damage the enemy line");

		Arrange();
		EquipmentItem command = Weapon("hexagon", "command_cluster");
		Fire(command);
		object mine = List("_playerMines")[0]!;
		_targets[0].GlobalPosition = Effect<Vector3>(mine, "Position");
		beforeTick = _targets[0].CurrentHealth;
		Fire(command);
		Check(List("_playerMines").Count == 0 && _targets[0].CurrentHealth < beforeTick && List("_fpsDelayedSpecials").Count > 0, "Command Cluster second trigger remotely detonates real deployed mines and queues clusters");
		beforeTick = _targets[0].CurrentHealth;
		Invoke("TickFpsModulatorSpecials", 0.5f);
		Check(_targets[0].CurrentHealth < beforeTick, "Command Cluster delayed explosions damage real crowd targets");

		Arrange();
		Fire(Weapon("hypercone", "tunnel_bore"));
		Invoke("TickPlayerProjectiles", 0.1f);
		Check(List("_playerZones").Count > 0, "Tunnel Bore leaves a real damaging wake while the drill travels");
		object wake = List("_playerZones")[0]!;
		_targets[1].GlobalPosition = Effect<Vector3>(wake, "Position");
		beforeTick = _targets[1].CurrentHealth;
		Invoke("TickZones", 0.1f);
		Check(_targets[1].CurrentHealth < beforeTick, "Tunnel wake field hits a real crowd record");

		Arrange();
		Fire(Weapon("impossiblecube", "event_horizon_tear"));
		object singularity = List("_playerZones")[0]!;
		beforeTick = _targets[0].CurrentHealth;
		for (int tick = 0; tick < Mathf.CeilToInt(Effect<float>(singularity, "Duration") * 60f) + 2; tick++) Invoke("TickZones", 1f / 60f);
		Check(_targets[0].CurrentHealth < beforeTick && List("_playerProjectiles").Count == 6, "Event Horizon Tear damages on expiry and emits six real fragments");

		Arrange();
		EquipmentItem cascade = Weapon("square", "critical_cascade");
		cascade.Substats = new List<SubstatRoll> { new(SubstatType.CritChance, 100f) };
		sideHealth = _targets[1].CurrentHealth;
		ShotDamage(cascade);
		Check(_targets[1].CurrentHealth < sideHealth, "Critical Cascade arcs real critical-hit damage into a nearby target");
		sideHealth = _targets[1].CurrentHealth;
		ShotDamage(cascade);
		Check(_targets[1].CurrentHealth == sideHealth, "Critical Cascade cooldown prevents same-frame repeated arcs");

		Arrange();
		EquipmentItem omni = Weapon("square", "omni_feedback");
		float ordinary = ShotDamage(omni);
		for (int hit = 1; hit < 7; hit++) Check(Near(ShotDamage(omni), ordinary), $"Omni Feedback ordinary hit {hit + 1}");
		Check(ShotDamage(omni) > ordinary, "Omni Feedback eighth hit adds real follow-up damage");

		Arrange();
		_targets[1].GlobalPosition = new Vector3(60.8f, 0f, -76f);
		EquipmentItem volatileWeapon = Weapon("square", "volitile");
		Select(volatileWeapon);
		sideHealth = _targets[1].CurrentHealth;
		ShotDamage(volatileWeapon);
		Check(_targets[1].CurrentHealth < sideHealth, "Volitile detonates primary damage onto a neighboring crowd record");

		Arrange();
		EquipmentItem reactor = Weapon("klein", "blood_price_reactor");
		Select(reactor);
		_targets[0].GlobalPosition = new Vector3(60f, 0f, -69f);
		Invoke("TickFpsModulatorSpecials", 0.1f);
		Field<FpsPlayerController>("_player").ApplyDamage(20f);
		beforeTick = _targets[0].CurrentHealth;
		Invoke("TickFpsModulatorSpecials", 1.6f);
		Check(_targets[0].CurrentHealth < beforeTick, "Blood Price Reactor converts actual defender damage into a crowd burst");
	}

	private void ValidateWings()
	{
		Arrange();
		EquipmentItem gatePrimary = Weapon();
		Select(gatePrimary);
		WeaponModulatorData wingPart = ModulatorCatalog.CreateByKey("wing_man");
		Check(!(bool)Invoke("CanInstallModulatorForFpsWeapon", gatePrimary, wingPart, -1)!, "Wing installation rejects the primary weapon slot");
		EquipmentItem firstSupport = Weapon("square", "wing_man"), secondSupport = Weapon();
		Field<List<EquipmentItem>>("_arsenal").AddRange(new[] { firstSupport, secondSupport });
		Check(!(bool)Invoke("CanInstallModulatorForFpsWeapon", secondSupport, wingPart, -1)!, "Wing installation rejects a second copy of the same active wing");
		Check(!(bool)Invoke("CanInstallModulatorForFpsWeapon", firstSupport, ModulatorCatalog.CreateByKey("wing_sister"), -1)!, "A support weapon accepts only one wing behavior");
		Check((bool)Invoke("CanInstallModulatorForFpsWeapon", secondSupport, ModulatorCatalog.CreateByKey("wing_sister"), -1)!, "Distinct wing behaviors can coexist on support weapons");
		foreach (string key in new[] { "wing_man", "wing_woman", "wing_brother", "wing_sister" })
		{
			Arrange();
			EquipmentItem primary = Weapon();
			Select(primary);
			float ordinary = ShotDamage(primary);
			Arrange();
			Select(primary);
			EquipmentItem support = Weapon("square", key);
			Field<List<EquipmentItem>>("_arsenal").Add(support);
			float before = _targets.Sum(target => target.CurrentHealth);
			if (key == "wing_man")
			{
				Fire(primary);
				Check(before - _targets.Sum(target => target.CurrentHealth) > ordinary, "Wing Man adds real escort damage on the primary volley");
			}
			else
			{
				Invoke("TickFpsWings", 0.1f);
				Invoke("TickPlayerProjectiles", 0.1f);
				Check(_targets.Sum(target => target.CurrentHealth) < before, $"{key} autonomously damages real crowd targets from a support slot");
			}
			before = _targets.Sum(target => target.CurrentHealth);
			Invoke("TickFpsWings", 0.001f);
			Check(_targets.Sum(target => target.CurrentHealth) == before, $"{key} respects its firing cooldown");
		}
		Arrange();
		Select(Weapon());
	}

	private void ValidateObjectTransforms()
	{
		Arrange();
		Invoke("FireWeaponAttack", Weapon("circle", "return_arc"), new Vector3(60f, 100f, -65f), Vector3.Forward, 1f, false);
		object saw = List("_playerProjectiles")[0]!;
		Vector3 toPlayer = (Field<FpsPlayerController>("_player").GlobalPosition + Vector3.Up - Effect<Vector3>(saw, "Position")).Normalized();
		saw.GetType().GetField("Life")!.SetValue(saw, 0.01f);
		Invoke("TickPlayerProjectiles", 0.02f);
		Check(List("_playerProjectiles").Count == 1 && Effect<Vector3>(saw, "Direction").Dot(toPlayer) > 0.99f, "Return Arc steers an expired real saw back toward the defender");
		Invoke("TickPlayerProjectiles", 4.1f);
		Check(List("_playerProjectiles").Count == 0, "Return Arc returns exactly once before retiring");

		Arrange();
		_targets[0].GlobalPosition = new Vector3(60f, 0f, -70f);
		_targets[1].GlobalPosition = new Vector3(60f, 0f, -74f);
		EquipmentItem bloom = Weapon("circle", "serration_bloom");
		bloom.Shape!.Hits = 2;
		Fire(bloom);
		Invoke("TickPlayerProjectiles", 0.4f);
		Check(List("_playerProjectiles").Count == 2 && List("_playerProjectiles").Cast<object>().All(value => Effect<object>(value, "Kind").ToString() == "Drill"), "Serration Bloom replaces an exhausted real saw with two nonrecursive fragments");

		Arrange();
		Fire(Weapon("tesseract", "phase_inversion"));
		object rift = List("_playerZones")[0]!;
		Vector3 center = Effect<Vector3>(rift, "Position");
		_targets[1].GlobalPosition = center + Vector3.Right * 2f;
		float distance = _targets[1].GlobalPosition.DistanceTo(center);
		Invoke("TickZones", 0.1f);
		Check(_targets[1].GlobalPosition.DistanceTo(center) < distance, "Phase Inversion pulls real crowd targets during its first phase");
		// Advance the real field age to the next phase while retaining the target fixture.
		rift.GetType().GetField("Age")!.SetValue(rift, 0.6f);
		_targets[1].GlobalPosition = center + Vector3.Right * 2f;
		distance = _targets[1].GlobalPosition.DistanceTo(center);
		Invoke("TickZones", 0.1f);
		Check(_targets[1].GlobalPosition.DistanceTo(center) > distance, "Phase Inversion pushes real crowd targets during its second phase");

		Arrange();
		Fire(Weapon("cube", "aegis_carousel"));
		object orbital = List("_playerOrbitals")[0]!;
		float spin = Effect<float>(orbital, "AngularSpeed");
		Invoke("TickOrbitals", 0.1f);
		Check(List("_playerZones").Count > 0, "Aegis Carousel leaves real orbital damage trails");
		Invoke("TickOrbitals", 2.1f);
		Check(Effect<float>(orbital, "AngularSpeed") * spin < 0f, "Aegis Carousel reverses actual orbital rotation periodically");

		Arrange();
		EquipmentItem pack = Weapon("pyramid", "pack_hunters");
		Invoke("DealDamageToEnemy", _targets[0], 10f, _targets[0].HeadPosition, pack, false, true);
		Fire(pack);
		object seeker = List("_playerProjectiles")[0]!;
		Check(ReferenceEquals(Invoke("ResolveFpsSeekerTarget", seeker), _targets[0]), "Pack Hunters subsequent seekers retain the first hit target");
		EquipmentItem locked = Weapon("pyramid", "lock_burst");
		Arrange();
		Fire(locked);
		seeker = List("_playerProjectiles")[0]!;
		IFpsCombatEnemy target = (IFpsCombatEnemy)Invoke("ResolveFpsSeekerTarget", seeker)!;
		_targets[2].GlobalPosition = _origin + Vector3.Forward * 2f;
		Check(ReferenceEquals(Invoke("ResolveFpsSeekerTarget", seeker), target), "Lock Burst preserves its real seeker lock when another enemy becomes nearer");

		Arrange();
		EquipmentItem primary = Weapon("circle");
		Select(primary);
		Fire(Weapon("boys", "primary_mimic"));
		Invoke("TickClones", 0.3f);
		Check(List("_playerProjectiles").Count > 0 && List("_playerProjectiles").Cast<object>().All(value => Effect<object>(value, "Kind").ToString() == "Sawblade"), "Primary Mimic clones spawn the primary weapon's actual saw attack pattern");
	}
	private void ValidatePlayerPassives()
	{
		Arrange();
		FpsPlayerController player = Field<FpsPlayerController>("_player");
		Select(Weapon());
		float baselineHealth = player.MaxHealth, baselineSpeed = player.MoveSpeed;
		Select(Weapon("square", "vital_reservoir"));
		Check(Near(player.MaxHealth, baselineHealth * 1.12f), "Vital Reservoir changes defender maximum health");
		Select(Weapon("square", "glass_hammer_core"));
		Check(Near(player.MaxHealth, baselineHealth * 0.88f), "Glass Hammer pays its actual defender health tradeoff");
		Select(Weapon("square", "stride_servo"));
		Check(Near(player.MoveSpeed, baselineSpeed * 1.08f), "Stride Servo changes the movement speed used by the controller");
		Select(Weapon("square", "recovery_circuit"));
		player.SetDebugHealth(player.MaxHealth, player.MaxHealth * 0.5f);
		float before = player.CurrentHealth;
		Invoke("TickFpsModulatorPassives", 2f);
		Check(Near(player.CurrentHealth - before, 2.4f), "Recovery Circuit actually heals the defender over time");
		EquipmentItem leech = Weapon("square", "hemophage_loop");
		Select(leech);
		player.SetDebugHealth(player.MaxHealth, player.MaxHealth * 0.5f);
		before = player.CurrentHealth;
		float damage = ShotDamage(leech);
		Check(Near(player.CurrentHealth - before, damage * 0.04f), "Hemophage Loop heals from actual crowd damage");
		Select(Weapon("square", "evasion_mesh"));
		player.SetDebugHealth(10000f, 10000f);
		RandomNumberGenerator rng = (RandomNumberGenerator)typeof(FpsPlayerController).GetField("_rng", Members)!.GetValue(player)!;
		rng.Seed = 431;
		for (int hit = 0; hit < 1000; hit++) player.ApplyDamage(1f);
		Check(player.CurrentHealth > 9000f && player.CurrentHealth < 9100f, "Evasion Mesh dodges some incoming real damage calls with a fixed RNG seed");
		Select(Weapon());
		player.SetDebugHealth(player.MaxHealth, player.MaxHealth);
	}

	private void ValidateDamageAndPatterns()
	{
		Arrange();
		float baseline = ShotDamage(Weapon());
		foreach (string key in new[] { "power_coil", "sharpened_form", "glass_hammer_core", "overclocked_core", "vampiric_engine" })
		{
			EquipmentItem weapon = Weapon("square", key);
			float expected = weapon.Modulators[0].GetResolvedEffects().Where(effect => effect.Type == ModulatorEffectType.DamageMultiplier).Sum(effect => effect.Magnitude - 1f) + 1f;
			Check(Near(ShotDamage(weapon), baseline * expected), $"{key} changes actual shot damage including its tradeoff");
		}
		Arrange();
		_targets[1].GlobalPosition = new Vector3(60f, 0f, -82f);
		float rear = _targets[1].CurrentHealth;
		ShotDamage(Weapon());
		Check(_targets[1].CurrentHealth == rear, "Ordinary Square stops at the first crowd target");
		ShotDamage(Weapon("square", "piercing_edge"));
		Check(_targets[1].CurrentHealth < rear, "Piercing Edge reaches a second crowd target through the real projectile path");

		Arrange();
		EquipmentItem saw = Weapon("circle");
		Fire(saw);
		object projectile = List("_playerProjectiles")[0]!;
		Vector3 start = Effect<Vector3>(projectile, "Position");
		Invoke("TickPlayerProjectiles", 0.05f);
		float baseTravel = Effect<Vector3>(projectile, "Position").DistanceTo(start);
		Arrange();
		Fire(Weapon("circle", "accelerated_flight"));
		projectile = List("_playerProjectiles")[0]!;
		start = Effect<Vector3>(projectile, "Position");
		Invoke("TickPlayerProjectiles", 0.05f);
		Check(Near(Effect<Vector3>(projectile, "Position").DistanceTo(start), baseTravel * 1.20f), "Accelerated Flight changes actual saw travel per tick");
		Arrange();
		Fire(saw);
		float radius = Effect<float>(List("_playerProjectiles")[0]!, "Radius");
		Arrange();
		Fire(Weapon("circle", "heavy_payload"));
		Check(Effect<float>(List("_playerProjectiles")[0]!, "Radius") > radius, "Heavy Payload enlarges the spawned saw collision footprint");
		Arrange();
		Fire(saw);
		int count = List("_playerProjectiles").Count;
		Arrange();
		Fire(Weapon("circle", "saw_storm"));
		Check(List("_playerProjectiles").Count == count + 1, "Saw Storm creates another real saw attack instance");
	}

	private void ValidateQualityWorkshop()
	{
		Arrange();
		List<WeaponModulatorData> recovered = Field<List<WeaponModulatorData>>("_recoveredModulators");
		WeaponModulatorData[] priorStorage = recovered.ToArray();
		int priorCoins = Field<int>("_defenseCoins"), priorCost = _map.DefenseSettings!.ModulatorFusionBaseCost;
		try
		{
			recovered.Clear();
			EquipmentItem weapon = Weapon();
			WeaponModulatorData part = ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Flawed);
			weapon.Modulators.Add(part);
			Select(weapon);
			SetField("_combatShopRemaining", 100f);
			SetField("_defenseCoins", 100);
			_map.DefenseSettings.ModulatorFusionBaseCost = 20;
			Field<FpsPlayerController>("_player").GlobalPosition = (Vector3)Invoke("ResolveDefenseGroundPosition", _map.DefenseSettings.ShopPosition)!;
			Invoke("OpenDefenseShop");
			Check(Field<bool>("_shopOpen"), "Quality workshop opens at the actual field shop location");
			Check((bool)Invoke("ReturnDefenseModulator", weapon, part)! && recovered.Count == 1 &&
				ReferenceEquals(recovered[0], part) && weapon.Modulators.Count == 0 && Field<int>("_defenseCoins") == 100,
				"Actual field workshop returns an installed Broken part to loose storage without a coin charge or quality loss");
			recovered.Add(ModulatorCatalog.Clone(part));
			SetField("_defenseCoins", 19);
			Check(!(bool)Invoke("FuseDefenseModulator", part)! && recovered.Count == 2 && Field<int>("_defenseCoins") == 19,
				"Actual field workshop refuses unaffordable fusion without consuming either part");
			SetField("_defenseCoins", 100);
			Check((bool)Invoke("FuseDefenseModulator", part)! && recovered.Count == 1 &&
				recovered[0].Key == "sharpened_form" && recovered[0].Tier == WeaponModulatorTier.Baseline && Field<int>("_defenseCoins") == 80 &&
				Mathf.Abs(recovered[0].GetResolvedEffects()[0].Magnitude - 1.12f) < .0001f,
				"Actual field workshop fuses two Broken copies into one Baseline copy and charges exactly twenty run coins");
			Check(!(bool)Invoke("FuseDefenseModulator", part)! && recovered.Count == 1 && Field<int>("_defenseCoins") == 80,
				"Stale field workshop callback cannot spend coins or fuse a second time");
			recovered.Add(ModulatorCatalog.Clone(recovered[0]));
			Invoke("CloseDefenseShop");
			Check(!(bool)Invoke("FuseDefenseModulator", recovered[0])! && recovered.Count == 2 && Field<int>("_defenseCoins") == 80,
				"Field fusion is unavailable after the actual shop closes");
		}
		finally
		{
			Invoke("CloseDefenseShop");
			recovered.Clear(); recovered.AddRange(priorStorage);
			SetField("_defenseCoins", priorCoins);
			_map.DefenseSettings.ModulatorFusionBaseCost = priorCost;
			Select(Weapon());
		}
	}

	private void ValidateQualityWaveReward()
	{
		Arrange();
		FpsDefenseSettings settings = _map.DefenseSettings!;
		float weaponChance = settings.WeaponDropChance, partChance = settings.ModulatorDropChance;
		List<WeaponModulatorData> pool = Field<List<WeaponModulatorData>>("_defenseModulatorPool");
		WeaponModulatorData[] priorPool = pool.ToArray();
		List<WeaponModulatorData> recovered = Field<List<WeaponModulatorData>>("_recoveredModulators");
		int before = recovered.Count;
		int groundBefore = List("_defensePickups").Cast<object>().Count(pickup => Effect<WeaponModulatorData?>(pickup, "Modulator") != null);
		try
		{
			settings.WeaponDropChance = 0f; settings.ModulatorDropChance = 0f;
			pool.Clear(); pool.Add(ModulatorCatalog.CreateByKey("extra_output"));
			Check(!(bool)Invoke("GrantDefenseWaveModulator")! && recovered.Count == before,
				"An uncleared real wave grants no guaranteed modulator early");
			foreach (IFpsCombatEnemy target in _targets.ToArray()) target.ApplyDamage(target.CurrentHealth + 1f);
			_crowd.FlushDeathEvents();
			object[] groundParts = List("_defensePickups").Cast<object>().Where(pickup => Effect<WeaponModulatorData?>(pickup, "Modulator") != null).ToArray();
			Check(_crowd.ActiveCount == 0 && recovered.Count == before && groundParts.Length == groundBefore + 1,
				"Completing a real wave drops exactly one quality-bearing part on the ground without adding it to storage");
			object rewardPickup = groundParts[^1];
			WeaponModulatorData reward = Effect<WeaponModulatorData>(rewardPickup, "Modulator");
			Check(reward.Key == "extra_output" && Enum.IsDefined(typeof(WeaponModulatorTier), reward.Tier) &&
				Field<string>("_lastWaveModulatorReward").StartsWith(reward.DisplayName, StringComparison.Ordinal),
				"Guaranteed wave reward uses the real catalog quality roll and exposes its actual quality name");
			Invoke("HandleWaveCleared"); Invoke("HandleWaveCleared");
			Check(recovered.Count == before && List("_defensePickups").Cast<object>().Count(pickup => Effect<WeaponModulatorData?>(pickup, "Modulator") != null) == groundBefore + 1 &&
				!(bool)Invoke("GrantDefenseWaveModulator")!,
				"Repeated real wave-clear handling cannot duplicate its guaranteed reward");
			Invoke("CollectDefensePickup", rewardPickup);
			Check(recovered.Count == before + 1 && ReferenceEquals(recovered[^1], reward) && !List("_defensePickups").Contains(rewardPickup),
				"Explicit ground pickup stores the guaranteed reward with its original quality and removes the ground record");
			EquipmentItem weapon = Weapon();
			Select(weapon);
			SetField("_combatShopRemaining", 100f);
			Check((bool)Invoke("CanInstallDefenseModulator", reward)!, "Guaranteed reward is compatible with the actual shop installation path");
			Invoke("InstallDefenseModulator", reward);
			Check(weapon.Modulators.Count == 1 && weapon.Modulators[0].Tier == reward.Tier &&
				!ReferenceEquals(weapon.Modulators[0], reward), "Actual installation clones the rewarded quality without resetting or doubling it");
			Invoke("FireWeaponAttack", weapon, _origin, Vector3.Forward, 1f, false);
			Check(List("_playerProjectiles").Count > 0 && List("_playerProjectiles").Cast<object>().All(record =>
				ReferenceEquals(Effect<EquipmentItem>(record, "SourceItem"), weapon)),
				"Attacks fired with the guaranteed reward retain the actual installed quality source");
		}
		finally { settings.WeaponDropChance = weaponChance; settings.ModulatorDropChance = partChance; pool.Clear(); pool.AddRange(priorPool); }
	}

	private void ValidateFinalWaveWorkshop()
	{
		Dictionary<string, byte[]?> saves = new();
		foreach (string name in new[] { "inventory_state.cfg", "game_state.cfg", "currency_state.cfg", "save_manager.cfg" })
		{
			string path = ProjectSettings.GlobalizePath("user://" + name);
			saves[path] = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
		}
		int totalWaves = Field<int>("_totalWaveCount");
		Check(totalWaves == 2 && Field<int>("_currentWaveNumber") == 1 && Field<int>("_nextWaveIndex") == 1 && !Field<bool>("_victory"),
			"Normal wave reward leaves the distinct configured final wave pending");
		Check((bool)Invoke("TrySpawnNextWave")! && Field<int>("_currentWaveNumber") == totalWaves && _crowd.ActiveCount > 0,
			"Final workshop fixture spawns the real last configured wave");
		FpsDefenseSettings settings = _map.DefenseSettings!;
		float weaponChance = settings.WeaponDropChance, partChance = settings.ModulatorDropChance;
		List<WeaponModulatorData> recovered = Field<List<WeaponModulatorData>>("_recoveredModulators");
		int before = recovered.Count;
		int finalGroundBefore = List("_defensePickups").Cast<object>().Count(pickup => Effect<WeaponModulatorData?>(pickup, "Modulator") != null);
		try
		{
			settings.WeaponDropChance = 0f; settings.ModulatorDropChance = 0f;
			foreach (IFpsCombatEnemy target in _targets.ToArray()) target.ApplyDamage(target.CurrentHealth + 1f);
			_crowd.FlushDeathEvents();
			Check(Field<bool>("_victory") && _crowd.ActiveCount == 0 && recovered.Count == before &&
				List("_defensePickups").Cast<object>().Count(pickup => Effect<WeaponModulatorData?>(pickup, "Modulator") != null) == finalGroundBefore + 1,
				"The real final wave ends defense and drops its exactly-once reward without automatically storing it");
			Field<FpsPlayerController>("_player").GlobalPosition = new Vector3(60f, 0f, -65f);
			Check(!(bool)Invoke("IsNearDefenseShop")!, "Final reward fixture remains away from the core shop");
			Button inventory = Field<Button>("_inventoryButton");
			EquipmentItem alternate = Weapon("circle");
			Field<List<EquipmentItem>>("_arsenal").Add(alternate);
			Check(!inventory.Disabled && inventory.Text == "Final reward / workshop", "Victory presents an enabled final-reward workshop button");
			inventory.EmitSignal(BaseButton.SignalName.Pressed);
			Check(Field<bool>("_shopOpen") && !Field<Control>("_overlayBackdrop").Visible && !IsCombatRunning(),
				"Pressing the real final-reward button opens the workshop away from the core without resuming combat");
			Field<VBoxContainer>("_shopContent").GetNode<Button>("InspectWeaponButton").EmitSignal(BaseButton.SignalName.Pressed);
			_map.PreviewInspectedWeapon(1);
			Button equip = Field<Node>("_weaponInspectionUi").FindChild("EquipInspectedShape", true, false) as Button
				?? throw new InvalidOperationException("Final inspection equip button is missing");
			equip.EmitSignal(BaseButton.SignalName.Pressed);
			Check(_map.IsWeaponInspectionOpen && !Field<bool>("_shopOpen") && !IsCombatRunning() && _map.EquippedWeaponIndex == 1 &&
				ReferenceEquals(Invoke("GetCurrentWeapon"), alternate),
				"Final workshop inspection actually equips a different shape while completed defense stays stopped");
			_map.CloseWeaponInspection();
			Check(Field<bool>("_shopOpen") && !_map.IsWeaponInspectionOpen && !IsCombatRunning(),
				"Closing final inspection returns to the victory workshop away from the core");
			Button results = Field<VBoxContainer>("_shopContent").GetChildren().OfType<Button>().Single(button => button.Text == "Back to results / F or Esc");
			results.EmitSignal(BaseButton.SignalName.Pressed);
			_map._PhysicsProcess(1d / 60d);
			Invoke("TickWaveFlow", 10f);
			Check(!Field<bool>("_shopOpen") && Field<bool>("_victory") && Field<Control>("_overlayBackdrop").Visible &&
				!IsCombatRunning() && !_crowd.CombatEnabled && _crowd.ActiveCount == 0 && recovered.Count == before &&
				List("_defensePickups").Cast<object>().Count(pickup => Effect<WeaponModulatorData?>(pickup, "Modulator") != null) == finalGroundBefore + 1,
				"Returning to results restores the victory overlay without restarting combat, spawning a wave or granting another reward");
			Check(saves.All(pair => pair.Value == null ? !System.IO.File.Exists(pair.Key) : System.IO.File.Exists(pair.Key) &&
				pair.Value.SequenceEqual(System.IO.File.ReadAllBytes(pair.Key))), "Final reward, workshop and inspection leave hub save files unchanged");
		}
		finally { settings.WeaponDropChance = weaponChance; settings.ModulatorDropChance = partChance; }
		bool IsCombatRunning() => (bool)Invoke("IsCombatRunning")!;
	}

	private void ValidateCatalogInstallation()
	{
		Check(!(bool)Invoke("IsDefensePartCompatible", ModulatorCatalog.CreateByKey("ballistic_tuning"), Weapon("triangle"))!, "Shop rejects projectile speed on a beam that has no travelling projectile");
		Check((bool)Invoke("IsDefensePartCompatible", ModulatorCatalog.CreateByKey("ballistic_tuning"), Weapon())!, "Shop accepts projectile speed on Square's real bullets");
		Check(!(bool)Invoke("IsDefensePartCompatible", ModulatorCatalog.CreateByKey("rebound_array"), Weapon("triangle"))!, "Shop rejects wall bounce on a beam that has no bouncing projectile");
		List<WeaponModulatorData> catalog = ModulatorCatalog.CreateAll();
		List<WeaponModulatorData> pool = Field<List<WeaponModulatorData>>("_defenseModulatorPool");
		Check(catalog.Where(part => part.Key != "apex_doctrine").All(part => pool.Any(candidate => candidate.Key == part.Key)) &&
			!pool.Any(part => part.Key == "apex_doctrine"), "North Gate loot/shop pool includes all ninety-nine active parts and excludes archived Apex");
		List<EquipmentItem> arsenal = Field<List<EquipmentItem>>("_arsenal");
		SetField("_combatShopRemaining", 100f);
		int sourceContextRecords = 0;
		foreach (WeaponModulatorData part in catalog)
		{
			if (part.Key == "apex_doctrine")
			{
				Check(EquipmentCatalogDefaults.CreateDefaultShapes().All(shape => !(bool)Invoke("IsDefensePartCompatible", part, Weapon(shape.Key))!),
					"Archived Apex remains readable but cannot be installed on any FPS shape");
				continue;
			}
			EquipmentItem? weapon = EquipmentCatalogDefaults.CreateDefaultShapes().Select(shape => Weapon(shape.Key))
				.FirstOrDefault(candidate => (bool)Invoke("IsDefensePartCompatible", part, candidate)!);
			Check(weapon != null, $"Catalog modulator {part.Key} has an eligible FPS weapon");
			Arrange();
			arsenal.Clear();
			if (part.Category == WeaponModulatorCategory.Wing) arsenal.Add(Weapon());
			arsenal.Add(weapon!);
			SetField("_selectedWeaponIndex", arsenal.Count - 1);
			Check((bool)Invoke("CanInstallDefenseModulator", part)!, $"{part.Key} passes the actual shop installation gate");
			Invoke("InstallDefenseModulator", part);
			Check(weapon!.Modulators.Any(installed => installed.Key == part.Key) && !ReferenceEquals(weapon.Modulators[0], part), $"{part.Key} installs a run-local clone through the actual shop path");
			Fire(weapon);
			foreach (string tick in new[] { "TickPlayerProjectiles", "TickMines", "TickZones", "TickOrbitals", "TickMortars", "TickEchoes", "TickClones", "TickModulatorEffectState" }) Invoke(tick, 0.016f);
			foreach (string listName in new[] { "_playerProjectiles", "_playerMines", "_playerZones", "_playerOrbitals", "_playerMortars", "_playerClones", "_modulatorLingeringZones" })
				foreach (object effect in List(listName))
				{
					EquipmentItem? source = Effect<EquipmentItem?>(effect, "SourceItem");
					if (source == null) continue; // Cosmetic pulses have no combat source.
					if (!ReferenceEquals(source, weapon)) throw new InvalidOperationException($"{part.Key} lost source context in {listName}");
					sourceContextRecords++;
				}
		}
		Check(sourceContextRecords > 0, $"Catalog attack records preserve their installed source item ({sourceContextRecords} live records inspected)");
		Check(_crowd.ActiveCount == 100, "Catalog installation and attacks retain all hundred high-health crowd records");
	}

	private void ValidateBounceAndLuck()
	{
		Arrange();
		Vector3 highOrigin = new(60f, 100f, -65f);
		Invoke("FireWeaponAttack", Weapon(), highOrigin, Vector3.Forward, 1f, false);
		object bullet = List("_playerProjectiles")[0]!;
		Vector3 initial = Effect<Vector3>(bullet, "Position");
		Invoke("TickPlayerProjectiles", 0.01f);
		float ordinaryTravel = Effect<Vector3>(bullet, "Position").DistanceTo(initial);
		Arrange();
		Invoke("FireWeaponAttack", Weapon("square", "ballistic_tuning"), highOrigin, Vector3.Forward, 1f, false);
		bullet = List("_playerProjectiles")[0]!;
		initial = Effect<Vector3>(bullet, "Position");
		Invoke("TickPlayerProjectiles", 0.01f);
		Check(Near(Effect<Vector3>(bullet, "Position").DistanceTo(initial), ordinaryTravel * 1.15f), "Ballistic Tuning changes actual Square bullet travel per tick");
		Arrange();
		Vector3 wallOrigin = new(60f, _targets[0].HeadPosition.Y, -94f);
		Invoke("FireWeaponAttack", Weapon(), wallOrigin, Vector3.Forward, 1f, false);
		Invoke("TickPlayerProjectiles", 0.05f);
		Check(List("_playerProjectiles").Count == 0, "Ordinary Square bullet retires at the real map boundary wall");
		EquipmentItem rebound = Weapon("square", "rebound_array");
		Invoke("FireWeaponAttack", rebound, wallOrigin, Vector3.Forward, 1f, false);
		Invoke("TickPlayerProjectiles", 0.05f);
		Check(List("_playerProjectiles").Count == 1 && Effect<Vector3>(List("_playerProjectiles")[0]!, "Direction").Z > 0f, "Rebound Array reflects an actual bullet off the physical boundary wall");
		_targets[0].GlobalPosition = new Vector3(60f, 0f, -90f);
		float before = _targets[0].CurrentHealth;
		Invoke("TickPlayerProjectiles", 0.07f);
		Check(_targets[0].CurrentHealth < before, "The reflected bullet damages a real crowd record on its return path");
		Arrange();
		before = _targets[1].CurrentHealth;
		ShotDamage(rebound);
		Check(_targets[1].CurrentHealth < before, "Rebound Array ricochets primary damage into a nearby real target");

		Arrange();
		Select(Weapon("square", "lucky_prism"));
		FpsDefenseSettings defense = _map.DefenseSettings!;
		float originalWeaponChance = defense.WeaponDropChance, originalModulatorChance = defense.ModulatorDropChance;
		int originalCoins = defense.CoinsPerEnemy;
		try
		{
			defense.WeaponDropChance = 0f; defense.ModulatorDropChance = 0f; defense.CoinsPerEnemy = 5;
			int balance = Field<int>("_defenseCoins");
			SetField("_defenseCoinRemainder", 0f);
			for (int drop = 0; drop < 100; drop++) Invoke("SpawnDefenseLoot", _origin);
			Invoke("SweepDefenseLoot");
			Check(Field<int>("_defenseCoins") - balance == 522, "Lucky Prism boosts actual run currency with fractional carry across one hundred drops");
			Select(Weapon("square", "lucky_prism", "apex_doctrine"));
			Check(Mathf.Abs((float)Invoke("GetFpsModulatorLuckMultiplier")! - 1.045f) < 0.00001f, "Archived Apex Doctrine adds no amplification to Lucky Prism");
			balance = Field<int>("_defenseCoins");
			SetField("_defenseCoinRemainder", 0f);
			for (int drop = 0; drop < 100; drop++) Invoke("SpawnDefenseLoot", _origin);
			Invoke("SweepDefenseLoot");
			Check(Field<int>("_defenseCoins") - balance == 522, "Archived Apex leaves actual Lucky Prism currency unchanged across one hundred drops");
		}
		finally { defense.WeaponDropChance = originalWeaponChance; defense.ModulatorDropChance = originalModulatorChance; defense.CoinsPerEnemy = originalCoins; }
		Select(Weapon());
	}

	private void ValidateImportedLoadout()
	{
		Arrange();
		EquipmentItem[] originals =
		{
			Weapon("triangle", "ballistic_tuning", "wing_man"),
			Weapon("square", "wing_man", "wing_woman"),
			Weapon("square", "wing_man"),
			Weapon("square", "wing_sister"),
		};
		EquipmentItem[] clones = originals.Select(item => (EquipmentItem)Invoke("CloneSliceWeapon", item)!).ToArray();
		List<EquipmentItem> arsenal = Field<List<EquipmentItem>>("_arsenal");
		arsenal.Clear(); arsenal.AddRange(clones);
		int recovered = Field<List<WeaponModulatorData>>("_recoveredModulators").Count;
		Invoke("PrepareFpsRunModulators");
		Check(clones[0].Modulators.Count == 0, "Imported primary removes unsupported beam speed and primary-slot wings");
		Check(clones[1].Modulators.Count == 1 && clones[1].Modulators[0].Key == "wing_man" && clones[2].Modulators.Count == 0 && clones[3].Modulators.Count == 1, "Imported supports retain distinct valid wings while removing duplicate behaviors");
		Check(Field<List<WeaponModulatorData>>("_recoveredModulators").Count == recovered + 4, "Removed imported parts remain recoverable inside the run");
		Check(originals.Select(item => item.Modulators.Count).SequenceEqual(new[] { 2, 2, 1, 1 }), "Imported run sanitization preserves all source equipment modulators");
		Select(Weapon());
	}

	private static T Effect<T>(object effect, string name) => (T)effect.GetType().GetField(name)!.GetValue(effect)!;
	private IList List(string name) => Field<IList>(name);
	private T Field<T>(string name) => (T)typeof(FpsVerticalSlice).GetField(name, Members)!.GetValue(_map)!;
	private void SetField(string name, object value) => typeof(FpsVerticalSlice).GetField(name, Members)!.SetValue(_map, value);
	private object? Invoke(string name, params object[] arguments)
	{
		MethodInfo method = typeof(FpsVerticalSlice).GetMethods(Members).Single(candidate => candidate.Name == name &&
			candidate.GetParameters().Length == arguments.Length && candidate.GetParameters().Select((parameter, index) =>
				arguments[index] == null || parameter.ParameterType.IsInstanceOfType(arguments[index])).All(matches => matches));
		return method.Invoke(_map, arguments);
	}
	private async Task Frames(int count) { for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < Mathf.Max(0.05f, expected * 0.002f);
	private void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); _checks++; GD.Print($"PASS: {description}"); }
}
