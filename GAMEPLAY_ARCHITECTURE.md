# Gameplay Architecture

This file maps the main gameplay control paths so fresh sessions can jump directly to the owning code.

## Top-Level Shape

- Core runtime behavior is spread across `Main` partial classes under `Scripts/` and `Scripts/Combat/`.
- Combat, weapon spawning, ticking, targeting, damage, and advanced modulator behavior are all implemented as partial methods on `Main`.
- Inventory and equipped-item persistence live outside `Main` in `Scripts/Managers/InventoryManager.cs`.

## First-Person Horde Defense

- `Scenes/Fps/NorthGate.tscn` is the first defense map, launched from the hub. It shares `FpsVerticalSlice` combat with the original FPS sandbox.
- `Scripts/Fps/FpsVerticalSlice.DefenseMap.cs` builds the hill, summit room with north doorway, core, and shop. Terrain triangles and custom crowd grounding share the same height function. The earlier lane geometry remains available when `HillMapSettings` is absent.
- `FpsHillMapSettings`, `FpsDefenseSettings`, and `FpsDefenseLaneSettings` define editor-tunable terrain, room, cardinal approach, core, loot, and shop settings. Current map resources live in `Resources/Fps/HillFort_*.tres`.
- `Scripts/Fps/FpsHordeManager.cs` stores enemies in preallocated arrays indexed by generation-safe handles. It owns the XZ grid, fixed-rate movement, bounded neighbor separation, targeting, queued deaths, and cube/pyramid MultiMeshes. It creates no per-minion scene nodes. The director ticks simulation before weapon effects and flushes deaths afterward.
- `FpsVerticalSlice.Horde.cs` connects terrain height, doorway routing, wall constraints, attack line-of-sight, loot, and wave completion. `IFpsCombatEnemy` and its plain crowd wrapper let existing weapon/modulator paths preserve enemy identity across batched rendering.
- Combat radius/segment/nearest queries use the crowd grid. Custom capsule tests order individual hits against the world-block distance; legacy targets use their actual capsule center. Candidate snapshots and queued crowd deaths prevent mass attacks from skipping shifted targets. Detailed physics bodies remain in the legacy FPS sandbox.
- `Scripts/Fps/FpsVerticalSlice.DefenseRun.cs` owns run currency, bounded/merged loot pickups, weapon/modulator collection, appearing shop windows, shop purchases, and live run weapon assembly. It does not write inventory saves.
- `Scripts/Fps/FpsDefenseCore.cs` owns the core collision, damage, repair, and destruction event. Enemies select core/player targets through the director; projectiles can damage the core on collision.
- `FpsEnemySpawnSettings.Count`, formation columns, spacing, and cardinal lane define each wave's groups. Closed lanes reject their spawn groups.
- Shops hold the preparation timer between waves. During combat, only the defender's input is held while the shop is open; enemies and ongoing weapon effects continue.
- All 100 fallback modulators have FPS runtime support. `ModulatorStats` owns footprint/range/output helpers and import validation; `ModulatorPassives` handles health, movement, dodge, regeneration, lifesteal, and Luck; `ModulatorSpecials` owns 16 transforms and guarded procs; `ModulatorWings` owns the four support firing behaviors and guidance. Shop/debug installation uses the FPS compatibility gate.
- Square fires moving bullets with speed, size, piercing, and physical wall reflections. Ricochets find nearby visible targets. Damage rolls crits once on primary hits, uses actual removed health for lifesteal, and prevents secondary effects from feeding their own proc chains. Radius checks measure against upright enemy capsules. Pulls and knockback use bounded, wall-safe `IFpsCombatEnemy.MoveBy`.
- Passive stat bridging in `StatGenerator` respects effect triggers and Apex scaling. Unsupported legacy Luck rolls are handled explicitly in FPS. Luck scales coin/drop yields; fractional coins carry across deaths to preserve small bonuses. Health retuning preserves the wounded health ratio and never revives a dead player.
- Hub loadout items are cloned into the run arsenal. FPS-incompatible installed parts move into recovered run parts, preserving saved items. Wings operate from support slots after slot zero and skip the currently selected item. The Wing firing guard preserves support on-fire effects while preventing recursive escort volleys.
- `Tools/Validation/ValidateHillHorde.cs` covers the live hill/crowd scene; its runner redirects saves into `.tmp/hill-horde-validation`. `CaptureHillHorde` produces rendered previews plus a diagnostic explicitly limited to crowd simulation time. `ValidateNorthGate` preserves earlier lane/shop/wave regression coverage against `NorthGateLaneSandbox.tscn`.
- `Tools/Validation/ValidateFpsModulators.cs` covers every catalog entry through compatible run installation and checks real combat/stat behaviors, transforms, Wings, wall bounces, source context, and imports. Its runner isolates saves under `.tmp/fps-modulator-validation`; the current suite passes 417 checks.

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
