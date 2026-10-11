using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Godot;

namespace SuperGatchaRebuild.Fps.Physics;

// Cosmetic physics only. The native world and all handles belong to this thread;
// the main thread sends bounded commands and copies completed pose packets.
internal sealed class Box3dCorpseWorker : IDisposable
{
    private const double FixedStep=1.0/60.0, MaximumPendingSeconds=.25;
    private readonly object _gate=new();
    private readonly AutoResetEvent _signal=new(false);
    private readonly Thread _thread;
    private readonly Box3dNativeWorld.NativeApi _api;
    private readonly Vector3 _gravity;
    private readonly Command[] _pending,_work;
    private readonly uint[] _requestedGenerations,_generations;
    private readonly int[] _handles;
    private readonly Box3dNativeWorld.NativeState[] _states;
    private readonly byte[] _valid;
    private CorpsePose[] _published,_writing;
    private int _publishedCount,_epoch=1;
    private CorpseFrameInfo _publishedInfo;
    private CorpseWorldSnapshot? _pendingWorld;
    private double _pendingSeconds,_droppedSeconds;
    private bool _disposeRequested;
    private int _runningRequest=1;
    private long _timeResetVersion;
    private long _pendingTimeResetVersion;
    private string? _failure;
    private bool _ready;
    private long _sequence;
    private bool _disposed;
    internal int Epoch => Volatile.Read(ref _epoch);
    internal bool IsReady => Volatile.Read(ref _ready);
    internal string FailureReason => Volatile.Read(ref _failure) ?? "";
    internal string NativeLibraryPath => _api.Path;
    internal bool IsStopped => !_thread.IsAlive;
    private readonly record struct Command(bool Pending,bool Spawn,uint Generation,CorpseSpawn Body);

    private Box3dCorpseWorker(Box3dNativeWorld.NativeApi api,Vector3 gravity,int capacity)
    {
        _api=api; _gravity=gravity;
        _pending=new Command[capacity]; _work=new Command[capacity];
        _requestedGenerations=new uint[capacity]; _generations=new uint[capacity]; _handles=new int[capacity];
        _states=new Box3dNativeWorld.NativeState[capacity]; _valid=new byte[capacity];
        _published=new CorpsePose[capacity]; _writing=new CorpsePose[capacity];
        _thread=new Thread(Run) { IsBackground=true,Name="Box3D corpse physics" };
        _thread.Start();
    }
    internal static bool TryCreate(Vector3 gravity,int capacity,out Box3dCorpseWorker? worker,out string reason,string? projectNativePath=null)
    {
        worker=null;
        if (!gravity.IsFinite() || capacity<1 || capacity>FpsHordeSettings.MaximumCorpseCapacity) {
            reason="Invalid corpse gravity or capacity."; return false;
        }
        if (!Box3dNativeWorld.NativeApi.TryLoad(out var api,out reason,projectNativePath)) return false;
        try { worker=new Box3dCorpseWorker(api!,gravity,capacity); return true; }
        catch(Exception error) { reason=error.Message; return false; }
    }
    internal bool TrySpawn(int slot,uint generation,in CorpseSpawn spawn)
    {
        if ((uint)slot>=(uint)_pending.Length || generation==0 || _disposed || FailureReason.Length!=0) return false;
        Box3dNativeWorld.ValidateSpawn(spawn);
        if (!Monitor.TryEnter(_gate)) return false;
        try {
            if (_disposeRequested) return false;
            _requestedGenerations[slot]=generation;
            _pending[slot]=new(true,true,generation,spawn);
        } finally { Monitor.Exit(_gate); }
        _signal.Set(); return true;
    }
    internal bool TryRemove(int slot,uint generation)
    {
        if ((uint)slot>=(uint)_pending.Length || _disposed || FailureReason.Length!=0 || !Monitor.TryEnter(_gate)) return false;
        try {
            if (_disposeRequested || _requestedGenerations[slot]!=generation) return false;
            _requestedGenerations[slot]=0;
            _pending[slot]=new(true,false,generation,default);
        } finally { Monitor.Exit(_gate); }
        _signal.Set(); return true;
    }
    // Geometry replacement is infrequent, fully validated, and coalesced; no scene
    // object or mutable Godot collection crosses the thread boundary.
    internal void ReplaceWorld(CorpseWorldSnapshot snapshot)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Box3dCorpseWorker));
        ValidateWorld(snapshot);
        lock(_gate) { if (_disposeRequested) return; _pendingWorld=snapshot; }
        _signal.Set();
    }
    internal int Clear()
    {
        if (_disposed) return Epoch;
        lock(_gate) {
            // Epochs cannot wrap into a still-valid packet within this worker.
            if (_epoch==int.MaxValue) throw new InvalidOperationException("Corpse worker epoch exhausted.");
            _epoch++; Array.Clear(_pending); Array.Clear(_requestedGenerations);
            _pendingSeconds=0; _publishedCount=0; _publishedInfo=default;
        }
        _signal.Set(); return Epoch;
    }
    internal void Advance(float dt,bool running)
    {
        if (_disposed || FailureReason.Length!=0) return;
        // Publish control before trying the mailbox lock: pause must never be
        // discarded when a completed-packet handoff happens at the same instant.
        if (Interlocked.Exchange(ref _runningRequest,running ? 1 : 0)!=(running ? 1 : 0))
            Interlocked.Increment(ref _timeResetVersion);
        // Brief managed hand-off only. Native simulation never holds this lock.
        // Contention is exceptional; discard cosmetic time rather than wait.
        if (!Monitor.TryEnter(_gate)) {
            if (running) AddDropped(Math.Max(0,double.IsFinite(dt) ? dt : 0));
            _signal.Set(); // Deliver atomic pause/resume even without a mailbox handoff.
            return;
        }
        try {
            if (_disposeRequested) return;
            long reset=Volatile.Read(ref _timeResetVersion);
            if (_pendingTimeResetVersion!=reset) { _pendingSeconds=0; _pendingTimeResetVersion=reset; }
            if (!running || !float.IsFinite(dt) || dt<=0) return;
            double total=_pendingSeconds+dt;
            _pendingSeconds=Math.Min(MaximumPendingSeconds,total);
            AddDropped(Math.Max(0,total-MaximumPendingSeconds));
        } finally { Monitor.Exit(_gate); _signal.Set(); }
    }
    internal bool TryCopyCompleted(Span<CorpsePose> destination,out int count,out CorpseFrameInfo info)
    {
        count=0; info=default;
        if (_disposed || !Monitor.TryEnter(_gate)) return false;
        try {
            if (_publishedInfo.Sequence==0 || _publishedInfo.Epoch!=_epoch) return false;
            if (destination.Length<_publishedCount) throw new ArgumentException("Corpse pose destination is too small.");
            // Exclude recycled generations even if their command arrived while a
            // native step was in flight; never expose an earlier body's pose.
            for(int i=0;i<_publishedCount;i++) {
                CorpsePose pose=_published[i];
                if (_requestedGenerations[pose.Slot]==pose.Generation) destination[count++]=pose;
            }
            info=_publishedInfo; return true;
        } finally { Monitor.Exit(_gate); }
    }
    private void Run()
    {
        try {
            using Box3dNativeWorld world=new(_api,_gravity);
            List<int> statics=new();
            int nativeEpoch=0,geometryVersion=-1;
            long timeResetVersion=0;
            double accumulator=0;
            while(true) {
                _signal.WaitOne();
                int epoch; bool running; double added; long requestedTimeReset; CorpseWorldSnapshot? replacement;
                lock(_gate) {
                    if (_disposeRequested) break;
                    epoch=_epoch; running=Volatile.Read(ref _runningRequest)!=0;
                    requestedTimeReset=Volatile.Read(ref _timeResetVersion);
                    added=_pendingTimeResetVersion==requestedTimeReset ? _pendingSeconds : 0;
                    _pendingSeconds=0;
                    replacement=_pendingWorld; _pendingWorld=null;
                    Array.Copy(_pending,_work,_pending.Length); Array.Clear(_pending);
                }
                if (nativeEpoch!=epoch) {
                    world.ClearDynamics(); Array.Clear(_handles); Array.Clear(_generations);
                    nativeEpoch=epoch; accumulator=0;
                }
                if (timeResetVersion!=requestedTimeReset) { accumulator=0; timeResetVersion=requestedTimeReset; }
                if (replacement!=null) {
                    foreach(int handle in statics) world.Remove(handle);
                    statics.Clear();
                    // Any failure rejects this entire backend; a partial collision
                    // world is never published as successful simulation.
                    foreach(CorpseStaticShape shape in replacement.Shapes) statics.Add(world.AddStatic(shape));
                    geometryVersion=replacement.Version;
                    world.WakeDynamics();
                }
                for(int slot=0;slot<_work.Length;slot++) {
                    Command command=_work[slot]; if (!command.Pending) continue;
                    if (command.Spawn) {
                        if (_handles[slot]!=0) world.Remove(_handles[slot]);
                        _handles[slot]=world.Spawn(command.Body); _generations[slot]=command.Generation;
                    } else {
                        // A spawn followed by removal may coalesce before the
                        // worker sees either. Removal means this slot is empty,
                        // including any older native generation still installed.
                        if (_handles[slot]!=0) world.Remove(_handles[slot]);
                        _handles[slot]=0; _generations[slot]=0;
                    }
                }
                double total=accumulator+added;
                accumulator=running ? Math.Min(MaximumPendingSeconds,total) : 0;
                AddDropped(Math.Max(0,total-MaximumPendingSeconds));
                long started=Stopwatch.GetTimestamp();
                int steps=0;
                while(running && accumulator+1e-9>=FixedStep && steps++<4) {
                    // Pause/clear requests can interrupt a multi-step batch between
                    // ticks; the native world remains solely owned by this thread.
                    if (Volatile.Read(ref _disposeRequested) || Volatile.Read(ref _runningRequest)==0
                        || Volatile.Read(ref _epoch)!=nativeEpoch || Volatile.Read(ref _timeResetVersion)!=requestedTimeReset)
                    { accumulator=0; break; }
                    if (geometryVersion<0) break; // no successful partial/empty startup world
                    world.Step((float)FixedStep); accumulator-=FixedStep;
                }
                double stepMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started=Stopwatch.GetTimestamp();
                world.ReadBatch(_handles,_states,_valid);
                double readMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                int count=0;
                for(int slot=0;slot<_handles.Length;slot++) {
                    if (_handles[slot]==0) continue;
                    if (_valid[slot]==0 || !_states[slot].IsFinite) throw new InvalidOperationException($"Invalid native corpse state in slot {slot}.");
                    var s=_states[slot];
                    _writing[count++]=new(slot,_generations[slot],nativeEpoch,new(s.Px,s.Py,s.Pz),new(s.Qx,s.Qy,s.Qz,s.Qw),
                        new(s.Vx,s.Vy,s.Vz),new(s.Wx,s.Wy,s.Wz),s.Sleeping!=0);
                }
                lock(_gate) {
                    if (_disposeRequested) break;
                    if (_epoch==nativeEpoch) {
                        (_writing,_published)=(_published,_writing); _publishedCount=count;
                        _publishedInfo=new(nativeEpoch,++_sequence,stepMs,readMs,accumulator+_pendingSeconds,
                            Volatile.Read(ref _droppedSeconds),geometryVersion,Volatile.Read(ref _runningRequest)!=0);
                        Volatile.Write(ref _ready,geometryVersion>=0);
                    }
                    if (running && geometryVersion>=0 && accumulator+1e-9>=FixedStep) _signal.Set();
                }
            }
        } catch(Exception error) { Volatile.Write(ref _failure,$"Box3D corpse worker failed: {error.Message}"); }
        finally { Volatile.Write(ref _ready,false); }
    }
    private void AddDropped(double seconds)
    {
        if (seconds<=0) return;
        double before;
        do { before=Volatile.Read(ref _droppedSeconds); }
        while(Interlocked.CompareExchange(ref _droppedSeconds,before+seconds,before)!=before);
    }
    private static void ValidateWorld(CorpseWorldSnapshot snapshot)
    {
        if (snapshot.Version<0 || snapshot.Shapes.Length>4096) throw new ArgumentException("Invalid corpse geometry version or static count.");
        int totalVertices=0;
        foreach(CorpseStaticShape shape in snapshot.Shapes) {
            var body=new CorpseSpawn(false,shape.Position,shape.Rotation,
                shape.Kind==CorpseStaticShapeKind.Box ? shape.Size : Vector3.One,Vector3.Zero,Vector3.Zero,
                0,0,shape.Friction,shape.Restitution);
            Box3dNativeWorld.ValidateSpawn(body);
            if (shape.Kind==CorpseStaticShapeKind.Box) continue;
            totalVertices=checked(totalVertices+shape.Vertices.Length);
            if (totalVertices>300000) throw new ArgumentException("Corpse static snapshot exceeds the vertex budget.");
            if (shape.Kind==CorpseStaticShapeKind.ConvexHull && (shape.Vertices.Length<4 || shape.Vertices.Length>64)
                || shape.Kind==CorpseStaticShapeKind.TriangleMesh && (shape.Vertices.Length<3 || shape.Vertices.Length>300000 || shape.Vertices.Length%3!=0)
                || shape.Kind is not (CorpseStaticShapeKind.ConvexHull or CorpseStaticShapeKind.TriangleMesh))
                throw new ArgumentException("Unsupported corpse static geometry.");
            foreach(Vector3 point in shape.Vertices) if (!point.IsFinite()) throw new ArgumentException("Nonfinite corpse geometry.");
            if (shape.Kind==CorpseStaticShapeKind.TriangleMesh)
                for(int i=0;i<shape.Vertices.Length;i+=3)
                    if ((shape.Vertices[i+1]-shape.Vertices[i]).Cross(shape.Vertices[i+2]-shape.Vertices[i]).LengthSquared()<1e-12f)
                        throw new ArgumentException("Degenerate corpse terrain triangle.");
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed=true;
        lock(_gate) { _disposeRequested=true; _pendingWorld=null; Array.Clear(_pending); }
        _signal.Set();
        // Shutdown is the only synchronization wait. Never free a world or event
        // while its owning native thread is still executing.
        _thread.Join();
        _signal.Dispose();
    }
}
