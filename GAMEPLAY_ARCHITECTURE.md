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
- `FpsVerticalSlice.Horde.cs` connects terrain height, gate/doorway routing, wall constraints, attack line-of-sight, loot, and wave completion. `IFpsCombatEnemy` and its plain crowd wrapper preserve enemy identity across batched rendering. The normal wave has 100 cubes split 25 per cardinal sector; manual up-to-2,000 populations use four bounded exterior sectors.
- `FpsHordeManager.Contacts.cs` resolves soft rounded body contacts, pressure-driven climbing, gravity and current lower-body support using bounded synchronous passes. `ContactGrid.cs` provides a preallocated 3D spatial hash so upper/lower layers do not crowd the weapon-query XZ buckets. Soft contacts replace the legacy avoidance pass while enabled. There are no per-enemy rigid bodies; the default is one contact pass at 30 Hz, with live 1–4 pass tuning.
- `SampleHillCrowdSupportHeight`, `SampleHillCrowdMaxFootHeight`, and `ConstrainHillCrowdBodyMovement` share world dimensions with physical geometry. Support requires arrival from above, preserving under-roof passage in the compact regression fixture; the expanded default interior is open sky. Fort wall walks, platforms, ramps, walls, and closed gates must agree between physics, crowd constraints, and support sampling. Leaving support resumes gravity. Climbing limits gate new ascent rather than removing existing contacts. Attacks evaluate final 3D body positions; hit capsules and render instances follow those same records.
- Unsupported airborne neighbors cannot mutually climb at the same height and cancel gravity. Climbing requires world/lower-body support or a genuinely lower neighbor; this prevents ceiling-underside crawling while preserving rooftop access.
- `FpsHordeManager.Rolling.cs` supplies a distance-driven cube gait: quarter turns per width, short actual movement pauses, and speed compensation while rolling. Only the MultiMesh pose rotates and lifts; combat/contact bounds remain upright. Pose interpolation freezes with simulation, and dead bodies retain the current visual transform.
- `DealDirectionalDamageToEnemy` carries actual hit travel/source directions through every weapon and secondary-proc path to `IFpsCombatEnemy.ApplyDamageFrom`. `FpsHordeDeath` captures fatal damage, excess over remaining health, role/color and visual pose before slot reuse. Deferred `EnemyDefeated` creates a corpse separately from the original `EnemyDied` reward notification.
- `FpsEnemyCorpsePool` reuses up to 64 single-body ragdolls by default, capped at 256. Bodies collide only with world geometry, exclude player/core, and use collision layer zero so weapon queries ignore them. Launch speed is base plus square-root overkill scaling with a cap and small upward boost. Lifetime runs on director physics ticks, pauses preserve velocity, and clear/restart removes the cosmetic state without rewards. Death and rolling settings remain run-local and editable in the grave menu.
- Combat radius/segment/nearest queries use the crowd grid. Custom capsule tests order individual hits against the world-block distance; legacy targets use their actual capsule center. Candidate snapshots and queued crowd deaths prevent mass attacks from skipping shifted targets. Detailed physics bodies remain in the legacy FPS sandbox.
- `Scripts/Fps/FpsVerticalSlice.DefenseRun.cs` owns run currency, bounded/merged loot pickups, weapon/modulator collection, appearing shop windows, shop purchases, and live run weapon assembly. It does not write inventory saves.
- `Scripts/Fps/FpsDefenseCore.cs` owns the core collision, damage, repair, and destruction event. Enemies select core/player targets through the director; projectiles can damage the core on collision.
- `FpsEnemySpawnSettings.Count`, formation columns, spacing, and cardinal lane define each wave's groups. Closed lanes reject their spawn groups.
- Shops hold the preparation timer between waves. During combat, only the defender's input is held while the shop is open; enemies and ongoing weapon effects continue.
- All 100 fallback modulators have FPS runtime support. `ModulatorStats` owns footprint/range/output helpers and import validation; `ModulatorPassives` handles health, movement, dodge, regeneration, lifesteal, and Luck; `ModulatorSpecials` owns 16 transforms and guarded procs; `ModulatorWings` owns the four support firing behaviors and guidance. Shop/debug installation uses the FPS compatibility gate.
- Square fires moving bullets with speed, size, piercing, and physical wall reflections. Ricochets find nearby visible targets. Damage rolls crits once on primary hits, uses actual removed health for lifesteal, and prevents secondary effects from feeding their own proc chains. Radius checks measure against upright enemy capsules. Pulls and knockback use bounded, wall-safe `IFpsCombatEnemy.MoveBy`.
- Passive stat bridging in `StatGenerator` respects effect triggers and Apex scaling. Unsupported legacy Luck rolls are handled explicitly in FPS. Luck scales coin/drop yields; fractional coins carry across deaths to preserve small bonuses. Health retuning preserves the wounded health ratio and never revives a dead player.
- Hub loadout items are cloned into the run arsenal. FPS-incompatible installed parts move into recovered run parts, preserving saved items. Wings operate from support slots after slot zero and skip the currently selected item. The Wing firing guard preserves support on-fire effects while preventing recursive escort volleys.
- `FpsWeaponVisualFactory` selects complete meter-scale GLBs in `Resources/Weapons` for all sixteen catalog shapes. Rarity materials belong to each instance; all ten bays use actual unlocks and installed parts. `ExportMultiWeaponRuntime.py` exports the fifteen generated Blender sources with every socket variant and a sampled `Fire` animation, preserving anchors attached to moving mechanisms. Successful `FpsPlayerController.TryConsumeFireRequest` calls play that cosmetic mechanism at the firing cadence; completion restores the authored rest pose. Eight per-shape `ViewModelPose` profiles fit the redesigned bodies in first person with smooth hip/aim offsets and cached rest-bounds centering. `ViewModelBasePosition` and `ViewModelScale` remain editor controls; gameplay shot origins and directions remain camera-based. Mobius disables generated mesh LODs to preserve its layered ribbon surfaces.
- `FpsVerticalSlice.WeaponInspection` owns pause/menu restoration and explicit run-arsenal equip; `FpsWeaponInspectionUI` shares the same factory for isolated, interactive model/shape previews and suspends their rendering while closed. Inspection uses authored dimensions independently of first-person presentation. Blender sources and static review exports remain in the Godot-ignored `Art/WeaponConcepts` folder. Weapon inspection validation passes 1,054 checks covering all sixteen authored shapes, fifteen mechanisms, rarity/bay states, instance independence, actual controller hip/aim presentation and save-safe preview/equip. Its capture runner defaults to all sixteen shapes and records hip, aim, actual firing, inspector and available mechanism views, asserting successful shots start imported animations. `-Shapes 'hypercone,pentachoron'` selects a smaller rendered review.
- `Tools/Validation/LegacyHillFixture.cs` supplies the former 200 m roofed map for compact combat, piling, rooftop and underside regressions. `ValidateHillHorde` and historical captures use isolated saves under `.tmp/hill-horde-validation`; `ValidateNorthGate` retains lane/shop/wave coverage against `NorthGateLaneSandbox.tscn`. These fixtures do not validate the expanded fort's scale or four approaches.
- `ValidateFortDefense.tscn` / `RunFortDefenseValidation.ps1` passes 80 checks: dimensions/slope, player ramp and wall-walk access, eight downward-firing LOS checks at platform outer edges, four gates, melee/projectile damage and breaches, rebuild preservation, four-way populations, and three actual Fort-tab health/resource-isolation/restore tests. `CaptureFortDefense.tscn` / `RunFortDefenseCapture.ps1` writes inspected views to `.tmp/fort-defense-capture`. First gate hit is at 37.33 simulated seconds; four seconds later 412 bodies are raised, maximum lift is 5.59 m, highest feet are 13.55 m versus the 14.096 m wall top, and gates have 660–685 HP. Wall overrun remains unverified. Its short 180-frame, 1600×900 VSync four-approach sample with 2,000 cubes measured frame median 23.94 ms / p95 28.10, separately from crowd CPU median 19.03 ms / p95 25.60 on i7-13700H/RTX A1000. It excludes sustained firing and corpse-heavy combat; compact-map figures remain separate.
- `Tools/Validation/ValidateFpsModulators.cs` covers every catalog entry through compatible run installation and checks real combat/stat behaviors, transforms, Wings, wall bounces, source context, and imports. Its runner isolates saves under `.tmp/fps-modulator-validation`; the current suite passes 417 checks.
- `FpsVerticalSlice.TuningMenu.cs` builds the grave-key live gameplay menu. Early `_Input` handling keeps grave/Escape available when numeric fields have keyboard focus. Menu visibility gates player controls separately from crowd simulation and links to the paused stat/loadout panel. Its Fort tab retunes gate HP from 1–20,000 without healing or closing breaches, toggles gate invulnerability, and restores all gates to full HP and collision.
- `FpsVerticalSlice.TuningRuntime.cs` copies tuning resources per run, implements bounded manual Add/Replace/Clear up to 2,000 enemies, and samples wall-clock frame intervals separately from `LastSimulationMilliseconds`. Manual tests hold encounter wave flow; real deaths still use the existing loot path. `FpsHordeManager.ApplyArchetypes` preserves handles, wounded health fractions, and original wave scaling while retuning living slots.
- `ValidateHordeTuning` covers manual/configured 2,000-enemy spawning, UI commands, focused-field hotkeys, live simulation/archetype controls, isolated resources, invulnerability, and damage/rewards. `CaptureHordeStress` generates a rendered menu/crowd review and short count-specific approach timings, plus separate simulation-only travel and staged doorway diagnostics. These do not establish sustained combat FPS.
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
