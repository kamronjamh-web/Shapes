using System;
using Godot;

namespace SuperGatchaRebuild.Fps;

public partial class FpsHordeManager
{
    internal double CompareGpuProbeDrive(object expected, float tolerance = .0001f)
    {
        Enemy[] reference = (Enemy[])expected;
        double maximum = 0;
        foreach (int slot in EnumerateAliveSlots())
        {
            ref Enemy actual = ref _enemies[slot];
            ref Enemy other = ref reference[slot];
            if (!other.Alive || actual.Generation != other.Generation || actual.TargetsPlayer != other.TargetsPlayer)
                throw new InvalidOperationException($"GPU drive flags/generation mismatch at {slot}.");
            float error = Math.Max(actual.Target.DistanceTo(other.Target), actual.DesiredVelocity.DistanceTo(other.DesiredVelocity));
            error = Math.Max(error, actual.PreviousPosition.DistanceTo(other.PreviousPosition));
            foreach (float scalar in new[] { actual.AttackTimer - other.AttackTimer, actual.DecisionTimer - other.DecisionTimer,
                Mathf.AngleDifference(actual.Yaw, other.Yaw), actual.RollDistance - other.RollDistance,
                actual.RollPause - other.RollPause, actual.RollNextPause - other.RollNextPause, actual.RollUnusedSeconds - other.RollUnusedSeconds })
                error = Math.Max(error, Math.Abs(scalar));
            error = Math.Max(error, QuaternionError(actual.RollOrientation, other.RollOrientation));
            error = Math.Max(error, QuaternionError(actual.PreviousRollOrientation, other.PreviousRollOrientation));
            if (!float.IsFinite(error) || error > tolerance)
                throw new InvalidOperationException($"GPU drive mismatch slot {slot}: error={error}; yaw={actual.Yaw}/{other.Yaw}, velocity={actual.DesiredVelocity}/{other.DesiredVelocity}, gait={actual.RollDistance}/{other.RollDistance} pause={actual.RollPause}/{other.RollPause}, target={actual.Target}/{other.Target}.");
            maximum = Math.Max(maximum, error);
        }
        return maximum;
    }

    private static float QuaternionError(Quaternion a, Quaternion b)
    {
        Vector4 av = new(a.X, a.Y, a.Z, a.W), bv = new(b.X, b.Y, b.Z, b.W);
        return Mathf.Min((av - bv).Length(), (av + bv).Length());
    }
}
