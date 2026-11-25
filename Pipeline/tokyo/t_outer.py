# -*- coding: utf-8 -*-
"""公园外、离球场中点 T.OUTER_R（200 m）以内的地面（正上方俯拍）-> out/outer.npy（线性）。

足球场的东京塔场景里，这一圈地面是 Unity 里的一块贴图：上面站着真的三维的楼
（t_nearbake）、真的公告板树、真的路灯。这里只拍地面——草地 / 林下（和远景草坪同一个材质）、大路（沥青）、公园的小路——
躺在上面的是楼、树、灯杆的影子和所有灯的光池（月亮、四根泛光灯、公园路灯、大路的路灯、楼里透出来的灯、增上寺的灯）。

    Blender -b --factory-startup -P t_outer.py -- <分辨率> <采样> [名字]"""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(1, os.path.dirname(HERE))
import t_layout as T
import n_world as UW
from n_world import reset, col, pbr, quad, add_obj, place_asset, setup_cycles
import t_nearbake_lib as NB

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
RES = int(argv[0]) if len(argv) > 0 else 1024
SAMPLES = int(argv[1]) if len(argv) > 1 else 32
TAG = argv[2] if len(argv) > 2 else 'outer_test'

sc = reset()
coll = sc.collection
R = T.OUTER_R

# 地面：草（和 t_far 的远景草坪同一个材质）+ 大路 + 公园的小路
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
    ramp.color_ramp.elements[0].position = 0.35; ramp.color_ramp.elements[0].color = (0.020, 0.040, 0.010, 1)
    ramp.color_ramp.elements[1].position = 1.05; ramp.color_ramp.elements[1].color = (0.060, 0.105, 0.025, 1)
    nt.links.new(mix.outputs[0], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.85
    return m

quad('turf', [(-R - 40, -R - 40, 0), (R + 40, -R - 40, 0), (R + 40, R + 40, 0), (-R - 40, R + 40, 0)], turf_material())
asphalt = pbr('asphalt', (0.040, 0.040, 0.042), 0.75)
paving = pbr('paving', (0.16, 0.15, 0.14), 0.85)
kerb = pbr('pavement', (0.11, 0.105, 0.10), 0.85)

def strip(name, pts, half, z, mat):
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0)
        if L < 0.5: continue
        nx, ny = -(y1 - y0) / L * half, (x1 - x0) / L * half
        ex, ey = (x1 - x0) / L * half * 0.3, (y1 - y0) / L * half * 0.3          # 两段之间稍微重叠，拐角不留缝
        quad(name, [(x0 + nx - ex, y0 + ny - ey, z), (x0 - nx - ex, y0 - ny - ey, z), (x1 - nx + ex, y1 - ny + ey, z), (x1 + nx + ex, y1 + ny + ey, z)], mat)

for pts, half in T._road_lines():
    if min(math.hypot(x, y) for x, y in pts) > R + 300: continue
    strip('sidewalk', pts, half + 3.5, 0.008, kerb)
    strip('road', pts, half, 0.016, asphalt)
for kind, p in T.PATHS:
    if kind == 'line':
        strip('path', [(p['x0'], p['y0']), (p['x1'], p['y1'])], p['w'] / 2, 0.012, paving)

# 投影子、发光的东西（镜头都看不见）
hedge = pbr('hedge', (0.02, 0.05, 0.015), 0.9)
X0, X1, Y0, Y1 = T.PARK_X0, T.PARK_X1, T.PARK_Y0, T.PARK_Y1
for (x0, y0, x1, y1) in ((X0, Y1 - 0.6, X1, Y1 + 0.6), (X0, Y0 - 0.6, X1, Y0 + 0.6), (X0 - 0.6, Y0, X0 + 0.6, Y1), (X1 - 0.6, Y0, X1 + 0.6, Y1)):
    UW.box('hedge', ((x0 + x1) / 2, (y0 + y1) / 2, 0.9), (x1 - x0, y1 - y0, 1.8), hedge, cam_visible=False)
near = NB.build_near(coll); near.visible_camera = False
NB.lights(sc, coll)                                    # 月亮、夜空、泛光灯和灯杆、公园路灯、大路路灯、增上寺的灯、树
post = pbr('lamppost', (0.05, 0.05, 0.05), 0.6)
for (x, y) in T.PARK_LAMPS:
    UW.cyl('lamppost', (x, y, 0), 0.07, T.PARK_LAMP_H - 0.2, post, cam_visible=False).visible_shadow = False      # 杆顶就在灯下面：投影子的话灯下是一块几米宽的黑斑
for (x, y) in T.STREET_LAMPS:
    UW.cyl('lamppost', (x, y, 0), 0.09, T.STREET_LAMP_H - 0.45, post, cam_visible=False).visible_shadow = False      # 同上

cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = 2 * R; cd.clip_start = 1; cd.clip_end = 3000
co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
co_.location = (0.0, 0.0, 500.0)
setup_cycles(sc, SAMPLES, RES, RES, transparent=False, denoise=True)
UW.render_npy(sc, TAG)
