using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    private static void AddAdvancedTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateAdvancedTemplate(
            "echo_chamber",
            "Echo Chamber",
            "Repeats the installed weapon after a short delay at reduced power.",
            2.1f,
            new Color(0.9961f, 0.7608f, 0.4314f),
            Tags("echo", "tempo", "tier3"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.EchoAfterDelay,
                Trigger = ModulatorEffectTrigger.OnFire,
                Magnitude = 0.45f,
                MagnitudeB = 0.55f,
                Count = 1,
            },
            blockedWeaponTags: Tags("passive")));

        output.Add(CreateAdvancedTemplate(
            "chain_reaction",
            "Chain Reaction",
            "Direct hits arc into nearby targets for reduced follow-up damage.",
            1.9f,
            new Color(0.4588f, 0.8392f, 1.0000f),
            Tags("chain", "proc", "tier3"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ChainToNearby,
                Trigger = ModulatorEffectTrigger.OnHit,
                Magnitude = 0.45f,
                MagnitudeB = 18f,
                Count = 2,
            },
            allowedWeaponTags: Tags("precision", "homing", "piercing")));

        output.Add(CreateAdvancedTemplate(
            "lingering_scar",
            "Lingering Scar",
            "Direct hits leave behind a short-lived damage field.",
            1.8f,
            new Color(1.0000f, 0.4824f, 0.6157f),
            Tags("zone", "lingering", "tier3"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.LingeringZone,
                Trigger = ModulatorEffectTrigger.OnHit,
                Magnitude = 0.35f,
                MagnitudeB = 2.8f,
                MagnitudeC = 2.8f,
            },
            allowedWeaponTags: Tags("precision", "homing", "piercing")));

        output.Add(CreateAdvancedTemplate(
            "predatory_mark",
            "Predatory Mark",
            "Tagged targets take amplified follow-up damage for a brief window.",
            1.8f,
            new Color(0.9294f, 0.9529f, 0.4980f),
            Tags("mark", "precision", "tier3"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ApplyMark,
                Trigger = ModulatorEffectTrigger.OnHit,
                Magnitude = 1.22f,
                MagnitudeB = 4.0f,
            },
            allowedWeaponTags: Tags("precision", "homing", "piercing")));

        output.Add(CreateAdvancedTemplate(
            "finisher_gate",
            "Finisher Gate",
            "Hits land much harder against targets already close to death.",
            1.7f,
            new Color(1.0000f, 0.3569f, 0.3569f),
            Tags("execute", "finisher", "tier3"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.ExecuteLowHealth,
                Trigger = ModulatorEffectTrigger.OnHit,
                Magnitude = 0.18f,
                MagnitudeB = 1.85f,
                MagnitudeC = 1.45f,
            }));

        output.Add(CreateAdvancedTemplate(
            "boss_geometry",
            "Boss Geometry",
            "Specializes the weapon for elites and bosses at the cost of weaker hits on rank-and-file enemies.",
            1.6f,
            new Color(0.7412f, 0.5804f, 1.0000f),
            Tags("boss", "elite", "hunter", "tier3"),
            new WeaponModulatorEffectData
            {
                Type = ModulatorEffectType.EliteBossHunter,
                Trigger = ModulatorEffectTrigger.OnHit,
                Magnitude = 1.40f,
                MagnitudeB = 0.85f,
            }));
    }

    private static WeaponModulatorData CreateAdvancedTemplate(
        string key,
        string displayName,
        string description,
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
            WeaponModulatorCategory.Proc,
            weight,
            uiColor,
            tags);

        modulator.PowerTier = ModulatorPowerTier.Tier3;
        modulator.AllowedWeaponTags = allowedWeaponTags == null ? new List<string>() : new List<string>(allowedWeaponTags);
        modulator.BlockedWeaponTags = blockedWeaponTags == null ? new List<string>() : new List<string>(blockedWeaponTags);
        modulator.Effects = new List<WeaponModulatorEffectData>
        {
            new()
            {
                Type = effect.Type,
                Trigger = effect.Trigger,
                Magnitude = effect.Magnitude,
                MagnitudeB = effect.MagnitudeB,
                MagnitudeC = effect.MagnitudeC,
                Count = effect.Count,
                CountB = effect.CountB,
                Key = effect.Key,
            },
        };
        modulator.RefreshPresentation();
        return modulator;
    }
}