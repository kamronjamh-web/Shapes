using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Visuals;

public readonly struct SocketVisualOptions
{
    public bool InspectionMode { get; init; }
    public bool EnableOrbitStyle { get; init; }
}

public static partial class WeaponModulatorVisualFactory
{
    private const float DefaultSocketZ = 0.56f;
    private const float CubeBaseTilt = -0.20f;

    private readonly struct SphereOrbitDefinition
    {
        public SphereOrbitDefinition(float radius, float verticalOffset, float phaseDegrees, float tiltXDegrees, float tiltZDegrees, float angularSpeed)
        {
            Radius = radius;
            VerticalOffset = verticalOffset;
            PhaseDegrees = phaseDegrees;
            TiltXDegrees = tiltXDegrees;
            TiltZDegrees = tiltZDegrees;
            AngularSpeed = angularSpeed;
        }

        public float Radius { get; }
        public float VerticalOffset { get; }
        public float PhaseDegrees { get; }
        public float TiltXDegrees { get; }
        public float TiltZDegrees { get; }
        public float AngularSpeed { get; }
    }

    private static readonly Vector3[] CubeSocketPositions =
    [
        new(-0.406f, -0.406f, -0.406f),
        new(0.406f, -0.406f, -0.406f),
        new(0.406f, 0.406f, -0.406f),
        new(-0.406f, 0.406f, -0.406f),
        new(-0.406f, -0.406f, 0.406f),
        new(0.406f, -0.406f, 0.406f),
        new(0.406f, 0.406f, 0.406f),
        new(-0.406f, 0.406f, 0.406f),
        new(-0.135f, 0.135f, 0.135f),
        new(0.135f, -0.135f, -0.135f),
    ];

    private static readonly Vector3[] HypersphereSocketPositions =
    [
        new(0.00f, -0.74f, 0.20f),
        new(0.42f, -0.50f, 0.42f),
        new(0.74f, -0.12f, 0.24f),
        new(0.48f, 0.32f, 0.48f),
        new(0.16f, 0.72f, 0.22f),
        new(-0.16f, 0.72f, 0.22f),
        new(-0.48f, 0.32f, 0.48f),
        new(-0.74f, -0.12f, 0.24f),
        new(-0.42f, -0.50f, 0.42f),
        new(0.00f, 0.00f, 0.62f),
    ];

    private static readonly SphereOrbitDefinition[] SphereOrbitDefinitions =
    [
        new(1.00f, 0.28f, 0f, 34f, 18f, 0.92f),
        new(1.04f, 0.06f, 72f, 16f, 54f, 0.86f),
        new(1.08f, 0.18f, 144f, 48f, -28f, 0.82f),
        new(1.02f, -0.06f, 216f, 10f, -56f, 0.88f),
        new(1.06f, 0.12f, 288f, 60f, 10f, 0.84f),
        new(1.06f, -0.28f, 36f, -36f, -20f, -0.78f),
        new(1.02f, -0.12f, 108f, -18f, -50f, -0.84f),
        new(1.10f, -0.18f, 180f, -50f, 28f, -0.76f),
        new(1.00f, 0.10f, 252f, -12f, 58f, -0.88f),
        new(1.08f, -0.04f, 324f, -62f, -8f, -0.80f),
    ];

    private static readonly int[][] SphereOrbitLayoutsByVisibleSlots =
    [
        [],
        [0],
        [0, 5],
        [0, 5, 1],
        [0, 5, 1, 7],
        [0, 5, 1, 7, 4],
        [0, 5, 1, 7, 4, 3],
        [0, 5, 1, 7, 4, 3, 2],
        [0, 5, 1, 7, 4, 3, 2, 6],
        [0, 5, 1, 7, 4, 3, 2, 6, 8],
        [0, 5, 1, 7, 4, 3, 2, 6, 8, 9],
    ];

    private sealed partial class OrbitingSphereSocketVisual : Node3D
    {
        private readonly SphereOrbitDefinition _definition;
        private readonly Color _accentColor;
        private readonly float _layoutScale;
        private readonly bool _ghost;
        private readonly MeshInstance3D[] _trailSegments;
        private readonly Vector3[] _trailPositions;

        private Node3D? _orbitCarrier;
        private Node3D? _orbitPivot;
        private Node3D? _payloadRoot;
        private Vector3 _orbitOffset;
        private float _orbitAngle;
        private bool _trailInitialized;

        public OrbitingSphereSocketVisual(string name, SphereOrbitDefinition definition, Color accentColor, float layoutScale, bool ghost)
        {
            Name = name;
            _definition = definition;
            _accentColor = accentColor;
            _layoutScale = layoutScale;
            _ghost = ghost;
            _trailSegments = new MeshInstance3D[ghost ? 5 : 8];
            _trailPositions = new Vector3[_trailSegments.Length];
            _orbitAngle = Mathf.DegToRad(definition.PhaseDegrees);
        }

        public override void _Ready()
        {
            BuildVisuals();
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            if (_orbitPivot == null || _payloadRoot == null)
            {
                return;
            }

            _orbitAngle = Mathf.Wrap(_orbitAngle + (float)delta * _definition.AngularSpeed, -Mathf.Pi, Mathf.Pi);
            _orbitPivot.Rotation = new Vector3(0f, _orbitAngle, 0f);
            _payloadRoot.RotationDegrees = new Vector3(
                12f * Mathf.Sin(_orbitAngle * 1.8f),
                Mathf.RadToDeg(-_orbitAngle) * 1.5f,
                _ghost ? 0f : 16f * Mathf.Sin(_orbitAngle * 2.3f));

            Vector3 currentLocalPoint = ComputeCurrentLocalPoint();
            UpdateTrail(currentLocalPoint, (float)delta);
        }

        private void BuildVisuals()
        {
            _orbitCarrier = new Node3D
            {
                Name = "OrbitCarrier",
                Position = new Vector3(0f, _definition.VerticalOffset * _layoutScale, 0f),
                RotationDegrees = new Vector3(_definition.TiltXDegrees, 0f, _definition.TiltZDegrees),
            };
            AddChild(_orbitCarrier);

            _orbitPivot = new Node3D
            {
                Name = "OrbitPivot",
                Rotation = new Vector3(0f, _orbitAngle, 0f),
            };
            _orbitCarrier.AddChild(_orbitPivot);

            _orbitOffset = new Vector3(_definition.Radius * _layoutScale, 0f, 0f);
            _payloadRoot = new Node3D
            {
                Name = "PayloadRoot",
                Position = _orbitOffset,
            };
            _orbitPivot.AddChild(_payloadRoot);

            BuildPayloadVisuals();
            BuildTrailVisuals();

            Vector3 initialLocalPoint = ComputeCurrentLocalPoint();
            for (int segmentIndex = 0; segmentIndex < _trailPositions.Length; segmentIndex++)
            {
                _trailPositions[segmentIndex] = initialLocalPoint;
                _trailSegments[segmentIndex].Position = initialLocalPoint;
            }

            _trailInitialized = true;
        }

        private void BuildPayloadVisuals()
        {
            if (_payloadRoot == null)
            {
                return;
            }

            Color haloColor = _ghost ? _accentColor.Lightened(0.20f) : _accentColor.Lightened(0.14f);
            float haloAlpha = _ghost ? 0.20f : 0.76f;
            float coreAlpha = _ghost ? 0.18f : 1f;

            MeshInstance3D halo = new()
            {
                Name = "OrbitHalo",
                Mesh = new TorusMesh
                {
                    InnerRadius = (_ghost ? 0.022f : 0.018f) * _layoutScale,
                    OuterRadius = (_ghost ? 0.042f : 0.035f) * _layoutScale,
                    Rings = 24,
                    RingSegments = 8,
                },
                RotationDegrees = new Vector3(78f, 0f, 18f),
                MaterialOverride = CreateAttachmentMaterial(haloColor, _ghost ? 0.58f : 1.9f, haloAlpha),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _payloadRoot.AddChild(halo);

            MeshInstance3D core = new()
            {
                Name = "OrbitCore",
                Mesh = new SphereMesh
                {
                    Radius = (_ghost ? 0.028f : 0.044f) * _layoutScale,
                    Height = (_ghost ? 0.056f : 0.088f) * _layoutScale,
                    RadialSegments = 18,
                    Rings = 9,
                },
                MaterialOverride = CreateAttachmentMaterial(_accentColor, _ghost ? 0.78f : 2.35f, coreAlpha),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _payloadRoot.AddChild(core);

            if (_ghost)
            {
                return;
            }

            MeshInstance3D shard = new()
            {
                Name = "OrbitShard",
                Mesh = new BoxMesh
                {
                    Size = new Vector3(0.018f, 0.018f, 0.095f) * _layoutScale,
                },
                Position = new Vector3(0.052f, 0f, 0f) * _layoutScale,
                RotationDegrees = new Vector3(28f, 34f, 36f),
                MaterialOverride = CreateAttachmentMaterial(haloColor, 2.8f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _payloadRoot.AddChild(shard);
        }

        private void BuildTrailVisuals()
        {
            for (int segmentIndex = 0; segmentIndex < _trailSegments.Length; segmentIndex++)
            {
                float t = segmentIndex / (float)Mathf.Max(1, _trailSegments.Length - 1);
                float radius = Mathf.Lerp(_ghost ? 0.020f : 0.030f, _ghost ? 0.006f : 0.010f, t) * _layoutScale;
                float alpha = Mathf.Lerp(_ghost ? 0.16f : 0.44f, 0.02f, t);
                float emission = Mathf.Lerp(_ghost ? 0.26f : 1.25f, _ghost ? 0.08f : 0.28f, t);
                MeshInstance3D segment = new()
                {
                    Name = $"Trace{segmentIndex + 1:00}",
                    Mesh = new SphereMesh
                    {
                        Radius = radius,
                        Height = radius * 2f,
                        RadialSegments = 12,
                        Rings = 6,
                    },
                    MaterialOverride = CreateAttachmentMaterial(_accentColor, emission, alpha),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(segment);
                _trailSegments[segmentIndex] = segment;
            }
        }

        private Vector3 ComputeCurrentLocalPoint()
        {
            if (_orbitCarrier == null || _orbitPivot == null)
            {
                return Vector3.Zero;
            }

            return _orbitCarrier.Transform * (_orbitPivot.Transform * _orbitOffset);
        }

        private void UpdateTrail(Vector3 currentLocalPoint, float deltaSeconds)
        {
            if (!_trailInitialized)
            {
                for (int segmentIndex = 0; segmentIndex < _trailPositions.Length; segmentIndex++)
                {
                    _trailPositions[segmentIndex] = currentLocalPoint;
                    _trailSegments[segmentIndex].Position = currentLocalPoint;
                }

                _trailInitialized = true;
                return;
            }

            float headLerp = 1f - Mathf.Exp(-deltaSeconds * (_ghost ? 8f : 15f));
            _trailPositions[0] = _trailPositions[0].Lerp(currentLocalPoint, headLerp);
            _trailSegments[0].Position = _trailPositions[0];

            for (int segmentIndex = 1; segmentIndex < _trailPositions.Length; segmentIndex++)
            {
                float followRate = Mathf.Lerp(_ghost ? 5f : 9f, _ghost ? 2.6f : 4.2f, segmentIndex / (float)Mathf.Max(1, _trailPositions.Length - 1));
                float followLerp = 1f - Mathf.Exp(-deltaSeconds * followRate);
                _trailPositions[segmentIndex] = _trailPositions[segmentIndex].Lerp(_trailPositions[segmentIndex - 1], followLerp);
                _trailSegments[segmentIndex].Position = _trailPositions[segmentIndex];
            }
        }
    }

    public static int GetVisibleSocketCount(EquipmentItem item)
    {
        int maxCapableSlots = EquipmentCatalogDefaults.GetUnlockedModulatorSlots(item.Rarity?.Key, EquipmentCatalogDefaults.MaxWeaponLevel);
        return Mathf.Clamp(Mathf.Max(5, maxCapableSlots), 5, 10);
    }

    public static bool UsesOrbitSocketLayout(EquipmentItem item)
    {
        return !string.IsNullOrEmpty(NormalizeShapeKey(item.Shape?.Key));
    }

    public static bool HasPersistentHubSocketVisuals(EquipmentItem item)
    {
        return UsesOrbitSocketLayout(item) && (item.Modulators?.Count ?? 0) > 0;
    }

    public static Vector3 GetSocketLocalPosition(EquipmentItem item, int socketIndex, int visibleSlots)
    {
        string shapeKey = NormalizeShapeKey(item.Shape?.Key);
        return shapeKey switch
        {
            "sphere" => GetSphereSocketLocalPosition(socketIndex, visibleSlots),
            "cube" => GetCubeSocketLocalPosition(socketIndex),
            "hyperphere" => GetHypersphereSocketLocalPosition(socketIndex, visibleSlots),
            _ => GetDefaultSocketLocalPosition(socketIndex, visibleSlots),
        };
    }

    public static void PopulateSocketVisuals(Node3D socketRoot, EquipmentItem item, float layoutScale = 1f, SocketVisualOptions options = default)
    {
        float safeScale = Mathf.Max(0.1f, layoutScale);
        string shapeKey = NormalizeShapeKey(item.Shape?.Key);
        int unlockedSlots = item.UnlockedModulatorSlots;
        int visibleSlots = GetVisibleSocketCount(item);
        int installedCount = item.Modulators?.Count ?? 0;

        if (options.EnableOrbitStyle && UsesOrbitSocketLayout(item))
        {
            PopulateSphereOrbitSockets(socketRoot, item, safeScale, unlockedSlots, visibleSlots, installedCount, options);
            return;
        }

        for (int socketIndex = 0; socketIndex < visibleSlots; socketIndex++)
        {
            bool unlocked = socketIndex < unlockedSlots;
            bool hasSocketEntry = socketIndex < installedCount && item.Modulators != null;
            WeaponModulatorData? modulator = hasSocketEntry ? item.Modulators![socketIndex] : null;
            bool installed = modulator != null;
            Color color = modulator?.UiColor
                ?? (unlocked ? new Color(0.26f, 0.86f, 1f, 1f) : new Color(0.26f, 0.31f, 0.40f, 1f));

            Vector3 anchorPosition = GetSocketLocalPosition(item, socketIndex, visibleSlots) * safeScale;
            Node3D anchorRoot = new()
            {
                Name = $"ModulatorSocket{socketIndex + 1:00}",
                Position = anchorPosition,
            };
            socketRoot.AddChild(anchorRoot);

            anchorRoot.AddChild(CreateSocketRing(color, unlocked, installed, safeScale));
            if (installed && modulator != null)
            {
                AddInstalledAttachment(anchorRoot, item, modulator, socketIndex, safeScale);
            }
        }
    }

    private static void PopulateSphereOrbitSockets(
        Node3D socketRoot,
        EquipmentItem item,
        float safeScale,
        int unlockedSlots,
        int visibleSlots,
        int installedCount,
        SocketVisualOptions options)
    {
        for (int socketIndex = 0; socketIndex < visibleSlots; socketIndex++)
        {
            bool unlocked = socketIndex < unlockedSlots;
            bool hasSocketEntry = socketIndex < installedCount && item.Modulators != null;
            WeaponModulatorData? modulator = hasSocketEntry ? item.Modulators![socketIndex] : null;
            bool installed = modulator != null;
            if (!installed && (!options.InspectionMode || !unlocked))
            {
                continue;
            }

            Color color = installed
                ? modulator!.UiColor
                : new Color(0.40f, 0.78f, 1f, 1f);
            OrbitingSphereSocketVisual orbitVisual = new(
                $"ModulatorOrbit{socketIndex + 1:00}",
                ResolveSphereOrbitDefinition(socketIndex, visibleSlots),
                color,
                safeScale,
                ghost: !installed);
            socketRoot.AddChild(orbitVisual);
        }
    }

    private static Vector3 GetDefaultSocketLocalPosition(int socketIndex, int visibleSlots)
    {
        float angle = -Mathf.Pi / 2f + socketIndex / (float)visibleSlots * Mathf.Tau;
        float radiusX = 0.92f + visibleSlots * 0.012f;
        float radiusY = 0.58f + visibleSlots * 0.008f;
        return new Vector3(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY, DefaultSocketZ);
    }

    private static Vector3 GetSphereSocketLocalPosition(int socketIndex, int visibleSlots)
    {
        SphereOrbitDefinition definition = ResolveSphereOrbitDefinition(socketIndex, visibleSlots);
        float phaseRadians = Mathf.DegToRad(definition.PhaseDegrees);
        Vector3 orbitVector = new(Mathf.Cos(phaseRadians) * definition.Radius, 0f, Mathf.Sin(phaseRadians) * definition.Radius);
        Basis orbitBasis = Basis.FromEuler(new Vector3(
            Mathf.DegToRad(definition.TiltXDegrees),
            0f,
            Mathf.DegToRad(definition.TiltZDegrees)));
        return new Vector3(0f, definition.VerticalOffset, 0f) + orbitBasis * orbitVector;
    }

    private static SphereOrbitDefinition ResolveSphereOrbitDefinition(int socketIndex, int visibleSlots)
    {
        if (SphereOrbitDefinitions.Length == 0)
        {
            return default;
        }

        int safeVisibleSlots = Mathf.Max(1, visibleSlots);
        if (safeVisibleSlots < SphereOrbitLayoutsByVisibleSlots.Length)
        {
            int[] layout = SphereOrbitLayoutsByVisibleSlots[safeVisibleSlots];
            if (layout.Length > 0)
            {
                int layoutIndex = Mathf.Clamp(socketIndex, 0, layout.Length - 1);
                int layoutTemplateIndex = Mathf.Clamp(layout[layoutIndex], 0, SphereOrbitDefinitions.Length - 1);
                return SphereOrbitDefinitions[layoutTemplateIndex];
            }
        }

        int templateIndex = Mathf.Clamp(
            Mathf.FloorToInt(socketIndex * SphereOrbitDefinitions.Length / (float)safeVisibleSlots),
            0,
            SphereOrbitDefinitions.Length - 1);
        return SphereOrbitDefinitions[templateIndex];
    }

    private static Vector3 GetCubeSocketLocalPosition(int socketIndex)
    {
        int clampedIndex = Mathf.Clamp(socketIndex, 0, CubeSocketPositions.Length - 1);
        return RotateSocketAroundX(CubeSocketPositions[clampedIndex], CubeBaseTilt);
    }

    private static Vector3 GetHypersphereSocketLocalPosition(int socketIndex, int visibleSlots)
    {
        if (HypersphereSocketPositions.Length == 0)
        {
            return GetDefaultSocketLocalPosition(socketIndex, visibleSlots);
        }

        int templateIndex = Mathf.Clamp(
            Mathf.FloorToInt(socketIndex * HypersphereSocketPositions.Length / (float)Mathf.Max(1, visibleSlots)),
            0,
            HypersphereSocketPositions.Length - 1);
        return HypersphereSocketPositions[templateIndex];
    }

    private static MeshInstance3D CreateSocketRing(Color color, bool unlocked, bool installed, float layoutScale)
    {
        return new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = 0.045f * layoutScale,
                OuterRadius = 0.086f * layoutScale,
                Rings = 28,
                RingSegments = 8,
            },
            MaterialOverride = CreateSocketMaterial(color, unlocked ? 0.86f : 0.30f, installed ? 1.45f : 0.65f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static void AddInstalledAttachment(Node3D anchorRoot, EquipmentItem item, WeaponModulatorData modulator, int socketIndex, float layoutScale)
    {
        string shapeKey = NormalizeShapeKey(item.Shape?.Key);
        Vector3 anchorPosition = anchorRoot.Position;
        Vector3 outward = ResolveAttachmentDirection(shapeKey, anchorPosition);
        Vector3 tangent = new(-outward.Y, outward.X, 0.22f);
        if (tangent.LengthSquared() <= 0.0001f)
        {
            tangent = Vector3.Up;
        }

        tangent = tangent.Normalized();

        float satelliteDistance = shapeKey == "hyperphere" ? 0.135f : 0.105f;
        Vector3 satelliteOffset = outward * (satelliteDistance * layoutScale);
        Color accent = modulator.UiColor.Lightened(0.18f);

        MeshInstance3D halo = new()
        {
            Name = "InstalledHalo",
            Mesh = new TorusMesh
            {
                InnerRadius = 0.018f * layoutScale,
                OuterRadius = 0.034f * layoutScale,
                Rings = 24,
                RingSegments = 8,
            },
            Position = satelliteOffset,
            RotationDegrees = new Vector3(78f, socketIndex * 37f, 12f),
            MaterialOverride = CreateAttachmentMaterial(accent, 1.9f, 0.74f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        anchorRoot.AddChild(halo);

        MeshInstance3D core = new()
        {
            Name = "InstalledCore",
            Mesh = new SphereMesh
            {
                Radius = 0.044f * layoutScale,
                Height = 0.088f * layoutScale,
                RadialSegments = 18,
                Rings = 9,
            },
            Position = satelliteOffset,
            MaterialOverride = CreateAttachmentMaterial(modulator.UiColor, 2.35f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        anchorRoot.AddChild(core);

        MeshInstance3D shard = new()
        {
            Name = "InstalledShard",
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.018f, 0.018f, shapeKey == "hyperphere" ? 0.125f : 0.095f) * layoutScale,
            },
            Position = satelliteOffset + outward * (0.052f * layoutScale),
            RotationDegrees = new Vector3(24f + socketIndex * 9f, 30f + socketIndex * 27f, 38f),
            MaterialOverride = CreateAttachmentMaterial(accent, 2.8f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        anchorRoot.AddChild(shard);

        if (shapeKey == "hyperphere")
        {
            MeshInstance3D companion = new()
            {
                Name = "InstalledCompanion",
                Mesh = new SphereMesh
                {
                    Radius = 0.022f * layoutScale,
                    Height = 0.044f * layoutScale,
                    RadialSegments = 12,
                    Rings = 6,
                },
                Position = satelliteOffset + tangent * (0.060f * layoutScale),
                MaterialOverride = CreateAttachmentMaterial(accent, 2.0f, 0.92f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            anchorRoot.AddChild(companion);
        }
    }

    private static Vector3 ResolveAttachmentDirection(string shapeKey, Vector3 anchorPosition)
    {
        Vector3 outward = anchorPosition.LengthSquared() > 0.0001f ? anchorPosition.Normalized() : Vector3.Forward;
        if (shapeKey == "hyperphere")
        {
            outward = (outward + new Vector3(0f, 0f, 0.48f)).Normalized();
        }

        return outward.LengthSquared() > 0.0001f ? outward : Vector3.Forward;
    }

    private static Vector3 RotateSocketAroundX(Vector3 position, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector3(
            position.X,
            position.Y * cos - position.Z * sin,
            position.Y * sin + position.Z * cos);
    }

    private static StandardMaterial3D CreateSocketMaterial(Color color, float alpha, float emissionEnergy)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, alpha),
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = emissionEnergy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.18f,
            Metallic = 0.05f,
        };
    }

    private static StandardMaterial3D CreateAttachmentMaterial(Color color, float emissionEnergy, float alpha = 1f)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, alpha),
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = emissionEnergy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = alpha < 0.999f
                ? BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass
                : BaseMaterial3D.TransparencyEnum.Disabled,
            Roughness = 0.12f,
            Metallic = 0.08f,
            RimEnabled = true,
            Rim = 0.65f,
            RimTint = 0.45f,
        };
    }

    private static string NormalizeShapeKey(string? key)
    {
        string normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "hypersphere" => "hyperphere",
            _ => normalized,
        };
    }
}