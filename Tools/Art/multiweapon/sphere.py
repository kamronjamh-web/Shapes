"""Nova Pulse upper: captured radial petals around a faceted sphere chamber."""

import math
from mathutils import Vector


def build(ctx):
    m = ctx.m
    center = (0, .105, .125)
    radius = .064

    def point(r, theta, phi):
        return (r * math.sin(theta) * math.cos(phi),
                r * math.sin(theta) * math.sin(phi), r * math.cos(theta))

    def patch(name, theta0, theta1, phi0, phi1, outer, inner, mat, parent,
              rows=10, columns=10, bevel=.00065):
        """A closed spherical shell patch, with explicit inner and edge faces."""
        verts = []
        stride = columns + 1
        layer = (rows + 1) * stride
        for r in (outer, inner):
            for j in range(rows + 1):
                theta = theta0 + (theta1 - theta0) * j / rows
                for i in range(columns + 1):
                    phi = phi0 + (phi1 - phi0) * i / columns
                    verts.append(point(r, theta, phi))
        faces = []
        for j in range(rows):
            for i in range(columns):
                a = j * stride + i
                b, c, d = a + 1, a + stride + 1, a + stride
                faces += [(a, b, c, d), (layer + d, layer + c, layer + b, layer + a)]
        border = list(range(stride))
        border += [j * stride + columns for j in range(1, rows + 1)]
        border += [rows * stride + i for i in range(columns - 1, -1, -1)]
        border += [j * stride for j in range(rows - 1, 0, -1)]
        for i, a in enumerate(border):
            b = border[(i + 1) % len(border)]
            faces.append((a, layer + a, layer + b, b))
        return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)

    chamber = ctx.empty('Sphere_Chamber_Center', center)
    # Deliberate polygon facets remain flat; no smoothing hides the panel form.
    n, lat = 40, 20
    verts = [(0, 0, .056)]
    for j in range(1, lat):
        verts += [point(.056, math.pi * j / lat, math.tau * i / n) for i in range(n)]
    verts.append((0, 0, -.056))
    bottom = len(verts) - 1
    faces = [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)]
    for j in range(lat - 2):
        a = 1 + j * n
        faces += [(a + i, a + n + i, a + n + (i + 1) % n, a + (i + 1) % n)
                  for i in range(n)]
    faces += [(bottom, 1 + (lat - 2) * n + (i + 1) % n,
               1 + (lat - 2) * n + i) for i in range(n)]
    ctx.mesh('Sphere_Dark_Inner_Core', verts, faces, m['recess'], bevel=0, parent=chamber)
    patch('Sphere_Equatorial_Pulse_Band', 1.492, 1.650, 0, math.tau,
          .059, .0565, m['accent'], chamber, rows=2, columns=48, bevel=.00025)

    # Radial guide shafts stay fixed to the core. Sliding collars travel with
    # the petals, so every open shell panel stays mechanically captured.
    moving = []
    for half in range(2):
        theta0, theta1 = ((.155, 1.485) if half == 0 else (1.657, math.pi - .155))
        for quadrant in range(4):
            phi0 = quadrant * math.pi / 2 + .027
            phi1 = (quadrant + 1) * math.pi / 2 - .027
            phi = (phi0 + phi1) / 2
            theta = (theta0 + theta1) / 2
            direction = Vector(point(1, theta, phi))
            key = '%s_%02d' % ('Upper' if half == 0 else 'Lower', quadrant)
            pivot = ctx.empty('Sphere_Petal_Pivot_' + key, parent=chamber)
            moving.append(pivot.name)
            patch('Sphere_Armor_Petal_' + key, theta0, theta1, phi0, phi1,
                  radius, .060, m['steel'], pivot)
            shaft = ctx.cylinder('Sphere_Captured_Guide_' + key, tuple(direction * .052),
                                 .0021, .014, m['edge'], vertices=12, bevel=.00025,
                                 parent=chamber)
            shaft.rotation_euler = direction.to_track_quat('Z', 'Y').to_euler()
            collar = ctx.cylinder('Sphere_Sliding_Collar_' + key, tuple(direction * .055),
                                  .0044, .014, m['frame'], vertices=12, bevel=.0005,
                                  parent=pivot)
            collar.rotation_euler = direction.to_track_quat('Z', 'Y').to_euler()
            for frame, spread in ((1, 0), (10, 0), (20, .008), (22, .008), (40, 0)):
                pivot.location = direction * spread
                pivot.keyframe_insert(data_path='location', frame=frame)
            pivot.location = (0, 0, 0)

    # Small fixed polar plugs close the tip of each segmented hemisphere.
    ctx.cylinder('Sphere_Upper_Polar_Post', (0, 0, .059), .005, .012,
                 m['frame'], vertices=12, bevel=.0005, parent=chamber)
    ctx.cylinder('Sphere_Top_Polar_Plug', (0, 0, .0635), .0086, .004,
                 m['frame'], vertices=16, bevel=.0008, parent=chamber)
    ctx.cylinder('Sphere_Top_Polar_Index', (0, 0, .066), .0044, .0015,
                 m['edge'], vertices=16, bevel=.0004, parent=chamber)
    ctx.cylinder('Sphere_Lower_Polar_Socket', (0, 0, -.062), .008, .008,
                 m['frame'], vertices=16, bevel=.0008, parent=chamber)

    # Warm graphite squared hardware visually anchors the spherical mechanism.
    ctx.box('Sphere_Rear_Attachment', (0, -.001, .048), (.046, .046, .025),
            m['steel'], bevel=.002)
    ctx.box('Sphere_Cradle_Spine', (0, .065, .043), (.049, .158, .018),
            m['frame'], bevel=.0025)
    ctx.box('Sphere_Squared_Cradle_Floor', (0, .112, .043), (.093, .096, .018),
            m['frame'], bevel=.003)
    ctx.box('Sphere_Cradle_Core_Seat', (0, .105, .049), (.030, .034, .008),
            m['steel'], bevel=.0015)
    ctx.cylinder('Sphere_Fixed_Polar_Support_Post', (0, .105, .0585), .007, .013,
                 m['steel'], vertices=16, bevel=.0006)
    for side in (-1, 1):
        ctx.box('Sphere_Cradle_Rail_' + str(side), (side * .047, .112, .048),
                (.011, .100, .012), m['frame'], bevel=.002)
        ctx.box('Sphere_Cradle_Rail_Inset_' + str(side), (side * .0527, .110, .047),
                (.001, .074, .004), m['recess'], bevel=.0004)
        ctx.box('Sphere_Cradle_Accent_' + str(side), (side * .0533, .110, .050),
                (.0008, .058, .0013), m['accent'], bevel=.0002)
        ctx.box('Sphere_Rear_Connector_Trim_' + str(side), (side * .024, -.001, .047),
                (.003, .039, .012), m['edge'], bevel=.0006)
        for y in (.074, .149):
            ctx.cylinder('Sphere_Cradle_Fastener_%s_%s' % (side, y),
                         (side * .0532, y, .049), .0027, .0015, m['steel'], axis='X',
                         vertices=12, bevel=.00035)

    # The pulse aperture sits flush at the forward pole of the sphere. It is
    # a compact nozzle, not a projecting barrel; the spherical silhouette wins.
    ctx.cylinder('Sphere_Forward_Aperture_Seat', (0, .165, .125), .020, .013,
                 m['frame'], axis='Y', vertices=24, bevel=.0013)
    ctx.ring('Sphere_Forward_Aperture_Armor', (0, .172, .125), .018, .0138, .006,
             m['steel'], axis='Y', vertices=24, bevel=.0009)
    ctx.cylinder('Sphere_Forward_Aperture_Recess', (0, .173, .125), .0136, .002,
                 m['recess'], axis='Y', vertices=24, bevel=.0004)
    ctx.ring('Sphere_Forward_Pulse_Lens', (0, .1745, .125), .011, .0087, .001,
             m['accent'], axis='Y', vertices=24, bevel=.00025)
    ctx.cylinder('Sphere_Forward_Emitter_Core', (0, .1748, .125), .0065, .001,
                 m['ink'], axis='Y', vertices=16, bevel=.0002)

    return {'muzzle': (0, .176, .125), 'moving_parts': moving,
            'description': 'Nova Pulse cannon: faceted sphere chamber with eight captured radial armor petals, an equatorial pulse band, and a squared support cradle.'}
