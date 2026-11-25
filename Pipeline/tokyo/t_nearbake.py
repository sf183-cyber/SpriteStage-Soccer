# -*- coding: utf-8 -*-
"""近处的真实建筑：离球场中点 T.NEAR_R（185 m）以内的 PLATEAU 楼（王子公园塔、增上寺、北边那栋 15 m 的楼、西边宝珠院一带……），
在 Blender 里用和全景带同一个外墙材质（t_city.facade_material：真实层高的窗、亮灯、外墙照片的平均颜色），在同样的夜里
（月亮、夜空、四根泛光灯、公园路灯、大路的路灯、增上寺的灯）烘焙光照，写成 Unity 里的三维：一张图集 + 一个网格，和白宫一样。

为什么不放进全景带：足球的镜头会跑到离球场中点三四十米的地方，几十米外的楼贴在全景带上，镜头一动就跟着“游”。

    Blender -b --factory-startup -P t_nearbake.py -- view <方位> <仰角> <视角> <名字> [x y z]   试看：从 (0,-25,3) 透视拍一张 1600x900
    Blender -b --factory-startup -P t_nearbake.py -- bake <图集边长> <采样>                     烘焙 -> out/near_bake.npy、World/tokyo/near.bytes
然后 python3 t_compose.py nearbake unity 冲洗图集（-> World/tokyo/near.jpg）。"""
import bpy, bmesh, sys, os, math, struct, json
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(1, os.path.dirname(HERE))
from n_world import *
import n_layout as U
import t_layout as T
from t_nearbake_lib import build_near, lights

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['view', '0', '4', '60', 'near_view']
MODE = argv[0]


sc = reset()
coll = sc.collection
ground = pbr('ground', (0.035, 0.037, 0.035), 0.9)
quad('ground', [(-600, -600, 0), (600, -600, 0), (600, 600, 0), (-600, 600, 0)], ground)
near = build_near(coll); reds = build_near.reds
lights(sc, coll)

if MODE == 'view':
    az, el, fov = float(argv[1]), float(argv[2]), float(argv[3]); tag = argv[4]
    eye = tuple(float(v) for v in argv[5:8]) if len(argv) >= 8 else (0.0, -25.0, 3.0)
    cd = bpy.data.cameras.new('cam'); cd.angle = math.radians(fov); cd.clip_start = 0.5; cd.clip_end = 5000
    co = bpy.data.objects.new('cam', cd); coll.objects.link(co); sc.camera = co
    co.location = eye; co.rotation_euler = (math.radians(90.0 + el), 0.0, -math.radians(az))
    setup_cycles(sc, 64, 1600, 900, transparent=False, denoise=True)
    render_npy(sc, tag)
    raise SystemExit

# ---- bake: 三角化、展 UV（墙比屋顶密 3 倍），烘焙 COMBINED 到图集
SIZE = int(argv[1]) if len(argv) > 1 else 2048
SAMPLES = int(argv[2]) if len(argv) > 2 else 64
me = near.data
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
bm.to_mesh(me); bm.free(); me.update()
nf = len(me.polygons)
nrm = np.empty(nf * 3, np.float32); me.polygons.foreach_get('normal', nrm); nrm = nrm.reshape(-1, 3)
area = np.empty(nf, np.float32); me.polygons.foreach_get('area', area)
group = np.where(np.abs(nrm[:, 2]) > 0.7, 1, 0)           # 0 墙，1 屋顶
target = (3.0, 1.0)
bpy.ops.object.select_all(action='DESELECT')
near.select_set(True); bpy.context.view_layer.objects.active = near
bk = me.uv_layers.new(name='bake'); me.uv_layers.active = bk          # 烘焙写进“活动的”UV；外墙材质按名字用 'UVMap'
for g in range(2):
    me.polygons.foreach_set('select', group == g); me.update()
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
bk = me.uv_layers['bake']                                 # （进出编辑模式以后旧的引用就失效了）
uvs = np.empty(len(me.loops) * 2, np.float32); bk.data.foreach_get('uv', uvs); uvs = uvs.reshape(-1, 2)
ls = np.empty(nf, np.int32); me.polygons.foreach_get('loop_start', ls)
tri = ls[:, None] + np.arange(3)[None, :]
a_, b_, c_ = uvs[tri[:, 0]], uvs[tri[:, 1]], uvs[tri[:, 2]]
uv_area = 0.5 * np.abs((b_[:, 0] - a_[:, 0]) * (c_[:, 1] - a_[:, 1]) - (b_[:, 1] - a_[:, 1]) * (c_[:, 0] - a_[:, 0]))
for g in range(2):
    sel = group == g
    if not sel.any(): continue
    dens = float(uv_area[sel].sum() / max(float(area[sel].sum()), 1e-9))
    k = math.sqrt(target[g] / max(dens, 1e-12))
    uvs[tri[sel].reshape(-1)] = uvs[tri[sel].reshape(-1)] * k + np.array([g * 1000.0, 0.0], np.float32)
    print('uv class', g, 'faces', int(sel.sum()), 'area %.0f m2' % float(area[sel].sum()))
bk.data.foreach_set('uv', uvs.reshape(-1))
me.polygons.foreach_set('select', np.ones(nf, bool))
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True, margin=0.0015)
bpy.ops.object.mode_set(mode='OBJECT')
bk = me.uv_layers['bake']; me.uv_layers.active = bk
img = bpy.data.images.new('near_bake', SIZE, SIZE, alpha=False, float_buffer=True)
for slot in near.material_slots:
    nt = slot.material.node_tree
    nn = nt.nodes.new('ShaderNodeTexImage'); nn.image = img
    nt.nodes.active = nn
setup_cycles(sc, SAMPLES, SIZE, SIZE, transparent=False, denoise=True)
sc.cycles.use_adaptive_sampling = False
sc.render.bake.margin = max(3, SIZE // 512)
sc.render.bake.use_clear = True
sc.render.bake.target = 'IMAGE_TEXTURES'
print('baking', SIZE, SAMPLES)
bpy.ops.object.bake(type='COMBINED')
buf = np.empty(SIZE * SIZE * 4, np.float32); img.pixels.foreach_get(buf)
arr = buf.reshape(SIZE, SIZE, 4)[::-1]
np.save(os.path.join(U.OUT, 'nearbake.npy'), arr[:, :, :3].astype(np.float16))
print('BAKE_OK mean', float(arr[:, :, :3].mean()))
co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
li = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', li)
bk.data.foreach_get('uv', uvs.reshape(-1)); uvs = uvs.reshape(-1, 2)
order = (ls[:, None] + np.array([0, 2, 1], np.int32)[None, :]).reshape(-1)
P = co[li[order]]; P = np.stack([P[:, 0], P[:, 2], P[:, 1]], 1).astype(np.float32)
os.makedirs(T.UNITY_WORLD, exist_ok=True)
with open(os.path.join(T.UNITY_WORLD, 'near.bytes'), 'wb') as f:
    f.write(b'G3M1'); f.write(struct.pack('<i', P.shape[0])); f.write(P.tobytes()); f.write(uvs[order].astype(np.float32).tobytes())
json.dump(dict(red=[dict(x=r[0], y=r[1], z=r[2]) for r in reds]), open(os.path.join(T.UNITY_WORLD, 'near_lights.json'), 'w'))     # 高楼顶上的航空障碍灯（Unity 里画光晕）
print('MESH_OK near verts', P.shape[0])
