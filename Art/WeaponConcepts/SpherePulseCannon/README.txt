Sphere - Pulse Cannon

Nova Pulse cannon: faceted sphere chamber with eight captured radial armor petals, an equatorial pulse band, and a squared support cradle.

Open SpherePulseCannon.blend in Blender. The saved scene opens at the rest pose.
Shared frame, ribbed grip, trigger guard, shape dock, and ten modulator bays come from the approved Square handgun.
The upper assembly and matching shape insert are newly authored. Bevels remain editable.

CONTROLS
Select SphereInsert | removable shape cartridge; Object Properties > Custom Properties > rarity_tier.
0 Common (white), 1 Uncommon (green), 2 Rare (blue), 3 Legendary (purple), 4 Mythic (red), 5 Divine (gold).
GunRoot.weapon_level: 1-5. GunRoot.equipped_modulators: 0-10. Original game unlock rules are preserved.
Default: Rare, level 5, three installed cartridges, six unlocked bays.
Press Play on the timeline for the mechanism study (frames 1-40, 30 fps). Frame 1 is the export pose.

HANDOFF
SpherePulseCannon.glb is a static snapshot of the default preview with the studio excluded.
The Blender source retains all empty/locked/installed socket geometry, drivers and keyframes.
GunRoot / ShapeSocket / MuzzleAnchor / ModulatorSocket01-10 are preserved. Named mechanism pivots are separate.
Metres. Blender +Y forward and +Z up; GLB converts to -Z forward and +Y up.
Renders contains front three-quarter, side silhouette, shape-slot and mechanism-pose views.
Runtime game export: Resources/Weapons; regenerate using Tools/Art/ExportMultiWeaponRuntime.py.

Validation: 90 rarity/level/equipment combinations; ten sockets; finite geometry; source reopened; GLB hierarchy inspected.
Visible default model: 29256 triangles. Built with Blender 5.2.2 LTS.
