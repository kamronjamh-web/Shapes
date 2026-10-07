using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using SuperGatchaRebuild;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Shapes;

public static partial class ShapeVisualFactory
{
    private const string ShapeVisualSettingsResourcePath = "res://Resources/Visuals/ShapeVisualSettings.tres";
    private const float ShapeVisualSettingsPollIntervalSeconds = 0.30f;

    private sealed class LiveShapeVisualRegistration
    {
        public WeakReference<Node3D> VisualRef { get; init; } = null!;
        public ShapeData? Shape { get; init; }
        public Color Tint { get; init; }
        public string ShapeKey { get; init; } = string.Empty;
    }

    private sealed partial class ShapeVisualSettingsWatcher : Node
    {
        public override void _EnterTree()
        {
            _shapeVisualSettingsWatcherAttachQueued = false;
        }

        public override void _Ready()
        {
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            PumpShapeVisualSettingsHotReload();
        }
    }

    private static Shader? _celBodyShader;
    private static Shader? _doubleSidedCelBodyShader;
    private static Shader? _opaqueCelBodyShader;
    private static Shader? _doubleSidedOpaqueCelBodyShader;
    private static ShapeVisualSettings? _shapeVisualSettings;
    private static readonly List<LiveShapeVisualRegistration> LiveShapeVisuals = new();
    private static readonly Callable ShapeVisualSettingsChangedCallable = Callable.From((Action)OnShapeVisualSettingsChanged);
    private static ShapeVisualSettings? _observedShapeVisualSettings;
    private static ShapeVisualSettingsWatcher? _shapeVisualSettingsWatcher;
    private static bool _shapeVisualSettingsWatcherAttachQueued;
    private static DateTime _shapeVisualSettingsLastWriteUtc;
    private static ulong _shapeVisualSettingsNextPollAtMsec;

    internal static float StandardCelOutlineWidth => GetShapeVisualSettings().StandardCelOutlineWidth;
    internal static float SlimCelOutlineWidth => GetShapeVisualSettings().SlimCelOutlineWidth;

    private static Color NeutralCelBaseColor => GetShapeVisualSettings().NeutralCelBaseColor;
    private static Color NeutralCelShadowColor => GetShapeVisualSettings().NeutralCelShadowColor;
    private static Color NeutralCelHighlightColor => GetShapeVisualSettings().NeutralCelHighlightColor;
    private static Color NeutralCelRimColor => GetShapeVisualSettings().NeutralCelRimColor;
    private static Color NeutralCelPointColor => GetShapeVisualSettings().NeutralCelPointColor;

    public static Node3D CreateVisual(ShapeData? shape, Color tint, bool allowSceneInstance = true)
    {
        string key = shape?.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        if (allowSceneInstance && shape?.VisualScene != null && !HasGeneratedVisual(key))
        {
            Node sceneNode = shape.VisualScene.Instantiate();
            if (sceneNode is Node3D sceneVisual)
            {
                sceneVisual.Name = BuildVisualName(shape);
                ApplyShapeTransform(sceneVisual, shape);
                TintVisual(sceneVisual, tint);
                return sceneVisual;
            }

            sceneNode.QueueFree();
        }

        if (TryCreateAnimatedVisual(key, tint, out Node3D? animatedVisual) && animatedVisual != null)
        {
            animatedVisual.Name = BuildVisualName(shape);
            ApplyShapeTransform(animatedVisual, shape);
            RegisterLiveShapeVisual(animatedVisual, shape, tint, key);
            return animatedVisual;
        }

        Node3D root = new() { Name = BuildVisualName(shape) };

        switch (key)
        {
            case "square":
                BuildSquare(root, tint);
                break;
            case "circle":
                BuildCircle(root, tint);
                break;
            case "triangle":
                BuildTriangle(root, tint);
                break;
            case "hexagon":
                BuildHexagon(root, tint);
                break;
            case "sphere":
                BuildSphere(root, tint);
                break;
            case "cube":
                BuildCube(root, tint);
                break;
            case "pyramid":
                BuildPyramid(root, tint);
                break;
            case "cone":
                BuildCone(root, tint);
                break;
            case "tesseract":
                BuildTesseract(root, tint);
                break;
            case "hyperphere":
                BuildHyperphere(root, tint);
                break;
            case "pentachoron":
                BuildPentachoron(root, tint);
                break;
            case "hypercone":
                BuildHypercone(root, tint);
                break;
            case "impossiblecube":
                BuildImpossibleCube(root, tint);
                break;
            case "mobius":
                BuildMobius(root, tint);
                break;
            case "klein":
                BuildKlein(root, tint);
                break;
            case "boys":
                BuildBoysSurface(root, tint);
                break;
            default:
                BuildSphere(root, tint);
                break;
        }

        ApplyShapeTransform(root, shape);
        RegisterLiveShapeVisual(root, shape, tint, key);
        return root;
    }

    public static bool UsesNativeAnimation(Node3D? visual)
    {
        return visual is LegacyShapeVisualAnimator or TesseractVisualAnimator;
    }

    private static bool TryCreateAnimatedVisual(string key, Color tint, out Node3D? visual)
    {
        switch (key)
        {
            case "sphere":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.FacetedSphere, tint, 1.67f);
                return true;
            case "tesseract":
                visual = CreateTesseractAnimatedVisual(tint);
                return true;
            case "hyperphere":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.Hyperphere, tint, 0.72f);
                return true;
            case "pentachoron":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.Pentachoron, tint, 0.90f);
                return true;
            case "hypercone":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.Hypercone, tint, 0.78f);
                return true;
            case "impossiblecube":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.ImpossibleCube, tint, 6.10f);
                return true;
            case "mobius":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.Mobius, tint, 0.60f);
                return true;
            case "klein":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.Klein, tint, 0.48f);
                return true;
            case "boys":
                visual = CreateLegacyAnimatedVisual(LegacyShapeVisualMode.BoysSurface, tint, 1.00f);
                return true;
            default:
                visual = null;
                return false;
        }
    }

    private static LegacyShapeVisualAnimator CreateLegacyAnimatedVisual(LegacyShapeVisualMode mode, Color tint, float animationSpeed)
    {
        LegacyShapeVisualAnimator visual = new()
        {
            Mode = mode,
            AnimationSpeed = animationSpeed,
        };
        visual.Configure(mode, tint);
        return visual;
    }

    private static TesseractVisualAnimator CreateTesseractAnimatedVisual(Color tint)
    {
        StandardMaterial3D material = CreateMaterial(tint, tint.Lightened(0.2f), 0.9f, false, true);
        TesseractVisualAnimator visual = new()
        {
            AnimationSpeed = 0.30f,
            ProjectionScale = 0.42f,
            VertexRadius = 0.035f,
        };
        visual.Configure(tint, material);
        return visual;
    }

    private static bool HasGeneratedVisual(string key)
    {
        return key is "square" or "circle" or "triangle" or "hexagon"
            or "sphere" or "cube" or "pyramid" or "cone"
            or "tesseract" or "hyperphere" or "pentachoron" or "hypercone"
            or "impossiblecube" or "mobius" or "klein" or "boys";
    }

    private static string BuildVisualName(ShapeData? shape)
    {
        string displayName = string.IsNullOrWhiteSpace(shape?.DisplayName) ? "Shape" : shape.DisplayName;
        return displayName.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal) + "Visual";
    }

    private static void ApplyShapeTransform(Node3D visual, ShapeData? shape)
    {
        if (shape == null)
        {
            return;
        }

        Vector3 safeBaseScale = RuntimeMath.FiniteOr(visual.Scale, Vector3.One);
        Vector3 safeVisualScale = RuntimeMath.FiniteOr(shape.VisualScale, Vector3.One);
        Vector3 combinedScale = new(
            safeBaseScale.X * safeVisualScale.X,
            safeBaseScale.Y * safeVisualScale.Y,
            safeBaseScale.Z * safeVisualScale.Z);
        visual.Scale = RuntimeMath.IsFinite(combinedScale) ? combinedScale : Vector3.One;

        Vector3 safeRotationDegrees = RuntimeMath.FiniteOr(shape.VisualRotationDegrees, Vector3.Zero);
        Vector3 combinedRotation = RuntimeMath.FiniteOr(visual.RotationDegrees + safeRotationDegrees, visual.RotationDegrees);
        if (RuntimeMath.IsFinite(combinedRotation))
        {
            visual.RotationDegrees = combinedRotation;
        }
    }

    private static void TintVisual(Node node, Color tint)
    {
        if (node is MeshInstance3D meshInstance)
        {
            meshInstance.MaterialOverride = CreateMaterial(tint, tint, 0.45f, false, false);
        }

        foreach (Node child in node.GetChildren())
        {
            TintVisual(child, tint);
        }
    }

    private static void BuildSquare(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(
            root,
            "SquarePlate",
            new BoxMesh { Size = new Vector3(0.9f, 0.12f, 0.9f) },
            tint,
            StandardCelOutlineWidth);
    }

    private static void BuildCircle(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(
            root,
            "CircleDisk",
            new CylinderMesh { TopRadius = 0.52f, BottomRadius = 0.52f, Height = 0.12f, RadialSegments = 48 },
            tint,
            StandardCelOutlineWidth);
    }

    private static void BuildTriangle(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(
            root,
            "TrianglePrism",
            new PrismMesh { LeftToRight = 0.8f, Size = new Vector3(0.95f, 0.18f, 0.95f) },
            tint,
            StandardCelOutlineWidth,
            new Vector3(0f, 0f, 90f));
    }

    private static void BuildHexagon(Node3D root, Color tint)
    {
        const float hexagonRadius = 0.55f;
        const float hexagonHeight = 0.14f;
        float hexagonOutlineWidth = StandardCelOutlineWidth;
        float hexagonApothem = hexagonRadius * 0.8660254f;
        float hexagonOutlineScale = (hexagonApothem + hexagonOutlineWidth) / hexagonApothem;
        float hexagonOutlineHeight = hexagonHeight + (hexagonOutlineWidth * 2f);

        CylinderMesh hexagonCoreMesh = new()
        {
            TopRadius = hexagonRadius,
            BottomRadius = hexagonRadius,
            Height = hexagonHeight,
            RadialSegments = 6,
        };

        CylinderMesh hexagonOutlineMesh = new()
        {
            TopRadius = hexagonRadius * hexagonOutlineScale,
            BottomRadius = hexagonRadius * hexagonOutlineScale,
            Height = hexagonOutlineHeight,
            RadialSegments = 6,
        };

        MeshInstance3D core = AddMesh(
            root,
            "HexagonCore",
            hexagonCoreMesh,
            CreateNeutralCelShadedMaterial());
        core.Position = Vector3.Zero;

        MeshInstance3D outline = AddMesh(
            root,
            "HexagonRarityOutline",
            hexagonOutlineMesh,
            CreateRarityOutlineShellMaterial(ResolveCelOutlineColor(tint), 0f));
        outline.Position = Vector3.Zero;
        outline.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static void BuildSphere(Node3D root, Color tint)
    {
        SphereMesh sphereMesh = new()
        {
            Radius = 0.52f,
            Height = 1.04f,
            RadialSegments = 32,
            Rings = 16,
        };

        AddMesh(root, "Sphere", sphereMesh, CreateNeutralCelShadedMaterial());
        MeshInstance3D outline = AddMesh(root, "SphereOutline", sphereMesh, CreateRarityOutlineShellMaterial(ResolveCelOutlineColor(tint), StandardCelOutlineWidth));
        outline.Scale = CalculateOutlineScale(sphereMesh, StandardCelOutlineWidth);
        outline.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static void BuildCube(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(
            root,
            "Cube",
            new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.9f) },
            tint,
            StandardCelOutlineWidth,
            new Vector3(18f, 32f, 8f));
    }

    private static void BuildPyramid(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(root, "Pyramid", CreatePyramidMesh(), tint, StandardCelOutlineWidth);
    }

    private static void BuildCone(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(
            root,
            "Cone",
            new CylinderMesh { TopRadius = 0f, BottomRadius = 0.52f, Height = 1.0f, RadialSegments = 40 },
            tint,
            StandardCelOutlineWidth);
    }

    private static void BuildTesseract(Node3D root, Color tint)
    {
        StandardMaterial3D lineMaterial = CreateMaterial(tint, tint.Lightened(0.2f), 1.0f, false, true);
        AddMesh(root, "TesseractEdges4D", CreateProjectedTesseractMesh(), lineMaterial);
        AddMesh(root, "TesseractVertices4D", CreateProjectedTesseractPointMesh(), CreatePointMaterial(tint.Lightened(0.16f), 6f));
    }

    private static void BuildHyperphere(Node3D root, Color tint)
    {
        AddMesh(root, "HyperpherePointCloud4D", CreateHyperpherePointMesh(), CreatePointMaterial(tint.Lightened(0.10f), 4.5f));
        AddMesh(root, "HyperphereGreatCircles", CreateHyperphereRingMesh(), CreateMaterial(tint.Lightened(0.12f), tint, 0.86f, true, true));
    }

    private static void BuildPentachoron(Node3D root, Color tint)
    {
        AddMesh(root, "PentachoronLines", CreateCompleteGraphMesh(new[]
        {
            new Vector3(0f, 0.62f, 0f),
            new Vector3(-0.55f, -0.28f, 0.42f),
            new Vector3(0.55f, -0.28f, 0.42f),
            new Vector3(0f, -0.28f, -0.58f),
            new Vector3(0f, 0.02f, 0.02f),
        }), CreateMaterial(tint, tint.Lightened(0.18f), 0.88f, false, true));
    }

    private static void BuildHypercone(Node3D root, Color tint)
    {
        AddMesh(root, "HyperconePointCloud4D", CreateHyperconePointMesh(), CreatePointMaterial(tint.Lightened(0.08f), 4f));
        AddMesh(root, "HyperconeRings4D", CreateHyperconeWireMesh(), CreateMaterial(tint.Lightened(0.16f), tint, 0.8f, true, true));
    }

    private static void BuildImpossibleCube(Node3D root, Color tint)
    {
        StandardMaterial3D material = CreateMaterial(tint, tint.Lightened(0.18f), 1.0f, false, true);
        AddMesh(root, "ImpossibleParadoxBars", CreateImpossibleCubeMesh(), material);
        AddMesh(root, "ImpossibleOverdrawCorners", CreateImpossibleCubeOverdrawMesh(), CreateMaterial(tint.Lightened(0.22f), tint.Lightened(0.22f), 1.2f, false, true));
    }

    private static void BuildMobius(Node3D root, Color tint)
    {
        AddNeutralCelMeshWithOutline(
            root,
            "MobiusStrip",
            CreateMobiusMesh(),
            tint,
            SlimCelOutlineWidth,
            new Vector3(12f, 0f, 0f));
        AddMesh(root, "MobiusSpine4D", CreateMobiusSpineMesh(), CreateNeutralCelAccentMaterial(0.72f, false, true));
    }

    private static void BuildKlein(Node3D root, Color tint)
    {
        AddMesh(root, "KleinBottleWire4D", CreateKleinBottleWireMesh(), CreateMaterial(tint.Lightened(0.10f), tint, 0.82f, true, true));
        AddMesh(root, "KleinBottlePoints4D", CreateKleinBottlePointMesh(), CreatePointMaterial(tint.Lightened(0.18f), 3.2f));
    }

    private static void BuildBoysSurface(Node3D root, Color tint)
    {
        AddMesh(root, "BoysSurfaceWire", CreateBoysSurfaceWireMesh(), CreateMaterial(tint.Lightened(0.08f), tint, 0.78f, true, true));
        AddMesh(root, "BoysSurfacePoints", CreateBoysSurfacePointMesh(), CreatePointMaterial(tint.Lightened(0.18f), 3f));
    }

    private static MeshInstance3D AddMesh(Node3D parent, string name, Mesh mesh, Material material, Vector3? position = null)
    {
        MeshInstance3D meshInstance = new()
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            Position = position ?? Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        parent.AddChild(meshInstance);
        return meshInstance;
    }

    private static void AddNeutralCelMeshWithOutline(Node3D root, string name, Mesh mesh, Color tint, float outlineWidth, Vector3? rotationDegrees = null)
    {
        MeshInstance3D core = AddMesh(root, name, mesh, CreateNeutralCelShadedMaterial());
        MeshInstance3D outline = AddMesh(root, name + "Outline", mesh, CreateRarityOutlineShellMaterial(ResolveCelOutlineColor(tint), 0f));
        outline.Scale = CalculateOutlineScale(mesh, outlineWidth);
        outline.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        if (rotationDegrees.HasValue)
        {
            core.RotationDegrees = rotationDegrees.Value;
            outline.RotationDegrees = rotationDegrees.Value;
        }
    }

    private static StandardMaterial3D CreateMaterial(Color albedo, Color emission, float emissionEnergy, bool transparent = false, bool unshaded = false)
    {
        bool whiteGlow = IsWhiteGlowTint(albedo) || IsWhiteGlowTint(emission);
        Color finalAlbedo = whiteGlow ? Colors.White : albedo;
        Color finalEmission = whiteGlow ? Colors.White : emission;
        float finalEmissionEnergy = whiteGlow ? Mathf.Max(emissionEnergy * 1.7f, 1.15f) : emissionEnergy;

        StandardMaterial3D material = new()
        {
            AlbedoColor = finalAlbedo,
            EmissionEnabled = true,
            Emission = finalEmission,
            EmissionEnergyMultiplier = finalEmissionEnergy,
            Metallic = 0.08f,
            Roughness = whiteGlow ? 0.18f : 0.34f,
        };

        if (whiteGlow)
        {
            material.RimEnabled = true;
            material.Rim = 1.0f;
            material.RimTint = 0.7f;
        }

        if (transparent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
        }

        if (unshaded)
        {
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        }

        return material;
    }

    internal static ShaderMaterial CreateNeutralCelShadedMaterial(bool doubleSided = false)
    {
        ShaderMaterial material = new()
        {
            Shader = GetCelBodyShader(doubleSided),
        };

        material.SetShaderParameter("base_color", NeutralCelBaseColor);
        material.SetShaderParameter("shadow_color", NeutralCelShadowColor);
        material.SetShaderParameter("highlight_color", NeutralCelHighlightColor);
        material.SetShaderParameter("rim_color", NeutralCelRimColor);
        return material;
    }

    internal static ShaderMaterial CreateOpaqueNeutralCelShadedMaterial(bool doubleSided = false)
    {
        ShaderMaterial material = new()
        {
            Shader = GetCelBodyShader(doubleSided, opaque: true),
        };

        material.SetShaderParameter("base_color", NeutralCelBaseColor);
        material.SetShaderParameter("shadow_color", NeutralCelShadowColor);
        material.SetShaderParameter("highlight_color", NeutralCelHighlightColor);
        material.SetShaderParameter("rim_color", NeutralCelRimColor);
        return material;
    }

    internal static StandardMaterial3D CreateNeutralCelCoreMaterial(bool doubleSided = false)
    {
        StandardMaterial3D material = CreateMaterial(NeutralCelBaseColor, NeutralCelHighlightColor, 0.08f, false, false);
        material.Metallic = 0.0f;
        material.Roughness = 0.92f;

        if (doubleSided)
        {
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        return material;
    }

    internal static Color ResolveCelOutlineColor(Color tint)
    {
        ShapeVisualSettings settings = GetShapeVisualSettings();
        Color outline = BlendColors(settings.OutlineBaseDarkColor, tint.Darkened(settings.OutlineTintDarken), settings.OutlineTintMix);
        outline.A = 1f;
        return outline;
    }

    internal static Color ResolveCelAccentColor(Color tint, float tintMix = 0.18f)
    {
        return BlendColors(new Color(0.88f, 0.92f, 0.98f, 1f), tint.Lightened(0.16f), Mathf.Clamp(tintMix, 0f, 1f));
    }

    internal static Color ResolveNeutralCelPointColor()
    {
        return NeutralCelPointColor;
    }

    internal static StandardMaterial3D CreateNeutralCelAccentMaterial(float emissionEnergy = 0.72f, bool transparent = true, bool unshaded = true)
    {
        return CreateMaterial(NeutralCelPointColor, NeutralCelHighlightColor, emissionEnergy, transparent, unshaded);
    }

    internal static Vector3 CalculateOutlineScale(Mesh mesh, float outlineWidth)
    {
        const float minSize = 0.0001f;
        Aabb bounds = mesh.GetAabb();
        Vector3 size = bounds.Size;
        float widthDelta = outlineWidth * 2f;

        return new Vector3(
            size.X > minSize ? (size.X + widthDelta) / size.X : 1f,
            size.Y > minSize ? (size.Y + widthDelta) / size.Y : 1f,
            size.Z > minSize ? (size.Z + widthDelta) / size.Z : 1f);
    }

    internal static StandardMaterial3D CreateRarityOutlineShellMaterial(Color color, float outlineWidth)
    {
        _ = outlineWidth;

        ShapeVisualSettings settings = GetShapeVisualSettings();

        Color outlineColor = color;
        outlineColor.A = 1f;
        Color emissionColor = BlendColors(outlineColor.Lightened(settings.OutlineEmissionLightenAmount), Colors.White, settings.OutlineEmissionWhiteMix);
        emissionColor.A = 1f;

        StandardMaterial3D material = new()
        {
            AlbedoColor = outlineColor,
            EmissionEnabled = true,
            Emission = emissionColor,
            EmissionEnergyMultiplier = settings.OutlineEmissionEnergy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            Roughness = 1.0f,
            Metallic = 0.0f,
        };
        return material;
    }

    private static ShapeVisualSettings GetShapeVisualSettings()
    {
        EnsureShapeVisualSettingsLiveWatcher();

        if (_shapeVisualSettings != null && GodotObject.IsInstanceValid(_shapeVisualSettings))
        {
            EnsureShapeVisualSettingsWatcher(_shapeVisualSettings);
            CaptureShapeVisualSettingsWriteTimeIfNeeded();
            return _shapeVisualSettings;
        }

        _shapeVisualSettings = ResourceLoader.Load<ShapeVisualSettings>(ShapeVisualSettingsResourcePath);
        _shapeVisualSettings ??= new ShapeVisualSettings();
        EnsureShapeVisualSettingsWatcher(_shapeVisualSettings);
        CaptureShapeVisualSettingsWriteTimeIfNeeded();
        return _shapeVisualSettings;
    }

    private static void EnsureShapeVisualSettingsLiveWatcher()
    {
        if (Engine.GetMainLoop() is not SceneTree sceneTree || sceneTree.Root == null)
        {
            return;
        }

        if (_shapeVisualSettingsWatcher != null
            && GodotObject.IsInstanceValid(_shapeVisualSettingsWatcher)
            && !_shapeVisualSettingsWatcher.IsQueuedForDeletion())
        {
            if (_shapeVisualSettingsWatcher.IsInsideTree() || _shapeVisualSettingsWatcherAttachQueued)
            {
                return;
            }

            _shapeVisualSettingsWatcherAttachQueued = true;
            sceneTree.Root.CallDeferred(Node.MethodName.AddChild, _shapeVisualSettingsWatcher);
            return;
        }

        _shapeVisualSettingsWatcher = new ShapeVisualSettingsWatcher
        {
            Name = "ShapeVisualSettingsWatcher",
        };
        _shapeVisualSettingsWatcherAttachQueued = true;
        sceneTree.Root.CallDeferred(Node.MethodName.AddChild, _shapeVisualSettingsWatcher);
    }

    public static void PumpShapeVisualSettingsHotReload()
    {
        ulong now = Time.GetTicksMsec();
        if (_shapeVisualSettingsNextPollAtMsec != 0 && now < _shapeVisualSettingsNextPollAtMsec)
        {
            return;
        }

        _shapeVisualSettingsNextPollAtMsec = now + (ulong)Mathf.RoundToInt(ShapeVisualSettingsPollIntervalSeconds * 1000f);
        DateTime currentWriteUtc = GetShapeVisualSettingsWriteTimeUtc();
        if (currentWriteUtc == DateTime.MinValue)
        {
            return;
        }

        if (_shapeVisualSettingsLastWriteUtc == DateTime.MinValue)
        {
            _shapeVisualSettingsLastWriteUtc = currentWriteUtc;
            return;
        }

        if (currentWriteUtc <= _shapeVisualSettingsLastWriteUtc)
        {
            return;
        }

        _shapeVisualSettingsLastWriteUtc = currentWriteUtc;
        ReloadShapeVisualSettingsFromDisk();
    }

    private static void CaptureShapeVisualSettingsWriteTimeIfNeeded()
    {
        if (_shapeVisualSettingsLastWriteUtc != DateTime.MinValue)
        {
            return;
        }

        _shapeVisualSettingsLastWriteUtc = GetShapeVisualSettingsWriteTimeUtc();
    }

    private static DateTime GetShapeVisualSettingsWriteTimeUtc()
    {
        string absolutePath = ProjectSettings.GlobalizePath(ShapeVisualSettingsResourcePath);
        return File.Exists(absolutePath)
            ? File.GetLastWriteTimeUtc(absolutePath)
            : DateTime.MinValue;
    }

    private static void ReloadShapeVisualSettingsFromDisk()
    {
        ShapeVisualSettings? reloaded = ResourceLoader.Load(ShapeVisualSettingsResourcePath, string.Empty, ResourceLoader.CacheMode.Replace) as ShapeVisualSettings;
        if (reloaded == null)
        {
            return;
        }

        _shapeVisualSettings = reloaded;
        EnsureShapeVisualSettingsWatcher(_shapeVisualSettings);
        RefreshLiveShapeVisuals();
    }

    private static void EnsureShapeVisualSettingsWatcher(ShapeVisualSettings settings)
    {
        if (_observedShapeVisualSettings != null
            && GodotObject.IsInstanceValid(_observedShapeVisualSettings)
            && !ReferenceEquals(_observedShapeVisualSettings, settings))
        {
            DisconnectShapeVisualSettingsWatcher(_observedShapeVisualSettings);
        }

        if (!settings.IsConnected(Resource.SignalName.Changed, ShapeVisualSettingsChangedCallable))
        {
            settings.Connect(Resource.SignalName.Changed, ShapeVisualSettingsChangedCallable);
        }

        _observedShapeVisualSettings = settings;
    }

    private static void DisconnectShapeVisualSettingsWatcher(ShapeVisualSettings settings)
    {
        if (!settings.IsConnected(Resource.SignalName.Changed, ShapeVisualSettingsChangedCallable))
        {
            return;
        }

        settings.Disconnect(Resource.SignalName.Changed, ShapeVisualSettingsChangedCallable);
    }

    private static void OnShapeVisualSettingsChanged()
    {
        RefreshLiveShapeVisuals();
    }

    private static void RefreshLiveShapeVisuals()
    {
        for (int registrationIndex = LiveShapeVisuals.Count - 1; registrationIndex >= 0; registrationIndex--)
        {
            LiveShapeVisualRegistration registration = LiveShapeVisuals[registrationIndex];
            if (!registration.VisualRef.TryGetTarget(out Node3D? visual)
                || visual == null
                || !GodotObject.IsInstanceValid(visual)
                || visual.IsQueuedForDeletion())
            {
                LiveShapeVisuals.RemoveAt(registrationIndex);
                continue;
            }

            if (visual is LegacyShapeVisualAnimator legacyVisual)
            {
                legacyVisual.RefreshForShapeVisualSettings();
                continue;
            }

            RefreshGeneratedVisual(visual, registration.ShapeKey, registration.Tint);
        }
    }

    private static void RegisterLiveShapeVisual(Node3D visual, ShapeData? shape, Color tint, string key)
    {
        if (!UsesShapeVisualSettings(key, visual))
        {
            return;
        }

        LiveShapeVisuals.Add(new LiveShapeVisualRegistration
        {
            VisualRef = new WeakReference<Node3D>(visual),
            Shape = shape,
            Tint = tint,
            ShapeKey = key,
        });
    }

    private static bool UsesShapeVisualSettings(string key, Node3D visual)
    {
        if (visual is LegacyShapeVisualAnimator)
        {
            return true;
        }

        return HasGeneratedVisual(key) && key != "tesseract";
    }

    private static void RefreshGeneratedVisual(Node3D root, string key, Color tint)
    {
        foreach (Node child in root.GetChildren())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }

        switch (key)
        {
            case "square":
                BuildSquare(root, tint);
                break;
            case "circle":
                BuildCircle(root, tint);
                break;
            case "triangle":
                BuildTriangle(root, tint);
                break;
            case "hexagon":
                BuildHexagon(root, tint);
                break;
            case "sphere":
                BuildSphere(root, tint);
                break;
            case "cube":
                BuildCube(root, tint);
                break;
            case "pyramid":
                BuildPyramid(root, tint);
                break;
            case "cone":
                BuildCone(root, tint);
                break;
            case "hyperphere":
                BuildHyperphere(root, tint);
                break;
            case "pentachoron":
                BuildPentachoron(root, tint);
                break;
            case "hypercone":
                BuildHypercone(root, tint);
                break;
            case "impossiblecube":
                BuildImpossibleCube(root, tint);
                break;
            case "mobius":
                BuildMobius(root, tint);
                break;
            case "klein":
                BuildKlein(root, tint);
                break;
            case "boys":
                BuildBoysSurface(root, tint);
                break;
        }
    }

    private static Shader GetCelBodyShader(bool doubleSided = false, bool opaque = false)
    {
        if (opaque && doubleSided)
        {
            if (_doubleSidedOpaqueCelBodyShader != null)
            {
                return _doubleSidedOpaqueCelBodyShader;
            }

            _doubleSidedOpaqueCelBodyShader = CreateCelBodyShader("cull_disabled", opaque: true);
            return _doubleSidedOpaqueCelBodyShader;
        }

        if (opaque)
        {
            if (_opaqueCelBodyShader != null)
            {
                return _opaqueCelBodyShader;
            }

            _opaqueCelBodyShader = CreateCelBodyShader("cull_back", opaque: true);
            return _opaqueCelBodyShader;
        }

        if (doubleSided)
        {
            if (_doubleSidedCelBodyShader != null)
            {
                return _doubleSidedCelBodyShader;
            }

            _doubleSidedCelBodyShader = CreateCelBodyShader("cull_disabled", opaque: false);
            return _doubleSidedCelBodyShader;
        }

        if (_celBodyShader != null)
        {
            return _celBodyShader;
        }

        _celBodyShader = CreateCelBodyShader("cull_back", opaque: false);
        return _celBodyShader;
    }

    private static Shader CreateCelBodyShader(string cullMode, bool opaque)
    {
        string alphaLine = opaque ? string.Empty : "    ALPHA = base_color.a;\n";

        return new Shader
        {
            Code = "shader_type spatial;\n"
                + $"render_mode {cullMode}, depth_draw_opaque, ambient_light_disabled, specular_disabled;\n\n"
                + @"uniform vec4 base_color : source_color = vec4(0.8, 0.84, 0.9, 1.0);
uniform vec4 shadow_color : source_color = vec4(0.3, 0.34, 0.42, 1.0);
uniform vec4 highlight_color : source_color = vec4(0.96, 0.98, 1.0, 1.0);
uniform vec4 rim_color : source_color = vec4(0.9, 0.94, 1.0, 1.0);
uniform float ambient_strength = 0.16;
uniform float shadow_threshold = 0.28;
uniform float highlight_threshold = 0.78;
uniform float band_softness = 0.05;
uniform float rim_strength = 0.10;
uniform float rim_power = 3.6;

void fragment() {
    ALBEDO = base_color.rgb;
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;

    float rim = pow(1.0 - max(dot(normalize(NORMAL), normalize(VIEW)), 0.0), rim_power);
    EMISSION = base_color.rgb * ambient_strength + rim_color.rgb * rim * rim_strength;
"
                + alphaLine
                + @"}

void light() {
    float ndotl = dot(normalize(NORMAL), normalize(LIGHT));
    float lit_band = smoothstep(shadow_threshold - band_softness, shadow_threshold + band_softness, ndotl);
    float highlight_band = smoothstep(highlight_threshold - band_softness, highlight_threshold + band_softness, ndotl);

    vec3 toon_color = mix(shadow_color.rgb, base_color.rgb, lit_band);
    toon_color = mix(toon_color, highlight_color.rgb, highlight_band);
    DIFFUSE_LIGHT += toon_color * LIGHT_COLOR * ATTENUATION * 0.85;
}
",
        };
    }

    private static StandardMaterial3D CreatePointMaterial(Color color, float pointSize)
    {
        StandardMaterial3D material = CreateMaterial(color, color, 1.0f, true, true);
        material.UsePointSize = true;
        material.PointSize = pointSize;
        return material;
    }

    private static Color BlendColors(Color from, Color to, float weight)
    {
        float clampedWeight = Mathf.Clamp(weight, 0f, 1f);
        return new Color(
            Mathf.Lerp(from.R, to.R, clampedWeight),
            Mathf.Lerp(from.G, to.G, clampedWeight),
            Mathf.Lerp(from.B, to.B, clampedWeight),
            Mathf.Lerp(from.A, to.A, clampedWeight));
    }

    private static bool IsWhiteGlowTint(Color color)
    {
        return color.R >= 0.96f && color.G >= 0.96f && color.B >= 0.96f;
    }

    private static ArrayMesh CreatePyramidMesh()
    {
        Vector3 top = new(0f, 0.58f, 0f);
        Vector3 a = new(-0.50f, -0.42f, -0.50f);
        Vector3 b = new(0.50f, -0.42f, -0.50f);
        Vector3 c = new(0.50f, -0.42f, 0.50f);
        Vector3 d = new(-0.50f, -0.42f, 0.50f);

        return CreateTriangleMesh(new[]
        {
            top, a, b,
            top, b, c,
            top, c, d,
            top, d, a,
            a, d, c,
            a, c, b,
        });
    }

    private static ArrayMesh CreateCubeLineMesh(float size, Vector3 offset)
    {
        float half = size * 0.5f;
        Vector3[] corners =
        {
            new(-half, -half, -half), new(half, -half, -half), new(half, half, -half), new(-half, half, -half),
            new(-half, -half, half), new(half, -half, half), new(half, half, half), new(-half, half, half),
        };

        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] += offset;
        }

        return CreateLineMesh(new[]
        {
            corners[0], corners[1], corners[1], corners[2], corners[2], corners[3], corners[3], corners[0],
            corners[4], corners[5], corners[5], corners[6], corners[6], corners[7], corners[7], corners[4],
            corners[0], corners[4], corners[1], corners[5], corners[2], corners[6], corners[3], corners[7],
        });
    }

    private static ArrayMesh CreateTesseractConnectorMesh(float outerSize, float innerSize, Vector3 innerOffset)
    {
        float outerHalf = outerSize * 0.5f;
        float innerHalf = innerSize * 0.5f;
        List<Vector3> points = new();

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    points.Add(new Vector3(x * outerHalf, y * outerHalf, z * outerHalf));
                    points.Add(new Vector3(x * innerHalf, y * innerHalf, z * innerHalf) + innerOffset);
                }
            }
        }

        return CreateLineMesh(points.ToArray());
    }

    private static ArrayMesh CreateCompleteGraphMesh(Vector3[] vertices)
    {
        List<Vector3> points = new();
        for (int i = 0; i < vertices.Length; i++)
        {
            for (int j = i + 1; j < vertices.Length; j++)
            {
                points.Add(vertices[i]);
                points.Add(vertices[j]);
            }
        }

        return CreateLineMesh(points.ToArray());
    }

    private static ArrayMesh CreateImpossibleCubeConnectorMesh()
    {
        return CreateLineMesh(new[]
        {
            new Vector3(-0.45f, -0.25f, 0.45f), new Vector3(0.42f, -0.47f, -0.10f),
            new Vector3(-0.45f, 0.45f, 0.45f), new Vector3(0.42f, 0.23f, -0.10f),
            new Vector3(0.25f, -0.25f, 0.45f), new Vector3(0.68f, -0.47f, -0.36f),
            new Vector3(0.25f, 0.45f, 0.45f), new Vector3(0.68f, 0.23f, -0.36f),
        });
    }

    private static ArrayMesh CreateProjectedTesseractMesh()
    {
        List<Point4> vertices = CreateTesseractVertices();
        List<Vector3> points = new();

        for (int i = 0; i < vertices.Count; i++)
        {
            for (int j = i + 1; j < vertices.Count; j++)
            {
                if (CoordinateDifferenceCount(vertices[i], vertices[j]) != 1)
                {
                    continue;
                }

                points.Add(ProjectForTesseract(vertices[i]));
                points.Add(ProjectForTesseract(vertices[j]));
            }
        }

        return CreateLineMesh(points.ToArray());
    }

    private static ArrayMesh CreateProjectedTesseractPointMesh()
    {
        List<Point4> vertices = CreateTesseractVertices();
        Vector3[] points = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            points[i] = ProjectForTesseract(vertices[i]);
        }

        return CreatePointMesh(points);
    }

    private static List<Point4> CreateTesseractVertices()
    {
        List<Point4> vertices = new(16);
        for (int i = 0; i < 16; i++)
        {
            Point4 point = new(
                (i & 1) != 0 ? 1f : -1f,
                (i & 2) != 0 ? 1f : -1f,
                (i & 4) != 0 ? 1f : -1f,
                (i & 8) != 0 ? 1f : -1f);
            vertices.Add(Rotate4D(point, 0.62f, 0.94f, 0.38f, 0.0f));
        }

        return vertices;
    }

    private static Vector3 ProjectForTesseract(Point4 point)
    {
        return Project4DTo3D(point, 4.0f, 0.58f);
    }

    private static int CoordinateDifferenceCount(Point4 left, Point4 right)
    {
        int count = 0;
        if (Mathf.Sign(left.X) != Mathf.Sign(right.X)) count++;
        if (Mathf.Sign(left.Y) != Mathf.Sign(right.Y)) count++;
        if (Mathf.Sign(left.Z) != Mathf.Sign(right.Z)) count++;
        if (Mathf.Sign(left.W) != Mathf.Sign(right.W)) count++;
        return count;
    }

    private static ArrayMesh CreateHyperpherePointMesh()
    {
        const int count = 192;
        Vector3[] points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.3999632f;
            float b = Mathf.Acos(Mathf.Lerp(-1f, 1f, (i + 0.5f) / count));
            float c = i * 1.5337337f;
            float sinB = Mathf.Sin(b);
            Point4 point = new(
                Mathf.Cos(a) * sinB,
                Mathf.Sin(a) * sinB,
                Mathf.Cos(c) * Mathf.Cos(b),
                Mathf.Sin(c) * Mathf.Cos(b));
            point = Rotate4D(point, 0.46f, 0.78f, 0.24f, 0f);
            points[i] = Project4DTo3D(point, 3.2f, 0.58f);
        }

        return CreatePointMesh(points);
    }

    private static ArrayMesh CreateHyperphereRingMesh()
    {
        List<Vector3> points = new();
        AddProjectedGreatCircle(points, 0, 1, 0.62f);
        AddProjectedGreatCircle(points, 0, 2, 0.62f);
        AddProjectedGreatCircle(points, 1, 2, 0.62f);
        AddProjectedGreatCircle(points, 0, 3, 0.62f);
        AddProjectedGreatCircle(points, 1, 3, 0.62f);
        return CreateLineMesh(points.ToArray());
    }

    private static void AddProjectedGreatCircle(List<Vector3> output, int axisA, int axisB, float scale)
    {
        const int segments = 72;
        for (int i = 0; i < segments; i++)
        {
            float t0 = Mathf.Tau * i / segments;
            float t1 = Mathf.Tau * (i + 1) / segments;
            Point4 a = PointOn4DPlane(axisA, axisB, t0);
            Point4 b = PointOn4DPlane(axisA, axisB, t1);
            output.Add(Project4DTo3D(Rotate4D(a, 0.46f, 0.78f, 0.24f, 0f), 3.2f, scale));
            output.Add(Project4DTo3D(Rotate4D(b, 0.46f, 0.78f, 0.24f, 0f), 3.2f, scale));
        }
    }

    private static Point4 PointOn4DPlane(int axisA, int axisB, float angle)
    {
        float[] values = { 0f, 0f, 0f, 0f };
        values[axisA] = Mathf.Cos(angle);
        values[axisB] = Mathf.Sin(angle);
        return new Point4(values[0], values[1], values[2], values[3]);
    }

    private static ArrayMesh CreateHyperconePointMesh()
    {
        const int circles = 20;
        const int pointsPerCircle = 28;
        List<Vector3> points = new(circles * pointsPerCircle + 1);

        for (int i = 0; i < circles; i++)
        {
            float t = (float)i / (circles - 1);
            float w = -1f + t * 2f;
            float radius = (1f - t) * 1.06f;
            for (int j = 0; j < pointsPerCircle; j++)
            {
                float angle = Mathf.Tau * j / pointsPerCircle;
                Point4 point = new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.12f * Mathf.Sin(angle * 2f), w);
                points.Add(Project4DTo3D(Rotate4D(point, 0.50f, 0.32f, 0.22f, 0f), 3.5f, 0.54f));
            }
        }

        points.Add(Project4DTo3D(Rotate4D(new Point4(0f, 0f, 0f, 1f), 0.50f, 0.32f, 0.22f, 0f), 3.5f, 0.54f));
        return CreatePointMesh(points.ToArray());
    }

    private static ArrayMesh CreateHyperconeWireMesh()
    {
        const int circles = 13;
        const int pointsPerCircle = 28;
        Vector3[,] projected = new Vector3[circles, pointsPerCircle];
        List<Vector3> points = new();

        for (int i = 0; i < circles; i++)
        {
            float t = (float)i / (circles - 1);
            float w = -1f + t * 2f;
            float radius = (1f - t) * 1.06f;
            for (int j = 0; j < pointsPerCircle; j++)
            {
                float angle = Mathf.Tau * j / pointsPerCircle;
                Point4 point = new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.12f * Mathf.Sin(angle * 2f), w);
                projected[i, j] = Project4DTo3D(Rotate4D(point, 0.50f, 0.32f, 0.22f, 0f), 3.5f, 0.54f);
            }
        }

        for (int i = 0; i < circles; i++)
        {
            for (int j = 0; j < pointsPerCircle; j++)
            {
                points.Add(projected[i, j]);
                points.Add(projected[i, (j + 1) % pointsPerCircle]);
            }
        }

        for (int j = 0; j < pointsPerCircle; j += 4)
        {
            for (int i = 0; i < circles - 1; i++)
            {
                points.Add(projected[i, j]);
                points.Add(projected[i + 1, j]);
            }
        }

        return CreateLineMesh(points.ToArray());
    }

    private static ArrayMesh CreateImpossibleCubeMesh()
    {
        Vector3[] vertices =
        {
            new(-0.52f, -0.52f, -0.52f), new(0.52f, -0.52f, -0.52f), new(0.52f, 0.52f, -0.52f), new(-0.52f, 0.52f, -0.52f),
            new(-0.52f, -0.52f, 0.52f), new(0.52f, -0.52f, 0.52f), new(0.52f, 0.52f, 0.52f), new(-0.52f, 0.52f, 0.52f),
        };
        int[,] edges =
        {
            {0, 1}, {1, 2}, {2, 3}, {3, 0},
            {4, 5}, {5, 6}, {6, 7}, {7, 4},
            {0, 4}, {1, 5}, {2, 6}, {3, 7},
        };

        List<Vector3> points = new();
        for (int i = 0; i < edges.GetLength(0); i++)
        {
            points.Add(vertices[edges[i, 0]]);
            points.Add(vertices[edges[i, 1]]);
        }

        return CreateLineMesh(points.ToArray());
    }

    private static ArrayMesh CreateImpossibleCubeOverdrawMesh()
    {
        return CreateLineMesh(new[]
        {
            new Vector3(-0.52f, -0.52f, 0.52f), new Vector3(-0.08f, -0.52f, 0.08f),
            new Vector3(0.52f, 0.52f, -0.52f), new Vector3(0.52f, 0.10f, -0.08f),
            new Vector3(-0.52f, 0.52f, 0.52f), new Vector3(-0.10f, 0.52f, 0.10f),
        });
    }

    private static ArrayMesh CreateMobiusSpineMesh()
    {
        const int segments = 180;
        List<Vector3> points = new();
        Vector3 previous = Vector3.Zero;
        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Tau * i / segments;
            Point4 point = new(Mathf.Cos(t), Mathf.Sin(t), 0f, Mathf.Sin(t * 2f) * 0.5f);
            Vector3 projected = Project4DTo3D(Rotate4D(point, 0.55f, 0.72f, 0.40f, 0f), 4.0f, 0.48f);
            if (i > 0)
            {
                points.Add(previous);
                points.Add(projected);
            }

            previous = projected;
        }

        return CreateLineMesh(points.ToArray());
    }

    private static ArrayMesh CreateKleinBottleWireMesh()
    {
        const int uSegments = 34;
        const int vSegments = 22;
        Vector3[,] points = CreateKleinBottleProjectedPoints(uSegments, vSegments);
        List<Vector3> lines = new();

        for (int u = 0; u <= uSegments; u += 2)
        {
            for (int v = 0; v < vSegments; v++)
            {
                lines.Add(points[u, v]);
                lines.Add(points[u, v + 1]);
            }
        }

        for (int v = 0; v <= vSegments; v += 3)
        {
            for (int u = 0; u < uSegments; u++)
            {
                lines.Add(points[u, v]);
                lines.Add(points[u + 1, v]);
            }
        }

        return CreateLineMesh(lines.ToArray());
    }

    private static ArrayMesh CreateKleinBottlePointMesh()
    {
        const int uSegments = 26;
        const int vSegments = 18;
        Vector3[,] grid = CreateKleinBottleProjectedPoints(uSegments, vSegments);
        List<Vector3> points = new();
        for (int u = 0; u <= uSegments; u += 2)
        {
            for (int v = 0; v <= vSegments; v += 2)
            {
                points.Add(grid[u, v]);
            }
        }

        return CreatePointMesh(points.ToArray());
    }

    private static Vector3[,] CreateKleinBottleProjectedPoints(int uSegments, int vSegments)
    {
        Vector3[,] output = new Vector3[uSegments + 1, vSegments + 1];
        for (int uIndex = 0; uIndex <= uSegments; uIndex++)
        {
            float u = Mathf.Tau * uIndex / uSegments;
            for (int vIndex = 0; vIndex <= vSegments; vIndex++)
            {
                float v = Mathf.Tau * vIndex / vSegments;
                float radius = 4f * (1f - Mathf.Cos(u) * 0.5f);
                Point4 point = new(
                    (6f * Mathf.Cos(u) * (1f + Mathf.Sin(u)) + radius * Mathf.Cos(v + Mathf.Pi)) * 0.05f,
                    16f * Mathf.Sin(u) * 0.05f,
                    radius * Mathf.Sin(v + Mathf.Pi) * 0.05f,
                    6f * Mathf.Cos(u) * (1f - Mathf.Sin(u)) * 0.05f);
                point = Rotate4D(point, 0.42f, 0.28f, 0.18f, 0f);
                output[uIndex, vIndex] = Project4DTo3D(point, 4.5f, 0.92f);
            }
        }

        return output;
    }

    private static ArrayMesh CreateBoysSurfaceWireMesh()
    {
        const int uSegments = 26;
        const int vSegments = 48;
        Vector3[,] points = CreateBoysSurfaceProjectedPoints(uSegments, vSegments);
        List<Vector3> lines = new();

        for (int u = 0; u <= uSegments; u += 2)
        {
            for (int v = 0; v < vSegments; v++)
            {
                lines.Add(points[u, v]);
                lines.Add(points[u, v + 1]);
            }
        }

        for (int v = 0; v <= vSegments; v += 4)
        {
            for (int u = 0; u < uSegments; u++)
            {
                lines.Add(points[u, v]);
                lines.Add(points[u + 1, v]);
            }
        }

        return CreateLineMesh(lines.ToArray());
    }

    private static ArrayMesh CreateBoysSurfacePointMesh()
    {
        const int uSegments = 22;
        const int vSegments = 38;
        Vector3[,] grid = CreateBoysSurfaceProjectedPoints(uSegments, vSegments);
        List<Vector3> points = new();
        for (int u = 0; u <= uSegments; u += 2)
        {
            for (int v = 0; v <= vSegments; v += 3)
            {
                points.Add(grid[u, v]);
            }
        }

        return CreatePointMesh(points.ToArray());
    }

    private static Vector3[,] CreateBoysSurfaceProjectedPoints(int uSegments, int vSegments)
    {
        Vector3[,] output = new Vector3[uSegments + 1, vSegments + 1];
        const float fold = 0.65f;
        for (int uIndex = 0; uIndex <= uSegments; uIndex++)
        {
            float u = Mathf.Pi * uIndex / uSegments;
            for (int vIndex = 0; vIndex <= vSegments; vIndex++)
            {
                float v = Mathf.Tau * vIndex / vSegments;
                float lobe = 1f + 0.3f * Mathf.Sin(3f * v) * Mathf.Sin(u);
                float flip = Mathf.Cos(v + fold);
                float x = Mathf.Sin(u) * Mathf.Cos(v) * lobe;
                float y = Mathf.Sin(u) * Mathf.Sin(v) * lobe;
                float z = Mathf.Cos(u) * flip;
                float rotatedX = x * Mathf.Cos(0.72f) - z * Mathf.Sin(0.72f);
                float rotatedZ = x * Mathf.Sin(0.72f) + z * Mathf.Cos(0.72f);
                float tiltedY = y * Mathf.Cos(0.30f) - rotatedZ * Mathf.Sin(0.30f);
                float tiltedZ = y * Mathf.Sin(0.30f) + rotatedZ * Mathf.Cos(0.30f);
                output[uIndex, vIndex] = new Vector3(rotatedX, tiltedY, tiltedZ) * 0.48f;
            }
        }

        return output;
    }

    private static ArrayMesh CreateMobiusMesh()
    {
        const int segments = 80;
        const int bands = 10;
        const float width = 0.22f;
        List<Vector3> triangles = new();

        for (int i = 0; i < segments; i++)
        {
            float u0 = Mathf.Tau * i / segments;
            float u1 = Mathf.Tau * (i + 1) / segments;

            for (int j = 0; j < bands; j++)
            {
                float v0 = Mathf.Lerp(-width, width, (float)j / bands);
                float v1 = Mathf.Lerp(-width, width, (float)(j + 1) / bands);

                Vector3 a = MobiusPoint(u0, v0);
                Vector3 b = MobiusPoint(u1, v0);
                Vector3 c = MobiusPoint(u1, v1);
                Vector3 d = MobiusPoint(u0, v1);

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        return CreateTriangleMesh(triangles.ToArray());
    }

    private static Vector3 MobiusPoint(float u, float v)
    {
        float radius = 0.46f + v * Mathf.Cos(u * 0.5f);
        return new Vector3(
            radius * Mathf.Cos(u),
            v * Mathf.Sin(u * 0.5f),
            radius * Mathf.Sin(u));
    }

    private static ArrayMesh CreateTwistedTubeMesh(int segments, int sides, float majorRadius, float minorRadius, float twist)
    {
        List<Vector3> triangles = new();
        for (int i = 0; i < segments; i++)
        {
            float u0 = Mathf.Tau * i / segments;
            float u1 = Mathf.Tau * (i + 1) / segments;

            for (int j = 0; j < sides; j++)
            {
                float v0 = Mathf.Tau * j / sides;
                float v1 = Mathf.Tau * (j + 1) / sides;

                Vector3 a = TwistedTubePoint(u0, v0, majorRadius, minorRadius, twist);
                Vector3 b = TwistedTubePoint(u1, v0, majorRadius, minorRadius, twist);
                Vector3 c = TwistedTubePoint(u1, v1, majorRadius, minorRadius, twist);
                Vector3 d = TwistedTubePoint(u0, v1, majorRadius, minorRadius, twist);

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        return CreateTriangleMesh(triangles.ToArray());
    }

    private static Vector3 TwistedTubePoint(float u, float v, float majorRadius, float minorRadius, float twist)
    {
        Vector3 center = new(
            majorRadius * Mathf.Cos(u),
            0.18f * Mathf.Sin(u * 2f),
            majorRadius * Mathf.Sin(u));
        Vector3 radial = new(Mathf.Cos(u), 0f, Mathf.Sin(u));
        float tubeAngle = v + twist * u;
        float localRadius = minorRadius * (1f + 0.18f * Mathf.Sin(u * 3f));
        return center + radial * Mathf.Cos(tubeAngle) * localRadius + Vector3.Up * Mathf.Sin(tubeAngle) * localRadius;
    }

    private static ArrayMesh CreateLumpySphereMesh()
    {
        const int rings = 18;
        const int segments = 36;
        List<Vector3> triangles = new();

        for (int ring = 0; ring < rings; ring++)
        {
            float theta0 = Mathf.Pi * ring / rings;
            float theta1 = Mathf.Pi * (ring + 1) / rings;

            for (int segment = 0; segment < segments; segment++)
            {
                float phi0 = Mathf.Tau * segment / segments;
                float phi1 = Mathf.Tau * (segment + 1) / segments;

                Vector3 a = LumpySpherePoint(theta0, phi0);
                Vector3 b = LumpySpherePoint(theta1, phi0);
                Vector3 c = LumpySpherePoint(theta1, phi1);
                Vector3 d = LumpySpherePoint(theta0, phi1);

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        return CreateTriangleMesh(triangles.ToArray());
    }

    private static Vector3 LumpySpherePoint(float theta, float phi)
    {
        float radius = 0.43f
            + 0.07f * Mathf.Sin(theta * 2f) * Mathf.Cos(phi * 3f)
            + 0.05f * Mathf.Sin(theta * 4f + phi * 2f);
        float sinTheta = Mathf.Sin(theta);
        return new Vector3(
            radius * sinTheta * Mathf.Cos(phi),
            radius * Mathf.Cos(theta),
            radius * sinTheta * Mathf.Sin(phi));
    }

    private static ArrayMesh CreateTriangleMesh(Vector3[] vertices)
    {
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static ArrayMesh CreateLineMesh(Vector3[] points)
    {
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }

    private static ArrayMesh CreatePointMesh(Vector3[] points)
    {
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);
        return mesh;
    }

    private static Point4 Rotate4D(Point4 point, float xwAngle, float yzAngle, float xyAngle, float zwAngle)
    {
        float x = point.X;
        float y = point.Y;
        float z = point.Z;
        float w = point.W;

        float cos = Mathf.Cos(xwAngle);
        float sin = Mathf.Sin(xwAngle);
        float nextX = x * cos - w * sin;
        float nextW = x * sin + w * cos;
        x = nextX;
        w = nextW;

        cos = Mathf.Cos(yzAngle);
        sin = Mathf.Sin(yzAngle);
        float nextY = y * cos - z * sin;
        float nextZ = y * sin + z * cos;
        y = nextY;
        z = nextZ;

        cos = Mathf.Cos(xyAngle);
        sin = Mathf.Sin(xyAngle);
        nextX = x * cos - y * sin;
        nextY = x * sin + y * cos;
        x = nextX;
        y = nextY;

        if (!Mathf.IsZeroApprox(zwAngle))
        {
            cos = Mathf.Cos(zwAngle);
            sin = Mathf.Sin(zwAngle);
            nextZ = z * cos - w * sin;
            nextW = z * sin + w * cos;
            z = nextZ;
            w = nextW;
        }

        return new Point4(x, y, z, w);
    }

    private static Vector3 Project4DTo3D(Point4 point, float wCamera, float scale)
    {
        float wFactor = wCamera / (wCamera - point.W);
        return new Vector3(point.X * wFactor * scale, point.Y * wFactor * scale, point.Z * wFactor * scale);
    }

    private readonly struct Point4
    {
        public Point4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }
    }
}