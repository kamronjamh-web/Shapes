using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

public enum ModifierKey
{
    Normal = 0,
    Shiny = 1,
    Electric = 2,
    Dark = 3,
    Unstable = 4,
    Shielded = 5,
    Prismatic = 6,
    Cursed = 7,
    Thorned = 8,
    Parasite = 9,
    Volitile = 10,
}

[GlobalClass]
public partial class ModifierData : Resource
{
    [Export] public ModifierKey Key = ModifierKey.Normal;
    [Export] public string DisplayName = "Normal";
    [Export(PropertyHint.Range, "0.00001,1000,0.00001")] public float Weight = 1.0f;
    [Export] public Color UiColor = Colors.White;

    [Export(PropertyHint.Range, "0.01,10,0.01")] public float HpMultiplier = 1.0f;
    [Export(PropertyHint.Range, "0.01,10,0.01")] public float DamageMultiplier = 1.0f;
    [Export(PropertyHint.Range, "0,100,0.0001")] public float XpMultiplierBonus = 0f;

    [Export] public string PrimaryEffectKey = "none";
    [Export(PropertyHint.MultilineText)] public string PrimaryEffectDescription = "No special effect";

    [Export] public string PassiveName = string.Empty;
    [Export(PropertyHint.MultilineText)] public string PassiveDescription = string.Empty;

    [Export] public string SynergyName = string.Empty;
    [Export(PropertyHint.MultilineText)] public string SynergyDescription = string.Empty;

    [Export] public string SuperSynergyName = string.Empty;
    [Export(PropertyHint.MultilineText)] public string SuperSynergyDescription = string.Empty;

    // Special generation rules for substats.
    [Export] public SubstatType GuaranteedSubstat = SubstatType.None;
    [Export] public bool GrantsAllSubstats = false;

    // Wildcard behavior (e.g. Prismatic) for synergy calculation.
    [Export] public bool IsWildcard = false;

    public override string ToString() => DisplayName;
}
