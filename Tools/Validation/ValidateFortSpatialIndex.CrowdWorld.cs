using System;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateFortSpatialIndex
{
    private void ValidateCombinedWorldResolver(string label, int randomCount)
    {
        void CompareResolved(Vector3 from, Vector3 desired, float radius, float height, string name)
        {
            _map.FortSpatialQueriesEnabled = false;
            Vector3 expected = _map.ResolveHordeBodyWorldReference(from, desired, radius, height);
            _map.FortSpatialQueriesEnabled = true;
            Vector3 actual = _map.ResolveHordeBodyWorld(from, desired, radius, height);
            if (!actual.IsFinite() || actual.DistanceTo(expected) > Tolerance)
                throw new InvalidOperationException($"{label}/{name}: combined {actual}, original {expected}; from={from}, desired={desired}, r={radius}, h={height}");
            _checks++; _queries++;
        }

        for (int i = 0; i < randomCount; i++)
        {
            Vector3 from = new(Range(-180f, 180f), Range(-2f, 45f), Range(-180f, 180f));
            Vector3 desired = from + new Vector3(Range(-35f, 35f), Range(-15f, 15f), Range(-35f, 35f));
            CompareResolved(from, desired, Range(.1f, 2f), Range(.2f, 12f), "random sweep");
        }
        FpsHillMapSettings settings = _map.HillMapSettings!;
        float ground = settings.SummitHeight, wall = settings.RoomWallHeight;
        foreach (FpsDefenseGate gate in _map.DefenseGates)
        {
            Vector3 outward = Outward(gate.SideIndex), across = new(-outward.Z, 0f, outward.X);
            Vector3 center = gate.GlobalPosition;
            foreach (float height in new[] { .2f, 1.2f, 8f, 16f })
            foreach (float along in new[] { -3f, -.1f, 0f, .1f, 3f })
            {
                Vector3 from = center + outward * along;
                CompareResolved(from, center - outward * 4f, .55f, height, "live gate / restore face / low passage");
                CompareResolved(from + Vector3.Up * (wall + .2f), center - outward * 4f, .55f, height, "fall onto gate region");
                CompareResolved(center + across * 20f + Vector3.Up * (wall + 2f),
                    center + across * 20f + Vector3.Up * (wall - 2f), .55f, height, "fall onto fort slab");
            }
            bool invulnerable = gate.DebugInvulnerable;
            gate.DebugInvulnerable = false;
            gate.ApplyEnemyDamage(gate.MaxHealth + 1f);
            CompareResolved(center + outward * 3f, center - outward * 4f, .55f, 1.2f, "breached gate");
            gate.ResetHealth();
            CompareResolved(center, center - outward * 4f, .55f, 1.2f, "restored around body");
            gate.DebugInvulnerable = invulnerable;
        }

        // Live derived values can change before a geometry rebuild. Both paths
        // must observe them immediately while retaining existing fort records.
        float savedHeight = settings.SummitHeight, savedFoot = settings.HillFootHalfSize;
        float savedThickness = settings.FortWallThickness, savedDoor = settings.DoorWidth;
        bool savedFort = settings.FortificationsEnabled;
        try
        {
            settings.SummitHeight = savedHeight + 2f;
            settings.HillFootHalfSize = savedFoot - 3f;
            settings.FortWallThickness = savedThickness + .25f;
            settings.DoorWidth = savedDoor + .5f;
            for (int i = 0; i < 500; i++)
            {
                Vector3 from = new(Range(-140f, 140f), Range(ground - 1f, ground + wall + 3f), Range(-140f, 140f));
                CompareResolved(from, from + new Vector3(Range(-8f, 8f), Range(-3f, 3f), Range(-8f, 8f)),
                    .55f, Range(.2f, 10f), "live settings without rebuild");
            }
            settings.FortificationsEnabled = false;
            for (int i = 0; i < 200; i++)
            {
                Vector3 from = new(Range(-130f, 130f), Range(0f, 40f), Range(-130f, 130f));
                CompareResolved(from, from + new Vector3(Range(-10f, 10f), -1f, Range(-10f, 10f)), .55f, 1.2f, "legacy fallback");
            }
        }
        finally
        {
            settings.SummitHeight = savedHeight;
            settings.HillFootHalfSize = savedFoot;
            settings.FortWallThickness = savedThickness;
            settings.DoorWidth = savedDoor;
            settings.FortificationsEnabled = savedFort;
            _map.FortSpatialQueriesEnabled = true;
        }
        Check(_map.HordeManager!.BodyWorldResolver != null, "Crowd uses the combined world resolver");
        GD.Print($"Combined world resolver {label}: random, live/restored gates, slabs, low passages, live retuning and legacy fallback match original callbacks.");
    }
}
