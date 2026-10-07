using Godot;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Managers;

// Global debug gate + forced roll selection state.
public partial class DebugManager : Node
{
    [Signal]
    public delegate void DebugActivatedEventHandler();

    [Signal]
    public delegate void DebugDeactivatedEventHandler();

    [Signal]
    public delegate void ForcedRollChangedEventHandler(string rarityKey, string shapeKey, bool hasModifier, int modifierValue);

    [Export] public bool EnableDebugInDevelopmentBuild = true;
    [Export] public string DevelopmentDebugPassword = "dev-debug";

    public bool DebugActive { get; private set; }

    public string ForcedRarityKey { get; private set; } = string.Empty;
    public string ForcedShapeKey { get; private set; } = string.Empty;
    public ModifierKey? ForcedModifierKey { get; private set; } = null;

    public bool CanUseDevelopmentDebug => EnableDebugInDevelopmentBuild && OS.IsDebugBuild();

    public bool TryActivateWithPassword(string password)
    {
        if (CanUseDevelopmentDebug)
        {
            ActivateDebug();
            return true;
        }

        if (!ValidatePassword(password))
        {
            return false;
        }

        ActivateDebug();
        return true;
    }

    public bool ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(DevelopmentDebugPassword))
        {
            return false;
        }

        return password == DevelopmentDebugPassword;
    }

    public void ActivateDebug()
    {
        if (DebugActive)
        {
            return;
        }

        DebugActive = true;
        EmitSignal(SignalName.DebugActivated);
    }

    public void DeactivateDebug()
    {
        if (!DebugActive)
        {
            return;
        }

        DebugActive = false;
        ClearForcedRoll();
        EmitSignal(SignalName.DebugDeactivated);
    }

    public void SetForcedRoll(string? rarityKey, string? shapeKey, ModifierKey? modifierKey)
    {
        ForcedRarityKey = (rarityKey ?? string.Empty).Trim();
        ForcedShapeKey = (shapeKey ?? string.Empty).Trim();
        ForcedModifierKey = modifierKey;

        EmitSignal(
            SignalName.ForcedRollChanged,
            ForcedRarityKey,
            ForcedShapeKey,
            ForcedModifierKey.HasValue,
            ForcedModifierKey.HasValue ? (int)ForcedModifierKey.Value : -1);
    }

    public void ClearForcedRoll()
    {
        ForcedRarityKey = string.Empty;
        ForcedShapeKey = string.Empty;
        ForcedModifierKey = null;

        EmitSignal(SignalName.ForcedRollChanged, string.Empty, string.Empty, false, -1);
    }

    public bool HasForcedRarity() => !string.IsNullOrWhiteSpace(ForcedRarityKey);
    public bool HasForcedShape() => !string.IsNullOrWhiteSpace(ForcedShapeKey);
    public bool HasForcedModifier() => ForcedModifierKey.HasValue;
}
