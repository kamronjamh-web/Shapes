"""Editable concept study only; does not replace runtime fort geometry."""
import bpy
import json
import math
import sys
import traceback
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art' / 'FortConcepts' / 'SteppedStronghold'
OUT.mkdir(parents=True, exist_ok=True)
(OUT / '.gdignore').write_text('', encoding='utf-8')
(OUT / 'Renders').mkdir(exist_ok=True)
STATUS = OUT / 'build-status.json'
GROUND = 8.0
PROXIES = []
RAMPS = []


def status(stage, **extra):
    STATUS.write_text(json.dumps({'stage': stage, **extra}, indent=2), encoding='utf-8')


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def mat(name, color, metallic=0, roughness=.7, emission=0):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*color, 1)
    result.use_nodes = True
    shader = result.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    if emission:
        shader.inputs['Emission Color'].default_value = (*color, 1)
        shader.inputs['Emission Strength'].default_value = emission
    return result


def relocate(obj, col):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    col.objects.link(obj)


def finish(obj, name, col, material, bevel=0):
    obj.name = name
    relocate(obj, col)
    if material:
        obj.data.materials.append(material)
    if bevel:
        modifier = obj.modifiers.new('Editable chamfer', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 1
    return obj


def box(name, center, size, col, material, bevel=.12, proxy=False):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    finish(obj, name, col, material, bevel)
    if proxy:
        record = {'name': name, 'type': 'box', 'center_blender': list(center), 'size_blender': list(size)}
        PROXIES.append(record)
        guide = bpy.data.objects.new('COL_' + name, obj.data.copy())
        collision.objects.link(guide)
        guide.location = center
        guide.display_type = 'WIRE'
        guide.hide_render = True
        guide['collision_type'] = 'axis_aligned_box'
        guide['source_visual'] = name
        obj['collision_proxy'] = guide.name
    return obj


def cardinal(side, along, across=0, z=GROUND):
    ox, oy = [(0, 1), (1, 0), (0, -1), (-1, 0)][side]
    ax, ay = -oy, ox
    return (ox * along + ax * across, oy * along + ay * across, z)


def cardinal_size(side, across, along, height):
    return (across, along, height) if side % 2 == 0 else (along, across, height)


def wedge(name, side, start, end, width, base, top, col, material, across=0):
    # Outward coordinate decreases on the main ascent ramps.
    p = lambda along, cross, z: cardinal(side, along, across + cross, z)
    verts = [p(start, -width/2, base), p(start, width/2, base),
             p(end, -width/2, base), p(end, width/2, base),
             p(end, -width/2, top), p(end, width/2, top)]
    faces = [(0, 1, 5, 4), (0, 4, 2), (1, 3, 5), (2, 4, 5, 3), (0, 2, 3, 1)]
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    col.objects.link(obj)
    finish(obj, name, col, material)
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj.select_set(False)
    guide = bpy.data.objects.new('COL_' + name, data.copy())
    collision.objects.link(guide)
    guide.display_type = 'WIRE'
    guide.hide_render = True
    guide['collision_type'] = 'cardinal_ramp'
    obj['collision_proxy'] = guide.name
    RAMPS.append({'name': name, 'type': 'cardinal_ramp', 'side': side,
                  'start': start, 'end': end, 'across_center': across,
                  'width': width, 'base': base, 'top': top})
    return obj


def text(name, body, location, size=2.5, rotation=(0, 0, 0), material=None):
    data = bpy.data.curves.new(name, 'FONT')
    data.body = body
    data.align_x = 'CENTER'
    data.align_y = 'CENTER'
    data.size = size
    data.extrude = .012
    obj = bpy.data.objects.new(name, data)
    annotations.objects.link(obj)
    obj.location = location
    obj.rotation_euler = rotation
    if material:
        data.materials.append(material)
    return obj


def camera(name, location, target, ortho=None, lens=28):
    data = bpy.data.cameras.new(name)
    obj = bpy.data.objects.new(name, data)
    studio.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data.clip_end = 1500
    data.lens = lens
    if ortho:
        data.type = 'ORTHO'
        data.ortho_scale = ortho
    return obj


def light(name, location, energy, size):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    studio.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector((0, 0, 8)) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def person(name, location, color):
    x, y, z = location
    box(name + '_body', (x, y, z + .92), (.45, .35, 1.25), guides, color, .03)
    box(name + '_head', (x, y, z + 1.68), (.3, .3, .3), guides, color, .04)
    box(name + '_feet', (x, y, z + .13), (.48, .44, .26), guides, color, .02)


def main():
    global collision, annotations, studio, guides
    status('building', blender=bpy.app.version_string)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for old in list(bpy.data.collections):
        bpy.data.collections.remove(old)
    outer = collection('01 | Outer walls and firing balconies')
    terrace = collection('02 | Connected middle terrace')
    core = collection('03 | Raised fallback and core')
    approaches = collection('04 | Broad ramps and clear approaches')
    details = collection('05 | Editable modular visual dressing')
    upgrades = collection('05A | Shape turret and defense upgrade placeholders')
    collision = collection('06 | CPU GPU collision proxies - wire only')
    guides = collection('07 | Player and horde scale references')
    annotations = collection('08 | Concept annotations - review only')
    studio = collection('09 | Ground light and review cameras')
    collision.hide_render = True
    collision.hide_viewport = True
    concrete = mat('Concrete | muted warm grey', (.32, .35, .34))
    deck = mat('Deck | lighter concrete', (.44, .48, .47))
    dark = mat('Metal | graphite', (.065, .095, .11), .45, .5)
    trim = mat('Metal | quiet grey blue', (.16, .23, .26), .5, .5)
    earth = mat('Ground | muted slate earth', (.11, .15, .135), roughness=.92)
    pathmat = mat('Approach | worn mineral', (.23, .27, .24))
    white = mat('Graphics | warm ivory', (.74, .79, .74))
    ink = mat('Graphics | dark floor annotations', (.06, .1, .11))
    cyan = mat('Core | pale cyan signal', (.26, .67, .72), .1, .45, .5)
    player = mat('Scale | cyan player', (.2, .7, .75), .2, .6)
    enemy = mat('Scale | subdued clay horde', (.57, .3, .2), .05, .75)
    accents = [mat('Sector | ' + n, c) for n, c in zip(['North', 'East', 'South', 'West'],
        [(.37, .48, .53), (.48, .46, .34), (.5, .37, .31), (.35, .48, .4)])]
    box('ReviewTerrain', (0, 0, GROUND - .45), (600, 600, .9), studio, earth, 0)
    box('CourtyardFoundation', (0, 0, GROUND - .12), (202, 202, .24), details, pathmat, .08)

    # 200m courtyard, four open-to-sky 24m entrances; no narrow gate tunnels.
    for side, sector in enumerate(['NORTH', 'EAST', 'SOUTH', 'WEST']):
        for sign in [-1, 1]:
            box(f'{sector}_WallPanel_{sign}', cardinal(side, 100, sign*56, GROUND+3.048),
                cardinal_size(side, 88, 2, 6.096), outer, concrete, .18, True)
            box(f'{sector}_WallWalk_{sign}', cardinal(side, 98, sign*56, GROUND+5.7),
                cardinal_size(side, 88, 6, .792), outer, deck, .08, True)
            box(f'{sector}_GatePylon_{sign}', cardinal(side, 101, sign*14, GROUND+4),
                cardinal_size(side, 3.6, 5, 8), details, trim, .15)
            box(f'{sector}_GateSignal_{sign}', cardinal(side, 103.55, sign*14, GROUND+4.1),
                cardinal_size(side, 1.1, .1, 4), details, accents[side], .015)
            box(f'{sector}_FiringBalcony_{sign}', cardinal(side, 107, sign*28, GROUND+5.7),
                cardinal_size(side, 16, 10, .792), outer, deck, .1, True)
            box(f'{sector}_BalconySupport_{sign}', cardinal(side, 104, sign*28, GROUND+2.55),
                cardinal_size(side, 3, 3, 5.1), details, concrete, .14)
            wedge(f'{sector}_WallWalkAccess_{sign}', side, 70, 95, 7,
                  GROUND, GROUND+6.096, approaches, deck, across=sign*28)
            for index in range(7):
                box(f'{sector}_Parapet_{sign}_{index}', cardinal(side, 100.8, sign*(21+index*11), GROUND+6.65),
                    cardinal_size(side, 6, .7, 1.1), details, concrete, .1)
        # Four broad route ribbons make the intended clearance legible.
        box(f'{sector}_Approach24m', cardinal(side, 121, 0, GROUND+.025),
            cardinal_size(side, 24, 82, .05), details, pathmat, 0)
        for sign in [-1, 1]:
            box(f'{sector}_RouteEdge_{sign}', cardinal(side, 121, sign*11.2, GROUND+.065),
                cardinal_size(side, .22, 78, .03), details, accents[side], 0)
        text(f'{sector}_GroundTitle', f'{sector} / 24 m CLEAR', cardinal(side, 131, 0, GROUND+.11),
             2.9, (0, 0, -side*math.pi/2), ink)
        wedge(f'{sector}_OuterRamp24m', side, 79, 61, 24, 8, 11, approaches, deck)
        box(f'{sector}_MiddleLink24m', cardinal(side, 41, 0, 9.5),
            cardinal_size(side, 24, 16, 3), terrace, deck, .13, True)
        wedge(f'{sector}_InnerRamp24m', side, 33, 15, 24, 11, 14, approaches, deck)
        # One upgrade socket on an existing firing balcony per front. The deck
        # remains the collision source; equipment and behavior are not authored.
        pad = cardinal(side, 107, -28, 14.096)
        box(f'{sector}_TurretUpgradePad5m', (pad[0],pad[1],pad[2]+.055),
            (5,5,.11), upgrades, dark, .12)
        box(f'{sector}_TurretPedestal', (pad[0],pad[1],pad[2]+.55),
            (1.5,1.5,1), upgrades, trim, .16)
        box(f'{sector}_TurretHeadRig', (pad[0],pad[1],pad[2]+1.45),
            cardinal_size(side,1.5,2.2,.75), upgrades, concrete, .12)
        socket = cardinal(side, 107, -28, 16.15)
        box(f'{sector}_ShapeSocketPlaceholder', socket, (1.05,1.05,.12), upgrades, accents[side], .03)
        box(f'{sector}_ModulatorSocketFace', cardinal(side,108.14,-28,15.48),
            cardinal_size(side,1.3,.11,.5), upgrades, dark, .025)
        for index in range(3):
            location = cardinal(side, 108.23, -28+(index-1)*.38, 15.48)
            box(f'{sector}_ModulatorSocket_{index+1}', location,
                cardinal_size(side,.2,.12,.28), upgrades, cyan, .025)
        text(f'{sector}_TurretLabel', 'SHAPE + MODS', cardinal(side,107,-28,14.23), .48,
             (0,0,-side*math.pi/2), white)
        # Painted placement reserves remain flush and keep the center clear.
        box(f'{sector}_SlowStripPlacementReserve', cardinal(side,87,0,8.075),
            cardinal_size(side,24,3,.025), upgrades, accents[side], 0)
        for sign in [-1,1]:
            box(f'{sector}_BarricadePlacementReserve_{sign}', cardinal(side,87,sign*17,8.08),
                cardinal_size(side,4,2,.035), upgrades, trim, 0)
    # The continuous ring is intentionally broad enough to rotate between fights.
    for label, center, size in [
        ('North', (0,55,9.5), (122,12,3)), ('South', (0,-55,9.5), (122,12,3)),
        ('East', (55,0,9.5), (12,98,3)), ('West', (-55,0,9.5), (12,98,3))]:
        box('MiddleTerrace_' + label, center, size, terrace, deck, .13, True)
    box('FallbackPlatform30m', (0,0,11), (30,30,6), core, concrete, .25, True)
    box('FallbackSurface', (0,0,14.045), (29.5,29.5,.09), details, deck, .08)
    # Exposed objective, with free movement on every side and no explosives.
    box('CoreFoundation', (0,0,14.4), (7,7,.8), details, trim, .22)
    box('CorePillar', (0,0,16), (3.4,3.4,2.5), details, dark, .22)
    box('CoreSignal', (0,0,17.5), (2.2,2.2,.6), details, cyan, .14)
    for side in range(4):
        box(f'CoreLight_{side}', cardinal(side, 1.74, 0, 16), cardinal_size(side,1.3,.06,1.7), details, cyan, .02)
    text('CoreSurfaceTitle', 'FALLBACK / CORE', (0,-8,14.12), 1.75, material=ink)
    box('RepairUpgradePocket', (10,10,14.18), (4,4,.15), upgrades, trim, .08)
    box('RepairStationPlaceholder', (10,10,14.85), (1.1,1.1,1.2), upgrades, concrete, .1)
    text('RepairPocketTitle', 'REPAIR', (10,8.7,14.3), .55, material=cyan)

    # Restrained defensive furniture, all individually editable and off the route.
    for x, y in [(-55,-35),(55,-35),(-35,55),(35,55),(-35,-55),(35,-55)]:
        box(f'Cover_{x}_{y}', (x,y,11.6), (4,1.3,1.2), terrace, concrete, .16, True)
    for x, y in [(-55,55), (55,-55), (-55,-55)]:
        box(f'CornerDeck_{x}_{y}', (x,y,11.1), (11.5,11.5,.2), details, trim, .14)
    box('ShopShelterRoof', (55,55,15), (9,8,.35), details, dark, .1)
    for x in [51.2,58.8]:
        for y in [51.8,58.2]:
            box(f'ShopPost_{x}_{y}', (x,y,12.9), (.3,.3,3.8), details, trim, .04)
    box('ShopCounter', (55,57,11.6), (5,1.4,1.2), details, trim, .1)
    text('ShopGroundTitle', 'SHOP', (55,53,11.2), 1.15, material=cyan)

    # Sector identities are dressing around the same wide geometric route.
    for sign in [-1,1]:
        box(f'NorthCausewayPylon_{sign}', (sign*18,144,GROUND+5), (3.4,3.4,10), details, concrete, .25)
        box(f'EastOpenApron_{sign}', (126,sign*18,GROUND+.055), (50,14,.08), details, trim, .02)
        for idx in range(5):
            rubble = box(f'SouthBrokenShoulder_{sign}_{idx}', (sign*(17+idx*4),-118-idx*2,GROUND+.6),
                        (3.4,2.5,1.2+idx*.12), details, concrete, .25)
            rubble.rotation_euler[2] = .15 * sign * idx
        for idx in range(3):
            box(f'WestRetainingBand_{sign}_{idx}', (-117-idx*10,sign*(20+idx*5),GROUND+.55),
                (22,1.5,1.1), details, concrete, .12)
    for side in range(4):
        for row in range(4):
            for lane in range(3):
                person(f'HordeScale_{side}_{row}_{lane}', cardinal(side, 140+row*6, (lane-1)*6), enemy)
    person('PlayerAtMiddleTerrace', (0,-54,11), player)
    person('PlayerAtCore', (8,-9,14.1), player)
    person('PlayerAtEntrance', (8,-96,8), player)

    overview = camera('Review | 01 overview', (205,-238,205), (0,0,8), 310)
    plan = camera('Review | 02 overhead plan', (0,0,320), (0,0,8), 520)
    playercam = camera('Review | 03 player height from south breach', (7,-97,9.7), (1,-36,12.2), lens=25)
    middlecam = camera('Review | 04 player height rotating middle terrace', (35,-55,12.7), (58,-40,11.7), lens=24)
    corecam = camera('Review | 05 player height from fallback', (10,-10,15.7), (24,-62,10), lens=27)
    turretcam = camera('Review | 06 shape turret upgrade socket', (35,118,18), (28,107,15.5), lens=42)
    light('SkyKey', (-100,-120,230), 1500000, 180)
    light('SkyFill', (120,50,180), 800000, 140)
    sun = bpy.data.lights.new('Sun', 'SUN')
    sun.energy = 2
    sun.angle = math.radians(18)
    sunobj = bpy.data.objects.new('Sun', sun)
    studio.objects.link(sunobj)
    sunobj.rotation_euler = (math.radians(24),math.radians(-18),math.radians(-30))
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    try:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1100
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world.color = (.18,.2,.23)
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get('Background')
    background.inputs['Color'].default_value = (.32,.39,.45,1)
    background.inputs['Strength'].default_value = .4
    scene.view_settings.view_transform = 'AgX'
    scene.camera = overview
    scene['concept_status'] = 'Review blockout only; runtime game is unchanged'
    scene['design'] = 'Four 24m clear approaches, connected middle ring, exposed raised fallback core'
    scene['collision_proxy_note'] = 'Wire collection contains box and cardinal-ramp design proxies; not imported into runtime'
    bpy.context.preferences.filepaths.save_version = 0
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                space = area.spaces.active
                space.clip_end = 1500
                space.shading.type = 'MATERIAL'
                space.region_3d.view_rotation = overview.rotation_euler.to_quaternion()
                space.region_3d.view_distance = 320
                space.region_3d.view_location = Vector((0,0,8))
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SteppedStronghold.blend'))
    # Reopen the authored source before rendering and validate retained editability.
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'SteppedStronghold.blend'), load_ui=False)
    assert bpy.data.objects.get('FallbackPlatform30m')
    assert len(PROXIES) <= 64 and len(RAMPS) <= 64
    for obj in bpy.data.objects:
        assert all(math.isfinite(v) for v in obj.matrix_world.translation), obj.name
        if obj.type == 'MESH':
            assert all(math.isfinite(v) for vert in obj.data.vertices for v in vert.co), obj.name
    manifest = {'concept': 'SteppedStronghold', 'blender_version': bpy.app.version_string,
        'units': 'metres; Blender X/Y horizontal, Z up; equivalent Godot X/Z horizontal, Y up',
        'world_ground_elevation': 8, 'middle_terrace_top': 11, 'fallback_top': 14,
        'courtyard_span': 200, 'middle_terrace_outer_span': 122, 'middle_terrace_width':12,
        'fallback_span':30, 'main_route_clear_width':24, 'main_ramp_run':18,
        'upgrade_placeholders': {'turret_pads':4,'turret_pad_span':5,
            'shape_modulator_socket_placeholders':True,'slow_strip_reserves':4,
            'barricade_reserves':8,'repair_pockets':1,'functional':False},
        'solid_proxy_count':len(PROXIES), 'ramp_proxy_count':len(RAMPS),
        'proxies_are_design_only':True, 'source_reopened':True, 'finite_geometry_checked':True,
        'integration':'No scene, gameplay script or runtime resource replaced.',
        'solids':PROXIES, 'ramps':RAMPS}
    (OUT/'collision-proxies.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (OUT/'README.txt').write_text(
        'STEPPED STRONGHOLD / editable Blender concept\n\n'
        'Open SteppedStronghold.blend directly. Every wall, slab, ramp, cover and visual detail remains a named individual object.\n'
        'Collections separate architecture, approach ramps, dressing, wire collision proxies, scale guides, annotations and studio.\n'
        'Toggle collection 06 to inspect simplified box/wedge proxies; these are hidden from review renders.\n\n'
        'DESIGN\n200m courtyard. Four open 24m entrance lanes. Middle 122m square terrace with 12m connected circulation.\n'
        'Ground elevation 8m, middle 11m, exposed 30m central fallback/core 14m.\n'
        'Each primary ascent is 24m wide, rising 3m over an 18m run. Players can rotate around the middle ring.\n'
        'The central platform remains reachable by the horde. The corner shop shelter sits off the main routes.\n'
        'North pylons, east open apron, broken south shoulders and west low bands identify sectors without reducing route clearance.\n'
        'Four 5m turret upgrade pads sit on existing outer firing balconies, one per front, with schematic shape/modulator sockets.\n'
        'Flush paint reserves suggest barricade/slow-strip placement. A central repair pocket sits off the core ramp lanes.\n'
        'Upgrade pieces are nonfunctional design placeholders; no specific owned item, attack behavior or economic rules are implemented.\n'
        'Cyan/clay 1.8m figures are scale references, not final characters or a 50k-horde performance simulation.\n\n'
        f'PROXIES\n{len(PROXIES)} axis-aligned boxes and {len(RAMPS)} cardinal ramps, below current 64/64 GPU caps.\n'
        'This study is conceptual: routing, rendering, visibility, budgets and exact collisions still need runtime integration validation.\n'
        'Visual parapets, decorative pylons, rubble, the shop shelter and the objective do not claim finalized collision.\n\n'
        'Renders: overview, overhead plan, fallback and middle terrace at player eye height, south breach, turret socket detail.\n'
        'Generator: Tools/Art/CreateSteppedStronghold.py. Blender metres, +Z up; game uses +Y up.\n'
        'No live fort or gameplay assets were changed.\n', encoding='utf-8')
    scene = bpy.context.scene
    for camname, filename in [
        ('Review | 01 overview','01_overview.png'),('Review | 02 overhead plan','02_overhead_plan.png'),
        ('Review | 05 player height from fallback','03_player_height.png'),
        ('Review | 04 player height rotating middle terrace','04_middle_terrace.png'),
        ('Review | 03 player height from south breach','05_south_breach.png'),
        ('Review | 06 shape turret upgrade socket','06_turret_socket.png')]:
        status('rendering',view=filename,blender=bpy.app.version_string)
        scene.camera = bpy.data.objects[camname]
        scene.render.filepath = str(OUT/'Renders'/filename)
        bpy.ops.render.render(write_still=True)
    scene.camera = bpy.data.objects['Review | 01 overview']
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SteppedStronghold.blend'))
    status('complete',blender=bpy.app.version_string,solid_proxies=len(PROXIES),ramp_proxies=len(RAMPS),source_reopened=True)


if __name__ == '__main__':
    sys.stdout = open(OUT/'build.log','w',encoding='utf-8',buffering=1)
    sys.stderr = sys.stdout
    try:
        main()
    except Exception as exc:
        traceback.print_exc()
        status('failed',error=str(exc))
        raise
