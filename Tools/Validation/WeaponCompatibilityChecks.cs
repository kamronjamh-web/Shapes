using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SuperGatchaRebuild.Equipment;
using SuperGatchaRebuild.Equipment.Data;
using SuperGatchaRebuild.Fps;
using SuperGatchaRebuild.Managers;

namespace SuperGatchaRebuild.Validation;

internal static class WeaponCompatibilityChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private const string AllShapes = "square circle triangle hexagon sphere cube pyramid cone tesseract hyperphere pentachoron hypercone impossiblecube mobius klein boys";

    public static void Run(FpsVerticalSlice map, Func<string, EquipmentItem> makeWeapon, Action<bool, string> check)
    {
        // Explicit stock eligibility from the reviewed pre-change catalog matrix. Do not derive
        // expectations from tags, resolved effect types, or the new shared production helper.
        Dictionary<string, HashSet<string>> expected = new(StringComparer.Ordinal);
        void Group(string keys, string shapes)
        {
            foreach (string key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                expected.Add(key, new HashSet<string>(shapes.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal));
        }
        Group("accelerator_fins boss_geometry bulwark_drive critical_cascade deadeye_tension evasion_mesh expanded_shape finisher_gate focused_edge glass_hammer_core hemophage_loop long_barrel long_reach longshot_pressure lucky_prism omni_feedback power_coil predator_rhythm quickened_core rapid_geometry reckless_focus recovery_circuit reinforced_geometry reinforced_shell rupture_crest sharpened_form siege_core siege_footing split_chamber stability_plating storm_relay stride_servo vampiric_engine vital_reservoir volitile wing_brother wing_man wing_sister wing_woman", AllShapes);
        Group("apex_doctrine", ""); // Archived key remains readable, but no new installation is allowed.
        Group("clone_relay primary_mimic", "boys");
        Group("return_arc saw_storm serration_bloom", "circle");
        Group("heavy_payload", "circle hyperphere");
        Group("backdraft_corridor flame_jet", "cone");
        Group("aegis_carousel orbital_bracing orbital_compression tight_orbit", "cube");
        Group("command_cluster fuse_tuner mine_relay quick_plant", "hexagon");
        Group("extended_uptime lasting_pattern", "hexagon cube tesseract impossiblecube boys");
        Group("rift_stabilizer", "hexagon tesseract impossiblecube");
        Group("drill_spool tunnel_bore", "hypercone");
        Group("high_arc_bombard mortar_barrage scorch_crater", "hyperphere");
        Group("event_horizon_tear void_compression", "impossiblecube");
        Group("blood_price_reactor", "klein");
        Group("echo_focus echo_weave", "mobius");
        Group("chain_capacitor closed_circuit", "pentachoron");
        Group("guidance_spine guiding_bias hunter_saturation lock_burst pack_hunters predator_lens", "pyramid");
        Group("collapse_pulse double_pulse nova_shear", "sphere");
        Group("wrath_furnace", "sphere klein");
        Group("burstline_converter needle_shot", "square");
        Group("lancing_spiral", "square circle pyramid");
        Group("piercing_edge rebound_array", "square circle pyramid hypercone");
        Group("accelerated_flight overburn_fins", "square circle pyramid hyperphere hypercone");
        Group("echo_chamber overclocked_core", "square circle triangle hexagon sphere cube pyramid cone tesseract hyperphere pentachoron hypercone impossiblecube boys");
        Group("chain_reaction lingering_scar predatory_mark", "square circle triangle pyramid hypercone");
        Group("extra_output", "square hexagon cube pyramid pentachoron boys");
        Group("ballistic_tuning precision_rail", "square hypercone");
        Group("phase_inversion", "tesseract");
        Group("beam_condenser line_driver prism_refraction", "triangle");
        List<WeaponModulatorData> catalog = ModulatorCatalog.CreateAll();
        check(expected.Count == 100 && catalog.Count == 100 && catalog.All(part => expected.ContainsKey(part.NormalizedKey)),
            "Independent stock compatibility expectations cover all one hundred catalog parts");
        string[] shapes = AllShapes.Split(' ');
        Dictionary<string, EquipmentItem> weapons = shapes.ToDictionary(shape => shape, makeWeapon);
        List<EquipmentItem> arsenal = (List<EquipmentItem>)typeof(FpsVerticalSlice).GetField("_arsenal", Private)!.GetValue(map)!;
        EquipmentItem[] originalArsenal = arsenal.ToArray();
        InventoryManager inventory = new(); // Detached fixture: no _Ready, save loading, or disk writes.
        List<EquipmentItem> items = (List<EquipmentItem>)typeof(InventoryManager).GetField("_items", Private)!.GetValue(inventory)!;
        List<WeaponModulatorData> stored = (List<WeaponModulatorData>)typeof(InventoryManager).GetField("_storedModulators", Private)!.GetValue(inventory)!;
        EquipmentItem?[] equipped = (EquipmentItem?[])typeof(InventoryManager).GetField("_equipped", Private)!.GetValue(inventory)!;
        bool Fps(EquipmentItem weapon, WeaponModulatorData part) => (bool)typeof(FpsVerticalSlice).GetMethod("CanInstallModulatorForFpsWeapon", Private)!.Invoke(map, new object[] { weapon, part, -1 })!;
        bool Hub(EquipmentItem weapon, WeaponModulatorData part)
        {
            items.Clear(); items.Add(weapon);
            stored.Clear(); stored.Add(part);
            return inventory.CanInstallStoredModulator(weapon.InstanceId, 0);
        }
        try
        {
            int pairs = 0;
            foreach (WeaponModulatorData part in catalog)
            foreach (string shape in shapes)
            {
                EquipmentItem weapon = weapons[shape];
                arsenal.Clear(); arsenal.Add(makeWeapon("square")); arsenal.Add(weapon);
                Array.Clear(equipped); equipped[0] = arsenal[0]; equipped[1] = weapon;
                bool want = expected[part.NormalizedKey].Contains(shape);
                bool shared = WeaponModulatorCompatibility.CanApplyToWeapon(part, weapon, out string reason);
                bool hub = Hub(weapon, part), fps = Fps(weapon, part);
                if (shared != want || hub != want || fps != want || (want ? !string.IsNullOrEmpty(reason) : string.IsNullOrWhiteSpace(reason)))
                    throw new InvalidOperationException($"{part.Key}/{shape}: expected={want}, shared={shared}, hub={hub}, FPS={fps}, reason={reason}");
                pairs++;
            }
            check(pairs == 1600, "All 1600 stock part/shape pairs preserve independent eligibility, hub/FPS agreement and useful rejection reasons");
            check(!WeaponModulatorCompatibility.CanApplyToWeapon(catalog[0], null, out string missingReason) && !string.IsNullOrWhiteSpace(missingReason),
                "Missing weapon receives an actionable compatibility rejection");
            EquipmentItem primary = makeWeapon("square"), support = makeWeapon("square"), second = makeWeapon("circle");
            WeaponModulatorData man = ModulatorCatalog.CreateByKey("wing_man"), sister = ModulatorCatalog.CreateByKey("wing_sister");
            arsenal.Clear(); arsenal.AddRange(new[] { primary, support, second });
            Array.Clear(equipped); equipped[0] = primary; equipped[1] = support; equipped[2] = second;
            check(!Hub(primary, man) && !Fps(primary, man) && Hub(support, man) && Fps(support, man),
                "Hub and FPS preserve Wing primary-slot rejection and support-slot acceptance");
            support.Modulators.Add(man);
            check(!Hub(second, man) && !Fps(second, man) && !Hub(support, sister) && !Fps(support, sister) && Hub(second, sister) && Fps(second, sister),
                "Hub and FPS agree on duplicate Wing keys, one Wing per weapon and distinct support Wings");
        }
        finally { arsenal.Clear(); arsenal.AddRange(originalArsenal); inventory.Free(); }
    }
}
