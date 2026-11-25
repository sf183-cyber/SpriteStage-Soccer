# -*- coding: utf-8 -*-
"""东京塔（Blender 里运行）：按公开尺寸用脚本搭的钢桁架塔，夜里打“地标灯”（Landmark Light）。

尺寸（出处记在 SOURCES.md）：
  - 高 333 m；塔脚中心间距 88.0 m（ja.wikipedia「東京タワー」）
  - 大展望台（メインデッキ）两层，在地上约 120 m 以上；特别展望台（トップデッキ）在约 223–250 m；塔身到 252.65 m（H.27），上面是天线
  - 大展望台上沿以上按 7 等分刷成国际橙和白相间；以下是橙色；大展望台外墙是白色（1998 年起）
  - 地标灯：180 盏（天线 12、特别展望台以上 16、特别展望台到大展望台 40、大展望台下 16、大展望台到塔下大楼屋顶 84、塔脚 12），
    冬季（10 月起）是暖色的高压钠灯
凭照片估的（没有图纸）：塔身的收分曲线、各层横梁的高度、大展望台 28 m 见方 13 m 高、特别展望台 12 m 见方、天线段的粗细。
坐标：塔的中心在原点，z 向上，米。四个面朝 ±x、±y。"""
import bpy, math
import numpy as np
from mathutils import Vector

H = 333.0
BODY_TOP = 252.65
DECK = (118.0, 131.0, 14.0)          # 大展望台：底、顶、半边长
TOPDECK = (241.0, 250.5, 6.4)        # 特别展望台
STRIPE0 = DECK[1]                    # 七段条纹从大展望台上沿开始
DETAIL = True                        # 主杆做成格构杆（近看用；远看 False 就够）
LEG_TOP = 48.0                       # 塔脚（四根大桁架腿）到这里并成一根角柱
ARCH_TOP = 42.0                      # 四面的拱顶（“アーチライン”约 40 m）

# 材质编号
ORANGE, WHITE, DECKWALL, GLASS, DARK, LAMP, RED, TOPGLASS, SHAFT = range(9)


def hw(z):
    """角柱中心线离塔轴的距离（每个方向），z = 0 时 44 m。指数收分，过 (118, 12) 和 (245, 4.8)。"""
    if z <= BODY_TOP:
        return 3.0 + 41.0 * math.exp(-z / 77.8)
    # 天线段
    t = (z - BODY_TOP)
    return max(0.25, 2.3 - t * 0.032)


def band(z):
    if z < STRIPE0:
        return ORANGE
    k = int((z - STRIPE0) / ((H - STRIPE0) / 7.0))
    return ORANGE if k % 2 == 0 else WHITE


class MB:
    """把许多根方截面杆件攒成一个网格。"""
    def __init__(self):
        self.v = []; self.f = []; self.m = []

    def beam(self, a, b, w, mat=None, d=None):
        a = np.asarray(a, float); b = np.asarray(b, float)
        ax = b - a; L = float(np.linalg.norm(ax))
        if L < 1e-4:
            return
        ax /= L
        ref = np.array([0.0, 0.0, 1.0]) if abs(ax[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
        u = np.cross(ax, ref); u /= np.linalg.norm(u); v = np.cross(ax, u)
        h, k = w / 2.0, (d if d else w) / 2.0
        if mat is None:
            mat = band((a[2] + b[2]) / 2)
        base = len(self.v)
        for p in (a, b):
            for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                self.v.append(tuple(p + u * su * h + v * sv * k))
        for q in ((0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (3, 2, 1, 0), (4, 5, 6, 7)):
            self.f.append(tuple(base + i for i in q)); self.m.append(mat)

    def truss(self, a, b, w, d, chord, lace, mat=None, ref=None):
        """格构杆：四根弦杆（截面 w x d）+ 四个面上的之字形缀条。离 300 m 看，东京塔的每根主杆都是这样的“花格”。
        ref：截面 d 方向大致朝哪（比如面的外法线）。"""
        a = np.asarray(a, float); b = np.asarray(b, float)
        ax = b - a; L = float(np.linalg.norm(ax))
        if L < 1e-3:
            return
        ax /= L
        r = np.asarray(ref if ref is not None else ((0, 0, 1.0) if abs(ax[2]) < 0.9 else (1.0, 0, 0)), float)
        v = r - ax * float(np.dot(r, ax))
        if np.linalg.norm(v) < 1e-6:
            v = np.cross(ax, (1.0, 0, 0))
        v /= np.linalg.norm(v); u = np.cross(ax, v)
        if mat is None:
            mat = band((a[2] + b[2]) / 2)
        offs = [u * (w / 2) + v * (d / 2), -u * (w / 2) + v * (d / 2), -u * (w / 2) - v * (d / 2), u * (w / 2) - v * (d / 2)]
        for o in offs:
            self.beam(a + o, b + o, chord, mat)
        pitch = max(w, d) * 1.1
        n = max(1, int(round(L / pitch)))
        for k in range(n):
            t0, t1 = a + ax * (L * k / n), a + ax * (L * (k + 1) / n)
            for j in range(4):
                o0, o1 = offs[j], offs[(j + 1) % 4]
                if k % 2 == 0:
                    self.beam(t0 + o0, t1 + o1, lace, mat)
                else:
                    self.beam(t0 + o1, t1 + o0, lace, mat)
            if k % 3 == 0:
                for j in range(4):
                    self.beam(t0 + offs[j], t0 + offs[(j + 1) % 4], lace, mat)      # 缀板

    def box(self, lo, hi, mat):
        x0, y0, z0 = lo; x1, y1, z1 = hi
        base = len(self.v)
        for z in (z0, z1):
            for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
                self.v.append((x, y, z))
        for q in ((0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (3, 2, 1, 0), (4, 5, 6, 7)):
            self.f.append(tuple(base + i for i in q)); self.m.append(mat)

    def quad(self, a, b, c, d, mat):
        base = len(self.v)
        for p in (a, b, c, d):
            self.v.append(tuple(p))
        self.f.append((base, base + 1, base + 2, base + 3)); self.m.append(mat)

    def to_object(self, name, mats, coll, origin=(0, 0, 0), rot=0.0):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        for m in mats:
            me.materials.append(m)
        me.polygons.foreach_set('material_index', np.array(self.m, np.int32))
        me.update()
        ob = bpy.data.objects.new(name, me)
        ob.location = origin; ob.rotation_euler = (0, 0, rot)
        coll.objects.link(ob)
        return ob


def corner(c, z, inset=0.0):
    r = hw(z) - inset
    return np.array([c[0] * r, c[1] * r, z])


CORNERS = [(1, 1), (-1, 1), (-1, -1), (1, -1)]        # 逆时针；面 i 在角 i 和角 i+1 之间


def levels():
    """主体横梁的高度：拱顶以上，间距约等于当地塔宽的 0.6 倍，几处楼面必须有梁。"""
    must = [LEG_TOP, 53.0, 88.0, DECK[0], DECK[1], TOPDECK[0], TOPDECK[1], BODY_TOP]
    zs = [LEG_TOP]
    z = LEG_TOP
    while z < BODY_TOP - 1:
        dz = min(30.0, max(6.5, 0.62 * 2 * hw(z)))
        z = z + dz
        zs.append(min(z, BODY_TOP))
    zs = sorted(set([round(v, 2) for v in zs + must]))
    out = []
    for v in zs:          # 合并太近的
        if out and v - out[-1] < 4.0:
            if v in must: out[-1] = v
            continue
        out.append(v)
    return out


def thick(z, lo, hi):
    """杆件粗细随高度变细：lo 是塔底的、hi 是塔顶的。"""
    t = min(1.0, max(0.0, z / BODY_TOP))
    return lo + (hi - lo) * t


def build_structure():
    mb = MB()
    zs = levels()
    # ---- 四根塔脚：每根是四根弦杆的空间桁架，从地面的 9 m 宽收到 48 m 处并成角柱
    for c in CORNERS:
        def leg_pt(z, sx, sy):
            e = 0.7 + 4.0 * max(0.0, 1.0 - z / LEG_TOP)
            p = corner(c, z)
            return p + np.array([sx * e, sy * e, 0.0])
        zz = list(np.arange(0.0, LEG_TOP + 0.01, 4.0))
        for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1)):
            for z0, z1 in zip(zz[:-1], zz[1:]):
                mb.beam(leg_pt(z0, sx, sy), leg_pt(z1, sx, sy), 1.05)
        for i, (z0, z1) in enumerate(zip(zz[:-1], zz[1:])):
            pts0 = [leg_pt(z0, sx, sy) for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
            pts1 = [leg_pt(z1, sx, sy) for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
            for j in range(4):
                a0, b0, a1, b1 = pts0[j], pts0[(j + 1) % 4], pts1[j], pts1[(j + 1) % 4]
                mb.beam(a0, b0, 0.42)                                  # 横缀条
                mb.beam(a0 if i % 2 == 0 else b0, b1 if i % 2 == 0 else a1, 0.32)       # 斜缀条（之字形）
        # 脚下的基座（混凝土）
        p = corner(c, 0.0)
        mb.box((p[0] - 6.5, p[1] - 6.5, -0.5), (p[0] + 6.5, p[1] + 6.5, 1.2), DARK)
    # ---- 角柱：48 m 以上
    zc = list(np.arange(LEG_TOP, BODY_TOP + 0.01, 3.0)) + [BODY_TOP]
    for c in CORNERS:
        for z0, z1 in zip(zc[:-1], zc[1:]):
            if DETAIL:
                w = 2.6 - 1.7 * min(1.0, (z0 - LEG_TOP) / (BODY_TOP - LEG_TOP))
                mb.truss(corner(c, z0), corner(c, z1), w, w, thick(z0, 0.42, 0.2), thick(z0, 0.16, 0.08), ref=(c[0], c[1], 0))
            else:
                mb.beam(corner(c, z0), corner(c, z1), thick(z0, 1.7, 0.75))
    # ---- 四面的拱：两根弯弦杆，中间缀条
    for i in range(4):
        A, B = CORNERS[i], CORNERS[(i + 1) % 4]
        def arch_pt(s, lift):
            za = 13.0 + (ARCH_TOP - 13.0) * (1.0 - s * s) ** 0.55 + lift
            pa, pb = corner(A, za), corner(B, za)
            t = 0.5 + 0.5 * s * 0.93
            return pa + (pb - pa) * t
        ss = np.linspace(-1.0, 1.0, 25)
        inner = [arch_pt(s, 0.0) for s in ss]
        outer = [arch_pt(s, 4.5 * (1 - 0.35 * s * s)) for s in ss]
        for k in range(len(ss) - 1):
            mb.beam(inner[k], inner[k + 1], 1.0)
            mb.beam(outer[k], outer[k + 1], 0.85)
            mb.beam(inner[k], outer[k], 0.36)
            mb.beam(inner[k], outer[k + 1] if k % 2 == 0 else outer[k], 0.3)
        # 拱和塔脚之间的斜撑
        for s in (-0.55, 0.55):
            p = arch_pt(s, 4.0)
            q = corner(A if s < 0 else B, LEG_TOP - 2)
            mb.beam(p, q, 0.6)
    # ---- 四个面：每一层横梁 + 交叉斜杆 + 半高的次梁 + 分格的小斜杆
    for zi in range(len(zs) - 1):
        z0, z1 = zs[zi], zs[zi + 1]
        in_deck = (z0 >= DECK[0] - 0.1 and z1 <= DECK[1] + 0.1) or (z0 >= TOPDECK[0] - 0.1 and z1 <= TOPDECK[1] + 0.1)
        zm = 0.5 * (z0 + z1)
        tw, td, ts = thick(z0, 1.3, 0.62), thick(z0, 1.1, 0.5), thick(z0, 0.6, 0.3)
        for i in range(4):
            A, B = CORNERS[i], CORNERS[(i + 1) % 4]
            a0, b0, a1, b1 = corner(A, z0), corner(B, z0), corner(A, z1), corner(B, z1)
            am, bm = corner(A, zm), corner(B, zm)
            nrm = np.array([(A[0] + B[0]) / 2, (A[1] + B[1]) / 2, 0.0])          # 这个面的外法线
            if DETAIL and z0 < DECK[0] + 1:
                mb.truss(a0, b0, tw * 1.25, tw * 0.55, 0.22, 0.09, ref=(0, 0, 1))
            else:
                mb.beam(a0, b0, tw)
            if in_deck:
                continue
            if DETAIL:
                big = z0 < DECK[0] + 1
                for p, q in ((a0, b1), (b0, a1)):
                    if big:
                        mb.truss(p, q, td * 1.1, td * 0.55, 0.18, 0.08, ref=nrm)
                    else:
                        mb.truss(p, q, td * 0.9, td * 0.5, 0.13, 0.06, ref=nrm)
            else:
                mb.beam(a0, b1, td); mb.beam(b0, a1, td)
            mb.beam(am, bm, ts)
            # 斜杆交点到上下横梁中点的竖杆、各半格的小交叉（照片上那种“花格”）
            c0, c1 = (a0 + b0) / 2, (a1 + b1) / 2
            x = (a0 + b1 + b0 + a1) / 4
            mb.beam(c0, x, ts); mb.beam(x, c1, ts)
            if z1 - z0 > 9:
                for (p, q, r, s) in ((a0, c0, am, (am + bm) / 2), (c0, b0, (am + bm) / 2, bm), (am, (am + bm) / 2, a1, c1), ((am + bm) / 2, bm, c1, b1)):
                    mb.beam(p, s, ts * 0.8); mb.beam(q, r, ts * 0.8)
        # 隔一层加一组水平的对角撑（从外面能透过格子看到）
        if zi % 2 == 0 and not in_deck:
            for i in range(2):
                mb.beam(corner(CORNERS[i], z0), corner(CORNERS[i + 2], z0), ts)
    # 最上面一层的梁
    for i in range(4):
        mb.beam(corner(CORNERS[i], BODY_TOP), corner(CORNERS[(i + 1) % 4], BODY_TOP), 0.5)
    if DETAIL:
        build_core(mb)
        build_gussets(mb, zs)
    # ---- 大展望台：28 m 见方、两层，白墙 + 一圈窗
    z0, z1, h = DECK
    build_deck(mb, z0, z1, h, floors=2)
    # ---- 特别展望台
    z0, z1, h = TOPDECK
    build_deck(mb, z0, z1, h, floors=1, glass=TOPGLASS)
    # ---- 天线段：塔身以上一段粗的格构（数字电视天线罩一带），再往上是渐细的格构柱和最后的杆
    ring_z0, ring_z1, ring_r = BODY_TOP, 264.0, 4.4
    for k in range(16):
        a = 2 * math.pi * k / 16
        p = np.array([ring_r * math.cos(a), ring_r * math.sin(a), 0])
        mb.beam(p + [0, 0, ring_z0], p + [0, 0, ring_z1], 0.35)
    for z in np.arange(ring_z0, ring_z1 + 0.1, 2.2):
        for k in range(16):
            a0, a1 = 2 * math.pi * k / 16, 2 * math.pi * (k + 1) / 16
            mb.beam((ring_r * math.cos(a0), ring_r * math.sin(a0), z), (ring_r * math.cos(a1), ring_r * math.sin(a1), z), 0.3)
    mast0, mast1 = ring_z1, 312.0
    zz = list(np.arange(mast0, mast1 + 0.01, 3.0))
    def mpt(c, z):
        r = 2.0 - (z - mast0) / (mast1 - mast0) * 1.25
        return np.array([c[0] * r, c[1] * r, z])
    for c in CORNERS:
        for z0, z1 in zip(zz[:-1], zz[1:]):
            mb.beam(mpt(c, z0), mpt(c, z1), 0.32)
    for zi, (z0, z1) in enumerate(zip(zz[:-1], zz[1:])):
        for i in range(4):
            A, B = CORNERS[i], CORNERS[(i + 1) % 4]
            mb.beam(mpt(A, z0), mpt(B, z0), 0.18)
            mb.beam(mpt(A, z0) if zi % 2 == 0 else mpt(B, z0), mpt(B, z1) if zi % 2 == 0 else mpt(A, z1), 0.15)
    # 顶上的杆
    for z0 in np.arange(mast1, H - 0.01, 3.0):
        z1 = min(H, z0 + 3.0)
        w0 = 0.9 - (z0 - mast1) / (H - mast1) * 0.65
        mb.beam((0, 0, z0), (0, 0, z1), w0)
    return mb


def build_core(mb):
    """塔中间的电梯井（从 FootTown 屋顶到大展望台底）、53 m 和 88 m 的避难步道、沿一侧之字形上去的外楼梯（590 级）。
    尺寸是看照片估的：电梯井约 8 m 见方的格构柱，灰白色；楼梯在朝东南的那一面里侧。"""
    z0, z1 = 24.0, DECK[0]
    e = 4.0
    cs = [(e, e), (-e, e), (-e, -e), (e, -e)]
    for (x, y) in cs:
        mb.beam((x, y, z0), (x, y, z1), 0.45, SHAFT)
    zz = list(np.arange(z0, z1 + 0.01, 3.2))
    for k, (za, zb) in enumerate(zip(zz[:-1], zz[1:])):
        for i in range(4):
            (xa, ya), (xb, yb) = cs[i], cs[(i + 1) % 4]
            mb.beam((xa, ya, za), (xb, yb, za), 0.22, SHAFT)
            if k % 2 == 0:
                mb.beam((xa, ya, za), (xb, yb, zb), 0.14, SHAFT)
            else:
                mb.beam((xb, yb, za), (xa, ya, zb), 0.14, SHAFT)
    # 电梯井和塔身之间的连杆（每 ~30 m 一层）
    for z in (53.0, 88.0, 112.0):
        for c in CORNERS:
            p = corner(c, z)
            mb.beam((c[0] * e, c[1] * e, z), (p[0], p[1], z), 0.35)
    # 避难步道：53 m、88 m 一圈平台（深色格栅）+ 栏杆
    for z in (53.0, 88.0):
        r = e + 3.2
        mb.box((-r, -r, z - 0.25), (r, r, z), DARK)
        for i in range(4):
            (xa, ya), (xb, yb) = [(r, r), (-r, r), (-r, -r), (r, -r)][i], [(r, r), (-r, r), (-r, -r), (r, -r)][(i + 1) % 4]
            mb.beam((xa, ya, z + 1.1), (xb, yb, z + 1.1), 0.08, SHAFT)
            for t in np.linspace(0, 1, 7):
                mb.beam((xa + (xb - xa) * t, ya + (yb - ya) * t, z), (xa + (xb - xa) * t, ya + (yb - ya) * t, z + 1.1), 0.06, SHAFT)
    # 外楼梯：在 -y 那一面（对着芝公园）里侧，之字形，每段 4 m 高
    zz = list(np.arange(z0, z1 - 2, 4.0))
    for k, za in enumerate(zz):
        zb = za + 4.0
        y = -(hw(za) - 3.0)
        x0, x1 = (-5.0, 5.0) if k % 2 == 0 else (5.0, -5.0)
        for dy in (-0.6, 0.6):
            mb.beam((x0, y + dy, za), (x1, y + dy, zb), 0.16, SHAFT)          # 两根斜梁
        mb.box((min(x0, x1) - 1.2 if k % 2 else max(x0, x1), y - 0.7, zb - 0.12), (min(x0, x1) if k % 2 else max(x0, x1) + 1.2, y + 0.7, zb), DARK)
        mb.beam((x0, y - 0.6, za + 1.0), (x1, y - 0.6, zb + 1.0), 0.05, SHAFT)   # 扶手


def build_gussets(mb, zs):
    """节点板：横梁和角柱相交的地方一块三角形的钢板（离 360 m 看是节点处一个个小亮块，让钢架不那么“细线”）。"""
    for z in zs:
        if z > BODY_TOP - 1: continue
        t = thick(z, 1.0, 0.35)
        for c in CORNERS:
            p = corner(c, z)
            for dx, dy in ((-c[0], 0), (0, -c[1])):
                q = p + np.array([dx * 2.2 * t, dy * 2.2 * t, 0.0])
                r_ = p + np.array([0.0, 0.0, -2.0 * t])
                mb.quad(p, q, r_, r_, band(z))
                mb.quad(p, r_, r_, q, band(z))


def build_deck(mb, z0, z1, h, floors=2, glass=GLASS):
    """展望台：白色的楼板和檐口，每层一圈玻璃窗（窗后是亮着的室内），窗间是白色竖框。"""
    fz = (z1 - z0) / floors
    mb.box((-h, -h, z0), (h, h, z0 + 0.9), DECKWALL)                       # 底板
    mb.box((-h - 0.6, -h - 0.6, z1 - 0.9), (h + 0.6, h + 0.6, z1 + 0.3), DECKWALL)    # 屋檐
    for f in range(floors):
        zb = z0 + f * fz
        if f > 0:
            mb.box((-h - 0.25, -h - 0.25, zb - 0.6), (h + 0.25, h + 0.25, zb + 0.6), DECKWALL)   # 层间的白带
        wz0, wz1 = zb + (0.9 if f == 0 else 0.6), zb + fz - (0.9 if f == floors - 1 else 0.6)
        # 窗：内缩 0.4 m 的发光面（室内的灯），外面一圈竖框
        e = h - 0.4
        for (p0, p1) in (((-e, -e), (e, -e)), ((e, -e), (e, e)), ((e, e), (-e, e)), ((-e, e), (-e, -e))):
            a = np.array([p0[0], p0[1], wz0]); b = np.array([p1[0], p1[1], wz0])
            mb.quad(a, b, b + [0, 0, wz1 - wz0], a + [0, 0, wz1 - wz0], glass)
        n = int(2 * h / 1.6)
        for k in range(n + 1):
            t = -h + 2 * h * k / n
            for (x0, y0, x1, y1) in ((t - 0.12, -h - 0.05, t + 0.12, -h + 0.15), (t - 0.12, h - 0.15, t + 0.12, h + 0.05),
                                     (-h - 0.05, t - 0.12, -h + 0.15, t + 0.12), (h - 0.15, t - 0.12, h + 0.05, t + 0.12)):
                mb.box((x0, y0, wz0), (x1, y1, wz1), DECKWALL)


# ---------------------------------------------------------------- 地标灯
HPS = (1.0, 0.66, 0.30)          # 高压钠灯（约 2100 K；照片里的塔是金橙色）
LAMP_W = 24.0                    # 总亮度倍数（试渲时调：按场景的曝光，塔身要亮到金色）


def lamp_list():
    """(位置, 照射的目标点, 瓦数, 光锥角度) - 按官方的 180 盏分布摆。"""
    L = []
    zs = levels()
    # 塔脚 12：每根塔脚 3 盏，在脚下朝上照
    for c in CORNERS:
        p = corner(c, 1.5)
        for k, (dx, dy) in enumerate(((0.9, 0.9), (-0.2, 1.0), (1.0, -0.2))):
            src = p + np.array([c[0] * dx * 5.5, c[1] * dy * 5.5, 0.0])
            L.append((src, corner(c, 40.0) * np.array([0.92, 0.92, 1.0]), 2600.0, 30.0))
    # 大展望台到塔下大楼屋顶 84：拱以上到大展望台下的各层横梁上，每个角 + 每个面中间，朝上照格子
    lower = [z for z in zs if 26.0 <= z < DECK[0] - 2] + [26.0, 34.0]
    lower = sorted(set(lower))
    per = max(1, 84 // (len(lower) * 8))
    for z in lower:
        for i in range(4):
            A, B = CORNERS[i], CORNERS[(i + 1) % 4]
            for t in ([0.0] + [0.5] * per)[:2]:
                if t > 0 and z < LEG_TOP + 4:
                    continue                    # 拱下面是空的，面的中间没有东西可挂灯
                p = corner(A, z) * (1 - t) + corner(B, z) * t
                p = p * np.array([0.94, 0.94, 1.0])
                tgt = (corner(A, z + 28) * (1 - t) + corner(B, z + 28) * t) * np.array([1.12, 1.12, 1.0])
                L.append((p, tgt, 1500.0, 70.0))
    # 大展望台下 16：照展望台的底面和下面的格子
    for k in range(16):
        a = 2 * math.pi * (k + 0.5) / 16
        r = hw(DECK[0] - 6) * 0.95
        p = np.array([r * math.cos(a), r * math.sin(a), DECK[0] - 6])
        L.append((p, np.array([r * 1.35 * math.cos(a), r * 1.35 * math.sin(a), DECK[0] + 2]), 250.0, 80.0))
    # 大展望台到特别展望台 40
    mid = [z for z in zs if DECK[1] < z < TOPDECK[0]]
    for z in mid:
        for i in range(4):
            p = corner(CORNERS[i], z) * np.array([0.9, 0.9, 1.0])
            L.append((p, corner(CORNERS[i], z + 22) * np.array([1.25, 1.25, 1.0]), 850.0, 75.0))
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        r = hw(DECK[1]) * 0.9
        p = np.array([r * math.cos(a), r * math.sin(a), DECK[1] + 0.6])
        L.append((p, np.array([r * 0.7 * math.cos(a), r * 0.7 * math.sin(a), DECK[1] + 30.0]), 1000.0, 60.0))
    # 特别展望台以上 16 + 天线 12
    for k in range(16):
        a = 2 * math.pi * (k + 0.5) / 16
        p = np.array([5.0 * math.cos(a), 5.0 * math.sin(a), TOPDECK[1] + 0.8])
        L.append((p, np.array([3.2 * math.cos(a), 3.2 * math.sin(a), 290.0]), 600.0, 40.0))
    for k in range(12):
        a = 2 * math.pi * (k + 0.5) / 12
        z = 268.0 + (k % 3) * 16.0
        r = 2.4 - (z - 264.0) * 0.025
        p = np.array([r * math.cos(a), r * math.sin(a), z])
        L.append((p, np.array([0.3 * math.cos(a), 0.3 * math.sin(a), z + 40.0]), 260.0, 35.0))
    return L


def materials():
    def mat(name, color, rough=0.55, emit=None, strength=0.0, spec=0.35):
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = tuple(color) + (1,)
        b.inputs['Roughness'].default_value = rough
        try:
            b.inputs['Specular IOR Level'].default_value = spec
        except KeyError:
            pass
        if emit is not None:
            b.inputs['Emission Color'].default_value = tuple(emit) + (1,)
            b.inputs['Emission Strength'].default_value = strength
        elif name in ('tt_orange', 'tt_white'):
            # 油漆不是一个颜色：一大片一大片地深浅不一（补漆、风吹日晒）
            nt = m.node_tree
            nz = nt.nodes.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 0.15; nz.inputs['Detail'].default_value = 3.0
            tc = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(tc.outputs['Object'], nz.inputs['Vector'])
            mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['To Min'].default_value = 0.82; mr.inputs['To Max'].default_value = 1.12
            nt.links.new(nz.outputs['Fac'], mr.inputs['Value'])
            mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'; mx.inputs['Factor'].default_value = 1.0
            mx.inputs[6].default_value = tuple(color) + (1,)
            cv = nt.nodes.new('ShaderNodeCombineXYZ'); [nt.links.new(mr.outputs['Result'], cv.inputs[k]) for k in ('X', 'Y', 'Z')]
            nt.links.new(cv.outputs['Vector'], mx.inputs[7]); nt.links.new(mx.outputs[2], b.inputs['Base Color'])
        return m
    return [
        mat('tt_orange', (0.58, 0.13, 0.035), 0.5),            # 国际橙（线性；风吹日晒后没那么纯）
        mat('tt_white', (0.36, 0.35, 0.32), 0.5),
        mat('tt_deckwall', (0.45, 0.45, 0.44), 0.6),
        mat('tt_glass', (0.02, 0.02, 0.02), 0.2, emit=(1.0, 0.88, 0.72), strength=0.15),     # 大展望台里的灯
        mat('tt_dark', (0.05, 0.05, 0.05), 0.8),
        mat('tt_lamp', (0.1, 0.1, 0.1), 0.5, emit=HPS, strength=6.0),                       # 灯具本身（照片上的亮点）
        mat('tt_red', (0.1, 0.0, 0.0), 0.5, emit=(1.0, 0.05, 0.02), strength=120.0),          # 航空障碍灯
        mat('tt_topglass', (0.02, 0.02, 0.02), 0.2, emit=(0.85, 0.92, 1.0), strength=0.30),   # 特别展望台（LED，偏冷）
        mat('tt_shaft', (0.42, 0.42, 0.40), 0.6),                                            # 电梯井、楼梯（灰白）
    ]


def build(coll, origin=(0.0, 0.0, 0.0), rot=0.0, lamp_scale=1.0, lamps_visible=True):
    """搭塔、放灯。返回塔的物体。"""
    mats = materials()
    mb = build_structure()
    ob = mb.to_object('TokyoTower', mats, coll, origin, rot)
    # 灯具本身（照片上的一个个亮点）和航空障碍灯：单独一个物体，只给镜头看，不参与照明（照明交给下面的聚光灯）
    heads = MB()
    lamps = lamp_list()
    for p, tgt, w, cone in lamps:
        heads.box(tuple(p - 0.25), tuple(p + 0.25), LAMP)
    for z in (H - 0.3, 300.0, 280.0):
        r = 0.6 if z > 310 else 1.6
        for c in CORNERS:
            q = np.array([c[0] * r, c[1] * r, z])
            heads.box(tuple(q - 0.3), tuple(q + 0.3), RED)
    for c in CORNERS:
        q = np.array([c[0] * (TOPDECK[2] + 0.7), c[1] * (TOPDECK[2] + 0.7), TOPDECK[1] + 0.4])
        heads.box(tuple(q - 0.3), tuple(q + 0.3), RED)
    ho = heads.to_object('TokyoTowerLamps', mats, coll, origin, rot)
    ho.visible_diffuse = False; ho.visible_glossy = False; ho.visible_transmission = False; ho.visible_shadow = False
    # 光源
    o = Vector(origin); R = Vector((0, 0, 1)).to_track_quat('Z', 'Y')
    cr, sr = math.cos(rot), math.sin(rot)
    def world(p):
        return o + Vector((p[0] * cr - p[1] * sr, p[0] * sr + p[1] * cr, p[2]))
    for k, (p, tgt, w, cone) in enumerate(lamps):
        ld = bpy.data.lights.new('tt_lamp%d' % k, 'SPOT')
        ld.energy = w * LAMP_W * lamp_scale; ld.color = HPS
        ld.spot_size = math.radians(cone); ld.spot_blend = 0.6; ld.shadow_soft_size = 0.5
        lo = bpy.data.objects.new('tt_lamp%d' % k, ld); coll.objects.link(lo)
        a, b = world(p), world(tgt)
        lo.location = a
        lo.rotation_euler = (b - a).to_track_quat('-Z', 'Y').to_euler()
        lo.visible_glossy = False
    print('TOWER_DONE verts %d faces %d lamps %d total %.0f W' % (len(mb.v), len(mb.f), len(lamps), sum(l[2] for l in lamps) * LAMP_W * lamp_scale))
    return ob
