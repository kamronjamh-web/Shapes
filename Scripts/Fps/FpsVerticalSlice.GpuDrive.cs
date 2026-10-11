using System;
using Godot;
using SuperGatchaRebuild.Fps.Gpu;

namespace SuperGatchaRebuild.Fps;

public partial class FpsVerticalSlice
{
    internal bool HasGpuCrowdDriveCallbacks(FpsHordeManager crowd)
        => crowd.ResolveObjectiveTarget == (Func<Vector3, Vector3>)ResolveHordeObjective
            && crowd.ResolveMovementTarget == (Func<Vector3, Vector3, Vector3>)ResolveHordeMovementTarget;

    internal GpuCrowdDriveGlobals CaptureGpuCrowdDriveGlobals(FpsHordeSettings settings,
        Vector3 core, Vector3 player, bool playerAlive, float managerExtent)
        => new()
        {
            Core = new Vector4(core.X, core.Y, core.Z, 0f),
            Player = new Vector4(player.X, player.Y, player.Z, playerAlive ? 1f : 0f),
            Movement = new Vector4(Mathf.Clamp(settings.DecisionHz, 1f, 30f), settings.PlayerAggroRadius,
                settings.CoreContactRadius, Mathf.Clamp(settings.CrowdPushStrength, .1f, 3f)),
            Rolling = new Vector4(Mathf.Clamp(settings.RollingPauseSeconds, 0f, .3f),
                Mathf.Clamp(settings.RollingDurationSeconds, 0f, 1f), Mathf.Clamp(settings.Gravity, 1f, 40f), settings.RollingEnabled ? 1f : 0f),
            Fort = new Vector4(FortInteriorHalf, HillRoomHalf, HillMapSettings!.RoomWallHeight, HillHeight),
            Bounds = new Vector4(managerExtent * .5f, 0f, 0f, 0f),
        };
}
