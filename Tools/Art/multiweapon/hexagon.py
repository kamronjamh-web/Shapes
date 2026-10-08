"""Hexagonal indexed mine drum: all geometry belongs to the upper assembly."""
import math


def build(ctx):
    m = ctx.m
    center = (0.0, 0.090, 0.097)
    pivot = ctx.empty('Hexagon_Drum_Index_Pivot', center)

    def hex_solid(name, loc, radius, depth, mat, parent=None, bevel=0.001):
        verts = [(loc[0] + radius * math.cos(i * math.pi / 3),
                  loc[1] + sign * depth / 2,
                  loc[2] + radius * math.sin(i * math.pi / 3))
                 for sign in (-1, 1) for i in range(6)]
        faces = [tuple(reversed(range(6))), tuple(range(6, 12))]
        faces += [(i, (i + 1) % 6, (i + 1) % 6 + 6, i + 6)
                  for i in range(6)]
        return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)

    def hex_ring(name, loc, outer, inner, depth, mat, parent=None, bevel=0.0008):
        verts = [(loc[0] + radius * math.cos(i * math.pi / 3),
                  loc[1] + sign * depth / 2,
                  loc[2] + radius * math.sin(i * math.pi / 3))
                 for sign in (-1, 1) for radius in (outer, inner)
                 for i in range(6)]
        faces = []
        for i in range(6):
            j = (i + 1) % 6
            faces.extend([(i, j, j + 6, i + 6),
                          (i + 12, i + 18, j + 18, j + 12),
                          (i, i + 12, j + 12, j),
                          (i + 6, j + 6, j + 18, i + 18)])
        return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)

    # A slim saddle clears all five side module bays on each receiver side.
    ctx.box('Hexagon_Cradle_Foot', (0, .089, .0305), (.039, .124, .006),
            m['frame'], bevel=.002)
    ctx.box('Hexagon_Cradle_Saddle', (0, .024, .052), (.039, .014, .038),
            m['frame'], bevel=.0025)
    for side in (-1, 1):
        ctx.box('Hexagon_Cradle_Cheek_' + str(side),
                (side * .016, .157, .048), (.007, .018, .029),
                m['frame'], bevel=.0015)

    # Rear coupling begins beyond the shared shape socket shell.
    hex_ring('Hexagon_Rear_Coupling', (0, .030, .097), .044, .030,
             .015, m['steel'], bevel=.0018)
    hex_solid('Hexagon_Rear_Axle', (0, .044, .097), .024, .024,
              m['recess'], bevel=.001)
    hex_solid('Hexagon_Drum_Core', (0, 0, 0), .060, .106,
              m['steel'], parent=pivot, bevel=.002)
    for end, yy in [('Rear', -.050), ('Front', .050)]:
        hex_ring('Hexagon_Drum_' + end + '_Band', (0, yy, 0),
                 .063, .048, .008, m['edge'], parent=pivot, bevel=.0013)
        hex_ring('Hexagon_Drum_' + end + '_Gasket', (0, yy * 1.10, 0),
                 .058, .037, .0025, m['recess'], parent=pivot,
                 bevel=.0004)

    # Six large armored flats are separated by exposed metallic corner spines.
    # Local Z is the outward normal; Y follows the bore axis.
    for i in range(6):
        theta = (i + .5) * math.pi / 3
        normal = (math.cos(theta), math.sin(theta))
        rotation = math.pi / 2 - theta

        def flat(name, tangent, yy, radius, size, mat, bevel):
            loc = (normal[0] * radius + normal[1] * tangent,
                   yy, normal[1] * radius - normal[0] * tangent)
            obj = ctx.box('Hexagon_Flat_%02d_%s' % (i + 1, name), loc,
                          size, mat, bevel=bevel, parent=pivot)
            obj.rotation_euler[1] = rotation
            return obj

        flat('Armor', 0, 0, .0537, (.053, .082, .004), m['steel'], .0014)
        flat('Inset', 0, .001, .0560, (.033, .047, .0014),
             m['recess'], .0007)
        flat('Index_Light', -.011, .004, .0570, (.003, .026, .0012),
             m['accent'], .00035)
        flat('Latch', .009, -.008, .0572, (.009, .019, .002),
             m['edge'], .00065)
        for yy in (-.031, .031):
            flat('Bolt_' + str(yy), .017, yy, .0568, (.004, .004, .0018),
                 m['edge'], .00065)

    # Fixed short throat makes the drum silhouette dominant.
    hex_ring('Hexagon_Forward_Housing', (0, .155, .097), .042, .027,
             .027, m['steel'], bevel=.002)
    hex_ring('Hexagon_Muzzle_Gasket', (0, .175, .097), .035, .025,
             .012, m['recess'], bevel=.0008)
    hex_ring('Hexagon_Muzzle_Collar', (0, .194, .097), .035, .024,
             .029, m['steel'], bevel=.002)
    hex_ring('Hexagon_Muzzle_Lip', (0, .212, .097), .036, .024,
             .008, m['edge'], bevel=.001)
    hex_ring('Hexagon_Inner_Throat', (0, .198, .097), .024, .0218,
             .026, m['recess'], bevel=.0004)

    # This independently parented shallow mine can be detached by downstream rigs.
    mine = ctx.empty('Hexagon_Detachable_Mine_Puck', (0, .207, .097))
    hex_solid('Hexagon_Mine_Shell', (0, 0, 0), .021, .010,
              m['frame'], parent=mine, bevel=.001)
    hex_solid('Hexagon_Mine_Face', (0, .0055, 0), .0178, .002,
              m['steel'], parent=mine, bevel=.0008)
    hex_ring('Hexagon_Mine_Arming_Trace', (0, .0068, 0), .0105, .009,
             .0009, m['accent'], parent=mine, bevel=.00025)
    hex_solid('Hexagon_Mine_Center', (0, .007, 0), .005, .001,
              m['recess'], parent=mine, bevel=.0004)

    # Mechanical indexing only; no projectile movement or firing simulation.
    pivot.rotation_mode = 'XYZ'
    pivot.rotation_euler[1] = 0.0
    pivot.keyframe_insert(data_path='rotation_euler', index=1, frame=1)
    pivot.rotation_euler[1] = math.pi / 3
    pivot.keyframe_insert(data_path='rotation_euler', index=1, frame=40)
    pivot.rotation_euler[1] = 0.0
    if pivot.animation_data and pivot.animation_data.action:
        try:
            for curve in pivot.animation_data.action.fcurves:
                for key in curve.keyframe_points:
                    key.interpolation = 'BEZIER'
        except AttributeError:
            pass  # Blender's layered actions retain their default eased keys.

    return {'muzzle': (0.0, .216, .097),
            'moving_parts': [pivot.name, mine.name],
            'description': 'Six-flat armored hexagonal mine drum with an indexed '
                           'rotor, gray saddle, recessed indicators, and detachable '
                           'hexagonal mine puck in a short faceted throat.'}
