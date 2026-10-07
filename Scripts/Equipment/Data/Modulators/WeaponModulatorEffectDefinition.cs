using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

[GlobalClass]
public partial class WeaponModulatorEffectDefinition : Resource
{
    [Export] public ModulatorEffectType Type { get; set; } = ModulatorEffectType.None;
    [Export] public ModulatorEffectTrigger Trigger { get; set; } = ModulatorEffectTrigger.Passive;
    [Export] public float Magnitude { get; set; }
    [Export] public float MagnitudeB { get; set; }
    [Export] public float MagnitudeC { get; set; }
    [Export] public int Count { get; set; }
    [Export] public int CountB { get; set; }
    [Export] public string Key { get; set; } = string.Empty;

    public WeaponModulatorEffectData ToData()
    {
        return new WeaponModulatorEffectData
        {
            Type = Type,
            Trigger = Trigger,
            Magnitude = Magnitude,
            MagnitudeB = MagnitudeB,
            MagnitudeC = MagnitudeC,
            Count = Count,
            CountB = CountB,
            Key = Key,
        };
    }
}