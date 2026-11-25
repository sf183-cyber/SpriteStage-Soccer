# -*- coding: utf-8 -*-
"""把 PLATEAU 的真实建筑（t_plateau.py 解析好的 out/plateau_bldg.pkl）加进城市网格（Blender 里运行）。

- 墙：LOD2 的真实墙面。UV 按“沿墙的米数、离地的米数”算，窗户、楼层由 t_city 的外墙材质画；
  层高 = PLATEAU 的楼高 / 地上层数，所以亮灯的楼层和真楼一样。
- 屋顶：LOD2 的真实屋顶形状，深灰、没有窗。
- 用途（建物利用現況）决定外墙：业务/官公厅 -> 办公（60 m 以上是玻璃幕墙），共同住宅/住宅 -> 住宅，宿泊 -> 酒店，商业 -> 底层亮。
- 东京塔：PLATEAU 里塔和 FootTown 是同一栋楼，只留 30 m 以下（FootTown），塔用我们自己的格构模型。
- 增上寺：大殿和三解脱门附近的楼当成寺院（木墙、深色瓦顶、没有窗），打灯沿用 t_landmarks.zojoji 的灯。
- 公园（Unity 画的那块）里的小建筑不要。"""
import os, math, pickle
import numpy as np
import t_geo as G
import t_layout as T
import t_landmarks as LM

HERE = os.path.dirname(os.path.abspath(__file__))

# PLATEAU 建物利用現況コード（bldg:usage）
OFFICE = {'401', '404', '431'}           # 業務施設、商業系複合施設、官公庁施設
SHOP = {'402'}                           # 商業施設
HOTEL = {'403'}                          # 宿泊施設
HOME = {'411', '412', '413', '414'}      # 住宅、共同住宅、店舗等併用住宅、店舗等併用共同住宅


def kind_of(b):
    u, h = b['usage'] or '', b['height']
    if u in HOTEL: return 'hotel'
    if u in HOME: return 'res' if h < 60 else 'restower'
    if u in OFFICE or u in SHOP: return 'glass' if h >= 60 else 'office'
    return 'glass' if h >= 90 else ('office' if h >= 25 else 'mid')


def facade(kind, seed, fh, rng):
    if kind in LM.FACADES:
        return LM.attrs(kind, seed, fh, rng)
    if kind == 'restower':          # 高层公寓：暖光、阳台开间
        a = LM.attrs('hotel', seed, fh, rng); a.update(ww=3.4, warm=0.75, litp=0.45, wr=0.20, wg=0.20, wb=0.19)
        return a
    if kind == 'res':
        a = LM.attrs('hotel', seed, fh, rng); a.update(ww=float(rng.uniform(3.2, 4.2)), mu=0.10, mv0=0.32, mv1=0.80, warm=0.80, litp=float(rng.uniform(0.35, 0.6)), bright=float(rng.uniform(0.5, 0.9)))
        c = rng.uniform(0.10, 0.24); a.update(wr=c, wg=c * 0.98, wb=c * 0.94)
        return a
    if kind == 'mid':
        a = LM.attrs('concrete', seed, fh, rng); a.update(ww=float(rng.uniform(2.2, 3.2)), mu=0.14, mv0=0.28, mv1=0.86, warm=float(rng.uniform(0.15, 0.5)))
        c = rng.uniform(0.06, 0.18); a.update(wr=c, wg=c, wb=c)
        return a
    if kind == 'temple':
        # 寺院：深色木墙，一排排格子门，门里透出一点暖光（夜里的增上寺）
        a = LM.attrs('concrete', seed, 40.0, rng)          # 一“层”40 m：门只在离地 0.8–7 m 那一排，上面的墙和山墙没有窗
        a.update(ww=2.4, mu=0.10, mv0=0.02, mv1=0.175, grp=1.0, warm=1.0, litp=0.6, bright=0.25, glass=0.0, wr=0.11, wg=0.065, wb=0.042, kind=3.0)
        return a
    return LM.attrs('office', seed, fh, rng)


def wall_uv(ring, u_off):
    """墙面的 UV：u = 沿墙水平方向的米数（从墙的一头量），v = 离地高度"""
    pts = np.array(ring)
    low = pts[np.argsort(pts[:, 2])[:2]]
    d = low[1, :2] - low[0, :2]
    if np.linalg.norm(d) < 0.3:
        # 竖条形的墙：找水平跨度最大的两点
        i, j = np.unravel_index(np.argmax(np.linalg.norm(pts[:, None, :2] - pts[None, :, :2], axis=2)), (len(pts), len(pts)))
        d = pts[j, :2] - pts[i, :2]
    n = np.linalg.norm(d)
    d = d / n if n > 1e-6 else np.array([1.0, 0.0])
    u = (pts[:, :2] - pts[0, :2]) @ d
    u = u - u.min() + u_off
    return [(float(a), float(z)) for a, z in zip(u, pts[:, 2])]


DETAILED = []          # 做立体立面的楼：(b, kind, fh, a, surfs)，给 t_facade3d


_ATLAS = None; _TEX = None; _IMG = {}
ATLAS_SIZE = 8192
PHOTO_DENSITY = 2.5        # 照片在墙上每米至少这么多像素，才用照片里的窗（否则只取颜色）


try:
    PCOL = pickle.load(open(os.path.join(HERE, 'out', 'plateau_pcolor.pkl'), 'rb'))     # 多边形 -> 外墙照片的平均颜色（线性）
except FileNotFoundError:
    PCOL = {}


def photo_info(pid, pts):
    """这个多边形有没有外墙照片：返回 (模式 0/1/2, 图集号, 图集 UV 列表, 照片平均亮度)"""
    global _ATLAS, _TEX
    import json
    if _ATLAS is None:
        try:
            _ATLAS = json.load(open(os.path.join(HERE, 'out', 'plateau_atlas.json')))
            _TEX = pickle.load(open(os.path.join(HERE, 'out', 'plateau_tex.pkl'), 'rb'))
        except FileNotFoundError:
            _ATLAS, _TEX = {}, {}
    if pid is None or pid not in _TEX: return 0, 0, None, 0.2
    img, uv = _TEX[pid]
    if img not in _ATLAS or len(uv) != len(pts): return 0, 0, None, 0.2
    ai, x0, y0, w, h, ow, oh = _ATLAS[img]
    auv = [((x0 + u * w) / ATLAS_SIZE, 1.0 - (y0 + (1.0 - v) * h) / ATLAS_SIZE) for (u, v) in uv]
    U = np.array(uv)
    a_uv = 0.5 * abs(np.dot(U[:, 0], np.roll(U[:, 1], 1)) - np.dot(U[:, 1], np.roll(U[:, 0], 1)))
    P = np.array(pts); n = np.zeros(3)
    for i in range(len(P)): n += np.cross(P[i], P[(i + 1) % len(P)])
    area = np.linalg.norm(n) / 2
    dens = math.sqrt(a_uv * ow * oh / max(area, 1e-3))
    # 平均亮度（线性）：t_atlas.py 预先算好的（Blender 自带的 Python 没有 PIL）
    if not _IMG:
        _IMG.update(pickle.load(open(os.path.join(HERE, 'out', 'plateau_pmean.pkl'), 'rb')))
    pm = _IMG.get(pid, 0.2)
    return (2 if dens >= PHOTO_DENSITY else 1), ai, auv, pm


def build(bl, rng=None):
    """返回 occupied（每栋楼的 (x, y, 半径)），给填充的楼、树避让。立体立面的楼记在 DETAILED。"""
    import t_facade3d as F3
    DETAILED.clear()
    rng = rng or np.random.default_rng(73)
    B = pickle.load(open(os.path.join(HERE, 'out', 'plateau_bldg.pkl'), 'rb'))
    dx, dy = G.scene(*G.ZOJOJI_DAIDEN); gx, gy = G.scene(*G.ZOJOJI_GATE)
    occupied = []
    nfaces = 0; nb = 0
    for b in B:
        cx, cy = b['cx'], b['cy']
        if T.in_park(cx, cy, 2.0):
            continue                                         # 公园里（Unity 那块）的小房子
        if T.is_near(b):
            continue                                         # 足球场：近处的楼是 Unity 里的三维（t_nearbake.py）
        surfs = b['surfs']; pids = b.get('pids') or [None] * len(surfs)
        tower = b['height'] > 300 and math.hypot(cx - 0.0, cy - T.TOWER_D) < 60
        if tower:
            keep = [i for i, (k, r) in enumerate(surfs) if max(p[2] for p in r) <= 30.0]       # 只留 FootTown
            surfs = [surfs[i] for i in keep]; pids = [pids[i] for i in keep]
            kind = 'foottown'; h = 24.0; st = 5
        else:
            h = b['height']; st = b['storeys']
            # 增上寺院内（大殿周围 130 m、三解脱门周围 30 m）的低楼都当寺院：PLATEAU 里大殿、安国殿等连成一个 35 m 高的综合体，
            # 用途还标的是 422；按普通楼画会满墙大窗
            in_temple = (math.hypot(cx - dx, cy - dy) < 130 and h < 40) or math.hypot(cx - gx, cy - gy) < 30
            kind = 'temple' if in_temple else kind_of(b)
        fh = h / st if st and 0 < st < 150 else {'res': 3.0, 'restower': 3.2, 'hotel': 3.4}.get(kind, 3.9)
        fh = min(max(fh, 2.8), 6.0) if kind != 'temple' else 40.0
        a = facade(kind, int(rng.integers(0, 100000)), fh, rng)
        detailed = F3.selected(b, kind)
        pmodes = [0] * len(surfs)
        if detailed:
            DETAILED.append((b, kind, fh, dict(a), surfs, pmodes))     # pmodes 在下面放墙时填（照片模式的墙不放立体窗）
            a = dict(a); a.update(mv0=2.0, mv1=1.0)              # 墙上不再画窗：窗是 t_facade3d 的几何体（或照片里的窗）
        roof = dict(a); roof.update(mv0=2.0, mv1=1.0, wr=0.06, wg=0.06, wb=0.065, glass=0.0)
        if kind == 'temple':
            roof.update(wr=0.03, wg=0.032, wb=0.034)
        u_off = 0.0
        for si, ((k, r), pid) in enumerate(zip(surfs, pids)):
            if k == 'ground' or len(r) < 3:
                continue
            pts = [(p[0], p[1], p[2]) for p in r]
            if k == 'wall':
                # 斜面（法线朝上）不画窗：当屋顶
                P = np.array(pts); nrm = np.zeros(3)
                for i in range(len(P)):
                    a0, a1 = P[i], P[(i + 1) % len(P)]
                    nrm += np.array([(a0[1] - a1[1]) * (a0[2] + a1[2]), (a0[2] - a1[2]) * (a0[0] + a1[0]), (a0[0] - a1[0]) * (a0[1] + a1[1])])
                nn = np.linalg.norm(nrm)
                if nn > 1e-9 and abs(nrm[2] / nn) > 0.35:
                    k = 'roof'
            if k == 'wall':
                uv = wall_uv(pts, u_off); u_off += 13.7
                if detailed and kind != 'temple' and pid in PCOL and all(math.isfinite(v) for v in PCOL[pid]):
                    # 2026-11-24：试过直接贴照片、把照片里暗的地方当窗点亮（photo_info 的模式 2），但照片分辨率低、白天有反光和阴影，
                    # 出来一块块花斑。现在只取这面墙照片的平均颜色，当真实的外墙颜色；窗仍是 t_facade3d 的立体窗
                    # 照片是白天拍的，偏亮、带天光的蓝：去掉一半饱和度、压到 0.42（0.75 时王子公园塔在月光下是一面亮蓝的墙）
                    c = np.array(PCOL[pid]); l = float(c @ np.array([0.2126, 0.7152, 0.0722]))
                    c = np.clip((l + (c - l) * 0.5) * 0.42, 0.015, 0.35)
                    aa = dict(a); aa.update(wr=float(c[0]), wg=float(c[1]), wb=float(c[2]))
                    bl.face(pts, uv, aa)
                    nfaces += 1
                    continue
                bl.face(pts, uv, a)
            else:
                bl.face(pts, [(0.0, 0.0)] * len(pts), roof)
            nfaces += 1
        nb += 1
        if not tower and h > 60:
            top = [p for (k, r) in surfs if k == 'roof' for p in r]
            if top:
                zt = max(p[2] for p in top)
                tp = np.array([p for p in top if p[2] > zt - 3.0])
                if len(tp) >= 2:
                    for q in (tp[np.argmin(tp[:, 0])], tp[np.argmax(tp[:, 0])], tp[np.argmin(tp[:, 1])], tp[np.argmax(tp[:, 1])]):
                        bl.red.append((float(q[0]), float(q[1]), float(q[2]) + 1.0))
        occupied.append((cx, cy, b['r']))
    print('PLATEAU_BUILT buildings %d faces %d' % (nb, nfaces))
    return occupied
