using System;
using Godot;

namespace SuperGatchaRebuild.Managers;

public readonly record struct GraphicsSettingsData(
    int RenderScalePercent,
    bool GlowEnabled,
    bool ShadowsEnabled,
    float ScreenShakeScale,
    bool FlashReductionEnabled,
    GraphicsAntiAliasingMode AntiAliasing);

public partial class SettingsManager : Node
{
    private static readonly StringName OriginalVisibleMeta = "settings_original_visible";
    private static readonly StringName OriginalShadowMeta = "settings_original_shadow_enabled";
    private static readonly StringName OriginalCastShadowMeta = "settings_original_cast_shadow";

    public const string SaveFilePath = "user://settings.cfg";
    public const string MetaSection = "_meta";
    public const string AudioSection = "audio";
    public const string GraphicsSection = "graphics";
    public const string GraphicsPresetKey = "preset";
    public const string RenderScalePercentKey = "render_scale_percent";
    public const string AntiAliasingKey = "anti_aliasing";
    public const string GlowEnabledKey = "glow_enabled";
    public const string ShadowsEnabledKey = "shadows_enabled";
    public const string ScreenShakeScaleKey = "screen_shake_scale";
    public const string FlashReductionEnabledKey = "flash_reduction_enabled";
    public const string SchemaVersionKey = "schema_version";
    public const int CurrentSchemaVersion = 4;

    public static SettingsManager? Instance { get; private set; }

    public event Action<GraphicsSettingsData>? GraphicsSettingsChanged;

    public GraphicsSettingsData GraphicsSettings { get; private set; } = FromPreset(GraphicsPresetCatalog.DefaultPreset);

    private bool _graphicsSettingsReady;

    public override void _EnterTree()
    {
        Instance = this;

        SceneTree? tree = GetTree();
        if (tree != null)
        {
            tree.NodeAdded += OnSceneTreeNodeAdded;
        }
    }

    public override void _Ready()
    {
        GraphicsSettings = LoadGraphicsSettingsFromDisk();
        _graphicsSettingsReady = true;
        ApplyRootGraphicsSettings(GraphicsSettings);
    }

    public override void _ExitTree()
    {
        SceneTree? tree = GetTree();
        if (tree != null)
        {
            tree.NodeAdded -= OnSceneTreeNodeAdded;
        }

        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public void ApplyGraphicsPreset(GraphicsPreset preset)
    {
        UpdateGraphicsSettings(FromPreset(preset));
    }

    public void UpdateGraphicsSettings(GraphicsSettingsData settings)
    {
        GraphicsSettings = Normalize(settings);
        ApplyRootGraphicsSettings(GraphicsSettings);
        SaveGraphicsSettingsToDisk(GraphicsSettings);
        GraphicsSettingsChanged?.Invoke(GraphicsSettings);
    }

    public static SettingsManager? Resolve(Node node)
    {
        return Instance ?? node.GetNodeOrNull<SettingsManager>("/root/SettingsManager");
    }

    public static GraphicsSettingsData FromPreset(GraphicsPreset preset)
    {
        GraphicsPresetProfile profile = GraphicsPresetCatalog.Get(preset);
        return Normalize(new GraphicsSettingsData(
            profile.RenderScalePercent,
            profile.GlowEnabled,
            profile.ShadowQuality != GraphicsShadowQuality.Off,
            profile.ScreenShakeScale,
            profile.FlashReductionEnabled,
            profile.AntiAliasing));
    }

    public static GraphicsSettingsData LoadGraphicsSettingsFromDisk()
    {
        ConfigFile cfg = new();
        if (cfg.Load(SaveFilePath) != Error.Ok)
        {
            return FromPreset(GraphicsPresetCatalog.DefaultPreset);
        }

        if (EnsureSchema(cfg))
        {
            _ = cfg.Save(SaveFilePath);
        }

        return ReadGraphicsSettings(cfg);
    }

    public static void SaveGraphicsSettingsToDisk(GraphicsSettingsData settings)
    {
        ConfigFile cfg = new();
        _ = cfg.Load(SaveFilePath);

        EnsureSchema(cfg);
        WriteGraphicsSettings(cfg, settings);

        Error error = cfg.Save(SaveFilePath);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Failed to save graphics settings: {error}");
        }
    }

    public static bool EnsureSchema(ConfigFile cfg)
    {
        int version = Mathf.Max(0, cfg.GetValue(MetaSection, SchemaVersionKey, 0).AsInt32());
        bool changed = version != CurrentSchemaVersion;

        if (version < 2)
        {
            cfg.SetValue(
                GraphicsSection,
                GraphicsPresetKey,
                GraphicsPresetCatalog.ToConfigValue(GraphicsPresetCatalog.DefaultPreset));
        }

        if (version < 3)
        {
            WriteGraphicsSettings(cfg, FromPreset(ReadStoredPreset(cfg)));
        }

        if (version >= 3 && version < 4 && !cfg.HasSectionKey(GraphicsSection, AntiAliasingKey))
        {
            GraphicsAntiAliasingMode defaultAntiAliasing = GraphicsPresetCatalog.Get(ReadStoredPreset(cfg)).AntiAliasing;
            cfg.SetValue(GraphicsSection, AntiAliasingKey, GraphicsPresetCatalog.ToConfigValue(defaultAntiAliasing));
        }

        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        return changed;
    }

    public static bool TryResolvePreset(GraphicsSettingsData settings, out GraphicsPreset preset)
    {
        GraphicsSettingsData normalized = Normalize(settings);
        foreach (GraphicsPresetProfile profile in GraphicsPresetCatalog.All)
        {
            if (normalized.Equals(FromPreset(profile.Preset)))
            {
                preset = profile.Preset;
                return true;
            }
        }

        preset = GraphicsPresetCatalog.DefaultPreset;
        return false;
    }

    private void ApplyRootGraphicsSettings(GraphicsSettingsData settings)
    {
        Viewport? root = GetTree()?.Root;
        if (root == null)
        {
            return;
        }

        ApplyNativeResolutionUiPolicy(root);
        ApplyGraphicsSettingsToBranch(root, settings);
    }

    private void OnSceneTreeNodeAdded(Node node)
    {
        if (!_graphicsSettingsReady || node == this)
        {
            return;
        }

        ApplyGraphicsSettingsToNode(node, GraphicsSettings);
    }

    private static void ApplyNativeResolutionUiPolicy(Viewport root)
    {
        if (root is Window window)
        {
            window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        }
    }

    public static void ApplyGraphicsSettingsToBranch(Node node, GraphicsSettingsData settings)
    {
        ApplyGraphicsSettingsToNode(node, settings);

        foreach (Node child in node.GetChildren())
        {
            ApplyGraphicsSettingsToBranch(child, settings);
        }
    }

    private static void ApplyGraphicsSettingsToNode(Node node, GraphicsSettingsData settings)
    {
        if (node is Viewport viewport)
        {
            viewport.Scaling3DScale = settings.RenderScalePercent / 100f;
            ApplyAntiAliasing(viewport, settings.AntiAliasing);
        }

        if (node is WorldEnvironment worldEnvironment && worldEnvironment.Environment != null)
        {
            worldEnvironment.Environment.GlowEnabled = settings.GlowEnabled;
        }

        if (node is Light3D light)
        {
            if (!light.HasMeta(OriginalShadowMeta))
            {
                light.SetMeta(OriginalShadowMeta, light.ShadowEnabled);
            }

            bool originalShadowEnabled = light.GetMeta(OriginalShadowMeta).AsBool();
            light.ShadowEnabled = settings.ShadowsEnabled && originalShadowEnabled;

            if (light.Name.ToString().Contains("Glow", StringComparison.OrdinalIgnoreCase))
            {
                if (!light.HasMeta(OriginalVisibleMeta))
                {
                    light.SetMeta(OriginalVisibleMeta, light.Visible);
                }

                light.Visible = settings.GlowEnabled && light.GetMeta(OriginalVisibleMeta).AsBool();
            }
        }

        if (node is GeometryInstance3D geometry)
        {
            if (!geometry.HasMeta(OriginalCastShadowMeta))
            {
                geometry.SetMeta(OriginalCastShadowMeta, (int)geometry.CastShadow);
            }

            GeometryInstance3D.ShadowCastingSetting originalCastShadow =
                (GeometryInstance3D.ShadowCastingSetting)geometry.GetMeta(OriginalCastShadowMeta).AsInt32();
            geometry.CastShadow = settings.ShadowsEnabled
                ? originalCastShadow
                : GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    private static GraphicsSettingsData Normalize(GraphicsSettingsData settings)
    {
        GraphicsAntiAliasingMode antiAliasing = Enum.IsDefined(typeof(GraphicsAntiAliasingMode), settings.AntiAliasing)
            ? settings.AntiAliasing
            : GraphicsPresetCatalog.Get(GraphicsPresetCatalog.DefaultPreset).AntiAliasing;

        return settings with
        {
            RenderScalePercent = Mathf.Clamp(settings.RenderScalePercent, 50, 200),
            ScreenShakeScale = Mathf.Clamp(settings.ScreenShakeScale, 0f, 1f),
            AntiAliasing = antiAliasing,
        };
    }

    private static void ApplyAntiAliasing(Viewport viewport, GraphicsAntiAliasingMode antiAliasing)
    {
        viewport.UseTaa = false;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.Msaa2D = Viewport.Msaa.Disabled;
        viewport.Msaa3D = Viewport.Msaa.Disabled;

        switch (antiAliasing)
        {
            case GraphicsAntiAliasingMode.Fxaa:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                break;
            case GraphicsAntiAliasingMode.Smaa:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
                break;
            case GraphicsAntiAliasingMode.Msaa2X:
                viewport.Msaa3D = Viewport.Msaa.Msaa2X;
                break;
            case GraphicsAntiAliasingMode.Msaa4X:
                viewport.Msaa3D = Viewport.Msaa.Msaa4X;
                break;
            case GraphicsAntiAliasingMode.Msaa8X:
                viewport.Msaa3D = Viewport.Msaa.Msaa8X;
                break;
        }
    }

    private static GraphicsPreset ReadStoredPreset(ConfigFile cfg)
    {
        string rawPreset = cfg.GetValue(
            GraphicsSection,
            GraphicsPresetKey,
            GraphicsPresetCatalog.ToConfigValue(GraphicsPresetCatalog.DefaultPreset)).AsString();
        return GraphicsPresetCatalog.ParseOrDefault(rawPreset);
    }

    private static GraphicsSettingsData ReadGraphicsSettings(ConfigFile cfg)
    {
        GraphicsSettingsData defaults = FromPreset(ReadStoredPreset(cfg));
        return Normalize(new GraphicsSettingsData(
            cfg.GetValue(GraphicsSection, RenderScalePercentKey, defaults.RenderScalePercent).AsInt32(),
            cfg.GetValue(GraphicsSection, GlowEnabledKey, defaults.GlowEnabled).AsBool(),
            cfg.GetValue(GraphicsSection, ShadowsEnabledKey, defaults.ShadowsEnabled).AsBool(),
            cfg.GetValue(GraphicsSection, ScreenShakeScaleKey, defaults.ScreenShakeScale).AsSingle(),
            cfg.GetValue(GraphicsSection, FlashReductionEnabledKey, defaults.FlashReductionEnabled).AsBool(),
            GraphicsPresetCatalog.ParseAntiAliasingOrDefault(
                cfg.GetValue(
                    GraphicsSection,
                    AntiAliasingKey,
                    GraphicsPresetCatalog.ToConfigValue(defaults.AntiAliasing)).AsString(),
                defaults.AntiAliasing)));
    }

    private static void WriteGraphicsSettings(ConfigFile cfg, GraphicsSettingsData settings)
    {
        GraphicsSettingsData normalized = Normalize(settings);
        cfg.SetValue(MetaSection, SchemaVersionKey, CurrentSchemaVersion);
        cfg.SetValue(GraphicsSection, RenderScalePercentKey, normalized.RenderScalePercent);
        cfg.SetValue(GraphicsSection, AntiAliasingKey, GraphicsPresetCatalog.ToConfigValue(normalized.AntiAliasing));
        cfg.SetValue(GraphicsSection, GlowEnabledKey, normalized.GlowEnabled);
        cfg.SetValue(GraphicsSection, ShadowsEnabledKey, normalized.ShadowsEnabled);
        cfg.SetValue(GraphicsSection, ScreenShakeScaleKey, normalized.ScreenShakeScale);
        cfg.SetValue(GraphicsSection, FlashReductionEnabledKey, normalized.FlashReductionEnabled);

        if (TryResolvePreset(normalized, out GraphicsPreset preset))
        {
            cfg.SetValue(GraphicsSection, GraphicsPresetKey, GraphicsPresetCatalog.ToConfigValue(preset));
        }
    }
}