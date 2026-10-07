using Godot;

namespace SuperGatchaRebuild;

public partial class PlayerActor : Node3D
{
    public float MaxHp = 100f;
    public float Hp = 100f;
    public float MoveSpeed = 19f;
    public float Radius = 1.2f;

    public bool BubbleActive = false;
    public float BubbleHp = 0f;
    public float BubbleRadius = 15f;

    public int BuffShieldCharges = 0;
    public float Overshield = 0f;
    public int ShieldedPieceCount = 0;

    public float FireRate = 8f;
    public float BulletSpeed = 80f;
    public float BulletDamage = 22f;

    private float _shootCd = 0f;
    private float _hurtFlash = 0f;
    private Vector2 _position2D = Vector2.Zero;

    private MeshInstance3D _bodyMesh = null!;
    private MeshInstance3D _coreMesh = null!;
    private MeshInstance3D _bubbleMesh = null!;
    private StandardMaterial3D _bodyMat = null!;
    private StandardMaterial3D _coreMat = null!;
    private StandardMaterial3D _bubbleMat = null!;
    private ShapeKind _shapeKind = ShapeKind.CoreCube;
    private RarityTier _shapeRarity = RarityTier.Common;

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
            base.Position = new Vector3(value.X, 1.0f, value.Y);
        }
    }

    public override void _Ready()
    {
        BuildVisuals();
        Position = _position2D;
        UpdateVisuals();
    }

    public override void _Process(double delta)
    {
        if (_shootCd > 0f)
        {
            _shootCd -= (float)delta;
        }

        if (_hurtFlash > 0f)
        {
            _hurtFlash -= (float)delta;
        }

        UpdateVisuals();
    }

    public bool ConsumeShootWindow()
    {
        if (_shootCd > 0f)
        {
            return false;
        }

        _shootCd = 1f / FireRate;
        return true;
    }

    public void TriggerHurtFlash()
    {
        _hurtFlash = 0.15f;
    }

    public void GrantBubble(float hp)
    {
        BubbleActive = true;
        BubbleHp = hp;
    }

    public void ResetStats()
    {
        Hp = MaxHp;
        BubbleActive = false;
        BubbleHp = 0f;
        BuffShieldCharges = 0;
        Overshield = 0f;
        ShieldedPieceCount = 0;
        _shootCd = 0f;
    }

    public void ApplyShape(ShapeKind kind, RarityTier rarity)
    {
        _shapeKind = kind;
        _shapeRarity = rarity;
        RebuildBodyMesh();
        UpdateVisuals();
    }

    private void BuildVisuals()
    {
        _bodyMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.65f, 1f, 1f),
            Metallic = 0.25f,
            Roughness = 0.35f,
        };
        _coreMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.15f, 1f, 1f, 0.95f),
            EmissionEnabled = true,
            Emission = new Color(0.1f, 0.95f, 1f, 1f),
            EmissionEnergyMultiplier = 1.6f,
            Metallic = 0.1f,
            Roughness = 0.25f,
        };
        _bubbleMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.95f, 1f, 0.12f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = new Color(0.18f, 0.95f, 1f, 0.8f),
            EmissionEnergyMultiplier = 1.2f,
            Metallic = 0.05f,
            Roughness = 0.35f,
        };

        _bodyMesh = new MeshInstance3D();
        _bodyMesh.MaterialOverride = _bodyMat;
        AddChild(_bodyMesh);

        _coreMesh = new MeshInstance3D();
        _coreMesh.Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f };
        _coreMesh.Position = new Vector3(0f, -0.5f, 0f);
        _coreMesh.MaterialOverride = _coreMat;
        AddChild(_coreMesh);

        _bubbleMesh = new MeshInstance3D();
        _bubbleMesh.Mesh = new SphereMesh { Radius = 1f, Height = 2f };
        _bubbleMesh.MaterialOverride = _bubbleMat;
        _bubbleMesh.Position = new Vector3(0f, 0.4f, 0f);
        AddChild(_bubbleMesh);

        RebuildBodyMesh();
    }

    private void RebuildBodyMesh()
    {
        if (_bodyMesh == null)
        {
            return;
        }

        switch (_shapeKind)
        {
            case ShapeKind.StrikerPyramid:
                _bodyMesh.Mesh = new CylinderMesh
                {
                    TopRadius = 0.02f,
                    BottomRadius = 0.95f,
                    Height = 3.4f,
                    RadialSegments = 6,
                };
                _bodyMesh.RotationDegrees = new Vector3(0f, 0f, -90f);
                break;
            case ShapeKind.BulwarkSphere:
                _bodyMesh.Mesh = new SphereMesh { Radius = 1.35f, Height = 2.7f };
                _bodyMesh.RotationDegrees = Vector3.Zero;
                break;
            default:
                _bodyMesh.Mesh = new BoxMesh { Size = new Vector3(2.4f, 3.6f, 2.4f) };
                _bodyMesh.RotationDegrees = Vector3.Zero;
                break;
        }
    }

    private void UpdateVisuals()
    {
        if (_bodyMat == null)
        {
            return;
        }

        Color body = _hurtFlash > 0f ? Colors.White : new Color(0.2f, 0.65f, 1f, 1f);
        float tier = _shapeRarity switch
        {
            RarityTier.Uncommon => 0.08f,
            RarityTier.Rare => 0.18f,
            RarityTier.Epic => 0.28f,
            RarityTier.Legendary => 0.40f,
            RarityTier.Mythic => 0.52f,
            _ => 0f,
        };
        body = body.Lerp(new Color(1f, 0.86f, 0.42f, 1f), tier);
        _bodyMat.AlbedoColor = body;
        _coreMat.AlbedoColor = new Color(0.15f, 1f, 1f, 0.95f);

        _bubbleMesh.Visible = BubbleActive;
        if (BubbleActive)
        {
            float s = Mathf.Max(0.1f, RuntimeMath.FiniteOr(BubbleRadius, 15f));
            _bubbleMesh.Scale = new Vector3(s, s * 0.55f, s);
        }
    }
}
