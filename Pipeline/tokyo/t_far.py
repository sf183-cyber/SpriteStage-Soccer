# -*- coding: utf-8 -*-
"""芝公园的远景草坪（正上方俯拍，公园那块 T.PARK_X0..X1 x T.PARK_Y0..Y1，外加 2.5 m）-> out/far.npy（线性）。
（2026-11-24）公园是不对称的长方形、真实的椭圆草坪的环路、四根足球场的灯杆、
近处的真实建筑（t_nearbake.build_near）和公园外的树也投影子、透光。

    Blender -b --factory-startup -P t_far.py -- <分辨率> <采样> [名字]

只有地面：草、公园的路，躺在上面的是树影（月光、灯杆的外溢光、公园路灯）和路灯的光池。
树（和 Unity 里同一批位置的扫描树）、灯杆只投影子，镜头看不见。光和近景草坪一样（u_world.night()），
只是去掉了照白宫的那排地灯，加上公园的路灯。"""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.dirname(HERE))
import t_layout as T
import n_world as UW
from n_world import reset, col, pbr, quad, add_obj, place_asset, setup_cycles

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
RES = int(argv[0]) if len(argv) > 0 else 1024
SAMPLES = int(argv[1]) if len(argv) > 1 else 32
TAG = argv[2] if len(argv) > 2 else 'far_test'

sc = reset()
UW.night(sc)
for o in [o for o in bpy.data.objects if o.name.startswith('wash')]:          # 白宫的地灯不要
    bpy.data.objects.remove(o, do_unlink=True)
UW.masts()

# 草：沿用篮球/白宫工程远景的草地材质（u_far.py 里的那个），这里直接写一份简化的
def turf_material():
    m = bpy.data.materials.new('turf'); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = nt.nodes.new('ShaderNodeTexCoord')
    n1 = nt.nodes.new('ShaderNodeTexNoise'); n1.inputs['Scale'].default_value = 0.07; n1.inputs['Detail'].default_value = 6
    n2 = nt.nodes.new('ShaderNodeTexNoise'); n2.inputs['Scale'].default_value = 1.2; n2.inputs['Detail'].default_value = 5
    nt.links.new(tc.outputs['Object'], n1.inputs['Vector']); nt.links.new(tc.outputs['Object'], n2.inputs['Vector'])
    mix = nt.nodes.new('ShaderNodeMath'); mix.operation = 'MULTIPLY_ADD'; mix.inputs[1].default_value = 0.7
    nt.links.new(n1.outputs['Fac'], mix.inputs[0]); nt.links.new(n2.outputs['Fac'], mix.inputs[2])
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.35; ramp.color_ramp.elements[0].color = (0.020, 0.046, 0.008, 1)
    ramp.color_ramp.elements[1].position = 1.05; ramp.color_ramp.elements[1].color = (0.070, 0.135, 0.022, 1)
    nt.links.new(mix.outputs[0], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.8
    return m

P = T.FAR_HALF; FX, FY = T.FAR_CX, T.FAR_CY
quad('turf', [(FX - P - 60, FY - P - 60, 0), (FX + P + 60, FY - P - 60, 0), (FX + P + 60, FY + P + 60, 0), (FX - P - 60, FY + P + 60, 0)], turf_material())
paving = pbr('paving', (0.16, 0.15, 0.14), 0.85)

def ring(name, cx, cy, rx, ry, w, z, mat, n=200, rot=0.0):
    me = bpy.data.meshes.new(name); vs, fs = [], []
    cr, sr = math.cos(rot), math.sin(rot)
    def R(x, y): return (cx + x * cr - y * sr, cy + x * sr + y * cr, z)
    for i in range(n):
        a = 2 * math.pi * i / n; c, s = math.cos(a), math.sin(a)
        vs.append(R((rx - w / 2) * c, (ry - w / 2) * s)); vs.append(R((rx + w / 2) * c, (ry + w / 2) * s))
    for i in range(n):
        j = (i + 1) % n; fs.append((2 * i, 2 * i + 1, 2 * j + 1, 2 * j))
    me.from_pydata(vs, [], fs)
    return add_obj(name, me, (mat,))

for kind, p in T.PATHS:
    if kind in ('ring', 'ellipse'):
        ring('path', p['cx'], p['cy'], p['rx'], p['ry'], p['w'], 0.012, paving, rot=p.get('rot', 0.0))
    else:
        dx, dy = p['x1'] - p['x0'], p['y1'] - p['y0']; L = math.hypot(dx, dy); nx, ny = -dy / L * p['w'] / 2, dx / L * p['w'] / 2
        quad('path', [(p['x0'] + nx, p['y0'] + ny, 0.012), (p['x0'] - nx, p['y0'] - ny, 0.012), (p['x1'] - nx, p['y1'] - ny, 0.012), (p['x1'] + nx, p['y1'] + ny, 0.012)], paving)

# 公园边上的绿篱（Unity 里也有；这里只投影子）
hedge = pbr('hedge', (0.02, 0.05, 0.015), 0.9)
X0, X1, Y0, Y1 = T.PARK_X0, T.PARK_X1, T.PARK_Y0, T.PARK_Y1
for (x0, y0, x1, y1) in ((X0, Y1 - 0.6, X1, Y1 + 0.6), (X0, Y0 - 0.6, X1, Y0 + 0.6), (X0 - 0.6, Y0, X0 + 0.6, Y1), (X1 - 0.6, Y0, X1 + 0.6, Y1)):
    UW.box('hedge', ((x0 + x1) / 2, (y0 + y1) / 2, 0.9), (x1 - x0, y1 - y0, 1.8), hedge, cam_visible=False)

# 树：和 Unity 里同一批（只投影子）
for (sp, x, y, h, v) in T.TREES + T.OUTER_TREES:
    place_asset(T.U.SPECIES[sp], x, y, h, rot_z=T.U.VARIANT_YAW[v], squash=1.1, visible_camera=False)
# 近处的真实建筑（投影子，窗里的灯也照一点地面）、大路的路灯
import t_nearbake_lib as NB
NB.build_near(col()).visible_camera = False
for (x, y) in T.STREET_LAMPS:
    ld = bpy.data.lights.new('sl', 'SPOT'); ld.energy = 900.0; ld.color = (0.95, 0.97, 1.0); ld.shadow_soft_size = 0.25
    ld.spot_size = math.radians(150); ld.spot_blend = 0.5
    lo = bpy.data.objects.new('sl', ld); col().objects.link(lo); lo.location = (x, y, T.STREET_LAMP_H - 0.15)

# 公园路灯：灯柱投影，灯照地
post = pbr('lamppost', (0.05, 0.05, 0.05), 0.6)
for (x, y) in T.PARK_LAMPS:
    UW.cyl('lamppost', (x, y, 0), 0.07, T.PARK_LAMP_H - 0.2, post, cam_visible=False).visible_shadow = False      # 杆顶就在灯下面：投影子的话灯下是一块几米宽的黑斑
    ld = bpy.data.lights.new('parklamp', 'POINT'); ld.energy = T.PARK_LAMP_W; ld.color = T.PARK_LAMP_COLOUR; ld.shadow_soft_size = 0.15
    lo = bpy.data.objects.new('parklamp', ld); col().objects.link(lo); lo.location = (x, y, T.PARK_LAMP_H - 0.1)

cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = 2 * P; cd.clip_start = 1; cd.clip_end = 3000
co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
co_.location = (FX, FY, 500.0)
setup_cycles(sc, SAMPLES, RES, RES, transparent=False, denoise=True)
UW.C.OUT = T.OUT
UW.C.render_npy(sc, TAG)
