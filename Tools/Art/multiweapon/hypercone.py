"""Layered hypercone drill with helical vanes and counter-rotating collar."""
import math

FULL_BODY = True
import bpy


def _sleeve(ctx, name, y0, y1, r0, r1, wall, mat, parent=None,
            z=.112, vertices=24, bevel=.001):
    verts = [(r*math.cos(math.tau*i/vertices), y,
              z+r*math.sin(math.tau*i/vertices))
             for y, outer in ((y0, r0), (y1, r1))
             for r in (outer, outer-wall) for i in range(vertices)]
    n = vertices
    faces = []
    for i in range(n):
        j = (i+1) % n
        faces.extend([(i, j, n+j, n+i),
                      (2*n+i, 3*n+i, 3*n+j, 2*n+j),
                      (i, 2*n+i, 2*n+j, j),
                      (n+i, n+j, 3*n+j, 3*n+i)])
    return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)


def _helix(ctx, name, phase, mat, parent):
    verts = []
    count = 28
    for i in range(count):
        t = i/(count-1)
        y = .006+.141*t
        if y < .050:
            r = .055+(.042-.055)*y/.055
        elif y < .096:
            r = .042+(.029-.042)*(y-.050)/.051
        else:
            r = .029+(.014-.029)*(y-.096)/.057
        angle = phase+math.tau*1.15*t
        for radius, offset in ((r-.0015, -.07), (r+.005, -.07),
                               (r+.005, .07), (r-.0015, .07)):
            verts.append((radius*math.cos(angle+offset), y,
                          radius*math.sin(angle+offset)))
    faces = [(3, 2, 1, 0)]
    for i in range(count-1):
        for k in range(4):
            faces.append((i*4+k, i*4+(k+1)%4,
                          (i+1)*4+(k+1)%4, (i+1)*4+k))
    faces.append(tuple((count-1)*4+k for k in range(4)))
    return ctx.mesh(name, verts, faces, mat, bevel=.0008, parent=parent)


def build(ctx):
    m = ctx.m
    ctx.box('Hypercone_Rear_Torsion_Spine', (0, -.167, 0),
            (.158, .374, .130), m['steel'], bevel=.015)
    ctx.box('Hypercone_Spine_Top_Panel', (0, -.165, .067),
            (.116, .285, .012), m['frame'], bevel=.004)
    ctx.box('Hypercone_Spine_Bottom_Recess', (0, -.164, -.067),
            (.110, .290, .006), m['recess'], bevel=.003)
    ctx.shape_dock((0, -.192, .082))
    for side in (-1, 1):
        ctx.box('Hypercone_Side_Service_Inset_%s' % side,
                (side*.080, -.165, .034), (.003, .313, .050),
                m['recess'], bevel=.003)
        for i in range(5):
            ctx.modulator_socket((1 if side > 0 else 6)+i,
                                 (side*.084, -.298+i*.064, .041),
                                 rotation=(0, side*math.pi/2, 0))
    # A broad rear brace and transverse hold establish a lance, rather than a pistol.
    ctx.box('Hypercone_Rear_Brace_Crosspiece', (0, -.344, 0),
            (.218, .035, .205), m['frame'], bevel=.010)
    ctx.box('Hypercone_Rear_Hold', (0, -.365, 0), (.130, .041, .044),
            m['grip'], bevel=.008)
    for i in range(8):
        ctx.box('Hypercone_Rear_Hold_Rib_%d' % i,
                (-.055+i*.016, -.388, 0), (.007, .006, .045),
                m['edge'], bevel=.0013)
    for side in (-1, 1):
        ctx.box('Hypercone_Brace_Upper_Lug_%s' % side,
                (side*.084, -.320, .100), (.034, .075, .024),
                m['steel'], bevel=.004)
    # A secondary side loop has two solid returns and a ribbed hold.
    for yy in (-.102, .010):
        ctx.box('Hypercone_Secondary_Hold_Return_%s' % yy,
                (.130, yy, 0), (.105, .033, .035), m['frame'], bevel=.005)
    ctx.box('Hypercone_Secondary_Hold', (.180, -.046, 0),
            (.032, .143, .035), m['grip'], bevel=.007)
    for i in range(7):
        ctx.box('Hypercone_Secondary_Rib_%d' % i,
                (.198, -.096+i*.017, 0), (.006, .007, .038),
                m['edge'], bevel=.0013)
    ctx.ring('Hypercone_Rear_Head_Bulkhead', (0, .021, 0),
             .144, .134, .033, m['frame'], axis='Y', vertices=24, bevel=.003)
    for side in (-1, 1):
        ctx.box('Hypercone_Bulkhead_Torque_Junction_%s' % side,
                (side*.107, .007, 0), (.061, .034, .045),
                m['frame'], bevel=.005)
    rotor = ctx.empty('Hypercone_Counterrotating_Telescopic_Sleeves', (0, .035, 0))
    scale = 2.4
    for i, (a, b, ra, rb) in enumerate(((0, .055, .055, .042),
                                      (.050, .101, .042, .029),
                                      (.096, .153, .029, .014))):
        _sleeve(ctx, 'Hypercone_Long_Taper_%d' % i, a*scale, b*scale,
                ra*scale, rb*scale, .0045*scale, m['steel'],
                rotor, z=0, bevel=.0035)
        _sleeve(ctx, 'Hypercone_Taper_Recess_%d' % i, (a+.002)*scale,
                (a+.009)*scale, (ra+.001)*scale, (ra-.001)*scale,
                .002*scale, m['recess'], rotor, z=0, bevel=.001)
    for i in range(3):
        vane = _helix(ctx, 'Hypercone_Long_Cutting_Helix_%d' % i,
                      i*math.tau/3, m['edge'], rotor)
        vane.scale = (scale,)*3
    counter = ctx.empty('Hypercone_Segmented_Counter_Collar', (0, .068, 0))
    ctx.ring('Hypercone_Counter_Collar_Base', (0, 0, 0),
             .1608, .1420, .036, m['recess'], axis='Y', vertices=24,
             bevel=.0024, parent=counter)
    for i in range(12):
        theta = i*math.tau/12
        panel = ctx.box('Hypercone_Collar_Armor_%02d' % i,
                        (.1524*math.cos(theta), 0, .1524*math.sin(theta)),
                        (.046, .052, .020), m['steel'],
                        bevel=.004, parent=counter)
        panel.rotation_euler[1] = math.pi/2-theta
        if i % 3 == 0:
            strip = ctx.box('Hypercone_Collar_Inlay_%02d' % i,
                            (.163*math.cos(theta), .006, .163*math.sin(theta)),
                            (.022, .004, .002), m['accent'],
                            bevel=.0005, parent=counter)
            strip.rotation_euler[1] = math.pi/2-theta
    tip = ctx.empty('Hypercone_Telescoping_Cutting_Point', (0, .392, 0))
    _sleeve(ctx, 'Hypercone_Cutting_Point_Guide', 0, .028, .034, .025,
            .006, m['recess'], tip, z=0, vertices=12, bevel=.0016)
    n = 16
    verts = [(r*math.cos(i*math.tau/n), y, r*math.sin(i*math.tau/n))
             for y, r in ((.015, .033), (.092, .0015)) for i in range(n)]
    ctx.mesh('Hypercone_Lance_Cutting_Point', verts,
             [tuple(reversed(range(n))), tuple(range(n, 2*n))]
             + [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)],
             m['edge'], bevel=.002, parent=tip)
    for side in (-1, 1):
        ctx.box('Hypercone_Longitudinal_Torque_Rail_%s' % side,
                (side*.080, -.115, -.059), (.026, .246, .021),
                m['frame'], bevel=.004)
        for j in range(4):
            ctx.box('Hypercone_Torque_Rail_Bolt_%s_%d' % (side, j),
                    (side*.095, -.210+j*.061, -.058), (.007, .012, .010),
                    m['edge'], bevel=.0014)
    for frame, amount in ((1, 0), (20, 1), (40, 0)):
        rotor.rotation_euler[1] = math.radians(110)*amount
        counter.rotation_euler[1] = -math.radians(38)*amount
        tip.location.y = .392-.030*amount
        rotor.keyframe_insert(data_path='rotation_euler', frame=frame)
        counter.keyframe_insert(data_path='rotation_euler', frame=frame)
        tip.keyframe_insert(data_path='location', index=1, frame=frame)
    rotor.rotation_euler[1] = counter.rotation_euler[1] = 0
    tip.location.y = .392
    return {'muzzle': (0, .484, 0),
            'moving_parts': [rotor.name, counter.name, tip.name],
            'design_name': 'Telescoping Drilling Lance',
            'hero_direction': (1.5, -.55, .75),
            'silhouette_direction': (1, 0, .08),
            'description': 'Long drilling lance with rear transverse bracing '
                           'and secondary side hold, three coaxial tapered '
                           'sleeves, helical cutting vanes, segmented counter '
                           'collar, and a telescoping faceted cutting point.'}


def build_insert(ctx, insert):
    for child in list(insert.children):
        bpy.data.objects.remove(child, do_unlink=True)
    carrier = ctx.box('Temporary hypercone carrier', (0, 0, 0),
                      (.028, .028, .004), ctx.m['accent'], bevel=0)
    insert.data = carrier.data
    bpy.data.objects.remove(carrier, do_unlink=True)
    insert['shape_representation'] = 'solid hypercone'
    insert['shape_solid'] = 'hypercone'
    for layer, (za, zb, ra, rb) in enumerate(((.003, .012, .012, .0087),
                                           (.010, .020, .009, .0052),
                                           (.018, .027, .0055, .0008))):
        n = 16
        verts = [(r*math.cos(i*math.tau/n), r*math.sin(i*math.tau/n), z)
                 for z, r in ((za, ra), (zb, rb)) for i in range(n)]
        ctx.mesh('HyperconeInsert_Layer_%d' % layer, verts,
                 [tuple(reversed(range(n))), tuple(range(n, 2*n))]
                 + [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)],
                 ctx.m['accent' if layer != 1 else 'steel'],
                 bevel=.0004, parent=insert)
    for i, (z, r) in enumerate(((.005, .012), (.013, .0082), (.021, .0044))):
        ctx.ring('HyperconeInsert_Layer_Collar_%d' % i, (0, 0, z),
                 r+.0004, r-.0004, .001, ctx.m['recess'], vertices=16,
                 bevel=.0001, parent=insert)
