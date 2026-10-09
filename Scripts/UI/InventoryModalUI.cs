using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

public partial class InventoryModalUI : ModalBase
{
    private enum InventoryModalMode
    {
        FullInventory,
        WeaponSelection,
        SocketManagement,
    }

    [Export] public ItemList? ItemListView;
    [Export] public RichTextLabel? ItemDetailsText;
    [Export] public Label? SummaryLabel;
    [Export] public Label? FeedbackLabel;
    [Export] public OptionButton? EquipSlotDropdown;
    [Export] public Button? EquipButton;
    [Export] public Button? UnequipButton;
    [Export] public Button? RefreshButton;
    [Export] public ShapePreviewViewport? ShapePreview;
    [Export] public NodePath InventoryManagerPath = new NodePath();

    private const int EquippedSlotCount = 5;
    private const float BasePreviewCriticalDamagePercent = 50f;
    private const int ContextActionNoop = 0;
    private const int ContextActionSelect = 1;
    private const int ContextActionUnequip = 2;
    private const int ContextActionEquipSelectedHere = 3;
    private const int ContextActionDelete = 4;
    private const int ContextActionEquipSlotBase = 100;

    private InventoryManager? _inventoryManager;
    private readonly List<EquipmentItem> _visibleItems = new();
    private readonly SynergyManager _synergyManager = new();
    private InventoryModalMode _mode = InventoryModalMode.FullInventory;
    private int _focusedSlotIndex;

    private HBoxContainer? _equippedStrip;
    private ScrollContainer? _itemGridScroll;
    private GridContainer? _itemGrid;
    private HBoxContainer? _browserTools;
    private HBoxContainer? _actionBar;
    private OptionButton? _filterDropdown;
    private OptionButton? _sortDropdown;
    private HBoxContainer? _selectionToolbar;
    private Button? _selectModeButton;
    private Button? _selectAllVisibleButton;
    private Button? _clearSelectionButton;
    private Button? _deleteSelectedButton;
    private Button? _advancedInfoButton;
    private ScrollContainer? _socketInventoryScroll;
    private VBoxContainer? _socketInventoryPanel;
    private Label? _socketTargetLabel;
    private Label? _socketHintLabel;
    private Label? _installedEssenceLabel;
    private ItemList? _storedEssenceList;
    private Button? _socketEssenceButton;
    private Button? _returnEssenceButton;
    private ItemList? _installedModulatorList;
    private ItemList? _storedModulatorList;
    private Button? _installModulatorButton;
    private Button? _returnModulatorButton;
    private PopupMenu? _contextMenu;
    private ConfirmationDialog? _deleteConfirmDialog;
    private readonly List<Button> _itemCardButtons = new();
    private readonly HashSet<string> _bulkSelectedInstanceIds = new(StringComparer.Ordinal);
    private readonly List<string> _pendingDeleteInstanceIds = new();
    private string? _selectedInstanceId;
    private string? _contextItemInstanceId;
    private string? _contextSelectedItemInstanceId;
    private int _contextSlotIndex = -1;
    private bool _bulkSelectMode;
    private bool _showAdvancedInfo;
    private float _cardVisualTime;
    private float _cardVisualRefreshTimer;

    private readonly struct WeaponStatPreview
    {
        public bool HasShape { get; init; }
        public bool HasDamage { get; init; }
        public int HitCount { get; init; }
        public int ProjectileCount { get; init; }
        public float DamagePercent { get; init; }
        public float AttackSpeedPercent { get; init; }
        public float CritChancePercent { get; init; }
        public float CritDamagePercent { get; init; }
        public float RangePercent { get; init; }
        public float SizePercent { get; init; }
        public float RicochetCount { get; init; }
        public float BounceTimesCount { get; init; }
        public float ProjectileBonus { get; init; }
        public float DamagePerProjectile { get; init; }
        public float DamagePerAttack { get; init; }
        public float AttacksPerSecond { get; init; }
        public float EffectiveCooldown { get; init; }
        public float Dps { get; init; }
    }

    public void ConfigureForFullInventory()
    {
        _mode = InventoryModalMode.FullInventory;
        if (!IsInsideTree())
        {
            return;
        }

        ApplyModePresentation();
        Refresh();
    }

    public void ConfigureForWeaponSelection(int slotIndex)
    {
        _mode = InventoryModalMode.WeaponSelection;
        _focusedSlotIndex = Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1);
        if (!IsInsideTree())
        {
            return;
        }

        EquipSlotDropdown?.Select(_focusedSlotIndex);
        ApplyModePresentation();
        Refresh();
    }

    public void ConfigureForSocketManagement(int slotIndex)
    {
        _mode = InventoryModalMode.SocketManagement;
        _focusedSlotIndex = Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1);
        _selectedInstanceId = _inventoryManager?.GetEquippedItemAt(_focusedSlotIndex)?.InstanceId;
        if (!IsInsideTree())
        {
            return;
        }

        EquipSlotDropdown?.Select(_focusedSlotIndex);
        ApplyModePresentation();
        Refresh();
    }

    public override void _Ready()
    {
        base._Ready();

        ResolveUiReferences();
        ResolveServices();
        BuildGeneratedUi();
        EnsureSlotOptions();
        WireControls();
        ApplyPresentation();
        Refresh();

        if (_inventoryManager != null)
        {
            _inventoryManager.InventoryChanged += OnInventoryChanged;
            _inventoryManager.EquippedChanged += OnInventoryChanged;
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        if (_inventoryManager != null)
        {
            _inventoryManager.InventoryChanged -= OnInventoryChanged;
            _inventoryManager.EquippedChanged -= OnInventoryChanged;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || _itemCardButtons.Count == 0)
        {
            return;
        }

        _cardVisualTime += (float)delta;
        _cardVisualRefreshTimer -= (float)delta;
        if (_cardVisualRefreshTimer > 0f)
        {
            return;
        }

        _cardVisualRefreshTimer = 0.08f;
        RefreshAnimatedModifierCardStyles();
    }

    public override void Open()
    {
        base.Open();
        Refresh();
    }

    public void Refresh()
    {
        EnsureSlotOptions();
        ApplyModePresentation();
        EquipmentItem? selectedBeforeRefresh = GetSelectedItem();
        _selectedInstanceId = selectedBeforeRefresh?.InstanceId ?? _selectedInstanceId;

        _visibleItems.Clear();
        ItemListView?.Clear();
        ClearItemCards();

        if (_inventoryManager == null)
        {
            _bulkSelectedInstanceIds.Clear();
            UpdateSelectionToolbar();
            SetSummary("Inventory not connected.");
            SetDetailsText("Inventory service not connected.");
            ShapePreview?.SetItem(null);
            SetFeedback("Inventory service not connected.");
            return;
        }

        PruneBulkSelection();

        List<EquipmentItem> filtered = BuildVisibleItems();

        filtered.Sort(CompareVisibleItems);
        RefreshEquippedStrip();
        RefreshSlotOptionLabels();

        for (int itemIndex = 0; itemIndex < filtered.Count; itemIndex++)
        {
            EquipmentItem item = filtered[itemIndex];
            _visibleItems.Add(item);
            int listIndex = ItemListView?.AddItem(BuildListEntry(itemIndex, item)) ?? -1;
            if (listIndex >= 0)
            {
                ApplyListItemColor(listIndex, item);
            }
            AddItemCard(itemIndex, item);
        }

        UpdateInventorySummary();
        UpdateSelectionToolbar();
        RefreshSocketInventoryUi();

        if (_visibleItems.Count == 0)
        {
            SetDetailsText("No items match the current filter.");
            ShapePreview?.SetItem(null);
            SetFeedback(_inventoryManager.Items.Count == 0 ? "Inventory is empty." : "No matching items.");
            return;
        }

        int selectedIndex = FindVisibleIndexByInstanceId(_selectedInstanceId);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
            _selectedInstanceId = _visibleItems[0].InstanceId;
        }

        ItemListView?.Select(selectedIndex);
        UpdateSelectedItemDetails();
        RefreshItemCardStyles();
    }

    private void ResolveUiReferences()
    {
        ItemListView ??= GetNodeOrNull<ItemList>("ModalPanel/VBox/Content/Left/ItemList");
        ItemDetailsText ??= GetNodeOrNull<RichTextLabel>("ModalPanel/VBox/Content/Right/DetailsText");
        SummaryLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/SummaryLabel");
        FeedbackLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/FeedbackLabel");
        EquipSlotDropdown ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/Content/Left/Actions/EquipSlotDropdown");
        EquipButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/Actions/EquipButton");
        UnequipButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/Actions/UnequipButton");
        RefreshButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/Actions/RefreshButton");
        ShapePreview ??= GetNodeOrNull<ShapePreviewViewport>("ModalPanel/VBox/Content/Right/ShapePreview");
        _equippedStrip ??= GetNodeOrNull<HBoxContainer>("ModalPanel/VBox/EquippedStrip");
        _itemGridScroll ??= GetNodeOrNull<ScrollContainer>("ModalPanel/VBox/Content/Left/ItemGridScroll");
        _itemGrid ??= GetNodeOrNull<GridContainer>("ModalPanel/VBox/Content/Left/ItemGridScroll/ItemGrid");
        _browserTools ??= GetNodeOrNull<HBoxContainer>("ModalPanel/VBox/Content/Left/BrowserTools");
        _actionBar ??= GetNodeOrNull<HBoxContainer>("ModalPanel/VBox/Content/Left/Actions");
        _filterDropdown ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/Content/Left/BrowserTools/FilterDropdown");
        _sortDropdown ??= GetNodeOrNull<OptionButton>("ModalPanel/VBox/Content/Left/BrowserTools/SortDropdown");
        _selectionToolbar ??= GetNodeOrNull<HBoxContainer>("ModalPanel/VBox/Content/Left/SelectionTools");
        _selectModeButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/SelectionTools/SelectModeButton");
        _selectAllVisibleButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/SelectionTools/SelectAllVisibleButton");
        _clearSelectionButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/SelectionTools/ClearSelectionButton");
        _deleteSelectedButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Left/SelectionTools/DeleteSelectedButton");
        _advancedInfoButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Content/Right/AdvancedInfoButton");
        _deleteConfirmDialog ??= GetNodeOrNull<ConfirmationDialog>("DeleteConfirmDialog");
        EnsureShapePreview();
    }

    private void BuildGeneratedUi()
    {
        VBoxContainer? rootBox = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox");
        VBoxContainer? leftPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Left");
        if (rootBox != null && _equippedStrip == null)
        {
            _equippedStrip = new HBoxContainer
            {
                Name = "EquippedStrip",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _equippedStrip.AddThemeConstantOverride("separation", 8);
            int insertIndex = SummaryLabel?.GetIndex() + 1 ?? 2;
            rootBox.AddChild(_equippedStrip);
            rootBox.MoveChild(_equippedStrip, Mathf.Clamp(insertIndex, 0, rootBox.GetChildCount() - 1));
        }

        if (leftPanel != null && (_filterDropdown == null || _sortDropdown == null))
        {
            _browserTools = new HBoxContainer
            {
                Name = "BrowserTools",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _browserTools.AddThemeConstantOverride("separation", 8);

            Label filterLabel = new() { Text = "Filter", CustomMinimumSize = new Vector2(42f, 0f) };
            _filterDropdown = new OptionButton { Name = "FilterDropdown", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Label sortLabel = new() { Text = "Sort", CustomMinimumSize = new Vector2(32f, 0f) };
            _sortDropdown = new OptionButton { Name = "SortDropdown", SizeFlagsHorizontal = SizeFlags.ExpandFill };

            _browserTools.AddChild(filterLabel);
            _browserTools.AddChild(_filterDropdown);
            _browserTools.AddChild(sortLabel);
            _browserTools.AddChild(_sortDropdown);

            int itemListIndex = ItemListView?.GetIndex() ?? 0;
            leftPanel.AddChild(_browserTools);
            leftPanel.MoveChild(_browserTools, Mathf.Clamp(itemListIndex, 0, leftPanel.GetChildCount() - 1));
        }

        EnsureItemGrid(leftPanel);
        EnsureSelectionToolbar(leftPanel);
        EnsureAdvancedInfoButton();
        EnsureSocketInventoryPanel();
        EnsureContextMenu();
        EnsureDeleteConfirmDialog();
        EnsureFilterOptions();
        EnsureSortOptions();
    }

    private void EnsureSocketInventoryPanel()
    {
        if (_socketInventoryPanel != null)
        {
            return;
        }

        VBoxContainer? rightPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Right");
        if (rightPanel == null)
        {
            return;
        }

        _socketInventoryScroll = new ScrollContainer
        {
            Name = "SocketInventoryScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        _socketInventoryPanel = new VBoxContainer
        {
            Name = "SocketInventoryPanel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _socketInventoryPanel.AddThemeConstantOverride("separation", 8);

        _socketTargetLabel = new Label
        {
            Name = "SocketTargetLabel",
            Text = "Select a weapon to manage its essences and modulators.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _socketTargetLabel.AddThemeFontSizeOverride("font_size", 13);
        _socketTargetLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.92f, 1f, 1f));
        _socketInventoryPanel.AddChild(_socketTargetLabel);

        HBoxContainer columns = new()
        {
            Name = "SocketColumns",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", 10);
        _socketInventoryPanel.AddChild(columns);

        VBoxContainer installedCol = new()
        {
            Name = "InstalledColumn",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        installedCol.AddThemeConstantOverride("separation", 6);
        columns.AddChild(installedCol);

        VBoxContainer inventoryCol = new()
        {
            Name = "InventoryColumn",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        inventoryCol.AddThemeConstantOverride("separation", 6);
        columns.AddChild(inventoryCol);

        installedCol.AddChild(CreateSocketColumnHeader("EQUIPPED ON WEAPON"));

        PanelContainer installedEssencePanel = CreateSocketSectionShell(out VBoxContainer installedEssenceContent);
        installedEssenceContent.AddChild(CreateSocketSectionTitle("Essence"));
        _installedEssenceLabel = new Label
        {
            Name = "InstalledEssenceLabel",
            Text = "(none)",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _installedEssenceLabel.AddThemeFontSizeOverride("font_size", 12);
        installedEssenceContent.AddChild(_installedEssenceLabel);
        _returnEssenceButton = new Button { Name = "ReturnEssenceButton", Text = "Return Essence", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        installedEssenceContent.AddChild(_returnEssenceButton);
        installedCol.AddChild(installedEssencePanel);

        installedCol.AddChild(CreateSocketSection(
            "Modulators",
            "InstalledModulatorList",
            96f,
            out _installedModulatorList,
            out VBoxContainer installedModulatorContent));
        _returnModulatorButton = new Button { Name = "ReturnModulatorButton", Text = "Return Selected", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        installedModulatorContent.AddChild(_returnModulatorButton);

        inventoryCol.AddChild(CreateSocketColumnHeader("INVENTORY"));

        inventoryCol.AddChild(CreateSocketSection(
            "Essences",
            "StoredEssenceList",
            110f,
            out _storedEssenceList,
            out VBoxContainer storedEssenceContent));
        _socketEssenceButton = new Button { Name = "SocketEssenceButton", Text = "Socket Essence", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        storedEssenceContent.AddChild(_socketEssenceButton);

        inventoryCol.AddChild(CreateSocketSection(
            "Modulators",
            "StoredModulatorList",
            110f,
            out _storedModulatorList,
            out VBoxContainer storedModulatorContent));
        _installModulatorButton = new Button { Name = "InstallModulatorButton", Text = "Install Modulator", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        storedModulatorContent.AddChild(_installModulatorButton);

        _socketHintLabel = new Label
        {
            Name = "SocketHintLabel",
            Text = "Pick an item from the inventory column on the right, then press its action button to install it on the equipped weapon. Use the Return buttons on the left to send installed items back to your inventory.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _socketHintLabel.AddThemeFontSizeOverride("font_size", 11);
        _socketHintLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.72f, 0.86f, 1f));
        _socketInventoryPanel.AddChild(_socketHintLabel);

        _socketInventoryScroll.AddChild(_socketInventoryPanel);
        int insertIndex = ItemDetailsText?.GetIndex() + 1 ?? rightPanel.GetChildCount();
        rightPanel.AddChild(_socketInventoryScroll);
        rightPanel.MoveChild(_socketInventoryScroll, Mathf.Clamp(insertIndex, 0, rightPanel.GetChildCount() - 1));
    }

    private static Label CreateSocketColumnHeader(string text)
    {
        Label label = new()
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.55f, 0.74f, 0.92f, 1f));
        return label;
    }

    private static Label CreateSocketSectionTitle(string text)
    {
        Label label = new()
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.84f, 0.93f, 1f, 1f));
        return label;
    }

    private static PanelContainer CreateSocketSectionShell(out VBoxContainer content)
    {
        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.040f, 0.060f, 0.105f, 0.92f), new Color(0.24f, 0.54f, 0.68f, 0.34f), 1));

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 6);
        margin.AddChild(content);
        return panel;
    }

    private static PanelContainer CreateSocketSection(
        string title,
        string listName,
        float minimumHeight,
        out ItemList list,
        out VBoxContainer content)
    {
        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.040f, 0.060f, 0.105f, 0.92f), new Color(0.24f, 0.54f, 0.68f, 0.34f), 1));

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 6);
        margin.AddChild(content);

        Label header = new()
        {
            Text = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        header.AddThemeFontSizeOverride("font_size", 12);
        header.AddThemeColorOverride("font_color", new Color(0.84f, 0.93f, 1f, 1f));
        content.AddChild(header);

        list = new ItemList
        {
            Name = listName,
            CustomMinimumSize = new Vector2(0f, minimumHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AllowReselect = true,
        };
        list.AddThemeConstantOverride("v_separation", 3);
        list.AddThemeFontSizeOverride("font_size", 11);
        content.AddChild(list);
        return panel;
    }

    private static HBoxContainer CreateSocketActionRow()
    {
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 6);
        return row;
    }

    private void EnsureSelectionToolbar(VBoxContainer? leftPanel)
    {
        if (leftPanel == null)
        {
            return;
        }

        if (_selectionToolbar == null)
        {
            _selectionToolbar = new HBoxContainer
            {
                Name = "SelectionTools",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _selectionToolbar.AddThemeConstantOverride("separation", 6);

            int insertIndex = _itemGridScroll?.GetIndex() ?? GetNodeOrNull<HBoxContainer>("ModalPanel/VBox/Content/Left/Actions")?.GetIndex() ?? leftPanel.GetChildCount();
            leftPanel.AddChild(_selectionToolbar);
            leftPanel.MoveChild(_selectionToolbar, Mathf.Clamp(insertIndex, 0, leftPanel.GetChildCount() - 1));
        }

        _selectModeButton ??= CreateToolbarButton("SelectModeButton", true, true);
        _selectAllVisibleButton ??= CreateToolbarButton("SelectAllVisibleButton", false, true);
        _clearSelectionButton ??= CreateToolbarButton("ClearSelectionButton", false, true);
        _deleteSelectedButton ??= CreateToolbarButton("DeleteSelectedButton", false, true);

        AddToolbarButtonIfNeeded(_selectModeButton);
        AddToolbarButtonIfNeeded(_selectAllVisibleButton);
        AddToolbarButtonIfNeeded(_clearSelectionButton);
        AddToolbarButtonIfNeeded(_deleteSelectedButton);
        UpdateSelectionToolbar();
    }

    private Button CreateToolbarButton(string name, bool toggleMode, bool expand)
    {
        return new Button
        {
            Name = name,
            ToggleMode = toggleMode,
            SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.ShrinkCenter,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
    }

    private void AddToolbarButtonIfNeeded(Button? button)
    {
        if (_selectionToolbar == null || button == null || button.GetParent() == _selectionToolbar)
        {
            return;
        }

        _selectionToolbar.AddChild(button);
    }

    private void EnsureDeleteConfirmDialog()
    {
        if (_deleteConfirmDialog != null)
        {
            _deleteConfirmDialog.Confirmed -= OnDeleteConfirmed;
            _deleteConfirmDialog.Confirmed += OnDeleteConfirmed;
            return;
        }

        _deleteConfirmDialog = new ConfirmationDialog
        {
            Name = "DeleteConfirmDialog",
            Title = "Delete Inventory Items",
            DialogText = "Delete selected inventory items?",
            Exclusive = true,
        };
        _deleteConfirmDialog.Confirmed += OnDeleteConfirmed;
        AddChild(_deleteConfirmDialog);
    }

    private void EnsureContextMenu()
    {
        if (_contextMenu != null)
        {
            return;
        }

        _contextMenu = new PopupMenu
        {
            Name = "InventoryContextMenu",
        };
        _contextMenu.IdPressed += OnContextMenuIdPressed;
        AddChild(_contextMenu);
    }

    private void EnsureAdvancedInfoButton()
    {
        if (_advancedInfoButton != null)
        {
            UpdateAdvancedInfoButton();
            return;
        }

        VBoxContainer? rightPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Right");
        if (rightPanel == null)
        {
            return;
        }

        _advancedInfoButton = new Button
        {
            Name = "AdvancedInfoButton",
            ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            TooltipText = "Show comparisons, raw weapon profile, live bonuses, and synergy diagnostics.",
        };

        int detailsIndex = ItemDetailsText?.GetIndex() ?? rightPanel.GetChildCount();
        rightPanel.AddChild(_advancedInfoButton);
        rightPanel.MoveChild(_advancedInfoButton, Mathf.Clamp(detailsIndex, 0, rightPanel.GetChildCount() - 1));
        UpdateAdvancedInfoButton();
    }

    private void EnsureItemGrid(VBoxContainer? leftPanel)
    {
        if (leftPanel == null)
        {
            return;
        }

        _itemGridScroll ??= GetNodeOrNull<ScrollContainer>("ModalPanel/VBox/Content/Left/ItemGridScroll");
        if (_itemGridScroll == null)
        {
            _itemGridScroll = new ScrollContainer
            {
                Name = "ItemGridScroll",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            int actionsIndex = GetNodeOrNull<HBoxContainer>("ModalPanel/VBox/Content/Left/Actions")?.GetIndex() ?? leftPanel.GetChildCount();
            leftPanel.AddChild(_itemGridScroll);
            leftPanel.MoveChild(_itemGridScroll, Mathf.Clamp(actionsIndex, 0, leftPanel.GetChildCount() - 1));
        }

        _itemGrid ??= _itemGridScroll.GetNodeOrNull<GridContainer>("ItemGrid");
        if (_itemGrid == null)
        {
            _itemGrid = new GridContainer
            {
                Name = "ItemGrid",
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _itemGrid.AddThemeConstantOverride("h_separation", 8);
            _itemGrid.AddThemeConstantOverride("v_separation", 8);
            _itemGridScroll.AddChild(_itemGrid);
        }
    }

    private void EnsureShapePreview()
    {
        if (ShapePreview != null)
        {
            return;
        }

        VBoxContainer? rightPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Right");
        if (rightPanel == null)
        {
            return;
        }

        ShapePreview = new ShapePreviewViewport
        {
            Name = "ShapePreview",
            PreviewSize = new Vector2I(420, 230),
            VisualScale = 1.38f,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        rightPanel.AddChild(ShapePreview);
        rightPanel.MoveChild(ShapePreview, Mathf.Min(1, rightPanel.GetChildCount() - 1));
    }

    private void WireControls()
    {
        if (ItemListView != null)
        {
            ItemListView.ItemSelected += OnItemSelected;
        }

        if (EquipButton != null)
        {
            EquipButton.Pressed += OnEquipPressed;
        }

        if (UnequipButton != null)
        {
            UnequipButton.Pressed += OnUnequipPressed;
        }

        if (RefreshButton != null)
        {
            RefreshButton.Pressed += Refresh;
        }

        if (_storedEssenceList != null)
        {
            _storedEssenceList.ItemSelected += OnSocketSelectionChanged;
        }

        if (_installedModulatorList != null)
        {
            _installedModulatorList.ItemSelected += OnSocketSelectionChanged;
        }

        if (_storedModulatorList != null)
        {
            _storedModulatorList.ItemSelected += OnSocketSelectionChanged;
        }

        if (_socketEssenceButton != null)
        {
            _socketEssenceButton.Pressed += OnSocketEssencePressed;
        }

        if (_returnEssenceButton != null)
        {
            _returnEssenceButton.Pressed += OnReturnEssencePressed;
        }

        if (_installModulatorButton != null)
        {
            _installModulatorButton.Pressed += OnInstallStoredModulatorPressed;
        }

        if (_returnModulatorButton != null)
        {
            _returnModulatorButton.Pressed += OnReturnInstalledModulatorPressed;
        }

        if (_selectModeButton != null)
        {
            _selectModeButton.Pressed += OnSelectModePressed;
        }

        if (_selectAllVisibleButton != null)
        {
            _selectAllVisibleButton.Pressed += OnSelectAllVisiblePressed;
        }

        if (_clearSelectionButton != null)
        {
            _clearSelectionButton.Pressed += OnClearSelectionPressed;
        }

        if (_deleteSelectedButton != null)
        {
            _deleteSelectedButton.Pressed += OnDeleteSelectedPressed;
        }

        if (_filterDropdown != null)
        {
            _filterDropdown.ItemSelected += OnFilterOrSortChanged;
        }

        if (_sortDropdown != null)
        {
            _sortDropdown.ItemSelected += OnFilterOrSortChanged;
        }

        if (_advancedInfoButton != null)
        {
            _advancedInfoButton.Pressed += OnAdvancedInfoPressed;
        }
    }

    private void ApplyPresentation()
    {
        Panel? panel = GetNodeOrNull<Panel>("ModalPanel");
        if (panel != null)
        {
            panel.OffsetLeft = 150f;
            panel.OffsetTop = 84f;
            panel.OffsetRight = 1450f;
            panel.OffsetBottom = 812f;
            panel.CustomMinimumSize = new Vector2(1300f, 728f);
            panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.035f, 0.050f, 0.095f, 0.96f), new Color(0.16f, 0.58f, 0.64f, 0.42f), 2));
        }

        Label? title = GetNodeOrNull<Label>("ModalPanel/VBox/Title");
        if (title != null)
        {
            title.Text = "Inventory Loadout";
            title.AddThemeFontSizeOverride("font_size", 22);
            title.AddThemeColorOverride("font_color", new Color(0.88f, 0.96f, 1f, 1f));
        }

        if (ItemListView != null)
        {
            ItemListView.Visible = false;
            ItemListView.AllowReselect = true;
            ItemListView.AddThemeConstantOverride("v_separation", 4);
            ItemListView.AddThemeFontSizeOverride("font_size", 13);
        }

        if (_itemGridScroll != null)
        {
            _itemGridScroll.CustomMinimumSize = new Vector2(0f, 420f);
        }

        if (ItemDetailsText != null)
        {
            ItemDetailsText.BbcodeEnabled = true;
            ItemDetailsText.ScrollActive = true;
            ItemDetailsText.CustomMinimumSize = new Vector2(0f, 260f);
            ItemDetailsText.AddThemeFontSizeOverride("normal_font_size", 13);
        }

        if (ShapePreview != null)
        {
            ShapePreview.PreviewSize = new Vector2I(560, 320);
            ShapePreview.CustomMinimumSize = new Vector2(560f, 320f);
            ShapePreview.VisualScale = 1.54f;
        }

        VBoxContainer? leftPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Left");
        VBoxContainer? rightPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Right");
        if (leftPanel != null)
        {
            leftPanel.CustomMinimumSize = new Vector2(360f, 0f);
            leftPanel.SizeFlagsStretchRatio = 0.78f;
        }

        if (rightPanel != null)
        {
            rightPanel.CustomMinimumSize = new Vector2(620f, 0f);
            rightPanel.SizeFlagsStretchRatio = 1.22f;
        }

        StyleButton(EquipButton, new Color(0.08f, 0.45f, 0.38f, 0.90f), new Color(0.23f, 0.95f, 0.78f, 0.74f));
        StyleButton(UnequipButton, new Color(0.34f, 0.12f, 0.16f, 0.90f), new Color(1f, 0.48f, 0.52f, 0.68f));
        StyleButton(RefreshButton, new Color(0.10f, 0.18f, 0.34f, 0.90f), new Color(0.42f, 0.66f, 1f, 0.68f));
        StyleButton(_selectModeButton, new Color(0.10f, 0.18f, 0.28f, 0.90f), new Color(0.45f, 0.70f, 0.92f, 0.68f));
        StyleButton(_selectAllVisibleButton, new Color(0.10f, 0.18f, 0.28f, 0.90f), new Color(0.45f, 0.70f, 0.92f, 0.68f));
        StyleButton(_clearSelectionButton, new Color(0.12f, 0.15f, 0.22f, 0.90f), new Color(0.48f, 0.58f, 0.72f, 0.62f));
        StyleButton(_deleteSelectedButton, new Color(0.38f, 0.09f, 0.12f, 0.92f), new Color(1f, 0.36f, 0.42f, 0.74f));
        StyleButton(_advancedInfoButton, new Color(0.12f, 0.15f, 0.24f, 0.90f), new Color(0.54f, 0.72f, 1f, 0.60f));
        StyleButton(_socketEssenceButton, new Color(0.08f, 0.42f, 0.34f, 0.90f), new Color(0.20f, 0.92f, 0.72f, 0.70f));
        StyleButton(_returnEssenceButton, new Color(0.28f, 0.14f, 0.18f, 0.90f), new Color(0.96f, 0.50f, 0.56f, 0.68f));
        StyleButton(_installModulatorButton, new Color(0.10f, 0.22f, 0.42f, 0.92f), new Color(0.44f, 0.76f, 1f, 0.72f));
        StyleButton(_returnModulatorButton, new Color(0.20f, 0.15f, 0.28f, 0.92f), new Color(0.78f, 0.62f, 1f, 0.68f));
        StyleButton(CloseButton, new Color(0.10f, 0.12f, 0.18f, 0.90f), new Color(0.54f, 0.66f, 0.82f, 0.62f));
        UpdateSelectionToolbar();
        ApplyModePresentation();
    }

    protected override void OnModalLayoutChanged(Vector2 panelSize, float scale)
    {
        float contentWidth = Mathf.Max(320f, panelSize.X - 48f);
        bool socketMode = _mode == InventoryModalMode.SocketManagement;
        bool selectionMode = _mode == InventoryModalMode.WeaponSelection;
        bool showSocketPanel = socketMode;
        bool showShapePreview = !socketMode;

        float leftWidth;
        float rightWidth;
        if (socketMode)
        {
            leftWidth = 0f;
            rightWidth = contentWidth;
        }
        else
        {
            leftWidth = selectionMode
                ? Mathf.Clamp(contentWidth * 0.40f, 280f, 420f)
                : Mathf.Clamp(contentWidth * 0.34f, 230f, 360f);
            rightWidth = Mathf.Clamp(contentWidth - leftWidth - 24f, 320f, 720f);
        }

        float verticalScale = Mathf.Clamp(scale, 0.62f, 1f);

        if (_itemGrid != null)
        {
            _itemGrid.Columns = leftWidth < 350f ? 1 : 2;
        }

        if (_itemGridScroll != null)
        {
            _itemGridScroll.CustomMinimumSize = new Vector2(0f, Mathf.Clamp(panelSize.Y * 0.52f, 220f, 420f));
        }

        if (ItemDetailsText != null)
        {
            float detailRatio = socketMode ? 0.18f : 0.30f;
            float detailMin = socketMode ? 110f : 170f;
            float detailMax = socketMode ? 180f : 260f;
            ItemDetailsText.CustomMinimumSize = new Vector2(0f, Mathf.Clamp(panelSize.Y * detailRatio, detailMin, detailMax));
        }

        if (_socketInventoryScroll != null)
        {
            _socketInventoryScroll.CustomMinimumSize = socketMode
                ? new Vector2(0f, Mathf.Clamp(panelSize.Y * 0.50f, 280f, 520f))
                : new Vector2(0f, 0f);
        }

        if (ShapePreview != null)
        {
            ShapePreview.Visible = showShapePreview;
            if (showShapePreview)
            {
                int previewWidth = Mathf.RoundToInt(Mathf.Clamp(rightWidth, 280f, 560f));
                int previewHeight = Mathf.RoundToInt(Mathf.Clamp(panelSize.Y * 0.34f, 150f, 320f));
                ShapePreview.ApplyPreviewSize(new Vector2I(previewWidth, previewHeight), Mathf.Clamp(1.54f * verticalScale, 1.05f, 1.54f));
            }
        }

        // Lists inside socket columns: only relevant when socket panel is visible.
        float listMin = socketMode ? Mathf.Clamp(panelSize.Y * 0.18f, 110f, 200f) : 0f;
        if (_storedEssenceList != null)
        {
            _storedEssenceList.CustomMinimumSize = new Vector2(0f, listMin);
        }

        if (_installedModulatorList != null)
        {
            _installedModulatorList.CustomMinimumSize = new Vector2(0f, listMin);
        }

        if (_storedModulatorList != null)
        {
            _storedModulatorList.CustomMinimumSize = new Vector2(0f, listMin);
        }

        VBoxContainer? leftPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Left");
        VBoxContainer? rightPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Right");
        if (leftPanel != null)
        {
            leftPanel.Visible = !socketMode;
            leftPanel.CustomMinimumSize = new Vector2(leftWidth, 0f);
        }

        if (rightPanel != null)
        {
            rightPanel.CustomMinimumSize = new Vector2(rightWidth, 0f);
        }
    }

    private void ApplyModePresentation()
    {
        Label? title = GetNodeOrNull<Label>("ModalPanel/VBox/Title");
        string slotLabel = GetSlotShortLabel(Mathf.Clamp(_focusedSlotIndex, 0, EquippedSlotCount - 1));

        if (title != null)
        {
            title.Text = _mode switch
            {
                InventoryModalMode.WeaponSelection => $"Choose Weapon - {slotLabel}",
                InventoryModalMode.SocketManagement => $"Essence & Modulators - {slotLabel}",
                _ => "Inventory Loadout",
            };
        }

        bool showWeaponBrowser = _mode != InventoryModalMode.SocketManagement;
        bool showSocketPanel = _mode == InventoryModalMode.SocketManagement;
        bool showShapePreview = _mode != InventoryModalMode.SocketManagement;

        VBoxContainer? leftPanel = GetNodeOrNull<VBoxContainer>("ModalPanel/VBox/Content/Left");
        if (leftPanel != null)
        {
            leftPanel.Visible = showWeaponBrowser;
        }

        if (_browserTools != null)
        {
            _browserTools.Visible = showWeaponBrowser;
        }

        if (_itemGridScroll != null)
        {
            _itemGridScroll.Visible = showWeaponBrowser;
        }

        if (ShapePreview != null)
        {
            ShapePreview.Visible = showShapePreview;
        }

        if (_selectionToolbar != null)
        {
            _selectionToolbar.Visible = _mode == InventoryModalMode.FullInventory;
        }

        if (_actionBar != null)
        {
            _actionBar.Visible = _mode != InventoryModalMode.SocketManagement;
        }

        if (EquipSlotDropdown != null)
        {
            EquipSlotDropdown.Visible = _mode == InventoryModalMode.FullInventory;
        }

        if (EquipButton != null)
        {
            EquipButton.Visible = _mode != InventoryModalMode.SocketManagement;
            EquipButton.Text = _mode == InventoryModalMode.WeaponSelection ? $"Equip to {slotLabel}" : "Equip";
        }

        if (UnequipButton != null)
        {
            UnequipButton.Visible = _mode == InventoryModalMode.FullInventory;
        }

        if (RefreshButton != null)
        {
            RefreshButton.Visible = _mode == InventoryModalMode.FullInventory;
        }

        if (_advancedInfoButton != null)
        {
            _advancedInfoButton.Visible = _mode != InventoryModalMode.WeaponSelection;
        }

        if (_socketInventoryScroll != null)
        {
            _socketInventoryScroll.Visible = showSocketPanel;
        }

        if (_socketInventoryPanel != null)
        {
            _socketInventoryPanel.Visible = showSocketPanel;
        }

        RecalculateModalLayout(true);
    }

    private List<EquipmentItem> BuildVisibleItems()
    {
        List<EquipmentItem> filtered = new();
        if (_inventoryManager == null)
        {
            return filtered;
        }

        if (_mode == InventoryModalMode.SocketManagement)
        {
            EquipmentItem? targetItem = GetSocketTargetItem();
            if (targetItem != null)
            {
                filtered.Add(targetItem);
                _selectedInstanceId = targetItem.InstanceId;
            }

            return filtered;
        }

        for (int itemIndex = 0; itemIndex < _inventoryManager.Items.Count; itemIndex++)
        {
            EquipmentItem item = _inventoryManager.Items[itemIndex];
            if (ShouldShowItem(item))
            {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    private EquipmentItem? GetSocketTargetItem()
    {
        if (_inventoryManager == null)
        {
            return null;
        }

        if (_mode == InventoryModalMode.SocketManagement)
        {
            return _inventoryManager.GetEquippedItemAt(Mathf.Clamp(_focusedSlotIndex, 0, EquippedSlotCount - 1))
                ?? FindItemByInstanceId(_selectedInstanceId);
        }

        EquipmentItem? selected = GetSelectedItem();
        if (selected != null)
        {
            return selected;
        }

        return _inventoryManager.GetEquippedItemAt(Mathf.Clamp(_focusedSlotIndex, 0, EquippedSlotCount - 1));
    }

    private void RefreshSocketInventoryUi()
    {
        if (_socketInventoryPanel == null)
        {
            return;
        }

        EquipmentItem? targetItem = GetSocketTargetItem();
        if (_socketTargetLabel != null)
        {
            _socketTargetLabel.Text = targetItem == null
                ? _mode == InventoryModalMode.SocketManagement
                    ? $"{GetSlotShortLabel(_focusedSlotIndex)} has no equipped weapon. Equip one from the Inventory first."
                    : "Select a weapon to manage its essences and modulators."
                : $"Customizing: {BuildPickerLabel(targetItem, false)}  \u2022  {FormatEquippedStatus(FindEquippedSlot(targetItem))}";
        }

        if (_installedEssenceLabel != null)
        {
            if (targetItem == null)
            {
                _installedEssenceLabel.Text = "-";
                _installedEssenceLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.62f, 0.72f, 1f));
            }
            else if (!targetItem.HasEssenceSlotUnlocked)
            {
                _installedEssenceLabel.Text = "Essence slot locked (level up to unlock)";
                _installedEssenceLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.55f, 0.50f, 1f));
            }
            else if (targetItem.HasEquippedEssence && targetItem.Essence is ModifierData essence)
            {
                _installedEssenceLabel.Text = essence.DisplayName;
                _installedEssenceLabel.AddThemeColorOverride("font_color", essence.UiColor);
            }
            else
            {
                _installedEssenceLabel.Text = "(no essence socketed)";
                _installedEssenceLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.70f, 0.82f, 1f));
            }
        }

        if (_storedEssenceList != null)
        {
            int selectedIndex = GetFirstSelectedIndex(_storedEssenceList);
            _storedEssenceList.Clear();
            if (_inventoryManager != null)
            {
                for (int i = 0; i < _inventoryManager.StoredEssences.Count; i++)
                {
                    ModifierData essence = _inventoryManager.StoredEssences[i];
                    int itemIndex = _storedEssenceList.AddItem(essence.DisplayName);
                    _storedEssenceList.SetItemCustomFgColor(itemIndex, essence.UiColor);
                }
            }

            if (_storedEssenceList.ItemCount > 0)
            {
                _storedEssenceList.Select(Mathf.Clamp(selectedIndex, 0, _storedEssenceList.ItemCount - 1));
            }
        }

        if (_installedModulatorList != null)
        {
            int selectedIndex = GetFirstSelectedIndex(_installedModulatorList);
            _installedModulatorList.Clear();
            if (targetItem != null)
            {
                for (int i = 0; i < targetItem.Modulators.Count; i++)
                {
                    WeaponModulatorData modulator = targetItem.Modulators[i];
                    int itemIndex = _installedModulatorList.AddItem(BuildModulatorListLabel(modulator));
                    _installedModulatorList.SetItemCustomFgColor(itemIndex, ModulatorLoot.GetTierColor(modulator.PowerTier));
                    _installedModulatorList.SetItemTooltip(itemIndex, ModulatorQualityText.Header(modulator) + "\n" + ModulatorQualityText.Describe(modulator));
                }
            }

            if (_installedModulatorList.ItemCount > 0)
            {
                _installedModulatorList.Select(Mathf.Clamp(selectedIndex, 0, _installedModulatorList.ItemCount - 1));
            }
        }

        if (_storedModulatorList != null)
        {
            int selectedIndex = GetFirstSelectedIndex(_storedModulatorList);
            _storedModulatorList.Clear();
            if (_inventoryManager != null)
            {
                for (int i = 0; i < _inventoryManager.StoredModulators.Count; i++)
                {
                    WeaponModulatorData modulator = _inventoryManager.StoredModulators[i];
                    int itemIndex = _storedModulatorList.AddItem(BuildModulatorListLabel(modulator));
                    _storedModulatorList.SetItemCustomFgColor(itemIndex, ModulatorLoot.GetTierColor(modulator.PowerTier));
                    _storedModulatorList.SetItemTooltip(itemIndex, ModulatorQualityText.Header(modulator) + "\n" +
                        ModulatorQualityText.Describe(modulator) + "\n\n" + ModulatorQualityText.Compare(modulator, targetItem));
                }
            }

            if (_storedModulatorList.ItemCount > 0)
            {
                _storedModulatorList.Select(Mathf.Clamp(selectedIndex, 0, _storedModulatorList.ItemCount - 1));
            }
        }

        UpdateSocketActionState(targetItem);
    }

    private void UpdateSocketActionState(EquipmentItem? targetItem)
    {
        int selectedStoredEssence = GetFirstSelectedIndex(_storedEssenceList);
        int selectedInstalledModulator = GetFirstSelectedIndex(_installedModulatorList);
        int selectedStoredModulator = GetFirstSelectedIndex(_storedModulatorList);
        WeaponModulatorData? selectedModulator = _inventoryManager != null && selectedStoredModulator >= 0 && selectedStoredModulator < _inventoryManager.StoredModulators.Count
            ? _inventoryManager.StoredModulators[selectedStoredModulator]
            : null;
        string? modulatorBlockReason = GetModulatorInstallBlockReason(targetItem, selectedModulator);

        if (_socketEssenceButton != null)
        {
            _socketEssenceButton.Text = targetItem?.HasEquippedEssence == true ? "Replace Essence" : "Socket Essence";
            _socketEssenceButton.Disabled = targetItem == null || selectedStoredEssence < 0 || !targetItem.HasEssenceSlotUnlocked;
            _socketEssenceButton.TooltipText = targetItem == null
                ? "Select a weapon first."
                : !targetItem.HasEssenceSlotUnlocked
                    ? "This weapon has not unlocked its essence slot yet."
                    : "Install the selected stored essence on the target weapon.";
        }

        if (_returnEssenceButton != null)
        {
            _returnEssenceButton.Disabled = targetItem == null || !targetItem.HasEquippedEssence;
            _returnEssenceButton.TooltipText = "Return the currently socketed essence to inventory.";
        }

        if (_installModulatorButton != null)
        {
            _installModulatorButton.Text = targetItem == null
                ? "Install Modulator"
                : targetItem.AvailableModulatorSlots > 0
                    ? $"Install Modulator ({targetItem.AvailableModulatorSlots} free)"
                    : "No Modulator Slots";
            _installModulatorButton.Disabled = targetItem == null || selectedStoredModulator < 0 || targetItem.AvailableModulatorSlots <= 0 || modulatorBlockReason != null;
            _installModulatorButton.TooltipText = (modulatorBlockReason ?? "Install the selected stored modulator on the target weapon.") +
                (selectedModulator == null ? "" : "\n\n" + ModulatorQualityText.Compare(selectedModulator, targetItem));
        }

        if (_returnModulatorButton != null)
        {
            _returnModulatorButton.Disabled = targetItem == null || selectedInstalledModulator < 0;
            _returnModulatorButton.TooltipText = "Return the selected installed modulator to inventory.";
        }
    }

    private static int GetFirstSelectedIndex(ItemList? list)
    {
        if (list == null)
        {
            return -1;
        }

        int[] selected = list.GetSelectedItems();
        return selected.Length == 0 ? -1 : selected[0];
    }

    private static string BuildModulatorListLabel(WeaponModulatorData modulator)
    {
        return $"{modulator.DisplayName}  |  Tier {(int)modulator.PowerTier + 1}  |  {modulator.QualityStrength * 100f:0}% power";
    }

    private void EnsureFilterOptions()
    {
        if (_filterDropdown == null || _filterDropdown.ItemCount > 0)
        {
            return;
        }

        _filterDropdown.AddItem("All Items");
        _filterDropdown.AddItem("Equipped");
        _filterDropdown.AddItem("Unequipped");
        _filterDropdown.AddItem("2D Shapes");
        _filterDropdown.AddItem("3D Shapes");
        _filterDropdown.AddItem("Ultra Shapes");
        _filterDropdown.AddItem("Mythic Shapes");
        _filterDropdown.AddItem("Passive Weapons");
        _filterDropdown.AddItem("Offense Rolls");
        _filterDropdown.AddItem("Defense Rolls");
        _filterDropdown.AddItem("Projectile Rolls");
        _filterDropdown.Select(0);
    }

    private void EnsureSortOptions()
    {
        if (_sortDropdown == null || _sortDropdown.ItemCount > 0)
        {
            return;
        }

        _sortDropdown.AddItem("Rarity");
        _sortDropdown.AddItem("Level");
        _sortDropdown.AddItem("Damage");
        _sortDropdown.AddItem("Health");
        _sortDropdown.AddItem("Shape");
        _sortDropdown.AddItem("Essence");
        _sortDropdown.AddItem("Equipped First");
        _sortDropdown.Select(0);
    }

    private void EnsureSlotOptions()
    {
        if (EquipSlotDropdown == null || EquipSlotDropdown.ItemCount > 0)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < EquippedSlotCount; slotIndex++)
        {
            EquipSlotDropdown.AddItem(GetSlotShortLabel(slotIndex), slotIndex);
        }

        EquipSlotDropdown.Select(0);
    }

    private void RefreshSlotOptionLabels()
    {
        if (EquipSlotDropdown == null)
        {
            return;
        }

        int selected = Mathf.Clamp(EquipSlotDropdown.Selected, 0, EquippedSlotCount - 1);
        EquipSlotDropdown.Clear();

        for (int slotIndex = 0; slotIndex < EquippedSlotCount; slotIndex++)
        {
            EquipmentItem? equipped = _inventoryManager?.GetEquippedItemAt(slotIndex);
            string label = GetSlotShortLabel(slotIndex);
            if (equipped != null)
            {
                label += $" - {BuildPickerLabel(equipped, false)}";
            }

            EquipSlotDropdown.AddItem(label, slotIndex);
        }

        EquipSlotDropdown.Select(selected);
    }

    private void RefreshEquippedStrip()
    {
        if (_equippedStrip == null)
        {
            return;
        }

        foreach (Node child in _equippedStrip.GetChildren())
        {
            _equippedStrip.RemoveChild(child);
            child.QueueFree();
        }

        for (int slotIndex = 0; slotIndex < EquippedSlotCount; slotIndex++)
        {
            EquipmentItem? equipped = _inventoryManager?.GetEquippedItemAt(slotIndex);
            InventoryEquippedSlotButton slotButton = new()
            {
                CustomMinimumSize = new Vector2(0f, 68f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SlotIndex = slotIndex,
                ItemInstanceId = equipped?.InstanceId,
                DragLabel = equipped == null ? GetSlotShortLabel(slotIndex) : BuildPickerLabel(equipped, false),
                Text = BuildEquippedSlotText(slotIndex, equipped),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText = BuildEquippedSlotTooltip(slotIndex, equipped),
            };

            Color rarityColor = GetRarityColor(equipped);
            bool hasModifierVisual = ModifierVisualTheme.TryGet(equipped, out ModifierVisualStyle modifierVisual) && modifierVisual.HasSpecialVisuals;
            float modifierPulse = hasModifierVisual ? GetModifierVisualPulse(modifierVisual, equipped, _cardVisualTime) : 0f;
            Color modifierAccent = hasModifierVisual ? GetModifierAccent(modifierVisual, equipped, _cardVisualTime) : rarityColor;
            Color slotBackground = hasModifierVisual
                ? modifierVisual.UsesRadiantVisuals
                    ? new Color(0.095f + modifierVisual.BaseColor.R * 0.055f, 0.070f + modifierVisual.BaseColor.G * 0.045f, 0.040f + modifierVisual.BaseColor.B * 0.030f, 0.94f)
                    : modifierVisual.UsesElectricVisuals
                        ? new Color(0.035f + modifierVisual.BaseColor.R * 0.050f, 0.060f + modifierVisual.BaseColor.G * 0.050f, 0.125f + modifierVisual.BaseColor.B * 0.070f, 0.95f)
                    : new Color(0.065f + modifierVisual.BaseColor.R * 0.045f, 0.055f + modifierVisual.BaseColor.G * 0.035f, 0.115f + modifierVisual.BaseColor.B * 0.045f, 0.94f)
                : new Color(0.04f, 0.06f, 0.11f, 0.92f);
            Color slotBorder = hasModifierVisual
                ? modifierVisual.UsesRadiantVisuals
                    ? modifierVisual.BaseColor.Lerp(modifierAccent, 0.38f + 0.34f * modifierPulse).Lightened(0.08f)
                    : modifierVisual.UsesElectricVisuals
                        ? modifierVisual.BaseColor.Lerp(modifierAccent, 0.44f + 0.36f * modifierPulse).Lightened(0.10f)
                    : modifierVisual.BaseColor.Lerp(modifierAccent, 0.32f + 0.44f * modifierPulse)
                : rarityColor.Darkened(0.20f);
            int slotBorderWidth = hasModifierVisual ? 2 : equipped == null ? 1 : 2;
            StyleBoxFlat normalSlot = CreatePanelStyle(slotBackground, slotBorder, slotBorderWidth);
            StyleBoxFlat hoverSlot = CreatePanelStyle(slotBackground.Lightened(0.14f), hasModifierVisual ? slotBorder.Lightened(0.18f) : rarityColor, Mathf.Max(2, slotBorderWidth));
            if (hasModifierVisual)
            {
                if (modifierVisual.UsesRadiantVisuals)
                {
                    ApplyShinyCardEffects(normalSlot, modifierVisual, modifierPulse, equipped != null, slotBorderWidth);
                    ApplyShinyCardEffects(hoverSlot, modifierVisual, Mathf.Min(1f, modifierPulse + 0.18f), true, slotBorderWidth + 1);
                }
                else if (modifierVisual.UsesElectricVisuals)
                {
                    ApplyElectricCardEffects(normalSlot, modifierVisual, modifierPulse, equipped != null, slotBorderWidth);
                    ApplyElectricCardEffects(hoverSlot, modifierVisual, Mathf.Min(1f, modifierPulse + 0.18f), true, slotBorderWidth + 1);
                }
                else
                {
                    ApplyUnstableCardEffects(normalSlot, modifierVisual, modifierPulse, equipped != null, slotBorderWidth);
                    ApplyUnstableCardEffects(hoverSlot, modifierVisual, Mathf.Min(1f, modifierPulse + 0.18f), true, slotBorderWidth + 1);
                }
            }

            slotButton.AddThemeStyleboxOverride("normal", normalSlot);
            slotButton.AddThemeStyleboxOverride("hover", hoverSlot);
            slotButton.AddThemeColorOverride("font_color", equipped == null ? new Color(0.62f, 0.70f, 0.82f, 1f) : hasModifierVisual ? GetModifierFontColor(modifierVisual, modifierPulse) : rarityColor.Lightened(0.18f));

            slotButton.AddThemeFontSizeOverride("font_size", 12);

            int capturedSlotIndex = slotIndex;
            EquipmentItem? capturedItem = equipped;
            slotButton.Pressed += () => OnEquippedSlotPressed(capturedSlotIndex, capturedItem);
            slotButton.ItemDropped += OnItemDroppedOnSlot;
            slotButton.ContextRequested += OnEquippedSlotContextRequested;
            _equippedStrip.AddChild(slotButton);
        }
    }

    private void ClearItemCards()
    {
        _itemCardButtons.Clear();
        if (_itemGrid == null)
        {
            return;
        }

        foreach (Node child in _itemGrid.GetChildren())
        {
            _itemGrid.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void AddItemCard(int visibleIndex, EquipmentItem item)
    {
        if (_itemGrid == null)
        {
            return;
        }

        InventoryItemCardButton cardButton = new()
        {
            CustomMinimumSize = new Vector2(176f, 132f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ToggleMode = true,
            ItemInstanceId = item.InstanceId,
            DragLabel = BuildPickerLabel(item, false),
            Text = string.Empty,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = BuildItemCardTooltip(item),
        };

        BuildItemCardVisual(cardButton, item);

        int capturedIndex = visibleIndex;
        cardButton.Pressed += () => OnItemCardPressed(capturedIndex);
        cardButton.ContextRequested += OnItemCardContextRequested;
        _itemGrid.AddChild(cardButton);
        _itemCardButtons.Add(cardButton);
        ApplyItemCardStyle(cardButton, item, false);
    }

    private void OnItemCardPressed(int visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= _visibleItems.Count)
        {
            return;
        }

        EquipmentItem item = _visibleItems[visibleIndex];
        _selectedInstanceId = item.InstanceId;
        if (_bulkSelectMode)
        {
            ToggleBulkSelection(item.InstanceId);
        }

        ItemListView?.Select(visibleIndex);
        RefreshItemCardVisuals();
        UpdateSelectedItemDetails();
    }

    private void RefreshItemCardStyles()
    {
        for (int buttonIndex = 0; buttonIndex < _itemCardButtons.Count && buttonIndex < _visibleItems.Count; buttonIndex++)
        {
            EquipmentItem item = _visibleItems[buttonIndex];
            bool selected = string.Equals(item.InstanceId, _selectedInstanceId, StringComparison.Ordinal);
            ApplyItemCardStyle(_itemCardButtons[buttonIndex], item, selected);
        }
    }

    private void RefreshItemCardVisuals()
    {
        for (int buttonIndex = 0; buttonIndex < _itemCardButtons.Count && buttonIndex < _visibleItems.Count; buttonIndex++)
        {
            BuildItemCardVisual((InventoryItemCardButton)_itemCardButtons[buttonIndex], _visibleItems[buttonIndex]);
        }

        RefreshItemCardStyles();
        UpdateSelectionToolbar();
    }

    private void RefreshAnimatedModifierCardStyles()
    {
        for (int buttonIndex = 0; buttonIndex < _itemCardButtons.Count && buttonIndex < _visibleItems.Count; buttonIndex++)
        {
            EquipmentItem item = _visibleItems[buttonIndex];
            if (!ModifierVisualTheme.HasSpecialVisuals(item))
            {
                continue;
            }

            bool selected = string.Equals(item.InstanceId, _selectedInstanceId, StringComparison.Ordinal);
            ApplyItemCardStyle(_itemCardButtons[buttonIndex], item, selected);
        }
    }

    private void ApplyItemCardStyle(Button cardButton, EquipmentItem item, bool selected)
    {
        Color rarityColor = GetRarityColor(item);
        bool equipped = FindEquippedSlot(item) >= 0;
        bool selectedForDelete = _bulkSelectedInstanceIds.Contains(item.InstanceId);
        bool cardPressed = _bulkSelectMode ? selectedForDelete : selected;
        if (selectedForDelete)
        {
            StyleBoxFlat deleteNormal = CreatePanelStyle(new Color(0.18f, 0.045f, 0.060f, 0.95f), new Color(1f, 0.33f, 0.40f, 0.90f), 3);
            StyleBoxFlat deleteHover = CreatePanelStyle(new Color(0.26f, 0.065f, 0.080f, 0.98f), new Color(1f, 0.52f, 0.58f, 1f), 3);
            StyleBoxFlat deletePressed = CreatePanelStyle(new Color(0.32f, 0.080f, 0.100f, 0.98f), new Color(1f, 0.62f, 0.66f, 1f), 4);
            deleteNormal.ShadowColor = new Color(1f, 0.14f, 0.20f, 0.22f);
            deleteNormal.ShadowSize = 9;
            deleteHover.ShadowColor = new Color(1f, 0.18f, 0.24f, 0.34f);
            deleteHover.ShadowSize = 13;
            deletePressed.ShadowColor = new Color(1f, 0.20f, 0.26f, 0.42f);
            deletePressed.ShadowSize = 15;
            cardButton.AddThemeStyleboxOverride("normal", deleteNormal);
            cardButton.AddThemeStyleboxOverride("hover", deleteHover);
            cardButton.AddThemeStyleboxOverride("pressed", deletePressed);
            cardButton.AddThemeColorOverride("font_color", new Color(1f, 0.82f, 0.84f, 1f));
            cardButton.AddThemeFontSizeOverride("font_size", 12);
            cardButton.ButtonPressed = cardPressed;
            return;
        }

        bool hasModifierVisual = ModifierVisualTheme.TryGet(item, out ModifierVisualStyle modifierVisual) && modifierVisual.HasSpecialVisuals;
        float modifierPulse = hasModifierVisual ? GetModifierVisualPulse(modifierVisual, item, _cardVisualTime) : 0f;
        Color backgroundColor = selected
            ? new Color(rarityColor.R * 0.18f, rarityColor.G * 0.18f, rarityColor.B * 0.18f, 0.92f)
            : new Color(0.045f, 0.060f, 0.105f, 0.92f);
        Color borderColor = selected ? rarityColor.Lightened(0.18f) : equipped ? rarityColor : rarityColor.Darkened(0.35f);
        int borderWidth = selected ? 3 : equipped ? 2 : 1;

        if (hasModifierVisual)
        {
            Color accentColor = GetModifierAccent(modifierVisual, item, _cardVisualTime);
            float blendAmount = selected ? 0.20f + modifierPulse * 0.10f : 0.12f + modifierPulse * 0.08f;
            Color visualBackground = modifierVisual.UsesRadiantVisuals
                ? modifierVisual.BaseColor.Darkened(0.18f)
                : modifierVisual.UsesElectricVisuals
                    ? modifierVisual.BaseColor.Darkened(0.26f)
                    : modifierVisual.BaseColor.Darkened(0.32f);
            backgroundColor = backgroundColor.Lerp(visualBackground, blendAmount);
            borderColor = modifierVisual.UsesRadiantVisuals
                ? modifierVisual.BaseColor.Lerp(accentColor, 0.42f + modifierPulse * 0.34f).Lightened(0.06f)
                : modifierVisual.UsesElectricVisuals
                    ? modifierVisual.BaseColor.Lerp(accentColor, 0.46f + modifierPulse * 0.38f).Lightened(0.08f)
                : modifierVisual.BaseColor.Lerp(accentColor, 0.34f + modifierPulse * 0.46f);
            if (selected)
            {
                borderColor = accentColor.Lightened(0.10f);
            }
            else if (equipped)
            {
                borderColor = borderColor.Lightened(0.08f);
            }

            borderWidth = Mathf.Max(borderWidth, selected ? 4 : equipped ? 3 : 2);
        }

        StyleBoxFlat normal = CreatePanelStyle(backgroundColor, borderColor, borderWidth);
        StyleBoxFlat hover = CreatePanelStyle(backgroundColor.Lightened(0.12f), rarityColor.Lightened(0.12f), Mathf.Max(2, borderWidth));
        StyleBoxFlat pressed = CreatePanelStyle(backgroundColor.Lightened(0.18f), rarityColor.Lightened(0.22f), 3);
        if (hasModifierVisual)
        {
            Color accentColor = GetModifierAccent(modifierVisual, item, _cardVisualTime + 0.19f);
            hover.BorderColor = accentColor.Lightened(0.14f);
            pressed.BorderColor = modifierVisual.SecondaryAccentColor.Lightened(0.18f);
            if (modifierVisual.UsesRadiantVisuals)
            {
                ApplyShinyCardEffects(normal, modifierVisual, modifierPulse, selected, borderWidth);
                ApplyShinyCardEffects(hover, modifierVisual, Mathf.Min(1f, modifierPulse + 0.16f), true, borderWidth + 1);
                ApplyShinyCardEffects(pressed, modifierVisual, 1f, true, borderWidth + 1);
            }
            else if (modifierVisual.UsesElectricVisuals)
            {
                ApplyElectricCardEffects(normal, modifierVisual, modifierPulse, selected, borderWidth);
                ApplyElectricCardEffects(hover, modifierVisual, Mathf.Min(1f, modifierPulse + 0.16f), true, borderWidth + 1);
                ApplyElectricCardEffects(pressed, modifierVisual, 1f, true, borderWidth + 1);
            }
            else
            {
                ApplyUnstableCardEffects(normal, modifierVisual, modifierPulse, selected, borderWidth);
                ApplyUnstableCardEffects(hover, modifierVisual, Mathf.Min(1f, modifierPulse + 0.16f), true, borderWidth + 1);
                ApplyUnstableCardEffects(pressed, modifierVisual, 1f, true, borderWidth + 1);
            }
        }

        cardButton.AddThemeStyleboxOverride("normal", normal);
        cardButton.AddThemeStyleboxOverride("hover", hover);
        cardButton.AddThemeStyleboxOverride("pressed", pressed);
        cardButton.AddThemeColorOverride("font_color", hasModifierVisual ? GetModifierFontColor(modifierVisual, modifierPulse).Lightened(selected ? 0.14f : 0f) : selected ? new Color(0.98f, 1f, 1f, 1f) : rarityColor.Lightened(0.16f));
        cardButton.AddThemeFontSizeOverride("font_size", 12);
        cardButton.ButtonPressed = cardPressed;
    }

    private void ResolveServices()
    {
        if (!InventoryManagerPath.IsEmpty)
        {
            _inventoryManager = GetNodeOrNull(InventoryManagerPath) as InventoryManager;
        }

        _inventoryManager ??= GetNodeOrNull("/root/InventoryManager") as InventoryManager;
    }

    private void OnInventoryChanged()
    {
        Refresh();
    }

    private void OnItemSelected(long index)
    {
        int selectedIndex = (int)index;
        if (selectedIndex >= 0 && selectedIndex < _visibleItems.Count)
        {
            _selectedInstanceId = _visibleItems[selectedIndex].InstanceId;
        }

        RefreshItemCardStyles();
        UpdateSelectedItemDetails();
    }

    private void OnFilterOrSortChanged(long index)
    {
        _ = index;
        _selectedInstanceId = GetSelectedItem()?.InstanceId;
        Refresh();
    }

    private void OnSocketSelectionChanged(long index)
    {
        _ = index;
        RefreshSocketInventoryUi();
    }

    private void OnAdvancedInfoPressed()
    {
        _showAdvancedInfo = _advancedInfoButton?.ButtonPressed ?? !_showAdvancedInfo;
        UpdateAdvancedInfoButton();
        UpdateSelectedItemDetails();
    }

    private void OnSelectModePressed()
    {
        _bulkSelectMode = _selectModeButton?.ButtonPressed ?? !_bulkSelectMode;
        if (!_bulkSelectMode)
        {
            _bulkSelectedInstanceIds.Clear();
        }

        RefreshItemCardVisuals();
        UpdateSelectedItemDetails();
        SetFeedback(_bulkSelectMode ? "Select items to delete." : "Delete selection cleared.");
        UpdateSelectionToolbar();
    }

    private void OnSelectAllVisiblePressed()
    {
        if (_visibleItems.Count == 0)
        {
            SetFeedback("No visible items to select.");
            return;
        }

        _bulkSelectMode = true;
        for (int itemIndex = 0; itemIndex < _visibleItems.Count; itemIndex++)
        {
            _bulkSelectedInstanceIds.Add(_visibleItems[itemIndex].InstanceId);
        }

        RefreshItemCardVisuals();
        SetFeedback($"Selected {_visibleItems.Count} visible item{(_visibleItems.Count == 1 ? string.Empty : "s")}.");
        UpdateSelectionToolbar();
    }

    private void OnClearSelectionPressed()
    {
        if (_bulkSelectedInstanceIds.Count == 0)
        {
            SetFeedback("No items are selected for deletion.");
            return;
        }

        _bulkSelectedInstanceIds.Clear();
        RefreshItemCardVisuals();
        SetFeedback("Delete selection cleared.");
        UpdateSelectionToolbar();
    }

    private void OnDeleteSelectedPressed()
    {
        BeginDeleteItems(_bulkSelectedInstanceIds);
    }

    private void ToggleBulkSelection(string instanceId)
    {
        if (_bulkSelectedInstanceIds.Contains(instanceId))
        {
            _bulkSelectedInstanceIds.Remove(instanceId);
        }
        else
        {
            _bulkSelectedInstanceIds.Add(instanceId);
        }
    }

    private void BeginDeleteItems(IEnumerable<string?> instanceIds)
    {
        _pendingDeleteInstanceIds.Clear();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string? instanceId in instanceIds)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || seen.Contains(instanceId))
            {
                continue;
            }

            if (FindItemByInstanceId(instanceId) == null)
            {
                continue;
            }

            seen.Add(instanceId);
            _pendingDeleteInstanceIds.Add(instanceId);
        }

        if (_pendingDeleteInstanceIds.Count == 0)
        {
            SetFeedback("Select at least one item to delete.");
            UpdateSelectionToolbar();
            return;
        }

        EnsureDeleteConfirmDialog();
        if (_deleteConfirmDialog == null)
        {
            return;
        }

        int equippedCount = CountPendingDeleteEquippedItems();
        string itemText = _pendingDeleteInstanceIds.Count == 1
            ? BuildPickerLabel(FindItemByInstanceId(_pendingDeleteInstanceIds[0])!, false)
            : $"{_pendingDeleteInstanceIds.Count} items";
        string equippedText = equippedCount > 0
            ? $"\n\n{equippedCount} equipped item{(equippedCount == 1 ? " is" : "s are")} included and will be unequipped."
            : string.Empty;

        _deleteConfirmDialog.DialogText = $"Delete {itemText}?\n\nThis cannot be undone.{equippedText}";
        _deleteConfirmDialog.GetOkButton().Text = "Delete";
        _deleteConfirmDialog.GetCancelButton().Text = "Cancel";
        _deleteConfirmDialog.PopupCentered(new Vector2I(460, 220));
    }

    private void OnDeleteConfirmed()
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return;
        }

        bool selectedDeleted = _selectedInstanceId != null && _pendingDeleteInstanceIds.Contains(_selectedInstanceId);
        int removed = _inventoryManager.DeleteItems(_pendingDeleteInstanceIds);
        for (int idIndex = 0; idIndex < _pendingDeleteInstanceIds.Count; idIndex++)
        {
            _bulkSelectedInstanceIds.Remove(_pendingDeleteInstanceIds[idIndex]);
        }

        _pendingDeleteInstanceIds.Clear();
        if (selectedDeleted)
        {
            _selectedInstanceId = null;
        }

        if (_bulkSelectedInstanceIds.Count == 0)
        {
            _bulkSelectMode = false;
        }

        SetFeedback(removed == 1 ? "Deleted 1 item." : $"Deleted {removed} items.");
        Refresh();
    }

    private int CountPendingDeleteEquippedItems()
    {
        int count = 0;
        for (int pendingIndex = 0; pendingIndex < _pendingDeleteInstanceIds.Count; pendingIndex++)
        {
            EquipmentItem? item = FindItemByInstanceId(_pendingDeleteInstanceIds[pendingIndex]);
            if (item != null && FindEquippedSlot(item) >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private void PruneBulkSelection()
    {
        if (_inventoryManager == null || _bulkSelectedInstanceIds.Count == 0)
        {
            return;
        }

        List<string> staleIds = new();
        foreach (string instanceId in _bulkSelectedInstanceIds)
        {
            if (FindItemByInstanceId(instanceId) == null)
            {
                staleIds.Add(instanceId);
            }
        }

        for (int staleIndex = 0; staleIndex < staleIds.Count; staleIndex++)
        {
            _bulkSelectedInstanceIds.Remove(staleIds[staleIndex]);
        }
    }

    private void UpdateSelectionToolbar()
    {
        if (_selectModeButton != null)
        {
            _selectModeButton.ButtonPressed = _bulkSelectMode;
            _selectModeButton.Text = _bulkSelectMode ? "Selecting" : "Select";
            _selectModeButton.TooltipText = "Toggle multi-select for deleting inventory items.";
        }

        if (_selectAllVisibleButton != null)
        {
            _selectAllVisibleButton.Text = "All Visible";
            _selectAllVisibleButton.Disabled = _visibleItems.Count == 0;
            _selectAllVisibleButton.TooltipText = "Select every item currently shown by the filter.";
        }

        if (_clearSelectionButton != null)
        {
            _clearSelectionButton.Text = "Clear";
            _clearSelectionButton.Disabled = _bulkSelectedInstanceIds.Count == 0;
            _clearSelectionButton.TooltipText = "Clear the current delete selection.";
        }

        if (_deleteSelectedButton != null)
        {
            int selectedCount = _bulkSelectedInstanceIds.Count;
            _deleteSelectedButton.Text = selectedCount > 0 ? $"Delete ({selectedCount})" : "Delete";
            _deleteSelectedButton.Disabled = selectedCount == 0;
            _deleteSelectedButton.TooltipText = "Delete selected inventory items after confirmation.";
        }

        UpdateInventorySummary();
    }

    private void UpdateInventorySummary()
    {
        if (_inventoryManager == null)
        {
            return;
        }

        int equippedCount = CountEquippedItems();
        string selectedSummary = _bulkSelectMode || _bulkSelectedInstanceIds.Count > 0 ? $"    Selected: {_bulkSelectedInstanceIds.Count}" : string.Empty;
        SetSummary($"Weapons: {_inventoryManager.Items.Count}/{_inventoryManager.MaxInventorySize}    Equipped: {equippedCount}/{EquippedSlotCount}    Essences: {_inventoryManager.StoredEssences.Count}    Modulators: {_inventoryManager.StoredModulators.Count}    Showing: {_visibleItems.Count}{selectedSummary}");
    }

    private void UpdateAdvancedInfoButton()
    {
        if (_advancedInfoButton == null)
        {
            return;
        }

        _advancedInfoButton.ButtonPressed = _showAdvancedInfo;
        _advancedInfoButton.Text = _showAdvancedInfo ? "Advanced Info: On" : "Advanced Info";
    }

    private void OnEquippedSlotPressed(int slotIndex, EquipmentItem? item)
    {
        _focusedSlotIndex = Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1);
        EquipSlotDropdown?.Select(_focusedSlotIndex);

        if (_mode == InventoryModalMode.WeaponSelection)
        {
            ApplyModePresentation();
            SetFeedback(item == null
                ? $"Choose a weapon for {GetSlotShortLabel(_focusedSlotIndex)}."
                : $"Choose a replacement weapon for {GetSlotShortLabel(_focusedSlotIndex)}.");
            return;
        }

        if (_mode == InventoryModalMode.SocketManagement)
        {
            if (item == null)
            {
                SetFeedback($"{GetSlotShortLabel(_focusedSlotIndex)} is empty.");
                RefreshSocketInventoryUi();
                return;
            }

            _selectedInstanceId = item.InstanceId;
            Refresh();
            return;
        }

        if (item == null)
        {
            SetFeedback($"{GetSlotShortLabel(slotIndex)} is empty.");
            UpdateSelectedItemDetails();
            return;
        }

        _selectedInstanceId = item.InstanceId;
        int visibleIndex = FindVisibleIndexByInstanceId(item.InstanceId);
        if (visibleIndex >= 0)
        {
            ItemListView?.Select(visibleIndex);
            RefreshItemCardStyles();
            UpdateSelectedItemDetails();
        }
        else
        {
            _filterDropdown?.Select(0);
            Refresh();
        }
    }

    private void OnItemDroppedOnSlot(int slotIndex, string instanceId)
    {
        TryEquipItemToSlot(instanceId, slotIndex);
    }

    private void OnItemCardContextRequested(string instanceId, Vector2 globalPosition)
    {
        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null)
        {
            SetFeedback("That inventory item could not be found.");
            return;
        }

        SelectItemByInstanceId(item.InstanceId, true);
        ShowItemContextMenu(item, globalPosition);
    }

    private void OnEquippedSlotContextRequested(int slotIndex, string? instanceId, Vector2 globalPosition)
    {
        EquipSlotDropdown?.Select(Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1));
        EquipmentItem? slotItem = string.IsNullOrWhiteSpace(instanceId) ? null : FindItemByInstanceId(instanceId);
        ShowSlotContextMenu(slotIndex, slotItem, globalPosition);
    }

    private void ShowItemContextMenu(EquipmentItem item, Vector2 globalPosition)
    {
        EnsureContextMenu();
        if (_contextMenu == null)
        {
            return;
        }

        _contextItemInstanceId = item.InstanceId;
        _contextSelectedItemInstanceId = item.InstanceId;
        _contextSlotIndex = FindEquippedSlot(item);
        _contextMenu.Clear();
        _contextMenu.AddItem("Select / inspect", ContextActionSelect);
        if (_contextSlotIndex >= 0)
        {
            _contextMenu.AddItem($"Unequip from {GetSlotShortLabel(_contextSlotIndex)}", ContextActionUnequip);
        }

        _contextMenu.AddSeparator();
        for (int slotIndex = 0; slotIndex < EquippedSlotCount; slotIndex++)
        {
            EquipmentItem? slotItem = _inventoryManager?.GetEquippedItemAt(slotIndex);
            string slotLabel = GetSlotShortLabel(slotIndex);
            string actionText = _contextSlotIndex == slotIndex
                ? $"Equipped in {slotLabel}"
                : slotItem == null
                    ? $"Equip to {slotLabel}"
                    : $"Replace {slotLabel}: {BuildPickerLabel(slotItem, false)}";
            _contextMenu.AddItem(actionText, ContextActionEquipSlotBase + slotIndex);
        }

        _contextMenu.AddSeparator();
        _contextMenu.AddItem("Delete item...", ContextActionDelete);

        PopupContextMenu(globalPosition);
    }

    private void ShowSlotContextMenu(int slotIndex, EquipmentItem? slotItem, Vector2 globalPosition)
    {
        EnsureContextMenu();
        if (_contextMenu == null)
        {
            return;
        }

        _contextItemInstanceId = slotItem?.InstanceId;
        _contextSelectedItemInstanceId = GetSelectedItem()?.InstanceId;
        _contextSlotIndex = Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1);
        _contextMenu.Clear();

        string slotLabel = GetSlotShortLabel(_contextSlotIndex);
        if (slotItem != null)
        {
            _contextMenu.AddItem($"Select {BuildPickerLabel(slotItem, false)}", ContextActionSelect);
            _contextMenu.AddItem($"Unequip {slotLabel}", ContextActionUnequip);
        }
        else
        {
            _contextMenu.AddItem($"{slotLabel} is empty", ContextActionNoop);
        }

        EquipmentItem? selected = GetSelectedItem();
        if (selected != null && !ReferenceEquals(selected, slotItem))
        {
            _contextMenu.AddSeparator();
            string selectedText = BuildPickerLabel(selected, false);
            string actionText = slotItem == null
                ? $"Equip selected here: {selectedText}"
                : $"Replace with selected: {selectedText}";
            _contextMenu.AddItem(actionText, ContextActionEquipSelectedHere);
        }

        PopupContextMenu(globalPosition);
    }

    private void PopupContextMenu(Vector2 globalPosition)
    {
        if (_contextMenu == null)
        {
            return;
        }

        _contextMenu.Position = new Vector2I(Mathf.RoundToInt(globalPosition.X), Mathf.RoundToInt(globalPosition.Y));
        _contextMenu.Popup();
    }

    private void OnContextMenuIdPressed(long id)
    {
        int action = (int)id;
        if (action == ContextActionNoop)
        {
            return;
        }

        if (action >= ContextActionEquipSlotBase && action < ContextActionEquipSlotBase + EquippedSlotCount)
        {
            TryEquipItemToSlot(_contextItemInstanceId, action - ContextActionEquipSlotBase);
            return;
        }

        switch (action)
        {
            case ContextActionSelect:
                if (!string.IsNullOrWhiteSpace(_contextItemInstanceId))
                {
                    SelectItemByInstanceId(_contextItemInstanceId, true);
                }
                break;
            case ContextActionUnequip:
                UnequipSlot(_contextSlotIndex);
                break;
            case ContextActionEquipSelectedHere:
                TryEquipItemToSlot(_contextSelectedItemInstanceId, _contextSlotIndex);
                break;
            case ContextActionDelete:
                BeginDeleteItems(_contextItemInstanceId == null ? Array.Empty<string>() : new[] { _contextItemInstanceId });
                break;
        }
    }

    private bool ShouldShowItem(EquipmentItem item)
    {
        int selectedFilter = _filterDropdown?.Selected ?? 0;
        return selectedFilter switch
        {
            1 => FindEquippedSlot(item) >= 0,
            2 => FindEquippedSlot(item) < 0,
            3 => item.Shape?.ShapeGroup == ShapeGroup.TwoD,
            4 => item.Shape?.ShapeGroup == ShapeGroup.ThreeD,
            5 => item.Shape?.ShapeGroup == ShapeGroup.Ultra,
            6 => item.Shape?.ShapeGroup == ShapeGroup.Mythic,
            7 => item.Shape?.AttackType is WeaponAttackType.TemporalEcho or WeaponAttackType.WrathAura,
            8 => HasAnySubstat(item, SubstatType.DamagePercent, SubstatType.AttackSpeed, SubstatType.CritChance, SubstatType.CritDamage),
            9 => HasAnySubstat(item, SubstatType.HealthPercent, SubstatType.DodgeChance, SubstatType.LifeStealPercent, SubstatType.HealthRegen),
            10 => HasAnySubstat(item, SubstatType.Size, SubstatType.Range, SubstatType.Ricochet, SubstatType.BounceTimes, SubstatType.ProjectileCount),
            _ => true,
        };
    }

    private int CompareVisibleItems(EquipmentItem left, EquipmentItem right)
    {
        int selectedSort = _sortDropdown?.Selected ?? 0;
        int comparison = selectedSort switch
        {
            1 => right.Level.CompareTo(left.Level),
            2 => right.Damage.CompareTo(left.Damage),
            3 => right.Health.CompareTo(left.Health),
            4 => string.Compare(left.Shape?.DisplayName, right.Shape?.DisplayName, StringComparison.OrdinalIgnoreCase),
            5 => string.Compare(left.Modifier?.DisplayName, right.Modifier?.DisplayName, StringComparison.OrdinalIgnoreCase),
            6 => EquippedSortValue(right).CompareTo(EquippedSortValue(left)),
            _ => GetRaritySortValue(right).CompareTo(GetRaritySortValue(left)),
        };

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.Level.CompareTo(left.Level);
        if (comparison != 0)
        {
            return comparison;
        }

        return string.Compare(left.Shape?.DisplayName, right.Shape?.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyListItemColor(int listIndex, EquipmentItem item)
    {
        if (ItemListView == null)
        {
            return;
        }

        Color rarityColor = GetRarityColor(item);
        bool equipped = FindEquippedSlot(item) >= 0;
        ItemListView.SetItemCustomFgColor(listIndex, equipped ? rarityColor.Lightened(0.24f) : rarityColor.Lightened(0.08f));
        ItemListView.SetItemCustomBgColor(listIndex, equipped ? new Color(rarityColor.R * 0.10f, rarityColor.G * 0.10f, rarityColor.B * 0.10f, 0.35f) : new Color(0f, 0f, 0f, 0f));
    }

    private void OnEquipPressed()
    {
        EquipmentItem? selected = GetSelectedItem();
        if (selected == null)
        {
            SetFeedback("Select an item first.");
            return;
        }

        int slotIndex = Mathf.Clamp(EquipSlotDropdown?.Selected ?? 0, 0, EquippedSlotCount - 1);
        if (TryEquipItemToSlot(selected.InstanceId, slotIndex) && _mode == InventoryModalMode.WeaponSelection)
        {
            Close();
        }
    }

    private void OnSocketEssencePressed()
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return;
        }

        EquipmentItem? targetItem = GetSocketTargetItem();
        if (targetItem == null)
        {
            SetFeedback("Select a weapon first.");
            return;
        }

        int storedIndex = GetFirstSelectedIndex(_storedEssenceList);
        if (storedIndex < 0 || storedIndex >= _inventoryManager.StoredEssences.Count)
        {
            SetFeedback("Select a stored essence first.");
            return;
        }

        string essenceName = _inventoryManager.StoredEssences[storedIndex].DisplayName;
        if (!_inventoryManager.TryInstallStoredEssence(targetItem.InstanceId, storedIndex, replaceExisting: true))
        {
            SetFeedback(targetItem.HasEssenceSlotUnlocked
                ? "Failed to equip that essence."
                : "This weapon's essence slot is still locked.");
            return;
        }

        _selectedInstanceId = targetItem.InstanceId;
        SetFeedback($"Socketed {essenceName} on {BuildPickerLabel(targetItem, false)}.");
        Refresh();
    }

    private void OnReturnEssencePressed()
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return;
        }

        EquipmentItem? targetItem = GetSocketTargetItem();
        if (targetItem == null || !targetItem.HasEquippedEssence)
        {
            SetFeedback("That weapon has no socketed essence to return.");
            return;
        }

        string essenceName = targetItem.Essence!.DisplayName;
        if (!_inventoryManager.TryReturnEssenceToStorage(targetItem.InstanceId))
        {
            SetFeedback("Failed to return that essence to inventory.");
            return;
        }

        _selectedInstanceId = targetItem.InstanceId;
        SetFeedback($"Returned {essenceName} to essence inventory.");
        Refresh();
    }

    private void OnInstallStoredModulatorPressed()
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return;
        }

        EquipmentItem? targetItem = GetSocketTargetItem();
        if (targetItem == null)
        {
            SetFeedback("Select a weapon first.");
            return;
        }

        int storedIndex = GetFirstSelectedIndex(_storedModulatorList);
        if (storedIndex < 0 || storedIndex >= _inventoryManager.StoredModulators.Count)
        {
            SetFeedback("Select a stored modulator first.");
            return;
        }

        string modulatorName = _inventoryManager.StoredModulators[storedIndex].DisplayName;
        string? modulatorBlockReason = GetModulatorInstallBlockReason(targetItem, _inventoryManager.StoredModulators[storedIndex]);
        if (modulatorBlockReason != null)
        {
            SetFeedback(modulatorBlockReason);
            return;
        }

        if (!_inventoryManager.TryInstallStoredModulator(targetItem.InstanceId, storedIndex))
        {
            SetFeedback(targetItem.AvailableModulatorSlots > 0
                ? "Failed to install that modulator."
                : "This weapon has no free modulator slots.");
            return;
        }

        _selectedInstanceId = targetItem.InstanceId;
        SetFeedback($"Installed {modulatorName} on {BuildPickerLabel(targetItem, false)}.");
        Refresh();
    }

    private string? GetModulatorInstallBlockReason(EquipmentItem? targetItem, WeaponModulatorData? modulator)
    {
        if (targetItem == null || modulator == null || !modulator.IsWingModulator)
        {
            return null;
        }

        int equippedSlot = FindEquippedSlot(targetItem);
        if (equippedSlot <= 0)
        {
            return "Wing modulators can only be installed on equipped support weapons.";
        }

        if (HasWingModulator(targetItem))
        {
            return "This weapon already has a Wing modulator.";
        }

        if (HasConflictingEquippedWingModulator(targetItem, modulator))
        {
            return $"{modulator.DisplayName} is already installed on another equipped support weapon.";
        }

        return null;
    }

    private bool HasConflictingEquippedWingModulator(EquipmentItem targetItem, WeaponModulatorData modulator)
    {
        if (_inventoryManager == null)
        {
            return false;
        }

        string wingKey = WeaponModulatorData.NormalizeKey(modulator.Key);
        if (string.IsNullOrWhiteSpace(wingKey))
        {
            return false;
        }

        for (int slotIndex = 1; slotIndex < EquippedSlotCount; slotIndex++)
        {
            EquipmentItem? equippedItem = _inventoryManager.GetEquippedItemAt(slotIndex);
            if (equippedItem == null || ReferenceEquals(equippedItem, targetItem))
            {
                continue;
            }

            for (int modulatorIndex = 0; modulatorIndex < equippedItem.Modulators.Count; modulatorIndex++)
            {
                WeaponModulatorData equippedModulator = equippedItem.Modulators[modulatorIndex];
                if (equippedModulator.IsWingModulator && equippedModulator.NormalizedKey == wingKey)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasWingModulator(EquipmentItem item)
    {
        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            if (item.Modulators[modulatorIndex].IsWingModulator)
            {
                return true;
            }
        }

        return false;
    }

    private void OnReturnInstalledModulatorPressed()
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return;
        }

        EquipmentItem? targetItem = GetSocketTargetItem();
        if (targetItem == null)
        {
            SetFeedback("Select a weapon first.");
            return;
        }

        int installedIndex = GetFirstSelectedIndex(_installedModulatorList);
        if (installedIndex < 0 || installedIndex >= targetItem.Modulators.Count)
        {
            SetFeedback("Select an installed modulator to return.");
            return;
        }

        string modulatorName = targetItem.Modulators[installedIndex].DisplayName;
        if (!_inventoryManager.TryReturnModulatorToStorage(targetItem.InstanceId, installedIndex))
        {
            SetFeedback("Failed to return that modulator to inventory.");
            return;
        }

        _selectedInstanceId = targetItem.InstanceId;
        SetFeedback($"Returned {modulatorName} to modulator inventory.");
        Refresh();
    }

    private void OnUnequipPressed()
    {
        int slotIndex = Mathf.Clamp(EquipSlotDropdown?.Selected ?? 0, 0, EquippedSlotCount - 1);
        UnequipSlot(slotIndex);
    }

    private bool TryEquipItemToSlot(string? instanceId, int slotIndex)
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            SetFeedback("Select an item first.");
            return false;
        }

        EquipmentItem? item = FindItemByInstanceId(instanceId);
        if (item == null)
        {
            SetFeedback("That inventory item could not be found.");
            return false;
        }

        int targetSlot = Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1);
        int previousSlot = FindEquippedSlot(item);
        if (previousSlot == targetSlot)
        {
            SelectItemByInstanceId(item.InstanceId, true);
            EquipSlotDropdown?.Select(targetSlot);
            SetFeedback($"{BuildPickerLabel(item, false)} is already in {GetSlotShortLabel(targetSlot)}.");
            return true;
        }

        EquipmentItem? replacedItem = _inventoryManager.GetEquippedItemAt(targetSlot);
        if (!_inventoryManager.TryEquip(item, targetSlot))
        {
            SetFeedback("Failed to equip item.");
            return false;
        }

        _selectedInstanceId = item.InstanceId;
        EquipSlotDropdown?.Select(targetSlot);
        string itemName = BuildPickerLabel(item, false);
        string targetLabel = GetSlotShortLabel(targetSlot);
        if (previousSlot >= 0)
        {
            SetFeedback($"Moved {itemName} from {GetSlotShortLabel(previousSlot)} to {targetLabel}.");
        }
        else if (replacedItem != null)
        {
            SetFeedback($"Equipped {itemName} to {targetLabel}, replacing {BuildPickerLabel(replacedItem, false)}.");
        }
        else
        {
            SetFeedback($"Equipped {itemName} to {targetLabel}.");
        }

        Refresh();
        return true;
    }

    private void UnequipSlot(int slotIndex)
    {
        if (_inventoryManager == null)
        {
            SetFeedback("Inventory service not connected.");
            return;
        }

        int targetSlot = Mathf.Clamp(slotIndex, 0, EquippedSlotCount - 1);
        EquipmentItem? item = _inventoryManager.GetEquippedItemAt(targetSlot);
        _inventoryManager.Unequip(targetSlot);
        if (item != null)
        {
            _selectedInstanceId = item.InstanceId;
            SetFeedback($"Unequipped {BuildPickerLabel(item, false)} from {GetSlotShortLabel(targetSlot)}.");
        }
        else
        {
            SetFeedback($"{GetSlotShortLabel(targetSlot)} is already empty.");
        }

        EquipSlotDropdown?.Select(targetSlot);
        Refresh();
    }

    private void SelectItemByInstanceId(string? instanceId, bool resetFilterIfHidden)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        _selectedInstanceId = instanceId;
        int visibleIndex = FindVisibleIndexByInstanceId(instanceId);
        if (visibleIndex >= 0)
        {
            ItemListView?.Select(visibleIndex);
            RefreshItemCardStyles();
            UpdateSelectedItemDetails();
            return;
        }

        if (resetFilterIfHidden && _filterDropdown != null)
        {
            _filterDropdown.Select(0);
            Refresh();
            return;
        }

        UpdateSelectedItemDetails();
    }

    private EquipmentItem? GetSelectedItem()
    {
        int selectedById = FindVisibleIndexByInstanceId(_selectedInstanceId);
        if (selectedById >= 0)
        {
            return _visibleItems[selectedById];
        }

        int selected = GetSelectedListIndex();
        if (selected < 0 || selected >= _visibleItems.Count)
        {
            return null;
        }

        return _visibleItems[selected];
    }

    private int GetSelectedListIndex()
    {
        if (ItemListView == null)
        {
            return -1;
        }

        int[] selected = ItemListView.GetSelectedItems();
        if (selected.Length == 0)
        {
            return -1;
        }

        return selected[0];
    }

    private int FindVisibleIndexByInstanceId(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return -1;
        }

        for (int itemIndex = 0; itemIndex < _visibleItems.Count; itemIndex++)
        {
            if (string.Equals(_visibleItems[itemIndex].InstanceId, instanceId, StringComparison.Ordinal))
            {
                return itemIndex;
            }
        }

        return -1;
    }

    private EquipmentItem? FindItemByInstanceId(string? instanceId)
    {
        if (_inventoryManager == null || string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        for (int itemIndex = 0; itemIndex < _inventoryManager.Items.Count; itemIndex++)
        {
            EquipmentItem item = _inventoryManager.Items[itemIndex];
            if (string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private int FindEquippedSlot(EquipmentItem item)
    {
        if (_inventoryManager == null)
        {
            return -1;
        }

        for (int slotIndex = 0; slotIndex < EquippedSlotCount; slotIndex++)
        {
            if (ReferenceEquals(_inventoryManager.GetEquippedItemAt(slotIndex), item))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private string BuildListEntry(int index, EquipmentItem item)
    {
        _ = index;
        return BuildPickerLabel(item, false);
    }

    private void BuildItemCardVisual(InventoryItemCardButton cardButton, EquipmentItem item)
    {
        foreach (Node child in cardButton.GetChildren())
        {
            cardButton.RemoveChild(child);
            child.QueueFree();
        }

        Color rarityColor = GetRarityColor(item);
        Color modifierColor = GetCardModifierColor(item);
        int equippedSlot = FindEquippedSlot(item);

        MarginContainer margin = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        SetControlFullRect(margin);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);

        VBoxContainer stack = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        stack.AddThemeConstantOverride("separation", 4);

        HBoxContainer header = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        header.AddThemeConstantOverride("separation", 5);

        Label modifierBadge = CreateCardLabel(BuildModifierChipText(item), 10, modifierColor.Lightened(0.16f), true);
        modifierBadge.CustomMinimumSize = new Vector2(52f, 0f);
        header.AddChild(modifierBadge);

        Label rarityBadge = CreateCardLabel((item.Rarity?.DisplayName ?? "Unknown").ToUpperInvariant(), 10, rarityColor.Lightened(0.20f), true);
        rarityBadge.HorizontalAlignment = HorizontalAlignment.Right;
        rarityBadge.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(rarityBadge);
        stack.AddChild(header);

        Label shapeName = CreateCardLabel(item.Shape?.DisplayName ?? "Unknown Shape", 15, new Color(0.96f, 0.985f, 1f, 1f), true);
        shapeName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        stack.AddChild(shapeName);

        Label levelLine = CreateCardLabel(BuildCardLevelLine(item), 10, new Color(0.70f, 0.80f, 0.94f, 1f), false);
        stack.AddChild(levelLine);

        HBoxContainer pillRow = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        pillRow.AddThemeConstantOverride("separation", 4);
        AddCardStatPills(pillRow, item, rarityColor, modifierColor);
        stack.AddChild(pillRow);

        bool selectedForDelete = _bulkSelectedInstanceIds.Contains(item.InstanceId);
        string statusText = _bulkSelectMode
            ? selectedForDelete ? "✓ Selected" : "Click to select"
            : equippedSlot >= 0 ? $"● {GetSlotShortLabel(equippedSlot)}" : "Drag to equip • Right-click";
        Color statusColor = selectedForDelete
            ? new Color(1f, 0.48f, 0.54f, 1f)
            : equippedSlot >= 0 ? rarityColor.Lightened(0.24f) : new Color(0.56f, 0.66f, 0.82f, 1f);
        Label statusLine = CreateCardLabel(statusText, 10, statusColor, selectedForDelete);
        stack.AddChild(statusLine);

        margin.AddChild(stack);
        cardButton.AddChild(margin);
    }

    private void AddCardStatPills(HBoxContainer pillRow, EquipmentItem item, Color rarityColor, Color modifierColor)
    {
        pillRow.AddChild(CreateCardStatPill($"HP {StatGenerator.GetEffectiveHealth(item)}", new Color(0.45f, 0.95f, 0.70f, 1f), new Color(0.045f, 0.115f, 0.085f, 0.78f)));
        pillRow.AddChild(CreateCardStatPill($"SHOT {StatGenerator.GetEffectiveDamage(item)}", new Color(1f, 0.74f, 0.52f, 1f), new Color(0.140f, 0.075f, 0.050f, 0.78f)));

        WeaponStatPreview stats = BuildWeaponStatPreview(item);
        if (stats.HasDamage)
        {
            pillRow.AddChild(CreateCardStatPill($"DPS {FormatStatNumber(stats.Dps)}", modifierColor.Lightened(0.16f), new Color(modifierColor.R * 0.16f, modifierColor.G * 0.16f, modifierColor.B * 0.20f, 0.80f)));
            return;
        }

        SubstatRoll? standoutRoll = FindCardStandoutSubstat(item);
        if (standoutRoll != null)
        {
            pillRow.AddChild(CreateCardStatPill(BuildSubstatPillText(standoutRoll), rarityColor.Lightened(0.18f), new Color(rarityColor.R * 0.12f, rarityColor.G * 0.12f, rarityColor.B * 0.16f, 0.78f)));
            return;
        }

        pillRow.AddChild(CreateCardStatPill(item.Shape == null ? "No Shape" : "Utility", new Color(0.74f, 0.82f, 0.96f, 1f), new Color(0.065f, 0.085f, 0.135f, 0.78f)));
    }

    private static PanelContainer CreateCardStatPill(string text, Color textColor, Color backgroundColor)
    {
        PanelContainer pill = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        StyleBoxFlat style = CreatePanelStyle(backgroundColor, textColor.Darkened(0.24f), 1);
        style.CornerRadiusBottomLeft = 6;
        style.CornerRadiusBottomRight = 6;
        style.CornerRadiusTopLeft = 6;
        style.CornerRadiusTopRight = 6;
        pill.AddThemeStyleboxOverride("panel", style);

        MarginContainer margin = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_top", 2);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.AddThemeConstantOverride("margin_bottom", 2);

        Label label = CreateCardLabel(text, 9, textColor, true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        margin.AddChild(label);
        pill.AddChild(margin);
        return pill;
    }

    private static Label CreateCardLabel(string text, int fontSize, Color color, bool prominent)
    {
        Label label = new()
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, prominent ? 0.70f : 0.45f));
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", prominent ? 2 : 1);
        return label;
    }

    private static void SetControlFullRect(Control control)
    {
        control.AnchorLeft = 0f;
        control.AnchorTop = 0f;
        control.AnchorRight = 1f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = 0f;
        control.OffsetTop = 0f;
        control.OffsetRight = 0f;
        control.OffsetBottom = 0f;
    }

    private static string BuildModifierChipText(EquipmentItem item)
    {
        if (ModifierVisualTheme.TryGet(item, out ModifierVisualStyle style) && style.HasSpecialVisuals)
        {
            return style.ShortTag;
        }

        if (item.Modulators.Count > 0 && !ShouldShowModifierInPicker(item.Modifier))
        {
            return "MODS";
        }

        if (!ShouldShowModifierInPicker(item.Modifier))
        {
            return item.HasEssenceSlotUnlocked ? "EMPTY" : "LOCK";
        }

        string displayName = item.Modifier?.DisplayName ?? "MOD";
        return displayName.Length <= 8 ? displayName.ToUpperInvariant() : displayName[..8].ToUpperInvariant();
    }

    private static Color GetCardModifierColor(EquipmentItem item)
    {
        if (ModifierVisualTheme.TryGet(item, out ModifierVisualStyle style))
        {
            return style.HasSpecialVisuals ? style.AccentColor : style.BaseColor;
        }

        return GetModifierColor(item);
    }

    private static string BuildCardLevelLine(EquipmentItem item)
    {
        string essence = ShouldShowModifierInPicker(item.Modifier)
            ? item.Modifier!.DisplayName
            : item.HasEssenceSlotUnlocked ? "Empty Essence" : "Locked Essence";
        return $"Lv {item.Level}/5  Mod {item.Modulators.Count}/{item.UnlockedModulatorSlots}  {essence}";
    }

    private static SubstatRoll? FindCardStandoutSubstat(EquipmentItem item)
    {
        return FindSubstat(item, SubstatType.DamagePercent)
            ?? FindSubstat(item, SubstatType.AttackSpeed)
            ?? FindSubstat(item, SubstatType.ProjectileCount)
            ?? FindSubstat(item, SubstatType.CritChance)
            ?? FindSubstat(item, SubstatType.CritDamage);
    }

    private static string BuildSubstatPillText(SubstatRoll roll)
    {
        string label = roll.Type switch
        {
            SubstatType.DamagePercent => "DMG%",
            SubstatType.AttackSpeed => "ATK%",
            SubstatType.ProjectileCount => "PROJ",
            SubstatType.CritChance => "CRIT",
            SubstatType.CritDamage => "CDMG",
            _ => roll.Type.ToString(),
        };
        string suffix = roll.Type == SubstatType.ProjectileCount ? string.Empty : "%";
        return $"{label} +{roll.Value:0.#}{suffix}";
    }

    private string BuildEquippedSlotText(int slotIndex, EquipmentItem? item)
    {
        string label = GetSlotShortLabel(slotIndex);
        if (item == null)
        {
            return $"{label}\nEmpty";
        }

        return $"{label}\n{BuildPickerLabel(item, true)}";
    }

    private SynergySnapshot BuildSynergySnapshot(EquipmentItem? projectedItem, int projectedSlotIndex)
    {
        List<EquipmentSlot> supportSlots = new();
        for (int slotIndex = 1; slotIndex < EquippedSlotCount; slotIndex++)
        {
            EquipmentSlot supportSlot = new(slotIndex, EquipmentSlotType.Support);
            EquipmentItem? item = _inventoryManager?.GetEquippedItemAt(slotIndex);
            if (projectedItem != null)
            {
                if (slotIndex == projectedSlotIndex)
                {
                    item = projectedItem;
                }
                else if (ReferenceEquals(item, projectedItem))
                {
                    item = null;
                }
            }

            if (item != null)
            {
                supportSlot.Equip(item);
            }

            supportSlots.Add(supportSlot);
        }

        return _synergyManager.EvaluateSupportSynergies(supportSlots);
    }

    private int CountEquippedItems()
    {
        int count = 0;
        for (int slotIndex = 0; slotIndex < EquippedSlotCount; slotIndex++)
        {
            if (_inventoryManager?.GetEquippedItemAt(slotIndex) != null)
            {
                count++;
            }
        }

        return count;
    }

    private static string GetSlotShortLabel(int slotIndex)
    {
        return slotIndex == 0 ? "Primary" : $"Support {slotIndex}";
    }

    private static SubstatRoll? FindSubstat(EquipmentItem item, SubstatType type)
    {
        float total = StatGenerator.GetTotalSubstatValue(item, type);
        return Mathf.Abs(total) > 0.001f ? new SubstatRoll(type, total) : null;
    }

    private static bool HasAnySubstat(EquipmentItem item, params SubstatType[] types)
    {
        for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            if (FindSubstat(item, types[typeIndex]) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetRaritySortValue(EquipmentItem item)
    {
        string key = item.Rarity?.Key?.Trim().ToLowerInvariant() ?? "common";
        return key switch
        {
            "divine" => 5,
            "mythic" => 4,
            "legendary" => 3,
            "rare" => 2,
            "uncommon" => 1,
            _ => 0,
        };
    }

    private int EquippedSortValue(EquipmentItem item)
    {
        int slot = FindEquippedSlot(item);
        return slot >= 0 ? EquippedSlotCount - slot : 0;
    }

    private static Color GetRarityColor(EquipmentItem? item)
    {
        return item?.Rarity?.UiColor ?? Colors.White;
    }

    private static Color GetModifierColor(EquipmentItem item)
    {
        return item.Modifier?.UiColor ?? new Color(0.78f, 0.84f, 0.92f, 1f);
    }

    private void SetSummary(string text)
    {
        if (SummaryLabel != null)
        {
            SummaryLabel.Text = text;
        }
    }

    private void SetDetailsText(string text)
    {
        if (ItemDetailsText != null)
        {
            ItemDetailsText.Text = text;
        }
    }

    private void SetFeedback(string message)
    {
        if (FeedbackLabel != null)
        {
            FeedbackLabel.Text = message;
        }
    }

    private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int borderWidth)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
        };
    }

    private static float GetModifierVisualPulse(ModifierVisualStyle style, EquipmentItem? item, float time)
    {
        if (style.UsesRadiantVisuals)
        {
            return GetShinyVisualPulse(item, time);
        }

        if (style.UsesElectricVisuals)
        {
            return GetElectricVisualPulse(item, time);
        }

        if (style.UsesGlitchVisuals)
        {
            return GetUnstableVisualPulse(item, time);
        }

        return 0f;
    }

    private static Color GetModifierAccent(ModifierVisualStyle style, EquipmentItem? item, float time)
    {
        if (style.UsesRadiantVisuals)
        {
            return GetShinyAccent(style, item, time);
        }

        if (style.UsesElectricVisuals)
        {
            return GetElectricAccent(style, item, time);
        }

        if (style.UsesGlitchVisuals)
        {
            return GetUnstableAccent(style, item, time);
        }

        return style.AccentColor;
    }

    private static Color GetModifierFontColor(ModifierVisualStyle style, float pulse)
    {
        if (style.UsesRadiantVisuals)
        {
            return style.BaseColor.Lerp(style.AccentColor, 0.28f + pulse * 0.42f).Lightened(0.08f);
        }

        if (style.UsesElectricVisuals)
        {
            return style.BaseColor.Lerp(style.SecondaryAccentColor, 0.34f + pulse * 0.40f).Lightened(0.14f);
        }

        return style.AccentColor.Lightened(0.12f);
    }

    private static float GetUnstableVisualPulse(EquipmentItem? item, float time)
    {
        float seed = StableVisualSeed(item?.InstanceId);
        float slowPulse = (Mathf.Sin(time * (2.7f + seed * 0.8f) + seed * Mathf.Tau) + 1f) * 0.5f;
        int steppedFrame = Mathf.FloorToInt((time + seed * 3.7f) * 8.0f);
        float glitchStep = steppedFrame % 6 == 0 ? 1f : steppedFrame % 11 == 0 ? 0.72f : 0f;
        return Mathf.Clamp(slowPulse * 0.52f + glitchStep * 0.48f, 0f, 1f);
    }

    private static Color GetUnstableAccent(ModifierVisualStyle style, EquipmentItem? item, float time)
    {
        float seed = StableVisualSeed(item?.InstanceId);
        return Mathf.Sin(time * 16f + seed * Mathf.Tau) >= 0f ? style.AccentColor : style.SecondaryAccentColor;
    }

    private static float GetShinyVisualPulse(EquipmentItem? item, float time)
    {
        float seed = StableVisualSeed(item?.InstanceId);
        float softPulse = (Mathf.Sin(time * (1.9f + seed * 0.35f) + seed * Mathf.Tau) + 1f) * 0.5f;
        float flashPulse = Mathf.Pow((Mathf.Sin(time * (1.05f + seed * 0.15f) + seed * 5.1f) + 1f) * 0.5f, 9.0f);
        int twinkleFrame = Mathf.FloorToInt((time + seed * 2.9f) * 5.5f);
        float twinkleStep = twinkleFrame % 9 == 0 ? 0.62f : twinkleFrame % 13 == 0 ? 0.38f : 0f;
        return Mathf.Clamp(softPulse * 0.38f + flashPulse * 0.42f + twinkleStep * 0.20f, 0f, 1f);
    }

    private static Color GetShinyAccent(ModifierVisualStyle style, EquipmentItem? item, float time)
    {
        float seed = StableVisualSeed(item?.InstanceId);
        float sparkle = Mathf.Pow((Mathf.Sin(time * 4.2f + seed * Mathf.Tau) + 1f) * 0.5f, 4.5f);
        return style.BaseColor.Lerp(style.AccentColor, 0.35f + sparkle * 0.55f);
    }

    private static float GetElectricVisualPulse(EquipmentItem? item, float time)
    {
        float seed = StableVisualSeed(item?.InstanceId);
        float hum = (Mathf.Sin(time * (6.8f + seed * 1.4f) + seed * Mathf.Tau) + 1f) * 0.5f;
        float snap = Mathf.Pow((Mathf.Sin(time * (13.5f + seed * 2.2f) + seed * 8.1f) + 1f) * 0.5f, 4.8f);
        int arcFrame = Mathf.FloorToInt((time + seed * 4.3f) * 9.5f);
        float arcStep = arcFrame % 7 == 0 ? 0.70f : arcFrame % 11 == 0 ? 0.42f : 0f;
        return Mathf.Clamp(hum * 0.30f + snap * 0.42f + arcStep * 0.28f, 0f, 1f);
    }

    private static Color GetElectricAccent(ModifierVisualStyle style, EquipmentItem? item, float time)
    {
        float seed = StableVisualSeed(item?.InstanceId);
        float snap = Mathf.Pow((Mathf.Sin(time * 8.4f + seed * Mathf.Tau) + 1f) * 0.5f, 2.8f);
        Color flashColor = snap > 0.72f ? style.SecondaryAccentColor : style.GlowColor;
        return style.AccentColor.Lerp(flashColor, 0.22f + snap * 0.58f);
    }

    private static void ApplyUnstableCardEffects(StyleBoxFlat style, ModifierVisualStyle modifierVisual, float pulse, bool selected, int baseBorderWidth)
    {
        float shadowAlpha = selected ? 0.34f + pulse * 0.20f : 0.20f + pulse * 0.16f;
        style.ShadowColor = new Color(modifierVisual.BaseColor.R, modifierVisual.BaseColor.G, modifierVisual.BaseColor.B, shadowAlpha);
        style.ShadowSize = (selected ? 12 : 8) + Mathf.RoundToInt(pulse * (selected ? 8f : 6f));
        style.ShadowOffset = pulse > 0.64f ? new Vector2(2f, -1f) : Vector2.Zero;

        if (pulse <= 0.62f)
        {
            return;
        }

        int glitchWidth = Mathf.Max(1, baseBorderWidth);
        style.BorderWidthLeft = glitchWidth + 1;
        style.BorderWidthTop = glitchWidth;
        style.BorderWidthRight = Mathf.Max(1, glitchWidth - 1);
        style.BorderWidthBottom = glitchWidth + 1;
    }

    private static void ApplyShinyCardEffects(StyleBoxFlat style, ModifierVisualStyle modifierVisual, float pulse, bool selected, int baseBorderWidth)
    {
        float shadowAlpha = selected ? 0.30f + pulse * 0.18f : 0.16f + pulse * 0.15f;
        style.ShadowColor = new Color(modifierVisual.AccentColor.R, modifierVisual.AccentColor.G, modifierVisual.AccentColor.B, shadowAlpha);
        style.ShadowSize = (selected ? 11 : 7) + Mathf.RoundToInt(pulse * (selected ? 9f : 6f));
        style.ShadowOffset = Vector2.Zero;
        style.BorderColor = style.BorderColor.Lerp(modifierVisual.AccentColor, 0.12f + pulse * 0.24f);

        if (pulse <= 0.72f)
        {
            return;
        }

        int shineWidth = Mathf.Max(1, baseBorderWidth + 1);
        style.BorderWidthLeft = shineWidth;
        style.BorderWidthTop = shineWidth;
        style.BorderWidthRight = shineWidth;
        style.BorderWidthBottom = shineWidth;
    }

    private static void ApplyElectricCardEffects(StyleBoxFlat style, ModifierVisualStyle modifierVisual, float pulse, bool selected, int baseBorderWidth)
    {
        float shadowAlpha = selected ? 0.34f + pulse * 0.22f : 0.18f + pulse * 0.20f;
        style.ShadowColor = new Color(modifierVisual.AccentColor.R, modifierVisual.AccentColor.G, modifierVisual.AccentColor.B, shadowAlpha);
        style.ShadowSize = (selected ? 12 : 8) + Mathf.RoundToInt(pulse * (selected ? 10f : 7f));
        style.ShadowOffset = pulse > 0.66f ? new Vector2(1f, -1f) : Vector2.Zero;
        Color flashColor = pulse > 0.78f ? modifierVisual.SecondaryAccentColor : modifierVisual.GlowColor;
        style.BorderColor = style.BorderColor.Lerp(flashColor, 0.10f + pulse * 0.30f);

        if (pulse <= 0.58f)
        {
            return;
        }

        int arcWidth = Mathf.Max(1, baseBorderWidth + (pulse > 0.78f ? 2 : 1));
        style.BorderWidthLeft = arcWidth;
        style.BorderWidthTop = Mathf.Max(1, arcWidth - 1);
        style.BorderWidthRight = arcWidth;
        style.BorderWidthBottom = Mathf.Max(1, arcWidth - 1);
    }

    private static float StableVisualSeed(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0.5f;
        }

        unchecked
        {
            uint hash = 2166136261u;
            for (int charIndex = 0; charIndex < text.Length; charIndex++)
            {
                hash ^= text[charIndex];
                hash *= 16777619u;
            }

            return (hash & 0xFFFFu) / 65535f;
        }
    }

    private static void StyleButton(Button? button, Color background, Color border)
    {
        if (button == null)
        {
            return;
        }

        StyleBoxFlat normal = CreatePanelStyle(background, border, 1);
        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = background.Lightened(0.16f);
        hover.BorderColor = border.Lightened(0.18f);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeColorOverride("font_color", new Color(0.92f, 0.97f, 1f, 1f));
    }
}
