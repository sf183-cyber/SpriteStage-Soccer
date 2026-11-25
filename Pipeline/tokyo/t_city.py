# -*- coding: utf-8 -*-
"""东京塔周围的城市（Blender 里运行）：程序生成的楼群，夜里窗户按楼层、按房间随机亮着灯。

每栋楼是一个盒子（高楼有一两级退台），墙面的 UV 是“沿墙的米数、离地的米数”，窗户、楼层、亮不亮全在着色器里按
面属性算（楼的随机种子、层高、开间、亮灯比例、冷暖……），所以几千栋楼只有一个材质、一个网格。
另外：高于 60 m 的楼顶四角有红色航空障碍灯；近处一些中低层楼的墙上挂竖招牌；主要的路边有路灯。
塔脚下的“FootTown”（5 层、深褐色）也在这里。
没有任何真实的楼名、商标、文字。"""
import bpy, math
import numpy as np
import t_layout as T
U_SPECIES = T.U.SPECIES

HAZE = (0.0105, 0.0085, 0.0078)       # 远处空气的颜色（城市灯光照亮的雾霾，偏暖灰）
HAZE_L = 8000.0
USE_PHOTO_ATLAS = False           # 整张贴 PLATEAU 外墙照片（试过，花斑；现在只用每面墙的平均颜色，见 t_plateau_build）                    # 雾霾的“能见距离”（米）：exp(-d/L)


class Buildings:
    def __init__(self):
        self.v = []; self.f = []; self.uv = []; self.attr = {k: [] for k in
            ('seed', 'fh', 'ww', 'mu', 'mv0', 'mv1', 'grp', 'warm', 'litp', 'wr', 'wg', 'wb', 'glass', 'bright', 'kind', 'photo', 'patlas', 'pmean')}
        self.uv2 = []          # PLATEAU 外墙照片在图集里的 UV（没有照片的面是 0）
        self.red = []          # 航空障碍灯的位置
        self.signs = []        # (中心, 朝向向量, 宽, 高, 颜色)

    def face(self, pts, uvs, a, uv2=None):
        base = len(self.v)
        self.v.extend(pts); self.f.append(tuple(range(base, base + len(pts)))); self.uv.extend(uvs)
        self.uv2.extend(uv2 if uv2 is not None else [(0.0, 0.0)] * len(pts))
        if 'photo' not in a:
            a = dict(a); a['photo'] = 0.0; a['patlas'] = 0.0; a['pmean'] = 0.2
        if 'kind' not in a:
            # 0 玻璃幕墙办公、1 普通办公/商住、2 住宅/酒店、3 寺院
            a = dict(a); a['kind'] = 0.0 if a['glass'] > 0.5 else (2.0 if a['warm'] >= 0.7 else 1.0)
        for k in self.attr:
            self.attr[k].append(a[k])

    def box(self, cx, cy, w, d, z0, z1, rot, a):
        c, s = math.cos(rot), math.sin(rot)
        def P(x, y, z):
            return (cx + x * c - y * s, cy + x * s + y * c, z)
        corners = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
        lens = [w, d, w, d]
        u0 = 0.0
        for i in range(4):
            (x0, y0), (x1, y1) = corners[i], corners[(i + 1) % 4]
            L = lens[i]
            self.face([P(x0, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y0, z1)],
                      [(u0, z0), (u0 + L, z0), (u0 + L, z1), (u0, z1)], a)
            u0 += L + 7.3
        roof = dict(a); roof['mv0'], roof['mv1'] = 2.0, 1.0; roof['wr'], roof['wg'], roof['wb'] = 0.09, 0.09, 0.09; roof['glass'] = 0.0
        self.face([P(*corners[0], z1), P(*corners[1], z1), P(*corners[2], z1), P(*corners[3], z1)], [(0, 0)] * 4, roof)

    def to_object(self, name, mat, coll):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        uvl = me.uv_layers.new(name='UVMap')
        loops = np.array([li for p in me.polygons for li in p.loop_indices])
        vidx = np.zeros(len(me.loops), np.int64); me.loops.foreach_get('vertex_index', vidx)
        uv = np.array(self.uv, np.float32)[vidx]
        uvl.data.foreach_set('uv', uv.ravel())
        uvp = me.uv_layers.new(name='PhotoUV')
        uvp.data.foreach_set('uv', np.array(self.uv2, np.float32)[vidx].ravel())
        for k, vals in self.attr.items():
            at = me.attributes.new(k, 'FLOAT', 'FACE')
            at.data.foreach_set('value', np.array(vals, np.float32))
        me.materials.append(mat)
        me.update()
        ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
        return ob


def facade_material():
    """外墙（2026-11-24 第二版）。u/v 是墙上的米数。
    第一版的窗是一整块白：远看像白色棋盘格。现在每个窗：
      - 窗框和竖梃（宽窗分 2-3 格）、办公楼的横档；寺院是细格子门
      - 室内的光不是平的：上面亮（天花板的灯，办公楼能看到一排排灯管），下面暗（桌子、家具）
      - 有的拉着百叶（暗、有横纹），住宅有的拉着窗帘（暖色、暗）
      - 色温：办公楼冷白和带一点绿的日光灯，住宅暖黄，也有 LED 白；整体亮度压低，不再冲成纯白
      - 没亮的窗是深色玻璃，会反光
      - 底层的店铺不再一律亮着（第一版连成一条发光的“墙”）
    墙面加一点按位置变化的深浅（板材、污渍）。"""
    m = bpy.data.materials.new('city_facade'); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; Lk = nt.links
    for n in list(N): N.remove(n)
    out = N.new('ShaderNodeOutputMaterial')
    def attr(name):
        a = N.new('ShaderNodeAttribute'); a.attribute_type = 'GEOMETRY'; a.attribute_name = name; return a.outputs['Fac']
    def M(op, a, b=None, c=None):
        n = N.new('ShaderNodeMath'); n.operation = op
        for i, x in enumerate((a, b, c)):
            if x is None: continue
            if isinstance(x, (int, float)): n.inputs[i].default_value = x
            else: Lk.new(x, n.inputs[i])
        return n.outputs[0]
    def mix(f, a, b):                     # a*(1-f) + b*f  （标量）
        return M('ADD', M('MULTIPLY', a, M('SUBTRACT', 1.0, f)), M('MULTIPLY', b, f))
    def smooth(e0, e1, x):
        n = N.new('ShaderNodeMapRange'); n.interpolation_type = 'SMOOTHSTEP'
        n.inputs['From Min'].default_value = e0; n.inputs['From Max'].default_value = e1
        Lk.new(x, n.inputs['Value']); return n.outputs['Result']
    def rgb(c):
        n = N.new('ShaderNodeRGB'); n.outputs[0].default_value = tuple(c) + (1,); return n.outputs[0]
    def cmix(f, a, b):
        n = N.new('ShaderNodeMix'); n.data_type = 'RGBA'
        Lk.new(f, n.inputs['Factor']); Lk.new(a, n.inputs[6]); Lk.new(b, n.inputs[7]); return n.outputs[2]
    def wnoise(x, y, z, w):
        c = N.new('ShaderNodeCombineXYZ'); Lk.new(x, c.inputs['X']); Lk.new(y, c.inputs['Y']); Lk.new(z, c.inputs['Z'])
        n = N.new('ShaderNodeTexWhiteNoise'); n.noise_dimensions = '4D'
        Lk.new(c.outputs['Vector'], n.inputs['Vector']); n.inputs['W'].default_value = w
        return n.outputs['Value']
    uvn = N.new('ShaderNodeUVMap'); uvn.uv_map = 'UVMap'
    sep = N.new('ShaderNodeSeparateXYZ'); Lk.new(uvn.outputs['UV'], sep.inputs['Vector'])
    u, v = sep.outputs['X'], sep.outputs['Y']
    fh, ww, seed, kind = attr('fh'), attr('ww'), attr('seed'), attr('kind')
    is_glass = M('LESS_THAN', kind, 0.5)
    is_res = M('MULTIPLY', M('GREATER_THAN', kind, 1.5), M('LESS_THAN', kind, 2.5))
    is_temple = M('GREATER_THAN', kind, 2.5)
    fl = M('FLOOR', M('DIVIDE', v, fh)); fv = M('FRACT', M('DIVIDE', v, fh))
    bay = M('FLOOR', M('DIVIDE', u, ww)); fu = M('FRACT', M('DIVIDE', u, ww))
    grp = M('FLOOR', M('DIVIDE', bay, attr('grp')))
    mu, mv0, mv1 = attr('mu'), attr('mv0'), attr('mv1')
    win = M('MULTIPLY', M('MULTIPLY', M('GREATER_THAN', fu, mu), M('LESS_THAN', fu, M('SUBTRACT', 1.0, mu))),
            M('MULTIPLY', M('GREATER_THAN', fv, mv0), M('LESS_THAN', fv, mv1)))
    # 窗内的坐标 0..1
    wu = M('DIVIDE', M('SUBTRACT', fu, mu), M('MAXIMUM', M('SUBTRACT', 1.0, M('MULTIPLY', mu, 2.0)), 0.01))
    wv = M('DIVIDE', M('SUBTRACT', fv, mv0), M('MAXIMUM', M('SUBTRACT', mv1, mv0), 0.01))
    # 竖梃：宽窗分格（每格约 1.1 m）；寺院是细格子
    panes = M('MAXIMUM', 1.0, M('FLOOR', M('ADD', M('DIVIDE', M('MULTIPLY', ww, M('SUBTRACT', 1.0, M('MULTIPLY', mu, 2.0))), 1.1), 0.5)))
    panes = mix(is_temple, panes, 6.0)
    fp = M('FRACT', M('MULTIPLY', wu, panes))
    mull = M('MAXIMUM', M('LESS_THAN', fp, 0.045), M('GREATER_THAN', fp, 0.955))
    transom = M('MULTIPLY', M('LESS_THAN', M('ABSOLUTE', M('SUBTRACT', wv, 0.80)), 0.018), M('SUBTRACT', 1.0, is_glass))
    shoji = M('MULTIPLY', is_temple, M('LESS_THAN', M('FRACT', M('MULTIPLY', wv, 8.0)), 0.07))
    frame = M('MAXIMUM', M('MAXIMUM', mull, transom), shoji)
    pane = M('MULTIPLY', win, M('SUBTRACT', 1.0, frame))          # 玻璃（能透出室内光的部分）
    # 随机数：组（整层/一段）、每个窗
    h1 = wnoise(seed, fl, grp, 0.0); h2 = wnoise(seed, fl, grp, 1.7); h3 = wnoise(seed, fl, grp, 3.3)
    hb = wnoise(seed, fl, bay, 5.1); hb2 = wnoise(seed, fl, bay, 7.9)
    ground = M('LESS_THAN', fl, 0.5)
    litp = mix(ground, attr('litp'), 0.42)
    lit = M('MULTIPLY', M('LESS_THAN', h1, litp), M('GREATER_THAN', hb, 0.10))
    bright = M('MULTIPLY', attr('bright'), M('ADD', 0.55, M('MULTIPLY', h3, 0.75)))
    bright = M('MULTIPLY', bright, M('ADD', 0.60, M('MULTIPLY', hb, 0.55)))
    bright = M('MULTIPLY', bright, M('ADD', 1.0, M('MULTIPLY', ground, 0.5)))
    # 室内的光：上亮下暗；办公楼天花板一排排灯管
    prof = M('MULTIPLY', M('ADD', 0.50, M('MULTIPLY', smooth(0.45, 1.0, wv), 0.75)), M('ADD', 0.45, M('MULTIPLY', smooth(0.08, 0.40, wv), 0.55)))
    tubes = M('MULTIPLY', M('MULTIPLY', M('LESS_THAN', M('FRACT', M('ADD', M('MULTIPLY', u, 0.62), seed)), 0.22), smooth(0.80, 0.94, wv)),
              M('SUBTRACT', 1.0, is_res))
    prof = M('ADD', prof, M('MULTIPLY', tubes, 0.55))
    # 百叶（暗、横纹）和窗帘（住宅：暖、暗）
    blind = M('LESS_THAN', hb2, mix(is_res, 0.28, 0.15))
    slats = M('ADD', 0.30, M('MULTIPLY', M('LESS_THAN', M('FRACT', M('MULTIPLY', wv, 26.0)), 0.55), 0.14))
    curtain = M('MULTIPLY', is_res, M('LESS_THAN', hb2, 0.55))
    prof = mix(blind, prof, slats)
    prof = mix(M('MULTIPLY', curtain, M('SUBTRACT', 1.0, blind)), prof, 0.42)
    k = M('MULTIPLY', M('MULTIPLY', M('MULTIPLY', pane, lit), bright), prof)
    # 色温：暖（住宅、酒店）/ 冷白 / 带绿的日光灯 / LED 白；窗帘是暖米色
    warm = M('LESS_THAN', h2, attr('warm'))
    cool = cmix(M('GREATER_THAN', h3, 0.55), rgb((0.80, 0.92, 1.0)), rgb((0.86, 1.0, 0.90)))
    cool = cmix(M('GREATER_THAN', h3, 0.85), cool, rgb((1.0, 0.96, 0.90)))
    col = cmix(warm, cool, rgb((1.0, 0.70, 0.42)))
    col = cmix(M('MULTIPLY', curtain, M('SUBTRACT', 1.0, blind)), col, rgb((1.0, 0.62, 0.36)))
    col = cmix(is_temple, col, rgb((1.0, 0.66, 0.36)))
    # 墙：按位置的深浅变化（板材、污渍）
    nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 0.08; nz.inputs['Detail'].default_value = 4.0
    cxyz = N.new('ShaderNodeCombineXYZ'); Lk.new(u, cxyz.inputs['X']); Lk.new(v, cxyz.inputs['Y']); Lk.new(seed, cxyz.inputs['Z'])
    Lk.new(cxyz.outputs['Vector'], nz.inputs['Vector'])
    wallk = M('ADD', 0.78, M('MULTIPLY', nz.outputs['Fac'], 0.45))
    wallc = N.new('ShaderNodeCombineXYZ'); Lk.new(attr('wr'), wallc.inputs['X']); Lk.new(attr('wg'), wallc.inputs['Y']); Lk.new(attr('wb'), wallc.inputs['Z'])
    wallv = N.new('ShaderNodeVectorMath'); wallv.operation = 'SCALE'; Lk.new(wallc.outputs['Vector'], wallv.inputs[0]); Lk.new(wallk, wallv.inputs['Scale'])
    # ---- PLATEAU 的外墙照片（白天的航拍）：photo 1 = 只用颜色；2 = 分辨率够，照片里暗的（玻璃）当窗，按楼层随机点亮
    import os as _os
    photo, patlas = attr('photo'), attr('patlas')
    puv = N.new('ShaderNodeUVMap'); puv.uv_map = 'PhotoUV'
    def atlas_tex(i):
        f = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), 'out', 'plateau_atlas_%d.jpg' % i)
        t = N.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(f, check_existing=True); t.extension = 'EXTEND'
        Lk.new(puv.outputs['UV'], t.inputs['Vector']); return t.outputs['Color']
    if USE_PHOTO_ATLAS:
        pcol = cmix(M('GREATER_THAN', patlas, 0.5), atlas_tex(0), atlas_tex(1))
    else:
        pcol = rgb((0.2, 0.2, 0.2))             # 不用图集（两张 8192 的图在 Cycles 里要占约 0.5 GB 显存）
    pl = N.new('ShaderNodeRGBToBW'); Lk.new(pcol, pl.inputs['Color'])
    pmean = attr('pmean')
    pmask = M('MULTIPLY', M('GREATER_THAN', photo, 1.5),
              smooth(0.0, 1.0, M('DIVIDE', M('SUBTRACT', M('MULTIPLY', pmean, 0.92), pl.outputs['Val']), M('MAXIMUM', M('MULTIPLY', pmean, 0.35), 0.01))))
    plit = M('MULTIPLY', M('MULTIPLY', pmask, M('LESS_THAN', h1, attr('litp'))), M('GREATER_THAN', hb, 0.12))
    pk = M('MULTIPLY', M('MULTIPLY', plit, bright), M('ADD', 0.55, M('MULTIPLY', fv, 0.6)))
    k = M('ADD', k, M('MULTIPLY', pk, 0.85))
    glassy = M('MAXIMUM', pane, M('MULTIPLY', attr('glass'), M('SUBTRACT', 1.0, M('MULTIPLY', win, frame))))
    glassy = M('MAXIMUM', glassy, pmask)
    photo_wall = N.new('ShaderNodeVectorMath'); photo_wall.operation = 'SCALE'; Lk.new(pcol, photo_wall.inputs[0]); photo_wall.inputs['Scale'].default_value = 0.55
    wallsrc = cmix(M('GREATER_THAN', photo, 0.5), wallv.outputs['Vector'], photo_wall.outputs['Vector'])
    base = cmix(glassy, wallsrc, rgb((0.010, 0.013, 0.017)))
    base = cmix(M('MULTIPLY', win, frame), base, rgb((0.045, 0.047, 0.050)))      # 窗框：深灰金属
    rough = N.new('ShaderNodeMapRange'); Lk.new(glassy, rough.inputs['Value']); rough.inputs['To Min'].default_value = 0.85; rough.inputs['To Max'].default_value = 0.06
    bsdf = N.new('ShaderNodeBsdfPrincipled')
    Lk.new(base, bsdf.inputs['Base Color']); Lk.new(rough.outputs['Result'], bsdf.inputs['Roughness'])
    try:
        Lk.new(M('ADD', 0.5, M('MULTIPLY', glassy, 0.4)), bsdf.inputs['Specular IOR Level'])
    except KeyError:
        pass
    Lk.new(col, bsdf.inputs['Emission Color']); Lk.new(M('MULTIPLY', k, 0.075), bsdf.inputs['Emission Strength'])
    # 雾霾
    camd = N.new('ShaderNodeCameraData')
    fog = M('SUBTRACT', 1.0, M('EXPONENT', M('DIVIDE', camd.outputs['View Distance'], -HAZE_L)))
    em = N.new('ShaderNodeEmission'); em.inputs['Color'].default_value = HAZE + (1,); em.inputs['Strength'].default_value = 1.0
    mx = N.new('ShaderNodeMixShader')
    Lk.new(fog, mx.inputs['Fac']); Lk.new(bsdf.outputs['BSDF'], mx.inputs[1]); Lk.new(em.outputs['Emission'], mx.inputs[2])
    Lk.new(mx.outputs['Shader'], out.inputs['Surface'])
    return m


def haze_wrap(mat):
    """给别的材质（塔、树、地面）也套上同样的雾霾。"""
    nt = mat.node_tree; N = nt.nodes; Lk = nt.links
    out = [n for n in N if n.type == 'OUTPUT_MATERIAL'][0]
    src = out.inputs['Surface'].links[0].from_socket
    camd = N.new('ShaderNodeCameraData')
    d = N.new('ShaderNodeMath'); d.operation = 'DIVIDE'; d.inputs[1].default_value = -HAZE_L; Lk.new(camd.outputs['View Distance'], d.inputs[0])
    e = N.new('ShaderNodeMath'); e.operation = 'EXPONENT'; Lk.new(d.outputs[0], e.inputs[0])
    f = N.new('ShaderNodeMath'); f.operation = 'SUBTRACT'; f.inputs[0].default_value = 1.0; Lk.new(e.outputs[0], f.inputs[1])
    em = N.new('ShaderNodeEmission'); em.inputs['Color'].default_value = HAZE + (1,)
    mix = N.new('ShaderNodeMixShader')
    Lk.new(f.outputs[0], mix.inputs['Fac']); Lk.new(src, mix.inputs[1]); Lk.new(em.outputs['Emission'], mix.inputs[2])
    Lk.new(mix.outputs['Shader'], out.inputs['Surface'])


def emissive(name, color, strength):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (0.02, 0.02, 0.02, 1)
    b.inputs['Emission Color'].default_value = tuple(color) + (1,); b.inputs['Emission Strength'].default_value = strength
    return m


def in_view_cone(x, y, half_deg):
    az = math.degrees(math.atan2(x - T.TOWER_X, y))
    return abs(az) < half_deg


def green_areas():
    """真实的绿地和寺院（场景坐标的圆：x, y, 半径），里面不放楼：塔的院子和もみじ谷、增上寺、芝公园南边（王子公园塔周围）。"""
    import t_geo as G
    gx, gy = G.scene(*G.ZOJOJI_DAIDEN); hx, hy = G.scene(*G.ZOJOJI_GATE)
    px, py = G.scene(35.65519, 139.74701)
    return [(0.0, T.TOWER_D * 0.72, 150.0), (gx, gy, 120.0), (hx, hy, 35.0), (px, py, 105.0), (-40.0, -150.0, 110.0)]


def plan(occupied, seed=5):
    """填充的楼（真实的地标在 t_landmarks 里）：(x, y, 宽, 深, 高, 转角, 种类)。
    种类：office（玻璃幕墙）、mid（普通办公/商住）、res（住宅，有阳台）。
    东京这一带是中低层为主，高楼都是有名有姓的（t_landmarks）；远处（>1.5 km）才随机放一些高楼补天际线。"""
    import t_geo as G
    rng = np.random.default_rng(seed)
    B = []
    greens = green_areas()
    def free(x, y, r):
        if math.hypot(x, y) < T.OUTER_R + 25 + r: return False          # （足球场：OUTER_R 以内是 Unity 里的地面和三维的楼）
        for (gx, gy, gr) in greens:
            if math.hypot(x - gx, y - gy) < gr + r * 0.5: return False
        for (ox, oy, orad) in occupied:
            if math.hypot(x - ox, y - oy) < orad + r: return False
        return True
    R = 5200.0
    step = 86.0
    xs = np.arange(-R, R, step)
    for gx in xs:
        for gy in xs:
            bx, by = gx + rng.uniform(-8, 8), gy + rng.uniform(-8, 8)
            dist = math.hypot(bx, by)
            if dist > R: continue
            far = dist > 1500
            if far and rng.random() < 0.6: continue           # 远处只留一部分（矮的从地面看不见）
            blk = step - rng.uniform(12, 20)                  # 路宽
            n = 1 if far else int(rng.integers(2, 4))
            sub = blk / n
            rot = math.radians(rng.normal(0, 7))
            for i in range(n):
                for j in range(n):
                    if rng.random() < (0.10 if not far else 0.35): continue
                    w = sub * rng.uniform(0.62, 0.92); d = sub * rng.uniform(0.62, 0.92)
                    x = bx - blk / 2 + (i + 0.5) * sub + rng.uniform(-3, 3)
                    y = by - blk / 2 + (j + 0.5) * sub + rng.uniform(-3, 3)
                    if not free(x, y, max(w, d) / 2): continue
                    if G.covered(x, y): continue                  # PLATEAU 方格里的楼全是真的
                    if dist < 700:
                        h = min(rng.lognormal(math.log(17), 0.45), 48.0)
                    elif not far:
                        h = rng.lognormal(math.log(24), 0.5)
                        h = rng.uniform(100, 150) if rng.random() < 0.04 else min(h, 85.0)
                    else:
                        h = rng.lognormal(math.log(45), 0.45)
                        if rng.random() < 0.10: h = rng.uniform(120, 200)
                        if h < 40: continue
                    if h > 90: w, d = max(w, 34.0), max(d, 30.0)
                    h = max(h, 7.0)
                    kind = 'office' if h > 70 else ('res' if rng.random() < 0.4 else 'mid')
                    B.append((x, y, w, d, h, rot, kind))
    return B


def build(coll, seed=5):
    rng = np.random.default_rng(seed + 100)
    bl = Buildings()
    import t_landmarks, t_plateau_build
    occupied = t_plateau_build.build(bl)                   # PLATEAU 的真实建筑（下载了的那几个方格）
    import t_facade3d
    t_facade3d.build(coll, t_plateau_build.DETAILED)       # 近处的楼：立体的窗、阳台、挑檐、楼顶设备
    occupied += t_landmarks.build(coll, bl)                # 方格以外的有名高楼（维基数据的坐标高度 + 估的外形）
    plan_ = plan(occupied, seed)
    for (x, y, w, d, h, rot, kind) in plan_:
        sd = float(rng.integers(0, 100000))
        if kind == 'office':
            a = dict(seed=sd, fh=4.1, ww=1.6, mu=0.05, mv0=0.10, mv1=0.93, grp=float(rng.integers(4, 9)), warm=0.12,
                     litp=float(rng.uniform(0.18, 0.5)), glass=1.0, bright=float(rng.uniform(0.7, 1.2)))
            g = rng.uniform(0.025, 0.05); a.update(wr=g, wg=g * 1.05, wb=g * 1.15)
        elif kind == 'mid':
            a = dict(seed=sd, fh=float(rng.uniform(3.4, 3.9)), ww=float(rng.uniform(2.2, 3.2)), mu=0.14, mv0=0.28, mv1=0.86, grp=float(rng.integers(1, 4)),
                     warm=float(rng.uniform(0.15, 0.5)), litp=float(rng.uniform(0.25, 0.55)), glass=0.0, bright=float(rng.uniform(0.6, 1.1)))
            c = rng.uniform(0.06, 0.18); t = rng.uniform(-0.015, 0.015); a.update(wr=c + t, wg=c, wb=c - t)
        else:
            a = dict(seed=sd, fh=2.95, ww=float(rng.uniform(3.2, 4.2)), mu=0.10, mv0=0.32, mv1=0.80, grp=1.0, warm=0.80,
                     litp=float(rng.uniform(0.35, 0.6)), glass=0.0, bright=float(rng.uniform(0.5, 0.9)))
            c = rng.uniform(0.10, 0.24); a.update(wr=c, wg=c * 0.98, wb=c * 0.94)
        # 高楼：一两级退台
        if h > 90 and rng.random() < 0.6:
            h1 = h * rng.uniform(0.55, 0.75)
            bl.box(x, y, w, d, 0.0, h1, rot, a)
            bl.box(x, y, w * 0.78, d * 0.78, h1, h, rot, a)
            top_w, top_d = w * 0.78, d * 0.78
        else:
            bl.box(x, y, w, d, 0.0, h, rot, a)
            top_w, top_d = w, d
        # 楼顶设备
        if rng.random() < 0.7:
            mw, md = top_w * rng.uniform(0.25, 0.5), top_d * rng.uniform(0.25, 0.5)
            r = dict(a); r.update(litp=0.0, glass=0.0, wr=0.12, wg=0.12, wb=0.12, mv0=2.0, mv1=1.0)
            bl.box(x + rng.uniform(-3, 3), y + rng.uniform(-3, 3), mw, md, h, h + rng.uniform(2.5, 5.0), rot, r)
        if h > 60:
            c, s = math.cos(rot), math.sin(rot)
            for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                px, py = sx * top_w / 2 * 0.95, sy * top_d / 2 * 0.95
                bl.red.append((x + px * c - py * s, y + px * s + py * c, h + 0.8))
        # 竖招牌：近处的中低层楼，朝公园那面
        dist = math.hypot(x, y)
        if kind != 'office' and dist < 1100 and 14 < h < 60 and rng.random() < 0.22:
            nx, ny = -x / dist, -y / dist                       # 朝向公园
            ch = rng.choice([(1.0, 0.92, 0.85), (1.0, 0.25, 0.18), (0.35, 0.65, 1.0), (1.0, 0.75, 0.25), (0.4, 1.0, 0.55), (1.0, 0.4, 0.75)])
            sh = min(h - 4, rng.uniform(7, 14))
            bl.signs.append(((x + nx * (min(w, d) / 2 + 0.6), y + ny * (min(w, d) / 2 + 0.6), h - 2 - sh / 2), (nx, ny), rng.uniform(1.4, 2.4), sh, ch))
    build.occupied = occupied
    mat = facade_material()
    ob = bl.to_object('City', mat, coll)
    # 航空障碍灯、招牌：只给镜头看的发光体
    red = emissive('city_red', (1.0, 0.04, 0.02), 900.0)
    me = bpy.data.meshes.new('red'); vs, fs = [], []
    for (x, y, z) in bl.red:
        b = len(vs); r = 0.45
        vs += [(x - r, y - r, z - r), (x + r, y - r, z - r), (x + r, y + r, z - r), (x - r, y + r, z - r), (x - r, y - r, z + r), (x + r, y - r, z + r), (x + r, y + r, z + r), (x - r, y + r, z + r)]
        fs += [(b, b + 1, b + 5, b + 4), (b + 1, b + 2, b + 6, b + 5), (b + 2, b + 3, b + 7, b + 6), (b + 3, b, b + 4, b + 7), (b + 4, b + 5, b + 6, b + 7)]
    me.from_pydata(vs, [], fs); me.materials.append(red)
    ro = bpy.data.objects.new('AviationLights', me); coll.objects.link(ro)
    ro.visible_diffuse = False; ro.visible_glossy = False; ro.visible_shadow = False
    # 招牌：每块一个材质颜色
    sm = {}
    for (c, n, w, h, col) in bl.signs:
        key = tuple(col)
        if key not in sm:
            sm[key] = ([], [], emissive('sign_%d' % len(sm), col, 0.9))
        vs, fs, _ = sm[key]
        tx, ty = -n[1], n[0]
        b = len(vs)
        vs += [(c[0] - tx * w / 2, c[1] - ty * w / 2, c[2] - h / 2), (c[0] + tx * w / 2, c[1] + ty * w / 2, c[2] - h / 2),
               (c[0] + tx * w / 2, c[1] + ty * w / 2, c[2] + h / 2), (c[0] - tx * w / 2, c[1] - ty * w / 2, c[2] + h / 2)]
        fs.append((b, b + 1, b + 2, b + 3))
    for i, (vs, fs, m) in enumerate(sm.values()):
        me = bpy.data.meshes.new('signs%d' % i); me.from_pydata(vs, [], fs); me.materials.append(m)
        so = bpy.data.objects.new('Signs%d' % i, me); coll.objects.link(so)
    print('CITY_DONE buildings %d faces %d red lights %d signs %d' % (len(plan_), len(bl.f), len(bl.red), len(bl.signs)))
    return ob


def ground(coll):
    """公园以外的地面：沥青和人行道，路边一排路灯（发光的灯头 + 点光源）。"""
    m = bpy.data.materials.new('city_ground'); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (0.035, 0.035, 0.037, 1); b.inputs['Roughness'].default_value = 0.9
    haze_wrap(m)
    P, S = T.PARK, 9000.0
    # 足球场：中间挖掉一个半径 OUTER_R 的圆（那里的地面是 Unity 里的俯拍贴图 t_outer.py），外面一圈到 9 km
    me = bpy.data.meshes.new('cityground')
    n = 96; vs = []; fs = []
    for i in range(n):
        a = 2 * math.pi * i / n
        vs.append((T.OUTER_R * math.sin(a), T.OUTER_R * math.cos(a), 0)); vs.append((S * math.sin(a), S * math.cos(a), 0))
    for i in range(n):
        j = (i + 1) % n; fs.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1))
    me.from_pydata(vs, [], fs); me.materials.append(m)
    g = bpy.data.objects.new('CityGround', me); coll.objects.link(g)
    # 2026-11-24 第二版：按地图上描的真实大路（roads.json：桜田通り、祝田通り、日比谷通り、东京塔通り、环状三号、首都高）
    # 摆路灯（LED 白，照亮路面和楼的底部）、两个方向的车（车头两盏白灯、车尾两盏红灯，车身挡住背面）、高架上的钠灯
    import t_geo as G, json, os
    R = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'roads.json')))
    rng = np.random.default_rng(17)
    lamps = []; cars = []; hw_lamps = []
    def walk(pts, step):
        for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
            L = math.hypot(x1 - x0, y1 - y0)
            if L < 1: continue
            tx, ty = (x1 - x0) / L, (y1 - y0) / L
            for k in range(int(L // step) + 1):
                t = k * step + rng.uniform(-2, 2)
                if t > L: break
                yield x0 + tx * t, y0 + ty * t, tx, ty
    def in_park(x, y, m=4.0):
        return math.hypot(x, y) < T.OUTER_R + m          # 足球场：OUTER_R 以内的路灯在 Unity 里（t_layout.STREET_LAMPS）
    for name, road in R.items():
        pts = [G.scene(*ll) for ll in road]
        if name == '_shuto':
            for (x, y, tx, ty) in walk(pts, 34.0):
                if math.hypot(x, y) > 2500: continue
                for side in (-8.5, 8.5):
                    hw_lamps.append((x - ty * side, y + tx * side, 12.5 + 7.5))
                for lane in (-5.5, -2.0, 2.0, 5.5):
                    if rng.random() < 0.45: cars.append((x - ty * lane + tx * rng.uniform(-12, 12), y + tx * lane + ty * rng.uniform(-12, 12), tx * (1 if lane > 0 else -1), ty * (1 if lane > 0 else -1), 12.6))
            continue
        half = 11.0 if name in ('sakurada', 'iwaida', 'hibiya', 'kanjo3') else 7.0
        for (x, y, tx, ty) in walk(pts, 30.0):
            if math.hypot(x, y) > 2000: continue
            for side in (-half, half):
                lx, ly = x - ty * side, y + tx * side
                if not in_park(lx, ly): lamps.append((lx, ly))
        lanes = (-7.5, -4.0, 4.0, 7.5) if half > 10 else (-2.0, 2.0)
        for (x, y, tx, ty) in walk(pts, 9.0):
            if math.hypot(x, y) > 1500: continue
            for lane in lanes:
                if rng.random() > 0.22: continue
                cx, cy = x - ty * lane, y + tx * lane
                if in_park(cx, cy, 6.0): continue
                d = 1 if lane > 0 else -1
                cars.append((cx, cy, tx * d, ty * d, 0.0))
    # 车：深色车身（4.4 x 1.8 x 1.45）+ 前面两盏白灯、后面两盏红灯（只在车头/车尾那一面，从背面看被车身挡住）
    body = bpy.data.materials.new('car_body'); body.use_nodes = True
    bb = body.node_tree.nodes['Principled BSDF']; bb.inputs['Base Color'].default_value = (0.03, 0.03, 0.035, 1); bb.inputs['Roughness'].default_value = 0.25
    head = emissive('car_head', (1.0, 0.95, 0.85), 60.0); tail = emissive('car_tail', (1.0, 0.03, 0.02), 25.0)
    bv, bf, hv, hf, tv, tf = [], [], [], [], [], []
    def boxv(V, Fs, cx, cy, tx, ty, z0, a0, a1, b0, b1, c0, c1):
        nx, ny = -ty, tx; base = len(V)
        for (a, b, c) in ((a0, b0, c0), (a1, b0, c0), (a1, b1, c0), (a0, b1, c0), (a0, b0, c1), (a1, b0, c1), (a1, b1, c1), (a0, b1, c1)):
            V.append((cx + tx * a + nx * b, cy + ty * a + ny * b, z0 + c))
        Fs += [(base, base + 1, base + 5, base + 4), (base + 1, base + 2, base + 6, base + 5), (base + 2, base + 3, base + 7, base + 6), (base + 3, base, base + 4, base + 7), (base + 4, base + 5, base + 6, base + 7), (base, base + 3, base + 2, base + 1)]
    def quadv(V, Fs, cx, cy, tx, ty, z0, a, b0, b1, c0, c1):
        nx, ny = -ty, tx; base = len(V)
        V += [(cx + tx * a + nx * b0, cy + ty * a + ny * b0, z0 + c0), (cx + tx * a + nx * b1, cy + ty * a + ny * b1, z0 + c0),
              (cx + tx * a + nx * b1, cy + ty * a + ny * b1, z0 + c1), (cx + tx * a + nx * b0, cy + ty * a + ny * b0, z0 + c1)]
        Fs.append((base, base + 1, base + 2, base + 3))
    for (cx, cy, tx, ty, z0) in cars:
        boxv(bv, bf, cx, cy, tx, ty, z0, -2.2, 2.2, -0.9, 0.9, 0.25, 1.45)
        for b in (-0.65, 0.48):
            quadv(hv, hf, cx, cy, tx, ty, z0, 2.215, b, b + 0.17, 0.62, 0.74)
            quadv(tv, tf, cx, cy, tx, ty, z0, -2.215, b, b + 0.17, 0.80, 0.92)
    for nm, V, Fs, mt in (('Cars', bv, bf, body), ('CarHeadlights', hv, hf, head), ('CarTaillights', tv, tf, tail)):
        me = bpy.data.meshes.new(nm); me.from_pydata(V, [], Fs); me.materials.append(mt)
        ob_ = bpy.data.objects.new(nm, me); coll.objects.link(ob_)
        if nm != 'Cars':
            ob_.visible_diffuse = False; ob_.visible_shadow = False       # 车灯本身不照东西（路灯和店铺的光已经够）
    haze_wrap(body)
    # 首都高：高架桥面（宽 17 m、厚 2 m、离地 12.5 m）+ 桥墩，两侧钠灯
    pts = [G.scene(*ll) for ll in R['_shuto']]
    deck = bpy.data.materials.new('shuto'); deck.use_nodes = True
    deck.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.13, 0.13, 0.13, 1)
    haze_wrap(deck)
    dv, df = [], []
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0); tx, ty = (x1 - x0) / L, (y1 - y0) / L
        boxv(dv, df, (x0 + x1) / 2, (y0 + y1) / 2, tx, ty, 10.5, -L / 2 - 1, L / 2 + 1, -8.5, 8.5, 0.0, 2.0)
        boxv(dv, df, (x0 + x1) / 2, (y0 + y1) / 2, tx, ty, 12.5, -L / 2 - 1, L / 2 + 1, -8.6, -8.3, 0.0, 1.1)
        boxv(dv, df, (x0 + x1) / 2, (y0 + y1) / 2, tx, ty, 12.5, -L / 2 - 1, L / 2 + 1, 8.3, 8.6, 0.0, 1.1)
        for k in range(int(L // 30) + 1):
            px_, py_ = x0 + tx * k * 30, y0 + ty * k * 30
            boxv(dv, df, px_, py_, tx, ty, 0.0, -1.2, 1.2, -3.0, 3.0, 0.0, 10.5)
    me = bpy.data.meshes.new('Shuto'); me.from_pydata(dv, [], df); me.materials.append(deck)
    sh = bpy.data.objects.new('Shuto', me); coll.objects.link(sh)
    sodium = emissive('sodium', (1.0, 0.62, 0.25), 40.0)
    me = bpy.data.meshes.new('hwlamps'); vs, fs = [], []
    for (x, y, z) in hw_lamps:
        b = len(vs); r = 0.2
        vs += [(x - r, y - r, z - r), (x + r, y - r, z - r), (x + r, y + r, z - r), (x - r, y + r, z - r), (x - r, y - r, z + r), (x + r, y - r, z + r), (x + r, y + r, z + r), (x - r, y + r, z + r)]
        fs += [(b, b + 1, b + 5, b + 4), (b + 1, b + 2, b + 6, b + 5), (b + 2, b + 3, b + 7, b + 6), (b + 3, b, b + 4, b + 7), (b, b + 3, b + 2, b + 1)]
        ld = bpy.data.lights.new('hw', 'POINT'); ld.energy = 500.0; ld.color = (1.0, 0.62, 0.28); ld.shadow_soft_size = 0.3
        lo = bpy.data.objects.new('hw', ld); lo.location = (x, y, z - 0.4); coll.objects.link(lo)
    me.from_pydata(vs, [], fs); me.materials.append(sodium)
    ho_ = bpy.data.objects.new('HwLamps', me); coll.objects.link(ho_)
    ho_.visible_diffuse = False; ho_.visible_glossy = False; ho_.visible_shadow = False
    print('CARS %d, highway lamps %d' % (len(cars), len(hw_lamps)))
    head = emissive('streetlamp', (0.95, 0.97, 1.0), 14.0)
    me = bpy.data.meshes.new('lampheads'); vs, fs = [], []
    pole = bpy.data.materials.new('pole'); pole.use_nodes = True
    pole.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.08, 0.08, 0.085, 1)
    pv, pf = [], []
    for (x, y) in lamps:
        z = 8.0; r = 0.18; bidx = len(vs)
        vs += [(x - r, y - r, z - 0.08), (x + r, y - r, z - 0.08), (x + r, y + r, z - 0.08), (x - r, y + r, z - 0.08), (x - r, y - r, z + 0.08), (x + r, y - r, z + 0.08), (x + r, y + r, z + 0.08), (x - r, y + r, z + 0.08)]
        fs += [(bidx, bidx + 1, bidx + 5, bidx + 4), (bidx + 1, bidx + 2, bidx + 6, bidx + 5), (bidx + 2, bidx + 3, bidx + 7, bidx + 6), (bidx + 3, bidx, bidx + 4, bidx + 7), (bidx, bidx + 3, bidx + 2, bidx + 1)]
        boxv(pv, pf, x, y, 1.0, 0.0, 0.0, -0.07, 0.07, -0.07, 0.07, 0.0, 8.0)
        ld = bpy.data.lights.new('sl', 'SPOT'); ld.energy = 900.0; ld.color = (0.95, 0.97, 1.0); ld.shadow_soft_size = 0.25
        ld.spot_size = math.radians(150); ld.spot_blend = 0.5
        lo = bpy.data.objects.new('sl', ld); lo.location = (x, y, z - 0.15); coll.objects.link(lo)     # 默认朝下照
    me2 = bpy.data.meshes.new('poles'); me2.from_pydata(pv, [], pf); me2.materials.append(pole)
    coll.objects.link(bpy.data.objects.new('Poles', me2))
    me.from_pydata(vs, [], fs); me.materials.append(head)
    ho = bpy.data.objects.new('LampHeads', me); coll.objects.link(ho)
    ho.visible_diffuse = False; ho.visible_glossy = False; ho.visible_shadow = False
    print('GROUND_DONE street lamps %d' % len(lamps))


def trees(coll, seed=9):
    """公园外面的真实绿地（扫描树，链接复制）：公园外一圈、もみじ谷（公园和塔之间）、塔的院子、增上寺、芝公园南边。
    塔的方向上的树不能太高：只挡住塔脚 40 m 以下（从远处看真实照片也是这样）。"""
    import u_world as UW
    import t_geo as G
    rng = np.random.default_rng(seed)
    occupied = getattr(build, 'occupied', [])
    pts = []
    def ok(x, y, h):
        if math.hypot(x, y) < T.OUTER_R + 3: return False             # 足球场：OUTER_R 以内的树是 Unity 里的公告板
        for (ox, oy, orad) in occupied:
            if math.hypot(x - ox, y - oy) < orad * 0.85: return False
        d = math.hypot(x, y)
        if in_view_cone(x, y, 9.0) and y < T.TOWER_D - 30:
            return h < 1.2 + d * math.tan(math.atan(40.0 / T.TOWER_D))
        return True
    def scatter(n, fn, hlo, hhi):
        for k in range(n):
            x, y = fn()
            h = rng.uniform(hlo, hhi)
            if G.covered(x, y, 'veg'): continue            # 这一块有 PLATEAU 的真实树
            if ok(x, y, h): pts.append((x, y, h))
    P = T.PARK
    def ring():
        while True:
            a = rng.uniform(0, 2 * math.pi); r = rng.uniform(T.OUTER_R + 4, T.OUTER_R + 80)
            x, y = r * math.sin(a), r * math.cos(a)
            if not in_view_cone(x, y, T.VISTA): return x, y
    scatter(420, ring, 13, 23)
    scatter(260, lambda: (rng.uniform(-120, 120), rng.uniform(P + 20, T.TOWER_D - 50)), 12, 22)          # もみじ谷
    def around(cx, cy, r0, r1):
        def f():
            a = rng.uniform(0, 2 * math.pi); r = rng.uniform(r0, r1)
            return cx + r * math.cos(a), cy + r * math.sin(a)
        return f
    scatter(180, around(0.0, T.TOWER_D, 45, 150), 12, 24)                                    # 塔的院子
    gx, gy = G.scene(*G.ZOJOJI_DAIDEN)
    scatter(160, around(gx, gy, 32, 125), 12, 26)                                           # 增上寺
    px, py = G.scene(35.65519, 139.74701)
    scatter(140, around(px, py, 40, 110), 12, 24)                                            # 芝公园南边
    import pickle, os
    real = pickle.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out', 'plateau_trees.pkl'), 'rb'))
    nreal = 0
    for (x, y, h) in real:
        if math.hypot(x, y) < T.OUTER_R + 3: continue
        if math.hypot(x, y) > 2500: continue
        pts.append((x, y, min(max(h, 4.0), 30.0))); nreal += 1
    print('real trees (PLATEAU) %d' % nreal)
    names = list(U_SPECIES.values())
    for (x, y, h) in pts:
        UW.place_asset(names[int(rng.integers(0, 2))], x, y, h, rot_z=float(rng.uniform(0, 6.28)), squash=float(rng.uniform(1.0, 1.25)))
    seen = set()
    for o in bpy.data.objects:
        for slot in o.material_slots:
            m = slot.material
            if m is not None and m.name not in seen and m.use_nodes and o.type == 'MESH' and not m.name.startswith(('city', 'tt_', 'sign', 'street', 'far', 'zj_', 'crown')):
                seen.add(m.name)
                try:
                    haze_wrap(m)
                except Exception as e:
                    print('haze skip', m.name, e)
    print('TREES_DONE %d' % len(pts))
