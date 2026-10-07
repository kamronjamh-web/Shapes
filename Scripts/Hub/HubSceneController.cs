using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Managers;
using SuperGatchaRebuild.Shapes;
using SuperGatchaRebuild.Visuals;

namespace SuperGatchaRebuild.Hub;

// Controls the 3D character preview in the hub.
public partial class HubSceneController : Node3D
{
    [Signal] public delegate void SlotClickedEventHandler(int slotIndex);

    private const int HubElectricEnvironmentAnchorCandidateCount = 12;
    private const float HubElectricEnvironmentAnchorMinRadius = 0.55f;
    private const float HubElectricEnvironmentAnchorMaxRadius = 2.65f;
    private const float SlotPickBoundsPaddingPixels = 44f;
    private const float InspectionCameraFov = 50f;
    private const float InspectionCameraZoomDistance = 3.2f;
    private const float InspectionBlendSpeed = 4.8f;
    private const float InspectionCameraLerpSpeed = 8.5f;
    private const float InspectionFocusDepthPadding = 8f;
    private const float InspectionNearFocusTransition = 1.35f;
    private const float InspectionFarFocusTransition = 12.8f;
    private const float InspectionFocusOverlayActivationBlend = 0.985f;

    private sealed class FloatingParticle
    {
        public FloatingParticle(
            MeshInstance3D node,
            Vector3 basePosition,
            Vector3 primaryDrift,
            Vector3 secondaryDrift,
            float phase,
            float speed,
            float baseScale)
        {
            Node = node;
            BasePosition = basePosition;
            PrimaryDrift = primaryDrift;
            SecondaryDrift = secondaryDrift;
            Phase = phase;
            Speed = speed;
            BaseScale = baseScale;
        }

        public MeshInstance3D Node { get; }
        public Vector3 BasePosition { get; }
        public Vector3 PrimaryDrift { get; }
        public Vector3 SecondaryDrift { get; }
        public float Phase { get; }
        public float Speed { get; }
        public float BaseScale { get; }
    }

    private sealed class BackdropMotionRig
    {
        public BackdropMotionRig(
            Node3D node,
            Vector3 basePosition,
            Vector3 baseRotation,
            Vector3 bobOffset,
            Vector3 rotationAxis,
            float translationSpeed,
            float rotationSpeed,
            float phase)
        {
            Node = node;
            BasePosition = basePosition;
            BaseRotation = baseRotation;
            BobOffset = bobOffset;
            RotationAxis = rotationAxis;
            TranslationSpeed = translationSpeed;
            RotationSpeed = rotationSpeed;
            Phase = phase;
        }

        public Node3D Node { get; }
        public Vector3 BasePosition { get; }
        public Vector3 BaseRotation { get; }
        public Vector3 BobOffset { get; }
        public Vector3 RotationAxis { get; }
        public float TranslationSpeed { get; }
        public float RotationSpeed { get; }
        public float Phase { get; }
    }

    private sealed class BackdropPulseLight
    {
        public BackdropPulseLight(OmniLight3D light, float baseEnergy, float amplitude, float speed, float phase)
        {
            Light = light;
            BaseEnergy = baseEnergy;
            Amplitude = amplitude;
            Speed = speed;
            Phase = phase;
        }

        public OmniLight3D Light { get; }
        public float BaseEnergy { get; }
        public float Amplitude { get; }
        public float Speed { get; }
        public float Phase { get; }
    }

    private static readonly Vector3[] SlotLayout =
    {
        new(0.0f, 0.55f, 2.35f),
        new(-2.75f, 0.70f, 1.15f),
        new(2.75f, 0.70f, 1.15f),
        new(-2.25f, 1.55f, -1.45f),
        new(2.25f, 1.55f, -1.45f),
    };

    private static readonly Vector3 SlotFocusPoint = new(0f, 1.35f, 0f);

    [Export] public Node3D? CharacterRoot;
    [Export] public Camera3D? PreviewCamera;
    [Export] public Godot.Collections.Array<NodePath> SlotNodePaths = new();

    [Export] public float CharacterRotateSpeed = 0.24f;
    [Export] public float CharacterBobAmplitude = 0.08f;
    [Export] public float CharacterBobSpeed = 1.35f;
    [Export] public float SlotBobAmplitude = 0.06f;
    [Export] public float SlotBobSpeed = 1.8f;

    private readonly List<FloatingParticle> _floatingParticles = new();
    private readonly List<BackdropMotionRig> _backdropMotionRigs = new();
    private readonly List<BackdropPulseLight> _backdropPulseLights = new();
    private readonly HubEquipmentSlotView?[] _slots = new HubEquipmentSlotView?[5];
    private readonly RandomNumberGenerator _electricAnchorRng = new();
    private readonly Dictionary<VisualInstance3D, uint> _inspectionOverlayOriginalVisualLayers = new();
    private readonly Dictionary<VisualInstance3D, uint> _inspectionOverlayOriginalLightLayers = new();

    private IHubEquipmentProvider? _equipmentProvider;
    private SettingsManager? _settingsManager;
    private CameraAttributesPractical? _inspectionCameraAttributes;
    private CanvasLayer? _inspectionFocusLayer;
    private SubViewportContainer? _inspectionFocusContainer;
    private SubViewport? _inspectionFocusViewport;
    private Camera3D? _inspectionFocusCamera;
    private Vector3 _cameraHomePosition;
    private Vector3 _cameraHomeRotation;
    private float _cameraHomeFov = 49f;
    private uint _previewCameraBaseCullMask = 0xFFFFF;
    private bool _previewCameraCullMaskCaptured;
    private bool _cameraHomeCaptured;
    private bool _weaponInspectionActive;
    private int _focusedInspectionSlotIndex = -1;
    private float _inspectionBlend;
    private float _time;
    private float _characterBaseY;

    public bool IsWeaponInspectionActive => _weaponInspectionActive;
    public Func<InputEvent, bool>? InspectionInputInterceptor { get; set; }

    public bool GetInspectionSocketAnchors(ICollection<HubSocketAnchor> anchors)
    {
        anchors.Clear();
        if (!_weaponInspectionActive)
        {
            return false;
        }

        ResolvePreviewCamera();
        HubEquipmentSlotView? focusedSlot = GetFocusedInspectionSlot();
        if (PreviewCamera == null || focusedSlot == null)
        {
            return false;
        }

        focusedSlot.BuildInspectionSocketAnchors(PreviewCamera, _focusedInspectionSlotIndex, anchors);
        return anchors.Count > 0;
    }

    public void CollectElectricEnvironmentAnchors(Vector3 sourceGlobal, ICollection<Vector3> anchors)
    {
        float stageSurfaceY = GetElectricStageSurfaceY();
        Vector2 sourceXZ = new(sourceGlobal.X, sourceGlobal.Z);
        float baseAngle = _electricAnchorRng.RandfRange(0f, Mathf.Tau);
        for (int anchorIndex = 0; anchorIndex < HubElectricEnvironmentAnchorCandidateCount; anchorIndex++)
        {
            float angle = baseAngle
                + anchorIndex * (Mathf.Tau / HubElectricEnvironmentAnchorCandidateCount)
                + _electricAnchorRng.RandfRange(-0.22f, 0.22f);
            float radius = Mathf.Sqrt(Mathf.Lerp(
                HubElectricEnvironmentAnchorMinRadius * HubElectricEnvironmentAnchorMinRadius,
                HubElectricEnvironmentAnchorMaxRadius * HubElectricEnvironmentAnchorMaxRadius,
                _electricAnchorRng.Randf()));
            Vector2 offset = new(Mathf.Cos(angle), Mathf.Sin(angle));
            AddElectricStageAnchor(anchors, sourceXZ + offset * radius, stageSurfaceY);
        }
    }

    public void CollectElectricCharacterAnchors(ICollection<Vector3> anchors)
    {
        if (CharacterRoot != null && IsInstanceValid(CharacterRoot))
        {
            Vector3 characterOrigin = CharacterRoot.GlobalTransform.Origin;
            anchors.Add(characterOrigin + new Vector3(0f, -0.18f, 0.18f));
            anchors.Add(characterOrigin + new Vector3(0f, 0.82f, 0.08f));
            anchors.Add(characterOrigin + new Vector3(0.18f, 0.34f, 0f));
        }
    }

    public ModifierVisualController.ElectricEnvironmentHit? ResolveElectricEnvironmentCollision(Vector3 fromGlobal, Vector3 toGlobal)
    {
        float deltaY = toGlobal.Y - fromGlobal.Y;
        if (Mathf.Abs(deltaY) < 0.0001f)
        {
            return null;
        }

        float stageSurfaceY = GetElectricStageSurfaceY();
        float t = (stageSurfaceY - fromGlobal.Y) / deltaY;
        if (t < 0f || t > 1f)
        {
            return null;
        }

        Vector3 hitPosition = fromGlobal.Lerp(toGlobal, t);
        if (new Vector2(hitPosition.X, hitPosition.Z).Length() > 4.95f)
        {
            return null;
        }

        return new ModifierVisualController.ElectricEnvironmentHit(hitPosition, Vector3.Up);
    }

    private static void AddElectricStageAnchor(ICollection<Vector3> anchors, Vector2 desiredXZ, float stageSurfaceY)
    {
        Vector2 clamped = desiredXZ.Length() > 4.35f
            ? desiredXZ.Normalized() * 4.35f
            : desiredXZ;
        anchors.Add(new Vector3(clamped.X, stageSurfaceY + 0.02f, clamped.Y));
    }

    private float GetElectricStageSurfaceY()
    {
        Node3D? stage = GetNodeOrNull<Node3D>("LegacyStage");
        return (stage != null && IsInstanceValid(stage) ? stage.GlobalTransform.Origin.Y : GlobalTransform.Origin.Y) + 0.09f;
    }

    public override void _Ready()
    {
        _electricAnchorRng.Randomize();
        ApplyLegacyPreviewVisuals();
        CaptureCameraHome();
        ResolveSettingsManager();

        if (CharacterRoot != null)
        {
            _characterBaseY = CharacterRoot.Position.Y;
        }
        CacheSlots();
        WireSlotSignals();
        ApplySlotLayout();
        RefreshSlots();
    }

    public override void _ExitTree()
    {
        if (_equipmentProvider != null)
        {
            _equipmentProvider.EquipmentChanged -= OnEquipmentChanged;
            _equipmentProvider = null;
        }

        if (_settingsManager != null)
        {
            _settingsManager.GraphicsSettingsChanged -= OnGraphicsSettingsChanged;
        }

        if (PreviewCamera != null)
        {
            RestorePreviewCameraCullMask();
            PreviewCamera.Attributes = null;
        }
    }

    public override void _Process(double delta)
    {
        float deltaSeconds = (float)delta;
        _time += deltaSeconds;
        ShapeVisualFactory.PumpShapeVisualSettingsHotReload();

        TickInspectionCamera(deltaSeconds);
        AnimateCharacter(deltaSeconds);
        AnimateSlots();
        AnimateBackdropMotion();
        AnimateFloatingParticles();
        UpdateInspectionFocusOverlay();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (HandleWeaponInspectionInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
        {
            return;
        }

        if (TryPickSlotAtViewportPosition(mb.Position, out int slotIndex))
        {
            ActivateSlotFromClick(slotIndex);
            GetViewport().SetInputAsHandled();
        }
    }

    public bool TryPickSlotAtViewportPosition(Vector2 viewportPosition, out int slotIndex)
    {
        CacheSlots();

        slotIndex = -1;
        if (PreviewCamera == null || _weaponInspectionActive)
        {
            return false;
        }

        float bestDistanceSquared = float.MaxValue;
        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot == null || !slot.TryGetProjectedPickBounds(PreviewCamera, out Rect2 pickBounds))
            {
                continue;
            }

            pickBounds = pickBounds.Grow(SlotPickBoundsPaddingPixels);
            if (!pickBounds.HasPoint(viewportPosition))
            {
                continue;
            }

            float distanceSquared = viewportPosition.DistanceSquaredTo(pickBounds.GetCenter());
            if (distanceSquared > bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            slotIndex = slot.SlotIndex;
        }

        if (slotIndex >= 0)
        {
            return true;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot == null || !slot.IsHovered || !slot.Visible)
            {
                continue;
            }

            slotIndex = slot.SlotIndex;
            return true;
        }

        return false;
    }

    public bool BeginWeaponInspection(int slotIndex)
    {
        ResolvePreviewCamera();
        CacheSlots();
        if (slotIndex < 0 || slotIndex >= _slots.Length)
        {
            return false;
        }

        HubEquipmentSlotView? slot = _slots[slotIndex];
        if (slot == null || !slot.HasItem)
        {
            return false;
        }

        _weaponInspectionActive = true;
        _focusedInspectionSlotIndex = slotIndex;
        SetProcess(true);
        PreviewCamera?.MakeCurrent();
        SetSlotInspectionModes();
        return true;
    }

    public void EndWeaponInspection()
    {
        if (!_weaponInspectionActive && _inspectionBlend <= 0.001f)
        {
            return;
        }

        _weaponInspectionActive = false;
        ClearInspectionFocusOverlay();
        SetSlotInspectionModes();
    }

    public void PrepareForSceneTransition()
    {
        EndWeaponInspection();
        ClearInspectionFocusOverlay();
        RestorePreviewCameraCullMask();

        if (_inspectionFocusLayer != null && IsInstanceValid(_inspectionFocusLayer))
        {
            _inspectionFocusLayer.QueueFree();
        }

        _inspectionFocusLayer = null;
        _inspectionFocusContainer = null;
        _inspectionFocusViewport = null;
        _inspectionFocusCamera = null;

        CacheSlots();
        for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
        {
            HubEquipmentSlotView? slot = _slots[slotIndex];
            if (slot == null)
            {
                continue;
            }

            slot.PrepareForSceneTransition();
        }

        SetProcess(false);
    }

    public bool RotateWeaponInspection(Vector2 mouseDelta)
    {
        if (!_weaponInspectionActive || mouseDelta.LengthSquared() <= 0.0001f)
        {
            return false;
        }

        HubEquipmentSlotView? focusedSlot = GetFocusedInspectionSlot();
        if (focusedSlot == null)
        {
            return false;
        }

        focusedSlot.RotateInspection(mouseDelta);
        return true;
    }

    public bool HandleWeaponInspectionInput(InputEvent @event)
    {
        if (!_weaponInspectionActive)
        {
            return false;
        }

        if (InspectionInputInterceptor != null && InspectionInputInterceptor(@event))
        {
            return true;
        }

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            EndWeaponInspection();
            return true;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                EndWeaponInspection();
                return true;
            }
        }

        return false;
    }

    private void ApplyLegacyPreviewVisuals()
    {
        if (PreviewCamera != null)
        {
            PreviewCamera.Position = new Vector3(0f, 4.05f, 10.4f);
            PreviewCamera.RotationDegrees = new Vector3(-19f, 0f, 0f);
            PreviewCamera.Fov = 49f;
        }

        DirectionalLight3D? keyLight = GetNodeOrNull<DirectionalLight3D>("KeyLight");
        if (keyLight != null)
        {
            keyLight.RotationDegrees = new Vector3(-49f, 32f, 0f);
            keyLight.LightEnergy = 1.68f;
            keyLight.LightColor = new Color(0.72f, 0.90f, 0.98f, 1f);
            keyLight.ShadowEnabled = true;
        }

        OmniLight3D? fillLight = GetNodeOrNull<OmniLight3D>("FillLight");
        if (fillLight != null)
        {
            fillLight.Position = new Vector3(0f, 2.8f, 4.2f);
            fillLight.LightEnergy = 1.85f;
            fillLight.OmniRange = 14f;
            fillLight.LightColor = new Color(0.12f, 0.72f, 0.66f, 1f);
        }

        WorldEnvironment? worldEnvironment = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (worldEnvironment?.Environment != null)
        {
            worldEnvironment.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            worldEnvironment.Environment.BackgroundColor = new Color(0.008f, 0.018f, 0.027f, 1f);
            worldEnvironment.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            worldEnvironment.Environment.AmbientLightColor = new Color(0.055f, 0.125f, 0.135f, 1f);
            worldEnvironment.Environment.AmbientLightEnergy = 0.72f;
            worldEnvironment.Environment.GlowEnabled = true;
        }

        BuildLegacyStage();
        BuildLegacyCharacter();
    }

    private void ResolveSettingsManager()
    {
        _settingsManager = SettingsManager.Resolve(this);
        if (_settingsManager == null)
        {
            return;
        }

        _settingsManager.GraphicsSettingsChanged -= OnGraphicsSettingsChanged;
        _settingsManager.GraphicsSettingsChanged += OnGraphicsSettingsChanged;
        ApplyGraphicsSettings(_settingsManager.GraphicsSettings);
    }

    private void OnGraphicsSettingsChanged(GraphicsSettingsData settings)
    {
        ApplyGraphicsSettings(settings);
    }

    private void ApplyGraphicsSettings(GraphicsSettingsData settings)
    {
        Viewport? viewport = GetViewport();
        if (viewport != null)
        {
            viewport.Scaling3DScale = settings.RenderScalePercent / 100f;
        }

        SettingsManager.ApplyGraphicsSettingsToBranch(this, settings);

        WorldEnvironment? worldEnvironment = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (worldEnvironment?.Environment != null)
        {
            worldEnvironment.Environment.GlowEnabled = settings.GlowEnabled;
        }

        DirectionalLight3D? keyLight = GetNodeOrNull<DirectionalLight3D>("KeyLight");
        if (keyLight != null)
        {
            keyLight.ShadowEnabled = settings.ShadowsEnabled;
        }
    }

    private void BuildLegacyStage()
    {
        Node3D stage = GetNodeOrNull<Node3D>("LegacyStage") ?? new Node3D { Name = "LegacyStage" };
        if (stage.GetParent() != this)
        {
            AddChild(stage);
            MoveChild(stage, 0);
        }

        MeshInstance3D platform = GetOrCreateMesh(stage, "PlatformDisk");
        platform.Mesh = new CylinderMesh
        {
            TopRadius = 4.95f,
            BottomRadius = 4.95f,
            Height = 0.18f,
            RadialSegments = 28,
        };
        platform.Position = new Vector3(0f, 0f, 0f);
        platform.MaterialOverride = CreateLegacyMaterial(
            new Color(0.030f, 0.050f, 0.060f, 0.98f),
            new Color(0.014f, 0.035f, 0.048f, 1f),
            0.08f,
            true);
        platform.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        MeshInstance3D outerRing = GetOrCreateMesh(stage, "PlatformOuterRing");
        outerRing.Mesh = new CylinderMesh
        {
            TopRadius = 4.55f,
            BottomRadius = 4.55f,
            Height = 0.035f,
            RadialSegments = 80,
        };
        outerRing.Position = new Vector3(0f, 0.13f, 0f);
        outerRing.MaterialOverride = CreateLegacyMaterial(
            new Color(0.08f, 0.46f, 0.44f, 0.44f),
            new Color(0.12f, 0.78f, 0.72f, 1f),
            0.55f,
            true);
        outerRing.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        MeshInstance3D ringCutout = GetOrCreateMesh(stage, "PlatformRingCutout");
        ringCutout.Mesh = new CylinderMesh
        {
            TopRadius = 4.40f,
            BottomRadius = 4.40f,
            Height = 0.04f,
            RadialSegments = 80,
        };
        ringCutout.Position = new Vector3(0f, 0.16f, 0f);
        ringCutout.MaterialOverride = CreateLegacyMaterial(
            new Color(0.026f, 0.040f, 0.048f, 0.98f),
            new Color(0.012f, 0.022f, 0.028f, 1f),
            0.08f,
            true);
        ringCutout.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        MeshInstance3D conduitRing = GetOrCreateMesh(stage, "PlatformConduitRing");
        conduitRing.Mesh = new TorusMesh
        {
            InnerRadius = 4.10f,
            OuterRadius = 4.28f,
            Rings = 72,
            RingSegments = 12,
        };
        conduitRing.Position = new Vector3(0f, 0.19f, 0f);
        conduitRing.RotationDegrees = Vector3.Zero;
        conduitRing.MaterialOverride = CreateMachineMaterial(
            new Color(0.10f, 0.38f, 0.38f, 0.82f),
            new Color(0.10f, 0.72f, 0.68f, 1f),
            0.35f,
            0.22f,
            0.42f,
            true);
        conduitRing.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        MeshInstance3D warningRing = GetOrCreateMesh(stage, "PlatformWarningRing");
        warningRing.Mesh = new TorusMesh
        {
            InnerRadius = 2.85f,
            OuterRadius = 2.98f,
            Rings = 64,
            RingSegments = 10,
        };
        warningRing.Position = new Vector3(0f, 0.185f, 0f);
        warningRing.RotationDegrees = Vector3.Zero;
        warningRing.MaterialOverride = CreateMachineMaterial(
            new Color(0.38f, 0.20f, 0.06f, 0.82f),
            new Color(0.98f, 0.58f, 0.18f, 1f),
            0.48f,
            0.10f,
            0.58f,
            true);
        warningRing.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        BuildLegacyMachineBackdrop(stage);
    }

    private void CaptureCameraHome()
    {
        ResolvePreviewCamera();
        if (PreviewCamera == null)
        {
            return;
        }

        ClearInspectionFocusOverlay();
        CapturePreviewCameraCullMask();

        _cameraHomePosition = PreviewCamera.Position;
        _cameraHomeRotation = PreviewCamera.Rotation;
        _cameraHomeFov = PreviewCamera.Fov;
        _cameraHomeCaptured = true;
    }

    private void TickInspectionCamera(float deltaSeconds)
    {
        ResolvePreviewCamera();
        if (PreviewCamera == null)
        {
            return;
        }

        if (!_cameraHomeCaptured)
        {
            CaptureCameraHome();
        }

        float targetBlend = _weaponInspectionActive ? 1f : 0f;
        _inspectionBlend = Mathf.MoveToward(_inspectionBlend, targetBlend, deltaSeconds * InspectionBlendSpeed);

        HubEquipmentSlotView? focusedSlot = GetFocusedInspectionSlot();
        float lerp = 1f - Mathf.Exp(-InspectionCameraLerpSpeed * deltaSeconds);
        if (_inspectionBlend > 0.001f && focusedSlot != null)
        {
            Vector3 focusPoint = focusedSlot.GlobalPosition + new Vector3(0f, 0.08f, 0f);
            Vector3 desiredPosition = focusPoint + new Vector3(0f, 0.32f, InspectionCameraZoomDistance);
            Transform3D desiredTransform = PreviewCamera.GlobalTransform;
            desiredTransform.Origin = desiredPosition;
            desiredTransform = desiredTransform.LookingAt(focusPoint, Vector3.Up);
            PreviewCamera.GlobalTransform = PreviewCamera.GlobalTransform.InterpolateWith(desiredTransform, lerp);
            PreviewCamera.Fov = Mathf.Lerp(PreviewCamera.Fov, InspectionCameraFov, lerp);
        }
        else
        {
            PreviewCamera.Position = PreviewCamera.Position.Lerp(_cameraHomePosition, lerp);
            PreviewCamera.Rotation = LerpEulerAngles(PreviewCamera.Rotation, _cameraHomeRotation, lerp);
            PreviewCamera.Fov = Mathf.Lerp(PreviewCamera.Fov, _cameraHomeFov, lerp);
        }

        UpdateInspectionDepthOfField();
        if (!_weaponInspectionActive && _inspectionBlend <= 0.001f)
        {
            _focusedInspectionSlotIndex = -1;
            SetSlotInspectionModes();
        }
    }

    private void UpdateInspectionDepthOfField()
    {
        ResolvePreviewCamera();
        if (PreviewCamera == null)
        {
            return;
        }

        if (_inspectionBlend <= 0.03f)
        {
            PreviewCamera.Attributes = null;
            return;
        }

        HubEquipmentSlotView? focusedSlot = GetFocusedInspectionSlot();
        Vector3 focusPoint = focusedSlot?.GlobalPosition + new Vector3(0f, 0.08f, 0f)
            ?? PreviewCamera.GlobalPosition + (-PreviewCamera.GlobalTransform.Basis.Z * InspectionCameraZoomDistance);
        float focusDistance = Mathf.Max(0.25f, PreviewCamera.GlobalPosition.DistanceTo(focusPoint));
        float farDistance = focusDistance + InspectionFocusDepthPadding;

        _inspectionCameraAttributes ??= new CameraAttributesPractical();
        _inspectionCameraAttributes.Set("dof_blur_far_enabled", true);
        _inspectionCameraAttributes.Set("dof_blur_far_distance", Mathf.Lerp(8f, farDistance, _inspectionBlend));
        _inspectionCameraAttributes.Set("dof_blur_far_transition", Mathf.Lerp(7f, InspectionFarFocusTransition, _inspectionBlend));
        _inspectionCameraAttributes.Set("dof_blur_near_enabled", false);
        PreviewCamera.Attributes = _inspectionCameraAttributes;
    }

    private void ResolvePreviewCamera()
    {
        if (PreviewCamera != null && IsInstanceValid(PreviewCamera))
        {
            return;
        }

        PreviewCamera = GetNodeOrNull<Camera3D>("PreviewCamera");
    }

    private static Vector3 LerpEulerAngles(Vector3 from, Vector3 to, float weight)
    {
        return new Vector3(
            Mathf.LerpAngle(from.X, to.X, weight),
            Mathf.LerpAngle(from.Y, to.Y, weight),
            Mathf.LerpAngle(from.Z, to.Z, weight));
    }

    private void BuildLegacyMachineBackdrop(Node3D stage)
    {
        Node3D backdrop = stage.GetNodeOrNull<Node3D>("MachineBackdrop") ?? new Node3D { Name = "MachineBackdrop" };
        if (backdrop.GetParent() != stage)
        {
            stage.AddChild(backdrop);
        }

        _floatingParticles.Clear();
        _backdropMotionRigs.Clear();
        _backdropPulseLights.Clear();

        if (backdrop.GetChildCount() > 0)
        {
            CollectBackdropAnimationNodes(backdrop);
            return;
        }

        StandardMaterial3D darkMetalMaterial = CreateMachineMaterial(
            new Color(0.055f, 0.080f, 0.094f, 1f),
            new Color(0.010f, 0.022f, 0.030f, 1f),
            0.05f,
            0.28f,
            0.58f,
            false);
        StandardMaterial3D steelMaterial = CreateMachineMaterial(
            new Color(0.090f, 0.120f, 0.132f, 1f),
            new Color(0.012f, 0.024f, 0.032f, 1f),
            0.06f,
            0.34f,
            0.46f,
            false);
        StandardMaterial3D tealStripMaterial = CreateMachineMaterial(
            new Color(0.090f, 0.285f, 0.290f, 0.88f),
            new Color(0.15f, 0.92f, 0.85f, 1f),
            0.95f,
            0.14f,
            0.34f,
            true);
        StandardMaterial3D amberStripMaterial = CreateMachineMaterial(
            new Color(0.34f, 0.18f, 0.05f, 0.90f),
            new Color(1.0f, 0.60f, 0.18f, 1f),
            1.05f,
            0.08f,
            0.46f,
            true);

        MeshInstance3D rearWall = GetOrCreateMesh(backdrop, "RearWall");
        rearWall.Mesh = new BoxMesh { Size = new Vector3(15.2f, 9.4f, 0.28f) };
        rearWall.Position = new Vector3(0f, 4.1f, -8.6f);
        rearWall.MaterialOverride = darkMetalMaterial;
        rearWall.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        MeshInstance3D rearAperture = GetOrCreateMesh(backdrop, "RearApertureFrame");
        rearAperture.Mesh = new TorusMesh
        {
            InnerRadius = 1.95f,
            OuterRadius = 2.48f,
            Rings = 56,
            RingSegments = 12,
        };
        rearAperture.Position = new Vector3(0f, 3.35f, -6.45f);
        rearAperture.RotationDegrees = new Vector3(90f, 0f, 0f);
        rearAperture.MaterialOverride = steelMaterial;
        rearAperture.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        MeshInstance3D rearApertureGlow = GetOrCreateMesh(backdrop, "RearApertureGlow");
        rearApertureGlow.Mesh = new TorusMesh
        {
            InnerRadius = 2.20f,
            OuterRadius = 2.34f,
            Rings = 56,
            RingSegments = 8,
        };
        rearApertureGlow.Position = new Vector3(0f, 3.35f, -6.25f);
        rearApertureGlow.RotationDegrees = new Vector3(90f, 0f, 0f);
        rearApertureGlow.MaterialOverride = tealStripMaterial;
        rearApertureGlow.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            float side = sideIndex == 0 ? -1f : 1f;

            MeshInstance3D outerPylon = GetOrCreateMesh(backdrop, $"OuterPylon{sideIndex}");
            outerPylon.Mesh = new BoxMesh { Size = new Vector3(1.10f, 7.8f, 1.05f) };
            outerPylon.Position = new Vector3(side * 6.1f, 3.65f, -5.75f);
            outerPylon.MaterialOverride = darkMetalMaterial;
            outerPylon.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

            MeshInstance3D innerPylon = GetOrCreateMesh(backdrop, $"InnerPylon{sideIndex}");
            innerPylon.Mesh = new BoxMesh { Size = new Vector3(0.76f, 6.1f, 0.82f) };
            innerPylon.Position = new Vector3(side * 3.95f, 3.10f, -5.05f);
            innerPylon.MaterialOverride = steelMaterial;
            innerPylon.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

            MeshInstance3D lightStrip = GetOrCreateMesh(backdrop, $"PylonLightStrip{sideIndex}");
            lightStrip.Mesh = new BoxMesh { Size = new Vector3(0.18f, 4.25f, 0.10f) };
            lightStrip.Position = new Vector3(side * 5.62f, 3.75f, -5.18f);
            lightStrip.MaterialOverride = tealStripMaterial;
            lightStrip.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            MeshInstance3D warningStrip = GetOrCreateMesh(backdrop, $"PylonWarningStrip{sideIndex}");
            warningStrip.Mesh = new BoxMesh { Size = new Vector3(0.16f, 1.45f, 0.08f) };
            warningStrip.Position = new Vector3(side * 3.52f, 1.45f, -4.66f);
            warningStrip.MaterialOverride = amberStripMaterial;
            warningStrip.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            OmniLight3D beaconLight = GetNodeOrNull<OmniLight3D>($"BeaconLight{sideIndex}") ?? new OmniLight3D { Name = $"BeaconLight{sideIndex}" };
            if (beaconLight.GetParent() != backdrop)
            {
                backdrop.AddChild(beaconLight);
            }

            beaconLight.Position = new Vector3(side * 4.95f, 2.15f, -4.78f);
            beaconLight.LightColor = new Color(1.0f, 0.58f, 0.16f, 1f);
            beaconLight.LightEnergy = 0.70f;
            beaconLight.OmniRange = 3.0f;
            beaconLight.ShadowEnabled = false;
            RegisterBackdropPulseLight(beaconLight, 0.70f, 0.28f, 1.1f + sideIndex * 0.12f, sideIndex * 1.1f);

            Node3D armRoot = GetNodeOrNull<Node3D>($"MachineArmRoot{sideIndex}") ?? new Node3D { Name = $"MachineArmRoot{sideIndex}" };
            if (armRoot.GetParent() != backdrop)
            {
                backdrop.AddChild(armRoot);
            }

            armRoot.Position = new Vector3(side * 4.8f, 4.75f, -4.6f);
            armRoot.RotationDegrees = new Vector3(side * 8f, side < 0f ? 18f : -18f, side * 7f);
            RegisterBackdropMotionRig(
                armRoot,
                armRoot.Position,
                armRoot.Rotation,
                new Vector3(0f, 0.18f, 0.08f),
                new Vector3(0.12f, 1f, 0.04f),
                0.32f + sideIndex * 0.06f,
                side < 0f ? 0.18f : -0.15f,
                sideIndex * 1.7f);

            MeshInstance3D armSegmentA = GetOrCreateMesh(armRoot, $"ArmSegmentA{sideIndex}");
            armSegmentA.Mesh = new BoxMesh { Size = new Vector3(0.22f, 1.60f, 0.24f) };
            armSegmentA.Position = new Vector3(0f, -0.70f, 0f);
            armSegmentA.MaterialOverride = steelMaterial;
            armSegmentA.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

            MeshInstance3D armSegmentB = GetOrCreateMesh(armRoot, $"ArmSegmentB{sideIndex}");
            armSegmentB.Mesh = new BoxMesh { Size = new Vector3(1.45f, 0.18f, 0.22f) };
            armSegmentB.Position = new Vector3(side * -0.66f, -1.42f, 0.08f);
            armSegmentB.MaterialOverride = steelMaterial;
            armSegmentB.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

            MeshInstance3D armTip = GetOrCreateMesh(armRoot, $"ArmTip{sideIndex}");
            armTip.Mesh = new BoxMesh { Size = new Vector3(0.40f, 0.44f, 0.34f) };
            armTip.Position = new Vector3(side * -1.28f, -1.48f, 0.10f);
            armTip.MaterialOverride = amberStripMaterial;
            armTip.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        MeshInstance3D upperBeam = GetOrCreateMesh(backdrop, "UpperBeam");
        upperBeam.Mesh = new BoxMesh { Size = new Vector3(10.2f, 0.34f, 0.52f) };
        upperBeam.Position = new Vector3(0f, 6.82f, -5.9f);
        upperBeam.MaterialOverride = steelMaterial;
        upperBeam.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        MeshInstance3D lowerConduit = GetOrCreateMesh(backdrop, "LowerConduit");
        lowerConduit.Mesh = new BoxMesh { Size = new Vector3(8.8f, 0.20f, 0.26f) };
        lowerConduit.Position = new Vector3(0f, 1.05f, -5.15f);
        lowerConduit.MaterialOverride = tealStripMaterial;
        lowerConduit.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        Node3D rotorRoot = GetNodeOrNull<Node3D>("RearRotorRoot") ?? new Node3D { Name = "RearRotorRoot" };
        if (rotorRoot.GetParent() != backdrop)
        {
            backdrop.AddChild(rotorRoot);
        }

        rotorRoot.Position = new Vector3(0f, 3.4f, -6.2f);
        rotorRoot.RotationDegrees = new Vector3(90f, 0f, 0f);
        RegisterBackdropMotionRig(
            rotorRoot,
            rotorRoot.Position,
            rotorRoot.Rotation,
            new Vector3(0f, 0.06f, 0f),
            Vector3.Forward,
            0.22f,
            0.24f,
            0.8f);

        MeshInstance3D rotorOuter = GetOrCreateMesh(rotorRoot, "RotorOuter");
        rotorOuter.Mesh = new TorusMesh
        {
            InnerRadius = 2.55f,
            OuterRadius = 2.78f,
            Rings = 64,
            RingSegments = 10,
        };
        rotorOuter.MaterialOverride = darkMetalMaterial;
        rotorOuter.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        MeshInstance3D rotorInner = GetOrCreateMesh(rotorRoot, "RotorInner");
        rotorInner.Mesh = new TorusMesh
        {
            InnerRadius = 1.50f,
            OuterRadius = 1.68f,
            Rings = 48,
            RingSegments = 8,
        };
        rotorInner.MaterialOverride = amberStripMaterial;
        rotorInner.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        Node3D counterRotorRoot = GetNodeOrNull<Node3D>("CounterRotorRoot") ?? new Node3D { Name = "CounterRotorRoot" };
        if (counterRotorRoot.GetParent() != backdrop)
        {
            backdrop.AddChild(counterRotorRoot);
        }

        counterRotorRoot.Position = new Vector3(0f, 3.4f, -6.0f);
        counterRotorRoot.RotationDegrees = new Vector3(90f, 0f, 18f);
        RegisterBackdropMotionRig(
            counterRotorRoot,
            counterRotorRoot.Position,
            counterRotorRoot.Rotation,
            new Vector3(0f, 0.05f, 0f),
            Vector3.Forward,
            0.18f,
            -0.17f,
            2.1f);

        MeshInstance3D counterRotor = GetOrCreateMesh(counterRotorRoot, "CounterRotor");
        counterRotor.Mesh = new TorusMesh
        {
            InnerRadius = 2.05f,
            OuterRadius = 2.18f,
            Rings = 52,
            RingSegments = 8,
        };
        counterRotor.MaterialOverride = tealStripMaterial;
        counterRotor.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        for (int i = 0; i < 24; i++)
        {
            float angle = i * (Mathf.Tau / 24f);
            float radius = 4.9f + (i % 3) * 0.34f;
            float sideSkew = (i % 2 == 0 ? -1f : 1f) * 0.45f;
            Vector3 basePosition = new(
                Mathf.Cos(angle) * radius * 1.1f,
                1.6f + (i % 6) * 0.48f,
                -6.4f + Mathf.Sin(angle) * 1.05f + sideSkew);
            float baseScale = 0.70f + (i % 4) * 0.10f;

            MeshInstance3D indicator = new()
            {
                Name = $"IndicatorParticle{i:00}",
                Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.018f, 0.06f) },
                Position = basePosition,
                Scale = Vector3.One * baseScale,
                MaterialOverride = i % 5 == 0 ? amberStripMaterial : tealStripMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            backdrop.AddChild(indicator);
            RegisterFloatingParticle(indicator, i, basePosition, baseScale);
        }
    }

    private void CollectBackdropAnimationNodes(Node3D backdrop)
    {
        int indicatorIndex = 0;
        foreach (Node child in backdrop.GetChildren())
        {
            switch (child)
            {
                case MeshInstance3D particle when child.Name.ToString().StartsWith("IndicatorParticle", StringComparison.Ordinal):
                    RegisterFloatingParticle(particle, indicatorIndex, particle.Position, particle.Scale.X <= 0f ? 1f : particle.Scale.X);
                    indicatorIndex++;
                    break;

                case Node3D motionRoot when child.Name.ToString().StartsWith("MachineArmRoot", StringComparison.Ordinal):
                {
                    int sideIndex = motionRoot.Name.ToString().EndsWith("1", StringComparison.Ordinal) ? 1 : 0;
                    float side = sideIndex == 0 ? -1f : 1f;
                    RegisterBackdropMotionRig(
                        motionRoot,
                        motionRoot.Position,
                        motionRoot.Rotation,
                        new Vector3(0f, 0.18f, 0.08f),
                        new Vector3(0.12f, 1f, 0.04f),
                        0.32f + sideIndex * 0.06f,
                        side < 0f ? 0.18f : -0.15f,
                        sideIndex * 1.7f);
                    break;
                }

                case Node3D motionRoot when child.Name == "RearRotorRoot":
                    RegisterBackdropMotionRig(motionRoot, motionRoot.Position, motionRoot.Rotation, new Vector3(0f, 0.06f, 0f), Vector3.Forward, 0.22f, 0.24f, 0.8f);
                    break;

                case Node3D motionRoot when child.Name == "CounterRotorRoot":
                    RegisterBackdropMotionRig(motionRoot, motionRoot.Position, motionRoot.Rotation, new Vector3(0f, 0.05f, 0f), Vector3.Forward, 0.18f, -0.17f, 2.1f);
                    break;

                case OmniLight3D pulseLight when child.Name.ToString().StartsWith("BeaconLight", StringComparison.Ordinal):
                {
                    int sideIndex = child.Name.ToString().EndsWith("1", StringComparison.Ordinal) ? 1 : 0;
                    RegisterBackdropPulseLight(pulseLight, 0.70f, 0.28f, 1.1f + sideIndex * 0.12f, sideIndex * 1.1f);
                    break;
                }
            }
        }
    }

    private void RegisterFloatingParticle(MeshInstance3D particle, int index, Vector3 basePosition, float baseScale)
    {
        float primaryAngle = index * 1.6180339f;
        float secondaryAngle = index * 2.4142135f + 0.45f;
        float primaryRadius = 0.10f + (index % 7) * 0.018f;
        float secondaryRadius = 0.04f + (index % 5) * 0.014f;

        Vector3 primaryDrift = new(
            Mathf.Cos(primaryAngle) * primaryRadius,
            0.08f + (index % 6) * 0.025f,
            Mathf.Sin(primaryAngle) * primaryRadius);
        Vector3 secondaryDrift = new(
            Mathf.Cos(secondaryAngle) * secondaryRadius,
            0.03f + (index % 4) * 0.018f,
            Mathf.Sin(secondaryAngle) * secondaryRadius);

        _floatingParticles.Add(new FloatingParticle(
            particle,
            basePosition,
            primaryDrift,
            secondaryDrift,
            index * 0.73f,
            0.34f + (index % 11) * 0.045f,
            baseScale));
    }

    private void RegisterBackdropMotionRig(Node3D node, Vector3 basePosition, Vector3 baseRotation, Vector3 bobOffset, Vector3 rotationAxis, float translationSpeed, float rotationSpeed, float phase)
    {
        _backdropMotionRigs.Add(new BackdropMotionRig(
            node,
            basePosition,
            baseRotation,
            bobOffset,
            rotationAxis.LengthSquared() > 0.0001f ? rotationAxis.Normalized() : Vector3.Up,
            translationSpeed,
            rotationSpeed,
            phase));
    }

    private void RegisterBackdropPulseLight(OmniLight3D light, float baseEnergy, float amplitude, float speed, float phase)
    {
        _backdropPulseLights.Add(new BackdropPulseLight(light, baseEnergy, amplitude, speed, phase));
    }

    private void AnimateBackdropMotion()
    {
        for (int index = _backdropMotionRigs.Count - 1; index >= 0; index--)
        {
            BackdropMotionRig rig = _backdropMotionRigs[index];
            if (!IsInstanceValid(rig.Node))
            {
                _backdropMotionRigs.RemoveAt(index);
                continue;
            }

            float phaseTime = _time * rig.TranslationSpeed + rig.Phase;
            Vector3 offset = rig.BobOffset * Mathf.Sin(phaseTime);
            rig.Node.Position = rig.BasePosition + offset;
            rig.Node.Rotation = rig.BaseRotation + rig.RotationAxis * Mathf.Sin(_time * rig.RotationSpeed + rig.Phase) * 0.18f;
        }

        for (int index = _backdropPulseLights.Count - 1; index >= 0; index--)
        {
            BackdropPulseLight pulse = _backdropPulseLights[index];
            if (!IsInstanceValid(pulse.Light))
            {
                _backdropPulseLights.RemoveAt(index);
                continue;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(_time * pulse.Speed + pulse.Phase);
            pulse.Light.LightEnergy = pulse.BaseEnergy + pulse.Amplitude * wave;
        }
    }

    private void AnimateFloatingParticles()
    {
        for (int index = 0; index < _floatingParticles.Count; index++)
        {
            FloatingParticle particle = _floatingParticles[index];
            float phaseTime = _time * particle.Speed + particle.Phase;
            float secondaryTime = _time * (particle.Speed * 0.67f + 0.15f) + particle.Phase * 1.7f;

            Vector3 offset = particle.PrimaryDrift * Mathf.Sin(phaseTime)
                + particle.SecondaryDrift * Mathf.Cos(secondaryTime);
            particle.Node.Position = particle.BasePosition + offset;

            float pulse = particle.BaseScale * (0.86f + 0.14f * Mathf.Sin(phaseTime * 1.9f));
            particle.Node.Scale = Vector3.One * pulse;
        }
    }

    private void BuildLegacyCharacter()
    {
        if (CharacterRoot == null)
        {
            return;
        }

        CharacterRoot.Position = new Vector3(0f, 2.28f, 0.05f);
        CharacterRoot.Scale = Vector3.One;

        MeshInstance3D? originalMesh = CharacterRoot.GetNodeOrNull<MeshInstance3D>("CharacterMesh");
        if (originalMesh != null)
        {
            originalMesh.Visible = false;
        }

        StandardMaterial3D upperMaterial = CreateLegacyMaterial(
            new Color(0.0f, 0.94f, 0.73f, 1f),
            new Color(0.0f, 0.95f, 0.75f, 1f),
            0.55f,
            false);
        StandardMaterial3D lowerMaterial = CreateLegacyMaterial(
            new Color(0.0f, 0.57f, 0.31f, 1f),
            new Color(0.0f, 0.72f, 0.46f, 1f),
            0.35f,
            false);

        MeshInstance3D gemTop = GetOrCreateMesh(CharacterRoot, "LegacyGemTop");
        gemTop.Mesh = new CylinderMesh
        {
            TopRadius = 1.18f,
            BottomRadius = 1.45f,
            Height = 0.55f,
            RadialSegments = 8,
        };
        gemTop.Position = new Vector3(0f, 0.72f, 0f);
        gemTop.MaterialOverride = upperMaterial;
        gemTop.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        MeshInstance3D gemBottom = GetOrCreateMesh(CharacterRoot, "LegacyGemBottom");
        gemBottom.Mesh = new CylinderMesh
        {
            TopRadius = 1.22f,
            BottomRadius = 0.0f,
            Height = 2.35f,
            RadialSegments = 8,
        };
        gemBottom.Position = new Vector3(0f, -0.72f, 0f);
        gemBottom.MaterialOverride = lowerMaterial;
        gemBottom.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

        OmniLight3D glow = CharacterRoot.GetNodeOrNull<OmniLight3D>("LegacyGemGlow") ?? new OmniLight3D { Name = "LegacyGemGlow" };
        if (glow.GetParent() != CharacterRoot)
        {
            CharacterRoot.AddChild(glow);
        }

        glow.Position = new Vector3(0f, 0.25f, 0.4f);
        glow.LightColor = new Color(0f, 0.95f, 0.70f, 1f);
        glow.LightEnergy = 2.0f;
        glow.OmniRange = 5.0f;
        glow.ShadowEnabled = false;
    }

    private static MeshInstance3D GetOrCreateMesh(Node parent, string name)
    {
        MeshInstance3D mesh = parent.GetNodeOrNull<MeshInstance3D>(name) ?? new MeshInstance3D { Name = name };
        if (mesh.GetParent() != parent)
        {
            parent.AddChild(mesh);
        }

        return mesh;
    }

    private static StandardMaterial3D CreateLegacyMaterial(Color albedo, Color emission, float emissionEnergy, bool transparent)
    {
        StandardMaterial3D material = new()
        {
            AlbedoColor = albedo,
            EmissionEnabled = true,
            Emission = emission,
            EmissionEnergyMultiplier = emissionEnergy,
            Metallic = 0.05f,
            Roughness = 0.48f,
        };

        if (transparent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        return material;
    }

    private static StandardMaterial3D CreateMachineMaterial(Color albedo, Color emission, float emissionEnergy, float metallic, float roughness, bool transparent)
    {
        StandardMaterial3D material = new()
        {
            AlbedoColor = albedo,
            EmissionEnabled = emissionEnergy > 0.001f,
            Emission = emission,
            EmissionEnergyMultiplier = emissionEnergy,
            Metallic = metallic,
            Roughness = roughness,
        };

        if (transparent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        return material;
    }

    public void BindEquipmentProvider(Node providerNode)
    {
        if (_equipmentProvider != null)
        {
            _equipmentProvider.EquipmentChanged -= OnEquipmentChanged;
            _equipmentProvider = null;
        }

        if (providerNode is IHubEquipmentProvider provider)
        {
            _equipmentProvider = provider;
            _equipmentProvider.EquipmentChanged += OnEquipmentChanged;
            RefreshSlots();
        }
        else
        {
            GD.PushWarning("HubSceneController provider does not implement IHubEquipmentProvider.");
        }
    }

    public void SetSlotItem(int slotIndex, EquipmentItem? item)
    {
        CacheSlots();

        if (slotIndex < 0 || slotIndex >= _slots.Length)
        {
            return;
        }

        HubEquipmentSlotView? slot = _slots[slotIndex];
        if (slot == null)
        {
            return;
        }

        if (item == null)
        {
            slot.ClearItem();
        }
        else
        {
            slot.SetItem(item);
        }

        slot.SetHighlighted(slotIndex == 0);
    }

    public void RefreshSlots()
    {
        CacheSlots();

        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot == null)
            {
                continue;
            }

            EquipmentItem? item = _equipmentProvider?.GetEquippedItemAt(i);
            if (item == null)
            {
                slot.ClearItem();
            }
            else
            {
                slot.SetItem(item);
            }

            slot.SetHighlighted(i == 0);
        }

        if (_weaponInspectionActive && GetFocusedInspectionSlot() == null)
        {
            EndWeaponInspection();
        }

        SetSlotInspectionModes();
        UpdateInspectionFocusOverlay();
    }

    private void EnsureInspectionFocusOverlay()
    {
        if (_inspectionFocusLayer == null || !IsInstanceValid(_inspectionFocusLayer))
        {
            _inspectionFocusLayer = new CanvasLayer
            {
                Name = "InspectionFocusOverlayLayer",
                Layer = 32,
                Visible = false,
            };
            AddChild(_inspectionFocusLayer);
        }

        if (_inspectionFocusContainer == null || !IsInstanceValid(_inspectionFocusContainer))
        {
            _inspectionFocusContainer = new SubViewportContainer
            {
                Name = "InspectionFocusOverlay",
                Stretch = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            SetFullRect(_inspectionFocusContainer);
            _inspectionFocusLayer.AddChild(_inspectionFocusContainer);
        }

        if (_inspectionFocusViewport == null || !IsInstanceValid(_inspectionFocusViewport))
        {
            _inspectionFocusViewport = new SubViewport
            {
                Name = "InspectionFocusViewport",
                TransparentBg = true,
                HandleInputLocally = false,
                OwnWorld3D = false,
            };
            _inspectionFocusContainer.AddChild(_inspectionFocusViewport);
        }

        if (_inspectionFocusCamera == null || !IsInstanceValid(_inspectionFocusCamera))
        {
            _inspectionFocusCamera = new Camera3D { Name = "InspectionFocusCamera" };
            _inspectionFocusViewport.AddChild(_inspectionFocusCamera);
            _inspectionFocusCamera.MakeCurrent();
        }
    }

    private void UpdateInspectionFocusOverlay()
    {
        HubEquipmentSlotView? focusedSlot = GetFocusedInspectionSlot();
        bool active = _weaponInspectionActive
            && _inspectionBlend >= InspectionFocusOverlayActivationBlend
            && PreviewCamera != null
            && focusedSlot != null
            && focusedSlot.UsesSharpInspectionOverlay
            && focusedSlot.CurrentItem?.Shape != null;

        if (!active || focusedSlot == null || PreviewCamera == null || focusedSlot.CurrentItem?.Shape == null)
        {
            ClearInspectionFocusOverlay();
            return;
        }

        EnsureInspectionFocusOverlay();
        if (_inspectionFocusLayer == null
            || _inspectionFocusViewport == null
            || _inspectionFocusCamera == null)
        {
            return;
        }

        _inspectionFocusLayer.Visible = true;
        focusedSlot.SetInspectionOverlayActive(true);
        focusedSlot.RefreshInspectionOverlayRenderLayers();
        RefreshInspectionOverlaySceneRenderLayers();
        RefreshInspectionOverlayLightLayers();
        PreviewCamera.CullMask = _previewCameraBaseCullMask & ~HubEquipmentSlotView.InspectionOverlayRenderLayerMask;
        _inspectionFocusCamera.GlobalTransform = PreviewCamera.GlobalTransform;
        _inspectionFocusCamera.Fov = PreviewCamera.Fov;
        _inspectionFocusCamera.Near = PreviewCamera.Near;
        _inspectionFocusCamera.Far = PreviewCamera.Far;
        _inspectionFocusCamera.CullMask = HubEquipmentSlotView.InspectionOverlayRenderLayerMask;
        _inspectionFocusCamera.Attributes = null;

        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot != null && slot != focusedSlot)
            {
                slot.SetInspectionOverlayActive(false);
            }
        }
    }

    private void ClearInspectionFocusOverlay()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            slot?.SetInspectionOverlayActive(false);
        }

        RestoreInspectionOverlaySceneRenderLayers();
        RestoreInspectionOverlayLightLayers();
        RestorePreviewCameraCullMask();

        if (_inspectionFocusLayer != null && IsInstanceValid(_inspectionFocusLayer))
        {
            _inspectionFocusLayer.Visible = false;
        }
    }

    private void RefreshInspectionOverlaySceneRenderLayers()
    {
        ApplyInspectionOverlaySceneRenderLayer(CharacterRoot);
    }

    private void ApplyInspectionOverlaySceneRenderLayer(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node is VisualInstance3D visual)
        {
            if (!_inspectionOverlayOriginalVisualLayers.ContainsKey(visual))
            {
                _inspectionOverlayOriginalVisualLayers[visual] = visual.Layers;
            }

            if (node is Light3D)
            {
                visual.Layers = _inspectionOverlayOriginalVisualLayers[visual] | HubEquipmentSlotView.InspectionOverlayRenderLayerMask;
            }
            else
            {
                visual.Layers = HubEquipmentSlotView.InspectionOverlayRenderLayerMask;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyInspectionOverlaySceneRenderLayer(child);
        }
    }

    private void RestoreInspectionOverlaySceneRenderLayers()
    {
        foreach (KeyValuePair<VisualInstance3D, uint> entry in _inspectionOverlayOriginalVisualLayers)
        {
            if (IsInstanceValid(entry.Key))
            {
                entry.Key.Layers = entry.Value;
            }
        }

        _inspectionOverlayOriginalVisualLayers.Clear();
    }

    private void RefreshInspectionOverlayLightLayers()
    {
        ApplyInspectionOverlayLightLayer(GetNodeOrNull<DirectionalLight3D>("KeyLight"));
        ApplyInspectionOverlayLightLayer(GetNodeOrNull<OmniLight3D>("FillLight"));
    }

    private void ApplyInspectionOverlayLightLayer(VisualInstance3D? light)
    {
        if (light == null || !IsInstanceValid(light))
        {
            return;
        }

        if (!_inspectionOverlayOriginalLightLayers.ContainsKey(light))
        {
            _inspectionOverlayOriginalLightLayers[light] = light.Layers;
        }

        light.Layers = _inspectionOverlayOriginalLightLayers[light] | HubEquipmentSlotView.InspectionOverlayRenderLayerMask;
    }

    private void RestoreInspectionOverlayLightLayers()
    {
        foreach (KeyValuePair<VisualInstance3D, uint> entry in _inspectionOverlayOriginalLightLayers)
        {
            if (IsInstanceValid(entry.Key))
            {
                entry.Key.Layers = entry.Value;
            }
        }

        _inspectionOverlayOriginalLightLayers.Clear();
    }

    private void CapturePreviewCameraCullMask()
    {
        if (_previewCameraCullMaskCaptured || PreviewCamera == null)
        {
            return;
        }

        _previewCameraBaseCullMask = PreviewCamera.CullMask;
        _previewCameraCullMaskCaptured = true;
    }

    private void RestorePreviewCameraCullMask()
    {
        if (PreviewCamera == null || !_previewCameraCullMaskCaptured)
        {
            return;
        }

        PreviewCamera.CullMask = _previewCameraBaseCullMask;
    }

    private static void SetFullRect(Control control)
    {
        control.AnchorLeft = 0f;
        control.AnchorTop = 0f;
        control.AnchorRight = 1f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = 0f;
        control.OffsetTop = 0f;
        control.OffsetRight = 0f;
        control.OffsetBottom = 0f;
    }

    private void CacheSlots()
    {
        Array.Clear(_slots, 0, _slots.Length);

        if (SlotNodePaths.Count > 0)
        {
            for (int i = 0; i < Math.Min(5, SlotNodePaths.Count); i++)
            {
                _slots[i] = GetNodeOrNull<HubEquipmentSlotView>(SlotNodePaths[i]);
            }

            return;
        }

        int index = 0;
        foreach (Node child in GetChildren())
        {
            if (index >= _slots.Length)
            {
                break;
            }

            if (child is HubEquipmentSlotView slot)
            {
                _slots[index] = slot;
                index++;
            }
        }
    }

    private void ApplySlotLayout()
    {
        CacheSlots();

        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot == null)
            {
                continue;
            }

            slot.SlotIndex = i;
            slot.Position = SlotLayout[i];
            OrientSlotTowardHub(slot);
        }
    }

    private void AnimateCharacter(float delta)
    {
        if (CharacterRoot == null)
        {
            return;
        }

        float backgroundMotionScale = Mathf.Lerp(1f, 0.12f, _inspectionBlend);
        CharacterRoot.RotateY(delta * CharacterRotateSpeed * backgroundMotionScale);

        Vector3 pos = CharacterRoot.Position;
        pos.Y = _characterBaseY + CharacterBobAmplitude * backgroundMotionScale * Mathf.Sin(_time * CharacterBobSpeed);
        CharacterRoot.Position = pos;
    }

    private void AnimateSlots()
    {
        CacheSlots();

        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot == null)
            {
                continue;
            }

            Vector3 basePos = SlotLayout[i];
            bool focused = i == _focusedInspectionSlotIndex && _inspectionBlend > 0.001f;
            float bobScale = focused ? 0.15f : Mathf.Lerp(1f, 0.0f, _inspectionBlend);
            float bob = SlotBobAmplitude * bobScale * Mathf.Sin(_time * SlotBobSpeed + i * 0.65f);
            slot.Position = new Vector3(basePos.X, basePos.Y + bob, basePos.Z);
            slot.Visible = true;
            slot.SetInspectionMode(_weaponInspectionActive && focused);
            slot.SetInspectionFocusBlend(_weaponInspectionActive && focused ? _inspectionBlend : 0f);
            slot.SetInspectionBackdropBlend(_weaponInspectionActive && !focused ? _inspectionBlend : 0f);
            OrientSlotTowardHub(slot);
        }
    }

    private HubEquipmentSlotView? GetFocusedInspectionSlot()
    {
        CacheSlots();
        if (_focusedInspectionSlotIndex < 0 || _focusedInspectionSlotIndex >= _slots.Length)
        {
            return null;
        }

        HubEquipmentSlotView? slot = _slots[_focusedInspectionSlotIndex];
        return slot != null && slot.HasItem ? slot : null;
    }

    private void SetSlotInspectionModes()
    {
        CacheSlots();
        for (int i = 0; i < _slots.Length; i++)
        {
            HubEquipmentSlotView? slot = _slots[i];
            if (slot == null)
            {
                continue;
            }

            bool focused = _weaponInspectionActive && i == _focusedInspectionSlotIndex;
            slot.SetInspectionMode(focused);
            slot.SetInspectionFocusBlend(focused ? _inspectionBlend : 0f);
            if (_inspectionBlend <= 0.001f && !_weaponInspectionActive)
            {
                slot.Visible = true;
            }
        }
    }

    private void OrientSlotTowardHub(HubEquipmentSlotView slot)
    {
        slot.FaceCamera = false;

        Vector3 target = ToGlobal(SlotFocusPoint);
        if ((target - slot.GlobalPosition).LengthSquared() <= 0.0001f)
        {
            return;
        }

        slot.LookAt(target, Vector3.Up, true);
    }

    private void OnEquipmentChanged()
    {
        RefreshSlots();
    }

    private void WireSlotSignals()
    {
        CacheSlots();

        foreach (HubEquipmentSlotView? slot in _slots)
        {
            if (slot == null)
            {
                continue;
            }

            slot.SlotClicked -= OnSlotClicked;
            slot.SlotClicked += OnSlotClicked;
        }
    }

    private void OnSlotClicked(int slotIndex)
    {
        ActivateSlotFromClick(slotIndex);
    }

    private void ActivateSlotFromClick(int slotIndex)
    {
        if (BeginWeaponInspection(slotIndex))
        {
            return;
        }

        EmitSignal(SignalName.SlotClicked, slotIndex);
    }
}

// Placeholder contract for whichever inventory/equipment system owns equipped items.
public interface IHubEquipmentProvider
{
    event Action EquipmentChanged;
    EquipmentItem? GetEquippedItemAt(int slotIndex);
}

public enum HubSocketKind
{
    Essence,
    Modulator,
}

public readonly struct HubSocketAnchor
{
    public HubSocketAnchor(
        HubSocketKind kind,
        int weaponSlotIndex,
        int socketIndex,
        Vector2 viewportPosition,
        bool unlocked,
        bool installed,
        Color color,
        string label,
        string tooltip,
        string title,
        string summary)
    {
        Kind = kind;
        WeaponSlotIndex = weaponSlotIndex;
        SocketIndex = socketIndex;
        ViewportPosition = viewportPosition;
        Unlocked = unlocked;
        Installed = installed;
        Color = color;
        Label = label;
        Tooltip = tooltip;
        Title = title;
        Summary = summary;
    }

    public HubSocketKind Kind { get; }
    public int WeaponSlotIndex { get; }
    public int SocketIndex { get; }
    public Vector2 ViewportPosition { get; }
    public bool Unlocked { get; }
    public bool Installed { get; }
    public Color Color { get; }
    public string Label { get; }
    public string Tooltip { get; }
    public string Title { get; }
    public string Summary { get; }
}
