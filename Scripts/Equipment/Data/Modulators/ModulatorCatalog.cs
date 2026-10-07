using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperGatchaRebuild.Equipment.Data;

public static partial class ModulatorCatalog
{
    public const string ResourceFolder = "res://Resources/Equipment/Modulators";

    private static readonly object CacheLock = new();

    private static List<WeaponModulatorData>? _cachedTemplates;
    private static Dictionary<string, WeaponModulatorData>? _cachedByKey;

    public static int Count
    {
        get
        {
            EnsureCache();
            return _cachedTemplates!.Count;
        }
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            _cachedTemplates = null;
            _cachedByKey = null;
        }
    }

    public static List<WeaponModulatorData> CreateAll()
    {
        EnsureCache();
        return CloneModulators(_cachedTemplates!);
    }

    public static List<WeaponModulatorData> CreateWeightedRollPool(Predicate<WeaponModulatorData>? filter = null)
    {
        EnsureCache();

        List<WeaponModulatorData> output = new();
        for (int i = 0; i < _cachedTemplates!.Count; i++)
        {
            WeaponModulatorData template = _cachedTemplates[i];
            if (template.Weight <= 0f)
            {
                continue;
            }

            if (filter != null && !filter(template))
            {
                continue;
            }

            output.Add(CloneModulator(template));
        }

        return output;
    }

    public static bool TryCreateByKey(string? key, out WeaponModulatorData modulator)
    {
        return TryCreateByKey(key, WeaponModulatorTier.Baseline, out modulator);
    }

    public static bool TryCreateByKey(string? key, WeaponModulatorTier tier, out WeaponModulatorData modulator)
    {
        EnsureCache();

        string normalizedKey = WeaponModulatorData.NormalizeKey(key);
        if (!string.IsNullOrWhiteSpace(normalizedKey) && _cachedByKey!.TryGetValue(normalizedKey, out WeaponModulatorData? template))
        {
            modulator = CreateTieredModulator(template, tier);
            return true;
        }

        modulator = _cachedTemplates!.Count > 0
            ? CreateTieredModulator(_cachedTemplates[0], WeaponModulatorTier.Baseline)
            : CreateFallbackModulator(normalizedKey);
        return false;
    }

    public static WeaponModulatorData CreateByKey(string? key)
    {
        return CreateByKey(key, WeaponModulatorTier.Baseline);
    }

    public static WeaponModulatorData CreateByKey(string? key, WeaponModulatorTier tier)
    {
        return TryCreateByKey(key, tier, out WeaponModulatorData modulator)
            ? modulator
            : modulator;
    }

    private static void EnsureCache()
    {
        if (_cachedTemplates != null && _cachedByKey != null)
        {
            return;
        }

        lock (CacheLock)
        {
            if (_cachedTemplates != null && _cachedByKey != null)
            {
                return;
            }

            List<WeaponModulatorData> templates = LoadResourceTemplates();
            if (templates.Count == 0)
            {
                templates = CreateCodeFallbackTemplates();
            }

            templates.Sort(static (left, right) => string.Compare(left.BaseDisplayName, right.BaseDisplayName, StringComparison.OrdinalIgnoreCase));

            Dictionary<string, WeaponModulatorData> byKey = new(StringComparer.OrdinalIgnoreCase);
            List<WeaponModulatorData> deduped = new(templates.Count);
            for (int i = 0; i < templates.Count; i++)
            {
                WeaponModulatorData template = templates[i];
                string normalizedKey = template.NormalizedKey;
                if (string.IsNullOrWhiteSpace(normalizedKey))
                {
                    continue;
                }

                if (byKey.ContainsKey(normalizedKey))
                {
                    GD.PushWarning($"Duplicate modulator key '{normalizedKey}' skipped.");
                    continue;
                }

                WeaponModulatorData clone = CloneModulator(template);
                byKey[normalizedKey] = clone;
                deduped.Add(clone);
            }

            _cachedTemplates = deduped;
            _cachedByKey = byKey;
        }
    }

    private static List<WeaponModulatorData> LoadResourceTemplates()
    {
        List<WeaponModulatorData> output = new();
        if (!DirAccess.DirExistsAbsolute(ResourceFolder))
        {
            return output;
        }

        DirAccess? dir = DirAccess.Open(ResourceFolder);
        if (dir == null)
        {
            return output;
        }

        List<string> resourceFiles = new();
        foreach (string file in dir.GetFiles())
        {
            if (file.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
            {
                resourceFiles.Add(file);
            }
        }

        resourceFiles.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < resourceFiles.Count; i++)
        {
            string path = $"{ResourceFolder}/{resourceFiles[i]}";
            WeaponModulatorDefinition? definition = ResourceLoader.Load(path) as WeaponModulatorDefinition;
            if (definition == null)
            {
                continue;
            }

            output.Add(definition.ToData());
        }

        return output;
    }

    private static List<WeaponModulatorData> CreateCodeFallbackTemplates()
    {
        List<WeaponModulatorData> output = new();
        AddTier1Templates(output);
        AddRemainingTier1ReadyTemplates(output);
        AddCoreTemplates(output);
        AddProjectileTemplates(output);
        AddSustainTemplates(output);
        AddUtilityTemplates(output);
        AddTier2Templates(output);
        AddRemainingTier2ReadyTemplates(output);
        AddProcTemplates(output);
        AddRemainingTier3ReadyTemplates(output);
        AddAdvancedTemplates(output);
        AddWingTemplates(output);
        return output;
    }

    private static void AddRemainingTier1ReadyTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTier1EffectTemplate(
            "vital_reservoir",
            "Vital Reservoir",
            "Simple survivability anchor for any build.",
            WeaponModulatorCategory.Sustain,
            7f,
            new Color(0.4510f, 0.8196f, 0.7725f),
            Tags("health", "survivability", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HealthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.12f,
                },
            }));

        output.Add(CreateTier1EffectTemplate(
            "recovery_circuit",
            "Recovery Circuit",
            "Flat regen bump to smooth attrition in long fights.",
            WeaponModulatorCategory.Sustain,
            6f,
            new Color(0.5961f, 0.8588f, 0.9882f),
            Tags("regen", "sustain", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HealthRegenAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.2f,
                },
            }));

        output.Add(CreateTier1EffectTemplate(
            "evasion_mesh",
            "Evasion Mesh",
            "Readable defensive stat that is not damage focused.",
            WeaponModulatorCategory.Sustain,
            6f,
            new Color(0.7255f, 0.9176f, 1.0000f),
            Tags("dodge", "defense", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DodgeChanceAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 6f,
                },
            }));

        output.Add(CreateTier1EffectTemplate(
            "stride_servo",
            "Stride Servo",
            "Mobility foundation modulator for reposition-heavy play.",
            WeaponModulatorCategory.Utility,
            6f,
            new Color(0.5294f, 0.9804f, 0.6549f),
            Tags("movement", "mobility", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.MovementSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.08f,
                },
            }));

        output.Add(CreateTier1EffectTemplate(
            "ballistic_tuning",
            "Ballistic Tuning",
            "Clean projectile delivery boost for precision lanes.",
            WeaponModulatorCategory.Projectile,
            6f,
            new Color(0.4784f, 0.7529f, 1.0000f),
            Tags("projectile_speed", "precision", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.15f,
                },
            },
            allowedWeaponTags: Tags("square", "triangle", "hypercone")));

        output.Add(CreateTier1EffectTemplate(
            "extended_uptime",
            "Extended Uptime",
            "Longer field and summon presence without adding mechanics.",
            WeaponModulatorCategory.Utility,
            6f,
            new Color(0.6863f, 0.8627f, 1.0000f),
            Tags("duration", "zone", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.20f,
                },
            },
            allowedWeaponTags: Tags("mine", "orbital", "zone", "clone")));

        output.Add(CreateTier1EffectTemplate(
            "fuse_tuner",
            "Fuse Tuner",
            "Mine cadence bump for setup-focused builds.",
            WeaponModulatorCategory.Projectile,
            5f,
            new Color(0.9804f, 0.7098f, 0.2745f),
            Tags("cooldown", "mine", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.92f,
                },
            },
            allowedWeaponTags: Tags("hexagon", "mine")));

        output.Add(CreateTier1EffectTemplate(
            "orbital_bracing",
            "Orbital Bracing",
            "Improves orbital contact consistency through reach.",
            WeaponModulatorCategory.Projectile,
            5f,
            new Color(0.5412f, 0.8784f, 0.9765f),
            Tags("range", "orbital", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.RangeMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.12f,
                },
            },
            allowedWeaponTags: Tags("cube", "orbital")));

        output.Add(CreateTier1EffectTemplate(
            "guidance_spine",
            "Guidance Spine",
            "Simple homing correction improvement for seeker weapons.",
            WeaponModulatorCategory.Utility,
            5f,
            new Color(0.6510f, 0.8549f, 1.0000f),
            Tags("homing", "guidance", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HomingStrengthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.20f,
                },
            },
            allowedWeaponTags: Tags("pyramid", "homing")));

        output.Add(CreateTier1EffectTemplate(
            "stability_plating",
            "Stability Plating",
            "Crowd-control utility via stronger knockback pressure.",
            WeaponModulatorCategory.Utility,
            4f,
            new Color(0.5608f, 0.8667f, 0.9608f),
            Tags("knockback", "control", "tier1", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.KnockbackMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.20f,
                },
            }));
    }

    private static void AddRemainingTier2ReadyTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTier2EffectTemplate(
            "glass_hammer_core",
            "Glass Hammer Core",
            "High damage with intentional fragility tradeoff.",
            WeaponModulatorCategory.Core,
            1.4f,
            new Color(1.0000f, 0.5333f, 0.4196f),
            Tags("damage", "health", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.24f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HealthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.88f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "rapid_geometry",
            "Rapid Geometry",
            "Faster cycle in exchange for narrower impact footprint.",
            WeaponModulatorCategory.Core,
            1.4f,
            new Color(0.5529f, 0.9451f, 0.9765f),
            Tags("cooldown", "area", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.82f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.88f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "deadeye_tension",
            "Deadeye Tension",
            "Crit-focused accuracy package with area loss.",
            WeaponModulatorCategory.Core,
            1.3f,
            new Color(0.9686f, 0.8745f, 0.4627f),
            Tags("crit", "area", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CritChanceAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 10f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.90f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "siege_footing",
            "Siege Footing",
            "Bigger hit zones while slightly reducing mobility.",
            WeaponModulatorCategory.Sustain,
            1.2f,
            new Color(0.7255f, 0.8784f, 1.0000f),
            Tags("area", "movement", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.20f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.MovementSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.92f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "longshot_pressure",
            "Longshot Pressure",
            "Reach-focused package with slower cadence.",
            WeaponModulatorCategory.Projectile,
            1.3f,
            new Color(0.5529f, 0.8902f, 0.9647f),
            Tags("range", "cooldown", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.RangeMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.22f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.10f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "predator_rhythm",
            "Predator Rhythm",
            "Harder crits at the cost of tempo.",
            WeaponModulatorCategory.Core,
            1.2f,
            new Color(0.9804f, 0.7608f, 0.2980f),
            Tags("crit_damage", "cooldown", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CritDamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.30f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.08f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "vampiric_engine",
            "Vampiric Engine",
            "Sustain conversion with lower base hit strength.",
            WeaponModulatorCategory.Sustain,
            1.2f,
            new Color(0.9765f, 0.5216f, 0.6706f),
            Tags("lifesteal", "damage", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.LifeStealAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 3f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.90f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "overburn_fins",
            "Overburn Fins",
            "Fast delivery profile that trims per-hit force.",
            WeaponModulatorCategory.Projectile,
            1.3f,
            new Color(0.5686f, 0.8431f, 1.0000f),
            Tags("projectile_speed", "damage", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.25f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.86f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "bulwark_drive",
            "Bulwark Drive",
            "Durability package with slower attack loop.",
            WeaponModulatorCategory.Sustain,
            1.2f,
            new Color(0.5137f, 0.8392f, 0.7804f),
            Tags("health", "cooldown", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HealthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.18f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.08f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "reckless_focus",
            "Reckless Focus",
            "Aggressive crit profile with meaningful survivability loss.",
            WeaponModulatorCategory.Core,
            1.2f,
            new Color(0.9882f, 0.7098f, 0.4000f),
            Tags("crit", "health", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CritChanceAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 12f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HealthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.86f,
                },
            }));

        output.Add(CreateTier2EffectTemplate(
            "precision_rail",
            "Precision Rail",
            "Lane pressure package for precision weapons.",
            WeaponModulatorCategory.Projectile,
            1.2f,
            new Color(0.4471f, 0.8078f, 0.9765f),
            Tags("range", "projectile_speed", "cooldown", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.RangeMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.18f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.18f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.06f,
                },
            },
            allowedWeaponTags: Tags("precision", "square", "triangle", "hypercone")));

        output.Add(CreateTier2EffectTemplate(
            "saw_storm",
            "Saw Storm",
            "More saw presence with reduced per-hit value.",
            WeaponModulatorCategory.Projectile,
            1.2f,
            new Color(0.5608f, 0.9765f, 0.7608f),
            Tags("projectile_count", "damage", "sawblade", "tier2", "remaining"),
            new[]
            {
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
                    Magnitude = 0.82f,
                },
            },
            allowedWeaponTags: Tags("circle", "sawblade")));

        output.Add(CreateTier2EffectTemplate(
            "beam_condenser",
            "Beam Condenser",
            "Tighter beam profile with stronger line damage.",
            WeaponModulatorCategory.Projectile,
            1.1f,
            new Color(0.4784f, 0.8627f, 1.0000f),
            Tags("damage", "area", "beam", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.22f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.82f,
                },
            },
            allowedWeaponTags: Tags("triangle", "laser")));

        output.Add(CreateTier2EffectTemplate(
            "mine_relay",
            "Mine Relay",
            "Rapid deployment with shorter mine persistence.",
            WeaponModulatorCategory.Projectile,
            1.1f,
            new Color(0.9765f, 0.6902f, 0.3137f),
            Tags("cooldown", "duration", "mine", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.78f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.82f,
                },
            },
            allowedWeaponTags: Tags("hexagon", "mine")));

        output.Add(CreateTier2EffectTemplate(
            "nova_shear",
            "Nova Shear",
            "Bigger nova with slower pulse cycle.",
            WeaponModulatorCategory.Projectile,
            1.1f,
            new Color(0.9765f, 0.5765f, 0.5216f),
            Tags("area", "cooldown", "nova", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.30f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.14f,
                },
            },
            allowedWeaponTags: Tags("sphere", "nova")));

        output.Add(CreateTier2EffectTemplate(
            "hunter_saturation",
            "Hunter Saturation",
            "Extra seeker output with lower hit strength.",
            WeaponModulatorCategory.Projectile,
            1.1f,
            new Color(0.6157f, 0.8314f, 1.0000f),
            Tags("projectile_count", "damage", "homing", "tier2", "remaining"),
            new[]
            {
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
                    Magnitude = 0.80f,
                },
            },
            allowedWeaponTags: Tags("pyramid", "homing")));

        output.Add(CreateTier2EffectTemplate(
            "flame_jet",
            "Flame Jet",
            "Longer cone reach with reduced linger time.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(1.0000f, 0.6471f, 0.3137f),
            Tags("range", "duration", "flame", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.RangeMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.22f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.85f,
                },
            },
            allowedWeaponTags: Tags("cone", "flame")));

        output.Add(CreateTier2EffectTemplate(
            "rift_stabilizer",
            "Rift Stabilizer",
            "Longer rift uptime with reduced tick damage.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(0.5216f, 0.7569f, 0.9922f),
            Tags("duration", "damage", "rift", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.30f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.88f,
                },
            },
            allowedWeaponTags: Tags("tesseract", "rift", "zone")));

        output.Add(CreateTier2EffectTemplate(
            "mortar_barrage",
            "Mortar Barrage",
            "Extra shell output with lower shell impact.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(0.8745f, 0.7765f, 1.0000f),
            Tags("projectile_count", "damage", "mortar", "tier2", "remaining"),
            new[]
            {
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
                    Magnitude = 0.78f,
                },
            },
            allowedWeaponTags: Tags("hyperphere", "mortar")));

        output.Add(CreateTier2EffectTemplate(
            "drill_spool",
            "Drill Spool",
            "Faster drill travel with shorter active time.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(0.6510f, 0.9098f, 1.0000f),
            Tags("projectile_speed", "duration", "drill", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.24f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.84f,
                },
            },
            allowedWeaponTags: Tags("hypercone", "drill")));

        output.Add(CreateTier2EffectTemplate(
            "wrath_furnace",
            "Wrath Furnace",
            "Harder aura ticks with lower survivability.",
            WeaponModulatorCategory.Sustain,
            1.0f,
            new Color(0.9804f, 0.4902f, 0.4510f),
            Tags("damage", "health", "wrath_aura", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.24f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HealthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.88f,
                },
            },
            allowedWeaponTags: Tags("klein", "wrath_aura", "aura")));

        output.Add(CreateTier2EffectTemplate(
            "needle_shot",
            "Needle Shot",
            "Square specializes into long-range puncture fire.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.4471f, 0.8039f, 0.9882f),
            Tags("damage", "range", "cooldown", "square", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.28f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.RangeMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.25f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.12f,
                },
            },
            allowedWeaponTags: Tags("square")));

        output.Add(CreateTier2EffectTemplate(
            "line_driver",
            "Line Driver",
            "Beam becomes thinner and significantly stronger.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.5294f, 0.9059f, 1.0000f),
            Tags("damage", "area", "beam", "triangle", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.35f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.75f,
                },
            },
            allowedWeaponTags: Tags("triangle")));

        output.Add(CreateTier2EffectTemplate(
            "quick_plant",
            "Quick Plant",
            "Mines deploy rapidly but expire rapidly.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.9922f, 0.6784f, 0.2863f),
            Tags("cooldown", "duration", "mine", "hexagon", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.75f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.75f,
                },
            },
            allowedWeaponTags: Tags("hexagon")));

        output.Add(CreateTier2EffectTemplate(
            "high_arc_bombard",
            "High Arc Bombard",
            "Mortar shells travel faster but with tighter blast radius.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.8392f, 0.7725f, 1.0000f),
            Tags("projectile_speed", "area", "mortar", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileSpeedMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.28f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.88f,
                },
            },
            allowedWeaponTags: Tags("hyperphere")));

        output.Add(CreateTier2EffectTemplate(
            "orbital_compression",
            "Orbital Compression",
            "Extra orbital at tighter radius for high contact risk.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(0.5333f, 0.8980f, 0.9569f),
            Tags("orbital", "projectile_count", "radius", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileCountAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Count = 1,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewOrbitRadiusScalar,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.78f,
                },
            },
            allowedWeaponTags: Tags("cube", "orbital")));

        output.Add(CreateTier2EffectTemplate(
            "chain_capacitor",
            "Chain Capacitor",
            "Longer chain routes with lower arc damage.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(0.4824f, 0.7608f, 1.0000f),
            Tags("chain", "damage", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ChainCountAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Count = 2,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.86f,
                },
            },
            allowedWeaponTags: Tags("pentachoron", "chainlightning")));

        output.Add(CreateTier2EffectTemplate(
            "void_compression",
            "Void Compression",
            "Stronger pull with reduced effect radius.",
            WeaponModulatorCategory.Projectile,
            1.0f,
            new Color(0.5294f, 0.6784f, 0.9804f),
            Tags("void", "pull", "area", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.PullStrengthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.35f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.AreaMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.86f,
                },
            },
            allowedWeaponTags: Tags("impossiblecube", "void", "pull")));

        output.Add(CreateTier2EffectTemplate(
            "echo_focus",
            "Echo Focus",
            "Heavier echoes with slower trigger cadence.",
            WeaponModulatorCategory.Utility,
            1.0f,
            new Color(0.6902f, 0.7608f, 1.0000f),
            Tags("echo", "cooldown", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CloneOrEchoPowerMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.28f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.10f,
                },
            },
            allowedWeaponTags: Tags("mobius", "temporal_echo", "echo")));

        output.Add(CreateTier2EffectTemplate(
            "clone_relay",
            "Clone Relay",
            "More clone contribution with shorter clone life.",
            WeaponModulatorCategory.Utility,
            1.0f,
            new Color(0.6902f, 0.8706f, 0.9843f),
            Tags("clone", "duration", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CloneOrEchoPowerMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.24f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DurationMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.82f,
                },
            },
            allowedWeaponTags: Tags("boys", "shadow_clone", "clone")));

        output.Add(CreateTier2EffectTemplate(
            "return_arc",
            "Return Arc",
            "Sawblade returns once after max travel for extra lane control.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.6000f, 0.9804f, 0.7608f),
            Tags("sawblade", "return", "damage", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewSawReturn,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Count = 1,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.90f,
                },
            },
            allowedWeaponTags: Tags("circle", "sawblade")));

        output.Add(CreateTier2EffectTemplate(
            "double_pulse",
            "Double Pulse",
            "Nova emits a weaker return pulse after expansion.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.9961f, 0.6275f, 0.5608f),
            Tags("nova", "pulse", "cooldown", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewSecondPulse,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.60f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.CooldownMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.15f,
                },
            },
            allowedWeaponTags: Tags("sphere", "nova", "pulse")));

        output.Add(CreateTier2EffectTemplate(
            "tight_orbit",
            "Tight Orbit",
            "Adds one orbital and shrinks orbit radius substantially.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.5412f, 0.8941f, 0.9804f),
            Tags("orbital", "projectile_count", "radius", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.ProjectileCountAdd,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Count = 1,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewOrbitRadiusScalar,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.72f,
                },
            },
            allowedWeaponTags: Tags("cube", "orbital")));

        output.Add(CreateTier2EffectTemplate(
            "lock_burst",
            "Lock Burst",
            "Seekers aggressively stack on a locked target with lower base damage.",
            WeaponModulatorCategory.Projectile,
            0.9f,
            new Color(0.6314f, 0.8235f, 1.0000f),
            Tags("homing", "lock", "damage", "tradeoff", "tier2", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.HomingStrengthMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.20f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewTargetLockBias,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.40f,
                },
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.DamageMultiplier,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.86f,
                },
            },
            allowedWeaponTags: Tags("pyramid", "homing")));
    }

    private static void AddRemainingTier3ReadyTemplates(List<WeaponModulatorData> output)
    {
        output.Add(CreateTier3EffectTemplate(
            "critical_cascade",
            "Critical Cascade",
            "Critical hits trigger a short micro-chain to one nearby enemy.",
            WeaponModulatorCategory.Proc,
            0.7f,
            new Color(0.5725f, 0.8078f, 1.0000f),
            Tags("crit", "chain", "proc", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewOnCritChainProc,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.35f,
                    MagnitudeB = 10f,
                    MagnitudeC = 0.35f,
                    Count = 1,
                },
            }));

        output.Add(CreateTier3EffectTemplate(
            "echo_weave",
            "Echo Weave",
            "Temporal Echo fires a delayed mirror volley with no recursion.",
            WeaponModulatorCategory.Proc,
            0.6f,
            new Color(0.7098f, 0.7804f, 1.0000f),
            Tags("echo", "mirror", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.40f,
                    MagnitudeB = 0.60f,
                    Key = "echo_weave",
                },
            },
            allowedWeaponTags: Tags("mobius", "temporal_echo", "echo")));

        output.Add(CreateTier3EffectTemplate(
            "scorch_crater",
            "Scorch Crater",
            "Mortar leaves a burning impact zone after explosion.",
            WeaponModulatorCategory.Proc,
            0.6f,
            new Color(1.0000f, 0.6039f, 0.3412f),
            Tags("mortar", "zone", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.30f,
                    MagnitudeB = 3.50f,
                    Key = "scorch_crater",
                },
            },
            allowedWeaponTags: Tags("hyperphere", "mortar")));

        output.Add(CreateTier3EffectTemplate(
            "closed_circuit",
            "Closed Circuit",
            "Chain lightning can re-strike the original target once.",
            WeaponModulatorCategory.Proc,
            0.6f,
            new Color(0.4471f, 0.7804f, 1.0000f),
            Tags("chain", "return", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.50f,
                    Count = 1,
                    Key = "closed_circuit",
                },
            },
            allowedWeaponTags: Tags("pentachoron", "chainlightning", "chain")));

        output.Add(CreateTier3EffectTemplate(
            "primary_mimic",
            "Primary Mimic",
            "Shadow Clones copy the primary-slot weapon at reduced strength.",
            WeaponModulatorCategory.Proc,
            0.6f,
            new Color(0.5608f, 0.8902f, 0.9804f),
            Tags("clone", "mimic", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.35f,
                    Key = "primary_mimic",
                },
            },
            allowedWeaponTags: Tags("boys", "shadow_clone", "clone")));

        output.Add(CreateTier3EffectTemplate(
            "burstline_converter",
            "Burstline Converter",
            "Square single shot converts into a controlled 3-round burst.",
            WeaponModulatorCategory.Proc,
            0.55f,
            new Color(0.4510f, 0.8000f, 0.9961f),
            Tags("square", "burst", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.45f,
                    Count = 3,
                    MagnitudeB = 0.18f,
                    Key = "burstline_converter",
                },
            },
            allowedWeaponTags: Tags("square", "precision")));

        output.Add(CreateTier3EffectTemplate(
            "serration_bloom",
            "Serration Bloom",
            "Sawblades split after reaching max pierce.",
            WeaponModulatorCategory.Proc,
            0.55f,
            new Color(0.6000f, 0.9843f, 0.7804f),
            Tags("sawblade", "split", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.55f,
                    Count = 2,
                    Key = "serration_bloom",
                },
            },
            allowedWeaponTags: Tags("circle", "sawblade")));

        output.Add(CreateTier3EffectTemplate(
            "prism_refraction",
            "Prism Refraction",
            "Beam forks into side beams after sustained contact.",
            WeaponModulatorCategory.Proc,
            0.50f,
            new Color(0.5412f, 0.8824f, 1.0000f),
            Tags("beam", "fork", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.25f,
                    MagnitudeB = 0.40f,
                    Key = "prism_refraction",
                },
            },
            allowedWeaponTags: Tags("triangle", "laser", "beam")));

        output.Add(CreateTier3EffectTemplate(
            "command_cluster",
            "Command Cluster",
            "Mines become remote-detonated clusters.",
            WeaponModulatorCategory.Proc,
            0.55f,
            new Color(0.9804f, 0.6745f, 0.2980f),
            Tags("mine", "cluster", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.35f,
                    Count = 3,
                    Key = "command_cluster",
                },
            },
            allowedWeaponTags: Tags("hexagon", "mine")));

        output.Add(CreateTier3EffectTemplate(
            "collapse_pulse",
            "Collapse Pulse",
            "Nova expands then collapses inward for a second hit window.",
            WeaponModulatorCategory.Proc,
            0.55f,
            new Color(0.9843f, 0.6157f, 0.5529f),
            Tags("nova", "return", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.65f,
                    Key = "collapse_pulse",
                },
            },
            allowedWeaponTags: Tags("sphere", "nova")));

        output.Add(CreateTier3EffectTemplate(
            "aegis_carousel",
            "Aegis Carousel",
            "Orbitals leave short damage trails and reverse orbit periodically.",
            WeaponModulatorCategory.Proc,
            0.45f,
            new Color(0.5725f, 0.8980f, 0.9843f),
            Tags("orbital", "trail", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.25f,
                    MagnitudeB = 2f,
                    Key = "aegis_carousel",
                },
            },
            allowedWeaponTags: Tags("cube", "orbital")));

        output.Add(CreateTier3EffectTemplate(
            "pack_hunters",
            "Pack Hunters",
            "First seeker marks target; follow-up seekers prioritize the mark.",
            WeaponModulatorCategory.Proc,
            0.55f,
            new Color(0.6392f, 0.8471f, 1.0000f),
            Tags("homing", "mark", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 3.0f,
                    MagnitudeB = 1.30f,
                    Key = "pack_hunters",
                },
            },
            allowedWeaponTags: Tags("pyramid", "homing")));

        output.Add(CreateTier3EffectTemplate(
            "backdraft_corridor",
            "Backdraft Corridor",
            "Flame cone leaves a burning corridor behind enemy line.",
            WeaponModulatorCategory.Proc,
            0.50f,
            new Color(1.0000f, 0.6471f, 0.3137f),
            Tags("flame", "corridor", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.32f,
                    MagnitudeB = 2.50f,
                    Key = "backdraft_corridor",
                },
            },
            allowedWeaponTags: Tags("cone", "flame")));

        output.Add(CreateTier3EffectTemplate(
            "phase_inversion",
            "Phase Inversion",
            "Rift alternates pull and push pulses during lifetime.",
            WeaponModulatorCategory.Proc,
            0.50f,
            new Color(0.5686f, 0.7412f, 0.9961f),
            Tags("rift", "push_pull", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.60f,
                    Key = "phase_inversion",
                },
            },
            allowedWeaponTags: Tags("tesseract", "rift", "zone")));

        output.Add(CreateTier3EffectTemplate(
            "tunnel_bore",
            "Tunnel Bore",
            "Drill leaves a damaging tunnel wake through traveled space.",
            WeaponModulatorCategory.Proc,
            0.45f,
            new Color(0.6510f, 0.9137f, 1.0000f),
            Tags("drill", "wake", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.28f,
                    MagnitudeB = 1.80f,
                    Key = "tunnel_bore",
                },
            },
            allowedWeaponTags: Tags("hypercone", "drill")));

        output.Add(CreateTier3EffectTemplate(
            "event_horizon_tear",
            "Event Horizon Tear",
            "Void singularity ends with implosion then fragment eruption.",
            WeaponModulatorCategory.Proc,
            0.45f,
            new Color(0.5059f, 0.6980f, 0.9922f),
            Tags("void", "implosion", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.60f,
                    MagnitudeB = 0.30f,
                    Count = 6,
                    Key = "event_horizon_tear",
                },
            },
            allowedWeaponTags: Tags("impossiblecube", "void", "pull")));

        output.Add(CreateTier3EffectTemplate(
            "blood_price_reactor",
            "Blood Price Reactor",
            "Wrath Aura stores taken damage and releases burst pulses.",
            WeaponModulatorCategory.Proc,
            0.45f,
            new Color(0.9882f, 0.4863f, 0.4510f),
            Tags("wrath_aura", "reactor", "transform", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewWeaponTransform,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 0.30f,
                    MagnitudeB = 1.50f,
                    Key = "blood_price_reactor",
                },
            },
            allowedWeaponTags: Tags("klein", "wrath_aura", "aura")));

        output.Add(CreateTier3EffectTemplate(
            "apex_doctrine",
            "Apex Doctrine",
            "Single-weapon commitment capstone that amplifies non-Tier3 modulators.",
            WeaponModulatorCategory.Proc,
            0.35f,
            new Color(0.9882f, 0.7922f, 0.4549f),
            Tags("capstone", "amplify", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewCapstoneRule,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.35f,
                    Key = "apex_doctrine",
                },
            }));

        output.Add(CreateTier3EffectTemplate(
            "omni_feedback",
            "Omni Feedback",
            "Every 8th successful hit emits a universal spectral follow-up strike.",
            WeaponModulatorCategory.Proc,
            0.35f,
            new Color(0.5137f, 0.8000f, 0.9961f),
            Tags("capstone", "followup", "tier3", "remaining"),
            new[]
            {
                new WeaponModulatorEffectData
                {
                    Type = ModulatorEffectType.NewCapstoneProc,
                    Trigger = ModulatorEffectTrigger.Passive,
                    Magnitude = 1.00f,
                    MagnitudeB = 12f,
                    Count = 8,
                    Key = "omni_feedback",
                },
            }));
    }

    private static WeaponModulatorData CreateTemplate(
        string key,
        string displayName,
        string description,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        params SubstatRoll[] substats)
    {
        List<SubstatRoll> rolls = new(substats.Length);
        for (int i = 0; i < substats.Length; i++)
        {
            rolls.Add(new SubstatRoll(substats[i].Type, substats[i].Value));
        }

        List<string> resolvedTags = new();
        if (tags != null)
        {
            foreach (string tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    resolvedTags.Add(tag);
                }
            }
        }

        WeaponModulatorData modulator = new()
        {
            Key = key,
            BaseDisplayName = displayName,
            DisplayName = displayName,
            BaseDescription = description,
            Description = description,
            PowerTier = ModulatorPowerTier.Tier1,
            Category = category,
            Tags = resolvedTags,
            AllowedWeaponTags = new List<string>(),
            BlockedWeaponTags = new List<string>(),
            Weight = weight,
            UiColor = uiColor,
            Substats = rolls,
            Effects = WeaponModulatorEffectData.FromSubstats(rolls),
            Tier = WeaponModulatorTier.Baseline,
        };

        modulator.RefreshPresentation();
        return modulator;
    }

    private static WeaponModulatorData CreateEffectTemplate(
        string key,
        string displayName,
        string description,
        ModulatorPowerTier powerTier,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        IEnumerable<WeaponModulatorEffectData> effects,
        IEnumerable<string>? allowedWeaponTags = null,
        IEnumerable<string>? blockedWeaponTags = null,
        bool supportsTier = false)
    {
        List<string> resolvedTags = NormalizeTagList(tags);
        List<string> resolvedAllowedWeaponTags = NormalizeTagList(allowedWeaponTags);
        List<string> resolvedBlockedWeaponTags = NormalizeTagList(blockedWeaponTags);

        List<WeaponModulatorEffectData> resolvedEffects = effects
            .Where(static effect => effect != null)
            .Select(CloneEffect)
            .ToList();

        WeaponModulatorData modulator = new()
        {
            Key = key,
            BaseDisplayName = displayName,
            DisplayName = displayName,
            BaseDescription = description,
            Description = description,
            PowerTier = powerTier,
            Category = category,
            Tags = resolvedTags,
            AllowedWeaponTags = resolvedAllowedWeaponTags,
            BlockedWeaponTags = resolvedBlockedWeaponTags,
            SupportsTier = supportsTier,
            Weight = weight,
            UiColor = uiColor,
            Substats = new List<SubstatRoll>(),
            Effects = resolvedEffects,
            Tier = WeaponModulatorTier.Baseline,
        };

        modulator.RefreshPresentation();
        return modulator;
    }

    private static WeaponModulatorData CreateTier1EffectTemplate(
        string key,
        string displayName,
        string description,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        IEnumerable<WeaponModulatorEffectData> effects,
        IEnumerable<string>? allowedWeaponTags = null,
        IEnumerable<string>? blockedWeaponTags = null)
    {
        return CreateEffectTemplate(
            key,
            displayName,
            description,
            ModulatorPowerTier.Tier1,
            category,
            weight,
            uiColor,
            tags,
            effects,
            allowedWeaponTags,
            blockedWeaponTags,
            false);
    }

    private static WeaponModulatorData CreateTier2EffectTemplate(
        string key,
        string displayName,
        string description,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        IEnumerable<WeaponModulatorEffectData> effects,
        IEnumerable<string>? allowedWeaponTags = null,
        IEnumerable<string>? blockedWeaponTags = null)
    {
        return CreateEffectTemplate(
            key,
            displayName,
            description,
            ModulatorPowerTier.Tier2,
            category,
            weight,
            uiColor,
            tags,
            effects,
            allowedWeaponTags,
            blockedWeaponTags,
            false);
    }

    private static WeaponModulatorData CreateTier3EffectTemplate(
        string key,
        string displayName,
        string description,
        WeaponModulatorCategory category,
        float weight,
        Color uiColor,
        IEnumerable<string>? tags,
        IEnumerable<WeaponModulatorEffectData> effects,
        IEnumerable<string>? allowedWeaponTags = null,
        IEnumerable<string>? blockedWeaponTags = null,
        bool supportsTier = false)
    {
        return CreateEffectTemplate(
            key,
            displayName,
            description,
            ModulatorPowerTier.Tier3,
            category,
            weight,
            uiColor,
            tags,
            effects,
            allowedWeaponTags,
            blockedWeaponTags,
            supportsTier);
    }

    private static List<string> NormalizeTagList(IEnumerable<string>? tags)
    {
        List<string> resolved = new();
        if (tags == null)
        {
            return resolved;
        }

        foreach (string tag in tags)
        {
            string normalized = WeaponModulatorData.NormalizeTag(tag);
            if (!string.IsNullOrWhiteSpace(normalized) && !resolved.Contains(normalized, StringComparer.Ordinal))
            {
                resolved.Add(normalized);
            }
        }

        return resolved;
    }

    private static WeaponModulatorEffectData CloneEffect(WeaponModulatorEffectData effect)
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

    private static string[] Tags(params string[] tags)
    {
        return tags;
    }

    private static List<WeaponModulatorData> CloneModulators(IReadOnlyList<WeaponModulatorData> source)
    {
        List<WeaponModulatorData> output = new(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            output.Add(CloneModulator(source[i]));
        }

        return output;
    }

    private static WeaponModulatorData CloneModulator(WeaponModulatorData src)
    {
        List<SubstatRoll> rolls = new(src.Substats.Count);
        for (int i = 0; i < src.Substats.Count; i++)
        {
            SubstatRoll roll = src.Substats[i];
            rolls.Add(new SubstatRoll(roll.Type, roll.Value));
        }

        List<string> tags = new(src.Tags.Count);
        for (int i = 0; i < src.Tags.Count; i++)
        {
            tags.Add(src.Tags[i]);
        }

        List<string> allowedWeaponTags = new(src.AllowedWeaponTags.Count);
        for (int i = 0; i < src.AllowedWeaponTags.Count; i++)
        {
            allowedWeaponTags.Add(src.AllowedWeaponTags[i]);
        }

        List<string> blockedWeaponTags = new(src.BlockedWeaponTags.Count);
        for (int i = 0; i < src.BlockedWeaponTags.Count; i++)
        {
            blockedWeaponTags.Add(src.BlockedWeaponTags[i]);
        }

        List<WeaponModulatorEffectData> effects = new(src.Effects.Count);
        for (int i = 0; i < src.Effects.Count; i++)
        {
            WeaponModulatorEffectData effect = src.Effects[i];
            effects.Add(new WeaponModulatorEffectData
            {
                Type = effect.Type,
                Trigger = effect.Trigger,
                Magnitude = effect.Magnitude,
                MagnitudeB = effect.MagnitudeB,
                MagnitudeC = effect.MagnitudeC,
                Count = effect.Count,
                CountB = effect.CountB,
                Key = effect.Key,
            });
        }

        WeaponModulatorData modulator = new()
        {
            Key = src.Key,
            BaseDisplayName = string.IsNullOrWhiteSpace(src.BaseDisplayName) ? src.DisplayName : src.BaseDisplayName,
            DisplayName = src.DisplayName,
            BaseDescription = string.IsNullOrWhiteSpace(src.BaseDescription) ? src.Description : src.BaseDescription,
            Description = src.Description,
            PowerTier = src.PowerTier,
            Category = src.Category,
            Tags = tags,
            AllowedWeaponTags = allowedWeaponTags,
            BlockedWeaponTags = blockedWeaponTags,
            SupportsTier = src.SupportsTier,
            Weight = src.Weight,
            UiColor = src.UiColor,
            Substats = rolls,
            Effects = effects,
            Tier = src.Tier,
        };

        modulator.RefreshPresentation();
        return modulator;
    }

    private static WeaponModulatorData CreateTieredModulator(WeaponModulatorData src, WeaponModulatorTier tier)
    {
        WeaponModulatorData modulator = CloneModulator(src);
        modulator.Tier = modulator.UsesTierPresentation ? tier : WeaponModulatorTier.Baseline;
        modulator.RefreshPresentation();
        return modulator;
    }

    private static WeaponModulatorData CreateFallbackModulator(string? key)
    {
        WeaponModulatorData modulator = new()
        {
            Key = string.IsNullOrWhiteSpace(key) ? "modulator" : key,
            BaseDisplayName = string.IsNullOrWhiteSpace(key) ? "Modulator" : string.Empty,
            DisplayName = string.Empty,
            BaseDescription = string.Empty,
            Description = string.Empty,
            PowerTier = ModulatorPowerTier.Tier1,
            Category = WeaponModulatorCategory.None,
            Tags = new List<string>(),
            AllowedWeaponTags = new List<string>(),
            BlockedWeaponTags = new List<string>(),
            SupportsTier = false,
            Weight = 1f,
            UiColor = Colors.White,
            Substats = new List<SubstatRoll>(),
            Effects = new List<WeaponModulatorEffectData>(),
            Tier = WeaponModulatorTier.Baseline,
        };

        modulator.RefreshPresentation();
        return modulator;
    }
}