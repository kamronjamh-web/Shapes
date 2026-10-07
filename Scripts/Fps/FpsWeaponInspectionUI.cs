using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Fps;

/// <summary>A presentation-only arsenal view. Selection and equip remain separate actions.</summary>
public partial class FpsWeaponInspectionUI : Control
{
    public event Action<int>? ShapeSelected;
    public event Action? EquipRequested;
    public event Action? CloseRequested;

    private static readonly Color Accent = new(0.39f, 0.87f, 0.94f);
    private static readonly Color Muted = new(0.61f, 0.69f, 0.77f);
    private IReadOnlyList<EquipmentItem> _weapons = Array.Empty<EquipmentItem>();
    private readonly List<Button> _choices = new();
    private VBoxContainer _list = null!;
    private Label _name = null!, _rarity = null!, _attack = null!, _description = null!, _stats = null!, _mods = null!;
    private Button _equip = null!;
    private SubViewport _gunViewport = null!, _shapeViewport = null!;
    private Node3D _gunPivot = null!, _shapePivot = null!;
    private Camera3D _gunCamera = null!, _shapeCamera = null!;
    private Node3D? _gunVisual, _shapeVisual;
    private SubViewportContainer _gunSurface = null!;
    private int _selected = -1;
    private bool _built, _inspectionVisible, _dragging;
    private bool _flatShape;
    private bool _identityInitialized;
    private float _identityFrameSize, _identityFrameClock;
    private float _zoom = 1f, _gunFrameSize = 0.4f;

    public override void _Ready()
    {
        EnsureBuilt();
        SetInspectionVisible(_inspectionVisible);
    }

    public void SetWeapons(IReadOnlyList<EquipmentItem> weapons, int selectedIndex, int equippedIndex)
    {
        EnsureBuilt();
        _weapons = weapons ?? Array.Empty<EquipmentItem>();
        foreach (Node child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        _choices.Clear();
        for (int i = 0; i < _weapons.Count; i++)
        {
            int index = i;
            Button button = MakeButton("", false);
            button.Name = $"ShapeChoice{i}";
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(0, 64);
            button.Pressed += () => ShapeSelected?.Invoke(index);
            _list.AddChild(button);
            _choices.Add(button);
        }
        _selected = -1;
        SetSelection(selectedIndex, equippedIndex);
    }

    public void SetSelection(int selectedIndex, int equippedIndex)
    {
        EnsureBuilt();
        int next = selectedIndex >= 0 && selectedIndex < _weapons.Count ? selectedIndex : -1;
        for (int i = 0; i < _choices.Count; i++)
        {
            EquipmentItem item = _weapons[i];
            _choices[i].Text = $"{item.Shape?.DisplayName ?? "Unknown Shape"}{(i == equippedIndex ? "  •" : "")}\n{item.Rarity?.DisplayName ?? "Common"} · Lv {item.Level}";
            _choices[i].AddThemeStyleboxOverride("normal", PanelStyle(i == next ? new Color(0.09f, 0.19f, 0.23f) : new Color(0.065f, 0.09f, 0.12f), i == next ? Accent : new Color(0.16f, 0.21f, 0.26f)));
        }
        _equip.Disabled = next < 0 || next == equippedIndex;
        _equip.Text = next >= 0 && next == equippedIndex ? "Equipped" : "Equip shape";
        if (next >= 0)
        {
            EquipmentItem current = _weapons[next];
            _rarity.Text = $"{current.Rarity?.DisplayName ?? "Common"}  ·  Level {current.Level}{(next == equippedIndex ? "  ·  Equipped" : "")}";
        }
        if (_selected == next) return;
        _selected = next;
        ClearVisual(_gunPivot, ref _gunVisual);
        ClearVisual(_shapePivot, ref _shapeVisual);
        _dragging = false;
        _zoom = 1f;
        _identityInitialized = false;
        _identityFrameSize = _identityFrameClock = 0;
        if (next < 0)
        {
            _name.Text = "No shapes collected";
            _rarity.Text = "Your current run arsenal appears here.";
            _attack.Text = _description.Text = _stats.Text = _mods.Text = "";
            return;
        }
        EquipmentItem weapon = _weapons[next];
        Color tint = weapon.Rarity?.UiColor ?? Colors.White;
        _name.Text = weapon.Shape?.DisplayName ?? "Unknown Shape";
        _rarity.Text = $"{weapon.Rarity?.DisplayName ?? "Common"}  ·  Level {weapon.Level}{(next == equippedIndex ? "  ·  Equipped" : "")}";
        _rarity.AddThemeColorOverride("font_color", tint.Lerp(Colors.White, 0.35f));
        _attack.Text = weapon.Shape?.AttackName ?? "";
        _description.Text = weapon.Shape?.SpecialDescription ?? "";
        if (string.IsNullOrWhiteSpace(_description.Text)) _description.Text = weapon.Shape?.Description ?? "";
        _stats.Text = $"Base damage   {weapon.Damage}\nShape health    {weapon.Health}\nBase interval   {weapon.Shape?.Cooldown ?? 0:0.##} s";
        _mods.Text = weapon.Modulators is { Count: > 0 }
            ? string.Join("\n", weapon.Modulators.Select(mod => mod.DisplayName))
            : "No modulators installed";
        _gunPivot.Rotation = new Vector3(0.35f, -0.95f, 0);
        _flatShape = weapon.Shape?.ShapeGroup == SuperGatchaRebuild.Equipment.Data.ShapeGroup.TwoD;
        _shapePivot.Rotation = !_flatShape ? new Vector3(-0.16f, 0.28f, 0)
            : weapon.Shape?.Key == "triangle" ? new Vector3(0, 0, 0)
            : new Vector3(1.45f, 0, 0);
        _gunVisual = FpsWeaponVisualFactory.Create(weapon);
        _gunPivot.AddChild(_gunVisual);
        _shapeVisual = ShapeVisualFactory.CreateVisual(weapon.Shape, tint);
        _shapePivot.AddChild(_shapeVisual);
        // Generated shapes finish populating meshes in _Ready.
        Callable.From(() => { if (IsInstanceValid(this) && _selected == next) FramePreviews(); }).CallDeferred();
    }

    public void SetInspectionVisible(bool visible)
    {
        _inspectionVisible = visible;
        EnsureBuilt();
        Visible = visible;
        _dragging = false;
        ProcessMode = visible ? ProcessModeEnum.Always : ProcessModeEnum.Disabled;
        _gunViewport.RenderTargetUpdateMode = _shapeViewport.RenderTargetUpdateMode = visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _gunViewport.ProcessMode = _shapeViewport.ProcessMode = visible ? ProcessModeEnum.Always : ProcessModeEnum.Disabled;
        if (visible) Callable.From(FramePreviews).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (_inspectionVisible && _shapeVisual != null && !_flatShape) _shapePivot.RotateY((float)delta * 0.24f);
        if (_inspectionVisible && _shapeVisual != null)
        {
            _identityFrameClock += (float)delta;
            if (_identityFrameClock >= 0.2f)
            {
                _identityFrameClock = 0;
                FrameShape();
            }
        }
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        Name = "WeaponInspection";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ColorRect backdrop = new() { Color = new Color(0.008f, 0.014f, 0.023f, 1f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MarginContainer margin = new(); AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 24);
        VBoxContainer outer = new(); margin.AddChild(outer); outer.AddThemeConstantOverride("separation", 18);
        HBoxContainer header = new(); outer.AddChild(header);
        Label heading = Text("WEAPON SHAPES", 19, Accent); heading.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(heading);
        Button close = MakeButton("Back", false); close.Name = "CloseWeaponInspection"; close.Pressed += () => CloseRequested?.Invoke(); header.AddChild(close);
        HBoxContainer columns = new() { SizeFlagsVertical = SizeFlags.ExpandFill }; outer.AddChild(columns); columns.AddThemeConstantOverride("separation", 20);

        VBoxContainer left = new() { CustomMinimumSize = new Vector2(220, 0) }; columns.AddChild(left); left.AddThemeConstantOverride("separation", 12);
        left.AddChild(Text("CURRENT RUN", 12, Muted));
        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _list.AddThemeConstantOverride("separation", 8); scroll.AddChild(_list);

        VBoxContainer center = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill }; columns.AddChild(center); center.AddThemeConstantOverride("separation", 10);
        _name = Text("Select a shape", 36, Colors.White); center.AddChild(_name);
        _rarity = Text("", 14, Muted); center.AddChild(_rarity);
        PanelContainer previewPanel = new() { SizeFlagsVertical = SizeFlags.ExpandFill }; previewPanel.AddThemeStyleboxOverride("panel", PanelStyle(new Color(0.028f, 0.044f, 0.061f), new Color(0.13f, 0.22f, 0.27f))); center.AddChild(previewPanel);
        _gunSurface = MakePreview(out _gunViewport, out _gunPivot, out _gunCamera);
        _gunSurface.Name = "HandgunPreview";
        _gunSurface.CustomMinimumSize = new Vector2(260, 260);
        previewPanel.AddChild(_gunSurface);
        _gunSurface.GuiInput += HandlePreviewInput;
        _gunSurface.MouseExited += () => _dragging = false;
        _gunSurface.Resized += () => { if (_inspectionVisible) Callable.From(FramePreviews).CallDeferred(); };
        center.AddChild(Text("Drag to rotate  ·  Scroll to zoom", 13, Muted));
        _equip = MakeButton("Equip shape", true); _equip.Name = "EquipInspectedShape"; _equip.CustomMinimumSize = new Vector2(0, 48); _equip.Pressed += () => EquipRequested?.Invoke(); center.AddChild(_equip);

        VBoxContainer right = new() { CustomMinimumSize = new Vector2(240, 0) }; columns.AddChild(right); right.AddThemeConstantOverride("separation", 10);
        right.AddChild(Text("SHAPE IDENTITY", 12, Accent));
        SubViewportContainer shapeSurface = MakePreview(out _shapeViewport, out _shapePivot, out _shapeCamera); shapeSurface.CustomMinimumSize = new Vector2(240, 220); shapeSurface.SizeFlagsVertical = SizeFlags.Fill; shapeSurface.MouseFilter = MouseFilterEnum.Ignore; right.AddChild(shapeSurface);
        shapeSurface.Resized += () => { if (_inspectionVisible) Callable.From(FramePreviews).CallDeferred(); };
        ScrollContainer detailScroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; right.AddChild(detailScroll);
        VBoxContainer details = new() { CustomMinimumSize = new Vector2(228, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; details.AddThemeConstantOverride("separation", 12); detailScroll.AddChild(details);
        _attack = Text("", 20, Colors.White); _attack.AutowrapMode = TextServer.AutowrapMode.WordSmart; details.AddChild(_attack);
        _description = Wrapped("", 14, Muted); details.AddChild(_description);
        _stats = Text("", 14, new Color(0.79f, 0.85f, 0.9f)); details.AddChild(_stats);
        details.AddChild(Text("INSTALLED MODULATORS", 12, Accent));
        _mods = Wrapped("", 14, Muted); details.AddChild(_mods);
    }

    private SubViewportContainer MakePreview(out SubViewport viewport, out Node3D pivot, out Camera3D camera)
    {
        SubViewportContainer surface = new() { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        viewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, Size = new Vector2I(640, 480), HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa4X };
        surface.AddChild(viewport);
        WorldEnvironment environment = new() { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.025f, 0.04f, 0.055f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.72f, 0.8f, 0.88f), AmbientLightEnergy = 0.7f, TonemapMode = Godot.Environment.ToneMapper.Filmic } };
        viewport.AddChild(environment);
        pivot = new Node3D(); viewport.AddChild(pivot);
        camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 0.4f, Position = new Vector3(0, 0, 5), Current = true, Near = 0.01f, Far = 1000f, KeepAspect = Camera3D.KeepAspectEnum.Height }; viewport.AddChild(camera);
        AddLight(viewport, new Vector3(-42, -35, 0), Colors.White, 1.65f);
        AddLight(viewport, new Vector3(20, 125, 0), new Color(0.55f, 0.76f, 1f), 0.9f);
        AddLight(viewport, new Vector3(-25, -155, 0), new Color(0.7f, 0.95f, 1f), 1.15f);
        return surface;
    }

    private static void AddLight(Node parent, Vector3 rotation, Color color, float energy) => parent.AddChild(new DirectionalLight3D { RotationDegrees = rotation, LightColor = color, LightEnergy = energy, ShadowEnabled = false });

    private void HandlePreviewInput(InputEvent input)
    {
        if (!_inspectionVisible) return;
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _dragging = button.Pressed;
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                _zoom = Mathf.Clamp(_zoom * (button.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f), 0.6f, 1.8f);
                _gunCamera.Size = _gunFrameSize * _zoom;
            }
        }
        if (input is InputEventMouseMotion motion && _dragging)
        {
            Vector3 rotation = _gunPivot.Rotation;
            rotation.Y += motion.Relative.X * 0.009f;
            rotation.X = Mathf.Clamp(rotation.X + motion.Relative.Y * 0.009f, -1.4f, 1.4f);
            _gunPivot.Rotation = rotation;
            FramePreviews();
        }
        _gunSurface.AcceptEvent();
    }

    private void FramePreviews()
    {
        if (!_built || !IsInsideTree()) return;
        if (_gunVisual != null) _gunFrameSize = Frame(_gunVisual, _gunPivot, _gunCamera, _gunViewport, false);
        FrameShape();
        _gunCamera.Size = _gunFrameSize * _zoom;
    }

    private void FrameShape()
    {
        if (_shapeVisual == null || !_shapeVisual.IsInsideTree()) return;
        if (!_identityInitialized)
        {
            // Four-dimensional point clouds populate instance transforms on their first tick.
            if (_shapeVisual is LegacyShapeVisualAnimator legacy) legacy._Process(0);
            if (_selected >= 0 && _weapons[_selected].Shape?.Key == "triangle")
            {
                // The canonical prism has its triangular face in XY, with a narrow X span
                // after its local 90-degree roll. Keep that face and normalize its aspect.
                Aabb? triangle = FpsWeaponVisualFactory.GetVisualBounds(_shapeVisual);
                if (triangle.HasValue && triangle.Value.Size.X > 0)
                    _shapeVisual.Scale *= new Vector3(triangle.Value.Size.Y / triangle.Value.Size.X, 1, 1);
            }
            _identityInitialized = true;
        }
        float framed = Frame(_shapeVisual, _shapePivot, _shapeCamera, _shapeViewport, !_flatShape);
        // Animated projections can expand after initialization. Grow the frame without
        // pulsing the camera size as individual points move in and out of the bounds.
        _identityFrameSize = Mathf.Max(_identityFrameSize, framed);
        _shapeCamera.Size = _identityFrameSize;
    }

    private static float Frame(Node3D visual, Node3D pivot, Camera3D camera, SubViewport viewport, bool rotationSafe)
    {
        Aabb? bounds = FpsWeaponVisualFactory.GetVisualBounds(visual);
        if (bounds == null) return camera.Size;
        Aabb box = visual.Transform * bounds.Value;
        visual.Position -= box.GetCenter();
        // A bounding sphere preserves the framing as the player turns the model.
        float diameter = Mathf.Max(box.Size.Length(), 0.04f);
        Vector3 center = pivot.Position;
        camera.Position = center + new Vector3(0, 0, Mathf.Max(diameter * 3f, 1f));
        float aspect = (float)viewport.Size.X / Mathf.Max(1, viewport.Size.Y);
        Aabb oriented = pivot.Transform * new Aabb(-box.Size * 0.5f, box.Size);
        camera.Size = rotationSafe ? diameter * 1.18f / Mathf.Min(1f, aspect)
            : Mathf.Max(oriented.Size.Y, oriented.Size.X / aspect) * 1.2f;
        return camera.Size;
    }

    private static void ClearVisual(Node parent, ref Node3D? visual)
    {
        if (visual == null) return;
        parent.RemoveChild(visual); visual.QueueFree(); visual = null;
    }

    private static Label Text(string text, int size, Color color)
    {
        Label label = new() { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color); return label;
    }
    private static Label Wrapped(string text, int size, Color color) { Label label = Text(text, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; return label; }
    private static Button MakeButton(string text, bool primary)
    {
        Button button = new() { Text = text, MouseDefaultCursorShape = CursorShape.PointingHand };
        button.AddThemeFontSizeOverride("font_size", 15);
        button.AddThemeColorOverride("font_color", primary ? new Color(0.025f, 0.08f, 0.1f) : Colors.White);
        button.AddThemeStyleboxOverride("normal", PanelStyle(primary ? Accent : new Color(0.07f, 0.1f, 0.13f), primary ? Accent : new Color(0.19f, 0.27f, 0.33f)));
        button.AddThemeStyleboxOverride("hover", PanelStyle(primary ? Accent.Lightened(0.15f) : new Color(0.12f, 0.2f, 0.25f), Accent));
        button.AddThemeStyleboxOverride("pressed", PanelStyle(new Color(0.15f, 0.36f, 0.4f), Accent));
        button.AddThemeStyleboxOverride("disabled", PanelStyle(new Color(0.09f, 0.13f, 0.16f), new Color(0.2f, 0.27f, 0.3f)));
        button.AddThemeStyleboxOverride("focus", PanelStyle(Colors.Transparent, Accent));
        return button;
    }
    private static StyleBoxFlat PanelStyle(Color background, Color border) => new() { BgColor = background, BorderColor = border, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 };
}
