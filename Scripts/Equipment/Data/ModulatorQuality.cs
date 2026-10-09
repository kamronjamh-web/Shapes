using System;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

/// <summary>Resolves detached values from immutable-in-practice baseline payloads, never from an earlier resolution.</summary>
public static class ModulatorQuality
{
    public static float GetStrength(WeaponModulatorTier quality) => quality switch
    {
        WeaponModulatorTier.Flawed => 0.5f,
        WeaponModulatorTier.Enhanced => 1.5f,
        WeaponModulatorTier.Flawless => 2f,
        _ => 1f,
    };

    public static float ScaleBonusMultiplier(float value, float strength) => value > 1f ? 1f + (value - 1f) * strength : value;
    public static float ScaleCooldown(float value, float strength) => value > 0f && value < 1f
        ? 1f / (1f + (1f / value - 1f) * strength) : value;
    private static float Positive(float value, float strength) => value > 0f ? value * strength : value;

    public static WeaponModulatorEffectData ResolveEffect(WeaponModulatorEffectData baseline, WeaponModulatorTier quality, string? partKey = null)
    {
        WeaponModulatorEffectData effect = CloneEffect(baseline);
        float strength = GetStrength(quality);
        effect.QualityStrength = strength;
        switch (effect.Type)
        {
            case ModulatorEffectType.CooldownMultiplier:
                effect.Magnitude = ScaleCooldown(effect.Magnitude, strength);
                break;
            case ModulatorEffectType.NewOrbitRadiusScalar:
                // Compression's tighter radius is a fixed risk attached to its output bonus.
                if (WeaponModulatorData.NormalizeKey(partKey) == "orbital_compression") break;
                // Tight orbit is the intended benefit, expressed as a radius reduction.
                effect.Magnitude = effect.Magnitude > 0f && effect.Magnitude < 1f
                    ? Math.Max(0.1f, 1f - (1f - effect.Magnitude) * strength)
                    : ScaleBonusMultiplier(effect.Magnitude, strength);
                break;
            case ModulatorEffectType.DamageMultiplier:
            case ModulatorEffectType.HealthMultiplier:
            case ModulatorEffectType.AreaMultiplier:
            case ModulatorEffectType.RangeMultiplier:
            case ModulatorEffectType.ProjectileSpeedMultiplier:
            case ModulatorEffectType.DurationMultiplier:
            case ModulatorEffectType.CritDamageMultiplier:
            case ModulatorEffectType.MovementSpeedMultiplier:
            case ModulatorEffectType.KnockbackMultiplier:
            case ModulatorEffectType.HomingStrengthMultiplier:
            case ModulatorEffectType.TickRateMultiplier:
            case ModulatorEffectType.PullStrengthMultiplier:
            case ModulatorEffectType.CloneOrEchoPowerMultiplier:
            case ModulatorEffectType.NewTargetLockBias:
            case ModulatorEffectType.NewCapstoneRule:
                effect.Magnitude = ScaleBonusMultiplier(effect.Magnitude, strength);
                break;
            case ModulatorEffectType.CritChanceAdd:
            case ModulatorEffectType.DodgeChanceAdd:
            case ModulatorEffectType.LifeStealAdd:
            case ModulatorEffectType.HealthRegenAdd:
            case ModulatorEffectType.LuckAdd:
                effect.Magnitude = Positive(effect.Magnitude, strength);
                break;
            case ModulatorEffectType.RicochetAdd:
            case ModulatorEffectType.BounceTimesAdd:
            case ModulatorEffectType.ProjectileCountAdd:
            case ModulatorEffectType.PierceAdd:
            case ModulatorEffectType.ChainCountAdd:
                effect.CountValue = Positive(baseline.ResolvedCountValue, strength);
                break;
            case ModulatorEffectType.NewSawReturn:
                effect.Magnitude = (effect.Magnitude > 0f ? effect.Magnitude : 0.9f) * strength;
                break;
            case ModulatorEffectType.NewSecondPulse:
            case ModulatorEffectType.NewOnCritChainProc:
            case ModulatorEffectType.NewCapstoneProc:
            case ModulatorEffectType.ChainToNearby:
            case ModulatorEffectType.LingeringZone:
                effect.Magnitude = Positive(effect.Magnitude, strength);
                break;
            case ModulatorEffectType.EchoAfterDelay:
                effect.MagnitudeB = Positive(effect.MagnitudeB, strength);
                break;
            case ModulatorEffectType.ApplyMark:
                effect.Magnitude = ScaleBonusMultiplier(effect.Magnitude, strength);
                break;
            case ModulatorEffectType.ExecuteLowHealth:
                // Health threshold is a trigger; quality strengthens only the finisher.
                effect.MagnitudeB = ScaleBonusMultiplier(effect.MagnitudeB, strength);
                effect.MagnitudeC = ScaleBonusMultiplier(effect.MagnitudeC, strength);
                break;
            case ModulatorEffectType.EliteBossHunter:
                effect.Magnitude = ScaleBonusMultiplier(effect.Magnitude, strength);
                effect.MagnitudeB = ScaleBonusMultiplier(effect.MagnitudeB, strength);
                break;
            case ModulatorEffectType.NewWeaponTransform:
                switch (WeaponModulatorData.NormalizeKey(effect.Key))
                {
                    case "echo_weave":
                        effect.Magnitude = Positive(effect.Magnitude, strength); // delay unchanged
                        break;
                    case "prism_refraction":
                        effect.MagnitudeB = Positive(effect.MagnitudeB, strength); // contact delay unchanged
                        break;
                    case "pack_hunters":
                        effect.MagnitudeB = ScaleBonusMultiplier(effect.MagnitudeB, strength); // mark lifetime unchanged
                        break;
                    case "phase_inversion":
                        effect.MagnitudeB = (effect.MagnitudeB > 0f ? effect.MagnitudeB : 16f) * strength; // force, pulse interval unchanged
                        break;
                    case "event_horizon_tear":
                        // One damage-strength axis for both phases, fixed fragment topology.
                        effect.Magnitude = Positive(effect.Magnitude, strength);
                        effect.MagnitudeB = Positive(effect.MagnitudeB, strength);
                        break;
                    default:
                        effect.Magnitude = Positive(effect.Magnitude, strength);
                        break;
                }
                break;
        }
        return effect;
    }

    public static WeaponModulatorEffectData CloneEffect(WeaponModulatorEffectData effect) => new()
    {
        Type = effect.Type, Trigger = effect.Trigger, Magnitude = effect.Magnitude,
        MagnitudeB = effect.MagnitudeB, MagnitudeC = effect.MagnitudeC,
        Count = effect.Count, CountB = effect.CountB, CountValue = effect.CountValue, Key = effect.Key,
    };

    internal sealed class ResolutionCache
    {
        private WeaponModulatorTier _quality;
        private string? _partKey;
        private WeaponModulatorEffectData[]? _raw;
        private SubstatRoll[]? _substats;
        private IReadOnlyList<WeaponModulatorEffectData>? _resolved;

        public IReadOnlyList<WeaponModulatorEffectData> Resolve(WeaponModulatorData part)
        {
            if (_resolved != null && _quality == part.Tier && _partKey == part.NormalizedKey && Matches(part)) return _resolved;
            _quality = part.Tier;
            _partKey = part.NormalizedKey;
            _raw = new WeaponModulatorEffectData[part.Effects.Count];
            List<WeaponModulatorEffectData> resolved = new(part.Effects.Count + part.Substats.Count);
            HashSet<SubstatType> represented = new();
            for (int i = 0; i < _raw.Length; i++)
            {
                _raw[i] = CloneEffect(part.Effects[i]);
                resolved.Add(ResolveEffect(_raw[i], part.Tier, _partKey));
                if (_raw[i].IsPassive && _raw[i].TryConvertToSubstatRoll(out SubstatRoll roll)) represented.Add(roll.Type);
            }
            _substats = new SubstatRoll[part.Substats.Count];
            for (int i = 0; i < _substats.Length; i++)
            {
                SubstatRoll roll = part.Substats[i];
                _substats[i] = new SubstatRoll(roll.Type, roll.Value);
                if (represented.Contains(roll.Type)) continue;
                WeaponModulatorEffectData raw = WeaponModulatorEffectData.FromSubstatRoll(roll);
                // Preserve nonintegral saved count bonuses when synthesizing a missing effect.
                if (roll.Type is SubstatType.Ricochet or SubstatType.BounceTimes or SubstatType.ProjectileCount) raw.CountValue = roll.Value;
                if (raw.Type != ModulatorEffectType.None) resolved.Add(ResolveEffect(raw, part.Tier, _partKey));
            }
            _resolved = resolved.AsReadOnly();
            return _resolved;
        }

        private bool Matches(WeaponModulatorData part)
        {
            if (_raw == null || _substats == null || _raw.Length != part.Effects.Count || _substats.Length != part.Substats.Count) return false;
            for (int i = 0; i < _raw.Length; i++)
            {
                WeaponModulatorEffectData a = _raw[i], b = part.Effects[i];
                if (a.Type != b.Type || a.Trigger != b.Trigger || !a.Magnitude.Equals(b.Magnitude)
                    || !a.MagnitudeB.Equals(b.MagnitudeB) || !a.MagnitudeC.Equals(b.MagnitudeC)
                    || a.Count != b.Count || a.CountB != b.CountB || a.CountValue != b.CountValue || a.Key != b.Key) return false;
            }
            for (int i = 0; i < _substats.Length; i++)
                if (_substats[i].Type != part.Substats[i].Type || !_substats[i].Value.Equals(part.Substats[i].Value)) return false;
            return true;
        }
    }
}
