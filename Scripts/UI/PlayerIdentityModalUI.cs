using Godot;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Modal for editing player display identity in the hub.
public partial class PlayerIdentityModalUI : ModalBase
{
    [Signal]
    public delegate void PlayerIdentityChangedEventHandler(string playerName, int avatarId);

    [Signal]
    public delegate void SettingsRequestedEventHandler();

    [Export] public LineEdit? PlayerNameInput;
    [Export] public OptionButton? AvatarDropdown;
    [Export] public Label? FeedbackLabel;
    [Export] public Button? SettingsButton;
    [Export] public CheckButton? DebugModeToggle;
    [Export] public Button? SaveButton;
    [Export] public Button? CancelButton;
    [Export] public NodePath IdentityServicePath = new NodePath();
    [Export] public NodePath DebugManagerPath = new NodePath();

    private const string SaveFilePath = "user://player_identity.cfg";

    private IPlayerIdentityService? _identityService;
    private DebugManager? _debugManager;
    private bool _suppressChanges;
    private bool _suppressDebugToggle;
    private bool _debugSignalsSubscribed;

    public override void _Ready()
    {
        base._Ready();

        ResolveIdentityService();
        ResolveDebugManager();
        ResolveSettingsButton();
        ResolveDebugModeToggle();
        EnsureAvatarOptions();
        WireControls();
        LoadIdentity();
        RefreshDebugToggle();
    }

    public override void _ExitTree()
    {
        UnsubscribeDebugSignals();
        base._ExitTree();
    }

    public override void Open()
    {
        base.Open();
        LoadIdentity();
        RefreshDebugToggle();
    }

    private void ResolveIdentityService()
    {
        if (!IdentityServicePath.IsEmpty)
        {
            Node? node = GetNodeOrNull(IdentityServicePath);
            if (node is IPlayerIdentityService typed)
            {
                _identityService = typed;
            }
        }

        _identityService ??= GetNodeOrNull("/root/GameStateManager") as IPlayerIdentityService;
        _identityService ??= GetNodeOrNull("/root/SaveManager") as IPlayerIdentityService;
    }

    private void ResolveDebugManager()
    {
        if (!DebugManagerPath.IsEmpty)
        {
            Node? node = GetNodeOrNull(DebugManagerPath);
            if (node is DebugManager typed)
            {
                _debugManager = typed;
            }
        }

        _debugManager ??= GetNodeOrNull("/root/DebugManager") as DebugManager;
        SubscribeDebugSignals();
    }

    private void EnsureAvatarOptions()
    {
        if (AvatarDropdown == null || AvatarDropdown.ItemCount > 0)
        {
            return;
        }

        AvatarDropdown.AddItem("Default", 0);
        AvatarDropdown.AddItem("Knight", 1);
        AvatarDropdown.AddItem("Mage", 2);
        AvatarDropdown.AddItem("Rogue", 3);
        AvatarDropdown.AddItem("Mech", 4);
    }

    private void ResolveSettingsButton()
    {
        SettingsButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/TopRow/SettingsButton");
    }

    private void ResolveDebugModeToggle()
    {
        DebugModeToggle ??= GetNodeOrNull<CheckButton>("ModalPanel/VBox/TopRow/DebugModeToggle");
    }

    private void WireControls()
    {
        if (PlayerNameInput != null)
        {
            PlayerNameInput.TextChanged += OnPlayerNameChanged;
        }

        if (AvatarDropdown != null)
        {
            AvatarDropdown.ItemSelected += OnAvatarSelected;
        }

        if (SaveButton != null)
        {
            SaveButton.Pressed += SaveAndClose;
        }

        if (SettingsButton != null)
        {
            SettingsButton.Pressed += OnSettingsButtonPressed;
        }

        if (DebugModeToggle != null)
        {
            DebugModeToggle.Pressed += OnDebugModeTogglePressed;
        }

        if (CancelButton != null)
        {
            CancelButton.Pressed += Close;
        }
    }

    private void LoadIdentity()
    {
        string name;
        int avatarId;

        if (_identityService != null)
        {
            name = _identityService.PlayerName;
            avatarId = _identityService.AvatarId;
        }
        else
        {
            (name, avatarId) = ReadIdentityFromConfig();
        }

        _suppressChanges = true;

        if (PlayerNameInput != null)
        {
            PlayerNameInput.Text = string.IsNullOrWhiteSpace(name) ? "Player" : name;
        }

        if (AvatarDropdown != null && AvatarDropdown.ItemCount > 0)
        {
            int clamped = Mathf.Clamp(avatarId, 0, AvatarDropdown.ItemCount - 1);
            AvatarDropdown.Select(clamped);
        }

        _suppressChanges = false;
        SetFeedback("Identity loaded.");
    }

    private void OnDebugModeTogglePressed()
    {
        if (_suppressDebugToggle)
        {
            return;
        }

        ResolveDebugManager();
        if (_debugManager == null)
        {
            SetFeedback("Debug manager not found.");
            RefreshDebugToggle();
            return;
        }

        bool requestedActive = DebugModeToggle?.ButtonPressed == true;
        if (requestedActive)
        {
            if (_debugManager.DebugActive)
            {
                SetFeedback("Debug mode already enabled.");
            }
            else if (_debugManager.CanUseDevelopmentDebug)
            {
                _debugManager.ActivateDebug();
                SetFeedback("Debug mode enabled.");
            }
            else
            {
                SetFeedback("Debug toggle is available in development builds.");
            }
        }
        else
        {
            if (_debugManager.DebugActive)
            {
                _debugManager.DeactivateDebug();
                SetFeedback("Debug mode disabled.");
            }
        }

        RefreshDebugToggle();
    }

    private void OnPlayerNameChanged(string _) => SaveIdentityImmediate();

    private void OnAvatarSelected(long _) => SaveIdentityImmediate();

    private void SaveIdentityImmediate()
    {
        if (_suppressChanges)
        {
            return;
        }

        string playerName = PlayerNameInput?.Text?.Trim() ?? "Player";
        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = "Player";
        }

        int avatarId = AvatarDropdown?.Selected ?? 0;

        if (_identityService != null)
        {
            _identityService.PlayerName = playerName;
            _identityService.AvatarId = avatarId;
            _identityService.SaveIdentity();
        }
        else
        {
            WriteIdentityToConfig(playerName, avatarId);
        }

        EmitSignal(SignalName.PlayerIdentityChanged, playerName, avatarId);
        SetFeedback("Saved.");
    }

    private void SaveAndClose()
    {
        SaveIdentityImmediate();
        Close();
    }

    private void OnSettingsButtonPressed()
    {
        SaveIdentityImmediate();
        Close();
        CallDeferred(MethodName.EmitDeferredSettingsRequested);
    }

    private void EmitDeferredSettingsRequested()
    {
        EmitSignal(SignalName.SettingsRequested);
    }

    private void SubscribeDebugSignals()
    {
        if (_debugManager == null || _debugSignalsSubscribed)
        {
            return;
        }

        _debugManager.DebugActivated += OnDebugStateChanged;
        _debugManager.DebugDeactivated += OnDebugStateChanged;
        _debugSignalsSubscribed = true;
    }

    private void UnsubscribeDebugSignals()
    {
        if (_debugManager == null || !_debugSignalsSubscribed)
        {
            return;
        }

        _debugManager.DebugActivated -= OnDebugStateChanged;
        _debugManager.DebugDeactivated -= OnDebugStateChanged;
        _debugSignalsSubscribed = false;
    }

    private void OnDebugStateChanged()
    {
        RefreshDebugToggle();
    }

    private void RefreshDebugToggle()
    {
        if (DebugModeToggle == null)
        {
            return;
        }

        bool debugActive = _debugManager?.DebugActive == true;
        bool canToggleOn = _debugManager?.CanUseDevelopmentDebug == true;

        _suppressDebugToggle = true;
        DebugModeToggle.ButtonPressed = debugActive;
        DebugModeToggle.Disabled = _debugManager == null || (!debugActive && !canToggleOn);
        DebugModeToggle.Text = debugActive ? "Debug Mode: On" : "Debug Mode";
        DebugModeToggle.TooltipText = DebugModeToggle.Disabled
            ? "Debug mode can be toggled from here in development builds."
            : "Toggle debug mode for dev tools and the dev arena.";
        _suppressDebugToggle = false;
    }

    private static (string Name, int AvatarId) ReadIdentityFromConfig()
    {
        ConfigFile cfg = new();
        Error error = cfg.Load(SaveFilePath);
        if (error != Error.Ok)
        {
            return ("Player", 0);
        }

        string name = cfg.GetValue("identity", "player_name", "Player").AsString();
        int avatarId = cfg.GetValue("identity", "avatar_id", 0).AsInt32();

        return (name, avatarId);
    }

    private static void WriteIdentityToConfig(string name, int avatarId)
    {
        ConfigFile cfg = new();
        _ = cfg.Load(SaveFilePath);

        cfg.SetValue("identity", "player_name", name);
        cfg.SetValue("identity", "avatar_id", avatarId);

        Error error = cfg.Save(SaveFilePath);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Failed to save player identity: {error}");
        }
    }

    private void SetFeedback(string text)
    {
        if (FeedbackLabel != null)
        {
            FeedbackLabel.Text = text;
        }
    }
}

// Optional contract for GameStateManager/SaveManager integration.
public interface IPlayerIdentityService
{
    string PlayerName { get; set; }
    int AvatarId { get; set; }
    void SaveIdentity();
}
