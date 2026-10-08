"""Three nested projection cages and sliding rift projectors."""
import math

FULL_BODY = True
import bpy
from mathutils import Vector


def _beam(ctx, name, a, b, width, mat, parent=None):
    a, b = Vector(a), Vector(b)
    obj = ctx.box(name, (a+b)/2, (width, width, (b-a).length),
                  mat, bevel=width*.20, parent=parent)
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return obj


def _cage(ctx, name, half, center, width, mat, parent=None):
    c = Vector(center)
    corners = [c+Vector((x*half, y*half, z*half))
               for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    for i, a in enumerate(corners):
        for j in range(i+1, 8):
            b = corners[j]
            if sum(abs(a[k]-b[k]) > 1e-7 for k in range(3)) == 1:
                _beam(ctx, '%s_Edge_%d_%d' % (name, i, j), a, b,
                      width, mat, parent)
    return corners


def build(ctx):
    m = ctx.m
    center = (0, .310, 0)
    outer = _cage(ctx, 'Tesseract_Suspended_Outer', .200, center, .014, m['steel'])
    middle = _cage(ctx, 'Tesseract_Projection_Middle', .116, center, .009, m['edge'])
    inner = _cage(ctx, 'Tesseract_Projection_Inner', .052, center, .0055, m['steel'])
    for i in range(8):
        _beam(ctx, 'Tesseract_Outer_Projection_Link_%d' % i,
              outer[i], middle[i], .008, m['steel'])
        _beam(ctx, 'Tesseract_Inner_Projection_Link_%d' % i,
              middle[i], inner[i], .0045, m['recess'])
        ctx.box('Tesseract_Cage_Corner_%d' % i, outer[i], (.029, .029, .029),
                m['frame'], bevel=.005)
    # Wide front/rear cross members suspend the cages between two telescopic arms.
    for yy in (.075, .545):
        ctx.box('Tesseract_Suspension_Crossmember_%s' % yy,
                (0, yy, -.215), (.624, .035, .033), m['frame'], bevel=.006)
        for side in (-1, 1):
            ctx.box('Tesseract_Arm_Track_Rise_%s_%s' % (yy, side),
                    (side*.274, yy, -.137), (.080, .045, .145),
                    m['frame'], bevel=.007)
            _beam(ctx, 'Tesseract_Cage_Brace_%s_%s' % (yy, side),
                  (side*.195, yy, -.210), (side*.200, .110 if yy < .3 else .510, -.200),
                  .016, m['edge'])
    ctx.box('Tesseract_Rear_Dock_Bridge', (0, .030, .091),
            (.592, .100, .050), m['steel'], bevel=.007)
    ctx.box('Tesseract_Dock_Bridge_Inset', (0, -.003, .091),
            (.424, .002, .030), m['recess'], bevel=.002)
    ctx.shape_dock((0, .028, .126))
    core = ctx.empty('Tesseract_Rift_Pulse', center)
    ctx.box('Tesseract_Rift_Dark_Volume', (0, 0, 0), (.066, .071, .066),
            m['ink'], bevel=.006, parent=core)
    for axis in range(3):
        size = [.075, .075, .075]
        size[axis] = .003
        ctx.box('Tesseract_Rift_Plane_%d' % axis, (0, 0, 0), size,
                m['accent'], bevel=.0007, parent=core)
    moving = [core.name]
    for side in (-1, 1):
        arm = ctx.empty('Tesseract_Sliding_Arm_%s' % side, (side*.274, .310, 0))
        moving.append(arm.name)
        ctx.box('Tesseract_Arm_Main_%s' % side, (0, 0, 0), (.083, .474, .140),
                m['steel'], bevel=.009, parent=arm)
        for zz in (-.050, .050):
            ctx.box('Tesseract_Arm_Track_%s_%s' % (side, zz),
                    (0, 0, zz), (.091, .420, .012), m['edge'],
                    bevel=.002, parent=arm)
        ctx.box('Tesseract_Arm_Outer_Inset_%s' % side, (side*.043, 0, 0),
                (.002, .400, .072), m['recess'], bevel=.003, parent=arm)
        for i in range(5):
            ctx.modulator_socket((1 if side > 0 else 6)+i,
                                 (side*.046, -.120+i*.065, 0),
                                 rotation=(0, side*math.pi/2, 0), parent=arm)
        for yy in (-.191, .191):
            ctx.box('Tesseract_Arm_End_Armor_%s_%s' % (side, yy),
                    (0, yy, 0), (.094, .032, .150), m['frame'],
                    bevel=.005, parent=arm)
        # Side handle loops are part of the arms, with clear hand openings.
        for zz in (-.073, .073):
            ctx.box('Tesseract_Handle_Bridge_%s_%s' % (side, zz),
                    (side*.068, -.174, zz), (.100, .040, .025),
                    m['steel'], bevel=.004, parent=arm)
        ctx.box('Tesseract_Side_Hold_%s' % side,
                (side*.111, -.174, 0), (.031, .043, .148),
                m['grip'], bevel=.006, parent=arm)
        for j in range(7):
            ctx.box('Tesseract_Hold_Rib_%s_%d' % (side, j),
                    (side*.128, -.174, -.060+j*.020), (.006, .047, .006),
                    m['edge'], bevel=.0012, parent=arm)
        ctx.box('Tesseract_Projector_Front_%s' % side, (0, .239, 0),
                (.063, .009, .092), m['recess'], bevel=.003, parent=arm)
        for zz in (-.044, .044):
            ctx.box('Tesseract_Projector_Charge_%s_%s' % (side, zz),
                    (0, .245, zz), (.042, .002, .003), m['accent'],
                    bevel=.0006, parent=arm)
        for frame, amount in ((1, 0), (20, 1), (40, 0)):
            arm.location.x = side*(.274+.028*amount)
            arm.location.y = .310+.020*amount
            arm.keyframe_insert(data_path='location', frame=frame)
        arm.location = (side*.274, .310, 0)
    for frame, amount in ((1, 0), (20, 1), (40, 0)):
        core.rotation_euler[1] = math.radians(25)*amount
        core.scale = (1+.14*amount,)*3
        core.keyframe_insert(data_path='rotation_euler', frame=frame)
        core.keyframe_insert(data_path='scale', frame=frame)
    core.rotation_euler[1] = 0
    core.scale = (1, 1, 1)
    return {'muzzle': (0, .561, 0), 'moving_parts': moving,
            'design_name': 'Two-Handed Rift Frame',
            'hero_direction': (1.15, -1.55, .95),
            'silhouette_direction': (1, 0, .08),
            'description': 'Broad two-handed rift frame with three suspended '
                           'nested projection cages, eight diagonal corner '
                           'links per layer, substantial telescoping side arms '
                           'and twin ribbed integrated handle bridges.'}


def build_insert(ctx, insert):
    for child in list(insert.children):
        bpy.data.objects.remove(child, do_unlink=True)
    carrier = ctx.box('Temporary tesseract carrier', (0, 0, 0),
                      (.028, .028, .004), ctx.m['accent'], bevel=0)
    insert.data = carrier.data
    bpy.data.objects.remove(carrier, do_unlink=True)
    insert['shape_representation'] = 'solid tesseract'
    insert['shape_solid'] = 'tesseract'
    outer = _cage(ctx, 'TesseractInsert_Outer', .0115, (0, 0, .015),
                  .0015, ctx.m['accent'], insert)
    inner = _cage(ctx, 'TesseractInsert_Inner', .0054, (0, 0, .015),
                  .0011, ctx.m['steel'], insert)
    for i in range(8):
        _beam(ctx, 'TesseractInsert_Projection_%d' % i, outer[i], inner[i],
              .0008, ctx.m['steel'], insert)
    ctx.box('TesseractInsert_Core', (0, 0, .015), (.005, .005, .005),
            ctx.m['ink'], bevel=.0005, parent=insert)
