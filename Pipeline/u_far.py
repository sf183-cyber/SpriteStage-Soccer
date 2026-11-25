# -*- coding: utf-8 -*-
"""The whole south lawn seen from straight above -> Pipeline/out/far.npy (scene-linear).

    Blender -b --factory-startup -P u_far.py -- <resolution> <samples> [tag]

Ground only: turf, the South Drive round the oval lawn and the cross drive - with the shadows of every
tree and of the house lying on it.  Everything that stands up is invisible to this camera."""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import u_layout as L
from u_world import *

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
RES = int(argv[0]) if len(argv) > 0 else 1024
SAMPLES = int(argv[1]) if len(argv) > 1 else 32
TAG = argv[2] if len(argv) > 2 else 'far_test'

sc = reset()
daylight(sc)

# the measured house (wh_model.py) only throws its shadow here
import wh_model as WH
MB_MAIN, MB_DETAIL = WH.build()
me = bpy.data.meshes.new('house'); me.from_pydata(MB_MAIN.v, [], MB_MAIN.f); me.update()
hob = bpy.data.objects.new('house', me); col().objects.link(hob)
me.materials.append(pbr('house', (0.7, 0.7, 0.68), 0.6))
hob.location = (0.0, L.HOUSE_Y, 0.0); hob.visible_camera = False


def turf_material():
    """Mown turf as it looks from far enough that single blades are gone: the two stripe directions, patchy colour."""
    m, nt, b = new_mat('turf')
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Object'], sep.inputs['Vector'])
    def stripes(sock, period, phase):
        a = nt.nodes.new('ShaderNodeMath'); a.operation = 'ADD'; a.inputs[1].default_value = phase
        nt.links.new(sock, a.inputs[0])
        d = nt.nodes.new('ShaderNodeMath'); d.operation = 'DIVIDE'; d.inputs[1].default_value = period
        nt.links.new(a.outputs[0], d.inputs[0])
        f = nt.nodes.new('ShaderNodeMath'); f.operation = 'FLOOR'; nt.links.new(d.outputs[0], f.inputs[0])
        mo = nt.nodes.new('ShaderNodeMath'); mo.operation = 'MODULO'; mo.inputs[1].default_value = 2.0
        nt.links.new(f.outputs[0], mo.inputs[0])
        return mo.outputs[0]
    sy = stripes(sep.outputs['Y'], 1.5, L.HW + 300.0)
    sx = stripes(sep.outputs['X'], 2.0, 400.0)
    n1 = nt.nodes.new('ShaderNodeTexNoise'); n1.inputs['Scale'].default_value = 0.07; n1.inputs['Detail'].default_value = 6
    n2 = nt.nodes.new('ShaderNodeTexNoise'); n2.inputs['Scale'].default_value = 1.2; n2.inputs['Detail'].default_value = 5
    nt.links.new(tc.outputs['Object'], n1.inputs['Vector']); nt.links.new(tc.outputs['Object'], n2.inputs['Vector'])
    def lin(sock, k, c=0.0):
        mth = nt.nodes.new('ShaderNodeMath'); mth.operation = 'MULTIPLY_ADD'; mth.inputs[1].default_value = k; mth.inputs[2].default_value = c
        nt.links.new(sock, mth.inputs[0]); return mth.outputs[0]
    def add(a_, b_):
        mth = nt.nodes.new('ShaderNodeMath'); mth.operation = 'ADD'
        nt.links.new(a_, mth.inputs[0]); nt.links.new(b_, mth.inputs[1]); return mth.outputs[0]
    v = add(add(lin(sy, -0.11, 0.56), lin(sx, -0.03)), add(lin(n1.outputs['Fac'], 0.40, -0.20), lin(n2.outputs['Fac'], 0.16, -0.08)))
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (0.020, 0.046, 0.008, 1)
    ramp.color_ramp.elements[1].position = 1.0; ramp.color_ramp.elements[1].color = (0.070, 0.135, 0.022, 1)
    nt.links.new(v, ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.75
    return m


def ring(name, cx, cy, rx, ry, w, z, mat, n=160):
    me = bpy.data.meshes.new(name)
    vs, fs = [], []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        vs.append((cx + (rx - w / 2) * c, cy + (ry - w / 2) * s, z)); vs.append((cx + (rx + w / 2) * c, cy + (ry + w / 2) * s, z))
    for i in range(n):
        j = (i + 1) % n
        fs.append((2 * i, 2 * i + 1, 2 * j + 1, 2 * j))
    me.from_pydata(vs, [], fs)
    return add_obj(name, me, (mat,))


cx, cy, half = L.FAR['cx'], L.FAR['cy'], L.FAR['size'] / 2
quad('turf', [(cx - half - 200, cy - half - 200, 0), (cx + half + 200, cy - half - 200, 0), (cx + half + 200, cy + half + 200, 0), (cx - half - 200, cy + half + 200, 0)], turf_material())
gravel = pbr('drive', (0.150, 0.140, 0.125), 0.85)
D = L.DRIVE
ring('drive', D['cx'], D['cy'], D['rx'], D['ry'], D['w'], 0.015, gravel)
box('cross_drive', (0, L.CROSS_DRIVE_Y, 0.014), (230, D['w'], 0.03), gravel)
if os.environ.get('NO_TREES') != '1':
    trees()
goals(); corner_flags()

cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = L.FAR['size']; cd.clip_start = 1; cd.clip_end = 3000
co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
co_.location = (cx, cy, 500.0)
setup_cycles(sc, SAMPLES, RES, RES, transparent=False, denoise=True)
render_npy(sc, TAG)
