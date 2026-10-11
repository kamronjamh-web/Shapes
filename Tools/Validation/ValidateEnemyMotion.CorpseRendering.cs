using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateEnemyMotion
{
    private async Task ValidateCorpseRendering()
    {
        FpsEnemyCorpsePool fixture = new()
        {
            Settings = new FpsHordeSettings { CorpseCapacity = 3, CorpseLifetimeSeconds = 15f, CorpseEnabled = true },
            Position = new Vector3(100f, 40f, 100f), Rotation = new Vector3(0f, .37f, 0f),
        };
        AddChild(fixture);
        fixture.Tick(0f, false);
        Dictionary<int, FpsHordeDeath> deaths = new();
        void Spawn(int identity, FpsEnemyRole role, Vector3 size, Color color)
        {
            Vector3 center = new(101f + identity * .1f, 45f, 100f);
            // Duplicate cubes use exactly the same scaled basis, so the shape
            // cache assertion does not silently quantize distinct float sizes.
            Basis orientation = new(new Quaternion(new Vector3(1f, 2f, 3f).Normalized(), .2f * (identity == 3 ? 1 : identity)));
            FpsHordeEnemySnapshot enemy = new(new(identity, 1), center, center, size.X * .5f, 0f, 40f, role, size.Y);
            FpsHordeDeath death = new(enemy, color, new Transform3D(orientation * Basis.FromScale(size), center), Vector3.Back, 50f, 10f);
            deaths.Add(identity, death); fixture.SpawnCorpse(death);
        }
        Spawn(1, FpsEnemyRole.Bruiser, new Vector3(1.1f, 1.2f, 1.1f), new Color(.3f, .55f, .85f));
        Spawn(2, FpsEnemyRole.Striker, new Vector3(.9f, 1f, .9f), new Color(.85f, .3f, .2f));
        Spawn(3, FpsEnemyRole.Bruiser, new Vector3(1.1f, 1.2f, 1.1f), new Color(.2f, .8f, .35f));
        fixture._Process(0d);
        ValidateCorpseInstances(fixture, deaths);
        Check(Descendants(fixture).OfType<MeshInstance3D>().Count() == 0,
            "Corpses publish through two batched meshes without per-body visual nodes");
        MultiMeshInstance3D[] renderers = Descendants(fixture).OfType<MultiMeshInstance3D>().ToArray();
        Check(renderers.Length == 2 && renderers[0].MaterialOverride.GetInstanceId() == renderers[1].MaterialOverride.GetInstanceId(),
            "Cube and pyramid corpses share one lit emission material");
        var bodies = Descendants(fixture).OfType<RigidBody3D>().OrderBy(b => b.Name.ToString()).ToArray();
        Shape3D firstShape = bodies[0].GetChild<CollisionShape3D>(0).Shape;
        Shape3D thirdShape = bodies[2].GetChild<CollisionShape3D>(0).Shape;
        Check(firstShape.GetInstanceId() == thirdShape.GetInstanceId(), "Identical corpse geometry shares an immutable collision shape");
        long uploads = fixture.CorpseRenderUploadCount;
        fixture._Process(0d); fixture.Tick(.5f, false); fixture._Process(0d);
        Check(fixture.CorpseRenderUploadCount == uploads, "Paused unchanged corpses reuse their published buffers");
        fixture.Tick(0f, true);
        foreach (RigidBody3D body in bodies)
        {
            body.GravityScale = 0f; body.LinearVelocity = Vector3.Zero; body.AngularVelocity = Vector3.Zero;
            body.Sleeping = true;
        }
        // Native unfreeze/transform commands may wake a newly configured body
        // during the first server synchronization. Observe actual settled sleep.
        await Frames(2);
        for (int i = 0; i < 90 && bodies.Any(b => !b.Sleeping); i++) await Frames(1);
        Check(bodies.All(b => b.Sleeping), "Zero-gravity stopped fallback bodies reach native sleep");
        fixture._Process(0d);
        long poseReads = fixture.CorpsePoseReadCount;
        fixture._Process(0d);
        Check(fixture.CorpsePoseReadCount == poseReads && bodies.All(b => !b.Freeze),
            $"Sleeping corpses avoid repeated transform reads while remaining physics-owned (reads {poseReads}->{fixture.CorpsePoseReadCount}, sleeping {bodies.Count(b => b.Sleeping)}/{bodies.Length}, frozen {bodies.Count(b => b.Freeze)})");
        bodies[0].Sleeping = false;
        Transform3D woke = bodies[0].GlobalTransform; woke.Origin += Vector3.Up;
        bodies[0].GlobalTransform = woke;
        fixture._Process(0d);
        Check(fixture.CorpsePoseReadCount > poseReads && fixture.CorpseRenderUploadCount > uploads,
            "A woken moved corpse refreshes its batched pose");
        fixture.Tick(0f, false);

        Spawn(4, FpsEnemyRole.Striker, new Vector3(1.6f, .8f, 1.6f), new Color(.9f, .7f, .15f));
        List<FpsEnemyCorpseSnapshot> snapshots = new(); fixture.CopyActiveSnapshots(snapshots);
        Check(snapshots.Count == 3 && snapshots.Single(c => c.Slot == 0).EnemyHandle.Slot == 4,
            "Full corpse pool recycles the exact oldest spawn slot");
        Check(((BoxShape3D)thirdShape).Size.IsEqualApprox(new Vector3(1.1f, 1.2f, 1.1f)),
            "Recycling a cube into a retuned pyramid leaves another corpse's shared shape unchanged");
        fixture._Process(0d); ValidateCorpseInstances(fixture, deaths);
        Check(fixture.CorpseRenderUploadCount > uploads, "Recycling republishes role, scale and color buffers");
        fixture.Settings.CorpseCapacity = 2;
        fixture._Process(0d);
        Check(fixture.ActiveCount == 2 && renderers.Sum(r => r.Multimesh.VisibleInstanceCount) == 2,
            "Render-before-tick capacity shrink retires removed slots and updates batched visibility");
        fixture.Tick(0f, false);
        fixture.Clear();
        Check(renderers.All(r => r.Multimesh.VisibleInstanceCount == 0), "Clear hides both batches immediately");
        fixture.Settings.CorpseCapacity = 3;
        Spawn(5, FpsEnemyRole.Bruiser, new Vector3(.7f, 1.3f, .7f), new Color(.65f, .2f, .75f));
        fixture.CopyActiveSnapshots(snapshots);
        Check(snapshots.Single().Slot == 0, "Cleared free-slot bitmap preserves lowest available slot reuse");
        fixture._Process(0d); ValidateCorpseInstances(fixture, deaths);
        fixture.Settings.CorpseEnabled = false; fixture.Tick(0f, true); fixture._Process(0d);
        Check(fixture.ActiveCount == 0 && renderers.All(r => r.Multimesh.VisibleInstanceCount == 0),
            "Disabling corpse tuning clears physics and both visual batches");
        fixture.QueueFree();
        await Frames(2);
    }

    private void ValidateCorpseInstances(FpsEnemyCorpsePool pool, Dictionary<int, FpsHordeDeath> deaths)
    {
        List<FpsEnemyCorpseSnapshot> snapshots = new(); pool.CopyActiveSnapshots(snapshots);
        MultiMesh cubes = pool.GetNode<MultiMeshInstance3D>("CorpseCubes").Multimesh;
        MultiMesh pyramids = pool.GetNode<MultiMeshInstance3D>("CorpsePyramids").Multimesh;
        bool headless = DisplayServer.GetName() == "headless";
        float[] cubeBuffer = (float[])typeof(FpsEnemyCorpsePool).GetField("_corpseCubeBuffer", Private)!.GetValue(pool)!;
        float[] pyramidBuffer = (float[])typeof(FpsEnemyCorpsePool).GetField("_corpsePyramidBuffer", Private)!.GetValue(pool)!;
        int cube = 0, pyramid = 0;
        foreach (FpsEnemyCorpseSnapshot corpse in snapshots)
        {
            FpsHordeDeath death = deaths[corpse.EnemyHandle.Slot];
            MultiMesh mesh = corpse.Role == FpsEnemyRole.Striker ? pyramids : cubes;
            int instance = corpse.Role == FpsEnemyRole.Striker ? pyramid++ : cube++;
            Transform3D expected = corpse.WorldTransform;
            expected.Basis *= Basis.FromScale(death.VisualTransform.Basis.Scale.Abs());
            float[] buffer = corpse.Role == FpsEnemyRole.Striker ? pyramidBuffer : cubeBuffer;
            int offset = instance * 16;
            // The headless dummy renderer does not retain native buffer data.
            // Still check the exact publication payload against independent math.
            Transform3D packed = new(new Basis(new Vector3(buffer[offset], buffer[offset + 4], buffer[offset + 8]),
                new Vector3(buffer[offset + 1], buffer[offset + 5], buffer[offset + 9]),
                new Vector3(buffer[offset + 2], buffer[offset + 6], buffer[offset + 10])),
                new Vector3(buffer[offset + 3], buffer[offset + 7], buffer[offset + 11]));
            Transform3D actual = pool.GlobalTransform * (headless ? packed : mesh.GetInstanceTransform(instance));
            Check(actual.IsEqualApprox(expected), "Batched corpse transform preserves independent world pose, rotation and size");
            Color actualColor = headless ? new Color(buffer[offset + 12], buffer[offset + 13], buffer[offset + 14], buffer[offset + 15]) : mesh.GetInstanceColor(instance);
            Check(actualColor.IsEqualApprox(death.BodyColor.SrgbToLinear()),
                "Batched corpse color preserves the original albedo and emission input");
        }
        Check(cubes.VisibleInstanceCount == cube && pyramids.VisibleInstanceCount == pyramid,
            "Batched role counts contain every active corpse and no retired tail entries");
        if (headless) GD.Print("LIMIT: headless checks the managed corpse publication payload; actual native MultiMesh readback requires a graphics renderer.");
    }
}
