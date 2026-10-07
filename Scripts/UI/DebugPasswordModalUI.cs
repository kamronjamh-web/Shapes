using Godot;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Password gate to activate debug mode in non-development runs.
public partial class DebugPasswordModalUI : ModalBase
{
    [Signal]
    public delegate void DebugAccessGrantedEventHandler();

    [Export] public LineEdit? PasswordInput;
    [Export] public Button? OkButton;
    [Export] public Button? CancelButton;
    [Export] public Label? FeedbackLabel;

    [Export] public NodePath DebugManagerPath = new NodePath();

    private DebugManager? _debugManager;

    public override void _Ready()
    {
        base._Ready();

        ResolveUiReferences();
        ResolveDebugManager();

        if (OkButton != null)
        {
            OkButton.Pressed += TrySubmitPassword;
        }

        if (CancelButton != null)
        {
            CancelButton.Pressed += Close;
        }

        if (PasswordInput != null)
        {
            PasswordInput.Secret = true;
            PasswordInput.TextSubmitted += _ => TrySubmitPassword();
        }
    }

    public override void Open()
    {
        base.Open();

        if (_debugManager == null)
        {
            ResolveDebugManager();
        }

        if (PasswordInput != null)
        {
            PasswordInput.Text = string.Empty;
            PasswordInput.GrabFocus();
        }

        SetFeedback(string.Empty);
    }

    private void ResolveDebugManager()
    {
        if (!DebugManagerPath.IsEmpty)
        {
            _debugManager = GetNodeOrNull(DebugManagerPath) as DebugManager;
        }

        _debugManager ??= GetNodeOrNull("/root/DebugManager") as DebugManager;
    }

    private void ResolveUiReferences()
    {
        PasswordInput ??= GetNodeOrNull<LineEdit>("ModalPanel/VBox/PasswordInput");
        OkButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/OkButton");
        CancelButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/CancelButton");
        FeedbackLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/FeedbackLabel");
    }

    private void TrySubmitPassword()
    {
        if (_debugManager == null)
        {
            ResolveDebugManager();
        }

        if (_debugManager == null)
        {
            SetFeedback("Debug manager not found.");
            return;
        }

        string input = PasswordInput?.Text?.Trim() ?? string.Empty;

        if (_debugManager.TryActivateWithPassword(input))
        {
            SetFeedback("Debug access enabled.");
            EmitSignal(SignalName.DebugAccessGranted);
            Close();
            return;
        }

        SetFeedback("Wrong password.");

        if (PasswordInput != null)
        {
            PasswordInput.Text = string.Empty;
            PasswordInput.GrabFocus();
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
