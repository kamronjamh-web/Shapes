using Godot;

namespace SuperGatchaRebuild.UI;

// Base behavior for all hub/debug modal controls.
public partial class ModalBase : Control
{
    [Signal]
    public delegate void OpenedEventHandler();

    [Signal]
    public delegate void ClosedEventHandler();

    [Export] public bool BlockingModal = true;
    [Export] public bool CloseOnBackgroundClick = true;
    [Export] public bool CloseOnEscape = true;
    [Export] public Button? CloseButton;
    [Export] public NodePath ModalPanelPath = new("ModalPanel");
    [Export] public bool ResponsiveModalLayout = true;
    [Export(PropertyHint.Range, "0.4,1,0.01")] public float MaxViewportWidthRatio = 0.92f;
    [Export(PropertyHint.Range, "0.4,1,0.01")] public float MaxViewportHeightRatio = 0.90f;
    [Export(PropertyHint.Range, "0,128,1")] public float MinViewportMargin = 16f;
    [Export(PropertyHint.Range, "0,160,1")] public float MaxViewportMargin = 64f;

    private bool _closeButtonWired;
    private bool _hasPreferredPanelSize;
    private bool _recalculatingLayout;
    private Control? _modalPanel;
    private Vector2 _preferredPanelSize;
    private Viewport? _wiredViewport;

    private static readonly string[] CloseButtonFallbackPaths =
    {
        "ModalPanel/VBox/CloseButton",
        "ModalPanel/VBox/Buttons/CloseButton",
        "ModalPanel/VBox/ButtonsRow/CloseButton",
        "ModalPanel/VBox/Buttons/CancelButton",
    };

    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        ResolveCloseButton();
        WireCloseButton();
        WireViewportResizeSignal();
        RecalculateModalLayout(true);

        Visible = IsOpen;
    }

    public override void _ExitTree()
    {
        if (_wiredViewport != null)
        {
            _wiredViewport.SizeChanged -= OnViewportSizeChanged;
            _wiredViewport = null;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            RecalculateModalLayout();
        }
    }

    public virtual void Open()
    {
        if (IsOpen)
        {
            return;
        }

        // Exported node references can occasionally be lost on inherited scripts.
        // Resolve and wire once more at open time for resilience.
        ResolveCloseButton();
        WireCloseButton();

        IsOpen = true;
        Visible = true;
        RecalculateModalLayout();
        EmitSignal(SignalName.Opened);
    }

    public virtual void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Visible = false;
        EmitSignal(SignalName.Closed);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        if (CloseOnEscape && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!CloseOnBackgroundClick)
        {
            return;
        }

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Control? panel = ResolveModalPanel();
            Rect2 panelRect = panel?.GetGlobalRect() ?? new Rect2();
            if (panel == null || !panelRect.HasPoint(mb.GlobalPosition))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void ResolveCloseButton()
    {
        if (CloseButton != null)
        {
            return;
        }

        for (int i = 0; i < CloseButtonFallbackPaths.Length; i++)
        {
            Button? byPath = GetNodeOrNull<Button>(CloseButtonFallbackPaths[i]);
            if (byPath != null)
            {
                CloseButton = byPath;
                return;
            }
        }

        CloseButton = FindChild("CloseButton", true, false) as Button;
        CloseButton ??= FindChild("CancelButton", true, false) as Button;
    }

    protected void RecalculateModalLayout(bool refreshPreferredSize = false)
    {
        if (!ResponsiveModalLayout || _recalculatingLayout)
        {
            return;
        }

        _recalculatingLayout = true;
        try
        {
            SetFullRect(this);
            SetFullRect(GetNodeOrNull<Control>("Backdrop"));

            Control? panel = ResolveModalPanel();
            if (panel == null)
            {
                return;
            }

            Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Size;
            if (viewportSize.X <= 1f || viewportSize.Y <= 1f)
            {
                viewportSize = Size;
            }

            if (viewportSize.X <= 1f || viewportSize.Y <= 1f)
            {
                return;
            }

            if (refreshPreferredSize || !_hasPreferredPanelSize)
            {
                _preferredPanelSize = ResolvePreferredPanelSize(panel, viewportSize);
                _hasPreferredPanelSize = true;
            }

            float shortSide = Mathf.Min(viewportSize.X, viewportSize.Y);
            float margin = Mathf.Clamp(shortSide * 0.04f, MinViewportMargin, MaxViewportMargin);
            float maxWidth = Mathf.Max(1f, Mathf.Min(viewportSize.X - margin * 2f, viewportSize.X * MaxViewportWidthRatio));
            float maxHeight = Mathf.Max(1f, Mathf.Min(viewportSize.Y - margin * 2f, viewportSize.Y * MaxViewportHeightRatio));

            Vector2 panelSize = new(
                Mathf.Min(_preferredPanelSize.X, maxWidth),
                Mathf.Min(_preferredPanelSize.Y, maxHeight));
            panelSize.X = Mathf.Max(1f, panelSize.X);
            panelSize.Y = Mathf.Max(1f, panelSize.Y);

            float left = Mathf.Round((viewportSize.X - panelSize.X) * 0.5f);
            float top = Mathf.Round((viewportSize.Y - panelSize.Y) * 0.5f);

            panel.CustomMinimumSize = panelSize;
            panel.SetAnchorsPreset(LayoutPreset.TopLeft);
            panel.OffsetLeft = left;
            panel.OffsetTop = top;
            panel.OffsetRight = left + panelSize.X;
            panel.OffsetBottom = top + panelSize.Y;

            float scale = Mathf.Min(panelSize.X / Mathf.Max(1f, _preferredPanelSize.X), panelSize.Y / Mathf.Max(1f, _preferredPanelSize.Y));
            OnModalLayoutChanged(panelSize, scale);
        }
        finally
        {
            _recalculatingLayout = false;
        }
    }

    protected virtual void OnModalLayoutChanged(Vector2 panelSize, float scale)
    {
        _ = panelSize;
        _ = scale;
    }

    private Control? ResolveModalPanel()
    {
        if (_modalPanel != null && IsInstanceValid(_modalPanel))
        {
            return _modalPanel;
        }

        _modalPanel = ModalPanelPath.IsEmpty
            ? GetNodeOrNull<Control>("ModalPanel")
            : GetNodeOrNull<Control>(ModalPanelPath);
        _modalPanel ??= FindChild("ModalPanel", true, false) as Control;
        return _modalPanel;
    }

    private static Vector2 ResolvePreferredPanelSize(Control panel, Vector2 viewportSize)
    {
        Vector2 minimum = panel.CustomMinimumSize;
        Vector2 current = panel.Size;
        Vector2 offsetSize = new(
            Mathf.Abs(panel.OffsetRight - panel.OffsetLeft),
            Mathf.Abs(panel.OffsetBottom - panel.OffsetTop));

        Vector2 preferred = new(
            Mathf.Max(Mathf.Max(minimum.X, current.X), offsetSize.X),
            Mathf.Max(Mathf.Max(minimum.Y, current.Y), offsetSize.Y));

        if (preferred.X <= 1f)
        {
            preferred.X = Mathf.Max(360f, viewportSize.X * 0.55f);
        }

        if (preferred.Y <= 1f)
        {
            preferred.Y = Mathf.Max(240f, viewportSize.Y * 0.55f);
        }

        return preferred;
    }

    private void WireViewportResizeSignal()
    {
        Viewport? viewport = GetViewport();
        if (viewport == null || ReferenceEquals(_wiredViewport, viewport))
        {
            return;
        }

        if (_wiredViewport != null)
        {
            _wiredViewport.SizeChanged -= OnViewportSizeChanged;
        }

        _wiredViewport = viewport;
        _wiredViewport.SizeChanged += OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged()
    {
        RecalculateModalLayout();
    }

    private static void SetFullRect(Control? control)
    {
        if (control == null)
        {
            return;
        }

        control.SetAnchorsPreset(LayoutPreset.FullRect);
        control.OffsetLeft = 0f;
        control.OffsetTop = 0f;
        control.OffsetRight = 0f;
        control.OffsetBottom = 0f;
    }

    private void WireCloseButton()
    {
        if (_closeButtonWired || CloseButton == null)
        {
            return;
        }

        CloseButton.Pressed += OnCloseButtonPressed;
        _closeButtonWired = true;
    }

    private void OnCloseButtonPressed()
    {
        Close();
    }
}
