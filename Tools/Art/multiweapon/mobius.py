"""Wearable half-twist forearm ribbon with travelling temporal emitters."""
import math
import bpy
from mathutils import Vector


def _ribbon(ctx,name,center,radius,width,thick,mat,parent=None,n=72,k=6):
    def point(u,v):
        return Vector(((radius+v*math.cos(u/2))*math.cos(u),
                       v*math.sin(u/2),(radius+v*math.cos(u/2))*math.sin(u)))
    vertices=[]
    for side in (-1,1):
        for i in range(n):
            u=math.tau*i/n
            for j in range(k+1):
                v=width*(j/k-.5)
                tangent=point(u+.0001,v)-point(u-.0001,v)
                cross=point(u,v+.0001)-point(u,v-.0001)
                normal=tangent.cross(cross).normalized()
                vertices.append(Vector(center)+point(u,v)+normal*side*thick/2)
    stride=n*(k+1)
    def ix(side,i,j):
        if i==n: return (1-side)*stride+(k-j)
        return side*stride+i*(k+1)+j
    faces=[]
    for i in range(n):
        for j in range(k):
            for side in (0,1):
                face=(ix(side,i,j),ix(side,i+1,j),ix(side,i+1,j+1),ix(side,i,j+1))
                faces.append(face if side else tuple(reversed(face)))
        for j in (0,k): faces.append((ix(0,i,j),ix(1,i,j),ix(1,i+1,j),ix(0,i+1,j)))
    obj=ctx.mesh(name,vertices,faces,mat,bevel=min(.0007,thick*.18),parent=parent)
    for poly in obj.data.polygons: poly.use_smooth=True
    return obj


FULL_BODY = True


def build(ctx):
    m=ctx.m
    ribbon=ctx.empty('Mobius_ForearmHalfTwistRibbon')
    _ribbon(ctx,'Mobius_BroadContinuousForearmBand',(0,0,0),.083,.060,.005,m['steel'],ribbon,n=96,k=10)
    _ribbon(ctx,'Mobius_BondedGraphiteInnerWall',(0,0,0),.083,.046,.0059,m['recess'],ribbon,n=96,k=8)
    _ribbon(ctx,'Mobius_TwistedCeramicLaminate',(0,0,0),.083,.030,.0065,m['frame'],ribbon,n=96,k=6)
    _ribbon(ctx,'Mobius_ContinuousTemporalSeam',(0,0,0),.083,.002,.0072,m['accent'],ribbon,n=96,k=2)
    # These bonded housings remain outside the open forearm channel.
    ctx.box('Mobius_DorsalDockBridge',(0,-.052,.095),(.062,.152,.012),m['steel'],.002)
    ctx.box('Mobius_DorsalIsolationLayer',(0,-.052,.090),(.066,.155,.004),m['recess'],.0008)
    ctx.shape_dock((0,-.071,.105))
    for side in (-1,1):
        ctx.box('Mobius_BondedSideControlPanel_'+str(side),(side*.096,-.025,0),(.010,.150,.037),m['steel'],.002)
        ctx.box('Mobius_SidePanelGasket_'+str(side),(side*.094,-.025,0),(.012,.154,.041),m['recess'],.001)
        for i,y in enumerate((-.079,-.052,-.025,.002,.029)):
            ctx.modulator_socket(i+1+(5 if side==-1 else 0),(side*.105,y,0),rotation=(0,side*math.pi/2,0))
        for y in (-.097,.047):
            ctx.cylinder('Mobius_CapturedBandBearing_%s_%s'%(side,y),(side*.096,y,0),.007,.014,m['edge'],axis='X',vertices=12,bevel=.0007)
        ctx.box('Mobius_DelayedPulseTrace_'+str(side),(side*.030,-.047,.100),(.002,.098,.0015),m['accent'],.00025)
    def head_pose(u):
        p=Vector((.083*math.cos(u),0,.083*math.sin(u)))
        normal=Vector((-math.cos(u)*math.sin(u/2),math.cos(u/2),-math.sin(u)*math.sin(u/2)))
        return p+normal*.012,normal
    moving=[]
    for i,u0 in enumerate((.38,math.pi+.38)):
        p,n=head_pose(u0)
        head=ctx.empty('Mobius_TravellingTemporalEmitter_'+str(i),p)
        head.rotation_euler=n.to_track_quat('Z','Y').to_euler()
        moving.append(head.name)
        ctx.box('Mobius_EmitterSlidingShoe_'+str(i),(0,0,-.011),(.033,.041,.007),m['recess'],.001,parent=head)
        ctx.box('Mobius_EmitterArmoredBody_'+str(i),(0,0,0),(.028,.036,.021),m['steel'],.002,parent=head)
        ctx.box('Mobius_EmitterRearCeramicJacket_'+str(i),(0,-.012,.002),(.033,.014,.025),m['frame'],.0013,parent=head)
        ctx.ring('Mobius_EmitterFocusCollar_'+str(i),(0,.004,.014),.011,.007,.006,m['edge'],vertices=16,bevel=.0006,parent=head)
        ctx.cylinder('Mobius_EmitterDarkPulseLens_'+str(i),(0,.004,.017),.0068,.002,m['ink'],vertices=16,bevel=.0003,parent=head)
        ctx.ring('Mobius_TemporalPulseTrace_'+str(i),(0,.004,.0185),.0056,.0035,.001,m['accent'],vertices=16,bevel=.0002,parent=head)
        for side in (-1,1):
            ctx.box('Mobius_EmitterCapturedRail_%s_%s'%(i,side),(side*.016,0,0),(.004,.034,.021),m['edge'],.0006,parent=head)
        ghost=ctx.empty('Mobius_DelayedGhostPulse_'+str(i),parent=head)
        moving.append(ghost.name)
        ctx.ring('Mobius_GhostPulseCollar_'+str(i),(0,.004,.022),.008,.006,.0015,m['frame'],vertices=16,bevel=.0003,parent=ghost)
        ctx.ring('Mobius_GhostPulseSeam_'+str(i),(0,.004,.023),.0068,.006,.0007,m['accent'],vertices=16,bevel=.00015,parent=ghost)
        for frame,amount in ((1,0),(12,.65 if i==0 else .15),(20,1 if i==0 else .75),(28,.25 if i==0 else 1),(40,0)):
            p,n=head_pose(u0+amount*.28)
            head.location=p
            head.rotation_euler=n.to_track_quat('Z','Y').to_euler()
            head.keyframe_insert(data_path='location',frame=frame)
            head.keyframe_insert(data_path='rotation_euler',frame=frame)
            ghost.location.z=amount*.022
            ghost.scale=(1+amount*.3,)*3
            ghost.keyframe_insert(data_path='location',frame=frame)
            ghost.keyframe_insert(data_path='scale',frame=frame)
        head.location,normal=head_pose(u0)
        head.rotation_euler=normal.to_track_quat('Z','Y').to_euler()
        ghost.location=(0,0,0);ghost.scale=(1,1,1)
    return {'muzzle':(.080,.065,.030),'moving_parts':moving,
            'design_name':'Mobius Forearm Echo Band','hero_direction':(.7,-.8,.55),
            'silhouette_direction':(.25,1,.25),
            'description':'Wearable broad continuous metallic half-twist ribbon enclosing an open forearm channel. Two captured temporal emitters travel along the twisted surface; a primary pulse is followed by a delayed expanding ghost pulse. Bonded control panels carry the dock and ten modulator bays. No pistol chassis.'}


def build_insert(ctx,insert):
    carrier=ctx.box('Mobius_TemporaryCarrier',(0,0,0),(.029,.029,.004),ctx.m['accent'],0)
    insert.data=carrier.data
    bpy.data.objects.remove(carrier,do_unlink=True)
    for child in list(insert.children_recursive): bpy.data.objects.remove(child,do_unlink=True)
    ctx.box('MobiusInsert_CarrierInset',(0,0,.0023),(.024,.024,.0008),ctx.m['ink'],.0003,parent=insert)
    obj=_ribbon(ctx,'MobiusInsert_HalfTwistSolid',(0,0,.015),.009,.007,.0009,ctx.m['steel'],insert,48,4)
    obj['shape_solid']='mobius'
    _ribbon(ctx,'MobiusInsert_ChargeSeam',(0,0,.015),.009,.001,.0013,ctx.m['accent'],insert,48,2)
    insert['shape_representation']='continuous half-twist ribbon solid'
