# Temporary Direction Checklist

Tracking file for the current design direction. We will check items off as they are completed.

Status key:
- [x] Implemented
- [ ] Not implemented yet

Last updated: 2026-06-26

## Core Gameplay Direction

- [ ] Modulators replace the traditional item system as the main run progression loop.
- [ ] Character levels drive character-specific stat growth.
- [ ] Arena level-ups award modulator choices.
- [x] Weapon levels unlock additional modulator slots.
- [x] Weapon rarity determines starting power, slot capacity, and essence unlock timing.
- [x] Essences are a separate system for bonuses/drawbacks/synergies.

## Character Design Direction

- [ ] Character system exists (Vector, Bastion, Flux).
- [ ] Character growth is automatic on player level-up (no draft tradeoff).
- [ ] Vector progression passives (projectile speed/crit damage focus).
- [ ] Bastion progression passives (max health/regen focus).
- [ ] Flux progression passives (attack speed/cooldown focus).

## Arena Level-Up Rewards

- [x] Direct WeaponUpgrade choice removed from normal level-up drafts.
- [ ] Level-up flow gives 3 modulator choices.
- [ ] Draft resolution supports install, upgrade, replace, or recycle outcomes.
- [ ] Direct weapon upgrades come from non-draft sources (bosses/elites/forge/events/pickups).

## Modulator Structure and Quality

- [x] Tier and quality are separate data concepts.
- [ ] Tier 1 is consistently simple stat bonuses.
- [ ] Tier 2 is consistently tradeoffs/combined bonuses.
- [ ] Tier 3 is consistently build-defining effects.
- [ ] Quality only scales numbers and does not add new mechanics.

## Modulator Expansion Direction (Remaining Identities)

- [x] Expansion target fixed at 100 unique modulator identities (quality variants do not increase identity count).
- [x] Current fallback catalog baseline is 37 identities total.
- [x] Remaining identities to design: 63 total.

Current-to-target tier delta:

| Tier | Current | Target | Remaining |
| --- | ---: | ---: | ---: |
| Tier 1 | 20 | 30 | 10 |
| Tier 2 | 6 | 40 | 34 |
| Tier 3 | 11 | 30 | 19 |
| Total | 37 | 100 | 63 |

Tier 3 remaining split (to preserve weapon identity):

- 16 weapon transformations (one per current weapon).
- 1 additional proc identity.
- 2 rare universal capstones.
- Wings are considered complete for the initial target set (4/4).

Authoring priorities for the next pass:

1. Add Tier 2 tag/family modulators first (largest current gap and best decision density).
2. Add one Tier 3 signature transformation per weapon (identity pass, not pure percentage buffs).
3. Add Tier 1 and Tier 2 defensive/utility picks that are not only damage multipliers.

Quality guidance (applies to all tiers unless a row says N/A):

- Flawed/Baseline/Enhanced/Flawless are quality variants of one identity, not new identities.
- Use true multiplicative value notation primarily for Tier 3, or for Tier 2 rows that include an explicit drawback in the same row.
- Prefer numeric-only quality scaling:
	- Stat-only modulators: scale magnitude values.
	- Tradeoff modulators: scale positive component more than downside, keep readability.
	- Proc/transformation modulators: scale chance, radius, uptime, or coefficient; do not add new mechanic branches at higher quality.
- If a transformation is binary and complexity-sensitive, mark quality as "mechanic locked" and scale only one safe numeric coefficient.

Planning artifact:

- Use `Modulator_Remaining_Plan.xlsx` as the source of truth for remaining 63 identities.
- Each row includes suggested effects and quality scaling, plus blank fields for final design intent and final numbers.

## Modulator Moving and Replacing Economy

- [x] Install into an empty slot is free.
- [ ] Replace/move cost exists and is primarily tier-based.
- [ ] Essence move/replace cost exists.
- [ ] Repeated same-run movement applies escalating cost.
- [x] Removing modulators does not destroy them.
- [ ] Salvage value is based on tier and quality.
- [ ] Upgrade/reforge cost is based on tier and quality.

## Duplicate Modulators

- [ ] Basic stat modulators support stacking or rank-up progression.
- [ ] Advanced modulators upgrade existing effects instead of creating duplicate proc systems.
- [ ] Duplicate handling keeps rewards useful when slots are full.

## Modulator Draft Rules

- [ ] Draft weighting considers weapon tags.
- [ ] Draft weighting considers available slots.
- [ ] Draft weighting considers current synergies/build direction.
- [ ] Draft weighting prioritizes under-modded weapons where relevant.
- [ ] Drafts attempt to provide at least two usable choices.
- [ ] Full-build fallback options exist: upgrade duplicate, replace, recycle, reroll, or convert to forge currency.

## Complex Effect Limits

- [x] One wing modulator per support weapon is enforced.
- [x] Cross-weapon duplicate wing-key conflicts are enforced.
- [ ] Tier 3 proc modulators per weapon are capped.
- [ ] Capstone conflict groups are enforced.
- [ ] Duplicate unique transformations are blocked.
- [ ] Duplicate procs convert to ranks rather than independent instances.

## Weapon Rarity and Level Capacity

- [x] Weapon level cap is 5.
- [x] Rarity-based max modulator slots are implemented (Common 4, Uncommon 5, Rare 6, Legendary 7, Mythic 8, Divine 10 at L5).
- [x] Endgame theoretical cap of 50 modulators is supported (5 Divine weapons at L5).

## Weapon XP

- [x] Base XP thresholds are 450, 1350, 3200, 7200.
- [x] XP is split evenly among equipped weapons below level cap.
- [x] No catch-up XP system is currently present.
- [ ] Rarity-based XP multipliers for weapon leveling are implemented.

## Essence Structure and Resonance

- [x] Matching-piece synergy model is implemented (passive/synergy/super).
- [x] Prismatic contributes to effective counts.
- [x] Full Spectrum override exists.
- [ ] Resonance rank presentation layer (I/II/III/IV) is implemented in player-facing UX.

## Volatile Naming and Identity Split

- [ ] Player-facing spelling standardized to Volatile while keeping legacy key compatibility.
- [ ] Volatile essence identity is distinct from Volatile modulator identity.
- [ ] Legacy Volitile and modulator Volitile runtime behavior paths are fully decoupled.

## Notes

- This file is intentionally temporary and execution-focused.
- As work lands, update checkbox state and add a short note in LAST_CHANGES.md.