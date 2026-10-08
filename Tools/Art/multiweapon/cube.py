"""Cube orbital-guard projector upper: armored deployer and four guard blocks."""
import math
import bpy


def build(ctx):
    m = ctx.m
    moving = []

    def box(name, loc, size, mat='steel', bevel=.001, parent=None):
        return ctx.box('CUBE_' + name, loc, size, m[mat], bevel=bevel,
                       parent=parent)

    def square_frame(name, y, outer, inner, depth, mat='steel', z=.103):
        # Four rails give the aperture a real hole and explicit corner joins.
        wall = (outer - inner) / 2
        span = (outer + inner) / 4
        for side in (-1, 1):
            box(name + '_Vertical_%s' % side, (side * span, y, z),
                (wall, depth, outer), mat, .0012)
            box(name + '_Horizontal_%s' % side, (0, y, z + side * span),
                (inner, depth, wall), mat, .0012)

    # A compact receiver bridge leaves the shared rear cartridge dock unobstructed.
    box('ReceiverBridge', (0, .015, .052), (.043, .069, .031), 'steel', .002)
    # Below the lower leaf's full deployment sweep (minimum underside .0338).
    box('SupportSaddle', (0, .071, .029), (.054, .088, .006), 'frame', .0012)
    box('LowerServiceSpine', (0, .104, .027), (.028, .102, .008), 'steel', .001)
    box('RearBulkhead', (0, .041, .103), (.102, .016, .106), 'steel', .0028)
    box('RearThermalInset', (0, .0319, .106), (.069, .002, .064), 'recess', .0015)
    box('RearPowerSeam', (0, .0307, .133), (.046, .0012, .002), 'accent', .0003)

    # Fixed internal corner struts define the cube even when its armor is deployed.
    for x in (-.045, .045):
        for z in (.058, .148):
            box('CornerStrut_%s_%s' % (x, z), (x, .099, z),
                (.009, .105, .009), 'edge', .0014)
    square_frame('FrontGasket', .153, .103, .076, .010, 'recess')
    square_frame('DeploymentCollar', .161, .106, .077, .010, 'frame')
    square_frame('InnerApertureLip', .1663, .081, .076, .002, 'steel')
    for side in (-1, 1):
        box('CollarChargeBar_%s' % side, (side * .046, .1666, .103),
            (.002, .001, .047), 'accent', .0003)

    # Hinged armor plates carry their inset panels and rarity marks with them.
    panels = []
    for side in (-1, 1):
        pivot = ctx.empty('CUBE_SidePanelHinge_%s' % side,
                          (side * .052, .048, .103))
        moving.append(pivot.name)
        panels.append((pivot, 'Z', -side * math.radians(32)))
        box('SideArmor_%s' % side, (0, .048, 0), (.008, .094, .087),
            'steel', .002, pivot)
        box('SideInset_%s' % side, (side * .0045, .049, 0),
            (.0018, .062, .060), 'recess', .0012, pivot)
        box('SideGrayPlate_%s' % side, (side * .0056, .049, .004),
            (.0014, .049, .040), 'frame', .001, pivot)
        box('SideRaritySeam_%s' % side, (side * .0065, .049, .017),
            (.0009, .036, .002), 'accent', .00025, pivot)
        ctx.cylinder('CUBE_HingePin_%s' % side,
                     (side * .052, .049, .103), .005, .092, m['edge'],
                     axis='Z', vertices=12, bevel=.0007)

    for top in (-1, 1):
        z = .103 + top * .052
        pivot = ctx.empty('CUBE_%sPanelHinge' % ('Top' if top == 1 else 'Bottom'),
                          (0, .048, z))
        moving.append(pivot.name)
        # The lower leaf has a short clearance-limited deployment angle.
        panels.append((pivot, 'X', math.radians(34 if top == 1 else -8)))
        box('HorizontalArmor_%s' % top, (0, .048, 0), (.087, .094, .008),
            'steel', .002, pivot)
        box('HorizontalInset_%s' % top, (0, .049, top * .0045),
            (.063, .065, .0018), 'recess', .0012, pivot)
        box('HorizontalGrayPlate_%s' % top, (0, .049, top * .0056),
            (.047, .047, .0014), 'frame', .001, pivot)
        box('HorizontalChargeSeam_%s' % top, (0, .049, top * .0065),
            (.035, .002, .0009), 'accent', .00025, pivot)

    # Four real cubic guards sit in a 2x2 launch matrix; their fronts are visible
    # in the recess at rest and extend through the aperture during deployment.
    guards = []
    for sx in (-1, 1):
        for sz in (-1, 1):
            guard = ctx.empty('CUBE_GuardBlock_%s_%s' % (sx, sz),
                              (sx * .0205, .142, .103 + sz * .0205))
            moving.append(guard.name)
            guards.append((guard, sx, sz))
            box('GuardArmor_%s_%s' % (sx, sz), (0, 0, 0),
                (.032, .032, .032), 'steel', .0024, guard)
            box('GuardFrontInset_%s_%s' % (sx, sz), (0, .0165, 0),
                (.021, .0018, .021), 'recess', .0011, guard)
            box('GuardCeramicFace_%s_%s' % (sx, sz), (0, .0175, 0),
                (.014, .001, .014), 'frame', .0007, guard)
            box('GuardStatus_%s_%s' % (sx, sz), (0, .0181, .004),
                (.009, .0008, .0016), 'accent', .00025, guard)

    # Dark shallow back plate and cross divider frame the four separate sockets.
    box('LaunchMatrixBack', (0, .112, .103), (.073, .004, .073),
        'recess', .0008)
    box('MatrixDividerVertical', (0, .135, .103), (.003, .041, .073),
        'ink', .0004)
    box('MatrixDividerHorizontal', (0, .135, .103), (.073, .041, .003),
        'ink', .0004)

    for pivot, axis, angle in panels:
        index = 0 if axis == 'X' else 2
        for frame, amount in ((1, 0), (8, 0), (15, 1), (25, 1), (34, 0), (40, 0)):
            pivot.rotation_euler[index] = angle * amount
            pivot.keyframe_insert(data_path='rotation_euler', frame=frame)
        pivot.rotation_euler[index] = 0
    for guard, sx, sz in guards:
        start = (sx * .0205, .142, .103 + sz * .0205)
        for frame, amount in ((1, 0), (12, 0), (20, 1), (25, 1), (36, 0), (40, 0)):
            guard.location = (start[0] + sx * .001 * amount,
                              start[1] + .034 * amount,
                              start[2] + sz * .001 * amount)
            guard.keyframe_insert(data_path='location', frame=frame)
        guard.location = start

    return {'muzzle': (0, .167, .103), 'moving_parts': moving,
            'description': 'Cube orbital-guard projector: a square armored '
                           'deployment cradle with four opening armor leaves '
                           'and a visible 2x2 matrix of beveled cubic guards. '
                           'The mechanism study opens the housing and extends '
                           'the guards before returning to the stowed pose.'}
