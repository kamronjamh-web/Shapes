using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
	private void ApplyFpsModulatorPassives(EquipmentItem weapon)
	{
		float healthScale = Mathf.Max(0.01f, 1f + GetFpsPassiveSubstat(weapon, SubstatType.HealthPercent) / 100f);
		float moveScale = Mathf.Max(0.01f, 1f + GetFpsPassiveSubstat(weapon, SubstatType.MovementSpeed) / 100f);
		float maxHealth = Mathf.Max(1f, _debugBaseMaxHealth) * healthScale;
		float dodge = GetFpsPassiveSubstat(weapon, SubstatType.DodgeChance);
		if (_arenaDebugOverridesActive)
		{
			maxHealth += _arenaDebugOverrides.MaxHpFlat;
			moveScale *= Mathf.Max(0.01f, 1f + _arenaDebugOverrides.MoveSpeedPercent / 100f);
			dodge += _arenaDebugOverrides.DodgeChancePercent;
		}
		_player.RetuneCombatStats(maxHealth, Mathf.Max(0.1f, _debugBaseMoveSpeed) * moveScale, dodge);
	}

	private void TickFpsModulatorPassives(float dt)
	{
		if (!IsCombatRunning() || !_player.IsAlive || dt <= 0f || !float.IsFinite(dt)) return;
		EquipmentItem? weapon = GetCurrentWeapon();
		if (weapon == null) return;
		float regen = GetFpsPassiveSubstat(weapon, SubstatType.HealthRegen);
		if (_arenaDebugOverridesActive) regen += Mathf.Max(0f, _arenaDebugOverrides.HealthRegenPerSecond);
		_player.Heal(Mathf.Max(0f, regen) * dt);
	}

	private void OnFpsModulatorEnemyDamage(EquipmentItem? source, float actualDamage)
	{
		if (source == null || !_player.IsAlive || actualDamage <= 0f || !float.IsFinite(actualDamage)) return;
		float lifeSteal = GetFpsPassiveSubstat(source, SubstatType.LifeStealPercent);
		if (_arenaDebugOverridesActive) lifeSteal += Mathf.Max(0f, _arenaDebugOverrides.LifeStealPercent);
		_player.Heal(actualDamage * Mathf.Max(0f, lifeSteal) / 100f);
	}

	private float GetFpsModulatorLuckMultiplier()
	{
		EquipmentItem? weapon = GetCurrentWeapon();
		if (weapon == null) return 1f;
		float luck = GetFpsPassiveSubstat(weapon, SubstatType.Luck);
		float apexScale = WeaponModulatorSharedRuntime.GetApexDoctrineMultiplier(weapon);
		// Luck has no generic effect type, so resolved effects cannot represent this legacy roll.
		if (weapon.Modulators != null)
			foreach (WeaponModulatorData modulator in weapon.Modulators)
			{
				if (modulator?.Substats == null) continue;
				foreach (SubstatRoll roll in modulator.Substats)
					if (roll.Type == SubstatType.Luck && float.IsFinite(roll.Value)) luck += roll.Value * apexScale;
			}
		return Mathf.Max(0f, 1f + luck / 400f);
	}

	private static float GetFpsPassiveSubstat(EquipmentItem weapon, SubstatType type)
	{
		float value = StatGenerator.GetTotalSubstatValue(weapon, type);
		return float.IsFinite(value) ? value : 0f;
	}
}
