# -*- coding: utf-8 -*-
"""Tree impostors: every scanned tree photographed from all round under the scene's own sun.

    Blender -b --factory-startup -P u_trees.py -- <frame width px> <samples>

For each species and each of its two yaw variants: VIEWS pictures, taken from a low angle (10 degrees),
with the sun where it really is - so a tree seen from the north is back-lit and one seen from the west shows
its bright side.  Output: out/tree_<species>_<variant>_<view>.npy (premultiplied, scene-linear) + out/trees.json"""
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import u_layout as L
from u_world import *

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
FW = int(argv[0]) if len(argv) > 0 else 192
SAMPLES = int(argv[1]) if len(argv) > 1 else 32
ONLY = argv[2] if len(argv) > 2 else None
ELEV = 10.0
HREF = 20.0                        # the tree is photographed at this height (metres)


def matte_leaves(mats):
    """The scanned leaves are glossy enough to mirror the sun as white dots when the tree is seen against the
    light; at impostor size those dots read as frost.  Take most of the mirror reflection out."""
    seen = set()
    for o, _ in mats:
        for slot in o.material_slots:
            m = slot.material
            if m is None or m.name in seen or not m.use_nodes:
                continue
            seen.add(m.name)
            nt = m.node_tree
            for n in [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED']:
                sp_in = n.inputs['Specular IOR Level']
                for l in list(sp_in.links):
                    nt.links.remove(l)
                sp_in.default_value = 0.12
                r_in = n.inputs['Roughness']
                if r_in.links:
                    src = r_in.links[0].from_socket
                    mx = nt.nodes.new('ShaderNodeMath'); mx.operation = 'MAXIMUM'; mx.inputs[1].default_value = 0.72
                    nt.links.new(src, mx.inputs[0]); nt.links.new(mx.outputs[0], r_in)
                else:
                    r_in.default_value = max(r_in.default_value, 0.72)
    print('matte leaves:', len(seen), 'materials')


meta = {}
for si, sp in enumerate(('jac', 'small')):
    if ONLY and sp != ONLY:
        continue
    for v in (0, 1):
        sc = reset()
        import wh_common as C
        C._ASSET_CACHE.clear()
        daylight(sc)
        mats, h0, r0, foot = load_asset(L.SPECIES[sp])
        matte_leaves(mats)
        place_asset(L.SPECIES[sp], 0, 0, HREF, rot_z=L.VARIANT_YAW[v], squash=1.1)
        turf = pbr('turf', (0.045, 0.085, 0.016), 0.8)
        quad('ground', [(-300, -300, 0), (300, -300, 0), (300, 300, 0), (-300, 300, 0)], turf, cam_visible=False)
        radius = r0 * (HREF / h0) * 1.1
        fw_m = 2 * radius * 1.08                       # frame width in metres
        el = math.radians(ELEV)
        fh_m = (HREF * math.cos(el) + 2 * radius * math.sin(el)) * 1.06
        FH = int(round(FW * fh_m / fw_m / 8.0)) * 8
        fh_m = fw_m * FH / FW
        base = 0.03 * fh_m                             # the foot of the trunk sits this far above the bottom edge
        cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = max(fw_m, fh_m); cd.clip_start = 1; cd.clip_end = 3000
        co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
        setup_cycles(sc, SAMPLES, FW, FH, transparent=True, denoise=True)
        for k in range(L.VIEWS):
            phi = 2 * math.pi * k / L.VIEWS                 # the camera stands in direction (cos phi, sin phi) from the tree
            d = Vector((math.cos(phi), math.sin(phi), 0.0))
            fwd = (-d * math.cos(el) + Vector((0, 0, -math.sin(el)))).normalized()
            up = (d * (-math.sin(el)) + Vector((0, 0, math.cos(el)))).normalized()
            centre = Vector((0, 0, 0)) + up * (fh_m / 2 - base)        # frame centre: foot of the trunk at 'base' above the bottom
            co_.location = centre - fwd * 600.0
            co_.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
            render_npy(sc, 'tree_%s_%d_%02d' % (sp, v, k))
        meta[sp] = dict(frameW=FW, frameH=FH, widthPerHeight=fw_m / HREF, heightPerHeight=fh_m / HREF, base=base / fh_m)
        print('TREE_OK', sp, v, FW, FH)
old = {}
p = os.path.join(L.OUT, 'trees.json')
if os.path.exists(p):
    old = json.load(open(p))
old.update(meta)
json.dump(old, open(p, 'w'), indent=1)
