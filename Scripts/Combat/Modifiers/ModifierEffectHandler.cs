using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Combat.Modifiers;

public readonly struct ModifierHitContext
{
    public ModifierHitContext(Node? source, Node? target, float damage)
    {
        Source = source;
        Target = target;
        Damage = damage;
    }

    public Node? Source { get; }
    public Node? Target { get; }
    public float Damage { get; }
}

public readonly struct ModifierKillContext
{
    public ModifierKillContext(Node? killed, Node? killer)
    {
        Killed = killed;
        Killer = killer;
    }

    public Node? Killed { get; }
    public Node? Killer { get; }
}

public readonly struct ModifierDamageContext
{
    public ModifierDamageContext(Node? source, float damageTaken)
    {
        Source = source;
        DamageTaken = damageTaken;
    }

    public Node? Source { get; }
    public float DamageTaken { get; }
}

// Base runtime effect API. One handler per modifier implementation.
public abstract partial class ModifierEffectHandler : Node
{
    public EquipmentItem? PrimaryItem { get; protected set; }
    public Node? RuntimeOwner { get; protected set; }
    public ModifierData? Data { get; protected set; }

    public virtual void OnPrimaryEquipped(EquipmentItem item, Node owner)
    {
        PrimaryItem = item;
        RuntimeOwner = owner;
        Data = item.Modifier;
    }

    public virtual void OnHit(in ModifierHitContext context)
    {
    }

    public virtual void OnKill(in ModifierKillContext context)
    {
    }

    public virtual void OnPlayerDamaged(in ModifierDamageContext context)
    {
    }

    public virtual void OnSynergyActivated()
    {
    }

    public virtual void OnSuperSynergyActivated()
    {
    }
}
