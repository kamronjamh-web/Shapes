$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot 'Modulator_Remaining_Plan.xlsx'

if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}

$rows = [System.Collections.Generic.List[object]]::new()

function Add-Row {
    param(
        [string]$RowId,
        [string]$Tier,
        [string]$Bucket,
        [string]$Eligibility,
        [string]$TargetWeaponOrTag,
        [string]$SuggestedName,
        [string]$SuggestedEffect,
        [string]$SuggestedMechanicType,
        [string]$SuggestedBaselineNumbers,
        [string]$QualitySuggestion,
        [string]$Priority,
        [string]$RuntimeNotes
    )

    $rows.Add([pscustomobject]@{
        RowId = $RowId
        Tier = $Tier
        Bucket = $Bucket
        Eligibility = $Eligibility
        TargetWeaponOrTag = $TargetWeaponOrTag
        SuggestedName = $SuggestedName
        SuggestedEffect = $SuggestedEffect
        SuggestedMechanicType = $SuggestedMechanicType
        SuggestedBaselineNumbers = $SuggestedBaselineNumbers
        QualitySuggestion = $QualitySuggestion
        Priority = $Priority
        RuntimeNotes = $RuntimeNotes
        YourDesignIntent = ''
        YourFinalEffect = ''
        YourFinalNumbers = ''
        Status = ''
    }) | Out-Null
}

function Get-CurrentQualitySuggestion {
    param(
        [string]$Tier
    )

    switch ($Tier) {
        'Tier1' { return 'Proposed: Flawed 70%, Baseline 100%, Enhanced 130%, Flawless 160% of listed values.' }
        'Tier2' { return 'Proposed: Flawed 75%, Baseline 100%, Enhanced 125%, Flawless 150% of listed values. Keep downside visible at every quality.' }
        'Tier3' { return 'Proposed: Flawed 80%, Baseline 100%, Enhanced 120%, Flawless 140% of listed payload values. Do not change mechanics.' }
        default { return 'Proposed: scale numbers only across quality; do not add mechanics.' }
    }
}

# Tier 1 remaining (10)
Add-Row 'T1-U01' 'Tier1' 'UniversalStat' 'Universal' 'all' 'Vital Reservoir' 'Simple survivability anchor for any build.' 'HealthMultiplier' '+12% max health.' 'Scale additive percent: Flawed +6%, Baseline +12%, Enhanced +18%, Flawless +24%.' 'P2' 'Pure passive and low risk.'
Add-Row 'T1-U02' 'Tier1' 'UniversalStat' 'Universal' 'all' 'Recovery Circuit' 'Flat regen bump to smooth attrition in long fights.' 'HealthRegenAdd' '+1.2 health regen.' 'Scale regen only: +0.8, +1.2, +1.6, +2.0.' 'P2' 'Pure passive and low risk.'
Add-Row 'T1-U03' 'Tier1' 'UniversalStat' 'Universal' 'all' 'Evasion Mesh' 'Readable defensive stat that is not damage focused.' 'DodgeChanceAdd' '+6 dodge chance.' 'Scale chance only: +3, +6, +9, +12.' 'P2' 'Pure passive and low risk.'
Add-Row 'T1-U04' 'Tier1' 'UniversalStat' 'Universal' 'all' 'Stride Servo' 'Mobility foundation modulator for reposition-heavy play.' 'MovementSpeedMultiplier' '+8% move speed.' 'Scale additive percent: +4%, +8%, +12%, +16%.' 'P2' 'Pure passive and low risk.'
Add-Row 'T1-T01' 'Tier1' 'TagSpecific' 'Tag-based' 'square|triangle|hypercone' 'Ballistic Tuning' 'Clean projectile delivery boost for precision lanes.' 'ProjectileSpeedMultiplier' '+15% projectile speed.' 'Scale additive percent: +8%, +15%, +22%, +30%.' 'P2' 'Uses existing projectile-speed runtime path.'
Add-Row 'T1-T02' 'Tier1' 'TagSpecific' 'Tag-based' 'mine|orbital|zone|clone' 'Extended Uptime' 'Longer field and summon presence without adding mechanics.' 'DurationMultiplier' '+20% duration.' 'Scale additive percent: +10%, +20%, +30%, +40%.' 'P2' 'Uses existing duration runtime path.'
Add-Row 'T1-T03' 'Tier1' 'TagSpecific' 'Tag-based' 'hexagon|mine' 'Fuse Tuner' 'Mine cadence bump for setup-focused builds.' 'CooldownMultiplier' '-8% cooldown (faster).' 'Scale additive percent: -4%, -8%, -12%, -16% cooldown.' 'P2' 'Tag gate avoids non-mine spillover.'
Add-Row 'T1-T04' 'Tier1' 'TagSpecific' 'Tag-based' 'cube|orbital' 'Orbital Bracing' 'Improves orbital contact consistency through reach.' 'RangeMultiplier' '+12% range.' 'Scale additive percent: +6%, +12%, +18%, +25%.' 'P2' 'Range multiplier already resolves on orbital behavior.'
Add-Row 'T1-T05' 'Tier1' 'TagSpecific' 'Tag-based' 'pyramid|homing' 'Guidance Spine' 'Simple homing correction improvement for seeker weapons.' 'HomingStrengthMultiplier' '+20% homing strength.' 'Scale additive percent: +10%, +20%, +30%, +40%.' 'P2' 'Uses existing homing-strength runtime hook.'
Add-Row 'T1-D01' 'Tier1' 'UtilityDefensive' 'Universal' 'all' 'Stability Plating' 'Crowd-control utility via stronger knockback pressure.' 'KnockbackMultiplier' '+20% knockback.' 'Scale additive percent: +8%, +20%, +32%, +45%.' 'P3' 'Verify knockback multiplier path is active in all attack families.'

# Tier 2 remaining (34)
# Universal combined-stat (10)
Add-Row 'T2-U01' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Glass Hammer Core' 'High damage with intentional fragility tradeoff.' 'DamageMultiplier + HealthMultiplier' 'Upside: +24% damage. Drawback: -12% max health.' 'Scale both sides slightly; keep downside visible at all qualities.' 'P1' 'No new runtime required.'
Add-Row 'T2-U02' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Rapid Geometry' 'Faster cycle in exchange for narrower impact footprint.' 'CooldownMultiplier + AreaMultiplier' 'Upside: -18% cooldown (faster cadence). Drawback: -12% area.' 'Scale both sides; upside should grow a little faster than downside.' 'P1' 'No new runtime required.'
Add-Row 'T2-U03' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Deadeye Tension' 'Crit-focused accuracy package with area loss.' 'CritChanceAdd + AreaMultiplier' 'Upside: +10 crit chance. Drawback: -10% area.' 'Scale crit first; area penalty should stay mostly flat.' 'P1' 'No new runtime required.'
Add-Row 'T2-U04' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Siege Footing' 'Bigger hit zones while slightly reducing mobility.' 'AreaMultiplier + MovementSpeedMultiplier' 'Upside: +20% area. Drawback: -8% move speed.' 'Scale area more than speed penalty.' 'P2' 'No new runtime required.'
Add-Row 'T2-U05' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Longshot Pressure' 'Reach-focused package with slower cadence.' 'RangeMultiplier + CooldownMultiplier' 'Upside: +22% range. Drawback: +10% cooldown (slower cadence).' 'Scale range; keep cadence tax near constant.' 'P1' 'No new runtime required.'
Add-Row 'T2-U06' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Predator Rhythm' 'Harder crits at the cost of tempo.' 'CritDamageMultiplier + CooldownMultiplier' 'Upside: +30% crit damage. Drawback: +8% cooldown (slower cadence).' 'Scale crit damage only for most tiers.' 'P2' 'No new runtime required.'
Add-Row 'T2-U07' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Vampiric Engine' 'Sustain conversion with lower base hit strength.' 'LifeStealAdd + DamageMultiplier' 'Upside: +3 lifesteal. Drawback: -10% damage.' 'Scale lifesteal primarily; damage penalty nearly fixed.' 'P2' 'No new runtime required.'
Add-Row 'T2-U08' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Overburn Fins' 'Fast delivery profile that trims per-hit force.' 'ProjectileSpeedMultiplier + DamageMultiplier' 'Upside: +25% projectile speed. Drawback: -14% damage.' 'Scale speed more than damage penalty.' 'P1' 'Validate non-projectile families ignore or convert this cleanly.'
Add-Row 'T2-U09' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Bulwark Drive' 'Durability package with slower attack loop.' 'HealthMultiplier + CooldownMultiplier' 'Upside: +18% max health. Drawback: +8% cooldown (slower cadence).' 'Scale health only; cadence penalty mostly static.' 'P2' 'No new runtime required.'
Add-Row 'T2-U10' 'Tier2' 'UniversalCombined' 'Universal' 'all' 'Reckless Focus' 'Aggressive crit profile with meaningful survivability loss.' 'CritChanceAdd + HealthMultiplier' 'Upside: +12 crit chance. Drawback: -14% max health.' 'Scale crit chance; keep health downside stable.' 'P2' 'No new runtime required.'

# Tag-based or weapon-family (16)
Add-Row 'T2-T01' 'Tier2' 'TagFamily' 'Tag-based' 'precision|square|triangle|hypercone' 'Precision Rail' 'Lane pressure package for precision weapons.' 'RangeMultiplier + ProjectileSpeedMultiplier + CooldownMultiplier' 'Upside: +18% range and +18% projectile speed. Drawback: +6% cooldown (slower cadence).' 'Scale range and speed; cadence tax should remain mild.' 'P1' 'Uses existing scalar stats only.'
Add-Row 'T2-T02' 'Tier2' 'TagFamily' 'Tag-based' 'circle|sawblade' 'Saw Storm' 'More saw presence with reduced per-hit value.' 'ProjectileCountAdd + DamageMultiplier' 'Upside: +1 projectile. Drawback: -18% damage.' 'Scale count at top quality only if readable; otherwise scale damage penalty less.' 'P1' 'Count changes need sawblade spawn-limit safeguards.'
Add-Row 'T2-T03' 'Tier2' 'TagFamily' 'Tag-based' 'triangle|laser' 'Beam Condenser' 'Tighter beam profile with stronger line damage.' 'DamageMultiplier + AreaMultiplier' 'Upside: +22% damage. Drawback: -18% area.' 'Scale damage only beyond baseline.' 'P1' 'Area maps to beam width/volume in runtime.'
Add-Row 'T2-T04' 'Tier2' 'TagFamily' 'Tag-based' 'hexagon|mine' 'Mine Relay' 'Rapid deployment with shorter mine persistence.' 'CooldownMultiplier + DurationMultiplier' 'Upside: -22% cooldown (faster deployment). Drawback: -18% duration.' 'Scale cadence mostly; duration tax stays meaningful.' 'P1' 'Matches requested mine tradeoff pattern.'
Add-Row 'T2-T05' 'Tier2' 'TagFamily' 'Tag-based' 'sphere|nova' 'Nova Shear' 'Bigger nova with slower pulse cycle.' 'AreaMultiplier + CooldownMultiplier' 'Upside: +30% area. Drawback: +14% cooldown (slower cadence).' 'Scale area only after baseline.' 'P2' 'No new runtime required.'
Add-Row 'T2-T06' 'Tier2' 'TagFamily' 'Tag-based' 'cube|orbital' 'Orbital Compression' 'Extra orbital at tighter radius for high contact risk.' 'ProjectileCountAdd + NewOrbitRadiusScalar' 'Upside: +1 orbital. Drawback: -22% orbit radius.' 'Scale count/radius carefully; keep one clear downside.' 'P1' 'Requires explicit orbit-radius runtime scalar.'
Add-Row 'T2-T07' 'Tier2' 'TagFamily' 'Tag-based' 'pyramid|homing' 'Hunter Saturation' 'Extra seeker output with lower hit strength.' 'ProjectileCountAdd + DamageMultiplier' 'Upside: +1 projectile. Drawback: -20% damage.' 'Scale count only at high quality if readability holds.' 'P1' 'Works with current homing and projectile hooks.'
Add-Row 'T2-T08' 'Tier2' 'TagFamily' 'Tag-based' 'cone|flame' 'Flame Jet' 'Longer cone reach with reduced linger time.' 'RangeMultiplier + DurationMultiplier' 'Upside: +22% range. Drawback: -15% duration.' 'Scale range more than duration penalty.' 'P2' 'Needs cone-range parity check in FPS and arena.'
Add-Row 'T2-T09' 'Tier2' 'TagFamily' 'Tag-based' 'tesseract|rift|zone' 'Rift Stabilizer' 'Longer rift uptime with reduced tick damage.' 'DurationMultiplier + DamageMultiplier' 'Upside: +30% duration. Drawback: -12% damage.' 'Scale duration only for most qualities.' 'P1' 'No new runtime required.'
Add-Row 'T2-T10' 'Tier2' 'TagFamily' 'Tag-based' 'hyperphere|mortar' 'Mortar Barrage' 'Extra shell output with lower shell impact.' 'ProjectileCountAdd + DamageMultiplier' 'Upside: +1 projectile. Drawback: -22% damage.' 'Scale projectile count very carefully to avoid visual clutter.' 'P1' 'Needs mortar spread/range clamps.'
Add-Row 'T2-T11' 'Tier2' 'TagFamily' 'Tag-based' 'pentachoron|chainlightning' 'Chain Capacitor' 'Longer chain routes with lower arc damage.' 'ChainCountAdd + DamageMultiplier' 'Upside: +2 chain count. Drawback: -14% damage.' 'Scale chain count or arc damage not both at once.' 'P1' 'Confirm chain count mod is consumed in runtime.'
Add-Row 'T2-T12' 'Tier2' 'TagFamily' 'Tag-based' 'hypercone|drill' 'Drill Spool' 'Faster drill travel with shorter active time.' 'ProjectileSpeedMultiplier + DurationMultiplier' 'Upside: +24% projectile speed. Drawback: -16% duration.' 'Scale speed only beyond baseline.' 'P1' 'Projectile speed already affects drill travel.'
Add-Row 'T2-T13' 'Tier2' 'TagFamily' 'Tag-based' 'impossiblecube|void' 'Void Compression' 'Stronger pull with reduced effect radius.' 'PullStrengthMultiplier + AreaMultiplier' 'Upside: +35% pull strength. Drawback: -14% area.' 'Scale pull strength; radius tax mostly fixed.' 'P2' 'Pull-strength scalar exists in effect enum but may need hookup audit.'
Add-Row 'T2-T14' 'Tier2' 'TagFamily' 'Tag-based' 'mobius|temporal_echo|echo' 'Echo Focus' 'Heavier echoes with slower trigger cadence.' 'CloneOrEchoPowerMultiplier + CooldownMultiplier' 'Upside: +28% echo power. Drawback: +10% cooldown (slower cadence).' 'Scale echo power only.' 'P1' 'Uses first-wave echo runtime path.'
Add-Row 'T2-T15' 'Tier2' 'TagFamily' 'Tag-based' 'klein|wrath_aura|aura' 'Wrath Furnace' 'Harder aura ticks with lower survivability.' 'DamageMultiplier + HealthMultiplier' 'Upside: +24% damage. Drawback: -12% max health.' 'Scale damage only after baseline.' 'P2' 'Aura path should inherit source-item modifiers consistently.'
Add-Row 'T2-T16' 'Tier2' 'TagFamily' 'Tag-based' 'boys|shadow_clone|clone' 'Clone Relay' 'More clone contribution with shorter clone life.' 'CloneOrEchoPowerMultiplier + DurationMultiplier' 'Upside: +24% clone power. Drawback: -18% duration.' 'Scale clone power first; duration downside constant.' 'P1' 'Clone duration scaling already exists and should be reused.'

# Weapon-specific directional (8)
Add-Row 'T2-W01' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'square' 'Needle Shot' 'Square specializes into long-range puncture fire.' 'DamageMultiplier + RangeMultiplier + CooldownMultiplier' 'Upside: +28% damage and +25% range. Drawback: +12% cooldown (slower cadence).' 'Scale damage/range; keep cadence tax fixed.' 'P2' 'No new runtime required.'
Add-Row 'T2-W02' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'circle' 'Return Arc' 'Sawblade returns once after max travel for extra lane control.' 'NewSawReturn + DamageMultiplier' 'Upside: one return pass. Drawback: -10% damage.' 'Mechanic mostly locked; quality scales return damage coefficient.' 'P2' 'Requires dedicated saw return runtime branch.'
Add-Row 'T2-W03' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'triangle' 'Line Driver' 'Beam becomes thinner and significantly stronger.' 'DamageMultiplier + AreaMultiplier' 'Upside: +35% damage. Drawback: -25% area.' 'Scale damage only.' 'P2' 'Area maps to beam width.'
Add-Row 'T2-W04' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'hexagon' 'Quick Plant' 'Mines deploy rapidly but expire rapidly.' 'CooldownMultiplier + DurationMultiplier' 'Upside: -25% cooldown (faster deployment). Drawback: -25% duration.' 'Scale cooldown only above baseline.' 'P1' 'Direct fit for requested mine directional pattern.'
Add-Row 'T2-W05' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'sphere' 'Double Pulse' 'Nova emits a weaker return pulse after expansion.' 'NewSecondPulse + CooldownMultiplier' 'Upside: second pulse at 60% power. Drawback: +15% cooldown (slower cadence).' 'Scale return pulse coefficient only.' 'P2' 'Requires nova second-pulse runtime support.'
Add-Row 'T2-W06' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'cube' 'Tight Orbit' 'Adds one orbital and shrinks orbit radius substantially.' 'ProjectileCountAdd + NewOrbitRadiusScalar' 'Upside: +1 orbital. Drawback: -28% orbit radius.' 'Scale radius or added orbit count one step at a time.' 'P1' 'Matches requested orbital directional tradeoff.'
Add-Row 'T2-W07' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'pyramid' 'Lock Burst' 'Seekers aggressively stack on a locked target with lower base damage.' 'HomingStrengthMultiplier + DamageMultiplier + NewTargetLockBias' 'Upside: +20% homing and high lock bias. Drawback: -14% damage.' 'Scale lock bias and homing more than damage.' 'P1' 'Needs explicit lock-bias parameter in seeker targeting.'
Add-Row 'T2-W08' 'Tier2' 'WeaponDirectional' 'Weapon-specific' 'hyperphere' 'High Arc Bombard' 'Mortar shells travel faster but with tighter blast radius.' 'ProjectileSpeedMultiplier + AreaMultiplier' 'Upside: +28% projectile speed. Drawback: -12% area.' 'Scale speed; keep radius downside present.' 'P1' 'Uses mortar projectile-speed path plus area scaling.'

# Tier 3 remaining (19)
# Additional proc (1)
Add-Row 'T3-P01' 'Tier3' 'Proc' 'Universal' 'all' 'Critical Cascade' 'Critical hits trigger a short micro-chain to one nearby enemy.' 'NewOnCritChainProc' '35% chain damage, 10m radius, 0.35s internal cooldown.' 'Scale chain damage and radius; keep proc identity fixed.' 'P1' 'Needs OnCrit trigger support or crit-flag handoff into OnHit resolver.'

# Weapon transformations (16)
Add-Row 'T3-X01' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'square' 'Burstline Converter' 'Square single shot converts into a controlled 3-round burst.' 'NewWeaponTransform' '3 shots at 45% each with 0.18s spacing.' 'Mechanic locked; quality scales per-shot coefficient only.' 'P1' 'Transformation branch in square fire routine.'
Add-Row 'T3-X02' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'circle' 'Serration Bloom' 'Sawblades split after reaching max pierce.' 'NewWeaponTransform' 'Split into 2 mini blades at 55% damage.' 'Scale split damage first; split count only at top tier if readable.' 'P1' 'User-request example and strong identity anchor.'
Add-Row 'T3-X03' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'triangle' 'Prism Refraction' 'Beam forks into side beams after sustained contact.' 'NewWeaponTransform' 'After 0.25s contact spawn 2 forks at 40% damage.' 'Scale fork damage only.' 'P2' 'Needs per-target contact timer state.'
Add-Row 'T3-X04' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'hexagon' 'Command Cluster' 'Mines become remote-detonated clusters.' 'NewWeaponTransform' 'Detonation spawns 3 child mines at 35% damage.' 'Scale child-mine damage or child count, not both.' 'P1' 'User-request example and high build direction value.'
Add-Row 'T3-X05' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'sphere' 'Collapse Pulse' 'Nova expands then collapses inward for a second hit window.' 'NewWeaponTransform' 'Return wave at 65% damage after outer edge reaches max.' 'Scale return-wave coefficient only.' 'P1' 'User-request example and pairs with nova identity.'
Add-Row 'T3-X06' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'cube' 'Aegis Carousel' 'Orbitals leave short damage trails and reverse orbit periodically.' 'NewWeaponTransform' 'Trail ticks at 25% damage and reverse every 2s.' 'Scale trail damage and trail duration; keep reversal behavior fixed.' 'P2' 'Requires orbital trail entity path.'
Add-Row 'T3-X07' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'pyramid' 'Pack Hunters' 'First seeker marks target; follow-up seekers prioritize the mark.' 'NewWeaponTransform' 'Mark lasts 3s and grants 30% seeker retarget bias.' 'Scale mark duration or bonus damage only.' 'P1' 'Can reuse ApplyMark state if shared safely.'
Add-Row 'T3-X08' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'cone' 'Backdraft Corridor' 'Flame cone leaves a burning corridor behind enemy line.' 'NewWeaponTransform' 'Corridor lasts 2.5s at 32% DPS coefficient.' 'Scale corridor duration and coefficient only.' 'P2' 'Needs cone-hit sweep to spawn aligned lingering zone.'
Add-Row 'T3-X09' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'tesseract' 'Phase Inversion' 'Rift alternates pull and push pulses during lifetime.' 'NewWeaponTransform' 'Pulse every 0.6s with pull then push sequence.' 'Scale pulse strength and pulse count only.' 'P2' 'Can build on existing pull and zone tick systems.'
Add-Row 'T3-X10' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'hyperphere' 'Scorch Crater' 'Mortar leaves a burning impact zone after explosion.' 'NewWeaponTransform' 'Crater lasts 3.5s at 30% DPS coefficient.' 'Scale crater duration or DPS coefficient.' 'P1' 'User-request example and straightforward with lingering-zone framework.'
Add-Row 'T3-X11' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'pentachoron' 'Closed Circuit' 'Chain lightning can re-strike the original target once.' 'NewWeaponTransform' 'One allowed return strike at 50% arc damage.' 'Scale return-strike damage only.' 'P1' 'User-request example and clear chain identity.'
Add-Row 'T3-X12' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'hypercone' 'Tunnel Bore' 'Drill leaves a damaging tunnel wake through traveled space.' 'NewWeaponTransform' 'Wake lasts 1.8s at 28% DPS coefficient.' 'Scale wake duration and coefficient; keep tunnel mechanic fixed.' 'P2' 'Needs drill path sampling and wake zone spawn.'
Add-Row 'T3-X13' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'impossiblecube' 'Event Horizon Tear' 'Void singularity ends with implosion then fragment eruption.' 'NewWeaponTransform' 'Final burst 160% then 6 shards at 30% each.' 'Scale burst and shard coefficients only.' 'P2' 'Needs deterministic finalization event hook.'
Add-Row 'T3-X14' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'mobius' 'Echo Weave' 'Temporal Echo fires a delayed mirror volley with no recursion.' 'NewWeaponTransform' 'Mirror volley at 40% power after 0.6s delay.' 'Scale mirror power only; keep recursion blocked.' 'P1' 'Leverages existing EchoAfterDelay safety pattern.'
Add-Row 'T3-X15' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'klein' 'Blood Price Reactor' 'Wrath Aura stores taken damage and releases burst pulses.' 'NewWeaponTransform' 'Store 30% recent damage and pulse every 1.5s.' 'Scale stored-percent cap and pulse coefficient.' 'P2' 'Needs damage-received buffer on player context.'
Add-Row 'T3-X16' 'Tier3' 'WeaponTransformation' 'Weapon-specific' 'boys' 'Primary Mimic' 'Shadow Clones copy the primary-slot weapon at reduced strength.' 'NewWeaponTransform' 'Clone mimic at 35% power and no recursive clone spawning.' 'Scale mimic coefficient only.' 'P1' 'User-request example and likely highest Tier3 identity value.'

# Rare universal capstones (2)
Add-Row 'T3-C01' 'Tier3' 'Capstone' 'Universal' 'all' 'Apex Doctrine' 'Single-weapon commitment capstone that amplifies non-Tier3 modulators.' 'NewCapstoneRule' 'If this is only Tier3 on weapon: +35% magnitude to Tier1/2 effects.' 'Mechanic locked; quality scales amplification only.' 'P2' 'Needs conflict-group enforcement and clear UI rule text.'
Add-Row 'T3-C02' 'Tier3' 'Capstone' 'Universal' 'all' 'Omni Feedback' 'Every 8th successful hit emits a universal spectral follow-up strike.' 'NewCapstoneProc' 'Follow-up strike at 100% coefficient with 12m search radius.' 'Scale follow-up coefficient and radius; keep cadence fixed.' 'P2' 'Requires shared hit-counter state per weapon instance.'

if ($rows.Count -ne 63) {
    throw "Expected 63 rows but generated $($rows.Count)."
}

$excel = $null
$workbook = $null
$summary = $null
$data = $null
$legend = $null
$valueGuide = $null
$currentAndProposed = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false

    $workbook = $excel.Workbooks.Add()
    while ($workbook.Worksheets.Count -lt 5) {
        $null = $workbook.Worksheets.Add()
    }

    $summary = $workbook.Worksheets.Item(1)
    $data = $workbook.Worksheets.Item(2)
    $legend = $workbook.Worksheets.Item(3)
    $valueGuide = $workbook.Worksheets.Item(4)
    $currentAndProposed = $workbook.Worksheets.Item(5)

    $summary.Name = 'Summary'
    $data.Name = 'Remaining_63'
    $legend.Name = 'Legend'
    $valueGuide.Name = 'Value_Guide'
    $currentAndProposed.Name = 'Current_And_Proposed'

    $summary.Cells.Item(1, 1).Value2 = 'Modulator Expansion Direction'
    $summary.Cells.Item(2, 1).Value2 = 'Target identities: 100 (quality variants do not increase identity count).'
    $summary.Cells.Item(3, 1).Value2 = 'Current identities: 37'
    $summary.Cells.Item(4, 1).Value2 = 'Remaining identities in this workbook: 63'
    $summary.Cells.Item(5, 1).Value2 = 'Combined current + proposed identities: 100'

    $summary.Cells.Item(6, 1).Value2 = 'Tier'
    $summary.Cells.Item(6, 2).Value2 = 'Current'
    $summary.Cells.Item(6, 3).Value2 = 'Target'
    $summary.Cells.Item(6, 4).Value2 = 'Remaining'

    $summary.Cells.Item(7, 1).Value2 = 'Tier1'
    $summary.Cells.Item(7, 2).Value2 = 20
    $summary.Cells.Item(7, 3).Value2 = 30
    $summary.Cells.Item(7, 4).Formula = '=C7-B7'

    $summary.Cells.Item(8, 1).Value2 = 'Tier2'
    $summary.Cells.Item(8, 2).Value2 = 6
    $summary.Cells.Item(8, 3).Value2 = 40
    $summary.Cells.Item(8, 4).Formula = '=C8-B8'

    $summary.Cells.Item(9, 1).Value2 = 'Tier3'
    $summary.Cells.Item(9, 2).Value2 = 11
    $summary.Cells.Item(9, 3).Value2 = 30
    $summary.Cells.Item(9, 4).Formula = '=C9-B9'

    $summary.Cells.Item(10, 1).Value2 = 'Total'
    $summary.Cells.Item(10, 2).Formula = '=SUM(B7:B9)'
    $summary.Cells.Item(10, 3).Formula = '=SUM(C7:C9)'
    $summary.Cells.Item(10, 4).Formula = '=SUM(D7:D9)'

    $summary.Cells.Item(12, 1).Value2 = 'Rows by tier from Remaining_63 sheet'
    $summary.Cells.Item(13, 1).Value2 = 'Tier1 rows'
    $summary.Cells.Item(13, 2).Formula = '=COUNTIF(Remaining_63!B:B,"Tier1")'
    $summary.Cells.Item(14, 1).Value2 = 'Tier2 rows'
    $summary.Cells.Item(14, 2).Formula = '=COUNTIF(Remaining_63!B:B,"Tier2")'
    $summary.Cells.Item(15, 1).Value2 = 'Tier3 rows'
    $summary.Cells.Item(15, 2).Formula = '=COUNTIF(Remaining_63!B:B,"Tier3")'

    $summary.Cells.Item(13, 4).Formula = '=COUNTIF(Current_And_Proposed!C:C,"Tier1")'
    $summary.Cells.Item(14, 4).Formula = '=COUNTIF(Current_And_Proposed!C:C,"Tier2")'
    $summary.Cells.Item(15, 4).Formula = '=COUNTIF(Current_And_Proposed!C:C,"Tier3")'

    $summary.Cells.Item(17, 1).Value2 = 'Design rule'
    $summary.Cells.Item(17, 2).Value2 = 'Quality should scale numbers, not add new mechanics.'
    $summary.Cells.Item(18, 1).Value2 = 'Value rule'
    $summary.Cells.Item(18, 2).Value2 = 'Use true multiplicative x-values only on Tier3, or on Tier2 rows that include a clear drawback.'

    $headers = @(
        'RowId',
        'Tier',
        'Bucket',
        'Eligibility',
        'TargetWeaponOrTag',
        'SuggestedName',
        'SuggestedEffect',
        'SuggestedMechanicType',
        'SuggestedBaselineNumbers',
        'QualitySuggestion',
        'Priority',
        'RuntimeNotes',
        'YourDesignIntent',
        'YourFinalEffect',
        'YourFinalNumbers',
        'Status'
    )

    for ($column = 0; $column -lt $headers.Count; $column++) {
        $data.Cells.Item(1, $column + 1).Value2 = $headers[$column]
    }

    $rowIndex = 2
    foreach ($row in $rows) {
        $data.Cells.Item($rowIndex, 1).Value2 = $row.RowId
        $data.Cells.Item($rowIndex, 2).Value2 = $row.Tier
        $data.Cells.Item($rowIndex, 3).Value2 = $row.Bucket
        $data.Cells.Item($rowIndex, 4).Value2 = $row.Eligibility
        $data.Cells.Item($rowIndex, 5).Value2 = $row.TargetWeaponOrTag
        $data.Cells.Item($rowIndex, 6).Value2 = $row.SuggestedName
        $data.Cells.Item($rowIndex, 7).Value2 = $row.SuggestedEffect
        $data.Cells.Item($rowIndex, 8).Value2 = $row.SuggestedMechanicType
        $data.Cells.Item($rowIndex, 9).Value2 = $row.SuggestedBaselineNumbers
        $data.Cells.Item($rowIndex, 10).Value2 = $row.QualitySuggestion
        $data.Cells.Item($rowIndex, 11).Value2 = $row.Priority
        $data.Cells.Item($rowIndex, 12).Value2 = $row.RuntimeNotes
        $data.Cells.Item($rowIndex, 13).Value2 = $row.YourDesignIntent
        $data.Cells.Item($rowIndex, 14).Value2 = $row.YourFinalEffect
        $data.Cells.Item($rowIndex, 15).Value2 = $row.YourFinalNumbers
        $data.Cells.Item($rowIndex, 16).Value2 = $row.Status
        $rowIndex++
    }

    $lastDataRow = $rows.Count + 1

    $data.Range('A1:P1').Font.Bold = $true
    $data.Range('A1:P1').Interior.ColorIndex = 15
    $data.Range('M1:P1').Interior.ColorIndex = 36
    $data.Range("M2:P$lastDataRow").Interior.ColorIndex = 19

    $tableRange = $data.Range("A1:P$lastDataRow")
    $tableRange.Borders.LineStyle = 1
    $tableRange.WrapText = $true

    $statusValidationRange = $data.Range("P2:P$lastDataRow")
    $statusValidationRange.Validation.Delete()
    $statusValidationRange.Validation.Add(3, 1, 1, '=Value_Guide!$H$2:$H$8')
    $statusValidationRange.Validation.IgnoreBlank = $true
    $statusValidationRange.Validation.InCellDropdown = $true
    $statusValidationRange.Validation.InputTitle = 'Status'
    $statusValidationRange.Validation.InputMessage = 'Select a state from the dropdown.'
    $statusValidationRange.Validation.ErrorTitle = 'Invalid status'
    $statusValidationRange.Validation.ErrorMessage = 'Choose a value from the configured status list.'

    $data.Rows.Item(1).AutoFilter() | Out-Null
    $data.Columns.AutoFit() | Out-Null

    $data.Activate() | Out-Null
    $excel.ActiveWindow.SplitRow = 1
    $excel.ActiveWindow.FreezePanes = $true

    $legend.Cells.Item(1, 1).Value2 = 'Legend'
    $legend.Cells.Item(2, 1).Value2 = 'Bucket'
    $legend.Cells.Item(2, 2).Value2 = 'Meaning'
    $legend.Cells.Item(3, 1).Value2 = 'UniversalStat'
    $legend.Cells.Item(3, 2).Value2 = 'Tier1 simple readable stat modulators.'
    $legend.Cells.Item(4, 1).Value2 = 'TagSpecific'
    $legend.Cells.Item(4, 2).Value2 = 'Tier1 simple bonuses for limited tag groups.'
    $legend.Cells.Item(5, 1).Value2 = 'UtilityDefensive'
    $legend.Cells.Item(5, 2).Value2 = 'Tier1 non-damage utility and defense coverage.'
    $legend.Cells.Item(6, 1).Value2 = 'UniversalCombined'
    $legend.Cells.Item(6, 2).Value2 = 'Tier2 combined stats and explicit tradeoffs.'
    $legend.Cells.Item(7, 1).Value2 = 'TagFamily'
    $legend.Cells.Item(7, 2).Value2 = 'Tier2 tag or family-focused combinations.'
    $legend.Cells.Item(8, 1).Value2 = 'WeaponDirectional'
    $legend.Cells.Item(8, 2).Value2 = 'Tier2 directional upgrades for specific weapons.'
    $legend.Cells.Item(9, 1).Value2 = 'Proc'
    $legend.Cells.Item(9, 2).Value2 = 'Tier3 proc identity.'
    $legend.Cells.Item(10, 1).Value2 = 'WeaponTransformation'
    $legend.Cells.Item(10, 2).Value2 = 'Tier3 one-per-weapon signature transformation.'
    $legend.Cells.Item(11, 1).Value2 = 'Capstone'
    $legend.Cells.Item(11, 2).Value2 = 'Tier3 rare universal build-defining effects.'

    $legend.Cells.Item(13, 1).Value2 = 'Quality guidance'
    $legend.Cells.Item(14, 1).Value2 = 'Flawed'
    $legend.Cells.Item(14, 2).Value2 = 'Reduced numeric strength only.'
    $legend.Cells.Item(15, 1).Value2 = 'Baseline'
    $legend.Cells.Item(15, 2).Value2 = 'Reference tuning target.'
    $legend.Cells.Item(16, 1).Value2 = 'Enhanced'
    $legend.Cells.Item(16, 2).Value2 = 'Higher numeric strength, same mechanic.'
    $legend.Cells.Item(17, 1).Value2 = 'Flawless'
    $legend.Cells.Item(17, 2).Value2 = 'Top numeric strength, same mechanic.'

    $legend.Range('A1:B2').Font.Bold = $true
    $legend.Columns.AutoFit() | Out-Null

    $valueGuide.Cells.Item(1, 1).Value2 = 'Value Encoding Guide'
    $valueGuide.Cells.Item(1, 1).Font.Bold = $true
    $valueGuide.Cells.Item(1, 1).Font.Size = 14

    $valueGuide.Cells.Item(3, 1).Value2 = 'Policy'
    $valueGuide.Cells.Item(3, 1).Font.Bold = $true
    $valueGuide.Cells.Item(4, 1).Value2 = 'Use true multiplicative xN.NN values only on Tier3, or on Tier2 rows that include a stated drawback.'
    $valueGuide.Cells.Item(5, 1).Value2 = 'Tier1 guidance: use additive +N% or flat +N formats for readability.'
    $valueGuide.Cells.Item(6, 1).Value2 = 'Tier2 guidance: multiplicative values are allowed only when accompanied by downside in the same row.'
    $valueGuide.Cells.Item(7, 1).Value2 = 'Tier3 guidance: multiplicative and coefficient forms are expected for procs/transformations/capstones.'

    $valueGuide.Cells.Item(9, 1).Value2 = 'Token'
    $valueGuide.Cells.Item(9, 2).Value2 = 'Meaning'
    $valueGuide.Cells.Item(9, 3).Value2 = 'Example'
    $valueGuide.Cells.Item(9, 4).Value2 = 'Notes'
    $valueGuide.Range('A9:D9').Font.Bold = $true
    $valueGuide.Range('A9:D9').Interior.ColorIndex = 15

    $valueGuide.Cells.Item(10, 1).Value2 = '+N%'
    $valueGuide.Cells.Item(10, 2).Value2 = 'Additive percent bonus or penalty.'
    $valueGuide.Cells.Item(10, 3).Value2 = '+12% damage, -8% cooldown'
    $valueGuide.Cells.Item(10, 4).Value2 = 'Preferred for Tier1 and non-transform Tier2 documentation.'

    $valueGuide.Cells.Item(11, 1).Value2 = 'xN.NN'
    $valueGuide.Cells.Item(11, 2).Value2 = 'True multiplicative scalar.'
    $valueGuide.Cells.Item(11, 3).Value2 = 'x1.30 damage, x0.82 cooldown'
    $valueGuide.Cells.Item(11, 4).Value2 = 'Use on Tier3, or Tier2 rows with explicit drawback.'

    $valueGuide.Cells.Item(12, 1).Value2 = '+N'
    $valueGuide.Cells.Item(12, 2).Value2 = 'Flat integer or point addition.'
    $valueGuide.Cells.Item(12, 3).Value2 = '+1 projectile, +10 crit chance'
    $valueGuide.Cells.Item(12, 4).Value2 = 'Use for count/chance style effects.'

    $valueGuide.Cells.Item(13, 1).Value2 = '0.NN'
    $valueGuide.Cells.Item(13, 2).Value2 = 'Ratio/coefficient value.'
    $valueGuide.Cells.Item(13, 3).Value2 = '0.35 chain damage scale'
    $valueGuide.Cells.Item(13, 4).Value2 = 'Common for Tier3 proc payloads.'

    $valueGuide.Cells.Item(14, 1).Value2 = 'Ns / Nm'
    $valueGuide.Cells.Item(14, 2).Value2 = 'Time or range units.'
    $valueGuide.Cells.Item(14, 3).Value2 = '4.0s mark duration, 18m chain range'
    $valueGuide.Cells.Item(14, 4).Value2 = 'Keeps numbers unambiguous when filling final specs.'

    $valueGuide.Cells.Item(16, 1).Value2 = 'Effect field mapping'
    $valueGuide.Cells.Item(16, 1).Font.Bold = $true
    $valueGuide.Cells.Item(17, 1).Value2 = 'Field'
    $valueGuide.Cells.Item(17, 2).Value2 = 'Runtime meaning'
    $valueGuide.Cells.Item(17, 3).Value2 = 'Typical format'
    $valueGuide.Range('A17:C17').Font.Bold = $true
    $valueGuide.Range('A17:C17').Interior.ColorIndex = 15

    $valueGuide.Cells.Item(18, 1).Value2 = 'Magnitude'
    $valueGuide.Cells.Item(18, 2).Value2 = 'Primary scalar/chance/coefficient value.'
    $valueGuide.Cells.Item(18, 3).Value2 = '+12%, x1.25, 0.35'

    $valueGuide.Cells.Item(19, 1).Value2 = 'MagnitudeB'
    $valueGuide.Cells.Item(19, 2).Value2 = 'Secondary numeric payload (duration/range/alt scalar).'
    $valueGuide.Cells.Item(19, 3).Value2 = '4.0s, 18m, 0.55'

    $valueGuide.Cells.Item(20, 1).Value2 = 'MagnitudeC'
    $valueGuide.Cells.Item(20, 2).Value2 = 'Tertiary numeric payload where needed.'
    $valueGuide.Cells.Item(20, 3).Value2 = '2.8s, 1.45'

    $valueGuide.Cells.Item(21, 1).Value2 = 'Count'
    $valueGuide.Cells.Item(21, 2).Value2 = 'Primary integer count.'
    $valueGuide.Cells.Item(21, 3).Value2 = '+1, +2'

    $valueGuide.Cells.Item(22, 1).Value2 = 'CountB / Key'
    $valueGuide.Cells.Item(22, 2).Value2 = 'Stored metadata fields (currently limited runtime use).'
    $valueGuide.Cells.Item(22, 3).Value2 = 'Use only if implementation explicitly consumes them.'

    $valueGuide.Cells.Item(1, 8).Value2 = 'Status options'
    $valueGuide.Cells.Item(1, 8).Font.Bold = $true
    $valueGuide.Cells.Item(2, 8).Value2 = 'Planned'
    $valueGuide.Cells.Item(3, 8).Value2 = 'Drafted'
    $valueGuide.Cells.Item(4, 8).Value2 = 'Needs Runtime Hook'
    $valueGuide.Cells.Item(5, 8).Value2 = 'Ready for Build'
    $valueGuide.Cells.Item(6, 8).Value2 = 'Implemented'
    $valueGuide.Cells.Item(7, 8).Value2 = 'Playtest'
    $valueGuide.Cells.Item(8, 8).Value2 = 'Rework'

    $currentAndProposedHeaders = @(
        'IdentityId',
        'CatalogStatus',
        'Tier',
        'Name',
        'SourceBucket',
        'Eligibility',
        'TargetWeaponOrTag',
        'EffectSummary',
        'MechanicType',
        'BaselineNumbers',
        'QualitySuggestion',
        'Notes'
    )

    for ($column = 0; $column -lt $currentAndProposedHeaders.Count; $column++) {
        $currentAndProposed.Cells.Item(1, $column + 1).Value2 = $currentAndProposedHeaders[$column]
    }

    $currentAndProposed.Range('A1:L1').Font.Bold = $true
    $currentAndProposed.Range('A1:L1').Interior.ColorIndex = 15

    $currentRows = @(
        @('C-001','Current','Tier1','Power Coil','Core','Universal','all','Stacks raw damage into weapon core.','DamagePercent substat','+22% damage','Fallback catalog'),
        @('C-002','Current','Tier1','Accelerator Fins','Core','Universal','all','Speeds attack cadence and crit frequency.','AttackSpeed + CritChance substats','+18% attack speed, +6 crit chance','Fallback catalog'),
        @('C-003','Current','Tier1','Long Barrel','Core','Universal','all','Extends reach and critical impact.','Range + CritDamage substats','+10% range, +14% crit damage','Fallback catalog'),
        @('C-004','Current','Tier1','Siege Core','Core','Universal','all','Heavier impact profile.','DamagePercent + Size substats','+28% damage, +10% size','Fallback catalog'),
        @('C-005','Current','Tier1','Rebound Array','Projectile','Universal','all','Adds ricochet and bounce behavior.','Ricochet + BounceTimes substats','+1 ricochet, +1 bounce','Fallback catalog'),
        @('C-006','Current','Tier1','Split Chamber','Projectile','Universal','all','Adds a second firing lane.','ProjectileCount + DamagePercent substats','+1 projectile, +10% damage','Fallback catalog'),
        @('C-007','Current','Tier1','Storm Relay','Projectile','Universal','all','Aggressive multishot profile.','ProjectileCount + AttackSpeed + Range substats','+1 projectile, +14% attack speed, +6% range','Fallback catalog'),
        @('C-008','Current','Tier1','Reinforced Shell','Sustain','Universal','all','Durability and regeneration baseline.','HealthPercent + HealthRegen substats','+18% health, +2 regen','Fallback catalog'),
        @('C-009','Current','Tier1','Hemophage Loop','Sustain','Universal','all','Sustain and mobility package.','LifeSteal + MovementSpeed substats','+4 lifesteal, +8% move speed','Fallback catalog'),
        @('C-010','Current','Tier1','Lucky Prism','Utility','Universal','all','Drop/crit utility profile.','Luck + CritChance substats','+18 luck, +8 crit chance','Fallback catalog'),
        @('C-011','Current','Tier1','Sharpened Form','Tier1 Set','Universal','all','Simple baseline damage increase.','DamageMultiplier effect','x1.12 damage','Fallback catalog'),
        @('C-012','Current','Tier1','Quickened Core','Tier1 Set','Universal','all','Simple cadence increase.','CooldownMultiplier effect','x0.90 cooldown','Fallback catalog'),
        @('C-013','Current','Tier1','Expanded Shape','Tier1 Set','Universal','all','Simple area increase.','AreaMultiplier effect','x1.14 area','Fallback catalog'),
        @('C-014','Current','Tier1','Long Reach','Tier1 Set','Universal','all','Simple range increase.','RangeMultiplier effect','x1.18 range','Fallback catalog'),
        @('C-015','Current','Tier1','Accelerated Flight','Tier1 Set','Tag-based','square|circle|pyramid|hypercone|hyperphere','Projectile delivery speed increase.','ProjectileSpeedMultiplier effect','x1.20 projectile speed','Fallback catalog'),
        @('C-016','Current','Tier1','Lasting Pattern','Tier1 Set','Tag-based','mine|orbital|zone|clone','Longer persistent object lifetime.','DurationMultiplier effect','x1.22 duration','Fallback catalog'),
        @('C-017','Current','Tier1','Piercing Edge','Tier1 Set','Tag-based','square|circle|pyramid|hypercone','Adds extra pass-through.','PierceAdd effect','+1 pierce','Fallback catalog'),
        @('C-018','Current','Tier1','Focused Edge','Tier1 Set','Universal','all','Simple crit frequency increase.','CritChanceAdd effect','+8 crit chance','Fallback catalog'),
        @('C-019','Current','Tier1','Rupture Crest','Tier1 Set','Universal','all','Simple crit damage increase.','CritDamageMultiplier effect','x1.25 crit damage','Fallback catalog'),
        @('C-020','Current','Tier1','Guiding Bias','Tier1 Set','Tag-based','pyramid','Homing correction increase.','HomingStrengthMultiplier effect','x1.20 homing strength','Fallback catalog'),
        @('C-021','Current','Tier2','Reinforced Geometry','Tier2 Set','Universal','all','Damage plus area package.','DamageMultiplier + AreaMultiplier effects','x1.15 damage, x1.12 area','Fallback catalog'),
        @('C-022','Current','Tier2','Heavy Payload','Tier2 Set','Tag-based','circle|hyperphere','Big area with slower travel.','AreaMultiplier + ProjectileSpeedMultiplier effects','x1.35 area, x0.85 projectile speed','Fallback catalog'),
        @('C-023','Current','Tier2','Overclocked Core','Tier2 Set','Universal','all','Faster cadence with weaker hits.','CooldownMultiplier + DamageMultiplier effects','x0.72 cooldown, x0.86 damage','Fallback catalog'),
        @('C-024','Current','Tier2','Extra Output','Tier2 Set','Tag-based','square|hexagon|cube|pyramid|pentachoron|boys','Additional instance with less per-hit strength.','ProjectileCountAdd + DamageMultiplier effects','+1 projectile, x0.76 damage','Fallback catalog'),
        @('C-025','Current','Tier2','Lancing Spiral','Tier2 Set','Tag-based','square|circle|pyramid','Range and passthrough pressure.','PierceAdd + RangeMultiplier effects','+1 pierce, x1.16 range','Fallback catalog'),
        @('C-026','Current','Tier2','Predator Lens','Tier2 Set','Tag-based','pyramid','Guidance and speed package.','HomingStrengthMultiplier + ProjectileSpeedMultiplier effects','x1.26 homing, x1.15 speed','Fallback catalog'),
        @('C-027','Current','Tier3','Volitile','Proc','Universal','all','All player damage causes extra detonation.','Custom Tier3 proc behavior','50% detonation damage, staged radius','Fallback catalog'),
        @('C-028','Current','Tier3','Echo Chamber','Advanced','Universal','all','Repeats fired weapon after delay.','EchoAfterDelay effect','Delay 0.45s, echo power 0.55, count 1','Fallback catalog'),
        @('C-029','Current','Tier3','Chain Reaction','Advanced','Tag-based','precision|homing|piercing','Direct hits arc to nearby enemies.','ChainToNearby effect','0.45 damage scale, 18 range, 2 chains','Fallback catalog'),
        @('C-030','Current','Tier3','Lingering Scar','Advanced','Tag-based','precision|homing|piercing','Direct hits create short-lived damage zone.','LingeringZone effect','0.35 scale, 2.8 radius, 2.8s duration','Fallback catalog'),
        @('C-031','Current','Tier3','Predatory Mark','Advanced','Tag-based','precision|homing|piercing','Marked targets take amplified follow-up damage.','ApplyMark effect','x1.22 mark amp, 4.0s','Fallback catalog'),
        @('C-032','Current','Tier3','Finisher Gate','Advanced','Universal','all','Extra damage versus low-health targets.','ExecuteLowHealth effect','0.18 threshold, x1.85 execute, x1.45 boss cap','Fallback catalog'),
        @('C-033','Current','Tier3','Boss Geometry','Advanced','Universal','all','Elite/boss specialization trade profile.','EliteBossHunter effect','x1.40 elite/boss, x0.85 normal','Fallback catalog'),
        @('C-034','Current','Tier3','Wing Man','Wing','Tag-based','support','Escort wing behavior.','Wing behavior key','Tier3 wing identity','Fallback catalog'),
        @('C-035','Current','Tier3','Wing Woman','Wing','Tag-based','support','Hunter wing behavior.','Wing behavior key','Tier3 wing identity','Fallback catalog'),
        @('C-036','Current','Tier3','Wing Brother','Wing','Tag-based','support','Autonomous roaming wing behavior.','Wing behavior key','Tier3 wing identity','Fallback catalog'),
        @('C-037','Current','Tier3','Wing Sister','Wing','Tag-based','support','Fire-control wing behavior.','Wing behavior key','Tier3 wing identity','Fallback catalog')
    )

    $combinedRowIndex = 2
    foreach ($entry in $currentRows) {
        $currentAndProposed.Cells.Item($combinedRowIndex, 1).Value2 = $entry[0]
        $currentAndProposed.Cells.Item($combinedRowIndex, 2).Value2 = $entry[1]
        $currentAndProposed.Cells.Item($combinedRowIndex, 3).Value2 = $entry[2]
        $currentAndProposed.Cells.Item($combinedRowIndex, 4).Value2 = $entry[3]
        $currentAndProposed.Cells.Item($combinedRowIndex, 5).Value2 = $entry[4]
        $currentAndProposed.Cells.Item($combinedRowIndex, 6).Value2 = $entry[5]
        $currentAndProposed.Cells.Item($combinedRowIndex, 7).Value2 = $entry[6]
        $currentAndProposed.Cells.Item($combinedRowIndex, 8).Value2 = $entry[7]
        $currentAndProposed.Cells.Item($combinedRowIndex, 9).Value2 = $entry[8]
        $currentAndProposed.Cells.Item($combinedRowIndex, 10).Value2 = $entry[9]
        $currentAndProposed.Cells.Item($combinedRowIndex, 11).Value2 = Get-CurrentQualitySuggestion -Tier $entry[2]
        $currentAndProposed.Cells.Item($combinedRowIndex, 12).Value2 = $entry[10]

        $combinedRowIndex++
    }

    foreach ($row in $rows) {
        $currentAndProposed.Cells.Item($combinedRowIndex, 1).Value2 = $row.RowId
        $currentAndProposed.Cells.Item($combinedRowIndex, 2).Value2 = 'Proposed'
        $currentAndProposed.Cells.Item($combinedRowIndex, 3).Value2 = $row.Tier
        $currentAndProposed.Cells.Item($combinedRowIndex, 4).Value2 = $row.SuggestedName
        $currentAndProposed.Cells.Item($combinedRowIndex, 5).Value2 = $row.Bucket
        $currentAndProposed.Cells.Item($combinedRowIndex, 6).Value2 = $row.Eligibility
        $currentAndProposed.Cells.Item($combinedRowIndex, 7).Value2 = $row.TargetWeaponOrTag
        $currentAndProposed.Cells.Item($combinedRowIndex, 8).Value2 = $row.SuggestedEffect
        $currentAndProposed.Cells.Item($combinedRowIndex, 9).Value2 = $row.SuggestedMechanicType
        $currentAndProposed.Cells.Item($combinedRowIndex, 10).Value2 = $row.SuggestedBaselineNumbers
        $currentAndProposed.Cells.Item($combinedRowIndex, 11).Value2 = $row.QualitySuggestion
        $currentAndProposed.Cells.Item($combinedRowIndex, 12).Value2 = $row.RuntimeNotes
        $combinedRowIndex++
    }

    $combinedLastRow = $combinedRowIndex - 1
    $currentAndProposed.Range("A1:L$combinedLastRow").Borders.LineStyle = 1
    $currentAndProposed.Range("A1:L$combinedLastRow").WrapText = $true
    $currentAndProposed.Rows.Item(1).AutoFilter() | Out-Null
    $currentAndProposed.Columns.AutoFit() | Out-Null

    $currentAndProposed.Range("B2:B38").Interior.ColorIndex = 35
    if ($combinedLastRow -ge 39) {
        $currentAndProposed.Range("B39:B$combinedLastRow").Interior.ColorIndex = 19
    }

    $summary.Cells.Item(19, 1).Value2 = 'Current identities loaded'
    $summary.Cells.Item(19, 2).Value2 = 37
    $summary.Cells.Item(20, 1).Value2 = 'Proposed identities loaded'
    $summary.Cells.Item(20, 2).Value2 = 63
    $summary.Cells.Item(21, 1).Value2 = 'Combined identities loaded'
    $summary.Cells.Item(21, 2).Formula = '=B19+B20'

    $valueGuide.Columns.AutoFit() | Out-Null
    $valueGuide.Range('A1:D22').WrapText = $true

    $summary.Columns.AutoFit() | Out-Null
    $summary.Range('A1:B1').Font.Bold = $true
    $summary.Range('A6:D6').Font.Bold = $true
    $summary.Range('A10:D10').Font.Bold = $true
    $summary.Range('A17:A21').Font.Bold = $true

    $workbook.SaveAs($outputPath, 51)
    $workbook.Close($true)
    $excel.Quit()

    Write-Output "Created workbook: $outputPath"
    Write-Output "Row count: $($rows.Count)"
}
finally {
    if ($legend -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($legend)
    }

    if ($currentAndProposed -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($currentAndProposed)
    }

    if ($valueGuide -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($valueGuide)
    }

    if ($data -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($data)
    }

    if ($summary -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
    }

    if ($workbook -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($workbook)
    }

    if ($excel -ne $null) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
