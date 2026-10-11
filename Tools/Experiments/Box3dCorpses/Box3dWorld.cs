using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Godot;

namespace SuperGatchaRebuild.Experiments.Box3dCorpses;

internal readonly record struct Box3dBodyState(Vector3 Position, Quaternion Rotation,
    Vector3 LinearVelocity, Vector3 AngularVelocity, bool Sleeping);

// Experiment only. One serial native world; never call Step and mutation concurrently.
internal sealed class Box3dWorld : IDisposable
{
    internal const string UpstreamCommit = "5d83df83ab47172c2c745b44315e7538ead02397";
    private nint _world;
    private readonly HashSet<int> _dynamics = new();
    internal int ActiveDynamicCount => Native.Count(Handle);
    internal Box3dWorld(Vector3 gravity, bool corpseContacts = false)
    {
        RequireFinite(gravity);
        _world = Native.Create(gravity.X, gravity.Y, gravity.Z, corpseContacts ? 1 : 0);
        if (_world == 0) throw new InvalidOperationException("Box3D world creation failed.");
    }
    internal int AddStaticBox(Vector3 center, Quaternion rotation, Vector3 size, float friction = .8f)
    {
        NativeBody def = Definition(center, rotation, size, Vector3.Zero, Vector3.Zero, 0, 0, friction, 0, 1);
        return Checked(Native.Box(Handle, ref def, 0));
    }
    internal int AddStaticHull(Vector3 center, Quaternion rotation, Vector3[] localVertices, float friction = .8f)
    {
        if (localVertices.Length < 4 || localVertices.Length > 64) throw new ArgumentOutOfRangeException(nameof(localVertices));
        float[] xyz = new float[localVertices.Length * 3];
        for (int i = 0; i < localVertices.Length; i++)
        {
            RequireFinite(localVertices[i]);
            xyz[i * 3] = localVertices[i].X; xyz[i * 3 + 1] = localVertices[i].Y; xyz[i * 3 + 2] = localVertices[i].Z;
        }
        NativeBody def = Definition(center, rotation, Vector3.One, Vector3.Zero, Vector3.Zero, 0, 0, friction, 0, 1);
        return Checked(Native.Hull(Handle, ref def, xyz, localVertices.Length));
    }
    internal int SpawnBox(Vector3 center, Quaternion rotation, Vector3 size, Vector3 linearVelocity,
        Vector3 angularVelocity, float linearDamping, float angularDamping, float friction, float restitution, float mass = 1f)
        => Spawn(false, center, rotation, size, linearVelocity, angularVelocity, linearDamping, angularDamping, friction, restitution, mass);
    internal int SpawnPyramid(Vector3 center, Quaternion rotation, Vector3 size, Vector3 linearVelocity,
        Vector3 angularVelocity, float linearDamping, float angularDamping, float friction, float restitution, float mass = 1f)
        => Spawn(true, center, rotation, size, linearVelocity, angularVelocity, linearDamping, angularDamping, friction, restitution, mass);
    private int Spawn(bool pyramid, Vector3 center, Quaternion rotation, Vector3 size, Vector3 linearVelocity,
        Vector3 angularVelocity, float linearDamping, float angularDamping, float friction, float restitution, float mass)
    {
        NativeBody def = Definition(center, rotation, size, linearVelocity, angularVelocity, linearDamping, angularDamping, friction, restitution, mass);
        int handle = Checked(pyramid ? Native.Pyramid(Handle, ref def) : Native.Box(Handle, ref def, 1));
        _dynamics.Add(handle); return handle;
    }
    internal void Step(float dt, int subSteps = 4)
    {
        if (Native.Step(Handle, dt, subSteps) == 0) throw new ArgumentOutOfRangeException(nameof(dt));
    }
    internal Box3dBodyState Read(int id)
    {
        NativeState state = default;
        if (Native.Read(Handle, id, ref state) == 0) throw new ArgumentException("Removed or unknown Box3D body.", nameof(id));
        return new(new(state.Px, state.Py, state.Pz), new(state.Qx, state.Qy, state.Qz, state.Qw),
            new(state.Vx, state.Vy, state.Vz), new(state.Wx, state.Wy, state.Wz), state.Sleeping != 0);
    }
    internal bool Remove(int id)
    {
        bool removed = Native.Remove(Handle, id) != 0;
        if (removed) _dynamics.Remove(id);
        return removed;
    }
    internal void ClearDynamics() { Native.Clear(Handle); _dynamics.Clear(); }
    public void Dispose()
    {
        if (_world != 0) { Native.Destroy(_world); _world = 0; _dynamics.Clear(); }
        GC.SuppressFinalize(this);
    }
    ~Box3dWorld() { if (_world != 0) Native.Destroy(_world); }
    private nint Handle => _world != 0 ? _world : throw new ObjectDisposedException(nameof(Box3dWorld));
    private static int Checked(int id) => id > 0 ? id : throw new InvalidOperationException("Box3D body/hull creation failed.");
    private static void RequireFinite(Vector3 v) { if (!v.IsFinite()) throw new ArgumentException("Nonfinite vector."); }
    private static NativeBody Definition(Vector3 p, Quaternion q, Vector3 s, Vector3 v, Vector3 w, float ld, float ad, float f, float r, float m)
    {
        RequireFinite(p); RequireFinite(s); RequireFinite(v); RequireFinite(w);
        if (!q.IsFinite() || Math.Abs(q.LengthSquared() - 1f) > .001f || s.X <= 0 || s.Y <= 0 || s.Z <= 0
            || !float.IsFinite(ld) || !float.IsFinite(ad) || !float.IsFinite(f) || !float.IsFinite(r) || !float.IsFinite(m)
            || ld < 0 || ad < 0 || f < 0 || r < 0 || r > 1 || m <= 0) throw new ArgumentException("Invalid body configuration.");
        return new() { Px=p.X, Py=p.Y, Pz=p.Z, Qx=q.X, Qy=q.Y, Qz=q.Z, Qw=q.W, Sx=s.X, Sy=s.Y, Sz=s.Z,
            Vx=v.X, Vy=v.Y, Vz=v.Z, Wx=w.X, Wy=w.Y, Wz=w.Z, LinearDamping=ld, AngularDamping=ad, Friction=f, Restitution=r, Mass=m };
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBody
    {
        public float Px,Py,Pz,Qx,Qy,Qz,Qw,Sx,Sy,Sz,Vx,Vy,Vz,Wx,Wy,Wz,LinearDamping,AngularDamping,Friction,Restitution,Mass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeState
    {
        public float Px,Py,Pz,Qx,Qy,Qz,Qw,Vx,Vy,Vz,Wx,Wy,Wz;
        public uint Sleeping;
    }
    private static class Native
    {
        // Absolute experiment-local loading avoids altering the application's shared
        // DllImport resolver or copying the engine into production/export directories.
        private static readonly nint Library;
        internal static readonly CreateFn Create;
        internal static readonly DestroyFn Destroy, Clear;
        internal static readonly BoxFn Box;
        internal static readonly PyramidFn Pyramid;
        internal static readonly HullFn Hull;
        internal static readonly StepFn Step;
        internal static readonly ReadFn Read;
        internal static readonly RemoveFn Remove;
        internal static readonly DestroyCountFn Count;
        static Native()
        {
            string path = System.Environment.GetEnvironmentVariable("BOX3D_CORPSES_DLL")
                ?? Path.Combine(ProjectSettings.GlobalizePath("res://"), ".tmp", "box3d-corpses", "build", "box3d_corpses.dll");
            Library = NativeLibrary.Load(path);
            Create=Load<CreateFn>("bxc_world_create"); Destroy=Load<DestroyFn>("bxc_world_destroy");
            Box=Load<BoxFn>("bxc_add_box"); Pyramid=Load<PyramidFn>("bxc_add_pyramid"); Hull=Load<HullFn>("bxc_add_static_hull");
            Step=Load<StepFn>("bxc_step"); Read=Load<ReadFn>("bxc_read"); Remove=Load<RemoveFn>("bxc_remove"); Clear=Load<DestroyFn>("bxc_clear_dynamics");
            Count=Load<DestroyCountFn>("bxc_dynamic_count");
            var abi=Load<AbiFn>("bxc_abi_size");
            if (abi(0)!=Marshal.SizeOf<NativeBody>() || abi(1)!=Marshal.SizeOf<NativeState>()) throw new InvalidOperationException("Box3D shim ABI mismatch.");
        }
        private static T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library,name));
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint CreateFn(float x,float y,float z,int contacts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DestroyFn(nint world);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BoxFn(nint world,ref NativeBody def,int dynamic);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PyramidFn(nint world,ref NativeBody def);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int HullFn(nint world,ref NativeBody def,[In] float[] xyz,int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StepFn(nint world,float dt,int substeps);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ReadFn(nint world,int id,ref NativeState state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RemoveFn(nint world,int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AbiFn(int which);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DestroyCountFn(nint world);
    }
}
