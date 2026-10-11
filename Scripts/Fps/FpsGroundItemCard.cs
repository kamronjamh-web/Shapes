using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;

namespace SuperGatchaRebuild.Fps;

/// <summary>A compact aimed-item card. Values describe this item's own bonuses.</summary>
public partial class FpsGroundItemCard : PanelContainer
{
    private Label _header = null!;
    private Label _name = null!;
    private Label _details = null!;
    private Label _stats = null!;
    private ScrollContainer _statsScroll = null!;
    private Label _action = null!;
    private string _signature = "";
    private bool _built;

    public override void _Ready() => Build();

    public void ShowItem(EquipmentItem? weapon, WeaponModulatorData? part, string actionHint)
    {
        if (weapon == null && part == null) { HideItem(); return; }
        Build();
        string details;
        string stats;
        string header;
        string name;
        if (weapon != null)
        {
            header = $"{weapon.Rarity?.DisplayName ?? "Common"} WEAPON";
            name = weapon.Shape?.DisplayName ?? "Weapon";
            float cadence = Mathf.Max(.1f, 1f + StatGenerator.GetTotalSubstatValue(weapon, SubstatType.AttackSpeed) / 100f);
            float interval = Mathf.Max(.05f, FpsVerticalSlice.GetWeaponRuntimeCooldown(weapon) / cadence);
            details = $"Level {weapon.Level}  ·  {weapon.Modulators?.Count ?? 0}/{weapon.UnlockedModulatorSlots} sockets\n" +
                $"Base damage {StatGenerator.GetEffectiveDamage(weapon)}  ·  Interval {Number(interval)} s\n" +
                $"Item health {StatGenerator.GetEffectiveHealth(weapon)}";
            if (weapon.HasEquippedEssence) details += $"  ·  {weapon.Essence!.DisplayName}";
            List<string> rows = new();
            foreach (SubstatType type in Enum.GetValues<SubstatType>())
            {
                if (type == SubstatType.None) continue;
                float total = StatGenerator.GetTotalSubstatValue(weapon, type);
                if (Mathf.Abs(total) < .0001f) continue;
                rows.Add(StatRow(type, total));
            }
            stats = rows.Count > 0 ? "Summed item bonuses\n" + string.Join("\n", rows) : "No substat bonuses";
        }
        else
        {
            header = $"TIER {(int)part!.PowerTier + 1} · {ModulatorQualityText.QualityName(part.Tier)} MODULATOR";
            name = string.IsNullOrWhiteSpace(part.DisplayName) ? part.BaseDisplayName : part.DisplayName;
            details = $"{ModulatorQualityText.Header(part)}\n{WeaponModulatorData.GetCategoryDisplayName(part.Category)}";
            stats = DescribeModulator(part);
        }
        string signature = string.Join("\u001f", header, name, details, stats, actionHint);
        if (_signature != signature)
        {
            _signature = signature;
            _header.Text = header; _name.Text = name; _details.Text = details;
            _stats.Text = stats; _action.Text = actionHint;
            int bodyLines = 0;
            foreach (string line in stats.Split('\n')) bodyLines += Math.Max(1, (line.Length + 43) / 44);
            float screenHeight = IsInsideTree() ? GetViewportRect().Size.Y : 720f;
            _statsScroll.CustomMinimumSize = new Vector2(0, Math.Min(Math.Max(80f, screenHeight - 300f), Math.Min(300f, bodyLines * 17f)));
            Color color = FpsGroundItemVisual.GetRarityColor(weapon, part);
            _header.AddThemeColorOverride("font_color", color);
            AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(.035f, .047f, .065f, .94f), BorderColor = new Color(color, .85f),
                BorderWidthLeft = 3, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
                ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 11, ContentMarginBottom = 11,
            });
            ResetSize();
        }
        Visible = true;
    }

    public void HideItem() => Visible = false;

    private void Build()
    {
        if (_built) return;
        _built = true;
        Name = "GroundItemCard";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorLeft = .5f; AnchorRight = .5f; AnchorTop = .5f; AnchorBottom = .5f;
        OffsetLeft = 42; OffsetRight = 402; OffsetTop = -200; OffsetBottom = -200;
        GrowHorizontal = GrowDirection.End; GrowVertical = GrowDirection.End;
        CustomMinimumSize = new Vector2(360, 0);
        VBoxContainer rows = new() { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 5);
        AddChild(rows);
        _header = AddLabel(rows, 13); _name = AddLabel(rows, 20); _details = AddLabel(rows, 14);
        rows.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore });
        _statsScroll = new ScrollContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        rows.AddChild(_statsScroll);
        _stats = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _stats.AddThemeFontSizeOverride("font_size", 12);
        _stats.AddThemeColorOverride("font_color", new Color(.9f, .93f, .97f));
        _stats.AddThemeConstantOverride("line_spacing", -2);
        _statsScroll.AddChild(_stats);
        _action = AddLabel(rows, 15);
        _action.AddThemeColorOverride("font_color", new Color(.98f, .87f, .56f));
        Visible = false;
    }
    private static Label AddLabel(VBoxContainer parent, int fontSize)
    {
        Label label = new() { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(.9f, .93f, .97f));
        parent.AddChild(label);
        return label;
    }
    private static string StatRow(SubstatType type, float total)
        => $"{SubstatRoll.ToDisplayName(type)}  {(total >= 0 ? "+" : "")}{Number(total)}{(SubstatRoll.UsesFlatDisplay(type) ? "" : "%")}";

    private static string DescribeModulator(WeaponModulatorData part)
    {
        string description = ModulatorQualityText.Describe(part);
        if (part.IsRetired) return description;
        // Describe's dictionary keeps the last effect with a matching label. Aggregate
        // passive substats through the combat calculation so duplicate rolls all count.
        HashSet<string> replacedLabels = new(StringComparer.Ordinal);
        foreach (WeaponModulatorEffectData effect in part.GetResolvedEffects())
        {
            if (effect.Trigger != ModulatorEffectTrigger.Passive || !effect.TryConvertToSubstatRoll(out SubstatRoll roll)) continue;
            replacedLabels.Add(roll.Type switch
            {
                SubstatType.DamagePercent => "Damage", SubstatType.HealthPercent => "Health",
                SubstatType.DodgeChance => "Dodge chance", SubstatType.LifeStealPercent => "Life steal",
                SubstatType.HealthRegen => "Health regeneration", SubstatType.CritDamage => "Critical damage",
                SubstatType.CritChance => "Critical chance", SubstatType.MovementSpeed => "Movement speed",
                SubstatType.Range => "Range", SubstatType.Size => "Area", SubstatType.AttackSpeed => "Attack rate",
                SubstatType.Ricochet => "Hit ricochets", SubstatType.BounceTimes => "Wall bounces",
                SubstatType.ProjectileCount => "Extra output", _ => "Luck",
            });
        }
        if (replacedLabels.Count == 0) return description;
        List<string> lines = new();
        foreach (string line in description.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon >= 0 && replacedLabels.Contains(line[..colon])) continue;
            lines.Add(line);
        }
        EquipmentItem ownEffects = new() { Modulators = new List<WeaponModulatorData> { part } };
        foreach (SubstatType type in Enum.GetValues<SubstatType>())
        {
            if (type == SubstatType.None) continue;
            float total = StatGenerator.GetTotalSubstatValue(ownEffects, type);
            if (Mathf.Abs(total) >= .0001f) lines.Add(StatRow(type, total));
        }
        return string.Join("\n", lines);
    }
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
