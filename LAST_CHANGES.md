# Last Changes

Use this file as a rolling handoff log for the most recent meaningful repo updates.
Add the newest entry at the top and keep each entry short and concrete.

## 2026-10-07

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
