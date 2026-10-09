using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Visuals;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Hub;

// Visual representation of one equipped shape slot around the hub character.
public partial class HubEquipmentSlotView : Node3D
{
    [Signal] public delegate void SlotClickedEventHandler(int slotIndex);

    public const uint InspectionOverlayRenderLayerMask = 1u << 19;
    private const uint LocalGlowIgnoredRenderLayerMask = 1u << 18;
    private const uint AllRenderLayersMask = (1u << 20) - 1u;
    private const uint LocalGlowLightCullMask = AllRenderLayersMask & ~LocalGlowIgnoredRenderLayerMask;

    private static readonly Vector3 EssenceSocketLocalPosition = Vector3.Zero;
    private const float InspectionBackgroundShrinkScale = 0.72f;
    private const float InspectionBackgroundTransparency = 0.58f;
    private const float InspectionBackgroundLightScale = 0.24f;
    private const float InspectionSpinBoostMultiplier = 1.45f;
    private const float InspectionPitchReturnSpeed = 8.5f;
    private const float InspectionStaticIntroYaw = 0.24f;
    private const float InspectionStaticIntroPitch = -0.12f;
    private const float InspectionStaticIntroScaleBoost = 0.08f;
    private const float InspectionStaticIntroLightBoost = 0.48f;
    private const float InspectionDepthProxyCameraOffset = 0.16f;

    private static Shader? _inspectionDepthProxyShader;

    [Export] public int SlotIndex = 0;
    [Export] public bool FaceCamera = false;
    [Export] public float ItemRotateSpeed = 0.62f;

    private EquipmentItem? _item;
    private MeshInstance3D? _ringMesh;
    private Node3D? _itemVisual;
    private ModifierVisualController? _modifierVisual;
    private Node3D? _socketRoot;
    private OmniLight3D? _glowLight;
    private Area3D? _clickArea;
    private MeshInstance3D? _inspectionDepthProxy;

    private readonly Dictionary<VisualInstance3D, uint> _inspectionOverlayOriginalLayers = new();
    private readonly StandardMaterial3D _ringMaterial = new();

    private bool _highlighted;
    private bool _hovered;
    private bool _inspectionMode;
    private bool _inspectionOverlayActive;
    private float _inspectionBackdropBlend;
    private float _inspectionFocusBlend;
    private float _inspectionYaw;
    private float _inspectionPitch;
    private float _releasedInspectionPitch;
    private float _spinAngle;
    private float _time;

    public bool HasItem => _item != null;
    public bool IsHovered => _hovered;
    public EquipmentItem? CurrentItem => _item;
    public Color CurrentTintColor => ResolveTintColor();

    public bool UsesSharpInspectionOverlay => _item?.Shape != null;

    public bool TryGetItemVisualGlobalTransform(out Transform3D globalTransform)
    {
        globalTransform = default;
        if (_itemVisual == null || !IsInstanceValid(_itemVisual))
        {
            return false;
        }

        globalTransform = _itemVisual.GlobalTransform;
        return true;
    }

    public void SetInspectionOverlayActive(bool active)
    {
        if (_inspectionOverlayActive == active)
        {
            return;
        }

        _inspectionOverlayActive = active;
        if (active)
        {
            RefreshInspectionOverlayRenderLayers();
        }
        else
        {
            RestoreInspectionOverlayRenderLayers();
        }

        RefreshInspectionDepthProxy();
    }

    public void RefreshInspectionOverlayRenderLayers()
    {
        if (!_inspectionOverlayActive)
        {
            return;
        }

        ApplyInspectionOverlayRenderLayer(_itemVisual);
        ApplyInspectionOverlayRenderLayer(_modifierVisual);
        ApplyInspectionOverlayRenderLayer(_socketRoot);
    }

    public void BuildInspectionSocketAnchors(Camera3D camera, int weaponSlotIndex, ICollection<HubSocketAnchor> anchors)
    {
        if (!_inspectionMode || _item == null || _socketRoot == null || !IsInstanceValid(_socketRoot))
        {
            return;
        }

        if (TryProjectSocketAnchor(camera, EssenceSocketLocalPosition, out Vector2 essencePosition))
        {
            bool essenceUnlocked = _item.HasEssenceSlotUnlocked;
            bool essenceInstalled = _item.HasEquippedEssence;
            Color essenceColor = essenceInstalled
                ? _item.Essence!.UiColor
                : essenceUnlocked ? new Color(0.95f, 0.78f, 0.28f, 1f) : new Color(0.34f, 0.39f, 0.48f, 1f);
            string essenceTooltip = essenceInstalled
                ? $"Return {_item.Essence!.DisplayName} to storage"
                : essenceUnlocked ? "Socket the first stored essence" : "Essence slot locked";

            anchors.Add(new HubSocketAnchor(
                HubSocketKind.Essence,
                weaponSlotIndex,
                -1,
                essencePosition,
                essenceUnlocked,
                essenceInstalled,
                essenceColor,
                "E",
                essenceTooltip,
                essenceInstalled && _item.Essence != null ? $"Essence - {_item.Essence.DisplayName}" : essenceUnlocked ? "Essence Slot" : "Essence Locked",
                essenceInstalled && _item.Essence != null
                    ? BuildEssenceCardSummary(_item.Essence)
                    : essenceUnlocked ? "Install a stored essence." : "Unlock the essence slot to install an essence."));
        }

        int visibleSlots = WeaponModulatorVisualFactory.GetVisibleSocketCount(_item);
        int installedCount = _item.Modulators?.Count ?? 0;
        int unlockedSlots = _item.UnlockedModulatorSlots;

        for (int socketIndex = 0; socketIndex < visibleSlots; socketIndex++)
        {
            if (!TryProjectModulatorSocketAnchor(camera, _item, socketIndex, visibleSlots, out Vector2 socketPosition))
            {
                continue;
            }

            bool unlocked = socketIndex < unlockedSlots;
            bool hasSocketEntry = socketIndex < installedCount && _item.Modulators != null;
            WeaponModulatorData? modulator = hasSocketEntry ? _item.Modulators![socketIndex] : null;
            bool installed = modulator != null;
            Color color = modulator?.UiColor
                ?? (unlocked ? new Color(0.26f, 0.86f, 1f, 1f) : new Color(0.26f, 0.31f, 0.40f, 1f));
            string tooltip = installed
                ? $"Return {modulator!.DisplayName} to storage"
                : unlocked ? "Socket the first compatible stored modulator" : "Modulator slot locked";

            anchors.Add(new HubSocketAnchor(
                HubSocketKind.Modulator,
                weaponSlotIndex,
                socketIndex,
                socketPosition,
                unlocked,
                installed,
                color,
                (socketIndex + 1).ToString(),
                tooltip,
                installed && modulator != null ? $"Slot {socketIndex + 1} - {modulator.DisplayName}" : unlocked ? $"Slot {socketIndex + 1} - Empty" : $"Slot {socketIndex + 1} - Locked",
                installed && modulator != null
                    ? BuildModulatorCardSummary(modulator)
                    : unlocked ? "Install a stored modulator." : "Unlock this slot by leveling the weapon."));
        }
    }

    private static string BuildEssenceCardSummary(ModifierData essence)
    {
        if (!string.IsNullOrWhiteSpace(essence.PrimaryEffectDescription))
        {
            return BuildShortSummary(essence.PrimaryEffectDescription, "Modifier currently applied.");
        }

        if (!string.IsNullOrWhiteSpace(essence.PassiveDescription))
        {
            return BuildShortSummary(essence.PassiveDescription, "Modifier currently applied.");
        }

        return "Modifier currently applied.";
    }

    private static string BuildModulatorCardSummary(WeaponModulatorData modulator)
    {
        return $"{ModulatorQualityText.Header(modulator)} | {ModulatorQualityText.Describe(modulator).Replace("\n", " | ")}";
    }

    private static string BuildShortSummary(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        string trimmed = text.Trim();
        int sentenceEnd = trimmed.IndexOf('.');
        if (sentenceEnd >= 0 && sentenceEnd < trimmed.Length - 1)
        {
            trimmed = trimmed[..(sentenceEnd + 1)];
        }

        return trimmed.Length <= 96 ? trimmed : trimmed[..93] + "...";
    }

    public bool TryGetProjectedPickBounds(Camera3D camera, out Rect2 bounds)
    {
        bounds = default;
        if (!Visible || camera.IsPositionBehind(GlobalPosition))
        {
            return false;
        }

        bool hasPoint = false;
        ExpandProjectedPoint(camera, GlobalPosition, ref bounds, ref hasPoint);

        if (_itemVisual != null && IsInstanceValid(_itemVisual) && _itemVisual.Visible)
        {
            IncludeProjectedMeshBounds(_itemVisual, camera, ref bounds, ref hasPoint);
        }
        else if (_ringMesh != null && IsInstanceValid(_ringMesh) && _ringMesh.Visible)
        {
            IncludeProjectedMeshBounds(_ringMesh, camera, ref bounds, ref hasPoint);
        }

        if (!hasPoint)
        {
            return false;
        }

        return true;
    }

    public override void _Ready()
    {
        BuildVisualNodes();
        RefreshVisual();
    }

    public override void _Process(double delta)
    {
        float deltaSeconds = (float)delta;
        _time += deltaSeconds;
        bool usesHubSpinPackage = UsesHubSpinPackage();
        UpdateReleasedInspectionPitch(deltaSeconds);

        if (_inspectionMode)
        {
            float inspectionScale = 1.42f;
            if (!usesHubSpinPackage)
            {
                inspectionScale *= 1f + InspectionStaticIntroScaleBoost * ResolveStaticInspectionIntroStrength();
            }

            Scale = Vector3.One * inspectionScale;
            if (!usesHubSpinPackage)
            {
                ApplyStaticInspectionLightPulse();
                ApplyInspectionRotation();
            }
        }
        else
        {
            float pulse = 0.95f + 0.05f * Mathf.Sin(_time * 2.2f + SlotIndex * 0.5f);
            float highlightScale = _highlighted ? 1.12f : 1f;
            float backdropScale = Mathf.Lerp(1f, InspectionBackgroundShrinkScale, _inspectionBackdropBlend);
            Scale = Vector3.One * pulse * highlightScale * backdropScale;
        }

        if (FaceCamera)
        {
            Camera3D? camera = GetViewport().GetCamera3D();
            if (camera != null)
            {
                LookAt(camera.GlobalPosition, Vector3.Up, true);
            }
        }

        if (usesHubSpinPackage)
        {
            UpdatePassiveSpin(deltaSeconds);
        }

        if (_modifierVisual != null && IsInstanceValid(_modifierVisual) && _itemVisual != null && IsInstanceValid(_itemVisual))
        {
            _modifierVisual.Rotation = _itemVisual.Rotation;
        }

        if (_inspectionOverlayActive)
        {
            RefreshInspectionOverlayRenderLayers();
        }

        RefreshInspectionDepthProxy();
    }

    public void SetItem(EquipmentItem item)
    {
        _item = item;
        RefreshVisual();
    }

    public void ClearItem()
    {
        _item = null;
        RefreshVisual();
    }

    public void PrepareForSceneTransition()
    {
        _inspectionMode = false;
        _inspectionOverlayActive = false;
        _inspectionBackdropBlend = 0f;
        _inspectionFocusBlend = 0f;
        _inspectionYaw = 0f;
        _inspectionPitch = 0f;
        _releasedInspectionPitch = 0f;
        _hovered = false;
        _spinAngle = 0f;

        SetProcess(false);
        Scale = Vector3.One;

        RestoreInspectionOverlayRenderLayers();
        ClearModifierVisual();

        if (_socketRoot != null)
        {
            if (IsInstanceValid(_socketRoot))
            {
                _socketRoot.QueueFree();
            }

            _socketRoot = null;
        }

        if (_itemVisual != null)
        {
            if (IsInstanceValid(_itemVisual))
            {
                _itemVisual.QueueFree();
            }

            _itemVisual = null;
        }

        if (_inspectionDepthProxy != null)
        {
            if (IsInstanceValid(_inspectionDepthProxy))
            {
                _inspectionDepthProxy.QueueFree();
            }

            _inspectionDepthProxy = null;
        }
    }

    public void RefreshVisual()
    {
        BuildVisualNodes();

        Color tint = ResolveTintColor();

        if (_item == null)
        {
            _ringMaterial.AlbedoColor = new Color(0.045f, 0.075f, 0.130f, 0.82f);
            _ringMaterial.Emission = new Color(0.040f, 0.135f, 0.165f, 1f);
            _ringMaterial.EmissionEnergyMultiplier = 0.28f;
        }
        else
        {
            _ringMaterial.AlbedoColor = tint.Darkened(0.3f);
            _ringMaterial.Emission = tint;
            _ringMaterial.EmissionEnergyMultiplier = 0.6f;
        }

        if (_item == null)
        {
            if (_ringMesh != null)
            {
                _ringMesh.Visible = true;
            }

            ClearItemVisual();
            RefreshSocketVisuals();
            RefreshInspectionDepthProxy();

            if (_glowLight != null)
            {
                _glowLight.LightColor = Colors.SlateGray;
                _glowLight.LightEnergy = 0.6f;
            }

            ApplyInspectionBackdrop();

            return;
        }

        if (_glowLight != null)
        {
            _glowLight.LightColor = tint;
            _glowLight.LightEnergy = _highlighted ? 3.25f : 2.2f;
        }

        ReplaceItemVisual(tint);
        RefreshSocketVisuals();
        RefreshInspectionDepthProxy();

        if (_ringMesh != null)
        {
            _ringMesh.Visible = false;
        }

        ApplyInspectionBackdrop();
    }

    public void SetInspectionMode(bool active)
    {
        if (_inspectionMode == active)
        {
            return;
        }

        if (!active)
        {
            if (UsesHubSpinPackage())
            {
                _spinAngle = Mathf.Wrap(_spinAngle + _inspectionYaw, -Mathf.Pi, Mathf.Pi);
            }

            _releasedInspectionPitch = _inspectionPitch;
            _inspectionYaw = 0f;
            _inspectionPitch = 0f;
            _inspectionFocusBlend = 0f;
        }
        else if (Mathf.Abs(_releasedInspectionPitch) > 0.0001f)
        {
            _inspectionPitch = _releasedInspectionPitch;
            _releasedInspectionPitch = 0f;
        }

        _inspectionMode = active;

        RefreshSocketVisuals();
        RefreshInspectionDepthProxy();
        ApplyInspectionRotation();
        ApplyInspectionBackdrop();
    }

    public void SetInspectionBackdropBlend(float blend)
    {
        float clampedBlend = Mathf.Clamp(blend, 0f, 1f);
        if (Mathf.IsEqualApprox(_inspectionBackdropBlend, clampedBlend))
        {
            return;
        }

        _inspectionBackdropBlend = clampedBlend;
        ApplyInspectionBackdrop();
    }

    public void SetInspectionFocusBlend(float blend)
    {
        float clampedBlend = Mathf.Clamp(blend, 0f, 1f);
        if (Mathf.IsEqualApprox(_inspectionFocusBlend, clampedBlend))
        {
            return;
        }

        _inspectionFocusBlend = clampedBlend;
        ApplyInspectionRotation();
    }

    public void RotateInspection(Vector2 mouseDelta)
    {
        if (!_inspectionMode)
        {
            return;
        }

        _inspectionYaw += mouseDelta.X * 0.0125f;
        _inspectionPitch = Mathf.Clamp(_inspectionPitch + mouseDelta.Y * 0.010f, -1.15f, 1.15f);
        ApplyInspectionRotation();
    }

    public void SetHighlighted(bool highlighted)
    {
        BuildVisualNodes();

        _highlighted = highlighted;
        if (_glowLight != null)
        {
            _glowLight.LightEnergy = highlighted ? 3.25f : 2.2f;
        }

        if (_modifierVisual != null && IsInstanceValid(_modifierVisual))
        {
            _modifierVisual.Intensity = highlighted ? 1.08f : 0.86f;
            _modifierVisual.RefreshLightBaseline();
        }

        ApplyInspectionBackdrop();
    }

    private void BuildVisualNodes()
    {
        if (_ringMesh == null || !IsInstanceValid(_ringMesh))
        {
            _ringMesh = new MeshInstance3D
            {
                Name = "PlaceholderRing",
                Mesh = new TorusMesh
                {
                    InnerRadius = 0.36f,
                    OuterRadius = 0.56f,
                    Rings = 48,
                    RingSegments = 10,
                },
                Position = Vector3.Zero,
                MaterialOverride = _ringMaterial,
            };
            AddChild(_ringMesh);
        }

        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            _glowLight = new OmniLight3D
            {
                Name = "Glow",
                Position = new Vector3(0f, 0.2f, 0f),
                OmniRange = 4f,
                LightEnergy = 1.5f,
                LightCullMask = LocalGlowLightCullMask,
                ShadowEnabled = false,
            };
            AddChild(_glowLight);
        }

        if (_glowLight != null)
        {
            _glowLight.LightCullMask = LocalGlowLightCullMask;
        }

        if (_inspectionDepthProxy == null || !IsInstanceValid(_inspectionDepthProxy))
        {
            _inspectionDepthProxy = new MeshInstance3D
            {
                Name = "InspectionDepthProxy",
                Mesh = new SphereMesh
                {
                    Radius = 1.0f,
                    Height = 2.0f,
                    RadialSegments = 48,
                    Rings = 24,
                },
                TopLevel = true,
                Scale = Vector3.One,
                MaterialOverride = CreateInspectionDepthProxyMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(_inspectionDepthProxy);
        }

        _ringMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _ringMaterial.EmissionEnabled = true;
        _ringMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _ringMaterial.AlbedoColor = new Color(0.055f, 0.105f, 0.170f, 0.82f);
        _ringMaterial.Emission = new Color(0.055f, 0.150f, 0.190f, 1f);
        _ringMaterial.EmissionEnergyMultiplier = 0.35f;

        if (_clickArea == null || !IsInstanceValid(_clickArea))
        {
            _clickArea = new Area3D
            {
                Name = "ClickArea",
                InputRayPickable = true,
                CollisionLayer = 1,
                CollisionMask = 0,
            };
            CollisionShape3D clickShape = new()
            {
                Name = "ClickShape",
                Shape = new SphereShape3D { Radius = 0.72f },
            };
            _clickArea.AddChild(clickShape);
            _clickArea.InputEvent += OnClickAreaInputEvent;
            _clickArea.MouseEntered += OnClickAreaMouseEntered;
            _clickArea.MouseExited += OnClickAreaMouseExited;
            AddChild(_clickArea);
        }
    }

    private void OnClickAreaInputEvent(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            EmitSignal(SignalName.SlotClicked, SlotIndex);
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnClickAreaMouseEntered()
    {
        _hovered = true;
        SetHighlighted(true);
    }

    private void OnClickAreaMouseExited()
    {
        _hovered = false;
        SetHighlighted(SlotIndex == 0);
    }

    private static void IncludeProjectedMeshBounds(Node node, Camera3D camera, ref Rect2 bounds, ref bool hasPoint)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Visible && meshInstance.Mesh != null)
        {
            Aabb aabb = meshInstance.GetAabb();
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
                        Vector3 globalPoint = meshInstance.GlobalTransform * localPoint;
                        ExpandProjectedPoint(camera, globalPoint, ref bounds, ref hasPoint);
                    }
                }
            }
        }
        else if (node is MultiMeshInstance3D multiMeshInstance && multiMeshInstance.Visible && multiMeshInstance.Multimesh?.Mesh != null)
        {
            Aabb aabb = multiMeshInstance.GetAabb();
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
                        Vector3 globalPoint = multiMeshInstance.GlobalTransform * localPoint;
                        ExpandProjectedPoint(camera, globalPoint, ref bounds, ref hasPoint);
                    }
                }
            }
        }

        foreach (Node child in node.GetChildren())
        {
            IncludeProjectedMeshBounds(child, camera, ref bounds, ref hasPoint);
        }
    }

    private static void ExpandProjectedPoint(Camera3D camera, Vector3 globalPoint, ref Rect2 bounds, ref bool hasPoint)
    {
        if (camera.IsPositionBehind(globalPoint))
        {
            return;
        }

        Vector2 screenPoint = camera.UnprojectPosition(globalPoint);
        if (!hasPoint)
        {
            bounds = new Rect2(screenPoint, Vector2.Zero);
            hasPoint = true;
            return;
        }

        bounds = bounds.Expand(screenPoint);
    }

    private void ReplaceItemVisual(Color tint)
    {
        ClearItemVisual();

        _itemVisual = ShapeVisualFactory.CreateVisual(_item?.Shape, tint);
        _itemVisual.Name = "ItemPreview";
        ApplyLocalGlowIgnoredRenderLayer(_itemVisual);
        _itemVisual.Scale = new Vector3(
            _itemVisual.Scale.X * 0.78f,
            _itemVisual.Scale.Y * 0.78f,
            _itemVisual.Scale.Z * 0.78f);
        AddChild(_itemVisual);
        ApplyModifierVisual();
        ApplyInspectionRotation();
    }

    private void RefreshSocketVisuals()
    {
        EnsureSocketRoot();

        if (_socketRoot == null)
        {
            return;
        }

        foreach (Node child in _socketRoot.GetChildren())
        {
            child.QueueFree();
        }

        bool showOutsideInspection = _item != null && WeaponModulatorVisualFactory.HasPersistentHubSocketVisuals(_item);
        _socketRoot.Visible = _item != null && (_inspectionMode || showOutsideInspection);
        if (_item == null || !_socketRoot.Visible)
        {
            return;
        }

        BuildModulatorSockets(_item);
        ApplyInspectionRotation();
    }

    private void EnsureSocketRoot()
    {
        if (_socketRoot != null && IsInstanceValid(_socketRoot))
        {
            return;
        }

        _socketRoot = new Node3D { Name = "InspectionSockets" };
        AddChild(_socketRoot);
    }

    private void BuildModulatorSockets(EquipmentItem item)
    {
        if (_socketRoot == null)
        {
            return;
        }

        WeaponModulatorVisualFactory.PopulateSocketVisuals(
            _socketRoot,
            item,
            options: new SocketVisualOptions
            {
                InspectionMode = _inspectionMode,
                EnableOrbitStyle = true,
            });
    }

    private bool TryProjectSocketAnchor(Camera3D camera, Vector3 localPosition, out Vector2 viewportPosition)
    {
        viewportPosition = default;
        if (_socketRoot == null || !IsInstanceValid(_socketRoot))
        {
            return false;
        }

        Vector3 globalPosition = _socketRoot.GlobalTransform * localPosition;
        if (camera.IsPositionBehind(globalPosition))
        {
            return false;
        }

        viewportPosition = camera.UnprojectPosition(globalPosition);
        return true;
    }

    private bool TryProjectModulatorSocketAnchor(Camera3D camera, EquipmentItem item, int socketIndex, int visibleSlots, out Vector2 viewportPosition)
    {
        viewportPosition = default;

        if (_socketRoot == null || !IsInstanceValid(_socketRoot))
        {
            return false;
        }

        if (WeaponModulatorVisualFactory.UsesOrbitSocketLayout(item))
        {
            string orbitNodeName = $"ModulatorOrbit{socketIndex + 1:00}";
            Node3D? orbitPayload = _socketRoot.GetNodeOrNull<Node3D>($"{orbitNodeName}/OrbitCarrier/OrbitPivot/PayloadRoot");
            if (orbitPayload != null && IsInstanceValid(orbitPayload))
            {
                Vector3 globalPosition = orbitPayload.GlobalTransform.Origin;
                if (!camera.IsPositionBehind(globalPosition))
                {
                    viewportPosition = camera.UnprojectPosition(globalPosition);
                    return true;
                }
            }
        }

        return TryProjectSocketAnchor(camera, WeaponModulatorVisualFactory.GetSocketLocalPosition(item, socketIndex, visibleSlots), out viewportPosition);
    }

    private bool UsesHubSpinPackage()
    {
        return _itemVisual != null
            && IsInstanceValid(_itemVisual)
            && _item?.Shape?.VisualScene == null
            && !ShapeVisualFactory.UsesNativeAnimation(_itemVisual);
    }

    private void UpdatePassiveSpin(float deltaSeconds)
    {
        float spinSpeed = ResolvePassiveSpinSpeed();
        if (Mathf.Abs(spinSpeed) > 0.0001f)
        {
            _spinAngle = Mathf.Wrap(_spinAngle + deltaSeconds * spinSpeed, -Mathf.Pi, Mathf.Pi);
        }

        ApplyInspectionRotation();
    }

    private float ResolvePassiveSpinSpeed()
    {
        float baseSpinSpeed = ItemRotateSpeed + SlotIndex * 0.055f;
        if (!_inspectionMode)
        {
            return baseSpinSpeed;
        }

        float blend = Mathf.Clamp(_inspectionFocusBlend, 0f, 1f);
        float decay = 1f - (blend * blend);
        float boost = Mathf.Sin(blend * Mathf.Pi) * (1f - blend) * InspectionSpinBoostMultiplier;
        return baseSpinSpeed * Mathf.Max(0f, decay + boost);
    }

    private float ResolveStaticInspectionIntroStrength()
    {
        if (!_inspectionMode || UsesHubSpinPackage())
        {
            return 0f;
        }

        float blend = Mathf.Clamp(_inspectionFocusBlend, 0f, 1f);
        return Mathf.Sin(blend * Mathf.Pi);
    }

    private float ResolveStaticInspectionIntroYawOffset()
    {
        float direction = SlotIndex % 2 == 0 ? 1f : -1f;
        return direction * InspectionStaticIntroYaw * ResolveStaticInspectionIntroStrength();
    }

    private float ResolveStaticInspectionIntroPitchOffset()
    {
        return InspectionStaticIntroPitch * ResolveStaticInspectionIntroStrength();
    }

    private void ApplyStaticInspectionLightPulse()
    {
        if (_glowLight == null || !IsInstanceValid(_glowLight))
        {
            return;
        }

        float baseEnergy = _item == null ? 0.6f : (_highlighted ? 3.25f : 2.2f);
        _glowLight.LightEnergy = baseEnergy * (1f + InspectionStaticIntroLightBoost * ResolveStaticInspectionIntroStrength());
    }

    private void UpdateReleasedInspectionPitch(float deltaSeconds)
    {
        if (_inspectionMode || Mathf.Abs(_releasedInspectionPitch) <= 0.0001f)
        {
            return;
        }

        float lerp = 1f - Mathf.Exp(-InspectionPitchReturnSpeed * deltaSeconds);
        _releasedInspectionPitch = Mathf.Lerp(_releasedInspectionPitch, 0f, lerp);
        if (Mathf.Abs(_releasedInspectionPitch) <= 0.001f)
        {
            _releasedInspectionPitch = 0f;
        }
    }

    private void ApplyInspectionBackdrop()
    {
        float dimBlend = _inspectionMode ? 0f : _inspectionBackdropBlend;

        if (_glowLight != null && IsInstanceValid(_glowLight))
        {
            float baseEnergy = _item == null ? 0.6f : (_highlighted ? 3.25f : 2.2f);
            _glowLight.LightEnergy = Mathf.Lerp(baseEnergy, baseEnergy * InspectionBackgroundLightScale, dimBlend);
        }
    }

    private void ApplyInspectionRotation()
    {
        bool usesHubSpinPackage = UsesHubSpinPackage();
        float introPitchOffset = _inspectionMode && !usesHubSpinPackage ? ResolveStaticInspectionIntroPitchOffset() : 0f;
        float introYawOffset = _inspectionMode && !usesHubSpinPackage ? ResolveStaticInspectionIntroYawOffset() : 0f;
        float displayPitch = (_inspectionMode ? _inspectionPitch : _releasedInspectionPitch) + introPitchOffset;
        if (!_inspectionMode)
        {
            if (_socketRoot != null && IsInstanceValid(_socketRoot))
            {
                _socketRoot.Rotation = Vector3.Zero;
            }

            if (!usesHubSpinPackage && Mathf.Abs(displayPitch) <= 0.0001f)
            {
                return;
            }
        }

        Vector3 rotation = new(
            displayPitch,
            (_inspectionMode ? _inspectionYaw : 0f) + (usesHubSpinPackage ? _spinAngle : 0f) + introYawOffset,
            0f);
        if (_itemVisual != null && IsInstanceValid(_itemVisual) && (_inspectionMode || usesHubSpinPackage || Mathf.Abs(displayPitch) > 0.0001f))
        {
            _itemVisual.Rotation = rotation;
        }

        if (_socketRoot != null && IsInstanceValid(_socketRoot))
        {
            _socketRoot.Rotation = _inspectionMode ? rotation : Vector3.Zero;
        }

        if (_modifierVisual != null && IsInstanceValid(_modifierVisual) && (_inspectionMode || usesHubSpinPackage || Mathf.Abs(displayPitch) > 0.0001f))
        {
            _modifierVisual.Rotation = rotation;
        }
    }

    private void RefreshInspectionDepthProxy()
    {
        if (_inspectionDepthProxy == null || !IsInstanceValid(_inspectionDepthProxy))
        {
            return;
        }

        bool visible = _inspectionMode && !_inspectionOverlayActive && ShouldUseInspectionDepthProxy();
        _inspectionDepthProxy.Visible = visible;
        if (!visible)
        {
            return;
        }

        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            _inspectionDepthProxy.Visible = false;
            return;
        }

        Vector3 center = GlobalPosition + new Vector3(0f, 0.08f, 0f) + camera.GlobalTransform.Basis.Z * InspectionDepthProxyCameraOffset;
        Vector3 scale = ResolveInspectionDepthProxyScale() * Mathf.Max(0.01f, Scale.X);
        _inspectionDepthProxy.GlobalTransform = new Transform3D(camera.GlobalTransform.Basis.Scaled(scale), center);
    }

    private bool ShouldUseInspectionDepthProxy()
    {
        string key = _item?.Shape?.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        return key is "tesseract"
            or "hyperphere"
            or "hypersphere"
            or "pentachoron"
            or "hypercone"
            or "impossiblecube"
            or "mobius"
            or "klein"
            or "boys";
    }

    private Vector3 ResolveInspectionDepthProxyScale()
    {
        string key = _item?.Shape?.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        return key switch
        {
            "hyperphere" or "hypersphere" => new Vector3(0.48f, 0.48f, 0.035f),
            "tesseract" => new Vector3(0.54f, 0.54f, 0.040f),
            "pentachoron" => new Vector3(0.50f, 0.50f, 0.040f),
            "hypercone" => new Vector3(0.52f, 0.52f, 0.040f),
            "impossiblecube" => new Vector3(0.56f, 0.56f, 0.045f),
            "mobius" => new Vector3(0.58f, 0.46f, 0.040f),
            "klein" => new Vector3(0.56f, 0.56f, 0.045f),
            "boys" => new Vector3(0.60f, 0.60f, 0.045f),
            _ => new Vector3(0.52f, 0.52f, 0.040f),
        };
    }

    private static ShaderMaterial CreateInspectionDepthProxyMaterial()
    {
        return new ShaderMaterial
        {
            Shader = GetInspectionDepthProxyShader(),
        };
    }

    private static Shader GetInspectionDepthProxyShader()
    {
        if (_inspectionDepthProxyShader != null)
        {
            return _inspectionDepthProxyShader;
        }

        _inspectionDepthProxyShader = new Shader
        {
            Code = @"shader_type spatial;
render_mode blend_mix, depth_draw_always, unshaded, cull_disabled;

void fragment() {
    ALBEDO = vec3(0.0);
    ALPHA = 0.0;
}",
        };
        return _inspectionDepthProxyShader;
    }

    private void ApplyModifierVisual()
    {
        ClearModifierVisual();

        if (_item == null || _itemVisual == null || _item.Shape == null || !ModifierVisualTheme.HasSpecialVisuals(_item))
        {
            return;
        }

        _modifierVisual = new ModifierVisualController
        {
            Name = "ModifierVisualController",
            Intensity = _highlighted ? 1.08f : 0.86f,
        };
        AddChild(_modifierVisual);
        _modifierVisual.Configure(_item, _itemVisual, _item.Shape, 0.78f, _glowLight, _highlighted ? 1.08f : 0.86f);

        if (_inspectionOverlayActive)
        {
            RefreshInspectionOverlayRenderLayers();
        }

        HubSceneController? hubSceneController = FindOwningHubSceneController();
        if (hubSceneController != null)
        {
            _modifierVisual.ElectricExternalAnchorPoolSize = 5;
            _modifierVisual.MinimumCharacterElectricAnchorPoolSlots = 1;
            _modifierVisual.ForceCharacterElectricAnchorAfterEnvironmentHits = 3;
            _modifierVisual.CollectEnvironmentElectricGlobalAnchors = anchors =>
            {
                if (_modifierVisual != null && IsInstanceValid(_modifierVisual))
                {
                    hubSceneController.CollectElectricEnvironmentAnchors(_modifierVisual.GlobalTransform.Origin, anchors);
                }
            };
            _modifierVisual.CollectCharacterElectricGlobalAnchors = hubSceneController.CollectElectricCharacterAnchors;
            _modifierVisual.ResolveElectricEnvironmentCollision = hubSceneController.ResolveElectricEnvironmentCollision;
        }
    }

    private HubSceneController? FindOwningHubSceneController()
    {
        Node? node = GetParent();
        while (node != null)
        {
            if (node is HubSceneController hubSceneController)
            {
                return hubSceneController;
            }

            node = node.GetParent();
        }

        return null;
    }

    private void ClearItemVisual()
    {
        ClearModifierVisual();

        if (_itemVisual == null)
        {
            return;
        }

        if (IsInstanceValid(_itemVisual))
        {
            _itemVisual.QueueFree();
        }

        _itemVisual = null;
        _inspectionOverlayActive = false;
        RestoreInspectionOverlayRenderLayers();
        RefreshInspectionDepthProxy();
    }

    private void ApplyInspectionOverlayRenderLayer(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node is VisualInstance3D visual)
        {
            if (!_inspectionOverlayOriginalLayers.ContainsKey(visual))
            {
                _inspectionOverlayOriginalLayers[visual] = visual.Layers;
            }

            visual.Layers = InspectionOverlayRenderLayerMask;
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyInspectionOverlayRenderLayer(child);
        }
    }

    private static void ApplyLocalGlowIgnoredRenderLayer(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node is VisualInstance3D visual && node is not Light3D)
        {
            visual.Layers = LocalGlowIgnoredRenderLayerMask;
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyLocalGlowIgnoredRenderLayer(child);
        }
    }

    private void RestoreInspectionOverlayRenderLayers()
    {
        foreach (KeyValuePair<VisualInstance3D, uint> entry in _inspectionOverlayOriginalLayers)
        {
            if (GodotObject.IsInstanceValid(entry.Key))
            {
                entry.Key.Layers = entry.Value;
            }
        }

        _inspectionOverlayOriginalLayers.Clear();
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

    private Color ResolveTintColor()
    {
        if (_item?.Rarity != null)
        {
            return _item.Rarity.UiColor;
        }

        return new Color(0.75f, 0.78f, 0.9f, 1f);
    }
}
