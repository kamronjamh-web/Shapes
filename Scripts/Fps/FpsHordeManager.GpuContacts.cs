using System;
using System.Diagnostics;
using Godot;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    internal Func<GpuCrowdWorld?>? GpuCrowdWorldProvider { get; set; }
    public string LastCrowdContactBackend { get; private set; } = "CPU";
    public string GpuCrowdContactFallbackReason { get; private set; } = string.Empty;
    private GpuCrowdKernel? _gpuContactKernel;
    private GpuCrowdBody[] _gpuContactBodies = Array.Empty<GpuCrowdBody>();
    private int _gpuContactCapacity;
    private bool _gpuContactFailed, _gpuContactWarningIssued;
    private Vector3[] _gpuMovementProposals = Array.Empty<Vector3>();
    private bool[] _gpuMovementPending = Array.Empty<bool>();
    private bool _gpuContactPreparationAttempted, _gpuMovementPrepared;
    private Func<Vector3, Vector3, float, float, Vector3>? _gpuKnownMovementResolver;
    private Func<GpuCrowdWorld?>? _gpuKnownWorldProvider;
    private bool _gpuKnownMovementSupported;
    private GpuCrowdDriveState[] _gpuDriveStates = Array.Empty<GpuCrowdDriveState>();
    private GpuCrowdDriveGlobals _gpuDriveGlobals;
    private bool _gpuDrivePrepared;
    private Vector3 _gpuDriveCore, _gpuDrivePlayer;
    private bool _gpuDrivePlayerAlive;
    private Func<Vector3, Vector3>? _gpuKnownObjective;
    private Func<Vector3, Vector3, Vector3>? _gpuKnownRoute;
    private Func<int, Vector3, Vector3, Vector3>? _gpuKnownSlotRoute;
    private bool _gpuKnownDriveSupported;
    internal bool GpuCrowdFullDriveEnabled { get; set; } = true;
    internal bool LastGpuCrowdDriveResolved { get; private set; }
    internal bool MeasureGpuBoundaryTime { get; set; }
    internal bool MeasureGpuKernelTime { get; set; }
    internal GpuCrowdBoundaryTiming LastGpuBoundaryTiming => _gpuBoundaryTiming;
    private GpuCrowdBoundaryTiming _gpuBoundaryTiming;
    private long BeginGpuBoundaryTiming() => MeasureGpuBoundaryTime ? Stopwatch.GetTimestamp() : 0;
    private double EndGpuBoundaryTiming(long start) => start != 0 ? Stopwatch.GetElapsedTime(start).TotalMilliseconds : 0;
    internal int GpuContactBufferCapacity => _gpuContactKernel?.Capacity ?? 0;
    internal bool LastGpuCrowdMovementResolved { get; private set; }

    private int RequiredGpuContactCapacity()
    {
        int prefix = HighestLiveSlot + 1;
        int rounded = 256;
        while (rounded < prefix) rounded <<= 1;
        return Math.Min(Capacity, rounded);
    }

    public override void _ExitTree()
    {
        DisposeGpuContactKernel();
        ResetGpuCrowdRenderBackend();
    }

    private void DisposeGpuContactKernel()
    {
        _gpuContactKernel?.Dispose();
        _gpuContactKernel = null;
        _gpuContactBodies = Array.Empty<GpuCrowdBody>();
    }

    internal void ResetGpuCrowdContactBackend()
    {
        DisposeGpuContactKernel();
        _gpuContactFailed = _gpuContactWarningIssued = false;
        _gpuContactCapacity = 0;
        GpuCrowdContactFallbackReason = string.Empty;
        LastCrowdContactBackend = "CPU";
        LastGpuCrowdDriveResolved = LastGpuCrowdMovementResolved = false;
    }

    private void SolveCrowdContactsWithBackend(float dt)
    {
        LastCrowdContactBackend = "CPU";
        LastGpuCrowdMovementResolved = false;
        LastGpuCrowdDriveResolved = false;
        try
        {
            if (!TryGpuCrowdContacts(dt))
            {
                if (_gpuDrivePrepared)
                {
                    SnapshotCrowdPreviousPositions();
                    MoveCrowdBodiesCpu(dt, _gpuDriveCore, _gpuDrivePlayer, _gpuDrivePlayerAlive, false);
                }
                else ResolveDeferredMovementOnCpu();
                SolveCrowdContacts(dt);
            }
        }
        finally
        {
            Array.Clear(_gpuMovementPending);
            _gpuContactPreparationAttempted = _gpuMovementPrepared = false;
            _gpuDrivePrepared = false;
        }
    }

    private bool PrepareGpuCrowdMovement() => PrepareGpuCrowdMovement(CorePosition?.Invoke() ?? Vector3.Zero,
        PlayerPosition?.Invoke() ?? Vector3.Zero, IsPlayerAlive?.Invoke() ?? false);

    private bool PrepareGpuCrowdMovement(Vector3 core, Vector3 player, bool playerAlive)
    {
        _gpuBoundaryTiming = default;
        // Only these known map delegates are pure world queries. Generic callbacks
        // may inspect earlier bodies or have side effects and retain CPU ordering.
        var resolver = BodyWorldResolver;
        var provider = GpuCrowdWorldProvider;
        bool worldChanged = !ReferenceEquals(_gpuKnownMovementResolver, resolver) || !ReferenceEquals(_gpuKnownWorldProvider, provider);
        if (worldChanged)
        {
            _gpuKnownMovementResolver = resolver;
            _gpuKnownWorldProvider = provider;
            _gpuKnownMovementSupported = resolver != null && resolver.Target is FpsVerticalSlice map
                && resolver.Method.DeclaringType == typeof(FpsVerticalSlice)
                && resolver.Method.Name == nameof(FpsVerticalSlice.ResolveHordeBodyWorld)
                && resolver.GetInvocationList().Length == 1
                && provider != null && ReferenceEquals(provider.Target, map)
                && provider.Method.Name == nameof(FpsVerticalSlice.CaptureGpuCrowdWorld)
                && provider.GetInvocationList().Length == 1;
        }
        if (!_gpuKnownMovementSupported) return false;
        if (worldChanged || !ReferenceEquals(_gpuKnownObjective, ResolveObjectiveTarget) || !ReferenceEquals(_gpuKnownRoute, ResolveMovementTarget)
            || !ReferenceEquals(_gpuKnownSlotRoute, ResolveMovementTargetForSlot))
        {
            _gpuKnownObjective = ResolveObjectiveTarget;
            _gpuKnownRoute = ResolveMovementTarget;
            _gpuKnownSlotRoute = ResolveMovementTargetForSlot;
            _gpuKnownDriveSupported = ((FpsVerticalSlice)resolver!.Target!).HasGpuCrowdDriveCallbacks(this);
        }
        _gpuContactPreparationAttempted = true;
        _gpuMovementPrepared = TryPrepareGpuCrowdContacts();
        _gpuDrivePrepared = _gpuMovementPrepared && GpuCrowdFullDriveEnabled && _gpuKnownDriveSupported;
        if (_gpuDrivePrepared)
        {
            _gpuDriveCore = core; _gpuDrivePlayer = player; _gpuDrivePlayerAlive = playerAlive;
            _gpuDriveGlobals = ((FpsVerticalSlice)resolver!.Target!).CaptureGpuCrowdDriveGlobals(Settings, core, player, playerAlive, _extent);
            if (_gpuDriveStates.Length != _gpuContactBodies.Length) _gpuDriveStates = new GpuCrowdDriveState[_gpuContactBodies.Length];
        }
        if (_gpuMovementPrepared && _gpuMovementPending.Length != Capacity)
        {
            _gpuMovementPending = new bool[Capacity];
            _gpuMovementProposals = new Vector3[Capacity];
        }
        return _gpuMovementPrepared;
    }

    private void ResolveDeferredMovementOnCpu()
    {
        if (!_gpuMovementPrepared) return;
        foreach (int slot in EnumerateAliveSlots())
        {
            if (!_gpuMovementPending[slot]) continue;
            ref Enemy e = ref _enemies[slot];
            Vector3 proposed = _gpuMovementProposals[slot];
            e.Position = ResolveBodyWorldPosition(e.PreviousPosition, proposed, e.Radius, e.Height);
            if (e.Position.Y > proposed.Y + 0.0001f) e.VerticalVelocity = 0f;
        }
    }

    private bool TryGpuCrowdContacts(float dt)
    {
        if (_gpuContactPreparationAttempted ? !_gpuMovementPrepared : !TryPrepareGpuCrowdContacts()) return false;
        try
        {
            long started = BeginGpuBoundaryTiming();
            GpuCrowdParameters parameters = PackGpuCrowd(_gpuContactBodies, dt, worldResolved: true,
                deferredMovement: _gpuMovementPrepared && !_gpuDrivePrepared);
            if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { BodyPackMs = EndGpuBoundaryTiming(started) };
            if (_gpuDrivePrepared)
            {
                started = BeginGpuBoundaryTiming();
                PackGpuCrowdDrive(_gpuDriveStates, _gpuContactBodies);
                if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { DrivePackMs = EndGpuBoundaryTiming(started) };
                started = BeginGpuBoundaryTiming();
                _gpuContactKernel!.UploadDrive(_gpuDriveStates, _gpuDriveGlobals);
                if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { UploadMs = EndGpuBoundaryTiming(started) };
            }
            started = BeginGpuBoundaryTiming();
            _gpuContactKernel!.Upload(_gpuContactBodies);
            if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { UploadMs = _gpuBoundaryTiming.UploadMs + EndGpuBoundaryTiming(started) };
            var run = _gpuContactKernel.Run(parameters, measureGpuTime: MeasureGpuKernelTime,
                resolveMovement: _gpuMovementPrepared, fullDrive: _gpuDrivePrepared);
            byte[] output = run.Output;
            if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with
                { DispatchAndWaitMs = run.Timing.DispatchAndWaitMs, ReadbackMs = run.Timing.ReadbackMs, GpuExecutionMs = run.Timing.GpuExecutionMs };
            if (_gpuDrivePrepared)
            {
                ApplyGpuCrowdFullResults(output, _gpuContactKernel.DriveOutput);
            }
            else ApplyGpuCrowdResults(output, worldResolved: true);
            LastCrowdContactBackend = "GPU";
            LastGpuCrowdMovementResolved = _gpuMovementPrepared;
            LastGpuCrowdDriveResolved = _gpuDrivePrepared;
            if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { Succeeded = true };
            GpuCrowdContactFallbackReason = string.Empty;
            return true;
        }
        catch (Exception error) { return FailGpuCrowdContacts(error); }
    }

    private bool TryPrepareGpuCrowdContacts()
    {
        long started = BeginGpuBoundaryTiming();
        try { return PrepareGpuCrowdContactsCore(); }
        finally
        {
            if (MeasureGpuBoundaryTime) _gpuBoundaryTiming = _gpuBoundaryTiming with { WorldPrepareMs = EndGpuBoundaryTiming(started) };
        }
    }

    private bool PrepareGpuCrowdContactsCore()
    {
        if (!Settings.GpuCrowdContactsEnabled)
        {
            ResetGpuCrowdContactBackend();
            return false;
        }
        if (_gpuContactCapacity != Capacity)
        {
            DisposeGpuContactKernel();
            _gpuContactFailed = false;
            _gpuContactCapacity = Capacity;
        }
        if (_gpuContactFailed) return false;
        if (!Settings.SoftCollisionEnabled || Settings.ContactIterations != 1)
            return GpuContactFallback("GPU contacts require enabled soft contacts and one contact iteration.");
        if (DisplayServer.GetName() == "headless" || RenderingServer.GetCurrentRenderingDriverName() != "vulkan")
            return GpuContactFallback("GPU contacts require a Vulkan renderer and a graphics device.");
        try
        {
            // The map supplies cached geometry and current gate state. Unsupported
            // worlds use the existing CPU path without attempting device creation.
            GpuCrowdWorld? world = GpuCrowdWorldProvider?.Invoke();
            if (world == null) return GpuContactFallback("This world does not support GPU fort contacts.");
            if (world.Header.Terrain.X != _extent * 0.5f)
                return GpuContactFallback("GPU fort bounds differ from the crowd map bounds.");
            int requiredCapacity = RequiredGpuContactCapacity();
            if (_gpuContactKernel == null || requiredCapacity > _gpuContactKernel.Capacity
                || requiredCapacity <= _gpuContactKernel.Capacity / 4)
            {
                DisposeGpuContactKernel();
                _gpuContactKernel = new GpuCrowdKernel(requiredCapacity, gridCapacity: Capacity);
                _gpuContactBodies = new GpuCrowdBody[requiredCapacity];
            }
            _gpuContactKernel.SetWorld(world);
            return true;
        }
        catch (Exception error)
        {
            // A failed device is not recreated every frame. Explicitly disabling and
            // reenabling the setting permits another attempt.
            return FailGpuCrowdContacts(error);
        }
    }

    private bool FailGpuCrowdContacts(Exception error)
    {
        _gpuContactFailed = true;
        DisposeGpuContactKernel();
        return GpuContactFallback($"GPU contact execution failed: {error.Message}");
    }

    private bool GpuContactFallback(string reason)
    {
        GpuCrowdContactFallbackReason = reason;
        if (!_gpuContactWarningIssued)
        {
            GD.PushWarning($"Crowd contacts use the CPU. {reason}");
            _gpuContactWarningIssued = true;
        }
        return false;
    }
}
