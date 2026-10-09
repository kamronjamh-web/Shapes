"""Isolate the approved Square cartridge for lightweight workbench thumbnails."""
import bpy, hashlib, json, struct
from pathlib import Path
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
source = ROOT / "Art/WeaponConcepts/SquareHandgun/SquareHandgun.blend"
before = hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
cartridge = next(obj for obj in bpy.data.objects["ModulatorSocket01"].children if obj.get("preview_example"))
objects = [cartridge] + list(cartridge.children_recursive)
cartridge.parent = None
cartridge.matrix_parent_inverse = Matrix.Identity(4)
cartridge.location = (0, 0, 0)
cartridge.rotation_euler = (0, 0, 0)
cartridge.scale = (1, 1, 1)
cartridge.name = "WorkbenchCartridge"
accent = None
for obj in objects:
    obj.animation_data_clear()
    obj.hide_render = obj.hide_viewport = False
    obj.hide_set(False)
    for surface in obj.material_slots:
        if "type color" in obj.name or "identity bar" in obj.name:
            if accent is None:
                accent = surface.material.copy(); accent.name = "ModulatorAccent"
                accent.animation_data_clear()
                if accent.node_tree: accent.node_tree.animation_data_clear()
            surface.material = accent
        elif surface.material:
            surface.material.animation_data_clear()
            if surface.material.node_tree: surface.material.node_tree.animation_data_clear()
    for prop in list(obj.keys()):
        if prop not in ("_RNA_UI",): del obj[prop]
bpy.ops.object.select_all(action="DESELECT")
for obj in objects: obj.select_set(True)
bpy.context.view_layer.objects.active = cartridge
for obj in list(bpy.data.objects):
    if obj not in objects: bpy.data.objects.remove(obj, do_unlink=True)
derived = ROOT / "Art/WeaponWorkbenchAnimations/WorkbenchCartridge.blend"
derived.parent.mkdir(parents=True, exist_ok=True)
(derived.parent / ".gdignore").touch(exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(derived))
out = ROOT / "Resources/Weapons/WorkbenchCartridge.glb"
bpy.ops.export_scene.gltf(filepath=str(out), export_format="GLB", use_selection=True,
    use_visible=False, export_apply=True, export_cameras=False, export_lights=False,
    export_yup=True, export_extras=True, export_animations=False)
bpy.context.view_layer.update()
points = [obj.matrix_world @ vertex.co for obj in objects if obj.type == "MESH" for vertex in obj.data.vertices]
lo = [min(point[i] for point in points) for i in range(3)]
hi = [max(point[i] for point in points) for i in range(3)]
data = out.read_bytes(); count = struct.unpack_from("<I", data, 12)[0]
doc = json.loads(data[20:20+count])
assert "ModulatorAccent" in [mat["name"] for mat in doc["materials"]]
report = {"source": str(source.relative_to(ROOT)), "source_sha256": before,
          "derived_source": str(derived.relative_to(ROOT)), "bytes": len(data),
          "bounds_blender": {"min": lo, "max": hi},
          "orientation": "meters; Godot +X right, +Y face outward, -Z cartridge top; face normal +Y",
          "identity_material": "ModulatorAccent", "objects": len(objects)}
out.with_suffix(".manifest.json").write_text(json.dumps(report, indent=2))
assert hashlib.sha256(source.read_bytes()).hexdigest() == before
print("WORKBENCH_CARTRIDGE_EXPORTED", json.dumps(report), flush=True)
