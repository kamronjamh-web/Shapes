using Godot;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    private FpsFortSpatialIndex? _gpuWorldIndex;
    private GpuCrowdWorld? _gpuWorldSnapshot;

    internal GpuCrowdWorld? CaptureGpuCrowdWorld()
    {
        if (!UsesFortifications || _fortSpatialIndex == null || _fortSolids.Count > 64 || _fortRamps.Count > 64)
            return null;
        // CPU terrain and restored-gate rules read live derived settings even
        // when live geometry rebuilds are disabled. Include those values in the
        // cache key so GPU contacts use the same header on that physics tick.
        Vector4 terrain = new(HillPlayableHalf, HillSummitHalf, HillFootHalf, HillHeight);
        Vector4 limits = new(1f, FortWallThickness, HillDoorWidth, _defenseGates.Count);
        if (_gpuWorldSnapshot == null || _gpuWorldIndex != _fortSpatialIndex
            || _gpuWorldSnapshot.Version != FortSpatialIndexVersion
            || _gpuWorldSnapshot.Gates.Length != _defenseGates.Count
            || _gpuWorldSnapshot.Header.Terrain != terrain || _gpuWorldSnapshot.Header.Limits != limits)
        {
            FpsFortSpatialIndex index = _fortSpatialIndex;
            GpuCrowdWorld snapshot = new(_fortSolids.Count, _fortRamps.Count, index.Cells.Length,
                _defenseGates.Count, FortSpatialIndexVersion);
            snapshot.HeaderData[0] = new GpuCrowdWorldHeader
            {
                Terrain = terrain,
                Grid = new Vector4(index.OriginX, index.OriginZ, index.CellSize, _fortGeometryHalf),
                GridWidth = (uint)index.Width, GridHeight = (uint)index.Height,
                SolidCount = (uint)_fortSolids.Count, RampCount = (uint)_fortRamps.Count,
                Limits = limits,
            };
            for (int i = 0; i < snapshot.Solids.Length; i++)
            {
                FortSolid solid = _fortSolids[i];
                snapshot.Solids[i] = new GpuCrowdWorldSolid
                {
                    Center = new Vector4(solid.Center.X, solid.Center.Y, solid.Center.Z, solid.Bottom),
                    Size = new Vector4(solid.Size.X, solid.Size.Y, solid.Size.Z, solid.Top),
                };
            }
            for (int i = 0; i < snapshot.Ramps.Length; i++)
            {
                FortRamp ramp = _fortRamps[i];
                snapshot.Ramps[i] = new GpuCrowdWorldRamp
                {
                    Outward = new Vector4(ramp.Outward.X, ramp.Outward.Y, ramp.Outward.Z, ramp.AcrossCenter),
                    Across = new Vector4(ramp.Across.X, ramp.Across.Y, ramp.Across.Z, ramp.Width),
                    Slope = new Vector4(ramp.Start, ramp.End, ramp.Base, ramp.Top),
                };
            }
            for (int i = 0; i < snapshot.Cells.Length; i++)
            {
                FpsFortSpatialIndex.Cell cell = index.Cells[i];
                snapshot.Cells[i] = new GpuCrowdWorldCell
                {
                    SolidLow = (uint)cell.Solids, SolidHigh = (uint)(cell.Solids >> 32),
                    RampLow = (uint)cell.Ramps, RampHigh = (uint)(cell.Ramps >> 32),
                };
            }
            _gpuWorldIndex = index;
            _gpuWorldSnapshot = snapshot;
        }
        // Gates can be destroyed/restored without a geometry rebuild. Reuse the
        // cached arrays, foot centres and cardinal directions on every capture.
        for (int i = 0; i < _defenseGates.Count; i++)
        {
            FpsDefenseGate gate = _defenseGates[i];
            Vector3 center = _defenseGateCenters[gate.SideIndex], size = gate.Dimensions;
            Vector3 outward = HillOutward(gate.SideIndex);
            ref GpuCrowdWorldGate packed = ref _gpuWorldSnapshot.Gates[i];
            packed.Center.X = center.X; packed.Center.Y = center.Y; packed.Center.Z = center.Z;
            packed.Center.W = gate.IsAlive ? 1f : 0f;
            packed.Size.X = size.X; packed.Size.Y = size.Y; packed.Size.Z = size.Z;
            packed.Outward.X = outward.X; packed.Outward.Y = outward.Y; packed.Outward.Z = outward.Z;
        }
        return _gpuWorldSnapshot;
    }
}
