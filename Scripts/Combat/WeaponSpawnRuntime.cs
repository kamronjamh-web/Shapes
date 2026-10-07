using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild;

public partial class Main
{
    private const float BaseWeaponEngageRange = 90f;
    private const float LegacyMineLife = 8f;
    private const float LegacyMineArmTime = 0.3f;
    private const float LegacyMineTriggerRadius = 3.5f;
    private const float LegacyMineExplosionRadius = 8f;
    private const float LegacyNovaDuration = 0.6f;
    private const float LegacyNovaMaxRadius = 35f;
    private const float LegacyLaserDuration = 0.35f;
    private const float LegacyLaserTickRate = 0.05f;
    private const float LegacyLaserHalfWidth = 1.2f;
    private const float LegacyFlameDuration = 0.45f;
    private const float LegacyFlameTickRate = 0.06f;
    private const float LegacyFlameHalfAngle = 0.55f;
    private const float LegacyFlameRange = 25f;
    private const float LegacyOrbitalLife = 10f;
    private const float LegacyOrbitalSpeed = 3.5f;
    private const float LegacyOrbitalRadius = 6f;
    private const float LegacyOrbitalHitRadius = 2.5f;
    private const float LegacyOrbitalHitCooldown = 0.25f;
    private const float LegacySeekerSpeed = 35f;
    private const float LegacySeekerTurnRate = 4.5f;
    private const float LegacyMortarFlightTime = 1f;
    private const float LegacyMortarArcHeight = 35f;
    private const float LegacyMortarExplosionRadius = 12f;
    private const float LegacyRiftDuration = 3f;
    private const float LegacyRiftPulseRate = 0.4f;
    private const float LegacyRiftBaseRadius = 10f;
    private const float LegacyChainRange = 18f;
    private const float LegacyChainDuration = 0.35f;
    private const float LegacyChainDamageDecay = 0.85f;
    private const float LegacyDrillSpeed = 50f;
    private const float LegacyDrillHitRadius = 2.5f;
    private const float LegacyDrillBurrowDuration = 1.2f;
    private const float LegacyDrillTickRate = 0.15f;
    private const float LegacyDrillLife = 5f;
    private const float LegacyVoidDuration = 4f;
    private const float LegacyVoidPullRadius = 30f;
    private const float LegacyVoidDamageRadius = 8f;
    private const float LegacyVoidPullForce = 40f;
    private const float LegacyVoidTickRate = 0.1f;
    private const float LegacyWrathTickRate = 0.2f;
    private const float LegacyWrathBaseRadius = 20f;
    private const float LegacyEchoDelay = 0.5f;
    private const float LegacyCloneLife = 5f;
    private const float LegacyCloneDamageMult = 0.6f;
    private const float LegacyCloneOrbitSpeed = 1.2f;
    private const float LegacyCloneOrbitDistance = 15f;

    private readonly Dictionary<BulletActor, int> _legacyPierceLeft = new();
    private readonly Dictionary<BulletActor, int> _legacySawReturnLeft = new();
    private readonly Dictionary<BulletActor, HomingBulletState> _legacyHomingBullets = new();
    private readonly List<TimedBeamEffect> _legacyBeams = new();
    private readonly List<MineEffect> _legacyMines = new();
    private readonly List<NovaEffect> _legacyNovas = new();
    private readonly List<FlameEffect> _legacyFlames = new();
    private readonly List<OrbitalEffect> _legacyOrbitals = new();
    private readonly List<RiftEffect> _legacyRifts = new();
    private readonly List<MortarEffect> _legacyMortars = new();
    private readonly List<ChainEffect> _legacyChains = new();
    private readonly List<DrillEffect> _legacyDrills = new();
    private readonly List<VoidEffect> _legacyVoids = new();
    private readonly List<CloneEffect> _legacyClones = new();
    private readonly List<DelayedEcho> _legacyEchoes = new();
    private readonly List<VisualPulseEffect> _legacyVisualPulses = new();
    private float _legacyWrathTick = 0f;
    private float _modulatorCritCascadeCooldown;
    private float _packHunterMarkTimeLeft;
    private Vector2 _packHunterMarkPosition;
    private float _bloodPriceStoredDamage;
    private float _bloodPricePulseTimer;
    private int _omniFeedbackHitCounter;

    private struct LegacyWeaponStats
    {
        public float Damage;
        public float Radius;
        public float Life;
        public float Speed;
        public float HomingTurnRate;
        public float RangeMultiplier;
        public float SizeScale;
        public int ProjectileCount;
        public int PierceCount;
        public Color Color;
        public bool IsCritical;
        public float SourceDamage;
        public RarityTier SourceRarity;
        public EquipmentItem? SourceItem;
    }

    private sealed class HomingBulletState
    {
        public float Speed;
        public float TurnRate;
        public bool PackHunterEnabled;
        public float PackHunterMarkDuration;
        public float PackHunterBias = 1f;
    }

    private sealed class TimedBeamEffect
    {
        public LaserBeamActor Actor = null!;
        public Vector2 Start;
        public Vector2 End;
        public float HalfWidth;
        public float Damage;
        public Color Color;
        public bool PrismForkEnabled;
        public bool PrismForkFired;
        public float PrismForkDelay = 0.25f;
        public float PrismForkDamageScale = 0.40f;
        public float Duration;
        public float Age;
        public float Tick;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
    }

    private sealed class MineEffect
    {
        public Vector2 Position;
        public float Age;
        public float Life;
        public float Damage;
        public float Radius;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public bool SpawnClusterOnDetonation;
        public int ClusterChildCount;
        public float ClusterChildDamageScale;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class NovaEffect
    {
        public Vector2 Position;
        public float Age;
        public float Duration;
        public float Damage;
        public float MaxRadius;
        public float ReturnPulseDamageScale;
        public bool ReturnPulseTriggered;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
        public readonly HashSet<EnemyActor> HitEnemies = new();
        public bool HitBoss;
    }

    private sealed class FlameEffect
    {
        public Vector2 Origin;
        public Vector2 Direction;
        public float Age;
        public float Tick;
        public float Duration;
        public float Damage;
        public float Range;
        public float CorridorDamageScale;
        public float CorridorDuration;
        public float CorridorTick;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class OrbitalEffect
    {
        public float Angle;
        public float Age;
        public float Life;
        public float Damage;
        public float OrbitRadius;
        public float Speed;
        public float SizeScale;
        public float TrailDamageScale;
        public float TrailTick;
        public float ReverseInterval;
        public float ReverseTimer;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
        public readonly Dictionary<Node3D, float> LastHit = new();
    }

    private sealed class RiftEffect
    {
        public Vector2 Position;
        public float Age;
        public float Tick;
        public float Duration;
        public float Damage;
        public float Radius;
        public bool AlternatingPushPull;
        public bool PushPhase;
        public float PulseInterval = LegacyRiftPulseRate;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class MortarEffect
    {
        public Vector2 Start;
        public Vector2 Target;
        public float Age;
        public float FlightTime;
        public float Damage;
        public float Radius;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class ChainEffect
    {
        public float Age;
        public Color Color;
        public bool IsCritical;
        public readonly List<LaserBeamActor> Beams = new();
    }

    private sealed class DrillEffect
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public float Tick;
        public float BurrowAge;
        public float Damage;
        public float WakeDamageScale;
        public float WakeDuration;
        public float WakeTick;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public EnemyActor? TargetEnemy;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class VoidEffect
    {
        public Vector2 Position;
        public float Age;
        public float Tick;
        public float Duration;
        public float Damage;
        public float PullRadius;
        public float DamageRadius;
        public float PullStrengthMultiplier = 1f;
        public bool EndBurstEnabled;
        public float EndBurstMultiplier;
        public int EndShardCount;
        public float EndShardDamageScale;
        public Color Color;
        public bool IsCritical;
        public EquipmentItem? SourceItem;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class CloneEffect
    {
        public Vector2 Center;
        public float Angle;
        public float Age;
        public float Life;
        public float FireTimer;
        public Color Color;
        public float SourceDamage;
        public RarityTier SourceRarity;
        public EquipmentItem? SourceItem;
        public bool MimicPrimaryShot;
        public float MimicDamageScale = 0.35f;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private sealed class DelayedEcho
    {
        public WeaponKind Kind;
        public Vector2 Origin;
        public Vector2 Direction;
        public float TimeLeft;
        public float DamageScale;
        public float SourceDamage;
        public RarityTier SourceRarity;
        public EquipmentItem? SourceItem;
    }

    private sealed class VisualPulseEffect
    {
        public Vector2 Position;
        public float Age;
        public float Duration;
        public float StartRadius;
        public float EndRadius;
        public Color Color;
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Material = null!;
    }

    private void FireLegacyWeaponVolley(Vector2 targetPosition)
    {
        ResolveArenaWeaponShot(0, targetPosition, targetPosition - _player.Position, out Vector2 origin, out Vector2 direction);
        NotifyArenaWeaponFired(0, direction);
        FireLegacyWeaponForKind(_weaponKind, direction, origin, 1f, false);
    }

    private void FireLegacyWeaponForKind(WeaponKind kind, Vector2 direction, Vector2 origin, float damageScale, bool isEcho, float? sourceDamageOverride = null, RarityTier? sourceRarityOverride = null, EquipmentItem? sourceItemOverride = null, Vector2? aimPointOverride = null, WingSupportShotTuning? shotTuning = null)
    {
        direction = RuntimeMath.SafeDirection(direction, Vector2.Right);
        origin = RuntimeMath.FiniteOr(origin, _player.Position);
        LegacyWeaponStats stats = BuildLegacyWeaponStats(kind, damageScale, sourceDamageOverride, sourceRarityOverride, sourceItemOverride, shotTuning);
        FireLegacyWeaponPattern(kind, direction, origin, stats, isEcho, aimPointOverride);
        ApplyModulatorOnFireEffects(kind, direction, origin, stats, isEcho);
    }

    private LegacyWeaponStats BuildLegacyWeaponStats(WeaponKind kind, float damageScale, float? sourceDamageOverride = null, RarityTier? sourceRarityOverride = null, EquipmentItem? sourceItemOverride = null, WingSupportShotTuning? shotTuning = null)
    {
        EquipmentItem? effectSourceItem = sourceItemOverride ?? _equippedLoadout[0];
        RarityTier sourceRarity = sourceRarityOverride ?? _weaponRarity;
        float sourceDamage = sourceDamageOverride ?? _player.BulletDamage;
        float projectileSizeScale = sourceItemOverride == null
            ? _bulletRadiusScale
            : GetSupportWeaponProjectileSizeScale(sourceItemOverride);
        float radius = Mathf.Clamp(RuntimeMath.FiniteOr(BaseBulletRadius * projectileSizeScale, BaseBulletRadius), 0.05f, 80f);
        float damage = Mathf.Max(0f, RuntimeMath.FiniteOr(sourceDamage * damageScale, BaseBulletDamage));
        float rangeMultiplier = sourceItemOverride == null
            ? GetEquipmentWeaponRangeMultiplier()
            : GetWeaponItemRangeMultiplier(sourceItemOverride);
        float life = Mathf.Max(0.01f, RuntimeMath.FiniteOr(GetWeaponShotLife(kind, sourceRarity) * rangeMultiplier, BaseShotLife));
        float speed = RuntimeMath.FiniteOr(
            sourceItemOverride == null ? _player.BulletSpeed : GetWeaponProjectileSpeed(kind, sourceRarity, sourceItemOverride),
            BaseBulletSpeed);
        bool isCritical = false;

        if (_arenaDebugOverridesActive)
        {
            radius *= Mathf.Max(0.05f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponSizePercent / 100f, 1f));
            damage *= Mathf.Max(0f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.WeaponDamagePercentBonus / 100f, 1f));
            life *= Mathf.Max(0.01f, RuntimeMath.FiniteOr(1f + _arenaDebugOverrides.RangeFlat / 100f, 1f));

            float critChance = Mathf.Clamp(_arenaDebugOverrides.CritChancePercent / 100f, 0f, 1f);
            if (critChance > 0f && _rng.Randf() < critChance)
            {
                damage *= 1f + _arenaDebugOverrides.CritDamagePercent / 100f;
                isCritical = true;
            }
        }

        damage = ApplyEquipmentShotDamageModifiers(damage, out bool equipmentCritical, sourceItemOverride);
        isCritical |= equipmentCritical;

        if (shotTuning.HasValue && sourceItemOverride != null)
        {
            WingSupportShotTuning tuning = shotTuning.Value;
            damage *= Mathf.Max(0f, RuntimeMath.FiniteOr(tuning.DamageMultiplier, 1f));

            float bonusCritDamagePercent = Mathf.Max(0f, RuntimeMath.FiniteOr(tuning.BonusCritDamagePercent, 0f));
            float sourceCritDamagePercent = GetWeaponItemCritDamagePercent(sourceItemOverride);
            if (isCritical)
            {
                if (bonusCritDamagePercent > 0f)
                {
                    float currentCritMultiplier = GetCritDamageMultiplier(sourceCritDamagePercent);
                    float tunedCritMultiplier = GetCritDamageMultiplier(sourceCritDamagePercent + bonusCritDamagePercent);
                    damage *= tunedCritMultiplier / Mathf.Max(0.01f, currentCritMultiplier);
                }
            }
            else
            {
                float bonusCritChance = Mathf.Clamp(RuntimeMath.FiniteOr(tuning.BonusCritChancePercent, 0f) / 100f, 0f, 1f);
                if (tuning.ForceCritical || (bonusCritChance > 0f && _rng.Randf() < bonusCritChance))
                {
                    damage *= GetCritDamageMultiplier(sourceCritDamagePercent + bonusCritDamagePercent);
                    isCritical = true;
                }
            }
        }

        radius = Mathf.Clamp(RuntimeMath.FiniteOr(radius, BaseBulletRadius), 0.05f, 80f);
        damage = Mathf.Max(0f, RuntimeMath.FiniteOr(damage, BaseBulletDamage));
        life = Mathf.Clamp(RuntimeMath.FiniteOr(life, BaseShotLife), 0.01f, 30f);
        speed = RuntimeMath.FiniteOr(speed, BaseBulletSpeed);

        int extraProjectiles = _arenaDebugOverridesActive
            ? Mathf.Max(0, Mathf.RoundToInt(_arenaDebugOverrides.WeaponProjectileCountFlat))
            : 0;
        extraProjectiles += sourceItemOverride == null
            ? Mathf.Max(0, Mathf.RoundToInt(_equipmentProjectileCountFlat))
            : GetWeaponItemProjectileCount(sourceItemOverride);
        int extraPierce = sourceItemOverride == null
            ? GetEquipmentPierceCountBonus()
            : GetWeaponItemPierceCount(sourceItemOverride);
        float homingTurnRate = LegacySeekerTurnRate * (sourceItemOverride == null
            ? GetEquipmentHomingStrengthMultiplier()
            : GetWeaponItemHomingStrengthMultiplier(sourceItemOverride));
        int splitProjectiles = Mathf.FloorToInt(GetItemPower(RunItemId.SplitShot)) + GetModifierFlat(ModifierKind.ExtraSplitShot);

        return new LegacyWeaponStats
        {
            Damage = damage,
            Radius = radius,
            Life = life,
            Speed = speed,
            HomingTurnRate = homingTurnRate,
            RangeMultiplier = rangeMultiplier,
            SizeScale = Mathf.Max(0.35f, radius / BaseBulletRadius),
            ProjectileCount = Mathf.Max(1, 1 + extraProjectiles + splitProjectiles),
            PierceCount = extraPierce,
            Color = GetWeaponShotColor(kind, sourceRarity),
            IsCritical = isCritical,
            SourceDamage = Mathf.Max(0f, sourceDamage),
            SourceRarity = sourceRarity,
            SourceItem = effectSourceItem,
        };
    }

    private void FireLegacyWeaponPattern(WeaponKind kind, Vector2 direction, Vector2 origin, LegacyWeaponStats stats, bool isEcho, Vector2? aimPointOverride = null)
    {
        switch (kind)
        {
            case WeaponKind.Circle:
                SpawnLegacyBullet(origin, direction * (stats.Speed * 0.45f), stats.Damage, stats.Color, stats.Radius * 3.5f, stats.Life, 5 + stats.PierceCount, stats.IsCritical, stats.SourceItem);
                break;
            case WeaponKind.Triangle:
                SpawnLegacyLaser(origin, direction, 80f * stats.SizeScale * stats.RangeMultiplier, stats.Damage * 2.5f, LegacyLaserHalfWidth * stats.SizeScale, stats.Color, stats.IsCritical, stats.SourceItem);
                break;
            case WeaponKind.Hexagon:
                SpawnHexMines(origin, stats, aimPointOverride);
                break;
            case WeaponKind.Sphere:
                SpawnLegacyNova(origin, stats.Damage * 0.8f, (LegacyNovaMaxRadius + 7.2f) * stats.SizeScale, stats.Color, stats.IsCritical, stats.SourceItem);
                if (stats.SourceItem != null && HasWeaponItemPassiveFlag(stats.SourceItem, ModulatorEffectType.NewSecondPulse))
                {
                    ScheduleLegacyEcho(WeaponKind.Sphere, origin, direction, 0.60f, stats.SourceDamage, stats.SourceRarity, stats.SourceItem, 0.30f);
                }
                break;
            case WeaponKind.Cube:
                SpawnLegacyOrbitals(stats);
                break;
            case WeaponKind.Pyramid:
                SpawnLegacySeekers(origin, direction, stats);
                break;
            case WeaponKind.Cone:
                SpawnLegacyFlame(origin, direction, stats.Damage * 2f, (LegacyFlameRange + 9.6f) * stats.SizeScale, stats.Color, stats.IsCritical, stats.SourceItem);
                break;
            case WeaponKind.Tesseract:
                SpawnLegacyRift(ResolveLegacyAimPoint(origin, direction, aimPointOverride, 39f, 30f), stats.Damage * 1.5f, (LegacyRiftBaseRadius + 4.8f) * stats.SizeScale, stats.Color, stats.IsCritical, stats.SourceItem);
                break;
            case WeaponKind.Hyperphere:
                SpawnLegacyMortar(origin, ResolveLegacyAimPoint(origin, direction, aimPointOverride, 36f, 40f), stats.Damage, LegacyMortarExplosionRadius * stats.SizeScale, stats.Color, stats.IsCritical, stats.SourceItem);
                if (stats.SourceItem != null && HasWeaponTransformFlag(stats.SourceItem, "scorch_crater"))
                {
                    float craterDamage = stats.Damage * 0.30f;
                    SpawnLegacyRift(ResolveLegacyAimPoint(origin, direction, aimPointOverride, 36f, 40f), craterDamage, LegacyMortarExplosionRadius * stats.SizeScale * 0.90f, stats.Color, stats.IsCritical, stats.SourceItem);
                }
                break;
            case WeaponKind.Pentachoron:
                int chainBonus = stats.SourceItem == null ? 0 : GetWeaponItemChainCountBonus(stats.SourceItem);
                SpawnLegacyChainLightning(origin, stats.Damage * 1.4f, Mathf.Min(10, 5 + stats.ProjectileCount * 2 + chainBonus), stats.Color, stats.IsCritical, preferredTargetOverride: aimPointOverride, sourceItem: stats.SourceItem);
                if (stats.SourceItem != null && HasWeaponTransformFlag(stats.SourceItem, "closed_circuit"))
                {
                    SpawnLegacyChainLightning(origin, stats.Damage * 1.4f * 0.50f, 1, stats.Color, stats.IsCritical, preferredTargetOverride: aimPointOverride, sourceItem: stats.SourceItem);
                }
                break;
            case WeaponKind.Hypercone:
                SpawnLegacyDrill(origin, direction, stats.Damage * 1.8f, stats.Life, stats.Color, stats.IsCritical, stats.SourceItem);
                break;
            case WeaponKind.ImpossibleCube:
                SpawnLegacyVoid(
                    ResolveLegacyAimPoint(origin, direction, aimPointOverride, LegacyVoidPullRadius + 24f, 35f),
                    stats.Damage * 3f,
                    (LegacyVoidPullRadius + 7.2f) * stats.SizeScale,
                    (LegacyVoidDamageRadius + 2.4f) * stats.SizeScale,
                    stats.Color,
                    stats.IsCritical,
                    stats.SourceItem);
                break;
            case WeaponKind.Mobius:
                FireLegacyWeaponForKind(WeaponKind.Square, direction, origin, 0.70f, true, stats.SourceDamage, stats.SourceRarity, stats.SourceItem);
                if (!isEcho)
                {
                    ScheduleLegacyEcho(WeaponKind.Square, origin, direction, 0.55f, stats.SourceDamage, stats.SourceRarity, stats.SourceItem);
                    if (stats.SourceItem != null && HasWeaponTransformFlag(stats.SourceItem, "echo_weave"))
                    {
                        float mirrorPower = GetWeaponTransformMagnitude(stats.SourceItem, "echo_weave", 0.40f);
                        float mirrorDelay = GetWeaponTransformMagnitudeB(stats.SourceItem, "echo_weave", 0.60f);
                        ScheduleLegacyEcho(WeaponKind.Square, origin, direction, mirrorPower, stats.SourceDamage, stats.SourceRarity, stats.SourceItem, mirrorDelay);
                    }
                }
                break;
            case WeaponKind.Klein:
                SpawnVisualPulse(_player.Position, LegacyWrathBaseRadius * 0.4f, LegacyWrathBaseRadius, stats.Color, 0.35f);
                break;
            case WeaponKind.BoysSurface:
                SpawnLegacyClones(origin, Mathf.Max(1, stats.ProjectileCount), stats.Color, stats.SourceDamage, stats.SourceRarity, stats.SourceItem);
                break;
            default:
                if (kind == WeaponKind.Square && TryFireBurstlineConverter(origin, direction, stats, isEcho))
                {
                    break;
                }

                FireFlatStrike(origin, direction, stats);
                break;
        }
    }

    private bool TryFireBurstlineConverter(Vector2 origin, Vector2 direction, LegacyWeaponStats stats, bool isEcho)
    {
        if (stats.SourceItem == null || !HasWeaponTransformFlag(stats.SourceItem, "burstline_converter"))
        {
            return false;
        }

        LegacyWeaponStats burstStats = stats;
        burstStats.ProjectileCount = 1;
        FireFlatStrike(origin, direction, burstStats);

        if (isEcho)
        {
            return true;
        }

        int burstCount = Mathf.Clamp(GetWeaponTransformCount(stats.SourceItem, "burstline_converter", 3), 2, 6);
        float followUpDamageScale = Mathf.Clamp(GetWeaponTransformMagnitude(stats.SourceItem, "burstline_converter", 0.45f), 0.05f, 3f);
        float burstInterval = Mathf.Clamp(GetWeaponTransformMagnitudeB(stats.SourceItem, "burstline_converter", 0.18f), 0.02f, 1.5f);
        for (int burstIndex = 1; burstIndex < burstCount; burstIndex++)
        {
            float t = burstCount <= 1 ? 1f : burstIndex / (float)(burstCount - 1);
            float shotScale = Mathf.Lerp(1f, followUpDamageScale, t);
            Vector2 burstDirection = direction.Rotated(_rng.RandfRange(-0.03f, 0.03f) * burstIndex).Normalized();
            ScheduleLegacyEcho(WeaponKind.Square, origin, burstDirection, shotScale, stats.SourceDamage, stats.SourceRarity, stats.SourceItem, burstInterval * burstIndex);
        }

        return true;
    }

    private void FireFlatStrike(Vector2 origin, Vector2 direction, LegacyWeaponStats stats)
    {
        int projectileCount = stats.ProjectileCount;
        if (projectileCount <= 1)
        {
            SpawnLegacyBullet(origin, direction * stats.Speed, stats.Damage, stats.Color, stats.Radius, stats.Life, stats.PierceCount, stats.IsCritical, stats.SourceItem);
            return;
        }

        float halfArc = Mathf.DegToRad(8f * (projectileCount - 1));
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            float t = projectileCount == 1 ? 0.5f : projectileIndex / (float)(projectileCount - 1);
            float angle = Mathf.Lerp(-halfArc, halfArc, t);
            float damageScale = Mathf.Max(0.45f, 0.9f - projectileIndex * 0.05f);
            Vector2 shotDirection = direction.Rotated(angle);
            SpawnLegacyBullet(origin, shotDirection * stats.Speed, stats.Damage * damageScale, stats.Color, stats.Radius * 0.92f, stats.Life, stats.PierceCount, stats.IsCritical, stats.SourceItem);
        }
    }

    private BulletActor SpawnLegacyBullet(Vector2 origin, Vector2 velocity, float damage, Color color, float radius, float life, int pierce, bool isCritical = false, EquipmentItem? sourceItem = null)
    {
        BulletActor bullet = SpawnBullet(origin, velocity, damage, TeamId.Player, color, radius, life, isCritical, sourceItem);
        if (pierce > 0)
        {
            _legacyPierceLeft[bullet] = pierce;
        }

        RegisterSawReturnState(bullet, sourceItem);

        return bullet;
    }

    private void RegisterSawReturnState(BulletActor bullet, EquipmentItem? sourceItem)
    {
        if (sourceItem == null)
        {
            _legacySawReturnLeft.Remove(bullet);
            return;
        }

        if (!HasWeaponItemPassiveFlag(sourceItem, ModulatorEffectType.NewSawReturn))
        {
            _legacySawReturnLeft.Remove(bullet);
            return;
        }

        _legacySawReturnLeft[bullet] = 1;
    }

    private void SpawnLegacySeekers(Vector2 origin, Vector2 direction, LegacyWeaponStats stats)
    {
        int count = Mathf.Max(1, stats.ProjectileCount);
        float lockBias = stats.SourceItem == null
            ? 1f
            : GetWeaponItemPassiveMaxMagnitude(stats.SourceItem, ModulatorEffectType.NewTargetLockBias, 1f);
        bool packHunters = stats.SourceItem != null && HasWeaponTransformFlag(stats.SourceItem, "pack_hunters");
        float packHunterMarkDuration = stats.SourceItem == null ? 3f : Mathf.Clamp(GetWeaponTransformMagnitude(stats.SourceItem, "pack_hunters", 3f), 0.1f, 15f);
        float packHunterBias = stats.SourceItem == null ? 1.3f : Mathf.Clamp(GetWeaponTransformMagnitudeB(stats.SourceItem, "pack_hunters", 1.3f), 1f, 4f);
        Vector2? lockTarget = null;
        if (packHunters && _packHunterMarkTimeLeft > 0f)
        {
            lockTarget = _packHunterMarkPosition;
            lockBias = Mathf.Max(lockBias, packHunterBias);
        }
        else if (lockBias > 1f && TryFindNearestTarget(origin, 220f, out Vector2 lockTargetPosition))
        {
            lockTarget = lockTargetPosition;
        }

        float halfArc = Mathf.DegToRad(10f * Mathf.Max(0, count - 1));
        for (int seekerIndex = 0; seekerIndex < count; seekerIndex++)
        {
            float t = count == 1 ? 0.5f : seekerIndex / (float)(count - 1);
            float angle = Mathf.Lerp(-halfArc, halfArc, t) + _rng.RandfRange(-0.10f, 0.10f);
            Vector2 seekerDirection = direction.Rotated(angle).Normalized();
            if (lockTarget.HasValue)
            {
                Vector2 lockDirection = RuntimeMath.SafeDirection(lockTarget.Value - origin, seekerDirection);
                float blend = Mathf.Clamp((lockBias - 1f) * 0.55f, 0f, 0.95f);
                seekerDirection = seekerDirection.Lerp(lockDirection, blend).Normalized();
            }

            BulletActor bullet = SpawnLegacyBullet(origin, seekerDirection * stats.Speed, stats.Damage * 1.3f, stats.Color, stats.Radius * 1.05f, stats.Life, stats.PierceCount, stats.IsCritical, stats.SourceItem);
            _legacyHomingBullets[bullet] = new HomingBulletState
            {
                Speed = stats.Speed,
                TurnRate = stats.HomingTurnRate,
                PackHunterEnabled = packHunters,
                PackHunterMarkDuration = packHunterMarkDuration,
                PackHunterBias = packHunterBias,
            };
        }
    }

    private void SpawnHexMines(Vector2 origin, LegacyWeaponStats stats, Vector2? deploymentCenterOverride = null)
    {
        float durationMultiplier = stats.SourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(stats.SourceItem);
        Vector2 center = deploymentCenterOverride.HasValue
            ? ClampResolve(deploymentCenterOverride.Value, 1f)
            : origin;
        int mineCount = Mathf.Max(1, stats.ProjectileCount);
        bool clusterTransform = stats.SourceItem != null && HasWeaponTransformFlag(stats.SourceItem, "command_cluster");
        int clusterCount = stats.SourceItem == null ? 0 : Mathf.Clamp(GetWeaponTransformCount(stats.SourceItem, "command_cluster", 3), 1, 8);
        float clusterDamageScale = stats.SourceItem == null ? 0.35f : Mathf.Clamp(GetWeaponTransformMagnitude(stats.SourceItem, "command_cluster", 0.35f), 0.05f, 3f);
        for (int mineIndex = 0; mineIndex < mineCount; mineIndex++)
        {
            float angle = (mineIndex / (float)mineCount) * Mathf.Tau + _rng.RandfRange(-0.25f, 0.25f);
            float distance = _rng.RandfRange(5f, 17f);
            Vector2 position = ClampResolve(center + AngleToDir(angle) * distance, 1f);
            MeshInstance3D mesh = CreateDiskVisual(position, LegacyMineExplosionRadius * 0.22f * stats.SizeScale, stats.Color, 0.38f, 0.18f);
            _legacyMines.Add(new MineEffect
            {
                Position = position,
                Life = LegacyMineLife * durationMultiplier,
                Damage = stats.Damage * 1.5f,
                Radius = LegacyMineExplosionRadius * stats.SizeScale,
                Color = stats.Color,
                IsCritical = stats.IsCritical,
                SourceItem = stats.SourceItem,
                SpawnClusterOnDetonation = clusterTransform,
                ClusterChildCount = clusterCount,
                ClusterChildDamageScale = clusterDamageScale,
                Mesh = mesh,
                Material = (StandardMaterial3D)mesh.MaterialOverride,
            });
        }
    }

    private void SpawnLegacyLaser(Vector2 origin, Vector2 direction, float length, float damage, float halfWidth, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float durationMultiplier = sourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(sourceItem);
        float angle = Mathf.Atan2(direction.Y, direction.X);
        float hitLength = RaycastPillarDistance(origin, angle, length, halfWidth * 1.5f);
        Vector2 end = origin + direction * hitLength;

        LaserBeamActor beam = new();
        AddChild(beam);
        beam.SetBeam(origin, end, halfWidth, color, false);

        _legacyBeams.Add(new TimedBeamEffect
        {
            Actor = beam,
            Start = origin,
            End = end,
            HalfWidth = halfWidth,
            Damage = damage,
            Color = color,
            PrismForkEnabled = sourceItem != null && HasWeaponTransformFlag(sourceItem, "prism_refraction"),
            PrismForkDelay = sourceItem == null ? 0.25f : Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "prism_refraction", 0.25f), 0.05f, 2f),
            PrismForkDamageScale = sourceItem == null ? 0.40f : Mathf.Clamp(GetWeaponTransformMagnitudeB(sourceItem, "prism_refraction", 0.40f), 0.05f, 3f),
            Duration = LegacyLaserDuration * durationMultiplier,
            Tick = 0f,
            IsCritical = isCritical,
            SourceItem = sourceItem,
        });
    }

    private void SpawnLegacyNova(Vector2 position, float damage, float maxRadius, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float durationMultiplier = sourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(sourceItem);
        MeshInstance3D mesh = CreateDiskVisual(position, 0.5f, color, 0.28f, 0.08f);
        _legacyNovas.Add(new NovaEffect
        {
            Position = position,
            Duration = LegacyNovaDuration * durationMultiplier,
            Damage = damage,
            MaxRadius = maxRadius,
            ReturnPulseDamageScale = sourceItem != null && HasWeaponTransformFlag(sourceItem, "collapse_pulse")
                ? Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "collapse_pulse", 0.65f), 0.05f, 3f)
                : 0f,
            Color = color,
            IsCritical = isCritical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void SpawnLegacyFlame(Vector2 origin, Vector2 direction, float damage, float range, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float durationMultiplier = sourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(sourceItem);
        MeshInstance3D mesh = CreateBoxVisual(origin + direction * (range * 0.45f), new Vector3(range * 0.55f, 0.16f, range), color, 0.24f, 0.16f);
        mesh.Rotation = new Vector3(0f, Mathf.Atan2(direction.X, direction.Y), 0f);

        _legacyFlames.Add(new FlameEffect
        {
            Origin = origin,
            Direction = direction,
            Duration = LegacyFlameDuration * durationMultiplier,
            Damage = damage,
            Range = range,
            CorridorDamageScale = sourceItem != null && HasWeaponTransformFlag(sourceItem, "backdraft_corridor")
                ? Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "backdraft_corridor", 0.32f), 0.05f, 3f)
                : 0f,
            CorridorDuration = sourceItem != null && HasWeaponTransformFlag(sourceItem, "backdraft_corridor")
                ? Mathf.Clamp(GetWeaponTransformMagnitudeB(sourceItem, "backdraft_corridor", 2.5f), 0.1f, 8f)
                : 0f,
            Color = color,
            IsCritical = isCritical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void SpawnLegacyOrbitals(LegacyWeaponStats stats)
    {
        float durationMultiplier = stats.SourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(stats.SourceItem);
        int count = Mathf.Max(2, 2 + stats.ProjectileCount);
        float orbitRadiusScalar = stats.SourceItem == null
            ? 1f
            : GetWeaponItemOrbitRadiusScalar(stats.SourceItem);
        bool aegisCarousel = stats.SourceItem != null && HasWeaponTransformFlag(stats.SourceItem, "aegis_carousel");
        float trailDamageScale = stats.SourceItem == null ? 0.25f : Mathf.Clamp(GetWeaponTransformMagnitude(stats.SourceItem, "aegis_carousel", 0.25f), 0.05f, 3f);
        for (int orbitalIndex = 0; orbitalIndex < count; orbitalIndex++)
        {
            MeshInstance3D mesh = CreateCubeVisual(_player.Position, 1.2f * stats.SizeScale, stats.Color, 0.85f);
            _legacyOrbitals.Add(new OrbitalEffect
            {
                Angle = (orbitalIndex / (float)count) * Mathf.Tau + _rng.RandfRange(-0.2f, 0.2f),
                Life = LegacyOrbitalLife * durationMultiplier,
                Damage = stats.Damage * 0.5f,
                OrbitRadius = (LegacyOrbitalRadius + 2.4f) * stats.SizeScale * Mathf.Clamp(orbitRadiusScalar, 0.35f, 2f),
                Speed = LegacyOrbitalSpeed + _rng.RandfRange(0f, 0.5f),
                SizeScale = stats.SizeScale,
                TrailDamageScale = aegisCarousel ? trailDamageScale : 0f,
                ReverseInterval = aegisCarousel ? 2f : 0f,
                ReverseTimer = aegisCarousel ? 2f : 0f,
                Color = stats.Color,
                IsCritical = stats.IsCritical,
                SourceItem = stats.SourceItem,
                Mesh = mesh,
                Material = (StandardMaterial3D)mesh.MaterialOverride,
            });
        }
    }

    private void SpawnLegacyRift(Vector2 position, float damage, float radius, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float durationMultiplier = sourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(sourceItem);
        MeshInstance3D mesh = CreateDiskVisual(position, radius, color, 0.22f, 0.10f);
        _legacyRifts.Add(new RiftEffect
        {
            Position = position,
            Duration = LegacyRiftDuration * durationMultiplier,
            Damage = damage,
            Radius = radius,
            AlternatingPushPull = sourceItem != null && HasWeaponTransformFlag(sourceItem, "phase_inversion"),
            PulseInterval = sourceItem != null && HasWeaponTransformFlag(sourceItem, "phase_inversion")
                ? Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "phase_inversion", 0.6f), 0.1f, 2f)
                : LegacyRiftPulseRate,
            Color = color,
            IsCritical = isCritical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void SpawnLegacyMortar(Vector2 start, Vector2 target, float damage, float radius, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float projectileSpeedMultiplier = sourceItem == null
            ? GetEquipmentProjectileSpeedMultiplier()
            : GetWeaponItemProjectileSpeedMultiplier(sourceItem);
        MeshInstance3D mesh = CreateSphereVisual(start, 1.2f, color, 0.95f, 2.5f);
        _legacyMortars.Add(new MortarEffect
        {
            Start = start,
            Target = ClampResolve(target, radius),
            FlightTime = Mathf.Clamp(LegacyMortarFlightTime / Mathf.Max(0.1f, projectileSpeedMultiplier), 0.2f, 2.5f),
            Damage = damage,
            Radius = radius,
            Color = color,
            IsCritical = isCritical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void SpawnLegacyChainLightning(Vector2 start, float damage, int maxArcs, Color color, bool isCritical, Vector2? preferredTargetOverride = null, float? rangeOverride = null, EquipmentItem? sourceItem = null)
    {
        ChainEffect chain = new() { Color = color, IsCritical = isCritical };
        Vector2 current = start;
        float currentDamage = damage;
        float chainRange = Mathf.Max(1f, RuntimeMath.FiniteOr(rangeOverride ?? LegacyChainRange, LegacyChainRange));
        HashSet<EnemyActor> visited = new();
        EnemyActor? preferredTarget = preferredTargetOverride.HasValue
            ? FindNearestEnemyActor(preferredTargetOverride.Value, 6f, visited)
            : null;

        for (int hopIndex = 0; hopIndex < maxArcs; hopIndex++)
        {
            EnemyActor? target = hopIndex == 0 && preferredTarget != null && preferredTarget.IsAlive && (preferredTarget.Position - current).Length() <= chainRange
                ? preferredTarget
                : FindNearestEnemyActor(current, chainRange, visited);
            if (target == null)
            {
                bool preferBoss = hopIndex == 0
                    && preferredTargetOverride.HasValue
                    && _boss != null
                    && _boss.IsAlive
                    && _boss.Position.DistanceTo(preferredTargetOverride.Value) <= 6f;
                if ((preferBoss || hopIndex == 0) && _boss != null && _boss.IsAlive && (_boss.Position - current).Length() <= chainRange)
                {
                    DealDamageToBoss(currentDamage, isCritical, false, sourceItem, false);
                    AddChainBeam(chain, current, _boss.Position, color);
                }
                break;
            }

            visited.Add(target);
            DealDamageToEnemy(target, currentDamage, current, false, isCritical, false, sourceItem, false);
            AddChainBeam(chain, current, target.Position, color);
            current = target.Position;
            currentDamage *= LegacyChainDamageDecay;
        }

        if (chain.Beams.Count > 0)
        {
            _legacyChains.Add(chain);
        }
    }

    private void AddChainBeam(ChainEffect chain, Vector2 start, Vector2 end, Color color)
    {
        LaserBeamActor beam = new();
        AddChild(beam);
        beam.SetBeam(start, end, 0.45f, color, false);
        chain.Beams.Add(beam);
    }

    private void SpawnLegacyDrill(Vector2 origin, Vector2 direction, float damage, float life, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float projectileSpeedMultiplier = sourceItem == null ? 1f : GetWeaponItemProjectileSpeedMultiplier(sourceItem);
        MeshInstance3D mesh = CreateDrillVisual(origin, color);
        _legacyDrills.Add(new DrillEffect
        {
            Position = origin,
            Velocity = direction * (LegacyDrillSpeed * projectileSpeedMultiplier),
            Life = life,
            Damage = damage,
            WakeDamageScale = sourceItem != null && HasWeaponTransformFlag(sourceItem, "tunnel_bore")
                ? Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "tunnel_bore", 0.28f), 0.05f, 3f)
                : 0f,
            WakeDuration = sourceItem != null && HasWeaponTransformFlag(sourceItem, "tunnel_bore")
                ? Mathf.Clamp(GetWeaponTransformMagnitudeB(sourceItem, "tunnel_bore", 1.8f), 0.1f, 6f)
                : 0f,
            Color = color,
            IsCritical = isCritical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void SpawnLegacyVoid(Vector2 position, float damage, float pullRadius, float damageRadius, Color color, bool isCritical, EquipmentItem? sourceItem)
    {
        float durationMultiplier = sourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(sourceItem);
        float pullStrengthMultiplier = sourceItem == null
            ? 1f
            : GetWeaponItemPullStrengthMultiplier(sourceItem);
        MeshInstance3D mesh = CreateDiskVisual(position, pullRadius, color.Darkened(0.35f), 0.24f, 0.12f);
        _legacyVoids.Add(new VoidEffect
        {
            Position = position,
            Duration = LegacyVoidDuration * durationMultiplier,
            Damage = damage,
            PullRadius = pullRadius,
            DamageRadius = damageRadius,
            PullStrengthMultiplier = Mathf.Clamp(pullStrengthMultiplier, 0.1f, 12f),
            EndBurstEnabled = sourceItem != null && HasWeaponTransformFlag(sourceItem, "event_horizon_tear"),
            EndBurstMultiplier = sourceItem == null ? 1.60f : Mathf.Clamp(GetWeaponTransformMagnitude(sourceItem, "event_horizon_tear", 1.60f), 0.1f, 8f),
            EndShardCount = sourceItem == null ? 6 : Mathf.Clamp(GetWeaponTransformCount(sourceItem, "event_horizon_tear", 6), 1, 24),
            EndShardDamageScale = sourceItem == null ? 0.30f : Mathf.Clamp(GetWeaponTransformMagnitudeB(sourceItem, "event_horizon_tear", 0.30f), 0.05f, 3f),
            Color = color,
            IsCritical = isCritical,
            SourceItem = sourceItem,
            Mesh = mesh,
            Material = (StandardMaterial3D)mesh.MaterialOverride,
        });
    }

    private void SpawnLegacyClones(Vector2 origin, int count, Color color, float sourceDamage, RarityTier sourceRarity, EquipmentItem? sourceItem)
    {
        float durationMultiplier = sourceItem == null
            ? GetEquipmentDurationMultiplier()
            : GetWeaponItemDurationMultiplier(sourceItem);
        float clonePowerMultiplier = sourceItem == null
            ? 1f
            : GetWeaponItemCloneOrEchoPowerMultiplier(sourceItem);
        for (int cloneIndex = 0; cloneIndex < count; cloneIndex++)
        {
            MeshInstance3D mesh = CreateCubeVisual(origin, 1.4f, color, 0.42f);
            _legacyClones.Add(new CloneEffect
            {
                Center = origin,
                Angle = (cloneIndex / (float)count) * Mathf.Tau + _rng.RandfRange(-0.2f, 0.2f),
                Life = LegacyCloneLife * durationMultiplier,
                FireTimer = _rng.RandfRange(0.05f, 0.35f),
                Color = color,
                SourceDamage = sourceDamage * Mathf.Clamp(clonePowerMultiplier, 0.1f, 12f),
                SourceRarity = sourceRarity,
                SourceItem = sourceItem,
                MimicPrimaryShot = sourceItem != null && HasWeaponTransformFlag(sourceItem, "primary_mimic"),
                MimicDamageScale = sourceItem == null ? 0.35f : GetWeaponTransformMagnitude(sourceItem, "primary_mimic", 0.35f),
                Mesh = mesh,
                Material = (StandardMaterial3D)mesh.MaterialOverride,
            });
        }
    }

    private void ScheduleLegacyEcho(WeaponKind kind, Vector2 origin, Vector2 direction, float damageScale, float sourceDamage, RarityTier sourceRarity, EquipmentItem? sourceItem, float? delayOverride = null)
    {
        float echoPowerMultiplier = sourceItem == null
            ? 1f
            : GetWeaponItemCloneOrEchoPowerMultiplier(sourceItem);
        _legacyEchoes.Add(new DelayedEcho
        {
            Kind = kind,
            Origin = origin,
            Direction = direction,
            DamageScale = damageScale * Mathf.Clamp(echoPowerMultiplier, 0.1f, 12f),
            SourceDamage = sourceDamage,
            SourceRarity = sourceRarity,
            SourceItem = sourceItem,
            TimeLeft = Mathf.Max(0.01f, delayOverride ?? LegacyEchoDelay),
        });
    }

    private static float GetLegacyWeaponCooldown(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Circle => 1.5f,
            WeaponKind.Triangle => 1.0f,
            WeaponKind.Hexagon => 1.0f,
            WeaponKind.Sphere => 2.5f,
            WeaponKind.Cube => 8.0f,
            WeaponKind.Pyramid => 1.2f,
            WeaponKind.Cone => 1.5f,
            WeaponKind.Tesseract => 4.0f,
            WeaponKind.Hyperphere => 3.0f,
            WeaponKind.Pentachoron => 2.5f,
            WeaponKind.Hypercone => 1.5f,
            WeaponKind.ImpossibleCube => 6.0f,
            WeaponKind.Mobius => 1.0f,
            WeaponKind.Klein => 1.0f,
            WeaponKind.BoysSurface => 7.0f,
            _ => 1.0f,
        };
    }

    private static float GetLegacyWeaponDamageBias(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Circle => 0.55f,
            WeaponKind.Triangle => 0.90f,
            WeaponKind.Hexagon => 0.70f,
            WeaponKind.Sphere => 1.15f,
            WeaponKind.Cube => 0.75f,
            WeaponKind.Pyramid => 1.35f,
            WeaponKind.Cone => 1.00f,
            WeaponKind.Tesseract => 1.75f,
            WeaponKind.Hyperphere => 1.80f,
            WeaponKind.Pentachoron => 1.35f,
            WeaponKind.Hypercone => 1.55f,
            WeaponKind.ImpossibleCube => 2.35f,
            WeaponKind.Mobius => 0.80f,
            WeaponKind.Klein => 0.80f,
            WeaponKind.BoysSurface => 1.35f,
            _ => 1.00f,
        };
    }

    private static float GetLegacyWeaponMoveBias(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Cube => 0.96f,
            WeaponKind.ImpossibleCube => 0.96f,
            WeaponKind.Klein => 0.98f,
            WeaponKind.Pyramid => 1.03f,
            _ => 1f,
        };
    }
}