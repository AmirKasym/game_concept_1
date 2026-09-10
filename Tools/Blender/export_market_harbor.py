"""Run the existing generator and round-trip FBX/glTF assets in background Blender."""
import bpy
import json
import runpy
import sys
from pathlib import Path

root_path = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
output = root_path / 'Art/Blender/MarketHarbor/Exports'
output.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
runpy.run_path(str(root_path / 'Tools/Blender/build_market_harbor.py'))
root = bpy.data.objects['ReferenceMarketHarbor']
visuals = list(bpy.data.collections['ReferenceMarketHarbor_Visuals'].objects)
colliders = list(bpy.data.collections['ReferenceMarketHarbor_Collision'].objects)
for obj in colliders:
    obj['game_collision'] = True
    obj['render_geometry'] = False

bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(output / 'MarketHarbor_ExportSource.blend'))
expected = {}

for stem, objects in [('MarketHarbor', visuals), ('MarketHarbor_Collision', colliders)]:
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    triangle_count = 0
    for obj in objects:
        obj.data.calc_loop_triangles()
        triangle_count += len(obj.data.loop_triangles)
    expected[stem] = {'meshes': len(objects), 'triangles': triangle_count}
    bpy.ops.export_scene.fbx(
        filepath=str(output / (stem + '.fbx')),
        use_selection=True, object_types={'MESH', 'EMPTY'},
        axis_forward='-Z', axis_up='Y', global_scale=1,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True, use_mesh_modifiers=True,
        use_triangles=True, mesh_smooth_type='FACE',
        use_custom_props=True, add_leaf_bones=False,
        bake_anim=False, path_mode='AUTO'
    )
    bpy.ops.export_scene.gltf(
        filepath=str(output / (stem + '.gltf')),
        export_format='GLTF_SEPARATE', use_selection=True,
        export_yup=True, export_apply=True, export_extras=True,
        export_cameras=False, export_lights=False, export_animations=False
    )

# Reload each deliverable independently, without lights/studio objects.
results = []
for stem in expected:
    for extension in ('fbx', 'gltf'):
        bpy.ops.object.select_all(action='DESELECT')
        for obj in list(bpy.data.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        path = output / (stem + '.' + extension)
        if extension == 'fbx':
            bpy.ops.import_scene.fbx(filepath=str(path))
        else:
            bpy.ops.import_scene.gltf(filepath=str(path))
        meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
        triangles = 0
        for obj in meshes:
            obj.data.calc_loop_triangles()
            triangles += len(obj.data.loop_triangles)
            assert all(abs(v - 1) < .0001 for v in obj.scale), obj.name
        assert len(meshes) == expected[stem]['meshes'], str(path)
        assert triangles == expected[stem]['triangles'], str(path)
        assert not any(obj.type in {'CAMERA', 'LIGHT'} for obj in bpy.context.scene.objects)
        if stem == 'MarketHarbor':
            assert all(any(material is not None for material in obj.data.materials) for obj in meshes)
        else:
            assert all(obj.name.startswith('COL_') for obj in meshes)
        corners = [obj.matrix_world @ __import__('mathutils').Vector(c) for obj in meshes for c in obj.bound_box]
        bounds = [max(p[i] for p in corners) - min(p[i] for p in corners) for i in range(3)]
        results.append({'file': path.name, 'mesh_count': len(meshes), 'triangles': triangles,
                        'bounds_metres_blender_xyz': [round(v, 4) for v in bounds], 'round_trip': 'PASS'})
for stem in expected:
    pair = [result for result in results if result['file'].startswith(stem + '.')]
    assert all(abs(a-b) < .01 for a,b in zip(pair[0]['bounds_metres_blender_xyz'], pair[1]['bounds_metres_blender_xyz']))
(output / 'ExportValidation.json').write_text(json.dumps(results, indent=2), encoding='utf8')
print('MARKET_EXPORT_PASS ' + json.dumps(results))
