using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using SuperGatchaRebuild.Combat;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Validation;

// Catalog-only contracts run cheaply; real attacks and inspection use their existing suites.
public partial class ValidateModulatorQuality : Node
{
    private int _checks;
    private static readonly WeaponModulatorTier[] Qualities =
        { WeaponModulatorTier.Flawed, WeaponModulatorTier.Baseline, WeaponModulatorTier.Enhanced, WeaponModulatorTier.Flawless };
    private static readonly float[] Strengths = { .5f, 1f, 1.5f, 2f };

    public override void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/.tmp/modulator-quality-validation/"), "Quality validation uses isolated saves");
            ValidateCatalog();
            ValidateConcreteStats();
            ValidatePersistence();
            ModulatorEconomyChecks.Run(Check);
            ValidatePresentation();
            ValidateLegacyAndStrongestProcs();
            GD.Print($"Modulator quality validation passed: {_checks} checks.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void ValidateCatalog()
    {
        List<WeaponModulatorData> baseline = ModulatorCatalog.CreateAll();
        Check(baseline.Count == 100 && baseline.Select(part => part.NormalizedKey).Distinct().Count() == 100,
            "The unchanged stock catalog has one hundred distinct parts");
        int variants = 0;
        foreach (WeaponModulatorData stock in baseline)
        foreach (WeaponModulatorTier quality in Qualities)
        {
            WeaponModulatorData part = ModulatorCatalog.CreateByKey(stock.Key, quality);
            Check(part.Tier == quality && part.UsesTierPresentation && part.PowerTier == stock.PowerTier,
                $"{stock.Key}/{quality}: quality is available independently of power tier");
            string rawBefore = Raw(part);
            string resolved = JsonSerializer.Serialize(part.GetResolvedEffects());
            bool stable = true;
            for (int read = 0; read < 4; read++)
            {
                part.RefreshPresentation();
                stable &= Raw(part) == rawBefore && JsonSerializer.Serialize(part.GetResolvedEffects()) == resolved;
            }
            Check(stable, $"{stock.Key}/{quality}: repeated reads and presentation preserve raw and effective values");
            Check(part.GetResolvedEffects().Select(effect => (effect.Type, effect.Trigger, effect.Key))
                .SequenceEqual(stock.GetResolvedEffects().Select(effect => (effect.Type, effect.Trigger, effect.Key))),
                $"{stock.Key}/{quality}: quality preserves effect identity, trigger and transform keys");
            if (quality == WeaponModulatorTier.Flawless)
                Check(part.DisplayName == "Flawless " + stock.BaseDisplayName,
                    $"{stock.Key}: Flawless presentation preserves the complete base name");
            if (quality == WeaponModulatorTier.Baseline)
                Check(JsonSerializer.Serialize(part.GetResolvedEffects()) == JsonSerializer.Serialize(stock.GetResolvedEffects()),
                    $"{stock.Key}: baseline effects remain unchanged");
            // Mutating one returned catalog instance must never poison other qualities or the cache.
            if (part.Effects.Count > 0) part.Effects[0].Magnitude += 987f;
            Check(Raw(ModulatorCatalog.CreateByKey(stock.Key, quality)) == rawBefore,
                $"{stock.Key}/{quality}: catalog variants deeply own their raw effect data");
            variants++;
        }
        Check(variants == 400, "All four hundred catalog quality variants were checked");
        Check(WeaponModulatorData.TryParseTierKey("flawed", out WeaponModulatorTier legacy) && legacy == WeaponModulatorTier.Flawed &&
            WeaponModulatorData.TryParseTierKey("broken", out WeaponModulatorTier broken) && broken == legacy &&
            WeaponModulatorData.FormatDisplayName("Flawed Sharpened Form", legacy) == "Broken Sharpened Form",
            "Legacy Flawed data resolves to Broken without a repeated name prefix");
    }

    private void ValidateConcreteStats()
    {
        foreach ((WeaponModulatorTier quality, float strength) in Qualities.Zip(Strengths))
        {
            Check(Near(ModulatorQuality.GetStrength(quality), strength), $"{quality}: uses the approved quality strength");
            EquipmentItem weapon = Weapon("square", "sharpened_form", quality);
            Check(Near(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.DamagePercent), 12f * strength),
                $"{quality}: scales the twelve-percent damage bonus rather than the entire weapon");
            Check(weapon.Damage == 100 && weapon.Health == 100,
                $"{quality}: reading modulator bonuses leaves the item's base damage and health intact");
            weapon = Weapon("square", "accelerated_flight", quality);
            Check(Near(WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon), 1f + .2f * strength),
                $"{quality}: projectile delivery scales around its neutral multiplier");
            weapon = Weapon("circle", "return_arc", quality);
            Check(WeaponModulatorSharedRuntime.HasWeaponItemPassiveFlag(weapon, ModulatorEffectType.NewSawReturn),
                $"{quality}: returning-saw behavior survives every quality");
            Check(Near(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.DamagePercent), -10f),
                $"{quality}: Return Arc retains its fixed ten-percent damage tradeoff");
            weapon = Weapon("circle", "heavy_payload", quality);
            Check(Near(WeaponModulatorSharedRuntime.GetWeaponItemProjectileSpeedMultiplier(weapon), .85f) &&
                Near(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.Size), 35f * strength),
                $"{quality}: Heavy Payload scales its benefit while preserving its delivery penalty");
            weapon = Weapon("square", "quickened_core", quality);
            Check(Near(StatGenerator.GetTotalSubstatValue(weapon, SubstatType.AttackSpeed), (1f / .9f - 1f) * 100f * strength),
                $"{quality}: cadence scales its speed benefit rather than multiplying cooldown");
            weapon = Weapon("cube", "tight_orbit", quality);
            Check(Near(WeaponModulatorSharedRuntime.GetWeaponItemOrbitRadiusScalar(weapon), 1f - .28f * strength),
                $"{quality}: Tight Orbit strengthens its intended smaller-radius benefit");
            weapon = Weapon("cube", "orbital_compression", quality);
            Check(Near(WeaponModulatorSharedRuntime.GetWeaponItemOrbitRadiusScalar(weapon), .78f),
                $"{quality}: Orbital Compression retains its radius tradeoff");
        }
        foreach (RarityData rarity in EquipmentCatalogDefaults.CreateDefaultRarities())
        foreach (WeaponModulatorTier quality in Qualities)
        {
            EquipmentItem weapon = Weapon("square", "apex_doctrine", quality);
            weapon.Rarity = rarity;
            Check(Near(WeaponModulatorSharedRuntime.GetApexDoctrineMultiplier(weapon), 1f) &&
                !WeaponModulatorCompatibility.CanApplyToWeapon(weapon.Modulators[0], weapon, out string reason) && !string.IsNullOrWhiteSpace(reason),
                $"{rarity.Key}/{quality}: archived Apex is inactive and unavailable independently of rarity or quality");
        }
        WeaponModulatorData mutable = ModulatorCatalog.CreateByKey("accelerated_flight", WeaponModulatorTier.Flawed);
        _ = mutable.GetResolvedEffects();
        mutable.Tier = WeaponModulatorTier.Flawless;
        Check(Near(mutable.GetResolvedEffects()[0].Magnitude, 1.4f), "Changing quality invalidates cached resolution without scaling a previous result");
        mutable.Effects[0].Magnitude = 1.3f;
        Check(Near(mutable.GetResolvedEffects()[0].Magnitude, 1.6f), "Changing raw baseline invalidates cached resolution");
        mutable.Tier = WeaponModulatorTier.Baseline;
        Check(Near(mutable.GetResolvedEffects()[0].Magnitude, 1.3f), "Returning to baseline resolves directly from the raw payload");
    }

    private void ValidatePersistence()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo save = typeof(InventoryManager).GetMethod("SaveModulator", flags)!;
        MethodInfo load = typeof(InventoryManager).GetMethod("LoadModulators", flags)!;
        MethodInfo clone = typeof(InventoryManager).GetMethod("CloneModulator", flags)!;
        List<WeaponModulatorData> parts = ModulatorCatalog.CreateAll().SelectMany(stock =>
            Qualities.Select(quality => ModulatorCatalog.CreateByKey(stock.Key, quality))).ToList();
        parts.Add(new WeaponModulatorData
        {
            Key = "quality_validation_saved_custom", BaseDisplayName = "Saved Custom", Tier = WeaponModulatorTier.Enhanced,
            Effects = new() { new() { Type = ModulatorEffectType.ProjectileCountAdd, CountValue = .5f } },
        });
        ConfigFile serialized = new();
        serialized.SetValue("quality_fixture", "modulator_count", parts.Count);
        for (int index = 0; index < parts.Count; index++)
        {
            WeaponModulatorData part = parts[index];
            string effective = JsonSerializer.Serialize(part.GetResolvedEffects());
            WeaponModulatorData copy = (WeaponModulatorData)clone.Invoke(null, new object[] { part })!;
            Check(Raw(copy) == Raw(part) && copy.Tier == part.Tier &&
                JsonSerializer.Serialize(copy.GetResolvedEffects()) == effective &&
                !ReferenceEquals(copy.Effects, part.Effects) && !ReferenceEquals(copy.Substats, part.Substats),
                $"{part.Key}/{part.Tier}: inventory cloning preserves quality without applying it again");
            save.Invoke(null, new object[] { serialized, "quality_fixture", index, part });
        }
        const string path = "user://modulator_quality_roundtrip.cfg";
        Check(serialized.Save(path) == Error.Ok, "Catalog quality entries and a saved custom fractional count serialize through the real inventory writer");
        ConfigFile disk = new();
        Check(disk.Load(path) == Error.Ok, "Quality fixture reloads from the isolated disk save");
        List<WeaponModulatorData> restored = (List<WeaponModulatorData>)load.Invoke(null, new object[] { disk, "quality_fixture" })!;
        Check(restored.Count == parts.Count && restored.Zip(parts).All(pair => pair.First.Tier == pair.Second.Tier &&
            pair.First.Key == pair.Second.Key && Raw(pair.First) == Raw(pair.Second) &&
            JsonSerializer.Serialize(pair.First.GetResolvedEffects()) == JsonSerializer.Serialize(pair.Second.GetResolvedEffects())),
            "Save/load preserves all raw baselines and applies quality exactly once, including archived Apex and an unknown fractional payload");
    }

    private void ValidatePresentation()
    {
        EquipmentItem installed = Weapon("square", "sharpened_form", WeaponModulatorTier.Baseline);
        WeaponModulatorData broken = ModulatorCatalog.CreateByKey("sharpened_form", WeaponModulatorTier.Flawed);
        Check(ModulatorQualityText.Header(broken) == "Tier 1 · Broken · 50% power" &&
            ModulatorQualityText.Describe(broken).Contains("Damage: +6%") &&
            ModulatorQualityText.Compare(broken, installed).Contains("Damage: +12% → +6%"),
            "Comparison shows the resolved six-percent Broken bonus against installed twelve-percent Baseline");
        EquipmentItem payload = Weapon("circle", "heavy_payload", WeaponModulatorTier.Flawed);
        string comparison = ModulatorQualityText.Compare(ModulatorCatalog.CreateByKey("heavy_payload", WeaponModulatorTier.Flawless), payload);
        Check(comparison.Contains("Area: +17.5% → +70%") && comparison.Contains("Flight speed: -15% → -15%"),
            "Comparison scales the benefit while displaying the unchanged delivery penalty");
        Check(ModulatorQualityText.Describe(ModulatorCatalog.CreateByKey("extra_output", WeaponModulatorTier.Flawed)).Contains("+0.5 average per attack"),
            "Fractional count presentation explains the average instead of rounding away Broken power");
        foreach ((WeaponModulatorTier quality, float strength) in Qualities.Zip(Strengths))
        {
            WeaponModulatorData wing = ModulatorCatalog.CreateByKey("wing_man", quality);
            Check(ModulatorQualityText.Describe(wing).Contains($"Wing cadence: {25f * (int)quality + 25f}% of host rate") &&
                Near(wing.WingCadenceMultiplier, .5f * strength),
                $"{quality}: Wing display agrees with actual host cadence");
        }
        WeaponModulatorData retired = ModulatorCatalog.CreateByKey("apex_doctrine", WeaponModulatorTier.Flawless);
        Check(ModulatorQualityText.Describe(retired).Contains("Retired") &&
            !ModulatorQualityText.Describe(retired).Contains("+"), "Archived Apex presentation exposes no active bonuses");
    }

    private void ValidateLegacyAndStrongestProcs()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        SuperGatchaRebuild.Main legacy = new(); // Detached: no legacy scene initialization or saves.
        try
        {
            MethodInfo resolve = typeof(SuperGatchaRebuild.Main).GetMethod("ResolveLegacyQualityCount", flags)!;
            MethodInfo reset = typeof(SuperGatchaRebuild.Main).GetMethod("ResetLegacyQualityCounts", flags)!;
            EquipmentItem first = Weapon("square", "extra_output", WeaponModulatorTier.Flawed);
            EquipmentItem second = Weapon("square", "extra_output", WeaponModulatorTier.Flawed);
            int Count(EquipmentItem source, string purpose) => (int)resolve.Invoke(legacy, new object[] { source, purpose, .5f })!;
            List<int> actual = new();
            for (int attack = 0; attack < 4; attack++)
            {
                // UI/shared reads between attacks must not consume the legacy attack carry.
                for (int preview = 0; preview < 3; preview++)
                {
                    _ = ModulatorQualityText.Describe(first.Modulators[0]);
                    Check(Near(WeaponModulatorSharedRuntime.GetWeaponItemProjectileCountValue(first), .5f), "Legacy preview preserves the fractional count snapshot");
                }
                actual.Add(Count(first, "output"));
            }
            Check(actual.SequenceEqual(new[] { 0, 1, 0, 1 }), "Legacy attacks retain half-count bonuses across pure preview reads");
            Check(Count(second, "output") == 0 && Count(first, "chain") == 0,
                "Legacy count carry is independent for different sources and delivery purposes");
            reset.Invoke(legacy, null);
            Check(Count(first, "output") == 0 && Count(first, "chain") == 0,
                "Legacy run reset clears both source and purpose count carry");
        }
        finally { legacy.Free(); }
        foreach (bool strongestFirst in new[] { true, false })
        {
            EquipmentItem cascade = Weapon("square", "critical_cascade", WeaponModulatorTier.Flawless);
            WeaponModulatorData weak = ModulatorCatalog.CreateByKey("critical_cascade", WeaponModulatorTier.Flawed);
            if (strongestFirst) cascade.Modulators.Add(weak); else cascade.Modulators.Insert(0, weak);
            Check(WeaponModulatorSharedRuntime.TryGetOnCritChainProc(cascade, out var chain) && Near(chain.DamageScale, .7f) &&
                Near(chain.Range, 10f) && Near(chain.CooldownSeconds, .35f) && chain.ArcCount == 1,
                "Mixed Cascade copies retain strongest quality damage with fixed proc range, cooldown and arc count in either order");
            EquipmentItem omni = Weapon("square", "omni_feedback", WeaponModulatorTier.Flawless);
            weak = ModulatorCatalog.CreateByKey("omni_feedback", WeaponModulatorTier.Flawed);
            if (strongestFirst) omni.Modulators.Add(weak); else omni.Modulators.Insert(0, weak);
            Check(WeaponModulatorSharedRuntime.TryGetOmniFeedbackConfig(omni, out var followup) && Near(followup.DamageScale, 2f) &&
                Near(followup.SearchRange, 12f) && followup.HitCadence == 8,
                "Mixed Omni copies retain strongest quality damage and fixed trigger cadence in either order");
        }
    }

    internal static EquipmentItem Weapon(string shape, string part, WeaponModulatorTier quality) => new()
    {
        Shape = EquipmentCatalogDefaults.CreateDefaultShapes().Single(value => value.Key == shape),
        Rarity = EquipmentCatalogDefaults.CreateRarityByKey("divine"), Level = 5, Damage = 100, Health = 100,
        Modulators = new() { ModulatorCatalog.CreateByKey(part, quality) },
    };
    private static string Raw(WeaponModulatorData part) => JsonSerializer.Serialize(new { part.Effects, part.Substats });
    private static bool Near(float value, float expected) => Math.Abs(value - expected) < .0001f;
    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
