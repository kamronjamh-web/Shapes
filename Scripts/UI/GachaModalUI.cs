using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.UI;

// Lightweight gacha modal shell with roll/fated-roll actions.
public partial class GachaModalUI : ModalBase
{
    [Signal]
    public delegate void RollRequestedEventHandler(bool fatedRoll);

    [Export] public Button? RollButton;
    [Export] public Button? FatedRollButton;
    [Export] public Label? FeedbackLabel;
    [Export] public ShapePreviewViewport? ShapePreview;

    [Export] public NodePath CurrencyManagerPath = new NodePath();

    private CurrencyManager? _currencyManager;

    public override void _Ready()
    {
        base._Ready();

        ResolveUiReferences();
        ResolveServices();
        RecalculateModalLayout();

        if (RollButton != null)
        {
            RollButton.Pressed += () => TryRequestRoll(false);
        }

        if (FatedRollButton != null)
        {
            FatedRollButton.Pressed += () => TryRequestRoll(true);
        }
    }

    public void ShowRolledItem(EquipmentItem item)
    {
        ShapePreview?.SetItem(item);
    }

    private void ResolveUiReferences()
    {
        RollButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/RollButton");
        FatedRollButton ??= GetNodeOrNull<Button>("ModalPanel/VBox/Buttons/FatedRollButton");
        FeedbackLabel ??= GetNodeOrNull<Label>("ModalPanel/VBox/FeedbackLabel");
        ShapePreview ??= GetNodeOrNull<ShapePreviewViewport>("ModalPanel/VBox/ShapePreview");
        EnsureShapePreview();
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
            PreviewSize = new Vector2I(480, 210),
            VisualScale = 1.35f,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
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

        int previewWidth = Mathf.RoundToInt(Mathf.Clamp(panelSize.X - 64f, 280f, 480f));
        int previewHeight = Mathf.RoundToInt(Mathf.Clamp(panelSize.Y * 0.50f, 150f, 210f));
        ShapePreview.ApplyPreviewSize(new Vector2I(previewWidth, previewHeight), Mathf.Clamp(1.35f * scale, 1.0f, 1.35f));
    }

    private void ResolveServices()
    {
        if (!CurrencyManagerPath.IsEmpty)
        {
            _currencyManager = GetNodeOrNull(CurrencyManagerPath) as CurrencyManager;
        }

        _currencyManager ??= GetNodeOrNull("/root/CurrencyManager") as CurrencyManager;
    }

    private void TryRequestRoll(bool useFated)
    {
        bool canRoll = useFated
            ? (_currencyManager?.FatedRollCurrency ?? 0) > 0
            : (_currencyManager?.RollCurrency ?? 0) > 0;

        if (!canRoll)
        {
            SetFeedback(useFated ? "No fated rolls available." : "No rolls available.");
            return;
        }

        EmitSignal(SignalName.RollRequested, useFated);
        SetFeedback(useFated ? "Fated roll requested." : "Roll requested.");
    }

    private void SetFeedback(string text)
    {
        if (FeedbackLabel != null)
        {
            FeedbackLabel.Text = text;
        }
    }
}
