MULTIWEAPON BLENDER ASSETS

The collection contains all sixteen shape families. Square is the original
approved reference. CreateMultiWeaponAssets.py builds the other fifteen from its
materials and driven cartridge interfaces. Stars follow the shape catalog;
Common-Divine item rarity separately controls accent color and unlocked bays.

ONE AND TWO STARS
Square, Circle, Triangle, Hexagon, Sphere, Cube, Pyramid, Cone use the shared
handgun frame with distinct upper assemblies and dimensional shape inserts.

THREE STARS - COMPLETE BODY REDESIGNS
Tesseract: two-handed rift frame with nested cube mechanisms.
Hyperphere: shoulder mortar with a spherical core and containment gimbals.
Pentachoron: electrical gauntlet with five articulated capacitor nodes.
Hypercone: long telescoping drill lance with counter-rotating sleeves.

FOUR STARS - COMPLETE BODY REDESIGNS
Impossible Cube: fractured floating cube enclosing a singularity.
Mobius: continuous half-twist ribbon around a forearm, with temporal emitters.
Klein: levitating self-threading vessel with expanding containment bands.
Boy's Surface: floating interwoven sculpture with unfolding clone mirrors.

Higher tiers retain the Square material language and working customization
interfaces, fitted to new forms. Their source handgun bodies are removed. Each
has a recognizable miniature shape cartridge and all ten modulator sockets.

FILES AND CONTROLS
Each Art/WeaponConcepts/<Asset> folder contains a self-contained .blend, static
review .glb, model-info.json, README.txt and four PNG review angles. Open the
.blend directly: the generators are not required. Bevels, parts and keyframes
remain editable. Concept folders have .gdignore to exclude studios from Godot.

Select the shape insert and edit rarity_tier in Object Properties > Custom
Properties: 0 Common, 1 Uncommon, 2 Rare, 3 Legendary, 4 Mythic, 5 Divine.
GunRoot.weapon_level is 1-5; GunRoot.equipped_modulators is 0-10.
Default review: Rare, level 5, three installed cartridges. Play frames 1-40 at
30 fps to inspect the mechanism; frame 1 is the static export pose.

Metres, Blender +Y forward/+Z up. GLB converts to -Z forward/+Y up.
GunRoot, ShapeSocket, MuzzleAnchor and ModulatorSocket01-10 remain named anchors.

REGENERATE WITH MICROSOFT STORE BLENDER
From the repository root in PowerShell:
& "$env:LOCALAPPDATA\Microsoft\WindowsApps\blender-launcher.exe" --background --factory-startup --python "$PWD\Tools\Art\CreateMultiWeaponAssets.py" -- --weapons tesseract hyperphere pentachoron hypercone impossiblecube mobius klein boys

Optional --source overrides the Square .blend reference. --no-render skips PNGs.
Without --weapons all fifteen generated forms are rebuilt. To preserve approved
assets, explicitly list only shapes you intend to change. The default source is
Art/WeaponConcepts/SquareHandgun/SquareHandgun.blend. A missing source can be
retrieved read-only from local Git revision 8c02b8d by the generator.

The Store launcher can return early; inspect .tmp/multiweapon-build-status.json
and .tmp/multiweapon-build.log for completion. Validation covers 90 combinations
per asset (six item rarities, five levels, three installed counts), finite mesh
geometry, dimensional inserts, all ten anchors and source animation after reopen.

REVIEW
Run ReviewRemainingMultiWeaponAssets.py through the same Blender launcher. It
reopens saved sources, checks mechanism animation, reimports static GLBs and
renders the 3-star and 4-star boards, plus affected earlier comparison boards.
The boards normalize display sizes; source files retain actual metre dimensions.
Completion: .tmp/multiweapon-remaining-review-status.json.
Use ReviewMultiWeaponAssets.py -- --weapons <three or four keys> for one board.

Run python Tools/Art/BuildMultiWeaponGallery.py to rebuild the offline gallery:
Art/WeaponConcepts/MultiWeaponCollection/Weapons.html. It has star filters,
four review angles per asset, and links to the self-contained Blender sources.

RUNTIME GAME ASSETS
All sixteen shape models are assigned in the game, including Cone and the eight
redesigned 3-/4-star bodies. Each redesigned body has individual first-person
hip/aim framing; the inspector keeps the authored metre-scale model. All fifteen
generated forms play their imported mechanism on successful shots.

ExportMultiWeaponRuntime.py supports all fifteen generated shape keys. Explicitly
pass --weapons to select exports. It includes every locked/empty/installed bay
variant and one sampled Fire animation, stripping preview drivers without
modifying the .blend. Anchors retain their moving parent hierarchy. Static review
GLBs must not replace these runtime exports. Preserve the Mobius runtime import
setting meshes/generate_lods=false to avoid artifacts on its layered ribbon.
Completion: .tmp/multiweapon-runtime-status.json; log: .tmp/multiweapon-runtime.log.

The game visual factory controls item rarity and bay visibility. Runtime checks
pass 1,054 weapon-inspection cases and 417 combat/modulator cases. Use
Tools/Validation/RunWeaponInspectionValidation.ps1 for integration checks and
RunWeaponInspectionCapture.ps1 for rendered hip/aim/fire/inspector/mechanism views
of all sixteen shapes. Add -Shapes 'hypercone,pentachoron' for a selected review.

Built with the installed Microsoft Store Blender 5.2.2 LTS.

PAUSED FPS WORKBENCH ANIMATIONS
Derived editable sources are Art/WeaponWorkbenchAnimations/*Workbench.blend.
Approved Art/WeaponConcepts sources remain untouched. Workbench authoring uses
the portable Blender 4.5.14 LTS at .tmp/tools/blender-4.5.14-windows-x64/blender.exe;
opening the newer approved sources can print version warnings. Verify output
manifests and standalone checks rather than treating Blender exit code as proof.

From the repository root in PowerShell:
& '.tmp/tools/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --python Tools/Art/ExportWeaponWorkbenchAnimations.py -- --weapons square circle
Omit --weapons to author all sixteen. Run this AFTER any runtime geometry export.
It appends only animation channels/buffers to existing runtime GLBs; nodes, meshes,
materials and original Fire data remain unchanged. Existing .glb.import settings
are not written. Reruns replace workbench animation tails rather than accumulating
unused animation data. Completion: .tmp/weapon-workbench-animation/status.json.

All sixteen models expose Disassemble and Assemble (0.70 seconds), plus
SocketSeat01-10 and SocketRemove01-10 (0.40 seconds). Assembly clips rotate and
translate individual authored components around the shape dock; GunRoot and
bay visibility stay caller-owned. Socket clips move cartridges on local +Z.
The fifteen existing Fire clips are preserved; Square had no original Fire clip.
Derived sources have named NLA tracks; Fire is active by default. To inspect a
workbench clip, mute other tracks and unmute the same named track on its animated
objects. Scrub frames 1-22 for assembly or 1-13 for cartridge motion at 30 fps.
Exported clip times start at zero. FpsWeaponWorkbenchAnimations advances clips
manually during pause, holds disassembled/removal endpoints, keeps the core
stationary and restores exact instance rest transforms after assembly/seating.

Standalone GLB validation without Godot:
& '.tmp/tools/blender-4.5.14-windows-x64/4.5/python/bin/python.exe' Tools/Art/ValidateWeaponWorkbenchAnimations.py
Checks all sixteen clips/contracts, finite samples, assembled/seated rest poses,
root ownership and approved source hashes; writes validation.json beside status.
This does not establish visual acceptance or runtime playback correctness.

Lightweight authored modulator thumbnail:
& '.tmp/tools/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --python Tools/Art/ExportWorkbenchCartridge.py
Exports Resources/Weapons/WorkbenchCartridge.glb and isolated editable
Art/WeaponWorkbenchAnimations/WorkbenchCartridge.blend from Square's first bay.
The five-object cartridge retains its shell, raised glyph and light bars; runtime
identity color uses material ModulatorAccent. It is 18 mm wide, 13.5 mm tall and
4.5 mm thick. Godot face normal +Y, top -Z; rotate about X by roughly +pi/2 for
a front thumbnail camera looking from +Z. Overrides must belong to each instance.
