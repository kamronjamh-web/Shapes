using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    // Fixed work budget for defense acquisition; rotating cells prevent a dense front
    // from monopolizing every refresh. Regular weapon queries retain full coverage.
    public void QueryRadiusBounded(Vector3 center, float radius, List<FpsHordeEnemySnapshot> results,
        ref int cursor, int visitBudget = 512, int resultBudget = 128)
    {
        results.Clear();
        EnsureGrid();
        LastQueryCandidateCount = 0;
        float margin = Mathf.Max(0f, radius) + _maxRadius;
        GetCellBounds(center.X - margin, center.Z - margin, center.X + margin, center.Z + margin,
            out int minX, out int minZ, out int maxX, out int maxZ);
        int width = maxX - minX + 1, cells = width * (maxZ - minZ + 1);
        cursor = Mathf.PosMod(cursor, cells);
        for (int step = 0; step < Mathf.Min(cells, 2048); step++)
        {
            int cell = cursor;
            cursor = (cursor + 1) % cells;
            for (int i = _gridHeads[(minZ + cell / width) * _gridWidth + minX + cell % width]; i >= 0; i = _gridNext[i])
            {
                if (++LastQueryCandidateCount > visitBudget) return;
                ref Enemy enemy = ref _enemies[i];
                if (!enemy.Alive) continue;
                Vector3 offset = enemy.Position - center; offset.Y = 0f;
                if (offset.LengthSquared() <= Mathf.Pow(radius + enemy.Radius, 2f)) results.Add(Snapshot(i));
                if (results.Count >= resultBudget) return;
            }
        }
    }
}
