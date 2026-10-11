using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;

namespace SuperGatchaRebuild.Fps.Physics;

// Called solely by the worker after NativeApi has loaded on the main thread.
// No SceneTree, Node, Resource, PhysicsServer or RenderingServer calls occur here.
internal sealed class Box3dNativeWorld : IDisposable
{
    private readonly NativeApi _api;
    private nint _world;
    internal Box3dNativeWorld(NativeApi api, Vector3 gravity)
    {
        _api = api;
        _world = api.Create(gravity.X, gravity.Y, gravity.Z, 0);
        if (_world == 0) throw new InvalidOperationException("Box3D world creation failed.");
    }
    internal int Spawn(in CorpseSpawn spawn)
    {
        NativeBody def = Definition(spawn.Position,spawn.Rotation,spawn.Size,spawn.LinearVelocity,spawn.AngularVelocity,
            spawn.LinearDamping,spawn.AngularDamping,spawn.Friction,spawn.Restitution,spawn.Mass);
        return Check(spawn.Pyramid ? _api.Pyramid(_world,ref def) : _api.Box(_world,ref def,1));
    }
    internal int AddStatic(CorpseStaticShape shape)
    {
        NativeBody def = Definition(shape.Position,shape.Rotation,shape.Kind == CorpseStaticShapeKind.Box ? shape.Size : Vector3.One,
            Vector3.Zero,Vector3.Zero,0,0,shape.Friction,shape.Restitution,1);
        if (shape.Kind == CorpseStaticShapeKind.Box) return Check(_api.Box(_world,ref def,0));
        float[] xyz = new float[checked(shape.Vertices.Length * 3)];
        for (int i=0;i<shape.Vertices.Length;i++) {
            Vector3 p=shape.Vertices[i]; xyz[3*i]=p.X; xyz[3*i+1]=p.Y; xyz[3*i+2]=p.Z;
        }
        return Check(shape.Kind == CorpseStaticShapeKind.ConvexHull
            ? _api.Hull(_world,ref def,xyz,shape.Vertices.Length)
            : _api.Mesh(_world,ref def,xyz,shape.Vertices.Length));
    }
    internal void Remove(int handle) { if (handle != 0 && _api.Remove(_world,handle)==0) throw new InvalidOperationException("Stale native corpse handle."); }
    internal void ClearDynamics() => _api.Clear(_world);
    internal void WakeDynamics() => _api.Wake(_world);
    internal void Step(float dt) { if (_api.Step(_world,dt,4)==0) throw new InvalidOperationException("Box3D step rejected."); }
    internal void ReadBatch(int[] handles, NativeState[] states, byte[] valid)
    {
        if (_api.ReadBatch(_world,handles,handles.Length,states,valid)==0) throw new InvalidOperationException("Box3D batch read failed.");
    }
    public void Dispose() { if (_world!=0) { _api.Destroy(_world); _world=0; } }
    internal static void ValidateSpawn(in CorpseSpawn spawn) => Definition(spawn.Position,spawn.Rotation,spawn.Size,
        spawn.LinearVelocity,spawn.AngularVelocity,spawn.LinearDamping,spawn.AngularDamping,spawn.Friction,spawn.Restitution,spawn.Mass);
    private static NativeBody Definition(Vector3 p,Quaternion q,Vector3 s,Vector3 v,Vector3 w,float ld,float ad,float f,float r,float m)
    {
        if (!p.IsFinite() || !s.IsFinite() || !v.IsFinite() || !w.IsFinite() || !q.IsFinite()
            || Math.Abs(q.LengthSquared()-1f)>.001f || s.X<=0 || s.Y<=0 || s.Z<=0
            || !float.IsFinite(ld) || !float.IsFinite(ad) || !float.IsFinite(f) || !float.IsFinite(r) || !float.IsFinite(m)
            || ld<0 || ad<0 || f<0 || r<0 || r>1 || m<=0) throw new ArgumentException("Invalid Box3D body configuration.");
        return new() { Px=p.X,Py=p.Y,Pz=p.Z,Qx=q.X,Qy=q.Y,Qz=q.Z,Qw=q.W,Sx=s.X,Sy=s.Y,Sz=s.Z,
            Vx=v.X,Vy=v.Y,Vz=v.Z,Wx=w.X,Wy=w.Y,Wz=w.Z,LinearDamping=ld,AngularDamping=ad,Friction=f,Restitution=r,Mass=m };
    }
    private static int Check(int handle) => handle>0 ? handle : throw new InvalidOperationException("Box3D rejected collision geometry or body creation.");
    [StructLayout(LayoutKind.Sequential)] internal struct NativeBody
    {
        public float Px,Py,Pz,Qx,Qy,Qz,Qw,Sx,Sy,Sz,Vx,Vy,Vz,Wx,Wy,Wz,LinearDamping,AngularDamping,Friction,Restitution,Mass;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeState
    {
        public float Px,Py,Pz,Qx,Qy,Qz,Qw,Vx,Vy,Vz,Wx,Wy,Wz;
        public uint Sleeping;
        internal bool IsFinite => float.IsFinite(Px)&&float.IsFinite(Py)&&float.IsFinite(Pz)
            &&float.IsFinite(Qx)&&float.IsFinite(Qy)&&float.IsFinite(Qz)&&float.IsFinite(Qw)
            &&float.IsFinite(Vx)&&float.IsFinite(Vy)&&float.IsFinite(Vz)&&float.IsFinite(Wx)&&float.IsFinite(Wy)&&float.IsFinite(Wz)
            && Qx*Qx+Qy*Qy+Qz*Qz+Qw*Qw>.5f;
    }
    internal sealed class NativeApi
    {
        private static readonly object LoadGate=new();
        private static NativeApi? _loaded;
        private readonly nint _library;
        internal readonly CreateFn Create;
        internal readonly VoidFn Destroy,Clear,Wake;
        internal readonly BoxFn Box;
        internal readonly PyramidFn Pyramid;
        internal readonly HullFn Hull,Mesh;
        internal readonly StepFn Step;
        internal readonly RemoveFn Remove;
        internal readonly BatchFn ReadBatch;
        internal string Path { get; }
        private NativeApi(string path)
        {
            Path=path; _library=NativeLibrary.Load(path);
            try {
                if (Load<VersionFn>("bxc_abi_version")()!=2) throw new InvalidOperationException("Box3D shim version differs from production ABI2.");
                var size=Load<SizeFn>("bxc_abi_size");
                if (size(0)!=Marshal.SizeOf<NativeBody>() || size(1)!=Marshal.SizeOf<NativeState>()) throw new InvalidOperationException("Box3D struct size mismatch.");
                Create=Load<CreateFn>("bxc_world_create"); Destroy=Load<VoidFn>("bxc_world_destroy");
                Box=Load<BoxFn>("bxc_add_box"); Pyramid=Load<PyramidFn>("bxc_add_pyramid");
                Hull=Load<HullFn>("bxc_add_static_hull"); Mesh=Load<HullFn>("bxc_add_static_mesh");
                Step=Load<StepFn>("bxc_step"); Remove=Load<RemoveFn>("bxc_remove");
                Clear=Load<VoidFn>("bxc_clear_dynamics"); Wake=Load<VoidFn>("bxc_wake_dynamics"); ReadBatch=Load<BatchFn>("bxc_read_batch");
            } catch { NativeLibrary.Free(_library); throw; }
        }
        internal static bool TryLoad(out NativeApi? api,out string reason,string? projectNativePath=null)
        {
            lock(LoadGate) {
                if (_loaded!=null) { api=_loaded; reason=""; return true; }
                if (!OperatingSystem.IsWindows() || !System.Environment.Is64BitProcess) { api=null; reason="Box3D native corpses require Windows x64."; return false; }
                string? overridePath=System.Environment.GetEnvironmentVariable("BOX3D_CORPSES_DLL");
                List<string> paths=new();
                if (!string.IsNullOrEmpty(overridePath)) paths.Add(overridePath);
                else {
                    string assembly=Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(assembly)) paths.Add(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(assembly)!,"box3d_corpses.dll"));
                    paths.Add(System.IO.Path.Combine(AppContext.BaseDirectory,"box3d_corpses.dll"));
                    string? process=System.Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(process)) paths.Add(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(process)!,"box3d_corpses.dll"));
                    // Captured on the main thread by the pool using GlobalizePath.
                    // Native loading itself never calls Godot APIs on the worker.
                    if (!string.IsNullOrEmpty(projectNativePath)) paths.Add(projectNativePath);
                    string cwd=Directory.GetCurrentDirectory();
                    paths.Add(System.IO.Path.Combine(cwd,"Native","Box3D","win-x64","box3d_corpses.dll"));
                    paths.Add(System.IO.Path.Combine(cwd,".tmp","box3d-corpses","build","box3d_corpses.dll"));
                }
                string errors="";
                foreach(string path in paths) {
                    if (!File.Exists(path)) continue;
                    try { _loaded=new NativeApi(path); api=_loaded; reason=""; return true; }
                    catch(Exception error) { errors+=$"{System.IO.Path.GetFileName(path)}: {error.Message} "; }
                }
                api=null; reason=errors.Length==0 ? "Box3D corpse DLL is absent." : errors.Trim(); return false;
            }
        }
        private T Load<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library,name));
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint CreateFn(float x,float y,float z,int contacts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void VoidFn(nint world);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BoxFn(nint world,ref NativeBody body,int dynamic);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PyramidFn(nint world,ref NativeBody body);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int HullFn(nint world,ref NativeBody body,[In]float[] xyz,int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StepFn(nint world,float dt,int substeps);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RemoveFn(nint world,int handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BatchFn(nint world,[In]int[] handles,int count,[Out]NativeState[] states,[Out]byte[] valid);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VersionFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SizeFn(int which);
    }
}
