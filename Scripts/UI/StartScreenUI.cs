using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

public partial class StartScreenUI : Control
{
    [Export] public Button? EnterHubButton;
    [Export] public Button? SettingsButton;
    [Export] public Button? QuitButton;
    [Export] public ShapePreviewViewport? ShowcasePreview;
    [Export] public Label? ShowcaseNameLabel;
    [Export] public Label? ShowcaseMetaLabel;
    [Export] public PackedScene? SettingsModalScene;
    [Export] public string HubScenePath = "res://Scenes/UI/StartHub.tscn";

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<ShapeData> _showcaseShapes = new();
    private readonly List<RarityData> _showcaseRarities = new();
    private readonly List<ModifierData> _showcaseEssences = new();

    private ModalManager? _modalManager;
    private ModalBase? _settingsModalInstance;

    public override void _Ready()
    {
        _rng.Randomize();
        ResolveUiReferences();
        ResolveServices();
        ApplyPresentation();
        BuildShowcasePools();
        WireButtons();
        ShowRandomShowcaseItem();
        EnterHubButton?.GrabFocus();
    }

    private void ResolveUiReferences()
    {
        EnterHubButton ??= GetNodeOrNull<Button>("SafeArea/Layout/LeftPanel/ButtonsRail/EnterHubButton")
            ?? GetNodeOrNull<Button>("SafeArea/Layout/LeftPanel/PanelMargin/Content/Buttons/EnterHubButton");
        SettingsButton ??= GetNodeOrNull<Button>("SafeArea/Layout/LeftPanel/ButtonsRail/SettingsButton")
            ?? GetNodeOrNull<Button>("SafeArea/Layout/LeftPanel/PanelMargin/Content/Buttons/SettingsButton");
        QuitButton ??= GetNodeOrNull<Button>("SafeArea/Layout/LeftPanel/ButtonsRail/QuitButton")
            ?? GetNodeOrNull<Button>("SafeArea/Layout/LeftPanel/PanelMargin/Content/Buttons/QuitButton");
        ShowcasePreview ??= GetNodeOrNull<ShapePreviewViewport>("SafeArea/Layout/RightPane/ShowcasePreview");
        ShowcaseNameLabel ??= GetNodeOrNull<Label>("SafeArea/Layout/LeftPanel/PanelMargin/Content/ShowcaseNameLabel");
        ShowcaseMetaLabel ??= GetNodeOrNull<Label>("SafeArea/Layout/LeftPanel/PanelMargin/Content/ShowcaseMetaLabel");
    }

    private void ResolveServices()
    {
        _modalManager = GetNodeOrNull<ModalManager>("/root/ModalManager");
        SettingsModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/SettingsModal.tscn");
    }

    private void ApplyPresentation()
    {
        if (GetNodeOrNull<ColorRect>("Background") is ColorRect background)
        {
            background.Color = new Color(0.006f, 0.012f, 0.020f, 1f);
        }

        if (GetNodeOrNull<ColorRect>("BackgroundGlow") is ColorRect backgroundGlow)
        {
            backgroundGlow.Color = new Color(0.055f, 0.115f, 0.160f, 0.30f);
        }

        ApplyButtonStyle(EnterHubButton, new Color(0.88f, 0.72f, 0.20f, 0.84f), new Color(1f, 0.93f, 0.55f, 0.92f), new Color(0.05f, 0.05f, 0.07f, 1f));
        ApplyButtonStyle(SettingsButton, new Color(0.030f, 0.056f, 0.088f, 0.78f), new Color(0.285f, 0.410f, 0.520f, 0.86f), Colors.White);
        ApplyButtonStyle(QuitButton, new Color(0.030f, 0.056f, 0.088f, 0.78f), new Color(0.285f, 0.410f, 0.520f, 0.86f), Colors.White);
    }

    private void BuildShowcasePools()
    {
        _showcaseShapes.Clear();
        _showcaseRarities.Clear();
        _showcaseEssences.Clear();

        List<ShapeData> allShapes = EquipmentCatalogDefaults.CreateDefaultShapes();
        for (int shapeIndex = 0; shapeIndex < allShapes.Count; shapeIndex++)
        {
            ShapeData shape = allShapes[shapeIndex];
            if (shape.ShapeGroup >= ShapeGroup.ThreeD)
            {
                _showcaseShapes.Add(shape);
            }
        }

        if (_showcaseShapes.Count == 0)
        {
            _showcaseShapes.AddRange(allShapes);
        }

        List<RarityData> allRarities = EquipmentCatalogDefaults.CreateDefaultRarities();
        for (int rarityIndex = 0; rarityIndex < allRarities.Count; rarityIndex++)
        {
            RarityData rarity = allRarities[rarityIndex];
            if (!string.Equals(rarity.Key, "common", StringComparison.OrdinalIgnoreCase))
            {
                _showcaseRarities.Add(rarity);
            }
        }

        if (_showcaseRarities.Count == 0)
        {
            _showcaseRarities.AddRange(allRarities);
        }

        List<ModifierData> allEssences = EquipmentCatalogDefaults.CreateDefaultModifiers(false);
        for (int essenceIndex = 0; essenceIndex < allEssences.Count; essenceIndex++)
        {
            ModifierData essence = allEssences[essenceIndex];
            if (essence.Key is ModifierKey.Shiny or ModifierKey.Electric or ModifierKey.Unstable)
            {
                _showcaseEssences.Add(essence);
            }
        }
    }

    private void WireButtons()
    {
        if (EnterHubButton != null)
        {
            EnterHubButton.Pressed += EnterHub;
        }

        if (SettingsButton != null)
        {
            SettingsButton.Pressed += OpenSettings;
        }

        if (QuitButton != null)
        {
            QuitButton.Pressed += QuitGame;
        }
    }

    private void ShowRandomShowcaseItem()
    {
        EquipmentItem showcaseItem = BuildRandomShowcaseItem();
        ShowcasePreview?.SetItem(showcaseItem);

        if (ShowcaseNameLabel != null)
        {
            ShowcaseNameLabel.Text = showcaseItem.Shape?.DisplayName ?? "Showcase";
        }

        if (ShowcaseMetaLabel != null)
        {
            string rarityName = showcaseItem.Rarity?.DisplayName ?? "Unknown";
            string essenceName = showcaseItem.HasEquippedEssence && showcaseItem.Essence != null
                ? $"{showcaseItem.Essence.DisplayName} Essence"
                : "Pure Core";
            ShowcaseMetaLabel.Text = $"{rarityName} | {essenceName}";
        }
    }

    private EquipmentItem BuildRandomShowcaseItem()
    {
        ShapeData shape = PickRandom(_showcaseShapes);
        RarityData rarity = PickRandom(_showcaseRarities);
        EquipmentItem item = new()
        {
            Shape = shape,
            Rarity = rarity,
            Level = EquipmentCatalogDefaults.MaxWeaponLevel,
            Stars = shape.Stars,
        };

        bool canUseEssence = item.HasEssenceSlotUnlocked && _showcaseEssences.Count > 0;
        bool shouldAddEssence = canUseEssence && _rng.Randf() < 0.78f;
        if (shouldAddEssence)
        {
            item.Essence = PickRandom(_showcaseEssences);
        }

        return item;
    }

    private T PickRandom<T>(IList<T> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Cannot pick from an empty showcase pool.");
        }

        return items[_rng.RandiRange(0, items.Count - 1)];
    }

    private void EnterHub()
    {
        if (string.IsNullOrWhiteSpace(HubScenePath) || !FileAccess.FileExists(HubScenePath))
        {
            GD.PushWarning($"Hub scene not found: {HubScenePath}");
            return;
        }

        Error error = GetTree().ChangeSceneToFile(HubScenePath);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Failed to change to hub scene: {error}");
        }
    }

    private void OpenSettings()
    {
        if (_settingsModalInstance != null && GodotObject.IsInstanceValid(_settingsModalInstance) && _settingsModalInstance.IsOpen)
        {
            if (_modalManager != null)
            {
                _modalManager.CloseModal(_settingsModalInstance);
            }
            else
            {
                _settingsModalInstance.Close();
            }

            return;
        }

        if (SettingsModalScene == null)
        {
            GD.PushWarning("Settings modal scene not assigned.");
            return;
        }

        Node modalNode = SettingsModalScene.Instantiate();
        if (modalNode is not ModalBase modal)
        {
            modalNode.QueueFree();
            GD.PushWarning("Settings modal scene does not inherit ModalBase.");
            return;
        }

        AddChild(modal);
        modal.BlockingModal = false;
        modal.ZIndex = 100;
        _settingsModalInstance = modal;
        modal.Closed += () =>
        {
            _settingsModalInstance = null;
            if (GodotObject.IsInstanceValid(modal))
            {
                modal.QueueFree();
            }
        };

        if (_modalManager != null)
        {
            _modalManager.OpenModal(modal);
        }
        else
        {
            modal.Open();
        }
    }

    private void QuitGame()
    {
        GetTree().Quit();
    }

    private static void ApplyButtonStyle(Button? button, Color background, Color border, Color fontColor)
    {
        if (button == null)
        {
            return;
        }

        button.CustomMinimumSize = new Vector2(280f, 58f);
        button.AddThemeFontSizeOverride("font_size", 16);
        button.AddThemeColorOverride("font_color", fontColor);
        button.AddThemeColorOverride("font_hover_color", fontColor);
        button.AddThemeColorOverride("font_pressed_color", fontColor);
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(background, border, 14));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(background.Lightened(0.12f), border.Lightened(0.18f), 14));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(background.Darkened(0.08f), border, 14));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
        };
    }
}