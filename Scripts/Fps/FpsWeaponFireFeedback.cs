using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Fps;

/// <summary>Reusable, instance-owned firing geometry. Authored mechanisms supply recoil.</summary>
public partial class FpsWeaponFireFeedback : Node3D
{
    private readonly List<(StandardMaterial3D Material, Color Emission, float Energy, bool Enabled)> _pulses = new();
    private StandardMaterial3D _flashMaterial = null!;
    private float _remaining;
    private float _duration;
    private string _shape = "";

    public static FpsWeaponFireFeedback? Create(Node3D weapon, Node3D? muzzle, string shape)
    {
        if (muzzle == null || shape is not ("square" or "circle" or "triangle" or "tesseract"
            or "hexagon" or "sphere" or "cube" or "pyramid" or "cone" or "hyperphere"
            or "pentachoron" or "hypercone" or "impossiblecube" or "mobius" or "klein" or "boys")) return null;
        FpsWeaponFireFeedback feedback = new() { Name = "WeaponFireFeedback", Visible = false, _shape = shape };
        // Muzzle anchors use the same -Z forward convention as the authored geometry.
        muzzle.AddChild(feedback);
        Color tint = weapon.HasMeta("rarity_color") ? weapon.GetMeta("rarity_color").AsColor() : Colors.White;
        feedback._flashMaterial = new StandardMaterial3D
        {
            AlbedoColor = tint.Lerp(Colors.White, .38f),
            EmissionEnabled = true,
            Emission = tint.Lerp(Colors.White, .25f),
            EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Keep depth testing: the muzzle aperture must remain attached to solid geometry.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        feedback.BuildGeometry();
        feedback.CapturePulseMaterials(weapon);
        return feedback;
    }

    public void Fire(float cooldownSeconds)
    {
        float cooldown = float.IsFinite(cooldownSeconds) ? cooldownSeconds : .15f;
        float maximum = _shape switch
        {
            "tesseract" or "hyperphere" or "impossiblecube" or "mobius" or "klein" or "boys" => .24f,
            "cone" or "hypercone" or "pentachoron" => .20f,
            _ => .16f,
        };
        _duration = Mathf.Clamp(cooldown * .55f, .075f, maximum);
        _remaining = _duration;
        Visible = true;
        ApplyPulse(1f);
    }

    public void Advance(float delta)
    {
        if (_remaining <= 0f || !float.IsFinite(delta) || delta <= 0f) return;
        _remaining = Mathf.Max(0f, _remaining - delta);
        float strength = _remaining / _duration;
        ApplyPulse(strength);
        if (_remaining <= 0f) Reset();
    }

    public void Reset()
    {
        _remaining = 0f;
        Visible = false;
        Transform = Transform3D.Identity;
        foreach (var pulse in _pulses)
        {
            pulse.Material.Emission = pulse.Emission;
            pulse.Material.EmissionEnergyMultiplier = pulse.Energy;
            pulse.Material.EmissionEnabled = pulse.Enabled;
        }
    }

    private void ApplyPulse(float strength)
    {
        _flashMaterial.EmissionEnergyMultiplier = 1.4f + strength * 3.2f;
        // Rapid aperture expansion followed by collapse; no per-shot nodes or materials.
        float swell = 1f + (1f - strength) * .55f;
        Vector3 dimensions = new(swell, swell, .45f + strength * .55f);
        Vector3 rotation = _shape == "circle" ? new Vector3(0f, 0f, (1f - strength) * .75f)
            : _shape == "tesseract" ? new Vector3(0f, 0f, (1f - strength) * -.4f) : Vector3.Zero;
        ApplyAdditionalPulse(strength, ref dimensions, ref rotation);
        // Scale in the feedback's rotated local axes. Only this owned aperture moves;
        // the imported Fire clip remains the sole source of mechanism transforms.
        Transform = new Transform3D(Basis.FromEuler(rotation) * Basis.FromScale(dimensions), Vector3.Zero);
        foreach (var pulse in _pulses)
        {
            pulse.Material.EmissionEnabled = true;
            pulse.Material.Emission = pulse.Emission.Lerp(_flashMaterial.Emission, strength * .7f);
            pulse.Material.EmissionEnergyMultiplier = pulse.Energy + strength * 2.2f;
        }
    }

    private void BuildGeometry()
    {
        if (_shape is not ("square" or "circle" or "triangle" or "tesseract"))
        {
            BuildAdditionalGeometry();
            return;
        }
        int sides = _shape switch { "triangle" => 3, "circle" => 12, _ => 4 };
        float radius = _shape == "tesseract" ? .105f : _shape == "triangle" ? .032f : .023f;
        float start = _shape == "triangle" ? Mathf.Pi * .5f : Mathf.Pi * .25f;
        AddRing(sides, radius, -.022f, start);
        if (_shape == "tesseract")
        {
            AddRing(4, radius * .52f, -.09f, start);
            for (int i = 0; i < 4; i++)
            {
                float angle = start + Mathf.Tau * i / 4;
                Vector3 axis = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                AddEdge(axis * radius + new Vector3(0f, 0f, -.022f), axis * radius * .52f + new Vector3(0f, 0f, -.09f));
            }
        }
        else
        {
            float length = _shape == "triangle" ? .13f : .06f;
            AddEdge(new Vector3(0f, 0f, -.018f), new Vector3(0f, 0f, -length));
        }
    }

    private void AddRing(int sides, float radius, float depth, float start)
    {
        for (int i = 0; i < sides; i++)
        {
            float a = start + Mathf.Tau * i / sides;
            float b = start + Mathf.Tau * (i + 1) / sides;
            AddEdge(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, depth),
                new Vector3(Mathf.Cos(b) * radius, Mathf.Sin(b) * radius, depth));
        }
    }

    private void AddEdge(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        Vector3 axis = direction.Normalized();
        Vector3 right = axis.Cross(Mathf.Abs(axis.Dot(Vector3.Up)) > .98f ? Vector3.Right : Vector3.Up).Normalized();
        MeshInstance3D piece = new()
        {
            Mesh = new BoxMesh { Size = new Vector3(EdgeThickness(), _shape == "tesseract" ? .004f : EdgeThickness(), direction.Length()) },
            MaterialOverride = _flashMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(right, axis.Cross(right).Normalized(), axis), (start + end) * .5f),
        };
        AddChild(piece);
    }

    private float EdgeThickness() => _shape switch
    {
        "tesseract" => .008f,
        "hyperphere" or "hypercone" or "impossiblecube" or "pentachoron" => .006f,
        "mobius" or "klein" or "boys" => .0035f,
        _ => .004f,
    };

    private void CapturePulseMaterials(Node node, bool inInsert = false)
    {
        inInsert |= node.Name.ToString() == "ShapeSocket";
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                Material? source = mesh.GetSurfaceOverrideMaterial(surface) ?? mesh.Mesh.SurfaceGetMaterial(surface);
                Material? imported = mesh.Mesh.SurfaceGetMaterial(surface);
                if (source is not StandardMaterial3D material || (!inInsert && imported?.ResourceName != "RarityAccent")) continue;
                // Duplicating every targeted surface isolates even factory-shared instance materials.
                StandardMaterial3D local = (StandardMaterial3D)material.Duplicate();
                mesh.SetSurfaceOverrideMaterial(surface, local);
                _pulses.Add((local, local.Emission, local.EmissionEnergyMultiplier, local.EmissionEnabled));
            }
        }
        foreach (Node child in node.GetChildren())
        {
            if (child != this) CapturePulseMaterials(child, inInsert);
        }
    }
}
