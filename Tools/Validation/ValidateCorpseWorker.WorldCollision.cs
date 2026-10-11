using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps.Physics;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateCorpseWorker
{
    private async Task ValidateFortContacts(CorpseWorldSnapshot closed, CorpseWorldSnapshot breached,
        CorpseWorldSnapshot restored, Vector3 gateCenter)
    {
        Check(Box3dCorpseWorker.TryCreate(Vector3.Zero, 1, out var created, out string reason), "Actual gate-contact worker starts: " + reason);
        using (Box3dCorpseWorker worker = created!)
        {
            CorpsePose[] poses = new CorpsePose[1];
            Vector3 outward = gateCenter - _map!.GlobalPosition; outward.Y = 0; outward = outward.Normalized();
            CorpseSpawn spawn = new(false, gateCenter + outward * 5f, Quaternion.Identity, Vector3.One * .25f,
                -outward * 20f, Vector3.Zero, 0, 0);
            foreach (var phase in new[] { (World: closed, Generation: 1u, Blocked: true),
                (World: breached, Generation: 2u, Blocked: false), (World: restored, Generation: 3u, Blocked: true) })
            {
                worker.ReplaceWorld(phase.World);
                await Until(() => worker.TrySpawn(0, phase.Generation, spawn), "Gate probe spawn accepted", record: false);
                await Until(() => worker.TryCopyCompleted(poses, out int count, out var info) && count == 1
                    && info.GeometryVersion == phase.World.Version && poses[0].Generation == phase.Generation,
                    "Gate probe world and generation installed", record: false);
                await AdvanceTicks(worker, poses, 30);
                float distance = (poses[0].Position - gateCenter).Dot(outward);
                Check(phase.Blocked ? distance > .2f : distance < -1f,
                    phase.Blocked ? "Closed/restored actual fort gate blocks a native cube at 20 m/s"
                        : "Breached actual fort gate lets the same native probe enter the doorway");
            }
        }
        Check(Box3dCorpseWorker.TryCreate(Vector3.Down * 9.8f, 4, out created, out reason), "Actual floor-contact worker starts: " + reason);
        using (Box3dCorpseWorker worker = created!)
        {
            worker.ReplaceWorld(restored);
            var hill = _map!.HillMapSettings!;
            float outside = (hill.HillFootHalfSize + hill.PlayableSize * .5f) * .5f;
            Vector3 terrain = new(outside, 0, 0), summit = new(hill.SummitHalfSize * .85f, hill.SummitHeight, 0);
            float terrainHeight = _map.SampleHillSurfaceHeight(terrain), summitHeight = _map.SampleHillSurfaceHeight(summit);
            float slopeX = (hill.SummitHalfSize + hill.HillFootHalfSize) * .5f;
            Vector3 slope = new(slopeX, 0, slopeX * .73f);
            float slopeHeight = _map.SampleHillSurfaceHeight(slope);
            Check(slopeHeight > 0 && slopeHeight < hill.SummitHeight,
                "Inclined terrain probe starts between the summit and hill foot");
            Vector3[] centers = { new(terrain.X, terrainHeight + 2f, terrain.Z), new(summit.X, summitHeight + 2f, summit.Z),
                new(slope.X, slopeHeight + 1f, slope.Z) };
            for (int slot = 0; slot < centers.Length; slot++)
            {
                int captured = slot;
                CorpseSpawn spawn = new(false, centers[slot], Quaternion.Identity, Vector3.One * .5f, Vector3.Zero, Vector3.Zero);
                await Until(() => worker.TrySpawn(captured, 1, spawn), "Actual floor probe spawn accepted", record: false);
            }
            CorpseStaticShape? ramp = restored.Shapes.FirstOrDefault(s => s.Kind == CorpseStaticShapeKind.ConvexHull);
            Vector3 rampCenter = default;
            if (ramp != null)
            {
                Vector3 min = ramp.Vertices[0], max = min;
                foreach (Vector3 vertex in ramp.Vertices) { min = min.Min(vertex); max = max.Max(vertex); }
                rampCenter = (min + max) * .5f;
                rampCenter.Y = HullTop(ramp.Vertices, rampCenter) + 1f;
                CorpseSpawn spawn = new(false, rampCenter, Quaternion.Identity, Vector3.One * .5f, Vector3.Zero, Vector3.Zero);
                await Until(() => worker.TrySpawn(3, 1, spawn), "Actual convex ramp probe spawn accepted", record: false);
            }
            CorpsePose[] poses = new CorpsePose[4];
            await Until(() => worker.TryCopyCompleted(poses, out int count, out var info)
                && count == (ramp == null ? 3 : 4) && info.GeometryVersion == restored.Version,
                "Actual terrain/summit/ramp bodies are published", record: false);
            await AdvanceTicks(worker, poses, 45);
            CorpsePose slopePose = poses.First(p => p.Slot == 2);
            float currentSlopeHeight = _map.SampleHillSurfaceHeight(slopePose.Position);
            Check(currentSlopeHeight > 0 && currentSlopeHeight < hill.SummitHeight
                && slopePose.Position.Y >= currentSlopeHeight + .1f && slopePose.Position.Y <= currentSlopeHeight + .8f,
                "Native falling cube contacts inclined terrain triangles at its current XZ without falling through");
            if (ramp != null)
            {
                CorpsePose pose = poses.First(p => p.Slot == 3);
                float support = HullTop(ramp.Vertices, pose.Position);
                Check(float.IsFinite(support) && pose.Position.Y >= support + .1f && pose.Position.Y <= support + 1f,
                    "Native falling cube contacts the actual convex fort ramp above its independent support plane");
            }
            await AdvanceTicks(worker, poses, 45);
            CorpsePose ground = poses.First(p => p.Slot == 0), top = poses.First(p => p.Slot == 1);
            Check(ground.Position.Y >= terrainHeight + .15f && ground.Position.Y < terrainHeight + .75f,
                "Native falling cube rests on the flat ground beyond the hill foot");
            Check(top.Position.Y >= summitHeight + .15f && top.Position.Y < summitHeight + .75f,
                "Native falling cube rests on the actual summit collision surface");
        }
    }

    private async Task AdvanceTicks(Box3dCorpseWorker worker, CorpsePose[] poses, int ticks)
    {
        long sequence = 0;
        if (worker.TryCopyCompleted(poses, out _, out var current)) sequence = current.Sequence;
        for (int i = 0; i < ticks; i++)
        {
            worker.Advance(Dt, true);
            await Until(() => worker.TryCopyCompleted(poses, out _, out var info) && info.Sequence > sequence,
                "Native collision probe fixed step", record: false);
            worker.TryCopyCompleted(poses, out _, out current); sequence = current.Sequence;
        }
    }

    // Independent convex face bound, evaluated at the probe's current XZ.
    // Ramp snapshots already contain copied world-space vertices.
    private static float HullTop(Vector3[] vertices, Vector3 point)
    {
        float top = float.PositiveInfinity;
        for (int a = 0; a < vertices.Length; a++) for (int b = a + 1; b < vertices.Length; b++)
            for (int c = b + 1; c < vertices.Length; c++)
            {
                Vector3 normal = (vertices[b] - vertices[a]).Cross(vertices[c] - vertices[a]);
                if (normal.LengthSquared() < .000001f) continue;
                normal = normal.Normalized();
                bool below = vertices.All(v => normal.Dot(v - vertices[a]) <= .0001f);
                if (!below) { normal = -normal; below = vertices.All(v => normal.Dot(v - vertices[a]) <= .0001f); }
                if (!below || normal.Y <= .00001f) continue;
                float y = vertices[a].Y - (normal.X * (point.X - vertices[a].X) + normal.Z * (point.Z - vertices[a].Z)) / normal.Y;
                top = Math.Min(top, y);
            }
        return top;
    }
}
