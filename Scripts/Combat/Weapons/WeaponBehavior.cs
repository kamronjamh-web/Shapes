using Godot;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild.Combat.Weapons;

// Placeholder until your real player combat controller is wired in.
// Replace/extend this class with your actual player runtime implementation.
public partial class PlayerController : Node3D
{
}

// Base runtime weapon behavior. One subclass per shape attack implementation.
public abstract partial class WeaponBehavior : Node3D
{
    protected EquipmentItem? EquippedItem;
    protected PlayerController? WeaponOwner;

    protected float CooldownRemaining;

    public virtual void Initialize(EquipmentItem item, PlayerController owner)
    {
        EquippedItem = item;
        WeaponOwner = owner;
        CooldownRemaining = 0f;
    }

    public virtual void Fire()
    {
        if (!CanFire())
        {
            return;
        }

        // Base behavior only consumes cooldown.
        ConsumeCooldown();
    }

    public virtual void TickCooldown(double delta)
    {
        CooldownRemaining = Mathf.Max(0f, CooldownRemaining - (float)delta);
    }

    public virtual Node3D? FindTarget()
    {
        // Hook this to your target query system (nearest enemy, best LOS, etc.).
        return null;
    }

    public virtual void ApplyModifierHooks()
    {
        // Hook modifier handlers here (OnHit, chain, proc, etc.).
    }

    public bool CanFire() => CooldownRemaining <= 0f;

    protected virtual float GetBaseCooldown()
    {
        if (EquippedItem?.Shape == null)
        {
            return 1f;
        }

        return Mathf.Max(0.01f, EquippedItem.Shape.Cooldown);
    }

    protected void ConsumeCooldown(float? overrideValue = null)
    {
        CooldownRemaining = overrideValue ?? GetBaseCooldown();
    }
}
