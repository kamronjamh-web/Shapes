using System;
using Godot;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

public partial class SettingsModalUI : ModalBase
{
    [Export] public OptionButton? GraphicsPresetOption;
    [Export] public Label? GraphicsPresetSummaryLabel;
    [Export] public HSlider? RenderScaleSlider;
    [Export] public OptionButton? AntiAliasingOption;
    [Export] public CheckButton? GlowToggle;
    [Export] public CheckButton? ShadowsToggle;
    [Export] public HSlider? ScreenShakeSlider;
    [Export] public CheckButton? FlashReductionToggle;

    [Export] public HSlider? MasterVolumeSlider;
    [Export] public HSlider? SfxVolumeSlider;
    [Export] public HSlider? MusicVolumeSlider;

    [Export] public Label? RenderScaleValueLabel;
    [Export] public Label? ScreenShakeValueLabel;
    [Export] public Label? MasterValueLabel;
    [Export] public Label? SfxValueLabel;
    [Export] public Label? MusicValueLabel;

    [Export] public NodePath AudioManagerPath = new();

    private const int CustomPresetId = -1;

    private IAudioManager? _audioManager;
    private SettingsManager? _settingsManager;
    private bool _suppressCallbacks;
    private int _customPresetIndex = -1;

    public override void _Ready()
    {
        base._Ready();

        ResolveControlReferences();
        ResolveAudioManager();
        ResolveSettingsManager();
        ConfigureGraphicsPresetOption();
        ConfigureGraphicsControls();
        ConfigureSlider(MasterVolumeSlider, 0, 1, 0.01, OnMasterSliderChanged);
        ConfigureSlider(SfxVolumeSlider, 0, 1, 0.01, OnSfxSliderChanged);
        ConfigureSlider(MusicVolumeSlider, 0, 1, 0.01, OnMusicSliderChanged);

        LoadSettings();
    }

    public override void Open()
    {
        base.Open();
        ResolveControlReferences();
        ResolveSettingsManager();
        LoadSettings();
    }

    public static GraphicsPreset LoadGraphicsPreset()
    {
        GraphicsSettingsData settings = SettingsManager.LoadGraphicsSettingsFromDisk();
        return SettingsManager.TryResolvePreset(settings, out GraphicsPreset preset)
            ? preset
            : GraphicsPresetCatalog.DefaultPreset;
    }

    public static GraphicsPresetProfile LoadGraphicsPresetProfile()
    {
        return GraphicsPresetCatalog.Get(LoadGraphicsPreset());
    }

    public static void SaveGraphicsPreset(GraphicsPreset preset)
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ApplyGraphicsPreset(preset);
            return;
        }

        SettingsManager.SaveGraphicsSettingsToDisk(SettingsManager.FromPreset(preset));
    }

    private void ResolveAudioManager()
    {
        if (!AudioManagerPath.IsEmpty)
        {
            Node? audioNode = GetNodeOrNull(AudioManagerPath);
            if (audioNode is IAudioManager typed)
            {
                _audioManager = typed;
                return;
            }
        }

        _audioManager ??= GetNodeOrNull("/root/AudioManager") as IAudioManager;
    }

    private void ResolveControlReferences()
    {
        GraphicsPresetOption ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/GraphicsPresetRow/GraphicsPresetOption");
        GraphicsPresetSummaryLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/GraphicsPresetSummary");
        RenderScaleSlider ??= GetNodeOrNull<HSlider>("ModalPanel/VBox/RenderScaleRow/RenderScaleSlider");
        AntiAliasingOption ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/AntiAliasingRow/AntiAliasingOption");
        GlowToggle ??= GetNodeOrNull<CheckButton>("ModalPanel/VBox/GlowRow/GlowToggle");
        ShadowsToggle ??= GetNodeOrNull<CheckButton>("ModalPanel/VBox/ShadowsRow/ShadowsToggle");
        ScreenShakeSlider ??= GetNodeOrNull<HSlider>("ModalPanel/VBox/ScreenShakeRow/ScreenShakeSlider");
        FlashReductionToggle ??= GetNodeOrNull<CheckButton>("ModalPanel/VBox/FlashReductionRow/FlashReductionToggle");

        MasterVolumeSlider ??= GetNodeOrNull<HSlider>("ModalPanel/VBox/MasterRow/MasterSlider");
        SfxVolumeSlider ??= GetNodeOrNull<HSlider>("ModalPanel/VBox/SfxRow/SfxSlider");
        MusicVolumeSlider ??= GetNodeOrNull<HSlider>("ModalPanel/VBox/MusicRow/MusicSlider");

        RenderScaleValueLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/RenderScaleRow/RenderScaleValue");
        ScreenShakeValueLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/ScreenShakeRow/ScreenShakeValue");
        MasterValueLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/MasterRow/MasterValue");
        SfxValueLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/SfxRow/SfxValue");
        MusicValueLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/MusicRow/MusicValue");
    }

    private void ResolveSettingsManager()
    {
        _settingsManager = SettingsManager.Resolve(this);
    }

    private SettingsManager? GetSettingsManager()
    {
        _settingsManager ??= SettingsManager.Resolve(this);
        return _settingsManager;
    }

    private static void ConfigureSlider(
        HSlider? slider,
        double minValue,
        double maxValue,
        double step,
        Godot.Range.ValueChangedEventHandler callback)
    {
        if (slider == null)
        {
            return;
        }

        slider.MinValue = minValue;
        slider.MaxValue = maxValue;
        slider.Step = step;
        slider.Editable = true;
        slider.ValueChanged += callback;
    }

    private void ConfigureGraphicsControls()
    {
        ConfigureSlider(RenderScaleSlider, 50, 200, 5, OnRenderScaleSliderChanged);
        ConfigureSlider(ScreenShakeSlider, 0, 1, 0.05, OnScreenShakeSliderChanged);
        ConfigureAntiAliasingOption();

        if (GlowToggle != null)
        {
            GlowToggle.Toggled += OnGlowToggled;
        }

        if (ShadowsToggle != null)
        {
            ShadowsToggle.Toggled += OnShadowsToggled;
        }

        if (FlashReductionToggle != null)
        {
            FlashReductionToggle.Toggled += OnFlashReductionToggled;
        }
    }

    private void LoadSettings()
    {
        float master = 1f;
        float sfx = 1f;
        float music = 1f;
        SettingsManager? settingsManager = GetSettingsManager();
        GraphicsSettingsData graphicsSettings = settingsManager?.GraphicsSettings ?? SettingsManager.LoadGraphicsSettingsFromDisk();

        if (_audioManager != null)
        {
            master = _audioManager.MasterVolume;
            sfx = _audioManager.SfxVolume;
            music = _audioManager.MusicVolume;
        }
        else
        {
            ConfigFile cfg = new();
            if (cfg.Load(SettingsManager.SaveFilePath) == Error.Ok)
            {
                if (EnsureSchema(cfg))
                {
                    _ = cfg.Save(SettingsManager.SaveFilePath);
                }

                master = cfg.GetValue(SettingsManager.AudioSection, "master", 1.0f).AsSingle();
                sfx = cfg.GetValue(SettingsManager.AudioSection, "sfx", 1.0f).AsSingle();
                music = cfg.GetValue(SettingsManager.AudioSection, "music", 1.0f).AsSingle();
            }
        }

        _suppressCallbacks = true;

        if (MasterVolumeSlider != null)
        {
            MasterVolumeSlider.Value = master;
        }

        if (SfxVolumeSlider != null)
        {
            SfxVolumeSlider.Value = sfx;
        }

        if (MusicVolumeSlider != null)
        {
            MusicVolumeSlider.Value = music;
        }

        _suppressCallbacks = false;

        ApplyGraphicsSettingsToControls(graphicsSettings);
        RefreshValueLabels();
    }

    private void ConfigureGraphicsPresetOption()
    {
        if (GraphicsPresetOption == null)
        {
            return;
        }

        GraphicsPresetOption.Clear();
        foreach (GraphicsPresetProfile profile in GraphicsPresetCatalog.All)
        {
            GraphicsPresetOption.AddItem(profile.DisplayName, (int)profile.Preset);
        }

        GraphicsPresetOption.AddItem("Custom", CustomPresetId);
        _customPresetIndex = GraphicsPresetOption.ItemCount - 1;

        GraphicsPresetOption.ItemSelected += OnGraphicsPresetSelected;
    }

    private void ConfigureAntiAliasingOption()
    {
        if (AntiAliasingOption == null)
        {
            return;
        }

        AntiAliasingOption.Clear();
        foreach (GraphicsAntiAliasingMode antiAliasing in GraphicsPresetCatalog.AntiAliasingModes)
        {
            AntiAliasingOption.AddItem(GraphicsPresetCatalog.GetAntiAliasingDisplayName(antiAliasing), (int)antiAliasing);
        }

        AntiAliasingOption.ItemSelected += OnAntiAliasingSelected;
    }

    private void OnGraphicsPresetSelected(long index)
    {
        if (_suppressCallbacks || GraphicsPresetOption == null)
        {
            return;
        }

        int selectedId = GraphicsPresetOption.GetItemId((int)index);
        if (!Enum.IsDefined(typeof(GraphicsPreset), selectedId))
        {
            RefreshGraphicsPresetSummary(BuildGraphicsSettingsFromControls());
            return;
        }

        GraphicsPreset preset = (GraphicsPreset)selectedId;
        GraphicsSettingsData settings = SettingsManager.FromPreset(preset);

        SettingsManager? settingsManager = GetSettingsManager();
        if (settingsManager != null)
        {
            settingsManager.ApplyGraphicsPreset(preset);
            settings = settingsManager.GraphicsSettings;
        }
        else
        {
            SettingsManager.SaveGraphicsSettingsToDisk(settings);
        }

        ApplyGraphicsSettingsToControls(settings);
    }

    private void OnRenderScaleSliderChanged(double value)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        ApplyGraphicsSettingsFromControls();
    }

    private void OnGlowToggled(bool pressed)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        ApplyGraphicsSettingsFromControls();
    }

    private void OnAntiAliasingSelected(long index)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        ApplyGraphicsSettingsFromControls();
    }

    private void OnShadowsToggled(bool pressed)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        ApplyGraphicsSettingsFromControls();
    }

    private void OnScreenShakeSliderChanged(double value)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        ApplyGraphicsSettingsFromControls();
    }

    private void OnFlashReductionToggled(bool pressed)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        ApplyGraphicsSettingsFromControls();
    }

    private void OnMasterSliderChanged(double value)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        SetAudioAndSave((float)value, (float)(SfxVolumeSlider?.Value ?? 1f), (float)(MusicVolumeSlider?.Value ?? 1f));
    }

    private void OnSfxSliderChanged(double value)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        SetAudioAndSave((float)(MasterVolumeSlider?.Value ?? 1f), (float)value, (float)(MusicVolumeSlider?.Value ?? 1f));
    }

    private void OnMusicSliderChanged(double value)
    {
        if (_suppressCallbacks)
        {
            return;
        }

        SetAudioAndSave((float)(MasterVolumeSlider?.Value ?? 1f), (float)(SfxVolumeSlider?.Value ?? 1f), (float)value);
    }

    private void SetAudioAndSave(float master, float sfx, float music)
    {
        master = Mathf.Clamp(master, 0f, 1f);
        sfx = Mathf.Clamp(sfx, 0f, 1f);
        music = Mathf.Clamp(music, 0f, 1f);

        if (_audioManager != null)
        {
            _audioManager.MasterVolume = master;
            _audioManager.SfxVolume = sfx;
            _audioManager.MusicVolume = music;
            _audioManager.ApplyVolumes();
        }

        ConfigFile cfg = new();
        _ = cfg.Load(SettingsManager.SaveFilePath);

        cfg.SetValue(SettingsManager.MetaSection, SettingsManager.SchemaVersionKey, SettingsManager.CurrentSchemaVersion);
        cfg.SetValue(SettingsManager.AudioSection, "master", master);
        cfg.SetValue(SettingsManager.AudioSection, "sfx", sfx);
        cfg.SetValue(SettingsManager.AudioSection, "music", music);

        Error error = cfg.Save(SettingsManager.SaveFilePath);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Failed to save settings: {error}");
        }

        RefreshValueLabels();
    }

    private void ApplyGraphicsSettingsToControls(GraphicsSettingsData settings)
    {
        _suppressCallbacks = true;

        UpdateGraphicsPresetSelection(settings);

        if (RenderScaleSlider != null)
        {
            RenderScaleSlider.Value = settings.RenderScalePercent;
        }

        SelectAntiAliasing(settings.AntiAliasing);

        if (GlowToggle != null)
        {
            GlowToggle.ButtonPressed = settings.GlowEnabled;
        }

        if (ShadowsToggle != null)
        {
            ShadowsToggle.ButtonPressed = settings.ShadowsEnabled;
        }

        if (ScreenShakeSlider != null)
        {
            ScreenShakeSlider.Value = settings.ScreenShakeScale;
        }

        if (FlashReductionToggle != null)
        {
            FlashReductionToggle.ButtonPressed = settings.FlashReductionEnabled;
        }

        _suppressCallbacks = false;
        RefreshGraphicsPresetSummary(settings);
        RefreshValueLabels();
    }

    private void UpdateGraphicsPresetSelection(GraphicsSettingsData settings)
    {
        if (GraphicsPresetOption == null)
        {
            return;
        }

        if (SettingsManager.TryResolvePreset(settings, out GraphicsPreset preset))
        {
            GraphicsPresetOption.Select((int)preset);
            return;
        }

        if (_customPresetIndex >= 0)
        {
            GraphicsPresetOption.Select(_customPresetIndex);
        }
    }

    private void SelectAntiAliasing(GraphicsAntiAliasingMode antiAliasing)
    {
        if (AntiAliasingOption == null)
        {
            return;
        }

        for (int i = 0; i < AntiAliasingOption.ItemCount; i++)
        {
            if (AntiAliasingOption.GetItemId(i) == (int)antiAliasing)
            {
                AntiAliasingOption.Select(i);
                return;
            }
        }
    }

    private GraphicsSettingsData BuildGraphicsSettingsFromControls()
    {
        GraphicsSettingsData fallback = GetSettingsManager()?.GraphicsSettings ?? SettingsManager.LoadGraphicsSettingsFromDisk();
        GraphicsAntiAliasingMode antiAliasing = fallback.AntiAliasing;

        if (AntiAliasingOption != null && AntiAliasingOption.Selected >= 0)
        {
            int selectedId = AntiAliasingOption.GetSelectedId();
            if (Enum.IsDefined(typeof(GraphicsAntiAliasingMode), selectedId))
            {
                antiAliasing = (GraphicsAntiAliasingMode)selectedId;
            }
        }

        return new GraphicsSettingsData(
            Mathf.RoundToInt((float)(RenderScaleSlider?.Value ?? fallback.RenderScalePercent)),
            GlowToggle?.ButtonPressed ?? fallback.GlowEnabled,
            ShadowsToggle?.ButtonPressed ?? fallback.ShadowsEnabled,
            (float)(ScreenShakeSlider?.Value ?? fallback.ScreenShakeScale),
            FlashReductionToggle?.ButtonPressed ?? fallback.FlashReductionEnabled,
            antiAliasing);
    }

    private void ApplyGraphicsSettingsFromControls()
    {
        GraphicsSettingsData settings = BuildGraphicsSettingsFromControls();

        SettingsManager? settingsManager = GetSettingsManager();
        if (settingsManager != null)
        {
            settingsManager.UpdateGraphicsSettings(settings);
            settings = settingsManager.GraphicsSettings;
        }
        else
        {
            SettingsManager.SaveGraphicsSettingsToDisk(settings);
        }

        _suppressCallbacks = true;
        UpdateGraphicsPresetSelection(settings);
        _suppressCallbacks = false;

        RefreshGraphicsPresetSummary(settings);
        RefreshValueLabels();
    }

    private void RefreshValueLabels()
    {
        if (RenderScaleValueLabel != null)
        {
            RenderScaleValueLabel.Text = $"{RenderScaleSlider?.Value ?? 100.0:0}%";
        }

        if (ScreenShakeValueLabel != null)
        {
            ScreenShakeValueLabel.Text = $"{(ScreenShakeSlider?.Value ?? 1.0) * 100.0:0}%";
        }

        if (MasterValueLabel != null)
        {
            MasterValueLabel.Text = $"{(MasterVolumeSlider?.Value ?? 1.0) * 100.0:0}%";
        }

        if (SfxValueLabel != null)
        {
            SfxValueLabel.Text = $"{(SfxVolumeSlider?.Value ?? 1.0) * 100.0:0}%";
        }

        if (MusicValueLabel != null)
        {
            MusicValueLabel.Text = $"{(MusicVolumeSlider?.Value ?? 1.0) * 100.0:0}%";
        }
    }

    private void RefreshGraphicsPresetSummary(GraphicsSettingsData? settingsOverride = null)
    {
        if (GraphicsPresetSummaryLabel == null)
        {
            return;
        }

        GraphicsSettingsData settings = settingsOverride ?? (_settingsManager?.GraphicsSettings ?? BuildGraphicsSettingsFromControls());
        GraphicsPresetSummaryLabel.Text = FormatGraphicsPresetSummary(settings);
    }

    private static string FormatGraphicsPresetSummary(GraphicsSettingsData settings)
    {
        string summary = SettingsManager.TryResolvePreset(settings, out GraphicsPreset preset)
            ? GraphicsPresetCatalog.Get(preset).Summary
            : "Custom mix focused on your current preference.";

        return string.Join('\n',
            summary,
            $"3D Render Scale: {settings.RenderScalePercent}%   AA: {GraphicsPresetCatalog.GetAntiAliasingDisplayName(settings.AntiAliasing)}",
            $"Shadows: {(settings.ShadowsEnabled ? "On" : "Off")}",
            $"Glow: {(settings.GlowEnabled ? "On" : "Off")}   Screen Shake: {settings.ScreenShakeScale * 100f:0}%",
            $"Flash Reduction: {(settings.FlashReductionEnabled ? "On" : "Off")}");
    }

    private static bool EnsureSchema(ConfigFile cfg)
    {
        return SettingsManager.EnsureSchema(cfg);
    }
}

public interface IAudioManager
{
    float MasterVolume { get; set; }
    float SfxVolume { get; set; }
    float MusicVolume { get; set; }
    void ApplyVolumes();
}
