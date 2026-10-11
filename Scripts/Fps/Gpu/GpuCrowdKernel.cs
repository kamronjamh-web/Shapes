using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;

namespace SuperGatchaRebuild.Fps.Gpu;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdBody
{
    public Vector4 PositionRadius, PreviousHeight, DrivePressure, WorldVelocityAlive;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdResult
{
    public Vector4 PositionPressure, VelocityCounts;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdParameters
{
    public uint Capacity, Buckets, Phase, Stacking;
    public float HorizontalCell, VerticalCell, MaxRadius, MaxHeight;
    public float Dt, Stiffness, ClimbStep, MaxPile;
    public uint Budget;
    public float MaxPush;
    public uint Reserved0, Reserved1;
}

internal readonly record struct GpuCrowdTiming(double DispatchAndWaitMs, double ReadbackMs, double GpuExecutionMs);

/// <summary>Local compute device for the optional crowd backend and investigation.</summary>
internal sealed class GpuCrowdKernel : IDisposable
{
    private readonly RenderingDevice _device;
    private readonly Rid _shader, _pipeline, _bodies, _heads, _next, _cells, _results;
    private readonly Rid _driveStates, _driveGlobals;
    private readonly GpuCrowdDriveGlobals[] _driveGlobalsUpload = new GpuCrowdDriveGlobals[1];
    internal byte[] DriveOutput { get; private set; } = Array.Empty<byte>();
    private Rid _uniforms;
    private readonly Rid[] _worldBuffers = new Rid[5];
    private readonly int[] _worldBufferSizes = new int[5];
    private readonly GpuCrowdWorldHeader[] _disabledWorldHeader = new GpuCrowdWorldHeader[1];
    private GpuCrowdWorld? _world;
    private bool _worldInitialized;
    private readonly byte[] _push = new byte[64];
    private ulong _sampleId;
    private bool _disposed;
    public int Capacity { get; }
    public int Buckets { get; }
    public string DeviceName => _device.GetDeviceName();

    public GpuCrowdKernel(int capacity, int? gridCapacity = null)
    {
        int hashCapacity = gridCapacity ?? capacity;
        if (capacity < 1 || capacity > FpsHordeSettings.MaximumCapacity
            || hashCapacity < capacity || hashCapacity > FpsHordeSettings.MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(gridCapacity), "GPU body/grid capacities must be positive, grid capacity must cover bodies, and both must fit the crowd limit.");
        if (RenderingServer.GetCurrentRenderingDriverName() != "vulkan")
            throw new InvalidOperationException("GPU timestamp conversion is verified only for the experiment's Vulkan driver.");
        Capacity = capacity;
        int buckets = 512;
        // Runtime body buffers may cover only the live slot prefix. Preserve the
        // reserved CPU grid size because hash collisions count toward scan budgets.
        while (buckets < hashCapacity * 4) buckets <<= 1;
        Buckets = buckets;
        if (Marshal.SizeOf<GpuCrowdBody>() != 64 || Marshal.SizeOf<GpuCrowdResult>() != 32 || Marshal.SizeOf<GpuCrowdParameters>() != 64)
            throw new InvalidOperationException("GPU buffer layout must match the shader's std430 structs.");
        if (Marshal.SizeOf<GpuCrowdWorldHeader>() != 64 || Marshal.SizeOf<GpuCrowdWorldSolid>() != 32
            || Marshal.SizeOf<GpuCrowdWorldRamp>() != 48 || Marshal.SizeOf<GpuCrowdWorldCell>() != 16
            || Marshal.SizeOf<GpuCrowdWorldGate>() != 48)
            throw new InvalidOperationException("GPU world layout must match the shader's std430 structs.");
        _device = RenderingServer.CreateLocalRenderingDevice()
            ?? throw new InvalidOperationException("GPU probe needs a RenderingDevice renderer and a real graphics device; headless/Compatibility is unsupported.");
        try
        {
            // Imported SPIR-V carries the GLSL include and works in exported packs
            // without raw source filters or runtime include assembly.
            RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://Resources/Shaders/GpuCrowd/crowd_contacts.glsl")
                ?? throw new InvalidOperationException("Could not load the imported crowd shader.");
            using RDShaderSpirV spirv = shaderFile.GetSpirV();
            string error = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            _shader = _device.ShaderCreateFromSpirV(spirv);
            _pipeline = _device.ComputePipelineCreate(_shader);
            if (!_pipeline.IsValid) throw new InvalidOperationException("Could not create the GPU contact pipeline.");
            _bodies = _device.StorageBufferCreate((uint)(capacity * 64));
            _heads = _device.StorageBufferCreate((uint)(buckets * 4));
            _next = _device.StorageBufferCreate((uint)(capacity * 4));
            _cells = _device.StorageBufferCreate((uint)(capacity * 16));
            _results = _device.StorageBufferCreate((uint)(capacity * 32));
            if (Marshal.SizeOf<GpuCrowdDriveState>() != 112 || Marshal.SizeOf<GpuCrowdDriveGlobals>() != 96)
                throw new InvalidOperationException("GPU drive layout must match std430.");
            _driveStates = _device.StorageBufferCreate((uint)(capacity * 112));
            _driveGlobals = _device.StorageBufferCreate(96);
            SetWorld(null);
        }
        catch { Dispose(); throw; }
    }

    public void SetWorld(GpuCrowdWorld? world)
    {
        bool resized = false;
        for (int i = 0; i < _worldBuffers.Length; i++)
        {
            int bytes = i switch
            {
                0 => 64,
                1 => Math.Max(1, world?.Solids.Length ?? 0) * 32,
                2 => Math.Max(1, world?.Ramps.Length ?? 0) * 48,
                3 => Math.Max(1, world?.Cells.Length ?? 0) * 16,
                _ => Math.Max(1, world?.Gates.Length ?? 0) * 48,
            };
            if (_worldBufferSizes[i] == bytes) continue;
            if (!resized && _uniforms.IsValid)
            {
                _device.FreeRid(_uniforms);
                _uniforms = default;
            }
            resized = true;
            if (_worldBuffers[i].IsValid) _device.FreeRid(_worldBuffers[i]);
            _worldBuffers[i] = default;
            _worldBufferSizes[i] = 0;
            _worldBuffers[i] = _device.StorageBufferCreate((uint)bytes);
            if (!_worldBuffers[i].IsValid) throw new InvalidOperationException("Could not allocate GPU world buffer.");
            _worldBufferSizes[i] = bytes;
        }
        if (resized || !_uniforms.IsValid) RebuildUniforms();
        if (!_worldInitialized || resized || !ReferenceEquals(_world, world))
        {
            UploadWorldArray(_worldBuffers[0], world?.HeaderData ?? _disabledWorldHeader);
            if (world != null)
            {
                UploadWorldArray(_worldBuffers[1], world.Solids);
                UploadWorldArray(_worldBuffers[2], world.Ramps);
                UploadWorldArray(_worldBuffers[3], world.Cells);
            }
            _world = world;
            _worldInitialized = true;
        }
        if (world != null) UploadWorldArray(_worldBuffers[4], world.Gates);
    }

    private void UploadWorldArray<T>(Rid buffer, T[] values) where T : struct
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values.AsSpan());
        if (bytes.IsEmpty) return;
        if (_device.BufferUpdate(buffer, 0, (uint)bytes.Length, bytes) != Error.Ok)
            throw new InvalidOperationException("GPU world upload failed.");
    }

    private void RebuildUniforms()
    {
        Godot.Collections.Array<RDUniform> uniforms = new();
        using Godot.Collections.Array uniformOwner = (Godot.Collections.Array)uniforms;
        try
        {
            Rid[] buffers = { _bodies, _heads, _next, _cells, _results,
                _worldBuffers[0], _worldBuffers[1], _worldBuffers[2], _worldBuffers[3], _worldBuffers[4], _driveStates, _driveGlobals };
            foreach (Rid buffer in buffers)
            {
                RDUniform uniform = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = uniforms.Count };
                uniform.AddId(buffer);
                uniforms.Add(uniform);
            }
            _uniforms = _device.UniformSetCreate(uniforms, _shader, 0);
            if (!_uniforms.IsValid) throw new InvalidOperationException("Could not bind GPU crowd/world buffers.");
        }
        finally { foreach (RDUniform uniform in uniforms) uniform.Dispose(); }
    }

    public void Upload(GpuCrowdBody[] bodies)
    {
        if (bodies.Length != Capacity) throw new ArgumentException("Snapshot capacity mismatch.");
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(bodies.AsSpan());
        Error error = _device.BufferUpdate(_bodies, 0, (uint)bytes.Length, bytes);
        if (error != Error.Ok) throw new InvalidOperationException($"GPU input upload failed: {error}");
    }

    internal void UploadDrive(GpuCrowdDriveState[] states, GpuCrowdDriveGlobals globals)
    {
        if (states.Length != Capacity) throw new ArgumentException("Drive snapshot capacity mismatch.");
        UploadWorldArray(_driveStates, states);
        _driveGlobalsUpload[0] = globals;
        UploadWorldArray(_driveGlobals, _driveGlobalsUpload);
    }

    // A repeated dispatch reuses an immutable input. It measures amortized submission
    // throughput only; it is NOT a simulation advancing several frames without readback.
    public (byte[] Output, GpuCrowdTiming Timing) Run(GpuCrowdParameters parameters, int repeats = 1, bool readback = true,
        bool worldQueries = false, bool measureGpuTime = true, bool resolveMovement = false, bool fullDrive = false)
    {
        if (fullDrive) resolveMovement = true;
        if (resolveMovement && (worldQueries || repeats != 1))
            throw new ArgumentException("Movement resolution requires one advancing contact dispatch.");
        if (resolveMovement && _world == null)
            throw new InvalidOperationException("Movement resolution requires GPU world geometry.");
        parameters.Capacity = (uint)Capacity;
        parameters.Buckets = (uint)Buckets;
        string startName = measureGpuTime ? $"crowd-{checked(++_sampleId)}-start" : "";
        string endName = measureGpuTime ? $"crowd-{_sampleId}-end" : "";
        long started = Stopwatch.GetTimestamp();
        if (measureGpuTime) _device.CaptureTimestamp(startName);
        long list = _device.ComputeListBegin();
        _device.ComputeListBindComputePipeline(list, _pipeline);
        _device.ComputeListBindUniformSet(list, _uniforms, 0);
        for (int repeat = 0; repeat < repeats; repeat++)
        for (uint dispatchPhase = 0; dispatchPhase < (worldQueries ? 1u : fullDrive ? 7u : resolveMovement ? 5u : 4u); dispatchPhase++)
        {
            uint phase = worldQueries ? 4u : fullDrive ? (dispatchPhase == 0u ? 6u : dispatchPhase == 1u ? 5u : dispatchPhase == 6u ? 7u : dispatchPhase - 2u)
                : resolveMovement ? (dispatchPhase == 0u ? 5u : dispatchPhase - 1u) : dispatchPhase;
            parameters.Phase = phase;
            MemoryMarshal.Write(_push.AsSpan(), in parameters);
            _device.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
            uint invocations = phase is 0 or 2 ? (uint)Buckets : (uint)Capacity;
            _device.ComputeListDispatch(list, (invocations + 63) / 64, 1, 1);
            _device.ComputeListAddBarrier(list);
        }
        _device.ComputeListEnd();
        if (measureGpuTime) _device.CaptureTimestamp(endName);
        _device.Submit();
        _device.Sync();
        double dispatchAndWait = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        double gpuExecution = measureGpuTime ? ReadGpuExecutionMilliseconds(startName, endName) : double.NaN;
        started = Stopwatch.GetTimestamp();
        byte[] output = readback ? _device.BufferGetData(_results) : Array.Empty<byte>();
        DriveOutput = fullDrive && readback ? _device.BufferGetData(_driveStates) : Array.Empty<byte>();
        double read = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (readback && output.Length != Capacity * 32) throw new InvalidOperationException("Incomplete GPU result buffer.");
        if (fullDrive && readback && DriveOutput.Length != Capacity * 112) throw new InvalidOperationException("Incomplete GPU drive buffer.");
        return (output, new GpuCrowdTiming(dispatchAndWait, read, gpuExecution));
    }

    private double ReadGpuExecutionMilliseconds(string startName, string endName)
    {
        // Local Sync waits and retrieves the just-submitted query results. Pair by
        // unique names: GetCapturedTimestampsFrame can repeat with rendering disabled.
        ulong start = 0, end = 0;
        int startCount = 0, endCount = 0;
        uint count = _device.GetCapturedTimestampsCount();
        for (uint index = 0; index < count; index++)
        {
            string name = _device.GetCapturedTimestampName(index);
            if (name == startName)
            {
                startCount++;
                start = _device.GetCapturedTimestampGpuTime(index);
            }
            else if (name == endName)
            {
                endCount++;
                end = _device.GetCapturedTimestampGpuTime(index);
            }
        }
        if (startCount != 1 || endCount != 1 || start == 0 || end == 0 || end <= start)
            throw new InvalidOperationException($"Invalid GPU timestamp pair {startName}/{endName}: matches {startCount}/{endCount}, values {start}/{end}.");
        // This Vulkan-only experiment uses Godot 4.7.2 commit ed1daf0bf:
        // drivers/vulkan/rendering_device_driver_vulkan.cpp converts query ticks
        // using Vulkan timestampPeriod and returns nanoseconds; the RD getter
        // does not rescale. This differs from the API documentation's stated units.
        return (end - start) / 1_000_000.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Rid rid in new[] { _uniforms, _pipeline, _shader, _results, _cells, _next, _heads, _bodies, _driveStates, _driveGlobals })
            if (rid.IsValid) _device.FreeRid(rid);
        foreach (Rid rid in _worldBuffers)
            if (rid.IsValid) _device.FreeRid(rid);
        _device.Free();
    }
}
