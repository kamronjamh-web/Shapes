using Godot;
using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild;

public partial class PickupActor : Node3D
{
    private enum RewardVisualTier
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
        Mythic = 5,
        Transcendent = 6,
    }

    public PickupKind Kind = PickupKind.XpOrb;
    public RarityTier Rarity = RarityTier.Common;
    public int Value = 1;
    public float Radius = 8f;
    public Vector2 Velocity = Vector2.Zero;
    public float Life = 24f;
    public bool Active = true;
    public EquipmentItem? EquipmentItem { get; private set; }
    public ModifierData? EssenceData { get; private set; }
    public WeaponModulatorData? ModulatorData { get; private set; }
    public bool CanAttemptCollection => Active && !_collectionInProgress && _collectionRetryCooldown <= 0f;
    public bool SupportsValueFusion => Kind == PickupKind.XpOrb;
    public readonly List<ModifierRoll> Modifiers = new();

    private static Mesh? _defaultPickupMesh;
    private static Mesh? _xpPickupMesh;
    private static Mesh? _coinPickupMesh;

    private Vector2 _position2D = Vector2.Zero;
    private MeshInstance3D _mesh = null!;
    private MeshInstance3D _rarityPillar = null!;
    private MeshInstance3D _beaconCore = null!;
    private MeshInstance3D _beaconBaseRing = null!;
    private Node3D? _shapeVisual;
    private OmniLight3D _beaconLight = null!;
    private OmniLight3D _dropLight = null!;
    private StandardMaterial3D _mat = null!;
    private StandardMaterial3D _pillarMat = null!;
    private StandardMaterial3D _beaconCoreMat = null!;
    private StandardMaterial3D _beaconRingMat = null!;
    private Vector3 _shapeVisualBaseScale = Vector3.One;
    private Vector3 _shapeVisualBaseRotation = Vector3.Zero;
    private bool _collectionInProgress;
    private float _collectionRetryCooldown;
    private float _age;

    public new Vector2 Position
    {
        get => _position2D;
        set
        {
            if (!RuntimeMath.IsFinite(value))
            {
                return;
            }

            _position2D = value;
            base.Position = new Vector3(value.X, 0.42f, value.Y);
        }
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.8f,
            Metallic = 0.15f,
            Roughness = 0.25f,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
        };

        _mesh = new MeshInstance3D
        {
            Mesh = ResolvePickupMesh(Kind),
            MaterialOverride = _mat,
        };
        AddChild(_mesh);

        _pillarMat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.3f,
            Metallic = 0.05f,
            Roughness = 0.42f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _rarityPillar = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 32 },
            MaterialOverride = _pillarMat,
        };
        AddChild(_rarityPillar);

        _beaconCoreMat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 2.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _beaconCore = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 24 },
            MaterialOverride = _beaconCoreMat,
        };
        AddChild(_beaconCore);

        _beaconRingMat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 2.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _beaconBaseRing = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 48 },
            MaterialOverride = _beaconRingMat,
        };
        AddChild(_beaconBaseRing);

        _beaconLight = new OmniLight3D
        {
            Visible = false,
            LightEnergy = 0f,
            OmniRange = 6f,
        };
        AddChild(_beaconLight);

        _dropLight = new OmniLight3D
        {
            Visible = false,
            LightEnergy = 0f,
            OmniRange = 8f,
        };
        AddChild(_dropLight);

        Position = _position2D;
        RefreshPickupMesh();
        if (Kind == PickupKind.ShapeItem && EquipmentItem != null && _shapeVisual == null)
        {
            ReplaceShapeItemVisual();
        }

        UpdateVisuals();
    }

    public override void _Process(double delta)
    {
        if (!Active)
        {
            return;
        }

        float dt = (float)delta;
        _age += dt;
        _collectionRetryCooldown = Mathf.Max(0f, _collectionRetryCooldown - dt);
        Position += Velocity * dt;
        Velocity *= Mathf.Pow(0.92f, dt * 60f);

        Life -= dt;
        if (Life <= 0f)
        {
            Deactivate();
            return;
        }

        UpdateVisuals();
    }

    public void Configure(
        PickupKind kind,
        int value,
        Vector2 pos,
        Vector2 impulse,
        RarityTier rarity = RarityTier.Common,
        IList<ModifierRoll>? modifiers = null)
    {
        Kind = kind;
        EquipmentItem = null;
        EssenceData = null;
        ModulatorData = null;
        ClearShapeItemVisual();
        Rarity = rarity;
        Value = value;
        Position = pos;
        Velocity = RuntimeMath.FiniteOr(impulse, Vector2.Zero);
        Active = true;
        _collectionInProgress = false;
        _collectionRetryCooldown = 0f;
        Life = 24f;
        _age = 0f;
        Modifiers.Clear();
        if (modifiers != null)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                Modifiers.Add(modifiers[i]);
            }
        }

        RefreshRewardState();
        RefreshPickupMesh();
        UpdateVisuals();
    }

    public void ConfigureShapeItem(EquipmentItem item, Vector2 pos, Vector2 impulse)
    {
        EquipmentItem = item;
        EssenceData = null;
        ModulatorData = null;
        Kind = PickupKind.ShapeItem;
        Rarity = MapEquipmentRarity(item.Rarity?.Key);
        Value = 0;
        Position = pos;
        Velocity = RuntimeMath.FiniteOr(impulse, Vector2.Zero);
        Active = true;
        _collectionInProgress = false;
        _collectionRetryCooldown = 0f;
        Life = 180f;
        Radius = 12f + GetEquipmentRarityRank(item.Rarity) * 1.1f;
        _age = 0f;
        Modifiers.Clear();

        RefreshPickupMesh();
        ReplaceShapeItemVisual();
        UpdateVisuals();
    }

    public void ConfigureEssence(ModifierData essence, Vector2 pos, Vector2 impulse)
    {
        EquipmentItem = null;
        EssenceData = essence;
        ModulatorData = null;
        ClearShapeItemVisual();
        Kind = PickupKind.Essence;
        Rarity = RarityTier.Mythic;
        Value = 0;
        Position = pos;
        Velocity = RuntimeMath.FiniteOr(impulse, Vector2.Zero);
        Active = true;
        _collectionInProgress = false;
        _collectionRetryCooldown = 0f;
        Life = 90f;
        Radius = 10f;
        _age = 0f;
        Modifiers.Clear();
        RefreshPickupMesh();
        UpdateVisuals();
    }

    public void ConfigureModulator(WeaponModulatorData modulator, Vector2 pos, Vector2 impulse)
    {
        EquipmentItem = null;
        EssenceData = null;
        ModulatorData = modulator;
        ClearShapeItemVisual();
        Kind = PickupKind.Modulator;
        Rarity = RarityTier.Rare;
        Value = 0;
        Position = pos;
        Velocity = RuntimeMath.FiniteOr(impulse, Vector2.Zero);
        Active = true;
        _collectionInProgress = false;
        _collectionRetryCooldown = 0f;
        Life = 90f;
        Radius = 9f;
        _age = 0f;
        Modifiers.Clear();
        RefreshPickupMesh();
        UpdateVisuals();
    }

    public void AbsorbPickup(PickupActor other)
    {
        if (other == null || !other.Active || other.Kind != Kind || !SupportsValueFusion)
        {
            return;
        }

        Value = Mathf.Max(1, Value + other.Value);
        Position = (Position + other.Position) * 0.5f;
        Velocity = (Velocity * 0.65f + other.Velocity * 0.35f) * 0.72f;
        Life = Mathf.Max(Life, other.Life);
        if ((int)other.Rarity > (int)Rarity)
        {
            Rarity = other.Rarity;
        }

        if (Kind == PickupKind.XpOrb && other.Modifiers.Count > 0)
        {
            for (int i = 0; i < other.Modifiers.Count; i++)
            {
                Modifiers.Add(other.Modifiers[i]);
            }
        }

        RefreshRewardState();
        RefreshPickupMesh();
        UpdateVisuals();
    }

    public void ApplyMagnet(Vector2 target, float dt)
    {
        if (Kind == PickupKind.ShapeItem)
        {
            return;
        }

        Vector2 to = target - Position;
        float dist = Mathf.Max(0.001f, to.Length());
        if (dist > 360f)
        {
            return;
        }

        float t = 1f - Mathf.Clamp(dist / 360f, 0f, 1f);
        float accel = Mathf.Lerp(260f, 1500f, t);
        Velocity += (to / dist) * accel * dt;
    }

    public bool TryBeginCollection()
    {
        if (!CanAttemptCollection)
        {
            return false;
        }

        _collectionInProgress = true;
        if (Kind == PickupKind.ShapeItem)
        {
            HideShapeDropBeacon();
        }

        return true;
    }

    public void CancelCollectionAttempt(float retryDelay)
    {
        _collectionInProgress = false;
        _collectionRetryCooldown = Mathf.Max(0.05f, retryDelay);
        if (Active)
        {
            UpdateVisuals();
        }
    }

    public void Deactivate()
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        _collectionInProgress = false;
        _collectionRetryCooldown = 0f;
        if (_mesh != null)
        {
            _mesh.Visible = false;
        }

        HideShapeDropBeacon();
        QueueFree();
    }

    private void UpdateVisuals()
    {
        if (_mesh == null || _mat == null)
        {
            return;
        }

        if (Kind == PickupKind.ShapeItem)
        {
            UpdateShapeItemVisuals();
            return;
        }

        RefreshRewardState();

        RewardVisualTier rewardTier = ResolveRewardVisualTier();
        Color c = Kind switch
        {
            PickupKind.Coin => new Color(1f, 0.82f, 0.16f, 1f),
            PickupKind.XpOrb => GetRewardTierColor(rewardTier),
            PickupKind.Heal => new Color(0.35f, 1f, 0.35f, 1f),
            PickupKind.Essence => (EssenceData?.UiColor ?? new Color(0.95f, 0.84f, 0.30f, 1f)).Lightened(0.16f),
            PickupKind.Modulator => ModulatorData?.UiColor ?? new Color(0.42f, 0.84f, 1f, 1f),
            _ => GetRarityColor(Rarity),
        };
        c.A = 1f;

        _mesh.Visible = Active;
        _mesh.Position = Kind switch
        {
            PickupKind.XpOrb => new Vector3(0f, 0.26f + Mathf.Sin(_age * 3.8f) * 0.12f, 0f),
            PickupKind.Coin => new Vector3(0f, 0.22f + Mathf.Sin(_age * 3.2f) * 0.10f, 0f),
            _ => Vector3.Zero,
        };
        float s = Radius * 0.16f;
        if (Kind == PickupKind.Essence)
        {
            s *= 1.2f;
        }
        else if (Kind == PickupKind.Modulator)
        {
            s *= 1.1f;
        }
        else if (Kind == PickupKind.XpOrb)
        {
            s *= 1.08f;
        }
        else if (Kind == PickupKind.Coin)
        {
            s *= 1.04f;
        }

        if (Kind is PickupKind.XpOrb or PickupKind.Coin)
        {
            s *= 0.5f;
        }

        _mesh.Scale = new Vector3(s, s, s);
        _mesh.Rotation = Kind switch
        {
            PickupKind.XpOrb => new Vector3(_age * 1.85f, _age * 2.35f, _age * 1.45f),
            PickupKind.Coin => new Vector3(_age * 1.45f, _age * 2.75f, _age * 1.92f),
            _ => new Vector3(0f, _age * 1.8f, 0f),
        };
        _mat.AlbedoColor = c;
        _mat.Emission = c;
        _mat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
        _mat.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        _mat.EmissionEnergyMultiplier = Kind switch
        {
            PickupKind.XpOrb => 2.4f + (int)rewardTier * 0.26f,
            PickupKind.Coin => 2.0f + (int)rewardTier * 0.24f,
            PickupKind.Essence => 2.3f,
            PickupKind.Modulator => 2.1f,
            _ => 1.8f,
        };
        _mat.Metallic = Kind == PickupKind.Coin ? 0.70f : Kind == PickupKind.XpOrb ? 0.24f : 0.15f;
        _mat.Roughness = Kind == PickupKind.Coin ? 0.14f : Kind == PickupKind.XpOrb ? 0.18f : 0.25f;

        HideShapePickupPresentation();

        _dropLight.Visible = false;
    }

    private void UpdateShapeItemVisuals()
    {
        Color rarityColor = ResolveEquipmentRarityColor(EquipmentItem?.Rarity);
        int rank = GetEquipmentRarityRank(EquipmentItem?.Rarity);
        float pulse = 0.5f + Mathf.Sin(_age * 4.6f) * 0.5f;
        float slowPulse = 0.5f + Mathf.Sin(_age * 1.7f) * 0.5f;
        float beamHeight = GetLootBeamHeight(rank);
        float beamRadius = GetLootBeamRadius(rank, pulse);
        float coreRadius = beamRadius * 0.30f;
        float ringRadius = GetLootRingRadius(rank, slowPulse);
        float bob = Mathf.Sin(_age * 3.2f) * 0.08f;
        float itemHeight = 1.12f + rank * 0.08f + bob;
        float beamCenterY = beamHeight * 0.5f + 0.18f;

        _mesh.Visible = false;

        if (_shapeVisual == null && EquipmentItem != null)
        {
            ReplaceShapeItemVisual();
        }

        if (_shapeVisual != null && IsInstanceValid(_shapeVisual))
        {
            _shapeVisual.Visible = Active;
            float scale = 0.94f + rank * 0.04f;
            _shapeVisual.Position = new Vector3(0f, itemHeight, 0f);
            _shapeVisual.Scale = _shapeVisualBaseScale * scale;
            _shapeVisual.Rotation = ShapeVisualFactory.UsesNativeAnimation(_shapeVisual)
                ? _shapeVisualBaseRotation
                : _shapeVisualBaseRotation + new Vector3(0f, _age * 1.9f, 0f);
        }

        _rarityPillar.Visible = Active;
        _rarityPillar.Position = new Vector3(0f, beamCenterY, 0f);
        _rarityPillar.Scale = new Vector3(beamRadius, beamHeight, beamRadius);

        _beaconCore.Visible = Active;
        _beaconCore.Position = new Vector3(0f, beamCenterY, 0f);
        _beaconCore.Scale = new Vector3(coreRadius, beamHeight * 1.06f, coreRadius);

        _beaconBaseRing.Visible = Active;
        _beaconBaseRing.Position = new Vector3(0f, -0.37f, 0f);
        _beaconBaseRing.Scale = new Vector3(ringRadius, 0.03f, ringRadius);

        Color hotColor = rarityColor.Lightened(0.20f);
        _mat.AlbedoColor = hotColor;
        _mat.Emission = hotColor;
        _mat.EmissionEnergyMultiplier = 3.4f + rank * 0.42f + pulse * 0.85f;

        _pillarMat.AlbedoColor = new Color(rarityColor.R, rarityColor.G, rarityColor.B, 0.10f + pulse * 0.06f);
        _pillarMat.Emission = rarityColor;
        _pillarMat.EmissionEnergyMultiplier = 1.8f + rank * 0.18f + pulse * 0.48f;

        _beaconCoreMat.AlbedoColor = new Color(rarityColor.R, rarityColor.G, rarityColor.B, 0.40f + pulse * 0.16f);
        _beaconCoreMat.Emission = rarityColor.Lightened(0.28f);
        _beaconCoreMat.EmissionEnergyMultiplier = 3.0f + rank * 0.24f + pulse * 0.72f;

        _beaconRingMat.AlbedoColor = new Color(rarityColor.R, rarityColor.G, rarityColor.B, 0.20f + slowPulse * 0.12f);
        _beaconRingMat.Emission = rarityColor;
        _beaconRingMat.EmissionEnergyMultiplier = 2.1f + rank * 0.16f + slowPulse * 0.45f;

        _beaconLight.Visible = Active;
        _beaconLight.LightColor = rarityColor.Lightened(0.24f);
        _beaconLight.LightEnergy = 2.0f + rank * 0.45f + pulse * 0.75f;
        _beaconLight.OmniRange = 12f + rank * 1.8f;
        _beaconLight.Position = new Vector3(0f, itemHeight + 1.2f, 0f);

        _dropLight.Visible = Active;
        _dropLight.LightColor = rarityColor.Lightened(0.35f);
        _dropLight.LightEnergy = 2.6f + rank * 0.5f + pulse * 0.95f;
        _dropLight.OmniRange = 10f + rank * 1.25f;
        _dropLight.Position = new Vector3(0f, itemHeight, 0f);
    }

    private void ReplaceShapeItemVisual()
    {
        ClearShapeItemVisual();

        Color rarityColor = ResolveEquipmentRarityColor(EquipmentItem?.Rarity);
        _shapeVisual = ShapeVisualFactory.CreateVisual(EquipmentItem?.Shape, rarityColor);
        _shapeVisual.Name = "ShapeDropVisual";
        _shapeVisualBaseScale = _shapeVisual.Scale;
        _shapeVisualBaseRotation = _shapeVisual.Rotation;
        AddChild(_shapeVisual);

        if (_mesh != null)
        {
            _mesh.Visible = false;
        }
    }

    private void ClearShapeItemVisual()
    {
        if (_shapeVisual == null)
        {
            return;
        }

        if (IsInstanceValid(_shapeVisual))
        {
            _shapeVisual.QueueFree();
        }

        _shapeVisual = null;
        _shapeVisualBaseScale = Vector3.One;
        _shapeVisualBaseRotation = Vector3.Zero;
    }

    private void HideShapeDropBeacon()
    {
        if (_shapeVisual != null && IsInstanceValid(_shapeVisual))
        {
            _shapeVisual.Visible = false;
        }

        if (_rarityPillar != null)
        {
            _rarityPillar.Visible = false;
        }

        if (_beaconCore != null)
        {
            _beaconCore.Visible = false;
        }

        if (_beaconBaseRing != null)
        {
            _beaconBaseRing.Visible = false;
        }

        if (_beaconLight != null)
        {
            _beaconLight.Visible = false;
        }

        if (_dropLight != null)
        {
            _dropLight.Visible = false;
        }
    }

    private void HideShapePickupPresentation()
    {
        if (_shapeVisual != null && IsInstanceValid(_shapeVisual))
        {
            _shapeVisual.Visible = false;
        }

        if (_rarityPillar != null)
        {
            _rarityPillar.Visible = false;
        }

        if (_beaconCore != null)
        {
            _beaconCore.Visible = false;
        }

        if (_beaconBaseRing != null)
        {
            _beaconBaseRing.Visible = false;
        }

        if (_beaconLight != null)
        {
            _beaconLight.Visible = false;
        }
    }

    private void RefreshRewardState()
    {
        Radius = Kind switch
        {
            PickupKind.XpOrb => 8f * (1f + (int)ResolveRewardVisualTier() * 0.075f),
            PickupKind.Coin => 7f * (1f + (int)ResolveRewardVisualTier() * 0.075f),
            PickupKind.Heal => 9f,
            PickupKind.Essence => 10f,
            PickupKind.Modulator => 9f,
            _ => Radius,
        };
    }

    private RewardVisualTier ResolveRewardVisualTier()
    {
        return Kind switch
        {
            PickupKind.XpOrb => Value switch
            {
                >= 520 => RewardVisualTier.Transcendent,
                >= 320 => RewardVisualTier.Mythic,
                >= 180 => RewardVisualTier.Legendary,
                >= 96 => RewardVisualTier.Epic,
                >= 48 => RewardVisualTier.Rare,
                >= 24 => RewardVisualTier.Uncommon,
                _ => RewardVisualTier.Common,
            },
            PickupKind.Coin => Value switch
            {
                >= 64 => RewardVisualTier.Transcendent,
                >= 32 => RewardVisualTier.Mythic,
                >= 16 => RewardVisualTier.Legendary,
                >= 8 => RewardVisualTier.Epic,
                >= 4 => RewardVisualTier.Rare,
                >= 2 => RewardVisualTier.Uncommon,
                _ => RewardVisualTier.Common,
            },
            _ => RewardVisualTier.Common,
        };
    }

    private static Color GetRewardTierColor(RewardVisualTier tier)
    {
        return tier switch
        {
            RewardVisualTier.Common => new Color(1f, 1f, 1f, 1f),
            RewardVisualTier.Uncommon => new Color(0.38f, 1f, 0.45f, 1f),
            RewardVisualTier.Rare => new Color(0.25f, 0.62f, 1f, 1f),
            RewardVisualTier.Epic => new Color(0.78f, 0.42f, 1f, 1f),
            RewardVisualTier.Legendary => new Color(0.95f, 0.24f, 0.22f, 1f),
            RewardVisualTier.Mythic => new Color(0.9922f, 0.8471f, 0.2078f, 1f),
            RewardVisualTier.Transcendent => new Color(0.48f, 1f, 1f, 1f),
            _ => Colors.White,
        };
    }

    private void RefreshPickupMesh()
    {
        if (_mesh == null)
        {
            return;
        }

        _mesh.Mesh = ResolvePickupMesh(Kind);
    }

    private static Mesh ResolvePickupMesh(PickupKind kind)
    {
        return kind switch
        {
            PickupKind.XpOrb => _xpPickupMesh ??= CreateBipyramidMesh(),
            PickupKind.Coin => _coinPickupMesh ??= CreateIcosahedronMesh(),
            _ => _defaultPickupMesh ??= new SphereMesh { Radius = 0.75f, Height = 1.5f },
        };
    }

    private static ArrayMesh CreateBipyramidMesh()
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        Vector3 top = new(0f, 1.25f, 0f);
        Vector3 bottom = new(0f, -1.25f, 0f);
        Vector3 front = new(0f, 0f, 1f);
        Vector3 right = new(1f, 0f, 0f);
        Vector3 back = new(0f, 0f, -1f);
        Vector3 left = new(-1f, 0f, 0f);

        AddTriangle(surfaceTool, top, front, right);
        AddTriangle(surfaceTool, top, right, back);
        AddTriangle(surfaceTool, top, back, left);
        AddTriangle(surfaceTool, top, left, front);
        AddTriangle(surfaceTool, bottom, right, front);
        AddTriangle(surfaceTool, bottom, back, right);
        AddTriangle(surfaceTool, bottom, left, back);
        AddTriangle(surfaceTool, bottom, front, left);

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    private static ArrayMesh CreateIcosahedronMesh()
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] vertices =
        {
            new(-1f, phi, 0f),
            new(1f, phi, 0f),
            new(-1f, -phi, 0f),
            new(1f, -phi, 0f),
            new(0f, -1f, phi),
            new(0f, 1f, phi),
            new(0f, -1f, -phi),
            new(0f, 1f, -phi),
            new(phi, 0f, -1f),
            new(phi, 0f, 1f),
            new(-phi, 0f, -1f),
            new(-phi, 0f, 1f),
        };

        for (int index = 0; index < vertices.Length; index++)
        {
            vertices[index] = vertices[index].Normalized();
        }

        int[] triangleIndices =
        {
            0, 11, 5,
            0, 5, 1,
            0, 1, 7,
            0, 7, 10,
            0, 10, 11,
            1, 5, 9,
            5, 11, 4,
            11, 10, 2,
            10, 7, 6,
            7, 1, 8,
            3, 9, 4,
            3, 4, 2,
            3, 2, 6,
            3, 6, 8,
            3, 8, 9,
            4, 9, 5,
            2, 4, 11,
            6, 2, 10,
            8, 6, 7,
            9, 8, 1,
        };

        for (int triangleIndex = 0; triangleIndex < triangleIndices.Length; triangleIndex += 3)
        {
            AddTriangle(
                surfaceTool,
                vertices[triangleIndices[triangleIndex]],
                vertices[triangleIndices[triangleIndex + 1]],
                vertices[triangleIndices[triangleIndex + 2]]);
        }

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    private static void AddTriangle(SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c)
    {
        surfaceTool.AddVertex(a);
        surfaceTool.AddVertex(b);
        surfaceTool.AddVertex(c);
    }

    private static Color GetRarityColor(RarityTier rarity)
    {
        return rarity switch
        {
            RarityTier.Common => new Color(1f, 1f, 1f, 1f),
            RarityTier.Uncommon => new Color(0.38f, 1f, 0.45f, 1f),
            RarityTier.Rare => new Color(0.25f, 0.62f, 1f, 1f),
            RarityTier.Epic => new Color(0.78f, 0.42f, 1f, 1f),
            RarityTier.Legendary => new Color(0.95f, 0.24f, 0.22f, 1f),
            RarityTier.Mythic => new Color(0.9922f, 0.8471f, 0.2078f, 1f),
            _ => new Color(1f, 1f, 1f, 1f),
        };
    }

    private static Color ResolveEquipmentRarityColor(RarityData? rarity)
    {
        if (rarity != null)
        {
            Color c = rarity.UiColor;
            c.A = 1f;
            return c;
        }

        return GetRarityColor(RarityTier.Common);
    }

    private static float GetLootBeamHeight(int rarityRank)
    {
        float t = Mathf.Clamp(rarityRank, 0, 5) / 5f;
        return Mathf.Lerp(10f, 104f, Mathf.Pow(t, 1.18f));
    }

    private static float GetLootBeamRadius(int rarityRank, float pulse)
    {
        float t = Mathf.Clamp(rarityRank, 0, 5) / 5f;
        return Mathf.Lerp(0.28f, 1.65f, Mathf.Pow(t, 1.08f)) + pulse * 0.05f;
    }

    private static float GetLootRingRadius(int rarityRank, float slowPulse)
    {
        float t = Mathf.Clamp(rarityRank, 0, 5) / 5f;
        return Mathf.Lerp(0.95f, 3.2f, Mathf.Pow(t, 1.04f)) + slowPulse * 0.16f;
    }
    private static int GetEquipmentRarityRank(RarityData? rarity)
    {
        return EquipmentCatalogDefaults.NormalizeRarityKey(rarity?.Key) switch
        {
            "uncommon" => 1,
            "rare" => 2,
            "legendary" => 3,
            "mythic" => 4,
            "divine" => 5,
            _ => 0,
        };
    }

    private static RarityTier MapEquipmentRarity(string? key)
    {
        return EquipmentCatalogDefaults.NormalizeRarityKey(key) switch
        {
            "uncommon" => RarityTier.Uncommon,
            "rare" => RarityTier.Rare,
            "legendary" => RarityTier.Epic,
            "mythic" => RarityTier.Legendary,
            "divine" => RarityTier.Mythic,
            _ => RarityTier.Common,
        };
    }
}
