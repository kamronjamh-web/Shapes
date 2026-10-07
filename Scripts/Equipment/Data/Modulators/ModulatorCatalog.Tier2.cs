using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddTier2Templates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTier2Template(
            "reinforced_geometry",
            "Reinforced Geometry",
            "Bigger, heavier attacks that reward area-focused builds.",
            WeaponModulatorCategory.Core,
            1.8f,
            new Color(0.9843f, 0.7216f, 0.4824f),
            Tags("geometry", "damage", "area", "tier2"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.15f,
            },
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.AreaMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.12f,
            }));

        output.Add(CreateTier2Template(
            "heavy_payload",
            "Heavy Payload",
            "Greatly expands impact area, but the attack travels more slowly.",
            WeaponModulatorCategory.Projectile,
            1.6f,
            new Color(0.9529f, 0.5255f, 0.4549f),
            Tags("payload", "area", "projectile", "tier2"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.AreaMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.35f,
            },
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 0.85f,
            },
            allowedWeaponTags: Tags("circle", "hyperphere"),
            blockedWeaponTags: Tags("passive")));

        output.Add(CreateTier2Template(
            "overclocked_core",
            "Overclocked Core",
            "Pushes attack cadence hard at the cost of weaker individual hits.",
            WeaponModulatorCategory.Core,
            1.7f,
            new Color(0.5059f, 0.8980f, 0.9961f),
            Tags("cadence", "cooldown", "damage", "tier2"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.CooldownMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 0.72f,
            },
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 0.86f,
            },
            blockedWeaponTags: Tags("passive")));

        output.Add(CreateTier2Template(
            "extra_output",
            "Extra Output",
            "Emits more attack instances while trimming per-hit strength.",
            WeaponModulatorCategory.Projectile,
            1.8f,
            new Color(0.6196f, 1.0000f, 0.7137f),
            Tags("multishot", "count", "damage", "tier2"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ProjectileCountAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Count = 1,
            },
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.DamageMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 0.76f,
            },
            allowedWeaponTags: Tags("square", "hexagon", "cube", "pyramid", "pentachoron", "boys"),
            blockedWeaponTags: Tags("passive")));

        output.Add(CreateTier2Template(
            "lancing_spiral",
            "Lancing Spiral",
            "Extends straight-line pressure with more reach and extra passthrough.",
            WeaponModulatorCategory.Projectile,
            1.5f,
            new Color(0.9333f, 0.9294f, 0.6078f),
            Tags("pierce", "range", "precision", "tier2"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.PierceAdd,
                Trigger = ModulatorEffectTrigger.Passive,
                Count = 1,
            },
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.RangeMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.16f,
            },
            allowedWeaponTags: Tags("square", "circle", "pyramid"),
            blockedWeaponTags: Tags("passive")));

        output.Add(CreateTier2Template(
            "predator_lens",
            "Predator Lens",
            "Improves tracking and delivery speed for guided attacks.",
            WeaponModulatorCategory.Utility,
            1.5f,
            new Color(0.6078f, 0.7765f, 1.0000f),
            Tags("homing", "tracking", "speed", "tier2"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.HomingStrengthMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.26f,
            },
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                Trigger = ModulatorEffectTrigger.Passive,
                Magnitude = 1.15f,
            },
            allowedWeaponTags: Tags("pyramid"),
            blockedWeaponTags: Tags("passive")));
    }

    private static WeaponModulatorData CreateTier2Template(
        string key,
        string displayName,
        string description,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        WeaponModulatorEffectData primaryEffect,
        WeaponModulatorEffectData secondaryEffect,
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

        modulator.PowerTier = ModulatorPowerTier.Tier2;
        modulator.AllowedWeaponTags = allowedWeaponTags == null ? new List<string>() : new List<string>(allowedWeaponTags);
        modulator.BlockedWeaponTags = blockedWeaponTags == null ? new List<string>() : new List<string>(blockedWeaponTags);
        modulator.Effects = new List<WeaponModulatorEffectData>
        {
            CloneTier2Effect(primaryEffect),
            CloneTier2Effect(secondaryEffect),
        };
        modulator.RefreshPresentation();
        return modulator;
    }

    private static WeaponModulatorEffectData CloneTier2Effect(WeaponModulatorEffectData effect)
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