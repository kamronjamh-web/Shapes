using Godot;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Equipment;

public enum ModifierVisualKind
{
    None = 0,
    Glitch = 1,
    Radiant = 2,
    Electric = 3,
    Polished = 4,
}

public readonly struct ModifierVisualStyle
{
    public ModifierVisualStyle(ModifierKey key, Color baseColor, Color accentColor, Color secondaryAccentColor, Color glowColor, string shortTag, ModifierVisualKind visualKind)
    {
        Key = key;
        BaseColor = baseColor;
        AccentColor = accentColor;
        SecondaryAccentColor = secondaryAccentColor;
        GlowColor = glowColor;
        ShortTag = shortTag;
        VisualKind = visualKind;
    }

    public ModifierKey Key { get; }
    public Color BaseColor { get; }
    public Color AccentColor { get; }
    public Color SecondaryAccentColor { get; }
    public Color GlowColor { get; }
    public string ShortTag { get; }
    public ModifierVisualKind VisualKind { get; }
    public bool HasSpecialVisuals => VisualKind != ModifierVisualKind.None;
    public bool UsesGlitchVisuals => VisualKind == ModifierVisualKind.Glitch;
    public bool UsesRadiantVisuals => VisualKind == ModifierVisualKind.Radiant;
    public bool UsesElectricVisuals => VisualKind == ModifierVisualKind.Electric;
    public bool UsesPolishedVisuals => VisualKind == ModifierVisualKind.Polished;
}

public static class ModifierVisualTheme
{
    public static readonly Color UnstablePurple = new(0.6588f, 0.3333f, 0.9686f, 1f);
    public static readonly Color UnstableCyan = new(0.08f, 0.90f, 1f, 1f);
    public static readonly Color UnstableMagenta = new(1f, 0.12f, 0.86f, 1f);
    public static readonly Color ShinyGold = new(0.9843f, 0.7490f, 0.1412f, 1f);
    public static readonly Color ShinyWhiteGold = new(1f, 0.965f, 0.78f, 1f);
    public static readonly Color ShinyAmber = new(1f, 0.52f, 0.08f, 1f);
    public static readonly Color ElectricBlue = new(0.2314f, 0.5098f, 0.9647f, 1f);
    public static readonly Color ElectricCyan = new(0.12f, 0.92f, 1f, 1f);
    public static readonly Color ElectricWhite = new(0.86f, 0.96f, 1f, 1f);
    public static readonly Color ElectricGold = new(1f, 0.843f, 0.0f, 1f);

    public static bool TryGet(EquipmentItem? item, out ModifierVisualStyle style)
    {
        style = default;
        ModifierData? modifier = item?.Modifier;
        if (modifier == null || modifier.Key == ModifierKey.Normal)
        {
            return false;
        }

        if (modifier.Key == ModifierKey.Unstable)
        {
            Color baseColor = modifier.UiColor.A > 0f ? modifier.UiColor : UnstablePurple;
            style = new ModifierVisualStyle(
                modifier.Key,
                baseColor,
                UnstableCyan,
                UnstableMagenta,
                baseColor.Lightened(0.16f),
                "GLITCH",
                ModifierVisualKind.Glitch);
            return true;
        }

        if (modifier.Key == ModifierKey.Shiny)
        {
            Color baseColor = modifier.UiColor.A > 0f ? modifier.UiColor : ShinyGold;
            style = new ModifierVisualStyle(
                modifier.Key,
                baseColor,
                ShinyWhiteGold,
                ShinyAmber,
                ShinyWhiteGold,
                "SHINE",
                ModifierVisualKind.Polished);
            return true;
        }

        if (modifier.Key == ModifierKey.Electric)
        {
            Color baseColor = modifier.UiColor.A > 0f ? modifier.UiColor : ElectricBlue;
            style = new ModifierVisualStyle(
                modifier.Key,
                baseColor,
                ElectricCyan,
                ElectricGold,
                ElectricWhite,
                "VOLT",
                ModifierVisualKind.Electric);
            return true;
        }

        Color genericColor = modifier.UiColor.A > 0f ? modifier.UiColor : Colors.White;
        style = new ModifierVisualStyle(
            modifier.Key,
            genericColor,
            genericColor.Lightened(0.18f),
            genericColor.Lightened(0.34f),
            genericColor,
            modifier.DisplayName,
            ModifierVisualKind.None);
        return true;
    }

    public static bool HasSpecialVisuals(EquipmentItem? item)
    {
        return TryGet(item, out ModifierVisualStyle style) && style.HasSpecialVisuals;
    }

    public static bool HasUnstableVisuals(EquipmentItem? item)
    {
        return TryGet(item, out ModifierVisualStyle style) && style.UsesGlitchVisuals;
    }
}