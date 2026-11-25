# -*- coding: utf-8 -*-
"""近处真实建筑的立体立面（2026-11-24）。

PLATEAU 的 LOD2 墙是平板。第一、二版的窗户是外墙材质画在平板上的，没有一点凹凸，看着像 CG。
这里给离对战地 DETAIL_R 以内（再加上远处的高楼）的楼、朝着镜头的墙，生成真正的几何体：
  - 每扇窗：玻璃 + 窗框（两边、窗楣、窗台，凸出墙面 10-18 cm）+ 竖梃。离得远（> FRAME_R）的只要玻璃。
  - 玻璃后面的房间用“室内映射”算（着色器里从玻璃往里打一条视线，算它先碰到后墙、侧墙、天花板还是地板）：
    能看到天花板上一排排灯板、后墙上的光、靠窗的桌子挡住下半截；有的拉着百叶、有的拉着窗帘、有的没开灯。
  - 每层楼一道挑檐（办公、商住）；公寓的长墙每层一个阳台（楼板 + 栏杆 + 隔板）；玻璃塔一根根竖向百叶。
  - 楼顶：女儿墙、空调机组和水箱。
  - 底层：店铺的大玻璃（一部分亮着）+ 门头上的招牌灯条。
全是 numpy 一次算好的网格（几百万个面），面属性给着色器。"""
import bpy, math
import numpy as np

DETAIL_R = 600.0          # 这个距离以内的楼做立体立面
TALL_R, TALL_H = 1700.0, 80.0     # 远处的高楼（天际线）也做
FRAME_R = 420.0           # 这个距离以内的窗有窗框；更远的只有玻璃（看不出来）
EYE = np.array([0.0, 0.0, 1.2])

# 材质槽
GLASS, FRAME, LEDGE, RAIL, ROOFBOX, SIGN, FIN = range(7)


def selected(b, kind):
    d = math.hypot(b['cx'], b['cy'])
    if kind == 'temple' or b['height'] < 6:
        return False
    return d < DETAIL_R or (d < TALL_R and b['height'] > TALL_H)


class Acc:
    """攒四边形：顶点 (V,3)、面 (F,4)、材质号、面属性、每个角的 UV"""
    def __init__(self):
        self.V = []; self.Q = []; self.M = []; self.P = []; self.UV = []; self.nv = 0

    def add(self, verts, quads, mat, attrs, uv=None):
        n = len(quads)
        self.V.append(verts); self.Q.append(quads + self.nv); self.nv += len(verts)
        self.M.append(np.full(n, mat, np.int32))
        self.P.append((n, {k: np.asarray(v, np.float32) for k, v in attrs.items()}))
        self.UV.append(uv if uv is not None else np.zeros((n, 4, 2), np.float32))

    def to_object(self, name, mats, coll):
        V = np.concatenate(self.V).astype(np.float32); Q = np.concatenate(self.Q).astype(np.int32)
        M = np.concatenate(self.M); UV = np.concatenate(self.UV).astype(np.float32)
        me = bpy.data.meshes.new(name)
        me.vertices.add(len(V)); me.vertices.foreach_set('co', V.ravel())
        me.loops.add(len(Q) * 4); me.loops.foreach_set('vertex_index', Q.ravel())
        me.polygons.add(len(Q))
        me.polygons.foreach_set('loop_start', np.arange(0, len(Q) * 4, 4, dtype=np.int32))
        me.polygons.foreach_set('loop_total', np.full(len(Q), 4, np.int32))
        me.update(calc_edges=True)
        me.polygons.foreach_set('material_index', M)
        uvl = me.uv_layers.new(name='WinUV'); uvl.data.foreach_set('uv', UV.ravel())
        shapes = {}
        for n, at in self.P:
            for k, v in at.items(): shapes[k] = v.shape[1:]
        for k, shp in shapes.items():
            arr = np.concatenate([at[k] if k in at else np.zeros((n,) + shp, np.float32) for n, at in self.P]).astype(np.float32)
            if arr.ndim == 2:
                at = me.attributes.new(k, 'FLOAT_VECTOR', 'FACE'); at.data.foreach_set('vector', arr.ravel())
            else:
                at = me.attributes.new(k, 'FLOAT', 'FACE'); at.data.foreach_set('value', arr)
        for m in mats:
            me.materials.append(m)
        me.validate(); me.update()
        ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
        print('FACADE3D %s: %d faces' % (name, len(Q)))
        return ob


# 方盒子（只要 5 个面：贴墙的那面不要）。坐标系：x 沿墙、z 向上、y 向外（米）
_BOX_Q = np.array([[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7], [4, 5, 6, 7]], np.int32)


def boxes_world(o, u, n, x0, x1, z0, z1, y0, y1):
    """N 个盒子（每个参数是 (N,) 数组），o: (N,3) 墙上的原点，u/n: (N,3) 沿墙/朝外的单位向量。返回顶点 (N*8,3)、面 (N*5,4)"""
    N = len(x0)
    xs = np.stack([x0, x1, x1, x0, x0, x1, x1, x0], 1)
    ys = np.stack([y1, y1, y0, y0, y1, y1, y0, y0], 1)        # 外面 y1，里面 y0
    zs = np.stack([z0, z0, z0, z0, z1, z1, z1, z1], 1)
    V = o[:, None, :] + u[:, None, :] * xs[:, :, None] + n[:, None, :] * ys[:, :, None]
    V[:, :, 2] += zs
    Q = (_BOX_Q[None, :, :] + (np.arange(N) * 8)[:, None, None]).reshape(-1, 4)
    return V.reshape(-1, 3), Q


def quads_world(o, u, n, x0, x1, z0, z1, y):
    N = len(x0)
    xs = np.stack([x0, x1, x1, x0], 1); zs = np.stack([z0, z0, z1, z1], 1)
    V = o[:, None, :] + u[:, None, :] * xs[:, :, None] + n[:, None, :] * np.asarray(y).reshape(-1, 1, 1)
    V[:, :, 2] += zs
    Q = np.arange(N * 4, dtype=np.int32).reshape(-1, 4)
    return V.reshape(-1, 3), Q


def inside(poly, pts):
    """多边形 (K,2) 里有哪些点 (M,2)（射线法，向量化）"""
    x, y = pts[:, 0][:, None], pts[:, 1][:, None]
    x0, y0 = poly[:, 0][None, :], poly[:, 1][None, :]
    x1, y1 = np.roll(poly[:, 0], -1)[None, :], np.roll(poly[:, 1], -1)[None, :]
    cond = (y0 > y) != (y1 > y)
    xi = x0 + (y - y0) * (x1 - x0) / np.where(y1 - y0 == 0, 1e-9, y1 - y0)
    return (np.sum(cond & (x < xi), 1) % 2) == 1


def wall_frame(r, cx, cy):
    """墙面：原点（最低边的一端）、沿墙方向 u、朝外法线 n、墙上二维坐标 (s, z) 的多边形"""
    P = np.asarray(r, np.float64)
    if len(P) < 3: return None
    low = P[np.argsort(P[:, 2])[:2]]
    d = low[1, :2] - low[0, :2]
    if np.linalg.norm(d) < 0.5:
        i, j = np.unravel_index(np.argmax(np.linalg.norm(P[:, None, :2] - P[None, :, :2], axis=2)), (len(P), len(P)))
        d = P[j, :2] - P[i, :2]
    L = np.linalg.norm(d)
    if L < 1e-6: return None
    u2 = d / L
    n2 = np.array([u2[1], -u2[0]])
    mid = P[:, :2].mean(0)
    if np.dot(n2, mid - np.array([cx, cy])) < 0: n2 = -n2
    s = (P[:, :2] - P[0, :2]) @ u2
    poly = np.stack([s, P[:, 2]], 1)
    o = np.array([P[0, 0], P[0, 1], 0.0])
    return o, np.array([u2[0], u2[1], 0.0]), np.array([n2[0], n2[1], 0.0]), poly


def build(coll, specs, rng=None):
    """specs: [(b, kind, fh, a, surfs)]，a 是外墙属性（ww、mu、mv0、mv1、litp、warm、bright、glass、seed、kind…）"""
    rng = rng or np.random.default_rng(311)
    acc = Acc()
    nwin = 0
    for (b, kind, fh, a, surfs, pmodes) in specs:
        dist = math.hypot(b['cx'], b['cy'])
        framed = dist < FRAME_R
        knum = a.get('kind', 1.0)
        res = kind in ('res', 'restower', 'hotel')
        glass_tower = (a['glass'] > 0.5)
        walls = []
        for (k, r), pm in zip(surfs, pmodes):
            if k != 'wall': continue
            if pm == 2: continue                  # 这面墙用 PLATEAU 照片里的窗（t_city 外墙材质），不再放立体窗
            wf = wall_frame(r, b['cx'], b['cy'])
            if wf is None: continue
            o, u, n, poly = wf
            # 只做朝着镜头的墙（全景带是从一点拍的）
            c3 = np.array([np.mean([p[0] for p in r]), np.mean([p[1] for p in r]), 0.0])
            if np.dot(n[:2], EYE[:2] - c3[:2]) <= 0: continue
            smin, smax = poly[:, 0].min(), poly[:, 0].max(); zmax = poly[:, 1].max(); zmin = poly[:, 1].min()
            if smax - smin < 2.0 or zmax - zmin < 3.0: continue
            walls.append((o, u, n, poly, smin, smax, zmax, zmin))
        if not walls: continue
        maxlen = max(w[5] - w[4] for w in walls)
        bseed = float(a['seed'])
        for (o, u, n, poly, smin, smax, zmax, zmin) in walls:
            rect = len(poly) == 4
            ww = float(a['ww']); mu = float(a['mu']); mv0 = float(a['mv0']); mv1 = float(a['mv1'])
            nb = int((smax - smin - 0.6) // ww)
            nf = int(zmax // fh)
            if nb < 1 or nf < 1: continue
            s0 = smin + (smax - smin - nb * ww) / 2
            F, Bv = np.meshgrid(np.arange(nf), np.arange(nb), indexing='ij')
            F = F.ravel(); Bv = Bv.ravel()
            shop = (F == 0) & (kind != 'restower') & (not glass_tower) & (zmin < 0.5)
            wwin = np.where(shop, ww * 0.92, ww * (1 - 2 * mu))
            zb = F * fh + np.where(shop, 0.35, fh * mv0)
            zt = F * fh + np.where(shop, fh * 0.82, fh * mv1)
            sc = s0 + (Bv + 0.5) * ww
            keep = (zt < zmax - 0.4) & (zb > zmin + 0.2)
            if not rect:
                for (cs, cz) in ((sc - wwin / 2, zb), (sc + wwin / 2, zb), (sc - wwin / 2, zt), (sc + wwin / 2, zt)):
                    keep &= inside(poly, np.stack([cs, cz], 1))
            F, Bv, wwin, zb, zt, sc, shop = F[keep], Bv[keep], wwin[keep], zb[keep], zt[keep], sc[keep], shop[keep]
            N = len(F)
            if N == 0: continue
            nwin += N
            O = np.repeat(o[None, :], N, 0) + np.outer(sc, u); O[:, 2] = 0.0
            U = np.repeat(u[None, :], N, 0); Nn = np.repeat(n[None, :], N, 0)
            # 每扇窗的随机属性：整层/一组亮不亮（和原来的外墙材质一样的规则）、每扇窗自己
            grp = np.floor(Bv / max(1.0, float(a['grp'])))
            rr = np.random.default_rng(int(bseed) * 131 + int(abs(o[0]) * 7 + abs(o[1]) * 13) % 100000)
            hg = rr.random(int(grp.max()) + 1 + 1000)[(grp.astype(int) * 37 + F * 11) % 1000]
            hb = rr.random(N)
            litp = np.where(shop, 0.42, float(a['litp']))
            lit = ((hg < litp) & (hb > 0.10)).astype(np.float32)
            warmp = float(a['warm'])
            tone = rr.random(N)
            col = np.where((tone < warmp)[:, None], [1.0, 0.70, 0.42], np.where((tone > 0.85)[:, None], [1.0, 0.96, 0.90], np.where((tone > 0.55)[:, None], [0.86, 1.0, 0.90], [0.80, 0.92, 1.0])))
            if knum >= 2.5: col[:] = [1.0, 0.66, 0.36]
            blind = (rr.random(N) < (0.15 if res else 0.28)).astype(np.float32)
            curtain = ((rr.random(N) < 0.5) & res & (blind < 0.5)).astype(np.float32)
            bright = float(a['bright']) * (0.55 + 0.75 * rr.random(N)) * np.where(shop, 1.4, 1.0)
            depth = np.where(res, 3.6, 5.5) * np.ones(N)
            # ---- 玻璃（室内映射）：UV 0..1；面属性给着色器
            gv, gq = quads_world(O, U, Nn, -wwin / 2, wwin / 2, zb, zt, 0.006)
            uv = np.tile(np.array([[0, 0], [1, 0], [1, 1], [0, 1]], np.float32), (N, 1, 1))
            acc.add(gv, gq, GLASS, dict(wu=U, wn=-Nn, wsize=np.stack([wwin, zt - zb, depth], 1), lit=lit, bright=bright,
                                       wcol=col, blind=blind, curtain=curtain, shop=shop.astype(np.float32), rnd=rr.random(N)), uv)
            # ---- 窗框（近处）：两边、窗楣、窗台；宽窗加竖梃
            if framed and not glass_tower:
                fw, fd = 0.09, 0.11
                X0 = np.concatenate([-wwin / 2 - fw, wwin / 2, -wwin / 2 - fw, -wwin / 2 - 0.08])
                X1 = np.concatenate([-wwin / 2, wwin / 2 + fw, wwin / 2 + fw, wwin / 2 + 0.08])
                Z0 = np.concatenate([zb, zb, zt, zb - 0.07])
                Z1 = np.concatenate([zt, zt, zt + fw, zb])
                Y1 = np.concatenate([np.full(N, fd), np.full(N, fd), np.full(N, fd + 0.02), np.full(N, 0.19)])
                bv, bq = boxes_world(np.tile(O, (4, 1)), np.tile(U, (4, 1)), np.tile(Nn, (4, 1)), X0, X1, Z0, Z1, np.zeros(4 * N), Y1)
                acc.add(bv, bq, FRAME, {})
                wide = wwin > 1.9
                if wide.any():
                    k = np.nonzero(wide)[0]
                    bv, bq = boxes_world(O[k], U[k], Nn[k], -0.03 * np.ones(len(k)), 0.03 * np.ones(len(k)), zb[k], zt[k], np.zeros(len(k)), np.full(len(k), 0.07))
                    acc.add(bv, bq, FRAME, {})
            # ---- 每层挑檐（办公、商住）/ 公寓阳台 / 玻璃塔竖向百叶
            Lw = smax - smin
            ow = np.array([o[0], o[1], 0.0]) + u * smin
            floors = np.arange(1, nf) * fh
            floors = floors[(floors < zmax - 0.5) & (floors > zmin + 0.5)]
            if len(floors):
                M_ = len(floors)
                OO = np.repeat(ow[None, :], M_, 0); UU = np.repeat(u[None, :], M_, 0); NN = np.repeat(n[None, :], M_, 0)
                if glass_tower:
                    bv, bq = boxes_world(OO, UU, NN, np.zeros(M_), np.full(M_, Lw), floors - 0.35, floors + 0.12, np.zeros(M_), np.full(M_, 0.05))
                    acc.add(bv, bq, FIN, {})
                elif res and Lw >= 0.6 * maxlen and Lw > 7:
                    # 阳台：楼板 1.1 m + 栏杆 1.05 m（磨砂玻璃/金属）+ 每两开间一块隔板
                    bv, bq = boxes_world(OO, UU, NN, np.zeros(M_), np.full(M_, Lw), floors - 0.18, floors, np.zeros(M_), np.full(M_, 1.15))
                    acc.add(bv, bq, LEDGE, {})
                    bv, bq = boxes_world(OO, UU, NN, np.zeros(M_), np.full(M_, Lw), floors, floors + 1.05, np.full(M_, 1.10), np.full(M_, 1.15))
                    acc.add(bv, bq, RAIL, {})
                    xs = np.arange(ww * 2, Lw - 0.5, ww * 2)
                    if len(xs):
                        XX, FF = np.meshgrid(xs, floors); XX = XX.ravel(); FF = FF.ravel(); K = len(XX)
                        bv, bq = boxes_world(np.repeat(ow[None, :], K, 0), np.repeat(u[None, :], K, 0), np.repeat(n[None, :], K, 0),
                                             XX - 0.04, XX + 0.04, FF, FF + 2.2, np.zeros(K), np.full(K, 1.1))
                        acc.add(bv, bq, RAIL, {})
                elif kind != 'temple':
                    bv, bq = boxes_world(OO, UU, NN, np.zeros(M_), np.full(M_, Lw), floors - 0.25, floors, np.zeros(M_), np.full(M_, 0.28))
                    acc.add(bv, bq, LEDGE, {})
            if glass_tower:
                xs = np.arange(0.0, Lw + 0.01, ww)
                K = len(xs)
                bv, bq = boxes_world(np.repeat(ow[None, :], K, 0), np.repeat(u[None, :], K, 0), np.repeat(n[None, :], K, 0),
                                     xs - 0.05, xs + 0.05, np.full(K, zmin), np.full(K, zmax - 0.2), np.zeros(K), np.full(K, 0.38))
                acc.add(bv, bq, FIN, {})
            # ---- 女儿墙（墙顶一道）
            bv, bq = boxes_world(ow[None, :], u[None, :], n[None, :], np.zeros(1), np.full(1, Lw), np.full(1, zmax), np.full(1, zmax + 0.9),
                                 np.full(1, -0.25), np.full(1, 0.02))
            acc.add(bv, bq, LEDGE, {})
            # ---- 底层招牌灯条（店铺，一部分）
            if not glass_tower and kind not in ('res', 'restower') and rr.random() < 0.45 and Lw > 4:
                c = [(1.0, 0.92, 0.80), (1.0, 0.35, 0.20), (0.35, 0.70, 1.0), (1.0, 0.80, 0.30), (0.95, 0.95, 1.0)][int(rr.integers(0, 5))]
                bv, bq = boxes_world(ow[None, :], u[None, :], n[None, :], np.full(1, Lw * 0.15), np.full(1, Lw * 0.85),
                                     np.full(1, fh * 0.86), np.full(1, fh * 0.86 + 0.6), np.zeros(1), np.full(1, 0.25))
                acc.add(bv, bq, SIGN, dict(scol=np.array([c] * 5, np.float32)))
        # ---- 楼顶设备：几个灰盒子（空调机组、水箱、电梯机房）
        roofs = [r for (k, r) in surfs if k == 'roof']
        if roofs:
            top = max(roofs, key=lambda r: max(p[2] for p in r))
            T = np.array(top); zt_ = T[:, 2].max()
            x0, x1, y0, y1 = T[:, 0].min(), T[:, 0].max(), T[:, 1].min(), T[:, 1].max()
            k = int(min(6, max(1, (x1 - x0) * (y1 - y0) / 120)))
            cxs = rng.uniform(x0 + 2, max(x0 + 2.1, x1 - 2), k); cys = rng.uniform(y0 + 2, max(y0 + 2.1, y1 - 2), k)
            ok = inside(T[:, :2], np.stack([cxs, cys], 1))
            for cx_, cy_ in zip(cxs[ok], cys[ok]):
                w_, d_, h_ = rng.uniform(1.5, 5), rng.uniform(1.5, 4), rng.uniform(1.2, 3.5)
                o_ = np.array([[cx_ - w_ / 2, cy_ - d_ / 2, 0.0]])
                bv, bq = boxes_world(o_, np.array([[1.0, 0, 0]]), np.array([[0, 1.0, 0]]), np.zeros(1), np.full(1, w_),
                                     np.full(1, zt_), np.full(1, zt_ + h_), np.zeros(1), np.full(1, d_))
                acc.add(bv, bq, ROOFBOX, {})
    print('FACADE3D windows %d' % nwin)
    if not acc.V:
        return None
    return acc.to_object('Facades3D', materials(), coll)


# ---------------------------------------------------------------- 材质
def materials():
    import t_city as C
    def plain(name, color, rough=0.6, spec=0.4):
        m = bpy.data.materials.new(name); m.use_nodes = True
        b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = tuple(color) + (1,); b.inputs['Roughness'].default_value = rough
        try: b.inputs['Specular IOR Level'].default_value = spec
        except KeyError: pass
        C.haze_wrap(m)
        return m
    frame = plain('f3_frame', (0.05, 0.052, 0.055), 0.4, 0.5)
    ledge = plain('f3_ledge', (0.16, 0.155, 0.15), 0.8)
    rail = plain('f3_rail', (0.06, 0.065, 0.07), 0.35, 0.6)
    roofbox = plain('f3_roofbox', (0.12, 0.12, 0.12), 0.7)
    fin = plain('f3_fin', (0.03, 0.035, 0.04), 0.25, 0.6)
    sign = bpy.data.materials.new('f3_sign'); sign.use_nodes = True
    nt = sign.node_tree; b = nt.nodes['Principled BSDF']
    at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_type = 'GEOMETRY'; at.attribute_name = 'scol'
    nt.links.new(at.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 0.35
    b.inputs['Base Color'].default_value = (0.05, 0.05, 0.05, 1)
    C.haze_wrap(sign)
    return [interior_material(), frame, ledge, rail, roofbox, sign, fin]


def interior_material():
    """玻璃 + 后面的房间（室内映射）。房间：宽 = 窗宽 + 1.2 m，进深 wsize.z，窗台离地 0.9 m，天花板比窗楣高 0.35 m。"""
    import t_city as C
    m = bpy.data.materials.new('f3_glass'); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; Lk = nt.links
    for n in list(N): N.remove(n)
    out = N.new('ShaderNodeOutputMaterial')
    def attr(name, vec=False):
        a = N.new('ShaderNodeAttribute'); a.attribute_type = 'GEOMETRY'; a.attribute_name = name
        return a.outputs['Vector'] if vec else a.outputs['Fac']
    def M(op, a, b=None, c=None):
        n = N.new('ShaderNodeMath'); n.operation = op
        for i, x in enumerate((a, b, c)):
            if x is None: continue
            if isinstance(x, (int, float)): n.inputs[i].default_value = x
            else: Lk.new(x, n.inputs[i])
        return n.outputs[0]
    def V(op, a, b=None):
        n = N.new('ShaderNodeVectorMath'); n.operation = op
        Lk.new(a, n.inputs[0])
        if b is not None:
            if isinstance(b, (int, float)): n.inputs['Scale'].default_value = b
            else: Lk.new(b, n.inputs[1])
        return n.outputs['Value'] if op == 'DOT_PRODUCT' else n.outputs['Vector']
    def sep(v):
        n = N.new('ShaderNodeSeparateXYZ'); Lk.new(v, n.inputs['Vector']); return n.outputs['X'], n.outputs['Y'], n.outputs['Z']
    def comb(x, y, z):
        n = N.new('ShaderNodeCombineXYZ')
        for s_, v in zip(('X', 'Y', 'Z'), (x, y, z)):
            if isinstance(v, (int, float)): n.inputs[s_].default_value = v
            else: Lk.new(v, n.inputs[s_])
        return n.outputs['Vector']
    def mix(f, a, b):
        return M('ADD', M('MULTIPLY', a, M('SUBTRACT', 1.0, f)), M('MULTIPLY', b, f))
    def smooth(e0, e1, x):
        n = N.new('ShaderNodeMapRange'); n.interpolation_type = 'SMOOTHSTEP'
        n.inputs['From Min'].default_value = e0; n.inputs['From Max'].default_value = e1
        Lk.new(x, n.inputs['Value']); return n.outputs['Result']
    geo = N.new('ShaderNodeNewGeometry')
    d = V('SCALE', geo.outputs['Incoming'], -1.0)                  # 视线方向（往里）
    wu, wn = attr('wu', True), attr('wn', True)
    wx, wy, wz = sep(attr('wsize', True))                           # 窗宽、窗高、房间进深
    dx, dy, dz = V('DOT_PRODUCT', d, wu), V('DOT_PRODUCT', d, wn), sep(d)[2]
    dy = M('MAXIMUM', dy, 0.02)
    uvn = N.new('ShaderNodeUVMap'); uvn.uv_map = 'WinUV'
    pu, pv, _ = sep(uvn.outputs['UV'])
    px = M('MULTIPLY', M('SUBTRACT', pu, 0.5), wx)                  # 玻璃上的点（米）：x 沿墙，z 从窗台算
    pz = M('MULTIPLY', pv, wz)
    hw = M('ADD', M('MULTIPLY', wx, 0.5), 0.6)
    zc = M('ADD', wz, 0.35); zf = -0.9
    # 碰到后墙、侧墙、天花板/地板的距离
    t_back = M('DIVIDE', wy, dy)
    sx = M('SIGN', dx)
    t_side = M('DIVIDE', M('SUBTRACT', M('MULTIPLY', sx, hw), px), M('ADD', M('ABSOLUTE', dx), 1e-4))
    t_side = M('ABSOLUTE', t_side)
    up = M('GREATER_THAN', dz, 0.0)
    zt = mix(up, zf, zc)
    t_vert = M('ABSOLUTE', M('DIVIDE', M('SUBTRACT', zt, pz), M('ADD', M('ABSOLUTE', dz), 1e-4)))
    t = M('MINIMUM', M('MINIMUM', t_back, t_side), t_vert)
    hx = M('ADD', px, M('MULTIPLY', dx, t)); hy = M('MULTIPLY', dy, t); hz = M('ADD', pz, M('MULTIPLY', dz, t))
    is_back = M('LESS_THAN', M('ABSOLUTE', M('SUBTRACT', t, t_back)), 1e-3)
    is_vert = M('MULTIPLY', M('LESS_THAN', M('ABSOLUTE', M('SUBTRACT', t, t_vert)), 1e-3), M('SUBTRACT', 1.0, is_back))
    is_ceil = M('MULTIPLY', is_vert, up)
    is_floor = M('MULTIPLY', is_vert, M('SUBTRACT', 1.0, up))
    is_side = M('MULTIPLY', M('SUBTRACT', 1.0, is_back), M('SUBTRACT', 1.0, is_vert))
    rnd = attr('rnd')
    # 天花板：一排排灯板（沿进深每 1.8 m 一块，宽占房间 60%）
    panel = M('MULTIPLY', M('LESS_THAN', M('FRACT', M('ADD', M('DIVIDE', hy, 1.8), rnd)), 0.32), M('LESS_THAN', M('ABSOLUTE', hx), M('MULTIPLY', hw, 0.62)))
    ceil_l = M('ADD', 0.30, M('MULTIPLY', panel, 2.2))
    # 后墙：上亮下暗；桌子/柜子挡住下半截（离地 0.75 m 以下暗）
    back_l = M('MULTIPLY', M('ADD', 0.30, M('MULTIPLY', smooth(-0.9, 2.6, hz), 0.45)), M('ADD', 0.35, M('MULTIPLY', M('GREATER_THAN', hz, -0.15), 0.65)))
    side_l = M('MULTIPLY', M('ADD', 0.22, M('MULTIPLY', smooth(-0.9, 2.6, hz), 0.3)), M('ADD', 0.4, M('MULTIPLY', M('GREATER_THAN', hz, -0.15), 0.6)))
    floor_l = M('ADD', 0.10, M('MULTIPLY', smooth(4.0, 0.5, hy), 0.12))
    room = M('ADD', M('ADD', M('MULTIPLY', is_ceil, ceil_l), M('MULTIPLY', is_back, back_l)), M('ADD', M('MULTIPLY', is_side, side_l), M('MULTIPLY', is_floor, floor_l)))
    # 窗前的桌子（离窗 0.3-1.2 m、高 0.75 m）：视线在那段深度上低于桌面就挡住
    z_at_desk = M('ADD', pz, M('MULTIPLY', dz, M('DIVIDE', 0.6, dy)))
    desk = M('MULTIPLY', M('LESS_THAN', z_at_desk, -0.15), M('LESS_THAN', rnd, 0.75))
    room = mix(desk, room, 0.12)
    # 百叶（横纹，均匀透光）、窗帘（暖、暗、均匀）、店铺（亮、平）
    blind, curtain, shop = attr('blind'), attr('curtain'), attr('shop')
    slats = M('ADD', 0.32, M('MULTIPLY', M('LESS_THAN', M('FRACT', M('MULTIPLY', pz, 9.0)), 0.55), 0.16))
    room = mix(blind, room, slats)
    room = mix(M('MULTIPLY', curtain, M('SUBTRACT', 1.0, blind)), room, 0.45)
    room = mix(shop, room, M('ADD', 0.75, M('MULTIPLY', is_ceil, 0.6)))
    lit, bright = attr('lit'), attr('bright')
    k = M('MULTIPLY', M('MULTIPLY', room, lit), bright)
    col = attr('wcol', True)
    cur = N.new('ShaderNodeMix'); cur.data_type = 'RGBA'
    Lk.new(M('MULTIPLY', curtain, M('SUBTRACT', 1.0, blind)), cur.inputs['Factor']); Lk.new(col, cur.inputs[6]); cur.inputs[7].default_value = (1.0, 0.62, 0.36, 1)
    bsdf = N.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Base Color'].default_value = (0.006, 0.008, 0.010, 1); bsdf.inputs['Roughness'].default_value = 0.05
    try: bsdf.inputs['Specular IOR Level'].default_value = 0.6
    except KeyError: pass
    Lk.new(cur.outputs[2], bsdf.inputs['Emission Color'])
    # 没开灯的房间不是全黑：外面的光照进去一点
    Lk.new(M('ADD', M('MULTIPLY', k, 0.105), M('MULTIPLY', M('SUBTRACT', 1.0, lit), M('MULTIPLY', room, 0.0016))), bsdf.inputs['Emission Strength'])
    Lk.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    C.haze_wrap(m)
    return m
