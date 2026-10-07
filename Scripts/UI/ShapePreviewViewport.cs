using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;
using SuperGatchaRebuild.Visuals;

namespace SuperGatchaRebuild.UI;

public partial class ShapePreviewViewport : SubViewportContainer
{
    [Export] public Vector2I PreviewSize = new(360, 220);
    [Export] public float VisualScale = 1.45f;
    [Export] public float RotateSpeed = 0.65f;
    [Export] public bool AutoFitContent = false;
    [Export(PropertyHint.Range, "0.30,0.98,0.01")] public float TargetViewportFill = 0.76f;
    [Export(PropertyHint.Range, "1,179,0.1")] public float MinCameraFitFovDegrees = 18f;
    [Export(PropertyHint.Range, "1,179,0.1")] public float MaxCameraFitFovDegrees = 56f;
    [Export] public Vector3 CameraPosition = new(0f, 0.35f, 3.25f);
    [Export] public Vector3 CameraRotationDegrees = new(-5f, 0f, 0f);
    [Export] public float CameraFov = 38f;
    [Export] public Color BackgroundColor = new(0.015f, 0.025f, 0.055f, 1f);
    [Export] public Color AmbientLightColor = new(0.12f, 0.18f, 0.28f, 1f);
    [Export] public float AmbientLightEnergy = 0.75f;
    [Export] public Vector3 KeyLightRotationDegrees = new(-38f, 35f, 0f);
    [Export] public Color KeyLightColor = new(0.86f, 0.95f, 1f, 1f);
    [Export] public float KeyLightEnergy = 1.7f;
    [Export] public Color GlowLightColor = new(0f, 0.8f, 0.72f, 1f);
    [Export] public Vector3 GlowLightPosition = new(0f, 0.8f, 1.6f);
    [Export] public float BaseGlowLightEnergy = 1.5f;
    [Export] public float GlowLightRange = 5f;
    [Export] public bool EnableCameraDrift = false;
    [Export] public Vector3 CameraDriftPositionAmplitude = new(0.08f, 0.05f, 0.10f);
    [Export] public Vector3 CameraDriftRotationAmplitude = new(0.35f, 0.65f, 0f);
    [Export(PropertyHint.Range, "0.01,2.00,0.01")] public float CameraDriftSpeed = 0.10f;
    [Export] public float PitchBaseDegrees = 10f;
    [Export] public float PitchAmplitudeDegrees = 4f;
    [Export] public float VerticalBobAmplitude = 0.05f;
    [Export(PropertyHint.Range, "-0.25,0.25,0.01")] public float VerticalFramingBias = 0.05f;
    [Export] public bool ShowLightingRoom = false;
    [Export] public Color RoomSurfaceColor = new(0.055f, 0.074f, 0.102f, 1f);
    [Export] public Color RoomAccentColor = new(0.135f, 0.182f, 0.220f, 1f);

    private SubViewport? _viewport;
    private Node3D? _previewRoot;
    private Node3D? _visualPivot;
    private Node3D? _visualFrame;
    private Node3D? _visual;
    private Node3D? _modulatorSocketVisuals;
    private ModifierVisualController? _modifierVisual;
    private float _time;
    private Vector3 _pivotBasePosition = Vector3.Zero;
    private float _resolvedCameraFov;

    public override void _Ready()
    {
        _resolvedCameraFov = CameraFov;
        BuildPreviewScene();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        ShapeVisualFactory.PumpShapeVisualSettingsHotReload();

        if (_visualPivot == null)
        {
            return;
        }

        bool usesNativeAnimation = ShapeVisualFactory.UsesNativeAnimation(_visual);
        float yRotation = usesNativeAnimation
            ? 0f
            : _visualPivot.RotationDegrees.Y + RotateSpeed * 60f * (float)delta;
        _visualPivot.RotationDegrees = new Vector3(
            usesNativeAnimation ? 0f : PitchBaseDegrees + PitchAmplitudeDegrees * Mathf.Sin(_time * 1.4f),
            yRotation,
            0f);
        _visualPivot.Position = _pivotBasePosition + new Vector3(0f, VerticalBobAmplitude * Mathf.Sin(_time * 2.1f), 0f);
        TickCameraDrift();
    }

    public void SetItem(EquipmentItem? item)
    {
        if (item == null)
        {
            ClearVisual();
            return;
        }

        SetShapeInternal(item.Shape, ResolveTint(item), item);
    }

    public void SetShape(ShapeData? shape, Color tint)
    {
        SetShapeInternal(shape, tint, null);
    }

    private void SetShapeInternal(ShapeData? shape, Color tint, EquipmentItem? item)
    {
        BuildPreviewScene();
        ClearVisual();

        if (_visualFrame == null)
        {
            return;
        }

        ConfigureGlowLight(item);

        _visual = ShapeVisualFactory.CreateVisual(shape, tint);
        _visual.Scale = new Vector3(
            _visual.Scale.X * VisualScale,
            _visual.Scale.Y * VisualScale,
            _visual.Scale.Z * VisualScale);
        _visualFrame.AddChild(_visual);
        ApplyModulatorSocketVisuals(item);
        ApplyModifierVisual(item, shape);
        RefreshContentFraming();
    }

    public void ApplyPreviewSize(Vector2I previewSize, float visualScale)
    {
        PreviewSize = new Vector2I(Mathf.Max(1, previewSize.X), Mathf.Max(1, previewSize.Y));
        VisualScale = Mathf.Max(0.1f, visualScale);
        CustomMinimumSize = new Vector2(PreviewSize.X, PreviewSize.Y);
        if (_viewport != null)
        {
            _viewport.Size = PreviewSize;
        }

        RefreshContentFraming();
    }

    private void BuildPreviewScene()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(PreviewSize.X, PreviewSize.Y);
        _resolvedCameraFov = _resolvedCameraFov <= 0.01f ? CameraFov : _resolvedCameraFov;

        _viewport ??= GetNodeOrNull<SubViewport>("PreviewViewport");
        if (_viewport == null)
        {
            _viewport = new SubViewport
            {
                Name = "PreviewViewport",
                Size = PreviewSize,
                TransparentBg = true,
                HandleInputLocally = false,
                OwnWorld3D = true,
            };
            AddChild(_viewport);
        }
        else
        {
            _viewport.Size = PreviewSize;
        }

        _viewport.TransparentBg = BackgroundColor.A < 0.999f;

        _previewRoot ??= _viewport.GetNodeOrNull<Node3D>("PreviewRoot");
        if (_previewRoot == null)
        {
            _previewRoot = new Node3D { Name = "PreviewRoot" };
            _viewport.AddChild(_previewRoot);
        }

        WorldEnvironment? worldEnvironment = _previewRoot.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (worldEnvironment == null)
        {
            worldEnvironment = new WorldEnvironment { Name = "WorldEnvironment", Environment = new Environment() };
            _previewRoot.AddChild(worldEnvironment);
        }

        worldEnvironment.Environment ??= new Environment();
        worldEnvironment.Environment.BackgroundMode = Environment.BGMode.Color;
        worldEnvironment.Environment.BackgroundColor = BackgroundColor;
        worldEnvironment.Environment.AmbientLightSource = Environment.AmbientSource.Color;
        worldEnvironment.Environment.AmbientLightColor = AmbientLightColor;
        worldEnvironment.Environment.AmbientLightEnergy = AmbientLightEnergy;

        Camera3D? camera = _previewRoot.GetNodeOrNull<Camera3D>("PreviewCamera");
        if (camera == null)
        {
            camera = new Camera3D
            {
                Name = "PreviewCamera",
                Position = CameraPosition,
                RotationDegrees = CameraRotationDegrees,
                Fov = _resolvedCameraFov,
                Current = true,
            };
            _previewRoot.AddChild(camera);
        }
        else
        {
            camera.Position = CameraPosition;
            camera.RotationDegrees = CameraRotationDegrees;
            camera.Fov = _resolvedCameraFov;
        }

        TickCameraDrift();

        if (_previewRoot.GetNodeOrNull<DirectionalLight3D>("KeyLight") == null)
        {
            _previewRoot.AddChild(new DirectionalLight3D
            {
                Name = "KeyLight",
                RotationDegrees = KeyLightRotationDegrees,
                LightEnergy = KeyLightEnergy,
                LightColor = KeyLightColor,
            });
        }
        else if (_previewRoot.GetNodeOrNull<DirectionalLight3D>("KeyLight") is DirectionalLight3D keyLight)
        {
            keyLight.RotationDegrees = KeyLightRotationDegrees;
            keyLight.LightEnergy = KeyLightEnergy;
            keyLight.LightColor = KeyLightColor;
            keyLight.ShadowEnabled = ShowLightingRoom;
        }

        if (_previewRoot.GetNodeOrNull<DirectionalLight3D>("KeyLight") is DirectionalLight3D activeKeyLight)
        {
            activeKeyLight.ShadowEnabled = ShowLightingRoom;
        }

        if (_previewRoot.GetNodeOrNull<OmniLight3D>("GlowLight") == null)
        {
            _previewRoot.AddChild(new OmniLight3D
            {
                Name = "GlowLight",
                Position = GlowLightPosition,
                LightEnergy = BaseGlowLightEnergy,
                OmniRange = GlowLightRange,
                LightColor = GlowLightColor,
            });
        }
        else if (_previewRoot.GetNodeOrNull<OmniLight3D>("GlowLight") is OmniLight3D glowLight)
        {
            glowLight.Position = GlowLightPosition;
            glowLight.LightColor = GlowLightColor;
            glowLight.LightEnergy = BaseGlowLightEnergy;
            glowLight.OmniRange = GlowLightRange;
        }

        SyncLightingRoom();

        _visualPivot ??= _previewRoot.GetNodeOrNull<Node3D>("VisualPivot");
        if (_visualPivot == null)
        {
            _visualPivot = new Node3D { Name = "VisualPivot" };
            _previewRoot.AddChild(_visualPivot);
        }

        _visualFrame ??= _visualPivot.GetNodeOrNull<Node3D>("VisualFrame");
        if (_visualFrame == null)
        {
            _visualFrame = new Node3D { Name = "VisualFrame" };
            _visualPivot.AddChild(_visualFrame);
        }
    }

    private void ClearVisual()
    {
        ClearModulatorSocketVisuals();
        ClearModifierVisual();

        if (_visualFrame != null)
        {
            _visualFrame.Position = Vector3.Zero;
            _visualFrame.Scale = Vector3.One;
        }

        if (_visual == null)
        {
            return;
        }

        if (IsInstanceValid(_visual))
        {
            _visual.QueueFree();
        }

        _visual = null;
    }

    private void ApplyModulatorSocketVisuals(EquipmentItem? item)
    {
        ClearModulatorSocketVisuals();

        if (_visualPivot == null || item == null)
        {
            return;
        }

        _modulatorSocketVisuals = new Node3D { Name = "ModulatorSockets" };
        _visualFrame?.AddChild(_modulatorSocketVisuals);
        WeaponModulatorVisualFactory.PopulateSocketVisuals(_modulatorSocketVisuals, item, VisualScale);
    }

    private void ClearModulatorSocketVisuals()
    {
        if (_modulatorSocketVisuals == null)
        {
            return;
        }

        if (IsInstanceValid(_modulatorSocketVisuals))
        {
            _modulatorSocketVisuals.QueueFree();
        }

        _modulatorSocketVisuals = null;
    }

    private void ApplyModifierVisual(EquipmentItem? item, ShapeData? shape)
    {
        ClearModifierVisual();

        if (_visualPivot == null || _visual == null || shape == null || !ModifierVisualTheme.HasSpecialVisuals(item))
        {
            return;
        }

        OmniLight3D? glowLight = _previewRoot?.GetNodeOrNull<OmniLight3D>("GlowLight");
        _modifierVisual = new ModifierVisualController
        {
            Name = "ModifierVisualController",
            Intensity = 1.12f,
        };
        _visualFrame?.AddChild(_modifierVisual);
        _modifierVisual.Configure(item, _visual, shape, VisualScale, glowLight, 1.12f);
    }

    private void ClearModifierVisual()
    {
        if (_modifierVisual == null)
        {
            return;
        }

        if (IsInstanceValid(_modifierVisual))
        {
            _modifierVisual.QueueFree();
        }

        _modifierVisual = null;
    }

    private void ConfigureGlowLight(EquipmentItem? item)
    {
        if (_previewRoot?.GetNodeOrNull<OmniLight3D>("GlowLight") is not OmniLight3D glowLight)
        {
            return;
        }

        Color sourceColor = item?.Essence?.UiColor ?? item?.Rarity?.UiColor ?? GlowLightColor;
        float mix = item?.HasEquippedEssence == true ? 0.72f : item?.Rarity != null ? 0.46f : 0f;
        glowLight.Position = GlowLightPosition;
        glowLight.LightColor = GlowLightColor.Lerp(sourceColor, mix);
        glowLight.LightEnergy = BaseGlowLightEnergy * (item?.HasEquippedEssence == true ? 1.18f : 1f);
        glowLight.OmniRange = GlowLightRange;
    }

    private void RefreshContentFraming()
    {
        if (_visualFrame == null)
        {
            return;
        }

        _visualFrame.Position = Vector3.Zero;
        _visualFrame.Scale = Vector3.One;
        _pivotBasePosition = Vector3.Zero;
        _resolvedCameraFov = CameraFov;

        if (!TryGetLocalContentBounds(out Aabb bounds))
        {
            return;
        }

        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        _visualFrame.Position = new Vector3(-center.X, -center.Y + bounds.Size.Y * VerticalFramingBias, -center.Z);

        if (!AutoFitContent || _previewRoot?.GetNodeOrNull<Camera3D>("PreviewCamera") is not Camera3D camera)
        {
            return;
        }

        camera.Position = CameraPosition;
        camera.RotationDegrees = CameraRotationDegrees;
        camera.Fov = CameraFov;

        if (!TryGetProjectedContentBounds(camera, out Rect2 projectedBounds))
        {
            return;
        }

        Vector2 viewportSize = _viewport?.GetVisibleRect().Size ?? new Vector2(PreviewSize.X, PreviewSize.Y);
        if (viewportSize.X <= 1f || viewportSize.Y <= 1f)
        {
            return;
        }

        float currentFill = Mathf.Max(projectedBounds.Size.X / viewportSize.X, projectedBounds.Size.Y / viewportSize.Y);
        if (currentFill <= 0.001f)
        {
            return;
        }

        float currentHalfFovRadians = Mathf.DegToRad(Mathf.Max(1f, CameraFov) * 0.5f);
        float requiredHalfFovRadians = Mathf.Atan(Mathf.Tan(currentHalfFovRadians) * currentFill / Mathf.Max(0.01f, TargetViewportFill));
        float resolvedFov = Mathf.RadToDeg(requiredHalfFovRadians * 2f);
        _resolvedCameraFov = Mathf.Clamp(resolvedFov, MinCameraFitFovDegrees, MaxCameraFitFovDegrees);
        camera.Fov = _resolvedCameraFov;
    }

    private bool TryGetLocalContentBounds(out Aabb bounds)
    {
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool hasBounds = false;

        IncludeLocalMeshBounds(_visual, Transform3D.Identity, ref min, ref max, ref hasBounds);
        IncludeLocalMeshBounds(_modulatorSocketVisuals, Transform3D.Identity, ref min, ref max, ref hasBounds);

        if (!hasBounds)
        {
            bounds = default;
            return false;
        }

        bounds = new Aabb(min, max - min);
        return true;
    }

    private bool TryGetProjectedContentBounds(Camera3D camera, out Rect2 bounds)
    {
        bounds = default;
        bool hasPoint = false;
        IncludeProjectedMeshBounds(_visual, camera, ref bounds, ref hasPoint);
        IncludeProjectedMeshBounds(_modulatorSocketVisuals, camera, ref bounds, ref hasPoint);
        return hasPoint;
    }

    private static void IncludeLocalMeshBounds(Node? node, Transform3D accumulatedTransform, ref Vector3 min, ref Vector3 max, ref bool hasBounds)
    {
        if (node == null)
        {
            return;
        }

        Transform3D nextTransform = accumulatedTransform;
        if (node is Node3D node3D)
        {
            if (!node3D.Visible)
            {
                return;
            }

            nextTransform *= node3D.Transform;

            if (node3D is MeshInstance3D meshInstance && meshInstance.Mesh != null)
            {
                ExpandAabb(meshInstance.GetAabb(), nextTransform, ref min, ref max, ref hasBounds);
            }
            else if (node3D is MultiMeshInstance3D multiMeshInstance && multiMeshInstance.Multimesh?.Mesh != null)
            {
                ExpandAabb(multiMeshInstance.GetAabb(), nextTransform, ref min, ref max, ref hasBounds);
            }
        }

        foreach (Node child in node.GetChildren())
        {
            IncludeLocalMeshBounds(child, nextTransform, ref min, ref max, ref hasBounds);
        }
    }

    private static void ExpandAabb(Aabb aabb, Transform3D transform, ref Vector3 min, ref Vector3 max, ref bool hasBounds)
    {
        Vector3 position = aabb.Position;
        Vector3 size = aabb.Size;

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 localPoint = new(
                        position.X + size.X * x,
                        position.Y + size.Y * y,
                        position.Z + size.Z * z);
                    Vector3 transformedPoint = transform * localPoint;
                    if (!hasBounds)
                    {
                        min = transformedPoint;
                        max = transformedPoint;
                        hasBounds = true;
                        continue;
                    }

                    min = min.Min(transformedPoint);
                    max = max.Max(transformedPoint);
                }
            }
        }
    }

    private static void IncludeProjectedMeshBounds(Node? node, Camera3D camera, ref Rect2 bounds, ref bool hasPoint)
    {
        if (node == null)
        {
            return;
        }

        if (node is MeshInstance3D meshInstance && meshInstance.Visible && meshInstance.Mesh != null)
        {
            ExpandProjectedAabb(meshInstance.GetAabb(), meshInstance.GlobalTransform, camera, ref bounds, ref hasPoint);
        }
        else if (node is MultiMeshInstance3D multiMeshInstance && multiMeshInstance.Visible && multiMeshInstance.Multimesh?.Mesh != null)
        {
            ExpandProjectedAabb(multiMeshInstance.GetAabb(), multiMeshInstance.GlobalTransform, camera, ref bounds, ref hasPoint);
        }

        foreach (Node child in node.GetChildren())
        {
            IncludeProjectedMeshBounds(child, camera, ref bounds, ref hasPoint);
        }
    }

    private static void ExpandProjectedAabb(Aabb aabb, Transform3D transform, Camera3D camera, ref Rect2 bounds, ref bool hasPoint)
    {
        Vector3 position = aabb.Position;
        Vector3 size = aabb.Size;

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 localPoint = new(
                        position.X + size.X * x,
                        position.Y + size.Y * y,
                        position.Z + size.Z * z);
                    Vector3 globalPoint = transform * localPoint;
                    if (camera.IsPositionBehind(globalPoint))
                    {
                        continue;
                    }

                    Vector2 screenPoint = camera.UnprojectPosition(globalPoint);
                    if (!hasPoint)
                    {
                        bounds = new Rect2(screenPoint, Vector2.Zero);
                        hasPoint = true;
                        continue;
                    }

                    bounds = bounds.Expand(screenPoint);
                }
            }
        }
    }

    private void SyncLightingRoom()
    {
        if (_previewRoot == null)
        {
            return;
        }

        Node3D roomRoot = _previewRoot.GetNodeOrNull<Node3D>("LightingRoom") ?? new Node3D { Name = "LightingRoom" };
        if (roomRoot.GetParent() != _previewRoot)
        {
            _previewRoot.AddChild(roomRoot);
            _previewRoot.MoveChild(roomRoot, 0);
        }

        roomRoot.Visible = ShowLightingRoom;
        if (!ShowLightingRoom)
        {
            return;
        }

        StandardMaterial3D shellMaterial = CreateRoomMaterial(RoomSurfaceColor, 0.12f, 0.30f);
        StandardMaterial3D accentMaterial = CreateRoomMaterial(RoomAccentColor, 0.18f, 0.22f);

        ConfigureRoomMesh(roomRoot, "Floor", new BoxMesh { Size = new Vector3(8.8f, 0.24f, 8.2f) }, new Vector3(0f, -1.42f, -1.2f), Vector3.Zero, shellMaterial);
        ConfigureRoomMesh(roomRoot, "BackWall", new BoxMesh { Size = new Vector3(8.8f, 5.2f, 0.24f) }, new Vector3(0f, 0.35f, -4.1f), Vector3.Zero, shellMaterial);
        ConfigureRoomMesh(roomRoot, "LeftWall", new BoxMesh { Size = new Vector3(0.24f, 5.2f, 7.6f) }, new Vector3(-4.15f, 0.35f, -0.70f), Vector3.Zero, shellMaterial);
        ConfigureRoomMesh(roomRoot, "RightWall", new BoxMesh { Size = new Vector3(0.24f, 5.2f, 7.6f) }, new Vector3(4.15f, 0.35f, -0.70f), Vector3.Zero, shellMaterial);
        ConfigureRoomMesh(roomRoot, "Ceiling", new BoxMesh { Size = new Vector3(8.8f, 0.24f, 7.6f) }, new Vector3(0f, 2.72f, -0.70f), Vector3.Zero, shellMaterial);
        ConfigureRoomMesh(roomRoot, "Pedestal", new CylinderMesh { TopRadius = 1.18f, BottomRadius = 1.28f, Height = 0.28f, RadialSegments = 28 }, new Vector3(0f, -0.98f, -0.16f), Vector3.Zero, accentMaterial);
        ConfigureRoomMesh(roomRoot, "RearDais", new BoxMesh { Size = new Vector3(2.7f, 2.9f, 0.18f) }, new Vector3(0f, 0.12f, -2.55f), Vector3.Zero, accentMaterial);
    }

    private void TickCameraDrift()
    {
        if (_previewRoot?.GetNodeOrNull<Camera3D>("PreviewCamera") is not Camera3D camera)
        {
            return;
        }

        if (!EnableCameraDrift)
        {
            camera.Position = CameraPosition;
            camera.RotationDegrees = CameraRotationDegrees;
            camera.Fov = _resolvedCameraFov;
            return;
        }

        float driftTime = _time * Mathf.Max(0.01f, CameraDriftSpeed);
        Vector3 positionOffset = new(
            Mathf.Sin(driftTime * 0.93f + 0.45f) * CameraDriftPositionAmplitude.X,
            Mathf.Sin(driftTime * 0.61f + 1.20f) * CameraDriftPositionAmplitude.Y,
            Mathf.Sin(driftTime * 0.47f + 2.05f) * CameraDriftPositionAmplitude.Z);
        Vector3 rotationOffset = new(
            Mathf.Sin(driftTime * 0.74f + 1.10f) * CameraDriftRotationAmplitude.X,
            Mathf.Sin(driftTime * 0.58f + 0.20f) * CameraDriftRotationAmplitude.Y,
            Mathf.Sin(driftTime * 0.52f + 2.30f) * CameraDriftRotationAmplitude.Z);

        camera.Position = CameraPosition + positionOffset;
        camera.RotationDegrees = CameraRotationDegrees + rotationOffset;
        camera.Fov = _resolvedCameraFov;
    }

    private static void ConfigureRoomMesh(Node3D parent, string name, Mesh mesh, Vector3 position, Vector3 rotationDegrees, Material material)
    {
        MeshInstance3D meshInstance = parent.GetNodeOrNull<MeshInstance3D>(name) ?? new MeshInstance3D { Name = name };
        if (meshInstance.GetParent() != parent)
        {
            parent.AddChild(meshInstance);
        }

        meshInstance.Mesh = mesh;
        meshInstance.Position = position;
        meshInstance.RotationDegrees = rotationDegrees;
        meshInstance.MaterialOverride = material;
    }

    private static StandardMaterial3D CreateRoomMaterial(Color albedo, float metallic, float roughness)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Metallic = metallic,
            Roughness = roughness,
        };
    }

    private static Color ResolveTint(EquipmentItem item)
    {
        if (item.Rarity != null)
        {
            return item.Rarity.UiColor;
        }

        return new Color(0.75f, 0.78f, 0.9f, 1f);
    }
}