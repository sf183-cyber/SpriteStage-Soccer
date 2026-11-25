# -*- coding: utf-8 -*-
"""近处的真实建筑（离球场中点 T.NEAR_R 以内的 PLATEAU 楼）在 Blender 里搭起来、打上夜里的光。t_nearbake.py（烘焙）、
t_far.py、t_outer.py（它们投影子、窗里的灯照地面）都用这里。Blender 里运行。"""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(1, os.path.dirname(HERE))
from n_world import *
import n_world as UW
import n_layout as U
import t_layout as T
import t_geo as G
import t_city as TC
import t_plateau_build as PB
import t_landmarks as LM


def build_near(coll):
    """近处的楼 -> 一个物体（外墙材质）；航空障碍灯的位置记在 build_near.reds"""
    TC.HAZE_L = 1.0e9                                     # 烘焙没有“镜头的距离”：雾霾不要（几百米内本来也只有 2%）
    bl = TC.Buildings()
    rng = np.random.default_rng(91)
    dx, dy = G.scene(*G.ZOJOJI_DAIDEN); gx, gy = G.scene(*G.ZOJOJI_GATE)
    n = 0
    for b in T.near_buildings():
        cx, cy = b['cx'], b['cy']
        surfs = b['surfs']; pids = b.get('pids') or [None] * len(surfs)
        h, st = b['height'], b['storeys']
        in_temple = (math.hypot(cx - dx, cy - dy) < 130 and h < 40) or math.hypot(cx - gx, cy - gy) < 30
        kind = 'temple' if in_temple else PB.kind_of(b)
        fh = h / st if st and 0 < st < 150 else {'res': 3.0, 'restower': 3.2, 'hotel': 3.4}.get(kind, 3.9)
        fh = min(max(fh, 2.8), 6.0) if kind != 'temple' else 40.0
        a = PB.facade(kind, int(rng.integers(0, 100000)), fh, rng)
        roof = dict(a); roof.update(mv0=2.0, mv1=1.0, wr=0.06, wg=0.06, wb=0.065, glass=0.0)
        if kind == 'temple':
            roof.update(wr=0.03, wg=0.032, wb=0.034)
        u_off = 0.0
        for (k, r), pid in zip(surfs, pids):
            if k == 'ground' or len(r) < 3:
                continue
            pts = [(p[0], p[1], p[2]) for p in r]
            if k == 'wall':
                P = np.array(pts); nrm = np.zeros(3)
                for i in range(len(P)):
                    nrm += np.cross(P[i], P[(i + 1) % len(P)])
                nn = np.linalg.norm(nrm)
                if nn > 1e-9 and abs(nrm[2] / nn) > 0.35:
                    k = 'roof'
            if k == 'wall':
                uv = PB.wall_uv(pts, u_off); u_off += 13.7
                aa = a
                if kind != 'temple' and pid in PB.PCOL and all(math.isfinite(v) for v in PB.PCOL[pid]):
                    c = np.array(PB.PCOL[pid]); l = float(c @ np.array([0.2126, 0.7152, 0.0722]))
                    c = np.clip((l + (c - l) * 0.5) * 0.42, 0.015, 0.35)
                    aa = dict(a); aa.update(wr=float(c[0]), wg=float(c[1]), wb=float(c[2]))
                bl.face(pts, uv, aa)
            else:
                bl.face(pts, [(0.0, 0.0)] * len(pts), roof)
        if h > 60:
            top = [p for (k, r) in surfs if k == 'roof' for p in r]
            if top:
                zt = max(p[2] for p in top)
                tp = np.array([p for p in top if p[2] > zt - 3.0])
                for q in (tp[np.argmin(tp[:, 0])], tp[np.argmax(tp[:, 0])], tp[np.argmin(tp[:, 1])], tp[np.argmax(tp[:, 1])]):
                    bl.red.append((float(q[0]), float(q[1]), float(q[2]) + 1.0))
        n += 1
    ob = bl.to_object('Near', TC.facade_material(), coll)
    print('NEAR_BUILT buildings %d faces %d red %d' % (n, len(bl.f), len(bl.red)))
    build_near.reds = bl.red
    return ob


def lights(sc, coll):
    """夜里的光：月亮、夜空、四根泛光灯（UW.night）、公园路灯、大路的路灯、增上寺的灯；灯杆、树只投影子"""
    UW.night(sc)
    UW.masts()
    for (x, y) in T.PARK_LAMPS:
        ld = bpy.data.lights.new('parklamp', 'POINT'); ld.energy = T.PARK_LAMP_W; ld.color = T.PARK_LAMP_COLOUR; ld.shadow_soft_size = 0.15
        lo = bpy.data.objects.new('parklamp', ld); col().objects.link(lo); lo.location = (x, y, T.PARK_LAMP_H - 0.1)
    for (x, y) in T.STREET_LAMPS:
        ld = bpy.data.lights.new('sl', 'SPOT'); ld.energy = 900.0; ld.color = (0.95, 0.97, 1.0); ld.shadow_soft_size = 0.25
        ld.spot_size = math.radians(150); ld.spot_blend = 0.5
        lo = bpy.data.objects.new('sl', ld); col().objects.link(lo); lo.location = (x, y, T.STREET_LAMP_H - 0.15)
    LM.zojoji(coll)                                       # 增上寺的灯（顺带建了三解脱门，在远处，不烘它）
    for (sp, x, y, h, v) in T.TREES + T.OUTER_TREES:
        place_asset(U.SPECIES[sp], x, y, h, rot_z=U.VARIANT_YAW[v], squash=1.1, visible_camera=False)


