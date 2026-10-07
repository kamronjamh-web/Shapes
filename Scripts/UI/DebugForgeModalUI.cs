using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Debug forge UI for constructing custom equipment and sending it into inventory.
public partial class DebugForgeModalUI : ModalBase
{
    private enum DebugModulatorFilterKind
    {
        All,
        Core,
        Projectile,
        Sustain,
        Utility,
        Proc,
        Wing,
    }

    [Signal]
    public delegate void ItemForgedEventHandler(string itemInstanceId);

    [Export] public OptionButton? ShapeDropdown;
    [Export] public OptionButton? RarityDropdown;
    [Export] public OptionButton? ModifierDropdown;

    [Export] public SpinBox? ItemLevelInput;

    [Export] public Label? PreviewHpLabel;
    [Export] public Label? PreviewDamageLabel;
    [Export] public Label? PreviewRarityLabel;
    [Export] public Label? FeedbackLabel;

    [Export] public VBoxContainer? SubstatRowsContainer;
    [Export] public Button? AddSubstatButton;
    [Export] public Button? ForgeButton;
    [Export] public Button? AddAllShapesButton;
    [Export] public Button? AddAllModulatorsButton;
    [Export] public ShapePreviewViewport? ShapePreview;

    [Export] public NodePath ForgeServicePath = new NodePath();
    [Export] public NodePath InventoryManagerPath = new NodePath();
    [Export] public bool AllowDuplicateSubstats = false;

    [ExportGroup("Data Folders")]
    [Export] public string ShapeDataFolder = "res://Resources/Equipment/Shapes";
    [Export] public string RarityDataFolder = "res://Resources/Equipment/Rarities";
    [Export] public string ModifierDataFolder = "res://Resources/Equipment/Modifiers";

    [Export] public Godot.Collections.Array<ShapeData> ShapeOptions = new();
    [Export] public Godot.Collections.Array<RarityData> RarityOptions = new();
    [Export] public Godot.Collections.Array<ModifierData> ModifierOptions = new();

    private readonly Dictionary<string, ShapeData> _shapeByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RarityData> _rarityByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ModifierKey, ModifierData> _modifierByKey = new();
    private readonly Dictionary<string, WeaponModulatorData> _modulatorByKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<SubstatRow> _substatRows = new();

    private DebugForgeService? _forgeService;
    private InventoryManager? _inventoryManager;
    private LineEdit? _modulatorSearchInput;
    private OptionButton? _modulatorFilterDropdown;
    private OptionButton? _modulatorDropdown;
    private OptionButton? _modulatorTierDropdown;
    private Button? _grantModulatorButton;
    private Label? _modulatorDescriptionLabel;

    public override void _Ready()
    {
        base._Ready();

        ResolveUiReferences();
        EnsureModulatorGrantControls();
        ResolveService();
        ResolveInventoryManager();
        ConfigureControls();
        RecalculateModalLayout();
        EnsureDefaultOptions();
        PopulateOptions();

        if (AddSubstatButton != null)
        {
            AddSubstatButton.Pressed += () => AddSubstatRow();
        }

        if (ForgeButton != null)
        {
            ForgeButton.Pressed += ForgeSelectedItem;
        }

        if (AddAllShapesButton != null)
        {
            AddAllShapesButton.Pressed += AddAllSelectedShapes;
        }

        if (AddAllModulatorsButton != null)
        {
            AddAllModulatorsButton.Pressed += AddAllModulatorsToStorage;
        }

        if (_substatRows.Count == 0)
        {
            AddSubstatRow();
        }

        RefreshPreview();
    }

    private void ResolveUiReferences()
    {
        ShapeDropdown ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/Selectors/ShapeDropdown");
        RarityDropdown ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/Selectors/RarityDropdown");
        ModifierDropdown ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/Selectors/ModifierDropdown");
        ItemLevelInput ??= GetNodeOrNull<SpinBox>("ModalPanel/VBox/Selectors/ItemLevelInput");
        PreviewHpLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/PreviewGrid/HpValue");
        PreviewDamageLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/PreviewGrid/DamageValue");
        PreviewRarityLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/PreviewGrid/RarityValue");
        FeedbackLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/FeedbackLabel");
        SubstatRowsContainer ??= GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/SubstatsContainer");
        AddSubstatButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/AddSubstatButton");
        ForgeButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/ForgeButton");
        AddAllShapesButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/AddAllShapesButton");
        AddAllModulatorsButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/AddAllModulatorsButton");
        ShapePreview ??= GetNodeOrNull<ShapePreviewViewport>("ModalPanel/VBox/ShapePreview");

        EnsureShapePreview();
    }

    private void EnsureModulatorGrantControls()
    {
        if (_modulatorSearchInput != null
            && _modulatorFilterDropdown != null
            && _modulatorDropdown != null
            && _modulatorTierDropdown != null
            && _grantModulatorButton != null
            && _modulatorDescriptionLabel != null)
        {
            return;
        }

        VBoxContainer? vbox = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox");
        if (vbox == null)
        {
            return;
        }

        VBoxContainer section = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/ModulatorSection") ?? new VBoxContainer
        {
            Name = "ModulatorSection",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        section.AddThemeConstantOverride("separation", 6);

        Label header = section.GetNodeOrNull<Label>("Header") ?? new Label
        {
            Name = "Header",
            Text = "Add Stored Modulator",
        };

        HBoxContainer filterRow = section.GetNodeOrNull<HBoxContainer>("FilterRow") ?? new HBoxContainer
        {
            Name = "FilterRow",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        filterRow.AddThemeConstantOverride("separation", 8);

        _modulatorSearchInput = filterRow.GetNodeOrNull<LineEdit>("ModulatorSearchInput") ?? new LineEdit
        {
            Name = "ModulatorSearchInput",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "Search modulators",
        };

        _modulatorFilterDropdown = filterRow.GetNodeOrNull<OptionButton>("ModulatorFilterDropdown") ?? new OptionButton
        {
            Name = "ModulatorFilterDropdown",
            CustomMinimumSize = new Vector2(150f, 0f),
        };

        HBoxContainer row = section.GetNodeOrNull<HBoxContainer>("Row") ?? new HBoxContainer
        {
            Name = "Row",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);

        _modulatorDropdown = row.GetNodeOrNull<OptionButton>("ModulatorDropdown") ?? new OptionButton
        {
            Name = "ModulatorDropdown",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        _modulatorTierDropdown = row.GetNodeOrNull<OptionButton>("ModulatorTierDropdown") ?? new OptionButton
        {
            Name = "ModulatorTierDropdown",
            CustomMinimumSize = new Vector2(130f, 0f),
        };

        _grantModulatorButton = row.GetNodeOrNull<Button>("GrantModulatorButton") ?? new Button
        {
            Name = "GrantModulatorButton",
            Text = "Add Modulator",
            CustomMinimumSize = new Vector2(150f, 0f),
        };

        _modulatorDescriptionLabel = section.GetNodeOrNull<Label>("Description") ?? new Label
        {
            Name = "Description",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = string.Empty,
        };
        _modulatorDescriptionLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        if (section.GetParent() == null)
        {
            int insertIndex = FeedbackLabel?.GetIndex() ?? vbox.GetChildCount();
            vbox.AddChild(section);
            vbox.MoveChild(section, Mathf.Clamp(insertIndex, 0, vbox.GetChildCount() - 1));
        }

        if (header.GetParent() != section)
        {
            section.AddChild(header);
        }

        if (filterRow.GetParent() != section)
        {
            section.AddChild(filterRow);
        }

        if (row.GetParent() != section)
        {
            section.AddChild(row);
        }

        if (_modulatorDescriptionLabel.GetParent() != section)
        {
            section.AddChild(_modulatorDescriptionLabel);
        }

        if (_modulatorSearchInput.GetParent() != filterRow)
        {
            filterRow.AddChild(_modulatorSearchInput);
        }

        if (_modulatorFilterDropdown.GetParent() != filterRow)
        {
            filterRow.AddChild(_modulatorFilterDropdown);
        }

        if (_modulatorDropdown.GetParent() != row)
        {
            row.AddChild(_modulatorDropdown);
        }

        if (_modulatorTierDropdown.GetParent() != row)
        {
            row.AddChild(_modulatorTierDropdown);
        }

        if (_grantModulatorButton.GetParent() != row)
        {
            row.AddChild(_grantModulatorButton);
        }
    }

    private void EnsureShapePreview()
    {
        if (ShapePreview != null)
        {
            return;
        }

        VBoxContainer? vbox = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox");
        if (vbox == null)
        {
            return;
        }

        ShapePreview = new ShapePreviewViewport
        {
            Name = "ShapePreview",
            PreviewSize = new Vector2I(440, 170),
            VisualScale = 1.25f,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(ShapePreview);
        vbox.MoveChild(ShapePreview, Mathf.Min(2, vbox.GetChildCount() - 1));
    }

    protected override void OnModalLayoutChanged(Vector2 panelSize, float scale)
    {
        if (ShapePreview == null)
        {
            return;
        }

        int previewWidth = Mathf.RoundToInt(Mathf.Clamp(panelSize.X - 64f, 280f, 440f));
        int previewHeight = Mathf.RoundToInt(Mathf.Clamp(panelSize.Y * 0.24f, 130f, 170f));
        ShapePreview.ApplyPreviewSize(new Vector2I(previewWidth, previewHeight), Mathf.Clamp(1.25f * scale, 0.95f, 1.25f));
    }

    private void EnsureDefaultOptions()
    {
        if (ShapeOptions.Count == 0)
        {
            LoadShapeResourcesFromFolder(ShapeDataFolder);
        }

        if (ShapeOptions.Count == 0)
        {
            List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
            for (int i = 0; i < shapes.Count; i++)
            {
                ShapeOptions.Add(shapes[i]);
            }
        }

        if (RarityOptions.Count == 0)
        {
            LoadRarityResourcesFromFolder(RarityDataFolder);
        }

        if (RarityOptions.Count == 0)
        {
            List<RarityData> rarities = EquipmentCatalogDefaults.CreateDefaultRarities();
            for (int i = 0; i < rarities.Count; i++)
            {
                RarityOptions.Add(rarities[i]);
            }
        }

        if (ModifierOptions.Count == 0)
        {
            LoadModifierResourcesFromFolder(ModifierDataFolder);
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

    private void LoadShapeResourcesFromFolder(string folderPath)
    {
        LoadResourcesFromFolder(folderPath, resource =>
        {
            if (resource is ShapeData shape)
            {
                ShapeOptions.Add(shape);
            }
        });
    }

    private void LoadRarityResourcesFromFolder(string folderPath)
    {
        LoadResourcesFromFolder(folderPath, resource =>
        {
            if (resource is RarityData rarity)
            {
                RarityOptions.Add(rarity);
            }
        });
    }

    private void LoadModifierResourcesFromFolder(string folderPath)
    {
        LoadResourcesFromFolder(folderPath, resource =>
        {
            if (resource is ModifierData modifier)
            {
                ModifierOptions.Add(modifier);
            }
        });
    }

    private static void LoadResourcesFromFolder(string folderPath, Action<Resource> onResourceLoaded)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !DirAccess.DirExistsAbsolute(folderPath))
        {
            return;
        }

        DirAccess? dir = DirAccess.Open(folderPath);
        if (dir == null)
        {
            return;
        }

        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Resource? loaded = ResourceLoader.Load($"{folderPath}/{file}");
            if (loaded != null)
            {
                onResourceLoaded(loaded);
            }
        }
    }

    private void ResolveService()
    {
        if (!ForgeServicePath.IsEmpty)
        {
            _forgeService = GetNodeOrNull(ForgeServicePath) as DebugForgeService;
        }

        _forgeService ??= GetNodeOrNull("/root/DebugForgeService") as DebugForgeService;
    }

    private void ResolveInventoryManager()
    {
        if (!InventoryManagerPath.IsEmpty)
        {
            _inventoryManager = GetNodeOrNull(InventoryManagerPath) as InventoryManager;
        }

        _inventoryManager ??= GetNodeOrNull("/root/InventoryManager") as InventoryManager;
    }

    private void ConfigureControls()
    {
        if (ItemLevelInput != null)
        {
            ItemLevelInput.MinValue = 1;
            ItemLevelInput.MaxValue = EquipmentCatalogDefaults.MaxWeaponLevel;
            ItemLevelInput.Step = 1;
            ItemLevelInput.Value = 1;
            ItemLevelInput.ValueChanged += _ => RefreshPreview();
        }

        if (ShapeDropdown != null)
        {
            ShapeDropdown.ItemSelected += _ => RefreshPreview();
        }

        if (RarityDropdown != null)
        {
            RarityDropdown.ItemSelected += _ =>
            {
                RefreshSubstatRanges();
                RefreshPreview();
            };
        }

        if (ModifierDropdown != null)
        {
            ModifierDropdown.ItemSelected += _ => RefreshPreview();
        }

        if (_modulatorSearchInput != null)
        {
            _modulatorSearchInput.TextChanged += _ => PopulateModulatorDropdown();
        }

        if (_modulatorFilterDropdown != null)
        {
            _modulatorFilterDropdown.ItemSelected += _ => PopulateModulatorDropdown();
        }

        if (_modulatorDropdown != null)
        {
            _modulatorDropdown.ItemSelected += _ => RefreshSelectedModulatorDescription();
        }

        if (_modulatorTierDropdown != null)
        {
            _modulatorTierDropdown.ItemSelected += _ => RefreshSelectedModulatorDescription();
        }

        if (_grantModulatorButton != null)
        {
            _grantModulatorButton.Pressed += GrantSelectedModulator;
        }
    }

    private void PopulateOptions()
    {
        PopulateShapeDropdown();
        PopulateRarityDropdown();
        PopulateModifierDropdown();
        PopulateModulatorFilterDropdown();
        PopulateModulatorDropdown();
        PopulateModulatorTierDropdown();
        RefreshSelectedModulatorDescription();
    }

    private void PopulateShapeDropdown()
    {
        if (ShapeDropdown == null)
        {
            return;
        }

        ShapeDropdown.Clear();
        _shapeByKey.Clear();

        for (int i = 0; i < ShapeOptions.Count; i++)
        {
            ShapeData? shape = ShapeOptions[i];
            if (shape == null)
            {
                continue;
            }

            _shapeByKey[shape.Key] = shape;
            AddOption(ShapeDropdown, $"{shape.DisplayName} {shape.Stars}*", shape.Key);
        }

        if (ShapeDropdown.ItemCount > 0)
        {
            ShapeDropdown.Select(0);
        }
    }

    private void PopulateRarityDropdown()
    {
        if (RarityDropdown == null)
        {
            return;
        }

        RarityDropdown.Clear();
        _rarityByKey.Clear();

        for (int i = 0; i < RarityOptions.Count; i++)
        {
            RarityData? rarity = RarityOptions[i];
            if (rarity == null)
            {
                continue;
            }

            _rarityByKey[rarity.Key] = rarity;
            AddOption(RarityDropdown, rarity.DisplayName, rarity.Key);
        }

        if (RarityDropdown.ItemCount > 0)
        {
            if (!SelectStringMetadata(RarityDropdown, "legendary"))
            {
                RarityDropdown.Select(0);
            }
        }
    }

    private void PopulateModifierDropdown()
    {
        if (ModifierDropdown == null)
        {
            return;
        }

        ModifierDropdown.Clear();
        _modifierByKey.Clear();

        for (int i = 0; i < ModifierOptions.Count; i++)
        {
            ModifierData? modifier = ModifierOptions[i];
            if (modifier == null)
            {
                continue;
            }

            _modifierByKey[modifier.Key] = modifier;
            AddOption(ModifierDropdown, modifier.DisplayName, (int)modifier.Key);
        }

        if (ModifierDropdown.ItemCount > 0)
        {
            if (!SelectModifierMetadata(ModifierDropdown, ModifierKey.Normal))
            {
                ModifierDropdown.Select(0);
            }
        }
    }

    private void PopulateModulatorDropdown()
    {
        if (_modulatorDropdown == null)
        {
            return;
        }

        string previousSelection = GetSelectedStringMetadata(_modulatorDropdown);
        string searchTerm = (_modulatorSearchInput?.Text ?? string.Empty).Trim();
        DebugModulatorFilterKind filter = GetSelectedModulatorFilter();

        _modulatorDropdown.Clear();
        _modulatorByKey.Clear();

        List<WeaponModulatorData> modulators = ModulatorCatalog.CreateAll();
        modulators.Sort(static (left, right) => string.Compare(left.BaseDisplayName, right.BaseDisplayName, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < modulators.Count; i++)
        {
            WeaponModulatorData modulator = modulators[i];
            if (!MatchesModulatorFilter(modulator, filter) || !MatchesModulatorSearch(modulator, searchTerm))
            {
                continue;
            }

            _modulatorByKey[modulator.Key] = modulator;
            AddOption(_modulatorDropdown, string.IsNullOrWhiteSpace(modulator.BaseDisplayName) ? modulator.DisplayName : modulator.BaseDisplayName, modulator.Key);
        }

        if (_modulatorDropdown.ItemCount > 0)
        {
            if (!SelectStringMetadata(_modulatorDropdown, previousSelection))
            {
                _modulatorDropdown.Select(0);
            }
        }

        RefreshSelectedModulatorDescription();
    }

    private void PopulateModulatorFilterDropdown()
    {
        if (_modulatorFilterDropdown == null)
        {
            return;
        }

        _modulatorFilterDropdown.Clear();
        AddOption(_modulatorFilterDropdown, "All", (int)DebugModulatorFilterKind.All);
        AddOption(_modulatorFilterDropdown, "Core", (int)DebugModulatorFilterKind.Core);
        AddOption(_modulatorFilterDropdown, "Projectile", (int)DebugModulatorFilterKind.Projectile);
        AddOption(_modulatorFilterDropdown, "Sustain", (int)DebugModulatorFilterKind.Sustain);
        AddOption(_modulatorFilterDropdown, "Utility", (int)DebugModulatorFilterKind.Utility);
        AddOption(_modulatorFilterDropdown, "Proc", (int)DebugModulatorFilterKind.Proc);
        AddOption(_modulatorFilterDropdown, "Wing", (int)DebugModulatorFilterKind.Wing);
        _modulatorFilterDropdown.Select(0);
    }

    private void PopulateModulatorTierDropdown()
    {
        if (_modulatorTierDropdown == null)
        {
            return;
        }

        _modulatorTierDropdown.Clear();
        AddOption(_modulatorTierDropdown, "Flawed", (int)WeaponModulatorTier.Flawed);
        AddOption(_modulatorTierDropdown, "Baseline", (int)WeaponModulatorTier.Baseline);
        AddOption(_modulatorTierDropdown, "Enhanced", (int)WeaponModulatorTier.Enhanced);
        AddOption(_modulatorTierDropdown, "Flawless", (int)WeaponModulatorTier.Flawless);
        _modulatorTierDropdown.Select(1);
    }

    private void AddSubstatRow()
    {
        VBoxContainer container = EnsureSubstatContainer();

        HBoxContainer rowNode = new();
        OptionButton typeDropdown = new();
        HSlider valueSlider = new();
        Label valueLabel = new();
        Button removeButton = new();

        rowNode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        valueSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        valueSlider.Step = 0.01;

        removeButton.Text = "Remove";

        foreach (SubstatType type in Enum.GetValues<SubstatType>())
        {
            if (type == SubstatType.None)
            {
                continue;
            }

            AddOption(typeDropdown, SubstatRoll.ToDisplayName(type), (int)type);
        }

        typeDropdown.Select(0);

        rowNode.AddChild(typeDropdown);
        rowNode.AddChild(valueSlider);
        rowNode.AddChild(valueLabel);
        rowNode.AddChild(removeButton);
        container.AddChild(rowNode);

        SubstatRow row = new()
        {
            Root = rowNode,
            TypeDropdown = typeDropdown,
            ValueSlider = valueSlider,
            ValueLabel = valueLabel,
            RemoveButton = removeButton,
        };

        _substatRows.Add(row);

        typeDropdown.ItemSelected += _ =>
        {
            UpdateSubstatRowRange(row);
            UpdateSubstatRowLabel(row);
            RefreshPreview();
        };

        valueSlider.ValueChanged += _ =>
        {
            UpdateSubstatRowLabel(row);
        };

        removeButton.Pressed += () =>
        {
            _substatRows.Remove(row);
            row.Root.QueueFree();
            RefreshPreview();
        };

        UpdateSubstatRowRange(row);
        UpdateSubstatRowLabel(row);
    }

    private VBoxContainer EnsureSubstatContainer()
    {
        if (SubstatRowsContainer != null)
        {
            return SubstatRowsContainer;
        }

        SubstatRowsContainer = new VBoxContainer();
        AddChild(SubstatRowsContainer);
        return SubstatRowsContainer;
    }

    private void RefreshSubstatRanges()
    {
        for (int i = 0; i < _substatRows.Count; i++)
        {
            UpdateSubstatRowRange(_substatRows[i]);
            UpdateSubstatRowLabel(_substatRows[i]);
        }
    }

    private void UpdateSubstatRowRange(SubstatRow row)
    {
        SubstatType type = GetRowSubstatType(row);
        RarityData? rarity = GetSelectedRarity();

        if (_forgeService != null && _forgeService.GetSubstatRange(type, rarity, out float min, out float max))
        {
            row.ValueSlider.MinValue = min;
            row.ValueSlider.MaxValue = max;
            row.ValueSlider.Step = SubstatRoll.UsesFlatDisplay(type) ? 0.1 : 0.01;
            row.ValueSlider.Value = Mathf.Clamp((float)row.ValueSlider.Value, min, max);
            return;
        }

        row.ValueSlider.MinValue = 0;
        row.ValueSlider.MaxValue = 100;
        row.ValueSlider.Step = 0.01;
    }

    private static void UpdateSubstatRowLabel(SubstatRow row)
    {
        SubstatType type = GetRowSubstatType(row);
        float value = (float)row.ValueSlider.Value;

        if (SubstatRoll.UsesFlatDisplay(type))
        {
            row.ValueLabel.Text = value.ToString("0.0");
        }
        else
        {
            row.ValueLabel.Text = $"{value:0.00}%";
        }
    }

    private void ForgeSelectedItem()
    {
        ShapeData? shape = GetSelectedShape();
        RarityData? rarity = GetSelectedRarity();
        ModifierData? modifier = GetSelectedModifier();
        int level = Mathf.RoundToInt((float)(ItemLevelInput?.Value ?? 1f));

        if (!TryBuildSubstats(out List<SubstatRoll> substats, out string error))
        {
            SetFeedback(error);
            return;
        }

        if (_forgeService == null)
        {
            SetFeedback("DebugForgeService is not connected.");
            return;
        }

        bool success = _forgeService.TryForgeItem(
            shape,
            rarity,
            modifier,
            level,
            substats,
            out EquipmentItem? item,
            out string feedback);

        SetFeedback(feedback);

        if (success && item != null)
        {
            ShapePreview?.SetItem(item);
            EmitSignal(SignalName.ItemForged, item.InstanceId);
        }
    }

    private void AddAllSelectedShapes()
    {
        if (_forgeService == null)
        {
            SetFeedback("DebugForgeService is not connected.");
            return;
        }

        RarityData? rarity = GetSelectedRarity();
        ModifierData? modifier = GetSelectedModifier();
        int level = Mathf.RoundToInt((float)(ItemLevelInput?.Value ?? 1f));

        if (rarity == null || modifier == null)
        {
            SetFeedback("Missing rarity or essence selection.");
            return;
        }

        List<ShapeData> shapes = new();
        for (int i = 0; i < ShapeOptions.Count; i++)
        {
            ShapeData? shape = ShapeOptions[i];
            if (shape != null)
            {
                shapes.Add(shape);
            }
        }

        if (shapes.Count == 0)
        {
            SetFeedback("No shapes available to add.");
            return;
        }

        int added = _forgeService.AddAllShapes(shapes, rarity, modifier, level, out int failed);
        if (added > 0)
        {
            SetFeedback($"Added {added} shapes to inventory ({rarity.DisplayName}, {modifier.DisplayName} essence, L{level}).");
            return;
        }

        SetFeedback(failed > 0 ? "Inventory full - could not add shapes." : "No shapes were added.");
    }

    private void AddAllModulatorsToStorage()
    {
        ResolveInventoryManager();
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory manager is not connected.");
            return;
        }

        int added = _inventoryManager.AddAllModulatorsToInventory(includeAllTiers: true);
        if (added > 0)
        {
            SetFeedback($"Added {added} modulator variants to storage.");
            return;
        }

        SetFeedback("All modulator variants are already in storage.");
    }

    private void GrantSelectedModulator()
    {
        if (_forgeService == null)
        {
            SetFeedback("DebugForgeService is not connected.");
            return;
        }

        WeaponModulatorData? baseModulator = GetSelectedDebugModulator();
        if (baseModulator == null)
        {
            SetFeedback("Choose a modulator first.");
            return;
        }

        WeaponModulatorTier tier = baseModulator.IsWingModulator ? GetSelectedModulatorTier() : WeaponModulatorTier.Baseline;
        bool success = _forgeService.TryGrantModulator(baseModulator.Key, tier, out _, out string feedback);
        SetFeedback(feedback);
    }

    private bool TryBuildSubstats(out List<SubstatRoll> substats, out string error)
    {
        substats = new List<SubstatRoll>();
        error = string.Empty;

        HashSet<SubstatType> seen = new();

        for (int i = 0; i < _substatRows.Count; i++)
        {
            SubstatRow row = _substatRows[i];
            SubstatType type = GetRowSubstatType(row);
            if (type == SubstatType.None)
            {
                continue;
            }

            if (!AllowDuplicateSubstats && !seen.Add(type))
            {
                error = "Duplicate substats are not allowed.";
                return false;
            }

            float value = (float)row.ValueSlider.Value;
            substats.Add(new SubstatRoll(type, value));
        }

        return true;
    }

    private void RefreshPreview()
    {
        ShapeData? shape = GetSelectedShape();
        RarityData? rarity = GetSelectedRarity();
        ModifierData? modifier = GetSelectedModifier();
        int level = Mathf.RoundToInt((float)(ItemLevelInput?.Value ?? 1f));

        RefreshShapePreview(shape, rarity, modifier);

        if (_forgeService != null)
        {
            _forgeService.CalculatePreview(shape, rarity, modifier, level, out int hp, out int dmg);

            if (PreviewHpLabel != null)
            {
                PreviewHpLabel.Text = hp.ToString();
            }

            if (PreviewDamageLabel != null)
            {
                PreviewDamageLabel.Text = dmg.ToString();
            }

            if (PreviewRarityLabel != null)
            {
                PreviewRarityLabel.Text = rarity?.DisplayName ?? "-";
            }

            return;
        }

        if (PreviewHpLabel != null)
        {
            PreviewHpLabel.Text = "-";
        }

        if (PreviewDamageLabel != null)
        {
            PreviewDamageLabel.Text = "-";
        }

        if (PreviewRarityLabel != null)
        {
            PreviewRarityLabel.Text = rarity?.DisplayName ?? "-";
        }
    }

    private void RefreshShapePreview(ShapeData? shape, RarityData? rarity, ModifierData? modifier)
    {
        if (ShapePreview == null)
        {
            return;
        }

        if (shape == null)
        {
            ShapePreview.SetItem(null);
            return;
        }

        EquipmentItem previewItem = new()
        {
            InstanceId = $"debug-preview-{shape.Key}-{rarity?.Key ?? "none"}-{modifier?.Key.ToString() ?? "none"}",
            Shape = shape,
            Rarity = rarity,
            Modifier = modifier,
            Level = Mathf.RoundToInt((float)(ItemLevelInput?.Value ?? 1f)),
            Stars = shape.Stars,
        };
        ShapePreview.SetItem(previewItem);
    }

    private static Color ResolvePreviewTint(RarityData? rarity)
    {
        if (rarity != null)
        {
            return rarity.UiColor;
        }

        return new Color(0.75f, 0.78f, 0.9f, 1f);
    }

    private ShapeData? GetSelectedShape()
    {
        string key = GetSelectedStringMetadata(ShapeDropdown);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return _shapeByKey.TryGetValue(key, out ShapeData? shape) ? shape : null;
    }

    private RarityData? GetSelectedRarity()
    {
        string key = GetSelectedStringMetadata(RarityDropdown);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return _rarityByKey.TryGetValue(key, out RarityData? rarity) ? rarity : null;
    }

    private ModifierData? GetSelectedModifier()
    {
        ModifierKey? key = GetSelectedModifierMetadata(ModifierDropdown);
        if (!key.HasValue)
        {
            return null;
        }

        return _modifierByKey.TryGetValue(key.Value, out ModifierData? modifier) ? modifier : null;
    }

    private static SubstatType GetRowSubstatType(SubstatRow row)
    {
        Variant metadata = row.TypeDropdown.GetItemMetadata(row.TypeDropdown.Selected);
        return metadata.VariantType == Variant.Type.Nil
            ? SubstatType.None
            : (SubstatType)metadata.AsInt32();
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

        Variant metadata = dropdown.GetItemMetadata(dropdown.Selected);
        return metadata.VariantType == Variant.Type.Nil ? string.Empty : metadata.AsString();
    }

    private static ModifierKey? GetSelectedModifierMetadata(OptionButton? dropdown)
    {
        if (dropdown == null || dropdown.Selected < 0)
        {
            return null;
        }

        Variant metadata = dropdown.GetItemMetadata(dropdown.Selected);
        if (metadata.VariantType == Variant.Type.Nil)
        {
            return null;
        }

        return (ModifierKey)metadata.AsInt32();
    }

    private static bool SelectStringMetadata(OptionButton dropdown, string key)
    {
        for (int i = 0; i < dropdown.ItemCount; i++)
        {
            Variant metadata = dropdown.GetItemMetadata(i);
            if (metadata.VariantType != Variant.Type.Nil
                && string.Equals(metadata.AsString(), key, StringComparison.OrdinalIgnoreCase))
            {
                dropdown.Select(i);
                return true;
            }
        }

        return false;
    }

    private static bool SelectModifierMetadata(OptionButton dropdown, ModifierKey key)
    {
        for (int i = 0; i < dropdown.ItemCount; i++)
        {
            Variant metadata = dropdown.GetItemMetadata(i);
            if (metadata.VariantType != Variant.Type.Nil && metadata.AsInt32() == (int)key)
            {
                dropdown.Select(i);
                return true;
            }
        }

        return false;
    }

    private WeaponModulatorData? GetSelectedDebugModulator()
    {
        string key = GetSelectedStringMetadata(_modulatorDropdown);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return _modulatorByKey.TryGetValue(key, out WeaponModulatorData? modulator) ? modulator : null;
    }

    private DebugModulatorFilterKind GetSelectedModulatorFilter()
    {
        if (_modulatorFilterDropdown == null || _modulatorFilterDropdown.Selected < 0)
        {
            return DebugModulatorFilterKind.All;
        }

        Variant metadata = _modulatorFilterDropdown.GetItemMetadata(_modulatorFilterDropdown.Selected);
        return metadata.VariantType == Variant.Type.Nil
            ? DebugModulatorFilterKind.All
            : (DebugModulatorFilterKind)metadata.AsInt32();
    }

    private WeaponModulatorTier GetSelectedModulatorTier()
    {
        if (_modulatorTierDropdown == null || _modulatorTierDropdown.Selected < 0)
        {
            return WeaponModulatorTier.Baseline;
        }

        Variant metadata = _modulatorTierDropdown.GetItemMetadata(_modulatorTierDropdown.Selected);
        return metadata.VariantType == Variant.Type.Nil
            ? WeaponModulatorTier.Baseline
            : (WeaponModulatorTier)metadata.AsInt32();
    }

    private void RefreshSelectedModulatorDescription()
    {
        WeaponModulatorData? modulator = GetSelectedDebugModulator();
        if (_modulatorDescriptionLabel == null)
        {
            return;
        }

        if (modulator == null)
        {
            if (_grantModulatorButton != null)
            {
                _grantModulatorButton.Disabled = true;
            }

            if (_modulatorTierDropdown != null)
            {
                _modulatorTierDropdown.Disabled = true;
            }

            _modulatorDescriptionLabel.Text = _modulatorDropdown != null && _modulatorDropdown.ItemCount == 0
                ? "No modulators match the current search or filter."
                : string.Empty;
            return;
        }

        if (_grantModulatorButton != null)
        {
            _grantModulatorButton.Disabled = false;
        }

        bool usesTierPresentation = modulator.UsesTierPresentation;
        if (_modulatorTierDropdown != null)
        {
            _modulatorTierDropdown.Disabled = !usesTierPresentation;
            if (!usesTierPresentation)
            {
                SelectTierDropdownValue(_modulatorTierDropdown, WeaponModulatorTier.Baseline);
            }
        }

        WeaponModulatorTier tier = usesTierPresentation ? GetSelectedModulatorTier() : WeaponModulatorTier.Baseline;
        WeaponModulatorData preview = ModulatorCatalog.CreateByKey(modulator.Key, tier);
        string tierNote = modulator.IsWingModulator
            ? $" Tier: {preview.Tier}. Cadence {Mathf.RoundToInt(preview.WingCadenceMultiplier * 100f)}%."
            : usesTierPresentation ? $" Tier: {preview.Tier}." : string.Empty;
        string categoryNote = $" Category: {WeaponModulatorData.GetCategoryDisplayName(preview.Category)}.";
        string tagNote = preview.Tags.Count > 0
            ? $" Tags: {string.Join(", ", preview.Tags)}."
            : string.Empty;
        string tierBehaviorNote = usesTierPresentation ? string.Empty : " Tier selection currently only affects tiered modulators.";
        _modulatorDescriptionLabel.Text = $"{preview.DisplayName}: {preview.Description}{tierNote}{categoryNote}{tagNote}{tierBehaviorNote}";
    }

    private static bool MatchesModulatorSearch(WeaponModulatorData modulator, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        string combined = string.Join(
            ' ',
            modulator.Key,
            modulator.BaseDisplayName,
            modulator.DisplayName,
            modulator.BaseDescription,
            modulator.Description,
            WeaponModulatorData.GetCategoryDisplayName(modulator.Category),
            string.Join(' ', modulator.Tags));
        return combined.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesModulatorFilter(WeaponModulatorData modulator, DebugModulatorFilterKind filter)
    {
        return filter switch
        {
            DebugModulatorFilterKind.All => true,
            DebugModulatorFilterKind.Core => modulator.Category == WeaponModulatorCategory.Core,
            DebugModulatorFilterKind.Projectile => modulator.Category == WeaponModulatorCategory.Projectile,
            DebugModulatorFilterKind.Sustain => modulator.Category == WeaponModulatorCategory.Sustain,
            DebugModulatorFilterKind.Utility => modulator.Category == WeaponModulatorCategory.Utility,
            DebugModulatorFilterKind.Proc => modulator.Category == WeaponModulatorCategory.Proc,
            DebugModulatorFilterKind.Wing => modulator.IsWingModulator || modulator.Category == WeaponModulatorCategory.Wing,
            _ => true,
        };
    }

    private static void SelectTierDropdownValue(OptionButton dropdown, WeaponModulatorTier tier)
    {
        for (int i = 0; i < dropdown.ItemCount; i++)
        {
            Variant metadata = dropdown.GetItemMetadata(i);
            if (metadata.VariantType != Variant.Type.Nil && metadata.AsInt32() == (int)tier)
            {
                dropdown.Select(i);
                return;
            }
        }
    }

    private void SetFeedback(string text)
    {
        if (FeedbackLabel != null)
        {
            FeedbackLabel.Text = text;
        }
    }

    private sealed class SubstatRow
    {
        public required HBoxContainer Root;
        public required OptionButton TypeDropdown;
        public required HSlider ValueSlider;
        public required Label ValueLabel;
        public required Button RemoveButton;
    }
}
