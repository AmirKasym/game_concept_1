"""Geometric route clearance audit against the exported collision surfaces."""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
root=Path(sys.argv[sys.argv.index('--')+1])
bpy.ops.wm.open_mainfile(filepath=str(root/'Art/Blender/LighthouseIsland.blend'))
vertices=[]; polygons=[]
for obj in bpy.data.objects:
    if not obj.name.startswith('COL_') or obj.type!='MESH': continue
    n=len(vertices); vertices.extend([obj.matrix_world@v.co for v in obj.data.vertices])
    polygons.extend([tuple(n+i for i in p.vertices) for p in obj.data.polygons])
bvh=BVHTree.FromPolygons(vertices,polygons)
routes=json.loads((root/'Assets/TradeWinds/Art/Islands/LighthouseIsland_Routes.json').read_text())
results={}
for route in routes['routes']:
    issues=[]
    pts=[Vector((p['x'],p['y'],p['z'])) for p in route['points']]
    for a,b in zip(pts,pts[1:]):
        steps=math.ceil((b-a).length/.4)
        for i in range(steps+1):
            p=a.lerp(b,i/steps)
            floor=bvh.ray_cast(p+Vector((0,0,.35)),Vector((0,0,-1)),.8)[0]
            head=bvh.ray_cast(p+Vector((0,0,.15)),Vector((0,0,1)),1.75)[0]
            if floor is None or head is not None:
                issues.append({'point':[round(float(t),2) for t in p], 'floor_missing':floor is None,'overhead':None if head is None else [round(float(t),2) for t in head]})
    results[route['name']]=issues
(root/'TestResults/island-route-audit.json').write_text(json.dumps(results,indent=2))
print('ROUTE_AUDIT '+json.dumps({n:len(v) for n,v in results.items()}))
