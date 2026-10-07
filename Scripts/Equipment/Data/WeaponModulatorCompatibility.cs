using System.Collections.Generic;
using SuperGatchaRebuild.Equipment;

namespace SuperGatchaRebuild.Equipment.Data;

public static class WeaponModulatorCompatibility
{
    public static HashSet<string> BuildWeaponTags(EquipmentItem? item)
    {
        HashSet<string> tags = new(System.StringComparer.Ordinal);
        if (item?.Shape == null)
        {
            return tags;
        }

        AddTag(tags, "weapon");
        AddTag(tags, item.Shape.Key);

        switch (item.Shape.AttackType)
        {
            case WeaponAttackType.Single:
                AddTag(tags, "projectile");
                AddTag(tags, "precision");
                break;
            case WeaponAttackType.Sawblade:
                AddTag(tags, "projectile");
                AddTag(tags, "piercing");
                break;
            case WeaponAttackType.Laser:
                AddTag(tags, "beam");
                AddTag(tags, "precision");
                break;
            case WeaponAttackType.Mine:
                AddTag(tags, "mine");
                AddTag(tags, "zone");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Nova:
                AddTag(tags, "aura");
                AddTag(tags, "pulse");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Orbital:
                AddTag(tags, "orbital");
                AddTag(tags, "sustain");
                break;
            case WeaponAttackType.Homing:
                AddTag(tags, "projectile");
                AddTag(tags, "homing");
                break;
            case WeaponAttackType.FlameCone:
                AddTag(tags, "cone");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Rift:
                AddTag(tags, "zone");
                AddTag(tags, "area");
                break;
            case WeaponAttackType.Mortar:
                AddTag(tags, "mortar");
                AddTag(tags, "area");
                AddTag(tags, "projectile");
                break;
            case WeaponAttackType.ChainLightning:
                AddTag(tags, "chain");
                AddTag(tags, "projectile");
                break;
            case WeaponAttackType.Drill:
                AddTag(tags, "projectile");
                AddTag(tags, "piercing");
                break;
            case WeaponAttackType.VoidSingularity:
                AddTag(tags, "zone");
                AddTag(tags, "area");
                AddTag(tags, "pull");
                break;
            case WeaponAttackType.TemporalEcho:
                AddTag(tags, "passive");
                AddTag(tags, "echo");
                break;
            case WeaponAttackType.WrathAura:
                AddTag(tags, "passive");
                AddTag(tags, "aura");
                break;
            case WeaponAttackType.ShadowClone:
                AddTag(tags, "clone");
                AddTag(tags, "replay");
                break;
        }

        return tags;
    }

    private static void AddTag(HashSet<string> tags, string tag)
    {
        string normalized = WeaponModulatorData.NormalizeTag(tag);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            tags.Add(normalized);
        }
    }
}