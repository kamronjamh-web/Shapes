using System.Runtime.InteropServices;
using Godot;

namespace SuperGatchaRebuild.Fps.Gpu;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdDriveState
{
    public Vector4 TargetPlayer, TimersYaw, Configuration, Gait, Orientation, PreviousOrientation, Velocity;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdDriveGlobals
{
    public Vector4 Core, Player, Movement, Rolling, Fort, Bounds;
}
