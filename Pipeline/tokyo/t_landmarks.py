# -*- coding: utf-8 -*-
"""东京塔周边的真实建筑（Blender 里运行）：按 t_geo.LANDMARKS 的真实坐标、高度、层数摆，外形按照片估。
窗户用 t_city 的同一个外墙材质（层高 = 高度 / 层数，所以亮灯的楼层数和真楼一样）。
另外：增上寺的大殿和三解脱门（传统屋顶，夜里有暖色的灯打亮）、东京塔脚下的 FootTown。"""
import bpy, math
import numpy as np
import t_geo as G
import t_city as C

FACADES = {
    'glass':    dict(ww=1.5, mu=0.035, mv0=0.13, mv1=0.95, grp=40.0, warm=0.06, litp=0.55, glass=1.0, bright=0.62, wall=(0.020, 0.028, 0.034)),      # 办公楼夜里是一整层一整层地亮
    'office':   dict(ww=1.9, mu=0.10, mv0=0.22, mv1=0.88, grp=12.0, warm=0.15, litp=0.45, glass=0.0, bright=0.75, wall=(0.10, 0.105, 0.11)),
    'hotel':    dict(ww=3.8, mu=0.16, mv0=0.18, mv1=0.84, grp=1.0, warm=0.85, litp=0.50, glass=0.0, bright=0.70, wall=(0.15, 0.14, 0.13)),
    'concrete': dict(ww=2.6, mu=0.18, mv0=0.30, mv1=0.80, grp=3.0, warm=0.35, litp=0.30, glass=0.0, bright=0.85, wall=(0.17, 0.165, 0.16)),
    'foottown': dict(ww=3.0, mu=0.10, mv0=0.22, mv1=0.82, grp=2.0, warm=0.75, litp=0.65, glass=0.0, bright=1.1, wall=(0.10, 0.065, 0.045)),
}


def attrs(kind, seed, fh, rng):
    f = FACADES[kind]
    a = dict(seed=float(seed), fh=fh, ww=f['ww'], mu=f['mu'], mv0=f['mv0'], mv1=f['mv1'], grp=f['grp'], warm=f['warm'],
             litp=float(np.clip(f['litp'] + rng.uniform(-0.1, 0.1), 0.1, 0.9)), glass=f['glass'], bright=f['bright'] * float(rng.uniform(0.9, 1.1)))
    a.update(wr=f['wall'][0], wg=f['wall'][1], wb=f['wall'][2])
    return a


def rounded_rect(w, d, r, n=5):
    """圆角矩形，逆时针，中心在原点"""
    r = min(r, w / 2 - 0.01, d / 2 - 0.01)
    pts = []
    for cx, cy, a0 in ((w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90), (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def prism(bl, cx, cy, rot, pts, z0, z1, a, s0=1.0, s1=1.0, top=None, roof=True):
    """多边形柱：底 z0 顶 z1，按 s0→s1 线性缩放（上宽下窄或收分），top(x, y) 可以让顶边高低起伏。墙的 u 是周长米数。"""
    c, s = math.cos(rot), math.sin(rot)
    def P(x, y, z, k):
        x, y = x * k, y * k
        return (cx + x * c - y * s, cy + x * s + y * c, z)
    n = len(pts); u = 0.0
    tops = [z1 + (top(px, py) if top else 0.0) for (px, py) in pts]
    for i in range(n):
        (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
        L = math.hypot(x1 - x0, y1 - y0) * s0
        bl.face([P(x0, y0, z0, s0), P(x1, y1, z0, s0), P(x1, y1, tops[(i + 1) % n], s1), P(x0, y0, tops[i], s1)],
                [(u, z0), (u + L, z0), (u + L, tops[(i + 1) % n]), (u, tops[i])], a)
        u += L
    if roof:
        r = dict(a); r.update(mv0=2.0, mv1=1.0, wr=0.06, wg=0.06, wb=0.065, glass=0.0)
        bl.face([P(x, y, tops[i] - 0.01, s1) for i, (x, y) in enumerate(pts)], [(0, 0)] * n, r)
    return [P(x, y, tops[i], s1) for i, (x, y) in enumerate(pts)]


def build(coll, bl, seed=41):
    """把真实建筑加进 bl（t_city.Buildings）。返回每栋的 (x, y, 半径)，给城市填充、树避让用。"""
    rng = np.random.default_rng(seed)
    occupied = []
    crown_pts = []
    for (name, lat, lon, h, floors, plan, w, d, rdeg, top, kind) in G.LANDMARKS:
        x, y = G.scene(lat, lon)
        if G.covered(x, y):
            continue                                    # 这一块用 PLATEAU 的真实建筑
        rot = G.scene_rot(rdeg)
        fh = h / max(1, floors)
        a = attrs(kind, rng.integers(0, 100000), fh, rng)
        if plan == 'round':
            pts = rounded_rect(w, d, min(w, d) * 0.22)
        else:
            pts = rounded_rect(w, d, 0.6, 1)
        if top == 'wave':
            # 麻布台ヒルズ森JPタワー：上面比下面宽一点，顶边是波浪（四个面的中间高、角上低）
            def wave(px, py):
                th = math.atan2(py, px)
                return -9.0 * math.sin(2 * th) ** 2
            prism(bl, x, y, rot, pts, 0.0, h * 0.55, a, 0.94, 1.0, roof=False)
            prism(bl, x, y, rot, pts, h * 0.55, h, a, 1.0, 1.06, top=wave)
        elif top == 'step':
            h1 = h * 0.86
            prism(bl, x, y, rot, pts, 0.0, h1, a)
            prism(bl, x, y, rot, [(px * 0.8, py * 0.8) for px, py in pts], h1, h, a)
        elif top == 'chamfer':
            h1 = h * 0.9
            prism(bl, x, y, rot, pts, 0.0, h1, a)
            prism(bl, x, y, rot, pts, h1, h, a, 1.0, 0.72)
        else:
            prism(bl, x, y, rot, pts, 0.0, h, a)
        if top == 'crown':
            crown_pts.append((x, y, rot, pts, h))
        # 楼顶的红色航空障碍灯
        if h > 60:
            c, s = math.cos(rot), math.sin(rot)
            for (px, py) in (pts[0], pts[len(pts) // 4], pts[len(pts) // 2], pts[3 * len(pts) // 4]):
                bl.red.append((x + px * c - py * s, y + px * s + py * c, h + 1.0))
        occupied.append((x, y, math.hypot(w, d) / 2 + 6.0))
    # 东京塔脚下的 FootTown：PLATEAU 里有（t_plateau_build 留了塔那栋楼 30 m 以下的部分）
    tx, ty = 0.0, G.TOWER_D
    import t_layout as T
    if not G.covered(tx, ty):
        a = attrs('foottown', 777, 4.8, rng)
        prism(bl, tx, ty, math.radians(T.TOWER_ROT), rounded_rect(70.0, 54.0, 1.0, 1), 0.0, 24.0, a)
    occupied.append((tx, ty, 60.0))
    # 顶上一圈灯（六本木ヒルズ、虎ノ门ステーションタワー之类）
    if crown_pts:
        m = C.emissive('crown', (0.85, 0.92, 1.0), 2.2)
        me = bpy.data.meshes.new('crowns'); vs, fs = [], []
        for (x, y, rot, pts, h) in crown_pts:
            c, s = math.cos(rot), math.sin(rot)
            for i in range(len(pts)):
                (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % len(pts)]
                p0 = (x + 1.01 * (x0 * c - y0 * s), y + 1.01 * (x0 * s + y0 * c)); p1 = (x + 1.01 * (x1 * c - y1 * s), y + 1.01 * (x1 * s + y1 * c))
                b = len(vs)
                vs += [(p0[0], p0[1], h - 4.0), (p1[0], p1[1], h - 4.0), (p1[0], p1[1], h - 2.6), (p0[0], p0[1], h - 2.6)]
                fs.append((b, b + 1, b + 2, b + 3))
        me.from_pydata(vs, [], fs); me.materials.append(m)
        co = bpy.data.objects.new('Crowns', me); coll.objects.link(co)
    occupied += zojoji(coll)
    print('LANDMARKS_DONE %d' % len(G.LANDMARKS))
    return occupied


# ---------------------------------------------------------------- 增上寺
def _mat(name, color, rough=0.6, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = tuple(color) + (1,); b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = tuple(emit) + (1,); b.inputs['Emission Strength'].default_value = strength
    C.haze_wrap(m)
    return m


def temple_roof(vs, fs, cx, cy, rot, w, d, z_eave, z_ridge, over, ridge_frac=0.45):
    """入母屋风的大屋顶：四面坡，屋檐挑出 over 米、檐口两头略翘；上面一条屋脊。分两段坡（下缓上陡）。"""
    c, s = math.cos(rot), math.sin(rot)
    def P(x, y, z):
        return (cx + x * c - y * s, cy + x * s + y * c, z)
    W, D = w / 2 + over, d / 2 + over
    rw, rd = W * ridge_frac, 0.4
    zm = z_eave + (z_ridge - z_eave) * 0.42
    Wm, Dm = W * 0.72, D * 0.62
    rings = [[(-W, -D, z_eave + 0.9), (W, -D, z_eave + 0.9), (W, D, z_eave + 0.9), (-W, D, z_eave + 0.9)],
             [(-Wm, -Dm, zm), (Wm, -Dm, zm), (Wm, Dm, zm), (-Wm, Dm, zm)],
             [(-rw, -rd, z_ridge), (rw, -rd, z_ridge), (rw, rd, z_ridge), (-rw, rd, z_ridge)]]
    # 檐口中间略低（四角起翘）
    mid_lo = [(0, -D, z_eave), (W, 0, z_eave), (0, D, z_eave), (-W, 0, z_eave)]
    for ring_a, ring_b in ((rings[0], rings[1]), (rings[1], rings[2])):
        for i in range(4):
            a0, a1, b0, b1 = ring_a[i], ring_a[(i + 1) % 4], ring_b[i], ring_b[(i + 1) % 4]
            if ring_a is rings[0]:
                m = mid_lo[i]
                base = len(vs); vs += [P(*a0), P(*m), P(*b0)]; fs.append((base, base + 1, base + 2))
                base = len(vs); vs += [P(*m), P(*a1), P(*b1), P(*b0)]; fs.append((base, base + 1, base + 2, base + 3))
            else:
                base = len(vs); vs += [P(*a0), P(*a1), P(*b1), P(*b0)]; fs.append((base, base + 1, base + 2, base + 3))
    base = len(vs); vs += [P(*p) for p in rings[2]]; fs.append((base, base + 1, base + 2, base + 3))


def zojoji(coll):
    """大殿（约 40 m 见方、屋脊约 25 m）和三解脱门（约 28 x 12 m、21 m 高、朱漆、两层屋顶）。尺寸是估的。"""
    roof = _mat('zj_roof', (0.030, 0.032, 0.034), 0.85)
    # 瓦：按高度一道一道的横纹（瓦垄），不然屋顶是一块光滑的板，月光下发蓝
    nt = roof.node_tree; N = nt.nodes
    b = [n for n in N if n.type == 'BSDF_PRINCIPLED'][0]
    tc = N.new('ShaderNodeTexCoord'); sep = N.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Object'], sep.inputs['Vector'])
    mul = N.new('ShaderNodeMath'); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = 2 * math.pi / 0.32
    nt.links.new(sep.outputs['Z'], mul.inputs[0])
    sn = N.new('ShaderNodeMath'); sn.operation = 'SINE'; nt.links.new(mul.outputs[0], sn.inputs[0])
    mr = N.new('ShaderNodeMapRange'); mr.inputs['To Min'].default_value = 0.45; mr.inputs['To Max'].default_value = 1.0
    mr.inputs['From Min'].default_value = -1.0; nt.links.new(sn.outputs[0], mr.inputs['Value'])
    mc = N.new('ShaderNodeMix'); mc.data_type = 'RGBA'; mc.blend_type = 'MULTIPLY'; mc.inputs['Factor'].default_value = 1.0
    mc.inputs[6].default_value = (0.030, 0.032, 0.034, 1); nt.links.new(mr.outputs['Result'], mc.inputs[7])
    nt.links.new(mc.outputs[2], b.inputs['Base Color'])
    wall = _mat('zj_wall', (0.26, 0.17, 0.11), 0.7)               # 木构的墙（不是白墙）
    lacquer = _mat('zj_lacquer', (0.42, 0.045, 0.02), 0.45)
    stone = _mat('zj_stone', (0.30, 0.29, 0.27), 0.8)
    warm_win = _mat('zj_win', (0.05, 0.03, 0.02), 0.6, emit=(1.0, 0.72, 0.42), strength=0.015)      # 殿里的灯，从门缝透出来一点（0.5 时像个发光的盒子）
    out = []
    def boxes(name, mat, items):
        me = bpy.data.meshes.new(name); vs, fs = [], []
        for (cx, cy, rot, w, d, z0, z1) in items:
            c, s = math.cos(rot), math.sin(rot)
            def P(x, y, z):
                return (cx + x * c - y * s, cy + x * s + y * c, z)
            b = len(vs)
            for z in (z0, z1):
                for (x, y) in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
                    vs.append(P(x, y, z))
            fs += [(b, b + 1, b + 5, b + 4), (b + 1, b + 2, b + 6, b + 5), (b + 2, b + 3, b + 7, b + 6), (b + 3, b, b + 4, b + 7), (b + 4, b + 5, b + 6, b + 7)]
        me.from_pydata(vs, [], fs); me.materials.append(mat)
        o = bpy.data.objects.new(name, me); coll.objects.link(o)
    # 大殿（PLATEAU 方格里就用真实的形状，这里只打灯）
    x, y = G.scene(*G.ZOJOJI_DAIDEN); rot = G.scene_rot(90.0)        # 正面朝东（估）
    hall = not G.covered(x, y)
    if hall:
        boxes('zj_podium', stone, [(x, y, rot, 52, 48, 0.0, 2.6)])
    if hall:
        boxes('zj_hall', wall, [(x, y, rot, 40, 36, 2.6, 12.5)])
    cols = []
    for k in range(9):
        t = -18 + 4.5 * k
        for (px, py) in ((t, -18.6), (t, 18.6), (-20.6, t * 0.9), (20.6, t * 0.9)):
            c, s = math.cos(rot), math.sin(rot)
            cols.append((x + px * c - py * s, y + px * s + py * c, rot, 0.7, 0.7, 2.6, 12.5))
    if hall:
        boxes('zj_cols', lacquer, cols)
        boxes('zj_doors', warm_win, [(x, y, rot, 40.3, 30, 4.0, 10.5), (x, y, rot, 32, 36.3, 4.0, 10.5)])
        vs, fs = [], []
        temple_roof(vs, fs, x, y, rot, 40, 36, 12.5, 26.0, 5.0)
        me = bpy.data.meshes.new('zj_roofs'); me.from_pydata(vs, [], fs); me.materials.append(roof)
        o = bpy.data.objects.new('zj_roofs', me); coll.objects.link(o)
    out.append((x, y, 40.0))
    lights = [(x + 30 * math.cos(rot) - dy * math.sin(rot), y + 30 * math.sin(rot) + dy * math.cos(rot), dy) for dy in (-14, 0, 14)]
    # 三解脱门
    gx, gy = G.scene(*G.ZOJOJI_GATE); grot = G.scene_rot(90.0)
    boxes('zj_gate_base', stone, [(gx, gy, grot, 30, 14, 0.0, 1.2)])
    gcols = []
    for k in range(6):
        t = -12.5 + 5 * k
        for py in (-5.5, 0.0, 5.5):
            c, s = math.cos(grot), math.sin(grot)
            gcols.append((gx + t * c - py * s, gy + t * s + py * c, grot, 0.8, 0.8, 1.2, 7.5))
    boxes('zj_gate_cols', lacquer, gcols)
    boxes('zj_gate_mid', lacquer, [(gx, gy, grot, 26, 10, 9.0, 15.0)])
    vs, fs = [], []
    temple_roof(vs, fs, gx, gy, grot, 26, 11, 7.6, 10.0, 2.4, 0.9)
    temple_roof(vs, fs, gx, gy, grot, 26, 10, 15.0, 21.0, 3.2, 0.5)
    me = bpy.data.meshes.new('zj_gate_roofs'); me.from_pydata(vs, [], fs); me.materials.append(roof)
    o = bpy.data.objects.new('zj_gate_roofs', me); coll.objects.link(o)
    out.append((gx, gy, 25.0))
    # 夜里打亮的灯：大殿正面、三解脱门前后（暖色）
    def spot(px, py, pz, tx, ty, tz, w):
        ld = bpy.data.lights.new('zj_light', 'SPOT'); ld.energy = w; ld.color = (1.0, 0.78, 0.52); ld.spot_size = math.radians(70); ld.spot_blend = 0.6
        lo = bpy.data.objects.new('zj_light', ld); coll.objects.link(lo); lo.location = (px, py, pz)
        from mathutils import Vector
        lo.rotation_euler = (Vector((tx, ty, tz)) - Vector((px, py, pz))).to_track_quat('-Z', 'Y').to_euler()
    if not hall:
        # PLATEAU 里的大殿：灯从院子里朝殿打（殿的中心按 PLATEAU 那个综合体估，在大殿坐标东南 20 m 左右）
        lights = [(x + 45 * math.cos(rot) - dy * math.sin(rot), y + 45 * math.sin(rot) + dy * math.cos(rot), dy) for dy in (-20, 0, 20)]
    for (lx, ly, dy) in lights:
        spot(lx, ly, 0.6, x, y + 0, 14.0, 2000.0)
    for side in (-1, 1):
        c, s = math.cos(grot), math.sin(grot)
        for t in (-8, 8):
            px, py = gx + t * c - side * 16 * s, gy + t * s + side * 16 * c
            spot(px, py, 0.5, gx + t * 0.5 * c, gy + t * 0.5 * s, 12.0, 5000.0)
    return out
