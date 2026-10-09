"""Author derived Blender workbench clips and append them to unchanged runtime GLB geometry/Fire.

blender --background --factory-startup --python Tools/Art/ExportWeaponWorkbenchAnimations.py -- --weapons square
Approved concept blends and existing import settings are never written.
"""
from __future__ import annotations
import argparse, copy, hashlib, json, math, struct, sys, traceback
from pathlib import Path
import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Resources/Weapons"
DERIVED = ROOT / "Art/WeaponWorkbenchAnimations"
TMP = ROOT / ".tmp/weapon-workbench-animation"
ASSETS = {"square": "SquareHandgun", "circle": "CircleSawblade", "triangle": "TriangleBeamCarbine",
          "hexagon": "HexagonMineLauncher", "sphere": "SpherePulseCannon", "cube": "CubeGuardProjector",
          "pyramid": "PyramidSeekerLauncher", "cone": "ConeFlameProjector", "tesseract": "TesseractRiftEngine",
          "hyperphere": "HyperphereMortar", "pentachoron": "PentachoronArcLattice", "hypercone": "HyperconeDrill",
          "impossiblecube": "ImpossibleCubeSingularity", "mobius": "MobiusEchoWeaver", "klein": "KleinAuraVessel",
          "boys": "BoysSurfaceClonePrism"}

def read_glb(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", data)
    assert magic == 0x46546c67 and version == 2 and length == len(data)
    n, kind = struct.unpack_from("<II", data, 12)
    assert kind == 0x4e4f534a
    doc = json.loads(data[20:20+n])
    offset = 20+n
    bn, bk = struct.unpack_from("<II", data, offset)
    assert bk == 0x004e4942
    return doc, data[offset+8:offset+8+bn]

def write_glb(path, doc, binary):
    doc["buffers"][0]["byteLength"] = len(binary)
    payload = json.dumps(doc, separators=(",", ":")).encode()
    payload += b" " * (-len(payload) % 4)
    binary += b"\0" * (-len(binary) % 4)
    path.write_bytes(struct.pack("<III", 0x46546c67, 2, 28+len(payload)+len(binary)) +
                    struct.pack("<II", len(payload), 0x4e4f534a) + payload +
                    struct.pack("<II", len(binary), 0x004e4942) + binary)

def append_clips(path, original, old_binary, generated, generated_binary):
    doc = copy.deepcopy(original)
    # A rerun replaces only these authored workbench tracks, never Fire.
    doc["animations"] = [a for a in doc.get("animations", []) if a["name"] == "Fire"]
    # Discard earlier workbench-only buffer tails on reruns, retaining every geometry/Fire reference.
    base_accessors = {primitive[field] for mesh in doc["meshes"] for primitive in mesh["primitives"]
                      for field in ("indices",) if field in primitive}
    base_accessors |= {index for mesh in doc["meshes"] for primitive in mesh["primitives"]
                       for index in primitive.get("attributes", {}).values()}
    base_accessors |= {index for clip in doc["animations"] for sampler in clip["samplers"]
                       for index in (sampler["input"], sampler["output"])}
    accessor_count = max(base_accessors) + 1
    base_views = {acc["bufferView"] for acc in doc["accessors"][:accessor_count] if "bufferView" in acc}
    base_views |= {image["bufferView"] for image in doc.get("images", []) if "bufferView" in image}
    view_count = max(base_views) + 1
    binary_length = max(view.get("byteOffset", 0)+view["byteLength"] for view in doc["bufferViews"][:view_count])
    binary_length += -binary_length % 4
    doc["accessors"] = doc["accessors"][:accessor_count]
    doc["bufferViews"] = doc["bufferViews"][:view_count]
    target_nodes = {node["name"]: i for i, node in enumerate(doc["nodes"])}
    data = bytearray(old_binary[:binary_length])
    accessor_map = {}
    def accessor(index, normalize_time=False):
        if index in accessor_map: return accessor_map[index]
        acc = copy.deepcopy(generated["accessors"][index])
        view = generated["bufferViews"][acc["bufferView"]]
        begin = view.get("byteOffset", 0)
        data.extend(b"\0" * (-len(data) % 4))
        new_view = dict(view, buffer=0, byteOffset=len(data))
        payload = bytearray(generated_binary[begin:begin+view["byteLength"]])
        if normalize_time:
            origin = acc["min"][0]
            stride = view.get("byteStride", 4)
            for i in range(acc["count"]):
                offset = acc.get("byteOffset", 0) + i * stride
                value = struct.unpack_from("<f", payload, offset)[0]
                struct.pack_into("<f", payload, offset, max(0, value-origin))
            acc["min"] = [0.0]; acc["max"] = [acc["max"][0]-origin]
        data.extend(payload)
        acc["bufferView"] = len(doc["bufferViews"])
        doc["bufferViews"].append(new_view)
        accessor_map[index] = len(doc["accessors"])
        doc["accessors"].append(acc)
        return accessor_map[index]
    report = []
    for clip in generated.get("animations", []):
        if clip["name"] == "Fire": continue
        assert clip["name"] in {"Assemble", "Disassemble"} | {f"Socket{verb}{i:02}" for verb in ("Seat", "Remove") for i in range(1, 11)}, clip["name"]
        new = copy.deepcopy(clip)
        targets = []
        for channel in new["channels"]:
            name = generated["nodes"][channel["target"]["node"]]["name"]
            assert name in target_nodes, (clip["name"], name)
            assert name != "GunRoot", "Root transforms must stay caller-owned"
            channel["target"]["node"] = target_nodes[name]
            targets.append(name)
        for sampler in new["samplers"]:
            sampler["input"] = accessor(sampler["input"], normalize_time=True)
            sampler["output"] = accessor(sampler["output"])
        duration = max(doc["accessors"][s["input"]]["max"][0] for s in new["samplers"])
        doc["animations"].append(new)
        report.append({"name": new["name"], "duration": duration, "targets": sorted(set(targets))})
    assert len(report) == 22, [r["name"] for r in report]
    assert all(len(r["targets"]) >= 3 for r in report if r["name"] in ("Assemble", "Disassemble"))
    assert doc["nodes"] == original["nodes"] and doc["meshes"] == original["meshes"] and doc["materials"] == original["materials"]
    assert [a for a in doc["animations"] if a["name"] == "Fire"] == [a for a in original.get("animations", []) if a["name"] == "Fire"]
    write_glb(path, doc, bytes(data))
    return report

def add_action(obj, name, matrices, frames):
    obj.animation_data_create()
    obj.animation_data.action = bpy.data.actions.new(name=f"{obj.name}_{name}")
    action = obj.animation_data.action
    for frame, matrix in zip(frames, matrices):
        obj.matrix_basis = matrix
        obj.keyframe_insert(data_path="location", frame=frame)
        obj.keyframe_insert(data_path="rotation_euler", frame=frame)
        obj.keyframe_insert(data_path="scale", frame=frame)
    for curve in action.fcurves:
        for key in curve.keyframe_points: key.interpolation = "BEZIER"
    obj.animation_data.action = None
    track = obj.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 1, action)
    strip.extrapolation = "NOTHING"
    strip.blend_type = "REPLACE"
    return track

def export(key):
    name = ASSETS[key]
    path = OUT / (name + ".glb")
    original, old_binary = read_glb(path)
    manifest_path = path.with_suffix(".manifest.json")
    manifest = json.loads(manifest_path.read_text())
    source = ROOT / "Art/WeaponConcepts" / name / (name + ".blend")
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(source))
    scene = bpy.context.scene
    scene.frame_set(1)
    gun = bpy.data.objects["GunRoot"]
    objects = [gun] + list(gun.children_recursive)
    by_source = {obj.name: obj for obj in objects}
    # Existing manifest is the authority for names, including duplicate bay role suffixes.
    mappings = [(by_source[row["source"]], row["name"]) for row in manifest["objects"]]
    for i, (obj, _) in enumerate(mappings): obj.name = f"__workbench_tmp_{i}"
    for obj, runtime_name in mappings:
        obj.name = runtime_name
        if obj.animation_data:
            for driver in list(obj.animation_data.drivers): obj.animation_data.drivers.remove(driver)
        obj.hide_render = obj.hide_viewport = False
        obj.hide_set(False)
    bpy.context.view_layer.update()
    rest = {obj: obj.matrix_basis.copy() for obj in objects}
    world = {obj: obj.matrix_world.copy() for obj in objects}
    core = bpy.data.objects["ShapeSocket"]
    center = core.matrix_world.translation.copy()
    for obj in objects:
        if obj.animation_data:
            action = obj.animation_data.action
            obj.animation_data_clear()
            if action:
                obj.animation_data_create()
                track = obj.animation_data.nla_tracks.new(); track.name = "Fire"
                strip = track.strips.new("Fire", 1, action); strip.extrapolation = "NOTHING"
                strip.action_frame_start = 1; strip.action_frame_end = 40
    # Fold authored top-level mechanical groups. Their attachments follow naturally.
    assembly = [obj for obj in gun.children if obj != core and not obj.name.startswith(("MuzzleAnchor", "ModulatorSocket"))]
    if len(assembly) < 3:
        # Sculptural vessels can have a single authored assembly root; articulate its sectors.
        assembly = [child for parent in assembly for child in parent.children
                    if child != core and not child.name.startswith(("MuzzleAnchor", "ModulatorSocket"))]
    assert len(assembly) >= 3, (key, "too few articulated assemblies")
    folded = {}
    for i, obj in enumerate(assembly):
        offset = world[obj].translation - center
        side = -1 if offset.x < 0 else 1
        angle = side * (.48 + (i % 3) * .10)
        rotation = Matrix.Rotation(angle, 4, "Y") @ Matrix.Rotation((i % 2 * 2 - 1) * .34, 4, "X")
        pose = world[obj].copy()
        pose.translation = center + offset * .22 + Vector((side * .025, 0, .008 * (i % 3)))
        pose = Matrix.Translation(pose.translation) @ rotation @ world[obj].to_3x3().to_4x4()
        local = obj.parent.matrix_world.inverted_safe() @ pose if obj.parent else pose
        folded[obj] = obj.matrix_parent_inverse.inverted_safe() @ local
        midpoint = rest[obj].lerp(folded[obj], .58)
        add_action(obj, "Disassemble", [rest[obj], midpoint, folded[obj]], [1, 11, 22])
        add_action(obj, "Assemble", [folded[obj], midpoint, rest[obj]], [1, 11, 22])
        obj.matrix_basis = rest[obj]
    # If the shape dock belongs to a moving fragment, compensate its parent to keep the core stationary.
    if core.parent != gun:
        ancestor = core.parent
        while ancestor and ancestor not in folded: ancestor = ancestor.parent
        if ancestor:
            transforms = []
            for fraction in (0, .58, 1):
                basis = rest[ancestor].lerp(folded[ancestor], fraction)
                changed = world[ancestor] @ rest[ancestor].inverted_safe() @ basis
                parent_world = changed @ world[ancestor].inverted_safe() @ world[core.parent]
                transforms.append(core.matrix_parent_inverse.inverted_safe() @ parent_world.inverted_safe() @ world[core])
            add_action(core, "Disassemble", transforms, [1, 11, 22])
            add_action(core, "Assemble", list(reversed(transforms)), [1, 11, 22])
            core.matrix_basis = rest[core]
    for slot in range(1, 11):
        bay = bpy.data.objects[f"ModulatorSocket{slot:02}"]
        cartridges = [obj for obj in bay.children if obj.name.startswith("Cartridge")]
        assert cartridges, (name, slot)
        for obj in cartridges:
            seated = rest[obj]
            lifted = seated.copy(); lifted.translation += Vector((0, 0, .055))
            latch = seated.copy(); latch.translation += Vector((0, 0, .004))
            add_action(obj, f"SocketSeat{slot:02}", [lifted, latch, seated], [1, 9, 13])
            add_action(obj, f"SocketRemove{slot:02}", [seated, latch, lifted], [1, 5, 13])
            obj.matrix_basis = seated
    for material in bpy.data.materials:
        material.animation_data_clear()
        if material.node_tree: material.node_tree.animation_data_clear()
    for obj in objects:
        if obj.animation_data:
            for track in obj.animation_data.nla_tracks: track.mute = track.name != "Fire"
        obj.matrix_basis = rest[obj]
    scene.frame_start, scene.frame_end = 1, 40
    scene.render.fps = 30
    derived = DERIVED / (name + "Workbench.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(derived))
    for obj in objects:
        if obj.animation_data:
            for track in obj.animation_data.nla_tracks: track.mute = False
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects: obj.select_set(True)
    temp = TMP / (name + ".glb")
    bpy.ops.export_scene.gltf(filepath=str(temp), export_format="GLB", use_selection=True,
        use_visible=False, export_apply=True, export_cameras=False, export_lights=False,
        export_yup=True, export_extras=True, export_animations=True,
        export_animation_mode="NLA_TRACKS", export_nla_strips=True,
        export_frame_range=False, export_force_sampling=True, export_frame_step=1)
    generated, binary = read_glb(temp)
    clips = append_clips(path, original, old_binary, generated, binary)
    manifest["workbench"] = {"source": str(derived.relative_to(ROOT)).replace("\\", "/"),
        "approved_source_sha256": source_hash, "clips": clips, "core": "ShapeSocket",
        "authoring": "component-local fold around the core; no whole-gun scale; local-axis cartridge seating"}
    manifest["bytes"] = path.stat().st_size
    manifest_path.write_text(json.dumps(manifest, indent=2))
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
    return {"shape": key, "bytes": path.stat().st_size, "added_bytes": path.stat().st_size - len(old_binary), "assemblies": len(assembly), "clips": len(clips)}

def main():
    parser = argparse.ArgumentParser(); parser.add_argument("--weapons", nargs="+", choices=list(ASSETS), default=list(ASSETS))
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    DERIVED.mkdir(parents=True, exist_ok=True); TMP.mkdir(parents=True, exist_ok=True)
    (DERIVED / ".gdignore").touch(exist_ok=True)
    reports = []
    for key in args.weapons:
        print("AUTHORING", key, flush=True)
        reports.append(export(key))
        (TMP / "status.json").write_text(json.dumps({"completed": reports}, indent=2))
    print("WORKBENCH_EXPORT_COMPLETE", json.dumps(reports), flush=True)

if __name__ == "__main__":
    try: main()
    except Exception: traceback.print_exc(); raise
