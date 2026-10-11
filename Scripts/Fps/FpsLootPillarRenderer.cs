using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>World-space ground origin, rarity color, metre dimensions and stable 0..1 animation phase.</summary>
public readonly record struct FpsLootPillarInstance(Vector3 Position, Color Color, float Height, float Width, float Phase, int RarityRank = 0, bool EmitSparks = true);

/// <summary>GPU-animated atlas billboards. Snapshot uploads occur only when ground loot changes.</summary>
public partial class FpsLootPillarRenderer : Node3D
{
    public const string AtlasPath = "res://Resources/Textures/Loot/LootPillarAtlas.png";
    private const string ShaderPath = "res://Resources/Shaders/Loot/loot_pillar.gdshader";
    private const float ChunkSize = 128f;
    private const int FloatsPerInstance = 20;
    private static ArrayMesh? _quad;
    private static Shader? _shader;
    private ShaderMaterial? _material;
    private Texture2D? _atlas;
    private readonly Dictionary<Vector2I, Chunk> _chunks = new();
    private readonly List<Vector2I> _emptyChunks = new();
    public int PillarCount { get; private set; }
    public int ChunkCount => _chunks.Count;
    public bool AtlasReady => _atlas != null;

    private sealed class Chunk
    {
        public required MultiMeshInstance3D Node;
        public required MultiMesh Mesh;
        public readonly List<FpsLootPillarInstance> Items = new();
        public float[] Buffer = Array.Empty<float>();
    }

    public override void _Ready() => EnsureResources();

    public void SetFlowSpeed(float speed)
    {
        EnsureResources();
        _material!.SetShaderParameter("flow_speed", float.IsFinite(speed) ? Math.Max(0f, speed) : .35f);
    }

    /// <summary>Deterministic capture override; null restores GPU TIME animation.</summary>
    public void SetAnimationTime(float? seconds)
    {
        EnsureResources();
        _material!.SetShaderParameter("animation_time_override", seconds.HasValue && float.IsFinite(seconds.Value)
            ? Math.Max(0f, seconds.Value) : -1f);
    }

    /// <summary>Allows test fixtures to supply an atlas without replacing the user's artwork.</summary>
    public void SetAtlas(Texture2D atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        _atlas = atlas;
        EnsureResources();
        _material!.SetShaderParameter("pillar_atlas", atlas);
        foreach (Chunk chunk in _chunks.Values) chunk.Node.Visible = true;
    }

    /// <summary>Replaces all pillars, including removals. Positions are supplied in world coordinates.</summary>
    public void SetPillars(IReadOnlyList<FpsLootPillarInstance> pillars)
    {
        ArgumentNullException.ThrowIfNull(pillars);
        EnsureResources();
        foreach (Chunk chunk in _chunks.Values) chunk.Items.Clear();
        Transform3D toLocal = IsInsideTree() ? GlobalTransform.AffineInverse() : Transform3D.Identity;
        PillarCount = 0;
        _sparkItems.Clear();
        foreach (FpsLootPillarInstance item in pillars)
        {
            if (!item.Position.IsFinite() || !float.IsFinite(item.Height) || !float.IsFinite(item.Width)
                || item.Height <= 0f || item.Width <= 0f) continue;
            Vector3 local = toLocal * item.Position;
            Vector2I key = new(Mathf.FloorToInt(local.X / ChunkSize), Mathf.FloorToInt(local.Z / ChunkSize));
            if (!_chunks.TryGetValue(key, out Chunk? chunk))
            {
                chunk = CreateChunk(key);
                _chunks.Add(key, chunk);
            }
            chunk.Items.Add(item with { Position = local - chunk.Node.Position });
            if (item.EmitSparks) _sparkItems.Add(item);
            PillarCount++;
        }
        _emptyChunks.Clear();
        foreach ((Vector2I key, Chunk chunk) in _chunks)
        {
            if (chunk.Items.Count == 0)
            {
                chunk.Mesh.VisibleInstanceCount = 0;
                chunk.Node.Visible = false;
                chunk.Node.QueueFree();
                _emptyChunks.Add(key);
                continue;
            }
            UploadChunk(chunk);
        }
        foreach (Vector2I key in _emptyChunks) _chunks.Remove(key);
        RefreshSparks();
    }

    private void EnsureResources()
    {
        if (_material != null) return;
        _quad ??= CreatePillarMesh();
        _shader ??= GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException("Loot pillar shader is missing.");
        _material = new ShaderMaterial { Shader = _shader };
        // A missing user attachment is quiet and invisible until SetAtlas supplies it.
        if (_atlas == null && ResourceLoader.Exists(AtlasPath)) _atlas = GD.Load<Texture2D>(AtlasPath);
        if (_atlas != null) _material.SetShaderParameter("pillar_atlas", _atlas);
    }

    private Chunk CreateChunk(Vector2I key)
    {
        MultiMesh mesh = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, UseCustomData = true, Mesh = _quad,
        };
        MultiMeshInstance3D node = new()
        {
            Name = $"LootPillars_{key.X}_{key.Y}", Multimesh = mesh, MaterialOverride = _material,
            Position = new Vector3(key.X * ChunkSize, 0, key.Y * ChunkSize),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = _atlas != null,
        };
        AddChild(node);
        return new Chunk { Node = node, Mesh = mesh };
    }

    private static void UploadChunk(Chunk chunk)
    {
        int count = chunk.Items.Count;
        int capacity = Math.Max(8, chunk.Buffer.Length / FloatsPerInstance);
        while (capacity < count) capacity = checked(capacity * 2);
        while (capacity > 8 && count <= capacity / 4) capacity /= 2;
        if (chunk.Buffer.Length != capacity * FloatsPerInstance)
        {
            chunk.Mesh.VisibleInstanceCount = 0;
            chunk.Mesh.InstanceCount = capacity;
            chunk.Buffer = new float[capacity * FloatsPerInstance];
        }
        Aabb bounds = default;
        for (int index = 0; index < count; index++)
        {
            FpsLootPillarInstance item = chunk.Items[index];
            int offset = index * FloatsPerInstance;
            // Row-major identity 3x4, followed by rarity RGBA and custom phase/height/width.
            chunk.Buffer[offset] = 1f; chunk.Buffer[offset + 5] = 1f; chunk.Buffer[offset + 10] = 1f;
            chunk.Buffer[offset + 3] = item.Position.X;
            chunk.Buffer[offset + 7] = item.Position.Y;
            chunk.Buffer[offset + 11] = item.Position.Z;
            chunk.Buffer[offset + 12] = item.Color.R;
            chunk.Buffer[offset + 13] = item.Color.G;
            chunk.Buffer[offset + 14] = item.Color.B;
            chunk.Buffer[offset + 15] = item.Color.A;
            chunk.Buffer[offset + 16] = float.IsFinite(item.Phase) ? item.Phase - Mathf.Floor(item.Phase) : 0f;
            chunk.Buffer[offset + 17] = item.Height;
            chunk.Buffer[offset + 18] = item.Width;
            chunk.Buffer[offset + 19] = Math.Clamp(item.RarityRank, 0, 5);
            float radius = item.Width * .65f;
            Aabb box = new(item.Position + new Vector3(-radius, -item.Height * .1f, -radius),
                new Vector3(radius * 2f, Math.Max(item.Height, .025f + item.Height * .1f), radius * 2f));
            bounds = index == 0 ? box : bounds.Merge(box);
        }
        chunk.Mesh.CustomAabb = bounds;
        chunk.Mesh.Buffer = chunk.Buffer;
        chunk.Mesh.VisibleInstanceCount = count;
    }

    private static ArrayMesh CreatePillarMesh()
    {
        Vector3[] vertices = new Vector3[12];
        Vector2[] uv = new Vector2[12];
        Vector2[] tags = new Vector2[12];
        int[] indices = new int[18];
        Vector2[] corners = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        for (int plane = 0; plane < 3; plane++)
        {
            int start = plane * 4;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 point = corners[corner];
                vertices[start + corner] = new Vector3(point.X - .5f, .5f - point.Y, 0);
                uv[start + corner] = point;
                tags[start + corner] = new Vector2(plane == 0 ? -1f : plane == 1 ? 1f : 2f, 0);
            }
            int triangle = plane * 6;
            indices[triangle] = start; indices[triangle + 1] = start + 1; indices[triangle + 2] = start + 2;
            indices[triangle + 3] = start; indices[triangle + 4] = start + 2; indices[triangle + 5] = start + 3;
        }
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = tags;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
