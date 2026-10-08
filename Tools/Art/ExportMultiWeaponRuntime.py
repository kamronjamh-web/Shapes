"""Export complete MultiWeapon geometry and a sampled Fire clip from approved sources.

Run in Microsoft Store Blender via blender-launcher.exe --background --factory-startup
--python Tools/Art/ExportMultiWeaponRuntime.py [-- --weapons circle triangle ...].
Review .blend files are read only. Runtime exports include hidden cartridge/lock
variants; EquipmentItem selects their visibility and colors in Godot.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import traceback

import bpy

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Resources/Weapons"
STATUS = ROOT / ".tmp/multiweapon-runtime-status.json"
ASSETS = {
    "circle": "CircleSawblade",
    "triangle": "TriangleBeamCarbine",
    "hexagon": "HexagonMineLauncher",
    "sphere": "SpherePulseCannon",
    "cube": "CubeGuardProjector",
    "pyramid": "PyramidSeekerLauncher",
    "cone": "ConeFlameProjector",
    "tesseract": "TesseractRiftEngine",
    "hyperphere": "HyperphereMortar",
    "pentachoron": "PentachoronArcLattice",
    "hypercone": "HyperconeDrill",
    "impossiblecube": "ImpossibleCubeSingularity",
    "mobius": "MobiusEchoWeaver",
    "klein": "KleinAuraVessel",
    "boys": "BoysSurfaceClonePrism",
}


def status(stage, **data):
    STATUS.write_text(json.dumps({"stage": stage, **data}, indent=2), encoding="utf-8")
    print(stage, data, flush=True)


def export(key):
    name = ASSETS[key]
    source = ROOT / "Art/WeaponConcepts" / name / (name + ".blend")
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    info = json.loads(source.with_name("model-info.json").read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(source))
    scene = bpy.context.scene
    scene.name = "Fire"
    scene.frame_start, scene.frame_end = 1, 40
    scene.render.fps = 30
    scene.frame_set(1)
    gun = bpy.data.objects["GunRoot"]
    objects = [gun] + list(gun.children_recursive)
    mechanism = [bpy.data.objects[part] for part in info["moving_parts"]]
    moving = [obj for obj in mechanism if obj.animation_data and obj.animation_data.action]
    assert moving, (name, "source has no mechanism animation")

    for obj in objects:
        original = obj.name
        # Keep mechanism actions, strip only the preview's visibility/property drivers.
        if obj.animation_data:
            for driver in list(obj.animation_data.drivers):
                obj.animation_data.drivers.remove(driver)
        obj.hide_render = False
        obj.hide_viewport = False
        obj.hide_set(False)
        slot = re.match(r"Modulator (\d+) \| (.*)", original)
        if slot:
            role = "Cartridge" if obj.get("preview_example") else slot[2]
            obj.name = "".join(word.capitalize() for word in re.findall(r"[a-zA-Z0-9]+", role))
        else:
            obj.name = re.sub(r"[^a-zA-Z0-9_]", "_", original)
        obj["source_name"] = original
        for prop in ("preview_example", "modulator_key", "rarity_tier", "rarity_legend",
                     "weapon_level", "equipped_modulators"):
            if prop in obj:
                del obj[prop]

    for material in bpy.data.materials:
        material.animation_data_clear()
        if material.node_tree:
            material.node_tree.animation_data_clear()
        if material.name.startswith("08 |"):
            material.name = "RarityAccent"
    gun["status"] = "Runtime geometry; EquipmentItem controls rarity and all ten socket states"
    gun["equipped_shape"] = key
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.update()
    path = OUT / (name + ".glb")
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, use_visible=False,
        export_apply=True, export_cameras=False, export_lights=False,
        export_yup=True, export_extras=True, export_animations=True,
        export_animation_mode="SCENE", export_anim_scene_split_object=False,
        export_nla_strips_merged_animation_name="Fire", export_frame_range=True,
        export_force_sampling=True, export_frame_step=1, export_current_frame=True)

    # Read back the actual GLB rather than trusting exporter success alone.
    data = path.read_bytes()
    magic, version, length, json_length, chunk_type = struct.unpack_from("<5I", data)
    assert (magic, version, length, chunk_type) == (0x46546C67, 2, len(data), 0x4E4F534A)
    document = json.loads(data[20:20 + json_length])
    nodes = document["nodes"]
    names = {node["name"] for node in nodes}
    for anchor in ["GunRoot", "ShapeSocket", "MuzzleAnchor"] + [f"ModulatorSocket{i:02}" for i in range(1, 11)]:
        assert anchor in names, (name, "missing anchor", anchor)
    assert "RarityAccent" in {material["name"] for material in document["materials"]}
    animations = document.get("animations", [])
    assert len(animations) == 1 and animations[0]["name"] == "Fire", (name, animations)
    targets = {nodes[channel["target"]["node"]]["name"] for channel in animations[0]["channels"]}
    assert all(obj.name in targets for obj in moving), (name, "missing moving part", targets)
    assert not any("Modulator" in target or "Cartridge" in target for target in targets), targets
    # Attachments can belong to animated fragments rather than GunRoot. Keep
    # their hierarchy intact so controls, inserts, and every bay variant move
    # with the fragment without receiving separate visibility animation.
    indices = {node['name']: i for i, node in enumerate(nodes)}
    parents = {child: i for i, node in enumerate(nodes) for child in node.get('children', [])}
    attachments = {}
    for anchor in ['ShapeSocket'] + [f'ModulatorSocket{i:02}' for i in range(1, 11)]:
        obj = bpy.data.objects[anchor]
        exported_parent = nodes[parents[indices[anchor]]]['name'] if indices[anchor] in parents else None
        assert exported_parent == (obj.parent.name if obj.parent else None), (name, anchor, 'attachment parent changed')
        animated_ancestors = []
        ancestor = obj.parent
        while ancestor:
            if ancestor.name in targets:
                animated_ancestors.append(ancestor.name)
            ancestor = ancestor.parent
        attachments[anchor] = {'parent': exported_parent, 'animated_ancestors': animated_ancestors}
    if key == 'impossiblecube':
        assert all(value['animated_ancestors'] for value in attachments.values()), (name, 'fragment attachments lost motion')
    for i in range(1, 11):
        bay = next(node for node in nodes if node["name"] == f"ModulatorSocket{i:02}")
        roles = [nodes[child]["name"] for child in bay.get("children", [])]
        assert all(any(role.startswith(prefix) for role in roles)
                   for prefix in ("Locked", "EmptyContact", "Cartridge")), (name, i, roles)
    animated_inputs = [document['accessors'][sampler['input']] for sampler in animations[0]['samplers']]
    clip_end = max(accessor['max'][0] for accessor in animated_inputs)
    clip_start = min(accessor['min'][0] for accessor in animated_inputs)
    assert abs((clip_end - clip_start) - 1.3) < .002, (name, 'unexpected Fire duration', clip_start, clip_end)
    assert max(accessor['count'] for accessor in animated_inputs) >= 40, (name, 'Fire was not sampled at 30fps')
    manifest = {
        "shape": key, "source": str(source.relative_to(ROOT)).replace("\\", "/"),
        "source_sha256": source_hash, "blender_version": bpy.app.version_string,
        "coordinates": "meters; Godot +X right, +Y up, -Z forward; Blender (x,y,z) -> (x,z,-y)",
        "animation": {"name": "Fire", "fps": 30, "frames": [1, 40], "moving_parts": [obj.name for obj in moving]},
        "mechanism_parts": [obj.name for obj in mechanism],
        "attachments": attachments,
        "nodes": len(nodes), "meshes": len(document["meshes"]), "bytes": len(data),
        "objects": [{"name": obj.name, "source": obj["source_name"],
                     "parent": obj.parent.name if obj.parent else None} for obj in objects],
    }
    path.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
    return {"shape": key, "file": path.name, "nodes": len(nodes), "bytes": len(data),
            "animated_parts": [obj.name for obj in moving]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weapons", nargs="+", choices=list(ASSETS), default=list(ASSETS))
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    OUT.mkdir(parents=True, exist_ok=True)
    reports = []
    for key in args.weapons:
        status("exporting", shape=key, completed=reports)
        reports.append(export(key))
    status("complete", assets=reports)


if __name__ == "__main__":
    STATUS.parent.mkdir(parents=True, exist_ok=True)
    sys.stdout = open(ROOT / ".tmp/multiweapon-runtime.log", "w", encoding="utf-8", buffering=1)
    sys.stderr = sys.stdout
    try:
        main()
    except Exception as error:
        traceback.print_exc()
        status("failed", error=str(error))
        raise
