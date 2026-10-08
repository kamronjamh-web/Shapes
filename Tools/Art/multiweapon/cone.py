"""Two-star Flame Cone projector: split tapered heat jacket and pilot igniter."""
import math
import bpy


def _sector(ctx, name, start, end, parent, mat):
    n = 12
    verts = []
    for y, radius in ((.052, .020), (.197, .047), (.052, .016), (.197, .042)):
        verts += [(radius * math.cos(start + (end-start)*i/n), y,
                   .101 + radius * math.sin(start + (end-start)*i/n)) for i in range(n+1)]
    s = n+1
    faces = []
    for i in range(n):
        faces += [(i,i+1,s+i+1,s+i), (2*s+i,3*s+i,3*s+i+1,2*s+i+1),
                  (i,2*s+i,2*s+i+1,i+1), (s+i,s+i+1,3*s+i+1,3*s+i)]
    faces += [(0,s,3*s,2*s),(n,2*s+n,3*s+n,s+n)]
    return ctx.mesh(name, verts, faces, mat, .001, parent=parent)


def build(ctx):
    m = ctx.m
    ctx.box('Cone | receiver bridge', (0,.034,.046), (.045,.107,.026), m['frame'], .002)
    ctx.box('Cone | heat shield saddle', (0,.103,.052), (.054,.089,.016), m['steel'], .0015)
    ctx.cylinder('Cone | mixing chamber', (0,.035,.101), .023,.064,m['steel'],axis='Y',vertices=24,bevel=.0014)
    ctx.ring('Cone | rear seal', (0,.018,.101), .024,.021,.008,m['frame'],axis='Y',vertices=24)
    ctx.box('Cone | supported riser', (0,.041,.071), (.035,.036,.044),m['frame'],.0018)
    ctx.cylinder('Cone | black inner diffuser', (0,.143,.101), .024,.104,m['recess'],axis='Y',vertices=24)
    ctx.ring('Cone | hot mouth', (0,.194,.101), .037,.027,.007,m['edge'],axis='Y',vertices=32,bevel=.0008)
    moving=[]
    for i in range(3):
        a = math.tau*i/3
        pivot=ctx.empty('Cone_Split_Shield_Pivot_%d'%i)
        moving.append(pivot.name)
        _sector(ctx,'Cone | tapered shield %d'%i,a+.035,a+math.tau/3-.035,pivot,m['steel'])
        radial=(math.cos(a+math.pi/3),math.sin(a+math.pi/3))
        for frame,travel in ((1,0),(20,.0035),(40,0)):
            pivot.location=(radial[0]*travel,0,radial[1]*travel)
            pivot.keyframe_insert(data_path='location',frame=frame)
        pivot.location=(0,0,0)
    for i in range(6):
        a=math.tau*i/6
        x,z=.028*math.cos(a),.101+.028*math.sin(a)
        fin=ctx.box('Cone | heat fin %d'%i,(x,.105,z),(.004,.079,.011),m['frame'],.0008)
        fin.rotation_euler.y=-a+math.pi/2
    for side in (-1,1):
        ctx.box('Cone | narrow rarity seam %d'%side,(side*.0268,.098,.053),(.001,.068,.0015),m['accent'],.0002)
        ctx.cylinder('Cone | saddle pin %d'%side,(side*.029,.061,.065),.004,.005,m['edge'],axis='X',vertices=12)
    ctx.cylinder('Cone | pilot feed', (0,.158,.069),.005,.067,m['frame'],axis='Y',vertices=12)
    ctx.ring('Cone | pilot shroud', (0,.194,.069),.007,.004,.009,m['steel'],axis='Y',vertices=16)
    ctx.cylinder('Cone | pilot contact', (0,.199,.069),.003,.002,m['accent'],axis='Y',vertices=12)
    ctx.box('Cone | fuel selector',(0,.010,.128),(.014,.026,.009),m['frame'],.001)
    return {'muzzle':(0,.204,.101),'moving_parts':moving,
            'description':'Two-star Flame Cone projector with three spreading conical heat shields, cooling fins, and supported pilot igniter.'}


def build_minishape(ctx, insert):
    n=24
    verts=[(.011*math.cos(math.tau*i/n),.011*math.sin(math.tau*i/n),.004) for i in range(n)]
    verts.append((0,0,.027))
    faces=[tuple(reversed(range(n)))]+[(i,(i+1)%n,n) for i in range(n)]
    obj=ctx.mesh('ConeInsert | solid cone',verts,faces,ctx.m['steel'],.00025,parent=insert)
    obj['shape_solid']='cone'
    ctx.ring('ConeInsert | base seam',(0,0,.0048),.0113,.0105,.001,ctx.m['accent'],vertices=24,bevel=.0001,parent=insert)


def build_insert(ctx, insert):
    carrier=ctx.box('Temporary Cone carrier',(0,0,0),(.029,.029,.004),ctx.m['accent'],0)
    insert.data=carrier.data
    bpy.data.objects.remove(carrier,do_unlink=True)
    for child in list(insert.children): bpy.data.objects.remove(child,do_unlink=True)
    insert['shape_representation']='solid cone'
    ctx.box('ConeInsert | inset',(0,0,.0022),(.024,.024,.0006),ctx.m['ink'],.0003,parent=insert)
    build_minishape(ctx,insert)
