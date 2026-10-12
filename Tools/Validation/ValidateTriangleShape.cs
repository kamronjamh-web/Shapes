using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Validation;

/// <summary>Triangle silhouette, closed geometry, and production-material render regressions.</summary>
public partial class ValidateTriangleShape : Node
{
    private int _checks;

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/triangle-shape-validation/"), "Triangle validation uses isolated saves");
            ShapeData shape = EquipmentCatalogDefaults.CreateDefaultShapes().Single(value => value.Key == "triangle");
            Node3D visual = ShapeVisualFactory.CreateVisual(shape, new Color(.9922f, .8471f, .2078f));
            AddChild(visual);
            MeshInstance3D core = visual.GetNode<MeshInstance3D>("TrianglePrism");
            MeshInstance3D outline = visual.GetNode<MeshInstance3D>("TrianglePrismOutline");
            ValidateGeometry(core.Mesh!, "core");
            ValidateGeometry(outline.Mesh!, "outline");
            Check(core.Transform == Transform3D.Identity && outline.Transform == Transform3D.Identity,
                "Triangle geometry requires no corrective rotation or scale");
            Check(outline.MaterialOverride is StandardMaterial3D material && material.CullMode == BaseMaterial3D.CullModeEnum.Front,
                "Closed triangle shell uses the production rarity outline material");
            float border = ShapeVisualFactory.StandardCelOutlineWidth;
            Aabb coreBounds = core.Mesh!.GetAabb(), outlineBounds = outline.Mesh!.GetAabb();
            Check(Mathf.IsEqualApprox(outlineBounds.Size.Y - coreBounds.Size.Y, border * 2f),
                "Triangle outline expands both cap surfaces by its configured width");
            Vector3[] corePoints = core.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector3[] outlinePoints = outline.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector2[] corners = corePoints.Select(point => new Vector2(point.X, point.Z)).Distinct().ToArray();
            Vector2[] outerCorners = outlinePoints.Select(point => new Vector2(point.X, point.Z)).Distinct().ToArray();
            Check(corners.All(corner => outerCorners.Any(outer => outer.Normalized().IsEqualApprox(corner.Normalized()))),
                "Outline keeps the triangle's apex and all three corners aligned");
            foreach (int edge in Enumerable.Range(0, 3))
            {
                Vector2 a = corners[edge], b = corners[(edge + 1) % 3];
                Vector2 outer = outerCorners.Single(point => point.Normalized().IsEqualApprox(a.Normalized()));
                float separation = Mathf.Abs((b - a).Cross(outer - a)) / a.DistanceTo(b);
                Check(Mathf.IsEqualApprox(separation, border), $"Triangle edge {edge} has an even rarity border");
            }
            EquipmentItem item = new() { Shape = shape, Rarity = EquipmentCatalogDefaults.CreateRarityByKey("divine") };
            Node3D ground = FpsGroundItemVisual.Create(item, null);
            AddChild(ground);
            Aabb groundBounds = FpsWeaponVisualFactory.GetVisualBounds(ground)!.Value;
            Check(Mathf.IsEqualApprox(groundBounds.Size.X, .78f) && groundBounds.Size.Y < .78f * .25f &&
                groundBounds.Size.Z > .78f * .75f, "Normalized ground triangle keeps its broad face and shallow thickness");
            ground.Hide();
            if (OS.GetCmdlineUserArgs().Contains("--capture")) await ValidateRenderedViews(visual, ground);
            GD.Print($"Triangle shape validation passed: {_checks} checks.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void ValidateGeometry(Mesh mesh, string label)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        Vector3[] corners = vertices.Distinct().ToArray();
        Check(vertices.Length == 24 && corners.Length == 6 && normals.Length == vertices.Length,
            $"Triangle {label} has two triangular caps and three closed sides with explicit normals");
        Check(vertices.Select(point => new Vector2(point.X, point.Z)).Distinct().Count() == 3,
            $"Triangle {label} projects to exactly three corners across its broad face");
        Aabb bounds = mesh.GetAabb();
        Check(bounds.Size.Y < bounds.Size.X * .25f && Mathf.IsEqualApprox(bounds.Size.Z / bounds.Size.X, Mathf.Sqrt(3f) * .5f),
            $"Triangle {label} has an equilateral face in XZ and a shallow Y extrusion");
        Dictionary<(Vector3, Vector3), int> edges = new();
        for (int index = 0; index < vertices.Length; index += 3)
        {
            Vector3 a = vertices[index], b = vertices[index + 1], c = vertices[index + 2];
            Vector3 cross = (b - a).Cross(c - a);
            Vector3 center = (a + b + c) / 3f;
            Check(cross.LengthSquared() > .00001f && normals[index].IsNormalized() &&
                normals[index].Dot(center) > 0f && cross.Normalized().Dot(normals[index]) < -.999f &&
                normals[index] == normals[index + 1] && normals[index] == normals[index + 2],
                $"Triangle {label} face {index / 3} has outward flat normals and clockwise front winding");
            AddEdge(a, b); AddEdge(b, c); AddEdge(c, a);
        }
        Check(edges.Values.All(count => count == 2), $"Triangle {label} is watertight with every edge shared by two faces");
        void AddEdge(Vector3 a, Vector3 b)
        {
            var reverse = (b, a);
            var key = edges.ContainsKey(reverse) ? reverse : (a, b);
            edges[key] = edges.GetValueOrDefault(key) + 1;
        }
    }

    private async Task ValidateRenderedViews(Node3D visual, Node3D ground)
    {
        Check(DisplayServer.GetName() != "headless", "Triangle image checks use native graphics");
        var viewport = new SubViewport { OwnWorld3D = true, Size = new Vector2I(512, 512), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        visual.Reparent(viewport);
        ground.Reparent(viewport);
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 1.5f,
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.10f, .16f, .22f) },
        };
        viewport.AddChild(camera);
        camera.MakeCurrent();
        var light = new DirectionalLight3D { RotationDegrees = new Vector3(-35f, -25f, 0f), LightEnergy = 1.2f };
        viewport.AddChild(light);
        string directory = ProjectSettings.GlobalizePath("res://.tmp/triangle-shape-validation/native");
        Directory.CreateDirectory(directory);
        foreach (var view in new[]
        {
            ("front", new Vector3(0f, 3f, 0f), Vector3.Forward),
            ("back", new Vector3(0f, -3f, 0f), Vector3.Forward),
            ("oblique", new Vector3(1.4f, 2.2f, -3f), Vector3.Up),
            ("side", new Vector3(0f, 0f, -3f), Vector3.Up),
        })
        {
            camera.Position = view.Item2;
            camera.LookAt(Vector3.Zero, view.Item3);
            light.Position = camera.Position;
            light.LookAt(Vector3.Zero, view.Item3);
            visual.Hide(); using Image empty = await Render(viewport);
            visual.Show(); using Image frame = await Render(viewport);
            Check(frame.SavePng(Path.Combine(directory, $"triangle-{view.Item1}.png")) == Error.Ok, $"Saved native triangle {view.Item1} view");
            int[] rowWidths = new int[frame.GetHeight()];
            int filled = 0, corePixels = 0;
            for (int y = 0; y < frame.GetHeight(); y++)
                for (int x = 0; x < frame.GetWidth(); x++)
                {
                    Color a = frame.GetPixel(x, y), b = empty.GetPixel(x, y);
                    if (Mathf.Max(Mathf.Abs(a.R - b.R), Mathf.Max(Mathf.Abs(a.G - b.G), Mathf.Abs(a.B - b.B))) < .025f) continue;
                    rowWidths[y]++; filled++;
                    // Neutral body remains distinct from the gold rarity shell
                    // in both the lit and shadow cel bands.
                    if (a.B >= a.R * .95f) corePixels++;
                }
            Check(filled > 5000 && corePixels > filled * .25f,
                $"Native {view.Item1} view draws the triangle body and its border");
            if (view.Item1 is "front" or "back")
            {
                int top = Array.FindIndex(rowWidths, count => count > 3), bottom = Array.FindLastIndex(rowWidths, count => count > 3);
                int upper = rowWidths[top + (bottom - top) / 4], lower = rowWidths[top + (bottom - top) * 3 / 4];
                Check(lower > 100 && upper < lower * .55f,
                    $"Native {view.Item1} broad face tapers to a triangle apex instead of a rectangular slab");
            }
        }
        visual.Hide(); ground.Show();
        Vector3 groundCenter = FpsWeaponVisualFactory.GetVisualBounds(ground)!.Value.GetCenter();
        camera.Position = groundCenter + new Vector3(1f, 1.4f, -2.1f);
        camera.LookAt(groundCenter, Vector3.Up);
        light.Position = camera.Position; light.LookAt(groundCenter, Vector3.Up);
        using Image groundFrame = await Render(viewport);
        Check(groundFrame.SavePng(Path.Combine(directory, "triangle-ground.png")) == Error.Ok,
            "Saved native production ground triangle with normalized dimensions");
    }

    private async Task<Image> Render(SubViewport viewport)
    {
        for (int index = 0; index < 3; index++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = viewport.GetTexture().GetImage(); image.Convert(Image.Format.Rgba8); return image;
    }

    private void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        _checks++; GD.Print("PASS: " + description);
    }
}
