using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

public enum ShapeGroup
{
    TwoD = 0,
    ThreeD = 1,
    Ultra = 2,
    Mythic = 3,
}

public enum WeaponAttackType
{
    Single = 0,
    Sawblade = 1,
    Laser = 2,
    Mine = 3,
    Nova = 4,
    Orbital = 5,
    Homing = 6,
    FlameCone = 7,
    Rift = 8,
    Mortar = 9,
    ChainLightning = 10,
    Drill = 11,
    VoidSingularity = 12,
    TemporalEcho = 13,
    WrathAura = 14,
    ShadowClone = 15,
}

[GlobalClass]
public partial class ShapeData : Resource
{
    [Export] public string Key = "square";
    [Export] public string DisplayName = "Square";
    [Export] public ShapeGroup ShapeGroup = ShapeGroup.TwoD;
    [Export(PropertyHint.Range, "0.00001,1000,0.00001")] public float Weight = 1.0f;
    [Export(PropertyHint.Range, "1,10,1")] public int Stars = 1;

    [Export] public WeaponAttackType AttackType = WeaponAttackType.Single;
    [Export] public string AttackName = "Flat Strike";
    [Export(PropertyHint.Range, "0,1000,1")] public int Hits = 1;
    [Export(PropertyHint.Range, "0,100000,0.01")] public float DamagePerHit = 80f;
    [Export(PropertyHint.Range, "0,100000,0.01")] public float TotalDamage = 80f;
    [Export(PropertyHint.Range, "0,9999,0.01")] public float Cooldown = 1f;

    [Export(PropertyHint.MultilineText)] public string SpecialDescription = string.Empty;
    [Export(PropertyHint.MultilineText)] public string Description = string.Empty;

    [ExportGroup("Visuals")]
    [Export] public PackedScene? VisualScene;
    [Export] public Vector3 VisualScale = Vector3.One;
    [Export] public Vector3 VisualRotationDegrees = Vector3.Zero;

    [ExportGroup("Runtime")]

    // Optional behavior scene to instantiate runtime weapon logic.
    [Export] public PackedScene? WeaponScene;

    public override string ToString() => $"{DisplayName} [{AttackType}]";
}
