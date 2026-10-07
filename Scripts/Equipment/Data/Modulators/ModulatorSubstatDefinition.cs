using Godot;

namespace SuperGatchaRebuild.Equipment.Data;

[GlobalClass]
public partial class ModulatorSubstatDefinition : Resource
{
    [Export] public SubstatType Type { get; set; } = SubstatType.None;
    [Export] public float Value { get; set; }

    public SubstatRoll ToSubstatRoll()
    {
        return new SubstatRoll(Type, Value);
    }
}