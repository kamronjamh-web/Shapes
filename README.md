# Shapes

A Godot C# first-person horde-defense prototype. Shapes define base weapon identities; installed modulators customize their behavior. Enemy shapes drop run currency and occasionally weapon shapes or modulators.

Open `project.godot` in Godot .NET, enter the hub, and select **North Gate** to play the first map. The playable terrain covers 200 × 200 metres. A simple enclosed room stands on an eight-metre hill and houses the core and appearing shop. Its North Gate opens toward the north foot of the hill, where 100 cube enemies spawn together and climb toward the room. The other room walls remain closed.

- **WASD / mouse / Space:** move, look, and jump.
- **Left / right mouse:** fire / aim.
- **Q / E / mouse wheel:** switch collected weapons.
- **F:** open or close an available shop within range.
- **N:** start the next wave during preparation.
- **Esc:** close the shop or open the pause menu.

Shops appear during preparation and after enough kills during combat. Combat continues while shopping during a wave. Shops sell weapon shapes, shape levels, core repairs, and compatible modulators; recovered modulators can be installed for free. Uncollected loot is gathered at wave clear.

All 100 current catalog modulators have North Gate runtime support, including the 16 weapon transforms, critical-hit procs, survival stats, Luck, and all four Wings. Square fires moving bullets; flight speed, piercing, ricochets, and wall bounces affect real attacks. Shops rotate shapes from the full weapon catalog and offer parts compatible with their attack family. Wings require a support weapon after the first arsenal slot, with one Wing per weapon and one of each Wing type per run. Incompatible parts on imported hub weapons move into the run's recovered parts for installation on another shape; saved equipment stays intact.

Map dimensions, spawn count, loot rates, and shop prices are editable in `Resources/Fps/HillFort_*.tres`. Hill dimensions support live changes while preserving the core and run equipment. Crowd movement, decision frequency, and separation settings apply live; changing allocated crowd capacity requires restarting the scene. Enemy archetypes are sampled when the crowd spawns.

The crowd uses packed enemy state, a spatial grid, and two shared MultiMesh renderers. Weapons use individual enemy handles for piercing, area attacks, homing, and modulator effects; minions do not create individual physics bodies or mesh nodes.

Build with `dotnet build SuperGatchaRebuild.csproj`. The Godot integration validation scene is `Tools/Validation/ValidateHillHorde.tscn`; `RunHillHordeValidation.ps1` runs it with isolated save data when given a Godot .NET executable path. `CaptureHillHorde.tscn` creates rendered previews and a simulation-only timing diagnostic under `.tmp/hill-horde-validation` using the same isolated environment. The earlier lane prototype remains in `NorthGateLaneSandbox.tscn`, covered by `ValidateNorthGate.tscn`.

`Tools/Validation/ValidateFpsModulators.tscn` exercises all 100 catalog entries and targeted combat behaviors against real crowd targets. `RunFpsModulatorValidation.ps1` isolates saves under `.tmp/fps-modulator-validation`. Latest validation: 417 modulator checks, 48 hill/horde checks, and 37 lane regression checks passed.

Current scope: one 100-enemy assault; currency, collected equipment, and purchased upgrades last for the current run. Crowd movement uses lightweight separation and room-wall constraints. Base construction, larger population stress testing, and additional approach directions remain future work. Allocated capacity is not a performance guarantee or a spawning layout for larger hordes.
