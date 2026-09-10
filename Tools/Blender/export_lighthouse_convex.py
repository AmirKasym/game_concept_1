"""Export small convex collision pieces without filling channels with one island hull."""
import bpy, bmesh, sys, json
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.wm.open_mainfile(filepath=str(root/'Art/Blender/LighthouseIsland.blend'))
sources=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('COL_')]
target=bpy.data.collections.new('LighthouseConvexCollision'); bpy.context.scene.collection.children.link(target)
parent=bpy.data.objects.new('LighthouseCollision',None); target.objects.link(parent)
pieces=[]
def hull(name,points):
    bm=bmesh.new()
    for p in points: bm.verts.new(p)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj=bpy.data.objects.new(name,mesh); target.objects.link(obj); obj.parent=parent
    obj['convex_collision']=True; pieces.append(obj)
for source in sources:
    mesh=source.data; adjacent=[set() for _ in mesh.vertices]
    for edge in mesh.edges:
        a,b=edge.vertices; adjacent[a].add(b); adjacent[b].add(a)
    unseen=set(range(len(mesh.vertices)))
    while unseen:
        component=set(); todo=[unseen.pop()]
        while todo:
            index=todo.pop(); component.add(index)
            for other in adjacent[index]&unseen: unseen.remove(other); todo.append(other)
        points=[source.matrix_world@mesh.vertices[i].co for i in component]
        complex_path=('Paths' in source.name or 'RampBanks' in source.name or 'CollisionOnly' in source.name) and len(component)>8
        if complex_path:
            faces=[f for f in mesh.polygons if f.vertices[0] in component and f.normal.z>=.5]
            # The authored ribbons have two top quads per segment. Merge each pair,
            # never an entire curved route, to keep collider count and hull size bounded.
            index=0
            while index<len(faces):
                ids=set(faces[index].vertices); index+=1
                if index<len(faces) and len(ids.intersection(faces[index].vertices))==2:
                    ids.update(faces[index].vertices); index+=1
                top=[source.matrix_world@mesh.vertices[i].co for i in ids]
                if 'RampBanks' in source.name:
                    base=min(p.z for p in points); bottom=[Vector((p.x,p.y,base)) for p in top]
                else:
                    depth=1.1 if 'CollisionOnly' in source.name else .18
                    bottom=[p-Vector((0,0,depth)) for p in top]
                hull('COL_Hull_%03d'%len(pieces),top+bottom)
        else: hull('COL_Hull_%03d'%len(pieces),points)
bpy.ops.object.select_all(action='DESELECT')
for obj in pieces+[parent]: obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active=parent
output=root/'Assets/TradeWinds/Art/IslandIntegration'; output.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(output/'LighthouseCollision.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,use_triangles=True,bake_anim=False,add_leaf_bones=False)
stats={'convex_pieces':len(pieces),'max_vertices':max(len(o.data.vertices) for o in pieces),'faces':sum(len(o.data.polygons) for o in pieces)}
(root/'TestResults/lighthouse-convex.json').write_text(json.dumps(stats,indent=2))
print('CONVEX_EXPORT '+json.dumps(stats))
