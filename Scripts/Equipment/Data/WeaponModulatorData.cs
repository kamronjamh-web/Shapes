using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

public enum WeaponModulatorCategory
{
    None,
    Core,
    Projectile,
    Sustain,
    Utility,
    Proc,
    Wing,
}

public enum WeaponModulatorTier
{
    Flawed = 0,
    Baseline = 1,
    Enhanced = 2,
    Flawless = 3,
}

public sealed class WeaponModulatorData
{
    public string Key { get; set; } = string.Empty;
    public string BaseDisplayName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ModulatorPowerTier PowerTier { get; set; } = ModulatorPowerTier.Tier1;
    public WeaponModulatorCategory Category { get; set; } = WeaponModulatorCategory.None;
    public List<string> Tags { get; set; } = new();
    public List<string> AllowedWeaponTags { get; set; } = new();
    public List<string> BlockedWeaponTags { get; set; } = new();
    public bool SupportsTier { get; set; }
    public float Weight { get; set; } = 1f;
    public Color UiColor { get; set; } = Colors.White;
    public List<SubstatRoll> Substats { get; set; } = new();
    public List<WeaponModulatorEffectData> Effects { get; set; } = new();
    public WeaponModulatorTier Tier { get; set; } = WeaponModulatorTier.Baseline;

    public bool IsWingModulator => Category == WeaponModulatorCategory.Wing || IsWingModulatorKey(Key);
    public bool IsRetired => NormalizedKey == "apex_doctrine";
    public bool UsesTierPresentation => true;
    public float QualityStrength => ModulatorQuality.GetStrength(Tier);
    private readonly ModulatorQuality.ResolutionCache _qualityCache = new();
    public string NormalizedKey => NormalizeKey(Key);
    public float WingCadenceMultiplier => IsWingModulator ? GetWingCadenceMultiplier(Tier) : 1f;

    public void RefreshPresentation()
    {
        Tags ??= new List<string>();
        AllowedWeaponTags ??= new List<string>();
        BlockedWeaponTags ??= new List<string>();
        Effects ??= new List<WeaponModulatorEffectData>();

        SupportsTier = true;
        if (!Enum.IsDefined(typeof(WeaponModulatorTier), Tier)) Tier = WeaponModulatorTier.Baseline;

        BaseDisplayName = StripTierPrefix(string.IsNullOrWhiteSpace(BaseDisplayName) ? DisplayName : BaseDisplayName);
        if (string.IsNullOrWhiteSpace(BaseDisplayName))
        {
            BaseDisplayName = BuildDisplayNameFromKey(Key);
        }

        BaseDescription = StripWingCadenceLine(string.IsNullOrWhiteSpace(BaseDescription) ? Description : BaseDescription);
        NormalizeTagsInPlace(Tags);
        NormalizeTagsInPlace(AllowedWeaponTags);
        NormalizeTagsInPlace(BlockedWeaponTags);

        DisplayName = FormatDisplayName(BaseDisplayName, UsesTierPresentation ? Tier : WeaponModulatorTier.Baseline);
        Description = FormatDescription(BaseDescription, Tier, IsWingModulator);
    }

    public bool CanApplyToWeapon(IReadOnlyCollection<string>? weaponTags)
    {
        if (weaponTags == null || weaponTags.Count == 0)
        {
            return AllowedWeaponTags.Count == 0;
        }

        if (AllowedWeaponTags.Count > 0)
        {
            bool foundAllowed = false;
            foreach (string weaponTag in weaponTags)
            {
                string normalizedWeaponTag = NormalizeTag(weaponTag);
                for (int allowedIndex = 0; allowedIndex < AllowedWeaponTags.Count; allowedIndex++)
                {
                    if (string.Equals(AllowedWeaponTags[allowedIndex], normalizedWeaponTag, StringComparison.Ordinal))
                    {
                        foundAllowed = true;
                        break;
                    }
                }

                if (foundAllowed)
                {
                    break;
                }
            }

            if (!foundAllowed)
            {
                return false;
            }
        }

        foreach (string weaponTag in weaponTags)
        {
            string normalizedWeaponTag = NormalizeTag(weaponTag);
            for (int blockedIndex = 0; blockedIndex < BlockedWeaponTags.Count; blockedIndex++)
            {
                if (string.Equals(BlockedWeaponTags[blockedIndex], normalizedWeaponTag, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public IReadOnlyList<WeaponModulatorEffectData> GetResolvedEffects()
    {
        return _qualityCache.Resolve(this);
    }

    public static string NormalizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool TryParseTierKey(string? key, out WeaponModulatorTier tier)
    {
        switch (NormalizeKey(key))
        {
            case "flawed":
            case "broken":
                tier = WeaponModulatorTier.Flawed;
                return true;
            case "baseline":
            case "base":
            case "standard":
                tier = WeaponModulatorTier.Baseline;
                return true;
            case "enhanced":
                tier = WeaponModulatorTier.Enhanced;
                return true;
            case "flawless":
                tier = WeaponModulatorTier.Flawless;
                return true;
            default:
                tier = WeaponModulatorTier.Baseline;
                return false;
        }
    }

    public static float GetWingCadenceMultiplier(WeaponModulatorTier tier)
    {
        return tier switch
        {
            WeaponModulatorTier.Flawed => 0.25f,
            WeaponModulatorTier.Baseline => 0.50f,
            WeaponModulatorTier.Enhanced => 0.75f,
            WeaponModulatorTier.Flawless => 1.0f,
            _ => 0.50f,
        };
    }

    public static string FormatDisplayName(string? baseDisplayName, WeaponModulatorTier tier)
    {
        string resolvedBaseName = string.IsNullOrWhiteSpace(baseDisplayName)
            ? "Modulator"
            : StripTierPrefix(baseDisplayName);

        return tier switch
        {
            WeaponModulatorTier.Flawed => $"Broken {resolvedBaseName}",
            WeaponModulatorTier.Enhanced => $"Enhanced {resolvedBaseName}",
            WeaponModulatorTier.Flawless => $"Flawless {resolvedBaseName}",
            _ => resolvedBaseName,
        };
    }

    public static string FormatDescription(string? baseDescription, WeaponModulatorTier tier, bool isWingModulator)
    {
        string trimmed = (baseDescription ?? string.Empty).Trim();
        if (!isWingModulator)
        {
            return trimmed;
        }

        string cadenceText = $"Wing cadence: {Mathf.RoundToInt(GetWingCadenceMultiplier(tier) * 100f)}% of the installed weapon.";
        return string.IsNullOrWhiteSpace(trimmed)
            ? cadenceText
            : $"{trimmed} {cadenceText}";
    }

    public static bool IsWingModulatorKey(string? key)
    {
        return NormalizeKey(key) switch
        {
            "wing_man" => true,
            "wing_woman" => true,
            "wing_brother" => true,
            "wing_sister" => true,
            _ => false,
        };
    }

    public static string NormalizeTag(string? tag)
    {
        return NormalizeKey(tag).Replace(' ', '_');
    }

    public static string GetCategoryDisplayName(WeaponModulatorCategory category)
    {
        return category switch
        {
            WeaponModulatorCategory.Core => "Core",
            WeaponModulatorCategory.Projectile => "Projectile",
            WeaponModulatorCategory.Sustain => "Sustain",
            WeaponModulatorCategory.Utility => "Utility",
            WeaponModulatorCategory.Proc => "Proc",
            WeaponModulatorCategory.Wing => "Wing",
            _ => "Uncategorized",
        };
    }

    public bool HasTag(string? tag)
    {
        string normalized = NormalizeTag(tag);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        for (int i = 0; i < Tags.Count; i++)
        {
            if (string.Equals(NormalizeTag(Tags[i]), normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;
    }

    private static void NormalizeTagsInPlace(List<string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = tags.Count - 1; i >= 0; i--)
        {
            string normalized = NormalizeTag(tags[i]);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                tags.RemoveAt(i);
                continue;
            }

            tags[i] = normalized;
        }
    }

    private static string StripTierPrefix(string? displayName)
    {
        string trimmed = (displayName ?? string.Empty).Trim();
        if (trimmed.StartsWith("Broken ", StringComparison.OrdinalIgnoreCase)) return trimmed[7..].TrimStart();
        if (trimmed.StartsWith("Baseline ", StringComparison.OrdinalIgnoreCase)) return trimmed[9..].TrimStart();
        if (trimmed.StartsWith("Flawed ", System.StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[7..].TrimStart();
        }

        if (trimmed.StartsWith("Enhanced ", System.StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[9..].TrimStart();
        }

        if (trimmed.StartsWith("Flawless ", System.StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[9..].TrimStart();
        }

        return trimmed;
    }

    private static string StripWingCadenceLine(string? description)
    {
        string trimmed = (description ?? string.Empty).Trim();
        int cadenceIndex = trimmed.IndexOf("Wing cadence:", System.StringComparison.OrdinalIgnoreCase);
        if (cadenceIndex >= 0)
        {
            trimmed = trimmed[..cadenceIndex].TrimEnd();
        }

        return trimmed;
    }

    private static string BuildDisplayNameFromKey(string? key)
    {
        string[] parts = NormalizeKey(key)
            .Replace('_', ' ')
            .Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "Modulator";
        }

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            parts[i] = part.Length == 0
                ? string.Empty
                : char.ToUpperInvariant(part[0]) + part[1..];
        }

        return string.Join(" ", parts);
    }
}
