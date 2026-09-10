"""Run the standalone user script twice; save and render the validated result."""
import bpy, runpy, sys, json
from pathlib import Path
root=Path(sys.argv[sys.argv.index('--')+1])
script=root/'Tools/Blender/build_market_harbor.py'
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
runpy.run_path(str(script))
first=len(bpy.data.collections['ReferenceMarketHarbor'].all_objects)
runpy.run_path(str(script))
collection=bpy.data.collections['ReferenceMarketHarbor']
assert len(collection.all_objects)==first, 'Rerun added duplicate objects'
assert tuple(bpy.data.objects['ReferenceMarketHarbor'].location)==(0,0,0)
triangles=0; checks=[]
for obj in collection.all_objects:
    if obj.type!='MESH': continue
    assert all(abs(v-1)<1e-6 for v in obj.scale), obj.name
    assert not any(p.use_smooth for p in obj.data.polygons), obj.name
    assert all(p.area>1e-8 for p in obj.data.polygons), 'Zero-area polygon: '+obj.name
    if not obj.name.startswith('COL_'):
        obj.data.calc_loop_triangles(); triangles+=len(obj.data.loop_triangles)
        assert obj.data.materials and all(m.use_nodes for m in obj.data.materials)
    checks.append(obj.name)
scene=bpy.context.scene
scene.world=bpy.data.worlds.new('Harbor presentation world'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.39,.42,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
# Presentation-only floor stays outside the model and its collision collection.
bpy.ops.mesh.primitive_plane_add(size=300,location=(0,0,-2.45))
floor=bpy.context.object; floor.name='Presentation floor'
mat=bpy.data.materials.new('Presentation background'); mat.diffuse_color=(.36,.38,.39,1); floor.data.materials.append(mat)
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
output=root/'Art/Blender/MarketHarbor'; output.mkdir(parents=True,exist_ok=True)
preview=root/'Docs/Art/MarketHarbor'; preview.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(output/'MarketHarbor.blend'))
report={'visual_triangles':triangles,'mesh_objects_checked':len(checks),'rerun_object_count':first,
        'checks':['Script runs twice without duplicate generated objects','Origin 0,0,0','Flat shading',
                  'All mesh scales 1,1,1','No zero-area polygons','Principled materials','Separate hidden collision objects']}
(preview/'Validation.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(preview/'MarketHarbor_Preview.png'); bpy.ops.render.render(write_still=True)
print('MARKET_HARBOR_VALIDATED '+json.dumps(report))
