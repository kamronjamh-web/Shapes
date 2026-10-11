# Gameplay Architecture

This file maps the main gameplay control paths so fresh sessions can jump directly to the owning code.

## Top-Level Shape

- Core runtime behavior is spread across `Main` partial classes under `Scripts/` and `Scripts/Combat/`.
- Combat, weapon spawning, ticking, targeting, damage, and advanced modulator behavior are all implemented as partial methods on `Main`.
- Inventory and equipped-item persistence live outside `Main` in `Scripts/Managers/InventoryManager.cs`.

## First-Person Horde Defense

- `Scenes/Fps/NorthGate.tscn` is the first defense map, launched from the hub. It shares `FpsVerticalSlice` combat with the original FPS sandbox.
- `Scripts/Fps/FpsVerticalSlice.DefenseMap.cs` owns terrain sampling, world construction, core, and shop; `Fortifications.cs` supplies the expanded fort geometry and standable surfaces. The default playable square is 1,609.344 m per side, one square mile total. A centered clear 200 × 200 m open-sky interior stands at elevation 8 m, enclosed by 20 ft / 6.096 m walls. Terrain slopes down from outside the walls near 102 m to a 220 m hill foot. Terrain triangles and custom crowd grounding share the same height function. The earlier lane geometry remains available when `HillMapSettings` is absent.
- `FpsHillMapSettings` and its `Fortifications.cs` partial, `FpsDefenseSettings`, and `FpsDefenseLaneSettings` define terrain, base, cardinal approach, core, loot, and shop settings. Current map resources live in `Resources/Fps/HillFort_*.tres`. Fort geometry includes four 15 ft / 4.572 m entry gates, a continuous 3 m wall walk, 8 × 6 m outward firing platforms, and access ramps.
- `FpsDefenseGate.cs` owns a static box collider, health bar/label, enemy damage and breach notifications. Default gate HP is 1,000; friendly damage is disabled. `FpsVerticalSlice.Gates.cs` connects construction, objective selection, approach routing, blocking, reset, and north/east/south/west HP telemetry. Retuning/rebuilding preserves wounded fractions and breaches; a breach removes collision and opens routing toward the core. Melee measures reach to the gate; ranged projectiles damage the collider struck. Gate behavior is covered by the expanded 80-check suite. Gate positions are cached, distant body checks reject cheaply, health bars reuse their meshes, and collision state changes only on breach/restore.
- `Scripts/Fps/FpsHordeManager.cs` stores enemies in preallocated arrays indexed by generation-safe handles. It owns the XZ grid, fixed-rate movement, bounded neighbor separation, targeting, queued deaths, and cube/pyramid MultiMeshes. Grid rebuilds clear only occupied cells to avoid full-mile clearing costs; map-spanning queries over 700 m are validated. It creates no per-minion scene nodes. The director ticks simulation before weapon effects and flushes deaths afterward. The map camera far distance is 2,414 m.
- `FpsVerticalSlice.Horde.cs` connects terrain height, gate/doorway routing, wall constraints, attack line-of-sight, loot, and wave completion. `IFpsCombatEnemy` and its plain crowd wrapper preserve enemy identity across batched rendering. The normal wave has 100 cubes split 25 per cardinal sector. North Gate reserves 100,000 slots; large manual populations use four bounded, disjoint exterior wedges with clearance between neighboring sectors.
- `FpsHordeManager.Contacts.cs` resolves soft rounded body contacts, pressure-driven climbing, gravity and current lower-body support using bounded synchronous passes. `ContactGrid.cs` provides a preallocated 3D spatial hash so upper/lower layers do not crowd the weapon-query XZ buckets. Soft contacts replace the legacy avoidance pass while enabled. There are no per-enemy rigid bodies; the default is one contact pass at 30 Hz, with live 1–4 pass tuning.
- `FpsHordeManager.GpuContacts.cs` selects the optional GPU crowd simulation backend, enabled by the North Gate resource. `Scripts/Fps/Gpu` and `Resources/Shaders/GpuCrowd` implement known-fort target selection, steering, gait, gravity, movement/world constraints, one contact/piling pass and rolling on a persistent local Vulkan device. Both result packets validate before changing CPU records; attacks, weapons, immediate deaths and generation handles retain their existing order. Unsupported callbacks retain CPU movement, and unsupported worlds/settings/renderers or device errors use CPU fallback. The live Crowd contact toggle resets a failed backend even while paused. Simulation still synchronously returns authoritative state to the CPU for combat.
- `FpsHordeManager.GpuRendering.cs` and `GpuCrowdRenderer.cs` publish enemy poses through a separate main-device compute pass into Godot's actual MultiMesh buffers. Raw body snapshots upload only after state changes; interpolation, rotation, visual ceiling limits and hit flashes evaluate once per body on the GPU each render frame. Production rendering performs no GPU readback or forced wait. Explicit bounds preserve culling; CPU/GPU transitions reset native transform caches. North Gate enables this independently of GPU contacts. Unsupported worlds, non-Vulkan/headless rendering, TAA/temporal upscaling, motion-vector debug and scene compositor effects requiring motion vectors use the existing CPU bulk renderer. Raw RenderingServer-created viewports/effects cannot be inspected automatically and must keep this option disabled if they require motion vectors.
- `FpsFortSpatialIndex.cs` caches 8 m XZ cells with ordered solid/ramp masks for conservative point and swept queries. Both CPU callbacks and the GPU world solver use nearby candidates, retaining original narrow-phase rules. `FpsVerticalSlice.GpuWorld.cs` caches geometry by index/version and current terrain/gate-rule values; dynamic gate state refreshes every dispatch. GPU data follows geometry rebuilds and terrain changes without rebuilds. Isolated parity, runtime/fallback validation, benchmark commands and scoped results are in `Docs/GpuCrowdInvestigation.txt`.
- `SampleHillCrowdSupportHeight`, `SampleHillCrowdMaxFootHeight`, and `ConstrainHillCrowdBodyMovement` share world dimensions with physical geometry. Support requires arrival from above, preserving under-roof passage in the compact regression fixture; the expanded default interior is open sky. Fort wall walks, platforms, ramps, walls, and closed gates must agree between physics, crowd constraints, and support sampling. Leaving support resumes gravity. Climbing limits gate new ascent rather than removing existing contacts. Attacks evaluate final 3D body positions; hit capsules and render instances follow those same records.
- Unsupported airborne neighbors cannot mutually climb at the same height and cancel gravity. Climbing requires world/lower-body support or a genuinely lower neighbor; this prevents ceiling-underside crawling while preserving rooftop access.
- `FpsHordeManager.Rolling.cs` supplies a distance-driven cube gait: quarter turns per width, short actual movement pauses, and speed compensation while rolling. Only the MultiMesh pose rotates and lifts; combat/contact bounds remain upright. Pose interpolation freezes with simulation, and dead bodies retain the current visual transform.
- `DealDirectionalDamageToEnemy` carries actual hit travel/source directions through every weapon and secondary-proc path to `IFpsCombatEnemy.ApplyDamageFrom`. `FpsHordeDeath` captures fatal damage, excess over remaining health, role/color and visual pose before slot reuse. Deferred `EnemyDefeated` creates a corpse separately from the original `EnemyDied` reward notification.
- `FpsEnemyCorpsePool` reuses up to 2,056 cosmetic single-body corpses. North Gate configures the full limit; generic settings default to 64. Two MultiMeshes share material and packed transform/color buffers; sleeping poses stop uploading until movement or a world change. Free-slot bits and chronological links preserve lowest-free allocation and exact oldest-first recycling. Launch, overkill caps, four-second lifetime, pause and reward separation are retained.
- The optional `CorpseWorkerEnabled` backend gives `Box3dCorpseWorker` sole ownership of native CPU physics on a background thread. The main thread copies known fort geometry and hands off bounded commands; it reads completed poses without waiting for simulation. Generation/epoch checks reject stale recycling/clear results. Gate breach/restore and arena rebuild refresh static geometry and wake sleepers. Corpses collide only with copied world geometry, excluding player/core, living enemies and each other. Unsupported platforms/worlds, missing DLLs and backend errors use batched Godot bodies; the grave menu can toggle the backend. Native Windows x64 source/pin/license, benchmark scope and validation are documented in `Docs/Box3dCorpses.txt` and `Docs/CorpseOptimization.txt`.
- Combat radius/segment/nearest queries use the crowd grid. Custom capsule tests order individual hits against the world-block distance; legacy targets use their actual capsule center. Candidate snapshots and queued crowd deaths prevent mass attacks from skipping shifted targets. Detailed physics bodies remain in the legacy FPS sandbox.
- `Scripts/Fps/FpsVerticalSlice.DefenseRun.cs` owns run currency, bounded/merged loot pickups, weapon/modulator collection, appearing shop windows, shop purchases, and live run weapon assembly. It does not write inventory saves.
- `Scripts/Fps/FpsDefenseCore.cs` owns the core collision, damage, repair, and destruction event. Enemies select core/player targets through the director; projectiles can damage the core on collision.
- `FpsEnemySpawnSettings.Count`, formation columns, spacing, and cardinal lane define each wave's groups. Closed lanes reject their spawn groups.
- Shops hold the preparation timer between waves. During combat, only the defender's input is held while the shop is open; enemies and ongoing weapon effects continue.
- The catalog retains 99 active modulators plus retired Apex Doctrine for saved-copy lookup. Active parts have FPS runtime support. `ModulatorStats` owns footprint/range/output helpers and import validation; `ModulatorPassives` handles health, movement, dodge, regeneration, lifesteal, and Luck; `ModulatorSpecials` owns 16 transforms and guarded procs; `ModulatorWings` owns the four support firing behaviors and guidance. `WeaponModulatorCompatibility.CanApplyToWeapon` combines tags, delivery capabilities and exact transform matching for hub, run, FPS shop/debug/import and inspection; contextual slot capacity and Wing limits remain with their callers. The existing FPS eligibility matrix is preserved.
- Mobius base echo records explicitly represent one already-spread output and replay it once through `FireTemporalEcho`. Generic on-fire echo records retain full-pattern replay. This removes squared multishot scaling while preserving source, timing, damage and nonrecursive scheduling.
- `FpsModulatorPresentation` explains installed effects and conditional behavior without mutating items. It covers selected-only player/reactor bonuses, paused selected Wings, Apex's retirement, Mimic fallback/crit/proc restrictions, sustained ticks, and shape-specific extra-output/duration behavior. The inspector refreshes these descriptions when equipping without changing selection and uses the actual runtime base cooldown for Mobius/Klein.
- Square fires moving bullets with speed, size, piercing, and physical wall reflections. Ricochets find nearby visible targets. Damage rolls crits once on primary hits, uses actual removed health for lifesteal, and prevents secondary effects from feeding their own proc chains. Radius checks measure against upright enemy capsules. Pulls and knockback use bounded, wall-safe `IFpsCombatEnemy.MoveBy`.
- `FpsVerticalSlice.WeaponPolish` connects all sixteen attacks to geometric projectiles, actual target-linked arcs, mine arming/detonation, mortar flight/impact, fields, delayed echoes, orbital guards, and clone shots without changing gameplay positions, damage, timing or the combat RNG. The `FpsWeaponPolishEffects` partials own three shared MultiMeshes capped at 384 transient fragments/trails, 1,024 persistent segments, and 128 faceted solids, plus 32 standalone burst records. Emission credits and distance culling limit transients; complete fields fall back to their original mesh when either required batch fills. Existing zero-damage pulse records retain their modulator state and render through the batches. New command-cluster flashes use the bounded burst pool. Nonuniform scale is applied in local axes; near-camera geometry is clipped or suppressed. All effect lifetimes advance on combat physics ticks and clear on run reset.
- `FpsWeaponFireFeedback` reuses distinct muzzle geometry under all sixteen authored anchors and duplicates only each instance's accent/insert materials for pulses. Existing imported `Fire` clips remain the mechanism animation. Cosmetic muzzle offsets converge onto camera-based projectile paths; stopping-hit trails end at the existing capsule intersection. `FpsWeaponPolishAudio` caches 49 procedural PCM streams and pools four local/eight spatial voices, with rate/distance limits, explicit combat pause, and existing SFX/Master volume routing. Explosions have four additional close/far pairs; identical clips use one player with native distance filtering, distinct clips crossfade. `FpsExplosionAudioSettings` supplies six slots and radius thresholds at 3/8 m; `DamageEnemiesInExplosion` and the Volitile hit scope pass final damage radius and suppress victim impact audio without changing damage/proc rules. Missing small/large recordings temporarily use the supplied medium clip. Continuous field/aura ticks retain their own cues. The original nine PCM buffers are regression checked. The grave menu exposes effects, sound, and transient-detail controls; low detail preserves active field boundaries. Configuration and native audio evidence: `Docs/ExplosionAudio.txt`.
- `ValidateWeaponPolish` compares per-target damage and each 60 Hz damage timeline for all sixteen weapons with effects on/off, checks actual mine/echo/clone/passive paths, and exercises cosmetic capacities, lifecycle, authored-material isolation, and actual audio playback/volume routing. Native capture validates geometry through real MultiMesh readback, unavailable with the headless dummy renderer. Fifty-five rendered images cover every equipped weapon and matched 2,000-enemy compact-fixture samples; the fixture restores crowd records, RNG and accumulator before each budget. The stress route fires all sixteen shapes at a forced cadence with 60 warmup and 480 sample frames per budget. Visual capture freezes effect age for screenshots. Short sequential VSync samples do not measure isolated GPU cost or sustained fort combat; synthesized audio playback is checked automatically, listening quality remains a review task.
- Passive stat bridging in `StatGenerator` respects triggers and resolved quality, including Luck. Retired Apex is skipped and grants no amplification. Luck scales coin/drop yields; fractional coins carry across deaths to preserve small bonuses. Health retuning preserves the wounded health ratio and never revives a dead player.
- Hub loadout items are cloned into the run arsenal. FPS-incompatible installed parts move into recovered run parts, preserving saved items. Wings operate from support slots after slot zero and skip the currently selected item. The Wing firing guard preserves support on-fire effects while preventing recursive escort volleys.
- `FpsWeaponVisualFactory` selects complete meter-scale GLBs in `Resources/Weapons` for all sixteen catalog shapes. Rarity materials belong to each instance; all ten bays use actual unlocks and installed parts. `ExportMultiWeaponRuntime.py` exports the fifteen generated Blender sources with every socket variant and a sampled `Fire` animation, preserving anchors attached to moving mechanisms. Successful `FpsPlayerController.TryConsumeFireRequest` calls play that cosmetic mechanism at the firing cadence; completion restores the authored rest pose. Eight per-shape `ViewModelPose` profiles fit the redesigned bodies in first person with smooth hip/aim offsets and cached rest-bounds centering. `ViewModelBasePosition` and `ViewModelScale` remain editor controls; gameplay shot origins and directions remain camera-based. Mobius disables generated mesh LODs to preserve its layered ribbon surfaces.
- `FpsVerticalSlice.WeaponInspection` owns Tab/pause/menu restoration. `FpsVerticalSlice.Inventory` validates exact run-owned references, inspected target, socket capacity/compatibility and Wing constraints before atomic storage/install/remove/equip operations. `FpsWeaponSocketLayout` maintains a weak per-item ten-slot presentation map over the existing dense modulator list; removals preserve other physical sockets without changing save schemas. `FpsInventoryWeaponStats` reads FPS delivery-specific damage, cadence, range and applicable modifier counts without consuming fractional attack state.
- `FpsWeaponInspectionUI` replaces the FPS inspector with searchable shape/cartridge grids, a live model viewport, projected physical socket drop targets, a ten-slot dock and focused stats/details. Godot drag payloads include owner/session and exact item references; cancellation is mutation-free, and drops revalidate against runtime state. Static thumbnails render once. Closing disables all viewports, settles a committed transformation and restores the previous menu/combat state. First-person models use the same physical socket map.
- `FpsWeaponWorkbenchAnimations` advances imported Blender clips manually while combat is paused: old Disassemble followed by new Assemble, plus per-socket seat/remove clips. It restores instance rest transforms and pins the shape core; camera fitting uses rest bounds and a stable core projection. All sixteen runtime GLBs preserve existing meshes/materials and fifteen original Fire clips (Square had no Fire clip). Derived editable sources in Godot-ignored `Art/WeaponWorkbenchAnimations` leave approved `Art/WeaponConcepts` files intact. Art scripts append animation data and validate original source hashes and endpoint poses.
- Inspection validation passes 1,222 headless / 1,225 native checks covering all sixteen authored models, assembly clips, twenty socket clips per gun, firing mechanisms, independent instances, sparse sockets, atomic invalid drops, real GUI transactions, pause restoration and unchanged hub saves. Headless explicitly skips OS hover/drag assertions and tests the actual drop contracts; `RunWeaponInspectionValidation.ps1 -Native` exercises the real GUI path. Capture supports `-Workbench -Resolution 1280x720` or `1600x1000`, and defaults to all sixteen hip/aim/fire/inspector views. `-Shapes 'hypercone,pentachoron'` narrows the render review.
- `Tools/Validation/LegacyHillFixture.cs` supplies the former 200 m roofed map for compact combat, piling, rooftop and underside regressions. `ValidateHillHorde` and historical captures use isolated saves under `.tmp/hill-horde-validation`; `ValidateNorthGate` retains lane/shop/wave coverage against `NorthGateLaneSandbox.tscn`. These fixtures do not validate the expanded fort's scale or four approaches.
- `ValidateFortDefense.tscn` / `RunFortDefenseValidation.ps1` passes 80 checks: dimensions/slope, player ramp and wall-walk access, eight downward-firing LOS checks at platform outer edges, four gates, melee/projectile damage and breaches, rebuild preservation, four-way populations, and three actual Fort-tab health/resource-isolation/restore tests. `CaptureFortDefense.tscn` / `RunFortDefenseCapture.ps1` writes inspected views to `.tmp/fort-defense-capture`. First gate hit is at 37.33 simulated seconds; four seconds later 412 bodies are raised, maximum lift is 5.59 m, highest feet are 13.55 m versus the 14.096 m wall top, and gates have 660–685 HP. Wall overrun remains unverified. Its short 180-frame, 1600×900 VSync four-approach sample with 2,000 cubes measured frame median 23.94 ms / p95 28.10, separately from crowd CPU median 19.03 ms / p95 25.60 on i7-13700H/RTX A1000. It excludes sustained firing and corpse-heavy combat; compact-map figures remain separate.
- `Tools/Validation/ValidateFpsModulators.cs` covers all 99 active catalog entries through compatible run installation plus retirement of Apex and checks real combat/stat behaviors, transforms, Wings, wall bounces, source context, and imports. `WeaponCompatibilityChecks` independently specifies all 100-by-16 expected installations and exercises the actual hub and FPS gates; targeted Mobius tests cover one/three outputs, source, delay, spread and no recursive replay. Its runner isolates saves under `.tmp/fps-modulator-validation`; the current suite passes 484 checks, including the complete 1,600-pair gate comparison. This does not test the balance of every pair. Optional inspector `-Modulators -QualityWorkshop` captures demonstrate quality explanations, actual fusion, comparisons and the victory workshop.
- `FpsVerticalSlice.TuningMenu.cs` builds the grave-key live gameplay menu. Early `_Input` handling keeps grave/Escape available when numeric fields have keyboard focus. Menu visibility gates player controls separately from crowd simulation and links to the paused stat/loadout panel. Its Fort tab retunes gate HP from 1–20,000 without healing or closing breaches, toggles gate invulnerability, and restores all gates to full HP and collision.
- `FpsVerticalSlice.TuningRuntime.cs` copies tuning resources per run, implements manual Add/Replace/Clear up to allocated capacity (100,000 in North Gate), and samples wall-clock frame intervals separately from `LastSimulationMilliseconds`. The Population tab has presets through 100,000. A shared occupancy hash checks actual body radii during Add; terrain space can limit oversized-body populations. Manual tests hold encounter wave flow; real deaths still use the existing loot path. `FpsHordeManager.ApplyArchetypes` preserves handles, wounded health fractions, and original wave scaling while retuning living slots.
- GPU body transfers cover the cached highest live slot, including holes, rounded from 256 slots up to allocated capacity, with growth and quarter-size shrinkage. The contact hash remains sized to reserved capacity for CPU/GPU neighbor-order parity. `FpsHordeManager.ActiveSlots.cs` skips empty 64-slot blocks while preserving ascending live-slot order, including callback kills/spawns. Spawn/death/Clear and probe restore maintain occupancy and highest-slot bookkeeping; GPU packing still writes the complete prefix.
- `FpsHordeManager.Rendering.cs` selects GPU poses or CPU bulk fallback and independently grows/shrinks role MultiMeshes. Both paths preserve interpolation, rolling, ceiling caps, flashes and local transforms. `RunHordeRenderValidation.ps1` compares actual GPU buffers to independent prior pose math and checks CPU/GPU screen images, native cache transitions, live settings, damage, spawning and restoration. `FpsVerticalSlice.CrowdWorld.cs` combines CPU movement constraints/support/ceiling sampling, reusing terrain and swept candidates when XZ is unchanged; slides and gate corrections trigger rechecks. Existing callbacks remain available for legacy worlds and parity testing.
- `ValidateLargeHorde` / `RunLargeHordeValidation.ps1` covers actual 100,000-enemy menu operations, mixed roles, bounded nonoverlapping formations, full/near-full Add, exact record/render counts, clear/generation safety, prefix coverage/parity and first-wave isolation. Logs are isolated under `.tmp/large-horde-validation`; this short run establishes operation, not sustained FPS. Historical CPU pose publication at 100k cost about 100-114 ms in Debug; current GPU measurements are recorded separately in `Docs/GpuCrowdInvestigation.txt`.
- `ValidateHordeTuning` covers manual/configured 2,000-enemy spawning, UI commands, focused-field hotkeys, live simulation/archetype controls, isolated resources, invulnerability, and damage/rewards. `CaptureHordeStress` generates a rendered menu/crowd review and short count-specific approach timings, plus separate simulation-only travel and staged doorway diagnostics. These do not establish sustained combat FPS.
- `Tools/Experiments/GpuCrowd/ProfileHordeFrameCost.cs` and `RunHordeFrameProfile.ps1` separate CPU publication, simulation and viewport GPU drawing at 1080p with 10k/25k/50k/100k cubes. Every live/hidden/frozen condition measures at least 90 frames and three seconds, with restored starting snapshots, actual simulation cadence, p95/p99/max, backend assertions and scene captures. Optional CPU-render/CPU-drive controls and asynchronous pose GPU timestamps isolate costs. See `Docs/GpuCrowdInvestigation.txt` for hardware, results and historical 1600x900 profiles. These no-firing diagnostics do not establish sustained combat FPS or the GTX 1060 / RX 580 target.
- Before fort expansion, `ValidateHordePiling` passed 39 checks covering a natural 2,000-enemy roof-reaching pile, body/support toggles, deaths releasing gravity, roof-edge falls, pause, raised shots, and vertical melee range. These remain compact-fixture regressions. `CaptureHordePiling` records natural formation screenshots and motion; normal capture measures rendered intervals, while `-ProfileOnly` records headless phase timing. Both use isolated saves under `.tmp`; fort performance requires a separate capture.

## Weapon Combat Flow

### 1. Targeting and range decisions

- `Scripts/Combat/WeaponTargetingRuntime.cs`
- Chooses functional target range, projectile range, engage range, and per-item projectile speed/range helpers.
- Also contains helper accessors for item-derived projectile count, crit chance, crit damage, and newer passive multipliers such as projectile speed and homing strength.

### 2. Weapon stat assembly and spawn

- `Scripts/Combat/WeaponSpawnRuntime.cs`
- `BuildLegacyWeaponStats()` collects the effective shot package for a fire event:
  - damage
  - radius/size
  - life/range scaling
  - projectile speed
  - projectile count
  - pierce count
  - homing turn rate
  - source item / source rarity / source damage
- `FireLegacyWeaponPattern()` routes by `WeaponKind` into the concrete spawn behavior.
- Persistent effect state for mines, beams, mortars, orbitals, chains, voids, clones, and echoes is also owned here.

### 3. Ongoing effect ticking

- `Scripts/Combat/WeaponEffectTickRuntime.cs`
- `TickLegacyWeaponEffects()` is the central tick dispatcher for ongoing spawned effects.
- This file owns homing steering, beam ticks, mine arming, novas, flames, rifts, mortars, chains, drills, voids, wrath aura, clones, echoes, modulator transient state, and cleanup.

### 4. Damage application

- `Scripts/Combat/WeaponDamageRuntime.cs`
- `DealDamageToEnemy()` and `DealDamageToBoss()` are the central damage sinks.
- These methods apply:
  - modulator damage multipliers
  - damage numbers
  - life steal
  - volatile explosions
  - equipment on-hit behavior
  - modulator on-hit effects
- Radius, segment, and cone helpers also live here and feed into those sinks.

## Equipment And Modulator Flow

- `ModulatorQuality` resolves cached detached effects from saved raw baselines at 0.5/1/1.5/2 strength. Beneficial deltas scale, penalties and defining trigger/topology parameters stay fixed. `WeaponModulatorData` retains the numeric legacy Flawed value with Broken presentation. `InventoryManager` preserves known-key raw payloads and quality; it never serializes resolved strength back as baseline.
- `ModulatorLoot` selects effect rarity first (70/25/5), then within-tier effect weight and independent quality (60/30/9/1). Its immutable profile accepts future skill bonuses. `ModulatorFusion` preflights exact loose copies and coins, preserves raw effects/rarity, and produces the next quality atomically. `FpsVerticalSlice.ModulatorWorkshop` owns shop actions, return/storage purchases, once-per-wave rewards and final-victory access; FPS loot stays run-local.
- `ModulatorQualityText` formats actual combat values and comparisons for FPS and hub/legacy displays. Fractional FPS count bonuses carry per source/purpose; legacy count consumers use a weak-source carry table. Nonstacking transforms/Cascade/Omni select the strongest quality. New `ValidateModulatorQuality` covers 2,321 catalog, scaling, legacy-count, persistence, seeded-loot, fusion and presentation contracts. Full rules and coin costs are in `Docs/ModulatorQuality.txt`.

### Equipment stat snapshot

- `Scripts/Combat/EquipmentModifierRuntime.cs`
- `RefreshEquipmentLoadout()` rebuilds runtime equipment state.
- `RebuildEquipmentStatSnapshot()` is the key passive aggregation pass.
- Flat item substats still feed legacy buckets.
- Modulator passive effects are resolved per installed modulator via `GetResolvedEffects()` and then either:
  - convert through `TryConvertToSubstatRoll()`, or
  - apply direct runtime handling for effect types like projectile speed, homing strength, and pierce.

### Generic modulator model

- `Scripts/Equipment/Data/WeaponModulatorData.cs`
- `Scripts/Equipment/Data/WeaponModulatorEffectData.cs`
- `WeaponModulatorData` owns high-level modulator identity, power tier, tags, install restrictions, tier presentation, and resolved effects.
- `WeaponModulatorEffectData` is the generic effect payload used for passive, on-fire, and on-hit modulator behavior.

### Advanced modulator runtime

- `Scripts/Combat/WeaponModulatorEffectRuntime.cs`
- Handles first-wave non-passive Tier 3 behavior.
- Current live examples include:
  - `EchoAfterDelay` on fire
  - `ApplyMark` on hit
  - `ChainToNearby` on hit
  - `LingeringZone` on hit
  - `ExecuteLowHealth` damage multiplier
  - `EliteBossHunter` damage multiplier
- Secondary damage/tick paths preserve source-item scaling but avoid recursive proc loops.

## Content Definition Paths

### Equipment catalog defaults

- `Scripts/Equipment/Data/EquipmentCatalogDefaults.cs`
- Defines fallback shapes, rarities, and modifiers.
- Useful when a task needs canonical shape keys, attack types, rarity ladders, or default content assumptions.

### Modulator catalog

- `Scripts/Equipment/Data/Modulators/ModulatorCatalog.cs`
- Resource-backed loading is attempted first.
- If no modulator resources are found, `CreateCodeFallbackTemplates()` assembles live content from partial catalogs.
- Current fallback catalog slices include core, projectile, sustain, utility, Tier 2, proc, advanced/Tier 3, and wing modulators.

## Persistence And Hub Integration

- `Scripts/Managers/InventoryManager.cs`
- Loads and saves inventory state, equipped items, stored essences, and stored modulators.
- The manager is also used as a lightweight equipment service for hub/debug flows.
- Current schema version is `6`.

## Practical Navigation Shortcuts

- Change passive stat behavior: start in `Scripts/Combat/EquipmentModifierRuntime.cs`.
- Change item-level stat math or legacy substat totals: start in `Scripts/Equipment/StatGenerator.cs`.
- Change where a weapon fires or how a weapon effect spawns: start in `Scripts/Combat/WeaponSpawnRuntime.cs`.
- Change targeting/range/projectile-speed logic: start in `Scripts/Combat/WeaponTargetingRuntime.cs`.
- Change damage or hit-triggered behavior: start in `Scripts/Combat/WeaponDamageRuntime.cs`.
- Change ticking or lifetime behavior of persistent effects: start in `Scripts/Combat/WeaponEffectTickRuntime.cs`.
- Change advanced modulator proc behavior: start in `Scripts/Combat/WeaponModulatorEffectRuntime.cs`.
- Change persistence or schema behavior: start in `Scripts/Managers/InventoryManager.cs`.
