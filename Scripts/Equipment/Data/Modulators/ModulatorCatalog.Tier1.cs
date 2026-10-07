using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddTier1Templates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTier1Template(
            "sharpened_form",
            "Sharpened Form",
            "Raises the weapon's baseline damage without changing its behavior.",
            WeaponModulatorCategory.Core,
            9f,
            new Color(0.9647f, 0.4549f, 0.2863f),
            Tags("damage", "baseline", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.12f,
            }));

        output.Add(CreateTier1Template(
            "quickened_core",
            "Quickened Core",
            "Tightens weapon cadence for a simple, legible throughput increase.",
            WeaponModulatorCategory.Core,
            9f,
            new Color(0.9922f, 0.7608f, 0.2353f),
            Tags("cooldown", "cadence", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CooldownMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 0.90f,
            }));

        output.Add(CreateTier1Template(
            "expanded_shape",
            "Expanded Shape",
            "Widens the effective hit footprint of attacks and zones.",
            WeaponModulatorCategory.Projectile,
            8f,
            new Color(0.6039f, 0.9569f, 0.6627f),
            Tags("area", "size", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.AreaMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.14f,
            }));

        output.Add(CreateTier1Template(
            "long_reach",
            "Long Reach",
            "Extends projectile travel or attack length for cleaner precision play.",
            WeaponModulatorCategory.Projectile,
            8f,
            new Color(0.4941f, 0.8824f, 0.9608f),
            Tags("range", "utility", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.RangeMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.18f,
            }));

        output.Add(CreateTier1Template(
            "accelerated_flight",
            "Accelerated Flight",
            "Improves shot delivery speed for projectile, mortar, and guided weapons.",
            WeaponModulatorCategory.Projectile,
            8f,
            new Color(0.5294f, 0.7686f, 1.0000f),
            Tags("projectile_speed", "delivery", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.20f,
            },
            allowedWeaponTags: Tags("square", "circle", "pyramid", "hypercone", "hyperphere")));

        output.Add(CreateTier1Template(
            "lasting_pattern",
            "Lasting Pattern",
            "Extends the life of created objects, fields, and persistent summons.",
            WeaponModulatorCategory.Utility,
            7f,
            new Color(0.6863f, 0.8039f, 1.0000f),
            Tags("duration", "zone", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DurationMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.22f,
            },
            allowedWeaponTags: Tags("mine", "orbital", "zone", "clone")));

        output.Add(CreateTier1Template(
            "piercing_edge",
            "Piercing Edge",
            "Adds one more layer of passthrough to weapons built for line pressure.",
            WeaponModulatorCategory.Projectile,
            7f,
            new Color(0.9294f, 0.9176f, 0.6157f),
            Tags("pierce", "lane_clear", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.PierceAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Count = 1,
            },
            allowedWeaponTags: Tags("square", "circle", "pyramid", "hypercone")));

        output.Add(CreateTier1Template(
            "focused_edge",
            "Focused Edge",
            "Raises critical hit frequency without changing the weapon's pattern.",
            WeaponModulatorCategory.Core,
            8f,
            new Color(0.9765f, 0.8392f, 0.3373f),
            Tags("crit", "chance", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CritChanceAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 8f,
            }));

        output.Add(CreateTier1Template(
            "rupture_crest",
            "Rupture Crest",
            "Makes each critical strike land harder for crit-leaning builds.",
            WeaponModulatorCategory.Core,
            8f,
            new Color(1.0000f, 0.6431f, 0.3451f),
            Tags("crit", "damage", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CritDamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.25f,
            }));

        output.Add(CreateTier1Template(
            "guiding_bias",
            "Guiding Bias",
            "Strengthens homing correction for guided or guided-aim weapons.",
            WeaponModulatorCategory.Utility,
            7f,
            new Color(0.6157f, 0.8588f, 1.0000f),
            Tags("homing", "guidance", "tier1"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.HomingStrengthMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.20f,
            },
            allowedWeaponTags: Tags("pyramid")));
    }

    private static WeaponModulatorData CreateTier1Template(
        string key,
        string displayName,
        string description,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        WeaponModulatorEffectData effect,
        IEnumerable<string>? allowedWeaponTags = null,
        IEnumerable<string>? blockedWeaponTags = null)
    {
        WeaponModulatorData modulator = CreateTemplate(
            key,
            displayName,
            description,
            category,
            weight,
            uiColor,
            tags);

        modulator.PowerTier = ModulatorPowerTier.Tier1;
        modulator.AllowedWeaponTags = allowedWeaponTags == null ? new List<string>() : new List<string>(allowedWeaponTags);
        modulator.BlockedWeaponTags = blockedWeaponTags == null ? new List<string>() : new List<string>(blockedWeaponTags);
        modulator.Effects = new List<WeaponModulatorEffectData>
        {
            CloneTier1Effect(effect),
        };
        modulator.RefreshPresentation();
        return modulator;
    }

    private static WeaponModulatorEffectData CloneTier1Effect(WeaponModulatorEffectData effect)
    {
        return new WeaponModulatorEffectData
        {
            Type = effect.Type,
            Trigger = effect.Trigger,
            Magnitude = effect.Magnitude,
            MagnitudeB = effect.MagnitudeB,
            MagnitudeC = effect.MagnitudeC,
            Count = effect.Count,
            CountB = effect.CountB,
            Key = effect.Key,
        };
    }
}