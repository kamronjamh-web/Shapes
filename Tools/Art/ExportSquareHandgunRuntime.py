"""Export approved Blender source to a complete, switchable runtime GLB.

Run with Blender --background --python Tools/Art/ExportSquareHandgunRuntime.py.
Does not modify the editable review source. Blender +Y/+Z maps to Godot -Z/+Y.
"""
import bpy
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Resources/Weapons"
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT / "Art/WeaponConcepts/SquareHandgun/SquareHandgun.blend"))
gun = bpy.data.objects["GunRoot"]
objects = [gun] + list(gun.children_recursive)
manifest = []
for obj in objects:
    original = obj.name
    # All visibility is owned by EquipmentItem at runtime, never by review drivers.
    obj.animation_data_clear()
    obj.hide_render = False
    obj.hide_viewport = False
    obj.hide_set(False)
    slot = re.match(r"Modulator (\d+) \| (.*)", original)
    if slot:
        role = slot[2]
        if obj.get("preview_example"):
            role = "Cartridge"
            for key in ("preview_example", "modulator_key"):
                if key in obj:
                    del obj[key]
        obj.name = "".join(word.capitalize() for word in re.findall(r"[a-zA-Z0-9]+", role))
    else:
        obj.name = re.sub(r"[^a-zA-Z0-9_]", "_", original)
    obj["source_name"] = original
    manifest.append({"name": obj.name, "source": original, "parent": obj.parent.name if obj.parent else None})
for material in bpy.data.materials:
    material.animation_data_clear()
    if material.node_tree:
        material.node_tree.animation_data_clear()
    if material.name.startswith("08 |"):
        material.name = "RarityAccent"
gun["status"] = "Runtime geometry; EquipmentItem controls rarity and all ten socket states"
for key in ("weapon_level", "equipped_modulators"):
    if key in gun:
        del gun[key]
insert = next(obj for obj in objects if obj.name.startswith("SquareInsert___removable"))
for key in ("rarity_tier", "rarity_legend"):
    if key in insert:
        del insert[key]
bpy.ops.object.select_all(action="DESELECT")
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.update()
bpy.ops.export_scene.gltf(
    filepath=str(OUT / "SquareHandgun.glb"), export_format="GLB", use_selection=True,
    use_visible=False, export_apply=True, export_cameras=False, export_lights=False,
    export_yup=True, export_extras=True, export_animations=False)
(OUT / "SquareHandgun.manifest.json").write_text(json.dumps({
    "coordinates": "meters; Godot +X right, +Y up, -Z forward; Blender (x,y,z) -> (x,z,-y)",
    "objects": manifest}, indent=2), encoding="utf-8")
print("RUNTIME_EXPORT_COMPLETE", len(objects))
