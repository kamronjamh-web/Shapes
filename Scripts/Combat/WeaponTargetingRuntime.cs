using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
    private bool TryFindCurrentWeaponTarget(out Vector2 targetPosition)
    {
        return TryFindWeaponTarget(_weaponKind, _weaponRarity, _player.Position, out targetPosition);
    }

    private float GetCurrentWeaponTargetRange()
    {
        return GetWeaponTargetRange(_weaponKind, _weaponRarity);
    }

    private bool TryFindWeaponTarget(WeaponKind kind, RarityTier rarity, Vector2 origin, out Vector2 targetPosition, EquipmentItem? sourceItem = null)
    {
        return TryFindNearestTarget(origin, GetWeaponTargetRange(kind, rarity, sourceItem), out targetPosition);
    }

    private float GetWeaponTargetRange(WeaponKind kind, RarityTier rarity, EquipmentItem? sourceItem = null)
    {
        float functionalRange = GetWeaponFunctionalTargetRange(kind, rarity, sourceItem);
        float engageRange = GetWeaponEngageRange(sourceItem);
        return Mathf.Min(functionalRange, engageRange);
    }

    private float GetWeaponEngageRange(EquipmentItem? sourceItem = null)
    {
        float rangeMultiplier = sourceItem == null
            ? GetEquipmentWeaponRangeMultiplier()
            : GetWeaponItemRangeMultiplier(sourceItem);
        float range = BaseWeaponEngageRange * rangeMultiplier;
        if (_arenaDebugOverridesActive)
        {
            range *= RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.RangeFlat / 100f, 1f);
        }

        return Mathf.Clamp(RuntimeMath.FiniteOr(range, BaseWeaponEngageRange), 1f, 1200f);
    }

    private float GetWeaponFunctionalTargetRange(WeaponKind kind, RarityTier rarity, EquipmentItem? sourceItem = null)
    {
        float projectileRange = GetProjectileRange(kind, rarity, sourceItem);
        float projectileSpeed = sourceItem == null
            ? _player.BulletSpeed
            : GetWeaponProjectileSpeed(kind, rarity, sourceItem);
        float sizeScale = sourceItem == null
            ? Mathf.Clamp(RuntimeMath.FiniteOr(_bulletRadiusScale, 1f), 0.35f, 12f)
            : Mathf.Clamp(RuntimeMath.FiniteOr(GetSupportWeaponProjectileSizeScale(sourceItem), 1f), 0.35f, 12f);

        return kind switch
        {
            WeaponKind.Circle => projectileSpeed * 0.45f * 2.8f,
            WeaponKind.Triangle => 80f * sizeScale,
            WeaponKind.Hexagon => 40f * sizeScale,
            WeaponKind.Sphere => (LegacyNovaMaxRadius + 7.2f) * sizeScale,
            WeaponKind.Cube => (LegacyOrbitalRadius + LegacyOrbitalHitRadius + 30f) * sizeScale,
            WeaponKind.Pyramid => 150f,
            WeaponKind.Cone => (LegacyFlameRange + 9.6f) * sizeScale,
            WeaponKind.Tesseract => 39f,
            WeaponKind.Hyperphere => 40f,
            WeaponKind.Pentachoron => LegacyChainRange,
            WeaponKind.Hypercone => 120f,
            WeaponKind.ImpossibleCube => LegacyVoidPullRadius + 24f,
            WeaponKind.Mobius => projectileRange,
            WeaponKind.Klein => LegacyWrathBaseRadius + 7.2f,
            WeaponKind.BoysSurface => 150f,
            _ => projectileRange,
        };
    }

    private float GetCurrentProjectileRange(WeaponKind kind)
    {
        return GetProjectileRange(kind, _weaponRarity);
    }

    private float GetProjectileRange(WeaponKind kind, RarityTier rarity, EquipmentItem? sourceItem = null)
    {
        float rangeMultiplier = sourceItem == null
            ? GetEquipmentWeaponRangeMultiplier()
            : GetWeaponItemRangeMultiplier(sourceItem);
        float shotLife = GetWeaponShotLife(kind, rarity) * rangeMultiplier;
        if (_arenaDebugOverridesActive)
        {
            shotLife *= RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.RangeFlat / 100f, 1f);
        }

        float projectileSpeed = sourceItem == null
            ? _player.BulletSpeed
            : GetWeaponProjectileSpeed(kind, rarity, sourceItem);
        float range = projectileSpeed * shotLife;
        return Mathf.Clamp(RuntimeMath.FiniteOr(range, BaseBulletSpeed * BaseShotLife), 1f, 1200f);
    }

    private float GetSupportWeaponProjectileSizeScale(EquipmentItem item)
    {
        return GetGlobalProjectileSizeMultiplier() * GetWeaponItemSizeMultiplier(item);
    }

    private float GetGlobalProjectileSizeMultiplier()
    {
        float modifierProjectileSize = GetModifierTotal(ModifierKind.ProjectileSizePct);
        return Mathf.Clamp(
            RuntimeMath.FiniteOr((1f + GetItemPower(RunItemId.MagnifyingLens) * 0.12f) * (1f + modifierProjectileSize), 1f),
            0.1f,
            12f);
    }

    private static float GetWeaponItemSizeMultiplier(EquipmentItem item)
    {
        return Mathf.Clamp(
            RuntimeMath.FiniteOr(1f + StatGenerator.GetTotalSubstatValue(item, SubstatType.Size) / 100f, 1f),
            0.1f,
            12f);
    }

    private static float GetWeaponItemRangeMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemRangeMultiplier(item);
    }

    private static float GetWeaponItemCritChancePercent(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemCritChancePercent(item);
    }

    private static float GetWeaponItemCritDamagePercent(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemCritDamagePercent(item);
    }

    private static int GetWeaponItemProjectileCount(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemProjectileCount(item);
    }

    private float GetWeaponProjectileSpeed(WeaponKind kind, RarityTier rarity, EquipmentItem? sourceItem = null)
    {
        float rarityTier = GetRarityMultiplier(rarity);
        float speed = RuntimeMath.FiniteOr(BaseBulletSpeed * GetWeaponSpeedMultiplier(kind, rarityTier), BaseBulletSpeed);
        if (sourceItem != null)
        {
            speed *= GetWeaponItemProjectileSpeedMultiplier(sourceItem);
        }

        return Mathf.Clamp(
            speed,
            1f,
            600f);
    }

    private static float GetWeaponItemProjectileSpeedMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(item);
    }

    private static float GetWeaponItemDurationMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemDurationMultiplier(item);
    }

    private static float GetWeaponItemHomingStrengthMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemHomingStrengthMultiplier(item);
    }

    private static float GetWeaponItemKnockbackMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemKnockbackMultiplier(item);
    }

    private static float GetWeaponItemPullStrengthMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemPullStrengthMultiplier(item);
    }

    private static float GetWeaponItemCloneOrEchoPowerMultiplier(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemCloneOrEchoPowerMultiplier(item);
    }

    private static float GetWeaponItemOrbitRadiusScalar(EquipmentItem item)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemOrbitRadiusScalar(item);
    }

    private int GetWeaponItemChainCountBonus(EquipmentItem item)
    {
        return ResolveLegacyQualityCount(item, "chain", WeaponModulatorSharedRuntime.GetWeaponItemPassiveCountValue(item, ModulatorEffectType.ChainCountAdd));
    }

    private int GetWeaponItemPierceCount(EquipmentItem item)
    {
        return ResolveLegacyQualityCount(item, "source pierce", WeaponModulatorSharedRuntime.GetWeaponItemPierceCountValue(item));
    }

    private static float GetWeaponItemPassiveMultiplier(EquipmentItem item, ModulatorEffectType type)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemPassiveMultiplier(item, type);
    }

    private int GetWeaponItemPassiveCount(EquipmentItem item, ModulatorEffectType type)
    {
        return ResolveLegacyQualityCount(item, type.ToString(), WeaponModulatorSharedRuntime.GetWeaponItemPassiveCountValue(item, type));
    }

    private static bool HasWeaponItemPassiveFlag(EquipmentItem item, ModulatorEffectType type)
    {
        return WeaponModulatorSharedRuntime.HasWeaponItemPassiveFlag(item, type);
    }

    private static float GetWeaponItemPassiveMaxMagnitude(EquipmentItem item, ModulatorEffectType type, float fallback)
    {
        return WeaponModulatorSharedRuntime.GetWeaponItemPassiveMaxMagnitude(item, type, fallback);
    }

    private static bool HasWeaponTransformFlag(EquipmentItem item, string transformKey)
    {
        string normalizedTarget = WeaponModulatorData.NormalizeKey(transformKey);
        bool found = false;
        WeaponModulatorSharedRuntime.ForEachResolvedEffect(item, ModulatorEffectTrigger.Passive, effect =>
        {
            if (effect.Type == ModulatorEffectType.NewWeaponTransform
                && string.Equals(WeaponModulatorData.NormalizeKey(effect.Key), normalizedTarget, StringComparison.Ordinal))
            {
                found = true;
            }
        });

        return found;
    }

    private static float GetWeaponTransformMagnitude(EquipmentItem item, string transformKey, float fallback)
    {
        float value = StrongestLegacyEffect(item, ModulatorEffectType.NewWeaponTransform, WeaponModulatorData.NormalizeKey(transformKey))?.Magnitude ?? fallback;
        return value > 0f ? value : fallback;
    }

    private static float GetWeaponTransformMagnitudeB(EquipmentItem item, string transformKey, float fallback)
    {
        float value = StrongestLegacyEffect(item, ModulatorEffectType.NewWeaponTransform, WeaponModulatorData.NormalizeKey(transformKey))?.MagnitudeB ?? fallback;
        return value > 0f ? value : fallback;
    }

    private static int GetWeaponTransformCount(EquipmentItem item, string transformKey, int fallback)
    {
        int value = StrongestLegacyEffect(item, ModulatorEffectType.NewWeaponTransform, WeaponModulatorData.NormalizeKey(transformKey))?.Count ?? fallback;
        return value > 0 ? value : fallback;
    }

    private Vector2 ResolveLegacyAimPoint(Vector2 origin, Vector2 direction, Vector2? aimPointOverride, float targetRange, float fallbackDistance)
    {
        return aimPointOverride.HasValue
            ? ClampResolve(aimPointOverride.Value, 1f)
            : FindTargetOrAimPoint(origin, direction, targetRange, fallbackDistance);
    }

    private Vector2 FindTargetOrAimPoint(Vector2 origin, Vector2 direction, float targetRange, float fallbackDistance)
    {
        return TryFindNearestTarget(origin, targetRange, out Vector2 targetPosition)
            ? targetPosition
            : ClampResolve(origin + direction * fallbackDistance, 1f);
    }

    private bool TryFindNearestTarget(Vector2 origin, float range, out Vector2 targetPosition)
    {
        float bestDistanceSq = range > 0f ? range * range : float.MaxValue;
        bool found = false;
        targetPosition = origin;

        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            float distanceSq = (enemy.Position - origin).LengthSquared();
            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                targetPosition = enemy.Position;
                found = true;
            }
        }

        if (_boss != null && _boss.IsAlive)
        {
            float bossDistanceSq = (_boss.Position - origin).LengthSquared();
            if (bossDistanceSq < bestDistanceSq)
            {
                targetPosition = _boss.Position;
                found = true;
            }
        }

        return found;
    }

    private EnemyActor? FindNearestEnemyActor(Vector2 origin, float range, HashSet<EnemyActor>? visited = null)
    {
        float bestDistanceSq = range > 0f ? range * range : float.MaxValue;
        EnemyActor? best = null;
        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive || (visited != null && visited.Contains(enemy)))
            {
                continue;
            }

            float distanceSq = (enemy.Position - origin).LengthSquared();
            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                best = enemy;
            }
        }

        return best;
    }

    private bool HasHostileInRadius(Vector2 center, float radius)
    {
        float radiusSq = radius * radius;
        foreach (EnemyActor enemy in _enemies)
        {
            if (enemy.IsAlive && (enemy.Position - center).LengthSquared() <= radiusSq)
            {
                return true;
            }
        }

        return _boss != null && _boss.IsAlive && (_boss.Position - center).LengthSquared() <= radiusSq;
    }
}
