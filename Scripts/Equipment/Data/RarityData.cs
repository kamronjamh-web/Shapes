using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

[GlobalClass]
public partial class RarityData : Resource
{
    [Export] public string Key = "common";
    [Export] public string DisplayName = "Common";
    [Export(PropertyHint.Range, "0.00001,1000,0.00001")] public float Weight = 1.0f;
    [Export(PropertyHint.Range, "0.01,10,0.01")] public float StatMultiplier = 1.0f;
    [Export(PropertyHint.Range, "0,10,1")] public int SubstatCount = 1;
    [Export(PropertyHint.Range, "0,100000000,1")] public int XpValue = 8;
    [Export] public Color UiColor = Colors.White;

    public override string ToString() => $"{DisplayName} ({Key})";
}
