# -*- coding: utf-8 -*-
"""公园边的绿篱贴图（2026-11-24）。
第一版的绿篱是 Unity 里一圈深绿色的方块，看着像一道黑墙。这里在 Blender 里搭一段 8 m 长、1.2 m 厚、约 1.8 m 高的
修剪过的绿篱（几十万片小叶子 + 一些枝条，顶上不齐），在均匀的天光下正面、顶面各拍一张（背景透明）。
左右两头的叶子按 8 m 周期复制，所以贴图横向能无缝拼接。Unity 里贴在绿篱的面片上，由 G3Hedge 着色器按场景的灯光打亮。

    Blender -b --factory-startup -P t_hedge.py -- [叶子数]
出 Assets/Resources/World/tokyo/hedge_front.png（1024 x 256）、hedge_top.png（1024 x 160）。"""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import t_layout as T

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
NLEAF = int(argv[0]) if argv else 260000
L, D, H = 8.0, 1.2, 1.8

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
rng = np.random.default_rng(5)


def shape_ok(x, y, z):
    """修剪过的绿篱：截面是上面略收的圆角矩形，顶面高低不平"""
    top = H - 0.10 + 0.08 * math.sin(x * 2.3) + 0.06 * math.sin(x * 5.9 + 1.0)
    half = D / 2 * (1.0 - 0.10 * max(0.0, (z - 1.0)) / (H - 1.0))
    return abs(y) < half and 0.0 < z < top


# 叶子：靠近表面的地方密（外面一层），里面稀
pts = []
while len(pts) < NLEAF:
    x = rng.uniform(0, L); y = rng.uniform(-D / 2, D / 2); z = rng.uniform(0, H)
    if not shape_ok(x, y, z): continue
    depth = min(D / 2 - abs(y), H - z)
    if depth > 0.18 and rng.random() < 0.85: continue
    pts.append((x, y, z))
pts = np.array(pts)
# 两头复制，横向无缝
ext = np.concatenate([pts, pts[pts[:, 0] < 0.4] + [L, 0, 0], pts[pts[:, 0] > L - 0.4] - [L, 0, 0]])
n = len(ext)
size = rng.uniform(0.035, 0.06, n)
# 每片叶子一个小菱形，法线随机（偏向外）
vs = np.zeros((n * 4, 3)); fs = []
for i, (p, sz) in enumerate(zip(ext, size)):
    nrm = rng.normal(0, 1, 3); nrm[1] += -1.5 if p[1] < 0 else 1.5; nrm[2] += 0.8
    nrm /= np.linalg.norm(nrm)
    a = np.cross(nrm, [0, 0, 1.0]); a /= max(np.linalg.norm(a), 1e-6); b = np.cross(nrm, a)
    vs[i * 4 + 0] = p + a * sz; vs[i * 4 + 1] = p + b * sz * 0.55; vs[i * 4 + 2] = p - a * sz; vs[i * 4 + 3] = p - b * sz * 0.55
    fs.append((i * 4, i * 4 + 1, i * 4 + 2, i * 4 + 3))
me = bpy.data.meshes.new('leaves'); me.from_pydata(vs.tolist(), [], fs)
col = np.repeat(rng.uniform(0.0, 1.0, n), 4)
ca = me.color_attributes.new('tone', 'FLOAT_COLOR', 'POINT')
rgba = np.zeros((n * 4, 4), np.float32); rgba[:, 0] = col; rgba[:, 3] = 1
ca.data.foreach_set('color', rgba.ravel())
ob = bpy.data.objects.new('leaves', me); sc.collection.objects.link(ob)
m = bpy.data.materials.new('leaf'); m.use_nodes = True
nt = m.node_tree; bsdf = nt.nodes['Principled BSDF']
attr = nt.nodes.new('ShaderNodeAttribute'); attr.attribute_name = 'tone'
ramp = nt.nodes.new('ShaderNodeValToRGB')
ramp.color_ramp.elements[0].color = (0.018, 0.050, 0.012, 1); ramp.color_ramp.elements[1].color = (0.060, 0.130, 0.030, 1)
nt.links.new(attr.outputs['Fac'], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.55
try:
    bsdf.inputs['Subsurface Weight'].default_value = 0.0
except KeyError:
    pass
me.materials.append(m)
# 里面的枝条（深褐）
br = bpy.data.materials.new('twig'); br.use_nodes = True
br.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.03, 0.022, 0.015, 1)
for k in range(140):
    x = rng.uniform(0, L)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.012, depth=1.4, location=(x, rng.uniform(-0.3, 0.3), 0.7),
                                        rotation=(rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 0))
    bpy.context.object.data.materials.append(br)

# 均匀的天光（拍出来近似“反照率 x 遮挡”，夜里的光由 Unity 着色器加）
world = bpy.data.worlds.new('w'); sc.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (1, 1, 1, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0
sc.render.engine = 'CYCLES'
prefs = bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type = 'METAL'; prefs.get_devices()
for d in prefs.devices: d.use = (d.type == 'METAL')
sc.cycles.device = 'GPU'; sc.cycles.samples = 96; sc.cycles.use_denoising = True
sc.render.film_transparent = True
sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'


def shoot(name, loc, rot, w, h, scale):
    cam = bpy.data.cameras.new(name); cam.type = 'ORTHO'; cam.ortho_scale = scale
    co = bpy.data.objects.new(name, cam); sc.collection.objects.link(co); sc.camera = co
    co.location = loc; co.rotation_euler = rot
    sc.render.resolution_x, sc.render.resolution_y = w, h
    out = os.path.join(T.UNITY_WORLD, name + '.png')
    sc.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print('HEDGE_OK', out)


os.makedirs(T.UNITY_WORLD, exist_ok=True)
# 正面：横向正好 8 m，竖向 2 m（底 0 顶 2）
shoot('hedge_front', (L / 2, -5.0, 1.0), (math.radians(90), 0, 0), 1024, 256, L)
# 顶面：横向 8 m，纵向 1.25 m
shoot('hedge_top', (L / 2, 0.0, 6.0), (0, 0, 0), 1024, 160, L)
