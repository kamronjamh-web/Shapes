"""Triangle: three tapered accelerator rails around a triangular beam throat."""
import math
import bpy


def build(ctx):
    m = ctx.m
    moving = []

    def prism(name, section, y0, y1, mat, bevel=.0007, parent=None,
              end=None):
        end = section if end is None else end
        n = len(section)
        verts = [(x, y0, z) for x, z in section]
        verts += [(x, y1, z) for x, z in end]
        faces = [tuple(reversed(range(n))), tuple(range(n, n * 2))]
        faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n)
                  for i in range(n)]
        return ctx.mesh(name, verts, faces, mat, bevel=bevel, parent=parent)

    def triangular_ring(name, outer, inner, y0, y1, mat, bevel=.0005):
        verts = [(x, y, z) for y in (y0, y1)
                 for loop in (outer, inner) for x, z in loop]
        faces = []
        for i in range(3):
            j = (i + 1) % 3
            faces.extend([(i, j, j + 6, i + 6),
                          (i + 3, i + 9, j + 9, j + 3),
                          (i, i + 3, j + 3, j),
                          (i + 6, j + 6, j + 9, i + 9)])
        return ctx.mesh(name, verts, faces, mat, bevel=bevel)

    # Broad machined transition from the shared pistol chassis to the rail cradle.
    prism('TRI_Breech', [(-.024, .035), (.024, .035), (.028, .076),
                        (.014, .101), (-.014, .101), (-.028, .076)],
          -.023, .067, m['steel'], .0018,
          end=[(-.021, .035), (.021, .035), (.026, .077),
               (.010, .106), (-.010, .106), (-.026, .077)])
    ctx.box('TRI_ReceiverJoint', (0, .025, .036), (.047, .093, .006),
            m['frame'], bevel=.001)
    ctx.box('TRI_BreechTopInsert', (0, .021, .102), (.012, .061, .003),
            m['recess'], bevel=.0006)
    for side in (-1, 1):
        x = side * .027
        ctx.box('TRI_BreechPanel_%s' % side, (x, .018, .070),
                (.0022, .062, .024), m['recess'], bevel=.001)
        ctx.box('TRI_BreechAccent_%s' % side, (x + side * .0013, .018, .079),
                (.001, .047, .0018), m['accent'], bevel=.00035)
        for j in range(3):
            vent = ctx.box('TRI_BreechVent_%s_%s' % (side, j),
                           (x + side * .0013, .000 + j * .009, .067),
                           (.001, .003, .012), m['ink'], bevel=.0003)
            vent.rotation_euler.x = -.20
        for y in (-.006, .046):
            ctx.cylinder('TRI_BreechFastener_%s_%s' % (side, y),
                         (side * .029, y, .083), .0021, .0011,
                         m['edge'], axis='X', vertices=8, bevel=.0003)

    # All three sections are solid polygonal rails, with an open centre between them.
    sections = [
        [(-.004, .119), (-.013, .098), (-.008, .086),
         (.008, .086), (.013, .098), (.004, .119)],
        [(-.034, .046), (-.018, .046), (-.008, .062),
         (-.016, .080), (-.030, .072)],
        [(.034, .046), (.030, .072), (.016, .080),
         (.008, .062), (.018, .046)],
    ]
    roots = []
    for i, section in enumerate(sections):
        root = ctx.empty('TRI_AcceleratorRail_%s' % i)
        roots.append(root)
        moving.append(root.name)
        tapered = [(x * .83, .074 + (z - .074) * .86)
                   for x, z in section]
        prism('TRI_RailHousing_%s' % i, section, .063, .284,
              m['steel'], .0012, root, tapered)

    # Wide inlaid channels read as intentional accelerator inserts, not surface wires.
    prism('TRI_TopRailInset', [(-.0035, .1178), (.0035, .1178),
                             (.0035, .119), (-.0035, .119)],
          .080, .266, m['recess'], .0003, roots[0],
          [(-.0029, .1125), (.0029, .1125),
           (.0029, .1137), (-.0029, .1137)])
    prism('TRI_TopRailCharge', [(-.001, .119), (.001, .119),
                              (.001, .1195), (-.001, .1195)],
          .086, .260, m['accent'], .00015, roots[0],
          [(-.00085, .114), (.00085, .114),
           (.00085, .1145), (-.00085, .1145)])
    for side, root in ((-1, roots[1]), (1, roots[2])):
        prism('TRI_SideRailInset_%s' % side,
              [(side * .0313, .056), (side * .0324, .056),
               (side * .0309, .067), (side * .0298, .067)],
              .080, .254, m['recess'], .00035, root,
              [(side * .0269, .058), (side * .0280, .058),
               (side * .0265, .068), (side * .0254, .068)])
        prism('TRI_SideRailCharge_%s' % side,
              [(side * .0319, .060), (side * .0323, .060),
               (side * .0320, .062), (side * .0316, .062)],
              .087, .247, m['accent'], .00015, root,
              [(side * .0278, .062), (side * .0282, .062),
               (side * .0279, .064), (side * .0275, .064)])
        for y in (.093, .236):
            ctx.box('TRI_RailInsetEnd_%s_%s' % (side, y),
                    (side * .0297, y, .061), (.002, .005, .010),
                    m['edge'], bevel=.0004, parent=root)

    # Dark central throat is deliberately narrow, leaving daylight around the rails.
    prism('TRI_BeamCore', [(-.006, .066), (.006, .066),
                          (.005, .078), (-.005, .078)],
          .064, .279, m['recess'], .0005)
    ctx.box('TRI_CoreExciter', (0, .177, .079), (.002, .183, .0013),
            m['accent'], bevel=.0002)

    # Front frame links the three rails into a unmistakably triangular muzzle.
    outer = [(0, .116), (-.034, .044), (.034, .044)]
    inner = [(0, .091), (-.014, .060), (.014, .060)]
    triangular_ring('TRI_ForwardCollar', outer, inner, .275, .290,
                    m['frame'], .0013)
    face_outer = [(0, .113), (-.031, .046), (.031, .046)]
    face_inner = [(0, .093), (-.015, .058), (.015, .058)]
    triangular_ring('TRI_MuzzleFace', face_outer, face_inner, .289, .295,
                    m['steel'], .0008)
    lip_outer = [(0, .095), (-.0165, .057), (.0165, .057)]
    lip_inner = [(0, .090), (-.0135, .061), (.0135, .061)]
    triangular_ring('TRI_MuzzleLuminousLip', lip_outer, lip_inner,
                    .295, .296, m['accent'], .00025)
    triangular_ring('TRI_ThroatLiner', face_inner,
                    [(0, .088), (-.012, .062), (.012, .062)],
                    .271, .2935, m['ink'], .0003)
    for side in (-1, 1):
        ctx.box('TRI_CollarLock_%s' % side, (side * .029, .282, .053),
                (.006, .009, .008), m['edge'], bevel=.0008)

    # One open midspan brace and a small ergonomic support balance the long nose.
    triangular_ring('TRI_MidspanBrace',
                    [(0, .114), (-.032, .045), (.032, .045)],
                    [(0, .102), (-.023, .051), (.023, .051)],
                    .166, .173, m['steel'], .0007)
    ctx.box('TRI_LowerCradle', (0, .143, .035), (.034, .059, .012),
            m['frame'], bevel=.0017)
    prism('TRI_AngledSupport', [(-.012, .037), (.012, .037),
                              (.011, -.028), (-.011, -.028)],
          .145, .180, m['frame'], .002,
          end=[(-.011, .035), (.011, .035), (.010, -.022), (-.010, -.022)])
    for side in (-1, 1):
        ctx.box('TRI_SupportPad_%s' % side,
                (side * .012, .161, .005), (.002, .023, .038),
                m['grip'], bevel=.001)
    ctx.box('TRI_SupportHeel', (0, .159, -.028), (.024, .035, .005),
            m['steel'], bevel=.001)
    ctx.box('TRI_RearSightBase', (0, -.012, .105), (.017, .009, .007),
            m['steel'], bevel=.0007)
    ctx.box('TRI_RearSightNotch', (0, -.012, .109), (.005, .010, .003),
            m['recess'], bevel=.0003)
    ctx.box('TRI_FrontSight', (0, .280, .117), (.005, .008, .007),
            m['steel'], bevel=.0006)

    # Charge closes the rail clearance by 1.4 mm, then releases and recoils.
    for i, root in enumerate(roots):
        delta = ((0, 0, -.0014), (.0014, 0, .0006),
                 (-.0014, 0, .0006))[i]
        for frame, factor, recoil in ((1, 0, 0), (9, 1, 0),
                                      (12, 1, 0), (14, 0, -.006),
                                      (21, 0, 0), (40, 0, 0)):
            root.location = (delta[0] * factor, recoil,
                             delta[2] * factor)
            root.keyframe_insert(data_path='location', frame=frame)
        root.location = (0, 0, 0)

    return {'muzzle': (0, .296, .073), 'moving_parts': moving,
            'description': 'Triangle beam carbine with three tapered polygonal '
                           'accelerator rails, an open triangular throat, and '
                           'a restrained charge-convergence animation.'}
