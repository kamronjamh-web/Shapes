"""Standalone hollow electrical gauntlet with a five-node simplex lattice."""
import math
import bpy
from mathutils import Vector


def _beam(ctx, name, a, b, width, mat, parent=None):
    a, b = Vector(a), Vector(b)
    obj = ctx.box(name, (a+b)/2, (width, width, (b-a).length), mat,
                  bevel=width*.18, parent=parent)
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return obj


def _plate(ctx, name, pts, mat, parent=None, thickness=.003):
    pts = [Vector(p) for p in pts]
    n = (pts[1]-pts[0]).cross(pts[2]-pts[0]).normalized()
    verts = [p+n*s*thickness/2 for s in (-1, 1) for p in pts]
    return ctx.mesh(name, verts, [(2,1,0),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],
                    mat, bevel=thickness*.22, parent=parent)


def _points():
    return [(-.067,.065,.071),(.067,.065,.071),(0,.192,.071),
            (0,.107,.199),(0,.111,.119)]


FULL_BODY = True


def build(ctx):
    m=ctx.m
    # A true hollow armored gauntlet: +Y is the direction of the absent hand.
    ctx.ring('Pentachoron_HollowForearmShell',(0,-.078,0),.058,.046,.132,m['steel'],axis='Y',vertices=24,bevel=.0015)
    for y in (-.148,-.009):
        ctx.ring('Pentachoron_CuffRim_'+str(y),(0,y,0),.062,.045,.010,m['frame'],axis='Y',vertices=24,bevel=.001)
        ctx.ring('Pentachoron_InnerCuffGasket_'+str(y),(0,y+.002,0),.048,.043,.008,m['recess'],axis='Y',vertices=24,bevel=.0007)
    ctx.box('Pentachoron_DorsalControlPlate',(0,-.080,.059),(.059,.114,.011),m['steel'],.002)
    ctx.box('Pentachoron_DorsalGasket',(0,-.080,.055),(.064,.120,.005),m['recess'],.001)
    ctx.shape_dock((0,-.082,.068))
    for side in (-1,1):
        for i,y in enumerate((-.136,-.109,-.082,-.055,-.028)):
            ctx.modulator_socket(i+1+(5 if side==-1 else 0),(side*.061,y,0),rotation=(0,side*math.pi/2,0))
        ctx.box('Pentachoron_CuffRaritySeam_'+str(side),(side*.032,-.077,.050),(.002,.109,.002),m['accent'],.0003)
    points=[(-.071,.034,.028),(.071,.034,.028),(-.050,.144,.035),(.050,.144,.035),(0,.094,.096)]
    # Four finger-side capacitor knuckles and one dorsal terminal form all K5 links.
    for i,p in enumerate(points):
        anchor=(p[0]*.62,-.005,.030 if i<4 else .052)
        _beam(ctx,'Pentachoron_ArticulatedWristLink_'+str(i),anchor,p,.010,m['steel'])
        ctx.cylinder('Pentachoron_WristLinkBearing_'+str(i),anchor,.008,.015,m['edge'],axis='X',vertices=12,bevel=.0007)
    for i in range(5):
        for j in range(i+1,5):
            _beam(ctx,'Pentachoron_ConductionLattice_%s_%s'%(i,j),points[i],points[j],.004,m['edge'])
            a,b=Vector(points[i]),Vector(points[j])
            for t in (.16,.84):
                collar=ctx.box('Pentachoron_LatticeCollar_%s_%s_%s'%(i,j,t),a.lerp(b,t),(.007,.007,.003),m['recess'],.0004)
                collar.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    moving=[]
    for i,p in enumerate(points):
        node=ctx.empty('Pentachoron_ArticulatedCapacitor_'+str(i),p)
        moving.append(node.name)
        ctx.box('Pentachoron_NodeHousing_'+str(i),(0,0,0),(.027,.030,.026),m['steel'],.002,parent=node)
        ctx.box('Pentachoron_NodeBlackGasket_'+str(i),(0,0,.014),(.024,.027,.003),m['recess'],.0008,parent=node)
        _plate(ctx,'Pentachoron_TriangleCapacitor_'+str(i),[(-.011,-.010,.017),(.011,-.010,.017),(0,.012,.017)],m['frame'],node,.003)
        _plate(ctx,'Pentachoron_TriangleDielectric_'+str(i),[(-.006,-.006,.019),(.006,-.006,.019),(0,.007,.019)],m['ivory'],node,.001)
        ctx.box('Pentachoron_ArcTerminal_'+str(i),(0,.016,0),(.009,.002,.010),m['accent'],.0006,parent=node)
        for frame,amount in ((1,0),(12,.6),(20,1),(28,.6),(40,0)):
            node.rotation_euler.x=amount*(.16 if i%2 else -.16)
            node.keyframe_insert(data_path='rotation_euler',frame=frame)
        node.rotation_euler.x=0
    return {'muzzle':(0,.163,.052),'moving_parts':moving,
            'design_name':'Pentachoron Electrical Gauntlet','hero_direction':(.8,-.65,.55),
            'silhouette_direction':(1,.15,.25),
            'description':'Electrical forearm gauntlet with an open hollow wrist cuff, five articulated triangular capacitor knuckles and all ten conduction paths of a projected pentachoron surrounding the hand space. No handgun body.'}


def build_insert(ctx, insert):
    carrier=ctx.box('Pentachoron_TemporaryCarrier',(0,0,0),(.029,.029,.004),ctx.m['accent'],0)
    insert.data=carrier.data
    bpy.data.objects.remove(carrier,do_unlink=True)
    for child in list(insert.children_recursive): bpy.data.objects.remove(child,do_unlink=True)
    ctx.box('PentachoronInsert_CarrierInset',(0,0,.0023),(.024,.024,.0008),ctx.m['ink'],.0003,parent=insert)
    pts=[(-.011,-.009,.005),(.011,-.009,.005),(0,.012,.005),(0,0,.026),(0,0,.013)]
    for i in range(5):
        node=ctx.box('PentachoronInsert_Node_%s'%i,pts[i],(.0025,.0025,.0025),ctx.m['accent'],.0003,parent=insert)
        node['shape_solid']='pentachoron'
        for j in range(i+1,5): _beam(ctx,'PentachoronInsert_Edge_%s_%s'%(i,j),pts[i],pts[j],.0009,ctx.m['steel'],insert)
    insert['shape_representation']='five-vertex ten-edge simplex solid'
