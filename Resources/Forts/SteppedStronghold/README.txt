LIVE STEPPED STRONGHOLD

SteppedStronghold.glb is the actual selected Blender architecture used by
NorthGate, not a procedural visual approximation. Runtime builds invisible
physics bodies from CollisionProxies.tres and feeds the same 39 boxes and 16
ramps to CPU crowd queries, spatial masks, GPU snapshots and corpse geometry.
collision-proxies.json is the human-readable equivalent. export-info.json records
the source hash and exported names. Review .blend remains unchanged.

Regenerate with Microsoft Store blender-launcher.exe --background
--factory-startup --python Tools/Art/ExportSteppedStrongholdRuntime.py, then import
the GLB with Godot. Collection 01-05A mesh architecture is selected explicitly;
studio, labels, scale figures and proxy meshes are excluded. Objective and turret
equipment placeholders are excluded because live gameplay provides those.
Exterior landmark/ribbon vertices conform to the default live hill slope instead
of floating over the review studio ground. These are decorative, with no added
physics proxies. Decorative parapets and shop shelter also have no collision.
Runtime lifts CourtyardFoundation by 0.02m to separate its top from the hill
floor and prevent z-fighting. The offset applies again after each arena rebuild.

Blender x,y,z becomes Godot x,z,-y. Inward ascent ramps reverse their longitudinal
axis so every FortRamp has Start < End and increases from Base to Top.

SteppedStrongholdEnabled defaults false on generic settings and true in the live
HillFort_Hill resource. The authored fort fixes the 200m courtyard, summit half
span102m, open24m lanes, connected122m ring, 30m fallback, and all deck/ramp sizes.
Room, gate, wall-walk and access-ramp dimensions tune the legacy fort only.
SummitHeight shifts architecture and all authoritative proxies together; live
objective, shop and player anchors sample platform/ramp tops. Playable extent
and hill-foot settings remain usable, with enough minimum space for architecture.
For geometric redesign, edit the Blender source/proxies and re-export together.
