using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Developer panel for forced rolls, progression shortcuts, and simulation utilities.
public partial class DebugPanelUI : Control
{
    [Export] public OptionButton? ForcedRarityDropdown;
    [Export] public OptionButton? ForcedShapeDropdown;
    [Export] public OptionButton? ForcedModifierDropdown;

    [Export] public OptionButton? AugmentTypeDropdown;
    [Export] public OptionButton? AugmentLevelDropdown;

    [Export] public Button? ApplySelectionButton;
    [Export] public Button? ClearSelectionButton;
    [Export] public Button? AddAllShapesButton;
    [Export] public Button? LevelUpButton;
    [Export] public Button? AddRollButton;
    [Export] public Button? AddFatedRollButton;
    [Export] public Button? AddAugmentButton;
    [Export] public Button? SimulateThousandButton;
    [Export] public Button? ResetCountsButton;
    [Export] public Button? DeactivateDebugButton;

    [Export] public Label? StatusLabel;

    [Export] public NodePath DebugManagerPath = new NodePath();
    [Export] public NodePath DebugCommandServicePath = new NodePath();

    [Export] public Godot.Collections.Array<RarityData> RarityOptions = new();
    [Export] public Godot.Collections.Array<ShapeData> ShapeOptions = new();
    [Export] public Godot.Collections.Array<ModifierData> ModifierOptions = new();

    private readonly RandomNumberGenerator _rng = new();

    private DebugManager? _debugManager;
    private DebugCommandService? _commandService;

    public override void _Ready()
    {
        _rng.Randomize();

        ResolveUiReferences();
        ResolveServices();
        EnsureDefaultOptions();
        PopulateDropdowns();
        WireButtons();
        RefreshVisibility();
        RefreshStatusText();
    }

    private void ResolveUiReferences()
    {
        ForcedRarityDropdown ??= GetNodeOrNull<OptionButton>("Panel/VBox/ForcedRarityDropdown");
        ForcedShapeDropdown ??= GetNodeOrNull<OptionButton>("Panel/VBox/ForcedShapeDropdown");
        ForcedModifierDropdown ??= GetNodeOrNull<OptionButton>("Panel/VBox/ForcedModifierDropdown");

        AugmentTypeDropdown ??= GetNodeOrNull<OptionButton>("Panel/VBox/AugmentTypeDropdown");
        AugmentLevelDropdown ??= GetNodeOrNull<OptionButton>("Panel/VBox/AugmentLevelDropdown");

        ApplySelectionButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/ApplySelectionButton");
        ClearSelectionButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/ClearSelectionButton");
        AddAllShapesButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/AddAllShapesButton");
        LevelUpButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/LevelUpButton");
        AddRollButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/AddRollButton");
        AddFatedRollButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/AddFatedRollButton");
        AddAugmentButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/AddAugmentButton");
        SimulateThousandButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/SimulateThousandButton");
        ResetCountsButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/ResetCountsButton");
        DeactivateDebugButton ??= GetNodeOrNull<Button>("Panel/VBox/ButtonGrid/DeactivateDebugButton");

        StatusLabel ??= GetNodeOrNull<Label>("Panel/VBox/StatusLabel");
    }

    private void EnsureDefaultOptions()
    {
        if (RarityOptions.Count == 0)
        {
            List<RarityData> rarities = EquipmentCatalogDefaults.CreateDefaultRarities();
            for (int i = 0; i < rarities.Count; i++)
            {
                RarityOptions.Add(rarities[i]);
            }
        }

        if (ShapeOptions.Count == 0)
        {
            List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
            for (int i = 0; i < shapes.Count; i++)
            {
                ShapeOptions.Add(shapes[i]);
            }
        }

        if (ModifierOptions.Count == 0)
        {
            List<ModifierData> modifiers = EquipmentCatalogDefaults.CreateDefaultModifiers(includeBaselineNormal: true);
            for (int i = 0; i < modifiers.Count; i++)
            {
                ModifierOptions.Add(modifiers[i]);
            }
        }
    }

    private void ResolveServices()
    {
        if (!DebugManagerPath.IsEmpty)
        {
            _debugManager = GetNodeOrNull(DebugManagerPath) as DebugManager;
        }

        _debugManager ??= GetNodeOrNull("/root/DebugManager") as DebugManager;

        if (!DebugCommandServicePath.IsEmpty)
        {
            _commandService = GetNodeOrNull(DebugCommandServicePath) as DebugCommandService;
        }

        _commandService ??= GetNodeOrNull("/root/DebugCommandService") as DebugCommandService;

        if (_debugManager != null)
        {
            _debugManager.DebugActivated += OnDebugStateChanged;
            _debugManager.DebugDeactivated += OnDebugStateChanged;
            _debugManager.ForcedRollChanged += OnForcedRollChanged;
        }
    }

    private void PopulateDropdowns()
    {
        PopulateRarityDropdown();
        PopulateShapeDropdown();
        PopulateModifierDropdown();
        PopulateAugmentDropdowns();
    }

    private void PopulateRarityDropdown()
    {
        if (ForcedRarityDropdown == null)
        {
            return;
        }

        ForcedRarityDropdown.Clear();
        AddOption(ForcedRarityDropdown, "Random", string.Empty);

        for (int i = 0; i < RarityOptions.Count; i++)
        {
            RarityData? rarity = RarityOptions[i];
            if (rarity == null)
            {
                continue;
            }

            AddOption(ForcedRarityDropdown, rarity.DisplayName, rarity.Key);
        }

        ForcedRarityDropdown.Select(0);
    }

    private void PopulateShapeDropdown()
    {
        if (ForcedShapeDropdown == null)
        {
            return;
        }

        ForcedShapeDropdown.Clear();
        AddOption(ForcedShapeDropdown, "Random", string.Empty);

        for (int i = 0; i < ShapeOptions.Count; i++)
        {
            ShapeData? shape = ShapeOptions[i];
            if (shape == null)
            {
                continue;
            }

            AddOption(ForcedShapeDropdown, shape.DisplayName, shape.Key);
        }

        ForcedShapeDropdown.Select(0);
    }

    private void PopulateModifierDropdown()
    {
        if (ForcedModifierDropdown == null)
        {
            return;
        }

        ForcedModifierDropdown.Clear();
        AddOption(ForcedModifierDropdown, "Random", -1);

        if (ModifierOptions.Count > 0)
        {
            for (int i = 0; i < ModifierOptions.Count; i++)
            {
                ModifierData? modifier = ModifierOptions[i];
                if (modifier == null)
                {
                    continue;
                }

                AddOption(ForcedModifierDropdown, modifier.DisplayName, (int)modifier.Key);
            }
        }
        else
        {
            foreach (ModifierKey value in Enum.GetValues<ModifierKey>())
            {
                AddOption(ForcedModifierDropdown, value.ToString(), (int)value);
            }
        }

        ForcedModifierDropdown.Select(0);
    }

    private void PopulateAugmentDropdowns()
    {
        if (AugmentTypeDropdown != null)
        {
            AugmentTypeDropdown.Clear();
            AugmentTypeDropdown.AddItem("Random");
            AugmentTypeDropdown.AddItem("Explosive");
            AugmentTypeDropdown.AddItem("Splitting");
            AugmentTypeDropdown.AddItem("Nova");
            AugmentTypeDropdown.AddItem("Turret");
            AugmentTypeDropdown.Select(0);
        }

        if (AugmentLevelDropdown != null)
        {
            AugmentLevelDropdown.Clear();
            AugmentLevelDropdown.AddItem("Random");
            AugmentLevelDropdown.AddItem("1");
            AugmentLevelDropdown.AddItem("2");
            AugmentLevelDropdown.AddItem("3");
            AugmentLevelDropdown.Select(0);
        }
    }

    private void WireButtons()
    {
        if (ApplySelectionButton != null)
        {
            ApplySelectionButton.Pressed += OnApplySelectionPressed;
        }

        if (ClearSelectionButton != null)
        {
            ClearSelectionButton.Pressed += OnClearSelectionPressed;
        }

        if (AddAllShapesButton != null)
        {
            AddAllShapesButton.Pressed += OnAddAllShapesPressed;
        }

        if (LevelUpButton != null)
        {
            LevelUpButton.Pressed += OnLevelUpPressed;
        }

        if (AddRollButton != null)
        {
            AddRollButton.Pressed += OnAddRollPressed;
        }

        if (AddFatedRollButton != null)
        {
            AddFatedRollButton.Pressed += OnAddFatedRollPressed;
        }

        if (AddAugmentButton != null)
        {
            AddAugmentButton.Pressed += OnAddAugmentPressed;
        }

        if (SimulateThousandButton != null)
        {
            SimulateThousandButton.Pressed += OnSimulateThousandPressed;
        }

        if (ResetCountsButton != null)
        {
            ResetCountsButton.Pressed += OnResetCountsPressed;
        }

        if (DeactivateDebugButton != null)
        {
            DeactivateDebugButton.Pressed += OnDeactivateDebugPressed;
        }
    }

    private void OnApplySelectionPressed()
    {
        if (!EnsureDebugManager())
        {
            SetStatus("Debug manager unavailable.");
            return;
        }

        string rarityKey = GetSelectedStringMetadata(ForcedRarityDropdown);
        string shapeKey = GetSelectedStringMetadata(ForcedShapeDropdown);
        ModifierKey? modifierKey = GetSelectedModifierMetadata(ForcedModifierDropdown);

        _debugManager?.SetForcedRoll(rarityKey, shapeKey, modifierKey);
        SetStatus("Forced roll selection applied.");
    }

    private void OnClearSelectionPressed()
    {
        if (!EnsureDebugManager())
        {
            SetStatus("Debug manager unavailable.");
            return;
        }

        _debugManager?.ClearForcedRoll();

        ForcedRarityDropdown?.Select(0);
        ForcedShapeDropdown?.Select(0);
        ForcedModifierDropdown?.Select(0);

        SetStatus("Forced roll selection cleared.");
    }

    private void OnAddAllShapesPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        int added = _commandService?.AddAllShapes() ?? 0;
        SetStatus($"Added {added} shapes to inventory.");
    }

    private void OnLevelUpPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        _commandService?.LevelUp(1);
        SetStatus("Leveled up by 1.");
    }

    private void OnAddRollPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        _commandService?.AddRollCurrency(1);
        SetStatus("+1 roll currency.");
    }

    private void OnAddFatedRollPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        _commandService?.AddFatedRollCurrency(1);
        SetStatus("+1 fated roll currency.");
    }

    private void OnAddAugmentPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        string type = ResolveAugmentType();
        int level = ResolveAugmentLevel();

        _commandService?.AddAugment(type, level);
        SetStatus($"Added augment: {type} L{level}.");
    }

    private void OnSimulateThousandPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        _commandService?.SimulateRolls(1000, false);
        SetStatus("Simulated 1000 rolls.");
    }

    private void OnResetCountsPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        _commandService?.ResetObservedCounts();
        SetStatus("Observed counts reset.");
    }

    private void OnDeactivateDebugPressed()
    {
        if (!EnsureCommandService())
        {
            SetStatus("Debug command service unavailable.");
            return;
        }

        _commandService?.DeactivateDebug();
        SetStatus("Debug mode deactivated.");
    }

    private void OnDebugStateChanged()
    {
        RefreshVisibility();
        RefreshStatusText();
    }

    private void OnForcedRollChanged(string rarityKey, string shapeKey, bool hasModifier, int modifierValue)
    {
        _ = rarityKey;
        _ = shapeKey;
        _ = hasModifier;
        _ = modifierValue;

        RefreshStatusText();
    }

    private void RefreshVisibility()
    {
        Visible = _debugManager?.DebugActive == true;
    }

    private void RefreshStatusText()
    {
        if (StatusLabel == null)
        {
            return;
        }

        if (_debugManager?.DebugActive != true)
        {
            StatusLabel.Text = "Debug inactive.";
            return;
        }

        string rarity = _debugManager.HasForcedRarity() ? _debugManager.ForcedRarityKey : "random rarity";
        string shape = _debugManager.HasForcedShape() ? _debugManager.ForcedShapeKey : "random shape";
        string modifier = _debugManager.HasForcedModifier()
            ? _debugManager.ForcedModifierKey!.Value.ToString()
            : "random essence";

        StatusLabel.Text = $"Debug active | Next roll: {rarity} / {shape} / {modifier}";
    }

    private void SetStatus(string text)
    {
        if (StatusLabel != null)
        {
            StatusLabel.Text = text;
        }
    }

    private static void AddOption(OptionButton dropdown, string text, Variant metadata)
    {
        dropdown.AddItem(text);
        int index = dropdown.ItemCount - 1;
        dropdown.SetItemMetadata(index, metadata);
    }

    private static string GetSelectedStringMetadata(OptionButton? dropdown)
    {
        if (dropdown == null || dropdown.Selected < 0)
        {
            return string.Empty;
        }

        Variant data = dropdown.GetItemMetadata(dropdown.Selected);
        return data.VariantType == Variant.Type.Nil ? string.Empty : data.AsString();
    }

    private static ModifierKey? GetSelectedModifierMetadata(OptionButton? dropdown)
    {
        if (dropdown == null || dropdown.Selected < 0)
        {
            return null;
        }

        Variant data = dropdown.GetItemMetadata(dropdown.Selected);
        if (data.VariantType == Variant.Type.Nil)
        {
            return null;
        }

        int raw = data.AsInt32();
        if (raw < 0)
        {
            return null;
        }

        return (ModifierKey)raw;
    }

    private string ResolveAugmentType()
    {
        string raw = AugmentTypeDropdown?.GetItemText(AugmentTypeDropdown.Selected) ?? "Random";
        if (!raw.Equals("Random", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        string[] pool = { "Explosive", "Splitting", "Nova", "Turret" };
        int index = _rng.RandiRange(0, pool.Length - 1);
        return pool[index];
    }

    private int ResolveAugmentLevel()
    {
        string raw = AugmentLevelDropdown?.GetItemText(AugmentLevelDropdown.Selected) ?? "Random";
        if (!raw.Equals("Random", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw, out int fixedLevel))
        {
            return Mathf.Clamp(fixedLevel, 1, 3);
        }

        // Random level weights: L3=10%, L2=30%, L1=60%.
        float roll = _rng.Randf();
        if (roll <= 0.10f)
        {
            return 3;
        }

        if (roll <= 0.40f)
        {
            return 2;
        }

        return 1;
    }

    private bool EnsureDebugManager()
    {
        if (_debugManager != null && GodotObject.IsInstanceValid(_debugManager))
        {
            return true;
        }

        _debugManager = GetNodeOrNull("/root/DebugManager") as DebugManager;
        return _debugManager != null;
    }

    private bool EnsureCommandService()
    {
        if (_commandService != null && GodotObject.IsInstanceValid(_commandService))
        {
            return true;
        }

        _commandService = GetNodeOrNull("/root/DebugCommandService") as DebugCommandService;
        return _commandService != null;
    }
}
