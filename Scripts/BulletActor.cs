using Godot;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild;

public partial class BulletActor : Node3D
{
    public Vector2 Velocity = Vector2.Zero;
    public float Damage = 8f;
    public float Radius = 1f;
    public TeamId Team = TeamId.Monster;
    public float Life = 4f;
    public Color BulletColor = Colors.White;
    public bool IsCriticalHit;
    public EquipmentItem? SourceItem;
    public bool Active = true;
    private Vector2 _position2D = Vector2.Zero;

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _mat = null!;

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
            base.Position = new Vector3(value.X, 0.45f, value.Y);
        }
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 1.4f,
            Metallic = 0.1f,
            Roughness = 0.25f,
        };

        _mesh = new MeshInstance3D();
        _mesh.Mesh = new SphereMesh { Radius = 0.65f, Height = 1.3f };
        _mesh.MaterialOverride = _mat;
        AddChild(_mesh);

        Position = _position2D;
        UpdateVisuals();
    }

    public override void _Process(double delta)
    {
        if (!Active)
        {
            return;
        }

        if (!RuntimeMath.IsFinite(Velocity))
        {
            Deactivate();
            return;
        }

        Position += Velocity * (float)delta;
        Life -= (float)delta;
        if (Life <= 0f)
        {
            Deactivate();
            return;
        }

        UpdateVisuals();
    }

    public void Configure(Vector2 pos, Vector2 vel, float dmg, TeamId team, Color col, float radius, float life = 4f, bool isCriticalHit = false, EquipmentItem? sourceItem = null)
    {
        Position = RuntimeMath.FiniteOr(pos, Position);
        Velocity = RuntimeMath.FiniteOr(vel, Vector2.Zero);
        Damage = Mathf.Max(0f, RuntimeMath.FiniteOr(dmg, 0f));
        Team = team;
        BulletColor = col;
        IsCriticalHit = isCriticalHit;
        SourceItem = sourceItem;
        Radius = Mathf.Clamp(RuntimeMath.FiniteOr(radius, 1f), 0.05f, 80f);
        Life = Mathf.Max(0.01f, RuntimeMath.FiniteOr(life, 4f));
        Active = true;
        if (_mesh != null)
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
        QueueFree();
    }

    private void UpdateVisuals()
    {
        if (_mesh == null || _mat == null)
        {
            return;
        }

        _mesh.Visible = Active;
        float s = Mathf.Clamp(RuntimeMath.FiniteOr(Radius, 1f) * 0.75f, 0.04f, 80f);
        _mesh.Scale = new Vector3(s, s, s);
        _mat.AlbedoColor = BulletColor;
        _mat.Emission = BulletColor;
    }
}
