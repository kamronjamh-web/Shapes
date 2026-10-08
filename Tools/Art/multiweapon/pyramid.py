"""Four hinged pyramid armor faces enclosing a miniature seeker magazine."""
import math


def build(ctx):
    m = ctx.m
    cy, base_z = .095, .043
    panel_names = []

    def triangle_prism(name, points, thickness, normal, mat,
                       parent=None, bevel=.001):
        verts = [tuple(p[j] + sign * thickness * normal[j] / 2
                       for j in range(3))
                 for sign in (-1, 1) for p in points]
        return ctx.mesh(name, verts,
                        [(2, 1, 0), (3, 4, 5), (0, 1, 4, 3),
                         (1, 2, 5, 4), (2, 0, 3, 5)],
                        mat, bevel=bevel, parent=parent)

    def small_pyramid(name, loc, half, height, mat, parent=None):
        x, y, z = loc
        verts = [(x-half, y-half, z), (x+half, y-half, z),
                 (x+half, y+half, z), (x-half, y+half, z),
                 (x, y, z+height)]
        return ctx.mesh(name, verts,
                        [(3, 2, 1, 0), (0, 1, 4), (1, 2, 4),
                         (2, 3, 4), (3, 0, 4)],
                        mat, bevel=.0009, parent=parent)

    # Graphite tray floats just above the original receiver and its module bays.
    ctx.box('Pyramid_Receiver_Bridge', (0, cy, .0335), (.041, .107, .011),
            m['frame'], bevel=.0025)
    ctx.box('Pyramid_Lower_Cradle', (0, cy, .041), (.120, .120, .010),
            m['grip'], bevel=.003)
    ctx.box('Pyramid_Magazine_Deck', (0, cy, .047), (.102, .102, .005),
            m['recess'], bevel=.0015)
    for side in (-1, 1):
        ctx.box('Pyramid_Cradle_Trim_' + str(side),
                (side*.0602, cy, .041), (.0014, .092, .0020),
                m['edge'], bevel=.00045)

    # The fixed mast and lens fit in the small central opening at the apex.
    ctx.box('Pyramid_Targeting_Mast', (0, cy, .1075), (.008, .008, .123),
            m['recess'], bevel=.0007)
    ctx.box('Pyramid_Apex_Sensor_Block', (0, cy, .171), (.010, .010, .009),
            m['edge'], bevel=.0013)
    ctx.box('Pyramid_Apex_Lens_Recess', (0, cy+.0052, .171),
            (.0068, .0008, .0056), m['recess'], bevel=.0005)
    ctx.box('Pyramid_Apex_Targeting_Lens', (0, cy+.0058, .171),
            (.0044, .0007, .0036), m['accent'], bevel=.0005)

    # Each face has a local X hinge; its local +Y points out from the pyramid.
    # Layers follow the sloped plane instead of standing upright on its surface.
    points = [(-.0575, 0, 0), (.0575, 0, 0), (0, -.0555, .127)]
    ny = .127 / math.hypot(.127, .0555)
    nz = .0555 / math.hypot(.127, .0555)
    normal = (0, ny, nz)
    centroid = tuple(sum(p[j] for p in points)/3 for j in range(3))

    for i, theta in enumerate((0, math.pi/2, math.pi, 3*math.pi/2)):
        name = ('Right', 'Front', 'Left', 'Rear')[i]
        pivot = ctx.empty('Pyramid_' + name + '_Panel_Hinge',
                          (.060*math.cos(theta), cy+.060*math.sin(theta), base_z))
        pivot.rotation_euler[2] = theta-math.pi/2
        panel_names.append(pivot.name)

        def layer(suffix, scale, offset, thick, mat, bevel):
            pts = [tuple(centroid[j] + scale*(p[j]-centroid[j])
                         + offset*normal[j] for j in range(3)) for p in points]
            return triangle_prism('Pyramid_' + name + '_' + suffix, pts,
                                  thick, normal, mat, pivot, bevel)

        layer('Graphite_Seam_Back', 1, 0, .004, m['recess'], .0013)
        layer('Sloped_Armor', .948, .0034, .004, m['steel'], .002)
        layer('Recessed_Triangle', .63, .0055, .0014, m['recess'], .0011)
        layer('Inset_Armor', .53, .0065, .0017, m['steel'], .0012)

        # Broad exposed metal hinge shafts ground each moving face.
        ctx.cylinder('Pyramid_' + name + '_Hinge_Shaft', (0, 0, .0015),
                     .0042, .084, m['edge'], axis='X', vertices=12,
                     bevel=.00065, parent=pivot)

        def face_box(suffix, x, height, width, length, thick, mat, offset):
            # A box's local Z aligns to the face's outward surface normal.
            z = height
            yy = -.0555*(z/.127)
            obj = ctx.box('Pyramid_' + name + '_' + suffix,
                          (x, yy+normal[1]*offset, z+normal[2]*offset),
                          (width, length, thick), mat,
                          bevel=min(.0007, thick*.35), parent=pivot)
            obj.rotation_euler[0] = -math.atan2(ny, nz)
            return obj

        face_box('Rarity_Inlay', 0, .050, .022, .0020, .0011,
                 m['accent'], .0080)
        face_box('Release_Latch', 0, .024, .019, .009, .0032,
                 m['edge'], .0071)

        pivot.rotation_euler[0] = 0
        pivot.keyframe_insert(data_path='rotation_euler', index=0, frame=1)
        pivot.rotation_euler[0] = -math.radians(13)
        pivot.keyframe_insert(data_path='rotation_euler', index=0, frame=12)
        pivot.keyframe_insert(data_path='rotation_euler', index=0, frame=25)
        pivot.rotation_euler[0] = 0
        pivot.keyframe_insert(data_path='rotation_euler', index=0, frame=40)

    # Four stable seeker units surround the sensor mast. Their ceramic noses
    # and square sockets become visible through the opened triangular faces.
    magazine = ctx.empty('Pyramid_Internal_Seeker_Magazine', (0, cy, .049))
    for i, (x, y) in enumerate(((-.021, -.021), (.021, -.021),
                               (.021, .021), (-.021, .021))):
        seeker = ctx.empty('Pyramid_Seeker_%02d' % (i+1), (x, y, 0),
                           parent=magazine)
        ctx.box('Pyramid_Seeker_%02d_Socket' % (i+1), (0, 0, .003),
                (.032, .032, .006), m['edge'], bevel=.001,
                parent=seeker)
        small_pyramid('Pyramid_Seeker_%02d_Shell' % (i+1),
                      (0, 0, .006), .014, .059, m['steel'], seeker)
        small_pyramid('Pyramid_Seeker_%02d_Nose' % (i+1),
                      (0, 0, .048), .004, .017, m['ivory'], seeker)
    return {'muzzle': (0, cy, .1755),
            'moving_parts': panel_names,
            'description': 'Tall four-face seeker pyramid with sloped armored '
                           'insets, outward folding base hinges, a fixed apex '
                           'targeting lens, and four captive miniature seekers.'}
