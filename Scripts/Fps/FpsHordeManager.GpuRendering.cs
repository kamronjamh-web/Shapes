using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    private const int GpuRenderBodyBytes = 96;
    private GpuCrowdRenderer? _gpuRenderer;
    private string _gpuRenderFailure = string.Empty;
    private bool _gpuRenderDirty = true, _gpuRenderBoundsSet;
    private bool _gpuRenderOwnsNativeStorage;
    private SceneTree? _gpuRenderEligibilityTree;
    private readonly List<Viewport> _gpuRenderViewports = new();
    private readonly List<Camera3D> _gpuRenderCameras = new();
    private readonly List<WorldEnvironment> _gpuRenderEnvironments = new();
    private double _renderClock, _gpuRenderUploadClock;
    private Aabb _gpuRenderWorldBounds;
    public string LastCrowdRenderBackend { get; private set; } = "CPU";
    public string GpuCrowdRenderFallbackReason { get; private set; } = string.Empty;
    internal long GpuRenderUploadCount { get; private set; }
    // Diagnostic opt-in survives backend recreation; normal rendering never
    // captures timestamps. Completed samples belong to prior GPU frames.
    internal bool MeasureGpuRenderTime { get; set; }
    internal double LastGpuRenderMilliseconds => _gpuRenderer?.LastGpuMilliseconds ?? double.NaN;
    internal long GpuRenderTimingSampleCount => _gpuRenderer?.GpuTimingSampleCount ?? 0;
    internal (long Samples, double Milliseconds) GetGpuRenderTimingSnapshot()
        => _gpuRenderer?.GetGpuTimingSnapshot() ?? (0L, double.NaN);

    internal void MarkRenderStateDirty() => _gpuRenderDirty = true;

    internal void ResetGpuCrowdRenderBackend()
    {
        _gpuRenderer?.Dispose();
        _gpuRenderer = null;
        _gpuRenderFailure = string.Empty;
        _gpuRenderDirty = true;
        _gpuRenderOwnsNativeStorage = false;
        StopGpuRenderEligibilityTracking();
        LastCrowdRenderBackend = "CPU";
        GpuCrowdRenderFallbackReason = string.Empty;
    }

    private bool GpuRenderFallback(string reason)
    {
        LastCrowdRenderBackend = "CPU";
        GpuCrowdRenderFallbackReason = reason;
        // Eligibility can change live; force a fresh authoritative snapshot on
        // return to GPU rendering. A failed renderer remains latched until reset.
        _gpuRenderDirty = true;
        _gpuRenderOwnsNativeStorage = false;
        return false;
    }

    private bool TryPublishGpuRender(int cubes, int pyramids, Transform3D toLocal, float blend)
    {
        if (!Settings.GpuCrowdRenderingEnabled)
        {
            if (_gpuRenderer != null || _gpuRenderFailure.Length > 0 || _gpuRenderEligibilityTree != null)
                ResetGpuCrowdRenderBackend();
            return GpuRenderFallback(string.Empty);
        }
        if (DisplayServer.GetName() == "headless" || RenderingServer.GetCurrentRenderingDriverName() != "vulkan")
            return GpuRenderFallback("GPU crowd rendering requires a Vulkan renderer and a graphics device.");
        if (GpuRenderNeedsMotionVectors())
            return GpuRenderFallback("GPU crowd rendering does not support TAA, temporal upscaling, motion-vector debug, or compositor effects requiring motion vectors.");
        if (GpuCrowdWorldProvider?.Target is not FpsVerticalSlice map
            || GpuCrowdWorldProvider != map.CaptureGpuCrowdWorld
            || MaxFootHeight != map.SampleHillCrowdMaxFootHeight)
            return GpuRenderFallback("This world's visual ceiling callback is not supported by GPU rendering.");
        if (_gpuRenderer?.LastError is string backendError && backendError.Length > 0)
            return GpuRenderFallback(backendError);
        if (_gpuRenderFailure.Length > 0) return GpuRenderFallback(_gpuRenderFailure);

        byte[]? bodyData = null;
        try
        {
            GpuCrowdWorld? world = GpuCrowdWorldProvider();
            if (world == null) return GpuRenderFallback("This world does not support GPU crowd rendering.");
            _gpuRenderer ??= new GpuCrowdRenderer();
            _gpuRenderer.MeasureGpuTime = MeasureGpuRenderTime;
            if (!_gpuRenderOwnsNativeStorage)
            {
                // Godot's individual getters/setters create a CPU data cache.
                // Later visibility growth can flush its stale regions over compute
                // output. Reallocation drops that cache and motion-vector offsets
                // once when handing storage back to GPU publication.
                int cubeAllocation = _cubeRenderBuffer.Length / RenderInstanceFloats;
                int pyramidAllocation = _pyramidRenderBuffer.Length / RenderInstanceFloats;
                _cubes!.VisibleInstanceCount = _pyramids!.VisibleInstanceCount = 0;
                _cubes.InstanceCount = 0;
                _pyramids.InstanceCount = 0;
                _cubes.InstanceCount = cubeAllocation;
                _pyramids.InstanceCount = pyramidAllocation;
                _gpuRenderDirty = true;
                _gpuRenderOwnsNativeStorage = true;
            }
            int count = cubes + pyramids;
            if (_gpuRenderDirty)
            {
                bodyData = ArrayPool<byte>.Shared.Rent(Math.Max(1, count * GpuRenderBodyBytes));
                PackGpuRenderBodies(bodyData.AsSpan(0, count * GpuRenderBodyBytes));
                _gpuRenderUploadClock = _renderClock;
            }
            // Explicit bounds are required: direct GPU writes do not update
            // MultiMesh's CPU transform cache. Include both interpolation ends
            // and the complete rotated body extent, transformed to local space.
            Aabb bounds = toLocal * _gpuRenderWorldBounds;
            _cubes!.CustomAabb = bounds;
            _pyramids!.CustomAabb = bounds;
            _gpuRenderBoundsSet = true;
            int cubeCapacity = _cubeRenderBuffer.Length / RenderInstanceFloats;
            int pyramidCapacity = _pyramidRenderBuffer.Length / RenderInstanceFloats;
            byte[] push = new byte[80];
            Span<float> values = MemoryMarshal.Cast<byte, float>(push);
            WriteGpuRenderLocalTransform(values, toLocal);
            values[12] = blend;
            values[13] = (float)(_renderClock - _gpuRenderUploadClock);
            values[14] = Settings.RollingEnabled ? 1f : 0f;
            values[15] = count;
            values[16] = cubeCapacity;
            values[17] = pyramidCapacity;
            // Submit always takes ownership, including failures/teardown.
            byte[]? submitted = bodyData;
            bodyData = null;
            _gpuRenderer.Submit(_cubes.GetRid(), _pyramids.GetRid(), cubeCapacity, pyramidCapacity,
                submitted, count, world, push);
            if (_gpuRenderDirty) GpuRenderUploadCount++;
            _gpuRenderDirty = false;
            if (_gpuRenderer.LastError.Length > 0) return GpuRenderFallback(_gpuRenderer.LastError);
            LastCrowdRenderBackend = "GPU";
            GpuCrowdRenderFallbackReason = string.Empty;
            return true;
        }
        catch (Exception error)
        {
            if (bodyData != null) ArrayPool<byte>.Shared.Return(bodyData);
            _gpuRenderFailure = $"GPU crowd rendering failed: {error.Message}";
            return GpuRenderFallback(_gpuRenderFailure);
        }
    }

    private bool GpuRenderNeedsMotionVectors()
    {
        if (_gpuRenderEligibilityTree == null)
        {
            _gpuRenderEligibilityTree = GetTree();
            TrackGpuRenderEligibilitySubtree(_gpuRenderEligibilityTree.Root);
            _gpuRenderEligibilityTree.NodeAdded += TrackGpuRenderEligibilityNode;
            _gpuRenderEligibilityTree.NodeRemoved += UntrackGpuRenderEligibilityNode;
        }
        // Godot's motion-vector allocation switch is global to the rendering
        // device, so other scene viewports/effects matter too. Raw RS-created
        // viewports/effects are unsupported: its global counters have no public
        // query API. Ordinary scene-node property changes are checked every frame.
        foreach (Viewport viewport in _gpuRenderViewports)
            if (viewport.UseTaa || viewport.Scaling3DMode is Viewport.Scaling3DModeEnum.Fsr2 or Viewport.Scaling3DModeEnum.MetalfxTemporal
                || viewport.DebugDraw == Viewport.DebugDrawEnum.MotionVectors) return true;
        foreach (Camera3D camera in _gpuRenderCameras) if (CompositorNeedsMotionVectors(camera.Compositor)) return true;
        foreach (WorldEnvironment environment in _gpuRenderEnvironments) if (CompositorNeedsMotionVectors(environment.Compositor)) return true;
        return false;
    }

    private static bool CompositorNeedsMotionVectors(Compositor? compositor)
    {
        if (compositor == null) return false;
        Godot.Collections.Array<CompositorEffect> effects = compositor.CompositorEffects;
        using Godot.Collections.Array effectsOwner = (Godot.Collections.Array)effects;
        foreach (CompositorEffect effect in effects)
            if (effect != null && effect.Enabled && effect.NeedsMotionVectors) return true;
        return false;
    }

    private void TrackGpuRenderEligibilitySubtree(Node node)
    {
        TrackGpuRenderEligibilityNode(node);
        Godot.Collections.Array<Node> children = node.GetChildren();
        using Godot.Collections.Array childrenOwner = (Godot.Collections.Array)children;
        foreach (Node child in children) TrackGpuRenderEligibilitySubtree(child);
    }
    private void TrackGpuRenderEligibilityNode(Node node)
    {
        if (node is Viewport viewport) _gpuRenderViewports.Add(viewport);
        if (node is Camera3D camera) _gpuRenderCameras.Add(camera);
        if (node is WorldEnvironment environment) _gpuRenderEnvironments.Add(environment);
    }
    private void UntrackGpuRenderEligibilityNode(Node node)
    {
        if (node is Viewport viewport) _gpuRenderViewports.Remove(viewport);
        if (node is Camera3D camera) _gpuRenderCameras.Remove(camera);
        if (node is WorldEnvironment environment) _gpuRenderEnvironments.Remove(environment);
    }
    private void StopGpuRenderEligibilityTracking()
    {
        if (_gpuRenderEligibilityTree != null && GodotObject.IsInstanceValid(_gpuRenderEligibilityTree))
        {
            _gpuRenderEligibilityTree.NodeAdded -= TrackGpuRenderEligibilityNode;
            _gpuRenderEligibilityTree.NodeRemoved -= UntrackGpuRenderEligibilityNode;
        }
        _gpuRenderEligibilityTree = null;
        _gpuRenderViewports.Clear(); _gpuRenderCameras.Clear(); _gpuRenderEnvironments.Clear();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = GpuRenderBodyBytes)]
    private struct GpuRenderUploadBody
    {
        public Vector4 PositionRadius;
        public Vector4 PreviousHeight;
        public Vector4 Color;
        public Vector4 PreviousQuaternion;
        public Vector4 Quaternion;
        public Vector4 Misc;
    }

    private void PackGpuRenderBodies(Span<byte> data)
    {
        Span<GpuRenderUploadBody> values = MemoryMarshal.Cast<byte, GpuRenderUploadBody>(data);
        int offset = 0, cube = 0, pyramid = 0;
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (int slot in EnumerateAliveSlots())
        {
            ref Enemy body = ref _enemies[slot];
            ref GpuRenderUploadBody packed = ref values[offset++];
            packed.PositionRadius.X = body.Position.X; packed.PositionRadius.Y = body.Position.Y;
            packed.PositionRadius.Z = body.Position.Z; packed.PositionRadius.W = body.Radius;
            packed.PreviousHeight.X = body.PreviousPosition.X; packed.PreviousHeight.Y = body.PreviousPosition.Y;
            packed.PreviousHeight.Z = body.PreviousPosition.Z; packed.PreviousHeight.W = body.Height;
            packed.Color.X = body.Color.R; packed.Color.Y = body.Color.G;
            packed.Color.Z = body.Color.B; packed.Color.W = body.Color.A;
            packed.PreviousQuaternion.X = body.PreviousRollOrientation.X; packed.PreviousQuaternion.Y = body.PreviousRollOrientation.Y;
            packed.PreviousQuaternion.Z = body.PreviousRollOrientation.Z; packed.PreviousQuaternion.W = body.PreviousRollOrientation.W;
            packed.Quaternion.X = body.RollOrientation.X; packed.Quaternion.Y = body.RollOrientation.Y;
            packed.Quaternion.Z = body.RollOrientation.Z; packed.Quaternion.W = body.RollOrientation.W;
            packed.Misc.X = body.Yaw; packed.Misc.Y = body.Flash;
            bool striker = body.Role == FpsEnemyRole.Striker;
            packed.Misc.Z = striker ? 1f : 0f;
            packed.Misc.W = striker ? pyramid++ : cube++;
            float pad = body.Radius * 2f + body.Height;
            minimum.X = Math.Min(minimum.X, Math.Min(body.Position.X, body.PreviousPosition.X) - pad);
            minimum.Y = Math.Min(minimum.Y, Math.Min(body.Position.Y, body.PreviousPosition.Y) - pad);
            minimum.Z = Math.Min(minimum.Z, Math.Min(body.Position.Z, body.PreviousPosition.Z) - pad);
            maximum.X = Math.Max(maximum.X, Math.Max(body.Position.X, body.PreviousPosition.X) + pad);
            maximum.Y = Math.Max(maximum.Y, Math.Max(body.Position.Y, body.PreviousPosition.Y) + pad);
            maximum.Z = Math.Max(maximum.Z, Math.Max(body.Position.Z, body.PreviousPosition.Z) + pad);
        }
        _gpuRenderWorldBounds = offset == 0 ? new Aabb(Vector3.Zero, Vector3.One) : new Aabb(minimum, maximum - minimum);
    }

    private static void WriteGpuRenderLocalTransform(Span<float> values, Transform3D transform)
    {
        values[0] = transform.Basis.X.X; values[1] = transform.Basis.Y.X;
        values[2] = transform.Basis.Z.X; values[3] = transform.Origin.X;
        values[4] = transform.Basis.X.Y; values[5] = transform.Basis.Y.Y;
        values[6] = transform.Basis.Z.Y; values[7] = transform.Origin.Y;
        values[8] = transform.Basis.X.Z; values[9] = transform.Basis.Y.Z;
        values[10] = transform.Basis.Z.Z; values[11] = transform.Origin.Z;
    }

    internal Task<(byte[] Cubes, byte[] Pyramids)> DebugReadbackGpuCrowdRenderBuffersAsync()
    {
        if (_gpuRenderer == null) throw new InvalidOperationException("GPU crowd renderer is not active.");
        TaskCompletionSource<(byte[], byte[])> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gpuRenderer.RequestDebugReadback((cubes, pyramids, error) =>
        {
            if (error != null) completion.TrySetException(new InvalidOperationException(error));
            else completion.TrySetResult((cubes, pyramids));
        });
        return completion.Task;
    }
}
