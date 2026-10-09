using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using SuperGatchaRebuild.DebugTools;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// In-arena debug panel with live stat overrides and stress-test controls.
public partial class ArenaDebugPanelUI : Control
{
    [Signal]
    public delegate void ExternalMenuVisibilityRequestedEventHandler(bool visible);

    [Export] public NodePath DebugCommandServicePath = new NodePath();
    [Export] public NodePath DebugManagerPath = new NodePath();
    [Export] public string HubScenePath = "res://Scenes/UI/StartHub.tscn";

    [ExportGroup("Global Stats")]
    [Export] public SpinBox? MoveSpeedPercentInput;
    [Export] public SpinBox? MaxHpFlatInput;
    [Export] public SpinBox? DodgeChancePercentInput;
    [Export] public SpinBox? LifeStealPercentInput;
    [Export] public SpinBox? HealthRegenPerSecondInput;
    [Export] public SpinBox? CritDamagePercentInput;
    [Export] public SpinBox? CritChancePercentInput;
    [Export] public SpinBox? RangeFlatInput;

    [ExportGroup("Per-Weapon")]
    [Export] public SpinBox? SizePercentInput;
    [Export] public SpinBox? AttackSpeedPercentInput;
    [Export] public SpinBox? DamagePercentBonusInput;
    [Export] public SpinBox? RicochetFlatInput;
    [Export] public SpinBox? BounceTimesFlatInput;
    [Export] public SpinBox? ProjectileCountFlatInput;

    [ExportGroup("Action Buttons")]
    [Export] public Button? ApplyLiveButton;
    [Export] public Button? ResetToEquippedButton;
    [Export] public Button? ReturnToHubButton;

    [ExportGroup("Stress Test")]
    [Export] public SpinBox? EnemyHpMultiplierInput;
    [Export] public Button? LightStressButton;
    [Export] public Button? MediumStressButton;
    [Export] public Button? HeavyStressButton;
    [Export] public Button? InsaneStressButton;
    [Export] public Button? SpawnGruntButton;
    [Export] public Button? SpawnBulwarkButton;
    [Export] public Button? SpawnChargerButton;
    [Export] public Button? SpawnMortarButton;
    [Export] public Button? BossRushButton;
    [Export] public Button? SpawnAllEnemyTypesButton;
    [Export] public Button? SpawnInterceptorButton;
    [Export] public Button? SpawnOverseerButton;
    [Export] public Button? SpawnCubeButton;
    [Export] public Button? KillAllEnemiesButton;

    [ExportGroup("Item Spawner")]
    [Export] public SpinBox? GrantCoinsInput;
    [Export] public Button? AddCoinsButton;
    [Export] public Button? SimulateItemDropButton;
    [Export] public Button? GrantAllItemsButton;
    [Export] public Button? ClearAllItemsButton;

    private sealed class LiveValueBinding
    {
        public LiveValueBinding(SpinBox input, Label valueLabel, Func<ArenaDebugStateSnapshot, string> formatter)
        {
            Input = input;
            ValueLabel = valueLabel;
            Formatter = formatter;
        }

        public SpinBox Input { get; }
        public Label ValueLabel { get; }
        public Func<ArenaDebugStateSnapshot, string> Formatter { get; }
    }

    private DebugCommandService? _commandService;
    private DebugManager? _debugManager;
    private bool _externalMenuGateEnabled;
    private bool _externalMenuVisible;
    private Button? _closeButton;
    private OptionButton? _shapeRarityDropdown;
    private OptionButton? _essenceDropdown;
    private OptionButton? _modulatorDropdown;
    private Button? _spawnMerchantShopButton;
    private Button? _spawnShapeButton;
    private Button? _spawnEssenceButton;
    private Button? _spawnModulatorButton;
    private readonly List<LiveValueBinding> _liveValueBindings = new();
    private readonly List<string> _shapeRarityKeys = new();
    private readonly List<ModifierKey> _essenceKeys = new();
    private readonly List<string> _modulatorKeys = new();
    private readonly List<string> _shapeKeys = new();
    private readonly List<string> _shapeLabels = new();
    private readonly List<string> _rarityKeys = new();
    private readonly List<string> _rarityLabels = new();
    private readonly List<string> _loadoutModulatorKeys = new();
    private OptionButton? _loadoutSlotDropdown;
    private OptionButton? _loadoutShapeDropdown;
    private OptionButton? _loadoutRarityDropdown;
    private OptionButton? _loadoutModulatorDropdown;
    private OptionButton? _loadoutModulatorTierDropdown;
    private SpinBox? _loadoutLevelInput;
    private SpinBox? _loadoutSocketIndexInput;
    private Button? _applyLoadoutWeaponButton;
    private Button? _clearLoadoutSlotButton;
    private Button? _addLoadoutModulatorButton;
    private Button? _replaceLoadoutModulatorButton;
    private Button? _removeLoadoutModulatorButton;
    private Button? _refreshLoadoutLabButton;
    private Label? _loadoutStatusLabel;
    private RichTextLabel? _loadoutDetailsLabel;
    private Label? _loadoutHintLabel;
    private int _selectedLoadoutSlot;
    private float _loadoutRefreshTimer;
    private bool _enhancedLayoutBuilt;
    private float _liveRefreshTimer;

    public override void _Ready()
    {
        ResolveUiReferences();
        ResolveServices();
        BuildEnhancedLayout();
        PopulateLootDropdowns();
        ConfigureInputs();
        WireButtons();
        RefreshLiveState(syncInputValues: true);
        RefreshVisibility();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _liveRefreshTimer -= (float)delta;
        if (_liveRefreshTimer > 0f)
        {
            return;
        }

        _liveRefreshTimer = 0.10f;
        RefreshLiveState();

        _loadoutRefreshTimer -= (float)delta;
        if (_loadoutRefreshTimer <= 0f)
        {
            _loadoutRefreshTimer = 0.20f;
            RefreshLoadoutLabState();
        }
    }

    private void ResolveUiReferences()
    {
        MoveSpeedPercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/MoveSpeedInput");
        MaxHpFlatInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/MaxHpInput");
        DodgeChancePercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/DodgeInput");
        LifeStealPercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/LifeStealInput");
        HealthRegenPerSecondInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/RegenInput");
        CritDamagePercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/CritDamageInput");
        CritChancePercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/CritChanceInput");
        RangeFlatInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/GlobalGrid/RangeInput");

        SizePercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/WeaponGrid/SizeInput");
        AttackSpeedPercentInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/WeaponGrid/AttackSpeedInput");
        DamagePercentBonusInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/WeaponGrid/DamageBonusInput");
        RicochetFlatInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/WeaponGrid/RicochetInput");
        BounceTimesFlatInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/WeaponGrid/BounceInput");
        ProjectileCountFlatInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/WeaponGrid/ProjectileCountInput");

        ApplyLiveButton ??= GetNodeOrNull<Button>("Panel/VBox/Actions/ApplyLiveButton");
        ResetToEquippedButton ??= GetNodeOrNull<Button>("Panel/VBox/Actions/ResetButton");
        ReturnToHubButton ??= GetNodeOrNull<Button>("Panel/VBox/Actions/ReturnHubButton");

        EnemyHpMultiplierInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/StressGrid/HpMultiplierInput");
        LightStressButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/LightButton");
        MediumStressButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/MediumButton");
        HeavyStressButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/HeavyButton");
        InsaneStressButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/InsaneButton");
        SpawnGruntButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnGruntButton");
        SpawnBulwarkButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnBulwarkButton");
        SpawnChargerButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnChargerButton");
        SpawnMortarButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnMortarButton");
        BossRushButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/BossRushButton");
        SpawnAllEnemyTypesButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnAllEnemyTypesButton");
        SpawnInterceptorButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnInterceptorButton");
        SpawnOverseerButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnOverseerButton");
        SpawnCubeButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/SpawnCubeButton");
        KillAllEnemiesButton ??= GetNodeOrNull<Button>("Panel/VBox/StressButtons/KillAllButton");

        GrantCoinsInput ??= GetNodeOrNull<SpinBox>("Panel/VBox/ItemsRow/CoinsInput");
        AddCoinsButton ??= GetNodeOrNull<Button>("Panel/VBox/ItemsRow/AddCoinsButton");
        SimulateItemDropButton ??= GetNodeOrNull<Button>("Panel/VBox/ItemsRow/SimulateItemDropButton");
        GrantAllItemsButton ??= GetNodeOrNull<Button>("Panel/VBox/ItemsRow/GrantAllButton");
        ClearAllItemsButton ??= GetNodeOrNull<Button>("Panel/VBox/ItemsRow/ClearAllButton");
    }

    private void ResolveServices()
    {
        if (!DebugCommandServicePath.IsEmpty)
        {
            _commandService = GetNodeOrNull(DebugCommandServicePath) as DebugCommandService;
        }

        _commandService ??= GetNodeOrNull("/root/DebugCommandService") as DebugCommandService;

        if (!DebugManagerPath.IsEmpty)
        {
            _debugManager = GetNodeOrNull(DebugManagerPath) as DebugManager;
        }

        _debugManager ??= GetNodeOrNull("/root/DebugManager") as DebugManager;

        if (_debugManager != null)
        {
            _debugManager.DebugActivated += OnDebugStateChanged;
            _debugManager.DebugDeactivated += OnDebugStateChanged;
        }
    }

    private void BuildEnhancedLayout()
    {
        if (_enhancedLayoutBuilt)
        {
            return;
        }

        VBoxContainer? root = GetNodeOrNull<VBoxContainer>("Panel/VBox");
        Label? title = GetNodeOrNull<Label>("Panel/VBox/Title");
        if (root == null || title == null)
        {
            return;
        }

        HBoxContainer header = new()
        {
            Name = "Header",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        header.AddThemeConstantOverride("separation", 8);
        root.AddChild(header);
        root.MoveChild(header, 0);

        title.Reparent(header);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.HorizontalAlignment = HorizontalAlignment.Left;

        _closeButton = new Button
        {
            Name = "CloseButton",
            Text = "Close",
            CustomMinimumSize = new Vector2(84f, 0f),
        };
        header.AddChild(_closeButton);

        TabContainer tabs = new()
        {
            Name = "Tabs",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(tabs);

        VBoxContainer statsTab = CreateTabPage("Stats");
        VBoxContainer enemiesTab = CreateTabPage("Enemies");
        VBoxContainer lootTab = CreateTabPage("Loot");
        VBoxContainer loadoutTab = CreateTabPage("LoadoutLab");

        tabs.AddChild(statsTab);
        tabs.SetTabTitle(0, "Stats");
        tabs.AddChild(enemiesTab);
        tabs.SetTabTitle(1, "Enemies");
        tabs.AddChild(lootTab);
        tabs.SetTabTitle(2, "Loot");
        tabs.AddChild(loadoutTab);
        tabs.SetTabTitle(3, "Loadout Lab");

        AddSectionToTab(statsTab, "Global Overrides", GetNodeOrNull<Control>("Panel/VBox/GlobalGrid"));
        AddSectionToTab(statsTab, "Weapon Overrides", GetNodeOrNull<Control>("Panel/VBox/WeaponGrid"));
        AddSectionToTab(statsTab, "Actions", GetNodeOrNull<Control>("Panel/VBox/Actions"));

        AddSectionToTab(enemiesTab, "Spawn Settings", GetNodeOrNull<Control>("Panel/VBox/StressGrid"));
        BuildArenaEventControls(enemiesTab);
        AddSectionToTab(enemiesTab, "Enemy Controls", GetNodeOrNull<Control>("Panel/VBox/StressButtons"));

        AddSectionToTab(lootTab, "Quick Loot", GetNodeOrNull<Control>("Panel/VBox/ItemsRow"));
        BuildTargetedDropControls(lootTab);
        BuildLoadoutLabControls(loadoutTab);
        BuildLiveValueBindings();

        _enhancedLayoutBuilt = true;
    }

    private static VBoxContainer CreateTabPage(string name)
    {
        VBoxContainer tab = new()
        {
            Name = name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        tab.AddThemeConstantOverride("separation", 10);
        return tab;
    }

    private void AddSectionToTab(VBoxContainer tab, string heading, Control? content)
    {
        if (content == null)
        {
            return;
        }

        tab.AddChild(CreateSectionLabel(heading));
        content.Reparent(tab);
    }

    private static Label CreateSectionLabel(string text)
    {
        Label label = new()
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color(0.88f, 0.93f, 1f, 0.95f));
        return label;
    }

    private void BuildTargetedDropControls(VBoxContainer lootTab)
    {
        lootTab.AddChild(CreateSectionLabel("Targeted Drops"));

        HBoxContainer shapeRow = CreateTargetedDropRow("Shape", out _shapeRarityDropdown, out _spawnShapeButton, "Spawn Shape");
        HBoxContainer essenceRow = CreateTargetedDropRow("Essence", out _essenceDropdown, out _spawnEssenceButton, "Spawn Essence");
        HBoxContainer modulatorRow = CreateTargetedDropRow("Modulator", out _modulatorDropdown, out _spawnModulatorButton, "Spawn Modulator");

        lootTab.AddChild(shapeRow);
        lootTab.AddChild(essenceRow);
        lootTab.AddChild(modulatorRow);
    }

    private void BuildArenaEventControls(VBoxContainer tab)
    {
        tab.AddChild(CreateSectionLabel("Arena Events"));

        _spawnMerchantShopButton = new Button
        {
            Text = "Spawn Shop (Merchant)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 34f),
        };

        tab.AddChild(_spawnMerchantShopButton);
    }

    private void BuildLoadoutLabControls(VBoxContainer tab)
    {
        tab.AddChild(CreateSectionLabel("Live Weapon Swap"));

        GridContainer weaponGrid = new()
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        weaponGrid.AddThemeConstantOverride("h_separation", 8);
        weaponGrid.AddThemeConstantOverride("v_separation", 6);

        _loadoutSlotDropdown = new OptionButton();
        _loadoutShapeDropdown = new OptionButton();
        _loadoutRarityDropdown = new OptionButton();
        _loadoutLevelInput = new SpinBox();
        _loadoutLevelInput.MinValue = 1;
        _loadoutLevelInput.MaxValue = EquipmentCatalogDefaults.MaxWeaponLevel;
        _loadoutLevelInput.Step = 1;
        _loadoutLevelInput.Value = 1;

        weaponGrid.AddChild(CreateInlineLabel("Slot"));
        weaponGrid.AddChild(_loadoutSlotDropdown);
        weaponGrid.AddChild(CreateInlineLabel("Shape"));
        weaponGrid.AddChild(_loadoutShapeDropdown);
        weaponGrid.AddChild(CreateInlineLabel("Rarity"));
        weaponGrid.AddChild(_loadoutRarityDropdown);
        weaponGrid.AddChild(CreateInlineLabel("Level"));
        weaponGrid.AddChild(_loadoutLevelInput);

        tab.AddChild(weaponGrid);

        HBoxContainer weaponButtons = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        weaponButtons.AddThemeConstantOverride("separation", 8);

        _applyLoadoutWeaponButton = new Button
        {
            Text = "Apply Weapon",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _clearLoadoutSlotButton = new Button
        {
            Text = "Clear Slot",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _refreshLoadoutLabButton = new Button
        {
            Text = "Refresh",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        weaponButtons.AddChild(_applyLoadoutWeaponButton);
        weaponButtons.AddChild(_clearLoadoutSlotButton);
        weaponButtons.AddChild(_refreshLoadoutLabButton);
        tab.AddChild(weaponButtons);

        tab.AddChild(CreateSectionLabel("Live Modulator Editing"));

        GridContainer modGrid = new()
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        modGrid.AddThemeConstantOverride("h_separation", 8);
        modGrid.AddThemeConstantOverride("v_separation", 6);

        _loadoutModulatorDropdown = new OptionButton();
        _loadoutModulatorTierDropdown = new OptionButton();
        _loadoutSocketIndexInput = new SpinBox
        {
            MinValue = 1,
            MaxValue = 32,
            Step = 1,
            Value = 1,
        };

        modGrid.AddChild(CreateInlineLabel("Modulator"));
        modGrid.AddChild(_loadoutModulatorDropdown);
        modGrid.AddChild(CreateInlineLabel("Tier"));
        modGrid.AddChild(_loadoutModulatorTierDropdown);
        modGrid.AddChild(CreateInlineLabel("Socket #"));
        modGrid.AddChild(_loadoutSocketIndexInput);

        tab.AddChild(modGrid);

        HBoxContainer modButtons = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        modButtons.AddThemeConstantOverride("separation", 8);

        _addLoadoutModulatorButton = new Button
        {
            Text = "Add Modulator",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _replaceLoadoutModulatorButton = new Button
        {
            Text = "Replace Socket",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _removeLoadoutModulatorButton = new Button
        {
            Text = "Remove Socket",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        modButtons.AddChild(_addLoadoutModulatorButton);
        modButtons.AddChild(_replaceLoadoutModulatorButton);
        modButtons.AddChild(_removeLoadoutModulatorButton);
        tab.AddChild(modButtons);

        _loadoutHintLabel = new Label
        {
            Text = "Tip: Socket # is 1-based. Use Refresh to pull latest runtime state.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _loadoutHintLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.86f, 0.96f, 0.90f));
        _loadoutHintLabel.AddThemeFontSizeOverride("font_size", 12);
        tab.AddChild(_loadoutHintLabel);

        _loadoutStatusLabel = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0f, 34f),
        };
        _loadoutStatusLabel.AddThemeFontSizeOverride("font_size", 12);
        _loadoutStatusLabel.AddThemeColorOverride("font_color", new Color(0.74f, 0.92f, 1f, 0.92f));
        tab.AddChild(_loadoutStatusLabel);

        _loadoutDetailsLabel = new RichTextLabel
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FitContent = true,
            CustomMinimumSize = new Vector2(0f, 120f),
            BbcodeEnabled = true,
            ScrollActive = true,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _loadoutDetailsLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        _loadoutDetailsLabel.AddThemeColorOverride("default_color", new Color(0.90f, 0.97f, 1f, 0.94f));
        tab.AddChild(_loadoutDetailsLabel);
    }

    private static Label CreateInlineLabel(string text)
    {
        return new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private static HBoxContainer CreateTargetedDropRow(string labelText, out OptionButton dropdown, out Button actionButton, string buttonText)
    {
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);

        Label label = new()
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(82f, 0f),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.AddChild(label);

        dropdown = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(dropdown);

        actionButton = new Button
        {
            Text = buttonText,
            CustomMinimumSize = new Vector2(128f, 0f),
        };
        row.AddChild(actionButton);

        return row;
    }

    private void BuildLiveValueBindings()
    {
        if (_liveValueBindings.Count > 0)
        {
            return;
        }

        AddLiveValueBinding(MoveSpeedPercentInput, snapshot => $"Now {Mathf.RoundToInt(snapshot.MoveSpeedPercent)}%");
        AddLiveValueBinding(MaxHpFlatInput, snapshot => $"Now {Mathf.RoundToInt(snapshot.MaxHp)}");
        AddLiveValueBinding(DodgeChancePercentInput, snapshot => $"Now {snapshot.DodgeChancePercent:0.0}%");
        AddLiveValueBinding(LifeStealPercentInput, snapshot => $"Now {snapshot.LifeStealPercent:0.0}%");
        AddLiveValueBinding(HealthRegenPerSecondInput, snapshot => $"Now {FormatRate(snapshot.HealthRegenPerSecond)}");
        AddLiveValueBinding(CritDamagePercentInput, snapshot => $"Now {Mathf.RoundToInt(snapshot.CritDamagePercent)}%");
        AddLiveValueBinding(CritChancePercentInput, snapshot => $"Now {snapshot.CritChancePercent:0.0}%");
        AddLiveValueBinding(RangeFlatInput, snapshot => $"Now {Mathf.RoundToInt(snapshot.WeaponRange)}");

        AddLiveValueBinding(SizePercentInput, snapshot => $"Now {Mathf.RoundToInt(snapshot.WeaponSizePercent)}%");
        AddLiveValueBinding(AttackSpeedPercentInput, snapshot => $"Now {FormatRate(snapshot.WeaponAttackSpeed)}");
        AddLiveValueBinding(DamagePercentBonusInput, snapshot => $"Now {FormatValue(snapshot.WeaponDamage)}");
        AddLiveValueBinding(RicochetFlatInput, snapshot => $"Now {snapshot.WeaponRicochet}");
        AddLiveValueBinding(BounceTimesFlatInput, snapshot => $"Now {snapshot.WeaponBounceTimes}");
        AddLiveValueBinding(ProjectileCountFlatInput, snapshot => $"Now {snapshot.WeaponProjectileCount}");
    }

    private void AddLiveValueBinding(SpinBox? input, Func<ArenaDebugStateSnapshot, string> formatter)
    {
        if (input == null)
        {
            return;
        }

        Label valueLabel = EnsureCurrentValueLabel(input);
        _liveValueBindings.Add(new LiveValueBinding(input, valueLabel, formatter));
    }

    private static Label EnsureCurrentValueLabel(SpinBox input)
    {
        if (input.GetParent() is HBoxContainer existingRow && existingRow.Name == input.Name + "Row")
        {
            for (int i = 0; i < existingRow.GetChildCount(); i++)
            {
                if (existingRow.GetChild(i) is Label existingLabel && existingLabel.Name == input.Name + "Current")
                {
                    return existingLabel;
                }
            }
        }

        Control? parent = input.GetParent() as Control;
        if (parent == null)
        {
            return new Label { Text = "Now --" };
        }

        int originalIndex = input.GetIndex();
        HBoxContainer row = new()
        {
            Name = input.Name + "Row",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 6);

        parent.AddChild(row);
        parent.MoveChild(row, originalIndex);
        input.Reparent(row);
        input.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        Label currentValue = new()
        {
            Name = input.Name + "Current",
            Text = "Now --",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(96f, 0f),
        };
        currentValue.AddThemeColorOverride("font_color", new Color(0.72f, 0.90f, 1f, 0.92f));
        row.AddChild(currentValue);
        return currentValue;
    }

    private void PopulateLootDropdowns()
    {
        if (_shapeRarityDropdown != null)
        {
            _shapeRarityDropdown.Clear();
            _shapeRarityKeys.Clear();
            List<RarityData> rarities = EquipmentCatalogDefaults.CreateDefaultRarities();
            for (int i = 0; i < rarities.Count; i++)
            {
                RarityData rarity = rarities[i];
                _shapeRarityDropdown.AddItem(rarity.DisplayName);
                _shapeRarityKeys.Add(rarity.Key);
            }

            if (_shapeRarityKeys.Count > 0)
            {
                _shapeRarityDropdown.Select(0);
            }
        }

        if (_essenceDropdown != null)
        {
            _essenceDropdown.Clear();
            _essenceKeys.Clear();
            List<ModifierData> essences = EquipmentCatalogDefaults.CreateDefaultModifiers(includeBaselineNormal: false);
            for (int i = 0; i < essences.Count; i++)
            {
                ModifierData essence = essences[i];
                _essenceDropdown.AddItem(essence.DisplayName);
                _essenceKeys.Add(essence.Key);
            }

            if (_essenceKeys.Count > 0)
            {
                _essenceDropdown.Select(0);
            }
        }

        if (_modulatorDropdown != null)
        {
            _modulatorDropdown.Clear();
            _modulatorKeys.Clear();
            List<WeaponModulatorData> modulators = EquipmentCatalogDefaults.CreateDefaultModulators();
            for (int i = 0; i < modulators.Count; i++)
            {
                WeaponModulatorData modulator = modulators[i];
                _modulatorDropdown.AddItem(modulator.DisplayName);
                _modulatorKeys.Add(modulator.Key);
            }

            if (_modulatorKeys.Count > 0)
            {
                _modulatorDropdown.Select(0);
            }
        }

        PopulateLoadoutLabDropdowns();
    }

    private void PopulateLoadoutLabDropdowns()
    {
        _shapeKeys.Clear();
        _shapeLabels.Clear();
        _rarityKeys.Clear();
        _rarityLabels.Clear();
        _loadoutModulatorKeys.Clear();

        List<ShapeData> shapes = EquipmentCatalogDefaults.CreateDefaultShapes();
        for (int i = 0; i < shapes.Count; i++)
        {
            ShapeData shape = shapes[i];
            _shapeKeys.Add(shape.Key);
            _shapeLabels.Add(shape.DisplayName);
        }

        List<RarityData> rarities = EquipmentCatalogDefaults.CreateDefaultRarities();
        for (int i = 0; i < rarities.Count; i++)
        {
            RarityData rarity = rarities[i];
            _rarityKeys.Add(rarity.Key);
            _rarityLabels.Add(rarity.DisplayName);
        }

        List<WeaponModulatorData> modulators = EquipmentCatalogDefaults.CreateDefaultModulators();
        for (int i = 0; i < modulators.Count; i++)
        {
            _loadoutModulatorKeys.Add(modulators[i].Key);
        }

        if (_loadoutSlotDropdown != null)
        {
            _loadoutSlotDropdown.Clear();
            for (int slotIndex = 0; slotIndex < 5; slotIndex++)
            {
                _loadoutSlotDropdown.AddItem(slotIndex == 0 ? "Primary" : $"Support {slotIndex}");
            }

            _loadoutSlotDropdown.Select(Mathf.Clamp(_selectedLoadoutSlot, 0, 4));
        }

        if (_loadoutShapeDropdown != null)
        {
            _loadoutShapeDropdown.Clear();
            for (int i = 0; i < _shapeLabels.Count; i++)
            {
                _loadoutShapeDropdown.AddItem(_shapeLabels[i]);
            }

            if (_shapeLabels.Count > 0)
            {
                _loadoutShapeDropdown.Select(0);
            }
        }

        if (_loadoutRarityDropdown != null)
        {
            _loadoutRarityDropdown.Clear();
            for (int i = 0; i < _rarityLabels.Count; i++)
            {
                _loadoutRarityDropdown.AddItem(_rarityLabels[i]);
            }

            if (_rarityLabels.Count > 0)
            {
                _loadoutRarityDropdown.Select(0);
            }
        }

        if (_loadoutModulatorDropdown != null)
        {
            _loadoutModulatorDropdown.Clear();
            for (int i = 0; i < modulators.Count; i++)
            {
                WeaponModulatorData modulator = modulators[i];
                string category = WeaponModulatorData.GetCategoryDisplayName(modulator.Category);
                _loadoutModulatorDropdown.AddItem($"{modulator.DisplayName} ({category})");
            }

            if (_loadoutModulatorKeys.Count > 0)
            {
                _loadoutModulatorDropdown.Select(0);
            }
        }

        if (_loadoutModulatorTierDropdown != null)
        {
            _loadoutModulatorTierDropdown.Clear();
            _loadoutModulatorTierDropdown.AddItem("Broken");
            _loadoutModulatorTierDropdown.AddItem("Baseline");
            _loadoutModulatorTierDropdown.AddItem("Enhanced");
            _loadoutModulatorTierDropdown.AddItem("Flawless");
            _loadoutModulatorTierDropdown.Select(1);
        }

        RefreshLoadoutLabState(force: true);
    }

    private void RefreshLiveState(bool syncInputValues = false)
    {
        ArenaDebugStateSnapshot snapshot = EnsureCommandService()
            ? _commandService!.GetArenaDebugState()
            : ArenaDebugStateSnapshot.Empty;

        for (int i = 0; i < _liveValueBindings.Count; i++)
        {
            LiveValueBinding binding = _liveValueBindings[i];
            binding.ValueLabel.Text = snapshot.HasRuntime ? binding.Formatter(snapshot) : "Now --";
        }

        if (syncInputValues)
        {
            ApplyOverrideInputs(snapshot.OverridesActive ? snapshot.Overrides : default);
        }
    }

    private void ApplyOverrideInputs(ArenaDebugOverrides data)
    {
        SetInputValue(MoveSpeedPercentInput, data.MoveSpeedPercent);
        SetInputValue(MaxHpFlatInput, data.MaxHpFlat);
        SetInputValue(DodgeChancePercentInput, data.DodgeChancePercent);
        SetInputValue(LifeStealPercentInput, data.LifeStealPercent);
        SetInputValue(HealthRegenPerSecondInput, data.HealthRegenPerSecond);
        SetInputValue(CritDamagePercentInput, data.CritDamagePercent);
        SetInputValue(CritChancePercentInput, data.CritChancePercent);
        SetInputValue(RangeFlatInput, data.RangeFlat);
        SetInputValue(SizePercentInput, data.WeaponSizePercent);
        SetInputValue(AttackSpeedPercentInput, data.WeaponAttackSpeedPercent);
        SetInputValue(DamagePercentBonusInput, data.WeaponDamagePercentBonus);
        SetInputValue(RicochetFlatInput, data.WeaponRicochetFlat);
        SetInputValue(BounceTimesFlatInput, data.WeaponBounceTimesFlat);
        SetInputValue(ProjectileCountFlatInput, data.WeaponProjectileCountFlat);
    }

    private static void SetInputValue(SpinBox? input, float value)
    {
        if (input != null)
        {
            input.Value = value;
        }
    }

    private static string FormatRate(float value)
    {
        return value % 1f > 0.001f ? $"{value:0.##}/s" : $"{value:0}/s";
    }

    private static string FormatValue(float value)
    {
        float abs = Mathf.Abs(value);
        if (abs >= 100f)
        {
            return value.ToString("0.#");
        }

        return value.ToString("0.##");
    }

    private void ConfigureInputs()
    {
        if (EnemyHpMultiplierInput != null)
        {
            EnemyHpMultiplierInput.MinValue = 0.1;
            EnemyHpMultiplierInput.MaxValue = 100.0;
            EnemyHpMultiplierInput.Step = 0.1;
            EnemyHpMultiplierInput.Value = 1.0;
        }

        if (GrantCoinsInput != null)
        {
            GrantCoinsInput.MinValue = 1;
            GrantCoinsInput.MaxValue = 99999999;
            GrantCoinsInput.Step = 1;
            GrantCoinsInput.Value = 100;
        }

        if (_loadoutLevelInput != null)
        {
            _loadoutLevelInput.MinValue = 1;
            _loadoutLevelInput.MaxValue = EquipmentCatalogDefaults.MaxWeaponLevel;
            _loadoutLevelInput.Step = 1;
            _loadoutLevelInput.Value = 1;
        }

        if (_loadoutSocketIndexInput != null)
        {
            _loadoutSocketIndexInput.MinValue = 1;
            _loadoutSocketIndexInput.MaxValue = 32;
            _loadoutSocketIndexInput.Step = 1;
            _loadoutSocketIndexInput.Value = 1;
        }
    }

    private void WireButtons()
    {
        if (_closeButton != null)
        {
            _closeButton.Pressed += OnClosePressed;
        }

        if (ApplyLiveButton != null)
        {
            ApplyLiveButton.Pressed += OnApplyLivePressed;
        }

        if (ResetToEquippedButton != null)
        {
            ResetToEquippedButton.Pressed += OnResetToEquippedPressed;
        }

        if (ReturnToHubButton != null)
        {
            ReturnToHubButton.Pressed += OnReturnToHubPressed;
        }

        if (LightStressButton != null)
        {
            LightStressButton.Pressed += () => SpawnStress(50);
        }

        if (MediumStressButton != null)
        {
            MediumStressButton.Pressed += () => SpawnStress(200);
        }

        if (HeavyStressButton != null)
        {
            HeavyStressButton.Pressed += () => SpawnStress(500);
        }

        if (InsaneStressButton != null)
        {
            InsaneStressButton.Pressed += () => SpawnStress(1000);
        }

        if (SpawnGruntButton != null)
        {
            SpawnGruntButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnGrunt(GetEnemyHpMultiplier());
                }
            };
        }

        if (SpawnBulwarkButton != null)
        {
            SpawnBulwarkButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnEnemy(global::SuperGatchaRebuild.EnemyKind.Bulwark, 1, GetEnemyHpMultiplier());
                }
            };
        }

        if (SpawnChargerButton != null)
        {
            SpawnChargerButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnEnemy(global::SuperGatchaRebuild.EnemyKind.Charger, 1, GetEnemyHpMultiplier());
                }
            };
        }

        if (SpawnMortarButton != null)
        {
            SpawnMortarButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnEnemy(global::SuperGatchaRebuild.EnemyKind.Mortar, 1, GetEnemyHpMultiplier());
                }
            };
        }

        if (BossRushButton != null)
        {
            BossRushButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnMonolith();
                }
            };
        }

        if (SpawnAllEnemyTypesButton != null)
        {
            SpawnAllEnemyTypesButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnAllEnemyTypes(GetEnemyHpMultiplier());
                }
            };
        }

        if (SpawnInterceptorButton != null)
        {
            SpawnInterceptorButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnInterceptor(GetEnemyHpMultiplier());
                }
            };
        }

        if (SpawnOverseerButton != null)
        {
            SpawnOverseerButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnOverseer();
                }
            };
        }

        if (SpawnCubeButton != null)
        {
            SpawnCubeButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnCube();
                }
            };
        }

        if (KillAllEnemiesButton != null)
        {
            KillAllEnemiesButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.KillAllEnemies();
                }
            };
        }

        if (AddCoinsButton != null)
        {
            AddCoinsButton.Pressed += OnAddCoinsPressed;
        }

        if (SimulateItemDropButton != null)
        {
            SimulateItemDropButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.SpawnRandomShapeItemDrop();
                }
            };
        }

        if (GrantAllItemsButton != null)
        {
            GrantAllItemsButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.GrantAllItemsX1();
                }
            };
        }

        if (ClearAllItemsButton != null)
        {
            ClearAllItemsButton.Pressed += () =>
            {
                if (EnsureCommandService())
                {
                    _commandService?.ClearAllItems();
                }
            };
        }

        if (_spawnMerchantShopButton != null)
        {
            _spawnMerchantShopButton.Pressed += OnSpawnMerchantShopPressed;
        }

        if (_spawnEssenceButton != null)
        {
            _spawnEssenceButton.Pressed += OnSpawnSelectedEssencePressed;
        }

        if (_spawnShapeButton != null)
        {
            _spawnShapeButton.Pressed += OnSpawnSelectedShapePressed;
        }

        if (_spawnModulatorButton != null)
        {
            _spawnModulatorButton.Pressed += OnSpawnSelectedModulatorPressed;
        }

        if (_loadoutSlotDropdown != null)
        {
            _loadoutSlotDropdown.ItemSelected += OnLoadoutSlotSelected;
        }

        if (_loadoutShapeDropdown != null)
        {
            _loadoutShapeDropdown.ItemSelected += (_) => ClearLoadoutStatus();
        }

        if (_loadoutRarityDropdown != null)
        {
            _loadoutRarityDropdown.ItemSelected += (_) => ClearLoadoutStatus();
        }

        if (_loadoutModulatorDropdown != null)
        {
            _loadoutModulatorDropdown.ItemSelected += (_) =>
            {
                ClearLoadoutStatus();
                RefreshLoadoutLabState(force: true);
            };
        }

        if (_loadoutModulatorTierDropdown != null)
        {
            _loadoutModulatorTierDropdown.ItemSelected += (_) =>
            {
                ClearLoadoutStatus();
                RefreshLoadoutLabState(force: true);
            };
        }

        if (_applyLoadoutWeaponButton != null)
        {
            _applyLoadoutWeaponButton.Pressed += OnApplyLoadoutWeaponPressed;
        }

        if (_clearLoadoutSlotButton != null)
        {
            _clearLoadoutSlotButton.Pressed += OnClearLoadoutSlotPressed;
        }

        if (_addLoadoutModulatorButton != null)
        {
            _addLoadoutModulatorButton.Pressed += OnAddLoadoutModulatorPressed;
        }

        if (_replaceLoadoutModulatorButton != null)
        {
            _replaceLoadoutModulatorButton.Pressed += OnReplaceLoadoutModulatorPressed;
        }

        if (_removeLoadoutModulatorButton != null)
        {
            _removeLoadoutModulatorButton.Pressed += OnRemoveLoadoutModulatorPressed;
        }

        if (_refreshLoadoutLabButton != null)
        {
            _refreshLoadoutLabButton.Pressed += () => RefreshLoadoutLabState(force: true);
        }
    }

    private void OnLoadoutSlotSelected(long index)
    {
        _selectedLoadoutSlot = Mathf.Clamp((int)index, 0, 4);
        ClearLoadoutStatus();
        RefreshLoadoutLabState(force: true);
    }

    private void OnApplyLoadoutWeaponPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        if (!TryGetSelectedLoadoutShapeKey(out string shapeKey) || !TryGetSelectedLoadoutRarityKey(out string rarityKey))
        {
            SetLoadoutStatus("Select a shape and rarity before applying.", isError: true);
            return;
        }

        int level = Mathf.Clamp(Mathf.RoundToInt((float)(_loadoutLevelInput?.Value ?? 1f)), 1, EquipmentCatalogDefaults.MaxWeaponLevel);
        bool ok = _commandService?.TrySetArenaDebugLoadoutItem(_selectedLoadoutSlot, shapeKey, rarityKey, level) == true;
        SetLoadoutStatus(ok
            ? $"Applied {GetDisplayNameForKey(_shapeKeys, _shapeLabels, shapeKey)} to slot {GetSlotLabel(_selectedLoadoutSlot)}."
            : "Could not apply the weapon to that slot.", isError: !ok);
        RefreshLoadoutLabState(force: true);
    }

    private void OnClearLoadoutSlotPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        bool ok = _commandService?.TryClearArenaDebugLoadoutSlot(_selectedLoadoutSlot) == true;
        SetLoadoutStatus(ok
            ? $"Cleared {GetSlotLabel(_selectedLoadoutSlot)}."
            : "Slot was already empty or could not be cleared.", isError: !ok);
        RefreshLoadoutLabState(force: true);
    }

    private void OnAddLoadoutModulatorPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        if (!TryGetSelectedLoadoutModulatorKey(out string modulatorKey))
        {
            SetLoadoutStatus("Select a modulator before adding.", isError: true);
            return;
        }

        string tierKey = GetSelectedLoadoutTierKey();
        bool ok = _commandService?.TryAddArenaDebugModulator(_selectedLoadoutSlot, modulatorKey, tierKey) == true;
        SetLoadoutStatus(ok
            ? "Added modulator to the next open socket."
            : "Unable to add modulator to this weapon (socket full or compatibility/wing rule failed).", isError: !ok);
        RefreshLoadoutLabState(force: true);
    }

    private void OnReplaceLoadoutModulatorPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        if (!TryGetSelectedLoadoutModulatorKey(out string modulatorKey))
        {
            SetLoadoutStatus("Select a modulator before replacing.", isError: true);
            return;
        }

        int socketIndex = Mathf.Max(0, Mathf.RoundToInt((float)(_loadoutSocketIndexInput?.Value ?? 1f)) - 1);
        string tierKey = GetSelectedLoadoutTierKey();
        bool ok = _commandService?.TryReplaceArenaDebugModulator(_selectedLoadoutSlot, socketIndex, modulatorKey, tierKey) == true;
        SetLoadoutStatus(ok
            ? $"Replaced socket {socketIndex + 1}."
            : "Unable to replace that socket. Check socket index and compatibility.", isError: !ok);
        RefreshLoadoutLabState(force: true);
    }

    private void OnRemoveLoadoutModulatorPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        int socketIndex = Mathf.Max(0, Mathf.RoundToInt((float)(_loadoutSocketIndexInput?.Value ?? 1f)) - 1);
        bool ok = _commandService?.TryRemoveArenaDebugModulator(_selectedLoadoutSlot, socketIndex) == true;
        SetLoadoutStatus(ok
            ? $"Removed socket {socketIndex + 1}."
            : "Unable to remove that socket (index may be empty/out of range).", isError: !ok);
        RefreshLoadoutLabState(force: true);
    }

    private void OnApplyLivePressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        ArenaDebugOverrides data = new()
        {
            MoveSpeedPercent = ToFloat(MoveSpeedPercentInput),
            MaxHpFlat = ToFloat(MaxHpFlatInput),
            DodgeChancePercent = ToFloat(DodgeChancePercentInput),
            LifeStealPercent = ToFloat(LifeStealPercentInput),
            HealthRegenPerSecond = ToFloat(HealthRegenPerSecondInput),
            CritDamagePercent = ToFloat(CritDamagePercentInput),
            CritChancePercent = ToFloat(CritChancePercentInput),
            RangeFlat = ToFloat(RangeFlatInput),

            WeaponSizePercent = ToFloat(SizePercentInput),
            WeaponAttackSpeedPercent = ToFloat(AttackSpeedPercentInput),
            WeaponDamagePercentBonus = ToFloat(DamagePercentBonusInput),
            WeaponRicochetFlat = ToFloat(RicochetFlatInput),
            WeaponBounceTimesFlat = ToFloat(BounceTimesFlatInput),
            WeaponProjectileCountFlat = ToFloat(ProjectileCountFlatInput),
        };

        _commandService?.ApplyArenaOverrides(data);
        RefreshLiveState();
    }

    private void OnResetToEquippedPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        _commandService?.ResetArenaOverrides();
        ApplyOverrideInputs(default);
        RefreshLiveState();
    }

    private void SpawnStress(int count)
    {
        if (!EnsureCommandService())
        {
            return;
        }

        _commandService?.SpawnStressWave(count, GetEnemyHpMultiplier());
    }

    private void OnAddCoinsPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        int amount = Mathf.RoundToInt((float)(GrantCoinsInput?.Value ?? 0));
        if (amount <= 0)
        {
            return;
        }

        _commandService?.AddCoins(amount);
    }

    private void OnSpawnSelectedEssencePressed()
    {
        if (!EnsureCommandService() || _essenceDropdown == null || _essenceKeys.Count == 0)
        {
            return;
        }

        int selected = Mathf.Clamp(_essenceDropdown.Selected, 0, _essenceKeys.Count - 1);
        _commandService?.SpawnEssenceDrop(_essenceKeys[selected]);
    }

    private void OnSpawnSelectedShapePressed()
    {
        if (!EnsureCommandService() || _shapeRarityDropdown == null || _shapeRarityKeys.Count == 0)
        {
            return;
        }

        int selected = Mathf.Clamp(_shapeRarityDropdown.Selected, 0, _shapeRarityKeys.Count - 1);
        _commandService?.SpawnRandomShapeItemDrop(_shapeRarityKeys[selected]);
    }

    private void OnSpawnSelectedModulatorPressed()
    {
        if (!EnsureCommandService() || _modulatorDropdown == null || _modulatorKeys.Count == 0)
        {
            return;
        }

        int selected = Mathf.Clamp(_modulatorDropdown.Selected, 0, _modulatorKeys.Count - 1);
        _commandService?.SpawnModulatorDrop(_modulatorKeys[selected]);
    }

    private void OnSpawnMerchantShopPressed()
    {
        if (!EnsureCommandService())
        {
            return;
        }

        _commandService?.SpawnMerchantShop();
    }

    private void OnClosePressed()
    {
        RequestPanelVisibility(false);
    }

    private float GetEnemyHpMultiplier()
    {
        return ToFloat(EnemyHpMultiplierInput, 1f);
    }

    private static float ToFloat(SpinBox? input, float fallback = 0f)
    {
        return input == null ? fallback : (float)input.Value;
    }

    private void OnReturnToHubPressed()
    {
        if (EnsureCommandService())
        {
            _commandService?.ReturnToHub(HubScenePath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(HubScenePath) && FileAccess.FileExists(HubScenePath))
        {
            _ = GetTree().ChangeSceneToFile(HubScenePath);
        }
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

    private void RefreshLoadoutLabState(bool force = false)
    {
        if (!force && !Visible)
        {
            return;
        }

        if (!EnsureCommandService())
        {
            if (_loadoutDetailsLabel != null)
            {
                _loadoutDetailsLabel.Text = "Debug command service unavailable.";
            }

            return;
        }

        ArenaDebugLoadoutSlotSnapshot snapshot = _commandService!.GetArenaLoadoutSlotSnapshot(_selectedLoadoutSlot);
        if (!snapshot.HasRuntime)
        {
            if (_loadoutDetailsLabel != null)
            {
                _loadoutDetailsLabel.Text = "Arena runtime not active.";
            }

            return;
        }

        if (_loadoutSlotDropdown != null && _loadoutSlotDropdown.ItemCount > 0)
        {
            int safeSlot = Mathf.Clamp(_selectedLoadoutSlot, 0, _loadoutSlotDropdown.ItemCount - 1);
            if (_loadoutSlotDropdown.Selected != safeSlot)
            {
                _loadoutSlotDropdown.Select(safeSlot);
            }
        }

        if (force && _loadoutLevelInput != null && snapshot.HasItem)
        {
            _loadoutLevelInput.Value = snapshot.Level;
        }

        if (force)
        {
            SelectOptionByValue(_loadoutShapeDropdown, _shapeKeys, snapshot.ShapeKey);
            SelectOptionByValue(_loadoutRarityDropdown, _rarityKeys, snapshot.RarityKey);
        }

        int maxSocketIndex = Mathf.Max(1, snapshot.UnlockedModulatorSlots <= 0 ? 1 : snapshot.UnlockedModulatorSlots);
        if (_loadoutSocketIndexInput != null)
        {
            _loadoutSocketIndexInput.MaxValue = maxSocketIndex;
            _loadoutSocketIndexInput.Value = Mathf.Clamp((float)_loadoutSocketIndexInput.Value, 1f, maxSocketIndex);
        }

        if (_loadoutDetailsLabel != null)
        {
            StringBuilder builder = new();
            builder.AppendLine($"[b]{snapshot.SlotLabel}[/b]");
            builder.AppendLine(snapshot.ItemLabel);

            if (snapshot.HasItem)
            {
                builder.AppendLine($"Shape: {GetDisplayNameForKey(_shapeKeys, _shapeLabels, snapshot.ShapeKey)} ({snapshot.ShapeKey})");
                builder.AppendLine($"Rarity: {GetDisplayNameForKey(_rarityKeys, _rarityLabels, snapshot.RarityKey)} ({snapshot.RarityKey})");
                builder.AppendLine($"Sockets: {snapshot.UnlockedModulatorSlots - snapshot.AvailableModulatorSlots}/{snapshot.UnlockedModulatorSlots}");
                builder.AppendLine();
                builder.AppendLine("[b]Installed Modulators[/b]");

                if (snapshot.InstalledModulatorLabels.Count == 0)
                {
                    builder.Append("None");
                }
                else
                {
                    for (int i = 0; i < snapshot.InstalledModulatorLabels.Count; i++)
                    {
                        builder.AppendLine(snapshot.InstalledModulatorLabels[i]);
                    }
                }

                AppendSelectedModulatorPreview(builder, snapshot);
            }
            else
            {
                builder.Append("Use Apply Weapon to create a test weapon in this slot.");

                AppendSelectedModulatorPreview(builder, snapshot);
            }

            _loadoutDetailsLabel.Text = builder.ToString();
        }
    }

    private void AppendSelectedModulatorPreview(StringBuilder builder, ArenaDebugLoadoutSlotSnapshot snapshot)
    {
        builder.AppendLine();
        builder.AppendLine("[b]Selected Modulator Preview[/b]");

        if (!TryGetSelectedLoadoutModulatorData(out WeaponModulatorData modulator))
        {
            builder.Append("Select a modulator + tier to preview added stats.");
            return;
        }

        builder.AppendLine(modulator.DisplayName);
        builder.AppendLine(ModulatorQualityText.Header(modulator));
        builder.AppendLine($"Category: {WeaponModulatorData.GetCategoryDisplayName(modulator.Category)}");

        if (snapshot.HasItem)
        {
            bool tagCompatible = IsModulatorTagCompatibleWithSnapshotWeapon(snapshot, modulator);
            bool hasOpenSocket = snapshot.AvailableModulatorSlots > 0;
            builder.AppendLine($"Tag Compatibility: {(tagCompatible ? "Compatible" : "Incompatible")}");
            builder.AppendLine($"Open Socket Available: {(hasOpenSocket ? "Yes" : "No (replace needed)")}");
        }

        builder.AppendLine("Effects If Installed:");
        builder.AppendLine(ModulatorQualityText.Describe(modulator));
    }

    private bool TryGetSelectedLoadoutModulatorData(out WeaponModulatorData modulator)
    {
        modulator = null!;
        if (!TryGetSelectedLoadoutModulatorKey(out string modulatorKey))
        {
            return false;
        }

        WeaponModulatorTier tier = WeaponModulatorTier.Baseline;
        _ = WeaponModulatorData.TryParseTierKey(GetSelectedLoadoutTierKey(), out tier);
        return EquipmentCatalogDefaults.TryCreateModulatorByKey(modulatorKey, tier, out modulator);
    }

    private static bool IsModulatorTagCompatibleWithSnapshotWeapon(ArenaDebugLoadoutSlotSnapshot snapshot, WeaponModulatorData modulator)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ShapeKey))
        {
            return false;
        }

        ShapeData shape = EquipmentCatalogDefaults.CreateShapeByKey(snapshot.ShapeKey);
        RarityData rarity = EquipmentCatalogDefaults.CreateRarityByKey(snapshot.RarityKey);
        EquipmentItem previewWeapon = new()
        {
            Shape = shape,
            Rarity = rarity,
            Level = Mathf.Clamp(snapshot.Level, 1, EquipmentCatalogDefaults.MaxWeaponLevel),
            Stars = Mathf.Max(1, shape.Stars),
            Substats = new List<SubstatRoll>(),
            Modulators = new List<WeaponModulatorData>(),
        };

        return WeaponModulatorCompatibility.CanApplyToWeapon(modulator, previewWeapon, out _);
    }

    private static string FormatPreviewSubstatDelta(SubstatType type, float value)
    {
        string name = SubstatRoll.ToDisplayName(type);
        if (SubstatRoll.UsesFlatDisplay(type))
        {
            float rounded = Mathf.Round(value);
            if (Mathf.IsEqualApprox(value, rounded))
            {
                return $"{name} +{rounded:0}";
            }

            return $"{name} +{value:0.##}";
        }

        return $"{name} +{value:0.##}%";
    }

    private static void SelectOptionByValue(OptionButton? dropdown, List<string> keys, string key)
    {
        if (dropdown == null || dropdown.ItemCount == 0 || keys.Count == 0 || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        for (int i = 0; i < keys.Count && i < dropdown.ItemCount; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
            {
                if (dropdown.Selected != i)
                {
                    dropdown.Select(i);
                }

                return;
            }
        }
    }

    private bool TryGetSelectedLoadoutShapeKey(out string shapeKey)
    {
        shapeKey = string.Empty;
        if (_loadoutShapeDropdown == null || _shapeKeys.Count == 0)
        {
            return false;
        }

        int selected = Mathf.Clamp(_loadoutShapeDropdown.Selected, 0, _shapeKeys.Count - 1);
        shapeKey = _shapeKeys[selected];
        return !string.IsNullOrWhiteSpace(shapeKey);
    }

    private bool TryGetSelectedLoadoutRarityKey(out string rarityKey)
    {
        rarityKey = string.Empty;
        if (_loadoutRarityDropdown == null || _rarityKeys.Count == 0)
        {
            return false;
        }

        int selected = Mathf.Clamp(_loadoutRarityDropdown.Selected, 0, _rarityKeys.Count - 1);
        rarityKey = _rarityKeys[selected];
        return !string.IsNullOrWhiteSpace(rarityKey);
    }

    private bool TryGetSelectedLoadoutModulatorKey(out string modulatorKey)
    {
        modulatorKey = string.Empty;
        if (_loadoutModulatorDropdown == null || _loadoutModulatorKeys.Count == 0)
        {
            return false;
        }

        int selected = Mathf.Clamp(_loadoutModulatorDropdown.Selected, 0, _loadoutModulatorKeys.Count - 1);
        modulatorKey = _loadoutModulatorKeys[selected];
        return !string.IsNullOrWhiteSpace(modulatorKey);
    }

    private string GetSelectedLoadoutTierKey()
    {
        if (_loadoutModulatorTierDropdown == null || _loadoutModulatorTierDropdown.ItemCount == 0)
        {
            return "baseline";
        }

        int selected = Mathf.Clamp(_loadoutModulatorTierDropdown.Selected, 0, _loadoutModulatorTierDropdown.ItemCount - 1);
        string text = _loadoutModulatorTierDropdown.GetItemText(selected);
        return string.IsNullOrWhiteSpace(text) ? "baseline" : text.ToLowerInvariant();
    }

    private static string GetDisplayNameForKey(List<string> keys, List<string> labels, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || keys.Count == 0 || labels.Count == 0)
        {
            return string.IsNullOrWhiteSpace(key) ? "Unknown" : key;
        }

        for (int i = 0; i < keys.Count && i < labels.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return labels[i];
            }
        }

        return key;
    }

    private static string GetSlotLabel(int slotIndex)
    {
        return slotIndex == 0 ? "Primary" : $"Support {slotIndex}";
    }

    private void SetLoadoutStatus(string text, bool isError)
    {
        if (_loadoutStatusLabel == null)
        {
            return;
        }

        _loadoutStatusLabel.Text = text;
        _loadoutStatusLabel.AddThemeColorOverride(
            "font_color",
            isError
                ? new Color(1f, 0.70f, 0.66f, 0.96f)
                : new Color(0.76f, 0.96f, 0.82f, 0.96f));
    }

    private void ClearLoadoutStatus()
    {
        if (_loadoutStatusLabel == null)
        {
            return;
        }

        _loadoutStatusLabel.Text = string.Empty;
        _loadoutStatusLabel.AddThemeColorOverride("font_color", new Color(0.74f, 0.92f, 1f, 0.92f));
    }

    private void OnDebugStateChanged()
    {
        RefreshVisibility();
    }

    public void EnableExternalMenuGate(bool enabled)
    {
        _externalMenuGateEnabled = enabled;
        if (enabled)
        {
            _externalMenuVisible = false;
        }

        RefreshVisibility();
    }

    public void SetExternalMenuVisible(bool visible)
    {
        _externalMenuVisible = visible;
        RefreshVisibility();
    }

    private void RequestPanelVisibility(bool visible)
    {
        if (_externalMenuGateEnabled)
        {
            _externalMenuVisible = visible;
            RefreshVisibility();
            EmitSignal(SignalName.ExternalMenuVisibilityRequested, visible);
            return;
        }

        Visible = visible;
    }

    private void RefreshVisibility()
    {
		bool wasVisible = Visible;
        bool debugAllowed = (_debugManager?.DebugActive == true) || OS.IsDebugBuild();
        Visible = _externalMenuGateEnabled
            ? (debugAllowed && _externalMenuVisible)
            : debugAllowed;

		if (Visible && !wasVisible)
		{
			_liveRefreshTimer = 0f;
            _loadoutRefreshTimer = 0f;
			RefreshLiveState(syncInputValues: true);
            RefreshLoadoutLabState(force: true);
		}
    }
}
