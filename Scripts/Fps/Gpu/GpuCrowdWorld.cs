using System.Runtime.InteropServices;
using Godot;

namespace SuperGatchaRebuild.Fps.Gpu;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdWorldHeader
{
    public Vector4 Terrain, Grid;
    public uint GridWidth, GridHeight, SolidCount, RampCount;
    public Vector4 Limits;
}
[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdWorldSolid { public Vector4 Center, Size; }
[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdWorldRamp { public Vector4 Outward, Across, Slope; }
[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdWorldCell { public uint SolidLow, SolidHigh, RampLow, RampHigh; }
[StructLayout(LayoutKind.Sequential)]
internal struct GpuCrowdWorldGate { public Vector4 Center, Size, Outward; }

// Static arrays are immutable after capture; only Gates changes between frames.
// HeaderData has one element so kernel uploads require no temporary allocation.
internal sealed class GpuCrowdWorld
{
    internal readonly GpuCrowdWorldHeader[] HeaderData = new GpuCrowdWorldHeader[1];
    internal GpuCrowdWorldHeader Header => HeaderData[0];
    internal readonly GpuCrowdWorldSolid[] Solids;
    internal readonly GpuCrowdWorldRamp[] Ramps;
    internal readonly GpuCrowdWorldCell[] Cells;
    internal readonly GpuCrowdWorldGate[] Gates;
    internal readonly int Version;

    internal GpuCrowdWorld(int solidCount, int rampCount, int cellCount, int gateCount, int version)
    {
        Solids = new GpuCrowdWorldSolid[solidCount];
        Ramps = new GpuCrowdWorldRamp[rampCount];
        Cells = new GpuCrowdWorldCell[cellCount];
        Gates = new GpuCrowdWorldGate[gateCount];
        Version = version;
    }
}
