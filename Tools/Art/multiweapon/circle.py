"""Circle upper: armored flywheel magazine with an exposed disc launch mouth."""

import math


def build(ctx):
    m = ctx.m
    cy = 0.100

    def prism(name, outline, bottom, top, mat, bevel=0.001, parent=None):
        n = len(outline)
        verts = [(x, y, bottom) for x, y in outline]
        verts += [(x, y, top) for x, y in outline]
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
        return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)

    def clipped_circle(radius, front_y, segments=64):
        """Round rear and sides ending at a precise straight front mouth."""
        cut_angle = math.asin((front_y - cy) / radius)
        return [(radius * math.cos(a), cy + radius * math.sin(a))
                for a in [math.pi - cut_angle + (math.pi + 2 * cut_angle) * i / segments
                          for i in range(segments + 1)]]

    # A load-bearing neck runs forward from the common shape dock, then widens
    # into the lower pan. Nothing intersects the shared modulator sockets.
    ctx.box('Circle_Neck_Recess', (0, 0.020, 0.041), (.042, .088, .014), m['recess'], bevel=.0012)
    ctx.box('Circle_Neck_Armor', (0, -.003, .053), (.049, .042, .018), m['steel'], bevel=.002)
    for side in (-1, 1):
        ctx.box('Circle_Neck_Frame_' + str(side), (side * .0235, .017, .042),
                (.004, .080, .011), m['frame'], bevel=.0008)
        ctx.box('Circle_Neck_Accent_' + str(side), (side * .0257, .015, .052),
                (.0009, .065, .0014), m['accent'], bevel=.0003)

    lower = clipped_circle(.083, .159)
    upper = clipped_circle(.080, .141)
    prism('Circle_Lower_Armored_Pan', lower, .037, .052, m['frame'], .0022)
    prism('Circle_Lower_Dark_Liner', clipped_circle(.080, .157), .0515, .055,
          m['recess'], .0008)
    prism('Circle_Upper_Armor', upper, .069, .089, m['steel'], .0024)
    prism('Circle_Upper_Seam', clipped_circle(.0785, .1395), .0668, .0702,
          m['recess'], .0006)

    # The round top has a broad stepped circular hatch, bearing cap, and a
    # single narrow rarity band, following the Square's material hierarchy.
    ctx.cylinder('Circle_Motor_Hatch_Recess', (0, .085, .0898), .036, .0028,
                 m['recess'], vertices=64, bevel=.0006)
    ctx.ring('Circle_Motor_Hatch_Bezel', (0, .085, .0915), .0345, .0308, .003,
             m['frame'], vertices=64, bevel=.0007)
    ctx.cylinder('Circle_Motor_Hatch', (0, .085, .092), .0305, .0035,
                 m['steel'], vertices=64, bevel=.001)
    ctx.ring('Circle_Hatch_Rarity_Line', (0, .085, .0941), .0243, .0232, .0007,
             m['accent'], vertices=64, bevel=.0002)
    ctx.cylinder('Circle_Hatch_Center', (0, .085, .095), .014, .003,
                 m['edge'], vertices=48, bevel=.0006)
    ctx.box('Circle_Hatch_Lock', (0, .085, .097), (.017, .004, .0018),
            m['recess'], bevel=.0005)
    for x in (-.024, .024):
        ctx.cylinder('Circle_Hatch_Fastener_' + str(x), (x, .085, .0945),
                     .0022, .0012, m['edge'], vertices=12, bevel=.0003)

    # Blade teeth are part of one watertight annular mesh, mounted on a named
    # pivot. The forward crescent is exposed above the lower launch shelf.
    pivot = ctx.empty('Circle_Blade_Spin_Pivot', (0, cy, .061))
    count = 32
    outline = []
    for tooth in range(count):
        for phase, radius in ((0.00, .073), (.20, .079), (.48, .079), (.85, .073)):
            a = 2 * math.pi * (tooth + phase) / count
            outline.append((radius * math.cos(a), radius * math.sin(a)))
    n = len(outline)
    verts = []
    for z in (-.0027, .0027):
        verts += [(x, y, z) for x, y in outline]
        verts += [(.014 * math.cos(2 * math.pi * i / n),
                   .014 * math.sin(2 * math.pi * i / n), z) for i in range(n)]
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces += [(i, j, n + j, n + i),
                  (2 * n + i, 3 * n + i, 3 * n + j, 2 * n + j),
                  (i, 2 * n + i, 2 * n + j, j),
                  (n + i, n + j, 3 * n + j, 3 * n + i)]
    ctx.mesh('Circle_Toothed_Silver_Disc', verts, faces, m['edge'],
             bevel=.00035, parent=pivot)
    ctx.ring('Circle_Blade_Inset', (0, 0, .0029), .062, .057, .00055,
             m['steel'], vertices=64, bevel=.0001, parent=pivot)
    for i in range(6):
        a = 2 * math.pi * i / 6
        pts = []
        for radius, ang in ((.036, a - .04), (.055, a + .10),
                            (.054, a + .145), (.035, a + .005)):
            pts.append((radius * math.cos(ang), radius * math.sin(ang)))
        prism('Circle_Blade_Expansion_Slot_%02d' % i, pts, .00275, .00315,
              m['recess'], .0001, pivot)
    ctx.cylinder('Circle_Bearing_Axle', (0, cy, .060), .013, .037,
                 m['steel'], vertices=48, bevel=.0008)

    # A horizontal open feed mouth clearly shows the silver cutting disc.
    # Paired cheek armor grows from the pan and roof, leaving the blade's
    # entire swept plane open between the lower and upper pieces.
    for side in (-1, 1):
        x = side * .066
        ctx.box('Circle_Mouth_Cheek_Lower_' + str(side), (x, .139, .0534),
                (.013, .019, .0068), m['steel'], bevel=.0008)
        ctx.box('Circle_Mouth_Cheek_Upper_' + str(side), (x, .139, .0676),
                (.013, .019, .0048), m['steel'], bevel=.0007)
        ctx.box('Circle_Mouth_Guide_' + str(side), (side * .056, .145, .056),
                (.005, .024, .002), m['edge'], bevel=.0005)
        ctx.box('Circle_Mouth_Lower_Accent_' + str(side), (side * .050, .155, .040),
                (.020, .001, .0014), m['accent'], bevel=.0002)
    ctx.box('Circle_Feed_Mouth_Lower_Lip', (0, .159, .044), (.099, .008, .011),
            m['frame'], bevel=.0016)
    ctx.box('Circle_Feed_Mouth_Upper_Lip', (0, .1405, .0795), (.099, .006, .015),
            m['steel'], bevel=.0012)
    ctx.box('Circle_Feed_Mouth_Upper_Dark_Edge', (0, .144, .074),
            (.092, .0012, .003), m['recess'], bevel=.0003)

    # Minimal, paired rearward exhaust details stay inside the top silhouette.
    for side in (-1, 1):
        for i in range(3):
            x = side * (.044 + .005 * i)
            ctx.box('Circle_Rear_Vent_%s_%s' % (side, i), (x, .059, .0897),
                    (.002, .015, .0015), m['recess'], bevel=.0005)
        ctx.box('Circle_Side_Index_' + str(side), (side * .080, .097, .078),
                (.001, .010, .004), m['accent'], bevel=.0003)
    ctx.box('Circle_Neck_Rear_Sight', (0, -.010, .065), (.016, .007, .007),
            m['steel'], bevel=.0008)
    ctx.box('Circle_Neck_Sight_Inlay', (0, -.014, .067), (.008, .001, .0018),
            m['ivory'], bevel=.0002)

    pivot.rotation_mode = 'XYZ'
    pivot.rotation_euler.z = 0
    pivot.keyframe_insert(data_path='rotation_euler', frame=1)
    pivot.rotation_euler.z = math.tau * 3
    pivot.keyframe_insert(data_path='rotation_euler', frame=40)
    # Restore the authored rest pose regardless of caller's scene frame.
    pivot.rotation_euler.z = 0
    return {'muzzle': (0, .168, .061),
            'moving_parts': ['Circle_Blade_Spin_Pivot'],
            'description': 'Wide armored circular disc launcher; exposed silver toothed flywheel in an open forward feed mouth.'}
