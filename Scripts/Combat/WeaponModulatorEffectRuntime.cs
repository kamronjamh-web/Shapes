using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
    private const float ModulatorLingeringZoneTickInterval = 0.35f;

    private sealed class ModulatorLingeringZoneEffect
    {
        public Vector2 Position;
        public float Age;
        public float Tick;
        public float Duration;
        public float Radius;
        public float TotalDamage;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class ModulatorMarkedTargetState
    {
        public float TimeLeft;
        public float DamageMultiplier = 1f;
    }

    private readonly List<ModulatorLingeringZoneEffect> _modulatorLingeringZones = new();
    private readonly Dictionary<EnemyActor, ModulatorMarkedTargetState> _modulatorMarkedEnemies = new();
    private float _modulatorBossMarkTimeLeft;
    private float _modulatorBossMarkDamageMultiplier = 1f;

    private void ApplyModulatorOnFireEffects(WeaponKind kind, Vector2 direction, Vector2 origin, LegacyWeaponStats stats, bool isEcho)
    {
        EquipmentItem? sourceItem = stats.SourceItem;
        if (sourceItem == null || isEcho)
        {
            return;
        }

        WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.OnFire, effect =>
        {
            if (!WeaponModulatorSharedRuntime.IsOnFireEchoEffect(effect))
            {
                return;
            }

            int echoCount = WeaponModulatorSharedRuntime.ResolveEchoCount(effect);
            float delay = WeaponModulatorSharedRuntime.ResolveEchoDelaySeconds(effect);
            float damageScale = WeaponModulatorSharedRuntime.ResolveEchoDamageScale(effect);
            for (int echoIndex = 0; echoIndex < echoCount; echoIndex++)
            {
                ScheduleLegacyEcho(kind, origin, direction, damageScale, stats.SourceDamage, stats.SourceRarity, sourceItem, delay + echoIndex * 0.05f);
            }
        });
    }

    private float ApplyModulatorEnemyDamageMultipliers(EnemyActor enemy, float damage, EquipmentItem? sourceItem)
    {
        if (sourceItem == null || damage <= 0f)
        {
            return damage;
        }

        bool enemyIsElite = enemy.Kind is EnemyKind.Interceptor or EnemyKind.Bulwark or EnemyKind.Charger or EnemyKind.Mortar;
        WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.OnHit, effect =>
        {
            if (WeaponModulatorSharedRuntime.IsOnHitApplyMark(effect))
            {
                if (_modulatorMarkedEnemies.TryGetValue(enemy, out ModulatorMarkedTargetState? markState) && markState.TimeLeft > 0f)
                {
                    damage *= Mathf.Max(1f, markState.DamageMultiplier);
                }

                return;
            }

            if (WeaponModulatorSharedRuntime.IsOnHitExecuteLowHealth(effect))
            {
                float threshold = WeaponModulatorSharedRuntime.ResolveExecuteThreshold(effect);
                float executeMultiplier = WeaponModulatorSharedRuntime.ResolveExecuteDamageMultiplier(effect);
                float hpRatio = enemy.MaxHp > 0f ? enemy.Hp / enemy.MaxHp : 1f;
                if (hpRatio <= threshold)
                {
                    damage *= Mathf.Max(1f, executeMultiplier);
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

    private float ApplyModulatorBossDamageMultipliers(float damage, EquipmentItem? sourceItem)
    {
        if (sourceItem == null || damage <= 0f)
        {
            return damage;
        }

        WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.OnHit, effect =>
        {
            if (WeaponModulatorSharedRuntime.IsOnHitApplyMark(effect))
            {
                if (_modulatorBossMarkTimeLeft > 0f)
                {
                    damage *= Mathf.Max(1f, _modulatorBossMarkDamageMultiplier);
                }

                return;
            }

            if (WeaponModulatorSharedRuntime.IsOnHitExecuteLowHealth(effect) && _boss != null)
            {
                float threshold = WeaponModulatorSharedRuntime.ResolveExecuteThreshold(effect);
                float executeMultiplier = WeaponModulatorSharedRuntime.ResolveExecuteDamageMultiplier(effect);
                float bossCapMultiplier = WeaponModulatorSharedRuntime.ResolveExecuteBossCapMultiplier(effect, executeMultiplier);
                float hpRatio = _boss.MaxHp > 0f ? _boss.Hp / _boss.MaxHp : 1f;
                if (hpRatio <= threshold)
                {
                    damage *= Mathf.Max(1f, Mathf.Min(executeMultiplier, bossCapMultiplier));
                }

                return;
            }

            if (WeaponModulatorSharedRuntime.IsOnHitEliteBossHunter(effect))
            {
                damage *= WeaponModulatorSharedRuntime.ResolveEliteBossHunterEliteMultiplier(effect);
            }
        });

        return damage;
    }

    private void ApplyModulatorOnEnemyHitEffects(EnemyActor enemy, float damage, Vector2 hitPosition, EquipmentItem? sourceItem, bool critical)
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

    private void ApplyModulatorOnBossHitEffects(float damage, Vector2 hitPosition, EquipmentItem? sourceItem, bool critical)
    {
        if (sourceItem == null)
        {
            return;
        }

        WeaponModulatorSharedRuntime.ForEachResolvedEffect(sourceItem, ModulatorEffectTrigger.OnHit, effect =>
        {
            if (WeaponModulatorSharedRuntime.IsOnHitApplyMark(effect))
            {
                _modulatorBossMarkTimeLeft = Mathf.Max(_modulatorBossMarkTimeLeft, WeaponModulatorSharedRuntime.ResolveMarkDurationSeconds(effect));
                _modulatorBossMarkDamageMultiplier = Mathf.Max(_modulatorBossMarkDamageMultiplier, WeaponModulatorSharedRuntime.ResolveMarkDamageMultiplier(effect));
                return;
            }

            if (WeaponModulatorSharedRuntime.IsOnHitChainToNearby(effect))
            {
                TriggerModulatorChainReaction(hitPosition, null, damage, critical, sourceItem, effect);
                return;
            }

            if (WeaponModulatorSharedRuntime.IsOnHitLingeringZone(effect))
            {
                SpawnModulatorLingeringZone(hitPosition, damage, critical, sourceItem, effect);
            }
        });
    }

    private void ApplyEnemyMark(EnemyActor enemy, WeaponModulatorEffectData effect)
    {
        float timeLeft = WeaponModulatorSharedRuntime.ResolveMarkDurationSeconds(effect);
        float damageMultiplier = WeaponModulatorSharedRuntime.ResolveMarkDamageMultiplier(effect);
        if (_modulatorMarkedEnemies.TryGetValue(enemy, out ModulatorMarkedTargetState? existing))
        {
            existing.TimeLeft = Mathf.Max(existing.TimeLeft, timeLeft);
            existing.DamageMultiplier = Mathf.Max(existing.DamageMultiplier, damageMultiplier);
            return;
        }

        _modulatorMarkedEnemies[enemy] = new ModulatorMarkedTargetState
        {
            TimeLeft = timeLeft,
            DamageMultiplier = damageMultiplier,
        };
    }

    private void TriggerModulatorChainReaction(Vector2 origin, EnemyActor? initialTarget, float damage, bool critical, EquipmentItem sourceItem, WeaponModulatorEffectData effect)
    {
        int remainingChains = WeaponModulatorSharedRuntime.ResolveChainCount(effect);
        float range = WeaponModulatorSharedRuntime.ResolveChainRange(effect);
        float damageScale = WeaponModulatorSharedRuntime.ResolveChainDamageScale(effect);
        float currentDamage = damage * damageScale;
        Vector2 current = origin;
        bool bossHit = false;
        HashSet<EnemyActor> visited = new();
        if (initialTarget != null)
        {
            visited.Add(initialTarget);
        }

        ChainEffect chain = new()
        {
            Color = GetWeaponShotColor(MapEquipmentShapeToWeapon(sourceItem.Shape?.Key), MapEquipmentRarity(sourceItem.Rarity?.Key)),
            IsCritical = critical,
        };

        while (remainingChains > 0 && currentDamage > 0.01f)
        {
            EnemyActor? nextEnemy = FindNearestEnemyActor(current, range, visited);
            if (nextEnemy != null)
            {
                visited.Add(nextEnemy);
                AddChainBeam(chain, current, nextEnemy.Position, chain.Color);
                DealDamageToEnemy(nextEnemy, currentDamage, current, false, critical, false, sourceItem, false);
                current = nextEnemy.Position;
                currentDamage *= 0.85f;
                remainingChains--;
                continue;
            }

            if (!bossHit && _boss != null && _boss.IsAlive && (_boss.Position - current).Length() <= range)
            {
                bossHit = true;
                AddChainBeam(chain, current, _boss.Position, chain.Color);
                DealDamageToBoss(currentDamage, critical, false, sourceItem, false);
            }

            break;
        }

        if (chain.Beams.Count > 0)
        {
            _legacyChains.Add(chain);
        }
    }

    private void SpawnModulatorLingeringZone(Vector2 position, float damage, bool critical, EquipmentItem sourceItem, WeaponModulatorEffectData effect)
    {
        float damageMultiplier = WeaponModulatorSharedRuntime.ResolveLingeringZoneDamageScale(effect);
        float radius = WeaponModulatorSharedRuntime.ResolveLingeringZoneRadius(effect);
        float duration = WeaponModulatorSharedRuntime.ResolveLingeringZoneDurationSeconds(effect);
        Color color = GetWeaponShotColor(MapEquipmentShapeToWeapon(sourceItem.Shape?.Key), MapEquipmentRarity(sourceItem.Rarity?.Key));
        MeshInstance3D mesh = CreateDiskVisual(position, radius, color, 0.18f, 0.08f);

        _modulatorLingeringZones.Add(new ModulatorLingeringZoneEffect
        {
            Position = position,
            Duration = duration,
            Radius = radius,
            TotalDamage = damage * damageMultiplier,
            Color = color,
            IsCritical = critical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void TickModulatorEffectState(float delta)
    {
        TickMarkedTargets(delta);
        TickLingeringZones(delta);
    }

    private void TickMarkedTargets(float delta)
    {
        List<EnemyActor>? expired = null;
        foreach (KeyValuePair<EnemyActor, ModulatorMarkedTargetState> pair in _modulatorMarkedEnemies)
        {
            EnemyActor enemy = pair.Key;
            ModulatorMarkedTargetState state = pair.Value;
            state.TimeLeft -= delta;
            if (!IsInstanceValid(enemy) || !enemy.IsAlive || state.TimeLeft <= 0f)
            {
                expired ??= new List<EnemyActor>();
                expired.Add(enemy);
            }
        }

        if (expired != null)
        {
            for (int i = 0; i < expired.Count; i++)
            {
                _modulatorMarkedEnemies.Remove(expired[i]);
            }
        }

        _modulatorBossMarkTimeLeft = Mathf.Max(0f, _modulatorBossMarkTimeLeft - delta);
        if (_modulatorBossMarkTimeLeft <= 0f)
        {
            _modulatorBossMarkDamageMultiplier = 1f;
        }
    }

    private void TickLingeringZones(float delta)
    {
        for (int zoneIndex = _modulatorLingeringZones.Count - 1; zoneIndex >= 0; zoneIndex--)
        {
            ModulatorLingeringZoneEffect zone = _modulatorLingeringZones[zoneIndex];
            zone.Age += delta;
            zone.Tick -= delta;
            if (zone.Age >= zone.Duration)
            {
                QueueFreeIfValid(zone.Mesh);
                _modulatorLingeringZones.RemoveAt(zoneIndex);
                continue;
            }

            if (zone.Tick <= 0f)
            {
                zone.Tick = ModulatorLingeringZoneTickInterval;
                float tickDamage = zone.TotalDamage * ModulatorLingeringZoneTickInterval / Mathf.Max(0.01f, zone.Duration);
                DamageTargetsInRadius(zone.Position, zone.Radius, tickDamage, zone.IsCritical, zone.SourceItem, false);
            }

            float fadeOut = Mathf.Min(1f, (zone.Duration - zone.Age) / Mathf.Max(0.01f, zone.Duration));
            float pulse = 0.92f + Mathf.Sin(zone.Age * 9f) * 0.08f;
            zone.Mesh.Scale = new Vector3(zone.Radius * pulse, 1f, zone.Radius * pulse);
            zone.Mesh.Rotation = new Vector3(0f, zone.Age * 2.4f, 0f);
            SetMaterialAlpha(zone.Material, 0.18f * fadeOut);
        }
    }

    private void ResetModulatorEffectState()
    {
        foreach (ModulatorLingeringZoneEffect zone in _modulatorLingeringZones)
        {
            QueueFreeIfValid(zone.Mesh);
        }

        _modulatorLingeringZones.Clear();
        _modulatorMarkedEnemies.Clear();
        _modulatorBossMarkTimeLeft = 0f;
        _modulatorBossMarkDamageMultiplier = 1f;
    }
}