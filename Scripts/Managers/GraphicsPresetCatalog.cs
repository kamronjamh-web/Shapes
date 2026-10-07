using System;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Managers;

public enum GraphicsPreset
{
    Low,
    Medium,
    High
}

public enum GraphicsAntiAliasingMode
{
    Off,
    Fxaa,
    Smaa,
    Msaa2X,
    Msaa4X,
    Msaa8X
}

public enum GraphicsShadowQuality
{
    Off,
    Hard,
    Filtered
}

public readonly record struct GraphicsPresetProfile(
    GraphicsPreset Preset,
    string DisplayName,
    int RenderScalePercent,
    GraphicsAntiAliasingMode AntiAliasing,
    GraphicsShadowQuality ShadowQuality,
    bool GlowEnabled,
    float EffectDensity,
    float ScreenShakeScale,
    bool FlashReductionEnabled,
    string Summary);

// Central preset tuning for the future graphics menu. Display options like window mode,
// VSync, and FPS limit should stay separate from these quality-focused presets.
public static class GraphicsPresetCatalog
{
    public static GraphicsPreset DefaultPreset => GraphicsPreset.Medium;

    public static IReadOnlyList<GraphicsAntiAliasingMode> AntiAliasingModes { get; } =
    [
        GraphicsAntiAliasingMode.Off,
        GraphicsAntiAliasingMode.Fxaa,
        GraphicsAntiAliasingMode.Smaa,
        GraphicsAntiAliasingMode.Msaa2X,
        GraphicsAntiAliasingMode.Msaa4X,
        GraphicsAntiAliasingMode.Msaa8X
    ];

    public static IReadOnlyList<GraphicsPresetProfile> All { get; } =
    [
        new(
            GraphicsPreset.Low,
            "Low",
            80,
            GraphicsAntiAliasingMode.Off,
            GraphicsShadowQuality.Off,
            false,
            0.55f,
            0.65f,
            true,
            "Prioritizes frame rate and combat readability during dense waves."),
        new(
            GraphicsPreset.Medium,
            "Medium",
            90,
            GraphicsAntiAliasingMode.Fxaa,
            GraphicsShadowQuality.Hard,
            true,
            0.80f,
            0.85f,
            false,
            "Balanced default with readable VFX and restrained performance cost."),
        new(
            GraphicsPreset.High,
            "High",
            100,
            GraphicsAntiAliasingMode.Msaa4X,
            GraphicsShadowQuality.Filtered,
            true,
            1.00f,
            1.00f,
            false,
            "Full target look for desktop play, with intact glow, shadows, and effect density.")
    ];

    public static GraphicsPresetProfile Get(GraphicsPreset preset)
    {
        return preset switch
        {
            GraphicsPreset.Low => All[0],
            GraphicsPreset.Medium => All[1],
            GraphicsPreset.High => All[2],
            _ => All[1]
        };
    }

    public static GraphicsPreset ParseOrDefault(string rawValue)
    {
        if (Enum.TryParse(rawValue, true, out GraphicsPreset preset))
        {
            return preset;
        }

        return DefaultPreset;
    }

    public static string ToConfigValue(GraphicsPreset preset)
    {
        return preset.ToString().ToLowerInvariant();
    }

    public static GraphicsAntiAliasingMode ParseAntiAliasingOrDefault(string rawValue, GraphicsAntiAliasingMode fallback)
    {
        string normalized = rawValue.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        if (Enum.TryParse(normalized, true, out GraphicsAntiAliasingMode antiAliasing)
            && Enum.IsDefined(typeof(GraphicsAntiAliasingMode), antiAliasing))
        {
            return antiAliasing;
        }

        return fallback;
    }

    public static string ToConfigValue(GraphicsAntiAliasingMode antiAliasing)
    {
        return antiAliasing.ToString().ToLowerInvariant();
    }

    public static string GetAntiAliasingDisplayName(GraphicsAntiAliasingMode antiAliasing)
    {
        return antiAliasing switch
        {
            GraphicsAntiAliasingMode.Fxaa => "FXAA",
            GraphicsAntiAliasingMode.Smaa => "SMAA",
            GraphicsAntiAliasingMode.Msaa2X => "MSAA 2x",
            GraphicsAntiAliasingMode.Msaa4X => "MSAA 4x",
            GraphicsAntiAliasingMode.Msaa8X => "MSAA 8x",
            _ => "Off"
        };
    }
}