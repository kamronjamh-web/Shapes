using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Fps;

public partial class FpsInventoryDragData : RefCounted
{
    public FpsWeaponInspectionUI Owner { get; set; } = null!;
    public int Session { get; set; }
    public EquipmentItem Weapon { get; set; } = null!;
    public WeaponModulatorData? Part { get; set; }
    public int ArsenalIndex { get; set; } = -1;
    public int SourceSlot { get; set; } = -1;
}

/// <summary>Normal Godot drag/drop: cancellation never mutates the inventory.</summary>
public partial class FpsInventoryTile : Button
{
    public Func<FpsInventoryDragData?>? CreatePayload { get; set; }
    public Func<FpsInventoryDragData, bool>? AcceptPayload { get; set; }
    public Action<FpsInventoryDragData>? CommitPayload { get; set; }
    public string DragCaption { get; set; } = "Item";
    public Texture2D? PreviewTexture { get; set; }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        FpsInventoryDragData? payload = CreatePayload?.Invoke();
        if (payload == null) return default;
        SetDragPreview(Preview(DragCaption, PreviewTexture));
        return payload;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => Read(data) is { } payload && AcceptPayload?.Invoke(payload) == true;
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Read(data) is { } payload && AcceptPayload?.Invoke(payload) == true) CommitPayload?.Invoke(payload);
    }

    internal static FpsInventoryDragData? Read(Variant data) => data.VariantType == Variant.Type.Object ? data.AsGodotObject() as FpsInventoryDragData : null;

    private static Control Preview(string caption, Texture2D? texture)
    {
        PanelContainer panel = new() { Position = new Vector2(-46, -42), CustomMinimumSize = new Vector2(160, 116), MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, .94f) };
        panel.AddThemeStyleboxOverride("panel", FpsWeaponInspectionUI.CardStyle(new Color(.055f, .13f, .17f), new Color(.34f, .88f, .92f), 10));
        VBoxContainer column = new() { MouseFilter = MouseFilterEnum.Ignore }; panel.AddChild(column);
        if (texture != null) column.AddChild(new TextureRect { Texture = texture, CustomMinimumSize = new Vector2(136, 76), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        Label name = new() { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(140, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 13); column.AddChild(name);
        return panel;
    }
}

public partial class FpsInventoryDropTarget : Control
{
    public Func<FpsInventoryDragData, bool>? AcceptPayload { get; set; }
    public Action<FpsInventoryDragData>? CommitPayload { get; set; }
    public override bool _CanDropData(Vector2 atPosition, Variant data) => FpsInventoryTile.Read(data) is { } payload && AcceptPayload?.Invoke(payload) == true;
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (FpsInventoryTile.Read(data) is { } payload && AcceptPayload?.Invoke(payload) == true) CommitPayload?.Invoke(payload);
    }
}

/// <summary>Static 3D thumbnails render once; only the large workbench animates continuously.</summary>
public partial class FpsInventoryThumbnail : SubViewportContainer
{
    private SubViewport _viewport = null!;
    private Node3D _pivot = null!, _visual = null!;
    private Camera3D _camera = null!;
    private bool _built;
    private bool _normalizeTriangle;
    public Texture2D? ThumbnailTexture => _built ? _viewport.GetTexture() : null;

    public static FpsInventoryThumbnail Shape(EquipmentItem item)
    {
        FpsInventoryThumbnail preview = new() { _normalizeTriangle = item.Shape?.Key == "triangle" };
        preview.Build(ShapeVisualFactory.CreateVisual(item.Shape, item.Rarity?.UiColor ?? Colors.White),
            item.Shape?.ShapeGroup == ShapeGroup.TwoD
                ? item.Shape.Key == "triangle" ? Vector3.Zero : new Vector3(1.45f, 0, 0)
                : new Vector3(-.2f, .35f, 0));
        return preview;
    }

    public static FpsInventoryThumbnail Modulator(WeaponModulatorData part)
    {
        FpsInventoryThumbnail preview = new();
        PackedScene? scene = ResourceLoader.Exists("res://Resources/Weapons/WorkbenchCartridge.glb")
            ? GD.Load<PackedScene>("res://Resources/Weapons/WorkbenchCartridge.glb") : null;
        Node3D model = scene?.Instantiate<Node3D>() ?? new Node3D();
        FpsModulatorCartridgeStyle.Apply(model, part);
        // Authored face normal is +Y and cartridge top is -Z; turn its face toward the +Z camera.
        preview.Build(model, new Vector3(Mathf.Pi * .5f - .22f, -.45f, -.15f));
        return preview;
    }

    private void Build(Node3D visual, Vector3 rotation)
    {
        _built = true;
        Stretch = true; MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(64, 62);
        _viewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, Size = new Vector2I(128, 96), HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa2X };
        AddChild(_viewport);
        _viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.03f, .047f, .062f, 0), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.8f, .9f, 1), AmbientLightEnergy = .8f } });
        _pivot = new Node3D { Rotation = rotation }; _viewport.AddChild(_pivot);
        _visual = visual; _pivot.AddChild(_visual);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Current = true, Near = .001f, Far = 1000f, KeepAspect = Camera3D.KeepAspectEnum.Height }; _viewport.AddChild(_camera);
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -30, 0), LightEnergy = 1.8f });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(20, 145, 0), LightColor = new Color(.5f, .8f, 1), LightEnergy = 1.1f });
        Resized += () => { if (IsInsideTree()) Callable.From(Frame).CallDeferred(); };
    }

    public override void _Ready() => Callable.From(Frame).CallDeferred();
    public void RefreshThumbnail() { if (_built && IsInsideTree()) Frame(); }
    public void Suspend() { if (_built) _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; }

    private void Frame()
    {
        if (!_built || !IsInsideTree() || !IsInstanceValid(_visual)) return;
        if (_visual is LegacyShapeVisualAnimator legacy) legacy._Process(0);
        Aabb? bounds = FpsWeaponVisualFactory.GetVisualBounds(_visual);
        if (bounds == null) return;
        if (_normalizeTriangle && bounds.Value.Size.X > 0)
        {
            // The canonical prism's 90-degree roll compresses its triangular face in X.
            _visual.Scale *= new Vector3(bounds.Value.Size.Y / bounds.Value.Size.X, 1, 1);
            _normalizeTriangle = false;
        }
        Aabb box = _visual.Transform * bounds.Value;
        _visual.Position -= box.GetCenter();
        Aabb oriented = _pivot.Transform * new Aabb(-box.Size * .5f, box.Size);
        float aspect = (float)_viewport.Size.X / Math.Max(1, _viewport.Size.Y);
        _camera.Size = Math.Max(.001f, Math.Max(oriented.Size.Y, oriented.Size.X / aspect) * 1.25f);
        _camera.Position = new Vector3(0, 0, Math.Max(1, box.Size.Length() * 3));
        _visual.ProcessMode = ProcessModeEnum.Disabled;
        _viewport.RenderTargetUpdateMode = IsVisibleInTree() ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (Node descendant in Descendants(child)) yield return descendant; }
    }
}
