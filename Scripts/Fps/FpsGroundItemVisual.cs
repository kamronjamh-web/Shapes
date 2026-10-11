using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Shapes;

namespace SuperGatchaRebuild.Fps;

/// <summary>Ground shapes remain base shapes; modulators use the workbench cartridge.</summary>
public static partial class FpsGroundItemVisual
{
    private static PackedScene? _cartridgeScene;

    public static Node3D Create(EquipmentItem? weapon, WeaponModulatorData? part)
    {
        if (weapon == null && part == null) throw new ArgumentException("A ground item requires equipment.");
        Node3D root = new() { Name = "GroundEquipmentVisual" };
        // Pillars have their own GPU batch and lifetime, independent of model visibility.
        Node3D model;
        if (weapon != null)
        {
            model = new Node3D { Name = "GroundShapeModel" };
            model.AddChild(ShapeVisualFactory.CreateVisual(weapon.Shape, weapon.Rarity?.UiColor ?? GetRarityColor(weapon, null)));
            model.SetMeta("shape_key", weapon.Shape?.Key ?? "square");
            model.SetMeta("visual_source", "ShapeVisualFactory");
            model.SetMeta("assembled_weapon", false);
        }
        else model = CreateCartridge(part!);
        // Normalize after _Ready so generated shape geometry is included.
        root.AddChild(new GroundModel { Name = "ItemModel", Model = model, TargetLength = weapon != null ? .78f : .26f });
        return root;
    }

    public static Color GetRarityColor(EquipmentItem? weapon, WeaponModulatorData? part)
        => weapon != null ? WeaponColor(WeaponRank(weapon.Rarity?.Key)) : ModulatorLoot.GetTierColor(part?.PowerTier ?? ModulatorPowerTier.Tier1);

    private static int WeaponRank(string? key) => (key ?? "common").ToLowerInvariant() switch
    {
        "uncommon" => 1, "rare" => 2, "legendary" or "epic" => 3,
        "mythic" or "unreal" => 4, "divine" => 5, _ => 0,
    };
    private static Color WeaponColor(int rank) => rank switch
    {
        1 => new(.2980f, .6863f, .3137f), 2 => new(.1294f, .5882f, .9529f),
        3 => new(.6118f, .1529f, .6902f), 4 => new(.9451f, .2392f, .2118f),
        5 => new(.9922f, .8471f, .2078f), _ => Colors.White,
    };
    public static FpsLootPillarInstance GetPillarInstance(EquipmentItem? weapon, WeaponModulatorData? part,
        Vector3 position, float phase = 0f)
    {
        if (weapon == null && part == null) throw new ArgumentException("A pillar requires equipment.");
        int rank = weapon != null ? WeaponRank(weapon.Rarity?.Key) : Math.Clamp((int)part!.PowerTier, 0, 2);
        float height = weapon != null ? 1.2f + rank * .48f : 1.10f + rank * .52f;
        float width = weapon != null ? .62f + rank * .11f : .58f + rank * .13f;
        return new FpsLootPillarInstance(position, GetRarityColor(weapon, part), height, width, phase,
            weapon != null ? rank : rank * 2);
    }
    private static Node3D CreateCartridge(WeaponModulatorData part)
    {
        _cartridgeScene ??= GD.Load<PackedScene>("res://Resources/Weapons/WorkbenchCartridge.glb")
            ?? throw new InvalidOperationException("Workbench cartridge model is missing.");
        Node3D model = _cartridgeScene.Instantiate<Node3D>();
        FpsModulatorCartridgeStyle.Apply(model, part);
        return model;
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node descendant in Descendants(child)) yield return descendant;
    }
    private sealed partial class GroundModel : Node3D
    {
        public Node3D Model { get; init; } = null!;
        public float TargetLength { get; init; }
        public override void _EnterTree()
        {
            if (Model.GetParent() == null) AddChild(Model);
        }
        public override void _Ready()
        {
            foreach (Node node in Descendants(Model))
                if (node is LegacyShapeVisualAnimator legacy) legacy._Process(0);
            Aabb? bounds = FpsWeaponVisualFactory.GetVisualBounds(Model);
            if (bounds.HasValue)
            {
                Aabb box = bounds.Value;
                float scale = TargetLength / Mathf.Max(.001f, Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)));
                Model.Scale = Vector3.One * scale;
                Model.Position = new Vector3(-box.GetCenter().X * scale, .14f - box.Position.Y * scale, -box.GetCenter().Z * scale);
            }
            foreach (Node node in Descendants(Model))
            {
                if (node is GeometryInstance3D geometry) geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                if (node is Light3D light) light.Visible = false;
            }
        }
    }
}
