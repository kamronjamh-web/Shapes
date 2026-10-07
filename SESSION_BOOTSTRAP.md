# Session Bootstrap

Read this file and LAST_CHANGES.md at the start of a fresh session.

## Repo Snapshot

- Project type: Godot 4 C# game project.
- Main solution/build target: `SuperGatchaRebuild.csproj`.
- Fast validation command: `dotnet build "SuperGatchaRebuild.csproj"`.
- Build success is useful, but combat/content changes still need a Godot runtime playtest when behavior matters.

## Horde Defense Direction and First Map

- The game is named `Shapes`. Shapes are both enemies and base weapon identities; modulators are weapon components.
- Enemy shapes drop currency and sometimes weapon shapes or modulators. Appearing shops sell upgrades and equipment during both combat and wave breaks.
- Maps progress from one lane toward four lanes in the north, east, south, and west cardinal directions.
- The hub's `North Gate` button launches `Scenes/Fps/NorthGate.tscn`; the original `FpsVerticalSlice.tscn` remains a separate sandbox.
- North Gate now uses a 200 × 200 metre terrain area, an eight-metre hill, and an enclosed summit room containing the 600-health core and shop. Its sole doorway faces north down the hill. Current enemies spawn at the north foot and climb into the room.
- The first map now contains one wave of 100 simultaneous cube enemies. Preparation lasts 20 seconds; `N` skips the remaining preparation. The earlier 12/24/40 lane prototype remains in `Scenes/Fps/NorthGateLaneSandbox.tscn` for regression testing.
- `F` interacts with the shop within range. Shopping holds the preparation clock between waves, while enemies continue advancing during combat shopping.
- Currency, loot, and upgrades currently belong to the run; the equipped hub weapon is cloned. This prototype does not change saved inventory/equipment.
- Current tuning resources live in `Resources/Fps/HillFort_*.tres`; enemy archetypes reuse `NorthGate_Cube.tres` and `NorthGate_Pyramid.tres`. Hill geometry supports live-apply, grounding objectives/player while preserving core health and equipment. Crowd rates/separation apply live; capacity is allocated at scene start and archetypes are sampled at spawn.
- `FpsHordeManager` owns packed enemy arrays, stable generation handles, an XZ spatial grid, bounded separation, and two MultiMeshes. `IFpsCombatEnemy` lets the weapon/modulator paths address both crowd records and legacy body enemies. Death events flush after damage iteration.
- Integration validation: `Tools/Validation/ValidateHillHorde.tscn` covers 100 live enemies, terrain/room physics, natural hill climb and door entry, pause/live tuning, piercing/AoE/occlusion, loot/shop behavior, all-enemy death accounting, and legacy FPS combat. The original 37-check suite runs against the preserved lane sandbox. Use isolated-save runners in the same folder.
- Base construction and measured performance for 500–1,000+ enemies remain future work. The new crowd foundation is currently validated with 100 enemies; capacity alone does not establish larger-count performance.

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
