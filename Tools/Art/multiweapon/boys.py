"""Floating articulated clone sculpture, following the canonical Boy immersion."""
import math
import bpy
from mathutils import Vector


def _point(u,v):
    # Same three-lobed immersion as LegacyShapeVisualAnimator.PopulateBoysSurfaceGrid
    # at its rest phase. The polar folds intersect instead of forming a sphere.
    lobe=1+.3*math.sin(3*v)*math.sin(u)
    x=math.sin(u)*math.cos(v)*lobe
    y=math.sin(u)*math.sin(v)*lobe
    z=math.cos(u)*math.cos(v)
    return Vector((x,y*math.cos(.3)-z*math.sin(.3),
                   y*math.sin(.3)+z*math.cos(.3)))


def _surface(ctx,name,center,scale,mat,parent=None,u0=.025,u1=math.pi-.025,
             v0=0,v1=math.tau,nu=48,nv=72,offset=0,thickness=.0015):
    verts=[]
    for i in range(nu+1):
        u=u0+(u1-u0)*i/nu
        for j in range(nv+1):
            v=v0+(v1-v0)*j/nv
            normal=(_point(u+.0001,v)-_point(u-.0001,v)).cross(
                _point(u,v+.0001)-_point(u,v-.0001)).normalized()
            verts.append(Vector(center)+_point(u,v)*scale+normal*offset)
    faces=[]
    for i in range(nu):
        for j in range(nv):
            a=i*(nv+1)+j
            faces.append((a,a+nv+1,a+nv+2,a+1))
    obj=ctx.mesh(name,verts,faces,mat,bevel=0,parent=parent)
    for face in obj.data.polygons: face.use_smooth=True
    solid=obj.modifiers.new('Editable mirror surface thickness','SOLIDIFY')
    solid.thickness=thickness
    solid.offset=0
    return obj


FULL_BODY = True


def build(ctx):
    m=ctx.m
    core=ctx.empty('Boys_FloatingImmersionCore')
    _surface(ctx,'Boys_ThreeLobedInterwovenGraphiteCore',(0,0,0),.105,m['recess'],core,nu=44,nv=66,thickness=.0022)
    # No ground support: mirror sectors, optics and controls are bonded to the
    # freely suspended sculpture itself, preserving its interwoven negative spaces.
    ctx.box('Boys_RearBondedDockPlate',(0,-.121,-.028),(.065,.013,.065),m['steel'],.002)
    ctx.box('Boys_RearDockIsolation',(0,-.126,-.028),(.069,.005,.069),m['recess'],.001)
    ctx.shape_dock((0,-.135,-.028),rotation=(math.pi/2,0,0))
    for side in (-1,1):
        ctx.box('Boys_BondedLateralControlPanel_'+str(side),(side*.121,0,-.009),(.015,.147,.035),m['steel'],.002)
        ctx.box('Boys_ControlPanelIsolator_'+str(side),(side*.120,0,-.009),(.018,.152,.040),m['recess'],.001)
        for i,y in enumerate((-.054,-.027,0,.027,.054)):
            ctx.modulator_socket(i+1+(5 if side==-1 else 0),(side*.132,y,-.009),rotation=(0,side*math.pi/2,0))
    moving=[]
    for i in range(3):
        theta=math.pi/6+i*math.tau/3
        petal=ctx.empty('Boys_ArticulatedInterwovenMirror_'+str(i),parent=core)
        moving.append(petal.name)
        _surface(ctx,'Boys_BroadMirrorImmersionSector_'+str(i),(0,0,0),.105,m['steel'],petal,
                 u0=.18,u1=math.pi-.18,v0=theta-.87,v1=theta+.87,nu=32,nv=26,offset=.003,thickness=.0022)
        _surface(ctx,'Boys_LayeredMirrorCeramicInset_'+str(i),(0,0,0),.105,m['frame'],petal,
                 u0=.65,u1=2.47,v0=theta-.55,v1=theta+.55,nu=24,nv=20,offset=.0045,thickness=.0012)
        for side in (-1,1):
            border=theta+side*.575
            _surface(ctx,'Boys_FittedMirrorContour_%s_%s'%(i,side),(0,0,0),.105,m['recess'],petal,
                     u0=.63,u1=2.49,v0=border-.018,v1=border+.018,nu=24,nv=2,offset=.005,thickness=.001)
            u,v=1.52,theta+side*.62
            p=_point(u,v)*.105
            n=(_point(u+.0001,v)-_point(u-.0001,v)).cross(_point(u,v+.0001)-_point(u,v-.0001)).normalized()
            tab=ctx.box('Boys_CaptiveMirrorHinge_%s_%s'%(i,side),p+n*.006,(.014,.020,.005),m['edge'],.001,parent=petal)
            tab.rotation_euler=n.to_track_quat('Z','Y').to_euler()
            stud=ctx.cylinder('Boys_MirrorHingeStud_%s_%s'%(i,side),p+n*.009,.0035,.002,m['recess'],vertices=8,bevel=.0004,parent=petal)
            stud.rotation_euler=tab.rotation_euler.copy()
        _surface(ctx,'Boys_MirrorRarityMeridian_'+str(i),(0,0,0),.105,m['accent'],petal,
                 u0=.92,u1=2.14,v0=theta-.014,v1=theta+.014,nu=24,nv=2,offset=.0056,thickness=.0009)
        for frame,amount in ((1,0),(12,.55),(20,1),(28,.65),(40,0)):
            petal.location=(.010*math.cos(theta)*amount,.007*amount,.010*math.sin(theta)*amount)
            petal.rotation_euler.y=(i-1)*.09*amount
            petal.keyframe_insert(data_path='location',frame=frame)
            petal.keyframe_insert(data_path='rotation_euler',frame=frame)
        petal.location=(0,0,0);petal.rotation_euler=(0,0,0)
        # Smaller canonical copies peel out of the parent mirror sectors; their
        # movement reads as shadow cloning rather than another weapon barrel.
        ghost=ctx.empty('Boys_PeelingShadowClone_'+str(i))
        moving.append(ghost.name)
        _surface(ctx,'Boys_ShadowCloneImmersedMirror_'+str(i),(0,0,0),.036,m['steel'],ghost,nu=22,nv=36,thickness=.0012)
        _surface(ctx,'Boys_ShadowCloneMeridian_'+str(i),(0,0,0),.036,m['accent'],ghost,
                 u0=.65,u1=2.4,v0=theta-.02,v1=theta+.02,nu=16,nv=2,offset=.001,thickness=.0005)
        for frame,amount in ((1,0),(12,.25),(20,1),(28,.75),(40,0)):
            radius=.055+.084*amount
            ghost.location=(radius*math.cos(theta),.028+.055*amount,radius*math.sin(theta))
            ghost.rotation_euler.y=.32*amount
            ghost.keyframe_insert(data_path='location',frame=frame)
            ghost.keyframe_insert(data_path='rotation_euler',frame=frame)
        ghost.location=(.055*math.cos(theta),.028,.055*math.sin(theta));ghost.rotation_euler=(0,0,0)
    ctx.cylinder('Boys_RecessedThreeWayOpticalCore',(0,.083,0),.031,.020,m['steel'],axis='Y',vertices=3,bevel=.002)
    ctx.cylinder('Boys_ThreeWayCoreIsolation',(0,.096,0),.024,.004,m['recess'],axis='Y',vertices=3,bevel=.001)
    ctx.cylinder('Boys_ThreeWayCloneTrace',(0,.099,0),.013,.001,m['accent'],axis='Y',vertices=3,bevel=.0005)
    for i,theta in enumerate((math.pi/2,math.pi*7/6,math.pi*11/6)):
        x,z=.075*math.cos(theta),.075*math.sin(theta)
        a,b=Vector((0,.083,0)),Vector((x,.116,z))
        arm=ctx.box('Boys_BondedOpticalConductionArm_'+str(i),(a+b)/2,(.011,.011,(b-a).length),m['steel'],.0015)
        arm.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
        head=ctx.empty('Boys_SculptureCloneOptic_'+str(i),(x,.116,z))
        ctx.box('Boys_OpticArmor_'+str(i),(0,0,0),(.032,.028,.032),m['steel'],.002,parent=head)
        ctx.box('Boys_OpticRearJacket_'+str(i),(0,-.012,0),(.037,.009,.037),m['frame'],.0015,parent=head)
        ctx.ring('Boys_PrecisionOpticalCollar_'+str(i),(0,.019,0),.015,.010,.010,m['edge'],axis='Y',vertices=16,bevel=.001,parent=head)
        ctx.ring('Boys_OpticalFocusSeal_'+str(i),(0,.025,0),.011,.0075,.002,m['recess'],axis='Y',vertices=16,bevel=.0003,parent=head)
        ctx.ring('Boys_CloneFocusLens_'+str(i),(0,.027,0),.008,.005,.001,m['accent'],axis='Y',vertices=16,bevel=.0002,parent=head)
    return {'muzzle':(0,.145,0),'moving_parts':moving,
            'design_name':'Boy Surface Floating Clone Sculpture','hero_direction':(.8,.7,.5),
            'silhouette_direction':(1,.2,.2),
            'description':'Freely floating sculpture of three interwoven metallic immersion surfaces. Articulated layered mirrors unfold and three smaller canonical ghost copies peel away from the parent form. Precision three-way optics and bonded controls follow the sculpture; no handgun, ground pedestal or wrist frame.'}


def build_insert(ctx,insert):
    carrier=ctx.box('Boys_TemporaryCarrier',(0,0,0),(.029,.029,.004),ctx.m['accent'],0)
    insert.data=carrier.data
    bpy.data.objects.remove(carrier,do_unlink=True)
    for child in list(insert.children_recursive): bpy.data.objects.remove(child,do_unlink=True)
    ctx.box('BoysInsert_CarrierInset',(0,0,.0023),(.024,.024,.0008),ctx.m['ink'],.0003,parent=insert)
    obj=_surface(ctx,'BoysInsert_CanonicalImmersedSolid',(0,0,.015),.0095,ctx.m['steel'],insert,
                 nu=24,nv=36,thickness=.00065)
    obj['shape_solid']='boys'
    for i in range(3):
        theta=math.pi/6+i*math.tau/3
        _surface(ctx,'BoysInsert_CloneMeridian_%s'%i,(0,0,.015),.0095,ctx.m['accent'],insert,
                 u0=.5,u1=2.7,v0=theta-.03,v1=theta+.03,nu=16,nv=2,offset=.0004,thickness=.0003)
    insert['shape_representation']='canonical three-lobed self-intersecting immersion solid'
