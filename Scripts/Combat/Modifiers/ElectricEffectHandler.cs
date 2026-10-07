using Godot;

namespace SuperGatchaRebuild.Combat.Modifiers;

// Example pattern for one concrete modifier implementation.
// Wire this into your combat event bus and damage pipeline.
public partial class ElectricEffectHandler : ModifierEffectHandler
{
    public override void OnHit(in ModifierHitContext context)
    {
        // TODO: Primary effect (chain lightning +2 targets).
        // 1) Query nearby enemies around context.Target.
        // 2) Spawn arc hits at reduced damage.
        // 3) Apply visual/impact feedback.
    }

    public override void OnSynergyActivated()
    {
        // TODO: Conductor synergy.
        // Mark enemies as Charged and schedule periodic detonations.
    }

    public override void OnSuperSynergyActivated()
    {
        // TODO: Thunderstorm super synergy.
        // Every 4 sec: strike random enemy globally, then chain to nearby targets.
    }
}
