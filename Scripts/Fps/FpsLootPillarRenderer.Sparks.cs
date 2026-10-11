using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsLootPillarRenderer
{
    public const int SparkEmitterCapacity = 32;
    private const float SparkRange = 45f;
    private readonly List<FpsLootPillarInstance> _sparkItems = new();
    private readonly List<(FpsLootPillarInstance Item, float Distance)> _sparkNearest = new();
    private readonly List<SparkSlot> _sparkPool = new();
    private float _sparkRefresh;
    public int ActiveSparkEmitterCount { get; private set; }
    private sealed class SparkSlot
    {
        public required GpuParticles3D Node;
        public required ParticleProcessMaterial Process;
        public FpsLootPillarInstance? Item;
    }

    public override void _Process(double delta)
    {
        _sparkRefresh -= (float)delta;
        if (_sparkRefresh > 0f) return;
        _sparkRefresh = .25f;
        RefreshSparks();
    }

    private void RefreshSparks()
    {
        _sparkNearest.Clear();
        Camera3D? camera = IsInsideTree() ? GetViewport().GetCamera3D() : null;
        if (_atlas != null && camera != null)
        {
            Vector3 origin = camera.GlobalPosition;
            foreach (FpsLootPillarInstance item in _sparkItems)
            {
                float distance = item.Position.DistanceSquaredTo(origin);
                if (distance > SparkRange * SparkRange) continue;
                int index = 0;
                while (index < _sparkNearest.Count && _sparkNearest[index].Distance <= distance) index++;
                if (index >= SparkEmitterCapacity) continue;
                _sparkNearest.Insert(index, (item, distance));
                if (_sparkNearest.Count > SparkEmitterCapacity) _sparkNearest.RemoveAt(SparkEmitterCapacity);
            }
        }
        ActiveSparkEmitterCount = _sparkNearest.Count;
        // Retain assignments when nearest ordering changes so moving a camera cannot restart every particle.
        foreach (SparkSlot slot in _sparkPool)
        {
            if (!slot.Item.HasValue) continue;
            bool retained = false;
            foreach (var candidate in _sparkNearest)
                if (candidate.Item == slot.Item.Value) { retained = true; break; }
            if (retained) continue;
            slot.Node.Visible = false;
            slot.Node.Restart();
            slot.Node.Emitting = false;
            slot.Item = null;
        }
        foreach (var candidate in _sparkNearest)
        {
            SparkSlot? available = null;
            bool assigned = false;
            foreach (SparkSlot slot in _sparkPool)
            {
                if (slot.Item == candidate.Item) { assigned = true; break; }
                if (!slot.Item.HasValue) available ??= slot;
            }
            if (assigned) continue;
            if (available == null)
            {
                available = CreateSparkSlot(_sparkPool.Count);
                _sparkPool.Add(available);
            }
            FpsLootPillarInstance item = candidate.Item;
            int rank = Math.Clamp(item.RarityRank, 0, 5);
            available.Item = item;
            available.Node.GlobalPosition = item.Position + Vector3.Up * .08f;
            available.Node.Amount = 3 + rank * 2;
            available.Node.Lifetime = 1.25 + rank * .1;
            available.Process.Color = item.Color;
            available.Process.EmissionSphereRadius = item.Width * .16f;
            available.Process.InitialVelocityMin = .3f + rank * .055f;
            available.Process.InitialVelocityMax = .7f + rank * .11f;
            available.Process.ScaleMin = .55f + rank * .07f;
            available.Process.ScaleMax = .9f + rank * .12f;
            available.Node.Visible = true;
            available.Node.Restart();
            available.Node.Emitting = true;
        }
    }

    private SparkSlot CreateSparkSlot(int index)
    {
        Gradient gradient = new()
        {
            Offsets = new[] { 0f, .15f, .65f, 1f },
            Colors = new[] { new Color(1, 1, 1, 0), Colors.White, new Color(1, 1, 1, .65f), new Color(1, 1, 1, 0) },
        };
        ParticleProcessMaterial process = new()
        {
            Direction = Vector3.Up, Spread = 24f, Gravity = new Vector3(0, .2f, 0),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            ColorRamp = new GradientTexture1D { Gradient = gradient },
        };
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White,
        };
        GpuParticles3D node = new()
        {
            Name = $"LootSparks{index:00}", Amount = 3, Lifetime = 1.25, Randomness = .7f,
            LocalCoords = true, Emitting = false, Visible = false, ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(.018f, .035f), Material = material },
            VisibilityAabb = new Aabb(new Vector3(-1.5f, -.2f, -1.5f), new Vector3(3, 4, 3)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        return new SparkSlot { Node = node, Process = process };
    }
}
