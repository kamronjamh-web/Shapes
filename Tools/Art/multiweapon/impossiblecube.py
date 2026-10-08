"""Bent return beams and offset nested cages trap a faceted singularity."""
import math

FULL_BODY = True
import bpy
from mathutils import Vector


def _beam(ctx, name, a, b, width, mat, parent=None):
    a, b = Vector(a), Vector(b)
    obj = ctx.box(name, (a+b)/2, (width, width, (b-a).length),
                  mat, bevel=width*.22, parent=parent)
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return obj


def _bent_cube(ctx, name, half, offset, center, width, mat, parent=None):
    c = Vector(center)
    corners = [c+Vector((x*half, y*half, z*half))
               for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    for i, a in enumerate(corners):
        for j in range(i+1, 8):
            b = corners[j]
            axes = [k for k in range(3) if abs(a[k]-b[k]) > 1e-7]
            if len(axes) != 1:
                continue
            axis = axes[0]
            other = (axis+1) % 3
            step = Vector((0, 0, 0))
            step[other] = offset*(1 if a[other]-c[other] > 0 else -1)
            first, last = a.lerp(b, .29), a.lerp(b, .71)
            path = [a, first, first+step, last+step, last, b]
            for k in range(len(path)-1):
                _beam(ctx, '%s_Return_%d_%d_%d' % (name, i, j, k),
                      path[k], path[k+1], width, mat, parent)
    return corners


def _octahedron(ctx, name, radius, mat, parent, bevel):
    verts = [(radius, 0, 0), (-radius, 0, 0), (0, radius, 0),
             (0, -radius, 0), (0, 0, radius), (0, 0, -radius)]
    faces = [(0, 2, 4), (2, 1, 4), (1, 3, 4), (3, 0, 4),
             (2, 0, 5), (1, 2, 5), (3, 1, 5), (0, 3, 5)]
    return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)


def build(ctx):
    m = ctx.m
    center = Vector((0, .160, .030))
    pieces = []
    corners = [(x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    for i, signs in enumerate(corners):
        start = center+Vector(tuple(s*.240 for s in signs))
        chunk = ctx.empty('ImpossibleCube_Suspended_Fragment_%02d' % i, start)
        pieces.append((chunk, tuple(start), signs))
        ctx.box('ImpossibleCube_Fractured_Corner_%02d' % i, (0, 0, 0),
                (.066, .066, .066), m['frame'], bevel=.010, parent=chunk)
        # Each suspended corner owns three stepped half-beams; central gaps
        # are intentional fractures that widen when the corner fragments split.
        for axis in range(3):
            inward = Vector((0, 0, 0))
            inward[axis] = -signs[axis]
            bend = Vector((0, 0, 0))
            other = (axis+1) % 3
            bend[other] = signs[other]*.043
            a = inward*.070
            b = inward*.153
            path = [Vector((0, 0, 0)), a, a+bend, b+bend, b, inward*.195]
            for j in range(5):
                _beam(ctx, 'ImpossibleCube_Fragment_%02d_Beam_%d_%d' % (i, axis, j),
                      path[j], path[j+1], .021, m['steel'], chunk)
            # Bonded surface pad at each primary corner face.
            loc = [0, 0, 0]
            loc[axis] = signs[axis]*.034
            size = [.043, .043, .043]
            size[axis] = .003
            ctx.box('ImpossibleCube_Fragment_%02d_Face_Pad_%d' % (i, axis),
                    loc, size, m['recess'], bevel=.002, parent=chunk)
        ctx.modulator_socket(i+5 if i < 4 else i-3, (signs[0]*.039, 0, 0),
                             rotation=(0, signs[0]*math.pi/2, 0), parent=chunk)
        if i in (0, 7):
            ctx.modulator_socket(9 if i == 0 else 10, (0, signs[1]*.039, 0),
                                 rotation=(-signs[1]*math.pi/2, 0, 0), parent=chunk)
        if i == 3:
            ctx.shape_dock((0, 0, .043), parent=chunk)
        # Narrow ceramic edge traces emphasize the fractured corner topology.
        ctx.box('ImpossibleCube_Fragment_%02d_Charge' % i,
                (0, 0, signs[2]*.036), (.030, .003, .0015),
                m['accent'], bevel=.0004, parent=chunk)
    inner = ctx.empty('ImpossibleCube_Offset_Inner_Frame', center)
    _bent_cube(ctx, 'ImpossibleCube_Inner_Penrose_Return', .132, .015,
               (.011, -.007, .007), .010, m['edge'], inner)
    for axis in ('X', 'Y', 'Z'):
        ctx.ring('ImpossibleCube_Singularity_Field_'+axis, (0, 0, 0),
                 .122, .112, .006, m['steel'], axis=axis, vertices=16,
                 bevel=.0015, parent=inner)
    core = ctx.empty('ImpossibleCube_Dark_Singularity_Pulse', center)
    _octahedron(ctx, 'ImpossibleCube_Faceted_Dark_Core', .094, m['ink'], core, .007)
    for axis in ('X', 'Y', 'Z'):
        ctx.ring('ImpossibleCube_Core_Restraint_'+axis, (0, 0, 0),
                 .102, .099, .003, m['accent'], axis=axis, vertices=12,
                 bevel=.0007, parent=core)
    # Small trapping blades are bonded to the inner frame, keeping the field
    # mechanism inside the large, readable openings between fractured beams.
    for i in range(6):
        theta = i*math.tau/6
        blade = ctx.box('ImpossibleCube_Field_Vane_%d' % i,
                        (.127*math.cos(theta), 0, .127*math.sin(theta)),
                        (.034, .072, .018), m['steel'], bevel=.004, parent=inner)
        blade.rotation_euler[1] = math.pi/2-theta
    for frame, amount in ((1, 0), (20, 1), (40, 0)):
        for chunk, start, signs in pieces:
            chunk.location = tuple(start[k]+signs[k]*.030*amount for k in range(3))
            chunk.rotation_euler = (signs[0]*amount*.095,
                                    signs[1]*amount*.12,
                                    signs[2]*amount*.075)
            chunk.keyframe_insert(data_path='location', frame=frame)
            chunk.keyframe_insert(data_path='rotation_euler', frame=frame)
        inner.rotation_euler = (.08*amount, .24*amount, -.06*amount)
        core.rotation_euler = (.14*amount, -.33*amount, .16*amount)
        core.scale = (1+.08*amount,)*3
        inner.keyframe_insert(data_path='rotation_euler', frame=frame)
        core.keyframe_insert(data_path='rotation_euler', frame=frame)
        core.keyframe_insert(data_path='scale', frame=frame)
    for chunk, start, signs in pieces:
        chunk.location = start
        chunk.rotation_euler = (0, 0, 0)
    inner.rotation_euler = core.rotation_euler = (0, 0, 0)
    core.scale = (1, 1, 1)
    return {'muzzle': (0, .454, .030),
            'moving_parts': [p[0].name for p in pieces]+[inner.name, core.name],
            'design_name': 'Floating Fractured Singularity Cube',
            'hero_direction': (1.15, -1.55, 1.05),
            'silhouette_direction': (1, -.10, .08),
            'description': 'Floating impossible cube of eight suspended armored '
                           'corner fragments and stepped half-beams, splitting '
                           'and rotating around a dark singularity, offset '
                           'Penrose return cage, and orthogonal trapping fields.'}


def build_insert(ctx, insert):
    for child in list(insert.children):
        bpy.data.objects.remove(child, do_unlink=True)
    carrier = ctx.box('Temporary impossiblecube carrier', (0, 0, 0),
                      (.028, .028, .004), ctx.m['accent'], bevel=0)
    insert.data = carrier.data
    bpy.data.objects.remove(carrier, do_unlink=True)
    insert['shape_representation'] = 'solid impossiblecube'
    insert['shape_solid'] = 'impossiblecube'
    _bent_cube(ctx, 'ImpossibleCubeInsert_Bent_Frame', .009, .0026,
               (0, 0, .015), .0018, ctx.m['accent'], insert)
    core = ctx.empty('ImpossibleCubeInsert_Core', (0, 0, .015), parent=insert)
    _octahedron(ctx, 'ImpossibleCubeInsert_Singularity', .0047,
                ctx.m['ink'], core, .0003)
