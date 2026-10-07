using Godot;

namespace SuperGatchaRebuild.Combat.Weapons;

// Example shape behavior pattern for Circle -> Sawblade.
public partial class CircleSawbladeBehavior : WeaponBehavior
{
    [Export] public PackedScene? SawbladeProjectileScene;

    public override void Fire()
    {
        if (!CanFire() || WeaponOwner == null || EquippedItem?.Shape == null)
        {
            return;
        }

        // TODO: Spawn one large sawblade projectile.
        // - Use EquippedItem.Damage as base damage.
        // - Configure pierce count from shape hits (default 5).
        // - Apply modifier hooks for proc effects.
        // - Aim at nearest target from FindTarget().

        ConsumeCooldown(Mathf.Max(0.01f, EquippedItem.Shape.Cooldown));
    }
}
