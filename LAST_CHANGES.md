# Last Changes

Use this file as a rolling handoff log for the most recent meaningful repo updates.
Add the newest entry at the top and keep each entry short and concrete.

## 2026-10-07

- Integrated the eight redesigned 3-/4-star weapons and Cone, completing Blender runtime models for all sixteen MultiWeapon shapes. Dedicated exports preserve every bay variant, actual rarity/modulator customization, shape inserts, moving anchors and fifteen firing mechanisms. The redesigned bodies have individual hip/aim framing and stable bounds centering; inspector models retain their authored dimensions. Disabled generated Mobius LODs to remove ribbon surface artifacts and adjusted the drill/gauntlet framing after rendered review.
- Validation: build has zero warnings/errors; all 1,054 weapon-inspection checks and 417 combat/modulator checks pass. Reviewed hip, aim, actual firing, inspector and mechanism captures under `.tmp/weapon-inspection-capture`. Runtime exports were built with Microsoft Store Blender without modifying the editable sources; the capture runner now defaults to all sixteen shapes and supports a `-Shapes` filter.

- Redesigned all eight 3- and 4-star Blender weapons with complete new bodies: Tesseract rift frame, Hyperphere shoulder mortar, Pentachoron gauntlet, Hypercone drill lance, floating Impossible Cube, Mobius forearm ribbon, Klein aura vessel and Boy's Surface clone sculpture. They share the Square materials, shape-cartridge controls and ten modulator interfaces, fitted to the new forms. Saved editable mechanisms, static GLB review exports, four views each and refreshed tier lineups/gallery under `Art/WeaponConcepts`. Existing 1-/2-star assets are preserved; runtime assignment of these new forms is still separate.
- Microsoft Store Blender validation: all 720 rarity/level/equipment cases pass; all eight saved sources reopen with working motion, return to rest at frame 40, and reimport from GLB with ten socket anchors. Visual review corrected mortar launch clearance, handle-obscured modulator bays and the lance presentation angle. Build/review commands and handoff scope are documented in `Tools/Art/README.txt`.

- Integrated the six approved Blender transformations (Circle, Triangle, Hexagon, Sphere, Cube, Pyramid) into MultiWeapon shape switching and the inventory inspector. Complete runtime exports preserve all ten bay variants, actual rarity/modulator customization, shape inserts and effect anchors. Imported firing mechanisms follow successful shots and return to rest; first-person position/scale are exposed in the editor. The editable sources remain separate from game GLBs.
- Validation: build has zero warnings/errors; weapon inspection passes 537 checks and combat/modulator regression passes 417. Reviewed first-person, aimed, inspector and mechanism captures for all six under `.tmp/weapon-inspection-capture`. Exported with the installed Microsoft Store Blender 5.2.2 LTS and imported/rendered in Godot 4.7.2 mono. The sandboxed engine reports an unrelated root-certificate-store warning; imports, rendering and both suites complete successfully.

<<<<<<< HEAD
- Added the next MultiWeapon art trio: Sphere pulse cannon with eight expanding shell panels, Cube guard projector with four deploying guards, and Pyramid seeker launcher with hinged armor and captive seekers. Each retains the Square chassis, ten modulator bays and rarity controls, now with a solid 3D shape insert and matching side emblem.
- Built self-contained `.blend` sources, static `.glb` exports, four views per asset and the `MultiWeaponReview_Sphere_Cube_Pyramid` comparison using Microsoft Store Blender. All 270 new rarity/level/equipment combinations passed; solid insert dimensions, saved animations, source reopening and all three GLB imports were checked. Corrected shell/support clearance and sensor-cap connections during visual review. Art assets are ready for review; gameplay assignment is separate.
- Added editable Blender assets for the MultiWeapon's Circle disc launcher, Triangle beam carbine, and Hexagon mine launcher under `Art/WeaponConcepts`. All three inherit the approved Square grip/frame, shape dock, rarity materials and ten modulator bays, with new upper assemblies, matching inserts and 40-frame mechanism studies.
- Generated static GLB review exports, four rendered views per weapon, and a comparison render from reimported GLBs. Microsoft Store Blender 5.2.2 LTS passed 270 rarity/level/equipment combinations, source reopen/motion checks, and all three GLB imports with ten socket anchors each. Added reproducible art build/review scripts. This is an art handoff; gameplay scenes remain unassigned to these assets.
=======
- Integrated the approved Blender Square handgun through a complete runtime GLB export. Actual item rarity drives accents; ten side bays show installed modulators and empty/locked states. All sixteen shapes have matching inserts and distinct solid upper-body profiles; animated inserts retain canonical proportions.
- Added paused weapon inspection through the FPS pause menu's Inventory / Equipment button and the field shop. Shape selection previews separately from Equip shape, uses only the run/loadout arsenal, preserves wounded health, and restores the previous pause/shop state. Isolated viewports provide drag/zoom and a separate shape identity view; closing disables their rendering.
- Validation: build has zero warnings/errors; weapon inspection passes 267 checks, existing FPS modulators 417 and North Gate 37. In-game FPS and inspection renders are captured in `.tmp/weapon-inspection-capture`; headless checks and rendered review remain separate.

- Expanded North Gate's layout to a 1,609.344 × 1,609.344 m playable square, centered on a clear 200 × 200 m open-sky base at elevation 8 m. Walls are 20 ft / 6.096 m high; four 15 ft / 4.572 m cardinal gates have default 1,000 HP. Exterior terrain slopes down from the outer walls near 102 m to the 220 m hill foot. Added the continuous 3 m wall walk, 8 × 6 m outward firing platforms, and access-ramp layout.
- The normal 100-cube wave now uses four 25-enemy sectors; manual up-to-2,000 populations use four bounded exterior approaches. `FpsDefenseGate` supplies physical collision, world health display, enemy damage, breach events, and health-fraction/broken-state restoration across rebuilds. Friendly damage defaults off. Grave → Fort retunes gate HP from 1–20,000, toggles gate invulnerability, and restores all gates; the HUD reports north/east/south/west HP.
- Validation: zero-warning/error build; expanded fort 80 checks PASS, including eight downward-firing LOS checks from platform outer edges and three actual Fort-tab health/resource-isolation/restore tests; legacy hill 48, motion/deaths 26, piling 39, modulators 417, lane 37, and tuning 56 headless PASS. Inspected fort overview, gate, access, and natural 2,000-enemy assault views under `.tmp/fort-defense-capture`. First gate hit is at 37.33 simulated seconds; four seconds later 412 bodies are raised, maximum lift is 5.59 m, highest feet are 13.55 m versus the 14.096 m wall top, and gates have 660–685 HP. These observations establish gate pileup, not wall overrun.
- Reduced 2,000-body production spikes through cached gate positions/distance rejection, reused health-bar meshes, collision toggles only on breach/restore, consolidated HUD assignments, and skipping unused Wing tracking without changing active support behavior. The fresh four-approach 1600×900 VSync capture on i7-13700H/RTX A1000 measured 180 rendered intervals: median 23.94 ms / p95 28.10 / maximum 31.43. Separate crowd CPU median was 19.03 ms / p95 25.60. This is a short camera-specific diagnostic without sustained firing or corpse-heavy combat, not a long-combat FPS guarantee.
- Sparse XZ-grid rebuilds clear occupied cells rather than the entire mile-square extent. Queries spanning 700 m pass, and camera far distance is 2,414 m. `LegacyHillFixture.cs` retains the former 200 m roofed map for compact piling/roof and combat regressions. All earlier roof-capture timings below refer to that former map, not the expanded fort.
- Fixed unsupported enemies sustaining each other beneath the roof: climbing now requires actual world/lower-body support or a genuinely lower neighbor measured in the same contact snapshot. They fall from the underside while rooftop traversal remains supported.
- Reduced independent base equipment drops to 0.5% shapes / 1% modulators in both defense resources and defaults, preserving Luck bonuses. Added grave-menu Movement / deaths controls for drops, cube gait and corpse physics.
- Added distance-driven cube side rolling with real 0.055-second pauses between quarter turns, staggered starts, interpolation and stable combat bounds. Blocked or paused cubes hold their pose. Added a reusable 64-body, 4-second corpse pool: actual killing-hit direction, remaining-health overkill launch scaling, tumbling world collision, pause/resume and cleanup. Every production weapon/secondary damage path carries its own source direction; mass kills preserve original reward/death bookkeeping.
- Validation: zero-warning/error build; 26 new motion/death checks plus 39 piling, 48 hill/horde, 417 modulator, 37 lane and 57 rendered tuning checks passed. One parallel hill-suite run passed all assertions but crashed during Godot array finalization at shutdown; its isolated rerun exited cleanly. Inspected live-physics rolling and low/high-overkill tumble sequences under `.tmp/enemy-motion-capture`: the high-overkill example travelled about 21.8 m versus 2.0 m in 1.2 seconds. These are specific visual/physics fixtures, not a full-combat performance guarantee.
- Refreshed 2,000-body rolling/piling capture: natural roof traversal after 24 simulated seconds; rendered intervals median 23.25 ms / p95 27.79 and separate crowd-only ticks median 16.62 ms / p95 18.06 on i7-13700H/RTX A1000 at 1600×900 VSync. This short sample excludes sustained weapon fire and corpse-heavy combat; artifact `.tmp/horde-piling-capture/piling-diagnostic.txt`.
- Added soft enemy contact, gravity and pressure-driven piling with actual raised render/hit positions. Large waves naturally climb over summit walls and onto the roof; wall/lintel clearance and roof-edge falls use the real room dimensions. Melee attacks now account for vertical reach. Existing supports stay solid when climbing is disabled or its cap is lowered.
- Added grave-menu Crowd contact controls and raised-body telemetry. A preallocated 3D contact hash, removal of redundant steering separation, geometry early-outs and one default contact pass reduce dense-pile work while retaining live tuning up to four passes. Default climbing allowance is 10 m above terrain.
- Validation: build has zero warnings/errors; 39 piling, 48 hill/horde, 417 modulator, 37 lane and 57 rendered live-tuning checks passed. Natural 2,000-body testing reached the roof after 23 simulated seconds, with 1,275 bodies raised above terrain. Inspected rendered roof traversal, motion sequence and controls. Final 1600×900 VSync sample on i7-13700H/RTX A1000 measured rendered interval median 25.59 ms (p95 29.98), and separate crowd-only median 20.02 ms (p95 23.12). Short samples exclude sustained player firing; detailed diagnostics are in `.tmp/horde-piling-capture/piling-diagnostic.txt`.
- Added a grave/backtick live gameplay menu with Population/Simulation/Enemies/Player tabs, 100/500/1,000/2,000 presets, cube/pyramid selection, Add/Replace/Clear, live crowd/archetype/movement controls, invulnerability, and frame/simulation diagnostics. Editing blocks player controls while active combat continues; the existing paused stat/loadout panel is linked. Grave/Escape work with focused numeric fields.
- Raised North Gate allocation to 2,000, keeping the normal first wave at 100. Larger manual/configured waves use bounded north-foot formations with actual successful count accounting. Silent clear invalidates handles and removes old ordnance without rewards; manual population tests hold scripted wave completion. Run-local tuning resources preserve cached defaults, and live archetype edits preserve health fractions and wave scaling.
- Validation: build passed with zero warnings/errors; rendered live-tuning suite passed 57 checks, plus all 48 hill/horde, 417 modulator, and 37 lane checks. Invulnerability is enforced at player/core damage sinks, including real enemy projectile hits. Inspected rendered 2,000-crowd and menu captures. Short 1600×900 VSync samples on i7-13700H/RTX A1000 measured 2,000-cube travel simulation median 2.48 ms (p95 2.62), staged doorway median 8.37 ms (p95 10.93); rendered approach interval median 15.92 ms (p95 19.01). These are scoped diagnostics, not sustained full-combat FPS claims; artifacts are in `.tmp/horde-stress`.
>>>>>>> 8c02b8d8624a3231976edd760b8a3f6e98306b15
- Audited all 100 live fallback modulators and completed North Gate support: base stat effects and tradeoffs, crits, sustain/movement/Luck, projectile counts/piercing/ricochet/bounce, 16 weapon transforms, Return Arc, Second Pulse, targeting bias, Cascade, Omni, Volitile, and all four Wings.
- Square now fires moving bullets with exported base speed. Added capsule-aware field damage, wall-safe crowd/legacy displacement, secondary-proc guards, source-preserving support attacks, and bounded special state. Fixed startup health UI timing and Debug overrides that replaced passive health/movement effects.
- Expanded drops/shops to the complete weighted modulator/shape catalogs. Central FPS compatibility rejects unusable effect families and invalid Wings; imported incompatible parts are recovered into the run while saved equipment is preserved. Fixed Apex scaling for bridged passive stats and legacy Luck.
- Validation: build passed with zero warnings/errors; 417 modulator checks, 48 hill/horde checks, and 37 lane regression checks passed with isolated saves. The lane purchase regression now uses the actual rotating shape offer. These checks establish combat correctness at 100 enemies, not performance at larger populations.
- Replaced the hub's North Gate map with a 200 × 200 metre area, a simple eight-metre hill, and a roofed summit room containing the core and shop. Its only doorway faces north; all 100 initial enemies spawn together at the north foot and climb through it.
- Added the first data-driven horde implementation: preallocated state, generation-safe enemy handles, an XZ grid, bounded separation, fixed-rate simulation, shared cube/pyramid MultiMeshes, and deferred death events. Ordinary minions create no individual physics bodies or mesh nodes.
- Connected crowd enemies to weapons/modulators, precise capsule hits and world occlusion, piercing/area attacks, core/player threats, loot, shops, and victory/defeat. Preserved the original lane map as `NorthGateLaneSandbox.tscn`.
- Added disk-backed hill/horde tuning resources and live hill edits that ground the player/core/shop while preserving health and equipment. Crowd capacity requires a scene restart; archetypes are sampled at spawn.
- Validation: .NET build passed with zero warnings/errors; 48 hill/horde checks and 37 earlier lane checks passed. Rendered Vulkan previews inspected. Simulation-only timing is recorded separately and does not establish rendered FPS or larger-horde capacity.
- Base construction and measured 500–1,000+ enemy stress scenarios remain future work.

## 2026-10-06

- Added the playable `North Gate` first-person defense map and made it the hub's FPS destination: one north approach, a defended core, three sealed cardinal gates, and waves of 12, 24, and 40 shape enemies.
- Added run-local currency, weapon-shape and modulator drops, pickup collection, and shops available between waves and temporarily during combat. Shops sell shape weapons, compatible modulators, weapon levels, and core repairs; combat continues while shopping.
- Added disk-backed, live-editable defense and lane resources. Enemy formations follow enabled lanes, enemies attack the core or nearby player, and core destruction ends the run.
- Wired attack-speed modulators into firing cadence and Square piercing into multiple ray hits. Adjusted the HUD and added a pyramid enemy silhouette.
- Added an isolated Godot integration validation scene and runner. Validation: .NET build passed with zero warnings/errors; all 37 runtime checks passed; rendered map, first-person HUD, and shop previews inspected.
- Updated setup and architecture notes. Base construction and a crowd system for thousands of enemies remain future work; North Gate loot and purchases currently reset with the run.

## 2026-10-03

- Renamed the Godot application, start-screen title, and hub title to `Shapes`.
- Pinned the desktop user-data path to the existing `Super Gatcha Rebuild` save directory, with the lowercase Godot folder override on Linux, so the display-name change preserves inventory and settings.
- Validation: `dotnet build "SuperGatchaRebuild.csproj" --no-restore` passed with zero warnings/errors; `git diff --check` passed. Menu appearance has not been playtested.

## 2026-06-19

Recent repo changes:

- Added disk-backed FPS tuning resources under [Resources/Fps/FpsArenaLayoutSettings_Default.tres](Resources/Fps/FpsArenaLayoutSettings_Default.tres), [Resources/Fps/FpsEncounterSettings_Default.tres](Resources/Fps/FpsEncounterSettings_Default.tres), [Resources/Fps/FpsEnemyArchetype_Bruiser_Default.tres](Resources/Fps/FpsEnemyArchetype_Bruiser_Default.tres), and [Resources/Fps/FpsEnemyArchetype_Striker_Default.tres](Resources/Fps/FpsEnemyArchetype_Striker_Default.tres) so Inspector edits persist to `.tres` files.
- Wired [Scenes/Fps/FpsVerticalSlice.tscn](Scenes/Fps/FpsVerticalSlice.tscn) to reference those external resources for `ArenaLayoutSettings`, `EncounterSettings`, `BruiserArchetypeSettings`, and `StrikerArchetypeSettings` instead of relying on runtime-only defaults.
- This removes the main save trap where editing in-scene generated resources had no stable file path to write back to.

## 2026-06-15

Recent repo changes:

- Hardened scene transition lifecycle in [Scripts/UI/StartHubUI.cs](Scripts/UI/StartHubUI.cs) by adding delayed retirement for previous scenes instead of leaving them permanently deactivated.
- Added retirement prep that deactivates the outgoing scene and strips renderer-facing nodes (`VisualInstance3D` / `WorldEnvironment`) before final `QueueFree` after a frame delay, reducing repeated-run memory growth risk while preserving transition stability.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.

## 2026-06-15

Recent repo changes:

- Reworked hub launch transitions in [Scripts/UI/StartHubUI.cs](Scripts/UI/StartHubUI.cs) to avoid direct `ChangeSceneToFile` calls on Arena/FPS buttons; launch now flushes cleanup frames and switches by scene instantiation while deactivating the previous scene tree.
- Kept transition pre-cleanup for hub preview/socket/modifier visuals and paired it with the new launch path to avoid renderer-side geometry/light unpair crashes seen during scene changes.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.

## 2026-06-15

Recent repo changes:

- Added transition cleanup APIs in [Scripts/Hub/HubSceneController.cs](Scripts/Hub/HubSceneController.cs) and [Scripts/Hub/HubEquipmentSlotView.cs](Scripts/Hub/HubEquipmentSlotView.cs) to tear down inspection/socket/modifier visuals before scene switches.
- Updated [Scripts/UI/StartHubUI.cs](Scripts/UI/StartHubUI.cs) arena/FPS launch flow to guard re-entry, run hub preview cleanup, and defer `ChangeSceneToFile` by one process frame to avoid renderer-side scene-unpair crashes.
- Hardened hub socket anchor handling in [Scripts/Hub/HubEquipmentSlotView.cs](Scripts/Hub/HubEquipmentSlotView.cs) to treat null modulator entries as empty sockets.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.

## 2026-06-15

Recent repo changes:

- Hardened FPS weapon clone path in [Scripts/Fps/FpsVerticalSlice.Weapons.cs](Scripts/Fps/FpsVerticalSlice.Weapons.cs) by sanitizing modulator lists and deep-cloning modulator data before entering the FPS slice.
- Added a null guard in [Scripts/Equipment/StatGenerator.cs](Scripts/Equipment/StatGenerator.cs) so derived-stat recalculation skips invalid modulator entries instead of dereferencing them.
- Fixed null-unsafe orbit socket rendering in [Scripts/Visuals/WeaponModulatorVisualFactory.cs](Scripts/Visuals/WeaponModulatorVisualFactory.cs) by treating null socket entries as uninstalled/ghost sockets.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.

## 2026-06-15

Recent repo changes:

- Added resource-driven wave definitions in [Scripts/Fps/FpsEncounterWaveSettings.cs](Scripts/Fps/FpsEncounterWaveSettings.cs) and extended [Scripts/Fps/FpsEncounterSettings.cs](Scripts/Fps/FpsEncounterSettings.cs) with wave flow controls, inter-wave delay, and per-wave scaling steps.
- Reworked FPS encounter runtime in [Scripts/Fps/FpsVerticalSlice.cs](Scripts/Fps/FpsVerticalSlice.cs) to support multi-wave progression, clear-to-advance timing, wave-aware HUD objective/status text, and live-tuning watchers for nested wave/spawn resources.
- Added wave scaling support to [Scripts/Fps/FpsSliceEnemy.cs](Scripts/Fps/FpsSliceEnemy.cs) so wave multipliers can tune health, damage, move speed, and attack cadence without duplicating archetype data.
- Updated [Scripts/Fps/FpsVerticalSlice.Weapons.cs](Scripts/Fps/FpsVerticalSlice.Weapons.cs) status HUD text to show current/next wave context alongside threat count and weapon state.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.

## 2026-06-15

Recent repo changes:

- Added shared combat modulator resolution helpers in [Scripts/Combat/WeaponModulatorSharedRuntime.cs](Scripts/Combat/WeaponModulatorSharedRuntime.cs) and routed arena weapon-targeting passive stat accessors through them.
- Refactored arena modulator effect runtime in [Scripts/Combat/WeaponModulatorEffectRuntime.cs](Scripts/Combat/WeaponModulatorEffectRuntime.cs) to use the new shared resolver defaults for on-fire and on-hit effect handling.
- Refactored FPS weapon firing in [Scripts/Fps/FpsVerticalSlice.Weapons.cs](Scripts/Fps/FpsVerticalSlice.Weapons.cs) to centralize fire dispatch with explicit source-item context and echo-aware execution.
- Added FPS-side modulator runtime support for mark, chain-reaction, lingering-zone, execute/elite damage multipliers, and on-fire echo scheduling using shared resolver logic.
- Updated FPS damage/tick paths to use a central source-item-aware damage sink so projectile, beam/zone, orbital, mortar, clone, and aura hits can apply shared modulator behavior consistently.
- Added encounter-rebuild cleanup for FPS weapon/modulator runtime state in [Scripts/Fps/FpsVerticalSlice.cs](Scripts/Fps/FpsVerticalSlice.cs) and exposed enemy current/max health in [Scripts/Fps/FpsSliceEnemy.cs](Scripts/Fps/FpsSliceEnemy.cs) for execute-threshold calculations.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.

## 2026-06-05

Recent repo changes:

- FPS vertical slice now sources its live weapon from `InventoryManager.GetEquippedItemAt(0)` so combat behavior follows the player’s primary hub weapon instead of a local default arsenal.
- FPS first-person weapon presentation now reuses the hub visual stack: `ShapeVisualFactory` model, modulator socket visuals, and special essence visuals where applicable.
- Refactored the FPS slice to build a real weapon arsenal from `EquipmentCatalogDefaults` / `ShapeData` instead of a single hardcoded Arc Carbine.
- Added first-person implementations for the weapon card attack families, including beams, mines, orbitals, seekers, mortars, rifts, void pulls, echoes, wrath aura ticks, and shadow clones.
- Wired FPS weapon switching, HUD card details, and procedural viewmodel presentation to the currently equipped card.
- Added fallback damage baselines for passive mythic cards so Temporal Echo and Wrath Aura stay meaningful in the FPS slice.
- Validation completed with `dotnet build .\SuperGatchaRebuild.csproj` and the build passed.

## 2026-06-02

Recent repo changes:

- Added ten new Tier 1 fallback modulators: Sharpened Form, Quickened Core, Expanded Shape, Long Reach, Accelerated Flight, Lasting Pattern, Piercing Edge, Focused Edge, Rupture Crest, and Guiding Bias.
- Wired `DurationMultiplier` into spawned-object and persistent-field lifetimes so duration-based Tier 1 modulators affect mines, beams, novas, flames, orbitals, rifts, voids, and clones.
- Tightened several Tier 1 compatibility rules to concrete live shapes where the current runtime actually honors projectile-speed, pierce, and homing-strength effects.
- Fixed remaining Tier 1 behavior gaps so projectile-speed affects Hypercone drill travel and range scaling now reaches Triangle beam length and Hypercone drill lifetime.

## 2026-06-02

Recent repo changes:

- Added `SESSION_BOOTSTRAP.md` as a fast-start context file for fresh sessions.
- Added `GAMEPLAY_ARCHITECTURE.md` as a direct map of the combat, equipment, modulator, catalog, and persistence control paths.

## 2026-06-02

Recent repo changes:

- Added six Tier 2 fallback modulators: Reinforced Geometry, Heavy Payload, Overclocked Core, Extra Output, Lancing Spiral, and Predator Lens.
- Extended passive modulator runtime support for projectile speed, homing strength, and pierce so those Tier 2 effects are live in gameplay.
- Updated mortar behavior so projectile speed modifiers also affect Hyperphere flight time, which keeps Heavy Payload functional there.
- Tightened Tier 2 modulator compatibility so installs stay limited to weapon shapes where the implemented effects actually apply.
- Validation completed with `dotnet build "SuperGatchaRebuild.csproj"` and the build passed.
