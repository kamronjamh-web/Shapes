using Godot;
using System.Collections.Generic;

namespace SuperGatchaRebuild.Equipment.Data;

[GlobalClass]
public partial class WeaponModulatorDefinition : Resource
{
    [Export] public string Key { get; set; } = string.Empty;
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;
    [Export] public ModulatorPowerTier PowerTier { get; set; } = ModulatorPowerTier.Tier1;
    [Export] public WeaponModulatorCategory Category { get; set; } = WeaponModulatorCategory.None;
    [Export] public Godot.Collections.Array<string> Tags { get; set; } = new();
    [Export] public Godot.Collections.Array<string> AllowedWeaponTags { get; set; } = new();
    [Export] public Godot.Collections.Array<string> BlockedWeaponTags { get; set; } = new();
    [Export] public bool SupportsTier { get; set; }
    [Export] public float Weight { get; set; } = 1f;
    [Export] public Color UiColor { get; set; } = Colors.White;
    [Export] public Godot.Collections.Array<ModulatorSubstatDefinition> Substats { get; set; } = new();
    [Export] public Godot.Collections.Array<WeaponModulatorEffectDefinition> Effects { get; set; } = new();

    public WeaponModulatorData ToData()
    {
        List<SubstatRoll> rolls = new(Substats.Count);
        for (int i = 0; i < Substats.Count; i++)
        {
            ModulatorSubstatDefinition? definition = Substats[i];
            if (definition == null || definition.Type == SubstatType.None)
            {
                continue;
            }

            rolls.Add(definition.ToSubstatRoll());
        }

        List<string> tags = new(Tags.Count);
        for (int i = 0; i < Tags.Count; i++)
        {
            string tag = Tags[i];
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }

        List<string> allowedWeaponTags = new(AllowedWeaponTags.Count);
        for (int i = 0; i < AllowedWeaponTags.Count; i++)
        {
            string tag = AllowedWeaponTags[i];
            if (!string.IsNullOrWhiteSpace(tag))
            {
                allowedWeaponTags.Add(tag);
            }
        }

        List<string> blockedWeaponTags = new(BlockedWeaponTags.Count);
        for (int i = 0; i < BlockedWeaponTags.Count; i++)
        {
            string tag = BlockedWeaponTags[i];
            if (!string.IsNullOrWhiteSpace(tag))
            {
                blockedWeaponTags.Add(tag);
            }
        }

        List<WeaponModulatorEffectData> effects = new();
        for (int i = 0; i < Effects.Count; i++)
        {
            WeaponModulatorEffectDefinition? definition = Effects[i];
            if (definition != null && definition.Type != ModulatorEffectType.None)
            {
                effects.Add(definition.ToData());
            }
        }

        if (effects.Count == 0 && rolls.Count > 0)
        {
            effects.AddRange(WeaponModulatorEffectData.FromSubstats(rolls));
        }

        WeaponModulatorData modulator = new()
        {
            Key = Key,
            BaseDisplayName = DisplayName,
            DisplayName = DisplayName,
            BaseDescription = Description,
            Description = Description,
            PowerTier = PowerTier,
            Category = Category,
            Tags = tags,
            AllowedWeaponTags = allowedWeaponTags,
            BlockedWeaponTags = blockedWeaponTags,
            SupportsTier = SupportsTier,
            Weight = Weight,
            UiColor = UiColor,
            Substats = rolls,
            Effects = effects,
            Tier = WeaponModulatorTier.Baseline,
        };

        modulator.RefreshPresentation();
        return modulator;
    }
}