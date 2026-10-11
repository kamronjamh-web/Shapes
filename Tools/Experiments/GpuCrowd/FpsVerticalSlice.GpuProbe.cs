using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    // Require an isolated frozen map, but exercise the production spawn path.
    internal int SpawnGpuProbeFort(int count)
    {
        if (ProcessMode != ProcessModeEnum.Disabled || HordeManager == null || !UsesFortifications)
            throw new InvalidOperationException("Extended GPU probe spawning requires a disabled fort scene.");
        if (count < 1 || count > HordeManager.Capacity)
            throw new ArgumentOutOfRangeException(nameof(count));
        DebugSpawnRole = FpsEnemyRole.Bruiser;
        int spawned = DebugSpawnHorde(count, true);
        if (spawned != count || ActiveEnemyCount != count || HordeManager.ActiveCount != count)
            throw new InvalidOperationException($"Extended probe requested {count}, registered {spawned}, active {ActiveEnemyCount}/{HordeManager.ActiveCount}.");
        return spawned;
    }
}
