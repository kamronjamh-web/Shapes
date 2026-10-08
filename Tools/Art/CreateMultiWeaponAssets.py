"""Build MultiWeapon Blender assets with the approved Square material language.

Run inside Blender with --background --python this_file -- --source SquareHandgun.blend.
The source is read only. Each output is self-contained, with editable bevels,
the original rarity/socket drivers, a short mechanism animation and review cameras.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib
import json
import math
from pathlib import Path
import re
import struct
import subprocess
import sys
import traceback

import bpy
import bmesh
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).parent))
sys.dont_write_bytecode = True
OUTPUT = ROOT / 'Art' / 'WeaponConcepts'
STATUS = ROOT / '.tmp' / 'multiweapon-build-status.json'
CATALOG = (ROOT / 'Scripts/Equipment/Data/EquipmentCatalogDefaults.cs').read_text(encoding='utf-8')
RARITIES = [(key, name, tuple(float(x.strip().rstrip('f')) for x in rgb.split(',')))
            for key, name, rgb in re.findall(
                r'CreateRarity\("([^"]+)", "([^"]+)"[^\n]+new Color\(([^)]+)\)', CATALOG)]
PROGRESSION = {key: (int(start), int(end)) for key, start, end in re.findall(
    r'\["([^"]+)"\] = new WeaponCustomizationProgression\((\d+), (\d+), \d+\)', CATALOG)}
SPECS = {
    'circle': ('CircleSawblade', 'CI / 02', 'Circle - Disc Launcher', 48),
    'triangle': ('TriangleBeamCarbine', 'TR / 03', 'Triangle - Beam Carbine', 3),
    'hexagon': ('HexagonMineLauncher', 'HX / 04', 'Hexagon - Mine Launcher', 6),
    'sphere': ('SpherePulseCannon', 'SP / 05', 'Sphere - Pulse Cannon', 32),
    'cube': ('CubeGuardProjector', 'CU / 06', 'Cube - Guard Projector', 4),
    'pyramid': ('PyramidSeekerLauncher', 'PY / 07', 'Pyramid - Seeker Launcher', 3),
    'cone': ('ConeFlameProjector', 'CO / 08', 'Cone - Flame Projector', 32),
    'tesseract': ('TesseractRiftEngine', 'TE / 09', 'Tesseract - Rift Engine', 4),
    'hyperphere': ('HyperphereMortar', 'HS / 10', 'Hyperphere - Dimensional Mortar', 32),
    'pentachoron': ('PentachoronArcLattice', 'PE / 11', 'Pentachoron - Arc Lattice', 5),
    'hypercone': ('HyperconeDrill', 'HC / 12', 'Hypercone - Drill', 12),
    'impossiblecube': ('ImpossibleCubeSingularity', 'IC / 13', 'Impossible Cube - Singularity', 4),
    'mobius': ('MobiusEchoWeaver', 'MO / 14', 'Mobius - Echo Weaver', 32),
    'klein': ('KleinAuraVessel', 'KL / 15', 'Klein - Aura Vessel', 32),
    'boys': ('BoysSurfaceClonePrism', 'BS / 16', "Boy's Surface - Clone Prism", 3),
}
SOLID_SHAPES = {'sphere', 'cube', 'pyramid'}
CUSTOM_SHAPES = {'cone', 'tesseract', 'hyperphere', 'pentachoron', 'hypercone',
                 'impossiblecube', 'mobius', 'klein', 'boys'}
SHAPE_STARS = {key: int(stars) for key, stars in re.findall(
    r'CreateShape\("([^"]+)", "[^"]+", ShapeGroup\.\w+, [\d.]+f, (\d),', CATALOG)}
STAR_DESIGN = {
    1: 'Direct mechanical assembly, clean plates and a single dominant mechanism',
    2: 'Articulated dimensional housing, visible emitter and supporting hardware',
    3: 'Layered containment structures, nested cores and interacting precision mechanisms',
    4: 'Sculptural topology, interlocking conduits, multiple containment layers and fine precision details',
}


def status(stage, **kwargs):
    STATUS.parent.mkdir(parents=True, exist_ok=True)
    STATUS.write_text(json.dumps({'stage': stage, **kwargs}, indent=2), encoding='utf-8')
    print(stage, json.dumps(kwargs), flush=True)


def linear_color(rgb):
    return tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb)


class Context:
    """Geometry and reusable cartridge interfaces for each authored form."""
    def __init__(self):
        self.root = bpy.data.objects['GunRoot']
        self.collection = bpy.data.collections.new('06 | MultiWeapon authored assembly')
        bpy.context.scene.collection.children.link(self.collection)
        self.full_body = False
        self.placed_sockets = set()
        self.dock_placed = False
        self.m = {}
        for key, prefix in [('steel', '01 |'), ('frame', '02 |'), ('grip', '03 |'),
                            ('recess', '04 |'), ('edge', '05 |'), ('ivory', '06 |'),
                            ('ink', '07 |'), ('accent', '08 |')]:
            self.m[key] = next(m for m in bpy.data.materials if m.name.startswith(prefix))

    def finish(self, obj, mat, bevel, parent):
        obj.parent = parent if parent is not None else self.root
        if mat is not None:
            obj.data.materials.append(mat)
        if obj.type == 'MESH':
            for poly in obj.data.polygons:
                poly.use_smooth = False
            if bevel > 0:
                mod = obj.modifiers.new('Editable fitted chamfer', 'BEVEL')
                mod.width = bevel
                mod.segments = 2
                mod.limit_method = 'ANGLE'
                mod.harden_normals = True
                normals = obj.modifiers.new('Weighted planar normals', 'WEIGHTED_NORMAL')
                normals.keep_sharp = True
        return obj

    def mesh(self, name, verts, faces, mat, bevel=.0005, parent=None):
        data = bpy.data.meshes.new(name)
        data.from_pydata(verts, [], faces)
        data.update()
        bm = bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(data)
        bm.free()
        obj = bpy.data.objects.new(name, data)
        self.collection.objects.link(obj)
        return self.finish(obj, mat, bevel, parent)

    def box(self, name, loc, size, mat, bevel=.0006, parent=None):
        x, y, z = (v / 2 for v in size)
        verts = [(-x,-y,-z), (x,-y,-z), (x,y,-z), (-x,y,-z),
                 (-x,-y,z), (x,-y,z), (x,y,z), (-x,y,z)]
        faces = [(0,3,2,1), (4,5,6,7), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7)]
        obj = self.mesh(name, verts, faces, mat, bevel, parent)
        obj.location = loc
        return obj

    @staticmethod
    def axial_rotation(obj, axis):
        obj.rotation_euler = (0, math.pi / 2, 0) if axis == 'X' else (
            (math.pi / 2, 0, 0) if axis == 'Y' else (0, 0, 0))

    def cylinder(self, name, loc, radius, depth, mat, axis='Z', vertices=24,
                 bevel=.0004, parent=None):
        n = vertices
        verts = [(radius * math.cos(math.tau*i/n), radius * math.sin(math.tau*i/n), z)
                 for z in (-depth/2, depth/2) for i in range(n)]
        faces = [tuple(reversed(range(n))), tuple(range(n,2*n))]
        faces += [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)]
        obj = self.mesh(name, verts, faces, mat, bevel, parent)
        obj.location = loc
        self.axial_rotation(obj, axis)
        return obj

    def ring(self, name, loc, outer, inner, depth, mat, axis='Z', vertices=32,
             bevel=.0004, parent=None):
        n = vertices
        verts = [(r*math.cos(math.tau*i/n), r*math.sin(math.tau*i/n), z)
                 for z in (-depth/2, depth/2) for r in (outer, inner) for i in range(n)]
        faces = []
        for i in range(n):
            j = (i+1)%n
            faces += [(i,j,n+j,n+i), (2*n+i,3*n+i,3*n+j,2*n+j),
                      (i,2*n+i,2*n+j,j), (n+i,n+j,3*n+j,3*n+i)]
        obj = self.mesh(name, verts, faces, mat, bevel, parent)
        obj.location = loc
        self.axial_rotation(obj, axis)
        return obj

    def sphere(self, name, loc, radius, mat, segments=24, rings=12, parent=None):
        verts=[(0,0,radius)]
        for ring in range(1,rings):
            phi=math.pi*ring/rings
            verts += [(radius*math.sin(phi)*math.cos(math.tau*i/segments),
                       radius*math.sin(phi)*math.sin(math.tau*i/segments),radius*math.cos(phi))
                      for i in range(segments)]
        bottom=len(verts)
        verts.append((0,0,-radius))
        faces=[(0,1+i,1+(i+1)%segments) for i in range(segments)]
        for ring in range(rings-2):
            start=1+ring*segments
            faces += [(start+i,start+segments+i,start+segments+(i+1)%segments,
                       start+(i+1)%segments) for i in range(segments)]
        start=1+(rings-2)*segments
        faces += [(start+i,bottom,start+(i+1)%segments) for i in range(segments)]
        obj=self.mesh(name,verts,faces,mat,0,parent)
        obj.location=loc
        return obj

    def empty(self, name, loc=(0,0,0), parent=None):
        obj = bpy.data.objects.new(name, None)
        self.collection.objects.link(obj)
        obj.parent = parent if parent is not None else self.root
        obj.location = loc
        obj.empty_display_size = .007
        return obj

    def label(self, name, text, loc, rotation, size=.004, mat=None, parent=None):
        data = bpy.data.curves.new(name, 'FONT')
        data.body = text
        data.size = size
        data.extrude = .000015
        data.align_x = 'CENTER'
        data.align_y = 'CENTER'
        obj = bpy.data.objects.new(name, data)
        self.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = rotation
        return self.finish(obj, mat if mat is not None else self.m['ivory'], 0, parent)

    def shape_dock(self, loc, rotation=(0,0,0), parent=None):
        """Place the original driven cartridge in a new fitted 50 mm housing."""
        assert self.full_body and not self.dock_placed
        self.dock_placed = True
        socket = bpy.data.objects['ShapeSocket']
        socket.parent = parent if parent is not None else self.root
        socket.matrix_parent_inverse = Matrix.Identity(4)
        socket.location = loc
        socket.rotation_euler = rotation
        socket.scale = (1,1,1)
        self.box('Dock | new universal carrier', (0,0,-.007), (.050,.050,.006),
                 self.m['steel'], .002, socket)
        self.box('Dock | isolated pocket floor', (0,0,-.0028), (.036,.036,.002),
                 self.m['recess'], .0007, socket)
        for s in (-1,1):
            self.box(f'Dock | fitted side rail {s}', (s*.021,0,-.001), (.006,.045,.009),
                     self.m['edge'], .0009, socket)
            self.box(f'Dock | fitted end rail {s}', (0,s*.021,-.001), (.036,.006,.009),
                     self.m['frame'], .0009, socket)
            self.cylinder(f'Dock | captive stud {s}', (s*.021,-.016,.004), .0018,.0016,
                          self.m['ink'], vertices=8,bevel=.0002,parent=socket)
        self.box('Dock | positive latch', (0,-.020,.004), (.013,.006,.003),
                 self.m['steel'],.0006,socket)
        self.box('Dock | latch status', (0,-.020,.0058), (.007,.0017,.0007),
                 self.m['accent'],.00015,socket)
        return socket

    def modulator_socket(self, index, loc, rotation=(0,0,0), parent=None):
        """Place a complete driven bay; local +Z is its exposed face."""
        assert self.full_body and 1 <= index <= 10 and index not in self.placed_sockets
        self.placed_sockets.add(index)
        socket = bpy.data.objects[f'ModulatorSocket{index:02d}']
        socket.parent = parent if parent is not None else self.root
        socket.matrix_parent_inverse = Matrix.Identity(4)
        socket.location = loc
        socket.rotation_euler = rotation
        socket.scale = (1,1,1)
        self.box(f'Bay {index:02d} | fitted mounting tile', (0,0,-.0035), (.029,.025,.005),
                 self.m['steel'], .001, socket)
        for s in (-1,1):
            self.cylinder(f'Bay {index:02d} | captive stud {s}', (s*.013,0,-.0005),
                          .001,.001,self.m['edge'],vertices=8,bevel=.00015,parent=socket)
        return socket


def replace_ngon(obj, radius, depth, sides, phase=math.pi/2):
    verts = [(radius*math.cos(phase+math.tau*i/sides), radius*math.sin(phase+math.tau*i/sides), z)
             for z in (-depth/2, depth/2) for i in range(sides)]
    faces = [tuple(reversed(range(sides))), tuple(range(sides,2*sides))]
    faces += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    mats = list(obj.data.materials)
    data = bpy.data.meshes.new(obj.name + ' | matched shape')
    data.from_pydata(verts, [], faces)
    data.update()
    obj.data = data
    for mat in mats:
        data.materials.append(mat)


def solid_insert(ctx, insert, key):
    """The universal cartridge carries a recognizable solid, not a flat glyph."""
    # Keep the original insert object identity: existing rarity and socket
    # drivers point to its custom properties.
    carrier=ctx.box('Temporary insert carrier',(0,0,0),(.030,.030,.004),ctx.m['accent'],0)
    insert.data=carrier.data
    bpy.data.objects.remove(carrier,do_unlink=True)
    insert['shape_representation']='solid '+key
    for child in list(insert.children):
        bpy.data.objects.remove(child,do_unlink=True)
    ctx.box(key.title()+'Insert | carrier inset',(0,0,.0022),(.023,.023,.0007),
            ctx.m['ink'],.0004,parent=insert)
    if key=='sphere':
        obj=ctx.sphere('SphereInsert | faceted solid',(0,0,.014),.0118,
                       ctx.m['steel'],24,12,parent=insert)
        ctx.ring('SphereInsert | equatorial rarity seam',(0,0,.014),.0121,.0114,.0013,
                 ctx.m['accent'],vertices=32,bevel=.00015,parent=insert)
        ctx.ring('SphereInsert | upper latitude seam',(0,0,.0212),.0095,.0089,.00065,
                 ctx.m['accent'],vertices=24,bevel=.0001,parent=insert)
    elif key=='cube':
        obj=ctx.box('CubeInsert | beveled solid',(0,0,.013),(.019,.019,.019),
                    ctx.m['accent'],.0008,parent=insert)
        ctx.box('CubeInsert | inset top',(0,0,.0227),(.013,.013,.0005),
                ctx.m['ink'],.0003,parent=insert)
        for side in (-1,1):
            ctx.box(f'CubeInsert | inset side {side}',(side*.0097,0,.013),(.0005,.013,.013),
                    ctx.m['steel'],.0003,parent=insert)
            ctx.box(f'CubeInsert | inset end {side}',(0,side*.0097,.013),(.013,.0005,.013),
                    ctx.m['steel'],.0003,parent=insert)
    else:
        verts=[(-.012,-.012,.003),(.012,-.012,.003),(.012,.012,.003),(-.012,.012,.003),(0,0,.027)]
        faces=[(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)]
        obj=ctx.mesh('PyramidInsert | square-base solid',verts,faces,ctx.m['accent'],.00055,parent=insert)
        for i,face in enumerate(faces[1:]):
            corners=[Vector(verts[j]) for j in face]
            center=sum(corners,Vector())/3
            normal=(corners[1]-corners[0]).cross(corners[2]-corners[0]).normalized()
            panel=[center+(p-center)*.70+normal*.00012 for p in corners]
            ctx.mesh(f'PyramidInsert | sloped inset {i}',panel,[(0,1,2)],ctx.m['steel'],0,parent=insert)
    obj['shape_solid']=key
    ctx.box(key.title()+'Insert | indexing notch',(0,-.013,.0028),(.005,.002,.0007),
            ctx.m['ivory'],.0002,parent=insert)


def solid_witness(ctx,key,side):
    center=(side*.031,-.053,.058)
    if key=='sphere':
        ctx.sphere(f'Dock | sphere witness {side}',center,.0055,ctx.m['accent'],16,8)
    elif key=='cube':
        obj=ctx.box(f'Dock | cube witness {side}',center,(.007,.011,.011),ctx.m['accent'],.00055)
        obj.rotation_euler.x=.24
        ctx.box(f'Dock | cube witness inset {side}',(side*.0347,-.053,.058),
                (.0004,.006,.006),ctx.m['ink'],.0002).rotation_euler.x=.24
    else:
        x0,x1=side*.029,side*.035
        verts=[(x0,-.059,.0525),(x1,-.059,.0525),(x1,-.047,.0525),(x0,-.047,.0525),
               (side*.032,-.053,.065)]
        ctx.mesh(f'Dock | pyramid witness {side}',verts,
                 [(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)],ctx.m['accent'],.00035)


def prepare_full_body(ctx, key, module):
    """Retain driver identities and connectors, remove the complete handgun."""
    ctx.full_body = True
    root = ctx.root
    anchors = [bpy.data.objects['ShapeSocket'], bpy.data.objects['MuzzleAnchor']]
    anchors += [bpy.data.objects[f'ModulatorSocket{i:02d}'] for i in range(1,11)]
    keep = {root}
    for anchor in anchors:
        keep.add(anchor)
        keep.update(anchor.children_recursive)
    obsolete = [o.name for o in root.children_recursive if o not in keep]
    for name in obsolete:
        obj = bpy.data.objects.get(name)
        if obj is not None:
            bpy.data.objects.remove(obj, do_unlink=True)
    insert = bpy.data.objects['SquareInsert | removable ceramic cartridge']
    insert.name = key.title() + 'Insert | removable shape cartridge'
    insert['shape_key'] = key
    insert['rarity_legend'] = '0 Common / 1 Uncommon / 2 Rare / 3 Legendary / 4 Mythic / 5 Divine'
    module.build_insert(ctx, insert)
    ctx.m['accent'].name = '08 | Shape rarity accent - driven by ' + key.title() + 'Insert'
    root['design'] = 'MultiWeapon / ' + SPECS[key][2]
    root['equipped_shape'] = key
    root['shape_stars'] = SHAPE_STARS[key]
    root['star_design'] = STAR_DESIGN[SHAPE_STARS[key]]
    root['status'] = 'Editable redesigned Blender asset and static review export'
    root['shared_chassis'] = 'New body; shared Square materials, shape cartridge and ten modulator interfaces'
    root['full_body_redesign'] = True
    root['animation'] = 'Mechanism preview, frames 1-40 at 30 fps; frame 1 is rest pose'
    root['source_revision'] = 'SquareHandgun material/socket reference from repository revision 8c02b8d'
    return insert


def prepare_shared(ctx, key, code, sides, module):
    root = ctx.root
    for obj in list(root.children_recursive):
        if (obj.name.startswith(('Slide |', 'Accent | slide seam', 'Muzzle |', 'Marking |',
                                 'Sight | front', 'Dock | square witness', 'Dock | witness face'))):
            bpy.data.objects.remove(obj, do_unlink=True)
    shell = ctx.box('MultiWeapon | shared rear receiver', (0,-.066,.051),
                    (.057,.084,.044), ctx.m['steel'], .003)
    cutter = ctx.box('Temporary dock recess', (0,-.050,.074),
                     (.039,.039,.026), None, 0)
    # Cut the pocket before the editable chamfer and weighted normals.
    mod = shell.modifiers.new('Open shape-cartridge pocket', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'
    mod.object = cutter
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)
    for side in (-1,1):
        for i in range(3):
            vent = ctx.box(f'MultiWeapon | rear traction groove {side} {i}',
                           (side*.0286,-.093+i*.005,.052), (.0008,.0017,.024),
                           ctx.m['recess'], .0002)
            vent.rotation_euler.x = -.16
        ctx.box(f'MultiWeapon | receiver seam {side}', (side*.0286,-.050,.032),
                (.0007,.042,.0017), ctx.m['accent'], .0002)
        if key in CUSTOM_SHAPES:
            # Matching miniature witnesses are copied from the dimensional insert below.
            pass
        elif key in SOLID_SHAPES:
            solid_witness(ctx,key,side)
        else:
            ctx.cylinder(f'Dock | {key} witness {side}', (side*.0301,-.053,.058),
                         .0065,.0014,ctx.m['accent'],axis='X',vertices=sides,bevel=.00025)
            ctx.cylinder(f'Dock | {key} witness inset {side}', (side*.031,-.053,.058),
                         .0043,.0006,ctx.m['ink'],axis='X',vertices=sides,bevel=.00015)
    insert = bpy.data.objects['SquareInsert | removable ceramic cartridge']
    insert.name = key.title() + 'Insert | removable shape cartridge'
    insert['shape_key'] = key
    insert['rarity_legend'] = '0 Common / 1 Uncommon / 2 Rare / 3 Legendary / 4 Mythic / 5 Divine'
    if key in CUSTOM_SHAPES:
        module.build_insert(ctx, insert)
        insert['shape_representation'] = 'dimensional ' + key
        # Keep the same recognisable shape on both receiver cheeks. Copies have
        # local geometry/materials only; the original insert still owns all drivers.
        for side in (-1, 1):
            witness = ctx.empty(f'Dock | {key} dimensional witness {side}', (side*.030,-.053,.057))
            witness.rotation_euler.y = side*math.pi/2
            witness.scale = (.33,.33,.33)
            copies = {insert: witness}
            def copy_children(original):
                for child in original.children:
                    copied = child.copy()
                    ctx.collection.objects.link(copied)
                    copied.name = f'Dock | {key} witness {side} | ' + child.name
                    copied.animation_data_clear()
                    copied.parent = copies[original]
                    copied.matrix_parent_inverse = child.matrix_parent_inverse.copy()
                    copied.matrix_basis = child.matrix_basis.copy()
                    copies[child] = copied
                    copy_children(child)
            copy_children(insert)
    elif key in SOLID_SHAPES:
        solid_insert(ctx,insert,key)
    else:
        replace_ngon(insert, .016 if key == 'triangle' else .015, .009, sides)
        if key == 'triangle':
            insert.location.y = -.0015
        for child in list(insert.children):
            if 'dark square face' in child.name:
                child.name = key.title() + 'Insert | inset shape face'
                replace_ngon(child, .0108, .0009, sides)
            else:
                child.name = key.title() + 'Insert | indexing notch'
                child.location = (0, .0125, .005)
                child.scale = (.75,.35,1)
    ctx.m['accent'].name = '08 | Shape rarity accent - driven by ' + key.title() + 'Insert'
    ctx.label('MultiWeapon | assembly identifier', code, (.0291,-.034,.047),
              (math.pi/2,0,math.pi/2), .0038)
    root['design'] = 'MultiWeapon / ' + SPECS[key][2]
    root['equipped_shape'] = key
    root['shape_stars'] = SHAPE_STARS[key]
    root['star_design'] = STAR_DESIGN[SHAPE_STARS[key]]
    root['status'] = 'Editable Blender asset; static GLB review; runtime uses ExportMultiWeaponRuntime.py'
    root['shared_chassis'] = 'Approved Square handgun: original grip, frame, dock and ten modulator sockets'
    root['animation'] = 'Mechanism preview, frames 1-40 at 30 fps; frame 1 is rest pose'
    root['source_revision'] = 'SquareHandgun from repository revision 8c02b8d'
    return insert


def refresh(root, insert, tier=2, level=5, count=3):
    insert['rarity_tier'] = tier
    root['weapon_level'] = level
    root['equipped_modulators'] = count
    insert.update_tag()
    root.update_tag()
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()


def validate(ctx, insert):
    root = ctx.root
    cartridges = [o for o in root.children_recursive if o.get('preview_example')]
    sockets = [o for o in root.children_recursive if re.fullmatch(r'ModulatorSocket\d{2}',o.name)]
    assert len(cartridges) == len(sockets) == 10
    if ctx.full_body:
        assert ctx.dock_placed and ctx.placed_sockets == set(range(1,11)), 'Unplaced cartridge interface'
        assert not any(o.name.startswith(('Grip |','Frame |','Slide |','Trigger |','Sight |'))
                       for o in root.children_recursive), 'Inherited handgun geometry in redesigned body'
    accent = ctx.m['accent'].node_tree.nodes.get('Principled BSDF')
    checks = 0
    for tier,(key,_,rgb) in enumerate(RARITIES):
        start,end = PROGRESSION[key]
        for level in range(1,6):
            expected_unlocked = math.floor(start+(end-start)*(level-1)/4+.5)
            for count in (0,3,10):
                refresh(root,insert,tier,level,count)
                assert sum(not o.hide_render for o in cartridges) == min(count,expected_unlocked), (key,level,count)
                actual = accent.inputs['Base Color'].default_value[:3]
                assert all(abs(a-b)<1e-5 for a,b in zip(actual,linear_color(rgb))),key
                checks += 1
    refresh(root,insert)
    if insert['shape_key'] in SOLID_SHAPES:
        solids=[o for o in insert.children_recursive if o.get('shape_solid')==insert['shape_key']]
        assert len(solids)==1,'Missing solid shape insert'
        assert min(solids[0].dimensions)>.008,'Shape insert is flattened'
    if insert['shape_key'] in CUSTOM_SHAPES:
        children=[o for o in insert.children_recursive if o.type=='MESH']
        assert children, 'Missing dimensional shape insert'
        inverse=insert.matrix_world.inverted()
        points=[inverse @ obj.matrix_world @ Vector(corner) for obj in children for corner in obj.bound_box]
        size=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
        assert min(size)>.008 and max(size)<.045, ('Invalid shape insert envelope',size)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    triangles = 0
    bounds = []
    for obj in root.children_recursive:
        if obj.type not in {'MESH','FONT'}:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        data = evaluated.to_mesh()
        data.calc_loop_triangles()
        assert len(data.polygons)>0,obj.name
        assert all(math.isfinite(v) for vert in data.vertices for v in vert.co),obj.name
        if not obj.hide_render:
            triangles += len(data.loop_triangles)
            bounds.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
        evaluated.to_mesh_clear()
    dims = [max(p[i] for p in bounds)-min(p[i] for p in bounds) for i in range(3)]
    assert all(.025 < d < (1.4 if ctx.full_body else .70) for d in dims),dims
    return {'rarity_level_equipment_cases':checks,'modulator_sockets':len(sockets),
            'visible_triangles_at_default':triangles, 'dimensions_metres':dims,
            'model_objects':len(root.children_recursive)+1,'full_body_redesign':ctx.full_body}


def body_bounds(root):
    bpy.context.view_layer.update()
    return [o.matrix_world @ Vector(corner) for o in root.children_recursive
            if o.type in {'MESH','FONT'} and not o.hide_render for corner in o.bound_box]


def fit_camera(camera, points, direction, margin=1.16):
    """Fit both screen dimensions, including negative spaces and deployed parts."""
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    focus=(low+high)*.5
    camera.location=focus+Vector(direction).normalized()*max((high-low).length*3,.5)
    camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO'
    inv=camera.rotation_euler.to_matrix().transposed()
    projected=[inv @ (p-focus) for p in points]
    width=max(p.x for p in projected)-min(p.x for p in projected)
    height=max(p.y for p in projected)-min(p.y for p in projected)
    scene=bpy.context.scene
    camera.data.ortho_scale=max(width,height*scene.render.resolution_x/scene.render.resolution_y)*margin
    camera.data.clip_start=.001
    camera['review_focus']=list(focus)


def configure_studio(key, design=None, full_body=False):
    scene = bpy.context.scene
    scene.name = SPECS[key][0] + '_Mechanism'
    scene.render.fps = 30
    scene.frame_start = 1
    scene.frame_end = 40
    scene.frame_set(1)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.cycles.device = 'CPU'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        for dev in prefs.devices:
            dev.use = dev.type == 'OPTIX'
        if any(d.use for d in prefs.devices):
            scene.cycles.device = 'GPU'
    except Exception as exc:
        print('GPU unavailable:',exc,flush=True)
    scene.render.resolution_x = 1440
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .42
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.exposure = -.65
    hero = bpy.data.objects['Review | 01 three-quarter']
    side = bpy.data.objects['Review | 02 silhouette']
    rear = bpy.data.objects['Review | 03 slot and player angle']
    mod = bpy.data.objects['Review | 05 recessed modulator bank']
    if full_body:
        root=bpy.data.objects['GunRoot']
        points=[]
        for frame in (1,12,20,28,40):
            scene.frame_set(frame)
            points.extend(body_bounds(root))
        scene.frame_set(1)
        fit_camera(hero,points,design.get('hero_direction',(1.05,1.1,.8)))
        fit_camera(side,points,design.get('silhouette_direction',(1,0,.06)))
        socket=bpy.data.objects['ShapeSocket']
        bpy.context.view_layer.update()
        dock_points=[socket.matrix_world @ Vector((x,y,z))
                     for x in (-.048,.048) for y in (-.048,.048) for z in (-.015,.047)]
        fit_camera(rear,dock_points,socket.matrix_world.to_3x3() @ Vector((.45,-.6,1)),1.25)
        mod_points=[o.matrix_world @ Vector(corner) for i in range(1,11)
                    for o in bpy.data.objects[f'ModulatorSocket{i:02d}'].children_recursive
                    if o.type=='MESH' for corner in o.bound_box]
        fit_camera(mod,mod_points,design.get('hero_direction',(1.05,1.1,.8)))
        low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
        high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        focus=(low+high)*.5
        stage_scale=max(high-low)/.34
        bpy.data.objects['Studio ground'].location.z=low.z-(.065 if SHAPE_STARS[key]==4 else .012)
        for name,offset in [('Studio | broad key',(.3,-.3,.65)),
                            ('Studio | cool fill',(-.48,.02,.38)),
                            ('Studio | edge light',(.2,.48,.45))]:
            light=bpy.data.objects[name]
            light.location=focus+Vector(offset)*stage_scale
            light.rotation_euler=(focus-light.location).to_track_quat('-Z','Y').to_euler()
            light.data.energy*=stage_scale**2
            light.data.size*=stage_scale
        scene.camera=hero
        return hero,side,rear,mod
    target = (0,.070,.018) if key in CUSTOM_SHAPES else ((0,.063,-.005) if key=='triangle' else ((0,.043,.012) if key in SOLID_SHAPES else (0,.037,-.015)))
    scale = .56 if key in CUSTOM_SHAPES or key=='triangle' else (.51 if key in SOLID_SHAPES else .47)
    for camera,loc,focus,span in [
        (hero,(.48,.43,.30),target,scale),
        (side,(.62,.040,.045),target,scale*.96),
        (rear,(.24,-.44,.40),(0,-.015,.042),.29),
        (mod,(.48,.22,.23),(0,.033,.008),.29),
    ]:
        camera.location=loc
        camera.rotation_euler=(Vector(focus)-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=span
        camera.data.clip_start=.001
    scene.camera=hero
    return hero,side,rear,mod


def export_glb(root, path):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root]+list(root.children_recursive):
        if not obj.hide_viewport:
            obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    # Bake the shown rare/L5/three-cartridge state. The editable source retains
    # every locked/empty/installed option and the mechanism animation.
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB',use_selection=True,
        use_visible=True,export_apply=True,export_cameras=False,export_lights=False,
        export_yup=True,export_extras=True,export_animations=False)
    raw=path.read_bytes()
    magic,version,total=struct.unpack_from('<4sII',raw)
    assert magic==b'glTF' and version==2 and total==len(raw)
    length,kind=struct.unpack_from('<II',raw,12)
    assert kind==0x4E4F534A
    data=json.loads(raw[20:20+length])
    assert not data.get('cameras')
    names={n.get('name','') for n in data['nodes']}
    assert 'GunRoot' in names and 'ShapeSocket' in names and 'MuzzleAnchor' in names
    assert sum(bool(re.fullmatch(r'ModulatorSocket\d{2}',n)) for n in names)==10
    assert not any(n.startswith(('Studio','Review |')) for n in names)
    return {'glb_nodes':len(data['nodes']),'glb_meshes':len(data.get('meshes',[])),
            'glb_bytes':len(raw),'glb_checked':True}


def pose_view(insert,hero):
    bpy.ops.object.select_all(action='DESELECT')
    insert.select_set(True)
    bpy.context.view_layer.objects.active=insert
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.shading.type='MATERIAL'
                space.overlay.show_extras=False
                space.overlay.show_floor=False
                space.region_3d.view_rotation=hero.rotation_euler.to_quaternion()
                space.region_3d.view_location=Vector(hero.get('review_focus',(0,.055,-.005)))
                space.region_3d.view_distance=hero.data.ortho_scale*1.15
                space.clip_start=.001
            elif area.type=='PROPERTIES':
                area.spaces.active.context='OBJECT'


def build_asset(key,source,args):
    name,code,title,sides=SPECS[key]
    out=OUTPUT/name
    (out/'Renders').mkdir(parents=True,exist_ok=True)
    (out/'.gdignore').write_text('',encoding='utf-8')
    status('building',weapon=key)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    bpy.context.preferences.filepaths.save_version=0
    ctx=Context()
    module=importlib.import_module('multiweapon.'+key)
    full_body=getattr(module,'FULL_BODY',False)
    insert=prepare_full_body(ctx,key,module) if full_body else prepare_shared(ctx,key,code,sides,module)
    design=module.build(ctx)
    bpy.data.objects['MuzzleAnchor'].location=design['muzzle']
    bpy.data.objects['MuzzleAnchor']['purpose']='Visual effect/projectile origin; Blender +Y forward'
    for part in design['moving_parts']:
        bpy.data.objects[part]['mechanism_part']=True
    refresh(ctx.root,insert)
    hero,side,rear,mod=configure_studio(key,design,full_body)
    report=validate(ctx,insert)
    report.update({'model':name,'title':title,'equipped_shape':key,
                   'shape_stars':SHAPE_STARS[key],'star_design':STAR_DESIGN[SHAPE_STARS[key]],
                   'source':'SquareHandgun.blend','source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
                   'blender_version':bpy.app.version_string,'description':design['description'],
                   'design_name':design.get('design_name',title),
                   'moving_parts':design['moving_parts'],'source_file':name+'.blend','review_export':name+'.glb',
                   'shape_insert':('dimensional '+key) if key in CUSTOM_SHAPES else (('solid '+key) if key in SOLID_SHAPES else 'flat '+key),
                   'coordinates':'metres; Blender +Y forward/+Z up; GLB -Z forward/+Y up',
                   'rarity_control':insert.name+'.rarity_tier','default_preview':'Rare / level 5 / three installed modulators',
                   'animation_frames':[1,40],'animation_fps':30,'integration':'Editable art and static review export; runtime export is a separate step using ExportMultiWeaponRuntime.py'})
    bpy.context.scene.frame_set(1)
    report.update(export_glb(ctx.root,out/(name+'.glb')))
    pose_view(insert,hero)
    bpy.context.scene.camera=hero
    bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
    # Independent reload catches lost parenting or missing data before renders.
    bpy.ops.wm.open_mainfile(filepath=str(out/(name+'.blend')),load_ui=False)
    root=bpy.data.objects['GunRoot']
    saved_insert=next(o for o in root.children_recursive if o.get('shape_key')==key)
    assert saved_insert.parent.name=='ShapeSocket'
    assert all(bpy.data.objects.get(n) is not None for n in design['moving_parts'])
    report['blend_reopen_checked']=True
    animated=[bpy.data.objects[n] for n in design['moving_parts']
              if bpy.data.objects[n].animation_data and bpy.data.objects[n].animation_data.action]
    assert animated, key+' lacks an animated mechanism'
    rest=[obj.matrix_local.copy() for obj in animated]
    bpy.context.scene.frame_set(20)
    assert any(any(abs(a-b)>1e-5 for row_a,row_b in zip(before,obj.matrix_local) for a,b in zip(row_a,row_b))
               for before,obj in zip(rest,animated)),key+' mechanism did not animate after reopening'
    bpy.context.scene.frame_set(1)
    report['saved_animation_evaluated']=True
    if not args.no_render:
        scene=bpy.context.scene
        for camname,filename,frame in [
            ('Review | 01 three-quarter','01_three_quarter.png',1),
            ('Review | 02 silhouette','02_silhouette.png',1),
            ('Review | 03 slot and player angle','03_shape_slot.png',1),
            ('Review | 01 three-quarter','04_mechanism_pose.png',12 if key=='triangle' else 20),
        ]:
            status('rendering',weapon=key,view=filename)
            scene.camera=bpy.data.objects[camname]
            scene.frame_set(frame)
            scene.render.filepath=str(out/'Renders'/filename)
            bpy.ops.render.render(write_still=True)
        scene.frame_set(1)
        scene.camera=bpy.data.objects['Review | 01 three-quarter']
        pose_view(saved_insert,scene.camera)
        bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
    (out/'model-info.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    (out/'README.txt').write_text(
        f'{title}\n\n{design["description"]}\n\n'
        f'SHAPE TIER: {SHAPE_STARS[key]} stars. {STAR_DESIGN[SHAPE_STARS[key]]}.\n'
        'Stars follow the shape catalog; Common-Divine item rarity controls accent color separately.\n\n'
        f'Open {name}.blend in Blender. The saved scene opens at the rest pose.\n'
        + ('This is a complete body redesign. Square materials, cartridge interfaces and rarity controls carry the family identity.\n'
           'Shape dock and ten modulator bays are fitted to the new form. Bevels remain editable.\n\n' if full_body else
           'Shared frame, ribbed grip, trigger guard, shape dock, and ten modulator bays come from the approved Square handgun.\n'
           'The upper assembly and matching shape insert are newly authored. Bevels remain editable.\n\n') +
        'CONTROLS\n'
        f'Select {saved_insert.name}; Object Properties > Custom Properties > rarity_tier.\n'
        '0 Common (white), 1 Uncommon (green), 2 Rare (blue), 3 Legendary (purple), 4 Mythic (red), 5 Divine (gold).\n'
        'GunRoot.weapon_level: 1-5. GunRoot.equipped_modulators: 0-10. Original game unlock rules are preserved.\n'
        'Default: Rare, level 5, three installed cartridges, six unlocked bays.\n'
        'Press Play on the timeline for the mechanism study (frames 1-40, 30 fps). Frame 1 is the export pose.\n\n'
        'HANDOFF\n'
        f'{name}.glb is a static snapshot of the default preview with the studio excluded.\n'
        'The Blender source retains all empty/locked/installed socket geometry, drivers and keyframes.\n'
        'GunRoot / ShapeSocket / MuzzleAnchor / ModulatorSocket01-10 are preserved. Named mechanism pivots are separate.\n'
        'Metres. Blender +Y forward and +Z up; GLB converts to -Z forward and +Y up.\n'
        'Renders contains front three-quarter, side silhouette, shape-slot and mechanism-pose views.\n'
        'This folder is editable art and static review geometry. Runtime export/assignment is a separate step.\n\n'
        f'Validation: {report["rarity_level_equipment_cases"]} rarity/level/equipment combinations; '
        'ten sockets; finite geometry; source reopened; GLB hierarchy inspected.\n'
        f'Visible default model: {report["visible_triangles_at_default"]} triangles. Built with Blender {bpy.app.version_string}.\n',encoding='utf-8')
    status('asset_complete',weapon=key,report=report)
    return report


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--source',type=Path,default=OUTPUT/'SquareHandgun'/'SquareHandgun.blend')
    parser.add_argument('--weapons',nargs='+',choices=list(SPECS),default=list(SPECS))
    parser.add_argument('--no-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if not args.source.is_file() and args.source==OUTPUT/'SquareHandgun'/'SquareHandgun.blend':
        # This checkout can predate the Square commit. Read its approved source
        # from the local Git object database without changing branches or files.
        cached=ROOT/'.tmp'/'multiweapon-square-source.blend'
        cached.parent.mkdir(parents=True,exist_ok=True)
        original=subprocess.run(['git','show','8c02b8d:Art/WeaponConcepts/SquareHandgun/SquareHandgun.blend'],
                                cwd=ROOT,capture_output=True,check=True).stdout
        assert hashlib.sha256(original).hexdigest()=='cfdb814fe2d8c6ee7f2884651ef4584892481ded02536fd5da0e6884dc96dd94'
        cached.write_bytes(original)
        args.source=cached
    assert args.source.is_file(),f'Square source not found: {args.source}; pass --source with its .blend path'
    assert len(RARITIES)==6
    reports=[build_asset(key,args.source.resolve(),args) for key in args.weapons]
    status('complete',assets=reports)


if __name__=='__main__':
    log=ROOT/'.tmp'/'multiweapon-build.log'
    log.parent.mkdir(parents=True,exist_ok=True)
    sys.stdout=open(log,'w',encoding='utf-8',buffering=1)
    sys.stderr=sys.stdout
    try:
        main()
    except Exception as exc:
        traceback.print_exc()
        status('failed',error=str(exc))
        raise
