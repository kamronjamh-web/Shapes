using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private const float DefaultAimRange = 160f;
	private const float SawbladeSpeed = 30f;
	private const float SawbladeLife = 4.0f;
	private const float HomingSpeed = 24f;
	private const float DrillSpeed = 34f;
	private const float DrillLife = 2.6f;
	private const float MineArmTime = 0.30f;
	private const float MineLife = 10f;
	private const float MineTriggerRadius = 1.7f;
	private const float MineExplosionRadius = 4.4f;
	private const float RiftTickInterval = 0.35f;
	private const float VoidTickInterval = 0.22f;
	private const float WrathAuraTickInterval = 0.35f;
	private const float OrbitalHitRadius = 1.7f;
	private const float OrbitalHitCooldown = 0.30f;
	private const float MortarFlightTime = 0.85f;
	private const float MortarArcHeight = 6.2f;
	private const float CloneDuration = 6.0f;
	private const float CloneFireCadence = 0.75f;
	private const float CloneOrbitRadius = 3.2f;
	private const float ModulatorLingeringZoneTickInterval = 0.35f;

	private enum PlayerProjectileKind
	{
		Bullet,
		Sawblade,
		Homing,
		Drill,
	}

	private enum ZoneEffectKind
	{
		Rift,
		Void,
		Pulse,
		Flame,
		Beam,
	}

	private sealed class PlayerProjectileEffect
	{
		public PlayerProjectileKind Kind;
		public Vector3 Position;
		public Vector3 Direction = Vector3.Forward;
		public float Speed;
		public float Life;
		public float Damage;
		public float Radius;
		public int RemainingHits;
		public float TurnRate;
		public int RemainingBounces;
		public float VisualAge;
		public Vector3 VisualOffset;
		public bool TriggerModulatorOnHitEffects = true;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
		public readonly HashSet<IFpsCombatEnemy> HitEnemies = new();
	}

	private sealed class MineEffect
	{
		public Vector3 Position;
		public float VisualAge;
		public float ArmTime;
		public float Life;
		public float Damage;
		public float TriggerRadius;
		public float ExplosionRadius;
		public bool TriggerModulatorOnHitEffects;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
	}

	private sealed class ZoneEffect
	{
		public ZoneEffectKind Kind;
		public Vector3 Position;
		public bool FollowPlayer;
		public float Age;
		public float Duration;
		public float Tick;
		public float TickInterval;
		public float Radius;
		public float DamagePerTick;
		public float PullStrength;
		public Vector3 Direction;
		public Vector3 VisualOrigin;
		public WeaponAttackType? PolishType;
		public Color PolishRarity;
		public float Range;
		public bool TriggerModulatorOnHitEffects;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
	}

	private sealed class OrbitalEffect
	{
		public bool HasTravelSample;
		public float Angle;
		public float Age;
		public float Duration;
		public float OrbitRadius;
		public float AngularSpeed;
		public float Damage;
		public bool TriggerModulatorOnHitEffects;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
		public readonly Dictionary<IFpsCombatEnemy, float> LastHitAt = new();
	}

	private sealed class MortarEffect
	{
		public Vector3 Start;
		public Vector3 Target;
		public Vector3 PreviousVisualPosition;
		public bool HasVisualSample;
		public float Age;
		public float FlightTime;
		public float Damage;
		public float Radius;
		public bool TriggerModulatorOnHitEffects;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
	}

	private sealed class EchoEffect
	{
		public float Delay;
		public EquipmentItem Weapon = null!;
		public Vector3 Origin;
		public Vector3 Direction = Vector3.Forward;
		public float DamageScale = 1f;
		public bool ReplaySingleTemporalOutput;
	}

	private sealed class CloneEffect
	{
		public Vector3 VisualDirection = Vector3.Forward;
		public float Angle;
		public float Age;
		public float Duration;
		public float FireCooldown;
		public float OrbitRadius;
		public float Damage;
		public bool TriggerModulatorOnHitEffects = true;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
	}

	private sealed class ModulatorMarkedEnemyState
	{
		public float TimeLeft;
		public float DamageMultiplier = 1f;
	}

	private sealed class ModulatorLingeringZoneEffect
	{
		public Vector3 Position;
		public float Age;
		public float Tick;
		public float Duration;
		public float Radius;
		public float TotalDamage;
		public bool TriggerModulatorOnHitEffects;
		public bool IsCritical;
		public EquipmentItem? SourceItem;
		public MeshInstance3D Mesh = null!;
		public StandardMaterial3D Material = null!;
	}

	private readonly List<EquipmentItem> _arsenal = new();
	private readonly List<PlayerProjectileEffect> _playerProjectiles = new();
	private readonly List<MineEffect> _playerMines = new();
	private readonly List<ZoneEffect> _playerZones = new();
	private readonly List<OrbitalEffect> _playerOrbitals = new();
	private readonly List<MortarEffect> _playerMortars = new();
	private readonly List<EchoEffect> _playerEchoes = new();
	private readonly List<CloneEffect> _playerClones = new();
	private readonly List<ModulatorLingeringZoneEffect> _modulatorLingeringZones = new();
	private readonly Dictionary<IFpsCombatEnemy, ModulatorMarkedEnemyState> _modulatorMarkedEnemies = new();
	private readonly RandomNumberGenerator _weaponRng = new();

	private Node3D _weaponEffectRoot = null!;
	private int _selectedWeaponIndex;
	private float _wrathAuraTick;
	private bool _usingHubPrimaryWeapon;

	public override void _PhysicsProcess(double delta)
	{
		UpdateWeaponPolishState();
		CorpsePool?.Tick((float)delta, !_pauseRequested);
		TickGroundLootPhysics((float)delta);
		RefreshFortTurretVisuals();
		if (!IsCombatRunning())
		{
			return;
		}

		float dt = (float)delta;
		_weaponPolish?.BeginFrame(dt, _player.HeadPosition);
		HordeManager?.TickSimulation(dt);
		TickFortTurrets(dt);
		TickFpsModulatorPassives(dt);
		TickPlayerProjectiles(dt);
		TickMines(dt);
		TickZones(dt);
		TickOrbitals(dt);
		TickMortars(dt);
		TickEchoes(dt);
		TickModulatorEffectState(dt);
		TickClones(dt);
		TickWrathAura(dt);
		TickFpsModulatorSpecials(dt);
		TickFpsWings(dt);
		FlushHordeDeaths();
		_weaponPolish?.EndFrame();
	}

	private void BuildWeaponArsenal()
	{
		ResetFortTurrets();
		_weaponRng.Randomize();
		_arsenal.Clear();
		_reserveShapes.Clear();
		_usingHubPrimaryWeapon = false;

		EquipmentItem? primaryWeapon = ResolvePrimaryHubWeapon();
		if (primaryWeapon != null)
		{
			_arsenal.Add(primaryWeapon);
			InventoryManager? inventory = GetNodeOrNull<InventoryManager>("/root/InventoryManager");
			for (int slot = 1; slot < EquippedShapeCapacity; slot++)
				if (inventory?.GetEquippedItemAt(slot) is { Shape: not null, Rarity: not null } support) _arsenal.Add(CloneSliceWeapon(support));
			_usingHubPrimaryWeapon = true;
			_selectedWeaponIndex = 0;
			PrepareFpsRunModulators();
			return;
		}

		EquipmentItem? fallbackWeapon = CreateFallbackSliceWeapon();
		if (fallbackWeapon != null)
		{
			_arsenal.Add(fallbackWeapon);
		}

		_selectedWeaponIndex = 0;
	}

	private EquipmentItem? ResolvePrimaryHubWeapon()
	{
		InventoryManager? inventoryManager = GetNodeOrNull<InventoryManager>("/root/InventoryManager");
		EquipmentItem? equippedWeapon = inventoryManager?.GetEquippedItemAt(0);
		if (equippedWeapon?.Shape == null || equippedWeapon.Rarity == null)
		{
			return null;
		}

		return CloneSliceWeapon(equippedWeapon);
	}

	private static EquipmentItem CloneSliceWeapon(EquipmentItem source)
	{
		EquipmentItem clone = new()
		{
			InstanceId = source.InstanceId,
			Shape = source.Shape,
			Rarity = source.Rarity,
			Essence = source.Essence,
			Level = source.Level,
			WeaponXpProgress = source.WeaponXpProgress,
			Stars = source.Stars,
			Health = source.Health,
			Damage = source.Damage,
			Substats = CloneSliceSubstats(source.Substats),
			Modulators = CloneSliceModulators(source.Modulators),
		};

		StatGenerator.RecalculateDerivedStats(clone);
		return clone;
	}

	private EquipmentItem? CreateFallbackSliceWeapon()
	{
		ShapeData? fallbackShape = FindShapeTemplate("square");
		if (fallbackShape == null)
		{
			return null;
		}

		EquipmentItem fallbackWeapon = new()
		{
			Shape = fallbackShape,
			Rarity = EquipmentCatalogDefaults.CreateRarityByKey("common"),
			Level = 1,
			Stars = Mathf.Max(1, fallbackShape.Stars),
			Substats = new List<SubstatRoll>(),
			Modulators = new List<WeaponModulatorData>(),
		};

		StatGenerator.RecalculateDerivedStats(fallbackWeapon);
		return fallbackWeapon;
	}

	private static List<SubstatRoll> CloneSliceSubstats(IReadOnlyList<SubstatRoll>? source)
	{
		List<SubstatRoll> clone = new();
		if (source == null)
		{
			return clone;
		}

		for (int substatIndex = 0; substatIndex < source.Count; substatIndex++)
		{
			SubstatRoll substat = source[substatIndex];
			clone.Add(new SubstatRoll(substat.Type, substat.Value));
		}

		return clone;
	}

	private static List<WeaponModulatorData> CloneSliceModulators(IReadOnlyList<WeaponModulatorData>? source)
	{
		List<WeaponModulatorData> clone = new();
		if (source == null)
		{
			return clone;
		}

		for (int modulatorIndex = 0; modulatorIndex < source.Count; modulatorIndex++)
		{
			WeaponModulatorData? modulator = source[modulatorIndex];
			if (modulator == null)
			{
				continue;
			}

			clone.Add(CloneSliceModulator(modulator));
		}

		return clone;
	}

	private static WeaponModulatorData CloneSliceModulator(WeaponModulatorData source)
	{
		List<SubstatRoll> substats = new(source.Substats?.Count ?? 0);
		if (source.Substats != null)
		{
			for (int substatIndex = 0; substatIndex < source.Substats.Count; substatIndex++)
			{
				SubstatRoll roll = source.Substats[substatIndex];
				substats.Add(new SubstatRoll(roll.Type, roll.Value));
			}
		}

		List<WeaponModulatorEffectData> effects = new(source.Effects?.Count ?? 0);
		if (source.Effects != null)
		{
			for (int effectIndex = 0; effectIndex < source.Effects.Count; effectIndex++)
			{
				WeaponModulatorEffectData effect = source.Effects[effectIndex];
				effects.Add(new WeaponModulatorEffectData
				{
					Type = effect.Type,
					Trigger = effect.Trigger,
					Magnitude = effect.Magnitude,
					MagnitudeB = effect.MagnitudeB,
					MagnitudeC = effect.MagnitudeC,
					Count = effect.Count,
					CountValue = effect.CountValue,
					CountB = effect.CountB,
					Key = effect.Key,
				});
			}
		}

		WeaponModulatorData clone = new()
		{
			Key = source.Key,
			BaseDisplayName = source.BaseDisplayName,
			DisplayName = source.DisplayName,
			BaseDescription = source.BaseDescription,
			Description = source.Description,
			PowerTier = source.PowerTier,
			Category = source.Category,
			Tags = source.Tags != null ? new List<string>(source.Tags) : new List<string>(),
			AllowedWeaponTags = source.AllowedWeaponTags != null ? new List<string>(source.AllowedWeaponTags) : new List<string>(),
			BlockedWeaponTags = source.BlockedWeaponTags != null ? new List<string>(source.BlockedWeaponTags) : new List<string>(),
			SupportsTier = source.SupportsTier,
			Weight = source.Weight,
			UiColor = source.UiColor,
			Substats = substats,
			Effects = effects,
			Tier = source.Tier,
		};

		clone.RefreshPresentation();
		return clone;
	}

	private static ShapeData? FindShapeTemplate(string shapeKey)
	{
		List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
		for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
		{
			ShapeData shape = shapes[shapeIndex];
			if (string.Equals(shape.Key, shapeKey, StringComparison.OrdinalIgnoreCase))
			{
				return shape;
			}
		}

		return null;
	}

	private bool HandleWeaponInput(InputEvent @event)
	{
		if (_arsenal.Count <= 1 || _pauseRequested || _weaponInspectionOpen || (_victory && !(IsDefenseMap && _shopOpen)) || _defeat)
		{
			return false;
		}

		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			if (mouseButton.ButtonIndex == MouseButton.WheelUp)
			{
				StepWeapon(-1);
				return true;
			}

			if (mouseButton.ButtonIndex == MouseButton.WheelDown)
			{
				StepWeapon(1);
				return true;
			}
		}

		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
		{
			return false;
		}

		if (keyEvent.Keycode == Key.Q)
		{
			StepWeapon(-1);
			return true;
		}

		if (keyEvent.Keycode == Key.E)
		{
			StepWeapon(1);
			return true;
		}

		if (TryGetHotkeyWeaponIndex(keyEvent.Keycode, out int weaponIndex))
		{
			EquipWeapon(weaponIndex);
			return true;
		}

		return false;
	}

	private void StepWeapon(int direction)
	{
		if (_arsenal.Count == 0)
		{
			return;
		}

		int nextIndex = _selectedWeaponIndex + direction;
		if (nextIndex < 0)
		{
			nextIndex = _arsenal.Count - 1;
		}
		else if (nextIndex >= _arsenal.Count)
		{
			nextIndex = 0;
		}

		EquipWeapon(nextIndex);
	}

	private void EquipWeapon(int index, bool showNotice = true)
	{
		if (_arsenal.Count == 0 || _player == null || !GodotObject.IsInstanceValid(_player))
		{
			return;
		}

		int clampedIndex = Mathf.Clamp(index, 0, _arsenal.Count - 1);
		if (_selectedWeaponIndex == clampedIndex && showNotice)
		{
			UpdateWeaponHud();
			return;
		}

		_selectedWeaponIndex = clampedIndex;
		EquipmentItem weapon = _arsenal[_selectedWeaponIndex];
		ApplyWeaponHandling(weapon);
		if (weapon.Shape != null)
		{
			_player.ApplyWeaponItemVisual(weapon);
		}

		UpdateWeaponHud();

		if (showNotice && weapon.Shape != null)
		{
			ShowNotice($"{weapon.Shape.DisplayName} | {weapon.Shape.AttackName} | {weapon.Shape.SpecialDescription}", 2.6f);
		}
	}

	private void ApplyWeaponHandling(EquipmentItem weapon)
	{
		if (weapon.Shape == null)
		{
			return;
		}

		(float hipSpread, float aimSpread, float recoilKick, float hipFov, float aimFov) = weapon.Shape.AttackType switch
		{
			WeaponAttackType.Single => (0.8f, 0.12f, 0.055f, 78f, 61f),
			WeaponAttackType.Sawblade => (1.1f, 0.22f, 0.070f, 79f, 64f),
			WeaponAttackType.Laser => (0.18f, 0.04f, 0.030f, 76f, 57f),
			WeaponAttackType.Mine => (0.95f, 0.18f, 0.050f, 78f, 63f),
			WeaponAttackType.Nova => (1.35f, 0.35f, 0.040f, 82f, 68f),
			WeaponAttackType.Orbital => (0.90f, 0.18f, 0.045f, 80f, 64f),
			WeaponAttackType.Homing => (0.65f, 0.10f, 0.060f, 78f, 60f),
			WeaponAttackType.FlameCone => (1.65f, 0.60f, 0.055f, 80f, 67f),
			WeaponAttackType.Rift => (0.85f, 0.15f, 0.050f, 79f, 62f),
			WeaponAttackType.Mortar => (1.05f, 0.18f, 0.060f, 79f, 64f),
			WeaponAttackType.ChainLightning => (0.35f, 0.06f, 0.035f, 77f, 58f),
			WeaponAttackType.Drill => (0.75f, 0.12f, 0.065f, 79f, 61f),
			WeaponAttackType.VoidSingularity => (0.95f, 0.18f, 0.060f, 80f, 63f),
			WeaponAttackType.TemporalEcho => (0.55f, 0.10f, 0.045f, 77f, 60f),
			WeaponAttackType.WrathAura => (0.45f, 0.08f, 0.030f, 81f, 66f),
			WeaponAttackType.ShadowClone => (0.60f, 0.12f, 0.050f, 78f, 61f),
			_ => (1.0f, 0.2f, 0.055f, 78f, 63f),
		};

		_player.HipFireSpreadDegrees = hipSpread;
		_player.AimSpreadDegrees = aimSpread;
		_player.RecoilKick = recoilKick;
		_player.HipFov = hipFov;
		_player.AimFov = aimFov;
		_player.FireRate = 1f / Mathf.Max(0.05f, GetCurrentWeaponCooldownSeconds());
		_player.ShotDamage = GetWeaponBaseDamage(weapon);
		_player.ShotRange = GetWeaponRange(weapon.Shape.AttackType) * GetFpsRangeScale(weapon);
		ApplyFpsModulatorPassives(weapon);
	}

	private EquipmentItem? GetCurrentWeapon()
	{
		return _arsenal.Count == 0 || _selectedWeaponIndex < 0 || _selectedWeaponIndex >= _arsenal.Count
			? null
			: _arsenal[_selectedWeaponIndex];
	}

	private float GetCurrentWeaponCooldownSeconds()
	{
		EquipmentItem? weapon = GetCurrentWeapon();
		float cadence = weapon == null ? 1f : Mathf.Max(0.1f, 1f + StatGenerator.GetTotalSubstatValue(weapon, SubstatType.AttackSpeed) / 100f);
		return Mathf.Max(0.05f, GetWeaponRuntimeCooldown(weapon) / cadence);
	}

	internal static float GetWeaponRuntimeCooldown(EquipmentItem? weapon)
	{
		if (weapon?.Shape == null)
		{
			return 0.25f;
		}

		if (weapon.Shape.Cooldown < 900f)
		{
			return Mathf.Max(0.05f, weapon.Shape.Cooldown);
		}

		return weapon.Shape.AttackType switch
		{
			WeaponAttackType.TemporalEcho => 0.85f,
			WeaponAttackType.WrathAura => 1.10f,
			WeaponAttackType.ShadowClone => 2.80f,
			_ => 0.90f,
		};
	}

	private float GetWeaponBaseDamage(EquipmentItem weapon)
	{
		float directDamage = Mathf.Max(0f, StatGenerator.GetEffectiveDamage(weapon));
		if (directDamage > 0f)
		{
			return directDamage;
		}

		return weapon.Shape?.AttackType switch
		{
			WeaponAttackType.TemporalEcho => GetReferenceWeaponDamage(weapon, "square", 0.92f),
			WeaponAttackType.WrathAura => GetReferenceWeaponDamage(weapon, "sphere", 1.10f),
			_ => 1f,
		};
	}

	private float GetReferenceWeaponDamage(EquipmentItem sourceWeapon, string shapeKey, float multiplier)
	{
		EquipmentItem? referenceWeapon = CreateReferenceWeapon(shapeKey, sourceWeapon);
		if (referenceWeapon != null)
		{
			return Mathf.Max(1f, StatGenerator.GetEffectiveDamage(referenceWeapon) * multiplier);
		}

		return multiplier;
	}

	private EquipmentItem? CreateReferenceWeapon(string shapeKey, EquipmentItem sourceWeapon)
	{
		ShapeData? referenceShape = FindShapeTemplate(shapeKey);
		if (referenceShape == null)
		{
			return null;
		}

		EquipmentItem referenceWeapon = new()
		{
			Shape = referenceShape,
			Rarity = sourceWeapon.Rarity ?? EquipmentCatalogDefaults.CreateRarityByKey("common"),
			Essence = sourceWeapon.Essence,
			Level = sourceWeapon.Level,
			Stars = Mathf.Max(1, referenceShape.Stars),
			Substats = CloneSliceSubstats(sourceWeapon.Substats),
			Modulators = CloneSliceModulators(sourceWeapon.Modulators),
		};

		StatGenerator.RecalculateDerivedStats(referenceWeapon);
		return referenceWeapon;
	}

	private static float GetWeaponRange(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Mine => 22f,
			WeaponAttackType.Nova => 8.5f,
			WeaponAttackType.Orbital => 16f,
			WeaponAttackType.FlameCone => 13f,
			WeaponAttackType.Rift => 34f,
			WeaponAttackType.Mortar => 42f,
			WeaponAttackType.ChainLightning => 32f,
			WeaponAttackType.VoidSingularity => 34f,
			WeaponAttackType.WrathAura => 8.5f,
			_ => DefaultAimRange,
		};
	}

	private void UpdateWeaponHud()
	{
		if (_statusLabel == null || _weaponLabel == null || _weaponDetailLabel == null)
		{
			return;
		}

		EquipmentItem? weapon = GetCurrentWeapon();
		string encounterState = GetEncounterStatusSummary();
		if (weapon?.Shape == null)
		{
			_statusLabel.Text = $"Threats {ActiveEnemyCount} | {encounterState}";
			_weaponLabel.Text = "No weapon equipped";
			_weaponDetailLabel.Text = string.Empty;
			return;
		}

		ShapeData shape = weapon.Shape;
		float baseDamage = GetWeaponBaseDamage(weapon);
		float cooldown = GetCurrentWeaponCooldownSeconds();
		string mode = _player != null && _player.IsAiming ? "ADS" : "HIP";
		string weaponState = _arsenal.Count <= 1
			? (_usingHubPrimaryWeapon ? "Primary hub weapon" : "Fallback weapon")
			: $"Weapon {_selectedWeaponIndex + 1}/{_arsenal.Count}";
		_statusLabel.Text = IsDefenseMap ? $"Threats {ActiveEnemyCount} | {encounterState}" : $"Threats {ActiveEnemyCount} | {encounterState} | {weaponState}";
		_weaponLabel.Text = $"{shape.DisplayName} | {shape.AttackName} | {mode}";
		_weaponDetailLabel.Text = $"DMG {baseDamage:0} | HITS {Mathf.Max(1, shape.Hits)} | CD {cooldown:0.##}s | {shape.SpecialDescription}";
	}

	private void FireCurrentWeapon(Vector3 origin, Vector3 direction)
	{
		EquipmentItem? weapon = GetCurrentWeapon();
		if (weapon?.Shape == null)
		{
			return;
		}

		FireWeaponAttack(weapon, origin, direction, 1f, false);
		_weaponPolishAudio?.PlayShot(weapon.Shape.AttackType, origin);
	}

	private void FireWeaponAttack(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale, bool isEcho)
	{
		if (weapon.Shape == null)
		{
			return;
		}

		ShapeData shape = weapon.Shape;
		if (TryFireFpsSpecialTransform(weapon, origin, direction, damageScale, isEcho))
		{
			if (!isEcho) { ApplyModulatorOnFireEffects(weapon, origin, direction, damageScale); ApplyFpsWingOnFire(weapon, origin, direction, damageScale); }
			return;
		}
		bool handlesCount = shape.AttackType is WeaponAttackType.Mine or WeaponAttackType.Orbital or WeaponAttackType.Homing or WeaponAttackType.Mortar or WeaponAttackType.ChainLightning or WeaponAttackType.ShadowClone;
		int volleyCount = handlesCount ? 1 : GetFpsOutputCount(weapon);
		Vector3 initialDirection = direction.Normalized();
		for (int output = 0; output < volleyCount; output++)
		{
		 direction = initialDirection.Rotated(Vector3.Up, output == 0 ? 0f : ((output + 1) / 2) * 0.055f * (output % 2 == 0 ? -1f : 1f));
		switch (shape.AttackType)
		{
			case WeaponAttackType.Single:
				FireSquareProjectiles(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Sawblade:
				FireSawblade(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Laser:
				FirePiercingBeam(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Mine:
				DeployHexMines(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Nova:
				FireNovaPulse(weapon, damageScale);
				break;
			case WeaponAttackType.Orbital:
				DeployOrbitals(weapon, damageScale);
				break;
			case WeaponAttackType.Homing:
				FireHomingSeekers(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.FlameCone:
				FireFlameCone(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Rift:
				DeployRift(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Mortar:
				FireMortar(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.ChainLightning:
				FireChainLightning(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.Drill:
				FireDrill(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.VoidSingularity:
				DeployVoidSingularity(weapon, origin, direction, damageScale);
				break;
			case WeaponAttackType.TemporalEcho:
				FireTemporalEcho(weapon, origin, direction, damageScale, isEcho);
				break;
			case WeaponAttackType.WrathAura:
				FireWrathAuraBurst(weapon, damageScale);
				break;
			case WeaponAttackType.ShadowClone:
				SpawnShadowClones(weapon, damageScale);
				break;
			default:
				FireSingleShot(weapon, origin, direction, damageScale);
				break;
		}
		}
		ApplyFpsSpecialOnFire(weapon, origin, initialDirection, damageScale, isEcho);

		if (!isEcho)
		{
			ApplyModulatorOnFireEffects(weapon, origin, direction, damageScale);
			ApplyFpsWingOnFire(weapon, origin, initialDirection, damageScale);
		}
	}

	private float GetScaledWeaponDamage(EquipmentItem weapon, float damageScale = 1f)
	{
		return GetWeaponBaseDamage(weapon) * Mathf.Max(0f, damageScale);
	}

	private void FireSingleShot(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale = 1f)
		=> FireSingleShotWithEcho(weapon, origin, direction, damageScale, false);

	private void FireSingleShotWithEcho(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale, bool isEcho)
	{
		float damage = GetScaledWeaponDamage(weapon, damageScale);
		int hitCount = 1 + ResolveFpsCount(weapon, FpsCountPurpose.EchoPierce, WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(weapon));
		Vector3 end = origin + direction.Normalized() * GetWeaponRange(WeaponAttackType.Single) * GetFpsRangeScale(weapon);
		if (TryRaycastWorldBlock(origin, end, out Vector3 block)) end = block;
		List<IFpsCombatEnemy> hits = CollectEnemiesAlongSegment(origin, end, 0.04f * GetFpsAreaScale(weapon), null);
		if (weapon.Shape?.AttackType == WeaponAttackType.TemporalEcho)
		{
			Vector3 visualEnd = end;
			if (hits.Count > 0 && TryEnemySegmentHit(hits[Mathf.Min(hitCount, hits.Count) - 1], origin, end,
				0.04f * GetFpsAreaScale(weapon), out float distance)) visualEnd = origin + direction.Normalized() * distance;
			_weaponPolish?.Echo(ResolvePolishMuzzle(origin), visualEnd, PolishRarity(weapon), isEcho);
			if (isEcho) _weaponPolishAudio?.PlayActivation(WeaponAttackType.TemporalEcho, origin);
		}
		for (int hit = 0; hit < Mathf.Min(hitCount, hits.Count); hit++)
		{
			IFpsCombatEnemy enemy = hits[hit];
			DealDirectionalDamageToEnemy(enemy, damage, enemy.HeadPosition, weapon, false, true, direction);
		}
	}

	private void FireSawblade(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float projectileSpeedMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon);
		float durationMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
		int extraPierce = ResolveFpsCount(weapon, FpsCountPurpose.SawPierce, WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(weapon));
		SpawnPlayerProjectile(
			PlayerProjectileKind.Sawblade,
			origin + (direction * 1.2f),
			direction,
			SawbladeSpeed * projectileSpeedMultiplier,
			GetScaledWeaponDamage(weapon, damageScale) * 1.15f,
			0.9f,
			SawbladeLife * durationMultiplier * GetFpsRangeScale(weapon),
			Mathf.Max(2, weapon.Shape?.Hits ?? 2) + extraPierce,
			0f,
			GetWeaponAccentColor(weapon.Shape!.AttackType),
			weapon,
			true);
	}

	private void FirePiercingBeam(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		Vector3 end = ResolveAimPoint(origin, direction, 78f * GetFpsRangeScale(weapon));
		DamageEnemiesAlongSegment(origin, end, 1.10f * GetFpsAreaScale(weapon), GetScaledWeaponDamage(weapon, damageScale) * 1.25f, weapon, true);
		SpawnSustainedFpsAttack(ZoneEffectKind.Beam, weapon, origin, direction, origin.DistanceTo(end), GetScaledWeaponDamage(weapon, damageScale) * 0.3f);
		if (WeaponPolishEnabled)
			_weaponPolish?.Impact(WeaponAttackType.Laser, end, -direction, PolishRarity(weapon), .8f);
		else SpawnPulse(end, 0.6f, 0.16f, GetWeaponAccentColor(weapon.Shape!.AttackType));
	}

	private void DeployHexMines(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		int mineCount = GetFpsOutputCount(weapon, 3);
		Vector3 center = ResolveAimPoint(origin, direction, 18f * GetFpsRangeScale(weapon));
		center = ResolveDefenseGroundPosition(center); center.Y += 0.3f;
		for (int mineIndex = 0; mineIndex < mineCount; mineIndex++)
		{
			float angle = (mineIndex / (float)mineCount) * Mathf.Tau + _weaponRng.RandfRange(-0.18f, 0.18f);
			Vector3 offset = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
			Vector3 point = ResolveDefenseGroundPosition(center + offset * 1.6f); point.Y += 0.3f;
			SpawnMine(point, GetScaledWeaponDamage(weapon, damageScale) * 1.05f, GetWeaponAccentColor(weapon.Shape!.AttackType), weapon, true);
		}
	}

	private void FireNovaPulse(EquipmentItem weapon, float damageScale)
	{
		float radius = 8.5f * GetFpsAreaScale(weapon) * GetFpsRangeScale(weapon);
		DamageEnemiesInExplosion(_player.GlobalPosition, radius, GetScaledWeaponDamage(weapon, damageScale) * 1.35f, weapon, true);
		SpawnPolishedPulse(WeaponAttackType.Nova, _player.GlobalPosition, radius, 0.32f, GetWeaponAccentColor(weapon.Shape!.AttackType), weapon);
	}

	private void DeployOrbitals(EquipmentItem weapon, float damageScale)
	{
		Color color = GetWeaponAccentColor(weapon.Shape!.AttackType);
		float damage = GetScaledWeaponDamage(weapon, damageScale) * 0.85f;
		int orbitalCount = GetFpsOutputCount(weapon, 3);
		float durationMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
		for (int orbitalIndex = 0; orbitalIndex < orbitalCount; orbitalIndex++)
		{
			StandardMaterial3D material = CreateEffectMaterial(color, 1.45f);
			MeshInstance3D mesh = CreateEffectMesh(new BoxMesh { Size = new Vector3(0.52f, 0.52f, 0.52f) }, material);
			_weaponEffectRoot.AddChild(mesh);
			_playerOrbitals.Add(new OrbitalEffect
			{
				Angle = orbitalIndex / (float)orbitalCount * Mathf.Tau,
				Duration = 8f * durationMultiplier,
				OrbitRadius = 3.0f * GetFpsRangeScale(weapon) * WeaponModulatorSharedRuntime.GetWeaponItemOrbitRadiusScalar(weapon),
				AngularSpeed = 1.85f,
				Damage = damage,
				SourceItem = weapon,
				TriggerModulatorOnHitEffects = _fpsSpecialDamageDepth == 0,
				Mesh = mesh,
				Material = material,
			});
		}
	}

	private void FireHomingSeekers(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		int count = GetFpsOutputCount(weapon, 2);
		float projectileSpeedMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon);
		float durationMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
		float homingStrengthMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemHomingStrengthMultiplier(weapon);
		Color color = GetWeaponAccentColor(weapon.Shape!.AttackType);
		for (int seekerIndex = 0; seekerIndex < count; seekerIndex++)
		{
			float yawOffset = count == 1 ? 0f : Mathf.Lerp(-0.10f, 0.10f, seekerIndex / (float)(count - 1));
			Vector3 seekerDirection = direction.Rotated(Vector3.Up, yawOffset).Normalized();
			SpawnPlayerProjectile(
				PlayerProjectileKind.Homing,
				origin + (seekerDirection * 0.9f),
				seekerDirection,
				HomingSpeed * projectileSpeedMultiplier,
				GetScaledWeaponDamage(weapon, damageScale) * 1.05f,
				0.38f,
				5f * durationMultiplier * GetFpsRangeScale(weapon),
				1 + ResolveFpsCount(weapon, FpsCountPurpose.SeekerPierce, WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(weapon)),
				4.5f * homingStrengthMultiplier,
				color,
				weapon,
				true);
		}
	}

	private void FireFlameCone(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float range = 13f * GetFpsRangeScale(weapon) * WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon);
		DamageEnemiesInCone(origin, direction, range, Mathf.Clamp(0.42f * GetFpsAreaScale(weapon), 0.04f, 1.5f), GetScaledWeaponDamage(weapon, damageScale) * 1.05f, weapon, true);
		SpawnSustainedFpsAttack(ZoneEffectKind.Flame, weapon, origin, direction, range, GetScaledWeaponDamage(weapon, damageScale) * 0.3f);
		SpawnPolishedPulse(WeaponAttackType.FlameCone, origin + (direction * 4.5f), 4.0f, 0.20f, GetWeaponAccentColor(weapon.Shape!.AttackType), weapon);
	}

	private void DeployRift(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float durationMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
		Vector3 point = ResolveAimPoint(origin, direction, 34f * GetFpsRangeScale(weapon));
		SpawnZone(ZoneEffectKind.Rift, point, 4.2f * GetFpsAreaScale(weapon), 3.1f * durationMultiplier, GetScaledWeaponDamage(weapon, damageScale) * 0.55f, 0f, GetWeaponAccentColor(weapon.Shape!.AttackType), false, weapon, true);
	}

	private void FireMortar(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float projectileSpeedMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon);
		Vector3 target = ResolveAimPoint(origin, direction, 42f * GetFpsRangeScale(weapon));
		int count = GetFpsOutputCount(weapon);
		for (int output = 0; output < count; output++)
		{
		StandardMaterial3D material = CreateEffectMaterial(GetWeaponAccentColor(weapon.Shape!.AttackType), 1.55f);
		MeshInstance3D mesh = CreateEffectMesh(new SphereMesh { Radius = 0.28f, Height = 0.56f }, material);
		_weaponEffectRoot.AddChild(mesh);
		_playerMortars.Add(new MortarEffect
		{
			Start = origin,
			Target = target + new Vector3(output == 0 ? 0f : (output % 2 == 0 ? -1f : 1f) * ((output + 1) / 2) * 2.2f, 0f, 0f),
			FlightTime = Mathf.Clamp(MortarFlightTime / Mathf.Max(0.1f, projectileSpeedMultiplier), 0.2f, 2.5f),
			Damage = GetScaledWeaponDamage(weapon, damageScale) * 1.60f,
			Radius = 5.2f * GetFpsAreaScale(weapon),
			SourceItem = weapon,
			TriggerModulatorOnHitEffects = _fpsSpecialDamageDepth == 0,
			Mesh = mesh,
			Material = material,
		});
		}
	}

	private void FireChainLightning(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float damage = GetScaledWeaponDamage(weapon, damageScale);
		HashSet<IFpsCombatEnemy> visited = new();
		IFpsCombatEnemy? target = ResolveAttackTarget(origin, direction, 32f * GetFpsRangeScale(weapon));
		IFpsCombatEnemy? original = target;
		float initialDamage = damage;
		Vector3 chainOrigin = origin;
		int chainsRemaining = Mathf.Clamp(2 + (weapon.Shape?.Hits ?? 2) + ResolveFpsCount(weapon, FpsCountPurpose.PrimaryChains,
			WeaponModulatorSharedRuntime.GetWeaponItemProjectileCountValue(weapon)
			+ WeaponModulatorSharedRuntime.GetWeaponItemPassiveCountValue(weapon, ModulatorEffectType.ChainCountAdd)), 2, 32);
		while (target != null && chainsRemaining-- > 0)
		{
			_weaponPolish?.Arc(ResolvePolishMuzzle(chainOrigin), target.HeadPosition, PolishRarity(weapon));
			DealDirectionalDamageToEnemy(target, damage, target.HeadPosition, weapon, false, true, target.HeadPosition - chainOrigin);
			visited.Add(target);
			SpawnPolishedPulse(WeaponAttackType.ChainLightning, target.HeadPosition, 0.9f, 0.12f, GetWeaponAccentColor(weapon.Shape!.AttackType), weapon);
			chainOrigin = target.HeadPosition;
			damage *= 0.78f;
			target = FindNearestEnemy(chainOrigin, 11f * GetFpsRangeScale(weapon) * GetFpsAreaScale(weapon), visited, true);
		}
		OnFpsChainCompletedFromOrigin(original, weapon, initialDamage, original != null && chainOrigin.DistanceSquaredTo(original.HeadPosition) > 0.000001f ? chainOrigin : origin);
	}

	private void FireDrill(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float projectileSpeedMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon);
		int extraPierce = ResolveFpsCount(weapon, FpsCountPurpose.DrillPierce, WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(weapon));
		SpawnPlayerProjectile(
			PlayerProjectileKind.Drill,
			origin + (direction * 1.0f),
			direction,
			DrillSpeed * projectileSpeedMultiplier,
			GetScaledWeaponDamage(weapon, damageScale) * 1.20f,
			0.48f,
			DrillLife * GetFpsDurationScale(weapon) * GetFpsRangeScale(weapon),
			6 + extraPierce,
			0f,
			GetWeaponAccentColor(weapon.Shape!.AttackType),
			weapon,
			true);
	}

	private void DeployVoidSingularity(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float durationMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
		Vector3 point = ResolveAimPoint(origin, direction, 34f * GetFpsRangeScale(weapon));
		SpawnZone(ZoneEffectKind.Void, point, 6.6f * GetFpsAreaScale(weapon), 3.8f * durationMultiplier, GetScaledWeaponDamage(weapon, damageScale) * 0.48f, 22f * WeaponModulatorSharedRuntime.GetWeaponItemPullStrengthMultiplier(weapon), GetWeaponAccentColor(weapon.Shape!.AttackType), false, weapon, true);
	}

	private void FireTemporalEcho(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale, bool isEcho)
	{
		FireSingleShotWithEcho(weapon, origin, direction, 0.75f * damageScale, isEcho);
		if (isEcho)
		{
			return;
		}

		_playerEchoes.Add(new EchoEffect
		{
			Delay = 0.55f,
			// Each base record already represents one output with its authored spread.
			ReplaySingleTemporalOutput = true,
			Weapon = weapon,
			Origin = origin,
			Direction = direction,
			DamageScale = 0.55f * Mathf.Max(0f, damageScale) * WeaponModulatorSharedRuntime.GetWeaponItemCloneOrEchoPowerMultiplier(weapon),
		});
	}

	private void FireWrathAuraBurst(EquipmentItem weapon, float damageScale)
	{
		float missingHealthRatio = 1f - (_player.CurrentHealth / Mathf.Max(0.01f, _player.MaxHealth));
		float damage = GetScaledWeaponDamage(weapon, damageScale) * (0.60f + missingHealthRatio * 1.10f);
		DamageEnemiesInExplosion(_player.GlobalPosition, 4.6f * GetFpsAreaScale(weapon) * GetFpsRangeScale(weapon), damage, weapon, true);
		SpawnPolishedPulse(WeaponAttackType.WrathAura, _player.GlobalPosition, 4.6f * GetFpsAreaScale(weapon) * GetFpsRangeScale(weapon),
			0.25f, GetWeaponAccentColor(weapon.Shape!.AttackType), weapon);
	}

	private void SpawnShadowClones(EquipmentItem weapon, float damageScale)
	{
		Color color = GetWeaponAccentColor(weapon.Shape!.AttackType);
		float damage = GetScaledWeaponDamage(weapon, damageScale) * 0.65f * WeaponModulatorSharedRuntime.GetWeaponItemCloneOrEchoPowerMultiplier(weapon);
		float durationMultiplier = WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
		int count = GetFpsOutputCount(weapon, 2);
		for (int cloneIndex = 0; cloneIndex < count; cloneIndex++)
		{
			StandardMaterial3D material = CreateEffectMaterial(color, 1.20f, true);
			material.AlbedoColor = new Color(color.R, color.G, color.B, 0.42f);
			MeshInstance3D mesh = CreateEffectMesh(new BoxMesh { Size = new Vector3(0.42f, 0.82f, 0.22f) }, material);
			mesh.Scale *= GetFpsAreaScale(weapon);
			_weaponEffectRoot.AddChild(mesh);
			_playerClones.Add(new CloneEffect
			{
				Angle = cloneIndex / (float)count * Mathf.Tau,
				Duration = CloneDuration * durationMultiplier,
				FireCooldown = cloneIndex * 0.2f,
				OrbitRadius = CloneOrbitRadius,
				Damage = damage,
				SourceItem = weapon,
				TriggerModulatorOnHitEffects = _fpsSpecialDamageDepth == 0,
				Mesh = mesh,
				Material = material,
			});
		}
	}

	private void TickPlayerProjectiles(float dt)
	{
		for (int projectileIndex = _playerProjectiles.Count - 1; projectileIndex >= 0; projectileIndex--)
		{
			PlayerProjectileEffect projectile = _playerProjectiles[projectileIndex];
			projectile.Life -= dt;
			if (projectile.Life <= 0f)
			{
				if (!TryReturnFpsSaw(projectile)) { RemovePlayerProjectile(projectileIndex); continue; }
			}

			if (projectile.Kind == PlayerProjectileKind.Homing)
			{
				IFpsCombatEnemy? target = ResolveFpsSeekerTarget(projectile);
				if (target != null)
				{
					Vector3 toTarget = (target.HeadPosition - projectile.Position).Normalized();
					projectile.Direction = projectile.Direction.Lerp(toTarget, Mathf.Clamp(projectile.TurnRate * dt, 0f, 1f)).Normalized();
				}
			}

			Vector3 start = projectile.Position;
			Vector3 previousVisual = projectile.Mesh.GlobalPosition;
			Vector3 end = start + (projectile.Direction * projectile.Speed * dt);
			if (TryRaycastWorldBlockWithNormal(start, end, out Vector3 hitPoint, out Vector3 hitNormal))
			{
				ShowPolishedWorldHit(projectile, start, hitPoint, hitNormal);
				if (projectile.RemainingBounces > 0 && hitNormal.LengthSquared() > 0.5f)
				{
					projectile.RemainingBounces--;
					projectile.Direction = projectile.Direction.Bounce(hitNormal).Normalized();
					projectile.Position = hitPoint + hitNormal * 0.06f;
					projectile.Mesh.GlobalPosition = projectile.Position;
					continue;
				}
				projectile.Position = hitPoint;
				projectile.Mesh.GlobalPosition = hitPoint;
				RemovePlayerProjectile(projectileIndex);
				continue;
			}

			projectile.Position = end;
			projectile.Mesh.GlobalPosition = projectile.Position;
			UpdatePolishedProjectile(projectile, dt);
			TickFpsProjectileSpecial(projectile, start, dt);

			List<IFpsCombatEnemy> hits = CollectEnemiesAlongSegment(start, end, projectile.Radius, projectile.HitEnemies);
			{
				Vector3 visualEnd = projectile.Mesh.GlobalPosition;
				if (projectile.RemainingHits > 0 && hits.Count >= projectile.RemainingHits &&
					TryEnemySegmentHit(hits[projectile.RemainingHits - 1], start, end, projectile.Radius, out float stopDistance))
					visualEnd = start + (end - start).Normalized() * stopDistance;
				_weaponPolish?.ProjectileTrail(ProjectilePolishType(projectile.Kind),
					previousVisual, visualEnd, PolishRarity(projectile.SourceItem), projectile.Radius);
			}
			bool consumed = false;
			for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
			{
				IFpsCombatEnemy enemy = hits[hitIndex];
				DealDirectionalDamageToEnemy(enemy, projectile.Damage, enemy.HeadPosition, projectile.SourceItem, false, projectile.TriggerModulatorOnHitEffects, end - start);
				projectile.HitEnemies.Add(enemy);
				projectile.RemainingHits--;
				if (projectile.RemainingHits <= 0)
				{
					OnFpsProjectileExhausted(projectile);
					if (!TryReturnFpsSaw(projectile)) RemovePlayerProjectile(projectileIndex);
					consumed = true;
					break;
				}
			}

			if (consumed)
			{
				continue;
			}
		}
	}

	private void TickMines(float dt)
	{
		for (int mineIndex = _playerMines.Count - 1; mineIndex >= 0; mineIndex--)
		{
			MineEffect mine = _playerMines[mineIndex];
			mine.VisualAge += dt;
			mine.Life -= dt;
			mine.ArmTime -= dt;
			if (mine.Life <= 0f)
			{
				RemoveMine(mineIndex);
				continue;
			}

			bool armed = mine.ArmTime <= 0f;
			mine.Material.EmissionEnergyMultiplier = armed ? 2.1f : 0.9f;
			mine.Mesh.Visible = !(_weaponPolish?.DrawMine(mine.Position, mine.TriggerRadius,
				Mathf.Clamp(1f - mine.ArmTime / MineArmTime, 0f, 1f), mine.VisualAge, PolishRarity(mine.SourceItem)) ?? false);
			if (!armed || GetFpsTransformConfig(mine.SourceItem, "command_cluster") != null)
			{
				continue;
			}

			if (FindNearestEnemy(mine.Position, mine.TriggerRadius, null, false) == null)
			{
				continue;
			}

			DamageEnemiesInExplosion(mine.Position, mine.ExplosionRadius, mine.Damage, mine.SourceItem, mine.TriggerModulatorOnHitEffects);
			OnFpsMineDetonated(mine);
			SpawnPolishedPulse(WeaponAttackType.Mine, mine.Position, mine.ExplosionRadius, 0.20f, mine.Material.Emission, mine.SourceItem);
			RemoveMine(mineIndex);
		}
	}

	private void TickZones(float dt)
	{
		for (int zoneIndex = _playerZones.Count - 1; zoneIndex >= 0; zoneIndex--)
		{
			ZoneEffect zone = _playerZones[zoneIndex];
			zone.Age += dt;
			zone.Tick -= dt;
			if (zone.FollowPlayer)
			{
				zone.Position = _player.GlobalPosition + Vector3.Up * 0.2f;
			}

			zone.Mesh.GlobalPosition = zone.Position;
			float scale = zone.Kind == ZoneEffectKind.Pulse
				? Mathf.Lerp(0.3f, zone.Radius, Mathf.Clamp(zone.Age / Mathf.Max(0.01f, zone.Duration), 0f, 1f))
				: zone.Radius;
			zone.Mesh.Scale = new Vector3(scale, Mathf.Max(0.08f, scale * 0.12f), scale);
			if (zone.Kind == ZoneEffectKind.Void) PullEnemiesToward(zone.Position, zone.Radius, zone.PullStrength, dt);
			TickFpsZoneSpecial(zone, dt);
			zone.Mesh.Visible = !DrawPolishedZone(zone);

			if (zone.Tick <= 0f && zone.TickInterval > 0f)
			{
				zone.Tick = zone.TickInterval;
				switch (zone.Kind)
				{
					case ZoneEffectKind.Rift:
						DamageEnemiesInRadius(zone.Position, zone.Radius, zone.DamagePerTick, zone.SourceItem, zone.TriggerModulatorOnHitEffects);
						if (zone.SourceItem?.Shape?.AttackType == WeaponAttackType.Rift)
						{
							_weaponPolish?.RiftPulse(zone.Position, zone.Radius, PolishRarity(zone.SourceItem));
							_weaponPolishAudio?.PlayRiftPulse(zone.Position);
						}
						break;
					case ZoneEffectKind.Void:
						DamageEnemiesInRadius(zone.Position, zone.Radius, zone.DamagePerTick, zone.SourceItem, zone.TriggerModulatorOnHitEffects);
						break;
					case ZoneEffectKind.Flame:
						DamageEnemiesInCone(zone.Position, zone.Direction, zone.Range, Mathf.Clamp(zone.Radius, 0.04f, 1.5f), zone.DamagePerTick, zone.SourceItem, false);
						break;
					case ZoneEffectKind.Beam:
						DamageEnemiesAlongSegment(zone.Position, zone.Position + zone.Direction * zone.Range, zone.Radius, zone.DamagePerTick, zone.SourceItem, false);
						break;
				}
			}

			if (zone.Age < zone.Duration)
			{
				continue;
			}

			OnFpsZoneExpired(zone);
			RemoveZone(zoneIndex);
		}
	}

	private void TickOrbitals(float dt)
	{
		for (int orbitalIndex = _playerOrbitals.Count - 1; orbitalIndex >= 0; orbitalIndex--)
		{
			OrbitalEffect orbital = _playerOrbitals[orbitalIndex];
			orbital.Age += dt;
			if (orbital.Age >= orbital.Duration)
			{
				RemoveOrbital(orbitalIndex);
				continue;
			}

			Vector3 previousOrbitalPosition = orbital.HasTravelSample ? orbital.Mesh.GlobalPosition
				: _player.GlobalPosition + new Vector3(Mathf.Cos(orbital.Angle), 0.65f, Mathf.Sin(orbital.Angle)) * orbital.OrbitRadius;
			orbital.Angle += orbital.AngularSpeed * dt;
			Vector3 offset = new(Mathf.Cos(orbital.Angle), 0.65f, Mathf.Sin(orbital.Angle));
			orbital.Mesh.GlobalPosition = _player.GlobalPosition + (offset * orbital.OrbitRadius);
			orbital.HasTravelSample = true;
			orbital.Mesh.RotateY(dt * 5f);
			TickFpsOrbitalSpecial(orbital, dt);
			orbital.Mesh.Visible = !(_weaponPolish?.DrawOrbital(orbital.Mesh.GlobalPosition, orbital.Age,
				PolishRarity(orbital.SourceItem), GetFpsAreaScale(orbital.SourceItem)) ?? false);

			float hitRadius = OrbitalHitRadius * GetFpsAreaScale(orbital.SourceItem);
			foreach (IFpsCombatEnemy enemy in QueryCombatRadius(orbital.Mesh.GlobalPosition, hitRadius))
			{
				if (!enemy.IsAlive)
				{
					continue;
				}

				if (!FpsSphereIntersectsEnemy(enemy, orbital.Mesh.GlobalPosition, hitRadius))
				{
					continue;
				}

				if (orbital.LastHitAt.TryGetValue(enemy, out float lastHitAge) && orbital.Age - lastHitAge < OrbitalHitCooldown / GetFpsTickRate(orbital.SourceItem))
				{
					continue;
				}

				orbital.LastHitAt[enemy] = orbital.Age;
				Vector3 orbitalTravel = orbital.Mesh.GlobalPosition - previousOrbitalPosition;
				DealDirectionalDamageToEnemy(enemy, orbital.Damage, enemy.HeadPosition, orbital.SourceItem, false, orbital.TriggerModulatorOnHitEffects,
					orbitalTravel.LengthSquared() > 0.000001f ? orbitalTravel : enemy.CollisionCenter - orbital.Mesh.GlobalPosition);
			}
		}
	}

	private void TickMortars(float dt)
	{
		for (int mortarIndex = _playerMortars.Count - 1; mortarIndex >= 0; mortarIndex--)
		{
			MortarEffect mortar = _playerMortars[mortarIndex];
			mortar.Age += dt;
			if (mortar.Age >= mortar.FlightTime)
			{
				DamageEnemiesInExplosion(mortar.Target, mortar.Radius, mortar.Damage, mortar.SourceItem, mortar.TriggerModulatorOnHitEffects);
				OnFpsMortarImpact(mortar);
				SpawnPolishedPulse(WeaponAttackType.Mortar, mortar.Target, mortar.Radius, 0.22f, mortar.Material.Emission, mortar.SourceItem);
				RemoveMortar(mortarIndex);
				continue;
			}

			float t = mortar.Age / Mathf.Max(0.01f, mortar.FlightTime);
			Vector3 position = mortar.Start.Lerp(mortar.Target, t);
			position.Y += Mathf.Sin(t * Mathf.Pi) * MortarArcHeight;
			mortar.Mesh.GlobalPosition = position;
			Vector3 previous = mortar.HasVisualSample ? mortar.PreviousVisualPosition : mortar.Start;
			Vector3 travel = position - previous;
			mortar.Mesh.Visible = !(_weaponPolish?.DrawMortar(position, travel, mortar.Age, PolishRarity(mortar.SourceItem)) ?? false);
			_weaponPolish?.ProjectileTrail(WeaponAttackType.Mortar, previous, position, PolishRarity(mortar.SourceItem), .28f);
			mortar.PreviousVisualPosition = position;
			mortar.HasVisualSample = true;
		}
	}

	private void TickEchoes(float dt)
	{
		for (int echoIndex = _playerEchoes.Count - 1; echoIndex >= 0; echoIndex--)
		{
			EchoEffect echo = _playerEchoes[echoIndex];
			echo.Delay -= dt;
			if (echo.Delay > 0f)
			{
				continue;
			}

			if (echo.ReplaySingleTemporalOutput)
				FireTemporalEcho(echo.Weapon, echo.Origin, echo.Direction, echo.DamageScale, true);
			else
				FireWeaponAttack(echo.Weapon, echo.Origin, echo.Direction, echo.DamageScale, true);
			_playerEchoes.RemoveAt(echoIndex);
		}
	}

	private void TickClones(float dt)
	{
		for (int cloneIndex = _playerClones.Count - 1; cloneIndex >= 0; cloneIndex--)
		{
			CloneEffect clone = _playerClones[cloneIndex];
			clone.Age += dt;
			clone.FireCooldown -= dt;
			if (clone.Age >= clone.Duration)
			{
				RemoveClone(cloneIndex);
				continue;
			}

			clone.Angle += dt * 1.25f;
			Vector3 orbitOffset = new(Mathf.Cos(clone.Angle), 0.9f, Mathf.Sin(clone.Angle));
			Vector3 clonePosition = _player.GlobalPosition + (orbitOffset * clone.OrbitRadius);
			clone.Mesh.GlobalPosition = clonePosition;
			clone.Mesh.Visible = !(_weaponPolish?.DrawClone(clonePosition, clone.VisualDirection, clone.Age, clone.Duration,
				PolishRarity(clone.SourceItem), GetFpsAreaScale(clone.SourceItem)) ?? false);

			if (clone.FireCooldown > 0f)
			{
				continue;
			}

			IFpsCombatEnemy? target = FindNearestEnemy(clonePosition, 36f * GetFpsRangeScale(clone.SourceItem), null, true);
			if (target == null)
			{
				continue;
			}

			clone.FireCooldown = CloneFireCadence / GetFpsTickRate(clone.SourceItem);
			clone.VisualDirection = (target.HeadPosition - clonePosition).Normalized();
			if (TryFireFpsCloneMimic(clone, clonePosition, target)) continue;
			_weaponPolish?.CloneShot(clonePosition, target.HeadPosition, PolishRarity(clone.SourceItem));
			_weaponPolishAudio?.PlayActivation(WeaponAttackType.ShadowClone, clonePosition);
			DamageEnemiesInRadiusFromOrigin(target.HeadPosition, 0.25f * GetFpsAreaScale(clone.SourceItem), clone.Damage, clone.SourceItem, clone.TriggerModulatorOnHitEffects, clonePosition);
			SpawnPolishedPulse(WeaponAttackType.ShadowClone, target.HeadPosition, 0.6f, 0.12f, clone.Material.Emission, clone.SourceItem);
		}
	}

	private void TickWrathAura(float dt)
	{
		EquipmentItem? weapon = GetCurrentWeapon();
		if (weapon?.Shape?.AttackType != WeaponAttackType.WrathAura)
		{
			_wrathAuraTick = 0f;
			return;
		}

		float missingHealthRatio = 1f - (_player.CurrentHealth / Mathf.Max(0.01f, _player.MaxHealth));
		float radius = (6.2f + missingHealthRatio * 2.0f) * GetFpsAreaScale(weapon) * GetFpsRangeScale(weapon);
		_weaponPolish?.DrawWrath(_player.GlobalPosition, radius, _weaponPolish.SimulationTime, missingHealthRatio, PolishRarity(weapon));
		_wrathAuraTick -= dt;
		if (_wrathAuraTick > 0f)
		{
			return;
		}

		_wrathAuraTick = WrathAuraTickInterval / GetFpsTickRate(weapon);
		float damage = GetWeaponBaseDamage(weapon) * (0.35f + missingHealthRatio * 0.90f);
		DamageEnemiesInRadius(_player.GlobalPosition, radius, damage, weapon, true);
		SpawnPolishedPulse(WeaponAttackType.WrathAura, _player.GlobalPosition, radius, 0.16f, GetWeaponAccentColor(weapon.Shape.AttackType), weapon);
	}

	private void SpawnPlayerProjectile(
		PlayerProjectileKind kind,
		Vector3 origin,
		Vector3 direction,
		float speed,
		float damage,
		float radius,
		float life,
		int hits,
		float turnRate,
		Color color,
		EquipmentItem? sourceItem,
		bool triggerModulatorOnHitEffects)
	{
		if (_weaponEffectRoot == null || !GodotObject.IsInstanceValid(_weaponEffectRoot) || _weaponEffectRoot.IsQueuedForDeletion())
		{
			return;
		}

		StandardMaterial3D material = CreateEffectMaterial(color, 1.60f);
		Mesh mesh = kind switch
		{
			PlayerProjectileKind.Bullet => new BoxMesh { Size = new Vector3(.08f, .08f, .22f) },
			PlayerProjectileKind.Sawblade => FpsWeaponPolishEffects.SawbladeMesh,
			PlayerProjectileKind.Homing => FpsWeaponPolishEffects.HomingMesh,
			PlayerProjectileKind.Drill => FpsWeaponPolishEffects.DrillMesh,
			_ => new SphereMesh { Radius = 0.18f, Height = 0.36f },
		};
		MeshInstance3D meshInstance = CreateEffectMesh(mesh, material);
		Vector3 visualOffset = ProjectileVisualOffset(kind, origin, direction);
		{
			Color tint = FpsWeaponPolishEffects.Tint(ProjectilePolishType(kind), PolishRarity(sourceItem));
			material.AlbedoColor = tint;
			material.Emission = tint;
			material.EmissionEnergyMultiplier = kind == PlayerProjectileKind.Bullet ? .8f : .08f;
			if (kind == PlayerProjectileKind.Sawblade)
			{
				material.AlbedoColor = tint.Lerp(new Color(.30f, .36f, .42f), .65f);
				material.Metallic = .65f;
				material.Roughness = .4f;
			}
			material.VertexColorUseAsAlbedo = true;
		}
		float areaScale = GetFpsAreaScale(sourceItem);
		meshInstance.Scale *= areaScale;

		_weaponEffectRoot.AddChild(meshInstance);
		if (_weaponEffectRoot.IsInsideTree())
		{
			meshInstance.GlobalPosition = origin + visualOffset;
			meshInstance.Basis = FpsWeaponPolishEffects.Facing(direction).Scaled(Vector3.One * areaScale);
			if (kind == PlayerProjectileKind.Sawblade)
				meshInstance.Basis = (FpsWeaponPolishEffects.Facing(direction) * new Basis(Vector3.Right, Mathf.Pi * .43f)).Scaled(Vector3.One * areaScale * .4f);
			else if (kind is PlayerProjectileKind.Homing or PlayerProjectileKind.Drill)
				meshInstance.Basis = FpsWeaponPolishEffects.Facing(direction).Scaled(Vector3.One * areaScale * .08f);
		}
		else
		{
			meshInstance.Position = origin;
		}

		_playerProjectiles.Add(new PlayerProjectileEffect
		{
			Kind = kind,
			Position = origin,
			VisualOffset = visualOffset,
			Direction = direction.Normalized(),
			Speed = speed,
			Life = life,
			Damage = damage,
			Radius = radius * areaScale,
			RemainingHits = Mathf.Max(1, hits),
			TurnRate = turnRate,
			RemainingBounces = sourceItem == null ? 0 : ResolveFpsCount(sourceItem, FpsCountPurpose.Bounces,
				StatGenerator.GetTotalSubstatValue(sourceItem, SubstatType.BounceTimes), 12),
			TriggerModulatorOnHitEffects = triggerModulatorOnHitEffects && _fpsSpecialDamageDepth == 0,
			SourceItem = sourceItem,
			Mesh = meshInstance,
			Material = material,
		});
	}

	private void SpawnMine(Vector3 position, float damage, Color color, EquipmentItem? sourceItem, bool triggerModulatorOnHitEffects)
	{
		if (_weaponEffectRoot == null || !GodotObject.IsInstanceValid(_weaponEffectRoot) || _weaponEffectRoot.IsQueuedForDeletion())
		{
			return;
		}

		StandardMaterial3D material = CreateEffectMaterial(color, 1.10f);
		MeshInstance3D mesh = CreateEffectMesh(new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.38f, Height = 0.12f, RadialSegments = 6 }, material);
		_weaponEffectRoot.AddChild(mesh);
		if (_weaponEffectRoot.IsInsideTree())
		{
			mesh.GlobalPosition = position;
		}
		else
		{
			mesh.Position = position;
		}

		_playerMines.Add(new MineEffect
		{
			Position = position,
			ArmTime = MineArmTime,
			Life = MineLife * GetFpsDurationScale(sourceItem),
			Damage = damage,
			TriggerRadius = MineTriggerRadius * GetFpsAreaScale(sourceItem),
			ExplosionRadius = MineExplosionRadius * GetFpsAreaScale(sourceItem),
			TriggerModulatorOnHitEffects = triggerModulatorOnHitEffects && _fpsSpecialDamageDepth == 0,
			SourceItem = sourceItem,
			Mesh = mesh,
			Material = material,
		});
	}

	private void SpawnZone(
		ZoneEffectKind kind,
		Vector3 position,
		float radius,
		float duration,
		float damagePerTick,
		float pullStrength,
		Color color,
		bool followPlayer,
		EquipmentItem? sourceItem,
		bool triggerModulatorOnHitEffects)
	{
		if (_weaponEffectRoot == null || !GodotObject.IsInstanceValid(_weaponEffectRoot) || _weaponEffectRoot.IsQueuedForDeletion())
		{
			return;
		}

		StandardMaterial3D material = CreateEffectMaterial(color, 1.15f, true);
		material.AlbedoColor = new Color(color.R, color.G, color.B, kind == ZoneEffectKind.Pulse ? 0.20f : 0.14f);
		MeshInstance3D mesh = CreateEffectMesh(new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.08f, RadialSegments = 24 }, material);
		_weaponEffectRoot.AddChild(mesh);
		if (_weaponEffectRoot.IsInsideTree())
		{
			mesh.GlobalPosition = position;
		}
		else
		{
			mesh.Position = position;
		}

		_playerZones.Add(new ZoneEffect
		{
			Kind = kind,
			Position = position,
			FollowPlayer = followPlayer,
			VisualOrigin = position,
			Duration = duration,
			Tick = kind == ZoneEffectKind.Pulse ? float.MaxValue : 0f,
			TickInterval = (kind switch
			{
				ZoneEffectKind.Rift => RiftTickInterval,
				ZoneEffectKind.Void => VoidTickInterval,
				_ => 0f,
			}) / GetFpsTickRate(sourceItem),
			Radius = radius,
			DamagePerTick = damagePerTick,
			PullStrength = pullStrength,
			TriggerModulatorOnHitEffects = triggerModulatorOnHitEffects && _fpsSpecialDamageDepth == 0,
			SourceItem = sourceItem,
			Mesh = mesh,
			Material = material,
		});
	}

	private void SpawnPulse(Vector3 position, float radius, float duration, Color color)
	{
		SpawnZone(ZoneEffectKind.Pulse, position, radius, duration, 0f, 0f, color, false, null, false);
	}

	private IFpsCombatEnemy? ResolveAttackTarget(Vector3 origin, Vector3 direction, float range)
	{
		if (TryRaycastEnemy(origin, direction, range, out IFpsCombatEnemy? directEnemy, out _))
		{
			return directEnemy;
		}

		Vector3 aimPoint = ResolveAimPoint(origin, direction, range);
		return FindNearestEnemy(aimPoint, 6f, null, true);
	}

	private Vector3 ResolveAimPoint(Vector3 origin, Vector3 direction, float range)
	{
		if (_fpsWingAimPoint.HasValue) return _fpsWingAimPoint.Value;
		if (TryRaycastEnemy(origin, direction, range, out _, out Vector3 hit)) return hit;
		Vector3 end = origin + direction * range;
		return TryRaycastWorldBlock(origin, end, out hit) ? hit : end;
	}

	private bool TryRaycastEnemy(Vector3 origin, Vector3 direction, float range, out IFpsCombatEnemy? enemy, out Vector3 hitPoint)
	{
		Vector3 end = origin + direction.Normalized() * range;
		if (TryRaycastWorldBlock(origin, end, out Vector3 block)) end = block;
		List<IFpsCombatEnemy> hits = CollectEnemiesAlongSegment(origin, end, 0f, null);
		if (hits.Count > 0)
		{
			enemy = hits[0];
			TryEnemySegmentHit(enemy, origin, end, 0f, out float distance);
			hitPoint = origin + direction.Normalized() * distance;
			return true;
		}

		enemy = null;
		hitPoint = origin + (direction * range);
		return false;
	}

	private bool TryRaycastWorldBlock(Vector3 start, Vector3 end, out Vector3 hitPoint)
		=> TryRaycastWorldBlockWithNormal(start, end, out hitPoint, out _);

	private bool TryRaycastWorldBlockWithNormal(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal)
	{
		Godot.Collections.Array<Rid> exclude = new() { _player.GetRid() };
		for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
		{
			FpsSliceEnemy enemy = _enemies[enemyIndex];
			if (enemy != null && enemy.IsValid && enemy.IsAlive)
			{
				exclude.Add(enemy.GetRid());
			}
		}

		PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(start, end);
		query.CollideWithAreas = false;
		query.Exclude = exclude;
		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count == 0)
		{
			hitPoint = end;
			hitNormal = Vector3.Zero;
			return false;
		}

		hitPoint = result["position"].AsVector3();
		hitNormal = result["normal"].AsVector3();
		return true;
	}

	private int DamageEnemiesInRadius(Vector3 center, float radius, float damage, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
		=> DamageEnemiesInRadiusFromOrigin(center, radius, damage, sourceItem, triggerModulatorOnHitEffects, center);

	private int DamageEnemiesInRadiusFromOrigin(Vector3 center, float radius, float damage, EquipmentItem? sourceItem, bool triggerModulatorOnHitEffects, Vector3 sourceOrigin)
	{
		int hits = 0;
		foreach (IFpsCombatEnemy enemy in QueryCombatRadius(center, radius))
		{
			if (!enemy.IsAlive)
			{
				continue;
			}

			if (!FpsSphereIntersectsEnemy(enemy, center, radius))
			{
				continue;
			}

			DealDirectionalDamageToEnemy(enemy, damage, enemy.HeadPosition, sourceItem, false, triggerModulatorOnHitEffects, enemy.CollisionCenter - sourceOrigin);
			hits++;
		}

		return hits;
	}

	private int DamageEnemiesInCone(Vector3 origin, Vector3 direction, float range, float halfAngleRadians, float damage, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
	{
		int hits = 0;
		float cosLimit = Mathf.Cos(halfAngleRadians);
		foreach (IFpsCombatEnemy enemy in QueryCombatRadius(origin, range))
		{
			if (!enemy.IsAlive)
			{
				continue;
			}

			Vector3 toEnemy = enemy.HeadPosition - origin;
			float distance = toEnemy.Length();
			if (distance <= 0.001f || distance > range + enemy.CollisionRadius)
			{
				continue;
			}

			if (direction.Dot(toEnemy / distance) < cosLimit)
			{
				continue;
			}

			DealDirectionalDamageToEnemy(enemy, damage, enemy.HeadPosition, sourceItem, false, triggerModulatorOnHitEffects, enemy.CollisionCenter - origin);
			hits++;
		}

		return hits;
	}

	private int DamageEnemiesAlongSegment(Vector3 start, Vector3 end, float halfWidth, float damage, EquipmentItem? sourceItem = null, bool triggerModulatorOnHitEffects = true)
	{
		int hits = 0;
		foreach (IFpsCombatEnemy enemy in QueryCombatSegment(start, end, halfWidth))
		{
			if (!enemy.IsAlive)
			{
				continue;
			}

			if (!TryEnemySegmentHit(enemy, start, end, halfWidth, out _))
			{
				continue;
			}

			DealDirectionalDamageToEnemy(enemy, damage, enemy.HeadPosition, sourceItem, false, triggerModulatorOnHitEffects, end - start);
			hits++;
		}

		return hits;
	}

	private List<IFpsCombatEnemy> CollectEnemiesAlongSegment(Vector3 start, Vector3 end, float radius, HashSet<IFpsCombatEnemy>? excluded)
	{
		List<IFpsCombatEnemy> hits = new();
		foreach (IFpsCombatEnemy enemy in QueryCombatSegment(start, end, radius))
		{
			if (!enemy.IsAlive || (excluded != null && excluded.Contains(enemy)))
			{
				continue;
			}

			if (!TryEnemySegmentHit(enemy, start, end, radius, out _))
			{
				continue;
			}

			hits.Add(enemy);
		}

		hits.Sort((left, right) => { TryEnemySegmentHit(left, start, end, radius, out float a); TryEnemySegmentHit(right, start, end, radius, out float b); return a.CompareTo(b); });
		return hits;
	}

	private IFpsCombatEnemy? FindNearestEnemy(Vector3 origin, float maxRange, HashSet<IFpsCombatEnemy>? excluded, bool requireLineOfSight)
	{
		IFpsCombatEnemy? best = null;
		float bestDistanceSq = maxRange * maxRange;
		foreach (IFpsCombatEnemy enemy in QueryCombatRadius(origin, maxRange))
		{
			if (!enemy.IsAlive || (excluded != null && excluded.Contains(enemy)))
			{
				continue;
			}

			float distanceSq = enemy.GlobalPosition.DistanceSquaredTo(origin);
			if (distanceSq > bestDistanceSq)
			{
				continue;
			}

			if (requireLineOfSight && !HasLineOfSight(origin, enemy))
			{
				continue;
			}

			bestDistanceSq = distanceSq;
			best = enemy;
		}

		return best;
	}

	private bool HasLineOfSight(Vector3 origin, IFpsCombatEnemy enemy)
	{
		return !TryRaycastWorldBlock(origin, enemy.HeadPosition, out _);
	}

	private void PullEnemiesToward(Vector3 center, float radius, float force, float dt)
	{
		foreach (IFpsCombatEnemy enemy in QueryCombatRadius(center, radius))
		{
			if (!enemy.IsAlive)
			{
				continue;
			}

			Vector3 toEnemy = enemy.GlobalPosition - center;
			float distance = toEnemy.Length();
			if (distance <= 0.001f || distance > radius)
			{
				continue;
			}

			Vector3 pullDirection = -toEnemy / distance;
			float strength = force * (1f - distance / radius);
			enemy.MoveBy(pullDirection * strength * dt);
		}
	}

	private void DealDamageToEnemy(
		IFpsCombatEnemy enemy,
		float damage,
		Vector3 hitPosition,
		EquipmentItem? sourceItem,
		bool critical,
		bool triggerModulatorOnHitEffects)
		=> DealDirectionalDamageToEnemy(enemy, damage, hitPosition, sourceItem, critical,
			triggerModulatorOnHitEffects, enemy.CollisionCenter - _player.GlobalPosition);

	private void DealDirectionalDamageToEnemy(
		IFpsCombatEnemy enemy,
		float damage,
		Vector3 hitPosition,
		EquipmentItem? sourceItem,
		bool critical,
		bool triggerModulatorOnHitEffects,
		Vector3 travelDirection)
	{
		if (enemy == null || !enemy.IsAlive || damage <= 0f || !float.IsFinite(damage))
		{
			return;
		}

		triggerModulatorOnHitEffects &= _fpsSpecialDamageDepth == 0;
		bool rolledCritical = false;
		if (triggerModulatorOnHitEffects && sourceItem != null)
		{
			float chance = WeaponModulatorSharedRuntime.GetWeaponItemCritChancePercent(sourceItem);
			if (_arenaDebugOverridesActive) chance += _arenaDebugOverrides.CritChancePercent;
			rolledCritical = critical || _weaponRng.Randf() * 100f < Mathf.Clamp(chance, 0f, 100f);
			if (rolledCritical) damage *= 1.5f + WeaponModulatorSharedRuntime.GetWeaponItemCritDamagePercent(sourceItem) / 100f;
		}
		critical |= rolledCritical;
		float adjustedDamage = ApplyModulatorEnemyDamageMultipliers(enemy, damage, sourceItem);
		if (adjustedDamage <= 0f)
		{
			return;
		}

		float healthBefore = enemy.CurrentHealth;
		float actualDamage = Mathf.Min(adjustedDamage, healthBefore);
		enemy.ApplyDamageFrom(adjustedDamage, travelDirection);
		ShowPolishedEnemyHit(sourceItem, hitPosition, travelDirection, adjustedDamage, healthBefore, critical);
		OnFpsModulatorEnemyDamage(sourceItem, actualDamage);
		OnFpsModulatorSpecialHit(enemy, sourceItem, actualDamage, hitPosition, critical, triggerModulatorOnHitEffects);
		if (triggerModulatorOnHitEffects)
		{
			ApplyModulatorOnEnemyHitEffects(enemy, adjustedDamage, hitPosition, sourceItem, critical);
			if (sourceItem != null)
			{
				float push = 1.4f * (WeaponModulatorSharedRuntime.GetWeaponItemKnockbackMultiplier(sourceItem) - 1f);
				if (push > 0f && enemy.IsAlive) enemy.MoveBy((enemy.GlobalPosition - _player.GlobalPosition).Normalized() * push);
				ApplyFpsRicochet(enemy, actualDamage, sourceItem);
			}
		}
	}

	private void ApplyModulatorOnFireEffects(EquipmentItem sourceWeapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceWeapon, ModulatorEffectTrigger.OnFire, effect =>
		{
			if (!WeaponModulatorSharedRuntime.IsOnFireEchoEffect(effect))
			{
				return;
			}

			int echoCount = WeaponModulatorSharedRuntime.ResolveEchoCount(effect);
			float delay = WeaponModulatorSharedRuntime.ResolveEchoDelaySeconds(effect);
			float echoDamageScale = Mathf.Max(0f, damageScale) * WeaponModulatorSharedRuntime.ResolveEchoDamageScale(effect);
			for (int echoIndex = 0; echoIndex < echoCount; echoIndex++)
			{
				_playerEchoes.Add(new EchoEffect
				{
					Delay = delay + echoIndex * 0.05f,
					Weapon = sourceWeapon,
					Origin = origin,
					Direction = direction,
					DamageScale = echoDamageScale,
				});
			}
		});
	}

	private float ApplyModulatorEnemyDamageMultipliers(IFpsCombatEnemy enemy, float damage, EquipmentItem? sourceItem)
	{
		if (sourceItem == null || damage <= 0f)
		{
			return damage;
		}

		bool enemyIsElite = enemy.Role == FpsEnemyRole.Striker;
		WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.OnHit, effect =>
		{
			if (WeaponModulatorSharedRuntime.IsOnHitApplyMark(effect))
			{
				if (_modulatorMarkedEnemies.TryGetValue(enemy, out ModulatorMarkedEnemyState? markState) && markState.TimeLeft > 0f)
				{
					damage *= Mathf.Max(1f, markState.DamageMultiplier);
				}

				return;
			}

			if (WeaponModulatorSharedRuntime.IsOnHitExecuteLowHealth(effect))
			{
				float hpRatio = enemy.MaxHealth > 0f ? enemy.CurrentHealth / enemy.MaxHealth : 1f;
				if (hpRatio <= WeaponModulatorSharedRuntime.ResolveExecuteThreshold(effect))
				{
					damage *= Mathf.Max(1f, WeaponModulatorSharedRuntime.ResolveExecuteDamageMultiplier(effect));
				}

				return;
			}

			if (WeaponModulatorSharedRuntime.IsOnHitEliteBossHunter(effect))
			{
				float eliteMultiplier = WeaponModulatorSharedRuntime.ResolveEliteBossHunterEliteMultiplier(effect);
				float normalMultiplier = WeaponModulatorSharedRuntime.ResolveEliteBossHunterNormalMultiplier(effect);
				damage *= enemyIsElite ? eliteMultiplier : normalMultiplier;
			}
		});

		return damage;
	}

	private void ApplyModulatorOnEnemyHitEffects(IFpsCombatEnemy enemy, float damage, Vector3 hitPosition, EquipmentItem? sourceItem, bool critical)
	{
		if (sourceItem == null)
		{
			return;
		}

		WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.OnHit, effect =>
		{
			if (WeaponModulatorSharedRuntime.IsOnHitApplyMark(effect))
			{
				ApplyEnemyMark(enemy, effect);
				return;
			}

			if (WeaponModulatorSharedRuntime.IsOnHitChainToNearby(effect))
			{
				TriggerModulatorChainReaction(hitPosition, enemy, damage, critical, sourceItem, effect);
				return;
			}

			if (WeaponModulatorSharedRuntime.IsOnHitLingeringZone(effect))
			{
				SpawnModulatorLingeringZone(hitPosition, damage, critical, sourceItem, effect);
			}
		});
	}

	private void ApplyEnemyMark(IFpsCombatEnemy enemy, WeaponModulatorEffectData effect)
	{
		float timeLeft = WeaponModulatorSharedRuntime.ResolveMarkDurationSeconds(effect);
		float damageMultiplier = WeaponModulatorSharedRuntime.ResolveMarkDamageMultiplier(effect);
		if (_modulatorMarkedEnemies.TryGetValue(enemy, out ModulatorMarkedEnemyState? existing))
		{
			existing.TimeLeft = Mathf.Max(existing.TimeLeft, timeLeft);
			existing.DamageMultiplier = Mathf.Max(existing.DamageMultiplier, damageMultiplier);
			return;
		}

		_modulatorMarkedEnemies[enemy] = new ModulatorMarkedEnemyState
		{
			TimeLeft = timeLeft,
			DamageMultiplier = damageMultiplier,
		};
	}

	private void TriggerModulatorChainReaction(
		Vector3 origin,
		IFpsCombatEnemy? initialTarget,
		float damage,
		bool critical,
		EquipmentItem sourceItem,
		WeaponModulatorEffectData effect)
	{
		int remainingChains = WeaponModulatorSharedRuntime.ResolveChainCount(effect);
		float range = WeaponModulatorSharedRuntime.ResolveChainRange(effect);
		float damageScale = WeaponModulatorSharedRuntime.ResolveChainDamageScale(effect);
		float currentDamage = damage * damageScale;
		Vector3 current = origin;
		HashSet<IFpsCombatEnemy> visited = new();
		if (initialTarget != null)
		{
			visited.Add(initialTarget);
		}

		Color color = GetWeaponAccentColor(sourceItem.Shape?.AttackType ?? WeaponAttackType.Single);
		while (remainingChains > 0 && currentDamage > 0.01f)
		{
			IFpsCombatEnemy? nextEnemy = FindNearestEnemy(current, range, visited, false);
			if (nextEnemy == null)
			{
				break;
			}

			visited.Add(nextEnemy);
			_weaponPolish?.Arc(current, nextEnemy.HeadPosition, PolishRarity(sourceItem));
			DealDirectionalDamageToEnemy(nextEnemy, currentDamage, nextEnemy.HeadPosition, sourceItem, critical, false, nextEnemy.HeadPosition - current);
			SpawnPolishedPulse(WeaponAttackType.ChainLightning, nextEnemy.HeadPosition, 0.75f, 0.12f, color, sourceItem);
			current = nextEnemy.HeadPosition;
			currentDamage *= 0.85f;
			remainingChains--;
		}
	}

	private void SpawnModulatorLingeringZone(Vector3 position, float damage, bool critical, EquipmentItem sourceItem, WeaponModulatorEffectData effect)
	{
		if (_weaponEffectRoot == null || !GodotObject.IsInstanceValid(_weaponEffectRoot) || _weaponEffectRoot.IsQueuedForDeletion())
		{
			return;
		}

		float damageMultiplier = WeaponModulatorSharedRuntime.ResolveLingeringZoneDamageScale(effect);
		float radius = WeaponModulatorSharedRuntime.ResolveLingeringZoneRadius(effect);
		float duration = WeaponModulatorSharedRuntime.ResolveLingeringZoneDurationSeconds(effect);
		Color color = GetWeaponAccentColor(sourceItem.Shape?.AttackType ?? WeaponAttackType.Single);

		StandardMaterial3D material = CreateEffectMaterial(color, 1.18f, true);
		material.AlbedoColor = new Color(color.R, color.G, color.B, 0.18f);
		MeshInstance3D mesh = CreateEffectMesh(new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.08f, RadialSegments = 24 }, material);
		_weaponEffectRoot.AddChild(mesh);
		if (_weaponEffectRoot.IsInsideTree())
		{
			mesh.GlobalPosition = position;
		}
		else
		{
			mesh.Position = position;
		}

		_modulatorLingeringZones.Add(new ModulatorLingeringZoneEffect
		{
			Position = position,
			Duration = duration,
			Radius = radius,
			TotalDamage = damage * damageMultiplier,
			IsCritical = critical,
			SourceItem = sourceItem,
			TriggerModulatorOnHitEffects = false,
			Mesh = mesh,
			Material = material,
		});
	}

	private void TickModulatorEffectState(float dt)
	{
		TickMarkedTargets(dt);
		TickModulatorLingeringZones(dt);
	}

	private void TickMarkedTargets(float dt)
	{
		List<IFpsCombatEnemy>? expired = null;
		foreach (KeyValuePair<IFpsCombatEnemy, ModulatorMarkedEnemyState> pair in _modulatorMarkedEnemies)
		{
			IFpsCombatEnemy enemy = pair.Key;
			ModulatorMarkedEnemyState state = pair.Value;
			state.TimeLeft -= dt;
			if (!enemy.IsValid || !enemy.IsAlive || state.TimeLeft <= 0f)
			{
				expired ??= new List<IFpsCombatEnemy>();
				expired.Add(enemy);
			}
		}

		if (expired == null)
		{
			return;
		}

		for (int expiredIndex = 0; expiredIndex < expired.Count; expiredIndex++)
		{
			_modulatorMarkedEnemies.Remove(expired[expiredIndex]);
		}
	}

	private void TickModulatorLingeringZones(float dt)
	{
		for (int zoneIndex = _modulatorLingeringZones.Count - 1; zoneIndex >= 0; zoneIndex--)
		{
			ModulatorLingeringZoneEffect zone = _modulatorLingeringZones[zoneIndex];
			zone.Age += dt;
			zone.Tick -= dt;

			if (zone.Age >= zone.Duration)
			{
				zone.Mesh.QueueFree();
				_modulatorLingeringZones.RemoveAt(zoneIndex);
				continue;
			}

			if (zone.Tick <= 0f)
			{
				zone.Tick = ModulatorLingeringZoneTickInterval;
				float tickDamage = zone.TotalDamage * ModulatorLingeringZoneTickInterval / Mathf.Max(0.01f, zone.Duration);
				DamageEnemiesInRadius(zone.Position, zone.Radius, tickDamage, zone.SourceItem, zone.TriggerModulatorOnHitEffects);
			}

			float fadeOut = Mathf.Min(1f, (zone.Duration - zone.Age) / Mathf.Max(0.01f, zone.Duration));
			float pulse = 0.92f + Mathf.Sin(zone.Age * 9f) * 0.08f;
			zone.Mesh.Scale = new Vector3(zone.Radius * pulse, 1f, zone.Radius * pulse);
			zone.Mesh.Rotation = new Vector3(0f, zone.Age * 2.4f, 0f);
			SetEffectMaterialAlpha(zone.Material, 0.18f * fadeOut);
		}
	}

	private void ResetModulatorEffectState()
	{
		ResetFpsModulatorSpecials();
		ResetFpsWings();
		for (int zoneIndex = 0; zoneIndex < _modulatorLingeringZones.Count; zoneIndex++)
		{
			_modulatorLingeringZones[zoneIndex].Mesh.QueueFree();
		}

		_modulatorLingeringZones.Clear();
		_modulatorMarkedEnemies.Clear();
	}

	private void ResetWeaponEffectRuntimeState()
	{
		_weaponPolish?.Clear();
		_weaponPolishAudio?.Clear();
		for (int projectileIndex = 0; projectileIndex < _playerProjectiles.Count; projectileIndex++)
		{
			_playerProjectiles[projectileIndex].Mesh.QueueFree();
		}

		for (int mineIndex = 0; mineIndex < _playerMines.Count; mineIndex++)
		{
			_playerMines[mineIndex].Mesh.QueueFree();
		}

		for (int zoneIndex = 0; zoneIndex < _playerZones.Count; zoneIndex++)
		{
			_playerZones[zoneIndex].Mesh.QueueFree();
		}

		for (int orbitalIndex = 0; orbitalIndex < _playerOrbitals.Count; orbitalIndex++)
		{
			_playerOrbitals[orbitalIndex].Mesh.QueueFree();
		}

		for (int mortarIndex = 0; mortarIndex < _playerMortars.Count; mortarIndex++)
		{
			_playerMortars[mortarIndex].Mesh.QueueFree();
		}

		for (int cloneIndex = 0; cloneIndex < _playerClones.Count; cloneIndex++)
		{
			_playerClones[cloneIndex].Mesh.QueueFree();
		}

		_playerProjectiles.Clear();
		_playerMines.Clear();
		_playerZones.Clear();
		_playerOrbitals.Clear();
		_playerMortars.Clear();
		_playerEchoes.Clear();
		_playerClones.Clear();
		_wrathAuraTick = 0f;
		ResetModulatorEffectState();
	}

	private static void SetEffectMaterialAlpha(StandardMaterial3D material, float alpha)
	{
		Color color = material.AlbedoColor;
		material.AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(alpha, 0f, 1f));
	}

	private static float DistancePointToSegment(Vector3 point, Vector3 start, Vector3 end)
	{
		Vector3 segment = end - start;
		float segmentLengthSq = segment.LengthSquared();
		if (segmentLengthSq <= 0.000001f)
		{
			return point.DistanceTo(start);
		}

		float t = Mathf.Clamp((point - start).Dot(segment) / segmentLengthSq, 0f, 1f);
		Vector3 closest = start + (segment * t);
		return point.DistanceTo(closest);
	}

	private static bool TryGetHotkeyWeaponIndex(Key keycode, out int weaponIndex)
	{
		weaponIndex = keycode switch
		{
			Key.Key1 => 0,
			Key.Key2 => 1,
			Key.Key3 => 2,
			Key.Key4 => 3,
			Key.Key5 => 4,
			Key.Key6 => 5,
			Key.Key7 => 6,
			Key.Key8 => 7,
			Key.Key9 => 8,
			_ => -1,
		};
		return weaponIndex >= 0;
	}

	private static Color GetWeaponAccentColor(WeaponAttackType attackType)
	{
		return attackType switch
		{
			WeaponAttackType.Single => new Color(0.88f, 0.92f, 1f, 1f),
			WeaponAttackType.Sawblade => new Color(0.98f, 0.74f, 0.26f, 1f),
			WeaponAttackType.Laser => new Color(0.42f, 0.92f, 1f, 1f),
			WeaponAttackType.Mine => new Color(0.96f, 0.58f, 0.28f, 1f),
			WeaponAttackType.Nova => new Color(0.70f, 0.88f, 1f, 1f),
			WeaponAttackType.Orbital => new Color(0.78f, 0.82f, 1f, 1f),
			WeaponAttackType.Homing => new Color(0.62f, 1f, 0.74f, 1f),
			WeaponAttackType.FlameCone => new Color(1f, 0.58f, 0.28f, 1f),
			WeaponAttackType.Rift => new Color(0.96f, 0.54f, 0.88f, 1f),
			WeaponAttackType.Mortar => new Color(0.98f, 0.84f, 0.44f, 1f),
			WeaponAttackType.ChainLightning => new Color(0.58f, 0.88f, 1f, 1f),
			WeaponAttackType.Drill => new Color(0.84f, 0.74f, 0.54f, 1f),
			WeaponAttackType.VoidSingularity => new Color(0.64f, 0.52f, 1f, 1f),
			WeaponAttackType.TemporalEcho => new Color(0.98f, 0.82f, 0.52f, 1f),
			WeaponAttackType.WrathAura => new Color(1f, 0.44f, 0.44f, 1f),
			WeaponAttackType.ShadowClone => new Color(0.84f, 0.62f, 1f, 1f),
			_ => Colors.White,
		};
	}

	private static StandardMaterial3D CreateEffectMaterial(Color color, float emissionScale, bool transparent = false)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = emissionScale,
			Metallic = 0.08f,
			Roughness = 0.24f,
			Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	private static MeshInstance3D CreateEffectMesh(Mesh mesh, StandardMaterial3D material)
	{
		return new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private void RemovePlayerProjectile(int index)
	{
		PlayerProjectileEffect projectile = _playerProjectiles[index];
		projectile.Mesh.QueueFree();
		_playerProjectiles.RemoveAt(index);
	}

	private void RemoveMine(int index)
	{
		MineEffect mine = _playerMines[index];
		mine.Mesh.QueueFree();
		_playerMines.RemoveAt(index);
	}

	private void RemoveZone(int index)
	{
		ZoneEffect zone = _playerZones[index];
		zone.Mesh.QueueFree();
		_playerZones.RemoveAt(index);
	}

	private void RemoveOrbital(int index)
	{
		OrbitalEffect orbital = _playerOrbitals[index];
		orbital.Mesh.QueueFree();
		_playerOrbitals.RemoveAt(index);
	}

	private void RemoveMortar(int index)
	{
		MortarEffect mortar = _playerMortars[index];
		mortar.Mesh.QueueFree();
		_playerMortars.RemoveAt(index);
	}

	private void RemoveClone(int index)
	{
		CloneEffect clone = _playerClones[index];
		clone.Mesh.QueueFree();
		_playerClones.RemoveAt(index);
	}
}
