using Godot;
using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild;

public partial class Main
{
    private const float ShinyExtraOrbChance = 0.50f;
    private const float ShinyFlashCooldown = 10f;
    private const float ShinyFlashRadius = 10f;
    private const float ShinyFlashBlindDuration = 5f;
    private const float ShinyAuraRange = 120f;

    private const float ElectricArcDamagePercent = 0.20f;
    private const float ElectricArcRange = 100f;
    private const float ChargedDuration = 16f;
    private const float ChargedExplosionInterval = 4f;
    private const float ChargedExplosionRadius = 10f;
    private const float ChargedSpreadChance = 0.20f;
    private const float ElectricThunderInterval = 4f;
    private const float ElectricThunderDamage = 50f;
    private const float ElectricThunderAoeRadius = 4f;
    private const int ElectricThunderChainTargets = 10;
    private const float ElectricThunderChainRange = 10f;

    private const float DarkLifeStealPercent = 15f;
    private const float PhantomSpeed = 22f;
    private const float PhantomLifetime = 6f;
    private const float PhantomHitRadius = 2.5f;
    private const float PhantomExplosionRadius = 7f;
    private const int MaxPhantoms = 24;

    private const float UnstableJitterMin = 0.5f;
    private const float UnstableJitterMax = 2.0f;
    private const float GlitchShotChance = 0.20f;
    private const float EntropyPulseInterval = 10f;
    private const float EntropyPulseRange = 120f;
    private const float EntropyBerserkChance = 0.12f;
    private const float EntropyBerserkDuration = 3.5f;

    private const float OvershieldFraction = 0.20f;
    private const float OvershieldRechargeDelay = 8f;
    private const float ShieldBreakRange = 30f;
    private const float ShieldBreakSlowDuration = 3f;
    private const float BubbleDuration = 6f;
    private const float BubbleCooldown = 12f;
    private const float BubbleRadius = 15f;
    private const float BubbleHpMultiplier = 2f;

    private const int ChillStacksToFreeze = 3;
    private const float ChillSlowPerStack = 0.15f;
    private const float FrozenDuration = 3f;
    private const float ShatterThresholdPercent = 0.20f;
    private const float ShatterDamagePercent = 0.60f;
    private const float ShatterRadius = 6f;
    private const float ShatterRadiusSuper = 12f;

    private const float ThornReflectPercent = 0.30f;
    private const float ThornMarkDuration = 8f;
    private const float ThornCollisionCooldown = 1f;
    private const float ThornSpreadRange = 6f;
    private const float ThornExplosionDamagePercent = 1f;
    private const float ThornExplosionRadius = 8f;
    private const float VolitileExplosionDamagePercent = 0.50f;

    private const float BaseCriticalDamagePercent = 50f;

    private const float ParasiteInfectChance = 0.40f;
    private const float ParasiteDuration = 5f;
    private const float ParasiteChainChance = 0.50f;
    private const float ParasiteBossDuration = 8f;

    private readonly EquipmentItem?[] _equippedLoadout = new EquipmentItem?[5];
    private readonly float[] _wingSupportShotCooldowns = new float[5];
    private readonly SynergyManager _equipmentSynergyManager = new();
    private SynergySnapshot _equipmentSynergies = new();
    private InventoryManager? _inventoryManager;

    private float _equipmentHealthFlat;
    private float _primaryWeaponDamage;
    private float _equipmentDamagePct;
    private float _equipmentHealthPct;
    private float _equipmentDodgeChancePct;
    private float _equipmentLifeStealPct;
    private float _equipmentHealthRegen;
    private float _equipmentCritDamagePct;
    private float _equipmentCritChancePct;
    private float _equipmentMoveSpeedPct;
    private float _equipmentRangeFlat;
    private float _equipmentLuckFlat;
    private float _equipmentSizePct;
    private float _equipmentAttackSpeedPct;
    private float _equipmentRicochetFlat;
    private float _equipmentBounceTimesFlat;
    private float _equipmentProjectileCountFlat;
    private float _equipmentProjectileSpeedPct;
    private float _equipmentDurationPct;
    private float _equipmentHomingStrengthPct;
    private float _equipmentPierceFlat;

    private ModifierKey _primaryModifierKey = ModifierKey.Normal;
    private string _primaryModifierDisplayName = string.Empty;
    private int _shinyTier;
    private int _electricTier;
    private int _darkTier;
    private int _unstableTier;
    private int _shieldedTier;
    private int _glacialTier;
    private int _thornedTier;
    private int _parasiteTier;
    private int _volitileTier;
    private float _volitileEffectiveStacks;
    private bool _prismaticSurgeActive;
    private bool _fullSpectrumActive;

    private readonly Dictionary<EnemyActor, EquipmentEnemyState> _equipmentEnemyStates = new();
    private readonly List<EquipmentPhantom> _equipmentPhantoms = new();
    private float _equipmentShinyFlashTimer = ShinyFlashCooldown;
    private float _equipmentThunderTimer = ElectricThunderInterval;
    private float _equipmentEntropyTimer = EntropyPulseInterval;
    private float _equipmentOvershieldTimer = OvershieldRechargeDelay;
    private float _equipmentBubbleTimer;
    private float _equipmentBubbleCooldownTimer;
    private float _equipmentBossInfectedTimer;

    private sealed class EquipmentEnemyState
    {
        public int ChillStacks;
        public float FrozenTimer;
        public float ChargedTimer;
        public float ChargedExplosionTimer;
        public float ChargedDamage;
        public bool ChargedCanSpread;
        public float ThornedTimer;
        public float ThornedCollisionTimer;
        public float ShieldSlowTimer;
        public float BlindTimer;
        public Vector2 BlindDirection;
        public bool Infected;
        public float InfectedTimer;
        public float EntropySpeedMultiplier = 1f;
        public float EntropyDamageMultiplier = 1f;
        public float EntropyBerserkTimer;
    }

    private sealed class EquipmentPhantom
    {
        public Vector2 Position;
        public float Age;
        public float Damage;
        public MeshInstance3D Mesh = null!;
    }

    private void RefreshEquipmentLoadout()
    {
        RebuildEquipmentStatSnapshot();
        RebuildEquipmentSynergySnapshot();
        RebuildArenaWeaponVisuals();
    }

    private void RebuildEquipmentStatSnapshot()
    {
        _equipmentHealthFlat = 0f;
        _primaryWeaponDamage = 0f;
        _equipmentDamagePct = 0f;
        _equipmentHealthPct = 0f;
        _equipmentDodgeChancePct = 0f;
        _equipmentLifeStealPct = 0f;
        _equipmentHealthRegen = 0f;
        _equipmentCritDamagePct = 0f;
        _equipmentCritChancePct = 0f;
        _equipmentMoveSpeedPct = 0f;
        _equipmentRangeFlat = 0f;
        _equipmentLuckFlat = 0f;
        _equipmentSizePct = 0f;
        _equipmentAttackSpeedPct = 0f;
        _equipmentRicochetFlat = 0f;
        _equipmentBounceTimesFlat = 0f;
        _equipmentProjectileCountFlat = 0f;
        _equipmentProjectileSpeedPct = 0f;
        _equipmentDurationPct = 0f;
        _equipmentHomingStrengthPct = 0f;
        _equipmentPierceFlat = 0f;

        for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentItem? item = _equippedLoadout[slotIndex];
            if (item == null)
            {
                continue;
            }

            bool fromPrimaryItem = slotIndex == 0;

            _equipmentHealthFlat += StatGenerator.GetEffectiveHealth(item);
            if (fromPrimaryItem)
            {
                _primaryWeaponDamage = StatGenerator.GetEffectiveDamage(item);
            }

            for (int substatIndex = 0; substatIndex < item.Substats.Count; substatIndex++)
            {
                SubstatRoll roll = item.Substats[substatIndex];
                AddEquipmentSubstat(roll.Type, roll.Value, fromPrimaryItem);
            }

            for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData modulator = item.Modulators[modulatorIndex];
                if (modulator.IsRetired) continue;
                IReadOnlyList<WeaponModulatorEffectData> effects = modulator.GetResolvedEffects();
                for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                {
                    AddEquipmentModulatorPassiveEffect(effects[effectIndex], fromPrimaryItem);
                }
            }
        }

        EquipmentItem? primaryItem = _equippedLoadout[0];
        _primaryModifierKey = primaryItem?.Modifier?.Key ?? ModifierKey.Normal;
        _primaryModifierDisplayName = primaryItem?.Modifier?.DisplayName ?? string.Empty;
    }

    private void AddEquipmentSubstat(SubstatType type, float value, bool fromPrimaryItem)
    {
        if (!fromPrimaryItem && IsPrimaryWeaponOnlySubstat(type))
        {
            return;
        }

        switch (type)
        {
            case SubstatType.DamagePercent:
            case SubstatType.HealthPercent:
                break;
            case SubstatType.DodgeChance:
                _equipmentDodgeChancePct += value;
                break;
            case SubstatType.LifeStealPercent:
                _equipmentLifeStealPct += value;
                break;
            case SubstatType.HealthRegen:
                _equipmentHealthRegen += value;
                break;
            case SubstatType.CritDamage:
                _equipmentCritDamagePct += value;
                break;
            case SubstatType.CritChance:
                _equipmentCritChancePct += value;
                break;
            case SubstatType.MovementSpeed:
                _equipmentMoveSpeedPct += value;
                break;
            case SubstatType.Range:
                _equipmentRangeFlat += value;
                break;
            case SubstatType.Luck:
                _equipmentLuckFlat += value;
                break;
            case SubstatType.Size:
                _equipmentSizePct += value;
                break;
            case SubstatType.AttackSpeed:
                _equipmentAttackSpeedPct += value;
                break;
            case SubstatType.Ricochet:
                _equipmentRicochetFlat += value;
                break;
            case SubstatType.BounceTimes:
                _equipmentBounceTimesFlat += value;
                break;
            case SubstatType.ProjectileCount:
                _equipmentProjectileCountFlat += value;
                break;
        }
    }

    private void AddEquipmentModulatorPassiveEffect(WeaponModulatorEffectData effect, bool fromPrimaryItem)
    {
        if (!effect.IsPassive)
        {
            return;
        }

        switch (effect.Type)
        {
            case ModulatorEffectType.ProjectileSpeedMultiplier:
                if (fromPrimaryItem)
                {
                    _equipmentProjectileSpeedPct += (effect.Magnitude - 1f) * 100f;
                }
                return;
            case ModulatorEffectType.DurationMultiplier:
                if (fromPrimaryItem)
                {
                    _equipmentDurationPct += (effect.Magnitude - 1f) * 100f;
                }
                return;
            case ModulatorEffectType.HomingStrengthMultiplier:
                if (fromPrimaryItem)
                {
                    _equipmentHomingStrengthPct += (effect.Magnitude - 1f) * 100f;
                }
                return;
            case ModulatorEffectType.PierceAdd:
                if (fromPrimaryItem)
                {
                    _equipmentPierceFlat += effect.ResolvedCountValue;
                }
                return;
        }

        if (!effect.TryConvertToSubstatRoll(out SubstatRoll roll))
        {
            return;
        }

        AddEquipmentSubstat(roll.Type, roll.Value, fromPrimaryItem);
    }

    private static bool IsPrimaryWeaponOnlySubstat(SubstatType type)
    {
        return type == SubstatType.DamagePercent
            || type == SubstatType.AttackSpeed
            || type == SubstatType.Size
            || type == SubstatType.Range
            || type == SubstatType.ProjectileCount
            || type == SubstatType.CritChance
            || type == SubstatType.CritDamage;
    }

    private float GetEquipmentProjectileSpeedMultiplier()
    {
        return Mathf.Clamp(RuntimeMath.FiniteOr(1f + _equipmentProjectileSpeedPct / 100f, 1f), 0.1f, 12f);
    }

    private float GetEquipmentDurationMultiplier()
    {
        return Mathf.Clamp(RuntimeMath.FiniteOr(1f + _equipmentDurationPct / 100f, 1f), 0.1f, 12f);
    }

    private float GetEquipmentHomingStrengthMultiplier()
    {
        return Mathf.Clamp(RuntimeMath.FiniteOr(1f + _equipmentHomingStrengthPct / 100f, 1f), 0.1f, 12f);
    }

    private int GetEquipmentPierceCountBonus()
    {
        return Mathf.Max(0, Mathf.RoundToInt(_equipmentPierceFlat));
    }

    private void RebuildEquipmentSynergySnapshot()
    {
        List<EquipmentSlot> supportSlots = new(4);
        for (int slotIndex = 1; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentSlot slot = new(slotIndex, EquipmentSlotType.Support);
            EquipmentItem? item = _equippedLoadout[slotIndex];
            if (item != null)
            {
                slot.Equip(item);
            }

            supportSlots.Add(slot);
        }

        _equipmentSynergies = _equipmentSynergyManager.EvaluateSupportSynergies(supportSlots);
        _fullSpectrumActive = _equipmentSynergies.FullSpectrumActive;
        _prismaticSurgeActive = _equipmentSynergies.PrismaticSynergyActive || _fullSpectrumActive;

        _shinyTier = GetEquipmentModifierTier(ModifierKey.Shiny);
        _electricTier = GetEquipmentModifierTier(ModifierKey.Electric);
        _darkTier = GetEquipmentModifierTier(ModifierKey.Dark);
        _unstableTier = GetEquipmentModifierTier(ModifierKey.Unstable);
        _shieldedTier = GetEquipmentModifierTier(ModifierKey.Shielded);
        _glacialTier = GetEquipmentModifierTier(ModifierKey.Cursed);
        _thornedTier = GetEquipmentModifierTier(ModifierKey.Thorned);
        _parasiteTier = GetEquipmentModifierTier(ModifierKey.Parasite);
        int legacyVolitileTier = GetEquipmentModifierTier(ModifierKey.Volitile);
        int legacyVolitileStacks = GetEquipmentModifierStackCount(ModifierKey.Volitile);
        float modulatorVolitileStage = GetInstalledVolitileModulatorStage();
        _volitileTier = legacyVolitileTier;
        _volitileEffectiveStacks = ResolveVolitileExplosionStage(legacyVolitileStacks, modulatorVolitileStage);
    }

    private int GetEquipmentModifierTier(ModifierKey key)
    {
        ModifierSynergyState state = _equipmentSynergies.GetState(key);
        if (state.SuperSynergyActive)
        {
            return 4;
        }

        if (state.SynergyActive)
        {
            return 2;
        }

        return state.PassiveActive ? 1 : 0;
    }

    private int GetEquipmentModifierStackCount(ModifierKey key)
    {
        ModifierSynergyState state = _equipmentSynergies.GetState(key);
        return state.PassiveActive ? Mathf.Clamp(state.EffectiveCount, 1, 4) : 0;
    }

    private float ResolveVolitileExplosionStage(int legacyStacks, float modulatorStage)
    {
        float stage = Math.Max(modulatorStage, GetLegacyVolitileStage(legacyStacks));
        return Mathf.Clamp(stage, 0, 4);
    }

    private int GetLegacyVolitileStage(int legacyStacks)
    {
        if (HasPrimaryModifier(ModifierKey.Volitile))
        {
            return Mathf.Max(1, legacyStacks);
        }

        return legacyStacks;
    }

    private float GetInstalledVolitileModulatorStage()
    {
        float highestStrength = 0f;
        float sumStrength = 0f;
        for (int slotIndex = 0; slotIndex < _equippedLoadout.Length; slotIndex++)
        {
            EquipmentItem? item = _equippedLoadout[slotIndex];
            if (item == null)
            {
                continue;
            }

            for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData? modulator = item.Modulators[modulatorIndex];
                if (modulator == null || !string.Equals(modulator.NormalizedKey, "volitile", StringComparison.Ordinal))
                {
                    continue;
                }

                float strength = modulator.QualityStrength;
                sumStrength += strength;
                highestStrength = Math.Max(highestStrength, strength);
            }
        }

        if (sumStrength == 0)
        {
            return 0;
        }

        return Mathf.Clamp(sumStrength + highestStrength, 1f, 4f);
    }

    private static int GetVolitileStageFromTier(WeaponModulatorTier tier)
    {
        return tier switch
        {
            WeaponModulatorTier.Flawed => 1,
            WeaponModulatorTier.Baseline => 2,
            WeaponModulatorTier.Enhanced => 3,
            WeaponModulatorTier.Flawless => 4,
            _ => 2,
        };
    }

    private void ApplyEquippedPrimaryToRun()
    {
        EquipmentItem? primaryItem = _equippedLoadout[0];
        if (primaryItem?.Shape != null)
        {
            _weaponKind = MapEquipmentShapeToWeapon(primaryItem.Shape.Key);
            _weaponCooldownSeconds = Mathf.Max(0.05f, RuntimeMath.FiniteOr(primaryItem.Shape.Cooldown, GetLegacyWeaponCooldown(_weaponKind)));
        }
        else
        {
            _weaponCooldownSeconds = GetLegacyWeaponCooldown(_weaponKind);
        }

        if (primaryItem?.Rarity != null)
        {
            _weaponRarity = MapEquipmentRarity(primaryItem.Rarity.Key);
        }
    }

    private void ResetEquipmentModifierRuntime()
    {
        ResetLegacyQualityCounts();
        InitializeRunEquipmentLoadout();

        foreach (EquipmentPhantom phantom in _equipmentPhantoms)
        {
            QueueFreeIfValid(phantom.Mesh);
        }

        _equipmentEnemyStates.Clear();
        _equipmentPhantoms.Clear();
        _equipmentShinyFlashTimer = ShinyFlashCooldown;
        _equipmentThunderTimer = ElectricThunderInterval;
        _equipmentEntropyTimer = EntropyPulseInterval;
        _equipmentOvershieldTimer = OvershieldRechargeDelay;
        _equipmentBubbleTimer = 0f;
        _equipmentBubbleCooldownTimer = 0f;
        _equipmentBossInfectedTimer = 0f;
    }

    private void RefreshEquipmentPlayerDefenses()
    {
        int shieldedPieceCount = HasPrimaryModifier(ModifierKey.Shielded) ? Math.Max(1, _shieldedTier) : _shieldedTier;
        _player.ShieldedPieceCount = shieldedPieceCount;
        if (shieldedPieceCount <= 0)
        {
            _player.Overshield = 0f;
            return;
        }

        float overshieldCap = GetEquipmentOvershieldCap();
        if (_player.Overshield <= 0f && _equipmentOvershieldTimer >= OvershieldRechargeDelay)
        {
            _player.Overshield = overshieldCap;
        }

        _player.Overshield = Mathf.Clamp(_player.Overshield, 0f, overshieldCap);
    }

    private float GetEquipmentOvershieldCap()
    {
        return Mathf.Max(1f, _player.MaxHp * OvershieldFraction);
    }

    private void TickEquipmentModifierRuntime(float dt)
    {
        TickEquipmentEnemyStates(dt);
        TickEquipmentPhantoms(dt);
        TickEquipmentPeriodicEffects(dt);
        TickEquipmentDefenses(dt);
        ApplyEquipmentRegen(dt);
        if (_equipmentBossInfectedTimer > 0f)
        {
            _equipmentBossInfectedTimer = Mathf.Max(0f, _equipmentBossInfectedTimer - dt);
        }
    }

    private void TickEquipmentEnemyStates(float dt)
    {
        if (_equipmentEnemyStates.Count == 0)
        {
            return;
        }

        List<EnemyActor> enemies = new(_equipmentEnemyStates.Keys);
        for (int index = enemies.Count - 1; index >= 0; index--)
        {
            EnemyActor enemy = enemies[index];
            if (!IsInstanceValid(enemy) || !enemy.IsAlive)
            {
                _equipmentEnemyStates.Remove(enemy);
                continue;
            }

            EquipmentEnemyState state = _equipmentEnemyStates[enemy];
            if (state.ChargedTimer > 0f)
            {
                state.ChargedTimer = Mathf.Max(0f, state.ChargedTimer - dt);
                state.ChargedExplosionTimer -= dt;
                if (state.ChargedExplosionTimer <= 0f)
                {
                    state.ChargedExplosionTimer = ChargedExplosionInterval;
                    TriggerChargedExplosion(enemy, state);
                }
            }

            if (state.FrozenTimer > 0f && _glacialTier < 4)
            {
                state.FrozenTimer = Mathf.Max(0f, state.FrozenTimer - dt);
            }

            state.ThornedTimer = Mathf.Max(0f, state.ThornedTimer - dt);
            state.ThornedCollisionTimer = Mathf.Max(0f, state.ThornedCollisionTimer - dt);
            state.ShieldSlowTimer = Mathf.Max(0f, state.ShieldSlowTimer - dt);
            state.BlindTimer = Mathf.Max(0f, state.BlindTimer - dt);
            state.EntropyBerserkTimer = Mathf.Max(0f, state.EntropyBerserkTimer - dt);

            if (state.Infected && state.InfectedTimer > 0f)
            {
                state.InfectedTimer = Mathf.Max(0f, state.InfectedTimer - dt);
                if (state.InfectedTimer <= 0f)
                {
                    state.Infected = false;
                }
            }
        }

        TickThornedCollisions();
    }

    private void TickEquipmentPeriodicEffects(float dt)
    {
        if (_shinyTier >= 2)
        {
            _equipmentShinyFlashTimer -= dt;
            if (_equipmentShinyFlashTimer <= 0f)
            {
                _equipmentShinyFlashTimer = ShinyFlashCooldown;
                TriggerShinyFlash();
            }
        }

        if (_electricTier >= 4)
        {
            _equipmentThunderTimer -= dt;
            if (_equipmentThunderTimer <= 0f)
            {
                _equipmentThunderTimer = ElectricThunderInterval;
                TriggerElectricThunderstorm();
            }
        }

        if (_unstableTier >= 4)
        {
            _equipmentEntropyTimer -= dt;
            if (_equipmentEntropyTimer <= 0f)
            {
                _equipmentEntropyTimer = EntropyPulseInterval;
                TriggerEntropyPulse();
            }
        }
    }

    private void TickEquipmentDefenses(float dt)
    {
        if (_player.ShieldedPieceCount >= 1)
        {
            _equipmentOvershieldTimer += dt;
            if (_equipmentOvershieldTimer >= OvershieldRechargeDelay)
            {
                float overshieldCap = GetEquipmentOvershieldCap();
                _player.Overshield = Mathf.Min(overshieldCap, _player.Overshield + overshieldCap * dt);
            }
        }

        if (_shieldedTier >= 4)
        {
            TickEquipmentBubble(dt);
        }
    }

    private void TickEquipmentBubble(float dt)
    {
        if (_equipmentBubbleTimer > 0f)
        {
            _equipmentBubbleTimer = Mathf.Max(0f, _equipmentBubbleTimer - dt);
            if (_equipmentBubbleTimer <= 0f)
            {
                _player.BubbleActive = false;
                _equipmentBubbleCooldownTimer = BubbleCooldown;
            }

            return;
        }

        _equipmentBubbleCooldownTimer = Mathf.Max(0f, _equipmentBubbleCooldownTimer - dt);
        if (_equipmentBubbleCooldownTimer <= 0f && !_player.BubbleActive)
        {
            _player.BubbleActive = true;
            _player.BubbleRadius = BubbleRadius;
            _player.BubbleHp = _player.MaxHp * BubbleHpMultiplier;
            _equipmentBubbleTimer = BubbleDuration;
            SpawnVisualPulse(_player.Position, 3f, BubbleRadius, new Color(0.40f, 0.82f, 1f, 0.34f), 0.45f);
        }
    }

    private void NotifyEquipmentPlayerIncomingDamage()
    {
        _equipmentOvershieldTimer = 0f;
    }

    private void NotifyEquipmentPlayerDamageBlocked()
    {
        _equipmentOvershieldTimer = 0f;
    }

    private void TriggerEquipmentOvershieldBreak()
    {
        if (_shieldedTier < 2)
        {
            return;
        }

        SpawnVisualPulse(_player.Position, 2f, ShieldBreakRange, new Color(0.50f, 0.86f, 1f, 0.38f), 0.35f);
        float rangeSq = ShieldBreakRange * ShieldBreakRange;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (!enemy.IsAlive)
            {
                continue;
            }

            Vector2 offset = enemy.Position - _player.Position;
            if (offset.LengthSquared() > rangeSq)
            {
                continue;
            }

            EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(enemy);
            state.ShieldSlowTimer = ShieldBreakSlowDuration;

            Vector2 pushDirection = offset.LengthSquared() > 0.01f ? offset.Normalized() : Vector2.Right;
            enemy.Position = ClampResolve(enemy.Position + pushDirection * 9f, enemy.Radius);
        }
    }

    private void ApplyEquipmentRegen(float dt)
    {
        if (_equipmentHealthRegen <= 0f)
        {
            return;
        }

        _player.Hp = Mathf.Min(_player.MaxHp, _player.Hp + _equipmentHealthRegen * dt);
    }

    private void ApplyEquipmentEnemyStatModifiers(EnemyActor enemy)
    {
        float speedMultiplier = 1f;
        float damageMultiplier = 1f;
        if (_equipmentEnemyStates.TryGetValue(enemy, out EquipmentEnemyState? state))
        {
            speedMultiplier *= Mathf.Max(0.05f, 1f - state.ChillStacks * ChillSlowPerStack);
            if (state.ShieldSlowTimer > 0f)
            {
                speedMultiplier *= 0.45f;
            }

            speedMultiplier *= state.EntropySpeedMultiplier;
            damageMultiplier *= state.EntropyDamageMultiplier;
        }

        if (_shinyTier >= 4 && (_player.Position - enemy.Position).LengthSquared() <= ShinyAuraRange * ShinyAuraRange)
        {
            speedMultiplier *= 0.5f;
        }

        enemy.MoveSpeed *= Mathf.Clamp(speedMultiplier, 0.05f, 3f);
        enemy.ContactDps *= Mathf.Clamp(damageMultiplier, 0.05f, 3f);
        enemy.BulletDamage *= Mathf.Clamp(damageMultiplier, 0.05f, 3f);
    }

    private bool TickEquipmentEnemyOverride(EnemyActor enemy, float dt)
    {
        if (!_equipmentEnemyStates.TryGetValue(enemy, out EquipmentEnemyState? state))
        {
            return false;
        }

        if (state.FrozenTimer > 0f)
        {
            enemy.LastVelocity = Vector2.Zero;
            return true;
        }

        if (state.Infected || state.EntropyBerserkTimer > 0f)
        {
            TickConvertedEnemy(enemy, state, dt);
            return true;
        }

        if (state.BlindTimer > 0f)
        {
            if (state.BlindDirection.LengthSquared() < 0.01f)
            {
                float angle = _rng.RandfRange(0f, Mathf.Tau);
                state.BlindDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            enemy.LastVelocity = state.BlindDirection * enemy.MoveSpeed * 0.75f;
            enemy.Position = ClampResolve(enemy.Position + enemy.LastVelocity * dt, enemy.Radius);
            return true;
        }

        return false;
    }

    private void TickConvertedEnemy(EnemyActor enemy, EquipmentEnemyState state, float dt)
    {
        EnemyActor? target = FindNearestEnemy(enemy.Position, enemy, excludeConverted: state.Infected);
        if (target == null)
        {
            enemy.LastVelocity = Vector2.Zero;
            return;
        }

        enemy.TickAI(
            dt,
            target.Position,
            target.LastVelocity,
            (position, velocity, damage, team, color, radius) =>
            {
                Vector2 startPosition = velocity.LengthSquared() > 0.001f
                    ? position + velocity.Normalized() * (enemy.Radius + radius + 0.7f)
                    : position;
                return SpawnBullet(startPosition, velocity, damage * 0.8f, TeamId.Friendly, new Color(0.26f, 1f, 0.50f), radius, 4f);
            },
            ClampResolve,
            IsBlockedByPillar);

        float touchRadius = enemy.Radius + target.Radius;
        if ((enemy.Position - target.Position).LengthSquared() <= touchRadius * touchRadius)
        {
            float damage = Mathf.Max(enemy.ContactDps, enemy.BulletDamage * 1.35f) * dt;
            bool targetWasAlive = target.IsAlive;
            DealDamageToEnemy(target, damage, enemy.Position, false);
            if (targetWasAlive && !target.IsAlive && state.Infected)
            {
                TryChainParasiteFromConvertedKill(target.Position);
            }
        }

        if (state.Infected && _parasiteTier >= 4 && _boss != null && _boss.IsAlive)
        {
            float bossTouchRadius = enemy.Radius + _boss.Radius;
            if ((enemy.Position - _boss.Position).LengthSquared() <= bossTouchRadius * bossTouchRadius)
            {
                _equipmentBossInfectedTimer = ParasiteBossDuration;
            }
        }
    }

    private void ApplyEquipmentOnEnemyHit(EnemyActor enemy, float damage, Vector2 sourcePosition)
    {
        bool enemyWasAlive = enemy.IsAlive;

        if (HasPrimaryModifier(ModifierKey.Electric) || _electricTier >= 1)
        {
            ArcElectricDamage(enemy, damage, sourcePosition);
        }

        if (_electricTier >= 2)
        {
            MarkCharged(enemy, damage);
        }

        if (HasPrimaryModifier(ModifierKey.Cursed) || _glacialTier >= 1)
        {
            ApplyGlacialHit(enemy, damage);
        }

        if (HasPrimaryModifier(ModifierKey.Thorned) || _thornedTier >= 1)
        {
            EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(enemy);
            state.ThornedTimer = ThornMarkDuration;
        }

        if (_unstableTier >= 2 && _rng.Randf() < GlitchShotChance)
        {
            FireGlitchShot(sourcePosition, enemy.Position);
        }

        if (enemyWasAlive && !enemy.IsAlive)
        {
            HandleEquipmentEnemyKilled(enemy);
        }
    }

    private void ArcElectricDamage(EnemyActor originEnemy, float damage, Vector2 sourcePosition)
    {
        float arcDamage = damage * ElectricArcDamagePercent;
        for (int chainIndex = 0; chainIndex < 2; chainIndex++)
        {
            EnemyActor? target = FindNearestEnemy(originEnemy.Position, originEnemy, false, ElectricArcRange);
            if (target == null)
            {
                break;
            }

            DealDamageToEnemy(target, arcDamage, sourcePosition, false);
            SpawnVisualPulse(target.Position, 0.5f, 2.2f, new Color(0.22f, 0.58f, 1f, 0.34f), 0.18f);
            originEnemy = target;
        }
    }

    private void MarkCharged(EnemyActor enemy, float damage)
    {
        EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(enemy);
        state.ChargedTimer = ChargedDuration;
        state.ChargedExplosionTimer = state.ChargedExplosionTimer <= 0f ? ChargedExplosionInterval : state.ChargedExplosionTimer;
        state.ChargedDamage = Mathf.Max(state.ChargedDamage, damage);
        state.ChargedCanSpread = true;
    }

    private void TriggerChargedExplosion(EnemyActor chargedEnemy, EquipmentEnemyState state)
    {
        if (!chargedEnemy.IsAlive)
        {
            return;
        }

        SpawnVisualPulse(chargedEnemy.Position, 1f, ChargedExplosionRadius, new Color(0.30f, 0.62f, 1f, 0.40f), 0.24f);
        float radiusSq = ChargedExplosionRadius * ChargedExplosionRadius;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor target = _enemies[enemyIndex];
            if (!target.IsAlive || ReferenceEquals(target, chargedEnemy))
            {
                continue;
            }

            if ((target.Position - chargedEnemy.Position).LengthSquared() > radiusSq)
            {
                continue;
            }

            DealDamageToEnemy(target, state.ChargedDamage, chargedEnemy.Position, false);
            if (state.ChargedCanSpread && _rng.Randf() < ChargedSpreadChance)
            {
                MarkCharged(target, state.ChargedDamage);
            }
        }
    }

    private void TriggerElectricThunderstorm()
    {
        EnemyActor? target = FindRandomAliveEnemy();
        if (target == null)
        {
            return;
        }

        Vector2 strikePosition = target.Position;
        SpawnVisualPulse(strikePosition, 1f, 14f, new Color(0.35f, 0.70f, 1f, 0.46f), 0.32f);
        DealDamageToEnemy(target, ElectricThunderDamage, strikePosition, false);

        EnemyActor? chainSource = target;
        for (int chainIndex = 0; chainIndex < ElectricThunderChainTargets; chainIndex++)
        {
            if (chainSource == null)
            {
                break;
            }

            EnemyActor? nextTarget = FindNearestEnemy(chainSource.Position, chainSource, false, ElectricThunderChainRange);
            if (nextTarget == null)
            {
                break;
            }

            DealDamageToEnemy(nextTarget, ElectricThunderDamage * 0.65f, chainSource.Position, false);
            SpawnVisualPulse(nextTarget.Position, 0.5f, ElectricThunderAoeRadius, new Color(0.35f, 0.70f, 1f, 0.38f), 0.18f);
            chainSource = nextTarget;
        }
    }

    private void ApplyGlacialHit(EnemyActor enemy, float damage)
    {
        EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(enemy);
        if (state.FrozenTimer > 0f && _glacialTier >= 2 && damage >= enemy.MaxHp * ShatterThresholdPercent)
        {
            TriggerShatter(enemy);
            return;
        }

        state.ChillStacks = Mathf.Min(ChillStacksToFreeze, state.ChillStacks + 1);
        if (state.ChillStacks >= ChillStacksToFreeze)
        {
            state.FrozenTimer = _glacialTier >= 4 ? 9999f : FrozenDuration;
            state.ChillStacks = 0;
            SpawnVisualPulse(enemy.Position, 0.6f, 3.2f, new Color(0.55f, 0.96f, 1f, 0.38f), 0.22f);
        }
    }

    private void TriggerShatter(EnemyActor enemy)
    {
        float radius = _glacialTier >= 4 ? ShatterRadiusSuper : ShatterRadius;
        float radiusSq = radius * radius;
        float damage = enemy.MaxHp * ShatterDamagePercent;
        SpawnVisualPulse(enemy.Position, 1f, radius, new Color(0.62f, 0.95f, 1f, 0.44f), 0.28f);

        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor target = _enemies[enemyIndex];
            if (!target.IsAlive || ReferenceEquals(target, enemy))
            {
                continue;
            }

            if ((target.Position - enemy.Position).LengthSquared() > radiusSq)
            {
                continue;
            }

            DealDamageToEnemy(target, damage, enemy.Position, false);
            EquipmentEnemyState targetState = GetOrCreateEquipmentEnemyState(target);
            targetState.ChillStacks = Mathf.Min(ChillStacksToFreeze - 1, targetState.ChillStacks + 1);
        }

        if (_equipmentEnemyStates.TryGetValue(enemy, out EquipmentEnemyState? state))
        {
            state.FrozenTimer = 0f;
        }
    }

    private void FireGlitchShot(Vector2 sourcePosition, Vector2 targetPosition)
    {
        WeaponKind randomKind = WeaponPool[_rng.RandiRange(0, WeaponPool.Length - 1)];
        Vector2 direction = targetPosition - sourcePosition;
        if (direction.LengthSquared() < 0.01f)
        {
            float angle = _rng.RandfRange(0f, Mathf.Tau);
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        FireLegacyWeaponForKind(randomKind, direction, sourcePosition, 0.45f, true);
        SpawnVisualPulse(sourcePosition, 0.7f, 4f, new Color(0.76f, 0.34f, 1f, 0.32f), 0.2f);
    }

    private void TriggerShinyFlash()
    {
        SpawnVisualPulse(_player.Position, 1f, ShinyFlashRadius, new Color(1f, 0.86f, 0.20f, 0.42f), 0.28f);
        float radiusSq = ShinyFlashRadius * ShinyFlashRadius;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (!enemy.IsAlive || (enemy.Position - _player.Position).LengthSquared() > radiusSq)
            {
                continue;
            }

            EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(enemy);
            state.BlindTimer = ShinyFlashBlindDuration;
            float angle = _rng.RandfRange(0f, Mathf.Tau);
            state.BlindDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
    }

    private void TriggerEntropyPulse()
    {
        SpawnVisualPulse(_player.Position, 4f, EntropyPulseRange, new Color(0.78f, 0.28f, 1f, 0.28f), 0.38f);
        float rangeSq = EntropyPulseRange * EntropyPulseRange;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (!enemy.IsAlive || (enemy.Position - _player.Position).LengthSquared() > rangeSq)
            {
                continue;
            }

            EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(enemy);
            state.EntropySpeedMultiplier = _rng.RandfRange(0.55f, 1.65f);
            state.EntropyDamageMultiplier = _rng.RandfRange(0.55f, 1.85f);
            if (_rng.Randf() < EntropyBerserkChance)
            {
                state.EntropyBerserkTimer = EntropyBerserkDuration;
            }
        }
    }

    private void TickThornedCollisions()
    {
        if (_thornedTier < 2)
        {
            return;
        }

        List<EnemyActor> markedEnemies = new(_equipmentEnemyStates.Keys);
        for (int markedIndex = 0; markedIndex < markedEnemies.Count; markedIndex++)
        {
            EnemyActor markedEnemy = markedEnemies[markedIndex];
            if (!_equipmentEnemyStates.TryGetValue(markedEnemy, out EquipmentEnemyState? state))
            {
                continue;
            }

            if (!markedEnemy.IsAlive || state.ThornedTimer <= 0f || state.ThornedCollisionTimer > 0f)
            {
                continue;
            }

            EnemyActor? collisionTarget = FindNearestEnemy(markedEnemy.Position, markedEnemy, false, markedEnemy.Radius + ThornSpreadRange);
            if (collisionTarget == null)
            {
                continue;
            }

            state.ThornedCollisionTimer = ThornCollisionCooldown;
            float damage = Mathf.Max(8f, markedEnemy.MaxHp * 0.12f);
            DealDamageToEnemy(collisionTarget, damage, markedEnemy.Position, false);
            if (_thornedTier >= 2)
            {
                EquipmentEnemyState targetState = GetOrCreateEquipmentEnemyState(collisionTarget);
                targetState.ThornedTimer = ThornMarkDuration;
            }
        }
    }

    private void ApplyEquipmentThornedReflect(EnemyActor? sourceEnemy, float amount)
    {
        if (sourceEnemy == null || !(HasPrimaryModifier(ModifierKey.Thorned) || _thornedTier >= 1))
        {
            return;
        }

        float reflectDamage = amount * ThornReflectPercent;
        if (reflectDamage <= 0f)
        {
            return;
        }

        DealDamageToEnemy(sourceEnemy, reflectDamage, _player.Position, false);
        EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(sourceEnemy);
        state.ThornedTimer = ThornMarkDuration;
    }

    private void HandleEquipmentEnemyKilled(EnemyActor enemy)
    {
        if (_darkTier >= 2 || HasPrimaryModifier(ModifierKey.Dark))
        {
            SpawnDarkPhantom(enemy.Position, Mathf.Max(20f, _player.BulletDamage * 0.55f));
        }

        if (_equipmentEnemyStates.TryGetValue(enemy, out EquipmentEnemyState? state))
        {
            if (_thornedTier >= 4 && state.ThornedTimer > 0f)
            {
                TriggerThornedDeathExplosion(enemy.Position, enemy.MaxHp * ThornExplosionDamagePercent);
            }

            if (state.Infected && _parasiteTier >= 2 && _rng.Randf() < ParasiteChainChance)
            {
                TryInfectNearestEnemy(enemy.Position);
            }
        }

        if ((HasPrimaryModifier(ModifierKey.Parasite) || _parasiteTier >= 1) && _rng.Randf() < ParasiteInfectChance)
        {
            TryInfectNearestEnemy(enemy.Position);
        }
    }

    private void ForgetEquipmentEnemyState(EnemyActor enemy)
    {
        _equipmentEnemyStates.Remove(enemy);
    }

    private void TriggerThornedDeathExplosion(Vector2 position, float damage)
    {
        SpawnVisualPulse(position, 1f, ThornExplosionRadius, new Color(0.15f, 0.86f, 0.36f, 0.38f), 0.25f);
        float radiusSq = ThornExplosionRadius * ThornExplosionRadius;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor target = _enemies[enemyIndex];
            if (!target.IsAlive || (target.Position - position).LengthSquared() > radiusSq)
            {
                continue;
            }

            DealDamageToEnemy(target, damage, position, false);
        }
    }

    private void TrySpawnEquipmentBonusXpOrb(EnemyActor enemy, int xpValue)
    {
        if (_shinyTier < 1 || _rng.Randf() >= ShinyExtraOrbChance)
        {
            return;
        }

        SpawnPickup(PickupKind.XpOrb, Mathf.Max(1, Mathf.RoundToInt(xpValue * 0.75f)), enemy.Position, 150f);
    }

    private void SpawnDarkPhantom(Vector2 position, float damage)
    {
        while (_equipmentPhantoms.Count >= MaxPhantoms)
        {
            EquipmentPhantom oldPhantom = _equipmentPhantoms[0];
            QueueFreeIfValid(oldPhantom.Mesh);
            _equipmentPhantoms.RemoveAt(0);
        }

        MeshInstance3D mesh = CreateSphereVisual(position, 0.85f, new Color(0.46f, 0.22f, 0.80f, 0.78f), 0.72f, 2.2f);
        _equipmentPhantoms.Add(new EquipmentPhantom
        {
            Position = position,
            Damage = damage,
            Mesh = mesh,
        });
    }

    private void TickEquipmentPhantoms(float dt)
    {
        for (int index = _equipmentPhantoms.Count - 1; index >= 0; index--)
        {
            EquipmentPhantom phantom = _equipmentPhantoms[index];
            phantom.Age += dt;
            EnemyActor? target = FindNearestEnemy(phantom.Position, null, false);
            if (phantom.Age >= PhantomLifetime || target == null)
            {
                QueueFreeIfValid(phantom.Mesh);
                _equipmentPhantoms.RemoveAt(index);
                continue;
            }

            Vector2 direction = target.Position - phantom.Position;
            if (direction.LengthSquared() > 0.01f)
            {
                phantom.Position += direction.Normalized() * PhantomSpeed * dt;
            }

            phantom.Mesh.Position = new Vector3(phantom.Position.X, 1.2f + Mathf.Sin((_gameTimer + phantom.Age) * 8f) * 0.25f, phantom.Position.Y);

            if ((target.Position - phantom.Position).LengthSquared() <= PhantomHitRadius * PhantomHitRadius)
            {
                SpawnVisualPulse(phantom.Position, 1f, PhantomExplosionRadius, new Color(0.42f, 0.16f, 0.76f, 0.36f), 0.24f);
                DamageEnemiesInRadius(phantom.Position, PhantomExplosionRadius, phantom.Damage, false);
                QueueFreeIfValid(phantom.Mesh);
                _equipmentPhantoms.RemoveAt(index);
            }
        }
    }

    private void TryInfectNearestEnemy(Vector2 origin)
    {
        EnemyActor? target = FindNearestEnemy(origin, null, true);
        if (target == null)
        {
            return;
        }

        EquipmentEnemyState state = GetOrCreateEquipmentEnemyState(target);
        state.Infected = true;
        state.InfectedTimer = _parasiteTier >= 4 ? -1f : ParasiteDuration;
        SpawnVisualPulse(target.Position, 0.8f, 4f, new Color(0.22f, 0.92f, 0.40f, 0.38f), 0.28f);
    }

    private void TryChainParasiteFromConvertedKill(Vector2 origin)
    {
        if (_parasiteTier >= 2 && _rng.Randf() < ParasiteChainChance)
        {
            TryInfectNearestEnemy(origin);
        }
    }

    private void DamageEnemiesInRadius(Vector2 position, float radius, float damage, bool triggerEquipmentModifiers)
    {
        float radiusSq = radius * radius;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (!enemy.IsAlive || (enemy.Position - position).LengthSquared() > radiusSq)
            {
                continue;
            }

            DealDamageToEnemy(enemy, damage, position, triggerEquipmentModifiers);
        }
    }

    private void TriggerVolitileExplosion(Vector2 center, float triggeringDamage, EnemyActor? primaryEnemy = null, bool critical = false, bool includeBoss = false)
    {
        if (GetVolitileEffectiveStackCount() <= 0)
        {
            return;
        }

        float explosionDamage = Mathf.Max(0f, RuntimeMath.FiniteOr(triggeringDamage * VolitileExplosionDamagePercent, 0f));
        if (explosionDamage <= 0f)
        {
            return;
        }

        float radius = GetVolitileExplosionRadius();
        SpawnVisualPulse(center, radius * 0.25f, radius, new Color(1f, 0.44f, 0.18f, 0.34f), 0.22f);

        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (!enemy.IsAlive || ReferenceEquals(enemy, primaryEnemy))
            {
                continue;
            }

            float hitRadius = radius + enemy.Radius;
            if ((enemy.Position - center).LengthSquared() <= hitRadius * hitRadius)
            {
                DealDamageToEnemy(enemy, explosionDamage, center, false, critical, false);
            }
        }

        if (_boss != null && (includeBoss || _boss.IsAlive))
        {
            float hitRadius = radius + _boss.Radius;
            if ((_boss.Position - center).LengthSquared() <= hitRadius * hitRadius)
            {
                DealDamageToBoss(explosionDamage, critical, false);
            }
        }

        if (primaryEnemy != null && primaryEnemy.IsAlive)
        {
            DealDamageToEnemy(primaryEnemy, explosionDamage, center, false, critical, false);
        }
    }

    private float GetVolitileExplosionRadius()
    {
        return GetVolitileExplosionRadiusForStage(GetVolitileEffectiveStackCount());
    }

    private float GetVolitileEffectiveStackCount()
    {
        return Mathf.Clamp(_volitileEffectiveStacks, 0, 4);
    }

    private static float GetVolitileExplosionRadiusForStage(float stage)
    {
        return Mathf.Clamp(stage, 1f, 4f);
    }

    private EquipmentEnemyState GetOrCreateEquipmentEnemyState(EnemyActor enemy)
    {
        if (!_equipmentEnemyStates.TryGetValue(enemy, out EquipmentEnemyState? state))
        {
            state = new EquipmentEnemyState();
            _equipmentEnemyStates[enemy] = state;
        }

        return state;
    }

    private EnemyActor? FindRandomAliveEnemy()
    {
        List<EnemyActor> aliveEnemies = new();
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (enemy.IsAlive)
            {
                aliveEnemies.Add(enemy);
            }
        }

        return aliveEnemies.Count == 0 ? null : aliveEnemies[_rng.RandiRange(0, aliveEnemies.Count - 1)];
    }

    private EnemyActor? FindNearestEnemy(Vector2 origin, EnemyActor? excludeEnemy, bool excludeConverted, float maxRange = float.MaxValue)
    {
        float bestDistanceSq = maxRange * maxRange;
        EnemyActor? bestEnemy = null;
        for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
        {
            EnemyActor enemy = _enemies[enemyIndex];
            if (!enemy.IsAlive || ReferenceEquals(enemy, excludeEnemy))
            {
                continue;
            }

            if (excludeConverted && _equipmentEnemyStates.TryGetValue(enemy, out EquipmentEnemyState? state) && state.Infected)
            {
                continue;
            }

            float distanceSq = (enemy.Position - origin).LengthSquared();
            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                bestEnemy = enemy;
            }
        }

        return bestEnemy;
    }

    private void ApplyEquipmentLifeSteal(float dealtDamage)
    {
        float lifeStealPercent = GetEquipmentLifeStealPercent();
        if (lifeStealPercent <= 0f || dealtDamage <= 0f)
        {
            return;
        }

        float heal = dealtDamage * lifeStealPercent / 100f;
        _player.Hp = Mathf.Min(_player.MaxHp, _player.Hp + heal);
    }

    private float GetEquipmentLifeStealPercent()
    {
        float percent = _equipmentLifeStealPct;
        if (HasPrimaryModifier(ModifierKey.Dark) || _darkTier >= 1)
        {
            percent += DarkLifeStealPercent;
        }

        return percent;
    }

    private float GetTotalLifeStealPercent()
    {
        float percent = GetEquipmentLifeStealPercent();
        if (_arenaDebugOverridesActive)
        {
            percent += Mathf.Max(0f, _arenaDebugOverrides.LifeStealPercent);
        }

        return percent;
    }

    private float GetTotalDodgeChancePercent()
    {
        float percent = _equipmentDodgeChancePct;
        if (_arenaDebugOverridesActive)
        {
            percent += Mathf.Max(0f, _arenaDebugOverrides.DodgeChancePercent);
        }

        return Mathf.Clamp(percent, 0f, 95f);
    }

    private float GetTotalCritChancePercent()
    {
        float percent = _equipmentCritChancePct;
        if (_arenaDebugOverridesActive)
        {
            percent += Mathf.Max(0f, _arenaDebugOverrides.CritChancePercent);
        }

        return Mathf.Clamp(percent, 0f, 100f);
    }

    private float GetTotalCritDamagePercent()
    {
        float percent = _equipmentCritDamagePct;
        if (_arenaDebugOverridesActive)
        {
            percent += Mathf.Max(0f, _arenaDebugOverrides.CritDamagePercent);
        }

        return percent;
    }

    private static float GetCritDamageMultiplier(float critDamagePercent)
    {
        float totalCritDamagePercent = BaseCriticalDamagePercent + Mathf.Max(0f, critDamagePercent);
        return 1f + totalCritDamagePercent / 100f;
    }

    private float GetTotalRegenPerSecond()
    {
        float regen = _equipmentHealthRegen;
        if (_arenaDebugOverridesActive)
        {
            regen += Mathf.Max(0f, _arenaDebugOverrides.HealthRegenPerSecond);
        }

        return regen;
    }

    private float GetEquipmentWeaponSizeMultiplier()
    {
        return Mathf.Clamp(RuntimeMath.FiniteOr(1f + _equipmentSizePct / 100f, 1f), 0.1f, 12f);
    }

    private float GetEquipmentWeaponRangeMultiplier()
    {
        return Mathf.Clamp(RuntimeMath.FiniteOr(1f + _equipmentRangeFlat / 100f, 1f), 0.1f, 12f);
    }

    private float GetEquipmentXpGainMultiplier()
    {
        float multiplier = 1f;
        if (HasPrimaryModifier(ModifierKey.Shiny))
        {
            multiplier += 0.25f;
        }

        return multiplier;
    }

    private float GetEquipmentCoinGainMultiplier()
    {
        return 1f + _equipmentLuckFlat / 400f;
    }

    private float ApplyEquipmentShotDamageModifiers(float damage, out bool critical, EquipmentItem? sourceItem = null)
    {
        critical = false;
        damage = Mathf.Max(0f, RuntimeMath.FiniteOr(damage, BaseBulletDamage));
        if (HasPrimaryModifier(ModifierKey.Unstable) || _unstableTier >= 1)
        {
            damage *= _rng.RandfRange(UnstableJitterMin, UnstableJitterMax);
        }

        float critChancePercent = sourceItem == null
            ? GetTotalCritChancePercent()
            : GetWeaponItemCritChancePercent(sourceItem);
        float critDamagePercent = sourceItem == null
            ? GetTotalCritDamagePercent()
            : GetWeaponItemCritDamagePercent(sourceItem);
        float critChance = Mathf.Clamp(critChancePercent / 100f, 0f, 1f);
        if (critChance > 0f && _rng.Randf() < critChance)
        {
            damage *= GetCritDamageMultiplier(critDamagePercent);
            critical = true;
        }

        return Mathf.Max(0f, RuntimeMath.FiniteOr(damage, BaseBulletDamage));
    }

    private float ApplyEquipmentBossDamageMultiplier(float amount)
    {
        return _equipmentBossInfectedTimer > 0f ? amount * 0.5f : amount;
    }

    private void TryFirePrismaticSurge(Vector2 targetPosition)
    {
        if (!_prismaticSurgeActive || _rng.Randf() >= 0.25f)
        {
            return;
        }

        ResolveArenaWeaponShot(0, targetPosition, targetPosition - _player.Position, out _, out Vector2 baseDirection);
        float angleOffset = _rng.RandfRange(-0.45f, 0.45f);
        Vector2 surgeDirection = baseDirection.Rotated(angleOffset);
        Vector2 surgeOrigin = GetArenaWeaponFireOrigin(0, surgeDirection);
        NotifyArenaWeaponFired(0, surgeDirection);
        FireLegacyWeaponForKind(_weaponKind, surgeDirection, surgeOrigin, 0.65f, true);
    }

    private bool HasPrimaryModifier(ModifierKey key)
    {
        return _primaryModifierKey == key;
    }

    private string BuildEquipmentModifierSummaryText()
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(_primaryModifierDisplayName) && _primaryModifierKey != ModifierKey.Normal)
        {
            parts.Add($"Primary Essence {_primaryModifierDisplayName}");
        }

        AddEquipmentTierSummary(parts, "Shiny", _shinyTier);
        AddEquipmentTierSummary(parts, "Electric", _electricTier);
        AddEquipmentTierSummary(parts, "Dark", _darkTier);
        AddEquipmentTierSummary(parts, "Unstable", _unstableTier);
        AddEquipmentTierSummary(parts, "Shielded", _shieldedTier);
        AddEquipmentTierSummary(parts, "Glacial", _glacialTier);
        AddEquipmentTierSummary(parts, "Thorned", _thornedTier);
        AddEquipmentTierSummary(parts, "Parasitic", _parasiteTier);
        if (_volitileEffectiveStacks > 0)
        {
            parts.Add($"Volitile R{Mathf.Clamp(_volitileEffectiveStacks, 1, 4)}");
        }
        if (_prismaticSurgeActive)
        {
            parts.Add(_fullSpectrumActive ? "Full Spectrum" : "Chromatic Surge");
        }

        return parts.Count == 0 ? string.Empty : "Equip " + string.Join(", ", parts);
    }

    private static void AddEquipmentTierSummary(List<string> parts, string label, int tier)
    {
        if (tier <= 0)
        {
            return;
        }

        string suffix = tier >= 4 ? "S" : tier >= 2 ? "+" : string.Empty;
        parts.Add(label + suffix);
    }

    private static WeaponKind MapEquipmentShapeToWeapon(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "circle" => WeaponKind.Circle,
            "triangle" => WeaponKind.Triangle,
            "hexagon" => WeaponKind.Hexagon,
            "sphere" => WeaponKind.Sphere,
            "cube" => WeaponKind.Cube,
            "pyramid" => WeaponKind.Pyramid,
            "cone" => WeaponKind.Cone,
            "tesseract" => WeaponKind.Tesseract,
            "hyperphere" => WeaponKind.Hyperphere,
            "hypersphere" => WeaponKind.Hyperphere,
            "pentachoron" => WeaponKind.Pentachoron,
            "hypercone" => WeaponKind.Hypercone,
            "impossiblecube" => WeaponKind.ImpossibleCube,
            "impossible_cube" => WeaponKind.ImpossibleCube,
            "mobius" => WeaponKind.Mobius,
            "klein" => WeaponKind.Klein,
            "boys" => WeaponKind.BoysSurface,
            "boyssurface" => WeaponKind.BoysSurface,
            "boys_surface" => WeaponKind.BoysSurface,
            _ => WeaponKind.Square,
        };
    }

    private static RarityTier MapEquipmentRarity(string? key)
    {
        return EquipmentCatalogDefaults.NormalizeRarityKey(key) switch
        {
            "uncommon" => RarityTier.Uncommon,
            "rare" => RarityTier.Rare,
            "legendary" => RarityTier.Epic,
            "mythic" => RarityTier.Legendary,
            "divine" => RarityTier.Mythic,
            _ => RarityTier.Common,
        };
    }
}
