using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Hub;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Main hub UI orchestration: buttons, modal entry points, currency labels, and arena visibility rules.
public partial class StartHubUI : Control
{
    private enum HubChromeSlideDirection
    {
        Up,
        Down,
    }

    private enum HubInlinePickerKind
    {
        None,
        Essence,
        Modulator,
    }

    private sealed class HubEssenceMenuGroup
    {
        public HubEssenceMenuGroup(ModifierData representative, int representativeStoredIndex)
        {
            Representative = representative;
            RepresentativeStoredIndex = representativeStoredIndex;
            Count = 1;
        }

        public ModifierData Representative { get; }
        public int RepresentativeStoredIndex { get; }
        public int Count { get; set; }
    }

    private sealed class HubModulatorMenuGroup
    {
        public HubModulatorMenuGroup(WeaponModulatorData representative, int representativeStoredIndex)
        {
            Representative = representative;
            RepresentativeStoredIndex = representativeStoredIndex;
            Count = 1;
        }

        public WeaponModulatorData Representative { get; }
        public int RepresentativeStoredIndex { get; }
        public int Count { get; set; }
    }

    private sealed class HubChromeSlideTarget
    {
        public HubChromeSlideTarget(Control control, HubChromeSlideDirection direction, MouseFilterEnum baseMouseFilter)
        {
            Control = control;
            Direction = direction;
            BaseMouseFilter = baseMouseFilter;
        }

        public Control Control { get; }
        public HubChromeSlideDirection Direction { get; }
        public MouseFilterEnum BaseMouseFilter { get; }
        public float BaseLeft { get; set; }
        public float BaseTop { get; set; }
        public float BaseRight { get; set; }
        public float BaseBottom { get; set; }
    }

    private readonly struct HubWeaponSnapshot
    {
        public bool HasDamage { get; init; }
        public int HitCount { get; init; }
        public int ProjectileCount { get; init; }
        public float CritChancePercent { get; init; }
        public float CritDamagePercent { get; init; }
        public float DamagePerProjectile { get; init; }
        public float DamagePerAttack { get; init; }
        public float AttacksPerSecond { get; init; }
        public float EffectiveCooldown { get; init; }
        public float BaseDps { get; init; }
        public float ExpectedDps { get; init; }
    }

    private const float HubSocketCardWidth = 248f;
    private const float HubSocketCardMaxHeight = 68f;
    private const float HubSocketCardMinHeight = 54f;
    private const float HubSocketCardGap = 8f;
    private const float HubSocketCardAccentWidth = 5f;
    private const float HubSocketCardTextInset = 14f;
    private const float HubSocketStatsPanelWidth = 448f;
    private const float HubSocketStatsPanelMinHeight = 174f;
    private const float HubSocketStatsPanelGap = 12f;
    private const float HubSocketStatsPanelHorizontalPadding = 28f;
    private const float HubSocketStatsPanelVerticalPadding = 24f;
    private const float HubSocketStatsPanelContentGap = 6f;
    private const float HubSocketStatsPanelCardReserveHeight = 96f;
    private const float HubSocketStatsPanelPickerReserveHeight = 248f;
    private const int HubSocketStatsVisibleSubstats = 10;
    private const float HubSocketScreenMargin = 18f;
    private const float HubSocketTopReserve = 88f;
    private const float HubSocketBottomReserve = 116f;
    private const float HubSocketDragThresholdPixels = 8f;
    private const float HubSocketTraceWidth = 2.2f;
    private const float HubSocketTraceDotRadius = 4.5f;
    private const float HubSocketTraceRevealSpeed = 8.5f;
    private const float HubSocketTraceFadeSpeed = 9.5f;
    private const float HubInlinePickerRevealSpeed = 10.5f;
    private const float HubInlinePickerFadeSpeed = 12.5f;
    private const float HubInlinePickerMotionSpeed = 15f;
    private const float HubInspectionChromeMotionSpeed = 9f;
    private const float HubInspectionChromeTravelMargin = 28f;
    private const float HubEssencePickerBottomMinWidth = 460f;
    private const float HubEssencePickerBottomMaxWidth = 620f;
    private const float HubEssencePickerBottomMinHeight = 180f;
    private const float HubEssencePickerBottomMaxHeight = 320f;
    private const float HubInlinePickerBottomSheetHiddenOffset = 28f;
    private const float HubEssencePickerDockGap = 4f;
    private const float HubBaseCriticalDamagePercent = 50f;
    private const int HubSocketMenuReturnEssence = 1;
    private const int HubSocketMenuReturnModulator = 2;
    private const int HubSocketMenuInstallEssenceBase = 10000;
    private const int HubSocketMenuInstallModulatorBase = 20000;
    private const int HubSocketMenuReplaceModulatorBase = 30000;

    [Signal]
    public delegate void ArenaRequestedEventHandler();

    [Signal]
    public delegate void SkillTreeRequestedEventHandler();

    [Signal]
    public delegate void AutoRollRequestedEventHandler();

    [ExportGroup("Buttons")]
    [Export] public Button? RollButton;
    [Export] public Button? InventoryButton;
    [Export] public Button? SkillTreeButton;
    [Export] public Button? AutoRollButton;
    [Export] public Button? DebugForgeButton;
    [Export] public Button? ArenaButton;
    [Export] public Button? FpsSliceButton;
    [Export] public Button? DebugArenaButton;
    [Export] public Button? SettingsButton;
    [Export] public Button? StatsButton;
    [Export] public Button? PlayerIdentityButton;
    [Export] public Button? DebugAccessButton;

    [ExportGroup("Status UI")]
    [Export] public Label? RollCurrencyLabel;
    [Export] public Label? FatedRollCurrencyLabel;
    [Export] public Label? DebugIndicatorLabel;
    [Export] public Control? DebugIndicator;

    [ExportGroup("Identity UI")]
    [Export] public Label? PlayerNameLabel;
    [Export] public Label? AvatarLabel;

    [ExportGroup("Hub")]
    [Export] public HubSceneController? HubPreviewController;
    [Export] public bool AutoRollUnlocked = false;
    [Export] public string ArenaScenePath = "res://Scenes/Main.tscn";
    [Export] public string FpsSliceScenePath = "res://Scenes/Fps/NorthGate.tscn";

    [ExportGroup("Modal Scenes")]
    [Export] public PackedScene? GachaModalScene;
    [Export] public PackedScene? InventoryModalScene;
    [Export] public PackedScene? SkillTreeModalScene;
    [Export] public PackedScene? SettingsModalScene;
    [Export] public PackedScene? StatsModalScene;
    [Export] public PackedScene? PlayerIdentityModalScene;
    [Export] public PackedScene? DebugPasswordModalScene;
    [Export] public PackedScene? DebugPanelScene;
    [Export] public PackedScene? DebugForgeModalScene;

    [ExportGroup("Manager Paths")]
    [Export] public NodePath ModalManagerPath = new NodePath();
    [Export] public NodePath CurrencyManagerPath = new NodePath();
    [Export] public NodePath DebugManagerPath = new NodePath();
    [Export] public NodePath InventoryManagerPath = new NodePath();
    [Export] public NodePath GameStateManagerPath = new NodePath();

    [ExportGroup("Gacha Data")]
    [Export] public string RarityDataFolder = "res://Resources/Equipment/Rarities";
    [Export] public string ShapeDataFolder = "res://Resources/Equipment/Shapes";
    [Export] public string ModifierDataFolder = "res://Resources/Equipment/Modifiers";

    public bool HubVisible { get; private set; } = true;
    public bool ArenaActive { get; private set; }

    private ModalManager? _modalManager;
    private CurrencyManager? _currencyManager;
    private DebugManager? _debugManager;
    private InventoryManager? _inventoryManager;
    private GameStateManager? _gameStateManager;

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<RarityData> _rarityPool = new();
    private readonly List<ShapeData> _shapePool = new();
    private readonly List<ModifierData> _modifierPool = new();
    private GachaRoller? _gachaRoller;

    private Control? _debugPanelInstance;
    private bool _debugSignalsSubscribed;

    private bool _gachaModalOpen;
    private bool _inventoryModalOpen;
    private bool _skillTreeModalOpen;
    private bool _settingsModalOpen;
    private bool _statsModalOpen;
    private bool _playerIdentityModalOpen;
    private bool _debugForgeOpen;

    private ModalBase? _gachaModalInstance;
    private ModalBase? _inventoryModalInstance;
    private ModalBase? _skillTreeModalInstance;
    private ModalBase? _settingsModalInstance;
    private ModalBase? _statsModalInstance;
    private ModalBase? _playerIdentityModalInstance;
    private ModalBase? _debugForgeModalInstance;
    private SubViewportContainer? _hubPreviewContainer;
    private SubViewport? _hubPreviewViewport;
    private Control? _hubSocketOverlay;
    private PanelContainer? _hubSocketStatsPanel;
    private Label? _hubSocketStatsTitleLabel;
    private Label? _hubSocketStatsMetaLabel;
    private ScrollContainer? _hubSocketStatsScroll;
    private VBoxContainer? _hubSocketStatsContent;
    private string? _hubSocketStatsRenderSignature;
    private PanelContainer? _hubInlinePickerPanel;
    private Label? _hubInlinePickerTitleLabel;
    private Label? _hubInlinePickerSubtitleLabel;
    private ScrollContainer? _hubInlinePickerScroll;
    private VBoxContainer? _hubInlinePickerList;
    private bool _hubSocketOverlayDrawSubscribed;
    private PopupMenu? _hubSocketMenu;
    private bool _hubPreviewInputSubscribed;
    private ulong _lastHubSlotClickTicks;
    private int _lastHubSlotClickIndex = -1;
    private int _activeHubSocketButtonIndex = -1;
    private float _activeHubSocketButtonDragDistance;
    private bool _activeHubSocketButtonDragging;
    private bool _inspectionPointerWasDown;
    private bool _inspectionPointerDragActive;
    private Vector2 _inspectionPointerLastPosition;
    private HubSocketAnchor _hubSocketMenuAnchor;
    private string? _hubSocketMenuItemInstanceId;
    private readonly List<Button> _hubSocketButtons = new();
    private readonly List<HubSocketAnchor> _hubSocketAnchors = new();
    private readonly List<Vector2> _hubSocketLayoutPositions = new();
    private readonly List<HubChromeSlideTarget> _hubChromeSlideTargets = new();
    private Vector2 _hubSocketCardSize = new(HubSocketCardWidth, HubSocketCardMaxHeight);
    private Vector2 _hubSocketStatsPanelSize = new(HubSocketStatsPanelWidth, HubSocketStatsPanelMinHeight);
    private int _hoveredHubSocketButtonIndex = -1;
    private int _selectedHubSocketButtonIndex = -1;
    private int _animatedHubSocketButtonIndex = -1;
    private float _hubSocketTraceReveal;
    private float _hubSocketTraceVisibility;
    private HubInlinePickerKind _activeHubInlinePickerKind;
    private int _activeHubInlinePickerButtonIndex = -1;
    private bool _hubInlinePickerDirty;
    private Rect2 _hubInlinePickerAnimatedRect;
    private Rect2 _hubInlinePickerRestRect;
    private float _hubInlinePickerOpenAmount;
    private bool _hubInlinePickerRestUsesBottomSheet;
    private ulong _hubInlinePickerEscapeConsumeUntilMsec;
    private long _hubInlinePickerHoverActionId = -1;
    private string? _hubInlinePickerHoverPreviewSummary;
    private float _hubInspectionChromeBlend;
    private bool _sceneTransitionInProgress;
    private const int SceneTransitionFlushFrames = 3;
    private const int SceneRetirementDelayFrames = 8;

    public override void _Ready()
    {
        _rng.Randomize();

        ResolveUiReferences();
        ApplyLegacyHubPresentation();
        ResolveModalScenes();
        ResolveManagers();
        BuildGachaPools();
        WireButtons();
        SubscribeSignals();
        BindHubPreviewProvider();

        EnsureDebugPanelLoaded();
        RefreshCurrencyLabels();
        RefreshIdentityUI();
        RefreshDebugVisibility();
        UpdateArenaButtonVisibility();
        EnsureHubSocketOverlay();
        CaptureHubChromeSlideTargets();
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview == null)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton
            && mouseButton.ButtonIndex == MouseButton.Left
            && mouseButton.Pressed
            && hubPreview.IsWeaponInspectionActive
            && TryBeginInspectionPointerDrag(hubPreview))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (HandleActiveHubSocketDragInput(@event, hubPreview))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (hubPreview.HandleWeaponInspectionInput(@event))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        TickInspectionPointerDrag();
        TickHubInspectionChromeAnimation((float)delta);
        TickHubSocketTraceAnimation((float)delta);
        RefreshHubSocketOverlay();
        TickHubInlinePickerAnimation((float)delta);
    }

    private void ResolveUiReferences()
    {
        RollButton ??= GetNodeOrNull<Button>("BottomBar/RollButton");
        InventoryButton ??= GetNodeOrNull<Button>("BottomBar/InventoryButton");
        SkillTreeButton ??= GetNodeOrNull<Button>("BottomBar/SkillTreeButton");
        AutoRollButton ??= GetNodeOrNull<Button>("BottomBar/AutoRollButton");
        DebugForgeButton ??= GetNodeOrNull<Button>("BottomBar/DebugForgeButton");
        ArenaButton ??= GetNodeOrNull<Button>("TopRow/ArenaButton");
        FpsSliceButton ??= GetNodeOrNull<Button>("TopRow/FpsSliceButton");
        DebugArenaButton ??= GetNodeOrNull<Button>("TopRow/DebugArenaButton");
        DebugArenaButton ??= GetNodeOrNull<Button>("DebugArenaButton");
        DebugArenaButton ??= new Button { Name = "DebugArenaButton" };
        SettingsButton ??= GetNodeOrNull<Button>("BottomBar/SettingsButton");
        StatsButton ??= GetNodeOrNull<Button>("BottomBar/StatsButton");
        PlayerIdentityButton ??= GetNodeOrNull<Button>("BottomBar/PlayerIdentityButton");
        DebugAccessButton ??= GetNodeOrNull<Button>("BottomBar/DebugAccessButton");

        RollCurrencyLabel ??= GetNodeOrNull<Label>("TopRow/CurrencyRow/RollValue");
        FatedRollCurrencyLabel ??= GetNodeOrNull<Label>("TopRow/CurrencyRow/FatedValue");
        DebugIndicator ??= GetNodeOrNull<Control>("DebugIndicator");
        DebugIndicatorLabel ??= GetNodeOrNull<Label>("DebugIndicator/DebugIndicatorLabel");
        PlayerNameLabel ??= GetNodeOrNull<Label>("TopRow/IdentityInfo/PlayerNameValue");
        AvatarLabel ??= GetNodeOrNull<Label>("TopRow/IdentityInfo/AvatarValue");

        HubPreviewController ??= GetNodeOrNull<HubSceneController>("Center/PreviewContainer/PreviewViewport/HubPreview");
        _hubPreviewContainer ??= GetNodeOrNull<SubViewportContainer>("Center/PreviewContainer");
        _hubPreviewViewport ??= GetNodeOrNull<SubViewport>("Center/PreviewContainer/PreviewViewport");
        _hubSocketOverlay ??= GetNodeOrNull<Control>("HubSocketOverlay");
    }

    private void ApplyLegacyHubPresentation()
    {
        ColorRect? background = GetNodeOrNull<ColorRect>("Background");
        if (background != null)
        {
            SetFullRect(background);
            background.Color = new Color(0.014f, 0.031f, 0.063f, 1f);
            background.MouseFilter = MouseFilterEnum.Ignore;
            background.ZIndex = -10;
        }

        Control? center = GetNodeOrNull<Control>("Center");
        if (center != null)
        {
            SetFullRect(center);
            center.MouseFilter = MouseFilterEnum.Pass;
            center.ZIndex = 0;
        }

        SubViewportContainer? previewContainer = _hubPreviewContainer ?? GetNodeOrNull<SubViewportContainer>("Center/PreviewContainer");
        if (previewContainer != null)
        {
            SetFullRect(previewContainer);
            previewContainer.Stretch = true;
            previewContainer.MouseFilter = MouseFilterEnum.Stop;
            previewContainer.ZIndex = 0;
        }

        SubViewport? previewViewport = _hubPreviewViewport ?? GetNodeOrNull<SubViewport>("Center/PreviewContainer/PreviewViewport");
        if (previewViewport != null)
        {
            previewViewport.HandleInputLocally = true;
            previewViewport.PhysicsObjectPicking = true;
            previewViewport.GuiDisableInput = false;
        }

        Label? characterLabel = GetNodeOrNull<Label>("Center/CharacterLabel");
        if (characterLabel != null)
        {
            SetFullRect(characterLabel);
            characterLabel.Text = "Your Character";
            characterLabel.HorizontalAlignment = HorizontalAlignment.Center;
            characterLabel.VerticalAlignment = VerticalAlignment.Center;
            characterLabel.MouseFilter = MouseFilterEnum.Ignore;
            characterLabel.ZIndex = 3;
            characterLabel.AddThemeFontSizeOverride("font_size", 18);
            characterLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.40f));
            characterLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.55f));
            characterLabel.AddThemeConstantOverride("shadow_offset_x", 0);
            characterLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        }

        HBoxContainer? topRow = GetNodeOrNull<HBoxContainer>("TopRow");
        if (topRow != null)
        {
            SetTopWide(topRow, 20f, 20f, 76f);
            topRow.MouseFilter = MouseFilterEnum.Pass;
            topRow.ZIndex = 10;
            topRow.AddThemeConstantOverride("separation", 10);
        }

        Label? title = GetNodeOrNull<Label>("TopRow/TitleLabel");
        if (title != null)
        {
            title.Visible = false;
            title.CustomMinimumSize = Vector2.Zero;
        }

        Control? identityInfo = GetNodeOrNull<Control>("TopRow/IdentityInfo");
        if (identityInfo != null)
        {
            identityInfo.Visible = false;
        }

        ApplyLegacyPlayerIdentityChip();

        if (ArenaButton != null)
        {
            if (topRow != null && ArenaButton.GetParent() != this)
            {
                ArenaButton.Reparent(this);
            }

            ArenaButton.AnchorLeft = 0.5f;
            ArenaButton.AnchorRight = 0.5f;
            ArenaButton.AnchorTop = 0f;
            ArenaButton.AnchorBottom = 0f;
            ArenaButton.OffsetLeft = -66f;
            ArenaButton.OffsetTop = 18f;
            ArenaButton.OffsetRight = 66f;
            ArenaButton.OffsetBottom = 64f;
            ArenaButton.ZIndex = 14;
            ArenaButton.Text = "⚔ Arena";
            ArenaButton.CustomMinimumSize = new Vector2(132f, 46f);
            ApplyLegacyButtonStyle(
                ArenaButton,
                new Color(0.76f, 0.10f, 0.13f, 0.95f),
                new Color(1f, 0.27f, 0.31f, 0.92f),
                Colors.White);
        }

        if (FpsSliceButton != null)
        {
            if (FpsSliceButton.GetParent() == null)
            {
                AddChild(FpsSliceButton);
            }
            else if (FpsSliceButton.GetParent() != this)
            {
                FpsSliceButton.Reparent(this);
            }

            FpsSliceButton.AnchorLeft = 0.5f;
            FpsSliceButton.AnchorRight = 0.5f;
            FpsSliceButton.AnchorTop = 0f;
            FpsSliceButton.AnchorBottom = 0f;
            FpsSliceButton.OffsetLeft = -232f;
            FpsSliceButton.OffsetTop = 18f;
            FpsSliceButton.OffsetRight = -78f;
            FpsSliceButton.OffsetBottom = 64f;
            FpsSliceButton.ZIndex = 14;
            FpsSliceButton.Text = "North Gate";
            FpsSliceButton.CustomMinimumSize = new Vector2(154f, 46f);
            ApplyLegacyButtonStyle(
                FpsSliceButton,
                new Color(0.10f, 0.36f, 0.67f, 0.95f),
                new Color(0.20f, 0.62f, 0.94f, 0.94f),
                Colors.White);
        }

        if (DebugArenaButton != null)
        {
            if (DebugArenaButton.GetParent() == null)
            {
                AddChild(DebugArenaButton);
            }
            else if (DebugArenaButton.GetParent() != this)
            {
                DebugArenaButton.Reparent(this);
            }

            DebugArenaButton.AnchorLeft = 0.5f;
            DebugArenaButton.AnchorRight = 0.5f;
            DebugArenaButton.AnchorTop = 0f;
            DebugArenaButton.AnchorBottom = 0f;
            DebugArenaButton.OffsetLeft = 82f;
            DebugArenaButton.OffsetTop = 18f;
            DebugArenaButton.OffsetRight = 242f;
            DebugArenaButton.OffsetBottom = 64f;
            DebugArenaButton.ZIndex = 14;
            DebugArenaButton.Text = "🧪 Dev Arena";
            DebugArenaButton.CustomMinimumSize = new Vector2(160f, 46f);
            ApplyLegacyButtonStyle(
                DebugArenaButton,
                new Color(0.20f, 0.12f, 0.62f, 0.95f),
                new Color(0.49f, 0.27f, 0.86f, 0.95f),
                Colors.White);
        }

        HBoxContainer? currencyRow = GetNodeOrNull<HBoxContainer>("TopRow/CurrencyRow");
        if (currencyRow != null)
        {
            ApplyLegacyCurrencyRow(currencyRow);
        }

        HBoxContainer? bottomBar = GetNodeOrNull<HBoxContainer>("BottomBar");
        if (bottomBar != null)
        {
            ApplyLegacyControlsBar(bottomBar);
        }

        if (DebugIndicator != null)
        {
            DebugIndicator.ZIndex = 12;
            DebugIndicator.MouseFilter = MouseFilterEnum.Ignore;
            DebugIndicator.SetAnchorsPreset(LayoutPreset.BottomLeft);
            DebugIndicator.OffsetLeft = 24f;
            DebugIndicator.OffsetTop = -146f;
            DebugIndicator.OffsetRight = 620f;
            DebugIndicator.OffsetBottom = -98f;
        }

        MoveHubChromeToFront();
    }

    private void CaptureHubChromeSlideTargets()
    {
        _hubChromeSlideTargets.Clear();

        AddHubChromeSlideTarget(GetNodeOrNull<Control>("Center/CharacterLabel"), HubChromeSlideDirection.Up);
        AddHubChromeSlideTarget(PlayerIdentityButton, HubChromeSlideDirection.Up);
        AddHubChromeSlideTarget(ArenaButton, HubChromeSlideDirection.Up);
        AddHubChromeSlideTarget(FpsSliceButton, HubChromeSlideDirection.Up);
        AddHubChromeSlideTarget(DebugArenaButton, HubChromeSlideDirection.Up);
        AddHubChromeSlideTarget(GetNodeOrNull<Control>("TopRow/CurrencyRow"), HubChromeSlideDirection.Up);
        AddHubChromeSlideTarget(GetNodeOrNull<Control>("ControlsBackdrop"), HubChromeSlideDirection.Down);
        AddHubChromeSlideTarget(GetNodeOrNull<Control>("BottomBar"), HubChromeSlideDirection.Down);
        AddHubChromeSlideTarget(DebugIndicator, HubChromeSlideDirection.Down);
    }

    private void AddHubChromeSlideTarget(Control? control, HubChromeSlideDirection direction)
    {
        if (control == null || !GodotObject.IsInstanceValid(control))
        {
            return;
        }

        _hubChromeSlideTargets.Add(new HubChromeSlideTarget(control, direction, control.MouseFilter)
        {
            BaseLeft = control.OffsetLeft,
            BaseTop = control.OffsetTop,
            BaseRight = control.OffsetRight,
            BaseBottom = control.OffsetBottom,
        });
    }

    private void TickHubInspectionChromeAnimation(float deltaSeconds)
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        bool inspectionActive = hubPreview != null && hubPreview.IsWeaponInspectionActive && HubVisible && !ArenaActive;
        float targetBlend = inspectionActive ? 1f : 0f;
        _hubInspectionChromeBlend = Mathf.MoveToward(_hubInspectionChromeBlend, targetBlend, deltaSeconds * HubInspectionChromeMotionSpeed);

        if (_hubChromeSlideTargets.Count == 0)
        {
            CaptureHubChromeSlideTargets();
        }

        for (int targetIndex = 0; targetIndex < _hubChromeSlideTargets.Count; targetIndex++)
        {
            HubChromeSlideTarget target = _hubChromeSlideTargets[targetIndex];
            if (!GodotObject.IsInstanceValid(target.Control))
            {
                continue;
            }

            float travel = GetHubChromeSlideTravel(target);
            float signedOffset = (target.Direction == HubChromeSlideDirection.Up ? -travel : travel) * _hubInspectionChromeBlend;
            target.Control.OffsetLeft = target.BaseLeft;
            target.Control.OffsetTop = target.BaseTop + signedOffset;
            target.Control.OffsetRight = target.BaseRight;
            target.Control.OffsetBottom = target.BaseBottom + signedOffset;
            target.Control.MouseFilter = _hubInspectionChromeBlend > 0.01f ? MouseFilterEnum.Ignore : target.BaseMouseFilter;
        }
    }

    private static float GetHubChromeSlideTravel(HubChromeSlideTarget target)
    {
        float controlHeight = Mathf.Max(1f, Mathf.Max(target.Control.Size.Y, target.BaseBottom - target.BaseTop));
        return target.Direction switch
        {
            HubChromeSlideDirection.Up => Mathf.Max(controlHeight, target.BaseBottom) + HubInspectionChromeTravelMargin,
            _ => Mathf.Max(controlHeight, Mathf.Abs(target.BaseTop)) + HubInspectionChromeTravelMargin,
        };
    }

    private void ApplyLegacyCurrencyRow(HBoxContainer currencyRow)
    {
        if (currencyRow.GetParent() != this)
        {
            currencyRow.Reparent(this);
        }

        currencyRow.AnchorLeft = 1f;
        currencyRow.AnchorRight = 1f;
        currencyRow.AnchorTop = 0f;
        currencyRow.AnchorBottom = 0f;
        currencyRow.OffsetLeft = -156f;
        currencyRow.OffsetTop = 14f;
        currencyRow.OffsetRight = -32f;
        currencyRow.OffsetBottom = 50f;
        currencyRow.Alignment = BoxContainer.AlignmentMode.End;
        currencyRow.ZIndex = 15;
        currencyRow.MouseFilter = MouseFilterEnum.Pass;
        currencyRow.AddThemeConstantOverride("separation", 8);

        Label? rollLabel = currencyRow.GetNodeOrNull<Label>("RollLabel")
            ?? currencyRow.GetNodeOrNull<Label>("RollCurrencyChip/Margin/Content/RollLabel");
        Label? fatedLabel = currencyRow.GetNodeOrNull<Label>("FatedLabel")
            ?? currencyRow.GetNodeOrNull<Label>("FatedCurrencyChip/Margin/Content/FatedLabel");

        ConfigureCurrencyChip(currencyRow, "RollCurrencyChip", rollLabel, RollCurrencyLabel, "🎲", 0);
        ConfigureCurrencyChip(currencyRow, "FatedCurrencyChip", fatedLabel, FatedRollCurrencyLabel, "⭐", 1);
    }

    private void ApplyLegacyPlayerIdentityChip()
    {
        if (PlayerIdentityButton == null)
        {
            return;
        }

        if (PlayerIdentityButton.GetParent() != this)
        {
            PlayerIdentityButton.Reparent(this);
        }

        PlayerIdentityButton.AnchorLeft = 0f;
        PlayerIdentityButton.AnchorRight = 0f;
        PlayerIdentityButton.AnchorTop = 0f;
        PlayerIdentityButton.AnchorBottom = 0f;
        PlayerIdentityButton.OffsetLeft = 16f;
        PlayerIdentityButton.OffsetTop = 18f;
        PlayerIdentityButton.OffsetRight = 332f;
        PlayerIdentityButton.OffsetBottom = 98f;
        PlayerIdentityButton.ZIndex = 15;
        PlayerIdentityButton.Text = string.Empty;
        PlayerIdentityButton.CustomMinimumSize = new Vector2(316f, 80f);
        ApplyLegacyButtonStyle(
            PlayerIdentityButton,
            new Color(0.030f, 0.052f, 0.093f, 0.93f),
            new Color(1f, 1f, 1f, 0.15f),
            Colors.White);
        RefreshLegacyIdentityCard();
    }

    private void ConfigureCurrencyChip(
        HBoxContainer currencyRow,
        string chipName,
        Label? label,
        Label? value,
        string labelText,
        int index)
    {
        if (label == null || value == null)
        {
            return;
        }

        PanelContainer chip = currencyRow.GetNodeOrNull<PanelContainer>(chipName) ?? new PanelContainer { Name = chipName };
        if (chip.GetParent() != currencyRow)
        {
            currencyRow.AddChild(chip);
        }

        currencyRow.MoveChild(chip, Mathf.Clamp(index, 0, currencyRow.GetChildCount() - 1));
        chip.MouseFilter = MouseFilterEnum.Pass;
        chip.CustomMinimumSize = new Vector2(58f, 34f);
        chip.AddThemeStyleboxOverride("panel", CreateLegacyStyleBox(
            new Color(0f, 0f, 0f, 0.50f),
            new Color(1f, 1f, 1f, 0.10f),
            10));

        MarginContainer margin = chip.GetNodeOrNull<MarginContainer>("Margin") ?? new MarginContainer { Name = "Margin" };
        if (margin.GetParent() != chip)
        {
            chip.AddChild(margin);
        }

        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);

        HBoxContainer content = margin.GetNodeOrNull<HBoxContainer>("Content") ?? new HBoxContainer { Name = "Content" };
        if (content.GetParent() != margin)
        {
            margin.AddChild(content);
        }

        content.Alignment = BoxContainer.AlignmentMode.Center;
        content.AddThemeConstantOverride("separation", 5);

        label.Text = labelText;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.88f));

        value.VerticalAlignment = VerticalAlignment.Center;
        value.CustomMinimumSize = Vector2.Zero;
        value.AddThemeFontSizeOverride("font_size", 14);
        value.AddThemeColorOverride("font_color", Colors.White);

        if (label.GetParent() != content)
        {
            label.Reparent(content);
        }

        if (value.GetParent() != content)
        {
            value.Reparent(content);
        }
    }

    private void ApplyLegacyControlsBar(HBoxContainer bottomBar)
    {
        bottomBar.AnchorLeft = 0.5f;
        bottomBar.AnchorRight = 0.5f;
        bottomBar.AnchorTop = 1f;
        bottomBar.AnchorBottom = 1f;
        bottomBar.OffsetLeft = -236f;
        bottomBar.OffsetTop = -76f;
        bottomBar.OffsetRight = 236f;
        bottomBar.OffsetBottom = -16f;
        bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
        bottomBar.MouseFilter = MouseFilterEnum.Pass;
        bottomBar.ZIndex = 11;
        bottomBar.AddThemeConstantOverride("separation", 10);

        Panel backdrop = GetNodeOrNull<Panel>("ControlsBackdrop") ?? new Panel { Name = "ControlsBackdrop" };
        if (backdrop.GetParent() != this)
        {
            AddChild(backdrop);
        }

        int bottomIndex = bottomBar.GetIndex();
        if (backdrop.GetIndex() > bottomIndex)
        {
            MoveChild(backdrop, bottomIndex);
        }

        backdrop.AnchorLeft = 0.5f;
        backdrop.AnchorRight = 0.5f;
        backdrop.AnchorTop = 1f;
        backdrop.AnchorBottom = 1f;
        backdrop.OffsetLeft = -238f;
        backdrop.OffsetTop = -76f;
        backdrop.OffsetRight = 238f;
        backdrop.OffsetBottom = -15f;
        backdrop.MouseFilter = MouseFilterEnum.Ignore;
        backdrop.ZIndex = 10;
        backdrop.AddThemeStyleboxOverride("panel", CreateLegacyStyleBox(
            new Color(0.043f, 0.071f, 0.125f, 0.85f),
            new Color(1f, 1f, 1f, 0.10f),
            9));

        ApplyLegacyButtonStyle(
            RollButton,
            new Color(0.49f, 0.27f, 0.86f, 0.95f),
            new Color(0.55f, 0.36f, 0.96f, 0.95f),
            Colors.White,
            "Roll");
        ApplyLegacyButtonStyle(InventoryButton, new Color(0.047f, 0.102f, 0.137f, 0.95f), new Color(1f, 1f, 1f, 0.10f), Colors.White);
        ApplyLegacyButtonStyle(SkillTreeButton, new Color(0.047f, 0.102f, 0.137f, 0.95f), new Color(1f, 1f, 1f, 0.10f), Colors.White);
        ApplyLegacyButtonStyle(AutoRollButton, new Color(0.047f, 0.102f, 0.137f, 0.95f), new Color(1f, 1f, 1f, 0.10f), Colors.White);
        ApplyLegacyButtonStyle(
            DebugForgeButton,
            new Color(0.43f, 0.16f, 0.85f, 0.95f),
            new Color(0.49f, 0.27f, 0.86f, 0.95f),
            Colors.White,
            "🔧 Debug Forge");
        ApplyLegacyButtonStyle(SettingsButton, new Color(0.047f, 0.102f, 0.137f, 0.95f), new Color(1f, 1f, 1f, 0.10f), Colors.White);
        ApplyLegacyButtonStyle(StatsButton, new Color(0.047f, 0.102f, 0.137f, 0.95f), new Color(1f, 1f, 1f, 0.10f), Colors.White);
        ApplyLegacyButtonStyle(DebugAccessButton, new Color(0.047f, 0.102f, 0.137f, 0.95f), new Color(1f, 1f, 1f, 0.10f), Colors.White, "Debug");

        SetLegacyButtonSize(InventoryButton, 124f, 48f);
        SetLegacyButtonSize(SkillTreeButton, 120f, 48f);
        SetLegacyButtonSize(DebugForgeButton, 174f, 48f);

        if (RollButton != null)
        {
            RollButton.Visible = false;
        }

        if (AutoRollButton != null)
        {
            AutoRollButton.Visible = false;
        }

        if (SettingsButton != null)
        {
            SettingsButton.Visible = false;
        }

        if (StatsButton != null)
        {
            StatsButton.Visible = false;
        }

        if (DebugAccessButton != null)
        {
            DebugAccessButton.Visible = false;
        }
    }

    private void RefreshLegacyIdentityCard()
    {
        if (PlayerIdentityButton == null)
        {
            return;
        }

        Panel avatarBadge = PlayerIdentityButton.GetNodeOrNull<Panel>("AvatarBadge") ?? new Panel { Name = "AvatarBadge" };
        if (avatarBadge.GetParent() != PlayerIdentityButton)
        {
            PlayerIdentityButton.AddChild(avatarBadge);
        }

        avatarBadge.AnchorLeft = 0f;
        avatarBadge.AnchorRight = 0f;
        avatarBadge.AnchorTop = 0f;
        avatarBadge.AnchorBottom = 0f;
        avatarBadge.OffsetLeft = 14f;
        avatarBadge.OffsetTop = 14f;
        avatarBadge.OffsetRight = 66f;
        avatarBadge.OffsetBottom = 66f;
        avatarBadge.MouseFilter = MouseFilterEnum.Ignore;
        avatarBadge.AddThemeStyleboxOverride("panel", CreateLegacyStyleBox(
            new Color(0.43f, 0.30f, 0.95f, 1f),
            new Color(1f, 0.86f, 0.18f, 0.80f),
            999));

        Label avatarGlyph = avatarBadge.GetNodeOrNull<Label>("AvatarGlyph") ?? new Label { Name = "AvatarGlyph" };
        if (avatarGlyph.GetParent() != avatarBadge)
        {
            avatarBadge.AddChild(avatarGlyph);
        }

        SetFullRect(avatarGlyph);
        avatarGlyph.Text = "😀";
        avatarGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        avatarGlyph.VerticalAlignment = VerticalAlignment.Center;
        avatarGlyph.MouseFilter = MouseFilterEnum.Ignore;
        avatarGlyph.AddThemeFontSizeOverride("font_size", 30);
        avatarGlyph.AddThemeColorOverride("font_color", Colors.White);

        Label playerName = PlayerIdentityButton.GetNodeOrNull<Label>("PlayerName") ?? new Label { Name = "PlayerName" };
        if (playerName.GetParent() != PlayerIdentityButton)
        {
            PlayerIdentityButton.AddChild(playerName);
        }

        playerName.AnchorLeft = 0f;
        playerName.AnchorRight = 1f;
        playerName.AnchorTop = 0f;
        playerName.AnchorBottom = 0f;
        playerName.OffsetLeft = 80f;
        playerName.OffsetTop = 15f;
        playerName.OffsetRight = -14f;
        playerName.OffsetBottom = 44f;
        playerName.Text = ResolveLegacyPlayerName();
        playerName.VerticalAlignment = VerticalAlignment.Center;
        playerName.MouseFilter = MouseFilterEnum.Ignore;
        playerName.AddThemeFontSizeOverride("font_size", 24);
        playerName.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 1f, 1f));

        Label hint = PlayerIdentityButton.GetNodeOrNull<Label>("IdentityHint") ?? new Label { Name = "IdentityHint" };
        if (hint.GetParent() != PlayerIdentityButton)
        {
            PlayerIdentityButton.AddChild(hint);
        }

        hint.AnchorLeft = 0f;
        hint.AnchorRight = 1f;
        hint.AnchorTop = 0f;
        hint.AnchorBottom = 0f;
        hint.OffsetLeft = 80f;
        hint.OffsetTop = 46f;
        hint.OffsetRight = -14f;
        hint.OffsetBottom = 70f;
        hint.Text = "Click to edit";
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.MouseFilter = MouseFilterEnum.Ignore;
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.66f));
    }

    private static void SetLegacyButtonSize(Button? button, float width, float height)
    {
        if (button == null)
        {
            return;
        }

        button.CustomMinimumSize = new Vector2(width, height);
    }

    private static void ApplyLegacyButtonStyle(
        Button? button,
        Color background,
        Color border,
        Color fontColor,
        string? text = null)
    {
        if (button == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            button.Text = text;
        }

        button.CustomMinimumSize = new Vector2(Mathf.Max(button.CustomMinimumSize.X, 116f), 52f);
        button.MouseFilter = MouseFilterEnum.Stop;
        button.FocusMode = FocusModeEnum.All;
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", fontColor);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeStyleboxOverride("normal", CreateLegacyStyleBox(background, border, 10));
        button.AddThemeStyleboxOverride("hover", CreateLegacyStyleBox(background.Lightened(0.15f), border.Lightened(0.25f), 10));
        button.AddThemeStyleboxOverride("pressed", CreateLegacyStyleBox(background.Darkened(0.08f), border, 10));
        button.AddThemeStyleboxOverride("disabled", CreateLegacyStyleBox(background.Darkened(0.45f), border.Darkened(0.45f), 10));
    }

    private static StyleBoxFlat CreateLegacyStyleBox(Color background, Color border, int radius)
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

    private static void SetFullRect(Control control)
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

    private static void SetTopWide(Control control, float horizontalMargin, float top, float bottom)
    {
        control.AnchorLeft = 0f;
        control.AnchorRight = 1f;
        control.AnchorTop = 0f;
        control.AnchorBottom = 0f;
        control.OffsetLeft = horizontalMargin;
        control.OffsetRight = -horizontalMargin;
        control.OffsetTop = top;
        control.OffsetBottom = bottom;
    }

    private string BuildLegacyIdentityButtonText()
    {
        return $"{ResolveLegacyPlayerName()}\nClick to edit";
    }

    private string ResolveLegacyPlayerName()
    {
        return _gameStateManager != null && !string.IsNullOrWhiteSpace(_gameStateManager.PlayerName)
            ? _gameStateManager.PlayerName
            : PlayerNameLabel?.Text ?? "Player";
    }

    private void ResolveModalScenes()
    {
        GachaModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/GachaModal.tscn");
        InventoryModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/InventoryModal.tscn");
        SkillTreeModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/SkillTreeModal.tscn");
        SettingsModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/SettingsModal.tscn");
        StatsModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/StatsModal.tscn");
        PlayerIdentityModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/PlayerIdentityModal.tscn");
        DebugPasswordModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/DebugPasswordModal.tscn");
        DebugPanelScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/DebugPanel.tscn");
        DebugForgeModalScene ??= ResourceLoader.Load<PackedScene>("res://Scenes/UI/DebugForgeModal.tscn");
    }

    public void SetHubVisible(bool visible)
    {
        HubVisible = visible;
        Visible = visible;
        UpdateArenaButtonVisibility();
    }

    public void SetArenaActive(bool arenaActive)
    {
        ArenaActive = arenaActive;
        UpdateArenaButtonVisibility();
    }

    private void ResolveManagers()
    {
        _modalManager = ResolveNode<ModalManager>(ModalManagerPath, "ModalManager");
        _currencyManager = ResolveNode<CurrencyManager>(CurrencyManagerPath, "CurrencyManager");
        _debugManager = ResolveNode<DebugManager>(DebugManagerPath, "DebugManager");
        _inventoryManager = ResolveNode<InventoryManager>(InventoryManagerPath, "InventoryManager");
        _gameStateManager = ResolveNode<GameStateManager>(GameStateManagerPath, "GameStateManager");

        _gachaRoller = new GachaRoller(_rng, _debugManager);
    }

    private void WireButtons()
    {
        if (RollButton != null)
        {
            RollButton.Pressed += OnRollButtonPressed;
        }

        if (InventoryButton != null)
        {
            InventoryButton.Pressed += OnInventoryButtonPressed;
        }

        if (SkillTreeButton != null)
        {
            SkillTreeButton.Pressed += OnSkillTreePressed;
        }

        if (AutoRollButton != null)
        {
            AutoRollButton.Pressed += OnAutoRollPressed;
        }

        if (DebugForgeButton != null)
        {
            DebugForgeButton.Pressed += OnDebugForgePressed;
        }

        if (ArenaButton != null)
        {
            ArenaButton.Pressed += OnArenaButtonPressed;
        }

        if (FpsSliceButton != null)
        {
            FpsSliceButton.Pressed += OnFpsSliceButtonPressed;
        }

        if (DebugArenaButton != null)
        {
            DebugArenaButton.Pressed += OnDebugArenaButtonPressed;
        }

        if (SettingsButton != null)
        {
            SettingsButton.Pressed += OnSettingsPressed;
        }

        if (StatsButton != null)
        {
            StatsButton.Pressed += OnStatsPressed;
        }

        if (PlayerIdentityButton != null)
        {
            PlayerIdentityButton.Pressed += OnPlayerIdentityPressed;
        }

        if (DebugAccessButton != null)
        {
            DebugAccessButton.Pressed += OnDebugAccessPressed;
        }

        WireHubPreviewInput();
    }

    private void WireHubPreviewInput()
    {
        ResolveHubPreviewReferences();

        if (_hubPreviewInputSubscribed || _hubPreviewContainer == null)
        {
            return;
        }

        _hubPreviewContainer.GuiInput += OnHubPreviewGuiInput;
        _hubPreviewInputSubscribed = true;
    }

    private void OnHubPreviewGuiInput(InputEvent @event)
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview == null || _hubPreviewContainer == null)
        {
            return;
        }

        if (HandleActiveHubSocketDragInput(@event, hubPreview))
        {
            _hubPreviewContainer.AcceptEvent();
            return;
        }

        if (hubPreview.IsWeaponInspectionActive && hubPreview.HandleWeaponInspectionInput(@event))
        {
            _hubPreviewContainer.AcceptEvent();
            return;
        }

        if (@event is InputEventMouseButton inspectionButton
            && inspectionButton.ButtonIndex == MouseButton.Left
            && inspectionButton.Pressed
            && hubPreview.IsWeaponInspectionActive)
        {
            if (TryBeginInspectionPointerDrag(hubPreview))
            {
                _hubPreviewContainer.AcceptEvent();
                return;
            }
        }

        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
        {
            return;
        }

        Vector2 viewportPosition = ToHubPreviewViewportPosition(mb.Position);
        if (!hubPreview.TryPickSlotAtViewportPosition(viewportPosition, out int slotIndex))
        {
            return;
        }

        _hubPreviewContainer.AcceptEvent();
        OpenHubSlotEditor(slotIndex);
    }

    private void OnHubSocketOverlayGuiInput(InputEvent @event)
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview == null || _hubSocketOverlay == null || !hubPreview.IsWeaponInspectionActive)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton
            && mouseButton.ButtonIndex == MouseButton.Left
            && mouseButton.Pressed
            && _activeHubSocketButtonIndex < 0
            && TryBeginInspectionPointerDrag(hubPreview))
        {
            _hubSocketOverlay.AcceptEvent();
        }
    }

    private void OnHubPreviewSlotClicked(int slotIndex)
    {
        OpenHubSlotEditor(slotIndex);
    }

    private void OpenHubSlotEditor(int slotIndex)
    {
        ulong now = Time.GetTicksMsec();
        if (_lastHubSlotClickIndex == slotIndex && now - _lastHubSlotClickTicks < 250)
        {
            return;
        }

        _lastHubSlotClickIndex = slotIndex;
        _lastHubSlotClickTicks = now;

        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview != null && hubPreview.BeginWeaponInspection(slotIndex))
        {
            CloseAnyOpenHubModal();
            return;
        }

        OnHubLoadoutSlotPressed(slotIndex);
    }

    private Vector2 ToHubPreviewViewportPosition(Vector2 containerPosition)
    {
        if (_hubPreviewContainer == null || _hubPreviewViewport == null)
        {
            return containerPosition;
        }

        Vector2 containerSize = _hubPreviewContainer.Size;
        Vector2 viewportSize = _hubPreviewViewport.GetVisibleRect().Size;
        if (containerSize.X <= 0.001f || containerSize.Y <= 0.001f || viewportSize.X <= 0.001f || viewportSize.Y <= 0.001f)
        {
            return containerPosition;
        }

        return new Vector2(
            containerPosition.X / containerSize.X * viewportSize.X,
            containerPosition.Y / containerSize.Y * viewportSize.Y);
    }

    private void EnsureHubSocketOverlay()
    {
        if (_hubSocketOverlay != null && IsInstanceValid(_hubSocketOverlay))
        {
            WireHubSocketOverlayDraw();
            EnsureHubSocketStatsPanel();
            EnsureHubInlinePickerPanel();
            EnsureHubSocketMenu();
            return;
        }

        _hubSocketOverlay = new Control
        {
            Name = "HubSocketOverlay",
            MouseFilter = MouseFilterEnum.Pass,
            ZIndex = 30,
        };
        _hubSocketOverlay.GuiInput += OnHubSocketOverlayGuiInput;
        AddChild(_hubSocketOverlay);
        SetFullRect(_hubSocketOverlay);
        _hubSocketOverlay.Visible = false;
        _hubSocketOverlay.MoveToFront();
        WireHubSocketOverlayDraw();
        EnsureHubSocketStatsPanel();
        EnsureHubInlinePickerPanel();
        EnsureHubSocketMenu();
    }

    private void EnsureHubSocketStatsPanel()
    {
        if (_hubSocketOverlay == null)
        {
            return;
        }

        if (_hubSocketStatsPanel != null && IsInstanceValid(_hubSocketStatsPanel))
        {
            return;
        }

        _hubSocketStatsPanel = new PanelContainer
        {
            Name = "HubSocketStatsPanel",
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
            ZIndex = 31,
        };
        _hubSocketOverlay.AddChild(_hubSocketStatsPanel);

        MarginContainer margin = new()
        {
            Name = "Margin",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _hubSocketStatsPanel.AddChild(margin);

        VBoxContainer content = new()
        {
            Name = "Content",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);

        _hubSocketStatsTitleLabel = new Label
        {
            Name = "Title",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _hubSocketStatsTitleLabel.AddThemeFontSizeOverride("font_size", 17);
        content.AddChild(_hubSocketStatsTitleLabel);

        _hubSocketStatsMetaLabel = new Label
        {
            Name = "Meta",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _hubSocketStatsMetaLabel.AddThemeFontSizeOverride("font_size", 11);
        _hubSocketStatsMetaLabel.AddThemeColorOverride("font_color", new Color(0.73f, 0.80f, 0.88f, 0.92f));
        content.AddChild(_hubSocketStatsMetaLabel);

        _hubSocketStatsScroll = new ScrollContainer
        {
            Name = "Scroll",
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        content.AddChild(_hubSocketStatsScroll);

        _hubSocketStatsContent = new VBoxContainer
        {
            Name = "Body",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _hubSocketStatsContent.AddThemeConstantOverride("separation", 10);
        _hubSocketStatsScroll.AddChild(_hubSocketStatsContent);
    }

    private void EnsureHubInlinePickerPanel()
    {
        if (_hubSocketOverlay == null)
        {
            return;
        }

        if (_hubInlinePickerPanel != null && IsInstanceValid(_hubInlinePickerPanel))
        {
            return;
        }

        _hubInlinePickerPanel = new PanelContainer
        {
            Name = "HubInlinePickerPanel",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false,
            ZIndex = 32,
        };
        _hubSocketOverlay.AddChild(_hubInlinePickerPanel);

        MarginContainer margin = new()
        {
            Name = "Margin",
            MouseFilter = MouseFilterEnum.Pass,
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _hubInlinePickerPanel.AddChild(margin);

        VBoxContainer content = new()
        {
            Name = "Content",
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);

        _hubInlinePickerTitleLabel = new Label
        {
            Name = "Title",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _hubInlinePickerTitleLabel.AddThemeFontSizeOverride("font_size", 14);
        content.AddChild(_hubInlinePickerTitleLabel);

        _hubInlinePickerSubtitleLabel = new Label
        {
            Name = "Subtitle",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _hubInlinePickerSubtitleLabel.AddThemeFontSizeOverride("font_size", 11);
        content.AddChild(_hubInlinePickerSubtitleLabel);

        _hubInlinePickerScroll = new ScrollContainer
        {
            Name = "Scroll",
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        content.AddChild(_hubInlinePickerScroll);

        _hubInlinePickerList = new VBoxContainer
        {
            Name = "List",
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _hubInlinePickerList.AddThemeConstantOverride("separation", 6);
        _hubInlinePickerScroll.AddChild(_hubInlinePickerList);
    }

    private void WireHubSocketOverlayDraw()
    {
        if (_hubSocketOverlay == null || _hubSocketOverlayDrawSubscribed)
        {
            return;
        }

        _hubSocketOverlay.Draw += OnHubSocketOverlayDraw;
        _hubSocketOverlayDrawSubscribed = true;
    }

    private void EnsureHubSocketMenu()
    {
        if (_hubSocketMenu != null && IsInstanceValid(_hubSocketMenu))
        {
            return;
        }

        _hubSocketMenu = new PopupMenu
        {
            Name = "HubSocketMenu",
        };
        _hubSocketMenu.IdPressed += OnHubSocketMenuIdPressed;
        _hubSocketMenu.PopupHide += OnHubSocketMenuPopupHide;
        AddChild(_hubSocketMenu);
    }

    private void RefreshHubSocketOverlay()
    {
        EnsureHubSocketOverlay();
        ResolveHubPreviewReferences();

        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (_hubSocketOverlay == null || hubPreview == null || !hubPreview.IsWeaponInspectionActive || !HubVisible || ArenaActive)
        {
            HideHubSocketButtons();
            return;
        }

        if (!hubPreview.GetInspectionSocketAnchors(_hubSocketAnchors) || _hubSocketAnchors.Count == 0)
        {
            HideHubSocketButtons();
            return;
        }

        EquipmentItem? inspectedItem = ResolveInspectedHubItem();
        EquipmentItem? previewItem = ResolveHubInlinePickerPreviewItem(inspectedItem);
        EquipmentItem? displayItem = previewItem ?? inspectedItem;

        _hubSocketOverlay.Visible = true;
        _hubSocketOverlay.MouseFilter = MouseFilterEnum.Pass;
        BuildHubSocketOverlayLayout();
        UpdateHubSocketStatsPanel(inspectedItem, previewItem);
        RefreshHubInlinePickerPanel();

        for (int anchorIndex = 0; anchorIndex < _hubSocketAnchors.Count; anchorIndex++)
        {
            HubSocketAnchor anchor = _hubSocketAnchors[anchorIndex];
            Button button = GetOrCreateHubSocketButton(anchorIndex);
            Vector2 position = _hubSocketLayoutPositions[anchorIndex];
            bool hideForInlinePicker = _activeHubInlinePickerKind == HubInlinePickerKind.Modulator
                && anchor.Kind == HubSocketKind.Modulator
                && anchorIndex != _activeHubInlinePickerButtonIndex;

            if (_activeHubInlinePickerKind == HubInlinePickerKind.Essence && anchor.Kind == HubSocketKind.Essence)
            {
                hideForInlinePicker = true;
            }

            if (hideForInlinePicker)
            {
                button.Visible = false;
                continue;
            }

            button.Visible = true;
            button.Text = string.Empty;
            button.TooltipText = anchor.Tooltip;
            button.Position = position;
            button.Size = _hubSocketCardSize;
            button.CustomMinimumSize = _hubSocketCardSize;
            button.Disabled = false;
            ApplyHubSocketButtonContent(button, anchor, anchorIndex);
            ApplyHubSocketButtonStyle(button, anchor);
        }

        for (int buttonIndex = _hubSocketAnchors.Count; buttonIndex < _hubSocketButtons.Count; buttonIndex++)
        {
            _hubSocketButtons[buttonIndex].Visible = false;
        }

        _hubSocketOverlay.QueueRedraw();
    }

    private void TickInspectionPointerDrag()
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        bool canDrag = hubPreview != null && hubPreview.IsWeaponInspectionActive && HubVisible && !ArenaActive;
        bool pointerDown = canDrag && Input.IsMouseButtonPressed(MouseButton.Left);
        Vector2 pointerPosition = GetViewport().GetMousePosition();

        if (!pointerDown)
        {
            _inspectionPointerWasDown = false;
            _inspectionPointerDragActive = false;
            _inspectionPointerLastPosition = pointerPosition;
            return;
        }

        if (!_inspectionPointerWasDown)
        {
            _inspectionPointerWasDown = true;
            _inspectionPointerLastPosition = pointerPosition;
            return;
        }

        if (!_inspectionPointerDragActive)
        {
            _inspectionPointerLastPosition = pointerPosition;
            return;
        }

        Vector2 delta = pointerPosition - _inspectionPointerLastPosition;
        _inspectionPointerLastPosition = pointerPosition;

        if (delta.LengthSquared() <= 0.01f)
        {
            return;
        }

        hubPreview?.RotateWeaponInspection(delta);
        if (_activeHubSocketButtonIndex >= 0)
        {
            _activeHubSocketButtonDragDistance += delta.Length();
            if (_activeHubSocketButtonDragDistance >= HubSocketDragThresholdPixels)
            {
                _activeHubSocketButtonDragging = true;
            }
        }
    }

    private bool TryBeginInspectionPointerDrag(HubSceneController hubPreview)
    {
        if (_hubPreviewContainer == null || !hubPreview.IsWeaponInspectionActive)
        {
            return false;
        }

        _inspectionPointerDragActive = true;
        _inspectionPointerWasDown = true;
        _inspectionPointerLastPosition = GetViewport().GetMousePosition();
        return true;
    }

    private void HideHubSocketButtons()
    {
        if (_hubSocketOverlay != null)
        {
            _hubSocketOverlay.Visible = false;
            _hubSocketOverlay.MouseFilter = MouseFilterEnum.Ignore;
            _hubSocketOverlay.QueueRedraw();
        }

        if (_hubSocketStatsPanel != null)
        {
            _hubSocketStatsPanel.Visible = false;
        }

        _hubSocketStatsRenderSignature = null;
        _inspectionPointerDragActive = false;

        if (_hubInlinePickerPanel != null)
        {
            _hubInlinePickerPanel.Modulate = Colors.Transparent;
            _hubInlinePickerPanel.Visible = false;
        }

        _hubSocketMenu?.Hide();

        _hoveredHubSocketButtonIndex = -1;
        _selectedHubSocketButtonIndex = -1;
        _animatedHubSocketButtonIndex = -1;
        _hubSocketTraceReveal = 0f;
        _hubSocketTraceVisibility = 0f;
        _activeHubInlinePickerKind = HubInlinePickerKind.None;
        _activeHubInlinePickerButtonIndex = -1;
        _hubInlinePickerDirty = true;
        _hubInlinePickerAnimatedRect = default;
        _hubInlinePickerRestRect = default;
        _hubInlinePickerOpenAmount = 0f;
        _hubInlinePickerRestUsesBottomSheet = false;
        _hubInlinePickerHoverActionId = -1;
        _hubInlinePickerHoverPreviewSummary = null;
        _hubSocketAnchors.Clear();
        _hubSocketLayoutPositions.Clear();
        foreach (Button button in _hubSocketButtons)
        {
            button.Visible = false;
        }
    }

    private Button GetOrCreateHubSocketButton(int buttonIndex)
    {
        EnsureHubSocketOverlay();
        while (_hubSocketButtons.Count <= buttonIndex)
        {
            int capturedIndex = _hubSocketButtons.Count;
            Button button = new()
            {
                Name = $"HubSocketButton{capturedIndex + 1:00}",
                FocusMode = FocusModeEnum.None,
                MouseFilter = MouseFilterEnum.Stop,
                ZIndex = 31,
            };
            button.GuiInput += @event => OnHubSocketButtonGuiInput(capturedIndex, button, @event);
            button.MouseEntered += () => OnHubSocketButtonMouseEntered(capturedIndex);
            button.MouseExited += () => OnHubSocketButtonMouseExited(capturedIndex);
            button.AddThemeFontSizeOverride("font_size", 13);
            button.AddThemeConstantOverride("outline_size", 1);
            _hubSocketOverlay?.AddChild(button);
            EnsureHubSocketButtonContent(button);
            _hubSocketButtons.Add(button);
        }

        return _hubSocketButtons[buttonIndex];
    }

    private static void EnsureHubSocketButtonContent(Button button)
    {
        ColorRect accent = button.GetNodeOrNull<ColorRect>("Accent") ?? new ColorRect
        {
            Name = "Accent",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (accent.GetParent() != button)
        {
            button.AddChild(accent);
        }

        accent.AnchorLeft = 0f;
        accent.AnchorTop = 0f;
        accent.AnchorRight = 0f;
        accent.AnchorBottom = 1f;
        accent.OffsetLeft = 0f;
        accent.OffsetTop = 0f;
        accent.OffsetRight = HubSocketCardAccentWidth;
        accent.OffsetBottom = 0f;

        VBoxContainer textStack = button.GetNodeOrNull<VBoxContainer>("TextStack") ?? new VBoxContainer
        {
            Name = "TextStack",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        if (textStack.GetParent() != button)
        {
            button.AddChild(textStack);
        }

        textStack.AnchorLeft = 0f;
        textStack.AnchorTop = 0f;
        textStack.AnchorRight = 1f;
        textStack.AnchorBottom = 1f;
        textStack.OffsetLeft = HubSocketCardAccentWidth + HubSocketCardTextInset;
        textStack.OffsetTop = 10f;
        textStack.OffsetRight = -HubSocketCardTextInset;
        textStack.OffsetBottom = -10f;
        textStack.AddThemeConstantOverride("separation", 3);

        Label title = textStack.GetNodeOrNull<Label>("Title") ?? new Label
        {
            Name = "Title",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        if (title.GetParent() != textStack)
        {
            textStack.AddChild(title);
        }

        title.AddThemeFontSizeOverride("font_size", 13);

        Label summary = textStack.GetNodeOrNull<Label>("Summary") ?? new Label
        {
            Name = "Summary",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        if (summary.GetParent() != textStack)
        {
            textStack.AddChild(summary);
        }

        summary.AddThemeFontSizeOverride("font_size", 11);
    }

    private void BuildHubSocketOverlayLayout()
    {
        _hubSocketLayoutPositions.Clear();

        if (_hubSocketAnchors.Count == 0)
        {
            return;
        }

        int modulatorCount = 0;
        int essenceCount = 0;
        bool modulatorPickerOpen = _activeHubInlinePickerKind == HubInlinePickerKind.Modulator
            && _activeHubInlinePickerButtonIndex >= 0;
        bool essencePickerOpen = _activeHubInlinePickerKind == HubInlinePickerKind.Essence
            && _activeHubInlinePickerButtonIndex >= 0;
        for (int anchorIndex = 0; anchorIndex < _hubSocketAnchors.Count; anchorIndex++)
        {
            if (_hubSocketAnchors[anchorIndex].Kind == HubSocketKind.Essence)
            {
                essenceCount++;
            }
            else
            {
                modulatorCount++;
            }
        }

        int visibleModulatorCount = modulatorPickerOpen ? 1 : modulatorCount;
        _hubSocketCardSize = ResolveHubSocketCardSize(Math.Max(1, visibleModulatorCount));
    _hubSocketStatsPanelSize = ResolveHubSocketStatsPanelSize();

        Rect2 rightLaneRect = GetHubSocketRightLaneRect();
        Rect2 essencePickerRect = essencePickerOpen ? GetHubEssenceInlinePickerBottomRect() : default;
        Rect2 essenceDockRect = GetHubEssenceSocketDockRect(essenceCount, essencePickerOpen, essencePickerRect);

        float rightGap = ResolveHubSocketCardGap(visibleModulatorCount, rightLaneRect.Size.Y);
        float modulatorStartY = rightLaneRect.Position.Y;
        if (!modulatorPickerOpen && modulatorCount > 0)
        {
            float totalRightHeight = modulatorCount * _hubSocketCardSize.Y + Math.Max(0, modulatorCount - 1) * rightGap;
            modulatorStartY = rightLaneRect.Position.Y + Mathf.Max(0f, (rightLaneRect.Size.Y - totalRightHeight) * 0.5f);
        }

        float essenceGap = HubSocketCardGap;

        int modulatorIndex = 0;
        int essenceIndex = 0;
        for (int anchorIndex = 0; anchorIndex < _hubSocketAnchors.Count; anchorIndex++)
        {
            HubSocketAnchor anchor = _hubSocketAnchors[anchorIndex];
            if (anchor.Kind == HubSocketKind.Essence)
            {
                _hubSocketLayoutPositions.Add(new Vector2(
                    essenceDockRect.Position.X + essenceIndex * (_hubSocketCardSize.X + essenceGap),
                    essenceDockRect.Position.Y));
                essenceIndex++;
                continue;
            }

            if (modulatorPickerOpen && anchorIndex != _activeHubInlinePickerButtonIndex)
            {
                _hubSocketLayoutPositions.Add(new Vector2(
                    rightLaneRect.Position.X,
                    modulatorStartY));
                continue;
            }

            _hubSocketLayoutPositions.Add(new Vector2(
                rightLaneRect.Position.X,
                modulatorStartY + modulatorIndex * (_hubSocketCardSize.Y + rightGap)));
            modulatorIndex++;
        }
    }

    private Vector2 ResolveHubSocketCardSize(int cardCount)
    {
        float availableHeight = Mathf.Max(1f, (_hubSocketOverlay?.Size.Y ?? Size.Y) - HubSocketTopReserve - HubSocketBottomReserve);
        float height = HubSocketCardMaxHeight;
        if (cardCount > 0)
        {
            height = Mathf.Clamp((availableHeight - Mathf.Max(0, cardCount - 1) * 6f) / cardCount, HubSocketCardMinHeight, HubSocketCardMaxHeight);
        }

        return new Vector2(HubSocketCardWidth, height);
    }

    private Vector2 ResolveHubSocketStatsPanelSize()
    {
        Vector2 overlaySize = _hubSocketOverlay?.Size ?? Size;
        float top = Mathf.Max(HubSocketScreenMargin, HubSocketTopReserve);
        float width = Mathf.Min(HubSocketStatsPanelWidth, Mathf.Max(HubSocketCardWidth + 128f, overlaySize.X * 0.38f));
        float height = ResolveHubSocketStatsPanelAvailableHeight(overlaySize, top);
        return new Vector2(width, height);
    }

    private float ResolveHubSocketStatsPanelAvailableHeight(Vector2 overlaySize, float top)
    {
        float bottom = Mathf.Max(top + HubSocketStatsPanelMinHeight, overlaySize.Y - Mathf.Min(HubSocketBottomReserve, overlaySize.Y * 0.30f));
        float reserveBelow = HubSocketStatsPanelCardReserveHeight;
        return Mathf.Max(HubSocketStatsPanelMinHeight, bottom - top - reserveBelow);
    }

    private static float ResolveHubSocketCardGap(int cardCount, float availableHeight)
    {
        if (cardCount <= 1)
        {
            return 0f;
        }

        float remainingHeight = availableHeight - cardCount * HubSocketCardMinHeight;
        if (remainingHeight <= 0f)
        {
            return 4f;
        }

        return Mathf.Clamp(remainingHeight / (cardCount - 1), 4f, HubSocketCardGap);
    }

    private Vector2 ToHubSocketProjectedOverlayPosition(Vector2 viewportPosition)
    {
        if (_hubPreviewContainer == null || _hubPreviewViewport == null || _hubSocketOverlay == null)
        {
            return viewportPosition;
        }

        Vector2 viewportSize = _hubPreviewViewport.GetVisibleRect().Size;
        Vector2 containerSize = _hubPreviewContainer.Size;
        if (viewportSize.X <= 0.001f || viewportSize.Y <= 0.001f || containerSize.X <= 0.001f || containerSize.Y <= 0.001f)
        {
            return viewportPosition;
        }

        Vector2 containerLocalPosition = new(
            viewportPosition.X / viewportSize.X * containerSize.X,
            viewportPosition.Y / viewportSize.Y * containerSize.Y);
        Vector2 containerGlobalPosition = _hubPreviewContainer.GetGlobalRect().Position;
        Vector2 overlayGlobalPosition = _hubSocketOverlay.GetGlobalRect().Position;
        return containerGlobalPosition - overlayGlobalPosition + containerLocalPosition;
    }

    private Rect2 GetHubSocketRightLaneRect()
    {
        Vector2 overlaySize = _hubSocketOverlay?.Size ?? Size;
        float width = Mathf.Min(HubSocketCardWidth, Mathf.Max(180f, overlaySize.X - HubSocketScreenMargin * 2f));
        float left = Mathf.Max(HubSocketScreenMargin, overlaySize.X - HubSocketScreenMargin - width);
        float top = Mathf.Max(HubSocketScreenMargin, HubSocketTopReserve);
        float bottom = Mathf.Max(top + _hubSocketCardSize.Y, overlaySize.Y - Mathf.Min(HubSocketBottomReserve, overlaySize.Y * 0.30f));

        return new Rect2(new Vector2(left, top), new Vector2(width, Mathf.Max(1f, bottom - top)));
    }

    private Rect2 GetHubSocketStatsPanelRect()
    {
        Vector2 overlaySize = _hubSocketOverlay?.Size ?? Size;
        float width = Mathf.Min(_hubSocketStatsPanelSize.X, Mathf.Max(HubSocketCardWidth, overlaySize.X - HubSocketScreenMargin * 2f));
        float left = HubSocketScreenMargin;
        float top = Mathf.Max(HubSocketScreenMargin, HubSocketTopReserve);
        return new Rect2(new Vector2(left, top), _hubSocketStatsPanelSize with { X = width });
    }

    private Rect2 GetHubSocketLeftLaneRect()
    {
        Rect2 statsRect = GetHubSocketStatsPanelRect();
        Vector2 overlaySize = _hubSocketOverlay?.Size ?? Size;
        float leftHalfWidth = Mathf.Max(HubSocketCardWidth, overlaySize.X * 0.5f - HubSocketScreenMargin * 2f);
        float top = statsRect.End.Y + HubSocketStatsPanelGap;
        float bottom = Mathf.Max(top + _hubSocketCardSize.Y, overlaySize.Y - Mathf.Min(HubSocketBottomReserve, overlaySize.Y * 0.30f));
        return new Rect2(new Vector2(HubSocketScreenMargin, top), new Vector2(leftHalfWidth, Mathf.Max(1f, bottom - top)));
    }

    private Rect2 GetHubInlinePickerPanelRect()
    {
        Rect2 fallback = new(new Vector2(HubSocketScreenMargin, HubSocketTopReserve), new Vector2(HubSocketCardWidth, 220f));
        if (_activeHubInlinePickerButtonIndex < 0 || _activeHubInlinePickerButtonIndex >= _hubSocketLayoutPositions.Count)
        {
            return fallback;
        }

        Vector2 cardPosition = _hubSocketLayoutPositions[_activeHubInlinePickerButtonIndex];
        switch (_activeHubInlinePickerKind)
        {
            case HubInlinePickerKind.Essence:
                return GetHubEssenceInlinePickerBottomRect();
            case HubInlinePickerKind.Modulator:
            {
                Rect2 rightLaneRect = GetHubSocketRightLaneRect();
                float top = cardPosition.Y + _hubSocketCardSize.Y + HubSocketCardGap;
                float height = Mathf.Max(160f, rightLaneRect.End.Y - top);
                return new Rect2(new Vector2(rightLaneRect.Position.X, top), new Vector2(rightLaneRect.Size.X, height));
            }
            default:
                return fallback;
        }
    }

    private Rect2 GetHubEssenceInlinePickerBottomRect()
    {
        Vector2 overlaySize = _hubSocketOverlay?.Size ?? Size;
        Rect2 statsRect = GetHubSocketStatsPanelRect();
        float width = Mathf.Clamp(
            Mathf.Max(statsRect.Size.X + 48f, HubEssencePickerBottomMinWidth),
            HubEssencePickerBottomMinWidth,
            Mathf.Min(HubEssencePickerBottomMaxWidth, Mathf.Max(HubEssencePickerBottomMinWidth, overlaySize.X - HubSocketScreenMargin * 2f)));
            float maxHeight = Mathf.Max(96f, overlaySize.Y - HubSocketScreenMargin * 2f);
            float resolvedHeight = Mathf.Clamp(overlaySize.Y * 0.28f, 96f, Mathf.Min(HubEssencePickerBottomMaxHeight, maxHeight));
            float top = Mathf.Max(HubSocketTopReserve, overlaySize.Y - HubSocketScreenMargin - resolvedHeight);
        float left = Mathf.Max(HubSocketScreenMargin, (overlaySize.X - width) * 0.5f);
        return new Rect2(new Vector2(left, top), new Vector2(width, resolvedHeight));
    }

    private Rect2 GetHubEssenceSocketDockRect(int essenceCount, bool essencePickerOpen, Rect2 essencePickerRect)
    {
        Vector2 overlaySize = _hubSocketOverlay?.Size ?? Size;
        float totalWidth = Mathf.Max(
            _hubSocketCardSize.X,
            essenceCount * _hubSocketCardSize.X + Math.Max(0, essenceCount - 1) * HubSocketCardGap);
        float left = Mathf.Max(HubSocketScreenMargin, (overlaySize.X - totalWidth) * 0.5f);
        float top = essencePickerOpen
            ? essencePickerRect.Position.Y - _hubSocketCardSize.Y - HubEssencePickerDockGap
            : Mathf.Max(HubSocketTopReserve, overlaySize.Y - HubSocketScreenMargin - _hubSocketCardSize.Y);
        return new Rect2(new Vector2(left, top), new Vector2(totalWidth, _hubSocketCardSize.Y));
    }

    private Rect2 GetHubInlinePickerCollapsedRect()
    {
        Rect2 basis = _hubInlinePickerRestRect.Size.X > 0.001f
            ? _hubInlinePickerRestRect
            : new Rect2(new Vector2(HubSocketScreenMargin, HubSocketTopReserve), new Vector2(HubSocketCardWidth, 0f));

        if (_hubInlinePickerRestUsesBottomSheet)
        {
            return new Rect2(
                new Vector2(basis.Position.X, basis.End.Y + HubInlinePickerBottomSheetHiddenOffset),
                new Vector2(Mathf.Max(1f, basis.Size.X), 0f));
        }

        return new Rect2(basis.Position, new Vector2(Mathf.Max(1f, basis.Size.X), 0f));
    }

    private void TickHubSocketTraceAnimation(float deltaSeconds)
    {
        int targetButtonIndex = ResolveActiveHubSocketTraceButtonIndex();
        if (targetButtonIndex != _animatedHubSocketButtonIndex)
        {
            _animatedHubSocketButtonIndex = targetButtonIndex;
            _hubSocketTraceReveal = targetButtonIndex >= 0 ? 0f : _hubSocketTraceReveal;
        }

        float targetVisibility = _animatedHubSocketButtonIndex >= 0 ? 1f : 0f;
        float visibilitySpeed = targetVisibility > _hubSocketTraceVisibility ? HubSocketTraceRevealSpeed : HubSocketTraceFadeSpeed;
        _hubSocketTraceVisibility = Mathf.MoveToward(_hubSocketTraceVisibility, targetVisibility, deltaSeconds * visibilitySpeed);

        if (_animatedHubSocketButtonIndex >= 0)
        {
            _hubSocketTraceReveal = Mathf.MoveToward(_hubSocketTraceReveal, 1f, deltaSeconds * HubSocketTraceRevealSpeed);
        }
        else
        {
            _hubSocketTraceReveal = Mathf.MoveToward(_hubSocketTraceReveal, 0f, deltaSeconds * HubSocketTraceFadeSpeed);
        }

        if (_hubSocketOverlay != null && _hubSocketOverlay.Visible && _hubSocketTraceVisibility > 0.001f)
        {
            _hubSocketOverlay.QueueRedraw();
        }
    }

    private int ResolveActiveHubSocketTraceButtonIndex()
    {
        if (_activeHubInlinePickerKind == HubInlinePickerKind.Essence)
        {
            return -1;
        }

        if (_hoveredHubSocketButtonIndex >= 0 && _hoveredHubSocketButtonIndex < _hubSocketAnchors.Count)
        {
            return _hoveredHubSocketButtonIndex;
        }

        if (_selectedHubSocketButtonIndex >= 0 && _selectedHubSocketButtonIndex < _hubSocketAnchors.Count)
        {
            return _selectedHubSocketButtonIndex;
        }

        return -1;
    }

    private EquipmentItem? ResolveInspectedHubItem()
    {
        if (_inventoryManager == null || _hubSocketAnchors.Count == 0)
        {
            return null;
        }

        return _inventoryManager.GetEquippedItemAt(_hubSocketAnchors[0].WeaponSlotIndex);
    }

    private void RefreshHubInlinePickerPanel()
    {
        EnsureHubInlinePickerPanel();
        if (_hubInlinePickerPanel == null || _hubInlinePickerTitleLabel == null || _hubInlinePickerSubtitleLabel == null || _hubInlinePickerList == null)
        {
            return;
        }

        if (_activeHubInlinePickerKind == HubInlinePickerKind.None
            || _inventoryManager == null
            || _activeHubInlinePickerButtonIndex < 0
            || _activeHubInlinePickerButtonIndex >= _hubSocketAnchors.Count)
        {
            return;
        }

        HubSocketAnchor anchor = _hubSocketAnchors[_activeHubInlinePickerButtonIndex];
        EquipmentItem? item = _inventoryManager.GetEquippedItemAt(anchor.WeaponSlotIndex);
        if (item == null)
        {
            CloseHubInlinePicker();
            return;
        }

        if ((_activeHubInlinePickerKind == HubInlinePickerKind.Essence && anchor.Kind != HubSocketKind.Essence)
            || (_activeHubInlinePickerKind == HubInlinePickerKind.Modulator && anchor.Kind != HubSocketKind.Modulator))
        {
            CloseHubInlinePicker();
            return;
        }

        Rect2 panelRect = GetHubInlinePickerPanelRect();
        _hubInlinePickerRestRect = panelRect;
        _hubInlinePickerRestUsesBottomSheet = _activeHubInlinePickerKind == HubInlinePickerKind.Essence;

        if (_hubInlinePickerDirty)
        {
            RebuildHubInlinePickerPanel(item, anchor);
            _hubInlinePickerDirty = false;
        }
    }

    private void TickHubInlinePickerAnimation(float deltaSeconds)
    {
        EnsureHubInlinePickerPanel();
        if (_hubInlinePickerPanel == null)
        {
            return;
        }

        bool hasActiveTarget = _hubSocketOverlay != null
            && _hubSocketOverlay.Visible
            && _activeHubInlinePickerKind != HubInlinePickerKind.None
            && _activeHubInlinePickerButtonIndex >= 0
            && _activeHubInlinePickerButtonIndex < _hubSocketLayoutPositions.Count;

        Rect2 targetRect = hasActiveTarget ? GetHubInlinePickerPanelRect() : GetHubInlinePickerCollapsedRect();
        if (hasActiveTarget)
        {
            _hubInlinePickerRestRect = targetRect;
            _hubInlinePickerRestUsesBottomSheet = _activeHubInlinePickerKind == HubInlinePickerKind.Essence;
            if (_hubInlinePickerOpenAmount <= 0.001f && _hubInlinePickerAnimatedRect.Size.Y <= 0.001f)
            {
                _hubInlinePickerAnimatedRect = GetHubInlinePickerCollapsedRect();
            }
        }
        else if (_hubInlinePickerAnimatedRect.Size.X <= 0.001f && _hubInlinePickerRestRect.Size.X <= 0.001f)
        {
            _hubInlinePickerPanel.Visible = false;
            return;
        }

        float rectBlend = 1f - Mathf.Exp(-deltaSeconds * HubInlinePickerMotionSpeed);
        if (_hubInlinePickerAnimatedRect.Size.X <= 0.001f && _hubInlinePickerAnimatedRect.Size.Y <= 0.001f)
        {
            _hubInlinePickerAnimatedRect = targetRect;
        }
        else
        {
            _hubInlinePickerAnimatedRect = LerpHubInlinePickerRect(_hubInlinePickerAnimatedRect, targetRect, rectBlend);
        }

        float targetOpenAmount = hasActiveTarget ? 1f : 0f;
        float openSpeed = hasActiveTarget ? HubInlinePickerRevealSpeed : HubInlinePickerFadeSpeed;
        _hubInlinePickerOpenAmount = Mathf.MoveToward(_hubInlinePickerOpenAmount, targetOpenAmount, deltaSeconds * openSpeed);

        Vector2 displaySize = new(Mathf.Max(1f, _hubInlinePickerAnimatedRect.Size.X), Mathf.Max(0f, _hubInlinePickerAnimatedRect.Size.Y));
        _hubInlinePickerPanel.Position = _hubInlinePickerAnimatedRect.Position;
        _hubInlinePickerPanel.Size = displaySize;
        _hubInlinePickerPanel.CustomMinimumSize = displaySize;

        Color panelModulate = Colors.White;
        panelModulate.A = _hubInlinePickerOpenAmount;
        _hubInlinePickerPanel.Modulate = panelModulate;
        _hubInlinePickerPanel.Visible = hasActiveTarget || _hubInlinePickerOpenAmount > 0.01f || _hubInlinePickerAnimatedRect.Size.Y > 0.5f;
        if (!hasActiveTarget && _hubInlinePickerOpenAmount <= 0.001f)
        {
            _hubInlinePickerPanel.Visible = false;
        }
    }

    private static Rect2 LerpHubInlinePickerRect(Rect2 current, Rect2 target, float weight)
    {
        return new Rect2(current.Position.Lerp(target.Position, weight), current.Size.Lerp(target.Size, weight));
    }

    private void RebuildHubInlinePickerPanel(EquipmentItem item, HubSocketAnchor anchor)
    {
        if (_hubInlinePickerPanel == null || _hubInlinePickerTitleLabel == null || _hubInlinePickerSubtitleLabel == null || _hubInlinePickerList == null)
        {
            return;
        }

        foreach (Node child in _hubInlinePickerList.GetChildren())
        {
            _hubInlinePickerList.RemoveChild(child);
            child.QueueFree();
        }

        Color panelAccent = anchor.Color;
        _hubInlinePickerPanel.AddThemeStyleboxOverride(
            "panel",
            CreateLegacyStyleBox(
                new Color(panelAccent.R * 0.10f, panelAccent.G * 0.10f, panelAccent.B * 0.10f, 0.94f),
                panelAccent.Lightened(0.14f),
                14));

        switch (_activeHubInlinePickerKind)
        {
            case HubInlinePickerKind.Essence:
                BuildHubEssenceInlinePicker(item);
                break;
            case HubInlinePickerKind.Modulator:
                BuildHubModulatorInlinePicker(item, anchor);
                break;
            default:
                _hubInlinePickerTitleLabel.Text = string.Empty;
                _hubInlinePickerSubtitleLabel.Text = string.Empty;
                break;
        }
    }

    private void BuildHubEssenceInlinePicker(EquipmentItem item)
    {
        if (_hubInlinePickerTitleLabel == null || _hubInlinePickerSubtitleLabel == null)
        {
            return;
        }

        _hubInlinePickerTitleLabel.Text = item.HasEquippedEssence && item.Essence != null
            ? $"Essence Editor - {item.Essence.DisplayName}"
            : "Essence Picker";
        _hubInlinePickerSubtitleLabel.Text = item.HasEssenceSlotUnlocked
            ? item.HasEquippedEssence && item.Essence != null
                ? "Choose a replacement or return the current essence to storage."
                : "Choose a stored essence to socket into this weapon."
            : "This weapon has not unlocked its essence slot yet.";

        if (!item.HasEssenceSlotUnlocked)
        {
            AddHubInlinePickerDisabledRow("Essence slot locked", "Level this weapon to unlock essence socketing.");
            return;
        }

        if (item.HasEquippedEssence && item.Essence != null)
        {
            AddHubInlinePickerActionRow(
                HubSocketMenuReturnEssence,
                $"Return {item.Essence.DisplayName}",
                BuildHubEssenceMenuSummary(item.Essence),
                item.Essence.UiColor,
                BuildHubInlinePickerEmptyPreviewSummary("essence"),
                "Current",
                destructive: true);
        }

        List<HubEssenceMenuGroup> groups = BuildHubEssenceMenuGroups(item);
        if (groups.Count == 0)
        {
            AddHubInlinePickerDisabledRow("No stored essences", "There are no compatible stored essences available.");
            return;
        }

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            HubEssenceMenuGroup group = groups[groupIndex];
            string badge = group.Count > 1 ? $"x{group.Count}" : string.Empty;
            AddHubInlinePickerActionRow(
                HubSocketMenuInstallEssenceBase + group.RepresentativeStoredIndex,
                group.Representative.DisplayName,
                BuildHubEssenceMenuSummary(group.Representative),
                group.Representative.UiColor,
                BuildHubInlinePickerPreviewSummary(group.Representative.DisplayName, BuildHubEssenceMenuSummary(group.Representative)),
                badge);
        }
    }

    private void BuildHubModulatorInlinePicker(EquipmentItem item, HubSocketAnchor anchor)
    {
        if (_hubInlinePickerTitleLabel == null || _hubInlinePickerSubtitleLabel == null)
        {
            return;
        }

        bool installed = item.Modulators != null && anchor.SocketIndex >= 0 && anchor.SocketIndex < item.Modulators.Count;
        _hubInlinePickerTitleLabel.Text = installed && item.Modulators != null
            ? $"Slot {anchor.SocketIndex + 1} - {item.Modulators[anchor.SocketIndex].DisplayName}"
            : $"Modulator Slot {anchor.SocketIndex + 1}";
        _hubInlinePickerSubtitleLabel.Text = installed
            ? "Choose a replacement or return the current modulator to storage."
            : "Choose a compatible stored modulator for this slot.";

        if (anchor.SocketIndex < 0 || anchor.SocketIndex >= item.UnlockedModulatorSlots)
        {
            AddHubInlinePickerDisabledRow("Modulator slot locked", "Level this weapon to unlock more modulator slots.");
            return;
        }

        if (installed && item.Modulators != null)
        {
            WeaponModulatorData installedModulator = item.Modulators[anchor.SocketIndex];
            AddHubInlinePickerActionRow(
                HubSocketMenuReturnModulator,
                $"Return {installedModulator.DisplayName}",
                BuildHubModulatorMenuSummary(installedModulator),
                installedModulator.UiColor,
                BuildHubInlinePickerEmptyPreviewSummary("modulator"),
                "Current",
                destructive: true);
        }
        else if (item.AvailableModulatorSlots <= 0)
        {
            AddHubInlinePickerDisabledRow("No open modulator slots", "All unlocked modulator slots are currently filled.");
            return;
        }

        List<HubModulatorMenuGroup> groups = BuildHubModulatorMenuGroups(item, anchor.SocketIndex, installed);
        if (groups.Count == 0)
        {
            AddHubInlinePickerDisabledRow("No compatible modulators", "No stored modulators can be installed into this slot right now.");
            return;
        }

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            HubModulatorMenuGroup group = groups[groupIndex];
            int actionId = (installed ? HubSocketMenuReplaceModulatorBase : HubSocketMenuInstallModulatorBase) + group.RepresentativeStoredIndex;
            string badge = group.Count > 1
                ? $"x{group.Count}"
                : group.Representative.IsWingModulator ? "Wing" : string.Empty;
            AddHubInlinePickerActionRow(
                actionId,
                group.Representative.DisplayName,
                BuildHubModulatorMenuSummary(group.Representative),
                group.Representative.UiColor,
                BuildHubInlinePickerPreviewSummary(group.Representative.DisplayName, BuildHubModulatorMenuSummary(group.Representative)),
                badge);
        }
    }

    private void AddHubInlinePickerActionRow(long actionId, string title, string summary, Color accent, string previewSummary, string badgeText = "", bool destructive = false)
    {
        if (_hubInlinePickerList == null)
        {
            return;
        }

        Button row = CreateHubInlinePickerRow(title, summary, accent, badgeText, destructive, disabled: false);
        row.MouseEntered += () => SetHubInlinePickerHoverPreview(actionId, previewSummary);
        row.MouseExited += ClearHubInlinePickerHoverPreview;
        row.Pressed += () => ExecuteHubInlinePickerAction(actionId);
        _hubInlinePickerList.AddChild(row);
    }

    private void AddHubInlinePickerDisabledRow(string title, string summary)
    {
        if (_hubInlinePickerList == null)
        {
            return;
        }

        Button row = CreateHubInlinePickerRow(title, summary, new Color(0.40f, 0.46f, 0.55f, 1f), string.Empty, destructive: false, disabled: true);
        _hubInlinePickerList.AddChild(row);
    }

    private Button CreateHubInlinePickerRow(string title, string summary, Color accent, string badgeText, bool destructive, bool disabled)
    {
        Button row = new()
        {
            Text = string.Empty,
            FocusMode = FocusModeEnum.None,
            MouseFilter = disabled ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop,
            Disabled = disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, string.IsNullOrWhiteSpace(summary) ? 54f : 68f),
        };

        Color background = destructive
            ? new Color(0.20f, 0.08f, 0.09f, 0.94f)
            : new Color(accent.R * 0.10f, accent.G * 0.10f, accent.B * 0.10f, 0.90f);
        Color border = disabled ? new Color(0.34f, 0.38f, 0.44f, 1f) : accent.Lightened(0.16f);
        row.AddThemeStyleboxOverride("normal", CreateLegacyStyleBox(background, border, 12));
        row.AddThemeStyleboxOverride("hover", CreateLegacyStyleBox(background.Lightened(0.08f), border.Lightened(0.12f), 12));
        row.AddThemeStyleboxOverride("pressed", CreateLegacyStyleBox(background.Darkened(0.06f), border.Lightened(0.08f), 12));
        row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        ColorRect accentBar = new()
        {
            Name = "Accent",
            Color = disabled ? new Color(0.34f, 0.38f, 0.44f, 1f) : border,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        accentBar.AnchorLeft = 0f;
        accentBar.AnchorTop = 0f;
        accentBar.AnchorRight = 0f;
        accentBar.AnchorBottom = 1f;
        accentBar.OffsetRight = 4f;
        row.AddChild(accentBar);

        MarginContainer margin = new()
        {
            Name = "Margin",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        margin.AnchorLeft = 0f;
        margin.AnchorTop = 0f;
        margin.AnchorRight = 1f;
        margin.AnchorBottom = 1f;
        margin.OffsetLeft = 12f;
        margin.OffsetTop = 8f;
        margin.OffsetRight = -12f;
        margin.OffsetBottom = -8f;
        row.AddChild(margin);

        VBoxContainer content = new()
        {
            Name = "Content",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 3);
        margin.AddChild(content);

        HBoxContainer header = new()
        {
            Name = "Header",
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        header.AddThemeConstantOverride("separation", 6);
        content.AddChild(header);

        Label titleLabel = new()
        {
            Name = "Title",
            Text = title,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.AddThemeColorOverride("font_color", disabled ? new Color(0.66f, 0.70f, 0.76f, 1f) : Colors.White);
        header.AddChild(titleLabel);

        if (!string.IsNullOrWhiteSpace(badgeText))
        {
            Label badgeLabel = new()
            {
                Name = "Badge",
                Text = badgeText,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            badgeLabel.AddThemeFontSizeOverride("font_size", 10);
            badgeLabel.AddThemeColorOverride("font_color", disabled ? new Color(0.58f, 0.63f, 0.70f, 1f) : accent.Lightened(0.20f));
            header.AddChild(badgeLabel);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            Label summaryLabel = new()
            {
                Name = "Summary",
                Text = summary,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Off,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            summaryLabel.AddThemeFontSizeOverride("font_size", 10);
            summaryLabel.AddThemeColorOverride("font_color", disabled ? new Color(0.56f, 0.61f, 0.68f, 0.95f) : new Color(0.80f, 0.86f, 0.93f, 0.95f));
            content.AddChild(summaryLabel);
        }

        return row;
    }

    private void ExecuteHubInlinePickerAction(long actionId)
    {
        ClearHubInlinePickerHoverPreview();
        OnHubSocketMenuIdPressed(actionId);
        _hubInlinePickerDirty = true;
    }

    private void OpenHubInlinePicker(int buttonIndex, HubSocketAnchor anchor, EquipmentItem item)
    {
        HubInlinePickerKind kind = anchor.Kind == HubSocketKind.Essence ? HubInlinePickerKind.Essence : HubInlinePickerKind.Modulator;
        if (_activeHubInlinePickerKind == kind && _activeHubInlinePickerButtonIndex == buttonIndex)
        {
            CloseHubInlinePicker();
            return;
        }

        ClearHubInlinePickerHoverPreview();
        _hubSocketMenu?.Hide();
        _hubSocketMenuAnchor = anchor;
        _hubSocketMenuItemInstanceId = item.InstanceId;
        _activeHubInlinePickerKind = kind;
        _activeHubInlinePickerButtonIndex = buttonIndex;
        _selectedHubSocketButtonIndex = buttonIndex;
        _hubInlinePickerDirty = true;
        _hubSocketOverlay?.QueueRedraw();
    }

    private bool HandleHubInlinePickerInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent
            && keyEvent.Pressed
            && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            ulong now = Time.GetTicksMsec();
            if (_hubInlinePickerEscapeConsumeUntilMsec > now)
            {
                return true;
            }

            if (_activeHubInlinePickerKind == HubInlinePickerKind.None)
            {
                return false;
            }

            CloseHubInlinePicker();
            _hubInlinePickerEscapeConsumeUntilMsec = now + 50;
            _hubSocketOverlay?.QueueRedraw();
            return true;
        }

        if (_activeHubInlinePickerKind == HubInlinePickerKind.None)
        {
            return false;
        }

        return false;
    }

    private void CloseHubInlinePicker(bool clearSelected = true)
    {
        ClearHubInlinePickerHoverPreview();
        _activeHubInlinePickerKind = HubInlinePickerKind.None;
        _activeHubInlinePickerButtonIndex = -1;
        _hubInlinePickerDirty = true;

        if (clearSelected)
        {
            _selectedHubSocketButtonIndex = -1;
        }
    }

    private void SetHubInlinePickerHoverPreview(long actionId, string previewSummary)
    {
        _hubInlinePickerHoverActionId = actionId;
        _hubInlinePickerHoverPreviewSummary = string.IsNullOrWhiteSpace(previewSummary)
            ? null
            : previewSummary;
    }

    private void ClearHubInlinePickerHoverPreview()
    {
        _hubInlinePickerHoverActionId = -1;
        _hubInlinePickerHoverPreviewSummary = null;
    }

    private static string BuildHubInlinePickerPreviewSummary(string title, string summary)
    {
        string normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? "Candidate"
            : title.Trim();
        string normalizedSummary = string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : summary.Replace('\r', ' ').Replace('\n', ' ').Trim();

        return string.IsNullOrWhiteSpace(normalizedSummary)
            ? $"Preview: {normalizedTitle}"
            : $"Preview: {normalizedTitle} | {normalizedSummary}";
    }

    private static string BuildHubInlinePickerEmptyPreviewSummary(string socketType)
    {
        return $"Preview: Empty {socketType} slot";
    }

    private EquipmentItem? ResolveHubInlinePickerPreviewItem(EquipmentItem? item)
    {
        if (item == null || _inventoryManager == null || _hubInlinePickerHoverActionId < 0)
        {
            return null;
        }

        if (!string.Equals(item.InstanceId, _hubSocketMenuItemInstanceId, StringComparison.Ordinal))
        {
            return null;
        }

        EquipmentItem previewItem = CloneHubPreviewItem(item);
        if (!TryApplyHubInlinePreviewAction(previewItem, _hubInlinePickerHoverActionId))
        {
            return null;
        }

        StatGenerator.RecalculateDerivedStats(previewItem);
        return previewItem;
    }

    private bool TryApplyHubInlinePreviewAction(EquipmentItem previewItem, long actionId)
    {
        if (_inventoryManager == null)
        {
            return false;
        }

        if (actionId == HubSocketMenuReturnEssence)
        {
            previewItem.Essence = null;
            return true;
        }

        if (actionId == HubSocketMenuReturnModulator)
        {
            int socketIndex = _hubSocketMenuAnchor.SocketIndex;
            if (socketIndex < 0 || socketIndex >= previewItem.Modulators.Count)
            {
                return false;
            }

            previewItem.Modulators.RemoveAt(socketIndex);
            return true;
        }

        if (actionId >= HubSocketMenuInstallEssenceBase && actionId < HubSocketMenuInstallModulatorBase)
        {
            int storedEssenceIndex = (int)(actionId - HubSocketMenuInstallEssenceBase);
            if (storedEssenceIndex < 0 || storedEssenceIndex >= _inventoryManager.StoredEssences.Count)
            {
                return false;
            }

            previewItem.Essence = _inventoryManager.StoredEssences[storedEssenceIndex];
            return true;
        }

        if (actionId >= HubSocketMenuInstallModulatorBase && actionId < HubSocketMenuReplaceModulatorBase)
        {
            int storedModulatorIndex = (int)(actionId - HubSocketMenuInstallModulatorBase);
            if (storedModulatorIndex < 0 || storedModulatorIndex >= _inventoryManager.StoredModulators.Count)
            {
                return false;
            }

            previewItem.Modulators.Add(CloneHubPreviewModulator(_inventoryManager.StoredModulators[storedModulatorIndex]));
            return true;
        }

        if (actionId >= HubSocketMenuReplaceModulatorBase)
        {
            int storedModulatorIndex = (int)(actionId - HubSocketMenuReplaceModulatorBase);
            int socketIndex = _hubSocketMenuAnchor.SocketIndex;
            if (storedModulatorIndex < 0 || storedModulatorIndex >= _inventoryManager.StoredModulators.Count
                || socketIndex < 0 || socketIndex >= previewItem.Modulators.Count)
            {
                return false;
            }

            previewItem.Modulators[socketIndex] = CloneHubPreviewModulator(_inventoryManager.StoredModulators[storedModulatorIndex]);
            return true;
        }

        return false;
    }

    private static EquipmentItem CloneHubPreviewItem(EquipmentItem item)
    {
        return new EquipmentItem
        {
            InstanceId = item.InstanceId,
            Shape = item.Shape,
            Rarity = item.Rarity,
            Essence = item.Essence,
            Level = item.Level,
            Stars = item.Stars,
            Health = item.Health,
            Damage = item.Damage,
            Substats = CloneHubPreviewSubstats(item.Substats),
            Modulators = CloneHubPreviewModulators(item.Modulators),
        };
    }

    private static List<SubstatRoll> CloneHubPreviewSubstats(IList<SubstatRoll>? substats)
    {
        List<SubstatRoll> clone = new();
        if (substats == null)
        {
            return clone;
        }

        for (int substatIndex = 0; substatIndex < substats.Count; substatIndex++)
        {
            SubstatRoll substat = substats[substatIndex];
            clone.Add(new SubstatRoll(substat.Type, substat.Value));
        }

        return clone;
    }

    private static List<WeaponModulatorData> CloneHubPreviewModulators(IList<WeaponModulatorData>? modulators)
    {
        List<WeaponModulatorData> clone = new();
        if (modulators == null)
        {
            return clone;
        }

        for (int modulatorIndex = 0; modulatorIndex < modulators.Count; modulatorIndex++)
        {
            clone.Add(CloneHubPreviewModulator(modulators[modulatorIndex]));
        }

        return clone;
    }

    private static WeaponModulatorData CloneHubPreviewModulator(WeaponModulatorData modulator)
    {
        WeaponModulatorData clone = new()
        {
            Key = modulator.Key,
            BaseDisplayName = modulator.BaseDisplayName,
            DisplayName = modulator.DisplayName,
            BaseDescription = modulator.BaseDescription,
            Description = modulator.Description,
            Category = modulator.Category,
            Tags = new List<string>(modulator.Tags),
            SupportsTier = modulator.SupportsTier,
            Weight = modulator.Weight,
            UiColor = modulator.UiColor,
            Substats = CloneHubPreviewSubstats(modulator.Substats),
            Tier = modulator.Tier,
        };

        clone.RefreshPresentation();
        return clone;
    }

    private static void GetHubItemStatsContent(EquipmentItem? actualItem, EquipmentItem? previewItem, out string title, out string body)
    {
        EquipmentItem? displayItem = previewItem ?? actualItem;
        if (displayItem == null)
        {
            title = string.Empty;
            body = string.Empty;
            return;
        }

        title = BuildHubItemStatsTitle(displayItem);
        body = BuildHubItemStatsBody(displayItem, previewItem != null ? actualItem : null);
    }

    private void UpdateHubSocketStatsPanel(EquipmentItem? actualItem, EquipmentItem? previewItem)
    {
        EnsureHubSocketStatsPanel();
        if (_hubSocketStatsPanel == null
            || _hubSocketStatsTitleLabel == null
            || _hubSocketStatsMetaLabel == null
            || _hubSocketStatsContent == null)
        {
            return;
        }

        EquipmentItem? item = previewItem ?? actualItem;
        if (item == null)
        {
            _hubSocketStatsPanel.Visible = false;
            return;
        }

        EquipmentItem? baselineItem = previewItem != null ? actualItem : null;

        Rect2 statsRect = GetHubSocketStatsPanelRect();
        _hubSocketStatsPanel.Position = statsRect.Position;
        _hubSocketStatsPanel.Size = statsRect.Size;
        _hubSocketStatsPanel.CustomMinimumSize = statsRect.Size;
        _hubSocketStatsPanel.Visible = true;

        Color accent = item.Rarity?.UiColor ?? new Color(0.56f, 0.68f, 0.88f, 1f);
        _hubSocketStatsPanel.AddThemeStyleboxOverride(
            "panel",
            CreateLegacyStyleBox(
                new Color(accent.R * 0.08f, accent.G * 0.09f, accent.B * 0.10f, 0.90f),
                accent.Lightened(0.12f),
                14));

        _hubSocketStatsTitleLabel.Text = BuildHubItemStatsTitle(item);
        _hubSocketStatsTitleLabel.AddThemeColorOverride("font_color", Colors.White);
        _hubSocketStatsMetaLabel.Text = BuildHubItemMetaText(item, baselineItem != null);

        string renderSignature = BuildHubSocketStatsRenderSignature(item, baselineItem);
        if (!string.Equals(_hubSocketStatsRenderSignature, renderSignature, StringComparison.Ordinal))
        {
            ClearHubStatsPanelContent();
            BuildHubStatsPanelContent(_hubSocketStatsContent, item, baselineItem, accent);
            _hubSocketStatsRenderSignature = renderSignature;

            if (_hubSocketStatsScroll != null)
            {
                _hubSocketStatsScroll.ScrollVertical = 0;
            }
        }

        float innerWidth = Mathf.Max(96f, statsRect.Size.X - HubSocketStatsPanelHorizontalPadding);
        _hubSocketStatsTitleLabel.CustomMinimumSize = new Vector2(innerWidth, 0f);
        _hubSocketStatsMetaLabel.CustomMinimumSize = new Vector2(innerWidth, 0f);
    }

    private void ClearHubStatsPanelContent()
    {
        if (_hubSocketStatsContent == null)
        {
            return;
        }

        foreach (Node child in _hubSocketStatsContent.GetChildren())
        {
            _hubSocketStatsContent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void BuildHubStatsPanelContent(VBoxContainer parent, EquipmentItem item, EquipmentItem? baselineItem, Color accent)
    {
        Dictionary<SubstatType, float> totalSubstats = BuildHubTotalSubstatTotals(item);
        Dictionary<SubstatType, float>? baselineTotalSubstats = baselineItem != null ? BuildHubTotalSubstatTotals(baselineItem) : null;
        Dictionary<SubstatType, float> socketSubstats = BuildHubSocketSubstatTotals(item);
        Dictionary<SubstatType, float>? baselineSocketSubstats = baselineItem != null ? BuildHubSocketSubstatTotals(baselineItem) : null;
        HubWeaponSnapshot weaponSnapshot = BuildHubWeaponSnapshot(item, totalSubstats);
        HubWeaponSnapshot baselineWeaponSnapshot = baselineItem != null && baselineTotalSubstats != null
            ? BuildHubWeaponSnapshot(baselineItem, baselineTotalSubstats)
            : default;
        int effectiveHealth = StatGenerator.GetEffectiveHealth(item);
        int baselineEffectiveHealth = baselineItem != null ? StatGenerator.GetEffectiveHealth(baselineItem) : effectiveHealth;

        AddHubStatsHeroSection(parent, item, baselineItem, totalSubstats, baselineTotalSubstats, weaponSnapshot, baselineWeaponSnapshot, effectiveHealth, baselineEffectiveHealth, accent);
        if (baselineItem != null && baselineTotalSubstats != null)
        {
            AddHubStatsPreviewSection(parent, item, baselineItem, totalSubstats, baselineTotalSubstats, weaponSnapshot, baselineWeaponSnapshot, effectiveHealth, baselineEffectiveHealth, accent.Lightened(0.08f));
        }

        AddHubStatsActiveBuildSection(parent, item, baselineItem, accent);
        AddHubStatsModulatorDetailsSection(parent, item, accent);
        AddHubStatsBonusTotalsSection(parent, item, totalSubstats, baselineTotalSubstats, socketSubstats, baselineSocketSubstats, accent);
        AddHubStatsUtilitySection(parent, item, totalSubstats, baselineTotalSubstats, effectiveHealth, baselineEffectiveHealth, accent);
        AddHubStatsWeaponRollsSection(parent, item, accent);
    }

    private void AddHubStatsHeroSection(
        VBoxContainer parent,
        EquipmentItem item,
        EquipmentItem? baselineItem,
        Dictionary<SubstatType, float> totalSubstats,
        Dictionary<SubstatType, float>? baselineTotalSubstats,
        HubWeaponSnapshot weaponSnapshot,
        HubWeaponSnapshot baselineWeaponSnapshot,
        int effectiveHealth,
        int baselineEffectiveHealth,
        Color accent)
    {
        string subtitle = baselineItem != null
            ? "The biggest numbers stay pinned here so previews read immediately."
            : "The biggest numbers stay pinned here so the panel reads like a dashboard."
            ;
        VBoxContainer content = AddHubStatsSectionCard(parent, "At a glance", subtitle, accent);
        GridContainer grid = new()
        {
            Columns = 2,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        content.AddChild(grid);

        if (weaponSnapshot.HasDamage)
        {
            grid.AddChild(CreateHubHeroMetricCard(
                "Damage x Proj",
                BuildHubDamageProjectileSummary(weaponSnapshot),
                baselineItem != null ? BuildHubDamageProjectileDelta(weaponSnapshot, baselineWeaponSnapshot) : string.Empty,
                accent,
                emphasized: true));
            grid.AddChild(CreateHubHeroMetricCard(
                "HP",
                effectiveHealth.ToString(),
                baselineItem != null ? BuildHubIntDelta(effectiveHealth, baselineEffectiveHealth) : string.Empty,
                accent));
            grid.AddChild(CreateHubHeroMetricCard(
                "Rate",
                FormatHubRate(weaponSnapshot.AttacksPerSecond),
                baselineItem != null ? BuildHubFloatDelta(weaponSnapshot.AttacksPerSecond, baselineWeaponSnapshot.AttacksPerSecond, "0.##", "/s") : string.Empty,
                accent));
            grid.AddChild(CreateHubHeroMetricCard(
                "Crit",
                BuildHubHeroSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.CritChance), SubstatType.CritChance),
                baselineTotalSubstats != null ? BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.CritChance), GetHubSubstatValue(baselineTotalSubstats, SubstatType.CritChance), SubstatType.CritChance) : string.Empty,
                accent));
            return;
        }

        grid.AddChild(CreateHubHeroMetricCard("Mode", "Passive", string.Empty, accent, emphasized: true));
        grid.AddChild(CreateHubHeroMetricCard(
            "HP",
            effectiveHealth.ToString(),
            baselineItem != null ? BuildHubIntDelta(effectiveHealth, baselineEffectiveHealth) : string.Empty,
            accent));
        grid.AddChild(CreateHubHeroMetricCard(
            "Move",
            BuildHubSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.MovementSpeed), SubstatType.MovementSpeed),
            baselineTotalSubstats != null ? BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.MovementSpeed), GetHubSubstatValue(baselineTotalSubstats, SubstatType.MovementSpeed), SubstatType.MovementSpeed) : string.Empty,
            accent));
        grid.AddChild(CreateHubHeroMetricCard(
            "Luck",
            BuildHubSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.Luck), SubstatType.Luck),
            baselineTotalSubstats != null ? BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.Luck), GetHubSubstatValue(baselineTotalSubstats, SubstatType.Luck), SubstatType.Luck) : string.Empty,
            accent));
    }

    private void AddHubStatsPreviewSection(
        VBoxContainer parent,
        EquipmentItem item,
        EquipmentItem baselineItem,
        Dictionary<SubstatType, float> totalSubstats,
        Dictionary<SubstatType, float> baselineTotalSubstats,
        HubWeaponSnapshot weaponSnapshot,
        HubWeaponSnapshot baselineWeaponSnapshot,
        int effectiveHealth,
        int baselineEffectiveHealth,
        Color accent)
    {
        VBoxContainer content = AddHubStatsSectionCard(parent, "Preview changes", "Only changed values are shown here.", accent);
        int changeCount = 0;

        if (weaponSnapshot.HasDamage || baselineWeaponSnapshot.HasDamage)
        {
            changeCount += AddHubChangedStatRow(content, "Expected DPS", FormatHubStatNumber(weaponSnapshot.ExpectedDps), BuildHubFloatDelta(weaponSnapshot.ExpectedDps, baselineWeaponSnapshot.ExpectedDps, "0.###"));
            changeCount += AddHubChangedStatRow(content, "Shot", FormatHubStatNumber(weaponSnapshot.DamagePerProjectile), BuildHubFloatDelta(weaponSnapshot.DamagePerProjectile, baselineWeaponSnapshot.DamagePerProjectile, "0.###"));
            changeCount += AddHubChangedStatRow(content, "Rate", FormatHubRate(weaponSnapshot.AttacksPerSecond), BuildHubFloatDelta(weaponSnapshot.AttacksPerSecond, baselineWeaponSnapshot.AttacksPerSecond, "0.##", "/s"));
            changeCount += AddHubChangedStatRow(content, "Volley", FormatHubStatNumber(weaponSnapshot.DamagePerAttack), BuildHubFloatDelta(weaponSnapshot.DamagePerAttack, baselineWeaponSnapshot.DamagePerAttack, "0.###"));
            changeCount += AddHubChangedStatRow(content, "Cooldown", FormatHubSeconds(weaponSnapshot.EffectiveCooldown), BuildHubFloatDelta(weaponSnapshot.EffectiveCooldown, baselineWeaponSnapshot.EffectiveCooldown, "0.##", "s"));
            changeCount += AddHubChangedStatRow(content, "Projectiles", weaponSnapshot.ProjectileCount.ToString(), BuildHubIntDelta(weaponSnapshot.ProjectileCount, baselineWeaponSnapshot.ProjectileCount));
            changeCount += AddHubChangedStatRow(content, "Hits", weaponSnapshot.HitCount.ToString(), BuildHubIntDelta(weaponSnapshot.HitCount, baselineWeaponSnapshot.HitCount));
        }

        changeCount += AddHubChangedStatRow(content, "HP", effectiveHealth.ToString(), BuildHubIntDelta(effectiveHealth, baselineEffectiveHealth));
        changeCount += AddHubChangedStatRow(
            content,
            "Crit Chance",
            BuildHubSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.CritChance), SubstatType.CritChance),
            BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.CritChance), GetHubSubstatValue(baselineTotalSubstats, SubstatType.CritChance), SubstatType.CritChance));
        changeCount += AddHubChangedStatRow(
            content,
            "Crit Damage",
            BuildHubSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.CritDamage), SubstatType.CritDamage),
            BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.CritDamage), GetHubSubstatValue(baselineTotalSubstats, SubstatType.CritDamage), SubstatType.CritDamage));

        string currentEssence = BuildHubStatsEssenceChipText(item);
        string baselineEssence = BuildHubStatsEssenceChipText(baselineItem);
        if (!string.Equals(currentEssence, baselineEssence, StringComparison.Ordinal))
        {
            AddHubStatsRow(content, "Essence", currentEssence, note: $"was {baselineEssence}");
            changeCount++;
        }

        int maxSlots = Math.Max(
            Math.Max(item.UnlockedModulatorSlots, item.Modulators.Count),
            Math.Max(baselineItem.UnlockedModulatorSlots, baselineItem.Modulators.Count));
        for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
        {
            string currentSlot = BuildHubStatsModulatorChipText(item, slotIndex);
            string baselineSlot = BuildHubStatsModulatorChipText(baselineItem, slotIndex);
            if (string.Equals(currentSlot, baselineSlot, StringComparison.Ordinal))
            {
                continue;
            }

            AddHubStatsRow(content, $"Slot {slotIndex + 1}", currentSlot, note: $"was {baselineSlot}");
            changeCount++;
        }

        if (changeCount == 0)
        {
            AddHubStatsNote(content, "This hover changes socket ownership, but it does not move the visible headline stats.");
        }
    }

    private void AddHubStatsActiveBuildSection(VBoxContainer parent, EquipmentItem item, EquipmentItem? baselineItem, Color accent)
    {
        VBoxContainer content = AddHubStatsSectionCard(parent, "Active build", "The right-side socket rail picks targets. This strip summarizes the loadout.", accent);
        HFlowContainer flow = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        content.AddChild(flow);

        string essenceText = BuildHubStatsEssenceChipText(item);
        bool essenceChanged = baselineItem != null && !string.Equals(essenceText, BuildHubStatsEssenceChipText(baselineItem), StringComparison.Ordinal);
        AddHubStatsChip(flow, $"Essence: {essenceText}", item.Essence?.UiColor ?? accent, essenceChanged);

        if (item.Modulators.Count == 0)
        {
            AddHubStatsChip(flow, "No modulators installed", new Color(0.40f, 0.47f, 0.56f, 1f), false);
        }
        else
        {
            int visibleCount = Math.Min(4, item.Modulators.Count);
            for (int modulatorIndex = 0; modulatorIndex < visibleCount; modulatorIndex++)
            {
                WeaponModulatorData modulator = item.Modulators[modulatorIndex];
                bool changed = baselineItem != null && !string.Equals(BuildHubStatsModulatorChipText(item, modulatorIndex), BuildHubStatsModulatorChipText(baselineItem, modulatorIndex), StringComparison.Ordinal);
                AddHubStatsChip(flow, $"S{modulatorIndex + 1}: {modulator.DisplayName}", modulator.UiColor, changed);
            }

            if (item.Modulators.Count > visibleCount)
            {
                AddHubStatsChip(flow, $"+{item.Modulators.Count - visibleCount} more", new Color(0.40f, 0.47f, 0.56f, 1f), false);
            }
        }

        if (item.AvailableModulatorSlots > 0)
        {
            string slotText = item.AvailableModulatorSlots == 1 ? "1 open slot" : $"{item.AvailableModulatorSlots} open slots";
            AddHubStatsChip(flow, slotText, new Color(0.37f, 0.46f, 0.54f, 1f), false);
        }
    }

    private void AddHubStatsModulatorDetailsSection(VBoxContainer parent, EquipmentItem item, Color accent)
    {
        VBoxContainer content = AddHubStatsSectionCard(
            parent,
            "Modulator details",
            "Each installed modulator shows a short description plus green advantages and red drawbacks.",
            accent);

        if (item.Modulators.Count == 0)
        {
            AddHubStatsNote(content, "No modulators installed.");
            return;
        }

        Color advantageColor = new(0.62f, 0.95f, 0.74f, 1f);
        Color drawbackColor = new(1f, 0.67f, 0.71f, 1f);

        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            WeaponModulatorData modulator = item.Modulators[modulatorIndex];
            VBoxContainer modulatorCard = AddHubStatsSectionCard(
                content,
                $"Slot {modulatorIndex + 1}: {modulator.DisplayName}",
                BuildHubModulatorShortDescription(modulator),
                modulator.UiColor);

            Dictionary<SubstatType, float> statTotals = BuildHubModulatorStatTotals(modulator);
            List<SubstatType> sortedTypes = new(statTotals.Keys);
            sortedTypes.Sort(static (left, right) => ((int)left).CompareTo((int)right));

            bool hasAdvantages = false;
            bool hasDrawbacks = false;

            if (modulator.IsWingModulator)
            {
                AddHubStatsRow(
                    modulatorCard,
                    "Advantage",
                    $"Wing cadence {Mathf.RoundToInt(modulator.WingCadenceMultiplier * 100f)}% of installed weapon",
                    labelColor: advantageColor,
                    valueColor: advantageColor);
                hasAdvantages = true;
            }

            for (int typeIndex = 0; typeIndex < sortedTypes.Count; typeIndex++)
            {
                SubstatType type = sortedTypes[typeIndex];
                float value = statTotals[type];
                if (Mathf.IsZeroApprox(value))
                {
                    continue;
                }

                string statLine = $"{SubstatRoll.ToDisplayName(type)} {BuildHubSubstatValueText(value, type)}";
                if (value > 0f)
                {
                    AddHubStatsRow(modulatorCard, "Advantage", statLine, labelColor: advantageColor, valueColor: advantageColor);
                    hasAdvantages = true;
                }
                else
                {
                    AddHubStatsRow(modulatorCard, "Drawback", statLine, labelColor: drawbackColor, valueColor: drawbackColor);
                    hasDrawbacks = true;
                }
            }

            if (!hasAdvantages && !hasDrawbacks)
            {
                AddHubStatsNote(modulatorCard, "No direct stat deltas listed. This modulator is mostly behavior/proc focused.");
            }
            else if (!hasDrawbacks)
            {
                AddHubStatsNote(modulatorCard, "No explicit stat drawbacks listed.");
            }
        }
    }

    private static Dictionary<SubstatType, float> BuildHubModulatorStatTotals(WeaponModulatorData modulator)
    {
        Dictionary<SubstatType, float> totals = new();

        IReadOnlyList<WeaponModulatorEffectData> effects = modulator.GetResolvedEffects();
        for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            WeaponModulatorEffectData effect = effects[effectIndex];
            if (!effect.TryConvertToSubstatRoll(out SubstatRoll roll) || roll.Type == SubstatType.None || Mathf.IsZeroApprox(roll.Value))
            {
                continue;
            }

            totals[roll.Type] = totals.TryGetValue(roll.Type, out float existing)
                ? existing + roll.Value
                : roll.Value;
        }

        return totals;
    }

    private static string BuildHubModulatorShortDescription(WeaponModulatorData modulator)
    {
        string description = string.IsNullOrWhiteSpace(modulator.BaseDescription)
            ? modulator.Description
            : modulator.BaseDescription;
        description = description.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return "No short description available.";
        }

        int sentenceBreak = description.IndexOf('.');
        if (sentenceBreak > 0 && sentenceBreak <= 120)
        {
            return description[..(sentenceBreak + 1)].Trim();
        }

        return description.Length <= 120
            ? description
            : $"{description[..117].TrimEnd()}...";
    }

    private void AddHubStatsBonusTotalsSection(
        VBoxContainer parent,
        EquipmentItem item,
        Dictionary<SubstatType, float> totalSubstats,
        Dictionary<SubstatType, float>? baselineTotalSubstats,
        Dictionary<SubstatType, float> socketSubstats,
        Dictionary<SubstatType, float>? baselineSocketSubstats,
        Color accent)
    {
        VBoxContainer content = AddHubStatsSectionCard(parent, "Bonus totals", "Socket bonuses are called out separately so the core numbers stay readable.", accent);
        AddHubSubstatRowIfRelevant(content, "Damage %", socketSubstats, baselineSocketSubstats, SubstatType.DamagePercent);
        AddHubSubstatRowIfRelevant(content, "Attack Speed", socketSubstats, baselineSocketSubstats, SubstatType.AttackSpeed);
        AddHubSubstatRowIfRelevant(content, "Crit Chance", totalSubstats, baselineTotalSubstats, SubstatType.CritChance);
        AddHubSubstatRowIfRelevant(content, "Crit Damage", totalSubstats, baselineTotalSubstats, SubstatType.CritDamage);
        AddHubSubstatRowIfRelevant(content, "Projectile Count", totalSubstats, baselineTotalSubstats, SubstatType.ProjectileCount);
        AddHubSubstatRowIfRelevant(content, "Range", socketSubstats, baselineSocketSubstats, SubstatType.Range);
        AddHubSubstatRowIfRelevant(content, "Size", socketSubstats, baselineSocketSubstats, SubstatType.Size);
        AddHubSubstatRowIfRelevant(content, "Ricochet", socketSubstats, baselineSocketSubstats, SubstatType.Ricochet);
        AddHubSubstatRowIfRelevant(content, "Bounce Times", socketSubstats, baselineSocketSubstats, SubstatType.BounceTimes);

        if (content.GetChildCount() == 0)
        {
            AddHubStatsNote(content, "No socketed combat bonuses are active right now.");
        }
    }

    private void AddHubStatsUtilitySection(
        VBoxContainer parent,
        EquipmentItem item,
        Dictionary<SubstatType, float> totalSubstats,
        Dictionary<SubstatType, float>? baselineTotalSubstats,
        int effectiveHealth,
        int baselineEffectiveHealth,
        Color accent)
    {
        VBoxContainer content = AddHubStatsSectionCard(parent, "Survival & utility", string.Empty, accent);
        AddHubStatsRow(content, "HP", effectiveHealth.ToString(), baselineTotalSubstats != null ? BuildHubIntDelta(effectiveHealth, baselineEffectiveHealth) : string.Empty);
        AddHubSubstatRowIfRelevant(content, "Health %", totalSubstats, baselineTotalSubstats, SubstatType.HealthPercent);
        AddHubSubstatRowIfRelevant(content, "Dodge Chance", totalSubstats, baselineTotalSubstats, SubstatType.DodgeChance);
        AddHubSubstatRowIfRelevant(content, "Life Steal", totalSubstats, baselineTotalSubstats, SubstatType.LifeStealPercent);
        AddHubSubstatRowIfRelevant(content, "Health Regen", totalSubstats, baselineTotalSubstats, SubstatType.HealthRegen);
        AddHubSubstatRowIfRelevant(content, "Movement Speed", totalSubstats, baselineTotalSubstats, SubstatType.MovementSpeed);
        AddHubSubstatRowIfRelevant(content, "Luck", totalSubstats, baselineTotalSubstats, SubstatType.Luck);
    }

    private void AddHubStatsWeaponRollsSection(VBoxContainer parent, EquipmentItem item, Color accent)
    {
        List<string> weaponRolls = BuildHubSubstatLines(item.Substats, 6);
        if (weaponRolls.Count == 0)
        {
            return;
        }

        VBoxContainer content = AddHubStatsSectionCard(parent, "Weapon rolls", "These are the base rolls on the weapon itself, separate from the socketed build.", accent);
        for (int lineIndex = 0; lineIndex < weaponRolls.Count; lineIndex++)
        {
            AddHubStatsNote(content, weaponRolls[lineIndex]);
        }
    }

    private VBoxContainer AddHubStatsSectionCard(VBoxContainer parent, string title, string subtitle, Color accent)
    {
        PanelContainer card = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        card.AddThemeStyleboxOverride(
            "panel",
            CreateLegacyStyleBox(
                new Color(accent.R * 0.05f, accent.G * 0.06f, accent.B * 0.08f, 0.84f),
                accent.Lightened(0.08f),
                12));
        parent.AddChild(card);

        MarginContainer margin = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        card.AddChild(margin);

        VBoxContainer stack = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        stack.AddThemeConstantOverride("separation", 6);
        margin.AddChild(stack);

        if (!string.IsNullOrWhiteSpace(title))
        {
            Label titleLabel = new()
            {
                Text = title,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 12);
            titleLabel.AddThemeColorOverride("font_color", Colors.White);
            stack.AddChild(titleLabel);
        }

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            Label subtitleLabel = new()
            {
                Text = subtitle,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            subtitleLabel.AddThemeFontSizeOverride("font_size", 10);
            subtitleLabel.AddThemeColorOverride("font_color", new Color(0.69f, 0.76f, 0.84f, 0.90f));
            stack.AddChild(subtitleLabel);
        }

        VBoxContainer content = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 5);
        stack.AddChild(content);
        return content;
    }

    private Control CreateHubHeroMetricCard(string label, string value, string delta, Color accent, bool emphasized = false)
    {
        PanelContainer card = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 78f),
        };
        card.AddThemeStyleboxOverride(
            "panel",
            CreateLegacyStyleBox(
                new Color(accent.R * 0.10f, accent.G * 0.11f, accent.B * 0.13f, emphasized ? 0.94f : 0.88f),
                (emphasized ? accent.Lightened(0.18f) : accent.Lightened(0.08f)),
                10));

        MarginContainer margin = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        card.AddChild(margin);

        VBoxContainer stack = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        stack.AddThemeConstantOverride("separation", 3);
        margin.AddChild(stack);

        Label labelLabel = new()
        {
            Text = label,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        labelLabel.AddThemeFontSizeOverride("font_size", 10);
        labelLabel.AddThemeColorOverride("font_color", new Color(0.67f, 0.76f, 0.86f, 0.92f));
        stack.AddChild(labelLabel);

        Label valueLabel = new()
        {
            Text = value,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        valueLabel.AddThemeFontSizeOverride("font_size", emphasized ? 20 : 17);
        valueLabel.AddThemeColorOverride("font_color", Colors.White);
        stack.AddChild(valueLabel);

        if (!string.IsNullOrWhiteSpace(delta))
        {
            Label deltaLabel = new()
            {
                Text = delta,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            deltaLabel.AddThemeFontSizeOverride("font_size", 10);
            deltaLabel.AddThemeColorOverride("font_color", ResolveHubDeltaColor(delta));
            stack.AddChild(deltaLabel);
        }

        return card;
    }

    private void AddHubStatsChip(HFlowContainer parent, string text, Color accent, bool highlighted)
    {
        PanelContainer chip = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        chip.AddThemeStyleboxOverride(
            "panel",
            CreateLegacyStyleBox(
                new Color(accent.R * 0.10f, accent.G * 0.11f, accent.B * 0.12f, highlighted ? 0.96f : 0.86f),
                highlighted ? accent.Lightened(0.20f) : accent.Lightened(0.08f),
                10));
        parent.AddChild(chip);

        MarginContainer margin = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        chip.AddChild(margin);

        Label label = new()
        {
            Text = highlighted ? $"{text} • Preview" : text,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", Colors.White);
        margin.AddChild(label);
    }

    private void AddHubStatsRow(
        VBoxContainer parent,
        string label,
        string value,
        string delta = "",
        string note = "",
        Color? labelColor = null,
        Color? valueColor = null,
        Color? noteColor = null)
    {
        VBoxContainer wrapper = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        wrapper.AddThemeConstantOverride("separation", 2);
        parent.AddChild(wrapper);

        HBoxContainer row = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 6);
        wrapper.AddChild(row);

        Label labelLabel = new()
        {
            Text = label,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        labelLabel.AddThemeFontSizeOverride("font_size", 11);
        labelLabel.AddThemeColorOverride("font_color", labelColor ?? new Color(0.76f, 0.83f, 0.90f, 0.94f));
        row.AddChild(labelLabel);

        HBoxContainer right = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        right.AddThemeConstantOverride("separation", 5);
        row.AddChild(right);

        Label valueLabel = new()
        {
            Text = value,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueLabel.AddThemeFontSizeOverride("font_size", 12);
        valueLabel.AddThemeColorOverride("font_color", valueColor ?? Colors.White);
        right.AddChild(valueLabel);

        if (!string.IsNullOrWhiteSpace(delta))
        {
            Label deltaLabel = new()
            {
                Text = delta,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            deltaLabel.AddThemeFontSizeOverride("font_size", 10);
            deltaLabel.AddThemeColorOverride("font_color", ResolveHubDeltaColor(delta));
            right.AddChild(deltaLabel);
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            Label noteLabel = new()
            {
                Text = note,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            noteLabel.AddThemeFontSizeOverride("font_size", 10);
            noteLabel.AddThemeColorOverride("font_color", noteColor ?? new Color(0.63f, 0.70f, 0.78f, 0.88f));
            wrapper.AddChild(noteLabel);
        }
    }

    private void AddHubStatsNote(VBoxContainer parent, string text)
    {
        Label note = new()
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeFontSizeOverride("font_size", 10);
        note.AddThemeColorOverride("font_color", new Color(0.69f, 0.76f, 0.84f, 0.92f));
        parent.AddChild(note);
    }

    private void AddHubSubstatRowIfRelevant(VBoxContainer parent, string label, Dictionary<SubstatType, float> totals, Dictionary<SubstatType, float>? baselineTotals, SubstatType type)
    {
        float currentValue = GetHubSubstatValue(totals, type);
        float baselineValue = baselineTotals != null ? GetHubSubstatValue(baselineTotals, type) : currentValue;
        if (Mathf.IsZeroApprox(currentValue) && Mathf.IsZeroApprox(baselineValue))
        {
            return;
        }

        AddHubStatsRow(
            parent,
            label,
            BuildHubSubstatValueText(currentValue, type),
            baselineTotals != null ? BuildHubSubstatDelta(currentValue, baselineValue, type) : string.Empty);
    }

    private int AddHubChangedStatRow(VBoxContainer parent, string label, string value, string delta)
    {
        if (string.IsNullOrWhiteSpace(delta))
        {
            return 0;
        }

        AddHubStatsRow(parent, label, value, delta);
        return 1;
    }

    private static Color ResolveHubDeltaColor(string delta)
    {
        if (delta.Contains('+', StringComparison.Ordinal))
        {
            return new Color(0.64f, 0.93f, 0.76f, 1f);
        }

        if (delta.Contains('-', StringComparison.Ordinal))
        {
            return new Color(1f, 0.70f, 0.72f, 1f);
        }

        return new Color(0.80f, 0.85f, 0.91f, 0.94f);
    }

    private static string BuildHubItemMetaText(EquipmentItem item, bool previewing)
    {
        string meta = $"{item.Rarity?.DisplayName ?? "Unknown"} | L{item.Level} | {item.Stars}-star";
        return previewing ? $"{meta} | Previewing socket changes" : meta;
    }

    private static string BuildHubSocketStatsRenderSignature(EquipmentItem item, EquipmentItem? baselineItem)
    {
        StringBuilder builder = new();
        builder.Append(BuildHubItemStatsTitle(item));
        builder.Append('|');
        builder.Append(BuildHubItemMetaText(item, baselineItem != null));
        builder.Append('|');
        builder.Append(BuildHubItemStatsBody(item, baselineItem));
        return builder.ToString();
    }

    private static string BuildHubStatsEssenceChipText(EquipmentItem item)
    {
        if (!item.HasEssenceSlotUnlocked)
        {
            return "Locked";
        }

        if (!item.HasEquippedEssence || item.Essence == null)
        {
            return "Empty";
        }

        return item.Essence.DisplayName;
    }

    private static string BuildHubStatsModulatorChipText(EquipmentItem item, int slotIndex)
    {
        if (slotIndex >= item.UnlockedModulatorSlots)
        {
            return "Locked";
        }

        if (slotIndex >= item.Modulators.Count)
        {
            return "Empty";
        }

        return item.Modulators[slotIndex].DisplayName;
    }

    private static string BuildHubItemStatsTitle(EquipmentItem item)
    {
        string shapeName = item.Shape?.DisplayName ?? "Weapon";
        string attackName = item.Shape?.AttackName ?? "Unknown Attack";
        return $"{shapeName} | {attackName}";
    }

    private static string BuildHubItemStatsBody(EquipmentItem item, EquipmentItem? baselineItem = null)
    {
        StringBuilder builder = new();
        Dictionary<SubstatType, float> totalSubstats = BuildHubTotalSubstatTotals(item);
        Dictionary<SubstatType, float> socketSubstats = BuildHubSocketSubstatTotals(item);
        HubWeaponSnapshot weaponSnapshot = BuildHubWeaponSnapshot(item, totalSubstats);
        Dictionary<SubstatType, float>? baselineTotalSubstats = baselineItem != null ? BuildHubTotalSubstatTotals(baselineItem) : null;
        Dictionary<SubstatType, float>? baselineSocketSubstats = baselineItem != null ? BuildHubSocketSubstatTotals(baselineItem) : null;
        HubWeaponSnapshot baselineWeaponSnapshot = baselineItem != null && baselineTotalSubstats != null
            ? BuildHubWeaponSnapshot(baselineItem, baselineTotalSubstats)
            : default;
        int effectiveHealth = StatGenerator.GetEffectiveHealth(item);
        int baselineEffectiveHealth = baselineItem != null ? StatGenerator.GetEffectiveHealth(baselineItem) : effectiveHealth;

        builder.Append("[font_size=11][color=#9fb5d1]");
        builder.Append(EscapeHubBbcode($"{item.Rarity?.DisplayName ?? "Unknown"} | L{item.Level} | {item.Stars}-star"));
        builder.AppendLine("[/color][/font_size]");
        builder.AppendLine();

        AppendHubSectionHeader(builder, "Headline Combat", centered: true);
        if (weaponSnapshot.HasDamage)
        {
            AppendHubHeroValueLine(
                builder,
                "Expected DPS",
                FormatHubStatNumber(weaponSnapshot.ExpectedDps),
                baselineItem != null ? BuildHubFloatDelta(weaponSnapshot.ExpectedDps, baselineWeaponSnapshot.ExpectedDps, "0.###") : string.Empty);
            AppendHubHeroMetricLine(
                builder,
                "Shot",
                FormatHubStatNumber(weaponSnapshot.DamagePerProjectile),
                baselineItem != null ? BuildHubFloatDelta(weaponSnapshot.DamagePerProjectile, baselineWeaponSnapshot.DamagePerProjectile, "0.###") : string.Empty,
                BuildHubSourceSuffix(BuildHubCoreSourceLabels(item, true, SubstatType.DamagePercent)));
            AppendHubHeroMetricLine(
                builder,
                "Rate",
                FormatHubRate(weaponSnapshot.AttacksPerSecond),
                baselineItem != null ? BuildHubFloatDelta(weaponSnapshot.AttacksPerSecond, baselineWeaponSnapshot.AttacksPerSecond, "0.##", "/s") : string.Empty,
                BuildHubSourceSuffix(BuildHubCoreSourceLabels(item, false, SubstatType.AttackSpeed)));
            AppendHubHeroMetricLine(
                builder,
                "Volley",
                FormatHubStatNumber(weaponSnapshot.DamagePerAttack),
                baselineItem != null ? BuildHubFloatDelta(weaponSnapshot.DamagePerAttack, baselineWeaponSnapshot.DamagePerAttack, "0.###") : string.Empty);
            AppendHubHeroMetricLine(
                builder,
                "Cooldown",
                FormatHubSeconds(weaponSnapshot.EffectiveCooldown),
                baselineItem != null ? BuildHubFloatDelta(weaponSnapshot.EffectiveCooldown, baselineWeaponSnapshot.EffectiveCooldown, "0.##", "s") : string.Empty);
            AppendHubHeroMetricLine(
                builder,
                "Projectiles",
                weaponSnapshot.ProjectileCount.ToString(),
                baselineItem != null ? BuildHubIntDelta(weaponSnapshot.ProjectileCount, baselineWeaponSnapshot.ProjectileCount) : string.Empty,
                BuildHubSourceSuffix(BuildHubSubstatSourceLabels(item, SubstatType.ProjectileCount)));
            AppendHubHeroMetricLine(
                builder,
                "Hits",
                weaponSnapshot.HitCount.ToString(),
                baselineItem != null ? BuildHubIntDelta(weaponSnapshot.HitCount, baselineWeaponSnapshot.HitCount) : string.Empty);
            AppendHubHeroMetricLine(
                builder,
                "Crit Chance",
                BuildHubSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.CritChance), SubstatType.CritChance),
                baselineTotalSubstats != null ? BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.CritChance), GetHubSubstatValue(baselineTotalSubstats, SubstatType.CritChance), SubstatType.CritChance) : string.Empty,
                BuildHubSourceSuffix(BuildHubSubstatSourceLabels(item, SubstatType.CritChance)));
            AppendHubHeroMetricLine(
                builder,
                "Crit Damage",
                BuildHubSubstatValueText(GetHubSubstatValue(totalSubstats, SubstatType.CritDamage), SubstatType.CritDamage),
                baselineTotalSubstats != null ? BuildHubSubstatDelta(GetHubSubstatValue(totalSubstats, SubstatType.CritDamage), GetHubSubstatValue(baselineTotalSubstats, SubstatType.CritDamage), SubstatType.CritDamage) : string.Empty,
                BuildHubSourceSuffix(BuildHubSubstatSourceLabels(item, SubstatType.CritDamage)));
        }
        else
        {
            builder.AppendLine("[center][font_size=11][color=#9fb5d1]Passive / utility weapon[/color][/font_size][/center]");
        }

        List<string> weaponSubstatLines = BuildHubSubstatLines(item.Substats, HubSocketStatsVisibleSubstats);
        if (weaponSubstatLines.Count > 0)
        {
            builder.AppendLine();
            AppendHubSectionHeader(builder, "Weapon Substats");
            for (int lineIndex = 0; lineIndex < weaponSubstatLines.Count; lineIndex++)
            {
                AppendHubDetailTextLine(builder, $"Weapon: {weaponSubstatLines[lineIndex]}");
            }
        }

        builder.AppendLine();

        AppendHubSectionHeader(builder, "Socketed Build");
        string essenceSummary = BuildHubItemEssenceSummary(item);
        string essenceLine = $"Essence: {essenceSummary}";
        if (baselineItem != null && !string.Equals(essenceSummary, BuildHubItemEssenceSummary(baselineItem), StringComparison.Ordinal))
        {
            essenceLine += " (preview)";
        }

        AppendHubDetailTextLine(builder, essenceLine);
        AppendHubModulatorSlotLines(builder, item, baselineItem);

        bool hasSocketTotals = HasAnyNonZeroSubstat(socketSubstats,
                SubstatType.DamagePercent,
                SubstatType.AttackSpeed,
                SubstatType.CritChance,
                SubstatType.CritDamage,
                SubstatType.ProjectileCount,
                SubstatType.Range,
                SubstatType.Size,
                SubstatType.Ricochet,
                SubstatType.BounceTimes)
            || (baselineSocketSubstats != null && HasAnyNonZeroSubstat(baselineSocketSubstats,
                SubstatType.DamagePercent,
                SubstatType.AttackSpeed,
                SubstatType.CritChance,
                SubstatType.CritDamage,
                SubstatType.ProjectileCount,
                SubstatType.Range,
                SubstatType.Size,
                SubstatType.Ricochet,
                SubstatType.BounceTimes));

        if (hasSocketTotals)
        {
            builder.AppendLine();
            AppendHubSectionHeader(builder, "Socket Totals");
            AppendHubSubstatTotalLine(builder, "Damage Percent", socketSubstats, baselineSocketSubstats, item, SubstatType.DamagePercent);
            AppendHubSubstatTotalLine(builder, "Attack Speed", socketSubstats, baselineSocketSubstats, item, SubstatType.AttackSpeed);
            AppendHubSubstatTotalLine(builder, "Crit Chance", socketSubstats, baselineSocketSubstats, item, SubstatType.CritChance);
            AppendHubSubstatTotalLine(builder, "Crit Damage", socketSubstats, baselineSocketSubstats, item, SubstatType.CritDamage);
            AppendHubSubstatTotalLine(builder, "Projectile Count", socketSubstats, baselineSocketSubstats, item, SubstatType.ProjectileCount);
            AppendHubSubstatTotalLine(builder, "Range", socketSubstats, baselineSocketSubstats, item, SubstatType.Range);
            AppendHubSubstatTotalLine(builder, "Size", socketSubstats, baselineSocketSubstats, item, SubstatType.Size);
            AppendHubSubstatTotalLine(builder, "Ricochet", socketSubstats, baselineSocketSubstats, item, SubstatType.Ricochet);
            AppendHubSubstatTotalLine(builder, "Bounce Times", socketSubstats, baselineSocketSubstats, item, SubstatType.BounceTimes);
        }

        if (!item.HasEquippedEssence && item.Modulators.Count == 0 && !hasSocketTotals)
        {
            AppendHubDetailTextLine(builder, "No socketed bonuses installed");
        }

        builder.AppendLine();

        AppendHubSectionHeader(builder, "Survival & Utility");
        AppendHubMetricLine(
            builder,
            "HP",
            effectiveHealth.ToString(),
            baselineItem != null ? BuildHubIntDelta(effectiveHealth, baselineEffectiveHealth) : string.Empty,
            BuildHubSourceSuffix(BuildHubCoreSourceLabels(item, true, SubstatType.HealthPercent)));
        AppendHubSubstatTotalLine(builder, "Health Percent", totalSubstats, baselineTotalSubstats, item, SubstatType.HealthPercent);
        AppendHubSubstatTotalLine(builder, "Dodge Chance", totalSubstats, baselineTotalSubstats, item, SubstatType.DodgeChance);
        AppendHubSubstatTotalLine(builder, "Life Steal", totalSubstats, baselineTotalSubstats, item, SubstatType.LifeStealPercent);
        AppendHubSubstatTotalLine(builder, "Health Regen", totalSubstats, baselineTotalSubstats, item, SubstatType.HealthRegen);
        AppendHubSubstatTotalLine(builder, "Movement Speed", totalSubstats, baselineTotalSubstats, item, SubstatType.MovementSpeed);
        AppendHubSubstatTotalLine(builder, "Luck", totalSubstats, baselineTotalSubstats, item, SubstatType.Luck);

        return builder.ToString().TrimEnd();
    }

    private static Dictionary<SubstatType, float> BuildHubTotalSubstatTotals(EquipmentItem item)
    {
        Dictionary<SubstatType, float> totals = new();
        AccumulateHubSubstatTotals(totals, item.Substats);
        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            AccumulateHubSubstatTotals(totals, item.Modulators[modulatorIndex].Substats);
        }

        return totals;
    }

    private static Dictionary<SubstatType, float> BuildHubSocketSubstatTotals(EquipmentItem item)
    {
        Dictionary<SubstatType, float> totals = new();
        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            AccumulateHubSubstatTotals(totals, item.Modulators[modulatorIndex].Substats);
        }

        return totals;
    }

    private static HubWeaponSnapshot BuildHubWeaponSnapshot(EquipmentItem item, Dictionary<SubstatType, float> totalSubstats)
    {
        if (item.Shape == null)
        {
            return default;
        }

        ShapeData shape = item.Shape;
        float damagePerProjectile = Mathf.Max(0f, StatGenerator.GetEffectiveDamage(item));
        int hitCount = Mathf.Max(1, shape.Hits);
        int projectileCount = Mathf.Max(1, 1 + Mathf.RoundToInt(Mathf.Max(0f, GetHubSubstatValue(totalSubstats, SubstatType.ProjectileCount))));
        float attackSpeedPercent = Mathf.Max(0f, GetHubSubstatValue(totalSubstats, SubstatType.AttackSpeed));
        float critChancePercent = Mathf.Max(0f, GetHubSubstatValue(totalSubstats, SubstatType.CritChance));
        float critDamagePercent = Mathf.Max(0f, GetHubSubstatValue(totalSubstats, SubstatType.CritDamage));
        bool hasDamage = damagePerProjectile > 0f && shape.Cooldown < 900f;
        float attacksPerSecond = hasDamage
            ? (1f + attackSpeedPercent / 100f) / Mathf.Max(0.01f, shape.Cooldown)
            : 0f;
        float effectiveCooldown = attacksPerSecond > 0f ? 1f / attacksPerSecond : 0f;
        float damagePerAttack = damagePerProjectile * hitCount * projectileCount;
        float baseDps = damagePerAttack * attacksPerSecond;
        float expectedDps = baseDps * GetHubExpectedCritMultiplier(critChancePercent, critDamagePercent);

        return new HubWeaponSnapshot
        {
            HasDamage = hasDamage,
            HitCount = hitCount,
            ProjectileCount = projectileCount,
            CritChancePercent = critChancePercent,
            CritDamagePercent = critDamagePercent,
            DamagePerProjectile = damagePerProjectile,
            DamagePerAttack = damagePerAttack,
            AttacksPerSecond = attacksPerSecond,
            EffectiveCooldown = effectiveCooldown,
            BaseDps = baseDps,
            ExpectedDps = expectedDps,
        };
    }

    private static float GetHubExpectedCritMultiplier(float critChancePercent, float critDamagePercent)
    {
        float critChance = Mathf.Clamp(critChancePercent / 100f, 0f, 1f);
        float critBonus = HubBaseCriticalDamagePercent / 100f + Mathf.Max(0f, critDamagePercent) / 100f;
        return 1f + critChance * critBonus;
    }

    private static float GetHubSubstatValue(Dictionary<SubstatType, float> totals, SubstatType type)
    {
        return totals.TryGetValue(type, out float value) ? value : 0f;
    }

    private static string BuildHubSubstatSummary(Dictionary<SubstatType, float> totals, params SubstatType[] order)
    {
        List<string> parts = new();
        for (int typeIndex = 0; typeIndex < order.Length; typeIndex++)
        {
            SubstatType type = order[typeIndex];
            if (!totals.TryGetValue(type, out float value) || Mathf.IsZeroApprox(value))
            {
                continue;
            }

            parts.Add(new SubstatRoll(type, value).ToDisplayString());
        }

        return string.Join(" | ", parts);
    }

    private static void AppendHubMetricLine(StringBuilder builder, string label, string valueText, string deltaText = "", string sourceText = "")
    {
        builder.Append("[font_size=11]");
        builder.Append(EscapeHubBbcode(label));
        builder.Append(": [b]");
        builder.Append(EscapeHubBbcode(valueText));
        builder.Append("[/b]");
        if (!string.IsNullOrWhiteSpace(deltaText))
        {
            builder.Append(' ');
            builder.Append(EscapeHubBbcode(deltaText));
        }

        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            builder.Append(" [color=#8fa4be][font_size=9]");
            builder.Append(EscapeHubBbcode(sourceText));
            builder.Append("[/font_size][/color]");
        }

        builder.AppendLine("[/font_size]");
    }

    private static void AppendHubSectionHeader(StringBuilder builder, string title, bool centered = false)
    {
        if (centered)
        {
            builder.Append("[center]");
        }

        builder.Append("[font_size=13][b][color=#c7d7ea]");
        builder.Append(EscapeHubBbcode(title));
        builder.Append("[/color][/b][/font_size]");
        if (centered)
        {
            builder.Append("[/center]");
        }

        builder.AppendLine();
    }

    private static void AppendHubHeroValueLine(StringBuilder builder, string label, string valueText, string deltaText = "", string sourceText = "")
    {
        builder.Append("[center][font_size=10][color=#8ea2bb]");
        builder.Append(EscapeHubBbcode(label));
        builder.AppendLine("[/color][/font_size]");
        builder.Append("[font_size=20][b]");
        builder.Append(EscapeHubBbcode(valueText));
        builder.Append("[/b][/font_size]");
        AppendHubHeroMeta(builder, deltaText, sourceText);
        builder.AppendLine("[/center]");
    }

    private static void AppendHubHeroMetricLine(StringBuilder builder, string label, string valueText, string deltaText = "", string sourceText = "")
    {
        builder.Append("[center][font_size=10][color=#8ea2bb]");
        builder.Append(EscapeHubBbcode(label));
        builder.Append(": [/color][/font_size][font_size=14][b]");
        builder.Append(EscapeHubBbcode(valueText));
        builder.Append("[/b][/font_size]");
        AppendHubHeroMeta(builder, deltaText, sourceText);
        builder.AppendLine("[/center]");
    }

    private static void AppendHubHeroMeta(StringBuilder builder, string deltaText, string sourceText)
    {
        if (!string.IsNullOrWhiteSpace(deltaText))
        {
            builder.Append(" [font_size=10][color=#dbe7f5]");
            builder.Append(EscapeHubBbcode(deltaText));
            builder.Append("[/color][/font_size]");
        }

        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            builder.Append(" [font_size=9][color=#8fa4be]");
            builder.Append(EscapeHubBbcode(sourceText));
            builder.Append("[/color][/font_size]");
        }
    }

    private static void AppendHubDetailTextLine(StringBuilder builder, string text)
    {
        builder.Append("[font_size=11]");
        builder.Append(EscapeHubBbcode(text));
        builder.AppendLine("[/font_size]");
    }

    private static string EscapeHubBbcode(string? text)
    {
        return (text ?? string.Empty)
            .Replace("[", "[lb]")
            .Replace("]", "[rb]");
    }

    private static void AppendHubSubstatTotalLine(
        StringBuilder builder,
        string label,
        Dictionary<SubstatType, float> totals,
        Dictionary<SubstatType, float>? baselineTotals,
        EquipmentItem item,
        SubstatType type)
    {
        float currentValue = GetHubSubstatValue(totals, type);
        float baselineValue = baselineTotals != null ? GetHubSubstatValue(baselineTotals, type) : currentValue;
        if (Mathf.IsZeroApprox(currentValue) && Mathf.IsZeroApprox(baselineValue))
        {
            return;
        }

        AppendHubMetricLine(
            builder,
            label,
            BuildHubSubstatValueText(currentValue, type),
            baselineTotals != null ? BuildHubSubstatDelta(currentValue, baselineValue, type) : string.Empty,
            BuildHubSourceSuffix(BuildHubSubstatSourceLabels(item, type)));
    }

    private static string BuildHubSubstatValueText(float value, SubstatType type)
    {
        if (SubstatRoll.UsesFlatDisplay(type))
        {
            return value >= 0f ? $"+{value:0.0}" : value.ToString("0.0");
        }

        return value >= 0f ? $"+{value:0.00}%" : $"{value:0.00}%";
    }

    private static string BuildHubHeroSubstatValueText(float value, SubstatType type)
    {
        string display = BuildHubSubstatValueText(value, type);
        return display.StartsWith("+", StringComparison.Ordinal) ? display[1..] : display;
    }

    private static string BuildHubDamageProjectileSummary(HubWeaponSnapshot snapshot)
    {
        int projectileCount = Math.Max(1, snapshot.ProjectileCount);
        return $"{FormatHubStatNumber(snapshot.DamagePerProjectile)}x{projectileCount}";
    }

    private static string BuildHubDamageProjectileDelta(HubWeaponSnapshot current, HubWeaponSnapshot baseline)
    {
        string damageDelta = BuildHubFloatDelta(current.DamagePerProjectile, baseline.DamagePerProjectile, "0.###");
        string projectileDelta = BuildHubIntDelta(current.ProjectileCount, baseline.ProjectileCount);
        if (string.IsNullOrWhiteSpace(damageDelta) && string.IsNullOrWhiteSpace(projectileDelta))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(projectileDelta))
        {
            return damageDelta;
        }

        if (string.IsNullOrWhiteSpace(damageDelta))
        {
            return projectileDelta;
        }

        return $"{damageDelta} {projectileDelta}";
    }

    private static string BuildHubSubstatDelta(float currentValue, float baselineValue, SubstatType type)
    {
        float delta = currentValue - baselineValue;
        if (Mathf.IsZeroApprox(delta))
        {
            return string.Empty;
        }

        return SubstatRoll.UsesFlatDisplay(type)
            ? $"({delta:+0.0;-0.0})"
            : $"({delta:+0.00;-0.00}%)";
    }

    private static string BuildHubFloatDelta(float currentValue, float baselineValue, string format, string suffix = "")
    {
        float delta = currentValue - baselineValue;
        if (Mathf.Abs(delta) <= 0.0001f)
        {
            return string.Empty;
        }

        return $"({delta.ToString($"+{format};-{format};0")}{suffix})";
    }

    private static string BuildHubIntDelta(int currentValue, int baselineValue)
    {
        int delta = currentValue - baselineValue;
        return delta == 0 ? string.Empty : $"({delta:+#;-#;0})";
    }

    private static string BuildHubSourceSuffix(List<string> sourceLabels)
    {
        return sourceLabels.Count > 0 ? $"[{string.Join(", ", sourceLabels)}]" : string.Empty;
    }

    private static List<string> BuildHubCoreSourceLabels(EquipmentItem item, bool includeEssence, params SubstatType[] types)
    {
        List<string> labels = BuildHubSubstatSourceLabels(item, types);
        if (includeEssence && item.HasEquippedEssence)
        {
            InsertHubSourceLabel(labels, "Essence");
        }

        return labels;
    }

    private static List<string> BuildHubSubstatSourceLabels(EquipmentItem item, params SubstatType[] types)
    {
        List<string> labels = new();
        if (HasMatchingSubstat(item.Substats, types))
        {
            InsertHubSourceLabel(labels, "Weapon");
        }

        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            if (HasMatchingSubstat(item.Modulators[modulatorIndex].Substats, types))
            {
                InsertHubSourceLabel(labels, $"Slot {modulatorIndex + 1}");
            }
        }

        return labels;
    }

    private static void InsertHubSourceLabel(List<string> labels, string label)
    {
        for (int labelIndex = 0; labelIndex < labels.Count; labelIndex++)
        {
            if (string.Equals(labels[labelIndex], label, StringComparison.Ordinal))
            {
                return;
            }
        }

        labels.Add(label);
    }

    private static bool HasMatchingSubstat(IList<SubstatRoll>? substats, params SubstatType[] types)
    {
        if (substats == null || types.Length == 0)
        {
            return false;
        }

        for (int substatIndex = 0; substatIndex < substats.Count; substatIndex++)
        {
            SubstatRoll substat = substats[substatIndex];
            if (Mathf.IsZeroApprox(substat.Value))
            {
                continue;
            }

            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                if (substat.Type == types[typeIndex])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAnyNonZeroSubstat(Dictionary<SubstatType, float> totals, params SubstatType[] types)
    {
        for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            if (totals.TryGetValue(types[typeIndex], out float value) && !Mathf.IsZeroApprox(value))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> BuildHubSubstatLines(IList<SubstatRoll>? substats, int maxLines)
    {
        List<string> lines = new();
        if (substats == null)
        {
            return lines;
        }

        for (int substatIndex = 0; substatIndex < substats.Count; substatIndex++)
        {
            SubstatRoll substat = substats[substatIndex];
            if (substat.Type == SubstatType.None || Mathf.IsZeroApprox(substat.Value))
            {
                continue;
            }

            lines.Add(substat.ToDisplayString());
        }

        if (lines.Count > maxLines)
        {
            lines = lines.GetRange(0, maxLines);
            lines.Add($"+{Math.Max(0, substats.Count - maxLines)} more");
        }

        return lines;
    }

    private static void AppendHubModulatorSlotLines(StringBuilder builder, EquipmentItem item, EquipmentItem? baselineItem = null)
    {
        int visibleSlots = Math.Max(item.UnlockedModulatorSlots, item.Modulators.Count);
        int baselineVisibleSlots = baselineItem != null ? Math.Max(baselineItem.UnlockedModulatorSlots, baselineItem.Modulators.Count) : 0;
        int maxSlots = Math.Max(visibleSlots, baselineVisibleSlots);
        if (maxSlots <= 0 && item.AvailableModulatorSlots <= 0)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < Math.Max(maxSlots, item.UnlockedModulatorSlots); slotIndex++)
        {
            string slotSummary = BuildHubModulatorSlotSummary(item, slotIndex);
            string detailLine = $"Slot {slotIndex + 1}: {slotSummary}";
            if (baselineItem != null && !string.Equals(slotSummary, BuildHubModulatorSlotSummary(baselineItem, slotIndex), StringComparison.Ordinal))
            {
                detailLine += " (preview)";
            }

            AppendHubDetailTextLine(builder, detailLine);
        }
    }

    private static string BuildHubModulatorSlotSummary(EquipmentItem item, int slotIndex)
    {
        if (slotIndex >= item.UnlockedModulatorSlots)
        {
            return "Locked";
        }

        if (slotIndex >= item.Modulators.Count)
        {
            return "Empty";
        }

        WeaponModulatorData modulator = item.Modulators[slotIndex];
        string summary = BuildHubModulatorMenuSummary(modulator);
        return string.IsNullOrWhiteSpace(summary)
            ? modulator.DisplayName
            : $"{modulator.DisplayName} | {summary}";
    }

    private static string FormatHubStatNumber(float value)
    {
        float abs = Mathf.Abs(value);
        if (abs >= 100f)
        {
            return value.ToString("0.#");
        }

        if (abs >= 10f)
        {
            return value.ToString("0.##");
        }

        return value.ToString("0.###");
    }

    private static string FormatHubRate(float attacksPerSecond)
    {
        return attacksPerSecond > 0f ? $"{FormatHubStatNumber(attacksPerSecond)}/s" : "--/s";
    }

    private static string FormatHubSeconds(float seconds)
    {
        return seconds > 0f ? $"{seconds:0.##}s" : "--";
    }

    private static List<string> BuildHubItemSubstatLines(EquipmentItem item)
    {
        Dictionary<SubstatType, float> totals = BuildHubTotalSubstatTotals(item);
        List<SubstatRoll> combined = new();
        foreach (KeyValuePair<SubstatType, float> pair in totals)
        {
            combined.Add(new SubstatRoll(pair.Key, pair.Value));
        }

        return BuildHubSubstatLines(combined, HubSocketStatsVisibleSubstats);
    }

    private static string BuildHubItemEssenceSummary(EquipmentItem item)
    {
        if (!item.HasEssenceSlotUnlocked)
        {
            return "Locked";
        }

        if (!item.HasEquippedEssence || item.Essence == null)
        {
            return "Empty";
        }

        string summary = BuildHubEssenceMenuSummary(item.Essence);
        return string.IsNullOrWhiteSpace(summary)
            ? item.Essence.DisplayName
            : $"{item.Essence.DisplayName} | {summary}";
    }

    private static string BuildHubInstalledModulatorSummary(EquipmentItem item)
    {
        if (item.Modulators.Count == 0)
        {
            return "None";
        }

        int visibleCount = Math.Min(3, item.Modulators.Count);
        StringBuilder builder = new();
        for (int modulatorIndex = 0; modulatorIndex < visibleCount; modulatorIndex++)
        {
            if (modulatorIndex > 0)
            {
                builder.Append(", ");
            }

            builder.Append(item.Modulators[modulatorIndex].DisplayName);
        }

        if (item.Modulators.Count > visibleCount)
        {
            builder.Append(" +");
            builder.Append(item.Modulators.Count - visibleCount);
        }

        return builder.ToString();
    }

    private static void AccumulateHubSubstatTotals(Dictionary<SubstatType, float> totals, IList<SubstatRoll>? substats)
    {
        if (substats == null)
        {
            return;
        }

        for (int substatIndex = 0; substatIndex < substats.Count; substatIndex++)
        {
            SubstatRoll substat = substats[substatIndex];
            if (substat.Type == SubstatType.None)
            {
                continue;
            }

            totals[substat.Type] = totals.TryGetValue(substat.Type, out float existingValue)
                ? existingValue + substat.Value
                : substat.Value;
        }
    }

    private void OnHubSocketOverlayDraw()
    {
        if (_hubSocketOverlay == null || !_hubSocketOverlay.Visible)
        {
            return;
        }

        if (_hubSocketTraceVisibility <= 0.001f || _animatedHubSocketButtonIndex < 0)
        {
            return;
        }

        if (_animatedHubSocketButtonIndex >= _hubSocketAnchors.Count || _animatedHubSocketButtonIndex >= _hubSocketLayoutPositions.Count)
        {
            return;
        }

        HubSocketAnchor anchor = _hubSocketAnchors[_animatedHubSocketButtonIndex];
        Vector2 start = ToHubSocketProjectedOverlayPosition(anchor.ViewportPosition);
        Vector2 cardPosition = _hubSocketLayoutPositions[_animatedHubSocketButtonIndex];
        bool isLeftLane = cardPosition.X < ((_hubSocketOverlay.Size.X) * 0.5f);
        Vector2 end = cardPosition + new Vector2(isLeftLane ? _hubSocketCardSize.X : 0f, _hubSocketCardSize.Y * 0.5f);
        float bendX = Mathf.Lerp(start.X, end.X, 0.58f);

        Color baseColor = anchor.Installed ? anchor.Color.Lightened(0.14f) : anchor.Unlocked ? anchor.Color : anchor.Color.Darkened(0.08f);
        float alpha = (anchor.Installed ? 0.90f : anchor.Unlocked ? 0.62f : 0.36f) * _hubSocketTraceVisibility;
        Color traceColor = new(baseColor.R, baseColor.G, baseColor.B, alpha);
        Vector2[] path =
        [
            start,
            new Vector2(bendX, start.Y),
            new Vector2(bendX, end.Y),
            end,
        ];

        DrawHubSocketTracePath(path, traceColor, _hubSocketTraceReveal);
        _hubSocketOverlay.DrawCircle(start, HubSocketTraceDotRadius * Mathf.Clamp(_hubSocketTraceVisibility, 0.45f, 1f), traceColor);
    }

    private void DrawHubSocketTracePath(Vector2[] path, Color color, float reveal)
    {
        if (_hubSocketOverlay == null || path.Length < 2 || reveal <= 0.001f)
        {
            return;
        }

        float clampedReveal = Mathf.Clamp(reveal, 0f, 1f);
        float totalLength = 0f;
        for (int segmentIndex = 0; segmentIndex < path.Length - 1; segmentIndex++)
        {
            totalLength += path[segmentIndex].DistanceTo(path[segmentIndex + 1]);
        }

        if (totalLength <= 0.001f)
        {
            return;
        }

        float revealDistance = totalLength * clampedReveal;
        List<Vector2> revealedPoints = new() { path[0] };
        for (int segmentIndex = 0; segmentIndex < path.Length - 1; segmentIndex++)
        {
            Vector2 segmentStart = path[segmentIndex];
            Vector2 segmentEnd = path[segmentIndex + 1];
            float segmentLength = segmentStart.DistanceTo(segmentEnd);
            if (segmentLength <= 0.001f)
            {
                continue;
            }

            if (revealDistance >= segmentLength)
            {
                revealedPoints.Add(segmentEnd);
                revealDistance -= segmentLength;
                continue;
            }

            float t = Mathf.Clamp(revealDistance / segmentLength, 0f, 1f);
            revealedPoints.Add(segmentStart.Lerp(segmentEnd, t));
            break;
        }

        if (revealedPoints.Count >= 2)
        {
            _hubSocketOverlay.DrawPolyline(revealedPoints.ToArray(), color, HubSocketTraceWidth, true);
        }
    }

    private void ApplyHubSocketButtonContent(Button button, HubSocketAnchor anchor, int buttonIndex)
    {
        Label? title = button.GetNodeOrNull<Label>("TextStack/Title");
        if (title != null)
        {
            title.Text = anchor.Title;
        }

        Label? summary = button.GetNodeOrNull<Label>("TextStack/Summary");
        if (summary != null)
        {
            string resolvedSummary = buttonIndex == _activeHubInlinePickerButtonIndex && !string.IsNullOrWhiteSpace(_hubInlinePickerHoverPreviewSummary)
                ? _hubInlinePickerHoverPreviewSummary
                : anchor.Summary;
            summary.Text = resolvedSummary;
            summary.Visible = !string.IsNullOrWhiteSpace(resolvedSummary);
        }
    }

    private void ApplyHubSocketButtonStyle(Button button, HubSocketAnchor anchor)
    {
        Color color = anchor.Unlocked ? anchor.Color : new Color(0.36f, 0.41f, 0.50f, 1f);
        int buttonIndex = _hubSocketButtons.IndexOf(button);
        bool highlighted = buttonIndex >= 0 && (buttonIndex == _hoveredHubSocketButtonIndex || buttonIndex == _selectedHubSocketButtonIndex);
        bool previewActive = buttonIndex == _activeHubInlinePickerButtonIndex && !string.IsNullOrWhiteSpace(_hubInlinePickerHoverPreviewSummary);
        float alpha = anchor.Installed ? 0.94f : anchor.Unlocked ? 0.82f : 0.66f;
        Color background = new(color.R * 0.12f, color.G * 0.12f, color.B * 0.12f, alpha);
        Color border = highlighted
            ? color.Lightened(0.28f)
            : anchor.Installed ? color.Lightened(0.18f) : color;
        if (previewActive)
        {
            border = border.Lightened(0.10f);
        }

        button.AddThemeColorOverride("font_color", anchor.Unlocked ? Colors.White : new Color(0.68f, 0.72f, 0.78f, 1f));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeStyleboxOverride("normal", CreateLegacyStyleBox(highlighted ? background.Lightened(0.08f) : background, border, 12));
        button.AddThemeStyleboxOverride("hover", CreateLegacyStyleBox(background.Lightened(0.14f), border.Lightened(0.18f), 12));
        button.AddThemeStyleboxOverride("pressed", CreateLegacyStyleBox(background.Darkened(0.08f), border.Lightened(0.10f), 12));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        ColorRect? accent = button.GetNodeOrNull<ColorRect>("Accent");
        if (accent != null)
        {
            accent.Color = anchor.Unlocked ? border : new Color(0.32f, 0.36f, 0.42f, 1f);
        }

        Label? title = button.GetNodeOrNull<Label>("TextStack/Title");
        if (title != null)
        {
            title.AddThemeColorOverride("font_color", anchor.Unlocked ? Colors.White : new Color(0.70f, 0.74f, 0.80f, 1f));
        }

        Label? summary = button.GetNodeOrNull<Label>("TextStack/Summary");
        if (summary != null)
        {
            summary.AddThemeColorOverride(
                "font_color",
                previewActive
                    ? new Color(0.93f, 0.96f, 1f, 0.98f)
                    : anchor.Unlocked ? new Color(0.82f, 0.87f, 0.94f, 0.95f) : new Color(0.58f, 0.63f, 0.70f, 0.95f));
        }
    }

    private void OnHubSocketButtonGuiInput(int buttonIndex, Button button, InputEvent @event)
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview == null || !hubPreview.IsWeaponInspectionActive)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                _activeHubSocketButtonIndex = buttonIndex;
                _activeHubSocketButtonDragDistance = 0f;
                _activeHubSocketButtonDragging = false;
                button.AcceptEvent();
                return;
            }

            bool shouldClickSocket = _activeHubSocketButtonIndex == buttonIndex && !_activeHubSocketButtonDragging;
            ResetActiveHubSocketDrag();

            if (shouldClickSocket)
            {
                OnHubSocketButtonPressed(buttonIndex);
            }

            button.AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion mouseMotion && _activeHubSocketButtonIndex == buttonIndex)
        {
            _activeHubSocketButtonDragDistance += mouseMotion.Relative.Length();
            if (_activeHubSocketButtonDragDistance >= HubSocketDragThresholdPixels)
            {
                _activeHubSocketButtonDragging = true;
            }

            button.AcceptEvent();
        }
    }

    private void OnHubSocketButtonMouseEntered(int buttonIndex)
    {
        _hoveredHubSocketButtonIndex = buttonIndex;
        _hubSocketOverlay?.QueueRedraw();
    }

    private void OnHubSocketButtonMouseExited(int buttonIndex)
    {
        if (_hoveredHubSocketButtonIndex == buttonIndex)
        {
            _hoveredHubSocketButtonIndex = -1;
            _hubSocketOverlay?.QueueRedraw();
        }
    }

    private bool HandleActiveHubSocketDragInput(InputEvent @event, HubSceneController hubPreview)
    {
        if (_activeHubSocketButtonIndex < 0)
        {
            return false;
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            _activeHubSocketButtonDragDistance += mouseMotion.Relative.Length();
            if (_activeHubSocketButtonDragDistance >= HubSocketDragThresholdPixels)
            {
                _activeHubSocketButtonDragging = true;
            }

            return true;
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
        {
            ResetActiveHubSocketDrag();
            return true;
        }

        return false;
    }

    private void ResetActiveHubSocketDrag()
    {
        _activeHubSocketButtonIndex = -1;
        _activeHubSocketButtonDragDistance = 0f;
        _activeHubSocketButtonDragging = false;
    }

    private void OnHubSocketButtonPressed(int buttonIndex)
    {
        if (buttonIndex < 0 || buttonIndex >= _hubSocketAnchors.Count || _inventoryManager == null)
        {
            return;
        }

        HubSocketAnchor anchor = _hubSocketAnchors[buttonIndex];
        EquipmentItem? item = _inventoryManager.GetEquippedItemAt(anchor.WeaponSlotIndex);
        if (item == null)
        {
            return;
        }

        OpenHubInlinePicker(buttonIndex, anchor, item);
    }

    private void ShowHubSocketDropdown(HubSocketAnchor anchor, EquipmentItem item, int buttonIndex)
    {
        EnsureHubSocketMenu();
        if (_hubSocketMenu == null)
        {
            return;
        }

        _hubSocketMenuAnchor = anchor;
        _hubSocketMenuItemInstanceId = item.InstanceId;
        _hubSocketMenu.Clear();

        if (anchor.Kind == HubSocketKind.Essence)
        {
            PopulateHubEssenceSocketMenu(item);
        }
        else
        {
            PopulateHubModulatorSocketMenu(item, anchor.SocketIndex);
        }

        if (_hubSocketMenu.ItemCount == 0)
        {
            AddDisabledHubSocketMenuItem("No options available");
        }

        Vector2 menuPosition = buttonIndex >= 0 && buttonIndex < _hubSocketButtons.Count
            ? _hubSocketButtons[buttonIndex].GetGlobalRect().Position + new Vector2(0f, _hubSocketButtons[buttonIndex].Size.Y + 6f)
            : GetViewport().GetMousePosition();
        _hubSocketMenu.Position = new Vector2I(Mathf.RoundToInt(menuPosition.X), Mathf.RoundToInt(menuPosition.Y));
        _hubSocketMenu.Popup();
    }

    private void OnHubSocketMenuPopupHide()
    {
        if (_activeHubInlinePickerKind != HubInlinePickerKind.None)
        {
            return;
        }

        _selectedHubSocketButtonIndex = -1;
        _hubSocketOverlay?.QueueRedraw();
    }

    private void PopulateHubEssenceSocketMenu(EquipmentItem item)
    {
        if (_hubSocketMenu == null || _inventoryManager == null)
        {
            return;
        }

        if (!item.HasEssenceSlotUnlocked)
        {
            AddDisabledHubSocketMenuItem("Essence slot locked");
            return;
        }

        if (item.HasEquippedEssence && item.Essence != null)
        {
            _hubSocketMenu.AddItem($"Unequip {item.Essence.DisplayName}", HubSocketMenuReturnEssence);
            if (_inventoryManager.StoredEssences.Count > 0)
            {
                _hubSocketMenu.AddSeparator();
            }
        }

        if (_inventoryManager.StoredEssences.Count == 0)
        {
            AddDisabledHubSocketMenuItem("No stored essences");
            return;
        }

        List<HubEssenceMenuGroup> groups = BuildHubEssenceMenuGroups(item);
        if (groups.Count == 0)
        {
            AddDisabledHubSocketMenuItem(item.HasEquippedEssence
                ? "No alternate stored essences"
                : "No stored essences");
            return;
        }

        AddDisabledHubSocketMenuItem(item.HasEquippedEssence ? "Replace With" : "Install Essence");
        _hubSocketMenu.AddSeparator();

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            HubEssenceMenuGroup group = groups[groupIndex];
            _hubSocketMenu.AddItem(
                BuildHubEssenceMenuOptionLabel(group),
                HubSocketMenuInstallEssenceBase + group.RepresentativeStoredIndex);
        }
    }

    private List<HubEssenceMenuGroup> BuildHubEssenceMenuGroups(EquipmentItem item)
    {
        List<HubEssenceMenuGroup> groups = new();
        if (_inventoryManager == null)
        {
            return groups;
        }

        Dictionary<string, HubEssenceMenuGroup> groupsBySignature = new(StringComparer.Ordinal);
        string? equippedSignature = item.HasEquippedEssence && item.Essence != null
            ? BuildHubEssenceGroupSignature(item.Essence)
            : null;

        for (int storedIndex = 0; storedIndex < _inventoryManager.StoredEssences.Count; storedIndex++)
        {
            ModifierData essence = _inventoryManager.StoredEssences[storedIndex];
            string signature = BuildHubEssenceGroupSignature(essence);
            if (item.HasEquippedEssence && string.Equals(signature, equippedSignature, StringComparison.Ordinal))
            {
                continue;
            }

            if (groupsBySignature.TryGetValue(signature, out HubEssenceMenuGroup? existingGroup))
            {
                existingGroup.Count++;
                continue;
            }

            HubEssenceMenuGroup group = new(essence, storedIndex);
            groupsBySignature.Add(signature, group);
            groups.Add(group);
        }

        groups.Sort(static (left, right) => CompareHubEssenceMenuGroups(left, right));
        return groups;
    }

    private static int CompareHubEssenceMenuGroups(HubEssenceMenuGroup left, HubEssenceMenuGroup right)
    {
        int nameComparison = string.Compare(left.Representative.DisplayName, right.Representative.DisplayName, StringComparison.OrdinalIgnoreCase);
        if (nameComparison != 0)
        {
            return nameComparison;
        }

        return right.Count.CompareTo(left.Count);
    }

    private static string BuildHubEssenceGroupSignature(ModifierData essence)
    {
        return string.Join(
            "|",
            essence.Key,
            essence.DisplayName,
            essence.HpMultiplier.ToString("R", CultureInfo.InvariantCulture),
            essence.DamageMultiplier.ToString("R", CultureInfo.InvariantCulture),
            essence.XpMultiplierBonus.ToString("R", CultureInfo.InvariantCulture),
            essence.PrimaryEffectKey ?? string.Empty,
            essence.PrimaryEffectDescription ?? string.Empty,
            essence.PassiveName ?? string.Empty,
            essence.PassiveDescription ?? string.Empty,
            essence.SynergyName ?? string.Empty,
            essence.SynergyDescription ?? string.Empty,
            essence.SuperSynergyName ?? string.Empty,
            essence.SuperSynergyDescription ?? string.Empty,
            (int)essence.GuaranteedSubstat,
            essence.GrantsAllSubstats,
            essence.IsWildcard);
    }

    private static string BuildHubEssenceMenuOptionLabel(HubEssenceMenuGroup group)
    {
        ModifierData essence = group.Representative;
        string countSuffix = group.Count > 1 ? $" x{group.Count}" : string.Empty;
        string summary = BuildHubEssenceMenuSummary(essence);
        return string.IsNullOrWhiteSpace(summary)
            ? essence.DisplayName + countSuffix
            : $"{essence.DisplayName}{countSuffix} | {summary}";
    }

    private static string BuildHubEssenceMenuSummary(ModifierData essence)
    {
        if (!string.IsNullOrWhiteSpace(essence.PrimaryEffectDescription))
        {
            return BuildHubMenuSummaryText(essence.PrimaryEffectDescription, 64);
        }

        if (!string.IsNullOrWhiteSpace(essence.PassiveDescription))
        {
            return BuildHubMenuSummaryText(essence.PassiveDescription, 64);
        }

        if (!Mathf.IsEqualApprox(essence.DamageMultiplier, 1f) || !Mathf.IsEqualApprox(essence.HpMultiplier, 1f))
        {
            return $"DMG x{essence.DamageMultiplier:0.##}, HP x{essence.HpMultiplier:0.##}";
        }

        return essence.IsWildcard ? "Wildcard essence" : string.Empty;
    }

    private static string BuildHubMenuSummaryText(string text, int maxLength)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        int sentenceEnd = trimmed.IndexOf('.');
        if (sentenceEnd > 0)
        {
            trimmed = trimmed[..sentenceEnd];
        }

        trimmed = trimmed.Replace("\n", " ", StringComparison.Ordinal).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)] + "...";
    }

    private void PopulateHubModulatorSocketMenu(EquipmentItem item, int socketIndex)
    {
        if (_hubSocketMenu == null || _inventoryManager == null)
        {
            return;
        }

        if (socketIndex < 0 || socketIndex >= item.UnlockedModulatorSlots)
        {
            AddDisabledHubSocketMenuItem("Modulator slot locked");
            return;
        }

        bool installed = item.Modulators != null && socketIndex < item.Modulators.Count;
        if (installed)
        {
            WeaponModulatorData installedModulator = item.Modulators![socketIndex];
            _hubSocketMenu.AddItem($"Unequip {installedModulator.DisplayName}", HubSocketMenuReturnModulator);
            if (_inventoryManager.StoredModulators.Count > 0)
            {
                _hubSocketMenu.AddSeparator();
            }
        }
        else if (item.AvailableModulatorSlots <= 0)
        {
            AddDisabledHubSocketMenuItem("No open modulator slots");
            return;
        }

        List<HubModulatorMenuGroup> groups = BuildHubModulatorMenuGroups(item, socketIndex, installed);
        if (groups.Count == 0)
        {
            if (!installed || _inventoryManager.StoredModulators.Count > 0)
            {
                AddDisabledHubSocketMenuItem("No compatible stored modulators");
            }

            return;
        }

        AddDisabledHubSocketMenuItem(installed ? "Replace With" : "Install Modulator");
        _hubSocketMenu.AddSeparator();

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            HubModulatorMenuGroup group = groups[groupIndex];
            int actionId = (installed ? HubSocketMenuReplaceModulatorBase : HubSocketMenuInstallModulatorBase) + group.RepresentativeStoredIndex;
            _hubSocketMenu.AddItem(BuildHubModulatorMenuOptionLabel(group), actionId);
        }
    }

    private List<HubModulatorMenuGroup> BuildHubModulatorMenuGroups(EquipmentItem item, int socketIndex, bool installed)
    {
        List<HubModulatorMenuGroup> groups = new();
        if (_inventoryManager == null)
        {
            return groups;
        }

        Dictionary<string, HubModulatorMenuGroup> groupsBySignature = new(StringComparer.Ordinal);
        for (int storedIndex = 0; storedIndex < _inventoryManager.StoredModulators.Count; storedIndex++)
        {
            int replacingIndex = installed ? socketIndex : -1;
            if (!_inventoryManager.CanInstallStoredModulator(item.InstanceId, storedIndex, replacingIndex))
            {
                continue;
            }

            WeaponModulatorData modulator = _inventoryManager.StoredModulators[storedIndex];
            string signature = BuildHubModulatorGroupSignature(modulator);
            if (groupsBySignature.TryGetValue(signature, out HubModulatorMenuGroup? existingGroup))
            {
                existingGroup.Count++;
                continue;
            }

            HubModulatorMenuGroup group = new(modulator, storedIndex);
            groupsBySignature.Add(signature, group);
            groups.Add(group);
        }

        groups.Sort(static (left, right) => CompareHubModulatorMenuGroups(left, right));
        return groups;
    }

    private static int CompareHubModulatorMenuGroups(HubModulatorMenuGroup left, HubModulatorMenuGroup right)
    {
        int wingComparison = left.Representative.IsWingModulator.CompareTo(right.Representative.IsWingModulator);
        if (wingComparison != 0)
        {
            return wingComparison;
        }

        int nameComparison = string.Compare(left.Representative.DisplayName, right.Representative.DisplayName, StringComparison.OrdinalIgnoreCase);
        if (nameComparison != 0)
        {
            return nameComparison;
        }

        return right.Count.CompareTo(left.Count);
    }

    private static string BuildHubModulatorGroupSignature(WeaponModulatorData modulator)
    {
        string signature = modulator.NormalizedKey + "|" + modulator.Substats.Count;
        for (int substatIndex = 0; substatIndex < modulator.Substats.Count; substatIndex++)
        {
            SubstatRoll substat = modulator.Substats[substatIndex];
            signature += "|" + (int)substat.Type + ":" + substat.Value.ToString("R", CultureInfo.InvariantCulture);
        }

        return signature;
    }

    private static string BuildHubModulatorMenuOptionLabel(HubModulatorMenuGroup group)
    {
        WeaponModulatorData modulator = group.Representative;
        string countSuffix = group.Count > 1 ? $" x{group.Count}" : string.Empty;
        string summary = BuildHubModulatorMenuSummary(modulator);
        return string.IsNullOrWhiteSpace(summary)
            ? modulator.DisplayName + countSuffix
            : $"{modulator.DisplayName}{countSuffix} | {summary}";
    }

    private static string BuildHubModulatorMenuSummary(WeaponModulatorData modulator)
    {
        if (modulator.IsWingModulator)
        {
            return "Wing support";
        }

        if (modulator.Substats.Count == 0)
        {
            string description = string.IsNullOrWhiteSpace(modulator.BaseDescription)
                ? modulator.Description
                : modulator.BaseDescription;
            return BuildHubMenuSummaryText(description, 56);
        }

        int visibleCount = Math.Min(2, modulator.Substats.Count);
        string summary = string.Empty;
        for (int substatIndex = 0; substatIndex < visibleCount; substatIndex++)
        {
            if (substatIndex > 0)
            {
                summary += ", ";
            }

            summary += BuildHubCompactModulatorSubstatSummary(modulator.Substats[substatIndex]);
        }

        if (modulator.Substats.Count > visibleCount)
        {
            summary += $", +{modulator.Substats.Count - visibleCount} more";
        }

        return summary;
    }

    private static string BuildHubCompactModulatorSubstatSummary(SubstatRoll roll)
    {
        string label = roll.Type switch
        {
            SubstatType.DamagePercent => "Dmg",
            SubstatType.HealthPercent => "HP",
            SubstatType.DodgeChance => "Dodge",
            SubstatType.LifeStealPercent => "LifeSteal",
            SubstatType.HealthRegen => "Regen",
            SubstatType.CritDamage => "CritDmg",
            SubstatType.CritChance => "Crit",
            SubstatType.MovementSpeed => "Move",
            SubstatType.Range => "Range",
            SubstatType.Luck => "Luck",
            SubstatType.Size => "Size",
            SubstatType.AttackSpeed => "AtkSpd",
            SubstatType.Ricochet => "Ricochet",
            SubstatType.BounceTimes => "Bounce",
            SubstatType.ProjectileCount => "Proj",
            _ => SubstatRoll.ToDisplayName(roll.Type),
        };

        if (SubstatRoll.UsesFlatDisplay(roll.Type))
        {
            float rounded = Mathf.Round(roll.Value);
            string value = Mathf.IsEqualApprox(roll.Value, rounded)
                ? roll.Value.ToString("+0;-0;0")
                : roll.Value.ToString("+0.#;-0.#;0");
            return $"{label} {value}";
        }

        return $"{label} {roll.Value:+0.#;-0.#;0}%";
    }

    private void AddDisabledHubSocketMenuItem(string text)
    {
        if (_hubSocketMenu == null)
        {
            return;
        }

        int itemIndex = _hubSocketMenu.ItemCount;
        _hubSocketMenu.AddItem(text, 0);
        _hubSocketMenu.SetItemDisabled(itemIndex, true);
    }

    private void OnHubSocketMenuIdPressed(long id)
    {
        if (_inventoryManager == null || string.IsNullOrWhiteSpace(_hubSocketMenuItemInstanceId))
        {
            return;
        }

        int action = (int)id;
        bool changed = false;

        if (action == HubSocketMenuReturnEssence)
        {
            changed = _inventoryManager.TryReturnEssenceToStorage(_hubSocketMenuItemInstanceId);
        }
        else if (action == HubSocketMenuReturnModulator)
        {
            changed = _inventoryManager.TryReturnModulatorToStorage(_hubSocketMenuItemInstanceId, _hubSocketMenuAnchor.SocketIndex);
        }
        else if (action >= HubSocketMenuReplaceModulatorBase)
        {
            changed = _inventoryManager.TryReplaceStoredModulator(
                _hubSocketMenuItemInstanceId,
                _hubSocketMenuAnchor.SocketIndex,
                action - HubSocketMenuReplaceModulatorBase);
        }
        else if (action >= HubSocketMenuInstallModulatorBase)
        {
            changed = _inventoryManager.TryInstallStoredModulator(_hubSocketMenuItemInstanceId, action - HubSocketMenuInstallModulatorBase);
        }
        else if (action >= HubSocketMenuInstallEssenceBase)
        {
            changed = _inventoryManager.TryInstallStoredEssence(
                _hubSocketMenuItemInstanceId,
                action - HubSocketMenuInstallEssenceBase,
                replaceExisting: true);
        }

        if (!changed)
        {
            return;
        }

        _hubInlinePickerDirty = true;
        GetLiveHubPreviewController()?.RefreshSlots();
        RefreshHubSocketOverlay();
    }

    private void SubscribeSignals()
    {
        if (_currencyManager != null)
        {
            _currencyManager.RollCurrencyChanged += OnRollCurrencyChanged;
            _currencyManager.FatedRollCurrencyChanged += OnFatedRollCurrencyChanged;
        }

        SubscribeDebugSignals();

        if (_gameStateManager != null)
        {
            _gameStateManager.PlayerIdentityChanged += OnPlayerIdentityChanged;
        }

        if (_inventoryManager != null)
        {
            _inventoryManager.InventoryChanged += OnInventoryDataChanged;
            _inventoryManager.EquippedChanged += OnInventoryDataChanged;
        }

        if (_modalManager != null)
        {
            _modalManager.ModalOpened += OnModalManagerStateChanged;
            _modalManager.ModalClosed += OnModalManagerStateChanged;
            _modalManager.BlockingModalStateChanged += OnBlockingModalStateChanged;
        }
    }

    private void UnsubscribeSignals()
    {
        if (_currencyManager != null)
        {
            _currencyManager.RollCurrencyChanged -= OnRollCurrencyChanged;
            _currencyManager.FatedRollCurrencyChanged -= OnFatedRollCurrencyChanged;
        }

        if (_gameStateManager != null)
        {
            _gameStateManager.PlayerIdentityChanged -= OnPlayerIdentityChanged;
        }

        if (_inventoryManager != null)
        {
            _inventoryManager.InventoryChanged -= OnInventoryDataChanged;
            _inventoryManager.EquippedChanged -= OnInventoryDataChanged;
        }

        if (_modalManager != null)
        {
            _modalManager.ModalOpened -= OnModalManagerStateChanged;
            _modalManager.ModalClosed -= OnModalManagerStateChanged;
            _modalManager.BlockingModalStateChanged -= OnBlockingModalStateChanged;
        }

        if (_debugManager != null && _debugSignalsSubscribed)
        {
            _debugManager.DebugActivated -= OnDebugStateChanged;
            _debugManager.DebugDeactivated -= OnDebugStateChanged;
            _debugManager.ForcedRollChanged -= OnForcedRollChanged;
            _debugSignalsSubscribed = false;
        }

        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview != null)
        {
            hubPreview.SlotClicked -= OnHubPreviewSlotClicked;
        }

        if (_hubPreviewInputSubscribed && _hubPreviewContainer != null && GodotObject.IsInstanceValid(_hubPreviewContainer))
        {
            _hubPreviewContainer.GuiInput -= OnHubPreviewGuiInput;
        }

        _hubPreviewInputSubscribed = false;
    }

    private void EnsureDebugPanelLoaded()
    {
        if (DebugPanelScene == null || _debugPanelInstance != null)
        {
            return;
        }

        Node panelNode = DebugPanelScene.Instantiate();
        if (panelNode is not Control panelControl)
        {
            panelNode.QueueFree();
            GD.PushWarning("DebugPanelScene is not a Control.");
            return;
        }

        AddChild(panelControl);
        _debugPanelInstance = panelControl;
        _debugPanelInstance.Visible = _debugManager?.DebugActive ?? false;
    }

    private void OnRollButtonPressed()
    {
        if (CloseOpenModal(_gachaModalInstance))
        {
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            GachaModalScene,
            () => _gachaModalOpen = true,
            () =>
            {
                _gachaModalOpen = false;
                _gachaModalInstance = null;
            },
            modal =>
            {
                _gachaModalInstance = modal;
                if (modal is GachaModalUI gacha)
                {
                    gacha.RollRequested += useFated => ProcessGachaRoll(gacha, useFated);
                }
            });
    }

    private void OnInventoryButtonPressed()
    {
        if (CloseOpenModal(_inventoryModalInstance))
        {
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            InventoryModalScene,
            () => _inventoryModalOpen = true,
            () =>
            {
                _inventoryModalOpen = false;
                _inventoryModalInstance = null;
            },
            modal =>
            {
                _inventoryModalInstance = modal;
                if (modal is InventoryModalUI inventory)
                {
                    inventory.ConfigureForFullInventory();
                }
            });
    }

    private void OnHubLoadoutSlotPressed(int slotIndex)
    {
        EquipmentItem? equipped = _inventoryManager?.GetEquippedItemAt(slotIndex);
        if (_inventoryModalInstance is InventoryModalUI openInventory && openInventory.IsOpen)
        {
            if (equipped == null)
            {
                openInventory.ConfigureForWeaponSelection(slotIndex);
            }
            else
            {
                openInventory.ConfigureForSocketManagement(slotIndex);
            }

            return;
        }

        CloseAnyOpenHubModal();
        OpenModalFromScene(
            InventoryModalScene,
            () => _inventoryModalOpen = true,
            () =>
            {
                _inventoryModalOpen = false;
                _inventoryModalInstance = null;
            },
            modal =>
            {
                _inventoryModalInstance = modal;
                if (modal is not InventoryModalUI inventory)
                {
                    return;
                }

                if (equipped == null)
                {
                    inventory.ConfigureForWeaponSelection(slotIndex);
                }
                else
                {
                    inventory.ConfigureForSocketManagement(slotIndex);
                }
            });
    }

    private void OnSkillTreePressed()
    {
        if (CloseOpenModal(_skillTreeModalInstance))
        {
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            SkillTreeModalScene,
            () =>
            {
                _skillTreeModalOpen = true;
                EmitSignal(SignalName.SkillTreeRequested);
            },
            () =>
            {
                _skillTreeModalOpen = false;
                _skillTreeModalInstance = null;
            },
            modal => _skillTreeModalInstance = modal);
    }

    private void OnAutoRollPressed()
    {
        if (CloseAnyOpenHubModal())
        {
            return;
        }

        EmitSignal(SignalName.AutoRollRequested);
    }

    private void OnDebugForgePressed()
    {
        if (CloseOpenModal(_debugForgeModalInstance))
        {
            return;
        }

        if (!CanShowDebugTools())
        {
            OnDebugAccessPressed();
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            DebugForgeModalScene,
            () => _debugForgeOpen = true,
            () =>
            {
                _debugForgeOpen = false;
                _debugForgeModalInstance = null;
            },
            modal => _debugForgeModalInstance = modal);
    }

    private void OnArenaButtonPressed()
    {
        LaunchArena();
    }

    private void OnFpsSliceButtonPressed()
    {
        LaunchFpsSlice();
    }

    private void OnDebugArenaButtonPressed()
    {
        if (!IsDebugArenaAvailable())
        {
            RefreshDebugVisibility();
            UpdateArenaButtonVisibility();
            return;
        }

        LaunchArena();
    }

    private async void LaunchArena()
    {
        if (_sceneTransitionInProgress)
        {
            return;
        }

        if (CloseAnyOpenHubModal())
        {
            return;
        }

        EmitSignal(SignalName.ArenaRequested);
        SetArenaActive(true);

        if (string.IsNullOrWhiteSpace(ArenaScenePath))
        {
            return;
        }

        if (!FileAccess.FileExists(ArenaScenePath))
        {
            GD.PushWarning($"Arena scene not found: {ArenaScenePath}");
            return;
        }

        _sceneTransitionInProgress = true;
        PrepareHubPreviewForSceneTransition();
        await FlushSceneTransitionFrames(SceneTransitionFlushFrames);

        Error err = SwitchSceneByInstantiation(ArenaScenePath);
        if (err != Error.Ok)
        {
            _sceneTransitionInProgress = false;
            RestoreHubPreviewAfterTransitionFailure();
            GD.PushWarning($"Failed to change to arena scene: {err}");
        }
    }

    private async void LaunchFpsSlice()
    {
        if (_sceneTransitionInProgress)
        {
            return;
        }

        if (CloseAnyOpenHubModal())
        {
            return;
        }

        SetArenaActive(true);

        if (string.IsNullOrWhiteSpace(FpsSliceScenePath))
        {
            return;
        }

        if (!FileAccess.FileExists(FpsSliceScenePath))
        {
            GD.PushWarning($"FPS slice scene not found: {FpsSliceScenePath}");
            return;
        }

        _sceneTransitionInProgress = true;
        PrepareHubPreviewForSceneTransition();
        await FlushSceneTransitionFrames(SceneTransitionFlushFrames);

        Error err = SwitchSceneByInstantiation(FpsSliceScenePath);
        if (err != Error.Ok)
        {
            _sceneTransitionInProgress = false;
            RestoreHubPreviewAfterTransitionFailure();
            GD.PushWarning($"Failed to change to FPS slice scene: {err}");
        }
    }

    private async Task FlushSceneTransitionFrames(int frameCount)
    {
        int frames = Mathf.Max(1, frameCount);
        for (int frameIndex = 0; frameIndex < frames; frameIndex++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private Error SwitchSceneByInstantiation(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            return Error.InvalidParameter;
        }

        PackedScene? packedScene = ResourceLoader.Load<PackedScene>(scenePath);
        if (packedScene == null)
        {
            return Error.CantOpen;
        }

        Node? nextScene = packedScene.Instantiate();
        if (nextScene == null)
        {
            return Error.Failed;
        }

        SceneTree tree = GetTree();
        Window? root = tree.Root;
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            nextScene.QueueFree();
            return Error.Unavailable;
        }

        Node? previousScene = tree.CurrentScene;
        root.AddChild(nextScene);
        tree.CurrentScene = nextScene;

        if (previousScene != null && GodotObject.IsInstanceValid(previousScene) && !ReferenceEquals(previousScene, nextScene))
        {
            PrepareSceneForRetirement(previousScene);
            ScheduleSceneRetirement(tree, previousScene);
        }

        return Error.Ok;
    }

    private static void PrepareSceneForRetirement(Node retiredScene)
    {
        DeactivateSceneTree(retiredScene);
        StripSceneRenderNodes(retiredScene);
    }

    private static void ScheduleSceneRetirement(SceneTree tree, Node retiredScene)
    {
        _ = RetireSceneAfterFrames(tree, retiredScene, SceneRetirementDelayFrames);
    }

    private static async Task RetireSceneAfterFrames(SceneTree tree, Node retiredScene, int frameDelay)
    {
        try
        {
            int delayFrames = Mathf.Max(1, frameDelay);
            for (int frameIndex = 0; frameIndex < delayFrames; frameIndex++)
            {
                if (tree == null || !GodotObject.IsInstanceValid(tree)
                    || retiredScene == null || !GodotObject.IsInstanceValid(retiredScene)
                    || retiredScene.IsQueuedForDeletion())
                {
                    return;
                }

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            if (retiredScene != null && GodotObject.IsInstanceValid(retiredScene) && !retiredScene.IsQueuedForDeletion())
            {
                retiredScene.QueueFree();
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Scene retirement failed: {exception.Message}");
        }
    }

    private static void DeactivateSceneTree(Node node)
    {
        node.ProcessMode = ProcessModeEnum.Disabled;

        if (node is CanvasItem canvasItem)
        {
            canvasItem.Visible = false;
        }
        else if (node is Node3D node3D)
        {
            node3D.Visible = false;
        }

        foreach (Node child in node.GetChildren())
        {
            DeactivateSceneTree(child);
        }
    }

    private static void StripSceneRenderNodes(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            StripSceneRenderNodes(child);
        }

        if (node is VisualInstance3D || node is WorldEnvironment)
        {
            node.QueueFree();
        }
    }

    private void PrepareHubPreviewForSceneTransition()
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview != null)
        {
            hubPreview.PrepareForSceneTransition();
            hubPreview.ProcessMode = ProcessModeEnum.Disabled;
        }

        if (_hubSocketOverlay != null && GodotObject.IsInstanceValid(_hubSocketOverlay))
        {
            _hubSocketOverlay.Visible = false;
        }

        if (_hubPreviewContainer != null && GodotObject.IsInstanceValid(_hubPreviewContainer))
        {
            _hubPreviewContainer.Visible = false;
            _hubPreviewContainer.ProcessMode = ProcessModeEnum.Disabled;
        }

        if (_hubPreviewViewport != null && GodotObject.IsInstanceValid(_hubPreviewViewport))
        {
            _hubPreviewViewport.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    private void RestoreHubPreviewAfterTransitionFailure()
    {
        if (_hubPreviewContainer != null && GodotObject.IsInstanceValid(_hubPreviewContainer))
        {
            _hubPreviewContainer.Visible = true;
            _hubPreviewContainer.ProcessMode = ProcessModeEnum.Inherit;
        }

        if (_hubPreviewViewport != null && GodotObject.IsInstanceValid(_hubPreviewViewport))
        {
            _hubPreviewViewport.ProcessMode = ProcessModeEnum.Inherit;
        }

        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview != null)
        {
            hubPreview.ProcessMode = ProcessModeEnum.Inherit;
            hubPreview.SetProcess(true);
            hubPreview.RefreshSlots();
        }

        RefreshHubSocketOverlay();
        UpdateHubPreviewAnimationState();
    }

    private void OnSettingsPressed()
    {
        if (CloseOpenModal(_settingsModalInstance))
        {
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            SettingsModalScene,
            () => _settingsModalOpen = true,
            () =>
            {
                _settingsModalOpen = false;
                _settingsModalInstance = null;
            },
            modal => _settingsModalInstance = modal);
    }

    private void OnStatsPressed()
    {
        if (CloseOpenModal(_statsModalInstance))
        {
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            StatsModalScene,
            () => _statsModalOpen = true,
            () =>
            {
                _statsModalOpen = false;
                _statsModalInstance = null;
            },
            modal => _statsModalInstance = modal);
    }

    private void OnPlayerIdentityPressed()
    {
        if (CloseOpenModal(_playerIdentityModalInstance))
        {
            return;
        }

        CloseAnyOpenHubModal();

        OpenModalFromScene(
            PlayerIdentityModalScene,
            () => _playerIdentityModalOpen = true,
            () =>
            {
                _playerIdentityModalOpen = false;
                _playerIdentityModalInstance = null;
            },
            modal =>
            {
                _playerIdentityModalInstance = modal;
                if (modal is PlayerIdentityModalUI identityModal)
                {
                    identityModal.PlayerIdentityChanged += (_, _) => RefreshIdentityUI();
                    identityModal.SettingsRequested += OnSettingsPressed;
                }
            });
    }

    private void OnDebugAccessPressed()
    {
        if (CloseAnyOpenHubModal())
        {
            return;
        }

        if (_debugManager == null)
        {
            _debugManager = ResolveNode<DebugManager>(DebugManagerPath, "DebugManager");
            if (_debugManager == null)
            {
                GD.PushWarning("DebugManager not found.");
                return;
            }

            SubscribeDebugSignals();
        }

        if (_debugManager.DebugActive)
        {
            EnsureDebugPanelLoaded();
            RefreshDebugVisibility();
            return;
        }

        // In editor/dev builds, allow one-click debug activation.
        if (_debugManager.CanUseDevelopmentDebug)
        {
            _debugManager.ActivateDebug();
            EnsureDebugPanelLoaded();
            RefreshDebugVisibility();
            return;
        }

        OpenModalFromScene(DebugPasswordModalScene, () => { }, () => { });
    }

    private void SubscribeDebugSignals()
    {
        if (_debugManager == null || _debugSignalsSubscribed)
        {
            return;
        }

        _debugManager.DebugActivated += OnDebugStateChanged;
        _debugManager.DebugDeactivated += OnDebugStateChanged;
        _debugManager.ForcedRollChanged += OnForcedRollChanged;
        _debugSignalsSubscribed = true;
    }

    private void OpenModalFromScene(PackedScene? scene, Action onOpened, Action onClosed, Action<ModalBase>? onModalInstantiated = null)
    {
        if (scene == null)
        {
            GD.PushWarning("Modal scene not assigned.");
            return;
        }

        Node modalNode = scene.Instantiate();
        if (modalNode is not ModalBase modal)
        {
            GD.PushWarning("Scene does not use ModalBase. Falling back to plain Control visibility.");
            if (modalNode is Control fallback)
            {
                AddChild(fallback);
                fallback.Visible = true;
            }

            return;
        }

        AddChild(modal);
        modal.BlockingModal = false;
        modal.ZIndex = 100;
        onModalInstantiated?.Invoke(modal);

        onOpened();

        modal.Closed += () =>
        {
            onClosed();
            UpdateArenaButtonVisibility();
            UpdateHubPreviewAnimationState();

            if (IsInstanceValid(modal))
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

        UpdateArenaButtonVisibility();
        UpdateHubPreviewAnimationState();
        MoveHubChromeToFront();
        modal.ZIndex = 100;
        modal.MoveToFront();
    }

    private bool CloseOpenModal(ModalBase? modal)
    {
        if (modal == null || !IsInstanceValid(modal) || !modal.IsOpen)
        {
            return false;
        }

        if (_modalManager != null)
        {
            _modalManager.CloseModal(modal);
        }
        else
        {
            modal.Close();
        }

        return true;
    }

    private bool CloseAnyOpenHubModal()
    {
        bool closed = false;
        closed |= CloseOpenModal(_gachaModalInstance);
        closed |= CloseOpenModal(_inventoryModalInstance);
        closed |= CloseOpenModal(_skillTreeModalInstance);
        closed |= CloseOpenModal(_settingsModalInstance);
        closed |= CloseOpenModal(_statsModalInstance);
        closed |= CloseOpenModal(_playerIdentityModalInstance);
        closed |= CloseOpenModal(_debugForgeModalInstance);
        return closed;
    }

    private void MoveHubChromeToFront()
    {
        MoveControlToFront(PlayerIdentityButton, 40);
        MoveControlToFront(ArenaButton, 40);
        MoveControlToFront(DebugArenaButton, 40);
        MoveControlToFront(_hubSocketOverlay, 30);
        MoveControlToFront(GetNodeOrNull<Control>("TopRow/CurrencyRow"), 40);
        MoveControlToFront(GetNodeOrNull<Control>("ControlsBackdrop"), 35);
        MoveControlToFront(GetNodeOrNull<Control>("BottomBar"), 40);
        MoveControlToFront(DebugIndicator, 40);
    }

    private void MoveControlToFront(Control? control, int zIndex)
    {
        if (control == null || control.GetParent() != this)
        {
            return;
        }

        control.ZIndex = zIndex;
        control.MoveToFront();
    }

    private void ProcessGachaRoll(GachaModalUI modal, bool useFated)
    {
        if (_currencyManager == null)
        {
            SetGachaFeedback(modal, "Currency manager not found.");
            return;
        }

        if (_inventoryManager == null)
        {
            SetGachaFeedback(modal, "Inventory manager not found.");
            return;
        }

        if (_inventoryManager.IsInventoryFull)
        {
            SetGachaFeedback(modal, "Inventory is full.");
            return;
        }

        if (_gachaRoller == null || _rarityPool.Count == 0 || _shapePool.Count == 0 || _modifierPool.Count == 0)
        {
            SetGachaFeedback(modal, "Gacha data missing.");
            return;
        }

        bool consumed = useFated
            ? _currencyManager.TryConsumeFatedRollCurrency(1)
            : _currencyManager.TryConsumeRollCurrency(1);

        if (!consumed)
        {
            SetGachaFeedback(modal, useFated ? "No fated rolls available." : "No rolls available.");
            return;
        }

        int level = Mathf.Max(1, _gameStateManager?.PlayerLevel ?? 1);
        EquipmentItem rolled = _gachaRoller.RollItem(_shapePool, _rarityPool, _modifierPool, level);

        if (!_inventoryManager.TryAddItem(rolled))
        {
            // Refund currency when item delivery fails.
            if (useFated)
            {
                _currencyManager.AddFatedRollCurrency(1);
            }
            else
            {
                _currencyManager.AddRollCurrency(1);
            }

            SetGachaFeedback(modal, "Failed to add rolled item to inventory.");
            return;
        }

        _gameStateManager?.RegisterObservedRoll(
            rolled.Rarity?.Key ?? string.Empty,
            rolled.Shape?.Key ?? string.Empty,
            rolled.Modifier?.Key.ToString().ToLowerInvariant() ?? string.Empty);
        _gameStateManager?.SaveProgress();

        string rarity = rolled.Rarity?.DisplayName ?? "Unknown";
        string shape = rolled.Shape?.DisplayName ?? "Unknown";
        string modifier = rolled.Modifier?.DisplayName ?? (rolled.HasEssenceSlotUnlocked ? "Empty Essence" : "No Essence");

        SetGachaFeedback(modal, $"Rolled: {rarity} {shape} ({modifier})");
        modal.ShowRolledItem(rolled);
        GetLiveHubPreviewController()?.RefreshSlots();
    }

    private static void SetGachaFeedback(GachaModalUI modal, string text)
    {
        if (modal.FeedbackLabel != null)
        {
            modal.FeedbackLabel.Text = text;
        }
    }

    private void OnRollCurrencyChanged(int amount)
    {
        if (RollCurrencyLabel != null)
        {
            RollCurrencyLabel.Text = amount.ToString();
        }
    }

    private void OnFatedRollCurrencyChanged(int amount)
    {
        if (FatedRollCurrencyLabel != null)
        {
            FatedRollCurrencyLabel.Text = amount.ToString();
        }
    }

    private void OnDebugStateChanged()
    {
        RefreshDebugVisibility();
        UpdateArenaButtonVisibility();
    }

    private void OnForcedRollChanged(string rarityKey, string shapeKey, bool hasModifier, int modifierValue)
    {
        _ = rarityKey;
        _ = shapeKey;
        _ = hasModifier;
        _ = modifierValue;

        RefreshDebugVisibility();
    }

    private void OnModalManagerStateChanged(ModalBase modal)
    {
        _ = modal;
        UpdateArenaButtonVisibility();
        UpdateHubPreviewAnimationState();
    }

    private void OnBlockingModalStateChanged(bool isBlocking)
    {
        _ = isBlocking;
        UpdateArenaButtonVisibility();
        UpdateHubPreviewAnimationState();
    }

    private void RefreshCurrencyLabels()
    {
        if (_currencyManager == null)
        {
            return;
        }

        OnRollCurrencyChanged(_currencyManager.RollCurrency);
        OnFatedRollCurrencyChanged(_currencyManager.FatedRollCurrency);
    }

    private void OnPlayerIdentityChanged(string playerName, int avatarId)
    {
        _ = playerName;
        _ = avatarId;

        RefreshIdentityUI();
    }

    private void RefreshIdentityUI()
    {
        if (_gameStateManager == null)
        {
            return;
        }

        if (PlayerNameLabel != null)
        {
            PlayerNameLabel.Text = string.IsNullOrWhiteSpace(_gameStateManager.PlayerName)
                ? "Player"
                : _gameStateManager.PlayerName;
        }

        if (AvatarLabel != null)
        {
            AvatarLabel.Text = $"Avatar {_gameStateManager.AvatarId}";
        }

        if (PlayerIdentityButton != null)
        {
            PlayerIdentityButton.Text = string.Empty;
            RefreshLegacyIdentityCard();
        }
    }

    private void RefreshDebugVisibility()
    {
        bool debugActive = _debugManager?.DebugActive == true;
        bool showDebugTools = CanShowDebugTools();

        if (RollButton != null)
        {
            RollButton.Visible = false;
        }

        if (AutoRollButton != null)
        {
            AutoRollButton.Visible = false;
        }

        if (DebugForgeButton != null)
        {
            DebugForgeButton.Visible = showDebugTools;
            DebugForgeButton.Text = "🔧 Debug Forge";
        }

        if (DebugIndicator != null)
        {
            DebugIndicator.Visible = debugActive;
        }

        if (DebugIndicatorLabel != null)
        {
            DebugIndicatorLabel.Visible = debugActive;
            DebugIndicatorLabel.Text = BuildDebugIndicatorText();
        }

        if (_debugPanelInstance != null)
        {
            _debugPanelInstance.Visible = debugActive;
        }
    }

    private string BuildDebugIndicatorText()
    {
        if (_debugManager == null || !_debugManager.DebugActive)
        {
            return string.Empty;
        }

        string rarity = _debugManager.HasForcedRarity() ? _debugManager.ForcedRarityKey : "random rarity";
        string shape = _debugManager.HasForcedShape() ? _debugManager.ForcedShapeKey : "random shape";
        string modifier = _debugManager.HasForcedModifier()
            ? _debugManager.ForcedModifierKey!.Value.ToString()
            : "random essence";

        return $"DEBUG ACTIVE | {rarity} / {shape} / {modifier}";
    }

    private bool CanShowDebugTools()
    {
        if (_debugManager != null)
        {
            return _debugManager.DebugActive || _debugManager.CanUseDevelopmentDebug;
        }

        return OS.IsDebugBuild();
    }

    private bool IsDebugArenaAvailable()
    {
        return _debugManager?.DebugActive == true;
    }

    private void UpdateArenaButtonVisibility()
    {
        bool blockingModalOpen = _modalManager?.IsAnyBlockingModalOpen() ?? false;

        bool visible = HubVisible
            && !_gachaModalOpen
            && !_inventoryModalOpen
            && !_skillTreeModalOpen
            && !_settingsModalOpen
            && !_statsModalOpen
            && !_playerIdentityModalOpen
            && !_debugForgeOpen
            && !ArenaActive
            && !blockingModalOpen;

        if (ArenaButton != null)
        {
            ArenaButton.Visible = visible;
        }

        if (FpsSliceButton != null)
        {
            FpsSliceButton.Visible = visible;
        }

        if (DebugArenaButton != null)
        {
            DebugArenaButton.Visible = visible && IsDebugArenaAvailable();
        }
    }

    private void UpdateHubPreviewAnimationState()
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview == null)
        {
            return;
        }

        bool blockingModalOpen = _modalManager?.IsAnyBlockingModalOpen() ?? false;
        hubPreview.SetProcess(!blockingModalOpen || hubPreview.IsWeaponInspectionActive);
    }

    private void BindHubPreviewProvider()
    {
        HubSceneController? hubPreview = GetLiveHubPreviewController();
        if (hubPreview == null || _inventoryManager == null)
        {
            return;
        }

        hubPreview.InspectionInputInterceptor = HandleHubInlinePickerInput;
        hubPreview.BindEquipmentProvider(_inventoryManager);
        hubPreview.RefreshSlots();
        hubPreview.SlotClicked -= OnHubPreviewSlotClicked;
        hubPreview.SlotClicked += OnHubPreviewSlotClicked;
    }

    private void OnInventoryDataChanged()
    {
        _hubInlinePickerDirty = true;
        GetLiveHubPreviewController()?.RefreshSlots();
        RefreshHubSocketOverlay();
    }

    private HubSceneController? GetLiveHubPreviewController()
    {
        if (HubPreviewController != null && GodotObject.IsInstanceValid(HubPreviewController))
        {
            return HubPreviewController;
        }

        ResolveHubPreviewReferences();
        return HubPreviewController != null && GodotObject.IsInstanceValid(HubPreviewController)
            ? HubPreviewController
            : null;
    }

    private void ResolveHubPreviewReferences()
    {
        if (HubPreviewController == null || !GodotObject.IsInstanceValid(HubPreviewController))
        {
            HubPreviewController = GetNodeOrNull<HubSceneController>("Center/PreviewContainer/PreviewViewport/HubPreview");
        }

        if (_hubPreviewContainer == null || !GodotObject.IsInstanceValid(_hubPreviewContainer))
        {
            _hubPreviewContainer = GetNodeOrNull<SubViewportContainer>("Center/PreviewContainer");
            _hubPreviewInputSubscribed = false;
        }

        if (_hubPreviewViewport == null || !GodotObject.IsInstanceValid(_hubPreviewViewport))
        {
            _hubPreviewViewport = GetNodeOrNull<SubViewport>("Center/PreviewContainer/PreviewViewport");
        }

        if (_hubSocketOverlay == null || !GodotObject.IsInstanceValid(_hubSocketOverlay))
        {
            _hubSocketOverlay = GetNodeOrNull<Control>("HubSocketOverlay");
            _hubSocketOverlayDrawSubscribed = false;
        }
    }

    private void BuildGachaPools()
    {
        _rarityPool.Clear();
        _shapePool.Clear();
        _modifierPool.Clear();

        LoadResourcesFromFolder(RarityDataFolder, _rarityPool);
        LoadResourcesFromFolder(ShapeDataFolder, _shapePool);
        LoadResourcesFromFolder(ModifierDataFolder, _modifierPool);

        if (_rarityPool.Count == 0)
        {
            AddDefaultRarities(_rarityPool);
        }

        if (_shapePool.Count == 0)
        {
            AddDefaultShapes(_shapePool);
        }

        if (_modifierPool.Count == 0)
        {
            AddDefaultModifiers(_modifierPool);
        }
    }

    private static void LoadResourcesFromFolder<T>(string folderPath, List<T> output) where T : Resource
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        if (!DirAccess.DirExistsAbsolute(folderPath))
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

            string fullPath = $"{folderPath}/{file}";
            Resource? loaded = ResourceLoader.Load(fullPath);
            if (loaded is T typed)
            {
                output.Add(typed);
            }
        }
    }

    private static void AddDefaultRarities(List<RarityData> output)
    {
        output.AddRange(EquipmentCatalogDefaults.CreateDefaultRarities());
    }

    private static void AddDefaultShapes(List<ShapeData> output)
    {
        output.AddRange(EquipmentCatalogDefaults.CreateDefaultShapes());
    }

    private static void AddDefaultModifiers(List<ModifierData> output)
    {
        output.AddRange(EquipmentCatalogDefaults.CreateDefaultModifiers(includeBaselineNormal: true));
    }

    private T? ResolveNode<T>(NodePath nodePath, string autoloadName) where T : class
    {
        if (!nodePath.IsEmpty)
        {
            Node? found = GetNodeOrNull(nodePath);
            if (found is T typedByPath)
            {
                return typedByPath;
            }
        }

        Node? autoloadNode = GetNodeOrNull($"/root/{autoloadName}");
        return autoloadNode as T;
    }
}
