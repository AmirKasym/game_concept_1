"""Reproducible original island based on the user's lighthouse reference.
Run: blender -b --factory-startup --python this_file -- <repository-root>
Blender coordinates: XY ground, Z up; FBX: -Z forward, Y up, metres.
"""
import bpy, bmesh, math, random, json, sys
from pathlib import Path
from mathutils import Vector
import numpy as np

ROOT = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
OUT = ROOT / 'Assets/TradeWinds/Art/Islands'
SOURCE = ROOT / 'Art/Blender'
PREVIEW = ROOT / 'Docs/Art'
for p in (OUT / 'Textures', SOURCE, PREVIEW): p.mkdir(parents=True, exist_ok=True)
random.seed(1703)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for c in list(bpy.data.collections):
    if c.name != 'Collection': bpy.data.collections.remove(c)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1

# One padded atlas. Each surface selects one of 16 hand-designed procedural tiles.
COLORS = [(.42,.29,.20),(.48,.56,.28),(.68,.56,.36),(.48,.51,.45),
          (.43,.27,.15),(.68,.56,.37),(.38,.28,.20),(.77,.71,.55),
          (.11,.22,.24),(.23,.34,.15),(.51,.64,.28),(.24,.27,.24),
          (.57,.30,.21),(.30,.45,.42),(.70,.53,.23),(.50,.35,.29)]
N = 1024; tile = N // 4
pix = np.ones((N,N,4),dtype=np.float32)
rng = np.random.default_rng(1703)
yy,xx=np.mgrid[0:tile,0:tile]
for k,color in enumerate(COLORS):
    noise=rng.normal(0,.016,(tile,tile))
    if k in (4,6):
        noise += .028*np.sin(xx*.20+np.sin(yy*.025)*2) - .10*((xx%64)<3)
    elif k==5:
        noise += .019*np.sin(yy*.46) - .065*((yy%64)<3)
        noise -= .045*(((xx+(yy//64%2)*32)%64)<2)
    elif k==3:
        noise -= .10*((yy%64)<4)
        noise -= .075*(((xx+(yy//64%2)*48)%96)<4)
    elif k==7: noise += .015*np.sin(xx*.018)*np.cos(yy*.025)
    elif k in (1,9,10): noise += .018*np.sin(xx*.30)*np.sin(yy*.23)
    block=np.clip(np.array(color)[None,None,:]+noise[:,:,None],0,1)
    y=(k//4)*tile; x=(k%4)*tile
    pix[y:y+tile,x:x+tile,:3]=block
atlas=bpy.data.images.new('Lighthouse_Atlas_1024',width=N,height=N,alpha=False)
atlas.pixels.foreach_set(pix.ravel()); atlas.filepath_raw=str(OUT/'Textures/Lighthouse_Atlas.png')
atlas.file_format='PNG'; atlas.save()
material=bpy.data.materials.new('Island_Atlas'); material.use_nodes=True
bsdf=material.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=.86
tex=material.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=atlas
material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])

groups={}; collision={}; routes={}
def mesh(group, verts, faces, colors, solid=False):
    bucket=groups.setdefault(group,[[],[],[]]); base=len(bucket[0]); bucket[0].extend(verts)
    bucket[1].extend([tuple(base+i for i in f) for f in faces])
    bucket[2].extend([colors]*len(faces) if isinstance(colors,int) else colors)
    if solid:
        b=collision.setdefault(group,[[],[]]); off=len(b[0]); b[0].extend(verts)
        b[1].extend([tuple(off+i for i in f) for f in faces])

def box(g,p,size,col=4,angle=0,solid=False):
    sx,sy,sz=[s/2 for s in size]; ca,sa=math.cos(angle),math.sin(angle)
    v=[]
    for x,y,z in [(-sx,-sy,-sz),(sx,-sy,-sz),(sx,sy,-sz),(-sx,sy,-sz),
                  (-sx,-sy,sz),(sx,-sy,sz),(sx,sy,sz),(-sx,sy,sz)]:
        v.append((p[0]+x*ca-y*sa,p[1]+x*sa+y*ca,p[2]+z))
    mesh(g,v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],col,solid)

def beam(g,a,b,r,col=4,sides=6,r2=None,solid=False):
    a,b=Vector(a),Vector(b); d=(b-a).normalized()
    u=d.cross(Vector((0,0,1)))
    if u.length<.01: u=d.cross(Vector((0,1,0)))
    u.normalize(); v=d.cross(u); r2=r if r2 is None else r2
    vs=[tuple(p+(u*math.cos(i*math.tau/sides)+v*math.sin(i*math.tau/sides))*radius)
        for p,radius in ((a,r),(b,r2)) for i in range(sides)]
    fs=[tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    fs += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    mesh(g,vs,fs,col,solid)

def island(g,c,rx,ry,top,bottom=-3,n=40):
    outline=[]
    for i in range(n):
        a=i*math.tau/n; jitter=1+.035*math.sin(a*5+.3)+.023*math.cos(a*9)
        outline.append((c[0]+rx*math.cos(a)*jitter,c[1]+ry*math.sin(a)*jitter))
    vs=[]
    for scale,z in [(1.015,bottom),(1,top-.65),(.975,top)]:
        vs.extend([(c[0]+(x-c[0])*scale,c[1]+(y-c[1])*scale,z) for x,y in outline])
    fs=[tuple(reversed(range(n))),tuple(range(2*n,3*n))]; cs=[0,1]
    for ring in range(2):
        for i in range(n):
            j=(i+1)%n
            fs.append((ring*n+i,ring*n+j,(ring+1)*n+j,(ring+1)*n+i)); cs.append(0 if ring==0 else 1)
    mesh(g,vs,fs,cs,True)

def path(g,points,width=3.6,col=2,thick=.18,solid=True,record=None):
    # Mitered ribbon, continuous top: no decorative stair lips in the collision mesh.
    pts=[Vector(p) for p in points]; vs=[]
    for i,p in enumerate(pts):
        d=pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]; d.z=0; d.normalize()
        normal=Vector((-d.y,d.x,0))*width/2
        vs += [tuple(p-normal),tuple(p+normal),tuple(p-normal-Vector((0,0,thick))),tuple(p+normal-Vector((0,0,thick)))]
    fs=[]
    for i in range(len(pts)-1):
        a=i*4;b=a+4
        fs += [(a,b,b+1,a+1),(a+2,a+3,b+3,b+2),(a,a+2,b+2,b),(a+1,b+1,b+3,a+3)]
    fs += [(0,1,3,2),(len(vs)-4,len(vs)-2,len(vs)-1,len(vs)-3)]
    mesh(g,vs,fs,col,solid)
    if record: routes[record]={'width':width,'points':[list(p) for p in points]}

def rock(g,p,size):
    n=7; vs=[]
    for level,z,scale in [(0,0,.8),(1,.45,1),(2,1,.52)]:
        for i in range(n):
            a=math.tau*i/n+.18*level
            vs.append((p[0]+math.cos(a)*size[0]*scale*(.9+random.random()*.2),p[1]+math.sin(a)*size[1]*scale,p[2]+z*size[2]))
    fs=[tuple(reversed(range(n))),tuple(range(2*n,3*n))]
    for ring in range(2):
        for i in range(n): fs.append((ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i))
    mesh(g,vs,fs,3)

def palm(p,height=7):
    g='Vegetation'; bend=random.uniform(-1,1)
    for i in range(7):
        a=(p[0]+bend*(i/7)**2,p[1]+.5*(i/7)**2,p[2]+height*i/7)
        b=(p[0]+bend*((i+1)/7)**2,p[1]+.5*((i+1)/7)**2,p[2]+height*(i+1)/7)
        beam(g,a,b,.24-i*.014,4,7,r2=.23-i*.014)
        beam(g,(a[0],a[1],a[2]+.02),(a[0],a[1],a[2]+.11),.26-i*.014,6,7)
    crown=Vector((p[0]+bend,p[1]+.5,p[2]+height))
    for j in range(8):
        a=j*math.tau/8+random.uniform(-.15,.15); d=Vector((math.cos(a),math.sin(a),0)); s=Vector((-d.y,d.x,0))
        verts=[]
        for i in range(6):
            t=i/5; q=crown+d*(t*3.5)+Vector((0,0,1.1*math.sin(t*math.pi)-1.3*t*t))
            w=.66*math.sin(t*math.pi)**.6
            verts += [tuple(q-s*w-Vector((0,0,.13*w))),tuple(q+Vector((0,0,.08))),tuple(q+s*w-Vector((0,0,.13*w)))]
        fs=[]
        for i in range(5):
            k=i*3; fs += [(k,k+3,k+4,k+1),(k+1,k+4,k+5,k+2)]
        # Thin solid leaves: backfaces remain visible with ordinary opaque Unity Lit.
        off=len(verts); verts += [(x,y,z-.035) for x,y,z in verts]
        fs += [tuple(off+x for x in reversed(f)) for f in fs[:]]
        mesh(g,verts,fs,[10 if i%2 else 9 for i in range(len(fs))])

def barrel(p,scale=1):
    g='Props'; n=10; vs=[]
    for z,r in [(0,.40),(.13,.46),(.57,.51),(1.03,.46),(1.15,.40)]:
        for i in range(n):
            a=i*math.tau/n; vs.append((p[0]+r*scale*math.cos(a),p[1]+r*scale*math.sin(a),p[2]+z*scale))
    fs=[tuple(reversed(range(n))),tuple(range(4*n,5*n))]
    for j in range(4):
        fs += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for i in range(n)]
    mesh(g,vs,fs,4)
    for z,r in [(.18,.475),(.88,.48)]: beam(g,(p[0],p[1],p[2]+z*scale),(p[0],p[1],p[2]+(z+.10)*scale),r*scale,11,10)

def crate(p,s=1.2,col=14):
    x,y,z=p; box('Props',(x,y,z+s/2),(s,s,s),col)
    for dx in [-1,1]:
        for dy in [-1,1]: box('Props',(x+dx*(s/2+.02),y+dy*(s/2-.07),z+s/2),(.10,.13,s+.04),4)
    for zz in [z+.09,z+s-.09]:
        box('Props',(x,y-s/2-.025,zz),(s,.09,.12),4)
        box('Props',(x,y+s/2+.025,zz),(s,.09,.12),4)
    beam('Props',(x-s*.40,y-s/2-.08,z+.18),(x+s*.40,y-s/2-.08,z+s-.18),.065,4,4)

def house(x,y,z,w=5.2,d=4.6,h=3.4,angle=0):
    g='Buildings'; start={key:len(value[0]) for key,value in groups.items()}
    # Build in local coordinates, then rotate/translate only this house's geometry.
    base=len(groups.setdefault(g,[[],[],[]])[0]); cb=len(collision.setdefault(g,[[],[]])[0])
    box(g,(0,0,.38),(w+.25,d+.2,.76),3,solid=True)
    box(g,(0,0,h/2+.65),(w,d,h),7,solid=True)
    eave=h+.65; ridge=eave+2.1
    v=[(-w/2,-d/2,eave),(w/2,-d/2,eave),(0,-d/2,ridge),(-w/2,d/2,eave),(w/2,d/2,eave),(0,d/2,ridge)]
    mesh(g,v,[(0,1,2),(3,5,4),(0,3,4,1),(0,2,5,3),(1,4,5,2)],7)
    # Three slopes per side give the reference's soft flared thatch silhouette.
    for side in [-1,1]:
        xs=[0,side*w*.27,side*w*.49,side*(w*.5+.55)]
        zs=[ridge+.16,eave+1.12,eave+.19,eave-.13]
        for i in range(3):
            verts=[(xs[i],-d/2-.48,zs[i]),(xs[i+1],-d/2-.48,zs[i+1]),(xs[i+1],d/2+.48,zs[i+1]),(xs[i],d/2+.48,zs[i])]
            verts += [(a,b,c-.16) for a,b,c in verts]
            mesh(g,verts,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],5)
        for yy in [-d/2-.51,d/2+.51]:
            for i in range(3): beam(g,(xs[i],yy,zs[i]+.04),(xs[i+1],yy,zs[i+1]+.04),.11,6,4)
    beam(g,(0,-d/2-.7,ridge+.2),(0,d/2+.7,ridge+.2),.13,6,6)
    for sx in [-1,1]:
        for sy in [-1,1]: box(g,(sx*(w/2-.07),sy*(d/2+.035),h/2+.65),(.18,.16,h),4)
    # Recessed door, mullioned windows and timber framing.
    box(g,(-.4,-d/2-.06,1.65),(1.15,.12,2),4)
    for dx in [-1,1]: box(g,(-.4+dx*.66,-d/2-.15,1.65),(.13,.16,2.18),6)
    box(g,(-.4,-d/2-.13,2.78),(1.45,.19,.15),6)
    box(g,(w*.29,-d/2-.075,2.15),(.9,.15,1.05),8)
    for zz in [1.59,2.69,2.14]: box(g,(w*.29,-d/2-.18,zz),(1.06,.12,.10),4)
    box(g,(w*.29,-d/2-.18,2.14),(.09,.12,1.12),4)
    beam(g,(0,-d/2-.09,eave+.15),(0,-d/2-.09,ridge-.1),.09,4,4)
    box(g,(w*.27,d*.17,ridge-.1),(.62,.65,1.5),3)
    box(g,(w*.27,d*.17,ridge+.69),(.80,.82,.20),11)
    ca,sa=math.cos(angle),math.sin(angle)
    for bucket,index in [(groups[g][0],base),(collision[g][0],cb)]:
        for i in range(index,len(bucket)):
            a,b,c=bucket[i]; bucket[i]=(x+a*ca-b*sa,y+a*sa+b*ca,z+c)
    barrel((x-w*.62,y-1,z)); crate((x+w*.62,y+1,z),.9,13)

def tower(p):
    x,y,z=p; g='Buildings'; h=8
    for dx in [-1,1]:
        for dy in [-1,1]:
            beam(g,(x+dx*1.7,y+dy*1.7,z),(x+dx*1.3,y+dy*1.3,z+h),.21,4,4)
    for level in [1.2,3.9,6.7]:
        for side in [-1,1]:
            beam(g,(x-1.6,y+side*1.5,z+level),(x+1.6,y+side*1.5,z+level+2.2),.12,4,4)
            beam(g,(x+1.6,y+side*1.5,z+level),(x-1.6,y+side*1.5,z+level+2.2),.12,4,4)
            beam(g,(x+side*1.5,y-1.6,z+level),(x+side*1.5,y+1.6,z+level+2.2),.12,4,4)
    box(g,(x,y,z+h),(4.1,4.1,.32),4)
    for side in [-1,1]:
        for t in [-1.5,-.75,0,.75,1.5]:
            box(g,(x+t,y+side*1.8,z+h+.7),(.10,.10,1.2),4)
            box(g,(x+side*1.8,y+t,z+h+.7),(.10,.10,1.2),4)
        box(g,(x,y+side*1.8,z+h+1.3),(3.8,.13,.13),4)
        box(g,(x+side*1.8,y,z+h+1.3),(.13,3.8,.13),4)
    for dx in [-1,1]:
        for dy in [-1,1]: beam(g,(x+dx*1.65,y+dy*1.65,z+h),(x+dx*1.65,y+dy*1.65,z+h+2.7),.12,4,4)
    mesh(g,[(x-2.5,y-2.5,z+h+2.6),(x+2.5,y-2.5,z+h+2.6),(x+2.5,y+2.5,z+h+2.6),(x-2.5,y+2.5,z+h+2.6),(x,y,z+h+4)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(0,3,2,1)],5)
    # The decorative tower is closed to climbing in this asset.
    box('CollisionOnly',(x,y,z+4),(3.4,3.4,8),4,solid=True)

def bridge(name,a,b,width=4.5):
    a,b=Vector(a),Vector(b); axis=b-a; side=Vector((-axis.y,axis.x,0)).normalized()
    points=[]
    for i in range(17):
        t=i/16; p=a.lerp(b,t); p.z+=.38*math.sin(math.pi*t); points.append(tuple(p))
    path('Paths_Bridges',points,width,3,.5,record=name)
    for sign in [-1,1]:
        edge=[tuple(Vector(p)+side*sign*(width/2-.14)+Vector((0,0,1))) for p in points]
        for j in range(16): beam('Buildings',edge[j],edge[j+1],.17,3,4)
        # Solid arched spandrel, open below; the channel is actually empty.
        verts=[]
        for i,p in enumerate(points):
            t=i/16; top=Vector(p)+side*sign*(width/2-.25)
            low=top.copy(); low.z-=.60+3.1*(abs(2*t-1)**1.7)
            verts += [tuple(top),tuple(low)]
        faces=[(i*2,i*2+1,i*2+3,i*2+2) for i in range(16)]
        mesh('Buildings',verts,faces,3)
        # Continuous parapet collision, independent of stone seams.
        path('CollisionOnly',edge,.28,3,1.1,True)

def dock(x,y,length=14,width=4.5):
    # Deck connects flush to the harbour at height 1.25 m.
    z=1.25; g='Docks'
    box('CollisionOnly',(x,y-length/2,z-.18),(width,length,.36),4,solid=True)
    for i in range(int(length/.42)):
        box(g,(x,y-i*.42-.20,z-.09),(width,.40,.18),4)
    for side in [-1,1]:
        beam(g,(x+side*(width/2-.3),y,z-.42),(x+side*(width/2-.3),y-length,z-.42),.17,6,4)
        for j in range(0,int(length)+1,4):
            beam(g,(x+side*(width/2-.18),y-j,-2.3),(x+side*(width/2-.18),y-j,z+.55),.22,4,8)
            beam(g,(x+side*(width/2-.18),y-j,z+.27),(x+side*(width/2-.18),y-j,z+.39),.25,2,10)
    return (x,y-length+1,z)

def boat(x,y,angle=0,col=13):
    g='Props_Boats'; start=len(groups.setdefault(g,[[],[],[]])[0])
    outline=[(0,-3.6),(1.2,-2.7),(1.45,-.8),(1.3,2.1),(.8,2.7),(-.8,2.7),(-1.3,2.1),(-1.45,-.8),(-1.2,-2.7)]
    n=len(outline); vs=[(a*.78,b*.9,-.35) for a,b in outline]+[(a,b,.7) for a,b in outline]
    fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    fs += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh(g,vs,fs,[col,4]+[col]*n)
    for i in range(n): beam(g,(*outline[i],.77),(*outline[(i+1)%n],.77),.12,7,4)
    box(g,(0,.6,1.15),(1.6,2,1.25),7); box(g,(0,.6,1.86),(1.95,2.4,.15),col)
    box(g,(0,-.42,1.35),(1.18,.08,.53),8)
    beam(g,(.5,.5,1.9),(.5,.5,2.7),.13,11,8)
    ca,sa=math.cos(angle),math.sin(angle)
    for i in range(start,len(groups[g][0])):
        a,b,c=groups[g][0][i]; groups[g][0][i]=(x+a*ca-b*sa,y+a*sa+b*ca,c)

def lighthouse():
    g='Buildings_Lighthouse'; x,y,z=-2,20,11.2
    beam(g,(x,y,z),(x,y,z+.5),3.0,3,12)
    beam(g,(x,y,z+.5),(x,y,z+15),2.25,7,12,r2=1.4,solid=True)
    for level,rad in [(1,2.25),(4,2.1),(11,1.7),(15,1.6)]:
        beam(g,(x,y,z+level),(x,y,z+level+.22),rad,6,12)
    box(g,(x,y-2.25,z+1.5),(1.0,.2,2),4)
    for level in [5.3,8.6,12.2]:
        box(g,(x,y-(2.25-level*.055),z+level),(.42,.15,.8),8)
    beam(g,(x,y,z+15),(x,y,z+15.4),2.25,4,16)
    beam(g,(x,y,z+15.4),(x,y,z+18.2),1.4,8,12)
    for i in range(12):
        a=i*math.tau/12; px=x+math.cos(a)*1.45; py=y+math.sin(a)*1.45
        beam(g,(px,py,z+15.4),(px,py,z+18.2),.075,14,5)
        bx=x+math.cos(a)*2.12; by=y+math.sin(a)*2.12
        beam(g,(bx,by,z+15.4),(bx,by,z+16.4),.065,4,5)
        aa=(i+1)*math.tau/12
        beam(g,(bx,by,z+16.4),(x+math.cos(aa)*2.12,y+math.sin(aa)*2.12,z+16.4),.065,4,5)
    beam(g,(x,y,z+18.2),(x,y,z+20),1.9,5,12,r2=.18)
    beam(g,(x,y,z+20),(x,y,z+21),.08,11,6)

# Broad low shoreline, terraced village, separate west/east headlands.
island('Terrain_LowVillage',(0,-1),29,21,1.20,n=48)
island('Terrain_WestHeadland',(-36,11),11,18,5.0,n=32)
island('Terrain_EastHeadland',(36,9),12,22,4.0,n=36)
island('Terrain_UpperVillage',(0,13),22,14,6.2,bottom=.8,n=40)
island('Terrain_LighthouseHill',(-2,20),10,8,11.2,bottom=5.9,n=28)

path('Paths_Harbour',[(-23,-13,1.25),(-14,-17,1.25),(0,-18,1.25),(15,-16,1.25),(24,-11,1.25)],4.8,record='Harbour promenade')
path('Paths_Village',[(-23,-9,1.25),(-14,-7,1.25),(0,-8,1.25),(13,-7,1.25),(23,-9,1.25)],3.8,record='Village lane')
path('Paths_WestAscent',[(-23,-9,1.25),(-25,-1,3.1),(-22,7,5.1),(-17,10,6.25)],4.5,record='West ascent')
path('Paths_EastAscent',[(23,-9,1.25),(25,-1,3.1),(22,7,5.1),(17,10,6.25)],4.5,record='East ascent')
path('Paths_Upper',[(-19,10,6.25),(-12,8,6.25),(0,7.5,6.25),(13,9,6.25),(20,14,6.25)],4.2,record='Upper terrace')
path('Paths_Summit',[(14,9,6.25),(14,17,7.5),(11,24,9.1),(5,26,10.5),(0,25,11.25),(-2,20,11.25)],3.8,record='Lighthouse ascent')
bridge('West stone bridge',(-36,15,5.05),(-18,15,6.25))
bridge('East high bridge',(18,18,6.25),(36,18,4.05))
bridge('East harbour bridge',(23,-9,1.25),(36,-9,4.05))
path('Paths_Headlands',[(-36,-1,5.05),(-38,7,5.05),(-36,15,5.05),(-33,20,5.05)],3.6)
path('Paths_Headlands',[(36,-9,4.05),(38,0,4.05),(37,9,4.05),(36,18,4.05),(35,23,4.05)],3.6)

for x,y,z,w,d,angle in [(-14,-1,1.2,5.2,4.8,-.13),(-7,0,1.2,5,4.7,.05),
                       (0,-1,1.2,5.4,4.6,.12),(10,-1,1.2,5.3,4.8,-.20),(18,-3,1.2,4.6,4.3,-.35),
                       (8,17,6.2,4.7,4.1,-.22)]:
    house(x,y,z,w,d,3.3,angle)
lighthouse()
for p in [(-37,11,5),(36,9,4),(14,22,6.2)]: tower(p)

# Piers and side berths leave the central long approach clear for the playable ship.
dock(-15,-16,15,5); dock(1,-18,18,5); dock(17,-15,15,5)
path('Docks',[(-19,-19,1.25),(-9,-21,1.25),(-3,-21,1.25)],4.0,4,.35)
for x,y,a,c in [(-20,-27,.07,13),(-9,-29,-.08,12),(6,-31,.1,13),(22,-26,-.1,14)]: boat(x,y,a,c)
for x,y,z in [(-16,-19,1.25),(-1,-23,1.25),(17,-18,1.25),(-34,5,5),(35,15,4)]:
    crate((x,y,z),1.3,14); crate((x+1.5,y+.2,z),1.1,13); crate((x,y,z+1.3),.95,12)
    barrel((x+2,y+1.6,z)); barrel((x-1.2,y+.5,z),.9)
# Dock crane, pulley and hanging hook.
for x in [-17.2,-12.8]: beam('Props',(x,-23,1.25),(x,-23,6),.19,4,6)
beam('Props',(-17.4,-23,6),(-12.6,-23,6),.20,4,6)
beam('Props',(-15,-23,6),(-15,-23,3.4),.035,2,6)
beam('Props',(-15,-23,3.4),(-14.8,-23,3.1),.08,11,6)
for i in range(6): beam('Props',(-17+i*.7,-23,2.6),(-17+i*.7,-24.8,2.6),.28,4,8)

for x,y,z,h in [(-43,3,5,7),(-39,23,5,7),(-30,17,5,6),(-26,20,0,7),
                (-15,15,6.2,7),(-10,25,6.2,8),(-6,24,11.2,7),(4,23,11.2,6),
                (20,20,6.2,6),(30,24,4,7),(42,16,4,8),(42,1,4,7),(34,-5,4,7),
                (-22,-6,1.2,6),(22,-12,1.2,7),(15,-12,1.2,6)]: palm((x,y,z),h)
for x,y,z,s in [(-43,-3,-.5,3),(-45,-1,-.5,2),(-31,-5,-1,2),(-16,14,6.2,2.4),(-13,16,6.2,3.1),
                (8,10,6.2,2.4),(10,11,6.2,1.8),(9,5,1.2,3),(12,5,1.2,2.2),(16,5,1.2,2.4),
                (32,10,4,2.5),(33,12,4,1.8),(43,-5,-.8,3),(45,-8,-.8,1.8),
                (21,-17,-1,2.2),(24,-16,-1,1.5),(-8,-21,-1,1.5)]:
    rock('Props_Rocks',(x,y,z),(s,s*.8,s*1.25))
for x,y,z in [(-39,17,5),(32,6,4),(13,24,6.2)]:
    for i in range(5): beam('Props',(x+i*.43,y,z+.25),(x+i*.43,y+3,z+.25),.27,4,8)
    for i in range(3): beam('Props',(x+.4+i*.43,y,z+.72),(x+.4+i*.43,y+3,z+.72),.27,4,8)

# Build clean meshes with one atlas material; recalculate exterior normals.
asset_collection=bpy.data.collections.new('Lighthouse Island | Unity export'); scene.collection.children.link(asset_collection)
root=bpy.data.objects.new('LighthouseIsland',None); asset_collection.objects.link(root)
root['metres_per_unit']=1.0; root['reference']='User supplied lighthouse village image'
export_objects=[root]; stats={}
def make_object(name,verts,faces,cols=None,collision_object=False):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    bm=bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free()
    obj=bpy.data.objects.new(name,data); asset_collection.objects.link(obj); obj.parent=root
    if not collision_object:
        data.materials.append(material); uv=data.uv_layers.new(name='AtlasUV')
        for poly,col in zip(data.polygons,cols):
            # Face-local projection fitted inside the tile with an 8-pixel gutter.
            normal=poly.normal; drop=max(range(3),key=lambda i:abs(normal[i])); axes=[i for i in range(3) if i!=drop]
            positions=[data.vertices[data.loops[l].vertex_index].co for l in poly.loop_indices]
            lo=[min(p[a] for p in positions) for a in axes]; hi=[max(p[a] for p in positions) for a in axes]
            for l,p in zip(poly.loop_indices,positions):
                u=(p[axes[0]]-lo[0])/max(hi[0]-lo[0],.001); v=(p[axes[1]]-lo[1])/max(hi[1]-lo[1],.001)
                uv.data[l].uv=((col%4+(8+240*u)/256)/4,(col//4+(8+240*v)/256)/4)
    else:
        obj.hide_render=True; obj.hide_set(True); obj['unity_collider']=True
    data.calc_loop_triangles(); stats[name]={'triangles':len(data.loop_triangles),'vertices':len(data.vertices)}
    export_objects.append(obj); return obj
for name,(v,f,c) in groups.items():
    if name!='CollisionOnly': make_object(name,v,f,c)
for name,(v,f) in collision.items(): make_object('COL_'+name,v,f,collision_object=True)

markers={'DockApproach':(1,-40,.0),'DockLanding':(1,-34,1.25),'PlayerSpawn':(1,-24,1.30),'SeaLevel':(0,0,0),'LighthouseView':(-2,15,11.3)}
for name,p in markers.items():
    obj=bpy.data.objects.new(name,None); asset_collection.objects.link(obj); obj.parent=root; obj.location=p
    obj.empty_display_type='ARROWS'; obj.empty_display_size=1; export_objects.append(obj)

report={'name':'LighthouseIsland','seed':1703,'atlas':'1024x1024 / 16 tiles / one opaque material',
        'visual_triangles':sum(s['triangles'] for n,s in stats.items() if not n.startswith('COL_')),
        'collision_triangles':sum(s['triangles'] for n,s in stats.items() if n.startswith('COL_')),
        'meshes':stats,'routes_blender_xyz':routes,'markers_blender_xyz':markers,
        'export':{'axis_forward':'-Z','axis_up':'Y','bake_space_transform':True,'global_scale':1},
        'notes':['Reference-inspired reconstruction; hidden sides inferred.','Buildings are exterior shells; tower interiors are not playable.',
                 'Collider meshes are separate COL_ children. Use Unity import tool to hide renderers and add static MeshColliders.']}
for name,r in routes.items():
    max_slope=0
    for a,b in zip(r['points'],r['points'][1:]):
        rise=abs(b[2]-a[2]); run=math.hypot(b[0]-a[0],b[1]-a[1]); max_slope=max(max_slope,math.degrees(math.atan2(rise,run)))
    r['max_slope_degrees']=round(max_slope,2)
report['max_route_slope_degrees']=max(r['max_slope_degrees'] for r in routes.values())
(PREVIEW/'LighthouseIsland_Report.json').write_text(json.dumps(report,indent=2),encoding='utf8')

# Export selection only: studio camera, lights and floor never enter the FBX.
bpy.ops.object.select_all(action='DESELECT')
for obj in export_objects: obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'LighthouseIsland.fbx'),use_selection=True,object_types={'MESH','EMPTY'},
    axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,
    add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE',embed_textures=False)
for obj in export_objects:
    if obj.name.startswith('COL_'): obj.hide_set(True)

# Studio presentation stored in .blend, with packed atlas for portability.
studio=bpy.data.collections.new('Studio | excluded from FBX'); scene.collection.children.link(studio)
def studio_obj(name,data,position):
    o=bpy.data.objects.new(name,data); studio.objects.link(o); o.location=position; return o
ground_mesh=bpy.data.meshes.new('Studio floor'); ground_mesh.from_pydata([(-200,-200,-3.3),(200,-200,-3.3),(200,200,-3.3),(-200,200,-3.3)],[],[(0,1,2,3)])
floor=studio_obj('Studio floor',ground_mesh,(0,0,0)); floor_mat=bpy.data.materials.new('Studio warm grey'); floor_mat.diffuse_color=(.36,.40,.41,1); floor.data.materials.append(floor_mat)
world=bpy.data.worlds.new('Soft coastal studio'); world.use_nodes=True; world.node_tree.nodes['Background'].inputs[0].default_value=(.48,.55,.60,1); world.node_tree.nodes['Background'].inputs[1].default_value=.5; scene.world=world
for name,pos,power,size in [('Key',(-40,-55,100),180000,65),('Fill',(60,-10,65),90000,70),('Rim',(0,65,85),140000,55)]:
    light=bpy.data.lights.new(name,'AREA'); light.energy=power; light.shape='DISK'; light.size=size
    o=studio_obj(name,light,pos); o.rotation_euler=(Vector((0,0,4))-o.location).to_track_quat('-Z','Y').to_euler()
camera=studio_obj('Island presentation camera',bpy.data.cameras.new('Island presentation camera'),(100,-135,105))
camera.rotation_euler=(Vector((0,0,5))-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='ORTHO'; camera.data.ortho_scale=122; scene.camera=camera
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
scene.render.resolution_x=1600; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.view_settings.view_transform='AgX'
atlas.pack()
for area in bpy.context.screen.areas if bpy.context.screen else []:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'LighthouseIsland.blend'))
scene.render.filepath=str(PREVIEW/'LighthouseIsland_Preview.png'); bpy.ops.render.render(write_still=True)
camera.location=(0,-48,5); camera.rotation_euler=(Vector((0,8,10))-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='PERSP'; camera.data.lens=27
scene.render.filepath=str(PREVIEW/'LighthouseIsland_Harbour.png'); bpy.ops.render.render(write_still=True)
print('ISLAND_COMPLETE '+json.dumps({k:report[k] for k in ('visual_triangles','collision_triangles','max_route_slope_degrees')}))
