using System.Collections.Generic;
using System.Linq;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

/// <summary>Explains the FPS use of installed parts without changing their effects or saved data.</summary>
public static class FpsModulatorPresentation
{
    public static string DescribeInstalled(EquipmentItem weapon, IReadOnlyList<EquipmentItem> arsenal,
        int weaponIndex, int equippedIndex)
    {
        if (weapon.Modulators.Count == 0) return "No modulators installed";
        return string.Join("\n\n", weapon.Modulators.Select(part => DescribePart(part, weapon,
            weaponIndex == equippedIndex, weaponIndex, arsenal.Count > 0 ? arsenal[0] : null)));
    }

    public static string DescribePart(WeaponModulatorData part, EquipmentItem weapon,
        bool isEquipped, int arsenalIndex, EquipmentItem? primary, bool includeHeading = true)
    {
        List<string> lines = includeHeading ? new() { part.DisplayName, ModulatorQualityText.Header(part) } : new();
        if (part.IsRetired)
        {
            lines.Add(ModulatorQualityText.Describe(part));
            return string.Join("\n", lines);
        }
        if (!WeaponModulatorCompatibility.CanApplyToWeapon(part, weapon, out string reason))
        {
            lines.Add($"Incompatible: {reason}");
            return string.Join("\n", lines);
        }

        lines.Add(ModulatorQualityText.Describe(part));
        var effects = part.GetResolvedEffects();
        bool Has(ModulatorEffectType type) => effects.Any(effect => effect.Type == type);
        WeaponAttackType type = weapon.Shape!.AttackType;
        bool mimic = type == WeaponAttackType.ShadowClone && weapon.Modulators.Any(mod =>
            mod.GetResolvedEffects().Any(effect => effect.Type == ModulatorEffectType.NewWeaponTransform &&
                WeaponModulatorData.NormalizeKey(effect.Key) == "primary_mimic"));
        bool needsHit = Has(ModulatorEffectType.CritChanceAdd) || Has(ModulatorEffectType.CritDamageMultiplier) ||
            Has(ModulatorEffectType.NewOnCritChainProc) || Has(ModulatorEffectType.NewCapstoneProc) ||
            Has(ModulatorEffectType.RicochetAdd) || Has(ModulatorEffectType.KnockbackMultiplier) ||
            effects.Any(effect => effect.Trigger == ModulatorEffectTrigger.OnHit &&
                effect.Type is not (ModulatorEffectType.ExecuteLowHealth or ModulatorEffectType.EliteBossHunter));

        if (needsHit && mimic)
            lines.Add("Primary Mimic: critical-hit, hit-triggered and knockback bonuses do not apply to copied attacks.");
        else if (needsHit && type is WeaponAttackType.Laser or WeaponAttackType.FlameCone)
            lines.Add("Only initial hits can roll critical hits or trigger hit effects; sustained ticks can still benefit from existing marks.");

        if (Has(ModulatorEffectType.CooldownMultiplier) && type == WeaponAttackType.WrathAura)
            lines.Add("Attack speed changes the manual burst cadence. Passive aura tick speed stays the same.");

        if (Has(ModulatorEffectType.ProjectileCountAdd)) lines.Add(DescribeExtraOutput(type));
        if (Has(ModulatorEffectType.ProjectileSpeedMultiplier) && type == WeaponAttackType.Mortar)
            lines.Add("Flight speed changes how quickly the mortar reaches its impact point.");
        if (Has(ModulatorEffectType.DurationMultiplier)) lines.Add(DescribeDuration(type));

        bool hasPlayerStats = Has(ModulatorEffectType.HealthMultiplier) || Has(ModulatorEffectType.DodgeChanceAdd) ||
            Has(ModulatorEffectType.HealthRegenAdd) || Has(ModulatorEffectType.MovementSpeedMultiplier) ||
            Has(ModulatorEffectType.LuckAdd);
        if (hasPlayerStats) lines.Add(isEquipped
            ? "Player stat bonuses apply while this shape is equipped."
            : "Player stat bonuses are inactive until this shape is equipped.");

        if (part.NormalizedKey == "primary_mimic")
        {
            lines.Add("Copies the first arsenal shape's base attack using this weapon's stats. Copied attacks cannot crit or trigger hit effects.");
            if (primary?.Shape?.AttackType is WeaponAttackType.TemporalEcho or WeaponAttackType.ShadowClone)
                lines.Add($"{primary.Shape.DisplayName} has no copied pattern; clones fire Square shots instead.");
            else if (primary?.Shape?.AttackType is WeaponAttackType.Nova or WeaponAttackType.Orbital or WeaponAttackType.WrathAura)
                lines.Add("This copied attack remains centered on the player.");
        }
        if (part.NormalizedKey == "blood_price_reactor") lines.Add(isEquipped
            ? "The reactor stores damage taken and vents while Klein is equipped."
            : "The reactor cannot charge or vent until Klein is equipped.");
        if (part.NormalizedKey == "volitile")
            lines.Add("Arsenal-wide: works from a support slot. Copied attacks and secondary damage cannot trigger explosions.");
        if (part.IsWingModulator) lines.Add(arsenalIndex <= 0
            ? "Requires a support arsenal slot."
            : isEquipped ? "Escort fire pauses while this shape is equipped." : "Escort fire is active while another shape is equipped.");
        return string.Join("\n", lines);
    }

    public static string DescribeAttack(EquipmentItem weapon) => weapon.Shape?.AttackType switch
    {
        WeaponAttackType.TemporalEcho => "Fires an instant shot, then repeats it after a short delay. Each extra shot receives one delayed repeat.",
        WeaponAttackType.WrathAura => "An aura damages nearby enemies while Klein is equipped and grows stronger with missing health. Firing releases a separate burst.",
        WeaponAttackType.ShadowClone => "Summons temporary clones that attack nearby enemies. Primary Mimic changes them to use the first arsenal shape's base attack.",
        _ => !string.IsNullOrWhiteSpace(weapon.Shape?.SpecialDescription)
            ? weapon.Shape.SpecialDescription : weapon.Shape?.Description ?? "",
    };

    private static string DescribeExtraOutput(WeaponAttackType type) => type switch
    {
        WeaponAttackType.Mine => "Extra output adds mines to each deployment.",
        WeaponAttackType.Orbital => "Extra output adds orbiting guards.",
        WeaponAttackType.Homing => "Extra output adds seekers to each volley.",
        WeaponAttackType.Mortar => "Extra output adds mortars to each barrage.",
        WeaponAttackType.ChainLightning => "Extra output adds targets to the lightning chain.",
        WeaponAttackType.ShadowClone => "Extra output adds summoned clones.",
        WeaponAttackType.Nova => "Extra output repeats the burst around the player, stacking its damage.",
        WeaponAttackType.WrathAura => "Extra output repeats the manual burst. It does not add passive aura ticks.",
        WeaponAttackType.TemporalEcho => "Extra output adds instant shots and one delayed repeat for each shot.",
        _ => "Extra output adds attacks to each volley.",
    };

    private static string DescribeDuration(WeaponAttackType type) => type switch
    {
        WeaponAttackType.Mine => "Duration extends how long deployed mines remain armed.",
        WeaponAttackType.Orbital => "Duration extends how long the orbiting guards remain active.",
        WeaponAttackType.ShadowClone => "Duration extends the clones' lifetime.",
        WeaponAttackType.Sawblade or WeaponAttackType.Homing or WeaponAttackType.Drill => "Duration extends projectile lifetime.",
        WeaponAttackType.Laser or WeaponAttackType.FlameCone => "Duration extends the sustained attack.",
        _ => "Duration extends the damage field's lifetime.",
    };
}
