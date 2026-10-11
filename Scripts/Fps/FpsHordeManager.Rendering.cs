using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    // Godot's 3D MultiMesh layout: three transform rows, RGBA, custom RGBA.
    private const int RenderInstanceFloats = 20;
    private float[] _cubeRenderBuffer = Array.Empty<float>();
    private float[] _pyramidRenderBuffer = Array.Empty<float>();

    public override void _Process(double delta)
    {
        if (_cubes == null || _pyramids == null) return;
        _renderClock += delta;
        int cubeCount = 0, pyramidCount = 0;
        foreach (int slot in EnumerateAliveSlots())
        {
            _enemies[slot].Flash = Mathf.Max(0f, _enemies[slot].Flash - (float)delta);
            if (_enemies[slot].Role == FpsEnemyRole.Striker) pyramidCount++;
            else cubeCount++;
        }
        EnsureRenderBuffer(_cubes, ref _cubeRenderBuffer, cubeCount);
        EnsureRenderBuffer(_pyramids, ref _pyramidRenderBuffer, pyramidCount);
        Transform3D toLocal = GlobalTransform.AffineInverse();
        float blend = CombatEnabled && Settings.Enabled
            ? Mathf.Clamp((float)_accumulator * Mathf.Clamp(Settings.SimulationHz, 5f, 60f), 0f, 1f) : 1f;
        if (TryPublishGpuRender(cubeCount, pyramidCount, toLocal, blend))
        {
            _cubes.VisibleInstanceCount = cubeCount;
            _pyramids.VisibleInstanceCount = pyramidCount;
            RenderedInstanceCount = cubeCount + pyramidCount;
            return;
        }
        // GPU writes bypass Godot's CPU-side transform cache. Reset explicit
        // bounds and publish complete CPU buffers when switching back.
        if (_gpuRenderBoundsSet)
        {
            _cubes.CustomAabb = default;
            _pyramids.CustomAabb = default;
            _gpuRenderBoundsSet = false;
        }
        int cubeOffset = 0, pyramidOffset = 0;
        foreach (int slot in EnumerateAliveSlots())
        {
            ref Enemy enemy = ref _enemies[slot];
            Vector3 feet = enemy.PreviousPosition.Lerp(enemy.Position, blend);
            Transform3D transform = toLocal * EnemyVisualTransform(enemy, feet, blend);
            if (enemy.Role == FpsEnemyRole.Striker)
            {
                WriteRenderInstance(_pyramidRenderBuffer, pyramidOffset, transform, enemy.Color, enemy.Flash > 0f);
                pyramidOffset += RenderInstanceFloats;
            }
            else
            {
                WriteRenderInstance(_cubeRenderBuffer, cubeOffset, transform, enemy.Color, enemy.Flash > 0f);
                cubeOffset += RenderInstanceFloats;
            }
        }
        // Arrays are reused. Only an occupied role needs an upload; hidden tail
        // entries never determine instance count or retain a dead visible enemy.
        if (cubeCount > 0) _cubes.Buffer = _cubeRenderBuffer;
        if (pyramidCount > 0) _pyramids.Buffer = _pyramidRenderBuffer;
        _cubes.VisibleInstanceCount = cubeCount;
        _pyramids.VisibleInstanceCount = pyramidCount;
        RenderedInstanceCount = cubeCount + pyramidCount;
    }

    private void EnsureRenderBuffer(MultiMesh mesh, ref float[] buffer, int count)
    {
        int required = Math.Min(256, Capacity);
        while (required < count) required = Math.Min(Capacity, required * 2);
        int allocated = buffer.Length / RenderInstanceFloats;
        if (required == allocated || (required < allocated && required > allocated / 4)) return;
        // Changing InstanceCount clears native storage. Fill/upload the complete
        // new buffer before publishing its visible count in this same frame.
        mesh.VisibleInstanceCount = 0;
        mesh.InstanceCount = required;
        buffer = new float[required * RenderInstanceFloats];
    }

    private static void WriteRenderInstance(float[] buffer, int offset, in Transform3D transform, in Color color, bool flash)
    {
        // Basis exposes columns; MultiMesh expects row-major 3x4 data.
        buffer[offset] = transform.Basis.X.X;
        buffer[offset + 1] = transform.Basis.Y.X;
        buffer[offset + 2] = transform.Basis.Z.X;
        buffer[offset + 3] = transform.Origin.X;
        buffer[offset + 4] = transform.Basis.X.Y;
        buffer[offset + 5] = transform.Basis.Y.Y;
        buffer[offset + 6] = transform.Basis.Z.Y;
        buffer[offset + 7] = transform.Origin.Y;
        buffer[offset + 8] = transform.Basis.X.Z;
        buffer[offset + 9] = transform.Basis.Y.Z;
        buffer[offset + 10] = transform.Basis.Z.Z;
        buffer[offset + 11] = transform.Origin.Z;
        buffer[offset + 12] = color.R;
        buffer[offset + 13] = color.G;
        buffer[offset + 14] = color.B;
        buffer[offset + 15] = color.A;
        buffer[offset + 16] = flash ? 1f : 0f;
        buffer[offset + 17] = 0f;
        buffer[offset + 18] = 0f;
        buffer[offset + 19] = 1f;
    }
}
