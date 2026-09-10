"""Render the saved source without changing the tested FBX or .blend."""
import bpy, sys
from pathlib import Path
from mathutils import Vector
root=Path(sys.argv[sys.argv.index('--')+1])
bpy.ops.wm.open_mainfile(filepath=str(root/'Art/Blender/LighthouseIsland.blend'))
scene=bpy.context.scene
scene.render.filepath=str(root/'Docs/Art/LighthouseIsland_Preview.png')
bpy.ops.render.render(write_still=True)
camera=scene.camera; camera.location=(0,-48,5)
camera.rotation_euler=(Vector((0,8,10))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='PERSP'; camera.data.lens=27
scene.render.filepath=str(root/'Docs/Art/LighthouseIsland_Harbour.png')
bpy.ops.render.render(write_still=True)
