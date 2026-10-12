"""Export approved editable fort architecture and authoritative CPU/GPU proxies.

Run blender-launcher.exe --background --factory-startup --python this_file.py.
The review source is read only. glTF converts Blender (X,Y,Z) to Godot (X,Z,-Y).
"""
import bpy
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Art/FortConcepts/SteppedStronghold/SteppedStronghold.blend'
OUT = ROOT / 'Resources/Forts/SteppedStronghold'
OUT.mkdir(parents=True, exist_ok=True)
source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
bpy.ops.object.select_all(action='DESELECT')
excluded = ('TurretPedestal', 'TurretHeadRig', 'ShapeSocketPlaceholder',
            'ModulatorSocket', 'RepairStationPlaceholder', 'CoreFoundation',
            'CorePillar', 'CoreSignal', 'CoreLight_', 'ShopCounter')
objects = []
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH':
        continue
    if not any(col.name.startswith(('01 |', '02 |', '03 |', '04 |', '05 |', '05A |'))
               for col in obj.users_collection):
        continue
    if any(term in obj.name for term in excluded):
        continue
    # Study ground ribbons assume a flat studio. Conform these and peripheral
    # landmarks to the live slope, retaining the actual architecture untouched.
    terrain_dressing = any(term in obj.name for term in
        ('Approach24m', 'RouteEdge_', 'EastOpenApron_', 'NorthCausewayPylon_',
         'SouthBrokenShoulder_', 'WestRetainingBand_'))
    if terrain_dressing:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        obj.select_set(False)
        for vert in obj.data.vertices:
            distance = max(abs(vert.co.x), abs(vert.co.y))
            ground = 8 * max(0, min(1, (220-distance)/118))
            vert.co.z += ground - 8
    assert all(math.isfinite(v) for vert in obj.data.vertices for v in vert.co)
    obj.hide_render = False
    obj.hide_viewport = False
    obj.hide_set(False)
    obj.select_set(True)
    objects.append(obj)
assert objects
bpy.ops.export_scene.gltf(filepath=str(OUT / 'SteppedStronghold.glb'),
    export_format='GLB', use_selection=True, export_apply=True,
    export_animations=False, export_cameras=False, export_lights=False,
    export_extras=False, export_yup=True)
proxies = json.loads(SOURCE.with_name('collision-proxies.json').read_text())
assert len(proxies['solids']) == 39 and len(proxies['ramps']) == 16
runtime = {'source': 'Art/FortConcepts/SteppedStronghold/SteppedStronghold.blend',
           'source_sha256': source_hash, 'solids': proxies['solids'], 'ramps': proxies['ramps']}
(OUT / 'collision-proxies.json').write_text(json.dumps(runtime, indent=2))
(OUT / 'CollisionProxies.tres').write_bytes((
    '[gd_resource type="Resource" format=3]\n\n[resource]\nmetadata/proxies = '
    + json.dumps(json.dumps(runtime, separators=(',', ':'))) + '\n').encode('utf-8'))
assert (OUT / 'CollisionProxies.tres').read_bytes().startswith(b'[gd_resource')
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == source_hash
(OUT / 'export-info.json').write_text(json.dumps({
    'source_sha256': source_hash, 'mesh_count': len(objects),
    'mesh_names': [o.name for o in objects], 'solid_count': 39, 'ramp_count': 16,
    'coordinate_conversion': 'Blender x,y,z => Godot x,z,-y',
    'excluded': 'studio, labels, scale figures, proxies, objective and equipment placeholders',
    'terrain_dressing': 'exterior landmarks/ribbons conformed to default live slope',
}, indent=2))
print('SteppedStronghold runtime export complete:', len(objects), 'meshes; 39 solids / 16 ramps')
