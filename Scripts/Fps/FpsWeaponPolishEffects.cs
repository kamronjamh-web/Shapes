using System;
using Godot;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

/// <summary>Bounded, cosmetic geometry. Never queries or mutates the combat simulation.</summary>
public partial class FpsWeaponPolishEffects : Node3D
{
    public const int MaxTransientInstances = 384;
    public const int MaxPersistentSegments = 1024;
    public int ActiveTransientCount => _count;
    public int VisibleSegmentCount => _segments;
    public float SimulationTime { get; private set; }
    public float Detail { get; set; } = 1f;
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; if (!value) Clear(); }
    }

    private struct Fragment
    {
        public Vector3 Position, Velocity, Size, Rotation, Spin;
        public Color Color;
        public float Age, Life, Gravity;
        public Basis Basis;
        public bool Line;
        public bool NearSafe;
    }

    private readonly Fragment[] _fragments = new Fragment[MaxTransientInstances];
    private readonly RandomNumberGenerator _random = new() { Seed = 0x57465058 };
    private MultiMesh _particles = null!;
    private MultiMesh _lines = null!;
    private int _count, _segments;
    private float _impactCredit = 12f, _trailCredit = 48f;
    private Vector3 _viewer;
    private bool _enabled = true;
    private static Mesh? _sawMesh;
    public static Mesh SawbladeMesh => _sawMesh ??= BuildSawbladeMesh();
    public static bool Supports(WeaponAttackType type) => (int)type is >= 0 and <= 15;

    public override void _Ready()
    {
        _particles = CreateBatch("FragmentsAndTrails", MaxTransientInstances);
        _lines = CreateBatch("FieldOutlines", MaxPersistentSegments);
        BuildRemainingBatch();
    }

    private MultiMesh CreateBatch(string name, int capacity)
    {
        // Instance colors provide the fade; one material and one mesh per batch.
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = Colors.White,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MultiMesh batch = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D
        {
            Name = name, Multimesh = batch,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return batch;
    }

    public void BeginFrame(float dt, Vector3 viewerPosition)
    {
        if (!float.IsFinite(dt) || dt < 0f) return;
        _viewer = viewerPosition;
        _segments = 0;
        _solidCount = 0;
        if (!Enabled) return;
        SimulationTime += dt;
        AdvanceBursts(dt);
        float detail = Mathf.Clamp(float.IsFinite(Detail) ? Detail : 1f, 0f, 1f);
        _impactCredit = Mathf.Min(16f, _impactCredit + dt * 90f * detail);
        _trailCredit = Mathf.Min(64f, _trailCredit + dt * 720f * detail);
        for (int i = _count - 1; i >= 0; i--)
        {
            ref Fragment part = ref _fragments[i];
            part.Age += dt;
            if (part.Age >= part.Life)
            {
                _fragments[i] = _fragments[--_count];
                continue;
            }
            part.Velocity += Vector3.Down * part.Gravity * dt;
            part.Position += part.Velocity * dt;
            part.Rotation += part.Spin * dt;
        }
    }

    public void EndFrame()
    {
        if (_particles == null) return;
        for (int i = 0; i < _count; i++)
        {
            ref Fragment part = ref _fragments[i];
            float remaining = 1f - part.Age / part.Life;
            float fade = Mathf.Min(1f, remaining * 3f);
            Vector3 size = part.Size * (part.Line ? new Vector3(fade, fade, 1f) : Vector3.One * (.3f + .7f * fade));
            Basis basis = (part.Line ? part.Basis : Basis.FromEuler(part.Rotation)) * Basis.FromScale(size);
            Vector3 position = part.Position;
            Color color = part.Color; color.A *= fade;
            if (part.NearSafe)
            {
                // Keep the original endpoints in the record; clip against the current viewer.
                Vector3 halfAxis = part.Basis.Z * (part.Size.Z * .5f);
                Vector3 start = part.Position - halfAxis;
                Vector3 end = part.Position + halfAxis;
                if (ClipNearSafeLine(ref start, ref end))
                {
                    Vector3 delta = end - start;
                    basis = Facing(delta) * Basis.FromScale(new Vector3(size.X, size.Y, delta.Length()));
                    position = (start + end) * .5f;
                }
                else
                {
                    basis = Basis.FromScale(Vector3.One * .0001f);
                    color.A = 0f;
                }
            }
            _particles.SetInstanceTransform(i, new Transform3D(basis, position));
            _particles.SetInstanceColor(i, color);
        }
        _particles.VisibleInstanceCount = Enabled ? _count : 0;
        _lines.VisibleInstanceCount = Enabled ? _segments : 0;
        if (_solids != null) _solids.VisibleInstanceCount = Enabled ? _solidCount : 0;
    }

    public void Clear()
    {
        _count = _segments = 0;
        _burstCount = _solidCount = 0;
        if (_solids != null) _solids.VisibleInstanceCount = 0;
        _impactCredit = 12f; _trailCredit = 48f;
        if (_particles != null) _particles.VisibleInstanceCount = 0;
        if (_lines != null) _lines.VisibleInstanceCount = 0;
    }

    public void Impact(WeaponAttackType type, Vector3 position, Vector3 direction, Color rarity,
        float intensity = 1f, bool world = false)
    {
        if (!Enabled || !Supports(type) || Detail <= 0f || _impactCredit < 1f || !position.IsFinite()) return;
        float distanceSq = position.DistanceSquaredTo(_viewer);
        if (distanceSq > 110f * 110f) return;
        _impactCredit -= 1f;
        intensity = Mathf.Clamp(float.IsFinite(intensity) ? intensity : 1f, .5f, 2.4f);
        direction = SafeDirection(direction);
        Color tint = world ? new Color(.78f, .73f, .63f).Lerp(rarity, .12f) : Tint(type, rarity);
        int count = distanceSq > 45f * 45f ? 2 : (type == WeaponAttackType.Rift ? 3 : 7);
        count = Math.Max(1, Mathf.RoundToInt(count * Mathf.Clamp(Detail, 0f, 1f)));
        Basis basis = Facing(direction);
        // A readable angular contact flash, followed by much dimmer solid fragments.
        int corners = type == WeaponAttackType.Laser ? 3 : 4;
        float flashRadius = (type == WeaponAttackType.Sawblade ? .28f : .16f) * Mathf.Sqrt(intensity);
        for (int i = 0; i < corners; i++)
        {
            float a = i * Mathf.Tau / corners;
            float b = (i + 1) * Mathf.Tau / corners;
            Vector3 p = basis * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * flashRadius;
            Vector3 q = basis * new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * flashRadius;
            AddLine(position + p, position + q, .022f, tint.Lerp(Colors.White, .55f), .10f);
        }
        for (int i = 0; i < count && _count < MaxTransientInstances; i++)
        {
            Vector3 spray = basis * new Vector3(_random.RandfRange(-1f, 1f), _random.RandfRange(-.5f, 1.1f), 0f);
            Vector3 velocity = (direction * _random.RandfRange(.4f, 1.7f) + spray) * (2.2f * intensity);
            float size = _random.RandfRange(.025f, .065f) * Mathf.Sqrt(intensity);
            _fragments[_count++] = new Fragment
            {
                Position = position, Velocity = velocity, Size = new Vector3(size, size, size * .45f),
                Rotation = spray, Spin = spray * 9f, Gravity = 6f,
                Life = _random.RandfRange(.16f, .38f), Color = tint.Lerp(new Color(.25f, .28f, .32f), .35f),
            };
        }
    }

    public void ProjectileTrail(WeaponAttackType type, Vector3 start, Vector3 end, Color rarity, float radius)
    {
        if (!Enabled || Detail <= 0f || _trailCredit < 1f || start.DistanceSquaredTo(_viewer) > 130f * 130f) return;
        if (type is not (WeaponAttackType.Single or WeaponAttackType.Sawblade or WeaponAttackType.Homing or WeaponAttackType.Drill or WeaponAttackType.Mortar)) return;
        Vector3 delta = end - start;
        if (delta.LengthSquared() < .0001f) return;
        _trailCredit--;
        float maxLength = type switch { WeaponAttackType.Single => 2.8f, WeaponAttackType.Homing => 1.3f, WeaponAttackType.Drill => 1.8f, WeaponAttackType.Mortar => 1.4f, _ => .75f };
        if (delta.LengthSquared() > maxLength * maxLength) start = end - delta.Normalized() * maxLength;
        Color tint = Tint(type, rarity); tint.A = type == WeaponAttackType.Mortar ? .35f : .72f;
        AddLine(start, end, type == WeaponAttackType.Single ? Mathf.Clamp(radius * .4f, .014f, .05f) : type == WeaponAttackType.Mortar ? .018f : .024f,
            tint, type == WeaponAttackType.Single ? .07f : type == WeaponAttackType.Mortar ? .15f : .11f);
        if (type == WeaponAttackType.Homing)
        {
            Basis side = Facing(delta);
            for (int i = -1; i <= 1; i += 2) AddLine(start + side.X * (.07f * i), end, .018f, tint, .14f);
        }
        else if (type == WeaponAttackType.Drill)
        {
            Basis side = Facing(delta);
            AddLine(start + side.X * (Mathf.Sin(SimulationTime * 28f) * .13f),
                end + side.Y * (Mathf.Cos(SimulationTime * 28f) * .13f), .028f, tint, .12f);
        }
        else if (type == WeaponAttackType.Mortar)
        {
            // Two faint offset rails follow the supplied flight tangent, leaving an arc wake.
            Basis side = Facing(delta);
            Color wake = tint; wake.A = .28f;
            AddLine(start + side.X * .08f, end + side.X * .025f, .014f, wake, .18f);
            AddLine(start - side.X * .08f, end - side.X * .025f, .014f, wake, .18f);
        }
    }

    public void Ricochet(Vector3 start, Vector3 end, Color rarity)
    {
        if (!Enabled || Detail <= 0f || _trailCredit < 1f) return;
        _trailCredit--;
        AddLine(start, end, .023f, Tint(WeaponAttackType.Single, rarity), .075f);
    }

    public bool DrawBeam(Vector3 origin, Vector3 end, Vector3 muzzle, float radius, float age, float duration, Color rarity)
    {
        if (!Enabled || _lines == null || _segments + 13 > MaxPersistentSegments) return false;
        Vector3 delta = end - origin;
        if (delta.LengthSquared() < .001f) return true;
        Basis basis = Facing(delta);
        Vector3 convergence = origin + delta.Normalized() * Mathf.Min(2f, delta.Length() * .3f);
        Color tint = Tint(WeaponAttackType.Laser, rarity);
        float fade = Mathf.Clamp((duration - age) / .085f, 0f, 1f);
        Color core = tint.Lerp(Colors.White, .6f); core.A = fade;
        Segment(muzzle, convergence, .018f * fade + .002f, core);
        Segment(convergence, end, .018f * fade + .002f, core);
        // Three faint rails show the real damaging cross-section without an opaque tube.
        tint.A = .22f * fade;
        Vector3 railStart = origin + delta.Normalized() * Mathf.Min(3f, delta.Length() * .6f);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.Tau / 3f + Mathf.Pi * .5f;
            Vector3 radial = basis * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            Segment(railStart + radial, end + radial, .012f, tint);
            float nextAngle = (i + 1) * Mathf.Tau / 3f + Mathf.Pi * .5f;
            Vector3 next = basis * new Vector3(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle), 0f) * radius;
            Segment(end + radial, end + next, .018f, tint);
            // A pulse travels along the already-active beam; it does not delay damage.
            Vector3 pulse = convergence.Lerp(end, Mathf.PosMod(age * 8f, 1f));
            Segment(pulse + radial * .3f, pulse + next * .3f, .011f, new Color(core.R, core.G, core.B, fade * .65f));
        }
        return true;
    }

    public bool DrawRift(Vector3 center, float radius, float age, float duration, float tickRemaining, float tickInterval, Color rarity)
    {
        const int segments = 108;
        if (!Enabled || _lines == null || _segments + segments > MaxPersistentSegments) return false;
        float fade = Mathf.Clamp(age / .12f, 0f, 1f) * Mathf.Clamp((duration - age) / .28f, 0f, 1f);
        float phase = tickInterval > 0f ? 1f - Mathf.Clamp(tickRemaining / tickInterval, 0f, 1f) : 0f;
        float alignment = Mathf.Pow(phase, 4f);
        Color tint = Tint(WeaponAttackType.Rift, rarity);
        Color boundary = tint; boundary.A = .28f * fade;
        // Three great circles mark the spherical damage volume at its exact radius.
        for (int axis = 0; axis < 3; axis++)
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Tau / 24f, b = (i + 1) * Mathf.Tau / 24f;
            Vector3 p = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 q = new(Mathf.Cos(b), 0f, Mathf.Sin(b));
            if (axis == 1) { p = new Vector3(p.X, p.Z, 0); q = new Vector3(q.X, q.Z, 0); }
            if (axis == 2) { p = new Vector3(0, p.X, p.Z); q = new Vector3(0, q.X, q.Z); }
            Segment(center + p * radius, center + q * radius, .018f, boundary);
        }
        for (int layer = 0; layer < 3; layer++)
        {
            float half = radius * (.17f + layer * .105f);
            float twist = (1f - alignment) * Mathf.Sin(age * .85f + layer * 1.4f) * (.25f + layer * .2f);
            Basis rotation = Basis.FromEuler(new Vector3(twist * .75f, twist, twist * .45f));
            Vector3 shift = new Vector3(Mathf.Sin(age * 1.7f + layer), Mathf.Cos(age * 1.1f + layer), 0f) * (radius * .06f * (1f - alignment));
            Color line = tint.Lerp(Colors.White, alignment * .65f);
            line.A = (.5f + alignment * .4f) * fade;
            for (int corner = 0; corner < 8; corner++)
            for (int axis = 0; axis < 3; axis++)
            {
                int other = corner ^ (1 << axis);
                if (other < corner) continue;
                Segment(center + shift + rotation * CubeCorner(corner) * half,
                    center + shift + rotation * CubeCorner(other) * half, .027f + alignment * .018f, line);
            }
        }
        return true;
    }

    public void RiftPulse(Vector3 center, float radius, Color rarity)
    {
        if (!Enabled || Detail <= 0f || _trailCredit < 4f || center.DistanceSquaredTo(_viewer) > 90f * 90f) return;
        _trailCredit -= 4f;
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Tau / 4f + SimulationTime;
            Vector3 radial = new Vector3(Mathf.Cos(angle), .25f, Mathf.Sin(angle)) * radius * .65f;
            AddLine(center + radial, center + radial * .75f, .025f, Tint(WeaponAttackType.Rift, rarity), .16f);
        }
    }

    private void Segment(Vector3 start, Vector3 end, float width, Color color)
    {
        Vector3 delta = end - start;
        if (_segments >= MaxPersistentSegments || delta.LengthSquared() < .000001f) return;
        _lines.SetInstanceTransform(_segments, new Transform3D(Facing(delta) * Basis.FromScale(new Vector3(width, width, delta.Length())), (start + end) * .5f));
        _lines.SetInstanceColor(_segments++, color);
    }

    private void AddLine(Vector3 start, Vector3 end, float width, Color color, float life, bool nearSafe = false)
    {
        Vector3 delta = end - start;
        if (_count >= MaxTransientInstances || delta.LengthSquared() < .000001f) return;
        _fragments[_count++] = new Fragment
        {
            Position = (start + end) * .5f, Size = new Vector3(width, width, delta.Length()),
            Basis = Facing(delta), Color = color, Life = life, Line = true, NearSafe = nearSafe,
        };
    }

    private static Vector3 CubeCorner(int index) => new((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1);
    private static Vector3 SafeDirection(Vector3 direction) => direction.IsFinite() && direction.LengthSquared() > .000001f ? direction.Normalized() : Vector3.Forward;
    public static Basis Facing(Vector3 direction)
    {
        direction = SafeDirection(direction);
        return Basis.LookingAt(direction, Mathf.Abs(direction.Dot(Vector3.Up)) > .98f ? Vector3.Right : Vector3.Up);
    }
    public static Color Tint(WeaponAttackType type, Color rarity)
    {
        Color identity = type switch
        {
            WeaponAttackType.Single => new Color(1f, .62f, .12f),
            WeaponAttackType.Sawblade => new Color(.13f, .68f, .82f),
            WeaponAttackType.Laser => new Color(.12f, .65f, 1f),
            WeaponAttackType.Mine => new Color(1f, .62f, .18f),
            WeaponAttackType.Nova => new Color(.2f, .7f, 1f),
            WeaponAttackType.Orbital => new Color(.24f, .94f, .62f),
            WeaponAttackType.Homing => new Color(1f, .38f, .25f),
            WeaponAttackType.FlameCone => new Color(1f, .29f, .055f),
            WeaponAttackType.Mortar => new Color(.48f, .37f, 1f),
            WeaponAttackType.ChainLightning => new Color(.32f, .77f, 1f),
            WeaponAttackType.Drill => new Color(.8f, .58f, .21f),
            WeaponAttackType.VoidSingularity => new Color(.48f, .17f, .76f),
            WeaponAttackType.TemporalEcho => new Color(.25f, .8f, .81f),
            WeaponAttackType.WrathAura => new Color(.93f, .16f, .5f),
            WeaponAttackType.ShadowClone => new Color(.48f, .57f, .82f),
            _ => new Color(.55f, .2f, .95f),
        };
        return identity.Lerp(rarity, .18f);
    }

    private static Mesh BuildSawbladeMesh()
    {
        SurfaceTool tool = new(); tool.Begin(Mesh.PrimitiveType.Triangles);
        const int points = 48;
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            tool.SetColor(color); tool.AddVertex(a); tool.AddVertex(b); tool.AddVertex(c);
        }
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.Tau / points, b = (i + 1) * Mathf.Tau / points;
            float ra = i % 2 == 0 ? .86f : .69f, rb = i % 2 == 0 ? .69f : .86f;
            Vector3 p = new(Mathf.Cos(a) * ra, Mathf.Sin(a) * ra, .045f);
            Vector3 q = new(Mathf.Cos(b) * rb, Mathf.Sin(b) * rb, .045f);
            Vector3 innerP = new(Mathf.Cos(a) * .37f, Mathf.Sin(a) * .37f, .045f);
            Vector3 innerQ = new(Mathf.Cos(b) * .37f, Mathf.Sin(b) * .37f, .045f);
            for (int face = 0; face < 2; face++)
            {
                Triangle(innerP, face == 0 ? q : p, face == 0 ? p : q, Colors.White);
                Triangle(innerP, face == 0 ? innerQ : q, face == 0 ? q : innerQ, Colors.White);
                Triangle(new Vector3(0, 0, p.Z), face == 0 ? innerQ : innerP, face == 0 ? innerP : innerQ,
                    i % 8 < 2 ? new Color(.65f, .7f, .76f) : new Color(.18f, .22f, .29f));
                p.Z = q.Z = innerP.Z = innerQ.Z = -.045f;
            }
            Triangle(p, q, new Vector3(q.X, q.Y, .045f), new Color(.4f, .5f, .6f));
            Triangle(p, new Vector3(q.X, q.Y, .045f), new Vector3(p.X, p.Y, .045f), new Color(.4f, .5f, .6f));
        }
        tool.GenerateNormals();
        return tool.Commit();
    }
}
