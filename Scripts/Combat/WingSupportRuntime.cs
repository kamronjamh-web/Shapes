using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
    private const string WingManModulatorKey = "wing_man";
    private const string WingWomanModulatorKey = "wing_woman";
    private const string WingBrotherModulatorKey = "wing_brother";
    private const string WingSisterModulatorKey = "wing_sister";

    private enum WingSupportBehavior
    {
        None,
        WingMan,
        WingWoman,
        WingBrother,
        WingSister,
    }

    private readonly struct WingSupportShotTuning
    {
        public WingSupportShotTuning(float damageMultiplier, float bonusCritChancePercent, float bonusCritDamagePercent, bool forceCritical)
        {
            DamageMultiplier = damageMultiplier;
            BonusCritChancePercent = bonusCritChancePercent;
            BonusCritDamagePercent = bonusCritDamagePercent;
            ForceCritical = forceCritical;
        }

        public float DamageMultiplier { get; }
        public float BonusCritChancePercent { get; }
        public float BonusCritDamagePercent { get; }
        public bool ForceCritical { get; }

        public static WingSupportShotTuning Default => new(1f, 0f, 0f, false);
    }

    private void TickWingSupportWeapons(float dt)
    {
        for (int slotIndex = 1; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentItem? item = _equippedLoadout[slotIndex];
            float wingCadenceMultiplier = item?.Shape != null
                ? GetWingCadenceMultiplier(item.Modulators)
                : 0f;
            if (item?.Shape == null || wingCadenceMultiplier <= 0f)
            {
                _wingSupportShotCooldowns[slotIndex] = 0f;
                continue;
            }

            _wingSupportShotCooldowns[slotIndex] = Mathf.Max(0f, _wingSupportShotCooldowns[slotIndex] - dt);
            if (_wingSupportShotCooldowns[slotIndex] > 0f)
            {
                continue;
            }

            WeaponKind kind = MapEquipmentShapeToWeapon(item.Shape.Key);
            RarityTier rarity = MapEquipmentRarity(item.Rarity?.Key);
            WingSupportBehavior wingBehavior = GetWingSupportBehavior(item.Modulators);
            float damage = Mathf.Max(0f, StatGenerator.GetEffectiveDamage(item));
            Vector2 fallbackOrigin = GetArenaWeaponFireOrigin(slotIndex, _arenaWeaponLastAimDirection);
            if (wingBehavior == WingSupportBehavior.WingMan)
            {
                continue;
            }

            if (!TryFindWingSupportTarget(item, kind, rarity, fallbackOrigin, wingBehavior, out Vector2 targetPosition, out Vector2? explicitAimPoint, out WingSupportShotTuning shotTuning))
            {
                continue;
            }

            ResolveArenaWeaponShot(slotIndex, targetPosition, targetPosition - _player.Position, out Vector2 origin, out Vector2 direction);
            NotifyArenaWeaponFired(slotIndex, direction);
            FireLegacyWeaponForKind(kind, direction, origin, 1f, false, damage, rarity, item, explicitAimPoint, shotTuning);
            _wingSupportShotCooldowns[slotIndex] = GetWingSupportWeaponCooldown(item, kind, wingCadenceMultiplier);
        }
    }

    private void TryFireSyncedWingSupportVolleys(Vector2 targetPosition)
    {
        for (int slotIndex = 1; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentItem? item = _equippedLoadout[slotIndex];
            float wingCadenceMultiplier = item?.Shape != null
                ? GetWingCadenceMultiplier(item.Modulators)
                : 0f;
            if (item?.Shape == null || wingCadenceMultiplier <= 0f)
            {
                continue;
            }

            if (GetWingSupportBehavior(item.Modulators) != WingSupportBehavior.WingMan)
            {
                continue;
            }

            if (_wingSupportShotCooldowns[slotIndex] > 0f)
            {
                continue;
            }

            WeaponKind kind = MapEquipmentShapeToWeapon(item.Shape.Key);
            RarityTier rarity = MapEquipmentRarity(item.Rarity?.Key);
            float damage = Mathf.Max(0f, StatGenerator.GetEffectiveDamage(item));

            ResolveArenaWeaponShot(slotIndex, targetPosition, targetPosition - _player.Position, out Vector2 origin, out Vector2 direction);
            NotifyArenaWeaponFired(slotIndex, direction);
            FireLegacyWeaponForKind(kind, direction, origin, 1f, false, damage, rarity, item, targetPosition, WingSupportShotTuning.Default);
            _wingSupportShotCooldowns[slotIndex] = GetWingSupportWeaponCooldown(item, kind, wingCadenceMultiplier);
        }
    }

    private bool TryFindWingSupportTarget(
        EquipmentItem item,
        WeaponKind kind,
        RarityTier rarity,
        Vector2 origin,
        WingSupportBehavior wingBehavior,
        out Vector2 targetPosition,
        out Vector2? explicitAimPoint,
        out WingSupportShotTuning shotTuning)
    {
        targetPosition = origin;
        explicitAimPoint = null;
        shotTuning = WingSupportShotTuning.Default;

        if (wingBehavior == WingSupportBehavior.WingWoman
            && TryResolveWingWomanShot(item, kind, rarity, origin, out targetPosition, out explicitAimPoint, out shotTuning))
        {
            return true;
        }

        if (TryResolveWingSisterAimPoint(item, kind, rarity, origin, out Vector2 guidedAimPoint))
        {
            targetPosition = guidedAimPoint;
            explicitAimPoint = guidedAimPoint;
            return true;
        }

        return TryFindWeaponTarget(kind, rarity, origin, out targetPosition, item);
    }

    private float GetWingSupportWeaponCooldown(EquipmentItem item, WeaponKind kind, float cadenceMultiplier)
    {
        float baseCooldown = item.Shape != null
            ? Mathf.Max(0.05f, RuntimeMath.FiniteOr(item.Shape.Cooldown, GetLegacyWeaponCooldown(kind)))
            : GetLegacyWeaponCooldown(kind);

        float attackSpeedMultiplier = Mathf.Max(
            0.01f,
            RuntimeMath.FiniteOr(1f + StatGenerator.GetTotalSubstatValue(item, SubstatType.AttackSpeed) / 100f, 1f));

        if (_arenaDebugOverridesActive)
        {
            attackSpeedMultiplier *= Mathf.Max(
                0.01f,
                RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponAttackSpeedPercent / 100f, 1f));
        }

        float effectiveCadenceMultiplier = Mathf.Clamp(RuntimeMath.FiniteOr(cadenceMultiplier, 0.50f), 0.01f, 1.0f);
        float effectiveRateMultiplier = attackSpeedMultiplier * effectiveCadenceMultiplier;
        return Mathf.Clamp(RuntimeMath.FiniteOr(baseCooldown / effectiveRateMultiplier, baseCooldown), 0.05f, 30f);
    }

    private static float GetWingCadenceMultiplier(IReadOnlyList<WeaponModulatorData> modulators)
    {
        float cadenceMultiplier = 0f;
        for (int modulatorIndex = 0; modulatorIndex < modulators.Count; modulatorIndex++)
        {
            if (modulators[modulatorIndex].IsWingModulator)
            {
                cadenceMultiplier = Mathf.Max(cadenceMultiplier, modulators[modulatorIndex].WingCadenceMultiplier);
            }
        }

        return cadenceMultiplier;
    }

    private static WingSupportBehavior GetWingSupportBehavior(IReadOnlyList<WeaponModulatorData> modulators)
    {
        if (HasWingModulatorKey(modulators, WingManModulatorKey))
        {
            return WingSupportBehavior.WingMan;
        }

        if (HasWingModulatorKey(modulators, WingWomanModulatorKey))
        {
            return WingSupportBehavior.WingWoman;
        }

        if (HasWingModulatorKey(modulators, WingBrotherModulatorKey))
        {
            return WingSupportBehavior.WingBrother;
        }

        if (HasWingModulatorKey(modulators, WingSisterModulatorKey))
        {
            return WingSupportBehavior.WingSister;
        }

        return WingSupportBehavior.None;
    }

    private bool TryResolveWingWomanShot(
        EquipmentItem sourceItem,
        WeaponKind kind,
        RarityTier rarity,
        Vector2 origin,
        out Vector2 targetPoint,
        out Vector2? explicitAimPoint,
        out WingSupportShotTuning shotTuning)
    {
        targetPoint = origin;
        explicitAimPoint = null;
        shotTuning = WingSupportShotTuning.Default;

        float targetRange = GetWeaponTargetRange(kind, rarity, sourceItem);
        float maxDistanceSquared = targetRange > 0f ? targetRange * targetRange : float.MaxValue;
        bool hasSisterMark = TryResolveWingSisterAimPoint(sourceItem, kind, rarity, origin, out Vector2 sisterMarkedPoint);

        EnemyActor? bestEnemy = null;
        float bestEnemyScore = float.MinValue;

        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            float distanceSquared = (enemy.Position - origin).LengthSquared();
            if (distanceSquared > maxDistanceSquared)
            {
                continue;
            }

            float score = GetWingWomanTargetScore(enemy.Position, enemy.Hp, enemy.MaxHp, enemy.Kind, distanceSquared, targetRange, hasSisterMark, sisterMarkedPoint);
            if (score <= bestEnemyScore)
            {
                continue;
            }

            bestEnemy = enemy;
            bestEnemyScore = score;
        }

        bool bossInRange = _boss != null
            && _boss.IsAlive
            && (_boss.Position - origin).LengthSquared() <= maxDistanceSquared;
        if (_boss != null && bossInRange)
        {
            float bossScore = GetWingWomanTargetScore(
                _boss.Position,
                _boss.Hp,
                _boss.MaxHp,
                null,
                (_boss.Position - origin).LengthSquared(),
                targetRange,
                hasSisterMark,
                sisterMarkedPoint) + 4.5f;
            if (bossScore > bestEnemyScore)
            {
                float bossHealthRatio = _boss.MaxHp > 0f ? _boss.Hp / _boss.MaxHp : 1f;
                targetPoint = ClampResolve(_boss.Position, 1f);
                explicitAimPoint = targetPoint;
                shotTuning = BuildWingWomanShotTuning(bossHealthRatio, true, hasSisterMark && _boss.Position.DistanceTo(sisterMarkedPoint) <= 7f);
                return true;
            }
        }

        if (bestEnemy == null)
        {
            if (!hasSisterMark)
            {
                return false;
            }

            targetPoint = sisterMarkedPoint;
            explicitAimPoint = sisterMarkedPoint;
            shotTuning = new WingSupportShotTuning(1f, 18f, 24f, false);
            return true;
        }

        bool markedTarget = hasSisterMark && bestEnemy.Position.DistanceTo(sisterMarkedPoint) <= 7f;
        float healthRatio = bestEnemy.MaxHp > 0f ? bestEnemy.Hp / bestEnemy.MaxHp : 1f;
        targetPoint = PredictWingSisterTargetPoint(origin, bestEnemy.Position, bestEnemy.LastVelocity, kind, rarity, WeaponModulatorTier.Enhanced);
        explicitAimPoint = targetPoint;
        shotTuning = BuildWingWomanShotTuning(healthRatio, IsWingWomanElite(bestEnemy.Kind), markedTarget);
        return true;
    }

    private static float GetWingWomanTargetScore(
        Vector2 targetPosition,
        float hp,
        float maxHp,
        EnemyKind? enemyKind,
        float distanceSquared,
        float maxDistance,
        bool hasMarkedPoint,
        Vector2 markedPoint)
    {
        float healthRatio = maxHp > 0f ? Mathf.Clamp(hp / maxHp, 0f, 1f) : 1f;
        float score = GetWingSisterDistanceScore(distanceSquared, maxDistance) * 0.65f;
        score += (1f - healthRatio) * 4.5f;
        score += healthRatio <= 0.12f ? 5.5f : healthRatio <= 0.25f ? 3.5f : 0f;

        if (enemyKind.HasValue)
        {
            score += enemyKind.Value switch
            {
                EnemyKind.Mortar => 3.6f,
                EnemyKind.Charger => 3.2f,
                EnemyKind.Interceptor => 2.9f,
                EnemyKind.Bulwark => 2.6f,
                _ => 1.2f,
            };
        }
        else
        {
            score += 8.4f;
        }

        if (hasMarkedPoint)
        {
            float markDistance = targetPosition.DistanceTo(markedPoint);
            score += Mathf.Lerp(4.25f, 0f, Mathf.Clamp(markDistance / 12f, 0f, 1f));
        }

        return score;
    }

    private static WingSupportShotTuning BuildWingWomanShotTuning(float healthRatio, bool eliteOrBoss, bool markedTarget)
    {
        float damageMultiplier = healthRatio <= 0.12f ? 1.45f : healthRatio <= 0.25f ? 1.22f : 1f;
        float critChanceBonus = eliteOrBoss ? 18f : 12f;
        float critDamageBonus = eliteOrBoss ? 32f : 20f;

        if (markedTarget)
        {
            critChanceBonus += 10f;
            critDamageBonus += 12f;
        }

        if (healthRatio <= 0.25f)
        {
            critChanceBonus += 12f;
        }

        return new WingSupportShotTuning(
            damageMultiplier,
            critChanceBonus,
            critDamageBonus,
            healthRatio <= 0.12f);
    }

    private static bool IsWingWomanElite(EnemyKind kind)
    {
        return kind is EnemyKind.Interceptor or EnemyKind.Charger or EnemyKind.Mortar or EnemyKind.Bulwark;
    }

    private bool TryResolveWingSisterAimPoint(
        EquipmentItem sourceItem,
        WeaponKind kind,
        RarityTier rarity,
        Vector2 origin,
        out Vector2 aimPoint)
    {
        aimPoint = origin;
        if (!TryGetActiveWingSisterTier(sourceItem, out WeaponModulatorTier sisterTier))
        {
            return false;
        }

        float targetRange = GetWeaponTargetRange(kind, rarity, sourceItem);
        bool prefersZoneGuidance = UsesWingSisterZoneGuidance(kind);

        if (prefersZoneGuidance && TryResolveWingSisterZoneMark(origin, targetRange, kind, sisterTier, out aimPoint))
        {
            return true;
        }

        if (TryResolveWingSisterTargetMark(origin, targetRange, kind, rarity, sisterTier, out aimPoint))
        {
            return true;
        }

        return prefersZoneGuidance && TryResolveWingSisterZoneMark(origin, targetRange, kind, sisterTier, out aimPoint);
    }

    private bool TryGetActiveWingSisterTier(EquipmentItem sourceItem, out WeaponModulatorTier tier)
    {
        tier = WeaponModulatorTier.Baseline;
        if (HasWingModulatorKey(sourceItem.Modulators, WingBrotherModulatorKey))
        {
            return false;
        }

        bool found = false;
        for (int slotIndex = 1; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentItem? equippedItem = _equippedLoadout[slotIndex];
            if (equippedItem == null)
            {
                continue;
            }

            for (int modulatorIndex = 0; modulatorIndex < equippedItem.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData modulator = equippedItem.Modulators[modulatorIndex];
                if (!string.Equals(modulator.NormalizedKey, WingSisterModulatorKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!found || (int)modulator.Tier > (int)tier)
                {
                    tier = modulator.Tier;
                }

                found = true;
            }
        }

        return found;
    }

    private bool TryResolveWingSisterTargetMark(
        Vector2 origin,
        float targetRange,
        WeaponKind kind,
        RarityTier rarity,
        WeaponModulatorTier tier,
        out Vector2 targetPoint)
    {
        float bestScore = float.MinValue;
        bool found = false;
        float maxDistanceSquared = targetRange > 0f ? targetRange * targetRange : float.MaxValue;
        float densityRadius = tier switch
        {
            WeaponModulatorTier.Flawed => 8f,
            WeaponModulatorTier.Baseline => 10f,
            WeaponModulatorTier.Enhanced => 12f,
            _ => 14f,
        };

        targetPoint = origin;

        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            float distanceSquared = (enemy.Position - origin).LengthSquared();
            if (distanceSquared > maxDistanceSquared)
            {
                continue;
            }

            float score = GetWingSisterTargetPriority(enemy.Kind)
                + GetWingSisterTargetDensityScore(enemy.Position, densityRadius) * GetWingSisterDensityWeight(kind)
                + GetWingSisterDistanceScore(distanceSquared, targetRange);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            targetPoint = PredictWingSisterTargetPoint(origin, enemy.Position, enemy.LastVelocity, kind, rarity, tier);
            found = true;
        }

        if (_boss != null && _boss.IsAlive)
        {
            float bossDistanceSquared = (_boss.Position - origin).LengthSquared();
            if (bossDistanceSquared <= maxDistanceSquared)
            {
                float bossScore = 9f + GetWingSisterTargetDensityScore(_boss.Position, densityRadius) * GetWingSisterDensityWeight(kind) * 0.5f;
                if (bossScore > bestScore)
                {
                    targetPoint = ClampResolve(_boss.Position, 1f);
                    found = true;
                }
            }
        }

        return found;
    }

    private bool TryResolveWingSisterZoneMark(
        Vector2 origin,
        float targetRange,
        WeaponKind kind,
        WeaponModulatorTier tier,
        out Vector2 zonePoint)
    {
        zonePoint = origin;
        if (tier == WeaponModulatorTier.Flawed)
        {
            return false;
        }

        float zoneRadius = kind switch
        {
            WeaponKind.Hexagon => 10f,
            WeaponKind.Cone => 13.5f,
            WeaponKind.Tesseract => 12f,
            WeaponKind.ImpossibleCube => 14f,
            _ => 11f,
        };
        float predictionTime = tier switch
        {
            WeaponModulatorTier.Baseline => 0.12f,
            WeaponModulatorTier.Enhanced => 0.22f,
            _ => 0.32f,
        };
        float maxDistanceSquared = targetRange > 0f ? targetRange * targetRange : float.MaxValue;
        float bestScore = float.MinValue;
        bool found = false;

        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            Vector2 candidate = PredictWingSisterZonePoint(enemy.Position, enemy.LastVelocity, predictionTime);
            if ((candidate - origin).LengthSquared() > maxDistanceSquared)
            {
                continue;
            }

            if (!TryScoreWingSisterZoneCandidate(candidate, zoneRadius, predictionTime, out Vector2 scoredPoint, out float score))
            {
                continue;
            }

            score += GetWingSisterDistanceScore((scoredPoint - origin).LengthSquared(), targetRange) * 0.5f;
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            zonePoint = scoredPoint;
            found = true;
        }

        if (_boss != null && _boss.IsAlive)
        {
            Vector2 bossCandidate = ClampResolve(_boss.Position, 1f);
            if ((bossCandidate - origin).LengthSquared() <= maxDistanceSquared
                && TryScoreWingSisterZoneCandidate(bossCandidate, zoneRadius, predictionTime, out Vector2 scoredPoint, out float score)
                && score > bestScore)
            {
                zonePoint = scoredPoint;
                found = true;
            }
        }

        return found;
    }

    private bool TryScoreWingSisterZoneCandidate(
        Vector2 candidate,
        float radius,
        float predictionTime,
        out Vector2 zonePoint,
        out float score)
    {
        Vector2 weightedCenter = Vector2.Zero;
        score = 0f;
        float totalWeight = 0f;
        bool includesBoss = false;
        int contributorCount = 0;

        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            Vector2 predictedPosition = PredictWingSisterZonePoint(enemy.Position, enemy.LastVelocity, predictionTime);
            float distance = predictedPosition.DistanceTo(candidate);
            if (distance > radius)
            {
                continue;
            }

            float weight = GetWingSisterTargetPriority(enemy.Kind) * Mathf.Lerp(1f, 0.35f, Mathf.Clamp(distance / radius, 0f, 1f));
            weightedCenter += predictedPosition * weight;
            totalWeight += weight;
            score += weight;
            contributorCount++;
        }

        if (_boss != null && _boss.IsAlive)
        {
            float distance = _boss.Position.DistanceTo(candidate);
            if (distance <= radius)
            {
                float weight = 6f * Mathf.Lerp(1f, 0.45f, Mathf.Clamp(distance / radius, 0f, 1f));
                weightedCenter += _boss.Position * weight;
                totalWeight += weight;
                score += weight;
                contributorCount++;
                includesBoss = true;
            }
        }

        if (!includesBoss && contributorCount < 2)
        {
            zonePoint = candidate;
            score = 0f;
            return false;
        }

        zonePoint = totalWeight > 0f ? ClampResolve(weightedCenter / totalWeight, 1f) : candidate;
        score += contributorCount * 0.4f;
        return totalWeight > 0f;
    }

    private Vector2 PredictWingSisterTargetPoint(
        Vector2 origin,
        Vector2 targetPosition,
        Vector2 targetVelocity,
        WeaponKind kind,
        RarityTier rarity,
        WeaponModulatorTier tier)
    {
        float tierScale = tier switch
        {
            WeaponModulatorTier.Flawed => 0.35f,
            WeaponModulatorTier.Baseline => 0.6f,
            WeaponModulatorTier.Enhanced => 0.82f,
            _ => 1f,
        };
        float distance = origin.DistanceTo(targetPosition);
        float leadTime = kind switch
        {
            WeaponKind.Hyperphere => LegacyMortarFlightTime * tierScale,
            WeaponKind.Hypercone => Mathf.Clamp(distance / LegacyDrillSpeed * tierScale, 0.05f, 0.8f),
            WeaponKind.Pyramid => 0.18f * tierScale,
            WeaponKind.Triangle => 0.04f * tierScale,
            WeaponKind.Pentachoron => 0.1f * tierScale,
            WeaponKind.Circle => Mathf.Clamp(distance / Mathf.Max(1f, GetWeaponProjectileSpeed(kind, rarity) * 0.45f) * tierScale, 0.05f, 0.45f),
            _ => Mathf.Clamp(distance / Mathf.Max(1f, GetWeaponProjectileSpeed(kind, rarity)) * tierScale, 0.04f, 0.45f),
        };

        Vector2 safeVelocity = RuntimeMath.FiniteOr(targetVelocity, Vector2.Zero);
        if (safeVelocity.LengthSquared() <= 0.0001f)
        {
            return ClampResolve(targetPosition, 1f);
        }

        return ClampResolve(targetPosition + safeVelocity * leadTime, 1f);
    }

    private static Vector2 PredictWingSisterZonePoint(Vector2 position, Vector2 velocity, float predictionTime)
    {
        Vector2 safeVelocity = RuntimeMath.FiniteOr(velocity, Vector2.Zero);
        return position + safeVelocity * predictionTime;
    }

    private static float GetWingSisterTargetPriority(EnemyKind kind)
    {
        return kind switch
        {
            EnemyKind.Mortar => 3.25f,
            EnemyKind.Charger => 2.85f,
            EnemyKind.Interceptor => 2.45f,
            EnemyKind.Bulwark => 2.05f,
            _ => 1.35f,
        };
    }

    private float GetWingSisterTargetDensityScore(Vector2 center, float radius)
    {
        float score = 0f;
        float radiusSquared = radius * radius;
        foreach (EnemyActor enemy in _enemies)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            float distanceSquared = (enemy.Position - center).LengthSquared();
            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            score += Mathf.Lerp(1f, 0.2f, Mathf.Clamp(Mathf.Sqrt(distanceSquared) / radius, 0f, 1f));
        }

        if (_boss != null && _boss.IsAlive)
        {
            float bossDistanceSquared = (_boss.Position - center).LengthSquared();
            if (bossDistanceSquared <= radiusSquared)
            {
                score += 3f * Mathf.Lerp(1f, 0.35f, Mathf.Clamp(Mathf.Sqrt(bossDistanceSquared) / radius, 0f, 1f));
            }
        }

        return score;
    }

    private static float GetWingSisterDensityWeight(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Pentachoron => 0.55f,
            WeaponKind.Circle => 0.35f,
            WeaponKind.Pyramid => 0.28f,
            _ => 0.18f,
        };
    }

    private static float GetWingSisterDistanceScore(float distanceSquared, float maxDistance)
    {
        float maxDistanceSquared = maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue;
        if (!float.IsFinite(maxDistanceSquared) || maxDistanceSquared <= 0f)
        {
            return 0f;
        }

        return Mathf.Lerp(1.25f, 0.15f, Mathf.Clamp(distanceSquared / maxDistanceSquared, 0f, 1f));
    }

    private static bool UsesWingSisterZoneGuidance(WeaponKind kind)
    {
        return kind is WeaponKind.Hexagon or WeaponKind.Cone or WeaponKind.Tesseract or WeaponKind.ImpossibleCube;
    }

    private static bool HasWingModulatorKey(IReadOnlyList<WeaponModulatorData> modulators, string normalizedKey)
    {
        for (int modulatorIndex = 0; modulatorIndex < modulators.Count; modulatorIndex++)
        {
            if (string.Equals(modulators[modulatorIndex].NormalizedKey, normalizedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}