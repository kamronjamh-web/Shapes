using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Fps;

/// <summary>Meter-scale weapon geometry, facing -Z with +Y up. No gameplay state is changed.</summary>
public static partial class FpsWeaponVisualFactory
{
    public const string SquareScenePath = "res://Resources/Weapons/SquareHandgun.glb";
    private static PackedScene? _squareScene;

    private sealed partial class FittedShape : Node3D
    {
        public Vector3 TargetSize { get; init; }
        public Node3D Content { get; init; } = null!;
        public Node3D Visual { get; init; } = null!;
        public bool PreserveAspect { get; init; }
        public float MountTopY { get; init; } = .079f;

        public override void _Ready()
        {
            // Native 4D animators create their meshes in _Ready. Child readiness precedes ours.
            if (Visual is LegacyShapeVisualAnimator legacy) legacy._Process(0);
            Aabb? bounds = Bounds(Visual, Transform3D.Identity);
            if (bounds == null) throw new InvalidOperationException("Weapon shape has no geometry after initialization.");
            Vector3 size = bounds.Value.Size;
            Scale = new Vector3(TargetSize.X / Mathf.Max(size.X, 0.001f), TargetSize.Y / Mathf.Max(size.Y, 0.001f), TargetSize.Z / Mathf.Max(size.Z, 0.001f));
            if (PreserveAspect)
            {
                // Thin ribbons and projected surfaces must retain their canonical proportions.
                float uniform = Mathf.Min(Scale.X, Mathf.Min(Scale.Y, Scale.Z));
                Scale = Vector3.One * uniform;
            }
            Content.Position = -bounds.Value.GetCenter();
            Position = new Vector3(Position.X, MountTopY + size.Y * Scale.Y * .5f, Position.Z);
        }
    }

    public static Node3D Create(EquipmentItem weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        _squareScene ??= GD.Load<PackedScene>(SquareScenePath);
        Node3D root = _squareScene.Instantiate<Node3D>();
        root.Name = "EquippedHandgun";
        string key = (weapon.Shape?.Key ?? "square").Trim().ToLowerInvariant();
        if (key == "hypersphere") key = "hyperphere";
        Color rarity = weapon.Rarity?.UiColor ?? Colors.White;
        root.SetMeta("shape_key", key);
        root.SetMeta("rarity_color", rarity);
        root.SetMeta("unlocked_modulator_slots", weapon.UnlockedModulatorSlots);
        root.SetMeta("units", "meters; forward -Z; up +Y");
        StandardMaterial3D rarityMaterial = AccentMaterial(rarity);

        foreach (Node node in Descendants(root))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null) continue;
            // Surface overrides belong to this instance; imported mesh/material resources stay immutable.
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                Material? material = mesh.Mesh.SurfaceGetMaterial(surface);
                if (material?.ResourceName == "RarityAccent")
                    mesh.SetSurfaceOverrideMaterial(surface, rarityMaterial);
            }
        }

        for (int index = 0; index < 10; index++)
        {
            Node3D? bay = Find(root, $"ModulatorSocket{index + 1:00}");
            if (bay == null) throw new InvalidOperationException($"Runtime weapon lacks modulator bay {index + 1}.");
            bool unlocked = index < weapon.UnlockedModulatorSlots;
            WeaponModulatorData? modulator = unlocked && index < (weapon.Modulators?.Count ?? 0)
                ? weapon.Modulators![index] : null;
            bool installed = modulator != null;
            bay.SetMeta("slot_index", index);
            bay.SetMeta("state", !unlocked ? "locked" : installed ? "installed" : "empty");
            bay.SetMeta("modulator_key", modulator?.Key ?? "");
            StandardMaterial3D? identity = installed ? AccentMaterial(modulator!.UiColor) : null;
            foreach (Node node in Descendants(bay))
            {
                if (node is not Node3D part) continue;
                string name = part.Name.ToString();
                if (name.StartsWith("Locked", StringComparison.Ordinal)) part.Visible = !unlocked;
                else if (name.StartsWith("EmptyContact", StringComparison.Ordinal)) part.Visible = unlocked && !installed;
                else if (name.StartsWith("Cartridge", StringComparison.Ordinal)) part.Visible = installed;
                if (identity != null && part is MeshInstance3D mesh &&
                    (name.StartsWith("TypeColor", StringComparison.Ordinal) || name.StartsWith("IdentityBar", StringComparison.Ordinal)))
                    mesh.MaterialOverride = identity;
            }
        }

        if (key != "square") BuildShapeUpper(root, weapon.Shape, key, rarity);
        return root;
    }

    private static void BuildShapeUpper(Node3D root, ShapeData? shape, string key, Color rarity)
    {
        // Frame, trigger, grip, controls, all ten recessed bays and sights remain shared.
        foreach (Node node in Descendants(root))
        {
            string name = node.Name.ToString();
            if (node is Node3D part && (name.StartsWith("Slide___", StringComparison.Ordinal) ||
                name.StartsWith("Dock___", StringComparison.Ordinal) || name == "ShapeSocket" ||
                name.StartsWith("Accent___slide", StringComparison.Ordinal) || name.StartsWith("Marking___", StringComparison.Ordinal)))
                part.Visible = false;
        }
        ShapeData canonical = new() { Key = key, ShapeGroup = shape?.ShapeGroup ?? ShapeGroup.TwoD };
        Node3D chamber = CreateStructuralUpper(key, rarity);
        chamber.Name = "ShapeUpperChamber";
        chamber.SetMeta("shape_key", key);
        root.AddChild(chamber);
        bool flat = canonical.ShapeGroup == ShapeGroup.TwoD;
        Node3D insert = FitShape(canonical, rarity, new Vector3(0.052f, flat ? 0.012f : 0.047f, 0.052f),
            new Vector3(0, flat ? 0.085f : 0.101f, 0.05f), false);
        insert.Name = "EquippedShapeInsert";
        insert.SetMeta("shape_key", key);
        root.AddChild(insert);
        root.AddChild(new MeshInstance3D
        {
            Name = "ShapeInsertPedestal",
            Position = new Vector3(0, 0.073f, 0.05f),
            Mesh = new CylinderMesh { TopRadius = 0.023f, BottomRadius = 0.027f, Height = 0.012f, RadialSegments = 16 },
            MaterialOverride = AccentMaterial(rarity),
        });
    }

    private static Node3D FitShape(ShapeData shape, Color color, Vector3 targetSize, Vector3 position, bool chamber)
    {
        Node3D visual = ShapeVisualFactory.CreateVisual(shape, color, allowSceneInstance: false);
        // Flat shapes lie on the chamber's top; 3D cones point down the barrel.
        if (chamber || shape.ShapeGroup == ShapeGroup.TwoD) visual.RotationDegrees = new Vector3(90, 0, 0);
        Node3D centered = new();
        FittedShape fit = new()
        {
            Position = position, TargetSize = targetSize, Content = centered, Visual = visual,
            PreserveAspect = shape.ShapeGroup != ShapeGroup.TwoD,
        };
        fit.AddChild(centered);
        centered.AddChild(visual);
        return fit;
    }

    private static Aabb? Bounds(Node3D node, Transform3D parent)
    {
        if (!node.Visible) return null;
        Transform3D transform = parent * node.Transform;
        Aabb? result = null;
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
            result = transform * mesh.Mesh.GetAabb();
        else if (node is MultiMeshInstance3D cloud && cloud.Multimesh?.Mesh != null)
        {
            MultiMesh multimesh = cloud.Multimesh;
            int count = multimesh.VisibleInstanceCount < 0 ? multimesh.InstanceCount : Math.Min(multimesh.VisibleInstanceCount, multimesh.InstanceCount);
            Aabb meshBounds = multimesh.Mesh.GetAabb();
            for (int i = 0; i < count; i++)
            {
                Aabb instanceBounds = transform * (multimesh.GetInstanceTransform(i) * meshBounds);
                result = result.HasValue ? result.Value.Merge(instanceBounds) : instanceBounds;
            }
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is not Node3D spatial) continue;
            Aabb? childBounds = Bounds(spatial, transform);
            if (childBounds.HasValue) result = result.HasValue ? result.Value.Merge(childBounds.Value) : childBounds;
        }
        return result;
    }

    /// <summary>CPU geometry bounds in root-local coordinates, including visible MultiMesh instances.</summary>
    public static Aabb? GetVisualBounds(Node3D root)
    {
        Aabb? result = null;
        if (!root.Visible) return result;
        // Exclude the root transform so callers can frame or normalize its local geometry.
        if (root is MeshInstance3D mesh && mesh.Mesh != null) result = mesh.Mesh.GetAabb();
        if (root is MultiMeshInstance3D cloud && cloud.Multimesh?.Mesh != null)
        {
            MultiMesh multi = cloud.Multimesh;
            int count = multi.VisibleInstanceCount < 0 ? multi.InstanceCount : Math.Min(multi.VisibleInstanceCount, multi.InstanceCount);
            for (int i = 0; i < count; i++)
            {
                Aabb box = multi.GetInstanceTransform(i) * multi.Mesh.GetAabb();
                result = result.HasValue ? result.Value.Merge(box) : box;
            }
        }
        foreach (Node child in root.GetChildren())
        {
            if (child is not Node3D spatial) continue;
            Aabb? box = Bounds(spatial, Transform3D.Identity);
            if (box.HasValue) result = result.HasValue ? result.Value.Merge(box.Value) : box;
        }
        return result;
    }

    private static Node3D CreateStructuralUpper(string key, Color rarity)
    {
        // A solid housing carries the barrel and sights. Shape identity controls both its
        // cross section and longitudinal profile; the canonical animated shape is the insert.
        (int sides, float[] widths, float[] heights, float twist) profile = key switch
        {
            "circle" => (12, new[] { .013f, .027f, .034f, .029f, .018f }, new[] { 1f, 1f, 1f, 1f, 1f }, 0f),
            "triangle" => (3, new[] { .010f, .016f, .021f, .028f, .033f }, new[] { .8f, .9f, 1f, 1f, 1f }, 0f),
            "hexagon" => (6, new[] { .020f, .028f, .030f, .030f, .022f }, new[] { .9f, 1f, 1f, 1f, .9f }, 0f),
            "sphere" => (16, new[] { .016f, .026f, .033f, .028f, .018f }, new[] { .75f, 1f, 1.12f, 1f, .8f }, 0f),
            "cube" => (4, new[] { .028f, .028f, .028f, .028f, .028f }, new[] { 1f, 1f, 1f, 1f, 1f }, .125f),
            "pyramid" => (3, new[] { .015f, .022f, .029f, .033f, .026f }, new[] { .55f, .8f, 1.18f, 1.05f, .8f }, 0f),
            "cone" => (12, new[] { .010f, .017f, .023f, .029f, .033f }, new[] { .5f, .7f, .85f, 1f, 1.05f }, 0f),
            "tesseract" => (4, new[] { .021f, .033f, .024f, .033f, .024f }, new[] { .8f, 1.1f, .85f, 1.1f, .85f }, .125f),
            "hyperphere" => (12, new[] { .018f, .034f, .022f, .034f, .021f }, new[] { .8f, 1.15f, .8f, 1.15f, .85f }, 0f),
            "pentachoron" => (5, new[] { .018f, .032f, .020f, .034f, .024f }, new[] { .8f, 1.15f, .8f, 1.15f, .85f }, .08f),
            "hypercone" => (8, new[] { .012f, .020f, .026f, .031f, .034f }, new[] { .6f, .75f, .9f, 1f, 1.1f }, .5f),
            "impossiblecube" => (4, new[] { .031f, .021f, .033f, .022f, .031f }, new[] { 1f, .8f, 1.15f, .8f, 1f }, .125f),
            "mobius" => (6, new[] { .024f, .030f, .022f, .030f, .024f }, new[] { .9f, 1.1f, .8f, 1.1f, .9f }, .5f),
            "klein" => (12, new[] { .014f, .022f, .031f, .035f, .022f }, new[] { .65f, .8f, 1.15f, 1.1f, .8f }, .1f),
            "boys" => (9, new[] { .023f, .033f, .025f, .032f, .021f }, new[] { .8f, 1.05f, .9f, 1.15f, .85f }, .2f),
            _ => (8, new[] { .022f, .028f, .030f, .028f, .023f }, new[] { 1f, 1f, 1f, 1f, 1f }, 0f),
        };
        List<Vector3> rings = new();
        for (int section = 0; section < 5; section++)
        {
            float z = Mathf.Lerp(-.132f, .108f, section / 4f);
            for (int vertex = 0; vertex < profile.sides; vertex++)
            {
                float angle = Mathf.Tau * (vertex / (float)profile.sides + profile.twist * section / 4f) + Mathf.Pi / 2;
                rings.Add(new Vector3(Mathf.Cos(angle) * profile.widths[section],
                    .050f + Mathf.Sin(angle) * .020f * profile.heights[section], z));
            }
        }
        SurfaceTool surface = new();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a, Vector3 b, Vector3 c) { surface.AddVertex(a); surface.AddVertex(b); surface.AddVertex(c); }
        for (int section = 0; section < 4; section++)
        {
            for (int vertex = 0; vertex < profile.sides; vertex++)
            {
                int next = (vertex + 1) % profile.sides;
                Vector3 a = rings[section * profile.sides + vertex], b = rings[section * profile.sides + next];
                Vector3 c = rings[(section + 1) * profile.sides + next], d = rings[(section + 1) * profile.sides + vertex];
                Triangle(a, b, c); Triangle(a, c, d);
            }
        }
        for (int vertex = 0; vertex < profile.sides; vertex++)
        {
            int next = (vertex + 1) % profile.sides;
            Triangle(new Vector3(0, .05f, -.132f), rings[next], rings[vertex]);
            Triangle(new Vector3(0, .05f, .108f), rings[4 * profile.sides + vertex], rings[4 * profile.sides + next]);
        }
        surface.GenerateNormals();
        Node3D upper = new();
        upper.AddChild(new MeshInstance3D
        {
            Name = "SolidShapeHousing", Mesh = surface.Commit(),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.21f, .26f, .30f), Metallic = .72f, Roughness = .32f },
        });
        upper.AddChild(new MeshInstance3D
        {
            Name = "ReceiverMount", Position = new Vector3(0, .031f, -.012f),
            Mesh = new BoxMesh { Size = new Vector3(.043f, .010f, .215f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.16f, .20f, .23f), Metallic = .6f, Roughness = .4f },
        });
        // A continuous fitted spine supports both existing sight posts and the shape dock.
        upper.AddChild(new MeshInstance3D
        {
            Name = "SightAndDockSpine", Position = new Vector3(0, .071f, -.012f),
            Mesh = new BoxMesh { Size = new Vector3(.024f, .009f, .224f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.13f, .17f, .20f), Metallic = .65f, Roughness = .4f },
        });
        upper.AddChild(new MeshInstance3D
        {
            Name = "ShapeHousingRaritySeam", Position = new Vector3(0, .032f, -.012f),
            Mesh = new BoxMesh { Size = new Vector3(.049f, .002f, .211f) }, MaterialOverride = AccentMaterial(rarity),
        });
        return upper;
    }

    private static StandardMaterial3D AccentMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.3f,
        Roughness = 0.35f,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = 0.12f,
    };

    private static Node3D? Find(Node root, string name)
    {
        foreach (Node node in Descendants(root))
            if (node.Name == name && node is Node3D spatial) return spatial;
        return null;
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node node in Descendants(child)) yield return node;
        }
    }
}
