using System;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild.Equipment.Data;

[Flags]
public enum WeaponModulatorCapability
{
    None = 0,
    ProjectileSpeed = 1,
    WorldBounce = 2,
    SawReturn = 4,
    SecondPulse = 8,
    TargetLock = 16,
}

public static class WeaponModulatorCompatibility
{
    /// <summary>Shared installation eligibility. Slot capacity and Wing loadout rules remain contextual.</summary>
    public static bool CanApplyToWeapon(WeaponModulatorData? part, EquipmentItem? weapon, out string reason)
    {
        if (part == null)
        {
            reason = "Select a modulator to install.";
            return false;
        }
        if (weapon?.Shape == null)
        {
            reason = "Select a weapon with a defined attack pattern.";
            return false;
        }

        string partName = !string.IsNullOrWhiteSpace(part.BaseDisplayName) ? part.BaseDisplayName
            : !string.IsNullOrWhiteSpace(part.DisplayName) ? part.DisplayName : "This modulator";
        if (part.IsRetired)
        {
            reason = $"{partName} is retired and cannot be installed. Keep it for a future replacement.";
            return false;
        }
        string weaponName = string.IsNullOrWhiteSpace(weapon.Shape.DisplayName) ? "this shape" : weapon.Shape.DisplayName;
        HashSet<string> tags = BuildWeaponTags(weapon);
        if (!part.CanApplyToWeapon(tags))
        {
            reason = $"{partName} does not fit {weaponName}.";
            return false;
        }

        WeaponAttackType type = weapon.Shape.AttackType;
        WeaponModulatorCapability capabilities = GetCapabilities(type);
        foreach (WeaponModulatorEffectData effect in part.GetResolvedEffects())
        {
            WeaponModulatorCapability required = RequiredCapability(effect.Type);
            if (required != WeaponModulatorCapability.None && (capabilities & required) != required)
            {
                reason = $"{partName} requires {CapabilityDescription(required)}; {weaponName} does not support it.";
                return false;
            }
            if (effect.Type == ModulatorEffectType.NewWeaponTransform && !IsTransformCompatible(effect.Key, type))
            {
                reason = $"{partName} requires a different attack pattern than {weaponName}.";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    /// <summary>Delivery capabilities preserve the existing sixteen-pattern FPS installation rules.</summary>
    public static WeaponModulatorCapability GetCapabilities(WeaponAttackType type) => type switch
    {
        WeaponAttackType.Single or WeaponAttackType.Drill => WeaponModulatorCapability.ProjectileSpeed | WeaponModulatorCapability.WorldBounce,
        WeaponAttackType.Sawblade => WeaponModulatorCapability.ProjectileSpeed | WeaponModulatorCapability.WorldBounce | WeaponModulatorCapability.SawReturn,
        WeaponAttackType.Homing => WeaponModulatorCapability.ProjectileSpeed | WeaponModulatorCapability.WorldBounce | WeaponModulatorCapability.TargetLock,
        WeaponAttackType.Mortar => WeaponModulatorCapability.ProjectileSpeed,
        WeaponAttackType.Nova => WeaponModulatorCapability.SecondPulse,
        _ => WeaponModulatorCapability.None,
    };

    public static bool IsTransformCompatible(string key, WeaponAttackType type) => WeaponModulatorData.NormalizeKey(key) switch
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

    private static WeaponModulatorCapability RequiredCapability(ModulatorEffectType effect) => effect switch
    {
        ModulatorEffectType.ProjectileSpeedMultiplier => WeaponModulatorCapability.ProjectileSpeed,
        ModulatorEffectType.BounceTimesAdd => WeaponModulatorCapability.WorldBounce,
        ModulatorEffectType.NewSawReturn => WeaponModulatorCapability.SawReturn,
        ModulatorEffectType.NewSecondPulse => WeaponModulatorCapability.SecondPulse,
        ModulatorEffectType.NewTargetLockBias => WeaponModulatorCapability.TargetLock,
        _ => WeaponModulatorCapability.None,
    };

    private static string CapabilityDescription(WeaponModulatorCapability capability) => capability switch
    {
        WeaponModulatorCapability.ProjectileSpeed => "projectile delivery speed",
        WeaponModulatorCapability.WorldBounce => "projectiles that bounce from world surfaces",
        WeaponModulatorCapability.SawReturn => "a returning sawblade",
        WeaponModulatorCapability.SecondPulse => "a repeatable nova pulse",
        WeaponModulatorCapability.TargetLock => "guided target locking",
        _ => "a matching attack capability",
    };

    public static HashSet<string> BuildWeaponTags(EquipmentItem? item)
    {
        HashSet<string> tags = new(System.StringComparer.Ordinal);
        if (item?.Shape == null)
        {
            return tags;
        }

        AddTag(tags, "weapon");
        AddTag(tags, item.Shape.Key);

        switch (item.Shape.AttackType)
        {
            case WeaponAttackType.Single:
                AddTag(tags, "projectile");
                AddTag(tags, "precision");
                break;
            case WeaponAttackType.Sawblade:
                AddTag(tags, "projectile");
                AddTag(tags, "piercing");
                break;
            case WeaponAttackType.Laser:
                AddTag(tags, "beam");
                AddTag(tags, "precision");
                break;
            case WeaponAttackType.Mine:
                AddTag(tags, "mine");
                AddTag(tags, "zone");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Nova:
                AddTag(tags, "aura");
                AddTag(tags, "pulse");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Orbital:
                AddTag(tags, "orbital");
                AddTag(tags, "sustain");
                break;
            case WeaponAttackType.Homing:
                AddTag(tags, "projectile");
                AddTag(tags, "homing");
                break;
            case WeaponAttackType.FlameCone:
                AddTag(tags, "cone");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Rift:
                AddTag(tags, "zone");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Mortar:
                AddTag(tags, "mortar");
                AddTag(tags, "area");
                AddTag(tags, "projectile");
                break;
            case WeaponAttackType.ChainLightning:
                AddTag(tags, "chain");
                AddTag(tags, "projectile");
                break;
            case WeaponAttackType.Drill:
                AddTag(tags, "projectile");
                AddTag(tags, "piercing");
                break;
            case WeaponAttackType.VoidSingularity:
                AddTag(tags, "zone");
                AddTag(tags, "area");
                AddTag(tags, "pull");
                break;
            case WeaponAttackType.TemporalEcho:
                AddTag(tags, "passive");
                AddTag(tags, "echo");
                break;
            case WeaponAttackType.WrathAura:
                AddTag(tags, "passive");
                AddTag(tags, "aura");
                break;
            case WeaponAttackType.ShadowClone:
                AddTag(tags, "clone");
                AddTag(tags, "replay");
                break;
        }

        return tags;
    }

    private static void AddTag(HashSet<string> tags, string tag)
    {
        string normalized = WeaponModulatorData.NormalizeTag(tag);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            tags.Add(normalized);
        }
    }
}
