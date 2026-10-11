using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Fps;

namespace SuperGatchaRebuild.Validation;

public partial class ValidateHordeRenderBuffers
{
    // Actual draw-consumer regression, independent of raw MultiMesh readback.
    // Isolated world/layer, fixed camera and black background keep postprocessing
    // and fort geometry out of the pixel comparison; production crowd materials remain.
    private async Task ValidateRenderedGpuImages()
    {
        if (_headless)
        {
            GD.Print("LIMIT: headless skips actual CPU/GPU rendered image comparison.");
            return;
        }
        const uint crowdLayer = 1u << 19;
        var viewport = new SubViewport
        {
            OwnWorld3D = true, Size = new Vector2I(480, 320),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
        };
        AddChild(viewport);
        try
        {
            var map = GD.Load<PackedScene>("res://Scenes/Fps/NorthGate.tscn").Instantiate<FpsVerticalSlice>();
            map.ProcessMode = ProcessModeEnum.Disabled;
            map.HordeSettings = new FpsHordeSettings
                { Capacity = 256, RollingEnabled = true, GpuCrowdRenderingEnabled = false };
            viewport.AddChild(map);
            HideImageFixtureCanvas(map);
            _crowd = map.HordeManager!;
            _crowd.SetProcess(false);
            _crowd.Clear();
            _crowd.SetCombatEnabled(true);
            _handles.Clear();
            var cube = FpsEnemyArchetypeSettings.CreateBruiserDefaults();
            cube.BodyColor = new Color(.12f, .65f, .95f);
            var pyramid = FpsEnemyArchetypeSettings.CreateStrikerDefaults();
            pyramid.BodyColor = new Color(.95f, .35f, .1f);
            Vector3 center = new(0f, _crowd.SurfaceHeight!(new Vector3(0f, 0f, -160f)), -160f);
            _handles.Add(_crowd.Spawn(cube, FpsEnemyRole.Bruiser, center + new Vector3(-2f, 0, 0)));
            _handles.Add(_crowd.Spawn(pyramid, FpsEnemyRole.Striker, center + new Vector3(2f, 0, 0)));
            Check(_crowd.ActiveCount == 2, "Rendered image fixture contains cube and pyramid");
            foreach (string name in new[] { "CrowdCubes", "CrowdPyramids" })
                _crowd.GetNode<MultiMeshInstance3D>(name).Layers = crowdLayer;
            _cubes = _crowd.GetNode<MultiMeshInstance3D>("CrowdCubes").Multimesh;
            _pyramids = _crowd.GetNode<MultiMeshInstance3D>("CrowdPyramids").Multimesh;
            Array enemies = Enemies();
            for (int i = 0; i < enemies.Length; i++)
            {
                object body = enemies.GetValue(i)!;
                if (!Read<bool>(body, "Alive")) continue;
                Write(body, "PreviousPosition", Read<Vector3>(body, "Position") - Vector3.Right * 3f);
                Write(body, "PreviousRollOrientation", new Quaternion(Vector3.Forward, .35f));
                Write(body, "RollOrientation", new Quaternion(new Vector3(1, 0, 1).Normalized(), .9f));
                Write(body, "Yaw", .43f);
                Write(body, "Flash", i == _handles[0].Slot ? 10f : 0f);
                enemies.SetValue(body, i);
            }
            _crowd.MarkRenderStateDirty();
            var camera = new Camera3D
            {
                CullMask = crowdLayer, Projection = Camera3D.ProjectionType.Orthogonal, Size = 13f,
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = Colors.White, AmbientLightEnergy = 1f,
                },
            };
            viewport.AddChild(camera);
            camera.GlobalPosition = center + new Vector3(0f, 9f, 15f);
            camera.LookAt(center + Vector3.Up, Vector3.Up);
            camera.MakeCurrent();
            string directory = ProjectSettings.GlobalizePath("res://.tmp/horde-render-validation/native/images");
            Directory.CreateDirectory(directory);
            double[] gpuCentroids = new double[2];
            for (int phase = 0; phase < 2; phase++)
            {
                SetAccumulator(phase / (double)_crowd.Settings.SimulationHz);
                using Image cpu = await CaptureCrowdImage(viewport, false);
                using Image gpu = await CaptureCrowdImage(viewport, true);
                Check(cpu.SavePng(Path.Combine(directory, $"pose-{phase}-cpu.png")) == Error.Ok
                    && gpu.SavePng(Path.Combine(directory, $"pose-{phase}-gpu.png")) == Error.Ok,
                    $"Rendered pose {phase}: CPU/GPU evidence images saved");
                gpuCentroids[phase] = CompareCrowdImages(cpu, gpu, phase);
            }
            Check(Math.Abs(gpuCentroids[1] - gpuCentroids[0]) > 15,
                "Actual GPU-drawn foreground moves between previous and current poses");
            GD.Print($"Rendered CPU/GPU evidence: {directory}; production materials, frozen rolled cube / pyramid / flash; fixed orthographic camera.");
        }
        finally
        {
            viewport.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // _ExitTree queues GPU-owned resource release on the render thread.
            // Let that frame finish while the engine/device are still alive.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private static void HideImageFixtureCanvas(Node node)
    {
        if (node is CanvasItem canvas) canvas.Visible = false;
        Godot.Collections.Array<Node> children = node.GetChildren();
        using Godot.Collections.Array childrenOwner = (Godot.Collections.Array)children;
        foreach (Node child in children) HideImageFixtureCanvas(child);
    }

    private async Task<Image> CaptureCrowdImage(SubViewport viewport, bool gpu)
    {
        _crowd.Settings.GpuCrowdRenderingEnabled = gpu;
        // Settle queued allocation / CPU-cache transitions and actual draw work.
        // Manual zero-delta publication keeps input and flash identical.
        for (int i = 0; i < 3; i++)
        {
            _crowd._Process(0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        Check(_crowd.LastCrowdRenderBackend == (gpu ? "GPU" : "CPU"),
            $"Rendered image uses requested {(gpu ? "GPU" : "CPU")} backend: {_crowd.GpuCrowdRenderFallbackReason}");
        return viewport.GetTexture().GetImage();
    }

    private double CompareCrowdImages(Image cpu, Image gpu, int phase)
    {
        Check(cpu.GetSize() == gpu.GetSize(), $"Rendered pose {phase}: matching image dimensions");
        int union = 0, mismatch = 0, cpuPixels = 0, gpuPixels = 0;
        double absoluteError = 0, gpuX = 0;
        for (int y = 0; y < cpu.GetHeight(); y++)
        for (int x = 0; x < cpu.GetWidth(); x++)
        {
            Color a = cpu.GetPixel(x, y), b = gpu.GetPixel(x, y);
            bool aVisible = Math.Max(a.R, Math.Max(a.G, a.B)) > .03f;
            bool bVisible = Math.Max(b.R, Math.Max(b.G, b.B)) > .03f;
            if (aVisible) cpuPixels++;
            if (bVisible) { gpuPixels++; gpuX += x; }
            if (!(aVisible || bVisible)) continue;
            union++;
            if (aVisible != bVisible) mismatch++;
            absoluteError += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3;
        }
        Check(cpuPixels > 200 && gpuPixels > 200,
            $"Rendered pose {phase}: CPU and GPU draw nonempty foreground ({cpuPixels}/{gpuPixels} pixels)");
        double silhouetteError = mismatch / (double)Math.Max(1, union);
        double colorError = absoluteError / Math.Max(1, union);
        Check(silhouetteError <= .05 && colorError <= .025,
            $"Rendered pose {phase}: silhouette/color difference {silhouetteError:P3}/{colorError:F6} within tolerance");
        return gpuX / Math.Max(1, gpuPixels);
    }
}
