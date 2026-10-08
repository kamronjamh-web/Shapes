"""Reopen the Blender sources, verify motion, import the GLBs and render a lineup."""
import bpy
import argparse
import json
import math
from mathutils import Vector
from pathlib import Path
import sys
import traceback

sys.path.insert(0, str(Path(__file__).parent))
sys.dont_write_bytecode=True
from CreateMultiWeaponAssets import SPECS as ASSET_SPECS, SHAPE_STARS

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Art'/'WeaponConcepts'
SPECS={key:(asset[0],asset[2].split(' - ')[0].upper(),asset[2].split(' - ')[1].upper())
       for key,asset in ASSET_SPECS.items()}
SPECS['square']=('SquareHandgun','SQUARE','HANDGUN')
DEFAULT_WEAPONS=['circle','triangle','hexagon']


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--weapons',nargs='+',choices=list(SPECS),default=DEFAULT_WEAPONS)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    assert 3<=len(args.weapons)<=4,'Review accepts three weapons or a four-tier star progression'
    specs=[SPECS[key] for key in args.weapons]
    out=ART/('MultiWeaponReview' if args.weapons==DEFAULT_WEAPONS else
             'MultiWeaponReview_'+'_'.join(key.title() for key in args.weapons))
    out.mkdir(parents=True,exist_ok=True)
    (out/'.gdignore').write_text('',encoding='utf-8')
    report=[]
    for key,(name,_,_) in zip(args.weapons,specs):
        bpy.ops.wm.open_mainfile(filepath=str(ART/name/(name+'.blend')),load_ui=False)
        scene=bpy.context.scene
        meta=json.loads((ART/name/'model-info.json').read_text())
        parts=[bpy.data.objects[n] for n in meta.get('moving_parts',[])]
        animated=[o for o in parts if o.animation_data and o.animation_data.action]
        assert animated or key=='square',name+' has no saved mechanism animation'
        scene.frame_set(1)
        first=[o.matrix_local.copy() for o in animated]
        scene.frame_set(20)
        assert key=='square' or any(any(abs(a-b)>1e-5 for ra,rb in zip(m,o.matrix_local) for a,b in zip(ra,rb))
                   for m,o in zip(first,animated)),name+' motion did not evaluate'
        scene.frame_set(40)
        assert all(all(abs(a-b)<1e-5 for ra,rb in zip(m,o.matrix_local) for a,b in zip(ra,rb))
                   for m,o in zip(first,animated)),name+' mechanism does not return to rest'
        report.append({'asset':name,'shape_stars':SHAPE_STARS[key],
                       'blend_reopened':True,'saved_animation_evaluated':bool(animated),
                       'animation_returns_to_rest':True})

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=48
    scene.cycles.use_denoising=True
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type='OPTIX'
        prefs.get_devices()
        for dev in prefs.devices:
            dev.use=dev.type=='OPTIX'
        scene.cycles.device='GPU' if any(d.use for d in prefs.devices) else 'CPU'
    except Exception:
        scene.cycles.device='CPU'
    count=len(specs)
    center=(count-1)/2
    scene.render.resolution_x=800*count
    scene.render.resolution_y=1150
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.exposure=-.35
    world=bpy.data.worlds.new('Review world')
    scene.world=world
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.08,.11,.16,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.42

    for index,(name,_,_) in enumerate(specs):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(ART/name/(name+'.glb')))
        objects=set(bpy.data.objects)-before
        root=next(o for o in objects if o.name.startswith('GunRoot'))
        assert any(o.name.startswith('ShapeSocket') for o in objects)
        assert any(o.name.startswith('MuzzleAnchor') for o in objects)
        assert sum(o.name.startswith('ModulatorSocket') for o in objects)==10
        meshes=[o for o in objects if o.type=='MESH']
        assert len(meshes)>100,name+' incomplete mesh import'
        root.name=name
        root.rotation_mode='XYZ'
        root.rotation_euler=(0,0,-math.radians(127))
        root.location=(0,0,0)
        bpy.context.view_layer.update()
        points=[o.matrix_world @ Vector(corner) for o in meshes for corner in o.bound_box]
        low=Vector(tuple(min(p[j] for p in points) for j in range(3)))
        high=Vector(tuple(max(p[j] for p in points) for j in range(3)))
        dims=high-low
        # Normalize presentation only. Exported sources retain their metre scale.
        scale=min(.34/dims.x,.32/dims.y,.30/dims.z)
        root.scale=(scale,)*3
        midpoint=(low+high)*.5*scale
        root.location=((index-center)*.43-midpoint.x,.035-midpoint.y,.026-low.z*scale)
        report[index].update({'glb_imported':True,'imported_meshes':len(meshes),'imported_sockets':10,
                              'review_display_scale':scale,'display_sizes_normalized':True})

    def mat(name,color,metallic=0,roughness=.5):
        material=bpy.data.materials.new(name)
        material.diffuse_color=(*color,1)
        material.use_nodes=True
        p=material.node_tree.nodes['Principled BSDF']
        p.inputs['Base Color'].default_value=(*color,1)
        p.inputs['Metallic'].default_value=metallic
        p.inputs['Roughness'].default_value=roughness
        return material

    ground=mat('Review | graphite stage',(.009,.015,.023),0,.8)
    platform=mat('Review | platform',(.022,.033,.048),.1,.65)
    white=mat('Review | typography',(.64,.74,.85),0,.6)
    blue=mat('Review | secondary typography',(.15,.4,.7),0,.5)
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.020))
    bpy.context.object.data.materials.append(ground)
    for index in range(count):
        bpy.ops.mesh.primitive_cube_add(size=1,location=((index-center)*.43,-.02,-.008))
        obj=bpy.context.object
        obj.dimensions=(.395,.42,.016)
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        obj.data.materials.append(platform)
        bevel=obj.modifiers.new('Soft stage edge','BEVEL')
        bevel.width=.006
        bevel.segments=3

    data=bpy.data.cameras.new('Lineup camera')
    camera=bpy.data.objects.new('Lineup camera',data)
    scene.collection.objects.link(camera)
    camera.location=(0,-2.4,1.24)
    target=Vector((0,-.02,.13))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO'
    data.ortho_scale=1.48*count/3
    data.clip_start=.01
    scene.camera=camera

    def label(body,x,z,size,material):
        curve=bpy.data.curves.new(body,'FONT')
        curve.body=body
        curve.align_x='CENTER'
        curve.size=size
        obj=bpy.data.objects.new(body,curve)
        scene.collection.objects.link(obj)
        obj.location=(x,-.34,z)
        obj.rotation_euler=camera.rotation_euler
        obj.data.materials.append(material)

    for i,(_,title,subtitle) in enumerate(specs):
        label(f'{SHAPE_STARS[args.weapons[i]]} STAR',(i-center)*.43,.055,.011,blue)
        label(title,(i-center)*.43,.031,.020,white)
        label(subtitle,(i-center)*.43,.010,.009,blue)
    label('FORMS SHOWN AT NORMALIZED DISPLAY SIZES',0,-.004,.009,blue)

    def light(name,loc,energy,size,color):
        data=bpy.data.lights.new(name,'AREA')
        data.energy=energy
        data.shape='DISK'
        data.size=size
        data.color=color
        obj=bpy.data.objects.new(name,data)
        scene.collection.objects.link(obj)
        obj.location=loc
        obj.rotation_euler=(Vector((0,.05,.1))-obj.location).to_track_quat('-Z','Y').to_euler()

    light('Key',(.1,-.45,1.15),135,1.45,(1,.92,.84))
    light('Fill',(-.85,-.25,.7),70,1.0,(.68,.8,1))
    light('Rim',(.4,.45,.8),130,.8,(.8,.9,1))
    scene.render.filepath=str(out/'MultiWeapon_Lineup.png')
    bpy.ops.render.render(write_still=True)
    (out/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('REVIEW_COMPLETE',json.dumps(report),flush=True)


if __name__=='__main__':
    sys.stdout=open(ROOT/'.tmp'/'multiweapon-review.log','w',encoding='utf-8',buffering=1)
    sys.stderr=sys.stdout
    try:
        main()
    except Exception:
        traceback.print_exc()
        raise
