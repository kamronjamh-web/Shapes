using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private static readonly string[] FpsWingKeys = { "wing_man", "wing_woman", "wing_brother", "wing_sister" };
	private readonly Dictionary<EquipmentItem, float> _fpsWingCooldowns = new();
	private readonly Dictionary<EquipmentItem, EquipmentItem> _fpsWingWomanSources = new();
	private readonly Dictionary<IFpsCombatEnemy, Vector3> _fpsWingPreviousPositions = new();
	private readonly Dictionary<IFpsCombatEnemy, Vector3> _fpsWingVelocities = new();
	private readonly List<IFpsCombatEnemy> _fpsWingRetiredTargets = new();
	private bool _fpsWingFiring;
	private Vector3? _fpsWingAimPoint;
	private float _fpsWingTime;

	private void ResetFpsWings()
	{
		_fpsWingCooldowns.Clear();
		_fpsWingWomanSources.Clear();
		_fpsWingPreviousPositions.Clear();
		_fpsWingVelocities.Clear();
		_fpsWingRetiredTargets.Clear();
		_fpsWingFiring = false;
		_fpsWingAimPoint = null;
		_fpsWingTime = 0f;
	}

	private void ApplyFpsWingOnFire(EquipmentItem primary, Vector3 origin, Vector3 direction, float damageScale)
	{
		if (_fpsWingFiring) return;
		Vector3 aim = ResolveAimPoint(origin, direction, DefaultAimRange);
		for (int slot = 1; slot < _arsenal.Count; slot++)
		{
			EquipmentItem support = _arsenal[slot];
			if (ReferenceEquals(support, primary) || GetFpsWingBehavior(support) != "wing_man" || !FpsWingReady(support)) continue;
			FireFpsWing(support, origin, aim, damageScale);
		}
	}

	private void TickFpsWings(float dt)
	{
		if (!IsCombatRunning() || !_player.IsAlive || dt <= 0f || !float.IsFinite(dt)) return;
		_fpsWingTime += dt;
		EquipmentItem? primary = GetCurrentWeapon();
		bool trackTargets = false;
		for (int slot = 1; slot < _arsenal.Count && !trackTargets; slot++)
		{
			EquipmentItem support = _arsenal[slot];
			string behavior = GetFpsWingBehavior(support);
			trackTargets = support.Shape != null && !ReferenceEquals(support, primary)
				&& behavior is "wing_woman" or "wing_brother" or "wing_sister";
		}
		if (trackTargets)
		{
			_fpsWingRetiredTargets.Clear();
			foreach (IFpsCombatEnemy target in _fpsWingPreviousPositions.Keys)
				if (!target.IsAlive) _fpsWingRetiredTargets.Add(target);
			foreach (IFpsCombatEnemy target in _fpsWingRetiredTargets)
			{
				_fpsWingPreviousPositions.Remove(target);
				_fpsWingVelocities.Remove(target);
			}
			foreach (IFpsCombatEnemy target in _combatEnemies)
			{
				if (!target.IsAlive) continue;
				Vector3 position = target.GlobalPosition;
				Vector3 velocity = _fpsWingPreviousPositions.TryGetValue(target, out Vector3 previous) ? (position - previous) / dt : Vector3.Zero;
				// Only horizontal motion is led; aiming remains above the sampled hill surface.
				velocity.Y = 0f;
				_fpsWingVelocities[target] = velocity.IsFinite() ? velocity.LimitLength(30f) : Vector3.Zero;
				_fpsWingPreviousPositions[target] = position;
			}
		}
		else
		{
			// A newly activated aiming wing starts with zero lead instead of stale inactive motion.
			_fpsWingPreviousPositions.Clear();
			_fpsWingVelocities.Clear();
			_fpsWingRetiredTargets.Clear();
		}
		for (int slot = 1; slot < _arsenal.Count; slot++)
		{
			EquipmentItem support = _arsenal[slot];
			if (_fpsWingCooldowns.TryGetValue(support, out float cooldown)) _fpsWingCooldowns[support] = Mathf.Max(0f, cooldown - dt);
			string behavior = GetFpsWingBehavior(support);
			if (ReferenceEquals(support, primary) || behavior.Length == 0 || behavior == "wing_man" || !FpsWingReady(support)) continue;
			Vector3 origin = _player.HeadPosition;
			if (!TryFindFpsWingTarget(support, origin, behavior, out Vector3 aim, out EquipmentItem shotWeapon, out float scale)) continue;
			if (behavior == "wing_brother") origin = GetFpsWingBrotherOrigin(slot, aim);
			FireFpsWing(support, origin, aim, scale, shotWeapon);
		}
	}

	private bool FpsWingReady(EquipmentItem item) => item.Shape != null && (!_fpsWingCooldowns.TryGetValue(item, out float cooldown) || cooldown <= 0f);

	private void FireFpsWing(EquipmentItem support, Vector3 origin, Vector3 aim, float damageScale, EquipmentItem? shotWeapon = null)
	{
		Vector3 direction = aim - origin;
		if (_fpsWingFiring || direction.LengthSquared() <= 0.000001f) return;
		_fpsWingFiring = true;
		_fpsWingAimPoint = aim;
		try
		{
			// A wing volley is a normal source attack. The local guard prevents escort recursion,
			// while its own echoes and on-fire transforms remain functional.
			FireWeaponAttack(shotWeapon ?? support, origin, direction.Normalized(), damageScale, false);
			_fpsWingCooldowns[support] = GetFpsWingCooldown(support);
		}
		finally
		{
			_fpsWingFiring = false;
			_fpsWingAimPoint = null;
		}
	}

	private float GetFpsWingCooldown(EquipmentItem item)
	{
		// Host cadence already expresses Broken/Baseline/Enhanced/Flawless as .25/.5/.75/1.
		// Its baseline-relative quality is .5/1/1.5/2; applying strength again would double-scale it.
		float cadence = 0f;
		foreach (WeaponModulatorData modulator in item.Modulators)
			if (modulator?.IsWingModulator == true) cadence = Mathf.Max(cadence, modulator.WingCadenceMultiplier);
		float speed = Mathf.Max(0.01f, 1f + GetFpsPassiveSubstat(item, SubstatType.AttackSpeed) / 100f);
		if (_arenaDebugOverridesActive) speed *= Mathf.Max(0.01f, 1f + _arenaDebugOverrides.WeaponAttackSpeedPercent / 100f);
		return Mathf.Clamp(GetWeaponRuntimeCooldown(item) / (speed * Mathf.Clamp(cadence, 0.01f, 1f)), 0.05f, 30f);
	}

	private static string GetFpsWingBehavior(EquipmentItem item)
	{
		foreach (string key in FpsWingKeys)
			if (HasFpsWingKey(item, key)) return key;
		return string.Empty;
	}

	private static bool HasFpsWingKey(EquipmentItem item, string key)
	{
		if (item.Modulators == null) return false;
		foreach (WeaponModulatorData modulator in item.Modulators)
			if (modulator != null && modulator.NormalizedKey == key) return true;
		return false;
	}

	private bool CanInstallFpsWingModulator(EquipmentItem item, WeaponModulatorData modulator, out string reason)
	{
		reason = string.Empty;
		if (!modulator.IsWingModulator) return true;
		if (_arsenal.IndexOf(item) <= 0)
		{
			reason = "Wing modulators require a support weapon.";
			return false;
		}
		foreach (EquipmentItem equipped in _arsenal)
			if (HasFpsWingKey(equipped, modulator.NormalizedKey))
			{
				reason = $"Only one {modulator.BaseDisplayName} can be active.";
				return false;
			}
		return true;
	}

	private bool TryFindFpsWingTarget(EquipmentItem item, Vector3 origin, string behavior, out Vector3 aim, out EquipmentItem shotWeapon, out float damageScale)
	{
		aim = origin;
		shotWeapon = item;
		damageScale = 1f;
		float range = GetWeaponRange(item.Shape!.AttackType) * WeaponModulatorSharedRuntime.GetWeaponItemRangeMultiplier(item);
		WeaponModulatorTier sisterTier = WeaponModulatorTier.Baseline;
		bool guided = behavior != "wing_brother" && TryGetFpsWingSisterTier(out sisterTier);
		Vector3 mark = origin;
		bool hasMark = guided && TryResolveFpsWingSisterAim(item, origin, range, sisterTier, out mark);
		if (behavior != "wing_woman")
		{
			if (hasMark) { aim = mark; return true; }
			IFpsCombatEnemy? nearest = FindNearestEnemy(origin, range, null, true);
			if (nearest == null) return false;
			aim = nearest.HeadPosition;
			return true;
		}
		IFpsCombatEnemy? best = null;
		float bestScore = float.MinValue;
		foreach (IFpsCombatEnemy target in QueryCombatRadius(origin, range))
		{
			if (!target.IsAlive || !HasLineOfSight(origin, target)) continue;
			float distanceSquared = origin.DistanceSquaredTo(target.HeadPosition);
			if (distanceSquared > range * range) continue;
			float hp = Mathf.Clamp(target.CurrentHealth / Mathf.Max(1f, target.MaxHealth), 0f, 1f);
			float score = FpsWingDistanceScore(distanceSquared, range) * 0.65f + (1f - hp) * 4.5f;
			score += hp <= 0.12f ? 5.5f : hp <= 0.25f ? 3.5f : 0f;
			score += target.Role == FpsEnemyRole.Striker ? 3.6f : 1.2f;
			if (hasMark) score += Mathf.Lerp(4.25f, 0f, Mathf.Clamp(target.HeadPosition.DistanceTo(mark) / 12f, 0f, 1f));
			if (score <= bestScore) continue;
			best = target;
			bestScore = score;
		}
		if (best == null) return false;
		float ratio = best.CurrentHealth / Mathf.Max(1f, best.MaxHealth);
		bool elite = best.Role == FpsEnemyRole.Striker;
		bool marked = hasMark && best.HeadPosition.DistanceTo(mark) <= 7f;
		float critChance = elite ? 18f : 12f;
		float critDamage = elite ? 32f : 20f;
		if (marked) { critChance += 10f; critDamage += 12f; }
		if (ratio <= 0.25f) critChance += 12f;
		if (ratio <= 0.12f) critChance = 100f;
		if (!_fpsWingWomanSources.TryGetValue(item, out EquipmentItem? tunedSource))
		{
			tunedSource = CloneSliceWeapon(item);
			_fpsWingWomanSources[item] = tunedSource;
		}
		// Keep source references stable for lock-on, mark, and hit-cadence state across volleys.
		tunedSource.Shape = item.Shape;
		tunedSource.Rarity = item.Rarity;
		tunedSource.Essence = item.Essence;
		tunedSource.Level = item.Level;
		tunedSource.Health = item.Health;
		tunedSource.Damage = item.Damage;
		tunedSource.Substats = CloneSliceSubstats(item.Substats);
		tunedSource.Modulators = CloneSliceModulators(item.Modulators);
		shotWeapon = tunedSource;
		shotWeapon.Substats.Add(new SubstatRoll(SubstatType.CritChance, critChance));
		shotWeapon.Substats.Add(new SubstatRoll(SubstatType.CritDamage, critDamage));
		damageScale = ratio <= 0.12f ? 1.45f : ratio <= 0.25f ? 1.22f : 1f;
		aim = PredictFpsWingTarget(item, origin, best, WeaponModulatorTier.Enhanced);
		return true;
	}

	private bool TryGetFpsWingSisterTier(out WeaponModulatorTier tier)
	{
		tier = WeaponModulatorTier.Baseline;
		bool found = false;
		EquipmentItem? current = GetCurrentWeapon();
		for (int slot = 1; slot < _arsenal.Count; slot++)
		{
			EquipmentItem item = _arsenal[slot];
			if (ReferenceEquals(item, current)) continue;
			foreach (WeaponModulatorData modulator in item.Modulators)
				if (modulator?.NormalizedKey == "wing_sister")
				{
					if (!found || modulator.Tier > tier) tier = modulator.Tier;
					found = true;
				}
		}
		return found;
	}

	private bool TryResolveFpsWingSisterAim(EquipmentItem item, Vector3 origin, float range, WeaponModulatorTier tier, out Vector3 aim)
	{
		aim = origin;
		WeaponAttackType attack = item.Shape!.AttackType;
		bool zone = attack is WeaponAttackType.Mine or WeaponAttackType.FlameCone or WeaponAttackType.Rift or WeaponAttackType.VoidSingularity;
		if (zone && tier != WeaponModulatorTier.Flawed && TryResolveFpsWingZone(item, origin, range, tier, out aim)) return true;
		float radius = tier switch { WeaponModulatorTier.Flawed => 8f, WeaponModulatorTier.Baseline => 10f, WeaponModulatorTier.Enhanced => 12f, _ => 14f };
		float densityWeight = attack switch { WeaponAttackType.ChainLightning => 0.55f, WeaponAttackType.Sawblade => 0.35f, WeaponAttackType.Homing => 0.28f, _ => 0.18f };
		float bestScore = float.MinValue;
		bool found = false;
		foreach (IFpsCombatEnemy target in QueryCombatRadius(origin, range))
		{
			if (!target.IsAlive || !HasLineOfSight(origin, target)) continue;
			float distanceSquared = origin.DistanceSquaredTo(target.HeadPosition);
			if (distanceSquared > range * range) continue;
			float density = 0f;
			foreach (IFpsCombatEnemy neighbor in QueryCombatRadius(target.GlobalPosition, radius))
				if (neighbor.IsAlive) density += Mathf.Lerp(1f, 0.2f, Mathf.Clamp(neighbor.GlobalPosition.DistanceTo(target.GlobalPosition) / radius, 0f, 1f));
			float score = FpsWingPriority(target) + density * densityWeight + FpsWingDistanceScore(distanceSquared, range);
			if (score <= bestScore) continue;
			bestScore = score;
			aim = PredictFpsWingTarget(item, origin, target, tier);
			found = true;
		}
		return found;
	}

	private bool TryResolveFpsWingZone(EquipmentItem item, Vector3 origin, float range, WeaponModulatorTier tier, out Vector3 aim)
	{
		aim = origin;
		float radius = item.Shape!.AttackType switch { WeaponAttackType.Mine => 10f, WeaponAttackType.FlameCone => 13.5f, WeaponAttackType.Rift => 12f, WeaponAttackType.VoidSingularity => 14f, _ => 11f };
		float lead = tier switch { WeaponModulatorTier.Baseline => 0.12f, WeaponModulatorTier.Enhanced => 0.22f, _ => 0.32f };
		float bestScore = float.MinValue;
		bool found = false;
		foreach (IFpsCombatEnemy candidate in QueryCombatRadius(origin, range))
		{
			if (!candidate.IsAlive || !HasLineOfSight(origin, candidate)) continue;
			Vector3 center = candidate.HeadPosition + GetFpsWingVelocity(candidate) * lead;
			Vector3 weighted = Vector3.Zero;
			float total = 0f;
			int contributors = 0;
			foreach (IFpsCombatEnemy target in QueryCombatRadius(center, radius + 10f))
			{
				if (!target.IsAlive || !HasLineOfSight(origin, target)) continue;
				Vector3 predicted = target.HeadPosition + GetFpsWingVelocity(target) * lead;
				float distance = center.DistanceTo(predicted);
				if (distance > radius) continue;
				float weight = FpsWingPriority(target) * Mathf.Lerp(1f, 0.35f, distance / radius);
				weighted += predicted * weight;
				total += weight;
				contributors++;
			}
			if (contributors < 2 || total <= 0f) continue;
			Vector3 point = weighted / total;
			float distanceSquared = origin.DistanceSquaredTo(point);
			if (distanceSquared > range * range || TryRaycastWorldBlock(origin, point, out _)) continue;
			float score = total + contributors * 0.4f + FpsWingDistanceScore(distanceSquared, range) * 0.5f;
			if (score <= bestScore) continue;
			bestScore = score;
			aim = point;
			found = true;
		}
		return found;
	}

	private Vector3 PredictFpsWingTarget(EquipmentItem item, Vector3 origin, IFpsCombatEnemy target, WeaponModulatorTier tier)
	{
		float tierScale = tier switch { WeaponModulatorTier.Flawed => 0.35f, WeaponModulatorTier.Baseline => 0.6f, WeaponModulatorTier.Enhanced => 0.82f, _ => 1f };
		float distance = origin.DistanceTo(target.HeadPosition);
		float speedScale = WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(item);
		float lead = item.Shape!.AttackType switch
		{
			WeaponAttackType.Mortar => MortarFlightTime / speedScale * tierScale,
			WeaponAttackType.Drill => Mathf.Clamp(distance / (DrillSpeed * speedScale) * tierScale, 0.05f, 0.8f),
			WeaponAttackType.Homing => 0.18f * tierScale,
			WeaponAttackType.Laser => 0.04f * tierScale,
			WeaponAttackType.ChainLightning => 0.1f * tierScale,
			WeaponAttackType.Sawblade => Mathf.Clamp(distance / (SawbladeSpeed * speedScale * 0.45f) * tierScale, 0.05f, 0.45f),
			WeaponAttackType.Single => 0f,
			_ => Mathf.Clamp(distance / (30f * speedScale) * tierScale, 0.04f, 0.45f),
		};
		Vector3 point = target.HeadPosition + GetFpsWingVelocity(target) * lead;
		return TryRaycastWorldBlock(origin, point, out _) ? target.HeadPosition : point;
	}

	private Vector3 GetFpsWingVelocity(IFpsCombatEnemy target) => _fpsWingVelocities.TryGetValue(target, out Vector3 velocity) ? velocity : Vector3.Zero;
	private static float FpsWingPriority(IFpsCombatEnemy target) => target.Role == FpsEnemyRole.Striker ? 3.25f : 1.35f;
	private static float FpsWingDistanceScore(float distanceSquared, float range) => Mathf.Lerp(1.25f, 0.15f, Mathf.Clamp(distanceSquared / Mathf.Max(0.001f, range * range), 0f, 1f));

	private Vector3 GetFpsWingBrotherOrigin(int slot, Vector3 target)
	{
		float angle = _fpsWingTime * 1.35f + slot * 1.19f;
		float radius = 7.5f + Mathf.Sin(_fpsWingTime * 1.75f + slot) * 1.2f;
		Vector3 proposed = target + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
		Vector3 tether = proposed - _player.HeadPosition;
		if (tether.LengthSquared() > 26f * 26f) proposed = _player.HeadPosition + tether.Normalized() * 26f;
		// An autonomous angle cannot place the wing through solid cover.
		return TryRaycastWorldBlock(_player.HeadPosition, proposed, out _) ? _player.HeadPosition : proposed;
	}
}
