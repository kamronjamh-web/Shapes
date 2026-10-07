using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	[ExportGroup("Weapon Simulation")]
	[Export(PropertyHint.Range, "1,600,1")] public float SquareProjectileSpeed { get; set; } = 180f;

	private void PrepareFpsRunModulators()
	{
		if (!IsDefenseMap) return;
		HashSet<string> wingKeys = new(StringComparer.Ordinal);
		for (int slot = 0; slot < _arsenal.Count; slot++)
		{
			EquipmentItem weapon = _arsenal[slot];
			bool hasWing = false;
			for (int index = 0; index < weapon.Modulators.Count;)
			{
				WeaponModulatorData part = weapon.Modulators[index];
				bool compatible = IsDefensePartCompatible(part, weapon);
				if (part.IsWingModulator)
				{
					compatible &= slot > 0 && !hasWing && !wingKeys.Contains(part.NormalizedKey);
					if (compatible) { hasWing = true; wingKeys.Add(part.NormalizedKey); }
				}
				if (compatible) { index++; continue; }
				weapon.Modulators.RemoveAt(index);
				_recoveredModulators.Add(part);
			}
		}
	}
	private static bool FpsSphereIntersectsEnemy(IFpsCombatEnemy enemy, Vector3 center, float radius)
	{
		Vector3 axis = enemy.CollisionCenter;
		float halfAxis = Mathf.Max(0f, enemy.CollisionHeight * 0.5f - enemy.CollisionRadius);
		axis.Y = Mathf.Clamp(center.Y, axis.Y - halfAxis, axis.Y + halfAxis);
		float expandedRadius = Mathf.Max(0f, radius) + enemy.CollisionRadius;
		return center.DistanceSquaredTo(axis) <= expandedRadius * expandedRadius;
	}
	private static bool IsFpsTransformCompatible(string key, WeaponAttackType type) => WeaponModulatorData.NormalizeKey(key) switch
	{
		"echo_weave" => type == WeaponAttackType.TemporalEcho,
		"scorch_crater" => type == WeaponAttackType.Mortar,
		"closed_circuit" => type == WeaponAttackType.ChainLightning,
		"primary_mimic" => type == WeaponAttackType.ShadowClone,
		"burstline_converter" => type == WeaponAttackType.Single,
		"serration_bloom" => type == WeaponAttackType.Sawblade,
		"prism_refraction" => type == WeaponAttackType.Laser,
		"command_cluster" => type == WeaponAttackType.Mine,
		"collapse_pulse" => type == WeaponAttackType.Nova,
		"aegis_carousel" => type == WeaponAttackType.Orbital,
		"pack_hunters" => type == WeaponAttackType.Homing,
		"backdraft_corridor" => type == WeaponAttackType.FlameCone,
		"phase_inversion" => type == WeaponAttackType.Rift,
		"tunnel_bore" => type == WeaponAttackType.Drill,
		"event_horizon_tear" => type == WeaponAttackType.VoidSingularity,
		"blood_price_reactor" => type == WeaponAttackType.WrathAura,
		_ => false,
	};
	private static float GetFpsAreaScale(EquipmentItem? weapon) => weapon == null ? 1f : Mathf.Clamp(1f + StatGenerator.GetTotalSubstatValue(weapon, SubstatType.Size) / 100f, 0.1f, 12f);
	private static float GetFpsRangeScale(EquipmentItem? weapon) => weapon == null ? 1f : WeaponModulatorSharedRuntime.GetWeaponItemRangeMultiplier(weapon);
	private static float GetFpsDurationScale(EquipmentItem? weapon) => weapon == null ? 1f : WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(weapon);
	private static float GetFpsTickRate(EquipmentItem? weapon) => weapon == null ? 1f : WeaponModulatorSharedRuntime.GetWeaponItemPassiveMultiplier(weapon, ModulatorEffectType.TickRateMultiplier);
	private static int GetFpsOutputCount(EquipmentItem weapon, int baseline = 1) => Mathf.Clamp(baseline + WeaponModulatorSharedRuntime.GetWeaponItemProjectileCount(weapon), 1, 32);

	private void FireSquareProjectiles(EquipmentItem weapon, Vector3 origin, Vector3 direction, float damageScale)
	{
		float speed = Mathf.Clamp(float.IsFinite(SquareProjectileSpeed) ? SquareProjectileSpeed : 180f, 1f, 600f) * WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon);
		SpawnPlayerProjectile(PlayerProjectileKind.Bullet, origin, direction, speed, GetScaledWeaponDamage(weapon, damageScale),
			0.04f, GetWeaponRange(WeaponAttackType.Single) * GetFpsRangeScale(weapon) / speed,
			1 + WeaponModulatorSharedRuntime.GetWeaponItemPierceCount(weapon), 0f, GetWeaponAccentColor(WeaponAttackType.Single), weapon, true);
	}

	private void ApplyFpsRicochet(IFpsCombatEnemy initial, float damage, EquipmentItem source)
	{
		int count = Mathf.Clamp(Mathf.RoundToInt(StatGenerator.GetTotalSubstatValue(source, SubstatType.Ricochet)), 0, 12);
		HashSet<IFpsCombatEnemy> visited = new() { initial };
		Vector3 origin = initial.HeadPosition;
		for (int i = 0; i < count; i++)
		{
			IFpsCombatEnemy? next = FindNearestEnemy(origin, 12f * GetFpsRangeScale(source), visited, true);
			if (next == null) break;
			visited.Add(next);
			damage *= 0.85f;
			DealDirectionalDamageToEnemy(next, damage, next.HeadPosition, source, false, false, next.HeadPosition - origin);
			SpawnPulse(next.HeadPosition, 0.4f, 0.12f, GetWeaponAccentColor(source.Shape!.AttackType));
			origin = next.HeadPosition;
		}
	}

	private void SpawnSustainedFpsAttack(ZoneEffectKind kind, EquipmentItem weapon, Vector3 origin, Vector3 direction, float range, float damage)
	{
		float duration = 0.25f * GetFpsDurationScale(weapon);
		SpawnZone(kind, origin, kind == ZoneEffectKind.Flame ? 0.42f * GetFpsAreaScale(weapon) : 1.1f * GetFpsAreaScale(weapon), duration, damage, 0f, GetWeaponAccentColor(weapon.Shape!.AttackType), false, weapon, false);
		ZoneEffect zone = _playerZones[^1];
		zone.Direction = direction.Normalized(); zone.Range = range; zone.TickInterval = 0.1f / GetFpsTickRate(weapon); zone.Tick = zone.TickInterval;
	}
}
