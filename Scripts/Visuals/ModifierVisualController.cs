using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Visuals;

public partial class ModifierVisualController : Node3D
{
    private const int MaxElectricEffectNodes = 220;
    private const int MaxElectricAnchorCandidates = 120;
    private const float ElectricFlickerScale = 0.50f;
    private const float ElectricFloorArcRefreshInterval = 0.20f;
    private const float ElectricFloorArcEvaluationDepth = 8.0f;
    private const float ElectricFloorArcNearClearance = 0.18f;
    private const float ElectricFloorArcFarClearance = 2.90f;
    private const float ElectricFloorArcMinChance = 0.08f;
    private const float ElectricFloorArcMaxChance = 1.0f;
    private const float ElectricSparkGravity = 11.5f;
    private const float ElectricLongLivedSparkChance = 0.14f;
    private const float ElectricLongLivedSparkDurationMultiplierMin = 2.4f;
    private const float ElectricLongLivedSparkDurationMultiplierMax = 4.1f;
    private const float ElectricEnvironmentDischargeChance = 0.74f;
    private const float ElectricCharacterDischargeChance = 0.20f;
    private const float ElectricPooledExternalDischargeChance = 0.74f;
    private const float ElectricPooledCharacterSelectionChance = 0.18f;

    private static readonly string[] NonCoreMeshNameTokens =
    [
        "outline",
        "shell",
        "edge",
        "edges",
        "wire",
        "line",
        "lines",
        "point",
        "points",
        "ring",
        "spine",
        "vertices",
    ];

    private static readonly Color ElectricSparkColor = new(1.0f, 0.86f, 0.20f, 1f);

    [Export] public float Intensity = 1f;

    private sealed class DisplacementMaterialTarget
    {
        public MeshInstance3D Mesh { get; init; } = null!;
        public Material? OriginalMaterialOverride { get; init; }
        public ShaderMaterial Material { get; init; } = null!;
    }

    private sealed class PolishedMaterialTarget
    {
        public MeshInstance3D Mesh { get; init; } = null!;
        public Material? OriginalMaterialOverride { get; init; }
        public ShaderMaterial Material { get; init; } = null!;
    }

    private sealed class ElectricEffectNode
    {
        public MeshInstance3D Mesh { get; init; } = null!;
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Acceleration { get; init; }
        public Vector3 StartScale { get; init; } = Vector3.One;
        public Vector3 EndScale { get; init; } = Vector3.One;
        public Color Color { get; init; } = Colors.White;
        public float Duration { get; init; } = 0.3f;
        public float Delay { get; init; }
        public float Seed { get; init; }
        public float AlphaMultiplier { get; init; } = 1f;
        public float FadePower { get; init; } = 0.64f;
        public float EmissionMultiplier { get; init; } = 1f;
        public bool DrawsIn { get; init; }
        public bool Flickers { get; init; }
        public bool UseWorldSpace { get; init; }
        public bool CollidesWithEnvironment { get; init; }
        public float BounceDamping { get; init; } = 0.28f;
        public float SurfaceDrag { get; init; } = 0.58f;
        public float Age { get; set; }
        public float LastActiveAge { get; set; }
    }

    private sealed class ElectricFloorArcSample
    {
        public Vector3 LocalAnchor { get; init; }
        public float Chance { get; init; }
    }

    private enum ElectricExternalAnchorKind
    {
        Environment,
        Character,
    }

    private sealed class ElectricExternalAnchorSlot
    {
        public ElectricExternalAnchorKind Kind { get; init; }
        public Vector3 Position { get; set; }
    }

    public readonly struct ElectricEnvironmentHit(Vector3 position, Vector3 normal)
    {
        public Vector3 Position { get; } = position;
        public Vector3 Normal { get; } = normal;
    }

    private enum ElectricAnchorSource
    {
        None,
        Provider,
        MeshBounds,
    }

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<Node3D> _generatedNodes = new();
    private readonly List<DisplacementMaterialTarget> _displacementMaterials = new();
    private readonly List<PolishedMaterialTarget> _polishedMaterials = new();
    private readonly List<MeshInstance3D> _shinySparkles = new();
    private readonly List<float> _shinySparkleSeeds = new();
    private readonly List<MeshInstance3D> _shinyRays = new();
    private readonly List<ElectricEffectNode> _electricEffects = new();
    private readonly List<Vector3> _electricAnchorBuffer = new();
    private readonly List<Vector3> _electricExternalAnchorBuffer = new();
    private readonly List<Vector3> _electricProviderLocalAnchors = new();
    private readonly List<ElectricExternalAnchorSlot> _electricExternalAnchorPool = new();
    private readonly List<ElectricFloorArcSample> _electricFloorArcSamples = new();

    private static Shader? _displacementShader;
    private static Shader? _polishedSparkleShader;

    private ModifierVisualStyle _style;
    private ModifierVisualKind _visualKind;
    private Node3D? _target;
    private OmniLight3D? _glowLight;
    private Node3D? _cyanGhost;
    private Node3D? _magentaGhost;
    private MeshInstance3D? _glitchShardA;
    private MeshInstance3D? _glitchShardB;
    private MeshInstance3D? _glitchShardC;
    private MeshInstance3D? _shinyHalo;
    private MeshInstance3D? _shinyAuraRing;
    private MeshInstance3D? _shinySweep;

    private bool _active;
    private Vector3 _targetBasePosition;
    private Vector3 _targetBaseScale = Vector3.One;
    private Color _baseLightColor = Colors.White;
    private float _baseLightEnergy = 1f;
    private float _time;
    private float _burstTimer;
    private float _burstDuration = 0.16f;
    private float _nextGlitchDelay = 0.8f;
    private float _stepTimer;
    private float _burstEnvelopeSkew = 1f;
    private float _shapeDisplacementAmount = 0.055f;
    private float _shaderSeed;
    private float _bandScale = 18f;
    private float _tearChance = 0.55f;
    private int _glitchesRemaining;
    private Vector3 _currentTargetOffset;
    private Vector3 _cyanOffset;
    private Vector3 _magentaOffset;
    private Vector3 _cyanRotation;
    private Vector3 _magentaRotation;
    private Vector3 _targetScaleJitter;
    private Vector3 _glitchAxis = Vector3.Right;
    private float _radiantFlashTimer;
    private float _radiantFlashDuration = 0.32f;
    private float _nextRadiantFlashDelay = 1.8f;
    private float _radiantSweepAngle;
    private float _radiantVisualScale = 1f;
    private float _electricBurstTimer;
    private float _electricBurstDuration = 0.18f;
    private float _nextElectricBurstDelay = 0.42f;
    private float _electricVisualScale = 1f;
    private float _electricEffectStepTimer;
    private int _electricEffectsRemaining;
    private float _electricLightSpikeTimer;
    private float _electricLightSpikeDuration = 0.09f;
    private float _electricFloorArcRefreshTimer;
    private int _electricEnvironmentDischargesSinceCharacterStrike;

    public Action<ICollection<Vector3>>? CollectEnvironmentElectricGlobalAnchors { get; set; }
    public Action<ICollection<Vector3>>? CollectCharacterElectricGlobalAnchors { get; set; }
    public Func<Vector3, Vector3, ElectricEnvironmentHit?>? ResolveElectricEnvironmentCollision { get; set; }
    public int ElectricExternalAnchorPoolSize { get; set; }
    public int MinimumCharacterElectricAnchorPoolSlots { get; set; }
    public int ForceCharacterElectricAnchorAfterEnvironmentHits { get; set; } = 3;

    public override void _Ready()
    {
        _rng.Randomize();
    }

    public override void _ExitTree()
    {
        RestoreTargetState();
        RestoreLightState();
        RestorePolishedMaterials();
        RestoreDisplacementMaterials();
    }

    public override void _Process(double delta)
    {
        if (!_active || _target == null || !IsInstanceValid(_target))
        {
            return;
        }

        float deltaSeconds = (float)delta;
        _time += deltaSeconds;

        if (_style.UsesGlitchVisuals)
        {
            float burstAmount = TickGlitchBurst(deltaSeconds);
            ApplyTargetOffset(burstAmount);
            ApplyDisplacementMaterials(burstAmount);
            ApplyGhostOffsets(burstAmount);
            ApplyLightPulse(burstAmount);
            return;
        }

        if (_style.UsesRadiantVisuals)
        {
            float flashAmount = TickRadiantFlash(deltaSeconds);
            ApplyRadiantTargetPulse(flashAmount);
            ApplyRadiantNodes(flashAmount, deltaSeconds);
            ApplyRadiantLightPulse(flashAmount);
            return;
        }

        if (_style.UsesElectricVisuals)
        {
            UpdateElectricFloorArcSamples(deltaSeconds);
            float burstAmount = TickElectricBurst(deltaSeconds);
            ApplyElectricTargetPulse(burstAmount);
            ApplyElectricNodes(burstAmount, deltaSeconds);
            ApplyElectricLightPulse(burstAmount);
            return;
        }

        if (_style.UsesPolishedVisuals)
        {
            ApplyPolishedMaterialAnimation();
            return;
        }
    }

    public void Configure(EquipmentItem? item, Node3D? target, ShapeData? shape, float visualScale, OmniLight3D? glowLight = null, float intensity = 1f)
    {
        ClearGeneratedVisuals();

        _target = target;
        _glowLight = glowLight;
        Intensity = Mathf.Max(0.1f, intensity);

        if (!ModifierVisualTheme.TryGet(item, out _style) || !_style.HasSpecialVisuals || _target == null || shape == null)
        {
            _active = false;
            Visible = false;
            _target = null;
            _glowLight = null;
            return;
        }

        Visible = true;
        _active = true;
        _visualKind = _style.VisualKind;
        _targetBasePosition = _target.Position;
        _targetBaseScale = _target.Scale;
        RefreshLightBaseline();
        _shaderSeed = _rng.RandfRange(0f, 8192f);

        if (_style.UsesGlitchVisuals)
        {
            _cyanGhost = CreateGhost(shape, _style.AccentColor, "UnstableCyanGhost", visualScale);
            _magentaGhost = CreateGhost(shape, _style.SecondaryAccentColor, "UnstableMagentaGhost", visualScale);
            _glitchShardA = CreateGlitchShard("UnstableShardA", _style.AccentColor, new Vector3(0.95f, 0.020f, 0.020f) * visualScale);
            _glitchShardB = CreateGlitchShard("UnstableShardB", _style.SecondaryAccentColor, new Vector3(0.68f, 0.018f, 0.018f) * visualScale);
            _glitchShardC = CreateGlitchShard("UnstableShardC", _style.BaseColor.Lightened(0.24f), new Vector3(0.48f, 0.016f, 0.016f) * visualScale);
            ApplyDisplacementMaterialsToTarget();

            SetBurstVisualsVisible(false);
            _glitchesRemaining = 0;
            _burstTimer = 0f;
            _nextGlitchDelay = _rng.RandfRange(0.12f, 1.45f);
            return;
        }

        if (_style.UsesRadiantVisuals)
        {
            CreateRadiantVisuals(visualScale);
            return;
        }

        if (_style.UsesPolishedVisuals)
        {
            ApplyPolishedMaterialsToTarget();
            return;
        }

        if (_style.UsesElectricVisuals)
        {
            CreateElectricVisuals(visualScale);
        }
    }

    public void RefreshLightBaseline()
    {
        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            return;
        }

        _baseLightColor = _glowLight.LightColor;
        _baseLightEnergy = _glowLight.LightEnergy;
    }

    private void CreateRadiantVisuals(float visualScale)
    {
        _radiantVisualScale = Mathf.Max(0.1f, visualScale);
        _radiantFlashTimer = 0f;
        _radiantFlashDuration = _rng.RandfRange(0.18f, 0.30f);
        _nextRadiantFlashDelay = _rng.RandfRange(1.10f, 2.60f);
        _radiantSweepAngle = _rng.RandfRange(0f, 360f);

        _shinyHalo = new MeshInstance3D
        {
            Name = "ShinyGlintVertical",
            Mesh = new BoxMesh { Size = new Vector3(0.028f, 0.34f, 0.010f) * _radiantVisualScale },
            MaterialOverride = CreateEffectMaterial(_style.AccentColor, 0f, 0f),
        };
        AddChild(_shinyHalo);
        _generatedNodes.Add(_shinyHalo);

        _shinyAuraRing = new MeshInstance3D
        {
            Name = "ShinyGlintHorizontal",
            Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.028f, 0.010f) * _radiantVisualScale },
            MaterialOverride = CreateEffectMaterial(_style.GlowColor, 0f, 0f),
        };
        AddChild(_shinyAuraRing);
        _generatedNodes.Add(_shinyAuraRing);

        _shinySweep = new MeshInstance3D
        {
            Name = "ShinyGlintDiagonal",
            Mesh = new BoxMesh { Size = new Vector3(0.19f, 0.020f, 0.010f) * _radiantVisualScale },
            MaterialOverride = CreateEffectMaterial(_style.SecondaryAccentColor, 0f, 0f),
        };
        AddChild(_shinySweep);
        _generatedNodes.Add(_shinySweep);

        for (int rayIndex = 0; rayIndex < 4; rayIndex++)
        {
            MeshInstance3D ray = CreateRadiantRay($"ShinyRay{rayIndex}", rayIndex % 2 == 0 ? _style.AccentColor : _style.SecondaryAccentColor);
            _shinyRays.Add(ray);
        }

        for (int sparkleIndex = 0; sparkleIndex < 5; sparkleIndex++)
        {
            float seed = _rng.RandfRange(0f, Mathf.Tau);
            MeshInstance3D sparkle = CreateShinySparkle($"ShinySparkle{sparkleIndex}", sparkleIndex % 3 == 0 ? _style.AccentColor : _style.BaseColor.Lightened(0.12f));
            _shinySparkles.Add(sparkle);
            _shinySparkleSeeds.Add(seed);
        }
    }

    private float TickRadiantFlash(float deltaSeconds)
    {
        if (_radiantFlashTimer > 0f)
        {
            _radiantFlashTimer -= deltaSeconds;
            float normalizedAge = 1f - Mathf.Clamp(_radiantFlashTimer / Mathf.Max(0.001f, _radiantFlashDuration), 0f, 1f);
            if (_radiantFlashTimer <= 0f)
            {
                _nextRadiantFlashDelay = _rng.RandfRange(1.90f, 4.20f);
            }

            return Mathf.Pow(Mathf.Sin(Mathf.Clamp(normalizedAge, 0f, 1f) * Mathf.Pi), 0.72f);
        }

        _nextRadiantFlashDelay -= deltaSeconds;
        if (_nextRadiantFlashDelay <= 0f)
        {
            _radiantFlashDuration = _rng.RandfRange(0.18f, 0.32f);
            _radiantFlashTimer = _radiantFlashDuration;
        }

        return 0f;
    }

    private void ApplyRadiantTargetPulse(float flashAmount)
    {
        if (_target == null || !IsInstanceValid(_target))
        {
            return;
        }

        float breath = (Mathf.Sin(_time * 2.15f) + 1f) * 0.5f;
        float floatOffset = Mathf.Sin(_time * 1.25f) * 0.008f * Intensity;
        float scalePulse = 1f + (0.004f + breath * 0.008f + flashAmount * 0.018f) * Intensity;
        _target.Position = _targetBasePosition + new Vector3(0f, floatOffset, 0f);
        _target.Scale = _targetBaseScale * scalePulse;
    }

    private void ApplyRadiantNodes(float flashAmount, float deltaSeconds)
    {
        float breath = (Mathf.Sin(_time * 2.15f) + 1f) * 0.5f;
        float glintPulse = Mathf.Pow((Mathf.Sin(_time * 1.16f) + 1f) * 0.5f, 5.6f);
        _radiantSweepAngle = Mathf.PosMod(_radiantSweepAngle + deltaSeconds * (18f + flashAmount * 42f), 360f);

        float glintAngle = _radiantSweepAngle * Mathf.Pi / 180f;
        Vector3 glintPosition = new(
            Mathf.Cos(glintAngle) * 0.36f * _radiantVisualScale,
            0.08f * _radiantVisualScale + Mathf.Sin(glintAngle * 1.35f) * 0.16f * _radiantVisualScale,
            0.10f * _radiantVisualScale + Mathf.Sin(glintAngle) * 0.04f * _radiantVisualScale);
        float glintScale = 0.82f + glintPulse * 0.08f + flashAmount * 1.05f;

        if (_shinyHalo != null && IsInstanceValid(_shinyHalo))
        {
            _shinyHalo.Position = glintPosition;
            _shinyHalo.RotationDegrees = Vector3.Zero;
            _shinyHalo.Scale = Vector3.One * glintScale;
            SetEffectMaterial(_shinyHalo, _style.AccentColor, 0.01f + flashAmount * 0.58f, 0.35f + flashAmount * 2.8f);
        }

        if (_shinyAuraRing != null && IsInstanceValid(_shinyAuraRing))
        {
            _shinyAuraRing.Position = glintPosition;
            _shinyAuraRing.RotationDegrees = Vector3.Zero;
            _shinyAuraRing.Scale = Vector3.One * (glintScale * 0.92f);
            SetEffectMaterial(_shinyAuraRing, _style.GlowColor, 0.01f + flashAmount * 0.52f, 0.30f + flashAmount * 2.3f);
        }

        if (_shinySweep != null && IsInstanceValid(_shinySweep))
        {
            _shinySweep.Position = glintPosition;
            _shinySweep.RotationDegrees = new Vector3(0f, 0f, 45f);
            _shinySweep.Scale = Vector3.One * (glintScale * 0.84f);
            SetEffectMaterial(_shinySweep, _style.SecondaryAccentColor, 0.01f + flashAmount * 0.36f, 0.24f + flashAmount * 1.85f);
        }

        for (int rayIndex = 0; rayIndex < _shinyRays.Count; rayIndex++)
        {
            MeshInstance3D ray = _shinyRays[rayIndex];
            if (!IsInstanceValid(ray))
            {
                continue;
            }

            float angle = rayIndex * Mathf.Tau / Mathf.Max(1, _shinyRays.Count) + _time * (0.28f + rayIndex * 0.025f);
            float radius = (0.54f + (rayIndex % 2) * 0.12f + breath * 0.04f) * _radiantVisualScale;
            float height = Mathf.Sin(_time * (1.18f + rayIndex * 0.14f) + rayIndex) * 0.14f * _radiantVisualScale;
            float depth = Mathf.Sin(angle + rayIndex * 0.85f) * 0.11f * _radiantVisualScale;
            float pulse = Mathf.Pow((Mathf.Sin(_time * (1.8f + rayIndex * 0.3f) + rayIndex) + 1f) * 0.5f, 2.2f);
            ray.Position = new Vector3(Mathf.Cos(angle) * radius, height, depth);
            ray.RotationDegrees = new Vector3(16f + Mathf.Sin(_time * 1.4f + rayIndex) * 12f, angle * 180f / Mathf.Pi + 28f, 20f + Mathf.Cos(_time * 1.1f + rayIndex) * 18f);
            ray.Scale = new Vector3(1f, 0.86f + pulse * 0.16f + flashAmount * 0.18f, 1f);
            SetEffectMaterial(ray, rayIndex % 2 == 0 ? _style.AccentColor : _style.SecondaryAccentColor, 0.14f + breath * 0.10f + pulse * 0.10f + flashAmount * 0.14f, 0.95f + breath * 0.45f + pulse * 0.38f + flashAmount * 1.15f);
        }

        for (int sparkleIndex = 0; sparkleIndex < _shinySparkles.Count; sparkleIndex++)
        {
            MeshInstance3D sparkle = _shinySparkles[sparkleIndex];
            if (!IsInstanceValid(sparkle))
            {
                continue;
            }

            float seed = sparkleIndex < _shinySparkleSeeds.Count ? _shinySparkleSeeds[sparkleIndex] : sparkleIndex;
            float orbit = seed + _time * (0.34f + sparkleIndex % 3 * 0.05f);
            float radius = (0.30f + sparkleIndex % 3 * 0.08f) * _radiantVisualScale;
            float twinkle = Mathf.Pow((Mathf.Sin(_time * (3.0f + seed * 0.09f) + seed * 1.4f) + 1f) * 0.5f, 5.4f);
            sparkle.Position = new Vector3(
                Mathf.Cos(orbit) * radius,
                Mathf.Sin(_time * (1.0f + sparkleIndex * 0.15f) + seed) * 0.11f * _radiantVisualScale,
                Mathf.Sin(orbit) * 0.08f * _radiantVisualScale);
            sparkle.RotationDegrees = new Vector3(90f, orbit * 180f / Mathf.Pi + sparkleIndex * 18f, 0f);
            sparkle.Scale = Vector3.One * (0.86f + twinkle * 0.16f + flashAmount * 0.10f);
            SetEffectMaterial(sparkle, sparkleIndex % 3 == 0 ? _style.AccentColor : _style.BaseColor.Lightened(0.12f), 0.18f + breath * 0.08f + twinkle * 0.14f + flashAmount * 0.08f, 0.82f + twinkle * 0.45f + flashAmount * 0.70f);
        }
    }

    private void ApplyRadiantLightPulse(float flashAmount)
    {
        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            return;
        }

        float breath = (Mathf.Sin(_time * 2.15f) + 1f) * 0.5f;
        _glowLight.LightColor = _baseLightColor.Lerp(_style.GlowColor, 0.34f + flashAmount * 0.26f);
        _glowLight.LightEnergy = _baseLightEnergy + (0.16f + breath * 0.22f + flashAmount * 0.90f) * Intensity;
    }

    private MeshInstance3D CreateRadiantRing(string name, Color color, float radius, float alpha, float emissionEnergy)
    {
        float outerRadius = Mathf.Max(0.04f, radius);
        MeshInstance3D ring = new()
        {
            Name = name,
            Mesh = new TorusMesh
            {
                InnerRadius = outerRadius * 0.92f,
                OuterRadius = outerRadius,
                Rings = 80,
                RingSegments = 8,
            },
            MaterialOverride = CreateEffectMaterial(color, alpha, emissionEnergy),
        };
        AddChild(ring);
        _generatedNodes.Add(ring);
        return ring;
    }

    private MeshInstance3D CreateRadiantRay(string name, Color color)
    {
        MeshInstance3D ray = new()
        {
            Name = name,
            Mesh = new BoxMesh { Size = new Vector3(0.060f, 0.180f, 0.028f) * _radiantVisualScale },
            MaterialOverride = CreateEffectMaterial(color, 0.22f, 1.20f),
        };
        AddChild(ray);
        _generatedNodes.Add(ray);
        return ray;
    }

    private MeshInstance3D CreateShinySparkle(string name, Color color)
    {
        float radius = _rng.RandfRange(0.028f, 0.044f) * _radiantVisualScale;
        MeshInstance3D sparkle = new()
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = radius * 0.35f,
                RadialSegments = 18,
            },
            MaterialOverride = CreateEffectMaterial(color, 0.42f, 1.35f),
        };
        AddChild(sparkle);
        _generatedNodes.Add(sparkle);
        return sparkle;
    }

    private void CreateElectricVisuals(float visualScale)
    {
        _electricVisualScale = Mathf.Max(0.1f, visualScale);
        _electricBurstTimer = 0f;
        _electricBurstDuration = _rng.RandfRange(0.12f, 0.34f);
        _nextElectricBurstDelay = RollElectricCycleDelay();
        _electricEffectStepTimer = 0f;
        _electricEffectsRemaining = 0;
        _electricLightSpikeTimer = 0f;
        _electricLightSpikeDuration = 0.09f;
        _electricFloorArcRefreshTimer = 0f;
        _electricFloorArcSamples.Clear();
        ResetElectricExternalAnchorPool();
    }

    private float TickElectricBurst(float deltaSeconds)
    {
        if (_electricBurstTimer > 0f)
        {
            _electricBurstTimer -= deltaSeconds;
        }

        if (_electricLightSpikeTimer > 0f)
        {
            _electricLightSpikeTimer -= deltaSeconds;
        }

        if (_electricEffectsRemaining <= 0)
        {
            _nextElectricBurstDelay -= deltaSeconds;
            if (_nextElectricBurstDelay <= 0f)
            {
                StartElectricBurstCycle();
            }
        }

        if (_electricEffectsRemaining > 0)
        {
            _electricEffectStepTimer -= deltaSeconds;
            if (_electricEffectStepTimer <= 0f)
            {
                TriggerElectricEffectStep();
                _electricEffectsRemaining--;
                if (_electricEffectsRemaining <= 0)
                {
                    _electricEffectStepTimer = 0f;
                    _nextElectricBurstDelay = RollElectricCycleDelay();
                }
                else
                {
                    _electricEffectStepTimer = RollElectricChainDelay();
                }
            }
        }

        if (_electricBurstTimer <= 0f)
        {
            return 0f;
        }

        float normalizedAge = 1f - Mathf.Clamp(_electricBurstTimer / Mathf.Max(0.001f, _electricBurstDuration), 0f, 1f);
        return Mathf.Pow(Mathf.Sin(Mathf.Clamp(normalizedAge, 0f, 1f) * Mathf.Pi), 0.42f);
    }

    private void StartElectricBurstCycle()
    {
        float roll = _rng.Randf();
        _electricEffectsRemaining = roll < 0.16f
            ? _rng.RandiRange(5, 6)
            : roll < 0.74f
                ? _rng.RandiRange(3, 4)
                : _rng.RandiRange(2, 3);
        _electricEffectStepTimer = 0f;
    }

    private float RollElectricChainDelay()
    {
        float roll = _rng.Randf();
        if (roll < 0.24f)
        {
            return _rng.RandfRange(0.018f, 0.040f);
        }

        if (roll < 0.82f)
        {
            return _rng.RandfRange(0.050f, 0.110f);
        }

        return _rng.RandfRange(0.125f, 0.220f);
    }

    private float RollElectricCycleDelay()
    {
        float roll = _rng.Randf();
        if (roll < 0.12f)
        {
            return _rng.RandfRange(0.24f, 0.55f);
        }

        if (roll < 0.80f)
        {
            return _rng.RandfRange(0.85f, 1.90f);
        }

        return _rng.RandfRange(2.10f, 3.20f);
    }

    private void TriggerElectricEffectStep()
    {
        float roll = _rng.Randf();
        int effectsToApply = roll < 0.70f ? 1 : roll < 0.92f ? 2 : 3;
        for (int effectIndex = 0; effectIndex < effectsToApply; effectIndex++)
        {
            SpawnRandomElectricEffect();
        }

        _electricBurstDuration = _rng.RandfRange(0.12f, 0.34f);
        _electricBurstTimer = _electricBurstDuration;
        _electricLightSpikeDuration = _rng.RandfRange(0.060f, 0.120f);
        _electricLightSpikeTimer = _electricLightSpikeDuration;
    }

    private void SpawnRandomElectricEffect()
    {
        int effect = _rng.RandiRange(0, 3);
        switch (effect)
        {
            case 0:
                SpawnElectricBurst();
                break;
            case 1:
                SpawnElectricArc();
                break;
            case 2:
                SpawnElectricSparkBurst();
                break;
            default:
                SpawnElectricLightning(null);
                break;
        }
    }

    private void SpawnElectricBurst()
    {
        Vector3 start = RandomElectricAnchorPoint(0.12f, 0.76f);
        SpawnElectricEndpointSparks(start, _rng.RandiRange(2, 4), 0.82f);

        if (_rng.Randf() < 0.85f)
        {
            SpawnElectricLightning(start);
        }
    }

    private void SpawnElectricArc()
    {
        Vector3 start = RandomElectricAnchorPoint(0.20f, 0.60f);
        Vector3 end = RandomElectricAnchorPoint(0.20f, 0.60f, start);
        SpawnJaggedElectricPath(start, end, _rng.RandiRange(3, 5), _style.GlowColor, _rng.RandfRange(0.26f, 0.46f), 0.010f * _electricVisualScale, true);

        if (_rng.Randf() < 0.45f)
        {
            SpawnElectricEndpointSparks(start, _rng.RandiRange(1, 2), 0.72f);
            SpawnElectricEndpointSparks(end, _rng.RandiRange(1, 2), 0.72f);
        }
    }

    private void SpawnElectricLightning(Vector3? startOverride)
    {
        Vector3 start = startOverride ?? RandomElectricAnchorPoint(0.40f, 0.20f);
        Vector3 startGlobal = ToGlobal(start);
        float floorArcChance = ResolveElectricFloorArcChance(start, startGlobal);

        if (UsesElectricExternalAnchorPool())
        {
            if (TryGetPooledElectricExternalAnchorTarget(floorArcChance, out Vector3 pooledEnd, out bool floorArc))
            {
                SpawnElectricWorldDischarge(startGlobal, pooledEnd, floorArc);
                return;
            }
        }
        else
        {
            if (_rng.Randf() < ElectricCharacterDischargeChance
                && TryGetExternalElectricAnchorPoint(CollectCharacterElectricGlobalAnchors, out Vector3 characterEnd, startGlobal, false))
            {
                SpawnElectricWorldDischarge(startGlobal, characterEnd, false);
                return;
            }

            if (_rng.Randf() < ElectricEnvironmentDischargeChance * floorArcChance
                && TryGetExternalElectricAnchorPoint(CollectEnvironmentElectricGlobalAnchors, out Vector3 externalEnd, startGlobal, true))
            {
                SpawnElectricWorldDischarge(startGlobal, externalEnd, true);
                return;
            }
        }

        bool hasAnchorEnd = TryGetElectricAnchorPoint(out Vector3 anchorEnd, start);
        Vector3 travel = new(_rng.RandfRange(-0.55f, 0.55f), _rng.RandfRange(-0.50f, 0.50f), 0f);
        if (travel.LengthSquared() < 0.080f)
        {
            travel = new Vector3(_rng.Randf() < 0.5f ? -0.44f : 0.44f, _rng.RandfRange(-0.28f, 0.28f), 0f);
        }

        Vector3 end = hasAnchorEnd ? anchorEnd : ClampElectricPoint(start + travel * _electricVisualScale, 0.10f * _electricVisualScale);
        SpawnElectricLocalLightningStrike(start, end);
    }

    private void SpawnElectricLocalLightningStrike(Vector3 start, Vector3 end)
    {
        float pathLength = (end - start).Length();
        int iterations = pathLength > 0.62f * _electricVisualScale ? 3 : 2;
        float baseDuration = _rng.RandfRange(0.10f, 0.22f);
        int pulseCount = RollElectricLightningPulseCount();
        float pulseDelay = 0f;

        SpawnElectricEndpointSparks(start, _rng.RandiRange(1, 3), 1.0f);
        SpawnElectricEndpointSparks(end, _rng.RandiRange(2, 4), 1.1f);

        for (int pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
        {
            float duration = baseDuration * (pulseIndex == 0 ? 1f : _rng.RandfRange(0.56f, 0.84f));
            float thickness = 0.012f * _electricVisualScale * (pulseIndex == 0 ? 1f : _rng.RandfRange(0.76f, 0.94f));
            float jaggedness = _rng.RandfRange(0.070f, 0.150f) * _electricVisualScale * (pulseIndex == 0 ? 1f : _rng.RandfRange(0.84f, 1.16f));
            List<Vector3> points = BuildLightningPathMidpointDisplacement(start, end, iterations, jaggedness, true);

            SpawnElectricPathSegments(points, _style.GlowColor, duration, thickness, true, false, false, pulseDelay);

            int branchCount = pulseIndex == 0
                ? _rng.RandiRange(1, 3)
                : _rng.RandiRange(0, 1);
            if (branchCount > 0)
            {
                SpawnElectricBranches(
                    points,
                    branchCount,
                    duration * _rng.RandfRange(0.68f, 0.92f),
                    0.008f * _electricVisualScale * (pulseIndex == 0 ? 1f : 0.84f),
                    false,
                    true,
                    pulseDelay + _rng.RandfRange(0.004f, 0.014f));
            }

            pulseDelay += RollElectricLightningRetraceDelay();
        }
    }

    private void SpawnElectricSparkBurst()
    {
        SpawnElectricSparkBurst(RandomElectricAnchorPoint(0.20f, 0.60f), _rng.RandiRange(2, 5), false, 1.0f);
    }

    private void SpawnElectricSparkBurst(Vector3 origin, int count, bool blueWhite, float energyScale, float baseDelay = 0f)
    {
        SpawnElectricSparkBurstGlobal(ToGlobal(origin), count, blueWhite, energyScale, baseDelay);
    }

    private void SpawnElectricSparkBurstGlobal(Vector3 originGlobal, int count, bool blueWhite, float energyScale, float baseDelay = 0f)
    {
        SpawnElectricSparkBurstInternal(originGlobal, count, blueWhite, energyScale, true, baseDelay);
    }

    private void SpawnElectricSparkBurstInternal(Vector3 origin, int count, bool blueWhite, float energyScale, bool worldSpace, float baseDelay = 0f)
    {
        for (int sparkIndex = 0; sparkIndex < count; sparkIndex++)
        {
            Color sparkColor = ElectricSparkColor;
            float sparkDuration = _rng.RandfRange(0.18f, 0.52f);
            if (_rng.Randf() < ElectricLongLivedSparkChance)
            {
                sparkDuration *= _rng.RandfRange(ElectricLongLivedSparkDurationMultiplierMin, ElectricLongLivedSparkDurationMultiplierMax);
            }

            SpawnElectricSparkParticle(
                origin,
                sparkColor,
                _rng.RandfRange(0.008f, 0.020f) * _electricVisualScale,
                _rng.RandfRange(0.22f, 0.78f) * _electricVisualScale * energyScale,
                sparkDuration,
                baseDelay + sparkIndex * _rng.RandfRange(0.012f, 0.040f),
                energyScale,
                worldSpace);
        }
    }

    private void SpawnJaggedElectricPath(Vector3 start, Vector3 end, int interiorSegments, Color color, float duration, float thickness, bool flicker)
    {
        int iterations = interiorSegments >= 5 ? 3 : 2;
        List<Vector3> points = BuildLightningPathMidpointDisplacement(start, end, iterations, _rng.RandfRange(0.090f, 0.170f) * _electricVisualScale, true);
        SpawnElectricPathSegments(points, color, duration, thickness, flicker);

        if (_rng.Randf() < (flicker ? 0.50f : 0.28f))
        {
            SpawnElectricBranches(points, _rng.RandiRange(1, 2), duration * 0.72f, thickness * 0.62f);
        }
    }

    private void SpawnElectricWorldDischarge(Vector3 startGlobal, Vector3 endGlobal, bool floorArc)
    {
        float pathLength = (endGlobal - startGlobal).Length();
        int iterations = pathLength > 1.35f * _electricVisualScale ? 3 : 2;
        float baseDuration = Mathf.Clamp(0.11f + pathLength * 0.045f, 0.12f, 0.24f);
        int pulseCount = RollElectricLightningPulseCount();
        float pulseDelay = 0f;

        SpawnElectricEndpointSparksGlobal(startGlobal, _rng.RandiRange(1, 2), 1.0f);
        if (floorArc)
        {
            SpawnElectricFloorImpactSparks(endGlobal, _rng.RandiRange(3, 5), 1.15f);
        }
        else
        {
            SpawnElectricEndpointSparksGlobal(endGlobal, _rng.RandiRange(2, 4), 1.15f);
        }

        for (int pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
        {
            float duration = baseDuration * (pulseIndex == 0 ? 1f : _rng.RandfRange(0.58f, 0.84f));
            float thickness = 0.013f * _electricVisualScale * (pulseIndex == 0 ? 1f : _rng.RandfRange(0.80f, 0.95f));
            float jaggedness = _rng.RandfRange(0.080f, 0.180f) * _electricVisualScale * (pulseIndex == 0 ? 1f : _rng.RandfRange(0.86f, 1.18f));
            List<Vector3> points = BuildLightningPathMidpointDisplacement(startGlobal, endGlobal, iterations, jaggedness, false);

            SpawnElectricPathSegments(points, _style.GlowColor, duration, thickness, true, false, true, pulseDelay);

            int branchCount = pulseIndex == 0 ? _rng.RandiRange(1, 2) : _rng.RandiRange(0, 1);
            if (branchCount > 0)
            {
                SpawnElectricBranches(
                    points,
                    branchCount,
                    duration * _rng.RandfRange(0.68f, 0.92f),
                    0.0085f * _electricVisualScale * (pulseIndex == 0 ? 1f : 0.86f),
                    true,
                    false,
                    pulseDelay + _rng.RandfRange(0.004f, 0.014f));
            }

            pulseDelay += RollElectricLightningRetraceDelay();
        }
    }

    private void SpawnElectricPathSegments(IReadOnlyList<Vector3> points, Color color, float duration, float thickness, bool flicker, bool branch = false, bool worldSpace = false, float baseDelay = 0f)
    {
        for (int pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
        {
            Vector3 from = points[pointIndex];
            Vector3 to = points[pointIndex + 1];
            float delay = baseDelay + pointIndex * (branch ? 0.007f : 0.010f);
            Color glowColor = color.Lerp(_style.AccentColor, branch ? 0.50f : 0.68f);
            Color coreColor = _style.GlowColor.Lerp(color, branch ? 0.18f : 0.08f);

            SpawnElectricSegmentLayer(from, to, glowColor, duration + (branch ? 0.030f : 0.080f), thickness * (branch ? 2.4f : 4.6f), delay, flicker, true, branch ? 0.18f : 0.26f, branch ? 0.66f : 0.82f, branch ? 0.78f : 0.90f, "ElectricLightningGlow", worldSpace, 0f);
            SpawnElectricSegmentLayer(from, to, coreColor, duration * (branch ? 0.82f : 0.64f), thickness * (branch ? 0.88f : 1.00f), delay + 0.004f, true, true, branch ? 0.74f : 0.96f, branch ? 1.18f : 1.72f, 0.56f, "ElectricLightningCore", worldSpace, 0f);
            SpawnElectricSegmentLayer(from, to, glowColor.Lerp(_style.GlowColor, 0.36f), duration + (branch ? 0.070f : 0.140f), thickness * (branch ? 1.35f : 2.0f), delay + 0.022f, true, false, branch ? 0.12f : 0.18f, branch ? 0.48f : 0.62f, 1.10f, "ElectricLightningAfterimage", worldSpace, branch ? 0.10f : 0.14f);

            if (!branch && _rng.Randf() < 0.38f)
            {
                SpawnElectricSegmentLayer(from, to, _style.GlowColor, duration * 0.42f, thickness * 0.45f, delay + 0.006f, true, true, 0.82f, 2.20f, 0.42f, "ElectricLightningHotCore", worldSpace, 0.04f);
            }
        }
    }

    private void SpawnElectricSegmentLayer(Vector3 from, Vector3 to, Color color, float duration, float thickness, float delay, bool flicker, bool drawsIn, float alphaMultiplier, float emissionMultiplier, float fadePower, string name, bool worldSpace, float driftScale)
    {
        if (_electricEffects.Count >= MaxElectricEffectNodes)
        {
            return;
        }

        Vector3 delta = to - from;
        if (delta.LengthSquared() < 0.00001f)
        {
            return;
        }

        float length = Mathf.Max(0.001f, delta.Length());
        MeshInstance3D segment = new()
        {
            Name = name,
            Mesh = new BoxMesh { Size = new Vector3(length, Mathf.Max(0.001f, thickness), Mathf.Max(0.001f, thickness * 0.58f)) },
            MaterialOverride = CreateEffectMaterial(color, Mathf.Clamp(alphaMultiplier, 0f, 1f), 2.0f * emissionMultiplier),
        };
        AddChild(segment);
        segment.TopLevel = worldSpace;

        Vector3 midpoint = (from + to) * 0.5f;
        SetEffectNodeTransform(segment, midpoint, CreateSegmentBasis(delta), worldSpace);

        _electricEffects.Add(new ElectricEffectNode
        {
            Mesh = segment,
            Position = midpoint,
            Velocity = driftScale <= 0f
                ? Vector3.Zero
                : new Vector3(0f, -_rng.RandfRange(0.035f, 0.120f), _rng.RandfRange(-0.010f, 0.010f)) * _electricVisualScale * driftScale,
            Acceleration = Vector3.Zero,
            StartScale = drawsIn ? new Vector3(0.05f, 0.82f, 0.82f) : Vector3.One,
            EndScale = Vector3.One,
            Color = color,
            Duration = duration,
            Delay = delay,
            Seed = _rng.RandfRange(0f, Mathf.Tau),
            AlphaMultiplier = alphaMultiplier,
            FadePower = fadePower,
            EmissionMultiplier = emissionMultiplier,
            DrawsIn = drawsIn,
            Flickers = flicker,
            UseWorldSpace = worldSpace,
        });
    }

    private List<Vector3> BuildLightningPathMidpointDisplacement(Vector3 start, Vector3 end, int iterations, float jaggedness, bool clampToShapeBounds)
    {
        List<Vector3> points = new()
        {
            clampToShapeBounds ? ClampElectricPoint(start, start.Z) : start,
            clampToShapeBounds ? ClampElectricPoint(end, end.Z) : end,
        };
        int clampedIterations = iterations < 1 ? 1 : iterations > 4 ? 4 : iterations;
        for (int iteration = 0; iteration < clampedIterations; iteration++)
        {
            List<Vector3> next = new(points.Count * 2);
            float roughness = jaggedness * Mathf.Pow(0.58f, iteration);
            float tangentJitter = 0.035f * Mathf.Pow(0.55f, iteration) * _electricVisualScale;
            for (int pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
            {
                Vector3 a = points[pointIndex];
                Vector3 b = points[pointIndex + 1];
                Vector3 delta = b - a;
                Vector3 tangent = delta.LengthSquared() > 0.0001f ? delta.Normalized() : Vector3.Right;

                Vector3 midpoint = (a + b) * 0.5f;
                if (clampToShapeBounds)
                {
                    Vector3 perpendicular = new(-tangent.Y, tangent.X, 0f);
                    if (perpendicular.LengthSquared() < 0.0001f)
                    {
                        perpendicular = Vector3.Up;
                    }

                    midpoint += perpendicular.Normalized() * _rng.RandfRange(-roughness, roughness);
                    midpoint += tangent * _rng.RandfRange(-tangentJitter, tangentJitter);
                    midpoint = ClampElectricPoint(midpoint, 0.10f * _electricVisualScale + _rng.RandfRange(-0.014f, 0.014f) * _electricVisualScale);
                }
                else
                {
                    Vector3 reference = Mathf.Abs(tangent.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
                    Vector3 perpendicularA = tangent.Cross(reference).Normalized();
                    if (perpendicularA.LengthSquared() < 0.0001f)
                    {
                        perpendicularA = Vector3.Forward;
                    }

                    Vector3 perpendicularB = tangent.Cross(perpendicularA).Normalized();
                    midpoint += perpendicularA * _rng.RandfRange(-roughness, roughness);
                    midpoint += perpendicularB * _rng.RandfRange(-roughness * 0.55f, roughness * 0.55f);
                    midpoint += tangent * _rng.RandfRange(-tangentJitter, tangentJitter);
                }

                next.Add(a);
                next.Add(midpoint);
            }

            next.Add(points[^1]);
            points = next;
        }

        return points;
    }

    private void SpawnElectricBranches(IReadOnlyList<Vector3> points, int branchCount, float duration, float thickness, bool worldSpace = false, bool clampToShapeBounds = true, float baseDelay = 0f)
    {
        if (points.Count < 3 || _electricEffects.Count >= MaxElectricEffectNodes)
        {
            return;
        }

        for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
        {
            int anchorIndex = _rng.RandiRange(1, points.Count - 2);
            Vector3 anchor = points[anchorIndex];
            Vector3 previous = points[anchorIndex - 1];
            Vector3 next = points[anchorIndex + 1];
            Vector3 direction = next - previous;
            Vector3 tangent = direction.LengthSquared() > 0.0001f ? direction.Normalized() : Vector3.Right;

            Vector3 perpendicular;
            Vector3 secondary;
            if (clampToShapeBounds)
            {
                perpendicular = new Vector3(-tangent.Y, tangent.X, 0f);
                if (perpendicular.LengthSquared() < 0.0001f)
                {
                    perpendicular = Vector3.Up;
                }

                secondary = Vector3.Forward;
            }
            else
            {
                Vector3 reference = Mathf.Abs(tangent.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
                perpendicular = tangent.Cross(reference).Normalized();
                if (perpendicular.LengthSquared() < 0.0001f)
                {
                    perpendicular = Vector3.Forward;
                }

                secondary = tangent.Cross(perpendicular).Normalized();
            }

            if (_rng.Randf() < 0.5f)
            {
                perpendicular *= -1f;
            }

            float branchLength = _rng.RandfRange(0.18f, 0.42f) * _electricVisualScale * (branchIndex == 0 ? 1f : 0.72f);
            Vector3 branchEnd = anchor
                + perpendicular.Normalized() * branchLength
                + tangent * _rng.RandfRange(-0.08f, 0.12f) * _electricVisualScale
                + secondary * _rng.RandfRange(-0.05f, 0.05f) * _electricVisualScale;
            if (clampToShapeBounds)
            {
                branchEnd = ClampElectricPoint(branchEnd, 0.11f * _electricVisualScale);
            }

            List<Vector3> branchPoints = BuildLightningPathMidpointDisplacement(anchor, branchEnd, _rng.RandiRange(1, 2), _rng.RandfRange(0.035f, 0.075f) * _electricVisualScale, clampToShapeBounds);
            SpawnElectricPathSegments(branchPoints, _style.GlowColor.Lerp(_style.AccentColor, 0.35f), duration * _rng.RandfRange(0.75f, 1.05f), thickness, true, true, worldSpace, baseDelay);

            if (_rng.Randf() < 0.65f)
            {
                float endpointDelay = baseDelay + Mathf.Max(0, branchPoints.Count - 2) * 0.007f;
                if (worldSpace)
                {
                    SpawnElectricEndpointSparksGlobal(branchEnd, _rng.RandiRange(1, 2), 0.65f, endpointDelay);
                }
                else
                {
                    SpawnElectricEndpointSparks(branchEnd, _rng.RandiRange(1, 2), 0.65f, endpointDelay);
                }
            }
        }
    }

    private void SpawnElectricEndpointSparks(Vector3 origin, int count, float energyScale, float baseDelay = 0f)
    {
        SpawnElectricSparkBurst(origin, count, true, energyScale, baseDelay);
    }

    private void SpawnElectricEndpointSparksGlobal(Vector3 originGlobal, int count, float energyScale, float baseDelay = 0f)
    {
        SpawnElectricSparkBurstGlobal(originGlobal, count, true, energyScale, baseDelay);
    }

    private void SpawnElectricSparkParticle(Vector3 origin, Color color, float radius, float travelDistance, float duration, float delay, float energyScale, bool worldSpace)
    {
        if (_electricEffects.Count >= MaxElectricEffectNodes)
        {
            return;
        }

        MeshInstance3D spark = new()
        {
            Name = "ElectricSpark",
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = CreateEffectMaterial(color, 0.90f, 2.8f * energyScale),
        };
        AddChild(spark);
        spark.TopLevel = worldSpace;
        SetEffectNodeTransform(spark, origin, Basis.Identity, worldSpace);

        float angle = _rng.RandfRange(0f, Mathf.Tau);
        Vector3 lateral = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 direction = lateral * _rng.RandfRange(0.70f, 1.10f)
            + Vector3.Up * _rng.RandfRange(0.22f, 0.82f)
            + new Vector3(_rng.RandfRange(-0.12f, 0.12f), 0f, _rng.RandfRange(-0.12f, 0.12f));
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = Vector3.Up;
        }

        _electricEffects.Add(new ElectricEffectNode
        {
            Mesh = spark,
            Position = origin,
            Velocity = direction.Normalized() * (travelDistance / Mathf.Max(0.001f, duration)),
            Acceleration = new Vector3(0f, -ElectricSparkGravity * _electricVisualScale, 0f),
            StartScale = Vector3.One * (0.90f + 0.18f * energyScale),
            EndScale = Vector3.One * 0.20f,
            Color = color,
            Duration = duration,
            Delay = delay,
            Seed = _rng.RandfRange(0f, Mathf.Tau),
            AlphaMultiplier = Mathf.Clamp(0.75f + energyScale * 0.20f, 0.4f, 1.0f),
            FadePower = 0.78f,
            EmissionMultiplier = Mathf.Clamp(0.90f + energyScale * 0.30f, 0.6f, 1.5f),
            Flickers = true,
            UseWorldSpace = worldSpace,
            CollidesWithEnvironment = worldSpace && ResolveElectricEnvironmentCollision != null,
            BounceDamping = 0.24f,
            SurfaceDrag = 0.52f,
        });
    }

    private Vector3 ClampElectricPoint(Vector3 point, float z)
    {
        return new Vector3(
            Mathf.Clamp(point.X, -0.52f * _electricVisualScale, 0.52f * _electricVisualScale),
            Mathf.Clamp(point.Y, -0.50f * _electricVisualScale, 0.50f * _electricVisualScale),
            z);
    }

    private Vector3 RandomElectricPoint(float horizontalRange, float verticalRange)
    {
        return new Vector3(
            _rng.RandfRange(-horizontalRange, horizontalRange) * _electricVisualScale,
            _rng.RandfRange(-verticalRange, verticalRange) * _electricVisualScale,
            0.10f * _electricVisualScale);
    }

    private bool ShouldUseElectricWorldSpaceSparks()
    {
        return ResolveElectricEnvironmentCollision != null
            || CollectEnvironmentElectricGlobalAnchors != null
            || CollectCharacterElectricGlobalAnchors != null;
    }

    private bool UsesElectricExternalAnchorPool()
    {
        return ElectricExternalAnchorPoolSize > 0
            && (CollectEnvironmentElectricGlobalAnchors != null || CollectCharacterElectricGlobalAnchors != null);
    }

    private void UpdateElectricFloorArcSamples(float deltaSeconds)
    {
        if (ResolveElectricEnvironmentCollision == null)
        {
            _electricFloorArcSamples.Clear();
            _electricFloorArcRefreshTimer = 0f;
            return;
        }

        _electricFloorArcRefreshTimer -= deltaSeconds;
        if (_electricFloorArcRefreshTimer > 0f)
        {
            return;
        }

        _electricFloorArcRefreshTimer = ElectricFloorArcRefreshInterval;
        RebuildElectricFloorArcSamples();
    }

    private void RebuildElectricFloorArcSamples()
    {
        _electricFloorArcSamples.Clear();

        ElectricAnchorSource source = CollectElectricAnchorCandidates(_electricAnchorBuffer);
        if (source == ElectricAnchorSource.None || _electricAnchorBuffer.Count == 0)
        {
            return;
        }

        int targetSampleCount = Math.Min(18, _electricAnchorBuffer.Count);
        int stride = Mathf.Max(1, Mathf.CeilToInt(_electricAnchorBuffer.Count / (float)targetSampleCount));
        for (int anchorIndex = 0; anchorIndex < _electricAnchorBuffer.Count; anchorIndex += stride)
        {
            Vector3 localAnchor = _electricAnchorBuffer[anchorIndex];
            _electricFloorArcSamples.Add(new ElectricFloorArcSample
            {
                LocalAnchor = localAnchor,
                Chance = EvaluateElectricFloorArcChance(ToGlobal(localAnchor)),
            });
        }
    }

    private float ResolveElectricFloorArcChance(Vector3 startLocal, Vector3 startGlobal)
    {
        if (_electricFloorArcSamples.Count == 0)
        {
            return EvaluateElectricFloorArcChance(startGlobal);
        }

        float bestDistance = float.PositiveInfinity;
        float bestChance = ElectricFloorArcMinChance;
        for (int sampleIndex = 0; sampleIndex < _electricFloorArcSamples.Count; sampleIndex++)
        {
            ElectricFloorArcSample sample = _electricFloorArcSamples[sampleIndex];
            float distance = sample.LocalAnchor.DistanceSquaredTo(startLocal);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestChance = sample.Chance;
            }
        }

        return bestChance;
    }

    private float EvaluateElectricFloorArcChance(Vector3 startGlobal)
    {
        if (ResolveElectricEnvironmentCollision == null)
        {
            return ElectricFloorArcMinChance;
        }

        ElectricEnvironmentHit? hit = ResolveElectricEnvironmentCollision(
            startGlobal,
            startGlobal + Vector3.Down * ElectricFloorArcEvaluationDepth);
        if (!hit.HasValue)
        {
            return ElectricFloorArcMinChance;
        }

        Vector3 normal = hit.Value.Normal.LengthSquared() > 0.0001f
            ? hit.Value.Normal.Normalized()
            : Vector3.Up;
        if (normal.Dot(Vector3.Up) < 0.55f)
        {
            return ElectricFloorArcMinChance;
        }

        float clearance = Mathf.Max(0f, startGlobal.Y - hit.Value.Position.Y);
        float clearanceRange = Mathf.Max(0.001f, ElectricFloorArcFarClearance - ElectricFloorArcNearClearance);
        float normalized = 1f - Mathf.Clamp((clearance - ElectricFloorArcNearClearance) / clearanceRange, 0f, 1f);
        return Mathf.Lerp(ElectricFloorArcMinChance, ElectricFloorArcMaxChance, Mathf.Pow(normalized, 0.82f));
    }

    private void SpawnElectricFloorImpactSparks(Vector3 originGlobal, int count, float energyScale, float baseDelay = 0f)
    {
        Vector3 sparkOrigin = originGlobal + new Vector3(0f, 0.03f * _electricVisualScale, 0f);
        for (int sparkIndex = 0; sparkIndex < count; sparkIndex++)
        {
            SpawnElectricSparkParticle(
                sparkOrigin,
                ElectricSparkColor,
                _rng.RandfRange(0.010f, 0.022f) * _electricVisualScale,
                _rng.RandfRange(0.26f, 0.72f) * _electricVisualScale * energyScale,
                _rng.RandfRange(0.22f, 0.48f),
                baseDelay + sparkIndex * _rng.RandfRange(0.010f, 0.026f),
                energyScale,
                true);
        }
    }

    private int RollElectricLightningPulseCount()
    {
        if (_electricEffects.Count >= MaxElectricEffectNodes - 36)
        {
            return 1;
        }

        float roll = _rng.Randf();
        if (roll < 0.20f)
        {
            return 3;
        }

        return roll < 0.88f ? 2 : 1;
    }

    private float RollElectricLightningRetraceDelay()
    {
        return _rng.RandfRange(0.018f, 0.044f);
    }

    private void ResetElectricExternalAnchorPool()
    {
        _electricExternalAnchorPool.Clear();
        _electricFloorArcSamples.Clear();
        _electricFloorArcRefreshTimer = 0f;
        _electricEnvironmentDischargesSinceCharacterStrike = 0;
    }

    private bool TryGetPooledElectricExternalAnchorTarget(float floorArcChance, out Vector3 targetGlobal, out bool floorArc)
    {
        targetGlobal = Vector3.Zero;
        floorArc = false;
        EnsureElectricExternalAnchorPool();
        if (_electricExternalAnchorPool.Count == 0)
        {
            return false;
        }

        int environmentSlotCount = 0;
        int characterSlotCount = 0;
        for (int slotIndex = 0; slotIndex < _electricExternalAnchorPool.Count; slotIndex++)
        {
            if (_electricExternalAnchorPool[slotIndex].Kind == ElectricExternalAnchorKind.Character)
            {
                characterSlotCount++;
            }
            else
            {
                environmentSlotCount++;
            }
        }

        bool forceCharacter = characterSlotCount > 0
            && _electricEnvironmentDischargesSinceCharacterStrike >= Mathf.Max(1, ForceCharacterElectricAnchorAfterEnvironmentHits);
        if (!forceCharacter && _rng.Randf() >= ElectricPooledExternalDischargeChance)
        {
            return false;
        }

        ElectricExternalAnchorKind desiredKind;
        if (forceCharacter)
        {
            desiredKind = ElectricExternalAnchorKind.Character;
        }
        else if (environmentSlotCount > 0 && _rng.Randf() < floorArcChance)
        {
            desiredKind = ElectricExternalAnchorKind.Environment;
        }
        else if (characterSlotCount > 0 && _rng.Randf() < ElectricPooledCharacterSelectionChance)
        {
            desiredKind = ElectricExternalAnchorKind.Character;
        }
        else if (environmentSlotCount > 0 && characterSlotCount == 0)
        {
            desiredKind = ElectricExternalAnchorKind.Environment;
        }
        else if (characterSlotCount > 0 && environmentSlotCount == 0)
        {
            desiredKind = ElectricExternalAnchorKind.Character;
        }
        else
        {
            return false;
        }

        int chosenIndex = ChooseElectricExternalAnchorSlotIndex(desiredKind);
        if (chosenIndex < 0)
        {
            return false;
        }

        ElectricExternalAnchorSlot chosenSlot = _electricExternalAnchorPool[chosenIndex];
        targetGlobal = chosenSlot.Position;
        floorArc = chosenSlot.Kind == ElectricExternalAnchorKind.Environment;
        if (chosenSlot.Kind == ElectricExternalAnchorKind.Character)
        {
            _electricEnvironmentDischargesSinceCharacterStrike = 0;
        }
        else
        {
            _electricEnvironmentDischargesSinceCharacterStrike++;
        }

        RegenerateElectricExternalAnchorSlot(chosenIndex);
        return true;
    }

    private void EnsureElectricExternalAnchorPool()
    {
        if (!UsesElectricExternalAnchorPool())
        {
            ResetElectricExternalAnchorPool();
            return;
        }

        int desiredCount = Mathf.Max(1, ElectricExternalAnchorPoolSize);
        int desiredCharacterCount = CollectCharacterElectricGlobalAnchors != null
            ? Mathf.Clamp(MinimumCharacterElectricAnchorPoolSlots, 0, desiredCount)
            : 0;
        int desiredEnvironmentCount = CollectEnvironmentElectricGlobalAnchors != null
            ? Mathf.Max(0, desiredCount - desiredCharacterCount)
            : 0;

        if (_electricExternalAnchorPool.Count != desiredCount
            || CountElectricExternalAnchorSlots(ElectricExternalAnchorKind.Environment) != desiredEnvironmentCount
            || CountElectricExternalAnchorSlots(ElectricExternalAnchorKind.Character) != desiredCharacterCount)
        {
            RebuildElectricExternalAnchorPool(desiredEnvironmentCount, desiredCharacterCount, desiredCount);
            return;
        }

        RefreshCharacterElectricAnchorSlots();
    }

    private int CountElectricExternalAnchorSlots(ElectricExternalAnchorKind kind)
    {
        int count = 0;
        for (int slotIndex = 0; slotIndex < _electricExternalAnchorPool.Count; slotIndex++)
        {
            if (_electricExternalAnchorPool[slotIndex].Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private void RebuildElectricExternalAnchorPool(int desiredEnvironmentCount, int desiredCharacterCount, int desiredTotalCount)
    {
        _electricExternalAnchorPool.Clear();
        PopulateElectricExternalAnchorSlots(ElectricExternalAnchorKind.Environment, desiredEnvironmentCount, CollectEnvironmentElectricGlobalAnchors);
        PopulateElectricExternalAnchorSlots(ElectricExternalAnchorKind.Character, desiredCharacterCount, CollectCharacterElectricGlobalAnchors);

        if (_electricExternalAnchorPool.Count < desiredTotalCount)
        {
            PopulateElectricExternalAnchorSlots(
                ElectricExternalAnchorKind.Environment,
                desiredTotalCount - _electricExternalAnchorPool.Count,
                CollectEnvironmentElectricGlobalAnchors,
                append: true);
        }

        _electricEnvironmentDischargesSinceCharacterStrike = 0;
        RefreshCharacterElectricAnchorSlots();
    }

    private void PopulateElectricExternalAnchorSlots(ElectricExternalAnchorKind kind, int count, Action<ICollection<Vector3>>? collectAnchors, bool append = false)
    {
        if (count <= 0 || collectAnchors == null)
        {
            return;
        }

        _electricExternalAnchorBuffer.Clear();
        collectAnchors(_electricExternalAnchorBuffer);
        if (_electricExternalAnchorBuffer.Count == 0)
        {
            return;
        }

        int added = 0;
        int uniqueCount = Math.Min(count, _electricExternalAnchorBuffer.Count);
        HashSet<int> selectedIndices = new();
        while (added < uniqueCount)
        {
            int candidateIndex = _rng.RandiRange(0, _electricExternalAnchorBuffer.Count - 1);
            if (!selectedIndices.Add(candidateIndex))
            {
                continue;
            }

            _electricExternalAnchorPool.Add(new ElectricExternalAnchorSlot
            {
                Kind = kind,
                Position = _electricExternalAnchorBuffer[candidateIndex],
            });
            added++;
        }

        while (append && added < count)
        {
            int candidateIndex = _rng.RandiRange(0, _electricExternalAnchorBuffer.Count - 1);
            _electricExternalAnchorPool.Add(new ElectricExternalAnchorSlot
            {
                Kind = kind,
                Position = _electricExternalAnchorBuffer[candidateIndex],
            });
            added++;
        }
    }

    private void RefreshCharacterElectricAnchorSlots()
    {
        if (CollectCharacterElectricGlobalAnchors == null)
        {
            return;
        }

        _electricExternalAnchorBuffer.Clear();
        CollectCharacterElectricGlobalAnchors(_electricExternalAnchorBuffer);
        if (_electricExternalAnchorBuffer.Count == 0)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < _electricExternalAnchorPool.Count; slotIndex++)
        {
            ElectricExternalAnchorSlot slot = _electricExternalAnchorPool[slotIndex];
            if (slot.Kind != ElectricExternalAnchorKind.Character)
            {
                continue;
            }

            int nearestIndex = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int candidateIndex = 0; candidateIndex < _electricExternalAnchorBuffer.Count; candidateIndex++)
            {
                float distance = _electricExternalAnchorBuffer[candidateIndex].DistanceSquaredTo(slot.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = candidateIndex;
                }
            }

            slot.Position = _electricExternalAnchorBuffer[nearestIndex];
        }
    }

    private int ChooseElectricExternalAnchorSlotIndex(ElectricExternalAnchorKind desiredKind)
    {
        int matchCount = 0;
        for (int slotIndex = 0; slotIndex < _electricExternalAnchorPool.Count; slotIndex++)
        {
            if (_electricExternalAnchorPool[slotIndex].Kind == desiredKind)
            {
                matchCount++;
            }
        }

        if (matchCount == 0)
        {
            return -1;
        }

        int targetMatch = _rng.RandiRange(0, matchCount - 1);
        int seen = 0;
        for (int slotIndex = 0; slotIndex < _electricExternalAnchorPool.Count; slotIndex++)
        {
            if (_electricExternalAnchorPool[slotIndex].Kind != desiredKind)
            {
                continue;
            }

            if (seen == targetMatch)
            {
                return slotIndex;
            }

            seen++;
        }

        return -1;
    }

    private void RegenerateElectricExternalAnchorSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _electricExternalAnchorPool.Count)
        {
            return;
        }

        ElectricExternalAnchorSlot slot = _electricExternalAnchorPool[slotIndex];
        Action<ICollection<Vector3>>? collectAnchors = slot.Kind == ElectricExternalAnchorKind.Character
            ? CollectCharacterElectricGlobalAnchors
            : CollectEnvironmentElectricGlobalAnchors;
        if (TryGetExternalElectricAnchorPoint(collectAnchors, out Vector3 newPosition, slot.Position, slot.Kind == ElectricExternalAnchorKind.Environment))
        {
            slot.Position = newPosition;
        }
    }

    private Vector3 RandomElectricAnchorPoint(float fallbackHorizontalRange, float fallbackVerticalRange, Vector3? avoidPoint = null)
    {
        return TryGetElectricAnchorPoint(out Vector3 anchor, avoidPoint)
            ? anchor
            : RandomElectricPoint(fallbackHorizontalRange, fallbackVerticalRange);
    }

    private bool TryGetElectricAnchorPoint(out Vector3 anchor, Vector3? avoidPoint = null)
    {
        anchor = Vector3.Zero;
        ElectricAnchorSource source = CollectElectricAnchorCandidates(_electricAnchorBuffer);
        if (source == ElectricAnchorSource.None)
        {
            return false;
        }

        int bestIndex = _rng.RandiRange(0, _electricAnchorBuffer.Count - 1);
        if (avoidPoint.HasValue && _electricAnchorBuffer.Count > 1)
        {
            float bestDistance = -1f;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int candidateIndex = _rng.RandiRange(0, _electricAnchorBuffer.Count - 1);
                float distance = _electricAnchorBuffer[candidateIndex].DistanceSquaredTo(avoidPoint.Value);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = candidateIndex;
                }
            }
        }

        Vector3 chosen = _electricAnchorBuffer[bestIndex];
        chosen.Z += _rng.RandfRange(0.030f, 0.085f) * _electricVisualScale;
        anchor = ClampElectricPoint(chosen, chosen.Z);
        return true;
    }

    private bool TryGetExternalElectricAnchorPoint(Action<ICollection<Vector3>>? collectAnchors, out Vector3 anchorGlobal, Vector3? avoidPoint = null, bool preferFarther = false)
    {
        anchorGlobal = Vector3.Zero;
        if (collectAnchors == null)
        {
            return false;
        }

        _electricExternalAnchorBuffer.Clear();
        collectAnchors(_electricExternalAnchorBuffer);
        if (_electricExternalAnchorBuffer.Count == 0)
        {
            return false;
        }

        int bestIndex = _rng.RandiRange(0, _electricExternalAnchorBuffer.Count - 1);
        if (preferFarther && avoidPoint.HasValue && _electricExternalAnchorBuffer.Count > 1)
        {
            float bestDistance = -1f;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int candidateIndex = _rng.RandiRange(0, _electricExternalAnchorBuffer.Count - 1);
                float distance = _electricExternalAnchorBuffer[candidateIndex].DistanceSquaredTo(avoidPoint.Value);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = candidateIndex;
                }
            }
        }

        anchorGlobal = _electricExternalAnchorBuffer[bestIndex];
        return true;
    }

    private ElectricAnchorSource CollectElectricAnchorCandidates(List<Vector3> anchors)
    {
        anchors.Clear();
        if (_target == null || !IsInstanceValid(_target))
        {
            return ElectricAnchorSource.None;
        }

        CollectElectricProviderAnchors(_target, anchors);
        if (anchors.Count > 0)
        {
            return ElectricAnchorSource.Provider;
        }

        anchors.Clear();
        CollectElectricMeshBoundsAnchors(_target, anchors);
        if (anchors.Count > 0)
        {
            return ElectricAnchorSource.MeshBounds;
        }

        anchors.Clear();
        return ElectricAnchorSource.None;
    }

    private void CollectElectricProviderAnchors(Node node, List<Vector3> anchors)
    {
        if (anchors.Count >= MaxElectricAnchorCandidates)
        {
            return;
        }

        if (node is Node3D providerNode && node is IElectricAnchorProvider provider)
        {
            _electricProviderLocalAnchors.Clear();
            provider.CollectElectricAnchorPoints(_electricProviderLocalAnchors);
            for (int pointIndex = 0; pointIndex < _electricProviderLocalAnchors.Count && anchors.Count < MaxElectricAnchorCandidates; pointIndex++)
            {
                AddElectricAnchorFromNode(providerNode, _electricProviderLocalAnchors[pointIndex], anchors);
            }
        }

        foreach (Node child in node.GetChildren())
        {
            CollectElectricProviderAnchors(child, anchors);
            if (anchors.Count >= MaxElectricAnchorCandidates)
            {
                return;
            }
        }
    }

    private void CollectElectricMeshBoundsAnchors(Node node, List<Vector3> anchors)
    {
        if (anchors.Count >= MaxElectricAnchorCandidates)
        {
            return;
        }

        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            AddElectricMeshBoundsAnchors(meshInstance, anchors);
        }

        foreach (Node child in node.GetChildren())
        {
            CollectElectricMeshBoundsAnchors(child, anchors);
            if (anchors.Count >= MaxElectricAnchorCandidates)
            {
                return;
            }
        }
    }

    private void AddElectricMeshBoundsAnchors(MeshInstance3D meshInstance, List<Vector3> anchors)
    {
        Aabb bounds = meshInstance.Mesh!.GetAabb();
        if (bounds.Size.LengthSquared() < 0.00001f)
        {
            return;
        }

        Vector3 min = bounds.Position;
        Vector3 max = bounds.Position + bounds.Size;
        Vector3 center = (min + max) * 0.5f;
        Vector3[] localPoints =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
            new(center.X, center.Y, min.Z), new(center.X, center.Y, max.Z), new(center.X, min.Y, center.Z),
            new(center.X, max.Y, center.Z), new(min.X, center.Y, center.Z), new(max.X, center.Y, center.Z),
        ];

        for (int pointIndex = 0; pointIndex < localPoints.Length && anchors.Count < MaxElectricAnchorCandidates; pointIndex++)
        {
            AddElectricAnchorFromNode(meshInstance, localPoints[pointIndex], anchors);
        }
    }

    private void AddElectricAnchorFromNode(Node3D sourceNode, Vector3 sourceLocalPoint, List<Vector3> anchors)
    {
        Vector3 localPoint = ToLocal(sourceNode.ToGlobal(sourceLocalPoint));
        if (localPoint.LengthSquared() < 0.00001f && anchors.Count > 0)
        {
            return;
        }

        anchors.Add(localPoint);
    }

    private void UpdateElectricEffectNodes(float deltaSeconds)
    {
        for (int effectIndex = _electricEffects.Count - 1; effectIndex >= 0; effectIndex--)
        {
            ElectricEffectNode effect = _electricEffects[effectIndex];
            if (!IsInstanceValid(effect.Mesh))
            {
                _electricEffects.RemoveAt(effectIndex);
                continue;
            }

            effect.Age += deltaSeconds;
            float activeAge = effect.Age - effect.Delay;
            if (activeAge < 0f)
            {
                effect.Mesh.Visible = false;
                continue;
            }

            effect.Mesh.Visible = true;
            float normalizedAge = Mathf.Clamp(activeAge / Mathf.Max(0.001f, effect.Duration), 0f, 1f);
            if (normalizedAge >= 1f)
            {
                effect.Mesh.QueueFree();
                _electricEffects.RemoveAt(effectIndex);
                continue;
            }

            float simulationDelta = activeAge - effect.LastActiveAge;
            if (simulationDelta > 0f)
            {
                Vector3 previousPosition = effect.Position;
                Vector3 nextPosition = effect.Position + effect.Velocity * simulationDelta + effect.Acceleration * (0.5f * simulationDelta * simulationDelta);
                Vector3 nextVelocity = effect.Velocity + effect.Acceleration * simulationDelta;

                if (effect.CollidesWithEnvironment && ResolveElectricEnvironmentCollision != null)
                {
                    ElectricEnvironmentHit? hit = ResolveElectricEnvironmentCollision(previousPosition, nextPosition);
                    if (hit.HasValue)
                    {
                        Vector3 normal = hit.Value.Normal.LengthSquared() > 0.0001f ? hit.Value.Normal.Normalized() : Vector3.Up;
                        nextPosition = hit.Value.Position + normal * (0.01f * _electricVisualScale);

                        float normalVelocity = nextVelocity.Dot(normal);
                        Vector3 tangentialVelocity = nextVelocity - normal * normalVelocity;
                        nextVelocity = tangentialVelocity * effect.SurfaceDrag;
                        if (normalVelocity < 0f)
                        {
                            nextVelocity -= normal * normalVelocity * effect.BounceDamping;
                        }
                    }
                }

                effect.Position = nextPosition;
                effect.Velocity = nextVelocity;
                effect.LastActiveAge = activeAge;
            }

            float fade = Mathf.Pow(1f - normalizedAge, effect.FadePower);
            float drawAmount = effect.DrawsIn ? Mathf.Clamp(normalizedAge * 3.2f, 0f, 1f) : 1f;
            float rawFlicker = effect.Flickers
                ? Mathf.Clamp(0.54f + 0.46f * Mathf.Sign(Mathf.Sin(_time * 48f + effect.Seed)), 0.28f, 1f)
                : 1f;
            float flicker = effect.Flickers
                ? Mathf.Lerp(1f, rawFlicker, ElectricFlickerScale)
                : 1f;
            SetEffectNodePosition(effect.Mesh, effect.Position, effect.UseWorldSpace);
            effect.Mesh.Scale = effect.StartScale.Lerp(effect.EndScale, Mathf.Pow(normalizedAge, 0.72f));
            if (effect.DrawsIn)
            {
                effect.Mesh.Scale = new Vector3(effect.Mesh.Scale.X * drawAmount, effect.Mesh.Scale.Y, effect.Mesh.Scale.Z);
            }

            SetEffectMaterial(effect.Mesh, effect.Color, fade * flicker * effect.AlphaMultiplier, (1.65f + flicker * 1.75f + (1f - normalizedAge) * 1.65f) * effect.EmissionMultiplier);
        }
    }

    private void ApplyElectricTargetPulse(float burstAmount)
    {
        _ = burstAmount;

        if (_target == null || !IsInstanceValid(_target))
        {
            return;
        }

        _target.Position = _targetBasePosition;
        _target.Scale = _targetBaseScale;
    }

    private void ApplyElectricNodes(float burstAmount, float deltaSeconds)
    {
        _ = burstAmount;
        UpdateElectricEffectNodes(deltaSeconds);
    }

    private void ApplyElectricLightPulse(float burstAmount)
    {
        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            return;
        }

        float rawFlicker = (Mathf.Sin(_time * 18.0f) + 1f) * 0.5f;
        float flicker = 0.5f + (rawFlicker - 0.5f) * ElectricFlickerScale;
        float lightSpike = _electricLightSpikeTimer > 0f
            ? Mathf.Pow(Mathf.Clamp(_electricLightSpikeTimer / Mathf.Max(0.001f, _electricLightSpikeDuration), 0f, 1f), 1.7f)
            : 0f;
        float hotFlicker = lightSpike * Mathf.Pow((Mathf.Sin(_time * 92.0f) + 1f) * 0.5f, 4.0f) * ElectricFlickerScale;
        Color electricColor = _style.BaseColor
            .Lerp(_style.GlowColor, 0.35f + flicker * 0.38f + lightSpike * 0.18f)
            .Lerp(_style.AccentColor, burstAmount * 0.42f + hotFlicker * 0.16f)
            .Lerp(_style.SecondaryAccentColor, hotFlicker * 0.20f);
        _glowLight.LightColor = _baseLightColor.Lerp(electricColor, 0.70f + burstAmount * 0.18f + lightSpike * 0.10f);
        _glowLight.LightEnergy = _baseLightEnergy + (0.14f + flicker * 0.20f + burstAmount * 1.15f + lightSpike * 2.65f + hotFlicker * 0.90f) * Intensity;
    }

    private float TickGlitchBurst(float deltaSeconds)
    {
        if (_burstTimer > 0f)
        {
            _burstTimer -= deltaSeconds;
            _stepTimer -= deltaSeconds;
            if (_stepTimer <= 0f)
            {
                RandomizeGlitchOffsets();
                _stepTimer = _rng.RandfRange(0.018f, 0.078f);
            }

            if (_burstTimer <= 0f)
            {
                SetBurstVisualsVisible(false);
                RestoreTargetState();
                _nextGlitchDelay = _glitchesRemaining > 0
                    ? RollChainDelay()
                    : RollCycleDelay();
                return 0f;
            }

            float normalizedAge = 1f - Mathf.Clamp(_burstTimer / Mathf.Max(0.001f, _burstDuration), 0f, 1f);
            float envelope = Mathf.Pow(Mathf.Sin(Mathf.Clamp(normalizedAge, 0f, 1f) * Mathf.Pi), _burstEnvelopeSkew);
            float dirtyFlicker = 0.68f + 0.32f * Mathf.Sin((_time + _shaderSeed) * _rng.RandfRange(17f, 43f));
            return Mathf.Clamp(Mathf.Max(0.18f, envelope) * dirtyFlicker, 0f, 1f);
        }

        _nextGlitchDelay -= deltaSeconds;
        bool rareEarlyInterrupt = _nextGlitchDelay > 0.28f && _rng.Randf() < deltaSeconds * 0.020f;
        if (_nextGlitchDelay <= 0f || rareEarlyInterrupt)
        {
            if (_glitchesRemaining <= 0)
            {
                _glitchesRemaining = _rng.RandiRange(1, _rng.Randf() < 0.14f ? 7 : 4);
            }

            StartGlitchBurst();
            return 1f;
        }

        return 0f;
    }

    private void StartGlitchBurst()
    {
        _glitchesRemaining = Mathf.Max(0, _glitchesRemaining - 1);
        float durationRoll = _rng.Randf();
        _burstDuration = durationRoll < 0.18f
            ? _rng.RandfRange(0.045f, 0.085f)
            : durationRoll < 0.84f
                ? _rng.RandfRange(0.105f, 0.225f)
                : _rng.RandfRange(0.245f, 0.380f);
        _burstTimer = _burstDuration;
        _stepTimer = 0f;
        _burstEnvelopeSkew = _rng.RandfRange(0.45f, 1.85f);
        _shapeDisplacementAmount = _rng.RandfRange(0.030f, 0.120f) * (durationRoll > 0.84f ? 1.35f : 1f);
        _shaderSeed = _rng.RandfRange(0f, 8192f);
        _bandScale = _rng.RandfRange(6f, 34f);
        _tearChance = _rng.RandfRange(0.24f, 0.88f);
        _glitchAxis = RandomUnitVector();
        RandomizeGlitchOffsets();
        SetBurstVisualsVisible(true);
    }

    private float RollChainDelay()
    {
        float roll = _rng.Randf();
        if (roll < 0.20f)
        {
            return _rng.RandfRange(0.015f, 0.045f);
        }

        if (roll < 0.82f)
        {
            return _rng.RandfRange(0.055f, 0.145f);
        }

        return _rng.RandfRange(0.18f, 0.42f);
    }

    private float RollCycleDelay()
    {
        float roll = _rng.Randf();
        if (roll < 0.12f)
        {
            return _rng.RandfRange(0.18f, 0.58f);
        }

        if (roll < 0.76f)
        {
            return _rng.RandfRange(0.85f, 3.75f);
        }

        return _rng.RandfRange(3.9f, 6.6f);
    }

    private void RandomizeGlitchOffsets()
    {
        float jitter = _rng.RandfRange(0.025f, 0.080f) * Intensity;
        float ghost = _rng.RandfRange(0.055f, 0.155f) * Intensity;
        _currentTargetOffset = new Vector3(
            _rng.RandfRange(-jitter, jitter),
            _rng.RandfRange(-jitter * 0.55f, jitter * 0.55f),
            _rng.RandfRange(-jitter * 0.35f, jitter * 0.35f));
        _cyanOffset = new Vector3(_rng.RandfRange(-ghost, ghost), _rng.RandfRange(-ghost * 0.55f, ghost * 0.55f), _rng.RandfRange(-ghost * 0.20f, ghost * 0.20f));
        _magentaOffset = new Vector3(_rng.RandfRange(-ghost, ghost), _rng.RandfRange(-ghost * 0.55f, ghost * 0.55f), _rng.RandfRange(-ghost * 0.20f, ghost * 0.20f));
        _cyanRotation = new Vector3(_rng.RandfRange(-5f, 5f), _rng.RandfRange(-9f, 9f), _rng.RandfRange(-6f, 6f));
        _magentaRotation = new Vector3(_rng.RandfRange(-5f, 5f), _rng.RandfRange(-9f, 9f), _rng.RandfRange(-6f, 6f));
        _targetScaleJitter = new Vector3(
            _rng.RandfRange(-0.090f, 0.120f),
            _rng.RandfRange(-0.075f, 0.105f),
            _rng.RandfRange(-0.065f, 0.095f)) * Intensity;
        if (_rng.Randf() < 0.45f)
        {
            _glitchAxis = RandomUnitVector();
        }

        _shaderSeed += _rng.RandfRange(3.7f, 97.1f);
        RandomizeShard(_glitchShardA, 0.32f);
        RandomizeShard(_glitchShardB, -0.18f);
        RandomizeShard(_glitchShardC, 0.02f);
    }

    private void ApplyTargetOffset(float burstAmount)
    {
        _ = burstAmount;

        if (_target == null || !IsInstanceValid(_target))
        {
            return;
        }

        _target.Position = _targetBasePosition;
        _target.Scale = _targetBaseScale;
    }

    private void ApplyGhostOffsets(float burstAmount)
    {
        bool visible = burstAmount > 0f;
        if (_cyanGhost != null && IsInstanceValid(_cyanGhost))
        {
            _cyanGhost.Visible = visible;
            _cyanGhost.Position = _targetBasePosition + _cyanOffset * Mathf.Max(0.35f, burstAmount);
            _cyanGhost.RotationDegrees = _cyanRotation;
        }

        if (_magentaGhost != null && IsInstanceValid(_magentaGhost))
        {
            _magentaGhost.Visible = visible;
            _magentaGhost.Position = _targetBasePosition + _magentaOffset * Mathf.Max(0.35f, burstAmount);
            _magentaGhost.RotationDegrees = _magentaRotation;
        }
    }

    private void ApplyDisplacementMaterials(float burstAmount)
    {
        if (_displacementMaterials.Count == 0)
        {
            ApplyDisplacementMaterialsToTarget();
        }

        float idleDisplacement = (0.0016f + 0.0012f * Mathf.Abs(Mathf.Sin(_time * 5.7f + _shaderSeed))) * Intensity;
        float activeDisplacement = _shapeDisplacementAmount * burstAmount * Intensity;
        float amount = idleDisplacement + activeDisplacement;

        for (int materialIndex = 0; materialIndex < _displacementMaterials.Count; materialIndex++)
        {
            DisplacementMaterialTarget materialTarget = _displacementMaterials[materialIndex];
            if (!IsInstanceValid(materialTarget.Mesh))
            {
                continue;
            }

            float materialSeed = _shaderSeed + materialIndex * 41.13f;
            materialTarget.Material.SetShaderParameter("glitch_amount", amount * (0.78f + (materialIndex % 5) * 0.085f));
            materialTarget.Material.SetShaderParameter("glitch_time", _time + materialIndex * 0.031f);
            materialTarget.Material.SetShaderParameter("glitch_seed", materialSeed);
            materialTarget.Material.SetShaderParameter("band_scale", _bandScale + (materialIndex % 4) * 2.3f);
            materialTarget.Material.SetShaderParameter("tear_chance", Mathf.Clamp(_tearChance + ((materialIndex % 3) - 1) * 0.075f, 0.05f, 0.95f));
            materialTarget.Material.SetShaderParameter("axis", _glitchAxis);
        }
    }

    private void ApplyDisplacementMaterialsToTarget()
    {
        if (_target == null || !IsInstanceValid(_target))
        {
            return;
        }

        RestoreDisplacementMaterials();
        CollectDisplacementMaterials(_target);
    }

    private void ApplyPolishedMaterialsToTarget()
    {
        if (_target == null || !IsInstanceValid(_target))
        {
            return;
        }

        RestorePolishedMaterials();
        CollectPolishedMaterials(_target);
    }

    private void ApplyPolishedMaterialAnimation()
    {
        if (_target == null || !IsInstanceValid(_target))
        {
            return;
        }

        Shader polishedShader = GetPolishedSparkleShader();
        bool needsRefresh = _polishedMaterials.Count == 0;
        if (!needsRefresh)
        {
            for (int materialIndex = 0; materialIndex < _polishedMaterials.Count; materialIndex++)
            {
                PolishedMaterialTarget materialTarget = _polishedMaterials[materialIndex];
                if (!IsInstanceValid(materialTarget.Mesh)
                    || !IsInstanceValid(materialTarget.Material)
                    || materialTarget.Mesh.MaterialOverride != materialTarget.Material
                    || materialTarget.Material.Shader != polishedShader)
                {
                    needsRefresh = true;
                    break;
                }
            }
        }

        if (needsRefresh)
        {
            ApplyPolishedMaterialsToTarget();
        }

        for (int materialIndex = 0; materialIndex < _polishedMaterials.Count; materialIndex++)
        {
            PolishedMaterialTarget materialTarget = _polishedMaterials[materialIndex];
            if (!IsInstanceValid(materialTarget.Mesh))
            {
                continue;
            }

            materialTarget.Material.SetShaderParameter("sparkle_time", _time + materialIndex * 0.19f);
            materialTarget.Material.SetShaderParameter("sparkle_intensity", 0.34f * Mathf.Max(0.5f, Intensity));
        }
    }

    private void CollectDisplacementMaterials(Node node)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null && ShouldApplyCoreModifierMaterial(meshInstance))
        {
            Material? original = meshInstance.MaterialOverride;
            ShaderMaterial material = CreateDisplacementMaterial(original);
            meshInstance.MaterialOverride = material;
            _displacementMaterials.Add(new DisplacementMaterialTarget
            {
                Mesh = meshInstance,
                OriginalMaterialOverride = original,
                Material = material,
            });
        }

        foreach (Node child in node.GetChildren())
        {
            CollectDisplacementMaterials(child);
        }
    }

    private void CollectPolishedMaterials(Node node)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null && ShouldApplyCoreModifierMaterial(meshInstance))
        {
            Material? original = meshInstance.MaterialOverride;
            ShaderMaterial material = CreatePolishedMaterial(original, _shaderSeed + _polishedMaterials.Count * 17.23f);
            meshInstance.MaterialOverride = material;
            _polishedMaterials.Add(new PolishedMaterialTarget
            {
                Mesh = meshInstance,
                OriginalMaterialOverride = original,
                Material = material,
            });
        }

        foreach (Node child in node.GetChildren())
        {
            CollectPolishedMaterials(child);
        }
    }

    private ShaderMaterial CreateDisplacementMaterial(Material? original)
    {
        Color albedo = new(_style.BaseColor.R, _style.BaseColor.G, _style.BaseColor.B, 0.92f);
        Color emission = _style.BaseColor.Lightened(0.10f);
        float emissionEnergy = 0.62f;

        if (original is StandardMaterial3D standardMaterial)
        {
            albedo = standardMaterial.AlbedoColor;
            emission = standardMaterial.EmissionEnabled ? standardMaterial.Emission : standardMaterial.AlbedoColor;
            emissionEnergy = Mathf.Max(standardMaterial.EmissionEnergyMultiplier, 0.42f);
        }

        ShaderMaterial material = new()
        {
            Shader = GetDisplacementShader(),
        };
        material.SetShaderParameter("albedo_color", albedo);
        material.SetShaderParameter("emission_color", emission.Lerp(_style.BaseColor, 0.25f));
        material.SetShaderParameter("emission_energy", emissionEnergy + 0.18f);
        material.SetShaderParameter("glitch_amount", 0f);
        material.SetShaderParameter("glitch_time", 0f);
        material.SetShaderParameter("glitch_seed", _shaderSeed);
        material.SetShaderParameter("band_scale", _bandScale);
        material.SetShaderParameter("tear_chance", _tearChance);
        material.SetShaderParameter("axis", _glitchAxis);
        return material;
    }

    private ShaderMaterial CreatePolishedMaterial(Material? original, float seed)
    {
        Color baseAlbedo = original is StandardMaterial3D originalStandard && originalStandard.AlbedoColor.A > 0f
            ? originalStandard.AlbedoColor
            : _style.BaseColor;
        Color glossTint = _style.BaseColor.Lerp(_style.GlowColor, 0.42f);
        Color polishedAlbedo = baseAlbedo.Lerp(glossTint, 0.16f);
        Color emission = (original is StandardMaterial3D emissionStandard && emissionStandard.EmissionEnabled
            ? emissionStandard.Emission
            : polishedAlbedo).Lerp(_style.GlowColor, 0.30f);
        float emissionEnergy = Mathf.Max(original is StandardMaterial3D energyStandard ? energyStandard.EmissionEnergyMultiplier : 0f, 0.16f);
        float metallic = Mathf.Max(original is StandardMaterial3D metallicStandard ? metallicStandard.Metallic : 0f, 0.82f);
        float roughness = Mathf.Min(original is StandardMaterial3D roughnessStandard ? roughnessStandard.Roughness : 1f, 0.16f);
        float rim = Mathf.Max(original is StandardMaterial3D rimStandard ? rimStandard.Rim : 0f, 0.82f);

        ShaderMaterial material = new()
        {
            Shader = GetPolishedSparkleShader(),
        };
        material.SetShaderParameter("albedo_color", new Color(polishedAlbedo.R, polishedAlbedo.G, polishedAlbedo.B, 1f));
        material.SetShaderParameter("glow_color", emission);
        material.SetShaderParameter("base_emission_energy", emissionEnergy);
        material.SetShaderParameter("base_metallic", metallic);
        material.SetShaderParameter("base_roughness", roughness);
        material.SetShaderParameter("base_rim", rim);
        material.SetShaderParameter("base_rim_tint", 0.32f);
        material.SetShaderParameter("sparkle_color", _style.GlowColor.Lerp(_style.AccentColor, 0.35f));
        material.SetShaderParameter("sparkle_time", 0f);
        material.SetShaderParameter("sparkle_seed", seed);
        material.SetShaderParameter("sparkle_intensity", 0.34f * Mathf.Max(0.5f, Intensity));
        material.SetShaderParameter("sparkle_density", 4.4f);
        material.SetShaderParameter("sparkle_threshold", 0.82f);
        material.SetShaderParameter("sparkle_energy", 4.6f);
        return material;
    }

    private static Shader GetDisplacementShader()
    {
        if (_displacementShader != null)
        {
            return _displacementShader;
        }

        _displacementShader = new Shader
        {
            Code = @"shader_type spatial;
render_mode blend_mix, cull_disabled;

uniform vec4 albedo_color : source_color = vec4(0.66, 0.33, 0.97, 0.92);
uniform vec4 emission_color : source_color = vec4(0.66, 0.33, 0.97, 1.0);
uniform float emission_energy = 0.8;
uniform float glitch_amount = 0.0;
uniform float glitch_time = 0.0;
uniform float glitch_seed = 0.0;
uniform float band_scale = 18.0;
uniform float tear_chance = 0.55;
uniform vec3 axis = vec3(1.0, 0.0, 0.0);

varying vec3 local_pos;

float glitch_hash(float n) {
    return fract(sin(n) * 43758.5453123);
}

void vertex() {
    local_pos = VERTEX;
}

void fragment() {
    vec3 safe_axis = normalize(axis + vec3(0.0007, 0.0011, 0.0017));
    float diagonal = dot(local_pos, safe_axis) * 0.77 + local_pos.y * 0.41;
    float band = floor((diagonal + glitch_seed) * band_scale + glitch_time * 9.0);
    float band_noise = glitch_hash(band + glitch_seed * 19.17);
    float active_band = step(1.0 - tear_chance, band_noise);
    float signed_noise = glitch_hash(band * 1.37 + glitch_seed * 29.0) * 2.0 - 1.0;
    float micro_noise = sin((local_pos.x * 31.0 + local_pos.y * 47.0 + local_pos.z * 19.0) + glitch_time * 28.0 + glitch_seed);
    float scan = 0.5 + 0.5 * sin(diagonal * band_scale * 0.42 + glitch_time * 18.0 + glitch_seed);
    float glitch_mask = clamp(active_band * (0.45 + 0.55 * scan) + max(0.0, micro_noise) * 0.12, 0.0, 1.0) * clamp(glitch_amount * 14.0, 0.06, 1.0);
    vec3 glitch_tint = mix(albedo_color.rgb, emission_color.rgb, clamp(0.22 + glitch_mask * 0.58 + signed_noise * 0.10, 0.0, 1.0));

    ALBEDO = mix(albedo_color.rgb, glitch_tint, 0.36 + glitch_mask * 0.42);
    ALPHA = clamp(albedo_color.a - glitch_mask * 0.12 + micro_noise * 0.03, 0.32, 1.0);
    EMISSION = emission_color.rgb * emission_energy * (1.0 + glitch_mask * 0.95 + max(0.0, micro_noise) * 0.18);
    ROUGHNESS = 0.32;
}",
        };
        return _displacementShader;
    }

    private static bool ShouldApplyCoreModifierMaterial(MeshInstance3D meshInstance)
    {
        string meshName = meshInstance.Name.ToString().Trim().ToLowerInvariant();
        for (int tokenIndex = 0; tokenIndex < NonCoreMeshNameTokens.Length; tokenIndex++)
        {
            if (meshName.Contains(NonCoreMeshNameTokens[tokenIndex], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static Shader GetPolishedSparkleShader()
    {
        if (_polishedSparkleShader != null)
        {
            return _polishedSparkleShader;
        }

        _polishedSparkleShader = new Shader
        {
            Code = @"shader_type spatial;
render_mode depth_draw_opaque, cull_disabled;

uniform vec4 albedo_color : source_color = vec4(0.98, 0.82, 0.28, 1.0);
uniform vec4 glow_color : source_color = vec4(1.0, 0.97, 0.82, 1.0);
uniform float base_emission_energy = 0.16;
uniform float base_metallic = 0.82;
uniform float base_roughness = 0.16;
uniform float base_rim = 0.82;
uniform float base_rim_tint = 0.32;

uniform vec4 sparkle_color : source_color = vec4(1.0, 0.96, 0.78, 1.0);
uniform float sparkle_time = 0.0;
uniform float sparkle_seed = 0.0;
uniform float sparkle_intensity = 0.34;
uniform float sparkle_density = 4.4;
uniform float sparkle_threshold = 0.82;
uniform float sparkle_energy = 4.6;

varying vec3 local_pos;

float sparkle_hash(vec3 p) {
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453123);
}

float sparkle_hash_1d(float n) {
    return fract(sin(n * 91.173 + 17.13) * 43758.5453123);
}

float sparkle_window(float phase, float center, float width, float sharpness) {
    float window = 1.0 - smoothstep(width, width * 1.7, abs(phase - center));
    return pow(max(window, 0.0), sharpness);
}

void vertex() {
    local_pos = VERTEX;
}

void fragment() {
    vec3 pos = local_pos * sparkle_density;
    vec3 cell = floor(pos + sparkle_seed);
    vec3 local_cell = fract(pos + sparkle_seed) - 0.5;
    float id = sparkle_hash(cell);
    float active = step(sparkle_threshold, id);

    float burst_rate = mix(0.45, 1.75, sparkle_hash(cell + vec3(4.2, 7.1, 2.6)));
    float burst_clock = sparkle_time * burst_rate + id * 13.7;
    float burst_index = floor(burst_clock);
    float burst_phase = fract(burst_clock);
    float burst_seed = sparkle_hash(cell + vec3(burst_index, burst_index * 0.73, burst_index * 1.37));
    float burst_enabled = step(0.64, burst_seed);
    float burst_center = mix(0.12, 0.88, sparkle_hash(cell + vec3(burst_index * 1.91, 5.4, 8.2)));
    float burst_width = mix(0.05, 0.22, sparkle_hash(cell + vec3(2.7, burst_index * 1.13, 6.8)));
    float burst_shape = smoothstep(burst_width, 0.0, abs(burst_phase - burst_center));
    burst_shape = pow(burst_shape, mix(1.6, 4.6, sparkle_hash_1d(burst_index + id * 29.0)));
    float blink = burst_enabled * burst_shape;

    float core = smoothstep(0.28, 0.0, length(local_cell));
    float cross_xy = smoothstep(0.12, 0.0, abs(local_cell.x)) * smoothstep(0.48, 0.0, abs(local_cell.y));
    float cross_yz = smoothstep(0.12, 0.0, abs(local_cell.y)) * smoothstep(0.48, 0.0, abs(local_cell.z));
    float star = max(core, max(cross_xy, cross_yz));

    float ndv = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
    float fresnel = pow(1.0 - ndv, 2.2);
    float sparkle = active * blink * star * (0.45 + fresnel * 0.55) * sparkle_intensity;

    ALBEDO = albedo_color.rgb;
    METALLIC = base_metallic;
    ROUGHNESS = base_roughness;
    RIM = base_rim;
    RIM_TINT = base_rim_tint;
    EMISSION = glow_color.rgb * base_emission_energy + sparkle_color.rgb * sparkle * sparkle_energy;
}",
        };
        return _polishedSparkleShader;
    }

    private void ApplyLightPulse(float burstAmount)
    {
        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            return;
        }

        Color accent = Mathf.Sin(_time * 38f) >= 0f ? _style.AccentColor : _style.SecondaryAccentColor;
        float idlePulse = (Mathf.Sin(_time * 3.1f) + 1f) * 0.5f;
        _glowLight.LightColor = _baseLightColor.Lerp(_style.BaseColor, 0.42f).Lerp(accent, burstAmount * 0.55f);
        _glowLight.LightEnergy = _baseLightEnergy + (0.18f + idlePulse * 0.22f + burstAmount * 1.15f) * Intensity;
    }

    private Node3D CreateGhost(ShapeData shape, Color color, string name, float visualScale)
    {
        Node3D ghost = ShapeVisualFactory.CreateVisual(shape, color);
        ghost.Name = name;
        ghost.Scale = new Vector3(ghost.Scale.X * visualScale, ghost.Scale.Y * visualScale, ghost.Scale.Z * visualScale);
        ghost.Visible = false;
        AddChild(ghost);
        _generatedNodes.Add(ghost);
        return ghost;
    }

    private MeshInstance3D CreateGlitchShard(string name, Color color, Vector3 size)
    {
        MeshInstance3D shard = new()
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = CreateEffectMaterial(color, 0.55f, 1.75f),
            Visible = false,
        };
        AddChild(shard);
        _generatedNodes.Add(shard);
        return shard;
    }

    private void RandomizeShard(MeshInstance3D? shard, float yBias)
    {
        if (shard == null || !IsInstanceValid(shard))
        {
            return;
        }

        shard.Position = new Vector3(_rng.RandfRange(-0.36f, 0.36f) * Intensity, (yBias + _rng.RandfRange(-0.18f, 0.18f)) * Intensity, _rng.RandfRange(-0.05f, 0.05f));
        shard.RotationDegrees = new Vector3(_rng.RandfRange(-10f, 10f), _rng.RandfRange(-35f, 35f), _rng.RandfRange(-18f, 18f));
    }

    private void SetBurstVisualsVisible(bool visible)
    {
        if (_cyanGhost != null && IsInstanceValid(_cyanGhost))
        {
            _cyanGhost.Visible = visible;
        }

        if (_magentaGhost != null && IsInstanceValid(_magentaGhost))
        {
            _magentaGhost.Visible = visible;
        }

        if (_glitchShardA != null && IsInstanceValid(_glitchShardA))
        {
            _glitchShardA.Visible = visible;
        }

        if (_glitchShardB != null && IsInstanceValid(_glitchShardB))
        {
            _glitchShardB.Visible = visible;
        }

        if (_glitchShardC != null && IsInstanceValid(_glitchShardC))
        {
            _glitchShardC.Visible = visible;
        }
    }

    private void ClearGeneratedVisuals()
    {
        RestoreTargetState();
        RestoreLightState();
        RestorePolishedMaterials();
        RestoreDisplacementMaterials();
        ClearElectricEffects();
        ResetElectricExternalAnchorPool();

        for (int nodeIndex = 0; nodeIndex < _generatedNodes.Count; nodeIndex++)
        {
            Node3D node = _generatedNodes[nodeIndex];
            if (IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _generatedNodes.Clear();
        _cyanGhost = null;
        _magentaGhost = null;
        _glitchShardA = null;
        _glitchShardB = null;
        _glitchShardC = null;
        _shinyHalo = null;
        _shinyAuraRing = null;
        _shinySweep = null;
        _shinySparkles.Clear();
        _shinySparkleSeeds.Clear();
        _shinyRays.Clear();
        _visualKind = ModifierVisualKind.None;
        _active = false;
    }

    private void ClearElectricEffects()
    {
        for (int effectIndex = 0; effectIndex < _electricEffects.Count; effectIndex++)
        {
            ElectricEffectNode effect = _electricEffects[effectIndex];
            if (IsInstanceValid(effect.Mesh))
            {
                effect.Mesh.QueueFree();
            }
        }

        _electricEffects.Clear();
    }

    private void RestoreTargetState()
    {
        if (_target != null && IsInstanceValid(_target))
        {
            _target.Position = _targetBasePosition;
            _target.Scale = _targetBaseScale;
        }
    }

    private void RestoreDisplacementMaterials()
    {
        for (int materialIndex = 0; materialIndex < _displacementMaterials.Count; materialIndex++)
        {
            DisplacementMaterialTarget materialTarget = _displacementMaterials[materialIndex];
            if (IsInstanceValid(materialTarget.Mesh))
            {
                materialTarget.Mesh.MaterialOverride = materialTarget.OriginalMaterialOverride;
            }
        }

        _displacementMaterials.Clear();
    }

    private void RestorePolishedMaterials()
    {
        for (int materialIndex = 0; materialIndex < _polishedMaterials.Count; materialIndex++)
        {
            PolishedMaterialTarget materialTarget = _polishedMaterials[materialIndex];
            if (IsInstanceValid(materialTarget.Mesh))
            {
                materialTarget.Mesh.MaterialOverride = materialTarget.OriginalMaterialOverride;
            }
        }

        _polishedMaterials.Clear();
    }

    private Vector3 RandomUnitVector()
    {
        Vector3 value = new(
            _rng.RandfRange(-1f, 1f),
            _rng.RandfRange(-0.75f, 0.75f),
            _rng.RandfRange(-1f, 1f));
        if (value.LengthSquared() < 0.001f)
        {
            return Vector3.Right;
        }

        return value.Normalized();
    }

    private void RestoreLightState()
    {
        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            return;
        }

        _glowLight.LightColor = _baseLightColor;
        _glowLight.LightEnergy = _baseLightEnergy;
    }

    private static StandardMaterial3D CreateEffectMaterial(Color color, float alpha, float emissionEnergy)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, alpha),
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = emissionEnergy,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static Basis CreateSegmentBasis(Vector3 delta)
    {
        Vector3 xAxis = delta.LengthSquared() > 0.0001f ? delta.Normalized() : Vector3.Right;
        Vector3 reference = Mathf.Abs(xAxis.Dot(Vector3.Up)) > 0.96f ? Vector3.Forward : Vector3.Up;
        Vector3 zAxis = xAxis.Cross(reference).Normalized();
        if (zAxis.LengthSquared() < 0.0001f)
        {
            zAxis = Vector3.Forward;
        }

        Vector3 yAxis = zAxis.Cross(xAxis).Normalized();
        return new Basis(xAxis, yAxis, zAxis);
    }

    private static void SetEffectNodeTransform(MeshInstance3D mesh, Vector3 origin, Basis basis, bool worldSpace)
    {
        Transform3D transform = new(basis, origin);
        if (worldSpace)
        {
            mesh.GlobalTransform = transform;
        }
        else
        {
            mesh.Transform = transform;
        }
    }

    private static void SetEffectNodePosition(MeshInstance3D mesh, Vector3 origin, bool worldSpace)
    {
        if (worldSpace)
        {
            Transform3D transform = mesh.GlobalTransform;
            transform.Origin = origin;
            mesh.GlobalTransform = transform;
        }
        else
        {
            Transform3D transform = mesh.Transform;
            transform.Origin = origin;
            mesh.Transform = transform;
        }
    }

    private static void SetEffectMaterial(MeshInstance3D mesh, Color color, float alpha, float emissionEnergy)
    {
        if (mesh.MaterialOverride is not StandardMaterial3D material)
        {
            return;
        }

        float clampedAlpha = Mathf.Clamp(alpha, 0f, 1f);
        material.AlbedoColor = new Color(color.R, color.G, color.B, clampedAlpha);
        material.Emission = color;
        material.EmissionEnergyMultiplier = Mathf.Max(0f, emissionEnergy);
    }
}