using Godot;

namespace SuperGatchaRebuild;

public partial class LaserBeamActor : Node3D
{
    public bool Active = false;
    public bool Telegraph = true;
    public Vector2 Start = Vector2.Zero;
    public Vector2 End = Vector2.Zero;
    public float BeamHalfWidth = 12f;
    public Color BeamColor = new Color(1f, 0.2f, 0.08f, 1f);

    private float _pulse = 0f;
    private MeshInstance3D _beamGlow = null!;
    private MeshInstance3D _beamCore = null!;
    private StandardMaterial3D _glowMat = null!;
    private StandardMaterial3D _coreMat = null!;
    private OmniLight3D _light = null!;

    public override void _Ready()
    {
        _glowMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 2.0f,
            Metallic = 0.05f,
            Roughness = 0.25f,
        };

        _coreMat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 3.0f,
            Metallic = 0.1f,
            Roughness = 0.2f,
        };

        _beamGlow = new MeshInstance3D();
        _beamGlow.Mesh = new BoxMesh();
        _beamGlow.MaterialOverride = _glowMat;
        _beamGlow.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_beamGlow);

        _beamCore = new MeshInstance3D();
        _beamCore.Mesh = new BoxMesh();
        _beamCore.MaterialOverride = _coreMat;
        _beamCore.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_beamCore);

        _light = new OmniLight3D();
        _light.LightColor = new Color(1f, 0.26f, 0.08f, 1f);
        _light.LightEnergy = 0f;
        _light.OmniRange = 40f;
        AddChild(_light);

        Disable();
    }

    public override void _Process(double delta)
    {
        if (!Active)
        {
            return;
        }

        _pulse += (float)delta;
        UpdateVisuals();
    }

    public void Disable()
    {
        Active = false;
        Visible = false;
        if (_light != null)
        {
            _light.LightEnergy = 0f;
        }
    }

    public void SetBeam(Vector2 start, Vector2 end, float halfWidth, Color color, bool telegraph)
    {
        if (!RuntimeMath.IsFinite(start) || !RuntimeMath.IsFinite(end))
        {
            Disable();
            return;
        }

        Start = start;
        End = end;
        BeamHalfWidth = Mathf.Max(0.01f, RuntimeMath.FiniteOr(halfWidth, 1f));
        BeamColor = color;
        Telegraph = telegraph;
        Active = true;
        Visible = true;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (!Active || _beamGlow == null)
        {
            return;
        }

        Vector2 span = End - Start;
        float len = span.Length();
        if (!RuntimeMath.IsFinite(len) || len < 1f)
        {
            return;
        }

        Vector2 dir = span / len;

        float width = Telegraph ? BeamHalfWidth * 0.75f : BeamHalfWidth;
        float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(_pulse * 6f));

        Color beamBase = new Color(BeamColor.R, BeamColor.G, BeamColor.B, 1f);
        Color glow = Telegraph
            ? new Color(beamBase.R, beamBase.G, beamBase.B, 0.12f + pulse * 0.18f)
            : new Color(beamBase.R, beamBase.G, beamBase.B, 0.26f);

        Color core = Telegraph
            ? new Color(beamBase.R, beamBase.G, beamBase.B, 0.55f + pulse * 0.35f)
            : new Color(beamBase.R, beamBase.G, beamBase.B, 0.98f);

        Vector2 mid = (Start + End) * 0.5f;
        Position = new Vector3(mid.X, 0.28f, mid.Y);
        Rotation = new Vector3(0f, Mathf.Atan2(dir.X, dir.Y), 0f);

        float glowW = width * 0.52f;
        float coreW = Mathf.Max(0.8f, width * 0.18f);

        BoxMesh glowMesh = (BoxMesh)_beamGlow.Mesh;
        glowMesh.Size = new Vector3(glowW, 0.28f, len);

        BoxMesh coreMesh = (BoxMesh)_beamCore.Mesh;
        coreMesh.Size = new Vector3(coreW, 0.12f, len);

        _glowMat.AlbedoColor = glow;
        _glowMat.Emission = new Color(glow.R, glow.G, glow.B, 1f);

        _coreMat.AlbedoColor = core;
        _coreMat.Emission = new Color(core.R, core.G, core.B, 1f);

        _light.LightColor = beamBase;
        _light.OmniRange = Mathf.Clamp(len * 0.55f, 30f, 260f);
        _light.LightEnergy = Telegraph ? 1.4f + pulse * 1.0f : 3.8f;
    }
}
