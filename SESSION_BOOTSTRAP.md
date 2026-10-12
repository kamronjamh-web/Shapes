# Session Bootstrap

Read this file and LAST_CHANGES.md at the start of a fresh session.

## Repo Snapshot

- Project type: Godot 4 C# game project.
- User delegation preference: use up to four delegated GPT-6.1 Sol agents at medium reasoning until the user changes this preference (subject to available concurrent slots).
- Main solution/build target: `SuperGatchaRebuild.csproj`.
- Fast validation command: `dotnet build "SuperGatchaRebuild.csproj"`.
- Build success is useful, but combat/content changes still need a Godot runtime playtest when behavior matters.
- Weapon inspection now binds `FpsWeaponInspectionUI` to the player's actual camera/world via `FpsWeaponInspectionUI.World.cs`. Tab raises a perspective model over the visible arena; the regular viewmodel/HUD hide until close. Socket selection plus a loose-part click installs, and `SocketAction` removes the selected part. Drag transfers are optional. Gameplay inspection disables the separate weapon preview viewport, preserves existing pause/shop return behavior, and fits resting geometry without reframing animated parts. `RunWeaponInspectionValidation.ps1` and the capture runner cover this presentation and inventory workflow.
- MultiWeapon now uses Blender models for all sixteen shapes, including Cone and the eight redesigned 3-/4-star forms. `FpsWeaponVisualFactory` shares the complete `Resources/Weapons` exports between first person and inspection; actual rarity and modulator data customize each instance. Successful shots play the fifteen imported mechanism animations. The redesigned bodies have individual hip/aim poses centered on their resting bounds; inspection retains authored metre-scale geometry. Runtime exports regenerate with `Tools/Art/ExportMultiWeaponRuntime.py` using Microsoft Store `blender-launcher.exe`; concept GLBs are static review snapshots. Weapon inspection: 1,054 checks; modulator regression: 417 checks; rendered hip/aim/fire/inspection/mechanism views reviewed in `.tmp/weapon-inspection-capture`.
- Editable art sources cover all sixteen shapes. The eight 3-/4-star forms have complete new bodies (specialized tools, wearables and floating artifacts) with shared Square materials and cartridge interfaces. Review `Art/WeaponConcepts/MultiWeaponCollection/Weapons.html`; commands are in `Tools/Art/README.txt`. All eight redesigns passed 720 customization cases, source reopen/motion/reset checks and GLB reimports before runtime integration. Mobius disables generated mesh LODs to preserve its layered ribbon surfaces; keep that import setting when regenerating its runtime GLB.

## Horde Defense Direction and First Map

- NorthGate now loads the approved Stepped Stronghold Blender export with four open 24 m approaches, connected middle terraces, a raised core, four shape-turret mounts and a northeast terrace shop. Editable source: `Art/FortConcepts/SteppedStronghold/SteppedStronghold.blend`; runtime: `Resources/Forts/SteppedStronghold`; exporter: `Tools/Art/ExportSteppedStrongholdRuntime.py`. The 39 box/16 ramp proxies drive player physics, CPU/GPU crowd support and corpse-worker collision. Stable per-enemy lanes and matching CPU/GPU staged routes climb both elevations. Generic settings retain the older fort via `SteppedStrongholdEnabled = false`; old gate regressions use `LegacyExpandedFortFixture`. Shop turrets accept exact owned Square shapes with supported passive modulators; other shapes and slow/barricade/shield/blast defenses remain future work. See `Docs/FortRedesign.txt` for controls, limits and validation.

- Base triangle geometry is now an explicit equilateral plate with flat outward normals and an even rarity outline; the old PrismMesh dimensions emphasized its rectangular side. `RunTriangleShapeValidation.ps1` covers watertight geometry plus native front/back/oblique/side silhouettes (33 headless / 45 native). Ground loot still passes 253 headless / 293 native checks.

- Stronghold integration validation: 15,589 headless / 17,573 native checks; four physical capsule ramp climbs, matched CPU/GPU staged approaches, ranged stopping, support/anchor/rebuild checks. Turret validation: 28 headless / 30 native with real shop and projectile/ownership/repair/pause flows. Legacy GPU crowd remains 82,657 native; gated fort80, spatial45,071 and ground loot253 pass. Captures are under `.tmp/stepped-stronghold-validation/native` and `.tmp/fort-turret-validation/native/capture`. Keep native GPU test runs sequential; concurrent diagnostics encountered driver device loss, isolated final reruns passed. These are correctness checks, not 50k FPS measurements.

- Ground weapon drops are base shapes, with full authored weapon models reserved for equipped presentation. Ground modulators are 0.26 m long (half the old size); installed/thumbnail scale is unchanged. Shared cartridge styling uses 1-3 notches for PowerTier and canonical rarity accent colors in all views, independently of quality. Enemy weapon drops burst outward/upward as temporary world-colliding rigid bodies (64 active cap, 2.5 s lifetime); settled items retain exact data and require F pickup. Pausing freezes flight, successful collection removes its body, and full inventory preserves it. Read `Docs/GroundLoot.txt`.

- Loot pillars now combine two crossed transparent planes and a separate horizontal ground glow in one batched mesh. The original six-frame atlas still blends; upward shader flow, white cores and soft rarity-colored edges add volume. A capped pool of 32 GPU spark emitters covers the nearest items within 45 m, with rarity-scaled count/size/speed; distant beams retain their independent visibility. See `FpsLootPillarRenderer.Sparks.cs` and `Docs/GroundLoot.txt`.

- Run equipment now has five active shape slots, two reserve shapes (swap with the held active shape in Tab inspection), and 100 loose modulators; installed parts remain on their shapes. Enemy equipment and wave rewards stay on the ground until aimed at and collected with F. Currency retains automatic pickup/sweep behavior. Ground equipment survives wave clears and arena rebuilds; full inventory leaves it in place. Inventory drop buttons free capacity, and victory's Collect ground loot button restores field movement without restarting combat. Ground models render within 120 m and a bounded visual budget. Independent GPU billboard pillars use the supplied six-frame atlas with frame interpolation, rarity color/height/width, and remain visible beyond the model distance/budget (normal world occlusion and camera frustum apply). See Docs/GroundLoot.txt.
- Large-map cube rolling now accounts for position precision: a final sub-ULP move no longer traps a cube just short of completing its roll. Both CPU and GPU paths are fixed, and the parent compute shader was updated to trigger include reimport. The native 50k spawn diagnostic goes from 2,410 stalled bodies to all 50,000 moving during the same final one-second interval; this is movement validation, not an FPS benchmark.
- Explosion audio now keeps nearby blasts unfiltered through 24 m and gradually adds the far shelf through 100 m. Per-event pitch varies +/-1.5 semitones through a dedicated cosmetic RNG; paired close/far voices share pitch. See `Docs/ExplosionAudio.txt` for the current audio validation and tuning.
- Explosions and chain reactions should originate primarily from the player and their weapons. Shared explosion audio is implemented: `Resources/Audio/Explosions/DefaultExplosionAudio.tres` has six size/distance slots and radius boundaries at 3/8 m; the supplied medium clip supplies runtime close/far sound, with temporary medium fallback for missing small/large assets. Native audio/combat checks pass 370; details and captures are in `Docs/ExplosionAudio.txt`.
- North Gate's active corpse limit and the supported tuning/inspector maximum are 2,056. Generic settings still default to 64. The pool allocates lazily, recycles oldest bodies at capacity, and retains the four-second lifetime.
- Corpses now share two MultiMeshes. North Gate enables `CorpseWorkerEnabled`, selecting a Windows x64 Box3D worker for known fort worlds; main-thread geometry snapshots follow gate and arena changes, and bounded generation/epoch handoffs preserve recycling and clear. Missing/unsupported native backends fall back to batched Godot bodies. Sleeping corpses retain their final pose without repeated uploads. Read `Docs/CorpseOptimization.txt` for profiling and `Docs/Box3dCorpses.txt` for native packaging and the earlier isolated comparison.
- The game is named `Shapes`. Shapes are both enemies and base weapon identities; modulators are weapon components.
- Enemy shapes drop currency and sometimes weapon shapes or modulators. Appearing shops sell upgrades and equipment during both combat and wave breaks.
- The default stepped fort has open north, east, south, and west approaches; the original one-lane map and gated-fort fixtures remain regression sandboxes.
- The hub's `North Gate` button launches `Scenes/Fps/NorthGate.tscn`; the original `FpsVerticalSlice.tscn` remains a separate sandbox.
- Historical gated-fort fixture (superseded by the stepped default): the expanded layout is a 1,609.344 m square (one square mile total), centered on a clear 200 Ã— 200 m open-sky base at elevation 8 m. Walls are 20 ft / 6.096 m high; four cardinal entry gates are 15 ft / 4.572 m wide with default 1,000 HP each. Exterior terrain descends from the outer walls near 102 m to the hill foot at 220 m. Continuous 3 m wall walks, 8 Ã— 6 m outward firing platforms, and access ramps provide elevated player positions.
- The normal first wave remains 100 cubes, with 25 in each cardinal sector. Manual tests support up to 100,000 enemies across four disjoint exterior sectors. Preparation lasts 20 seconds; `N` skips the remaining preparation. The earlier 12/24/40 lane prototype remains in `Scenes/Fps/NorthGateLaneSandbox.tscn` for regression testing.
- Expanded-fort suite passes 80 checks, including scale/slope, wall/platform/ramp access, eight downward-firing LOS checks from platform outer edges, gate blockage/damage/breaches, rebuild preservation, four-way populations, and three actual Fort-tab health/resource-isolation/restore checks. Overview/gate/access/assault images under `.tmp/fort-defense-capture` were inspected. First gate damage occurs after 37.33 simulated seconds; four seconds later 412 bodies are raised, maximum lift is 5.59 m, highest feet are 13.55 m, and gates have 660â€“685 HP. Wall top is 14.096 m; wall overrun remains unverified.
- Expanded-fort 2,000-cube four-approach diagnostic: at 1600Ã—900 VSync on i7-13700H/RTX A1000, 180 rendered intervals measured median 23.94 ms / p95 28.10 / maximum 31.43; separate crowd CPU samples measured median 19.03 ms / p95 25.60. The short camera-specific sample excludes sustained firing and corpse-heavy combat. Gate-position caching/distance rejection, reused health-bar geometry, collision toggles only on breach/restore, consolidated HUD updates, and skipping unused Wing tracking reduce production spikes while preserving support behavior.
- `FpsDefenseGate` owns physical gate collision, HP, world health display, and destruction notification. Friendly damage defaults off. Grave â†’ Fort provides maximum gate HP (1â€“20,000), gate invulnerability, and Restore all gates. HP edits preserve wounded fractions and breaches; restore closes breaches. The HUD shows north/east/south/west gate health.
- `F` interacts with the shop within range. Shopping holds the preparation clock between waves, while enemies continue advancing during combat shopping.
- Currency, loot, and upgrades currently belong to the run; the equipped hub weapon is cloned. This prototype does not change saved inventory/equipment.
- Current tuning defaults live in `Resources/Fps/HillFort_*.tres`; enemy archetypes reuse `NorthGate_Cube.tres` and `NorthGate_Pyramid.tres`. Run-local resource copies prevent menu edits from changing cached defaults. Hill geometry supports live-apply, grounding objectives/player while preserving core health and equipment. Crowd rates/separation and archetype edits apply live; capacity is allocated at scene start.
- Grave/backtick opens `FpsVerticalSlice.TuningMenu`: live Population/Simulation/Crowd contact/Enemies/Player tabs, presets 100/500/1,000/2,000/10,000/25,000/50,000/100,000, cube/pyramid selection, Add/Replace/Clear, invulnerability, and separate observed frame timing / crowd simulation diagnostics. Player controls are blocked while active combat continues; Escape closes tuning before pausing. The existing F1 paused stat/loadout editor remains available.
- `FpsVerticalSlice.TuningRuntime` owns session-local settings, successful spawn counts, collision-separated bounded exterior formations, silent clear, and manual wave-flow guards. North Gate allocates 100,000 slots while retaining its normal 100-enemy first wave. Large formations use disjoint cardinal wedges; Add checks neighboring occupied cells using actual body sizes. Terrain space can limit populations with oversized bodies. Existing health fractions, handles, wave scales, run currency, and equipment survive archetype retuning; replacing a population removes old attacks without kill rewards.
- Soft contacts and pressure-driven piling are now enabled by default. `FpsHordeManager.Contacts` uses actual Y, gravity and live support; `ContactGrid` adds a preallocated 3D broad phase. The default is one contact pass, 32 contact neighbors and a 10 m climb allowance above terrain. Grave â†’ Crowd contact tunes these live. Disabling climbing preserves existing physical support; steering separation applies only when soft contacts are disabled.
- North Gate enables GPU crowd simulation on Vulkan: known-fort targeting, steering/gait, gravity, movement/world constraints, contacts and rolling run on a local compute device. Both result packets validate before CPU authority changes; attacks/weapons still use synchronous readback. Generic callbacks retain CPU execution and unsupported worlds/settings/renderers or failures use CPU fallback. `FpsFortSpatialIndex` limits nearby structure checks. Grave â†’ Crowd contact toggles GPU use and reports movement/contact backends. Geometry rebuilds and live terrain/gate changes refresh GPU data. Body buffers cover the highest live slot (minimum 256); the hash grid retains reserved capacity for parity. Implementation and scoped measurements are in `Docs/GpuCrowdInvestigation.txt`.
- GPU crowd rendering is independently enabled for North Gate and exposed under Grave â†’ Simulation. Main-device compute writes real MultiMesh buffers, evaluating interpolation, rolling poses, ceilings and flashes once per body. Raw snapshots upload only after state changes; normal pose publication has no readback/forced wait. CPU bulk rendering handles unsupported worlds, non-Vulkan/headless, TAA/temporal upscaling and scene effects requiring motion vectors. Explicit bounds and native cache resets preserve culling and backend transitions; inspect GPU poses through the raw diagnostic readback, not individual CPU MultiMesh getters.
- Roof-underside crawling is prevented by requiring grounded/lower support for airborne climbing; roof-top traversal remains enabled in the compact fixture. The expanded default base is open sky. `FpsHordeManager.Rolling` adds displacement-driven cube quarter turns with 0.055 s step pauses and stable combat bounds. Grave â†’ Movement / deaths adjusts gait, corpse and loot settings.
- Base equipment drops are now 0.5% shapes and 1% modulators (Luck still applies), in both defense resources and defaults. `FpsHordeDeath` preserves fatal source direction and overkill beyond remaining HP. The reusable `FpsEnemyCorpsePool` provides world-colliding tumbling cubes/pyramids with 64-body/4-second defaults, stronger capped overkill launch, pause/resume and clear/restart handling. Projectiles, explosions, cones, chains, orbitals and secondary effects carry their own source directions.
- `RunEnemyMotionValidation.ps1` validates actual bullet/explosion fatal paths, bounded 2,000-death recycling/rewards, corpse physics/lifetime, stop/go rolling, blocked cubes, and unsupported roof-underside falling. `RunEnemyMotionCapture.ps1` records actual rendered rolling/tumbling sequences and metadata under `.tmp/enemy-motion-capture`; visual sequences were inspected.
- The compact fixture's summit walls, lintels and roof have height-aware body constraints. Enemies climb over that room and stand on its roof; leaving supports causes falling. Raised hit capsules and 3D melee range follow authoritative records. Before expansion, `RunHordePilingValidation.ps1` passed 39 dedicated checks; `RunHordePilingCapture.ps1` recorded natural 2,000-body roof traversal and separate render/CPU diagnostics under `.tmp/horde-piling-capture` (`-ProfileOnly` for headless phases).
- Historical compact-map piling capture with cube rolling: ordinary 2,000-cube wave reached the roof after 24 simulated seconds. At 1600Ã—900 VSync on i7-13700H/RTX A1000, 180 rendered intervals measured median 23.25 ms / p95 27.79; 120 separate crowd-only ticks measured median 16.62 ms / p95 18.06. Those captures were inspected before expansion. They do not measure the new fort. Earlier `.tmp/horde-stress` numbers below also predate soft contacts. Sustained firing and corpse-heavy full-combat performance remain unvalidated.
- `FpsHordeManager` owns packed enemy arrays, stable generation handles, an XZ spatial grid, bounded separation, and two MultiMeshes. Live-slot bitsets skip empty slots while preserving ascending callback order; highest live slot is cached. Role allocations follow population; CPU fallback uploads bulk transforms/colors/custom data. Combined CPU movement world queries share terrain/support/structure candidates. `IFpsCombatEnemy` lets weapons address crowd records and legacy enemies; deaths still flush after damage iteration.
- The accepted user baseline is 10k enemies at 1080p/60 FPS; the revised optimization target is 50k/1080p/about 45 FPS on GTX 1060 / RX 580 class hardware. The preferred horde design direction is explosive chaos and chain reactions; new enemy mechanics are still proposals. Local RTX 4070/i9-14900KF Debug diagnostics do not establish that target. The latest 10k matched renderer control cuts CPU pose publication from 10.5-11.7 ms to 0.53-0.59 ms (about 95%); full GPU simulation costs 3.1-4.0 ms per fixed-snapshot step. GPU poses evaluate once per body, independent of mesh vertex count. Current measurements use cubes and no firing; detailed meshes, sustained weapons and corpse loads remain unmeasured. Results/protocol: `Docs/GpuCrowdInvestigation.txt`, `.tmp/horde-frame-profile/gpu-final-*` and `render-control-*`. The earlier `optimized-crowd` 1600x900 results are historical.
- At 100k, the later bounded bucket sort and scalar boundary/attack pass reduce isolated step medians from 48.6 to 26.7 ms approach and 63.4 to 41.4 ms staged gates. Longer 1080p live diagnostics still range 17.5-57.6 FPS approach and 5.95-6.15 FPS congested gates; 100k/60 is not achieved. Full-state synchronous transfers and dense attack queries remain major costs. `RunLargeHordeValidation.ps1` covers actual 100k menu spawning, mixed roles, bounds/nonoverlap, generation safety, prefix buffers, exact render counts and reentrant attacks; native validation passes 132 checks. Compact regression fixtures retain 2,000 slots. Do not interpret short capacity checks as sustained-combat performance.
- Current build has zero warnings/errors. Regression suites pass: compact hill 48, motion/deaths 26, piling 39, modulators 417, lane 37, and tuning 56 headless. `Tools/Validation/LegacyHillFixture.cs` retains the former 200 m roofed map for compact combat and roof/support regressions; it is separate from the expanded-fort suite. Use isolated-save runners. The earlier native-cursor check remains historical rendered evidence, not a headless guarantee.
- The full-mile XZ grid now clears occupied cells instead of the entire extent; queries spanning 700 m are validated. The map camera far distance is 2,414 m. Historical compact-map CPU/render figures are not expanded-fort performance measurements.
- Before fort expansion, `RunHordeTuningValidation.ps1 -Rendered` passed 57 checks, including native cursor recapture, both enemy types, projectile invulnerability, and manual/configured 2,000-enemy spawning. `RunHordeStress.ps1` recorded compact-map 100/500/1,000/2,000 approach samples plus travel and staged doorway timings under `.tmp/horde-stress`. At 2,000 cubes on i7-13700H/RTX A1000, travel median 2.48 ms and dense-doorway median 8.37 ms measured crowd simulation only. Expanded-fort performance needs fresh measurements. Sustained firing, loot-heavy combat and base construction remain future work.

## Current Content Truth

- The modulator resource folder at `res://Resources/Equipment/Modulators` is currently absent.
- Live modulator content comes from fallback code partials under `Scripts/Equipment/Data/Modulators`.
- `ModulatorCatalog.CreateCodeFallbackTemplates()` is the live fallback assembly path.
- All 100 current fallback modulators are wired into North Gate combat, including 16 transforms and four Wings. The full weighted pool supplies loot/shop parts; FPS compatibility checks reject effects an attack family cannot consume. Imported hub weapons are cloned; incompatible parts are recovered locally for another shape.
- `FpsVerticalSlice.ModulatorStats`, `.ModulatorPassives`, `.ModulatorSpecials`, and `.ModulatorWings` own FPS effect support. Square uses moving bullets; its base speed is exported as `SquareProjectileSpeed`. Wings require arsenal slots after slot zero, one Wing per weapon, and unique Wing keys across the run.
- `Tools/Validation/ValidateFpsModulators.tscn` has 417 runtime checks covering all catalog installations, real damage/control/healing, transforms, Wings, recursion guards, Luck/Apex, source identity, and compatible imports. Its runner uses isolated saves. The 48 hill/horde and 37 lane checks also pass.
- `LAST_CHANGES.md` is the rolling newest-first handoff log for recent repo changes.

## Weapon Rarity and Progression Truth

This section documents the current weapon progression behavior in code.

### Rarity Baseline (EquipmentCatalogDefaults.RarityTemplates)

| Rarity | Stat Multiplier | Base Substat Count | Rarity XpValue (metadata) |
| --- | ---: | ---: | ---: |
| Common | 1.15 | 1 | 75 |
| Uncommon | 1.35 | 1 | 225 |
| Rare | 1.60 | 1 | 563 |
| Legendary | 1.90 | 2 | 1125 |
| Mythic | 2.80 | 3 | 11250 |
| Divine | 3.30 | 4 | 33750 |

Notes:
- `RarityData.XpValue` is currently metadata/persistence payload and is not used by weapon level-up threshold math.
- Weapon level cap is `5` (`EquipmentCatalogDefaults.MaxWeaponLevel`).

### Modulator and Essence Slot Unlocks (Per Rarity, Per Level)

Slot progression comes from `GetUnlockedModulatorSlots(rarity, level)`:
- Levels are clamped to `1..5`.
- `t = (level - 1) / 4`.
- `slots = RoundToInt(Lerp(startSlots, maxSlots, t))`, then clamped to `startSlots..maxSlots`.

Essence availability comes from `HasEssenceSlot(rarity, level)`:
- `true` when `level >= essenceUnlockLevel` for that rarity.

Legend:
- `xL` = `x` modulator slots, essence locked.
- `xU` = `x` modulator slots, essence unlocked.

| Rarity | Start->Max Mod Slots | Essence Unlock Level | Lv1 | Lv2 | Lv3 | Lv4 | Lv5 |
| --- | ---: | ---: | --- | --- | --- | --- | --- |
| Common | 1->4 | 4 | 1L | 2L | 3L | 3U | 4U |
| Uncommon | 2->5 | 4 | 2L | 3L | 4L | 4U | 5U |
| Rare | 3->6 | 3 | 3L | 4L | 5U | 5U | 6U |
| Legendary | 4->7 | 2 | 4L | 5U | 6U | 6U | 7U |
| Mythic | 4->8 | 2 | 4L | 5U | 6U | 7U | 8U |
| Divine | 5->10 | 1 | 5U | 6U | 8U | 9U | 10U |

### Weapon XP Required Per Level

From `GetWeaponXpRequiredForNextLevel(currentLevel)`:

| Level Transition | XP Required | Cumulative XP From Lv1 |
| --- | ---: | ---: |
| Lv1 -> Lv2 | 450 | 450 |
| Lv2 -> Lv3 | 1350 | 1800 |
| Lv3 -> Lv4 | 3200 | 5000 |
| Lv4 -> Lv5 | 7200 | 12200 |

### XP Gain and Distribution Logic

1. XP intake:
- XP orbs are collected in `CollectPickup` and call:
  - `GrantXp(pickup.Value * GetRarityMultiplier(pickup.Rarity) * xpGainMult)`
  - `xpGainMult = (1 + GetModifierTotal(ModifierKind.XpGainPct)) * GetEquipmentXpGainMultiplier()`
- `GrantXp(amount)` increases player `_xp` and also calls `GrantXpToRunLoadoutWeapons(amount)`.

2. Weapon XP split across equipped run loadout:
- Recipients are equipped run weapons with level `< MaxWeaponLevel`.
- Incoming XP is split evenly: `splitXp = amount / recipientCount`.
- Each weapon adds `splitXp` to stored `WeaponXpProgress` (tracked by run clone instance id).
- While enough XP is stored for next level:
  - subtract threshold,
  - level up once,
  - recalculate derived item stats,
  - recompute unlocked modulator slots and essence slot,
  - enqueue level-up status banner text.
- At max level (`5`), leftover stored XP is forced to `0`.

3. Persistence back to inventory:
- Run-clone level/XP is mirrored to source inventory item ids via `PersistRunLoadoutWeaponProgress()`.
- This persistence is called on run reset, arena exit, and run outcome handoff.
- `InventoryManager.TrySetItemWeaponProgress()` clamps level, clamps XP to non-negative, and zeros XP at max level.

4. Important behavior distinction:
- Weapon level-up XP thresholds are fixed constants (450/1350/3200/7200) and do not scale by rarity.
- Rarity changes slot progression, essence unlock timing, and stat/substat baselines.

## Systems In Flight

- Modulator data model supports `PowerTier`, `AllowedWeaponTags`, `BlockedWeaponTags`, and generic `Effects`.
- Passive modulator resolution is hybrid:
  - Many passive effects still bridge through `WeaponModulatorEffectData.TryConvertToSubstatRoll()`.
  - Some newer effects are handled directly in runtime code because they do not map cleanly to legacy `SubstatType` values.
- Tier 3 modulator runtime support is first-wave only, not a complete effect system.
- Tier 2 fallback modulators were recently added and now include runtime support for projectile speed, homing strength, pierce, and mortar flight-time scaling.

## Important Runtime Rules

- Source-item context matters for combat behavior. Advanced modulator fire/hit effects rely on `EquipmentItem` being passed through spawn, damage, and tick paths.
- Install restrictions are doing real work. Some modulators are intentionally gated by weapon tags because not every designed effect is universally wired yet.
- Avoid assuming resource-backed content is authoritative when the corresponding resource folder is missing.

## Feature Authoring Requirement

- For new Godot gameplay features, expose practical editor knobs by default (`[Export]` fields and/or resource-backed settings).
- When feasible, support live in-editor tuning (hot-reload/live-apply) so designers can tune behavior without follow-up implementation.
- Use the shape-outline workflow as the quality bar for live-editable tuning systems.

## Persistence Notes

- Inventory/equipment persistence is handled by `Scripts/Managers/InventoryManager.cs`.
- Current inventory schema version is `6`.
- Recent save changes include modulator `Effects`, tag restrictions, and `PowerTier` payload persistence.

## Suggested Next Work (When Unspecified)

Use this list when a session starts without a tightly scoped task. Pick the highest item that is not blocked.

1. FPS combat parity playtest pass
- Verify first-person behavior matches hub/arena expectations for on-fire and on-hit modulator effects (echo, mark, chain, lingering zone, execute, elite/boss hunter).
- Validate source-item context is preserved across projectile, beam/zone, orbital, mortar, clone, and aura damage paths.
- Record findings in `LAST_CHANGES.md` with concrete pass/fail notes.

2. FPS encounter progression
- Add multi-wave encounter flow to the FPS slice (wave budgets, scaling, and clear conditions).
- Keep wave tuning editor-driven via exported/resource settings and live-apply where practical.

3. FPS enemy behavior upgrades
- Improve enemy movement/decision quality around cover and line-of-sight edge cases.
- Keep tuning knobs exported so behavior can be iterated in-editor without code changes.

4. Modulator content authoring path
- If resource folder is still absent, continue fallback code catalog work in `Scripts/Equipment/Data/Modulators`.
- If resource folder appears, add resource-backed authoring while preserving fallback safety.

5. Regression safety improvements
- Add lightweight validation/check helpers for combat and modulator behavior where practical.
- Prioritize checks that detect divergence between FPS and arena combat behavior early.

Selection rule:
- If the user asks to "work on FPS" with no further detail, default to item 1 first, then item 2.
- If runtime behavior is changing, prioritize playtest validation over additional feature work.

## Start-Of-Session Checklist

1. Read `LAST_CHANGES.md` first.
2. Read this file.
3. If the task touches combat/modulators, read `GAMEPLAY_ARCHITECTURE.md`.
4. Validate code changes with `dotnet build "SuperGatchaRebuild.csproj"` unless the task is docs-only.
