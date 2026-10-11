using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsEnemyCorpsePool
{
    private const int CorpseInstanceFloats = 16;
    private MultiMesh? _corpseCubes, _corpsePyramids;
    private float[] _corpseCubeBuffer = Array.Empty<float>(), _corpsePyramidBuffer = Array.Empty<float>();
    private bool _visualDirty = true, _visualTransformKnown;
    private Transform3D _visualPoolTransform;
    private int _visualCapacity;
    internal long CorpseRenderUploadCount { get; private set; }
    internal long CorpsePoseReadCount { get; private set; }

    public override void _Process(double delta)
    {
        // UI edits can lower the limit between physics ticks. Retire excess slots
        // before allocating a buffer bounded by the new capacity.
        TrimCapacity();
        ReadWorkerPoses();
        if (ActiveCount == 0 && _corpseCubes == null) return;
        Transform3D poolTransform = GlobalTransform;
        if (_visualCapacity != Capacity) { _visualCapacity = Capacity; MarkVisualDirty(); }
        bool moved = !_visualTransformKnown || poolTransform != _visualPoolTransform;
        if (moved) { _visualPoolTransform = poolTransform; _visualTransformKnown = true; MarkVisualDirty(); }
        foreach (Corpse corpse in _slots)
            if (corpse.Active && (_running || moved)) SyncGodotPose(corpse, moved);
        if (_visualDirty) PublishCorpseVisuals(poolTransform.AffineInverse());
    }

    private void MarkVisualDirty() => _visualDirty = true;

    private void SyncGodotPose(Corpse corpse, bool force = false)
    {
        if (corpse.Body == null) return;
        bool sleeping = corpse.Body.Sleeping;
        if (force || !sleeping || !corpse.WasSleeping)
        {
            Transform3D pose = corpse.Body.GlobalTransform;
            CorpsePoseReadCount++;
            if (pose != corpse.Pose) { corpse.Pose = pose; MarkVisualDirty(); }
        }
        // One final read when sleeping begins. Checking sleep each frame permits
        // physics/world changes to wake a corpse without permanently freezing it.
        corpse.WasSleeping = sleeping;
    }

    private void EnsureCorpseRenderers()
    {
        if (_corpseCubes != null) return;
        // Instance colors are explicitly linearized below, matching the original
        // source_color albedo/emission uniforms on StandardMaterial3D.
        ShaderMaterial material = new()
        {
            Shader = new Shader { Code = "shader_type spatial;\nvoid fragment() { vec3 color = COLOR.rgb; if (OUTPUT_IS_SRGB) { color = mix(color * 12.92, 1.055 * pow(max(color, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, greaterThan(color, vec3(0.0031308))); } ALBEDO = color; EMISSION = color * 0.08; ROUGHNESS = 0.7; }\n" },
        };
        _corpseCubes = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, Mesh = new BoxMesh { Size = Vector3.One } };
        _corpsePyramids = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, Mesh = CreatePyramid() };
        AddChild(new MultiMeshInstance3D { Name = "CorpseCubes", Multimesh = _corpseCubes, MaterialOverride = material });
        AddChild(new MultiMeshInstance3D { Name = "CorpsePyramids", Multimesh = _corpsePyramids, MaterialOverride = material });
    }

    private void PublishCorpseVisuals(Transform3D toLocal)
    {
        EnsureCorpseRenderers();
        int cubes = 0, pyramids = 0;
        foreach (Corpse corpse in _slots)
            if (corpse.Active) { if (corpse.Role == FpsEnemyRole.Striker) pyramids++; else cubes++; }
        EnsureCorpseBuffer(_corpseCubes!, ref _corpseCubeBuffer, cubes);
        EnsureCorpseBuffer(_corpsePyramids!, ref _corpsePyramidBuffer, pyramids);
        int cubeOffset = 0, pyramidOffset = 0;
        foreach (Corpse corpse in _slots)
        {
            if (!corpse.Active) continue;
            Transform3D pose = corpse.Pose;
            pose.Basis *= Basis.FromScale(corpse.Size);
            pose = toLocal * pose;
            // StandardMaterial3D originally used the same color for albedo and
            // emission. COLOR is a linear shader attribute, not a source_color uniform.
            Color color = corpse.LinearColor;
            if (corpse.Role == FpsEnemyRole.Striker)
            {
                WriteCorpseInstance(_corpsePyramidBuffer, pyramidOffset, pose, color);
                pyramidOffset += CorpseInstanceFloats;
            }
            else
            {
                WriteCorpseInstance(_corpseCubeBuffer, cubeOffset, pose, color);
                cubeOffset += CorpseInstanceFloats;
            }
        }
        if (cubes > 0) { _corpseCubes!.Buffer = _corpseCubeBuffer; CorpseRenderUploadCount++; }
        if (pyramids > 0) { _corpsePyramids!.Buffer = _corpsePyramidBuffer; CorpseRenderUploadCount++; }
        _corpseCubes!.VisibleInstanceCount = cubes;
        _corpsePyramids!.VisibleInstanceCount = pyramids;
        _visualDirty = false;
    }

    private void EnsureCorpseBuffer(MultiMesh mesh, ref float[] buffer, int count)
    {
        int allocated = buffer.Length / CorpseInstanceFloats;
        int required = Math.Min(32, Capacity);
        while (required < count) required = Math.Min(Capacity, required * 2);
        // Avoid churn during normal deaths/expiration; shrinking the configured
        // pool can reclaim a large unused allocation.
        if (allocated >= required && allocated <= Capacity) return;
        mesh.VisibleInstanceCount = 0;
        mesh.InstanceCount = required;
        buffer = new float[required * CorpseInstanceFloats];
    }

    private static void WriteCorpseInstance(float[] buffer, int offset, in Transform3D pose, in Color color)
    {
        buffer[offset] = pose.Basis.X.X; buffer[offset + 1] = pose.Basis.Y.X;
        buffer[offset + 2] = pose.Basis.Z.X; buffer[offset + 3] = pose.Origin.X;
        buffer[offset + 4] = pose.Basis.X.Y; buffer[offset + 5] = pose.Basis.Y.Y;
        buffer[offset + 6] = pose.Basis.Z.Y; buffer[offset + 7] = pose.Origin.Y;
        buffer[offset + 8] = pose.Basis.X.Z; buffer[offset + 9] = pose.Basis.Y.Z;
        buffer[offset + 10] = pose.Basis.Z.Z; buffer[offset + 11] = pose.Origin.Z;
        buffer[offset + 12] = color.R; buffer[offset + 13] = color.G;
        buffer[offset + 14] = color.B; buffer[offset + 15] = color.A;
    }
}
