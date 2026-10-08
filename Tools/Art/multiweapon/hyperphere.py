"""Three-star dimensional mortar: nested spherical field cage on elevation trunnions."""
import math
import bpy
from mathutils import Vector


def _tube(ctx,name,points,radius,mat,parent=None,sides=10):
    verts=[]
    for i,p in enumerate(points):
        p=Vector(p)
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        tangent.normalize()
        guide=Vector((1,0,0)) if abs(tangent.x)<.8 else Vector((0,1,0))
        u=tangent.cross(guide).normalized(); v=tangent.cross(u).normalized()
        verts += [tuple(p+radius*(u*math.cos(math.tau*j/sides)+v*math.sin(math.tau*j/sides))) for j in range(sides)]
    faces=[tuple(reversed(range(sides))),tuple(range((len(points)-1)*sides,len(points)*sides))]
    for i in range(len(points)-1):
        for j in range(sides):
            a=i*sides+j;b=i*sides+(j+1)%sides
            faces.append((a,b,b+sides,a+sides))
    return ctx.mesh(name,verts,faces,mat,.0003,parent=parent)


FULL_BODY = True


def _orb_panel(ctx,name,a0,a1,parent):
    rows,cols=12,10
    verts=[]
    for r in (.105,.098):
        for j in range(rows+1):
            t=.54+(math.pi-1.03)*j/rows
            for i in range(cols+1):
                a=a0+(a1-a0)*i/cols
                verts.append((r*math.sin(t)*math.cos(a),r*math.sin(t)*math.sin(a),r*math.cos(t)))
    s=cols+1;layer=(rows+1)*s;faces=[]
    for j in range(rows):
        for i in range(cols):
            a=j*s+i;b=a+1;c=b+s;d=a+s
            faces += [(a,b,c,d),(layer+d,layer+c,layer+b,layer+a)]
    border=list(range(s))+[j*s+cols for j in range(1,rows+1)]
    border += [rows*s+i for i in range(cols-1,-1,-1)]+[j*s for j in range(rows-1,0,-1)]
    for i,a in enumerate(border):
        b=border[(i+1)%len(border)];faces.append((a,layer+a,layer+b,b))
    return ctx.mesh(name,verts,faces,ctx.m['steel'],.0012,parent=parent)


def _open_gimbal(ctx,name,outer,inner,depth,axis,mat,parent,bevel):
    """Closed-section horseshoe ring with a clear northern launch corridor."""
    n=58;cut=.67;verts=[]
    for axial in (-depth/2,depth/2):
        for r in (outer,inner):
            for j in range(n+1):
                a=cut+(math.tau-2*cut)*j/n
                verts.append((r*math.sin(a),axial,r*math.cos(a)) if axis=='Y' else
                             (axial,r*math.sin(a),r*math.cos(a)))
    s=n+1;faces=[]
    for j in range(n):
        faces += [(j,j+1,s+j+1,s+j), (2*s+j,3*s+j,3*s+j+1,2*s+j+1),
                  (j,2*s+j,2*s+j+1,j+1),(s+j,s+j+1,3*s+j+1,3*s+j)]
    faces += [(0,s,3*s,2*s),(n,2*s+n,3*s+n,s+n)]
    return ctx.mesh(name,verts,faces,mat,bevel,parent=parent)


def build(ctx):
    m=ctx.m
    # Curved transverse saddle straddles the shoulder. Its underside is an
    # actual open arch, distinct from a slab or a borrowed pistol receiver.
    saddle=[]
    for j in range(25):
        a=math.pi*j/24
        saddle.append((.155*math.cos(a),-.008,.030+.060*math.sin(a)))
    _tube(ctx,'Hyperphere | curved shoulder saddle',saddle,.024,m['frame'],sides=12)
    _tube(ctx,'Hyperphere | rear shoulder cushion',[(x,-.082,z-.006) for x,y,z in saddle],.022,m['grip'],sides=12)
    for side in (-1,1):
        ctx.box('Hyperphere | saddle longitudinal rail %d'%side,(side*.132,-.018,.052),(.040,.174,.041),m['steel'],.005)
        ctx.box('Hyperphere | trunnion tower %d'%side,(side*.153,.085,.131),(.029,.068,.171),m['frame'],.004)
        ctx.cylinder('Hyperphere | trunnion housing %d'%side,(side*.155,.085,.212),.030,.030,m['steel'],axis='X',vertices=32,bevel=.002)
        ctx.ring('Hyperphere | bearing seal %d'%side,(side*.172,.085,.212),.022,.017,.004,m['recess'],axis='X',vertices=24)
        ctx.ring('Hyperphere | bearing index %d'%side,(side*.175,.085,.212),.018,.016,.002,m['accent'],axis='X',vertices=24,bevel=.0003)
        handle=[(side*.151,-.055,.139),(side*.208,-.066,.151),(side*.224,-.025,.140),
                (side*.224,.024,.098),(side*.196,.035,.075),(side*.149,.025,.078)]
        _tube(ctx,'Hyperphere | offset grab handle %d'%side,handle,.012,m['grip'],sides=12)
        for y,z in ((-.055,.139),(.025,.078)):
            ctx.cylinder('Hyperphere | handle bond %s %s'%(side,z),(side*.154,y,z),.021,.020,m['steel'],axis='X',vertices=20,bevel=.001)
        ctx.box('Hyperphere | bonded control rail %d'%side,(side*.167,.032,.147),(.015,.194,.043),m['steel'],.0025)
        for i,y in enumerate((-.040,-.004,.032,.068,.104)):
            ctx.modulator_socket(i+1 if side>0 else i+6,(side*.178,y,.147),rotation=(0,side*math.pi/2,0))
    ctx.box('Hyperphere | aft dock spine',(0,-.104,.115),(.085,.103,.045),m['frame'],.004)
    ctx.box('Hyperphere | dock bridge',(0,-.056,.112),(.106,.072,.035),m['steel'],.003)
    ctx.shape_dock((0,-.113,.140))
    elevation=ctx.empty('Hyperphere_Shoulder_Elevation_Pivot',(0,.085,.212))
    for side in (-1,1):
        ctx.cylinder('Hyperphere | captured globe axle %d'%side,(side*.123,0,0),.016,.080,m['edge'],axis='X',vertices=24,bevel=.001,parent=elevation)
    ctx.sphere('Hyperphere | dark suspended globe',(0,0,0),.094,m['frame'],40,20,parent=elevation)
    for q in range(6):
        a=math.tau*q/6
        _orb_panel(ctx,'Hyperphere | spherical armor sector %d'%q,a+.045,a+math.tau/6-.045,elevation)
    gimbals=[]
    for i,(r,axis,tilt) in enumerate(((.123,'Z',.34),(.141,'Y',-.30),(.160,'X',0))):
        g=ctx.empty('Hyperphere_Containment_Gimbal_%d'%i,parent=elevation)
        gimbals.append(g)
        if axis=='Z':
            ring=ctx.ring('Hyperphere | rotating containment ring %d'%i,(0,0,0),r,r-.010,.014,m['steel'],axis=axis,vertices=64,bevel=.0015,parent=g)
        else:
            ring=_open_gimbal(ctx,'Hyperphere | rotating open containment ring %d'%i,r,r-.010,.014,axis,m['steel'],g,.0015)
        ring.rotation_euler.z+=tilt
        if axis=='Z':
            seal=ctx.ring('Hyperphere | ring inner field seal %d'%i,(0,0,0),r-.0105,r-.013,.006,m['recess'],axis=axis,vertices=64,bevel=.0005,parent=g)
        else:
            seal=_open_gimbal(ctx,'Hyperphere | open ring inner field seal %d'%i,r-.0105,r-.013,.006,axis,m['recess'],g,.0005)
        seal.rotation_euler.z+=tilt
        if i==0:
            ctx.ring('Hyperphere | restrained field equator',(0,0,0),r+.0005,r-.0015,.0015,m['accent'],vertices=64,bevel=.0002,parent=g)
    ctx.cylinder('Hyperphere | launch polar coupling',(0,0,.092),.054,.023,m['frame'],vertices=40,bevel=.002,parent=elevation)
    ctx.ring('Hyperphere | launch iris housing',(0,0,.112),.058,.043,.020,m['edge'],vertices=40,bevel=.0018,parent=elevation)
    ctx.cylinder('Hyperphere | launch chamber darkness',(0,0,.117),.0425,.004,m['recess'],vertices=40,parent=elevation)
    ctx.ring('Hyperphere | launch field index',(0,0,.124),.041,.0385,.002,m['accent'],vertices=40,bevel=.0003,parent=elevation)
    iris=ctx.empty('Hyperphere_Launch_Iris_Pivot',(0,0,.125),parent=elevation)
    for i in range(6):
        a=math.tau*i/6
        leaf=ctx.box('Hyperphere | radial iris petal %d'%i,(.029*math.cos(a),.029*math.sin(a),0),(.026,.013,.003),m['steel'],.001,parent=iris)
        leaf.rotation_euler.z=a
    for frame,amount in ((1,0),(20,1),(40,0)):
        elevation.rotation_euler.x=-.28-amount*.17;elevation.keyframe_insert(data_path='rotation_euler',frame=frame)
        iris.rotation_euler.z=amount*.26;iris.keyframe_insert(data_path='rotation_euler',frame=frame)
        for i,g in enumerate(gimbals):
            g.rotation_euler=(amount*.11*(i==0),amount*.17*(i==1),amount*.13*(i==2))
            g.keyframe_insert(data_path='rotation_euler',frame=frame)
    elevation.rotation_euler.x=-.28;iris.rotation_euler.z=0
    for g in gimbals:g.rotation_euler=(0,0,0)
    return {'muzzle':(0,.120,.334),'moving_parts':[elevation.name,iris.name]+[g.name for g in gimbals],
            'description':'Shoulder-mounted dimensional mortar: armored spherical globe in rotating containment gimbals, launch iris, curved shoulder saddle and offset grab handles.',
            'design_name':'Hyperphere Shoulder Mortar','hero_direction':(.85,.75,.54),'silhouette_direction':(1,.14,.10)}

def build_minishape(ctx,insert):
    obj=ctx.sphere('HyperphereInsert | inner sphere',(0,0,.014),.008,ctx.m['frame'],20,10,parent=insert)
    obj['shape_solid']='hyperphere'
    for axis in ('X','Y','Z'):
        ctx.ring('HyperphereInsert | nested meridian '+axis,(0,0,.014),.012,.0108,.0011,ctx.m['steel'],axis=axis,vertices=32,bevel=.0001,parent=insert)
    ctx.ring('HyperphereInsert | dimensional equator',(0,0,.014),.0086,.0079,.001,ctx.m['accent'],vertices=24,bevel=.0001,parent=insert)


def build_insert(ctx,insert):
    carrier=ctx.box('Temporary Hyperphere carrier',(0,0,0),(.029,.029,.004),ctx.m['accent'],0)
    insert.data=carrier.data;bpy.data.objects.remove(carrier,do_unlink=True)
    for child in list(insert.children): bpy.data.objects.remove(child,do_unlink=True)
    insert['shape_representation']='solid hyperphere'
    ctx.box('HyperphereInsert | inset',(0,0,.0022),(.024,.024,.0006),ctx.m['ink'],.0003,parent=insert)
    build_minishape(ctx,insert)
