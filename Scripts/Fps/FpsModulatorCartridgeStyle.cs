using System;
using System.Collections.Generic;
using Godot;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

/// <summary>One to three authored accent notches identify power tier independently of quality.</summary>
public static class FpsModulatorCartridgeStyle
{
    public static void Apply(Node3D model, WeaponModulatorData part)
    {
        int tier = Math.Clamp((int)part.PowerTier, 0, 2);
        Color color = ModulatorLoot.GetTierColor((ModulatorPowerTier)tier);
        // Keep per-cartridge ownership: inspection effects may recolor a single instance.
        StandardMaterial3D material = new StandardMaterial3D
        {
            AlbedoColor = color, EmissionEnabled = true, Emission = color,
            EmissionEnergyMultiplier = .8f, Metallic = .35f, Roughness = .32f,
        };
        List<MeshInstance3D> bars = new();
        foreach (Node node in Descendants(model))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null) continue;
            string name = mesh.Name.ToString().ToLowerInvariant().Replace(" ", "");
            bool bar = name.Contains("identitybar", StringComparison.Ordinal);
            if (bar) bars.Add(mesh);
            if (bar || name.Contains("typecolor", StringComparison.Ordinal)) mesh.MaterialOverride = material;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName == "ModulatorAccent")
                    mesh.SetSurfaceOverrideMaterial(surface, material);
        }
        if (bars.Count > 0)
        {
            MeshInstance3D template = bars[0];
            while (bars.Count < tier + 1)
            {
                MeshInstance3D clone = (MeshInstance3D)template.Duplicate();
                clone.Name = $"IdentityBarTier{bars.Count}";
                template.GetParent().AddChild(clone);
                bars.Add(clone);
            }
            for (int index = 0; index < bars.Count; index++)
            {
                MeshInstance3D bar = bars[index];
                bar.Visible = index <= tier;
                // Use the original face plane and spacing. Hidden authored extra bars
                // stay hidden, so slot-dependent source markings never add extra tiers.
                bar.Position = new Vector3(-.002f + index * .002f, template.Position.Y, template.Position.Z);
                bar.MaterialOverride = material;
            }
        }
        model.SetMeta("modulator_power_tier", tier + 1);
        model.SetMeta("modulator_notch_count", tier + 1);
        model.SetMeta("modulator_rarity_color", color);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node descendant in Descendants(child)) yield return descendant;
    }
}
