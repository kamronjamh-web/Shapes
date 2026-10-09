using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild.Equipment.Data;

/// <summary>Player-facing values from the same resolved payload used by combat.</summary>
public static class ModulatorQualityText
{
    public static string Header(WeaponModulatorData part)
        => $"Tier {(int)part.PowerTier + 1} · {QualityName(part.Tier)} · {Number(part.QualityStrength * 100f)}% power";

    public static string Describe(WeaponModulatorData part)
    {
        if (part.IsRetired) return "Retired: no effects. Keep for a future replacement.";
        var stats = BuildStats(part);
        string values = stats.Count > 0 ? string.Join("\n", stats.Select(pair => $"{pair.Key}: {pair.Value}")) : "No combat bonuses.";
        string mechanics = Mechanics(part);
        return string.IsNullOrEmpty(mechanics) ? values : mechanics + "\n" + values;
    }

    public static string Compare(WeaponModulatorData candidate, EquipmentItem? weapon)
    {
        WeaponModulatorData? installed = weapon?.Modulators?.Where(part => part.NormalizedKey == candidate.NormalizedKey)
            .OrderByDescending(part => part.QualityStrength).FirstOrDefault();
        if (installed == null) return "No matching part installed.";
        if (candidate.IsRetired) return "Retired: neither copy grants effects.";
        var before = BuildStats(installed);
        var after = BuildStats(candidate);
        List<string> lines = new() { $"Compared with installed {QualityName(installed.Tier)}:" };
        foreach (string key in before.Keys.Concat(after.Keys).Distinct())
        {
            string oldValue = before.TryGetValue(key, out string? old) ? old : "none";
            string newValue = after.TryGetValue(key, out string? next) ? next : "none";
            lines.Add($"{key}: {oldValue} → {newValue}");
        }
        return string.Join("\n", lines);
    }

    public static string QualityName(WeaponModulatorTier quality) => quality switch
    {
        WeaponModulatorTier.Flawed => "Broken", WeaponModulatorTier.Enhanced => "Enhanced",
        WeaponModulatorTier.Flawless => "Flawless", _ => "Baseline",
    };

    internal static Dictionary<string, string> BuildStats(WeaponModulatorData part)
    {
        Dictionary<string, string> stats = new();
        void Value(string label, float value, string suffix = "") => stats[label] = Number(value) + suffix;
        void Damage(string label, float value) => Value(label, value * 100f, "%");
        void Bonus(string label, float multiplier) => stats[label] = Signed((multiplier - 1f) * 100f) + "%";
        void Flat(string label, float value, string suffix = "") => stats[label] = Signed(value) + suffix;
        void Count(string label, float value) => Flat(label, value, " average per attack");
        if (part.IsWingModulator)
        {
            Value("Wing cadence", part.WingCadenceMultiplier * 100f, "% of host rate");
            stats["Wing fire"] = "uses the host weapon's attack and stats";
        }
        if (part.NormalizedKey == "volitile")
        {
            Damage("Explosion damage", .5f);
            Value("Single-copy explosion radius", Math.Clamp(2f * part.QualityStrength, 1f, 4f), " m");
            stats["Stacked radius"] = "sum of copy strengths + strongest copy, capped at 4 m";
        }
        foreach (WeaponModulatorEffectData effect in part.GetResolvedEffects())
        {
            float a = effect.Magnitude, b = effect.MagnitudeB, c = effect.MagnitudeC;
            switch (effect.Type)
            {
                case ModulatorEffectType.DamageMultiplier: Bonus("Damage", a); break;
                case ModulatorEffectType.HealthMultiplier: Bonus("Health", a); break;
                case ModulatorEffectType.CooldownMultiplier: Bonus("Attack rate", 1f / Math.Max(.01f, a)); break;
                case ModulatorEffectType.AreaMultiplier: Bonus("Area", a); break;
                case ModulatorEffectType.RangeMultiplier: Bonus("Range", a); break;
                case ModulatorEffectType.ProjectileSpeedMultiplier: Bonus("Flight speed", a); break;
                case ModulatorEffectType.DurationMultiplier: Bonus("Duration", a); break;
                case ModulatorEffectType.CritDamageMultiplier: Bonus("Critical damage", a); break;
                case ModulatorEffectType.MovementSpeedMultiplier: Bonus("Movement speed", a); break;
                case ModulatorEffectType.KnockbackMultiplier: Bonus("Knockback", a); break;
                case ModulatorEffectType.HomingStrengthMultiplier: Bonus("Homing strength", a); break;
                case ModulatorEffectType.TickRateMultiplier: Bonus("Damage tick rate", a); break;
                case ModulatorEffectType.PullStrengthMultiplier: Bonus("Pull strength", a); break;
                case ModulatorEffectType.CloneOrEchoPowerMultiplier: Bonus("Clone / echo damage", a); break;
                case ModulatorEffectType.CritChanceAdd: Flat("Critical chance", a, " percentage points"); break;
                case ModulatorEffectType.DodgeChanceAdd: Flat("Dodge chance", a, " percentage points"); break;
                case ModulatorEffectType.LifeStealAdd: Flat("Life steal", a, "%"); break;
                case ModulatorEffectType.HealthRegenAdd: Flat("Health regeneration", a, " / s"); break;
                case ModulatorEffectType.LuckAdd: Flat("Luck", a); break;
                case ModulatorEffectType.RicochetAdd: Count("Hit ricochets", effect.ResolvedCountValue); break;
                case ModulatorEffectType.BounceTimesAdd: Count("Wall bounces", effect.ResolvedCountValue); break;
                case ModulatorEffectType.ProjectileCountAdd: Count("Extra output", effect.ResolvedCountValue); break;
                case ModulatorEffectType.PierceAdd: Count("Extra pierce", effect.ResolvedCountValue); break;
                case ModulatorEffectType.ChainCountAdd: Count("Extra chain targets", effect.ResolvedCountValue); break;
                case ModulatorEffectType.NewOrbitRadiusScalar: Damage("Orbit radius", a); break;
                case ModulatorEffectType.NewSawReturn: Damage("Returning saw damage", a); break;
                case ModulatorEffectType.NewSecondPulse: Damage("Second pulse damage", a); break;
                case ModulatorEffectType.NewTargetLockBias: Bonus("Locked-target steering", a); break;
                case ModulatorEffectType.NewOnCritChainProc:
                    Damage("Critical chain damage", a); Value("Critical chain targets", effect.Count);
                    Value("Critical chain range", b, " m"); Value("Critical chain recovery", c, " s"); break;
                case ModulatorEffectType.NewCapstoneProc:
                    Damage("Follow-up damage", a); Value("Follow-up every", effect.Count, " hits");
                    Value("Follow-up range", b, " m"); break;
                case ModulatorEffectType.EchoAfterDelay:
                    Damage("Repeat damage", b); Value("Repeat delay", a, " s"); Value("Repeats", effect.Count); break;
                case ModulatorEffectType.ChainToNearby:
                    Damage("Chain damage", a); Value("Chain targets", effect.Count); Value("Chain range", b, " m"); break;
                case ModulatorEffectType.LingeringZone:
                    Damage("Field damage", a); Value("Field radius", b, " m"); Value("Field duration", c, " s"); break;
                case ModulatorEffectType.ApplyMark:
                    Bonus("Damage to marked targets", a); Value("Mark duration", b, " s"); break;
                case ModulatorEffectType.ExecuteLowHealth:
                    Damage("Finisher below enemy health", a); Bonus("Finisher damage", b); Bonus("Boss finisher damage", c); break;
                case ModulatorEffectType.EliteBossHunter:
                    Bonus("Elite / boss damage", a); Bonus("Normal enemy damage", b); break;
                case ModulatorEffectType.NewWeaponTransform:
                    switch (WeaponModulatorData.NormalizeKey(effect.Key))
                    {
                        case "echo_weave": Damage("Mirror volley damage", a); Value("Mirror delay", b, " s"); break;
                        case "scorch_crater": Damage("Crater damage", a); Value("Crater duration", b, " s"); break;
                        case "closed_circuit": Damage("Return strike damage", a); Value("Return strikes", effect.Count); break;
                        case "primary_mimic": Damage("Copied attack damage", a); break;
                        case "burstline_converter":
                            int count = Math.Max(2, effect.Count);
                            Value("Burst rounds", count);
                            Damage("Second round damage", effect.QualityStrength + (a - effect.QualityStrength) / (count - 1));
                            Damage("Final round damage", a); Value("Burst spacing", b, " s"); break;
                        case "serration_bloom": Damage("Fragment damage", a); Value("Fragments", effect.Count); break;
                        case "prism_refraction": Damage("Side beam damage", b); Value("Side beam delay", a, " s"); break;
                        case "command_cluster": Damage("Cluster blast damage", a); Value("Cluster blasts", effect.Count); break;
                        case "collapse_pulse": Damage("Collapse damage", a * 1.35f); break;
                        case "aegis_carousel": Damage("Trail damage over 0.55 s", a * .2f); Value("Orbit reversal interval", b, " s"); break;
                        case "pack_hunters": Bonus("Marked-target damage", b); Value("Pack mark duration", a, " s"); break;
                        case "backdraft_corridor": Damage("Corridor damage", a); Value("Corridor duration", b, " s"); break;
                        case "phase_inversion": Value("Push / pull strength", b); Value("Pulse interval", a, " s"); break;
                        case "tunnel_bore": Damage("Wake damage", a); Value("Wake duration", b, " s"); break;
                        case "event_horizon_tear":
                            Damage("Implosion damage vs field tick", a); Damage("Fragment damage vs field tick", b); Value("Fragments", effect.Count); break;
                        case "blood_price_reactor":
                            Damage("Damage taken stored", a); Damage("Stored charge released", .3f); Value("Vent interval", b, " s"); break;
                    }
                    break;
            }
        }
        return stats;
    }

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Signed(float value) => (value >= 0 ? "+" : "") + Number(value);

    private static string Mechanics(WeaponModulatorData part)
    {
        string text = part.NormalizedKey switch
        {
            "volitile" => "Direct hits trigger a nearby explosion; copies and secondary effects cannot trigger it.",
            "echo_weave" => "Firing adds a delayed mirror volley without further repeats.",
            "scorch_crater" => "Mortar impacts leave a burning field.",
            "closed_circuit" => "The lightning chain strikes its original target again.",
            "primary_mimic" => "Clones copy the first arsenal shape's base attack using their host's stats.",
            "burstline_converter" => "Replaces the single shot with a burst; the first round keeps normal damage.",
            "serration_bloom" => "Exhausted sawblades split into fragments.",
            "prism_refraction" => "The beam adds delayed side beams.",
            "command_cluster" => "Mine detonations trigger additional cluster blasts.",
            "collapse_pulse" => "Nova adds a delayed inward collapse hit.",
            "aegis_carousel" => "Orbitals leave damage trails and periodically reverse direction.",
            "pack_hunters" => "Seekers mark targets; later seekers prioritize the mark.",
            "backdraft_corridor" => "The flame attack leaves burning fields along its path.",
            "phase_inversion" => "The rift alternates between pulling and pushing enemies.",
            "tunnel_bore" => "The drill leaves a damaging wake.",
            "event_horizon_tear" => "The void field ends with an implosion and outward fragments.",
            "blood_price_reactor" => "Stores part of damage taken and periodically releases a burst.",
            _ => "",
        };
        if (!string.IsNullOrEmpty(text)) return text;
        var effects = part.GetResolvedEffects();
        if (effects.Any(effect => effect.Type == ModulatorEffectType.NewSawReturn)) return "Sawblades return after exhausting their hits.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.NewSecondPulse)) return "Nova adds a delayed second pulse.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.NewTargetLockBias)) return "Seekers prefer their locked target.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.NewOnCritChainProc)) return "Critical hits arc to nearby enemies.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.NewCapstoneProc)) return "Successful hits build toward a spectral follow-up strike.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.EchoAfterDelay)) return "Firing repeats the attack after a delay.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.ChainToNearby)) return "Direct hits arc to nearby enemies.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.LingeringZone)) return "Direct hits leave a damaging field.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.ApplyMark)) return "Direct hits mark targets for amplified follow-up damage.";
        if (effects.Any(effect => effect.Type == ModulatorEffectType.ExecuteLowHealth)) return "Deals bonus damage to enemies near death.";
        return "";
    }
}
