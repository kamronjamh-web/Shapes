using System;
using Godot;
using BitOperations = System.Numerics.BitOperations;

namespace SuperGatchaRebuild.Fps;

// Immutable XZ broad phase. Geometry and queries retain their original narrow phase.
internal sealed class FpsFortSpatialIndex
{
    internal struct Cell { public ulong Solids, Ramps; }
    internal readonly record struct Bounds(float MinX, float MinZ, float MaxX, float MaxZ);
    public float CellSize { get; }
    public float OriginX { get; }
    public float OriginZ { get; }
    public int Width { get; }
    public int Height { get; }
    public Cell[] Cells { get; }
    private readonly Cell _all;

    private FpsFortSpatialIndex(Bounds[] solids, Bounds[] ramps, float cellSize)
    {
        CellSize = cellSize;
        float minX = float.PositiveInfinity, minZ = minX;
        float maxX = float.NegativeInfinity, maxZ = maxX;
        foreach (Bounds[] group in new[] { solids, ramps })
        foreach (Bounds bounds in group)
        {
            minX = Mathf.Min(minX, bounds.MinX); minZ = Mathf.Min(minZ, bounds.MinZ);
            maxX = Mathf.Max(maxX, bounds.MaxX); maxZ = Mathf.Max(maxZ, bounds.MaxZ);
        }
        OriginX = Mathf.Floor(minX / cellSize) * cellSize;
        OriginZ = Mathf.Floor(minZ / cellSize) * cellSize;
        Width = Mathf.FloorToInt((maxX - OriginX) / cellSize) + 1;
        Height = Mathf.FloorToInt((maxZ - OriginZ) / cellSize) + 1;
        Cells = new Cell[checked(Width * Height)];
        _all = new Cell { Solids = AllBits(solids.Length), Ramps = AllBits(ramps.Length) };
        Add(solids, false); Add(ramps, true);
    }

    public static FpsFortSpatialIndex? Create(Bounds[] solids, Bounds[] ramps, float cellSize = 8f)
        => solids.Length > 64 || ramps.Length > 64 || solids.Length + ramps.Length == 0
            ? null : new FpsFortSpatialIndex(solids, ramps, cellSize);

    private static ulong AllBits(int count) => count == 64 ? ulong.MaxValue : (1UL << count) - 1UL;

    private void Add(Bounds[] bounds, bool ramps)
    {
        for (int i = 0; i < bounds.Length; i++)
        {
            Bounds b = bounds[i];
            int minX = Mathf.FloorToInt((b.MinX - OriginX) / CellSize);
            int minZ = Mathf.FloorToInt((b.MinZ - OriginZ) / CellSize);
            int maxX = Mathf.FloorToInt((b.MaxX - OriginX) / CellSize);
            int maxZ = Mathf.FloorToInt((b.MaxZ - OriginZ) / CellSize);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                ref Cell cell = ref Cells[z * Width + x];
                if (ramps) cell.Ramps |= 1UL << i; else cell.Solids |= 1UL << i;
            }
        }
    }

    public Cell Query(Vector3 from, Vector3 desired, float padding)
    {
        if (!from.IsFinite() || !desired.IsFinite() || !float.IsFinite(padding)) return _all;
        padding = Mathf.Max(0f, padding);
        float minX = Mathf.Min(from.X, desired.X) - padding, maxX = Mathf.Max(from.X, desired.X) + padding;
        float minZ = Mathf.Min(from.Z, desired.Z) - padding, maxZ = Mathf.Max(from.Z, desired.Z) + padding;
        if (maxX < OriginX || maxZ < OriginZ || minX >= OriginX + Width * CellSize || minZ >= OriginZ + Height * CellSize)
            return default;
        int x0 = Mathf.FloorToInt((Mathf.Max(minX, OriginX) - OriginX) / CellSize);
        int z0 = Mathf.FloorToInt((Mathf.Max(minZ, OriginZ) - OriginZ) / CellSize);
        int x1 = Mathf.Min(Width - 1, Mathf.FloorToInt((Mathf.Min(maxX, OriginX + Width * CellSize) - OriginX) / CellSize));
        int z1 = Mathf.Min(Height - 1, Mathf.FloorToInt((Mathf.Min(maxZ, OriginZ + Height * CellSize) - OriginZ) / CellSize));
        Cell result = default;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            ref Cell cell = ref Cells[z * Width + x];
            result.Solids |= cell.Solids; result.Ramps |= cell.Ramps;
        }
        return result;
    }

    // Pattern-based enumeration avoids heap allocation, including full-scan fallback.
    internal readonly struct Candidates(ulong mask, int count, bool filtered)
    {
        public Enumerator GetEnumerator() => new(mask, count, filtered);
        internal struct Enumerator(ulong mask, int count, bool filtered)
        {
            private ulong _mask = mask;
            private int _next;
            public int Current { get; private set; }
            public bool MoveNext()
            {
                if (!filtered) { Current = _next++; return Current < count; }
                if (_mask == 0) return false;
                Current = BitOperations.TrailingZeroCount(_mask);
                _mask &= _mask - 1;
                return true;
            }
        }
    }
}
