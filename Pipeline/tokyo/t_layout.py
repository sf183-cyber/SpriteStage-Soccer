# -*- coding: utf-8 -*-
"""东京塔夜场的全部尺寸（纯 Python）。轴：x 右、y 朝东京塔、z 上，米；足球场中点是原点。

按足球场摆（2026-11-24）：
  - 球场放在プリンス芝公園真实的椭圆草坪上（t_geo.ARENA，离塔 305 m），长边和看塔的方向垂直，默认机位正对着塔
  - 近景草坪（51.2 x 25.6 m，球场画线、球门、角旗、四根泛光灯杆）是 n_lawn_near.py 在夜里的光下烘的
  - 公园（Unity 里的三维：远景草坪、树、路灯、一圈绿篱）是一个不对称的长方形 PARK_BOX：南边 42 m 外就是真实的
    ザ・プリンス パークタワー東京 和酒店的附楼，东边 60 m 外是東京プリンスホテル，所以公园往北（塔的方向）伸得多
  - 公园以外的一切（东京塔、PLATEAU 的楼、远处的树和路）是一圈全景带（t_pano.py），Unity 里贴在“按方向变半径的圆筒”上
    （band_radius()：每个方向上挡在前面的那栋楼有多远，圆筒就放多远；塔的方向放在塔的距离上）。贴在球面上不行：
    足球的镜头会跑到离球场中点三四十米的地方，球面上近处的楼会跟着镜头“游”"""
import math, os, json, sys
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(1, os.path.dirname(HERE))
import n_layout as U

OUT = os.path.join(HERE, 'out')
UNITY_WORLD = os.path.join(U.UNITY_WORLD, 'tokyo')        # Assets/Resources/World/tokyo

import t_geo as G
TOWER_D = G.TOWER_D                 # 305 m
TOWER_X = 0.0
TOWER_ROT = math.degrees(G.scene_rot(G.TOWER_FACE_REAL))

# 公园（Unity 里画的那块）：x0, x1, y0, y1。PLATEAU 里这个范围内只有几个 3-5 m 的小亭子、厕所（不要了）
PARK_X0, PARK_X1, PARK_Y0, PARK_Y1 = -55.0, 55.0, -42.0, 72.0
PARK = 55.0                         # （旧代码里对称的半边长，只剩少数地方用）
FAR_CX, FAR_CY = 0.0, (PARK_Y0 + PARK_Y1) / 2           # 远景草坪贴图：正方形，盖住整个公园再多 2.5 m
FAR_HALF = max(PARK_X1 - PARK_X0, PARK_Y1 - PARK_Y0) / 2 + 2.5
VISTA = 22.0                        # 塔的方向左右这么多度以内，公园边上不种高树


def in_park(x, y, m=0.0):
    return PARK_X0 - m < x < PARK_X1 + m and PARK_Y0 - m < y < PARK_Y1 + m


# 离球场中点 NEAR_R 以内的真实建筑（王子公园塔、增上寺、北边那栋 15 m 的楼……）不进全景带，单独烘焙成 Unity 里真的三维
# （t_nearbake.py）：足球的镜头会跑到离中点三四十米的地方，近处的楼贴在全景带上会跟着镜头“游”。
# 公园外 OUTER_R 以内的地面（路、路灯的光池、树影）也是 Unity 里的一块俯拍贴图（t_outer.py），树是 Unity 里的公告板。
NEAR_R = 185.0
OUTER_R = 200.0
OUTER_RES = 4096


def bldg_min_dist(b):
    return min(math.hypot(q[0], q[1]) for _, r in b['surfs'] for q in r)


def is_near(b):
    """这栋（PLATEAU 的）楼烘成 Unity 里的三维（不在公园里、不是东京塔、离中点 NEAR_R 以内）"""
    if in_park(b['cx'], b['cy'], 2.0) or b['height'] > 300: return False
    return bldg_min_dist(b) < NEAR_R


_BLDG = None
def plateau():
    global _BLDG
    if _BLDG is None:
        import pickle
        _BLDG = pickle.load(open(os.path.join(OUT, 'plateau_bldg.pkl'), 'rb'))
    return _BLDG


def near_buildings():
    return [b for b in plateau() if is_near(b)]


# 全景带：从这一点渲一圈（经度全 360°，纬度 BAND_LO..BAND_HI 度），每度 PX_PER_DEG 像素
BAND_EYE = (0.0, 0.0, 3.0)          # 足球的镜头高（低机位 1-5 m，原版视角 13 m），取 3 m
BAND_LO, BAND_HI = -8.0, 60.0       # 塔顶在 47° 仰角
PX_PER_DEG = 48
BAND_TILES = 4
BAND_R = TOWER_D                    # 塔的方向上圆筒的半径
BAND_STEP = 0.5                     # band_radius() 每多少度一个半径

# 真实的椭圆草坪和它的环路（OpenStreetMap 网页地图上看的：中心 35.65623, 139.74723，东西 65 m、南北 70 m）
_oe, _on = G.en(35.65623, 139.74723)
_c, _s = math.cos(G.BEARING), math.sin(G.BEARING)
OVAL = dict(cx=_oe * _c - _on * _s, cy=_oe * _s + _on * _c, rx=32.5, ry=35.0, rot=G.BEARING, w=2.2)      # rot：东西轴在场景里的方向（弧度）


def _en_to_scene(e, n):
    """相对椭圆草坪中心的 (东, 北) 米 -> 场景坐标"""
    lat, lon = 35.65623 + n / G.M_LAT, 139.74723 + e / G.M_LON
    return G.scene(lat, lon)


def _paths():
    """公园里的路：椭圆草坪的环路 + 几条看地图描的小路（东西南北米，相对椭圆中心）"""
    P = [('ellipse', OVAL)]
    for (a, b, w) in (((8, 36), (110, 30), 2.5),            # 草坪北边往东（通到东边那片草坪）
                      ((-33, -6), (-75, -30), 2.2),         # 西南，往王子公园塔
                      ((4, -35), (22, -70), 2.2),           # 南边
                      ((-20, 30), (-55, 62), 2.2)):         # 西北，往宝珠院
        (x0, y0), (x1, y1) = _en_to_scene(*a), _en_to_scene(*b)
        P.append(('line', dict(x0=x0, y0=y0, x1=x1, y1=y1, w=w)))
    return P


PATHS = _paths()

# 公园的路灯：高 4.2 m，暖白（3000 K）
PARK_LAMP_H = 4.2
PARK_LAMP_W = 300.0
PARK_LAMP_COLOUR = (1.0, 0.78, 0.52)


def _near_pitch(x, y, m=0.0):
    """近景草坪（球场、观众、灯杆）范围内"""
    return abs(x) < 27.0 + m and abs(y) < 17.0 + m


def _ellipse_pt(o, a, grow=0.0):
    ex, ey = (o['rx'] + grow) * math.cos(a), (o['ry'] + grow) * math.sin(a)
    c, s = math.cos(o['rot']), math.sin(o['rot'])
    return o['cx'] + ex * c - ey * s, o['cy'] + ex * s + ey * c


def _lamps():
    L = []
    for k in range(14):
        x, y = _ellipse_pt(OVAL, 2 * math.pi * (k + 0.5) / 14, 2.4)
        if not _near_pitch(x, y, 1.0) and in_park(x, y, -3.0):
            L.append((x, y))
    for kind, p in PATHS[1:]:
        for t in (0.45, 0.85):
            x, y = p['x0'] + (p['x1'] - p['x0']) * t, p['y0'] + (p['y1'] - p['y0']) * t
            dx, dy = p['x1'] - p['x0'], p['y1'] - p['y0']; n = math.hypot(dx, dy)
            x, y = x - dy / n * 2.2, y + dx / n * 2.2
            if in_park(x, y, -3.0) and not _near_pitch(x, y, 1.0):
                L.append((x, y))
    return L


PARK_LAMPS = _lamps()


def _trees():
    """公园里的树（Unity 里的公告板，近景、远景草坪的烘焙里投影子）。地图上：椭圆草坪西边、西南是树林，东边、北边零散几棵；
    塔的方向（±VISTA°）只有矮灌木。"""
    rng = np.random.default_rng(31)
    T = []
    def ok(x, y):
        if _near_pitch(x, y, 9.0) or not in_park(x, y, -3.5): return False             # 离近景草坪 9 m 以上：近了会挡泛光灯，在球场上投下大块树影
        if any(math.hypot(x - f[0], y - f[1]) < 16.0 for f in U.FLOODS): return False
        if any(math.hypot(x - t[1], y - t[2]) < 7.0 for t in T): return False
        for kind, p in PATHS:
            if kind == 'line':
                dx, dy = p['x1'] - p['x0'], p['y1'] - p['y0']; L2 = dx * dx + dy * dy
                t = max(0.0, min(1.0, ((x - p['x0']) * dx + (y - p['y0']) * dy) / L2))
                if math.hypot(x - p['x0'] - t * dx, y - p['y0'] - t * dy) < 4.0: return False
        return True
    def add(sp, x, y, h):
        T.append((sp, float(x), float(y), float(h), int(rng.integers(0, 2))))
    tries = 0
    while len(T) < 46 and tries < 5000:
        tries += 1
        x, y = rng.uniform(PARK_X0, PARK_X1), rng.uniform(PARK_Y0, PARK_Y1)
        az = math.degrees(math.atan2(x, y))
        if not ok(x, y): continue
        w = 0.0
        if x < -36: w = 1.0                               # 西边的树林
        elif y < -30: w = 0.8                             # 南边（挡着王子公园塔的脚）
        elif x > 38: w = 0.55                             # 东边零散
        elif y > 40: w = 0.45                             # 北边
        else: w = 0.12                                    # 椭圆草坪的环路外面
        if rng.random() > w: continue
        if abs(az) < VISTA and y > 0:
            if rng.random() < 0.5: add('small', x, y, rng.uniform(3.5, 5.0))
            continue
        # 镜头常去的地方（默认机位在球场南边 15-35 m、高 10-27 m；球门后面的机位）不种高树，只有灌木：不然镜头会钻进树冠里
        if (abs(x) < 52 and y < -14) or (abs(x) > 28 and abs(y) < 18):
            if rng.random() < 0.35: add('small', x, y, rng.uniform(3.0, 4.5))
            continue
        k = min(1.0, max(0.0, (abs(az) - VISTA) / 25.0)) if y > 0 else 1.0
        add('jac' if rng.random() < 0.65 else 'small', x, y, rng.uniform(11.0, 15.0) + 7.0 * k)
    return T


TREES = _trees()



ROADS = json.load(open(os.path.join(HERE, 'roads.json')))


def _road_lines():
    """roads.json（地图上描的大路）-> [(折线的场景坐标点, 半宽)]"""
    out = []
    for name, road in ROADS.items():
        if name == '_shuto': continue
        half = 11.0 if name in ('sakurada', 'iwaida', 'hibiya', 'kanjo3') else 7.0
        out.append(([G.scene(*ll) for ll in road], half))
    return out


def _street_lamps():
    """OUTER_R 以内大路两边的路灯（每 30 m 一对，8 m 高）。全景带里不再画它们（t_city.ground 跳过 OUTER_R 以内的）"""
    L = []
    for pts, half in _road_lines():
        for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
            d = math.hypot(x1 - x0, y1 - y0)
            if d < 1: continue
            tx, ty = (x1 - x0) / d, (y1 - y0) / d
            for k in range(int(d // 30) + 1):
                x, y = x0 + tx * k * 30, y0 + ty * k * 30
                for side in (-half, half):
                    lx, ly = x - ty * side, y + tx * side
                    if math.hypot(lx, ly) < OUTER_R - 4 and not in_park(lx, ly, 4.0):
                        L.append((lx, ly))
    return L


STREET_LAMPS = _street_lamps()
STREET_LAMP_H = 8.0


def _road_dist(x, y):
    best = 1e9
    for pts, half in _road_lines():
        for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
            dx, dy = x1 - x0, y1 - y0; L2 = dx * dx + dy * dy
            if L2 < 1e-6: continue
            t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / L2))
            best = min(best, math.hypot(x - x0 - t * dx, y - y0 - t * dy) - half)
    return best


def _outer_trees():
    """公园外、OUTER_R 以内的树（Unity 里的公告板）：不长在 PLATEAU 的楼里、大路上；塔的方向上不能高到挡住塔脚 40 m 以上"""
    from matplotlib.path import Path
    rng = np.random.default_rng(57)
    polys = []
    for b in plateau():
        if bldg_min_dist(b) > OUTER_R + 40 or b['height'] > 300: continue
        pts = np.array(sorted(set((round(q[0], 1), round(q[1], 1)) for _, r in b['surfs'] for q in r)))
        if len(pts) < 3: continue
        c = pts.mean(0); ang = np.arctan2(pts[:, 1] - c[1], pts[:, 0] - c[0])
        polys.append((Path(pts[np.argsort(ang)]), c, np.abs(pts - c).max() + 6))
    T = []
    tries = 0
    while len(T) < 330 and tries < 40000:
        tries += 1
        a = rng.uniform(0, 2 * math.pi); r = math.sqrt(rng.uniform(0.0, 1.0)) * (OUTER_R - 6)
        x, y = r * math.sin(a), r * math.cos(a)
        if in_park(x, y, 4.0) or _road_dist(x, y) < 3.0: continue
        if any(abs(x - c[0]) < m and abs(y - c[1]) < m and p.contains_point((x, y), radius=-5.0) for p, c, m in polys): continue
        if any(math.hypot(x - t[1], y - t[2]) < 6.5 for t in T): continue
        h = rng.uniform(12.0, 22.0)
        az = math.degrees(math.atan2(x, y))
        if abs(az) < 9.0 and y > 0:
            h = min(h, BAND_EYE[2] + math.hypot(x, y) * 40.0 / TOWER_D)          # 塔脚 40 m 以上露出来
            if h < 3.0: continue
        T.append(('jac' if rng.random() < 0.6 else 'small', float(x), float(y), float(h), int(rng.integers(0, 2))))
    return T


def band_size():
    w = 360 * PX_PER_DEG
    h = int(round((BAND_HI - BAND_LO) * PX_PER_DEG))
    return w, h


def band_radius():
    """全景带每个方向（方位从塔的方向起、向右为正，每 BAND_STEP 度）放多远：那个方向上最先挡住视线的大楼的距离。
    从全景带的视点往外打射线，和 PLATEAU 每栋楼的平面外包多边形求交，取最近的、在视点看来高出地平线 2.5° 以上的那栋；
    塔的方向 ±9° 固定在塔的距离上；再按方位平滑（在 1/R 上平均，近的占优）。"""
    import pickle
    B = pickle.load(open(os.path.join(OUT, 'plateau_bldg.pkl'), 'rb'))
    ex, ey, ez = BAND_EYE
    n = int(round(360 / BAND_STEP))
    az = np.radians(np.arange(n) * BAND_STEP)
    dx, dy = np.sin(az), np.cos(az)
    best = np.full(n, 600.0)
    for b in B:
        if math.hypot(b['cx'], b['cy']) > 650 or in_park(b['cx'], b['cy'], 2.0): continue
        if b['height'] > 300: continue                        # 东京塔（和 FootTown）
        if is_near(b): continue                               # Unity 里的三维
        pts = sorted(set((round(q[0], 1), round(q[1], 1)) for _, r in b['surfs'] for q in r))
        if len(pts) < 3: continue
        # 凸包
        cr = lambda o, a, c: (a[0] - o[0]) * (c[1] - o[1]) - (a[1] - o[1]) * (c[0] - o[0])
        lo, up = [], []
        for p in pts:
            while len(lo) >= 2 and cr(lo[-2], lo[-1], p) <= 0: lo.pop()
            lo.append(p)
        for p in reversed(pts):
            while len(up) >= 2 and cr(up[-2], up[-1], p) <= 0: up.pop()
            up.append(p)
        h = np.array(lo[:-1] + up[:-1], np.float64) - [ex, ey]
        a0, a1 = h, np.roll(h, -1, 0)
        e = a1 - a0                                           # 射线 t*d 和边 a0 + s*e 求交
        den = dx[:, None] * e[None, :, 1] - dy[:, None] * e[None, :, 0]
        with np.errstate(divide='ignore', invalid='ignore'):
            t = (a0[None, :, 0] * e[None, :, 1] - a0[None, :, 1] * e[None, :, 0]) / den
            s = (a0[None, :, 0] * dy[:, None] - a0[None, :, 1] * dx[:, None]) / den
        hit = (np.abs(den) > 1e-9) & (t > 1.0) & (s >= 0) & (s <= 1)
        t = np.where(hit, t, np.inf).min(1)
        el = np.degrees(np.arctan2(b['top'] - ez, t))
        ok = np.isfinite(t) & (el > 2.5)
        best = np.where(ok & (t < best), t, best)
    rel = (np.degrees(az) + 180.0) % 360.0 - 180.0
    best = np.where(np.abs(rel) < 9.0, TOWER_D, best)
    best = np.clip(best, OUTER_R + 10.0, TOWER_D * 1.6)       # 不比 Unity 里那圈地面近
    # 楼与楼之间的缝（街道）只有几度宽，从缝里望出去的远处很小：先在 ±5° 里取最近（把缝填上），再在 1/R 上平滑（σ 4°），
    # 免得圆筒变成一颗海星——镜头一动，尖角两边的东西会被拉扯
    m = 10; ext = np.concatenate([best[-m:], best, best[:m]])
    best = np.array([ext[i:i + 2 * m + 1].min() for i in range(n)])
    inv = 1.0 / best
    m = 24; k = np.exp(-0.5 * (np.arange(-m, m + 1) * BAND_STEP / 4.0) ** 2); k /= k.sum()
    inv = np.convolve(np.concatenate([inv[-m:], inv, inv[:m]]), k, 'same')[m:-m]
    r = 1.0 / inv
    r = np.where(np.abs(rel) < 6.0, TOWER_D, r)
    return [round(float(v), 2) for v in r]


def export_json():
    """Assets/Resources/World/tokyo/layout.json：白宫场景的 layout.json 加上夜里的光、全景带、公园这些。"""
    src = json.load(open(os.path.join(U.UNITY_WORLD, 'layout.json')))
    src['farCx'], src['farCy'], src['farSize'] = FAR_CX, FAR_CY, 2 * FAR_HALF
    src['parkBox'] = [PARK_X0, PARK_X1, PARK_Y0, PARK_Y1]
    src['houseY'] = 1.0e6                      # 没有白宫（镜头的“别穿进楼里”也就不起作用）
    src['monumentH'] = 0.0
    src['night'] = True
    src['moonAz'], src['moonEl'], src['moonSize'], src['mastH'] = U.MOON_AZ, U.MOON_EL, U.MOON_SIZE, U.MAST_H
    src['floods'] = [dict(x=f[0], y=f[1], h=f[2], tx=f[3], ty=f[4]) for f in U.FLOODS]
    src['trees'] = [dict(species=0 if sp == 'jac' else 1, x=x, y=y, h=h, variant=v) for (sp, x, y, h, v) in TREES + OUTER_TREES]
    src['outerR'] = OUTER_R
    src['band'] = dict(r=BAND_R, eyeZ=BAND_EYE[2], lo=BAND_LO, hi=BAND_HI, tiles=BAND_TILES, step=BAND_STEP, radius=band_radius())
    src['parkLamps'] = [dict(x=x, y=y, h=PARK_LAMP_H) for (x, y) in PARK_LAMPS] + [dict(x=x, y=y, h=STREET_LAMP_H) for (x, y) in STREET_LAMPS]
    src['towerD'] = TOWER_D
    src['stage'] = 'tokyo'
    os.makedirs(UNITY_WORLD, exist_ok=True)
    json.dump(src, open(os.path.join(UNITY_WORLD, 'layout.json'), 'w'), indent=1)
    return src


def _cached_outer_trees():
    """公园外的树：系统的 python3 跑 t_layout.py 时算好存进 out/outer_trees.json（要 matplotlib）；Blender 里只读这个文件"""
    path = os.path.join(OUT, 'outer_trees.json')
    if __name__ == '__main__':
        T = _outer_trees()
        json.dump(T, open(path, 'w'))
        return T
    return [tuple(t) for t in json.load(open(path))] if os.path.exists(path) else []


OUTER_TREES = _cached_outer_trees()


if __name__ == '__main__':
    d = export_json()
    print('near buildings %d, outer trees %d, street lamps %d' % (len(near_buildings()), len(OUTER_TREES), len(STREET_LAMPS)))
    W, H = band_size()
    r = np.array(d['band']['radius'])
    print('tokyo (football) layout: %d trees, %d park lamps, band %d x %d (%d tiles), band radius %.0f..%.0f m' % (len(TREES), len(PARK_LAMPS), W, H, BAND_TILES, r.min(), r.max()))
    for a in range(0, 360, 15):
        print('  az %4d  r %6.1f' % (a if a <= 180 else a - 360, r[int(a / BAND_STEP)]))
