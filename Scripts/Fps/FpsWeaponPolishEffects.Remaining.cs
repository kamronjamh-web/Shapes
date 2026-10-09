using System;
using Godot;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

public partial class FpsWeaponPolishEffects
{
    public const int MaxSolidInstances = 128;
    public const int MaxCosmeticBursts = 32;
    public int VisibleSolidCount => _solidCount;
    public int ActiveCosmeticBurstCount => _burstCount;
    private MultiMesh _solids = null!;
    private int _solidCount;
    private struct CosmeticBurst { public Vector3 Center; public float Radius, Age; public Color Rarity; public WeaponAttackType Type; }
    private readonly CosmeticBurst[] _bursts = new CosmeticBurst[MaxCosmeticBursts];
    private int _burstCount;
    private static Mesh? _homingMesh, _drillMesh;
    private static readonly Vector3[] CloneOffsets =
    {
        new(0f, .63f, 0f), new(0f, .23f, 0f), new(-.24f, .2f, 0f), new(.24f, .2f, 0f),
        new(-.34f, -.06f, 0f), new(.34f, -.06f, 0f), new(-.12f, -.31f, 0f), new(.12f, -.31f, 0f), new(0f, -.07f, 0f),
    };
    public static Mesh HomingMesh => _homingMesh ??= BuildPointedMesh(false);
    public static Mesh DrillMesh => _drillMesh ??= BuildPointedMesh(true);

    private void BuildRemainingBatch()
    {
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = Colors.White,
        };
        Mesh mesh = new SphereMesh { Radius = .5f, Height = 1f, RadialSegments = 6, Rings = 2, Material = material };
        _solids = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = mesh, InstanceCount = MaxSolidInstances, VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D
        {
            Name = "FacetedSolids", Multimesh = _solids,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private bool Reserve(int segments, int solids, Vector3 position)
        => Enabled && _lines != null && _solids != null && position.IsFinite()
        && _segments + segments <= MaxPersistentSegments && _solidCount + solids <= MaxSolidInstances;

    private void Solid(Vector3 position, Vector3 size, Basis basis, Color color)
    {
        if (_solidCount >= MaxSolidInstances) return;
        // Faceted fragments disappear before the viewer enters their enclosing volume.
        // This avoids fullscreen translucent faces when walking through clones or flame.
        if (position.DistanceSquaredTo(_viewer) < size.LengthSquared() * .25f + .01f) return;
        _solids.SetInstanceTransform(_solidCount, new Transform3D(basis * Basis.FromScale(size), position));
        _solids.SetInstanceColor(_solidCount++, color);
    }

    private static Color Alpha(Color color, float alpha) { color.A = Mathf.Clamp(alpha, 0f, 1f); return color; }
    private static float LifeFade(float age, float duration)
        => Mathf.Clamp(age / .06f, 0f, 1f) * Mathf.Clamp((duration - age) / .18f, 0f, 1f);

    private void Ring(Vector3 center, float radius, Basis basis, int sides, float width, Color color, float phase = 0f, bool segmented = false)
    {
        for (int i = 0; i < sides; i++)
        {
            float a = phase + i * Mathf.Tau / sides;
            float b = phase + (i + (segmented ? .72f : 1f)) * Mathf.Tau / sides;
            Segment(center + basis * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius,
                center + basis * new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * radius, width, color);
        }
    }

    private void CubeOutline(Vector3 center, float half, Basis rotation, Color tint, float width)
    {
        for (int corner = 0; corner < 8; corner++)
        for (int axis = 0; axis < 3; axis++)
        {
            int other = corner ^ (1 << axis);
            if (other > corner) Segment(center + rotation * CubeCorner(corner) * half,
                center + rotation * CubeCorner(other) * half, width, tint);
        }
    }

    public bool DrawMine(Vector3 position, float radius, float armProgress, float age, Color rarity)
    {
        if (!Reserve(24, 7, position)) return false;
        Color tint = Tint(WeaponAttackType.Mine, rarity);
        float armed = Mathf.Clamp(armProgress, 0f, 1f);
        Basis ground = new(Vector3.Right, Vector3.Back, Vector3.Down);
        Ring(position + Vector3.Up * .07f, .38f, ground, 6, .035f, Alpha(tint, .7f));
        // Trigger footprint is a faint separated perimeter, not a filled disk.
        Ring(position + Vector3.Up * .035f, radius, ground, 12, .016f, Alpha(tint, .16f + armed * .16f), age * .12f, true);
        Solid(position + Vector3.Up * .1f, new(.56f, .16f, .56f), Basis.Identity, new Color(.15f, .17f, .2f, 1f));
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f;
            Vector3 radial = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 p = position + radial * (.18f + .13f * armed) + Vector3.Up * (.15f + .055f * armed);
            Basis pose = Basis.FromEuler(new Vector3(0f, -a, armed * .25f));
            Solid(p, new(.17f, .07f, .12f), pose, Alpha(tint, .8f));
            Segment(p, p + Vector3.Up * (.035f + .04f * armed), .03f, Alpha(tint.Lerp(Colors.White, armed * .25f), .9f));
        }
        return true;
    }

    public bool DrawOrbital(Vector3 position, float age, Color rarity, float sizeScale = 1f)
    {
        if (!Reserve(24, 1, position)) return false;
        Color tint = Tint(WeaponAttackType.Orbital, rarity);
        float step = age * 1.4f;
        float phase = Mathf.PosMod(step, 1f);
        float turn = (Mathf.Floor(step) + Mathf.SmoothStep(.7f, 1f, phase)) * Mathf.Pi * .5f;
        Basis pose = Basis.FromEuler(new Vector3(turn * .5f, turn, 0f));
        CubeOutline(position, .26f * sizeScale, pose, Alpha(tint, .95f), .045f * sizeScale);
        CubeOutline(position, .14f * sizeScale, pose, Alpha(tint.Lerp(Colors.White, .5f), .65f), .018f * sizeScale);
        Solid(position, Vector3.One * (.24f * sizeScale), pose, Alpha(tint, .4f));
        return true;
    }

    public bool DrawFlame(Vector3 origin, Vector3 direction, float range, float halfAngle, float age, float duration, Color rarity)
    {
        if (!Reserve(18, 12, origin)) return false;
        Basis facing = Facing(direction);
        Color tint = Tint(WeaponAttackType.FlameCone, rarity);
        float fade = LifeFade(age, duration);
        float angleLimit = Mathf.Clamp(halfAngle, .01f, 1.5f);
        float endRadius = Mathf.Sin(angleLimit) * range;
        float forwardLength = Mathf.Cos(angleLimit) * range;
        for (int i = 0; i < 12; i++)
        {
            float t = .12f + Mathf.PosMod(age * (1.7f + i * .037f) + i * .083f, 1f) * .86f;
            float angle = i * 2.399963f + age * .7f;
            float sampleAngle = angleLimit * (.2f + .45f * ((i % 3) / 2f));
            float distance = range * t;
            float radial = Mathf.Sin(sampleAngle) * distance;
            float forward = Mathf.Cos(sampleAngle) * distance;
            Vector3 localDirection = new(Mathf.Cos(angle) * Mathf.Sin(sampleAngle), Mathf.Sin(angle) * Mathf.Sin(sampleAngle), -Mathf.Cos(sampleAngle));
            Vector3 worldDirection = facing * localDirection;
            Vector3 p = origin + facing * new Vector3(Mathf.Cos(angle) * radial, Mathf.Sin(angle) * radial, -forward);
            // Keep each tongue's extent within the radial reach and angular margin.
            float length = Mathf.Min(Mathf.Min(range * .23f, 1.7f), Mathf.Min(distance * .5f, (range - distance) * 1.6f));
            float margin = (distance - length * .5f) * Mathf.Sin(angleLimit - sampleAngle);
            float width = Mathf.Min(Mathf.Clamp(.08f + endRadius * .1f * t, .08f, .7f), margin * .7f);
            width = Mathf.Min(width, Mathf.Max(0f, range - distance - length * .5f) * 1.2f);
            Color fire = tint.Lerp(new Color(1f, .82f, .22f), (1f - t) * .65f);
            Solid(p, new Vector3(width, width, length), Facing(worldDirection),
                Alpha(fire, fade * (.75f - t * .35f)));
            if (i < 6) Segment(p, p + worldDirection * Mathf.Min(.2f, (range - distance) * .7f), .028f, Alpha(fire, fade * .7f));
        }
        // Sparse outer ribs make the gameplay angle readable without filling the cone.
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Tau / 3f;
            Vector3 end = origin + facing * new Vector3(Mathf.Cos(a) * endRadius, Mathf.Sin(a) * endRadius, -forwardLength);
            Segment(origin.Lerp(end, Mathf.Min(.25f / Mathf.Max(.01f, range), .5f)), end, .01f, Alpha(tint, fade * .10f));
        }
        return true;
    }

    public bool DrawVoid(Vector3 position, float radius, float age, float duration, Color rarity)
    {
        if (!Reserve(72, 1, position)) return false;
        Color tint = Tint(WeaponAttackType.VoidSingularity, rarity);
        float fade = LifeFade(age, duration);
        float core = radius * .3f;
        // Only the small center is solid; a player inside sees no fullscreen shell.
        if (position.DistanceSquaredTo(_viewer) > core * core * 1.4f)
            Solid(position, Vector3.One * (core * 2f), Basis.FromEuler(new Vector3(age * .2f, age * .3f, 0f)), new Color(.018f, .009f, .03f, fade * .95f));
        for (int arm = 0; arm < 3; arm++)
        for (int i = 0; i < 20; i++)
        {
            float t = i / 20f, next = (i + 1) / 20f;
            Vector3 Spiral(float f)
            {
                float r = radius * (1f - f * .78f);
                float a = arm * Mathf.Tau / 3f + f * Mathf.Tau * 1.25f - age * 1.3f;
                return position + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(f * Mathf.Pi + age) * radius * .15f, Mathf.Sin(a) * r);
            }
            Segment(Spiral(t), Spiral(next), .025f, Alpha(tint, fade * (.2f + t * .55f)));
        }
        CubeOutline(position, core * .7f, Basis.FromEuler(new Vector3(age * .3f, -age * .4f, age * .2f)), Alpha(tint, fade * .7f), .026f);
        return true;
    }

    public bool DrawWrath(Vector3 center, float radius, float age, float healthIntensity, Color rarity)
    {
        if (!Reserve(56, 8, center)) return false;
        Color tint = Tint(WeaponAttackType.WrathAura, rarity);
        float power = Mathf.Clamp(healthIntensity, 0f, 1f);
        Basis ground = new(Vector3.Right, Vector3.Back, Vector3.Down);
        Ring(center, radius, ground, 32, .018f + power * .02f, Alpha(tint, .2f + power * .2f), age * .035f, true);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f + age * .15f;
            Vector3 axis = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 p = center + axis * radius * .92f + Vector3.Up * (.08f + .06f * Mathf.Sin(age * 3f + i));
            Solid(p, new Vector3(.1f, .26f + power * .24f, .1f), Basis.FromEuler(new Vector3(0f, a, .2f)), Alpha(tint, .65f));
            Segment(p, p + axis * .25f + Vector3.Up * (.3f + power * .3f), .025f, Alpha(tint, .6f));
        }
        return true;
    }

    public bool DrawClone(Vector3 position, Vector3 direction, float age, float duration, Color rarity, float sizeScale = 1f)
    {
        if (!Reserve(16, 9, position)) return false;
        Color tint = Tint(WeaponAttackType.ShadowClone, rarity);
        Basis facing = Facing(direction);
        float fade = LifeFade(age, duration);
        // The faceted body is broken into separated shards, retaining a recognizable silhouette.
        for (int i = 0; i < CloneOffsets.Length; i++)
        {
            Vector3 offset = CloneOffsets[i] * sizeScale;
            offset.X += Mathf.Sin(age * 3f + i * 1.8f) * .035f * sizeScale;
            Vector3 size = i == 0 ? new(.28f, .32f, .25f) : i == 1 ? new(.4f, .53f, .22f) : new(.15f, .38f, .16f);
            Solid(position + facing * offset, size * sizeScale, facing * Basis.FromEuler(new Vector3(0f, 0f, Mathf.Sin(age + i) * .12f)), Alpha(tint, fade * .32f));
        }
        CubeOutline(position + facing * new Vector3(0f, .23f, 0f) * sizeScale, .24f * sizeScale, facing, Alpha(tint, fade * .5f), .018f);
        Segment(position + facing * new Vector3(-.07f, .65f, -.14f) * sizeScale,
            position + facing * new Vector3(.07f, .65f, -.14f) * sizeScale, .027f, Alpha(tint.Lerp(Colors.White, .6f), fade * .8f));
        return true;
    }

    public bool DrawMortar(Vector3 position, Vector3 direction, float age, Color rarity)
    {
        if (Enabled && position.IsFinite() && position.DistanceSquaredTo(_viewer) < .7f * .7f) return true;
        if (!Reserve(24, 1, position)) return false;
        Color tint = Tint(WeaponAttackType.Mortar, rarity);
        Basis facing = Facing(direction);
        float aperture = Mathf.SmoothStep(0f, .1f, age);
        float growth = .2f + aperture * .8f;
        Solid(position, Vector3.One * (.45f * growth), Basis.FromEuler(new Vector3(age, age * .7f, 0f)), Alpha(tint, .7f * aperture));
        Ring(position, .38f * growth, facing, 12, .025f, Alpha(tint, .8f * aperture), age * 2f);
        Ring(position, .27f * growth, facing * Basis.FromEuler(new Vector3(Mathf.Pi * .5f, 0f, 0f)), 12, .018f, Alpha(tint, .4f * aperture), -age);
        return true;
    }

    /// <summary>Secondary damaging residues keep their own shape identity and actual footprint.</summary>
    public bool DrawResidue(WeaponAttackType type, Vector3 position, float radius, float age, float duration, Color rarity)
    {
        if (type is not (WeaponAttackType.Drill or WeaponAttackType.Mortar)) return false;
        int segments = type == WeaponAttackType.Drill ? 22 : 24;
        if (!Reserve(segments, 6, position)) return false;
        float fade = LifeFade(age, duration);
        Color tint = Tint(type, rarity);
        Basis ground = new(Vector3.Right, Vector3.Back, Vector3.Down);
        Ring(position + Vector3.Up * .035f, radius, ground, type == WeaponAttackType.Drill ? 16 : 24,
            .017f, Alpha(tint, fade * .25f), 0f, true);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f;
            Vector3 radial = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 p = position + radial * radius * .65f + Vector3.Up * .07f;
            if (type == WeaponAttackType.Drill)
            {
                Vector3 tangent = new(-radial.Z, 0f, radial.X);
                // Oblique grooves remain stationary; brightness traces the screw phase.
                Segment(p - tangent * radius * .12f, p + tangent * radius * .12f + radial * radius * .15f,
                    .025f, Alpha(tint, fade * (.3f + .2f * Mathf.Sin(age * 4f + i))));
                Solid(p, new Vector3(.08f, .06f, .12f), Basis.FromEuler(new Vector3(0f, -a, 0f)), Alpha(tint, fade * .35f));
            }
            else
            {
                Color coal = new Color(.15f, .09f, .12f, fade * .65f);
                Solid(p, new Vector3(.18f, .08f, .22f), Basis.FromEuler(new Vector3(0f, a, .15f)), coal.Lerp(Alpha(tint, fade * .65f), .22f + .08f * Mathf.Sin(age * 3f + i)));
            }
        }
        return true;
    }

    public bool DrawBurst(WeaponAttackType type, Vector3 center, float radius, float age, float duration, Color rarity, bool contracting = false)
    {
        int lines = type switch { WeaponAttackType.Mine => 12, WeaponAttackType.Nova => 72, WeaponAttackType.Mortar => 32, WeaponAttackType.FlameCone => 6, _ => 48 };
        int solids = type switch { WeaponAttackType.Mine => 6, WeaponAttackType.Mortar => 8, WeaponAttackType.FlameCone => 6, _ => 0 };
        if (!Reserve(lines, solids, center)) return false;
        float t = Mathf.Clamp(age / Mathf.Max(.01f, duration), 0f, 1f);
        bool inward = contracting || type == WeaponAttackType.VoidSingularity;
        float r = radius * (inward ? 1f - t * .8125f : .12f + t * .88f);
        Color tint = Alpha(Tint(type, rarity), (1f - t) * .65f);
        Basis ground = new(Vector3.Right, Vector3.Back, Vector3.Down);

        if (type == WeaponAttackType.Mine)
        {
            // A hexagonal shock front and six low armor fragments retain the mine identity.
            Ring(center, r, ground, 6, .04f, tint);
            Ring(center + Vector3.Up * .06f, r * .78f, ground, 6, .02f, Alpha(tint, tint.A * .5f), Mathf.Pi / 6f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.Tau / 6f;
                Vector3 radial = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = center + radial * r * .75f + Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * radius * .14f);
                Solid(p, new Vector3(.18f, .06f, .28f), Basis.FromEuler(new Vector3(t * 2f, -a, t)), Alpha(tint, tint.A * .85f));
            }
            return true;
        }
        if (type == WeaponAttackType.Nova)
        {
            // Three great circles read as a spherical pressure front without a filled shell.
            Ring(center, r, ground, 24, .028f, tint);
            float safeDistance = Mathf.Max(radius * 1.2f, 3f);
            if (_viewer.DistanceSquaredTo(center) > safeDistance * safeDistance)
            {
                Ring(center, r, Basis.Identity, 24, .023f, Alpha(tint, tint.A * .7f));
                Ring(center, r, new Basis(Vector3.Up, Vector3.Back, Vector3.Right), 24, .023f, Alpha(tint, tint.A * .7f));
            }
            return true;
        }
        if (type == WeaponAttackType.Mortar)
        {
            Ring(center, r, ground, 24, .035f, Alpha(tint, tint.A * .7f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Tau / 8f + .2f;
                Vector3 radial = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float height = Mathf.Sin(t * Mathf.Pi) * radius * (.3f + .08f * (i % 3));
                Vector3 p = center + radial * r * .6f + Vector3.Up * height;
                Solid(p, new Vector3(.1f, .3f + height * .25f, .1f), Basis.FromEuler(new Vector3(t * .5f, a, .2f)), Alpha(tint, tint.A));
                Segment(p - Vector3.Up * (.18f + height * .25f), p, .02f, Alpha(tint, tint.A * .6f));
            }
            return true;
        }
        if (type == WeaponAttackType.FlameCone)
        {
            // The endpoint sheds upward embers; the sustained cone supplies the flame front.
            for (int i = 0; i < 6; i++)
            {
                float a = i * 2.399963f;
                Vector3 radial = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = center + radial * radius * (.12f + (i % 3) * .09f) + Vector3.Up * t * radius * .65f;
                Color ember = Alpha(tint.Lerp(new Color(1f, .65f, .12f), .4f), tint.A * .6f);
                Solid(p, new Vector3(.06f, .16f, .06f), Basis.FromEuler(new Vector3(t, a, .2f)), ember);
                Segment(p, p + Vector3.Down * .14f, .015f, Alpha(ember, ember.A * .5f));
            }
            return true;
        }
        Ring(center, r, ground, 24, .025f, tint, age * .4f);
        // A second vertical ring only when it cannot intersect the viewer's near plane.
        if (_viewer.DistanceSquaredTo(center) > r * r * 1.15f)
            Ring(center, r * .95f, Basis.Identity, 24, .02f, Alpha(tint, tint.A * .55f), -age * .35f);
        return true;
    }

    public void Burst(WeaponAttackType type, Vector3 center, float radius, Color rarity)
    {
        if (!Enabled || Detail <= 0f || _burstCount >= _bursts.Length || !center.IsFinite()) return;
        _bursts[_burstCount++] = new CosmeticBurst { Type = type, Center = center, Radius = radius, Rarity = rarity };
    }
    public void Nova(Vector3 center, float radius, Color rarity) => Burst(WeaponAttackType.Nova, center, radius, rarity);
    private void AdvanceBursts(float dt)
    {
        for (int i = _burstCount - 1; i >= 0; i--)
        {
            _bursts[i].Age += dt;
            if (_bursts[i].Age >= .35f) { _bursts[i] = _bursts[--_burstCount]; continue; }
            var burst = _bursts[i];
            DrawBurst(burst.Type, burst.Center, burst.Radius, burst.Age, .35f, burst.Rarity);
        }
    }

    private void AddNearSafeLine(Vector3 start, Vector3 end, float width, Color color, float life)
        => AddLine(start, end, width, color, life, nearSafe: true);

    private bool ClipNearSafeLine(ref Vector3 start, ref Vector3 end)
    {
        const float nearRadiusSq = .4f * .4f;
        Vector3 delta = end - start;
        float lengthSq = delta.LengthSquared();
        if (lengthSq < .000001f) return false;
        Vector3 offset = start - _viewer;
        bool startInside = offset.LengthSquared() < nearRadiusSq;
        bool endInside = end.DistanceSquaredTo(_viewer) < nearRadiusSq;
        if (startInside && endInside) return false;
        float projected = Mathf.Clamp(-offset.Dot(delta) / lengthSq, 0f, 1f);
        if (!startInside && !endInside)
        {
            if ((offset + delta * projected).LengthSquared() < nearRadiusSq) return false;
        }
        else
        {
            float b = offset.Dot(delta);
            float c = offset.LengthSquared() - nearRadiusSq;
            float discriminant = b * b - lengthSq * c;
            if (discriminant < 0f) return false;
            float root = Mathf.Sqrt(discriminant);
            float t = Mathf.Clamp((-b + (startInside ? root : -root)) / lengthSq, 0f, 1f);
            if (startInside) start += delta * t;
            else end = start + delta * t;
        }
        return start.DistanceSquaredTo(end) >= .000001f;
    }

    public void Arc(Vector3 start, Vector3 end, Color rarity)
    {
        if (!Enabled || Detail <= 0f || _trailCredit < 2f || !start.IsFinite() || !end.IsFinite()) return;
        _trailCredit -= 2f;
        Vector3 delta = end - start; Basis side = Facing(delta);
        Color tint = Tint(WeaponAttackType.ChainLightning, rarity);
        Vector3 previous = start;
        for (int i = 1; i <= 9; i++)
        {
            float t = i / 9f;
            Vector3 point = start + delta * t;
            if (i < 9) point += side.X * _random.RandfRange(-.22f, .22f) + side.Y * _random.RandfRange(-.18f, .18f);
            AddNearSafeLine(previous, point, .04f, Alpha(tint, .85f), .14f);
            AddNearSafeLine(previous, point, .012f, tint.Lerp(Colors.White, .8f), .085f);
            if (i % 3 == 0 && i < 9) AddNearSafeLine(point, point + side.X * _random.RandfRange(-.4f, .4f) + side.Y * .25f, .018f, Alpha(tint, .5f), .1f);
            previous = point;
        }
    }

    public void Echo(Vector3 start, Vector3 end, Color rarity, bool replay)
    {
        if (!Enabled || Detail <= 0f || _trailCredit < 2f || !start.IsFinite() || !end.IsFinite()) return;
        _trailCredit -= 2f;
        Basis side = Facing(end - start);
        Color tint = Alpha(Tint(WeaponAttackType.TemporalEcho, rarity), replay ? .75f : .3f);
        for (int i = -1; i <= 1; i++)
            AddNearSafeLine(start + side.X * i * .065f, end + side.X * i * .065f, replay ? .025f : .014f, tint, replay ? .19f : .55f);
    }

    public void CloneShot(Vector3 start, Vector3 end, Color rarity)
    {
        if (!Enabled || Detail <= 0f || _trailCredit < 1f || !start.IsFinite() || !end.IsFinite()) return;
        _trailCredit--;
        AddLine(start, end, .016f, Alpha(Tint(WeaponAttackType.ShadowClone, rarity), .4f), .16f);
    }

    private static Mesh BuildPointedMesh(bool drill)
    {
        SurfaceTool tool = new(); tool.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color tint)
        { tool.SetColor(tint); tool.AddVertex(a); tool.AddVertex(b); tool.AddVertex(c); }
        int sides = drill ? 8 : 3;
        int layers = drill ? 5 : 2;
        for (int layer = 0; layer < layers; layer++)
        {
            float z = .3f - layer * .16f;
            float r = drill ? .18f * (1f - layer / (float)(layers + 1)) : .18f;
            float turn = drill ? layer * .6f : 0f;
            for (int i = 0; i < sides; i++)
            {
                float a = turn + i * Mathf.Tau / sides, b = turn + (i + 1) * Mathf.Tau / sides;
                Vector3 p = new(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z), q = new(Mathf.Cos(b) * r, Mathf.Sin(b) * r, z);
                Vector3 tip = new(0f, 0f, z - (drill ? .24f : .42f));
                Triangle(p, q, tip, i % 2 == 0 ? Colors.White : new Color(.45f, .5f, .58f));
                Triangle(q, p, new Vector3(0f, 0f, z + .03f), new Color(.24f, .27f, .32f));
            }
        }
        tool.GenerateNormals(); return tool.Commit();
    }
}
