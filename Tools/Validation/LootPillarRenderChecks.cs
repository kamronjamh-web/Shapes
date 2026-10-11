using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild.Validation;

/// <summary>Rendered atlas tests use the supplied artwork and production GPU shader.</summary>
internal static class LootPillarRenderChecks
{
    public static async Task Run(Node host, Action<bool, string> check)
    {
        SubViewport viewport = new()
        {
            Name = "LootAtlasValidationViewport", Size = new Vector2I(320, 640), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        host.AddChild(viewport);
        Node3D world = new(); viewport.AddChild(world);
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        Camera3D camera = new()
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 5f, Current = true,
            Position = new Vector3(0, 1.8f, 6), Far = 1000,
        };
        world.AddChild(camera); camera.LookAt(new Vector3(0, 1.8f, 0), Vector3.Up);
        FpsLootPillarRenderer renderer = new(); world.AddChild(renderer);
        renderer.SetFlowSpeed(0f);
        renderer.SetPillars(Array.Empty<FpsLootPillarInstance>());
        string directory = ProjectSettings.GlobalizePath("res://.tmp/ground-loot-validation/native");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        async Task<Image> Render(float time, string? name = null, bool includeSparks = false)
        {
            if (!includeSparks) foreach (GpuParticles3D sparks in Descendants(renderer).OfType<GpuParticles3D>()) sparks.Visible = false;
            renderer.SetAnimationTime(time);
            for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = viewport.GetTexture().GetImage(); image.Convert(Image.Format.Rgba8);
            if (name != null) check(image.SavePng(directory + "/" + name) == Error.Ok, "Atlas render saved: " + name);
            return image;
        }
        Image empty = await Render(0);
        renderer.SetPillars(new[] { new FpsLootPillarInstance(Vector3.Zero, Colors.White, 4f, 2f, 0f) });
        check(renderer.AtlasReady && renderer.PillarCount == 1 && renderer.ChunkCount == 1,
            "Native renderer loads the supplied six-frame atlas and batches a pillar");
        Image zero = await Render(0f, "atlas-frame-0.png");
        Image half = await Render(.0625f, "atlas-frame-0-half.png");
        Image one = await Render(.125f, "atlas-frame-1.png");
        Image five = await Render(.625f, "atlas-frame-5.png");
        Image loopHalf = await Render(.6875f, "atlas-frame-5-half.png");
        Image loop = await Render(.75f, "atlas-loop-0.png");
        double energy = Delta(empty, zero), transition = Delta(zero, one);
        check(energy > .001 && transition > .0001, "Supplied atlas produces visible light and distinct rendered animation frames");
        double beforeHalf = Delta(zero, half), afterHalf = Delta(half, one);
        check(beforeHalf > transition * .15 && afterHalf > transition * .15 &&
            beforeHalf < transition * .85 && afterHalf < transition * .85,
            "Rendered fractional animation crossfades between both adjacent atlas frames");
        double loopTransition = Delta(five, zero);
        check(Delta(five, loopHalf) > loopTransition * .15 && Delta(loopHalf, zero) > loopTransition * .15 &&
            Delta(five, loopHalf) < loopTransition * .85 && Delta(loopHalf, zero) < loopTransition * .85 && Delta(zero, loop) < .00001,
            "Rendered sixth frame crossfades into the first and completes a seamless loop");
        camera.Position = new Vector3(6, 1.8f, 0); camera.LookAt(new Vector3(0, 1.8f, 0), Vector3.Up);
        Image side = await Render(0f, "atlas-camera-side.png");
        double sideEnergy = Delta(empty, side);
        check(sideEnergy > energy * .9 && sideEnergy < energy * 1.1 && Delta(zero, side) < .002,
            "Crossed beam planes remain upright and equally visible from a ninety-degree camera angle");
        camera.Position = new Vector3(4.24264f, 1.8f, 4.24264f); camera.LookAt(new Vector3(0, 1.8f, 0), Vector3.Up);
        Image diagonal = await Render(0f, "atlas-camera-diagonal.png");
        check(Delta(empty, diagonal) > energy * .7,
            "Crossed planes retain visible beam thickness from a diagonal camera angle");
        camera.Position = new Vector3(0, 1.8f, 6); camera.LookAt(new Vector3(0, 1.8f, 0), Vector3.Up);
        renderer.SetFlowSpeed(.35f);
        Image flowing = await Render(.75f, "atlas-upward-flow.png");
        check(Delta(zero, flowing) > .0001,
            "Upward energy flow changes rendered brightness while the atlas returns to the same frame");
        renderer.SetFlowSpeed(0f);
        renderer.SetPillars(new[] { new FpsLootPillarInstance(Vector3.Zero, new Color(1f, .04f, .04f), 4f, 2f, 0f, 5) });
        Image red = await Render(0f, "atlas-white-core-red-edges.png");
        int whiteCore = 0, tintedEdges = 0;
        for (int y = 150; y < 480; y++) for (int x = 60; x < 260; x++)
        {
            Color pixel = red.GetPixel(x, y);
            if (pixel.R > .35f && pixel.G > .3f && pixel.B > .3f && Mathf.Max(pixel.R, Mathf.Max(pixel.G, pixel.B)) - Mathf.Min(pixel.R, Mathf.Min(pixel.G, pixel.B)) < .25f) whiteCore++;
            if (pixel.R > .12f && pixel.R > pixel.G * 1.8f && pixel.R > pixel.B * 1.8f) tintedEdges++;
        }
        check(whiteCore > 20 && tintedEdges > 20, "Rendered beam has a bright white core and visible rarity-colored edges");
        renderer.SetPillars(new[] { new FpsLootPillarInstance(Vector3.Zero, new Color(1f, .04f, .04f), .05f, 2f, 0f, 5) });
        camera.Position = new Vector3(0, 6, .01f); camera.LookAt(Vector3.Zero, Vector3.Forward);
        Image glow = await Render(0f, "atlas-ground-glow-overhead.png");
        int glowPixels = 0;
        for (int y = 235; y < 405; y++) for (int x = 75; x < 245; x++)
        {
            Color pixel = glow.GetPixel(x, y);
            if (pixel.R > .08f && pixel.R > pixel.G * 1.3f) glowPixels++;
        }
        check(glowPixels > 200, "Horizontal rarity glow remains visible from overhead when beam height is negligible");
        ValidateSparkAllocation(renderer, check);
        renderer.SetPillars(new[] { new FpsLootPillarInstance(Vector3.Zero, new Color(1f, .8f, .2f), 4f, 2f, 0f, 5) });
        foreach (MultiMeshInstance3D beams in Descendants(renderer).OfType<MultiMeshInstance3D>()) beams.Visible = false;
        foreach (GpuParticles3D sparks in Descendants(renderer).OfType<GpuParticles3D>().Where(node => node.Emitting))
        { sparks.Visible = true; sparks.Preprocess = .8; sparks.Restart(); }
        camera.Size = 2f; camera.Position = new Vector3(0, .8f, 3); camera.LookAt(new Vector3(0, .8f, 0), Vector3.Up);
        for (int frame = 0; frame < 30; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        Image sparkImage = await Render(0f, "atlas-gpu-sparks.png", true);
        check(Delta(empty, sparkImage) > .00001, "GPU sparks render independently while the beam mesh is hidden");
        check(RegionDelta(empty, zero, new Rect2I(40, 210, 8, 60)) < .001,
            "Atlas black background contributes no opaque billboard rectangle");
        renderer.SetPillars(Array.Empty<FpsLootPillarInstance>());
        Image removed = await Render(0f);
        check(renderer.PillarCount == 0 && renderer.ChunkCount == 0 && Delta(empty, removed) < .00001,
            "Removing the authoritative pillar removes its rendered light exactly");
        viewport.QueueFree();
    }

    private static void ValidateSparkAllocation(FpsLootPillarRenderer renderer, Action<bool, string> check)
    {
        int previousAmount = 0, maximumAmount = 0; float previousScale = 0f;
        foreach (string rarity in new[] { "common", "uncommon", "rare", "legendary", "mythic", "divine" })
        {
            EquipmentItem item = new() { Rarity = EquipmentCatalogDefaults.CreateRarityByKey(rarity) };
            renderer.SetPillars(new[] { FpsGroundItemVisual.GetPillarInstance(item, null, Vector3.Zero, 0f) });
            GpuParticles3D emitter = Descendants(renderer).OfType<GpuParticles3D>().Single(node => node.Emitting);
            ParticleProcessMaterial process = (ParticleProcessMaterial)emitter.ProcessMaterial;
            check(renderer.ActiveSparkEmitterCount == 1 && emitter.Amount > previousAmount && process.ScaleMax > previousScale,
                $"{rarity} factory rank increases actual GPU spark amount and size");
            previousAmount = emitter.Amount; previousScale = process.ScaleMax; maximumAmount = Math.Max(maximumAmount, emitter.Amount);
        }
        FpsLootPillarInstance[] many = Enumerable.Range(0, 50)
            .Select(index => new FpsLootPillarInstance(new Vector3(index * .15f, 0, 0), Colors.White, 4f, 2f, index * .01f, 5)).ToArray();
        renderer.SetPillars(many);
        GpuParticles3D[] active = Descendants(renderer).OfType<GpuParticles3D>().Where(node => node.Emitting).ToArray();
        check(renderer.PillarCount == 50 && renderer.ActiveSparkEmitterCount == FpsLootPillarRenderer.SparkEmitterCapacity &&
            active.Length <= FpsLootPillarRenderer.SparkEmitterCapacity && active.Sum(node => node.Amount) <= FpsLootPillarRenderer.SparkEmitterCapacity * maximumAmount,
            "Fifty nearby pillars retain all beams while GPU spark emitters and total particles stay bounded");
        renderer.SetPillars(Array.Empty<FpsLootPillarInstance>());
        check(renderer.ActiveSparkEmitterCount == 0 && Descendants(renderer).OfType<GpuParticles3D>().All(node => !node.Emitting && !node.Visible),
            "Removing ground pillars stops and hides every pooled GPU spark emitter");
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren()) foreach (Node descendant in Descendants(child)) yield return descendant;
    }

    internal static double Delta(Image first, Image second)
        => RegionDelta(first, second, new Rect2I(0, 0, first.GetWidth(), first.GetHeight()));

    private static double RegionDelta(Image first, Image second, Rect2I region)
    {
        double total = 0;
        for (int y = region.Position.Y; y < region.End.Y; y++)
            for (int x = region.Position.X; x < region.End.X; x++)
            {
                Color a = first.GetPixel(x, y), b = second.GetPixel(x, y);
                total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            }
        return total / (region.Size.X * region.Size.Y * 3d);
    }
}
