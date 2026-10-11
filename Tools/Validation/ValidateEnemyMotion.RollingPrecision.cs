using System;
using System.Reflection;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

internal static class RollingPrecisionFixture
{
    internal static void Stage(FpsHordeManager crowd, FpsHordeHandle handle, float distance, float? speed = null)
    {
        Array state = (Array)crowd.SaveGpuProbeState();
        object body = state.GetValue(handle.Slot)!;
        Type type = body.GetType();
        type.GetField("RollDistance")!.SetValue(body, distance);
        type.GetField("RollPause")!.SetValue(body, 0f);
        type.GetField("RollNextPause")!.SetValue(body, 0f);
        type.GetField("RollUnusedSeconds")!.SetValue(body, 0f);
        if (speed.HasValue) type.GetField("Speed")!.SetValue(body, speed.Value);
        type.GetField("PreviousPosition")!.SetValue(body, type.GetField("Position")!.GetValue(body));
        state.SetValue(body, handle.Slot);
        crowd.RestoreGpuProbeState(state);
    }

    internal static float Tolerance(Vector3 position)
    {
        float magnitude = Math.Max(Math.Abs(position.X), Math.Abs(position.Z));
        return Math.Max(.00001f, 2f * (MathF.BitIncrement(magnitude) - magnitude));
    }
}

public partial class ValidateEnemyMotion
{
    private void ValidateRollingPrecision()
    {
        FpsHordeManager crowd = new() { ProcessMode = ProcessModeEnum.Disabled,
            Settings = new FpsHordeSettings { Capacity = 100, MapExtent = 2048, GpuCrowdContactsEnabled = false,
                SoftCollisionEnabled = false, RollingEnabled = true, RollingPauseSeconds = 0, RollingDurationSeconds = 0,
                SeparationStrength = 0, SimulationHz = 30 }, CorePosition = () => Vector3.Zero, SurfaceHeight = _ => 0 };
        AddChild(crowd);
        FpsEnemyArchetypeSettings archetype = new() { CollisionRadius = .55f, MoveSpeed = 3.5f };
        try
        {
            const float width = 1.1f;
            FpsHordeHandle handle = crowd.Spawn(archetype, FpsEnemyRole.Bruiser, new Vector3(600, 0, 0));
            MethodInfo update = typeof(FpsHordeManager).GetMethod("UpdateRollingMotion", Private)!;
            foreach (float coordinate in new[] { 0f, MathF.BitDecrement(512f), 512f, 600f, 800f })
            {
                Vector3 position = new(coordinate, 0, 0);
                crowd.TrySetPosition(handle, position);
                float epsilon = RollingPrecisionFixture.Tolerance(position);
                RollingPrecisionFixture.Stage(crowd, handle, width - epsilon * .5f);
                update.Invoke(crowd, null);
                crowd.TryGetRollingState(handle, out float distance, out _, out _);
                Check(distance == 0, "Stationary terminal roll residual recovers at origin/float-coordinate boundaries " + coordinate);
                RollingPrecisionFixture.Stage(crowd, handle, width - epsilon * 2f);
                update.Invoke(crowd, null);
                crowd.TryGetRollingState(handle, out distance, out _, out _);
                Check(distance > width * .9f, "Residual beyond coordinate tolerance does not finish an incomplete roll " + coordinate);
            }
            RollingPrecisionFixture.Stage(crowd, handle, width * .5f);
            update.Invoke(crowd, null);
            crowd.TryGetRollingState(handle, out float blockedDistance, out _, out _);
            Check(blockedDistance == width * .5f, "A genuinely blocked mid-roll retains its unfinished gait");
            foreach (Vector3 origin in new[] { new Vector3(600, 0, 0), new Vector3(800, 0, 0),
                new Vector3(600, 0, 600), new Vector3(800, 0, 600) })
            {
                crowd.TrySetPosition(handle, origin);
                RollingPrecisionFixture.Stage(crowd, handle, 0);
                int cycles = 0; float previous = 0;
                for (int tick = 0; tick < 1600 && cycles < 100; tick++)
                {
                    crowd.TickSimulation(1f / 30f);
                    crowd.TryGetRollingState(handle, out float distance, out _, out _);
                    if (distance < previous) cycles++;
                    previous = distance;
                }
                crowd.TryGet(handle, out var body);
                Check(cycles >= 100 && body.Position.DistanceTo(origin) > 100,
                    "Large-coordinate cardinal/diagonal cube completes 100 natural roll cycles " + origin);
            }
        }
        finally { crowd.QueueFree(); }
    }
}
