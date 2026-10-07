using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

// Canonical defaults used when resource folders are empty.
public static class EquipmentCatalogDefaults
{
    public const int MaxWeaponLevel = 5;
    public const int ShapeCount = 16;
    public const int RarityCount = 6;
    public const int RollableModifierCount = 9;
    public static int ModulatorCount => ModulatorCatalog.Count;

    private readonly record struct WeaponCustomizationProgression(int StartingModulatorSlots, int MaxModulatorSlots, int EssenceUnlockLevel);

    private static readonly ShapeData[] ShapeTemplates =
    {
        CreateShape("square", "Square", ShapeGroup.TwoD, 20f, 1, WeaponAttackType.Single, "Flat Strike", 1, 80f, 80f, 1f, "A basic single-shot weapon."),
        CreateShape("circle", "Circle", ShapeGroup.TwoD, 20f, 1, WeaponAttackType.Sawblade, "Sawblade", 5, 40f, 200f, 1.5f, "Launches a spinning sawblade that pierces enemies."),
        CreateShape("triangle", "Triangle", ShapeGroup.TwoD, 20f, 1, WeaponAttackType.Laser, "Piercing Beam", 1, 70f, 70f, 1f, "Fires a focused beam that pierces targets in a line."),
        CreateShape("hexagon", "Hexagon", ShapeGroup.TwoD, 20f, 1, WeaponAttackType.Mine, "Hex Mines", 3, 30f, 90f, 1f, "Drops proximity mines that detonate on contact."),

        CreateShape("sphere", "Sphere", ShapeGroup.ThreeD, 4f, 2, WeaponAttackType.Nova, "Nova Pulse", 1, 100f, 100f, 2.5f, "Emits an expanding shockwave ring."),
        CreateShape("cube", "Cube", ShapeGroup.ThreeD, 4f, 2, WeaponAttackType.Orbital, "Orbital Guard", 1, 50f, 50f, 8f, "Summons orbiting cubes that damage on contact."),
        CreateShape("pyramid", "Pyramid", ShapeGroup.ThreeD, 4f, 2, WeaponAttackType.Homing, "Homing Seekers", 1, 120f, 120f, 1.2f, "Fires homing missiles at nearby targets."),
        CreateShape("cone", "Cone", ShapeGroup.ThreeD, 4f, 2, WeaponAttackType.FlameCone, "Flame Cone", 1, 80f, 80f, 1.5f, "Unleashes a frontal cone attack."),

        CreateShape("tesseract", "Tesseract", ShapeGroup.Ultra, 0.75f, 3, WeaponAttackType.Rift, "Dimension Rift", 1, 200f, 200f, 4f, "Opens a pulsing rift zone."),
        CreateShape("hyperphere", "Hyperphere", ShapeGroup.Ultra, 0.75f, 3, WeaponAttackType.Mortar, "Dimensional Mortar", 1, 200f, 200f, 3f, "Launches an arcing shell with AOE impact."),
        CreateShape("pentachoron", "Pentachoron", ShapeGroup.Ultra, 0.75f, 3, WeaponAttackType.ChainLightning, "Chain Lightning", 5, 120f, 600f, 2.5f, "Arcs lightning across multiple enemies."),
        CreateShape("hypercone", "Hypercone", ShapeGroup.Ultra, 0.75f, 3, WeaponAttackType.Drill, "Drill Shot", 1, 150f, 150f, 1.5f, "Fires a burrowing drill projectile."),

        CreateShape("impossiblecube", "Impossible Cube", ShapeGroup.Mythic, 0.25f, 4, WeaponAttackType.VoidSingularity, "Void Singularity", 1, 300f, 300f, 6f, "Creates a black hole that pulls and crushes enemies."),
        CreateShape("mobius", "Mobius", ShapeGroup.Mythic, 0.25f, 4, WeaponAttackType.TemporalEcho, "Temporal Echo", 0, 0f, 0f, 999f, "Passive: echoes fired weapons after a short delay."),
        CreateShape("klein", "Klein", ShapeGroup.Mythic, 0.25f, 4, WeaponAttackType.WrathAura, "Wrath Aura", 0, 0f, 0f, 999f, "Passive: aura damage scales with missing HP."),
        CreateShape("boys", "Boy's Surface", ShapeGroup.Mythic, 0.25f, 4, WeaponAttackType.ShadowClone, "Shadow Clones", 1, 180f, 180f, 7f, "Summons temporary shadow clones that replay attacks."),
    };

    private static readonly RarityData[] RarityTemplates =
    {
        CreateRarity("common", "Common", 30f, 1.15f, 1, 75, new Color(1f, 1f, 1f)),
        CreateRarity("uncommon", "Uncommon", 10f, 1.35f, 1, 225, new Color(0.2980f, 0.6863f, 0.3137f)),
        CreateRarity("rare", "Rare", 7f, 1.60f, 1, 563, new Color(0.1294f, 0.5882f, 0.9529f)),
        CreateRarity("legendary", "Legendary", 2f, 1.90f, 2, 1125, new Color(0.6118f, 0.1529f, 0.6902f)),
        CreateRarity("mythic", "Mythic", 0.1f, 2.80f, 3, 11250, new Color(0.9451f, 0.2392f, 0.2118f)),
        CreateRarity("divine", "Divine", 0.01f, 3.30f, 4, 33750, new Color(0.9922f, 0.8471f, 0.2078f)),
    };

    private static readonly ModifierData NormalTemplate = CreateModifier(
        ModifierKey.Normal,
        "Normal",
        80f,
        Colors.White,
        1f,
        1f,
        0f,
        "none",
        "No special effect.",
        SubstatType.None,
        false,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    private static readonly ModifierData[] RollableModifierTemplates =
    {
        CreateModifier(ModifierKey.Shiny, "Shiny", 5f, new Color(0.9843f, 0.7490f, 0.1412f), 1f, 1f, 0.0015f, "xp_boost", "+25% XP Gain.", SubstatType.None, false, false, "Lucky Drop", "Chance to spawn bonus XP orbs on kills.", "Blinding Flash", "Periodic blind pulse around the player.", "Radiant Dominion", "Global slowing aura around the player."),
        CreateModifier(ModifierKey.Electric, "Electric", 5f, new Color(0.2314f, 0.5098f, 0.9647f), 0.2f, 2f, 0.0015f, "chain_lightning", "-80% HP, +100% DMG, guaranteed DMG Percent.", SubstatType.DamagePercent, false, false, "Static Field", "Hits chain to 2 nearby enemies for 20% damage with visible lightning arcs.", "Conductor", "Charged targets detonate every 4s; original marks can spread non-recursive chain marks.", "Thunderstorm", "Every 4s a map-wide lightning strike chains through nearby enemies."),
        CreateModifier(ModifierKey.Dark, "Dark", 5f, new Color(0.5451f, 0.3608f, 0.9647f), 0.7f, 0.7f, 0.0015f, "lifesteal", "Dark aura with sustain-focused effects.", SubstatType.None, false, false, "Soul Drain", "Restore HP from dealt damage.", "Phantom Echo", "Kills can spawn hostile phantoms.", "Void Shroud", "Phantoms can chain through clustered enemies."),
        CreateModifier(ModifierKey.Unstable, "Unstable", 5f, new Color(0.6588f, 0.3333f, 0.9686f), 1f, 1f, 0.0015f, "double_damage", "Chaotic roll with random unstable effects.", SubstatType.None, false, false, "Jitter", "Shots vary wildly in damage.", "Glitch Shot", "Chance to fire random extra projectile patterns.", "Entropy Field", "Periodic chaos pulse on nearby enemies."),
        CreateModifier(ModifierKey.Shielded, "Shielded", 5f, new Color(0.3922f, 0.7843f, 1f), 2f, 0.2f, 0.0015f, "none", "Guaranteed Dodge Chance with defensive profile.", SubstatType.DodgeChance, false, false, "Overshield", "Maintains a rechargeable overshield.", "Shockwave Break", "Shield break repels nearby enemies.", "Bubble Fortress", "Deploys a temporary defensive bubble."),
        CreateModifier(ModifierKey.Prismatic, "Prismatic", 0.1f, new Color(0.9843f, 0.7490f, 0.1412f), 1f, 1f, 0f, "wildcard", "Rolls all substats and counts as wildcard.", SubstatType.None, true, true, "Wildcard", "Counts toward synergy thresholds.", "Chromatic Surge", "Occasional extra attack trigger.", "Full Spectrum", "Activates all modifier synergies."),
        CreateModifier(ModifierKey.Cursed, "Glacial", 0.1f, new Color(0.4039f, 0.9098f, 0.9765f), 1f, 1.5f, 0f, "deep_freeze", "Chills and freezes enemies.", SubstatType.None, false, false, "Deep Freeze", "Chill stacks can freeze enemies solid.", "Shatter", "Frozen targets can explode into chill AOE.", "Permafrost", "Frozen enemies remain frozen until shattered."),
        CreateModifier(ModifierKey.Thorned, "Thorned", 0.1f, new Color(0.0863f, 0.6392f, 0.2902f), 1.5f, 1f, 0f, "bramble_armor", "Reflective thorn retaliation.", SubstatType.None, false, false, "Bramble Skin", "Contact damage reflects to attackers.", "Bramble Spread", "Thorn marks can spread on collisions.", "Iron Maiden", "Marked enemies detonate on death."),
        CreateModifier(ModifierKey.Parasite, "Parasitic", 0.1f, new Color(0.2902f, 0.8706f, 0.5020f), 1f, 1.3f, 0f, "infection", "Infects enemies on kill.", SubstatType.None, false, false, "Infection", "Kills can convert nearby enemies temporarily.", "Contagion", "Infected kills can spread conversion.", "Full Conversion", "Converted enemies persist until destroyed."),
    };

    private static readonly ModifierData LegacyVolitileModifierTemplate = CreateModifier(ModifierKey.Volitile, "Volitile", 0.1f, new Color(1.0000f, 0.4863f, 0.2471f), 1f, 1f, 0f, "volatile_blast", "All damage instances erupt on contact.", SubstatType.None, false, false, "Detonation", "Legacy compatibility only. New Volitile drops now come from the modulator catalog.", "Blast Radius", "Legacy Volitile saves still preserve the old explosion behavior.", "Chain Reactor", "Volitile now follows the modulator tier ladder for new drops and installs.");

    private static readonly Dictionary<string, WeaponCustomizationProgression> WeaponCustomizationByRarity =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = new WeaponCustomizationProgression(1, 4, 4),
            ["uncommon"] = new WeaponCustomizationProgression(2, 5, 4),
            ["rare"] = new WeaponCustomizationProgression(3, 6, 3),
            ["legendary"] = new WeaponCustomizationProgression(4, 7, 2),
            ["mythic"] = new WeaponCustomizationProgression(4, 8, 2),
            ["divine"] = new WeaponCustomizationProgression(5, 10, 1),
        };

    private static readonly Dictionary<string, Dictionary<SubstatType, float>> SubstatCapsByRarity =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = CreateSubstatCaps(33.33f, 16.67f, 4f, 3.33f, 2.17f, 37.5f, 6.67f, 4f, 4f, 25f, 15f, 12f, 2f, 2f, 1f),
            ["uncommon"] = CreateSubstatCaps(46.67f, 23.33f, 5.5f, 4.67f, 3.33f, 50f, 8.33f, 5f, 5.5f, 35f, 20f, 16f, 3f, 3f, 1f),
            ["rare"] = CreateSubstatCaps(60f, 30f, 7f, 6f, 4.5f, 62.5f, 10f, 6.5f, 7f, 50f, 25f, 22f, 3f, 3f, 2f),
            ["legendary"] = CreateSubstatCaps(73.33f, 36.67f, 8.5f, 7.33f, 5.67f, 75f, 11.67f, 8f, 9f, 65f, 30f, 28f, 4f, 4f, 2f),
            ["mythic"] = CreateSubstatCaps(100f, 50f, 11.5f, 10f, 8f, 100f, 15f, 12f, 13f, 100f, 50f, 45f, 6f, 6f, 3f),
            ["divine"] = CreateSubstatCaps(120f, 60f, 13f, 12f, 10f, 120f, 16.5f, 15f, 16f, 125f, 60f, 55f, 8f, 8f, 4f),
        };

    public static List<ShapeData> CreateDefaultShapes()
    {
        List<ShapeData> output = new(ShapeTemplates.Length);
        for (int i = 0; i < ShapeTemplates.Length; i++)
        {
            output.Add(CloneShape(ShapeTemplates[i]));
        }

        return output;
    }

    public static List<RarityData> CreateDefaultRarities()
    {
        List<RarityData> output = new(RarityTemplates.Length);
        for (int i = 0; i < RarityTemplates.Length; i++)
        {
            output.Add(CloneRarity(RarityTemplates[i]));
        }

        return output;
    }

    public static List<ModifierData> CreateDefaultModifiers(bool includeBaselineNormal = false)
    {
        List<ModifierData> output = new(RollableModifierTemplates.Length + (includeBaselineNormal ? 1 : 0));

        if (includeBaselineNormal)
        {
            output.Add(CloneModifier(NormalTemplate));
        }

        for (int i = 0; i < RollableModifierTemplates.Length; i++)
        {
            output.Add(CloneModifier(RollableModifierTemplates[i]));
        }

        return output;
    }

    public static List<WeaponModulatorData> CreateDefaultModulators()
    {
        return ModulatorCatalog.CreateAll();
    }

    public static ShapeData CreateShapeByKey(string key)
    {
        for (int i = 0; i < ShapeTemplates.Length; i++)
        {
            if (string.Equals(ShapeTemplates[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return CloneShape(ShapeTemplates[i]);
            }
        }

        return CloneShape(ShapeTemplates[0]);
    }

    public static RarityData CreateRarityByKey(string key)
    {
        string normalizedKey = NormalizeRarityKey(key);
        for (int i = 0; i < RarityTemplates.Length; i++)
        {
            if (string.Equals(RarityTemplates[i].Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
            {
                return CloneRarity(RarityTemplates[i]);
            }
        }

        return CloneRarity(RarityTemplates[0]);
    }

    public static ModifierData CreateModifierByKey(ModifierKey key, bool includeBaselineNormal = true)
    {
        if (includeBaselineNormal && key == ModifierKey.Normal)
        {
            return CloneModifier(NormalTemplate);
        }

        if (key == ModifierKey.Volitile)
        {
            return CloneModifier(LegacyVolitileModifierTemplate);
        }

        for (int i = 0; i < RollableModifierTemplates.Length; i++)
        {
            if (RollableModifierTemplates[i].Key == key)
            {
                return CloneModifier(RollableModifierTemplates[i]);
            }
        }

        return CloneModifier(includeBaselineNormal ? NormalTemplate : RollableModifierTemplates[0]);
    }

    public static bool TryCreateModulatorByKey(string? key, out WeaponModulatorData modulator)
    {
        return TryCreateModulatorByKey(key, WeaponModulatorTier.Baseline, out modulator);
    }

    public static bool TryCreateModulatorByKey(string? key, WeaponModulatorTier tier, out WeaponModulatorData modulator)
    {
        return ModulatorCatalog.TryCreateByKey(key, tier, out modulator);
    }

    public static WeaponModulatorData CreateModulatorByKey(string? key)
    {
        return CreateModulatorByKey(key, WeaponModulatorTier.Baseline);
    }

    public static WeaponModulatorData CreateModulatorByKey(string? key, WeaponModulatorTier tier)
    {
        return ModulatorCatalog.CreateByKey(key, tier);
    }

    public static string NormalizeRarityKey(string? key)
    {
        string normalized = string.IsNullOrWhiteSpace(key)
            ? "common"
            : key.Trim().ToLowerInvariant();

        return normalized switch
        {
            "unreal" => "mythic",
            "epic" => "legendary",
            "transcendent" => "divine",
            _ => normalized,
        };
    }

    public static string RemapLegacyRarityKey(string? key)
    {
        string normalized = string.IsNullOrWhiteSpace(key)
            ? "common"
            : key.Trim().ToLowerInvariant();

        normalized = normalized switch
        {
            "unreal" => "mythic",
            "epic" => "legendary",
            _ => normalized,
        };

        return normalized switch
        {
            "uncommon" => "common",
            "rare" => "uncommon",
            "legendary" => "rare",
            "mythic" => "legendary",
            "divine" => "mythic",
            "transcendent" => "divine",
            _ => "common",
        };
    }

    public static int GetUnlockedModulatorSlots(string? rarityKey, int level)
    {
        WeaponCustomizationProgression progression = GetCustomizationProgression(rarityKey);
        int clampedLevel = Mathf.Clamp(level, 1, MaxWeaponLevel);
        if (clampedLevel <= 1)
        {
            return progression.StartingModulatorSlots;
        }

        float t = (clampedLevel - 1) / (float)(MaxWeaponLevel - 1);
        return Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(progression.StartingModulatorSlots, progression.MaxModulatorSlots, t)),
            progression.StartingModulatorSlots,
            progression.MaxModulatorSlots);
    }

    public static bool HasEssenceSlot(string? rarityKey, int level)
    {
        WeaponCustomizationProgression progression = GetCustomizationProgression(rarityKey);
        int clampedLevel = Mathf.Clamp(level, 1, MaxWeaponLevel);
        return clampedLevel >= progression.EssenceUnlockLevel;
    }

    public static bool TryGetSubstatCap(string? rarityKey, SubstatType type, out float maxValue)
    {
        maxValue = 0f;
        if (type == SubstatType.None)
        {
            return false;
        }

        string key = NormalizeRarityKey(rarityKey);

        if (!SubstatCapsByRarity.TryGetValue(key, out Dictionary<SubstatType, float>? caps))
        {
            caps = SubstatCapsByRarity["common"];
        }

        return caps.TryGetValue(type, out maxValue);
    }

    public static float GetSubstatCap(string? rarityKey, SubstatType type, float fallback = 1f)
    {
        return TryGetSubstatCap(rarityKey, type, out float maxValue) ? maxValue : fallback;
    }

    private static Dictionary<SubstatType, float> CreateSubstatCaps(
        float damagePercent,
        float healthPercent,
        float dodgeChance,
        float lifeStealPercent,
        float healthRegen,
        float critDamage,
        float critChance,
        float movementSpeed,
        float range,
        float luck,
        float size,
        float attackSpeed,
        float ricochet,
        float bounceTimes,
        float projectileCount)
    {
        return new Dictionary<SubstatType, float>
        {
            [SubstatType.DamagePercent] = damagePercent,
            [SubstatType.HealthPercent] = healthPercent,
            [SubstatType.DodgeChance] = dodgeChance,
            [SubstatType.LifeStealPercent] = lifeStealPercent,
            [SubstatType.HealthRegen] = healthRegen,
            [SubstatType.CritDamage] = critDamage,
            [SubstatType.CritChance] = critChance,
            [SubstatType.MovementSpeed] = movementSpeed,
            [SubstatType.Range] = range,
            [SubstatType.Luck] = luck,
            [SubstatType.Size] = size,
            [SubstatType.AttackSpeed] = attackSpeed,
            [SubstatType.Ricochet] = ricochet,
            [SubstatType.BounceTimes] = bounceTimes,
            [SubstatType.ProjectileCount] = projectileCount,
        };
    }

    private static ShapeData CreateShape(
        string key,
        string displayName,
        ShapeGroup shapeGroup,
        float weight,
        int stars,
        WeaponAttackType attackType,
        string attackName,
        int hits,
        float damagePerHit,
        float totalDamage,
        float cooldown,
        string specialDescription)
    {
        return new ShapeData
        {
            Key = key,
            DisplayName = displayName,
            ShapeGroup = shapeGroup,
            Weight = weight,
            Stars = stars,
            AttackType = attackType,
            AttackName = attackName,
            Hits = hits,
            DamagePerHit = damagePerHit,
            TotalDamage = totalDamage,
            Cooldown = cooldown,
            SpecialDescription = specialDescription,
            Description = specialDescription,
        };
    }

    private static RarityData CreateRarity(
        string key,
        string displayName,
        float weight,
        float statMultiplier,
        int substatCount,
        int xpValue,
        Color uiColor)
    {
        return new RarityData
        {
            Key = key,
            DisplayName = displayName,
            Weight = weight,
            StatMultiplier = statMultiplier,
            SubstatCount = substatCount,
            XpValue = xpValue,
            UiColor = uiColor,
        };
    }

    private static ModifierData CreateModifier(
        ModifierKey key,
        string displayName,
        float weight,
        Color uiColor,
        float hpMultiplier,
        float damageMultiplier,
        float xpMultiplierBonus,
        string primaryEffectKey,
        string primaryEffectDescription,
        SubstatType guaranteedSubstat,
        bool grantsAllSubstats,
        bool isWildcard,
        string passiveName,
        string passiveDescription,
        string synergyName,
        string synergyDescription,
        string superSynergyName,
        string superSynergyDescription)
    {
        return new ModifierData
        {
            Key = key,
            DisplayName = displayName,
            Weight = weight,
            UiColor = uiColor,
            HpMultiplier = hpMultiplier,
            DamageMultiplier = damageMultiplier,
            XpMultiplierBonus = xpMultiplierBonus,
            PrimaryEffectKey = primaryEffectKey,
            PrimaryEffectDescription = primaryEffectDescription,
            PassiveName = passiveName,
            PassiveDescription = passiveDescription,
            SynergyName = synergyName,
            SynergyDescription = synergyDescription,
            SuperSynergyName = superSynergyName,
            SuperSynergyDescription = superSynergyDescription,
            GuaranteedSubstat = guaranteedSubstat,
            GrantsAllSubstats = grantsAllSubstats,
            IsWildcard = isWildcard,
        };
    }

    private static ShapeData CloneShape(ShapeData src)
    {
        return new ShapeData
        {
            Key = src.Key,
            DisplayName = src.DisplayName,
            ShapeGroup = src.ShapeGroup,
            Weight = src.Weight,
            Stars = src.Stars,
            AttackType = src.AttackType,
            AttackName = src.AttackName,
            Hits = src.Hits,
            DamagePerHit = src.DamagePerHit,
            TotalDamage = src.TotalDamage,
            Cooldown = src.Cooldown,
            SpecialDescription = src.SpecialDescription,
            Description = src.Description,
            VisualScene = src.VisualScene,
            VisualScale = src.VisualScale,
            VisualRotationDegrees = src.VisualRotationDegrees,
            WeaponScene = src.WeaponScene,
        };
    }

    private static RarityData CloneRarity(RarityData src)
    {
        return new RarityData
        {
            Key = src.Key,
            DisplayName = src.DisplayName,
            Weight = src.Weight,
            StatMultiplier = src.StatMultiplier,
            SubstatCount = src.SubstatCount,
            XpValue = src.XpValue,
            UiColor = src.UiColor,
        };
    }

    private static ModifierData CloneModifier(ModifierData src)
    {
        return new ModifierData
        {
            Key = src.Key,
            DisplayName = src.DisplayName,
            Weight = src.Weight,
            UiColor = src.UiColor,
            HpMultiplier = src.HpMultiplier,
            DamageMultiplier = src.DamageMultiplier,
            XpMultiplierBonus = src.XpMultiplierBonus,
            PrimaryEffectKey = src.PrimaryEffectKey,
            PrimaryEffectDescription = src.PrimaryEffectDescription,
            PassiveName = src.PassiveName,
            PassiveDescription = src.PassiveDescription,
            SynergyName = src.SynergyName,
            SynergyDescription = src.SynergyDescription,
            SuperSynergyName = src.SuperSynergyName,
            SuperSynergyDescription = src.SuperSynergyDescription,
            GuaranteedSubstat = src.GuaranteedSubstat,
            GrantsAllSubstats = src.GrantsAllSubstats,
            IsWildcard = src.IsWildcard,
        };
    }

    private static WeaponCustomizationProgression GetCustomizationProgression(string? rarityKey)
    {
        string normalized = NormalizeRarityKey(rarityKey);
        return WeaponCustomizationByRarity.TryGetValue(normalized, out WeaponCustomizationProgression progression)
            ? progression
            : WeaponCustomizationByRarity["common"];
    }

}