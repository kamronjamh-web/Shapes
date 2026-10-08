"""Four-star Wrath Aura containment vessel with a self-threading sculpted neck."""
import math
import bpy
from mathutils import Vector


def _tube(ctx,name,points,radius,mat,parent=None,sides=12,bevel=.0004):
    verts=[]
    # Parallel transported frame prevents abrupt turns in the looping conduit.
    last_u=None
    for i,p in enumerate(points):
        p=Vector(p)
        tangent=(Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])).normalized()
        if last_u is None:
            guide=Vector((1,0,0)) if abs(tangent.x)<.8 else Vector((0,1,0))
            u=tangent.cross(guide).normalized()
        else:
            u=(last_u-tangent*last_u.dot(tangent)).normalized()
        v=tangent.cross(u).normalized();last_u=u
        verts += [tuple(p+radius*(u*math.cos(math.tau*j/sides)+v*math.sin(math.tau*j/sides))) for j in range(sides)]
    faces=[tuple(reversed(range(sides))),tuple(range((len(points)-1)*sides,len(points)*sides))]
    for i in range(len(points)-1):
        for j in range(sides):
            a=i*sides+j;b=i*sides+(j+1)%sides
            faces.append((a,b,b+sides,a+sides))
    return ctx.mesh(name,verts,faces,mat,bevel,parent=parent)


def _curve(points,steps=6):
    """Catmull-Rom path with repeated endpoint controls."""
    result=[]
    controls=[points[0]]+points+[points[-1]]
    for i in range(1,len(controls)-2):
        a,b,c,d=[Vector(p) for p in controls[i-1:i+3]]
        for j in range(steps):
            t=j/steps
            result.append(tuple(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)))
    result.append(points[-1])
    return result


def _vessel(ctx,name,parent,scale=1,mini=False):
    # Closed thick-looking body with stepped waist; both contour and looping
    # neck carry the Klein identity even when rendered as a cartridge miniature.
    profile=[(.059,.014),(.064,.028),(.075,.039),(.094,.041),(.113,.036),(.129,.025),(.140,.013)]
    verts=[];n=32
    for z,r in profile:
        verts += [(scale*r*math.cos(math.tau*i/n),scale*(.116+.83*r*math.sin(math.tau*i/n)),scale*z) for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple(range((len(profile)-1)*n,len(profile)*n))]
    for j in range(len(profile)-1):
        faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for i in range(n)]
    obj=ctx.mesh(name,verts,faces,ctx.m['frame'],.0006*scale,parent=parent)
    obj['shape_solid']='klein'
    # Loop's plane faces the three-quarter camera, while retaining a broad
    # side silhouette. The return enters the lower shoulder on a distinct path.
    points=[(-.012,.126,.131),(-.037,.157,.153),(-.040,.160,.184),(-.015,.135,.198),
            (.018,.098,.196),(.040,.078,.176),(.037,.083,.146),(.022,.101,.117),(0,.118,.094)]
    path=[tuple(scale*v for v in p) for p in _curve(points)]
    _tube(ctx,name+' | returning neck',path,.009*scale,ctx.m['steel'],parent,sides=12,bevel=.0004*scale)
    return obj,path


def _body_panel(ctx,name,angle,half_width,mat,parent,raised=.0025,z_bounds=(.077,.109)):
    """Closed curved panel fitted to the vessel's lower pressure chamber."""
    rows,columns=4,10
    verts=[]
    for offset in (raised,raised-.0018):
        for j in range(rows+1):
            z=z_bounds[0]+(z_bounds[1]-z_bounds[0])*j/rows
            if z<=.094: r=.039+(.041-.039)*(z-.075)/(.094-.075)
            else: r=.041+(.036-.041)*(z-.094)/(.113-.094)
            for i in range(columns+1):
                a=angle-half_width+2*half_width*i/columns
                verts.append(((r+offset)*math.cos(a),.116+.83*(r+offset)*math.sin(a),z))
    stride=columns+1;layer=(rows+1)*stride;faces=[]
    for j in range(rows):
        for i in range(columns):
            a=j*stride+i;b=a+1;c=b+stride;d=a+stride
            faces += [(a,b,c,d),(layer+d,layer+c,layer+b,layer+a)]
    border=list(range(stride))+[j*stride+columns for j in range(1,rows+1)]
    border += [rows*stride+i for i in range(columns-1,-1,-1)]+[j*stride for j in range(rows-1,0,-1)]
    for i,a in enumerate(border):
        b=border[(i+1)%len(border)];faces.append((a,layer+a,layer+b,b))
    return ctx.mesh(name,verts,faces,mat,.00045,parent=parent)


FULL_BODY = True


def _floating_shell(ctx,name,angle,parent,window=False):
    profile=[(.060,.029),(.080,.061),(.112,.088),(.151,.094),(.193,.082),(.219,.055),(.237,.026)]
    verts=[];cols=12
    for offset in (0,-.006):
        for z,r in profile:
            for i in range(cols+1):
                a=angle-.47+.94*i/cols
                verts.append(((r+offset)*math.cos(a),(r+offset)*math.sin(a),z))
    stride=cols+1;layer=len(profile)*stride;faces=[]
    for j in range(len(profile)-1):
        for i in range(cols):
            if window and j in (2,3) and 2<=i<10:
                continue
            a=j*stride+i;b=a+1;c=b+stride;d=a+stride
            faces += [(a,b,c,d),(layer+d,layer+c,layer+b,layer+a)]
    border=list(range(stride))+[j*stride+cols for j in range(1,len(profile))]
    border += [(len(profile)-1)*stride+i for i in range(cols-1,-1,-1)]+[j*stride for j in range(len(profile)-2,0,-1)]
    for i,a in enumerate(border):
        b=border[(i+1)%len(border)];faces.append((a,layer+a,layer+b,b))
    if window:
        hole=[2*stride+i for i in range(2,11)]
        hole += [j*stride+10 for j in (3,4)]
        hole += [4*stride+i for i in range(9,1,-1)]+[3*stride+2]
        for i,a in enumerate(hole):
            b=hole[(i+1)%len(hole)];faces.append((a,layer+a,layer+b,b))
    return ctx.mesh(name,verts,faces,ctx.m['steel'],.0012,parent=parent)


def build(ctx):
    m=ctx.m
    vessel=ctx.empty('Klein_Levitating_Vessel_Root')
    # Four thick sculpted sectors leave opposing through-windows. The return
    # conduit visibly threads into the lower bulb rather than stopping outside.
    for q in (0,1,3,4):
        a=math.tau*(q+.5)/6
        _floating_shell(ctx,'Klein | sculpted vessel sector %d'%q,a,vessel,window=q==0)
        for side in (-1,1):
            edge=[((r+.001)*math.cos(a+side*.47),(r+.001)*math.sin(a+side*.47),z)
                  for z,r in ((.060,.029),(.080,.061),(.112,.088),(.151,.094),(.193,.082),(.219,.055),(.237,.026))]
            _tube(ctx,'Klein | window precision rim %s %s'%(q,side),edge,.003,m['frame'],vessel,sides=8)
    ctx.cylinder('Klein | integrated lower return cap',(0,0,.058),.030,.016,m['frame'],vertices=32,bevel=.0015,parent=vessel)
    ctx.ring('Klein | lower return seal',(0,0,.050),.028,.023,.003,m['edge'],vertices=32,parent=vessel)
    path=_curve([(-.028,.025,.225),(-.075,.074,.268),(-.078,.079,.316),(-.039,.041,.354),
                 (.022,-.024,.351),(.080,-.079,.313),(.069,-.068,.249),(.036,-.036,.174),(0,0,.090)],steps=9)
    _tube(ctx,'Klein | self threading return neck',path,.017,m['steel'],vessel,sides=16,bevel=.0008)
    inner=[(x*.76+.010,y*.76-.005,.233+(z-.233)*.88) for x,y,z in path]
    _tube(ctx,'Klein | separated inner field liner',inner,.0055,m['recess'],vessel,sides=12)
    _tube(ctx,'Klein | narrow circulating field trace',[(x,y,z+.0047) for x,y,z in inner[21:41]],.0015,m['accent'],vessel,sides=8,bevel=.0002)
    # The conduit is visible through the body windows and curves back into the
    # lower return cap, closing the energy route inside the vessel.
    return_path=_curve([(0,0,.091),(-.025,.015,.075),(-.038,.037,.120),(-.035,.037,.180),(-.028,.025,.225)],steps=9)
    _tube(ctx,'Klein | internal closed circulation return',return_path,.010,m['frame'],vessel,sides=12)
    _tube(ctx,'Klein | internal return field line',[(x+.008,y+.003,z) for x,y,z in return_path[8:27]],.0018,m['accent'],vessel,sides=8,bevel=.0002)
    for i,index in enumerate((8,22,40,55,65)):
        p=Vector(path[index]);t=(Vector(path[index+1])-Vector(path[index-1])).normalized()
        rotation=t.to_track_quat('Z','Y').to_euler()
        collar=ctx.ring('Klein | neck dark seal %d'%i,tuple(p),.0215,.0165,.014,m['recess'],vertices=24,bevel=.0007,parent=vessel)
        collar.rotation_euler=rotation
        for stage in (-1,1):
            collar=ctx.ring('Klein | neck precision flange %s %s'%(i,stage),tuple(p+t*stage*.007),.023,.016,.004,m['edge'],vertices=24,bevel=.0007,parent=vessel)
            collar.rotation_euler=rotation
    bands=[]
    for level,(z,r) in enumerate(((.098,.108),(.161,.114),(.214,.086))):
        for q in range(4):
            angle=math.tau*(q+.5)/4
            band=ctx.empty('Klein_Expanding_Containment_%s_%s'%(level,q),parent=vessel)
            bands.append((band,angle,level))
            pts=[(r*math.cos(a),r*math.sin(a),z) for a in [angle-.61+1.22*j/24 for j in range(25)]]
            _tube(ctx,'Klein | interrupted containment band %s %s'%(level,q),pts,.006,m['frame'],band,sides=10,bevel=.0007)
            _tube(ctx,'Klein | band precision edge %s %s'%(level,q),[(x,y,z+.004) for x,y,z in pts],.0017,m['edge'],band,sides=8)
            direction=Vector((math.cos(angle),math.sin(angle),0))
            rod=ctx.cylinder('Klein | captured band guide %s %s'%(level,q),tuple(direction*(r-.016)+Vector((0,0,z))),.0025,.042,m['edge'],vertices=12,parent=vessel)
            rod.rotation_euler=direction.to_track_quat('Z','Y').to_euler()
            clamp=ctx.box('Klein | radial band clamp %s %s'%(level,q),(r*math.cos(angle),r*math.sin(angle),z),(.016,.014,.021),m['steel'],.0012,parent=band)
            clamp.rotation_euler.z=angle
            for frame,spread in ((1,0),(20,.012),(40,0)):
                band.location=(spread*math.cos(angle),spread*math.sin(angle),0)
                band.keyframe_insert(data_path='location',frame=frame)
            band.location=(0,0,0)
    # Bonded control pods are part of the shell rather than a weapon receiver.
    # Each vertical pod carries five bays, linked to both lower and upper ribs.
    for side,angle in ((0,math.pi/2),(1,3*math.pi/2)):
        normal=Vector((math.cos(angle),math.sin(angle),0))
        rotation=(math.pi/2,0,angle+math.pi/2)
        pod=ctx.box('Klein | bonded shell control pod %d'%side,tuple(normal*.097+Vector((0,0,.150))),(.039,.157,.014),m['frame'],.0025,parent=vessel)
        pod.rotation_euler=rotation
        for z,r in ((.083,.062),(.218,.057)):
            link=ctx.cylinder('Klein | pod bonded tie %s %s'%(side,z),tuple(normal*((r+.096)/2)+Vector((0,0,z))),.008,.096-r+.014,m['steel'],vertices=16,parent=vessel)
            link.rotation_euler=normal.to_track_quat('Z','Y').to_euler()
        for i,z in enumerate((.084,.117,.150,.183,.216)):
            ctx.modulator_socket(side*5+i+1,tuple(normal*.106+Vector((0,0,z))),rotation=rotation,parent=vessel)
    ctx.box('Klein | bonded shape dock shoulder',(0,.052,.220),(.057,.060,.025),m['frame'],.003,parent=vessel)
    ctx.shape_dock((0,.055,.234),rotation=(.12,0,0),parent=vessel)
    ctx.ring('Klein | lower aura field aperture',(0,0,.052),.022,.015,.003,m['accent'],vertices=32,bevel=.0003,parent=vessel)
    circulation=[]
    for i in range(3):
        bead=ctx.sphere('Klein | circulating field marker %d'%i,inner[12+i*15],.006,m['accent'],16,8,parent=vessel)
        circulation.append(bead)
        for frame,index in ((1,12+i*15),(20,30+i*12),(40,12+i*15)):
            bead.location=inner[index];bead.keyframe_insert(data_path='location',frame=frame)
        bead.location=inner[12+i*15]
    return {'muzzle':(0,0,.046),'moving_parts':[b.name for b,a,l in bands]+[b.name for b in circulation],
            'description':'Levitating Klein vessel: self-threading neck and closed inner circulation through opposing body windows, nested expanding containment bands and bonded shell controls.',
            'design_name':'Klein Levitating Aura Vessel','hero_direction':(.85,.78,.47),'silhouette_direction':(1,.08,.10)}

def build_minishape(ctx,insert):
    mini=ctx.empty('KleinInsert | miniature center',(0,-.0167,-.0052),parent=insert)
    _vessel(ctx,'KleinInsert | sculpted miniature',mini,scale=.15,mini=True)
    ctx.ring('KleinInsert | pressure accent',(0,.0007,.009),.0063,.0055,.0008,ctx.m['accent'],vertices=24,bevel=.0001,parent=insert)


def build_insert(ctx,insert):
    carrier=ctx.box('Temporary Klein carrier',(0,0,0),(.029,.029,.004),ctx.m['accent'],0)
    insert.data=carrier.data;bpy.data.objects.remove(carrier,do_unlink=True)
    for child in list(insert.children): bpy.data.objects.remove(child,do_unlink=True)
    insert['shape_representation']='solid klein'
    ctx.box('KleinInsert | inset',(0,0,.0022),(.024,.024,.0006),ctx.m['ink'],.0003,parent=insert)
    build_minishape(ctx,insert)
