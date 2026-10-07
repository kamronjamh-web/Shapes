using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private sealed class FpsDelayedSpecial
	{
		public float Delay;
		public Action Fire = null!;
	}
	private sealed class FpsSpecialObjectState
	{
		public float Timer;
		public float NextPulse;
		public bool Returned;
		public bool Contracting;
		public bool Split;
		public int InitialHits;
		public float InitialLife;
	}
	private readonly List<FpsDelayedSpecial> _fpsDelayedSpecials = new();
	private readonly Dictionary<object, FpsSpecialObjectState> _fpsSpecialObjects = new();
	private readonly Dictionary<EquipmentItem, IFpsCombatEnemy> _fpsLockedTargets = new();
	private readonly Dictionary<EquipmentItem, float> _fpsPackMarkTime = new();
	private readonly Dictionary<EquipmentItem, int> _fpsOmniHitCounts = new();
	private readonly Dictionary<EquipmentItem, float> _fpsBloodPrice = new();
	private float _fpsCascadeCooldown;
	private float _fpsPreviousHealth = -1f;
	private float _fpsPreviousMaxHealth = -1f;
	private int _fpsSpecialDamageDepth;

	private static WeaponModulatorEffectData? GetFpsTransformConfig(EquipmentItem? weapon, string key)
	{
		WeaponModulatorEffectData? result = null;
		WeaponModulatorSharedRuntime.ForEachResolvedEffect(weapon, ModulatorEffectTrigger.Passive, effect =>
		{
			if (effect.Type == ModulatorEffectType.NewWeaponTransform && WeaponModulatorData.NormalizeKey(effect.Key) == key)
				result = effect;
		});
		return result;
	}
	private static float FpsSpecialMagnitude(float value, float fallback, float minimum = 0.01f, float maximum = 12f)
		=> Mathf.Clamp(RuntimeMath.FiniteOr(value > 0f ? value : fallback, fallback), minimum, maximum);
	private FpsSpecialObjectState GetFpsSpecialObjectState(object value)
	{
		if (!_fpsSpecialObjects.TryGetValue(value, out FpsSpecialObjectState? state))
		{
			state = new FpsSpecialObjectState();
			_fpsSpecialObjects[value] = state;
		}
		return state;
	}
	private void QueueFpsSpecial(float delay, Action action)
	{
		if (_fpsDelayedSpecials.Count < 512)
			_fpsDelayedSpecials.Add(new FpsDelayedSpecial { Delay = Mathf.Max(0.01f, delay), Fire = action });
	}
	private void DealFpsSecondary(Action action)
	{
		_fpsSpecialDamageDepth++;
		try { action(); }
		finally { _fpsSpecialDamageDepth--; }
	}

	// Intercept an already deployed command cluster on the next trigger pull.
	private bool TryFireFpsSpecialTransform(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale, bool isEcho)
	{
		if (isEcho || weapon.Shape?.AttackType != WeaponAttackType.Mine || GetFpsTransformConfig(weapon, "command_cluster") == null)
			return false;
		List<MineEffect> deployed = _playerMines.FindAll(mine => ReferenceEquals(mine.SourceItem, weapon));
		if (deployed.Count == 0) return false;
		foreach (MineEffect mine in deployed)
		{
			DealFpsSecondary(() => DamageEnemiesInRadius(mine.Position, mine.ExplosionRadius, mine.Damage, weapon, false));
			OnFpsMineDetonated(mine);
			int index = _playerMines.IndexOf(mine);
			if (index >= 0) RemoveMine(index);
		}
		return true;
	}
	private void ApplyFpsSpecialOnFire(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale, bool isEcho)
	{
		if (isEcho || _fpsSpecialDamageDepth > 0) return;
		WeaponAttackType? type = weapon.Shape?.AttackType;
		WeaponModulatorEffectData? config;
		if (type == WeaponAttackType.Single && (config = GetFpsTransformConfig(weapon, "burstline_converter")) != null)
		{
			int count = Mathf.Clamp(config.Count > 0 ? config.Count : 3, 2, 6);
			float power = FpsSpecialMagnitude(config.Magnitude, 0.45f, 0.05f, 3f);
			float interval = FpsSpecialMagnitude(config.MagnitudeB, 0.18f, 0.02f, 1.5f);
			for (int i = 1; i < count; i++)
			{
				float scale = damageScale * Mathf.Lerp(1f, power, i / (float)(count - 1));
				Vector3 shotDirection = direction.Rotated(Vector3.Up, _weaponRng.RandfRange(-0.03f, 0.03f) * i);
				QueueFpsSpecial(interval * i, () => FireWeaponAttack(weapon, origin, shotDirection, scale, true));
			}
		}
		if (type == WeaponAttackType.TemporalEcho && (config = GetFpsTransformConfig(weapon, "echo_weave")) != null)
		{
			float scale = damageScale * FpsSpecialMagnitude(config.Magnitude, 0.4f);
			QueueFpsSpecial(FpsSpecialMagnitude(config.MagnitudeB, 0.6f, 0.05f, 5f), () => FireWeaponAttack(weapon, origin, direction, scale, true));
		}
		if (type == WeaponAttackType.Laser && (config = GetFpsTransformConfig(weapon, "prism_refraction")) != null)
		{
			float power = damageScale * FpsSpecialMagnitude(config.MagnitudeB, 0.4f);
			QueueFpsSpecial(FpsSpecialMagnitude(config.Magnitude, 0.25f, 0.05f, 2f), () =>
			{
				DealFpsSecondary(() =>
				{
					FirePiercingBeam(weapon, origin, direction.Rotated(Vector3.Up, -0.22f), power);
					FirePiercingBeam(weapon, origin, direction.Rotated(Vector3.Up, 0.22f), power);
				});
			});
		}
		if (type == WeaponAttackType.Nova)
		{
			if (WeaponModulatorSharedRuntime.HasWeaponItemPassiveFlag(weapon, ModulatorEffectType.NewSecondPulse))
				QueueFpsSpecial(0.3f, () => DealFpsSecondary(() => FireNovaPulse(weapon, damageScale * 0.6f)));
			if ((config = GetFpsTransformConfig(weapon, "collapse_pulse")) != null)
			{
				float scale = damageScale * FpsSpecialMagnitude(config.Magnitude, 0.65f);
				QueueFpsSpecial(0.45f, () =>
				{
					float radius = 8.5f * GetFpsAreaScale(weapon) * GetFpsRangeScale(weapon);
					DealFpsSecondary(() => DamageEnemiesInRadius(_player.GlobalPosition, radius * 0.92f, GetScaledWeaponDamage(weapon, scale) * 1.35f, weapon, false));
					SpawnPulse(_player.GlobalPosition + Vector3.Up, radius * 0.8f, 0.24f, GetWeaponAccentColor(WeaponAttackType.Nova));
					if (_playerZones.Count > 0) GetFpsSpecialObjectState(_playerZones[^1]).Contracting = true;
				});
			}
		}
		if (type == WeaponAttackType.FlameCone && (config = GetFpsTransformConfig(weapon, "backdraft_corridor")) != null)
		{
			float damage = GetScaledWeaponDamage(weapon, damageScale) * FpsSpecialMagnitude(config.Magnitude, 0.32f);
			float duration = FpsSpecialMagnitude(config.MagnitudeB, 2.5f, 0.1f, 8f) * WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
			for (int i = 1; i <= 3; i++) SpawnFpsSpecialZone(weapon, origin + direction * (3f * i), 2f, duration, damage);
		}
	}
	private void OnFpsChainCompleted(IFpsCombatEnemy? original, EquipmentItem weapon, float initialDamage)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(weapon, "closed_circuit");
		if (original == null || config == null) return;
		float damage = initialDamage * FpsSpecialMagnitude(config.Magnitude, 0.5f);
		QueueFpsSpecial(0.12f, () => DealFpsSecondary(() => DealDamageToEnemy(original, damage, original.HeadPosition, weapon, false, false)));
	}

	private void SpawnFpsSpecialZone(EquipmentItem weapon, Vector3 position, float radius, float duration, float damage)
	{
		SpawnZone(ZoneEffectKind.Rift, position, radius, duration, damage * RiftTickInterval / Mathf.Max(0.01f, duration), 0f,
			GetWeaponAccentColor(weapon.Shape?.AttackType ?? WeaponAttackType.Single), false, weapon, false);
		// Secondary fields retain source stats without transforming into another phase-inversion field.
		if (_playerZones.Count > 0) GetFpsSpecialObjectState(_playerZones[^1]).Returned = true;
	}
	private bool TryReturnFpsSaw(PlayerProjectileEffect projectile)
	{
		if (projectile.Kind != PlayerProjectileKind.Sawblade || projectile.SourceItem == null ||
			!WeaponModulatorSharedRuntime.HasWeaponItemPassiveFlag(projectile.SourceItem, ModulatorEffectType.NewSawReturn)) return false;
		FpsSpecialObjectState state = GetFpsSpecialObjectState(projectile);
		if (state.Returned) return false;
		state.Returned = true;
		Vector3 toPlayer = _player.GlobalPosition + Vector3.Up - projectile.Position;
		projectile.Direction = toPlayer.LengthSquared() > 0.001f ? toPlayer.Normalized() : -projectile.Direction;
		projectile.Damage *= 0.9f;
		projectile.Life = state.InitialLife > 0f ? state.InitialLife : SawbladeLife * WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(projectile.SourceItem);
		projectile.RemainingHits = Mathf.Max(1, state.InitialHits);
		projectile.HitEnemies.Clear();
		return true;
	}
	private void OnFpsProjectileExhausted(PlayerProjectileEffect projectile)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(projectile.SourceItem, "serration_bloom");
		if (projectile.Kind != PlayerProjectileKind.Sawblade || config == null || projectile.SourceItem == null) return;
		FpsSpecialObjectState state = GetFpsSpecialObjectState(projectile);
		if (state.Split) return;
		state.Split = true;
		int count = Mathf.Clamp(config.Count > 0 ? config.Count : 2, 1, 8);
		for (int i = 0; i < count; i++)
		{
			float angle = count == 1 ? 0f : Mathf.Lerp(-0.7f, 0.7f, i / (float)(count - 1));
			Vector3 direction = projectile.Direction.Rotated(Vector3.Up, angle);
			SpawnPlayerProjectile(PlayerProjectileKind.Drill, projectile.Position + direction * (projectile.Radius + 0.1f), direction,
				projectile.Speed * 0.9f, projectile.Damage * FpsSpecialMagnitude(config.Magnitude, 0.55f), projectile.Radius * 0.75f,
				Mathf.Max(0.15f, projectile.Life * 0.55f), 1, 0f, GetWeaponAccentColor(WeaponAttackType.Sawblade), projectile.SourceItem, false);
		}
	}
	private IFpsCombatEnemy? ResolveFpsSeekerTarget(PlayerProjectileEffect projectile)
	{
		EquipmentItem? source = projectile.SourceItem;
		IFpsCombatEnemy? preferred = null;
		float bias = 1f;
		if (source != null)
		{
			bool lockTarget = WeaponModulatorSharedRuntime.HasWeaponItemPassiveFlag(source, ModulatorEffectType.NewTargetLockBias);
			bool packMarked = _fpsPackMarkTime.TryGetValue(source, out float time) && time > 0f;
			if ((lockTarget || packMarked) && _fpsLockedTargets.TryGetValue(source, out IFpsCombatEnemy? target) &&
				target.IsValid && target.IsAlive && !projectile.HitEnemies.Contains(target)) preferred = target;
			bias = WeaponModulatorSharedRuntime.GetWeaponItemPassiveMaxMagnitude(source, ModulatorEffectType.NewTargetLockBias, 1f);
			WeaponModulatorEffectData? config = packMarked ? GetFpsTransformConfig(source, "pack_hunters") : null;
			if (config != null) bias = Mathf.Max(bias, FpsSpecialMagnitude(config.MagnitudeB, 1.3f, 1f, 4f));
		}
		IFpsCombatEnemy? nearest = preferred ?? FindNearestEnemy(projectile.Position, 32f, projectile.HitEnemies, false);
		if (source != null && nearest != null && WeaponModulatorSharedRuntime.HasWeaponItemPassiveFlag(source, ModulatorEffectType.NewTargetLockBias))
			_fpsLockedTargets[source] = nearest;
		if (nearest != null && bias > 1f && GetFpsSpecialObjectState(projectile).InitialHits == 0)
		{
			Vector3 targetDirection = (nearest.HeadPosition - projectile.Position).Normalized();
			projectile.Direction = projectile.Direction.Lerp(targetDirection, Mathf.Clamp((bias - 1f) * 0.55f, 0f, 0.95f)).Normalized();
		}
		return nearest;
	}
	private void TickFpsProjectileSpecial(PlayerProjectileEffect projectile, Vector3 previous, float dt)
	{
		FpsSpecialObjectState state = GetFpsSpecialObjectState(projectile);
		if (state.InitialHits == 0) { state.InitialHits = projectile.RemainingHits; state.InitialLife = projectile.Life + dt; }
		state.Timer -= dt;
		WeaponModulatorEffectData? config = GetFpsTransformConfig(projectile.SourceItem, "tunnel_bore");
		if (projectile.Kind == PlayerProjectileKind.Drill && config != null && projectile.SourceItem != null && state.Timer <= 0f)
		{
			state.Timer = 0.18f;
			SpawnFpsSpecialZone(projectile.SourceItem, (previous + projectile.Position) * 0.5f, Mathf.Max(0.7f, projectile.Radius * 1.6f),
				FpsSpecialMagnitude(config.MagnitudeB, 1.8f, 0.1f, 6f), projectile.Damage * FpsSpecialMagnitude(config.Magnitude, 0.28f));
		}
	}
	private void OnFpsMineDetonated(MineEffect mine)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(mine.SourceItem, "command_cluster");
		if (config == null || mine.SourceItem == null) return;
		int count = Mathf.Clamp(config.Count > 0 ? config.Count : 3, 1, 8);
		for (int i = 0; i < count; i++)
		{
			float angle = i * Mathf.Tau / count;
			Vector3 position = mine.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * mine.ExplosionRadius * 0.6f;
			float damage = mine.Damage * FpsSpecialMagnitude(config.Magnitude, 0.35f);
			QueueFpsSpecial(0.2f + i * 0.04f, () =>
			{
				DealFpsSecondary(() => DamageEnemiesInRadius(position, mine.ExplosionRadius * 0.65f, damage, mine.SourceItem, false));
				SpawnPulse(position, mine.ExplosionRadius * 0.65f, 0.2f, GetWeaponAccentColor(WeaponAttackType.Mine));
			});
		}
	}
	private void OnFpsMortarImpact(MortarEffect mortar)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(mortar.SourceItem, "scorch_crater");
		if (config != null && mortar.SourceItem != null)
			SpawnFpsSpecialZone(mortar.SourceItem, mortar.Target, mortar.Radius * 0.9f,
				FpsSpecialMagnitude(config.MagnitudeB, 3.5f, 0.1f, 12f), mortar.Damage * FpsSpecialMagnitude(config.Magnitude, 0.3f));
	}
	private void TickFpsZoneSpecial(ZoneEffect zone, float dt)
	{
		FpsSpecialObjectState state = GetFpsSpecialObjectState(zone);
		if (state.Contracting && zone.Kind == ZoneEffectKind.Pulse)
		{
			float scale = Mathf.Lerp(zone.Radius, zone.Radius * 0.1875f, Mathf.Clamp(zone.Age / Mathf.Max(0.01f, zone.Duration), 0f, 1f));
			zone.Mesh.Scale = new Vector3(scale, Mathf.Max(0.08f, scale * 0.12f), scale);
			return;
		}
		WeaponModulatorEffectData? config = GetFpsTransformConfig(zone.SourceItem, "phase_inversion");
		if (state.Returned || config == null || zone.Kind != ZoneEffectKind.Rift) return;
		float interval = FpsSpecialMagnitude(config.Magnitude, 0.6f, 0.1f, 2f);
		float direction = ((int)(zone.Age / interval) & 1) == 0 ? 1f : -1f;
		PullEnemiesToward(zone.Position, zone.Radius, 16f * direction, dt);
	}
	private void OnFpsZoneExpired(ZoneEffect zone)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(zone.SourceItem, "event_horizon_tear");
		if (zone.Kind != ZoneEffectKind.Void || config == null || zone.SourceItem == null) return;
		DealFpsSecondary(() => DamageEnemiesInRadius(zone.Position, zone.Radius * 1.25f, zone.DamagePerTick * FpsSpecialMagnitude(config.Magnitude, 1.6f), zone.SourceItem, false));
		SpawnPulse(zone.Position, zone.Radius * 1.5f, 0.26f, GetWeaponAccentColor(WeaponAttackType.VoidSingularity));
		int count = Mathf.Clamp(config.Count > 0 ? config.Count : 6, 1, 24);
		for (int i = 0; i < count; i++)
		{
			float angle = i * Mathf.Tau / count;
			Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
			SpawnPlayerProjectile(PlayerProjectileKind.Drill, zone.Position + Vector3.Up * 0.6f, direction, 26f,
				zone.DamagePerTick * FpsSpecialMagnitude(config.MagnitudeB, 0.3f), 0.9f, 2.4f, 1, 0f,
				GetWeaponAccentColor(WeaponAttackType.VoidSingularity), zone.SourceItem, false);
		}
	}
	private void TickFpsOrbitalSpecial(OrbitalEffect orbital, float dt)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(orbital.SourceItem, "aegis_carousel");
		if (config == null || orbital.SourceItem == null) return;
		FpsSpecialObjectState state = GetFpsSpecialObjectState(orbital);
		float interval = FpsSpecialMagnitude(config.MagnitudeB, 2f, 0.1f, 10f);
		if (orbital.Age >= state.NextPulse)
		{
			if (state.NextPulse > 0f) orbital.AngularSpeed *= -1f;
			state.NextPulse = orbital.Age + interval;
		}
		state.Timer -= dt;
		if (state.Timer <= 0f)
		{
			state.Timer = 0.2f;
			SpawnFpsSpecialZone(orbital.SourceItem, orbital.Mesh.GlobalPosition, 0.8f, 0.55f,
				orbital.Damage * FpsSpecialMagnitude(config.Magnitude, 0.25f) * 0.2f);
		}
	}
	private bool TryFireFpsCloneMimic(CloneEffect clone, Vector3 position, IFpsCombatEnemy target)
	{
		WeaponModulatorEffectData? config = GetFpsTransformConfig(clone.SourceItem, "primary_mimic");
		if (config == null || clone.SourceItem == null || _arsenal.Count == 0) return false;
		EquipmentItem primary = _arsenal[0];
		Vector3 direction = (target.HeadPosition - position).Normalized();
		float scale = FpsSpecialMagnitude(config.Magnitude, 0.35f) * WeaponModulatorSharedRuntime.GetWeaponItemCloneOrEchoPowerMultiplier(clone.SourceItem);
		DealFpsSecondary(() => FireFpsMimicPattern(clone.SourceItem, primary.Shape?.AttackType ?? WeaponAttackType.Single, position, direction, scale));
		return true;
	}
	// Select the primary's pattern while retaining the clone's source identity and stat context.
	private void FireFpsMimicPattern(EquipmentItem source, WeaponAttackType type, Vector3 origin, Vector3 direction, float scale)
	{
		int projectileStart = _playerProjectiles.Count, mineStart = _playerMines.Count, zoneStart = _playerZones.Count;
		int orbitalStart = _playerOrbitals.Count, mortarStart = _playerMortars.Count;
		switch (type)
		{
			case WeaponAttackType.Sawblade: FireSawblade(source, origin, direction, scale); break;
			case WeaponAttackType.Laser: FirePiercingBeam(source, origin, direction, scale); break;
			case WeaponAttackType.Mine: DeployHexMines(source, origin, direction, scale); break;
			case WeaponAttackType.Nova: FireNovaPulse(source, scale); break;
			case WeaponAttackType.Orbital: DeployOrbitals(source, scale); break;
			case WeaponAttackType.Homing: FireHomingSeekers(source, origin, direction, scale); break;
			case WeaponAttackType.FlameCone: FireFlameCone(source, origin, direction, scale); break;
			case WeaponAttackType.Rift: DeployRift(source, origin, direction, scale); break;
			case WeaponAttackType.Mortar: FireMortar(source, origin, direction, scale); break;
			case WeaponAttackType.ChainLightning: FireChainLightning(source, origin, direction, scale); break;
			case WeaponAttackType.Drill: FireDrill(source, origin, direction, scale); break;
			case WeaponAttackType.VoidSingularity: DeployVoidSingularity(source, origin, direction, scale); break;
			case WeaponAttackType.WrathAura: FireWrathAuraBurst(source, scale); break;
			default: FireSquareProjectiles(source, origin, direction, scale); break;
		}
		for (int i = projectileStart; i < _playerProjectiles.Count; i++) _playerProjectiles[i].TriggerModulatorOnHitEffects = false;
		for (int i = mineStart; i < _playerMines.Count; i++) _playerMines[i].TriggerModulatorOnHitEffects = false;
		for (int i = zoneStart; i < _playerZones.Count; i++) _playerZones[i].TriggerModulatorOnHitEffects = false;
		for (int i = orbitalStart; i < _playerOrbitals.Count; i++) _playerOrbitals[i].TriggerModulatorOnHitEffects = false;
		for (int i = mortarStart; i < _playerMortars.Count; i++) _playerMortars[i].TriggerModulatorOnHitEffects = false;
	}

	private void OnFpsModulatorSpecialHit(IFpsCombatEnemy enemy, EquipmentItem? source, float damage, Vector3 position, bool critical, bool trigger)
	{
		if (source == null || !trigger || _fpsSpecialDamageDepth > 0 || damage <= 0f) return;
		// Original damage paths opt in; every secondary proc opts out to prevent feedback loops.
		int copies = 0, stage = 0;
		foreach (EquipmentItem item in _arsenal)
			foreach (WeaponModulatorData mod in item.Modulators)
				if (mod != null && mod.NormalizedKey == "volitile") { copies++; stage = Mathf.Max(stage, (int)mod.Tier + 1); }
		if (copies > 0)
		{
			float radius = Mathf.Clamp(stage + copies - 1, 1, 4);
			SpawnPulse(position, radius, 0.22f, new Color(1f, 0.44f, 0.18f));
			DealFpsSecondary(() =>
			{
				foreach (IFpsCombatEnemy nearby in QueryCombatRadius(position, radius))
					if (nearby.IsAlive) DealDamageToEnemy(nearby, damage * 0.5f, nearby.HeadPosition, source, critical, false);
			});
		}
		WeaponModulatorEffectData? pack = GetFpsTransformConfig(source, "pack_hunters");
		if (trigger && source.Shape?.AttackType == WeaponAttackType.Homing && pack != null)
		{
			_fpsLockedTargets[source] = enemy;
			_fpsPackMarkTime[source] = FpsSpecialMagnitude(pack.Magnitude, 3f, 0.1f, 15f);
			ApplyEnemyMark(enemy, new WeaponModulatorEffectData { Magnitude = FpsSpecialMagnitude(pack.MagnitudeB, 1.3f, 1f, 4f), MagnitudeB = _fpsPackMarkTime[source] });
		}
		if (critical && _fpsCascadeCooldown <= 0f && WeaponModulatorSharedRuntime.TryGetOnCritChainProc(source, out var cascade))
		{
			_fpsCascadeCooldown = cascade.CooldownSeconds;
			HashSet<IFpsCombatEnemy> visited = new() { enemy };
			Vector3 point = position;
			DealFpsSecondary(() =>
			{
				for (int i = 0; i < cascade.ArcCount; i++)
				{
					IFpsCombatEnemy? next = FindNearestEnemy(point, cascade.Range, visited, true);
					if (next == null) break;
					visited.Add(next);
					DealDamageToEnemy(next, damage * cascade.DamageScale, next.HeadPosition, source, true, false);
					SpawnPulse(next.HeadPosition, 0.75f, 0.12f, GetWeaponAccentColor(WeaponAttackType.ChainLightning));
					point = next.HeadPosition;
				}
			});
		}
		if (WeaponModulatorSharedRuntime.TryGetOmniFeedbackConfig(source, out var omni))
		{
			_fpsOmniHitCounts.TryGetValue(source, out int hits);
			if (++hits >= omni.HitCadence)
			{
				hits = 0;
				IFpsCombatEnemy? followup = FindNearestEnemy(position, omni.SearchRange, null, true);
				if (followup != null)
				{
					DealFpsSecondary(() => DealDamageToEnemy(followup, damage * omni.DamageScale, followup.HeadPosition, source, critical, false));
					SpawnPulse(followup.HeadPosition, 3.4f, 0.22f, new Color(0.56f, 0.84f, 1f));
				}
			}
			_fpsOmniHitCounts[source] = hits;
		}
	}
	private void TickFpsModulatorSpecials(float dt)
	{
		_fpsCascadeCooldown = Mathf.Max(0f, _fpsCascadeCooldown - dt);
		for (int i = _fpsDelayedSpecials.Count - 1; i >= 0; i--)
		{
			FpsDelayedSpecial pending = _fpsDelayedSpecials[i];
			pending.Delay -= dt;
			if (pending.Delay > 0f) continue;
			_fpsDelayedSpecials.RemoveAt(i);
			pending.Fire();
		}
		foreach (EquipmentItem source in new List<EquipmentItem>(_fpsPackMarkTime.Keys))
			_fpsPackMarkTime[source] = Mathf.Max(0f, _fpsPackMarkTime[source] - dt);
		EquipmentItem? current = GetCurrentWeapon();
		WeaponModulatorEffectData? reactor = GetFpsTransformConfig(current, "blood_price_reactor");
		if (current != null && reactor != null)
		{
			_fpsBloodPrice.TryGetValue(current, out float stored);
			FpsSpecialObjectState state = GetFpsSpecialObjectState(current);
			state.Timer -= dt;
			if (stored > 0f && state.Timer <= 0f)
			{
				state.Timer = FpsSpecialMagnitude(reactor.MagnitudeB, 1.5f, 0.1f, 10f);
				float burst = stored * 0.3f;
				DealFpsSecondary(() => DamageEnemiesInRadius(_player.GlobalPosition, 6.2f, burst, current, false));
				SpawnPulse(_player.GlobalPosition + Vector3.Up, 6.2f, 0.25f, GetWeaponAccentColor(WeaponAttackType.WrathAura));
				stored *= 0.55f;
			}
			_fpsBloodPrice[current] = stored;
		}
		// Bound per-object state by the currently live effect records.
		HashSet<object> live = new();
		foreach (var value in _playerProjectiles) live.Add(value);
		foreach (var value in _playerZones) live.Add(value);
		foreach (var value in _playerOrbitals) live.Add(value);
		foreach (var value in _arsenal) live.Add(value);
		foreach (object value in new List<object>(_fpsSpecialObjects.Keys)) if (!live.Contains(value)) _fpsSpecialObjects.Remove(value);
	}
	private void CaptureFpsBloodPriceHealth(float currentHealth, float maxHealth)
	{
		float maximum = maxHealth;
		bool healthCapacityChanged = maximum > 0f && _fpsPreviousMaxHealth > 0f && !Mathf.IsEqualApprox(maximum, _fpsPreviousMaxHealth);
		float damage = _fpsPreviousHealth >= 0f && !healthCapacityChanged ? Mathf.Max(0f, _fpsPreviousHealth - currentHealth) : 0f;
		_fpsPreviousHealth = currentHealth;
		_fpsPreviousMaxHealth = maximum;
		EquipmentItem? current = GetCurrentWeapon();
		WeaponModulatorEffectData? reactor = GetFpsTransformConfig(current, "blood_price_reactor");
		if (damage <= 0f || current == null || reactor == null) return;
		_fpsBloodPrice.TryGetValue(current, out float stored);
		FpsSpecialObjectState state = GetFpsSpecialObjectState(current);
		if (stored <= 0f) state.Timer = FpsSpecialMagnitude(reactor.MagnitudeB, 1.5f, 0.1f, 10f);
		_fpsBloodPrice[current] = Mathf.Min(_player.MaxHealth * 6f,
			stored + damage * FpsSpecialMagnitude(reactor.Magnitude, 0.3f, 0f, 2f));
	}
	private void ResetFpsModulatorSpecials()
	{
		_fpsDelayedSpecials.Clear(); _fpsSpecialObjects.Clear(); _fpsLockedTargets.Clear(); _fpsPackMarkTime.Clear();
		_fpsOmniHitCounts.Clear(); _fpsBloodPrice.Clear(); _fpsCascadeCooldown = 0f;
		_fpsPreviousHealth = _player != null ? _player.CurrentHealth : -1f;
		_fpsPreviousMaxHealth = _player != null ? _player.MaxHealth : -1f; _fpsSpecialDamageDepth = 0;
	}
}
