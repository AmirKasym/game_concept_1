"""Prepare market integration copies with walkable terrace transitions."""
import bpy,runpy,sys
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
ns=runpy.run_path(str(root/'Tools/Blender/build_market_harbor.py'))
for obj in list(ns['collisions'].objects):
    if 'StairRamp' in obj.name: bpy.data.objects.remove(obj,do_unlink=True)
for obj in list(ns['visuals'].objects):
    if obj.name=='Stairs': bpy.data.objects.remove(obj,do_unlink=True)
ns['buffers']['Stairs']=([],[],[])
for a,b,width,fraction in [
    ((-6,-13,1.47),(-4,-6,4.25),3.6,1),
    ((25,-8,1.47),(21,-1,4.25),3.6,.62),
    ((8,8,4.25),(8,17,7.55),4,.78),
    ((29,5,1.47),(21,16,7.55),3.8,.82)]:
    a,b=Vector(a),Vector(b); middle=a.lerp(b,fraction); middle.z=b.z
    ns['stairs'](a,middle,width)
    if fraction<1: ns['stairs'](middle,b,width)
verts,faces,colors=ns['buffers']['Stairs']
mesh=ns['data_mesh']('Market integration stairs',verts,faces)
for material in ns['materials']: mesh.materials.append(material)
for polygon,index in zip(mesh.polygons,colors): polygon.material_index=index
obj=bpy.data.objects.new('Stairs',mesh); ns['visuals'].objects.link(obj); obj.parent=ns['root']
output=root/'Assets/TradeWinds/Art/IslandIntegration'
for name,collection in [('MarketHarbor',ns['visuals']),('MarketHarbor_Collision',ns['collisions'])]:
    bpy.ops.object.select_all(action='DESELECT'); ns['root'].select_set(True)
    for obj in collection.objects: obj.hide_set(False); obj.select_set(True)
    bpy.context.view_layer.objects.active=ns['root']
    bpy.ops.export_scene.fbx(filepath=str(output/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,use_triangles=True,mesh_smooth_type='FACE',bake_anim=False,add_leaf_bones=False)
print('MARKET_INTEGRATION_EXPORT collision_pieces='+str(len(ns['collisions'].objects)))
