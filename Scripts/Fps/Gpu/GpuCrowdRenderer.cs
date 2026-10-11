using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace SuperGatchaRebuild.Fps.Gpu;

/// <summary>Main-device pose dispatch into Godot-owned MultiMesh buffers.</summary>
internal sealed class GpuCrowdRenderer : IDisposable
{
    internal const int BodyBytes = 96, PushBytes = 80, InstanceBytes = 80;
    private readonly object _queueLock = new();
    private readonly object _timingLock = new();
    private readonly RDShaderFile _shaderFile;
    private RenderingDevice? _device;
    private Rid _shader, _pipeline, _input, _uniforms, _cubeBuffer, _pyramidBuffer;
    private readonly Rid[] _worldBuffers = new Rid[5];
    private readonly int[] _worldSizes = new int[5];
    private int _inputCapacity, _bodyCount, _ready, _disposed;
    private string _lastError = string.Empty;
    private GpuCrowdWorld? _capturedWorld;
    private byte[][]? _capturedWorldBytes, _uploadedWorldBytes;
    private static long _nextRendererId;
    private readonly string _timestampPrefix = $"crowd-pose-{Interlocked.Increment(ref _nextRendererId)}-";
    private long _timestampSequence, _lastGpuSampleSequence, _gpuTimingSampleCount;
    private int _measureGpuTime;
    private double _lastGpuMilliseconds = double.NaN;
    public bool Ready => Volatile.Read(ref _ready) != 0;
    public string LastError => Volatile.Read(ref _lastError);
    public bool MeasureGpuTime { get => Volatile.Read(ref _measureGpuTime) != 0; set => Volatile.Write(ref _measureGpuTime, value ? 1 : 0); }
    public double LastGpuMilliseconds => Volatile.Read(ref _lastGpuMilliseconds);
    public long GpuTimingSampleCount => Volatile.Read(ref _gpuTimingSampleCount);
    public (long Samples, double Milliseconds) GetGpuTimingSnapshot()
    {
        lock (_timingLock) return (_gpuTimingSampleCount, _lastGpuMilliseconds);
    }

    public GpuCrowdRenderer()
    {
        _shaderFile = GD.Load<RDShaderFile>("res://Resources/Shaders/GpuCrowd/crowd_render.glsl")
            ?? throw new InvalidOperationException("Could not load imported GPU crowd pose shader.");
    }

    /// <summary>Takes ownership of a Shared ArrayPool rental, returning it after upload.
    /// Null input reuses the prior uploaded bodies; push constants are copied.</summary>
    public void Submit(Rid cubeMultiMesh, Rid pyramidMultiMesh, int cubeCapacity, int pyramidCapacity,
        byte[]? newBodyData, int bodyCount, GpuCrowdWorld world, byte[] pushConstants)
    {
        bool queued = false;
        try
        {
            if (bodyCount < 0 || bodyCount > FpsHordeSettings.MaximumCapacity
                || cubeCapacity < 1 || pyramidCapacity < 1 || cubeCapacity > FpsHordeSettings.MaximumCapacity || pyramidCapacity > FpsHordeSettings.MaximumCapacity
                || !cubeMultiMesh.IsValid || !pyramidMultiMesh.IsValid || pushConstants.Length != PushBytes
                || (newBodyData != null && newBodyData.Length < checked(bodyCount * BodyBytes)))
                throw new ArgumentException("Invalid GPU render submission layout or capacity.");
            byte[] push = (byte[])pushConstants.Clone();
            lock (_queueLock)
            {
                if (_disposed != 0) return;
                if (!ReferenceEquals(_capturedWorld, world))
                {
                    _capturedWorldBytes = new[] { Bytes(world.HeaderData), Bytes(world.Solids), Bytes(world.Ramps), Bytes(world.Cells), new byte[48] };
                    _capturedWorld = world;
                }
                byte[][] worldBytes = _capturedWorldBytes!;
                RenderingServer.CallOnRenderThread(Callable.From(() =>
                {
                    try
                    {
                        if (Volatile.Read(ref _disposed) != 0 || LastError.Length != 0) return;
                        Dispatch(cubeMultiMesh, pyramidMultiMesh, cubeCapacity, pyramidCapacity, newBodyData, bodyCount, worldBytes, push);
                        Volatile.Write(ref _ready, 1);
                    }
                    catch (Exception error) { Fail(error); }
                    finally { if (newBodyData != null) ArrayPool<byte>.Shared.Return(newBodyData); }
                }));
                queued = true;
            }
        }
        finally { if (!queued && newBodyData != null) ArrayPool<byte>.Shared.Return(newBodyData); }
    }

    private static byte[] Bytes<T>(T[] values) where T : struct => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private void Initialize()
    {
        if (_device != null) return;
        _device = RenderingServer.GetRenderingDevice() ?? throw new InvalidOperationException("GPU poses require the main RenderingDevice.");
        using RDShaderSpirV spirv = _shaderFile.GetSpirV();
        string error = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (error.Length != 0) throw new InvalidOperationException(error);
        _shader = _device.ShaderCreateFromSpirV(spirv);
        _pipeline = _device.ComputePipelineCreate(_shader);
        if (!_shader.IsValid || !_pipeline.IsValid) throw new InvalidOperationException("GPU pose pipeline creation failed.");
    }

    private void Dispatch(Rid cubeMultiMesh, Rid pyramidMultiMesh, int cubeCapacity, int pyramidCapacity,
        byte[]? input, int count, byte[][] world, byte[] push)
    {
        Initialize();
        RenderingDevice device = _device!;
        bool measure = MeasureGpuTime;
        if (measure) ReadCompletedGpuTimes(device);
        Rid cubes = RenderingServer.MultimeshGetBufferRdRid(cubeMultiMesh);
        Rid pyramids = RenderingServer.MultimeshGetBufferRdRid(pyramidMultiMesh);
        if (!cubes.IsValid || !pyramids.IsValid) throw new InvalidOperationException("Godot did not expose its MultiMesh render buffers.");
        if (cubes != _cubeBuffer || pyramids != _pyramidBuffer)
        {
            FreeUniforms();
            _cubeBuffer = cubes; _pyramidBuffer = pyramids;
        }
        if (input != null)
        {
            int required = 256;
            while (required < count) required = Math.Min(FpsHordeSettings.MaximumCapacity, required * 2);
            if (!_input.IsValid || required > _inputCapacity || required <= _inputCapacity / 4)
            {
                FreeUniforms();
                if (_input.IsValid) device.FreeRid(_input);
                _input = device.StorageBufferCreate((uint)(required * BodyBytes));
                _inputCapacity = required;
                if (!_input.IsValid) throw new InvalidOperationException("GPU pose input allocation failed.");
            }
            if (count > 0 && device.BufferUpdate(_input, 0, (uint)(count * BodyBytes), input.AsSpan(0, count * BodyBytes)) != Error.Ok)
                throw new InvalidOperationException("GPU pose input upload failed.");
            _bodyCount = count;
        }
        else if (!_input.IsValid || count != _bodyCount)
            throw new InvalidOperationException("GPU pose reuse requires an uploaded input with the same live count.");
        if (!ReferenceEquals(_uploadedWorldBytes, world))
        {
            for (int i = 0; i < _worldBuffers.Length; i++)
            {
                int size = Math.Max(i switch { 0 => 64, 1 => 32, 2 => 48, 3 => 16, _ => 48 }, world[i].Length);
                if (size != _worldSizes[i])
                {
                    FreeUniforms();
                    if (_worldBuffers[i].IsValid) device.FreeRid(_worldBuffers[i]);
                    _worldBuffers[i] = device.StorageBufferCreate((uint)size);
                    _worldSizes[i] = size;
                    if (!_worldBuffers[i].IsValid) throw new InvalidOperationException("GPU pose world allocation failed.");
                }
                if (world[i].Length > 0 && device.BufferUpdate(_worldBuffers[i], 0, (uint)world[i].Length, world[i]) != Error.Ok)
                    throw new InvalidOperationException("GPU pose world upload failed.");
            }
            _uploadedWorldBytes = world;
        }
        if (!_uniforms.IsValid || !device.UniformSetIsValid(_uniforms))
        {
            // Replacing an engine-owned MultiMesh buffer automatically invalidates
            // dependent sets, even though the copied RID token remains nonzero.
            FreeUniforms();
            CreateUniforms();
        }
        if (count == 0) return;
        // Capacity values are part of the ABI, rather than trusting stale caller bytes.
        WriteFloat(push, 64, cubeCapacity);
        WriteFloat(push, 68, pyramidCapacity);
        WriteFloat(push, 60, count);
        string timestamp = measure ? _timestampPrefix + checked(++_timestampSequence) : string.Empty;
        if (measure) device.CaptureTimestamp(timestamp + "-start");
        long list = device.ComputeListBegin();
        device.ComputeListBindComputePipeline(list, _pipeline);
        device.ComputeListBindUniformSet(list, _uniforms, 0);
        device.ComputeListSetPushConstant(list, push, PushBytes);
        device.ComputeListDispatch(list, (uint)((count + 63) / 64), 1, 1);
        device.ComputeListEnd();
        if (measure) device.CaptureTimestamp(timestamp + "-end");
        // The main device submits with the engine frame and manages resource
        // barriers. Submit/Sync belong only to local devices; neither is used here.
    }

    private static void WriteFloat(byte[] bytes, int offset, float value) => MemoryMarshal.Write(bytes.AsSpan(offset, 4), in value);

    private void ReadCompletedGpuTimes(RenderingDevice device)
    {
        // The main device resolves older frame query results asynchronously. Pair
        // unique renderer/submission names, not current CPU frame indices; a query
        // frame can be observed by several callbacks before the next one resolves.
        Dictionary<long, ulong> starts = new();
        uint count = device.GetCapturedTimestampsCount();
        for (uint i = 0; i < count; i++)
        {
            string name = device.GetCapturedTimestampName(i);
            if (!name.StartsWith(_timestampPrefix, StringComparison.Ordinal)) continue;
            bool start = name.EndsWith("-start", StringComparison.Ordinal);
            bool end = name.EndsWith("-end", StringComparison.Ordinal);
            int suffixLength = start ? 6 : end ? 4 : 0;
            if (suffixLength == 0 || !long.TryParse(name.AsSpan(_timestampPrefix.Length, name.Length - _timestampPrefix.Length - suffixLength), out long sequence)
                || sequence <= _lastGpuSampleSequence) continue;
            ulong gpu = device.GetCapturedTimestampGpuTime(i);
            if (start) starts[sequence] = gpu;
            else if (starts.TryGetValue(sequence, out ulong begin) && begin > 0 && gpu > begin)
            {
                // Exact Vulkan4.7.2 returns nanoseconds despite API docs stating
                // microseconds; same verified conversion as GpuCrowdKernel.
                lock (_timingLock)
                {
                    Volatile.Write(ref _lastGpuMilliseconds, (gpu - begin) / 1_000_000.0);
                    Interlocked.Increment(ref _gpuTimingSampleCount);
                }
                _lastGpuSampleSequence = sequence;
            }
        }
    }

    private void CreateUniforms()
    {
        Godot.Collections.Array<RDUniform> values = new();
        // Array<T> wraps this exact disposable backing Array (Godot 4.7.2).
        using Godot.Collections.Array valuesOwner = (Godot.Collections.Array)values;
        try
        {
            AddUniform(values, 0, _input); AddUniform(values, 1, _cubeBuffer); AddUniform(values, 2, _pyramidBuffer);
            for (int i = 0; i < _worldBuffers.Length; i++) AddUniform(values, i + 5, _worldBuffers[i]);
            _uniforms = _device!.UniformSetCreate(values, _shader, 0);
            if (!_uniforms.IsValid) throw new InvalidOperationException("GPU pose uniform binding failed.");
        }
        finally { foreach (RDUniform value in values) value.Dispose(); }
    }
    private static void AddUniform(Godot.Collections.Array<RDUniform> values, int binding, Rid buffer)
    {
        RDUniform uniform = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = binding };
        uniform.AddId(buffer); values.Add(uniform);
    }
    private void FreeUniforms()
    {
        if (_uniforms.IsValid && _device!.UniformSetIsValid(_uniforms)) _device.FreeRid(_uniforms);
        _uniforms = default;
    }
    private void Fail(Exception error)
    {
        Volatile.Write(ref _lastError, error.Message);
        Volatile.Write(ref _ready, 0);
    }

    /// <summary>Diagnostic only. Callback runs on the render thread; raw buffers
    /// have 80-byte instance strides. No readback occurs in normal Submit.</summary>
    public void RequestDebugReadback(Action<byte[], byte[], string?> callback)
    {
        lock (_queueLock)
        {
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                byte[] cubes = Array.Empty<byte>(), pyramids = Array.Empty<byte>();
                string? error = null;
                try
                {
                    if (Volatile.Read(ref _disposed) != 0 || _device == null || !Ready) throw new InvalidOperationException(LastError.Length == 0 ? "GPU renderer is not ready." : LastError);
                    cubes = _device.BufferGetData(_cubeBuffer);
                    pyramids = _device.BufferGetData(_pyramidBuffer);
                }
                catch (Exception failure) { error = failure.Message; }
                try { callback(cubes, pyramids, error); }
                catch (Exception failure) { GD.PushError($"GPU pose diagnostic callback failed: {failure}"); }
            }));
        }
    }

    public void Dispose()
    {
        lock (_queueLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Volatile.Write(ref _ready, 0);
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                try
                {
                    if (_device == null) return;
                    FreeUniforms();
                    if (_pipeline.IsValid) _device.FreeRid(_pipeline);
                    if (_shader.IsValid) _device.FreeRid(_shader);
                    if (_input.IsValid) _device.FreeRid(_input);
                    foreach (Rid buffer in _worldBuffers) if (buffer.IsValid) _device.FreeRid(buffer);
                    // Engine-owned MultiMesh buffers and main device are never freed.
                }
                catch (Exception error) { Fail(error); }
            }));
        }
    }
}
