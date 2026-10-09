using System.Collections.Generic;
using System.Text;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.UI;

public partial class InventoryModalUI
{
    private void UpdateSelectedItemDetails()
    {
        EquipmentItem? item = GetSelectedItem();
        if (item == null)
        {
            SetDetailsText("Select an item to inspect details.");
            ShapePreview?.SetItem(null);
            return;
        }

        ShapePreview?.SetItem(item);
        SetDetailsText(BuildItemDetailsBbcode(item));
    }

    private string BuildItemDetailsBbcode(EquipmentItem item)
    {
        int targetSlotIndex = Mathf.Clamp(EquipSlotDropdown?.Selected ?? 0, 0, EquippedSlotCount - 1);
        EquipmentItem? comparedItem = _inventoryManager?.GetEquippedItemAt(targetSlotIndex);
        bool comparingDifferentItem = _showAdvancedInfo && comparedItem != null && !ReferenceEquals(comparedItem, item);

        string itemName = EscapeBbcode(BuildPickerLabel(item, false));
        string rarityColor = ToHtmlColor(GetRarityColor(item));
        int equippedSlot = FindEquippedSlot(item);

        StringBuilder builder = new();
        builder.AppendLine($"[color={rarityColor}][font_size=20][b]{itemName}[/b][/font_size][/color]");
        builder.AppendLine($"L{item.Level}  |  {item.Stars}-star  |  {FormatEquippedStatus(equippedSlot)}");
        builder.AppendLine();

        int effectiveHealth = StatGenerator.GetEffectiveHealth(item);
        int effectiveDamage = StatGenerator.GetEffectiveDamage(item);

        builder.AppendLine("[b]Main Stats[/b]");
        builder.AppendLine($"HP  [b]{effectiveHealth}[/b]     Bullet DMG  [b]{effectiveDamage}[/b]");
        if (_showAdvancedInfo && comparingDifferentItem && comparedItem != null)
        {
            builder.AppendLine($"Compared to {GetSlotShortLabel(targetSlotIndex)}:  HP {FormatDelta(effectiveHealth - StatGenerator.GetEffectiveHealth(comparedItem), false)}     Bullet DMG {FormatDelta(effectiveDamage - StatGenerator.GetEffectiveDamage(comparedItem), false)}");
        }
        else if (_showAdvancedInfo && comparedItem == null)
        {
            builder.AppendLine($"Compared to {GetSlotShortLabel(targetSlotIndex)}: empty slot");
        }
        else if (_showAdvancedInfo)
        {
            builder.AppendLine($"Compared to {GetSlotShortLabel(targetSlotIndex)}: currently equipped here");
        }

        builder.AppendLine();
        AppendWeaponProfile(builder, item, comparedItem, comparingDifferentItem, _showAdvancedInfo);
        builder.AppendLine();
        AppendCustomizationProfile(builder, item);
        if (ShouldShowModifierProfile(item, _showAdvancedInfo))
        {
            builder.AppendLine();
            AppendModifierProfile(builder, item);
        }
        builder.AppendLine();
        AppendSubstats(builder, item, comparedItem, comparingDifferentItem);

        return builder.ToString().TrimEnd();
    }

    private void AppendCustomizationProfile(StringBuilder builder, EquipmentItem item)
    {
        builder.AppendLine("[b]Customization[/b]");
        builder.AppendLine($"Weapon Level: {item.Level}/{EquipmentCatalogDefaults.MaxWeaponLevel}");

        string essenceText = item.HasEssenceSlotUnlocked
            ? item.HasEquippedEssence
                ? $"Socketed: [color={ToHtmlColor(item.Essence!.UiColor)}]{EscapeBbcode(item.Essence.DisplayName)}[/color]"
                : "Socketed: Empty"
            : "Socketed: Locked";
        builder.AppendLine($"Essence Slot: {essenceText}");
        builder.AppendLine($"Modulator Slots: {item.Modulators.Count}/{item.UnlockedModulatorSlots}");

        if (item.Modulators.Count == 0)
        {
            builder.AppendLine("Installed Modulators: None");
            return;
        }

        builder.AppendLine("Installed Modulators:");
        for (int i = 0; i < item.Modulators.Count; i++)
        {
            WeaponModulatorData modulator = item.Modulators[i];
            builder.AppendLine($"  [color={ToHtmlColor(ModulatorLoot.GetTierColor(modulator.PowerTier))}]{EscapeBbcode(modulator.DisplayName)}[/color]");
            builder.AppendLine($"  {EscapeBbcode(ModulatorQualityText.Header(modulator))}");
            builder.AppendLine($"  {EscapeBbcode(ModulatorQualityText.Describe(modulator)).Replace("\n", "\n  ")}");
        }
    }

    private void AppendWeaponProfile(StringBuilder builder, EquipmentItem item, EquipmentItem? comparedItem, bool comparingDifferentItem, bool showAdvancedInfo)
    {
        builder.AppendLine("[b]Weapon Profile[/b]");
        if (item.Shape == null)
        {
            builder.AppendLine("No shape data.");
            return;
        }

        ShapeData shape = item.Shape;
        WeaponStatPreview stats = BuildWeaponStatPreview(item);
        builder.AppendLine($"{EscapeBbcode(shape.AttackName)}  |  {shape.AttackType}");
        if (stats.HasDamage)
        {
            builder.AppendLine($"DMG [b]{FormatWeaponDamageText(stats)}[/b]  |  ATK/s [b]{FormatAttackSpeed(stats)}[/b]  |  DPS [b]{FormatStatNumber(stats.Dps)}[/b]");
            string compactWeaponBonuses = BuildCompactWeaponBonusText(item, stats);
            if (!string.IsNullOrWhiteSpace(compactWeaponBonuses))
            {
                builder.AppendLine($"Weapon bonuses: {compactWeaponBonuses}");
            }

            if (showAdvancedInfo)
            {
                builder.AppendLine($"Profile: {stats.HitCount} hit{(stats.HitCount == 1 ? string.Empty : "s")}  |  {FormatStatNumber(stats.EffectiveCooldown)}s cooldown  |  {stats.ProjectileCount} projectile{(stats.ProjectileCount == 1 ? string.Empty : "s")}");
                AppendWeaponBonusSummary(builder, item, stats);
            }
        }
        else if (shape.Hits > 0)
        {
            builder.AppendLine($"{shape.Hits} hit{(shape.Hits == 1 ? string.Empty : "s")}  |  {Mathf.Max(0.01f, shape.Cooldown):0.##}s cooldown");
        }
        else
        {
            builder.AppendLine("Passive / utility weapon.");
        }

        if (showAdvancedInfo && comparingDifferentItem && comparedItem?.Shape != null)
        {
            WeaponStatPreview comparedStats = BuildWeaponStatPreview(comparedItem);
            builder.AppendLine($"Compared weapon: DMG {FormatStatDelta(stats.DamagePerProjectile - comparedStats.DamagePerProjectile, "0.#")}  |  ATK/s {FormatStatDelta(stats.AttacksPerSecond - comparedStats.AttacksPerSecond, "0.##", "/s")}  |  DPS {FormatStatDelta(stats.Dps - comparedStats.Dps, "0.#")}");
        }

        if (!string.IsNullOrWhiteSpace(shape.SpecialDescription))
        {
            builder.AppendLine(EscapeBbcode(shape.SpecialDescription));
        }
    }

    private static bool ShouldShowModifierProfile(EquipmentItem item, bool showAdvancedInfo)
    {
        if (showAdvancedInfo)
        {
            return true;
        }

        return item.HasEquippedEssence;
    }

    private void AppendModifierProfile(StringBuilder builder, EquipmentItem item)
    {
        builder.AppendLine("[b]Essence / Synergy[/b]");
        if (item.Modifier == null)
        {
            builder.AppendLine(item.HasEssenceSlotUnlocked ? "No essence socketed." : "Essence slot locked.");
            return;
        }

        ModifierData modifier = item.Modifier;
        builder.AppendLine($"[color={ToHtmlColor(modifier.UiColor)}]{EscapeBbcode(modifier.DisplayName)}[/color]: {EscapeBbcode(modifier.PrimaryEffectDescription)}");

        SynergySnapshot currentSnapshot = BuildSynergySnapshot(null, -1);
        AppendSynergyLine(builder, modifier, currentSnapshot);
    }

    private void AppendSynergyLine(StringBuilder builder, ModifierData modifier, SynergySnapshot currentSnapshot)
    {
        if (modifier.Key == ModifierKey.Normal)
        {
            return;
        }

        string activeColor = ToHtmlColor(modifier.UiColor);
        if (modifier.Key == ModifierKey.Prismatic)
        {
            AppendSynergyTierLine(builder, "1x", modifier.PassiveName, modifier.PassiveDescription, currentSnapshot.PrismaticPassiveActive, currentSnapshot.PrismaticPassiveActive, activeColor);
            AppendSynergyTierLine(builder, "2x", modifier.SynergyName, modifier.SynergyDescription, currentSnapshot.PrismaticSynergyActive, currentSnapshot.PrismaticSynergyActive, activeColor);
            bool prismaticSuperActive = currentSnapshot.PrismaticSuperSynergyActive || currentSnapshot.FullSpectrumActive;
            AppendSynergyTierLine(builder, "4x", modifier.SuperSynergyName, modifier.SuperSynergyDescription, prismaticSuperActive, prismaticSuperActive, activeColor);
            return;
        }

        ModifierSynergyState current = currentSnapshot.GetState(modifier.Key);
        AppendSynergyTierLine(builder, "1x", modifier.PassiveName, modifier.PassiveDescription, current.PassiveActive, current.PassiveActive && current.RealCount < 1, activeColor);
        AppendSynergyTierLine(builder, "2x", modifier.SynergyName, modifier.SynergyDescription, current.SynergyActive, current.SynergyActive && current.RealCount < 2, activeColor);
        AppendSynergyTierLine(builder, "4x", modifier.SuperSynergyName, modifier.SuperSynergyDescription, current.SuperSynergyActive, current.SuperSynergyActive && current.RealCount < 4, activeColor);
    }

    private static void AppendSynergyTierLine(StringBuilder builder, string tier, string name, string description, bool active, bool activeByPrismatic, string activeColor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string text = $"{tier} {EscapeBbcode(name)}: {EscapeBbcode(description)}";
        if (active && activeByPrismatic)
        {
            builder.AppendLine($"[rainbow freq=0.8 sat=0.85 val=1.0]{text}[/rainbow]");
            return;
        }

        string color = active ? activeColor : "#6f7a8e";
        builder.AppendLine($"[color={color}]{text}[/color]");
    }

    private void AppendSubstats(StringBuilder builder, EquipmentItem item, EquipmentItem? comparedItem, bool comparingDifferentItem)
    {
        builder.AppendLine("[b]Substats[/b]");
        if (!HasAnySubstat(item,
            SubstatType.DamagePercent,
            SubstatType.HealthPercent,
            SubstatType.DodgeChance,
            SubstatType.LifeStealPercent,
            SubstatType.HealthRegen,
            SubstatType.CritDamage,
            SubstatType.CritChance,
            SubstatType.MovementSpeed,
            SubstatType.Range,
            SubstatType.Luck,
            SubstatType.Size,
            SubstatType.AttackSpeed,
            SubstatType.Ricochet,
            SubstatType.BounceTimes,
            SubstatType.ProjectileCount))
        {
            builder.AppendLine("None");
            return;
        }

        AppendSubstatGroup(builder, "Offense", item, comparedItem, comparingDifferentItem, SubstatType.DamagePercent, SubstatType.AttackSpeed, SubstatType.CritChance, SubstatType.CritDamage);
        AppendSubstatGroup(builder, "Defense", item, comparedItem, comparingDifferentItem, SubstatType.HealthPercent, SubstatType.DodgeChance, SubstatType.LifeStealPercent, SubstatType.HealthRegen);
        AppendSubstatGroup(builder, "Projectile", item, comparedItem, comparingDifferentItem, SubstatType.Size, SubstatType.Range, SubstatType.Ricochet, SubstatType.BounceTimes, SubstatType.ProjectileCount);
        AppendSubstatGroup(builder, "Utility", item, comparedItem, comparingDifferentItem, SubstatType.MovementSpeed, SubstatType.Luck);
    }

    private void AppendSubstatGroup(StringBuilder builder, string title, EquipmentItem item, EquipmentItem? comparedItem, bool comparingDifferentItem, params SubstatType[] types)
    {
        List<SubstatRoll> rolls = new();
        for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            SubstatRoll? roll = FindSubstat(item, types[typeIndex]);
            if (roll != null)
            {
                rolls.Add(roll);
            }
        }

        if (rolls.Count == 0)
        {
            return;
        }

        builder.AppendLine($"[color=#9fb5d1]{title}[/color]");
        for (int rollIndex = 0; rollIndex < rolls.Count; rollIndex++)
        {
            SubstatRoll roll = rolls[rollIndex];
            float comparedValue = comparingDifferentItem && comparedItem != null ? FindSubstat(comparedItem, roll.Type)?.Value ?? 0f : 0f;
            string delta = comparingDifferentItem ? $"  {FormatDelta(roll.Value - comparedValue, SubstatRoll.UsesFlatDisplay(roll.Type))}" : string.Empty;
            builder.AppendLine($"  {BuildSubstatRollDisplay(roll, item.Rarity)}{delta}");
        }
    }

    private static string BuildItemCardText(EquipmentItem item)
    {
        WeaponStatPreview stats = BuildWeaponStatPreview(item);
        if (!stats.HasShape)
        {
            return BuildPickerLabel(item, true);
        }

        if (!stats.HasDamage)
        {
            return $"{BuildPickerLabel(item, true)}\nPassive / Utility\nDPS --";
        }

        return $"{BuildPickerLabel(item, true)}\nDMG {FormatWeaponDamageText(stats)}  {FormatAttackSpeed(stats)}\nDPS {FormatStatNumber(stats.Dps)}";
    }

    private string BuildItemCardTooltip(EquipmentItem item)
    {
        int equippedSlot = FindEquippedSlot(item);
        string equippedText = equippedSlot >= 0 ? $"Equipped: {GetSlotShortLabel(equippedSlot)}" : "Not equipped";
        WeaponStatPreview stats = BuildWeaponStatPreview(item);
        int effectiveHealth = StatGenerator.GetEffectiveHealth(item);
        int effectiveDamage = StatGenerator.GetEffectiveDamage(item);
        const string actionHint = "Drag to a loadout slot or right-click for equip actions.";
        string socketLine = $"Essence {(item.HasEssenceSlotUnlocked ? (item.HasEquippedEssence ? item.Essence!.DisplayName : "Empty") : "Locked")} | Modulators {item.Modulators.Count}/{item.UnlockedModulatorSlots}";
        if (!stats.HasShape)
        {
            return $"{BuildPickerLabel(item, false)}\n{equippedText}\nLevel {item.Level} | HP {effectiveHealth} | Bullet DMG {effectiveDamage}\n{socketLine}\n{actionHint}";
        }

        string compactWeaponBonuses = BuildCompactWeaponBonusText(item, stats);
        string weaponLine = stats.HasDamage
            ? $"Weapon DMG {FormatWeaponDamageText(stats)} | ATK {FormatAttackSpeed(stats)} | DPS {FormatStatNumber(stats.Dps)}"
            : "Passive / utility weapon";
        string weaponBonusLine = !string.IsNullOrWhiteSpace(compactWeaponBonuses)
            ? $"\nWeapon bonuses: {compactWeaponBonuses}"
            : string.Empty;
        return $"{BuildPickerLabel(item, false)}\n{equippedText}\nLevel {item.Level} | HP {effectiveHealth} | Bullet DMG {effectiveDamage}\n{socketLine}\n{weaponLine}{weaponBonusLine}\n{actionHint}";
    }

    private string BuildEquippedSlotTooltip(int slotIndex, EquipmentItem? item)
    {
        string slotLabel = GetSlotShortLabel(slotIndex);
        if (item == null)
        {
            return $"{slotLabel}\nDrop an item here or right-click for slot actions.";
        }

        return $"{slotLabel}\n{BuildPickerLabel(item, false)}\nDrag to move this item, drop another item to replace it, or right-click for actions.";
    }

    private static WeaponStatPreview BuildWeaponStatPreview(EquipmentItem item)
    {
        if (item.Shape == null)
        {
            return default;
        }

        ShapeData shape = item.Shape;
        WeaponKind weaponKind = MapPreviewShapeToWeapon(shape.Key);
        RarityTier weaponRarity = MapPreviewRarity(item.Rarity?.Key);
        float damagePercent = Mathf.Max(0f, SumSubstatValue(item, SubstatType.DamagePercent));
        float attackSpeedPercent = Mathf.Max(0f, SumSubstatValue(item, SubstatType.AttackSpeed));
        float critChancePercent = Mathf.Max(0f, SumSubstatValue(item, SubstatType.CritChance));
        float critDamagePercent = Mathf.Max(0f, SumSubstatValue(item, SubstatType.CritDamage));
        float rangePercent = Mathf.Max(0f, SumSubstatValue(item, SubstatType.Range));
        float sizePercent = Mathf.Max(0f, SumSubstatValue(item, SubstatType.Size));
        float ricochetCount = Mathf.Max(0f, SumSubstatValue(item, SubstatType.Ricochet));
        float bounceTimesCount = Mathf.Max(0f, SumSubstatValue(item, SubstatType.BounceTimes));
        float projectileBonus = Mathf.Max(0f, SumSubstatValue(item, SubstatType.ProjectileCount));
        int projectileCount = Mathf.Max(1, 1 + Mathf.RoundToInt(projectileBonus));
        int hitCount = Mathf.Max(1, shape.Hits);
        float scaledMainDamage = Mathf.Max(0, StatGenerator.GetEffectiveDamage(item)) * GetPreviewWeaponDamageMultiplier(weaponKind, weaponRarity);
        float damagePerProjectile = scaledMainDamage;
        float critExpectedMultiplier = GetPreviewExpectedCritMultiplier(critChancePercent, critDamagePercent);

        float baseCooldown = Mathf.Max(0.01f, shape.Cooldown);
        float attackSpeedMultiplier = (1f + attackSpeedPercent / 100f) * GetPreviewWeaponFireMultiplier(weaponKind, weaponRarity);
        float attacksPerSecond = damagePerProjectile > 0f && shape.Cooldown < 900f
            ? attackSpeedMultiplier / baseCooldown
            : 0f;
        float effectiveCooldown = attacksPerSecond > 0f ? 1f / attacksPerSecond : 0f;
        float damagePerAttack = damagePerProjectile * hitCount * projectileCount * critExpectedMultiplier;

        return new WeaponStatPreview
        {
            HasShape = true,
            HasDamage = damagePerProjectile > 0f && attacksPerSecond > 0f,
            HitCount = hitCount,
            ProjectileCount = projectileCount,
            DamagePercent = damagePercent,
            AttackSpeedPercent = attackSpeedPercent,
            CritChancePercent = critChancePercent,
            CritDamagePercent = critDamagePercent,
            RangePercent = rangePercent,
            SizePercent = sizePercent,
            RicochetCount = ricochetCount,
            BounceTimesCount = bounceTimesCount,
            ProjectileBonus = projectileBonus,
            DamagePerProjectile = damagePerProjectile,
            DamagePerAttack = damagePerAttack,
            AttacksPerSecond = attacksPerSecond,
            EffectiveCooldown = effectiveCooldown,
            Dps = damagePerAttack * attacksPerSecond,
        };
    }

    private static float GetPreviewWeaponFireMultiplier(WeaponKind kind, RarityTier rarity)
    {
        return GetPreviewWeaponBaseFireProfileMultiplier(kind) * GetPreviewWeaponFireTierMultiplier(GetPreviewRarityMultiplier(rarity));
    }

    private static float GetPreviewWeaponDamageMultiplier(WeaponKind kind, RarityTier rarity)
    {
        return GetPreviewWeaponBaseDamageProfileMultiplier(kind) * GetPreviewWeaponFireTierMultiplier(GetPreviewRarityMultiplier(rarity));
    }

    private static float GetPreviewExpectedCritMultiplier(float critChancePercent, float critDamagePercent)
    {
        float critChance = Mathf.Clamp(critChancePercent / 100f, 0f, 1f);
        float critBonus = BasePreviewCriticalDamagePercent / 100f + Mathf.Max(0f, critDamagePercent) / 100f;
        return 1f + critChance * critBonus;
    }

    private static float GetPreviewWeaponBaseFireProfileMultiplier(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Circle => 0.72f,
            WeaponKind.Triangle => 0.65f,
            WeaponKind.Hexagon => 0.55f,
            WeaponKind.Sphere => 0.50f,
            WeaponKind.Cube => 0.65f,
            WeaponKind.Pyramid => 0.90f,
            WeaponKind.Cone => 0.78f,
            WeaponKind.Tesseract => 0.48f,
            WeaponKind.Hyperphere => 0.55f,
            WeaponKind.Pentachoron => 0.68f,
            WeaponKind.Hypercone => 0.60f,
            WeaponKind.ImpossibleCube => 0.42f,
            WeaponKind.Mobius => 0.78f,
            WeaponKind.Klein => 0.50f,
            WeaponKind.BoysSurface => 0.58f,
            _ => 1f,
        };
    }

    private static float GetPreviewWeaponBaseDamageProfileMultiplier(WeaponKind kind)
    {
        return kind switch
        {
            WeaponKind.Circle => 1.12f,
            WeaponKind.Triangle => 1.55f,
            WeaponKind.Hexagon => 1.60f,
            WeaponKind.Sphere => 1.85f,
            WeaponKind.Cube => 1.25f,
            WeaponKind.Pyramid => 1.10f,
            WeaponKind.Cone => 1.35f,
            WeaponKind.Tesseract => 2.00f,
            WeaponKind.Hyperphere => 1.75f,
            WeaponKind.Pentachoron => 1.45f,
            WeaponKind.Hypercone => 1.65f,
            WeaponKind.ImpossibleCube => 2.30f,
            WeaponKind.Mobius => 1.05f,
            WeaponKind.Klein => 1.90f,
            WeaponKind.BoysSurface => 1.80f,
            _ => 1f,
        };
    }

    private static float GetPreviewWeaponFireTierMultiplier(float weaponTier)
    {
        return 1f + (weaponTier - 1f) * 0.52f;
    }

    private static float GetPreviewRarityMultiplier(RarityTier rarity)
    {
        return rarity switch
        {
            RarityTier.Uncommon => 1.16f,
            RarityTier.Rare => 1.35f,
            RarityTier.Epic => 1.60f,
            RarityTier.Legendary => 1.95f,
            RarityTier.Mythic => 2.35f,
            _ => 1f,
        };
    }

    private static WeaponKind MapPreviewShapeToWeapon(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "circle" => WeaponKind.Circle,
            "triangle" => WeaponKind.Triangle,
            "hexagon" => WeaponKind.Hexagon,
            "sphere" => WeaponKind.Sphere,
            "cube" => WeaponKind.Cube,
            "pyramid" => WeaponKind.Pyramid,
            "cone" => WeaponKind.Cone,
            "tesseract" => WeaponKind.Tesseract,
            "hyperphere" => WeaponKind.Hyperphere,
            "hypersphere" => WeaponKind.Hyperphere,
            "pentachoron" => WeaponKind.Pentachoron,
            "hypercone" => WeaponKind.Hypercone,
            "impossiblecube" => WeaponKind.ImpossibleCube,
            "impossible_cube" => WeaponKind.ImpossibleCube,
            "mobius" => WeaponKind.Mobius,
            "klein" => WeaponKind.Klein,
            "boys" => WeaponKind.BoysSurface,
            "boyssurface" => WeaponKind.BoysSurface,
            "boys_surface" => WeaponKind.BoysSurface,
            _ => WeaponKind.Square,
        };
    }

    private static RarityTier MapPreviewRarity(string? key)
    {
        return EquipmentCatalogDefaults.NormalizeRarityKey(key) switch
        {
            "uncommon" => RarityTier.Uncommon,
            "rare" => RarityTier.Rare,
            "legendary" => RarityTier.Epic,
            "mythic" => RarityTier.Legendary,
            "divine" => RarityTier.Mythic,
            _ => RarityTier.Common,
        };
    }

    private static string BuildCompactWeaponBonusText(EquipmentItem item, WeaponStatPreview stats)
    {
        List<string> bonuses = new();
        if (stats.CritChancePercent > 0f)
        {
            bonuses.Add($"CRIT +{stats.CritChancePercent:0.##}%");
        }

        if (stats.CritDamagePercent > 0f)
        {
            bonuses.Add($"CRIT DMG +{stats.CritDamagePercent:0.##}%");
        }

        if (stats.RangePercent > 0f)
        {
            bonuses.Add($"Range +{stats.RangePercent:0.##}%");
        }

        if (stats.SizePercent > 0f)
        {
            bonuses.Add($"Size +{stats.SizePercent:0.##}%");
        }

        if (stats.RicochetCount > 0f)
        {
            bonuses.Add($"Ricochet +{stats.RicochetCount:0.#}");
        }

        if (stats.BounceTimesCount > 0f)
        {
            bonuses.Add($"Bounces +{stats.BounceTimesCount:0.#}");
        }

        float volitileStage = GetVolitilePreviewStage(item);
        if (volitileStage > 0)
        {
            bonuses.Add($"Volitile R{volitileStage:0.#}");
        }

        return bonuses.Count > 0 ? string.Join("  |  ", bonuses) : string.Empty;
    }

    private static void AppendWeaponBonusSummary(StringBuilder builder, EquipmentItem item, WeaponStatPreview stats)
    {
        List<string> offenseBonuses = new();
        if (stats.DamagePercent > 0f)
        {
            offenseBonuses.Add($"DMG +{stats.DamagePercent:0.##}%");
        }

        if (stats.AttackSpeedPercent > 0f)
        {
            offenseBonuses.Add($"ATK SPD +{stats.AttackSpeedPercent:0.##}%");
        }

        if (stats.CritChancePercent > 0f)
        {
            offenseBonuses.Add($"CRIT +{stats.CritChancePercent:0.##}%");
        }

        if (stats.CritDamagePercent > 0f)
        {
            offenseBonuses.Add($"CRIT DMG +{stats.CritDamagePercent:0.##}%");
        }

        float volitileStage = GetVolitilePreviewStage(item);
        if (volitileStage > 0)
        {
            offenseBonuses.Add($"Volitile R{volitileStage:0.#} @ 50% splash");
        }

        if (offenseBonuses.Count > 0)
        {
            builder.AppendLine($"Live offense: {string.Join("  |  ", offenseBonuses)}");
        }

        List<string> projectileBonuses = new();
        if (stats.ProjectileBonus > 0f)
        {
            projectileBonuses.Add($"Projectiles +{stats.ProjectileBonus:0.#}");
        }

        if (stats.RangePercent > 0f)
        {
            projectileBonuses.Add($"Range +{stats.RangePercent:0.##}%");
        }

        if (stats.SizePercent > 0f)
        {
            projectileBonuses.Add($"Size +{stats.SizePercent:0.##}%");
        }

        if (stats.RicochetCount > 0f)
        {
            projectileBonuses.Add($"Ricochet +{stats.RicochetCount:0.#}");
        }

        if (stats.BounceTimesCount > 0f)
        {
            projectileBonuses.Add($"Bounces +{stats.BounceTimesCount:0.#}");
        }

        if (projectileBonuses.Count > 0)
        {
            builder.AppendLine($"Live projectile: {string.Join("  |  ", projectileBonuses)}");
        }
    }

    private static float SumSubstatValue(EquipmentItem item, SubstatType type)
    {
        return StatGenerator.GetTotalSubstatValue(item, type);
    }

    private static float GetVolitilePreviewStage(EquipmentItem item)
    {
        float highestStrength = 0;
        float totalStrength = 0;
        for (int modulatorIndex = 0; modulatorIndex < item.Modulators.Count; modulatorIndex++)
        {
            WeaponModulatorData? modulator = item.Modulators[modulatorIndex];
            if (modulator == null || !string.Equals(modulator.NormalizedKey, "volitile", System.StringComparison.Ordinal))
            {
                continue;
            }

            totalStrength += modulator.QualityStrength;
            highestStrength = Mathf.Max(highestStrength, modulator.QualityStrength);
        }

        if (totalStrength <= 0)
        {
            return 0;
        }

        return Mathf.Clamp(highestStrength + totalStrength, 1f, 4f);
    }

    private static string FormatWeaponDamageText(WeaponStatPreview stats)
    {
        string damage = FormatStatNumber(stats.DamagePerProjectile);
        return stats.ProjectileCount > 1 ? $"{damage} x {stats.ProjectileCount}" : damage;
    }

    private static string FormatAttackSpeed(WeaponStatPreview stats)
    {
        return stats.AttacksPerSecond > 0f ? $"{FormatStatNumber(stats.AttacksPerSecond)}/s" : "--/s";
    }

    private static string FormatStatNumber(float value)
    {
        float abs = Mathf.Abs(value);
        if (abs >= 100f)
        {
            return value.ToString("0.#");
        }

        return value.ToString("0.##");
    }

    private static string FormatStatDelta(float value, string format, string suffix = "")
    {
        if (Mathf.Abs(value) < 0.001f)
        {
            return $"[color=#9aa8bc]+0{suffix}[/color]";
        }

        string color = value > 0f ? "#62e6a8" : "#ff6b78";
        string sign = value > 0f ? "+" : string.Empty;
        return $"[color={color}]{sign}{value.ToString(format)}{suffix}[/color]";
    }

    private static string BuildPickerLabel(EquipmentItem item, bool multiline)
    {
        string rarity = item.Rarity?.DisplayName ?? "Unknown";
        string shape = item.Shape?.DisplayName ?? "Unknown";
        string modifier = ShouldShowModifierInPicker(item.Modifier) ? $"{item.Modifier!.DisplayName} " : string.Empty;

        return multiline ? $"{modifier}{rarity}\n{shape}" : $"{modifier}{rarity} {shape}";
    }

    private static bool ShouldShowModifierInPicker(ModifierData? modifier)
    {
        return modifier != null && modifier.Key != ModifierKey.Normal;
    }

    private static string FormatEquippedStatus(int equippedSlot)
    {
        return equippedSlot >= 0 ? $"Equipped: {GetSlotShortLabel(equippedSlot)}" : "Not equipped";
    }

    private static string FormatDelta(float value, bool flatDisplay)
    {
        if (Mathf.Abs(value) < 0.001f)
        {
            return "[color=#9aa8bc]+0[/color]";
        }

        string color = value > 0f ? "#62e6a8" : "#ff6b78";
        string sign = value > 0f ? "+" : string.Empty;
        string suffix = flatDisplay ? string.Empty : "%";
        string format = flatDisplay ? "0.#" : "0.##";
        return $"[color={color}]{sign}{value.ToString(format)}{suffix}[/color]";
    }

    private static string BuildSubstatRollDisplay(SubstatRoll roll, RarityData? rarity)
    {
        (string grade, string color) = GetSubstatRollGrade(roll, rarity);
        string label = EscapeBbcode(SubstatRoll.ToDisplayName(roll.Type));
        bool flatDisplay = SubstatRoll.UsesFlatDisplay(roll.Type);
        string valueText = flatDisplay
            ? $"+{roll.Value:0.0}"
            : $"+{roll.Value:0.00}%";

        return $"[color={color}][b]{grade}:[/b] {label} {valueText}[/color]";
    }

    private static (string Grade, string Color) GetSubstatRollGrade(SubstatRoll roll, RarityData? rarity)
    {
        if (!StatGenerator.TryGetSubstatRange(roll.Type, rarity, out _, out float max) || max <= 0f)
        {
            return ("?", "#b8c4d9");
        }

        float quality = Mathf.Clamp(roll.Value / max, 0f, 1f);

        if (roll.Value >= max - 0.001f)
        {
            return ("S", "#ffd86a");
        }

        if (quality >= 0.90f)
        {
            return ("A", "#62e6a8");
        }

        if (quality >= 0.75f)
        {
            return ("B", "#7fc8ff");
        }

        if (quality >= 0.50f)
        {
            return ("C", "#b8c4d9");
        }

        if (quality >= 0.10f)
        {
            return ("D", "#d9aa6a");
        }

        return ("F", "#ff6b78");
    }

    private static string EscapeBbcode(string text)
    {
        return text.Replace("[", "(").Replace("]", ")");
    }

    private static string ToHtmlColor(Color color)
    {
        int red = Mathf.Clamp(Mathf.RoundToInt(color.R * 255f), 0, 255);
        int green = Mathf.Clamp(Mathf.RoundToInt(color.G * 255f), 0, 255);
        int blue = Mathf.Clamp(Mathf.RoundToInt(color.B * 255f), 0, 255);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }
}
