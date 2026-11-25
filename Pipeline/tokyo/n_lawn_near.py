# -*- coding: utf-8 -*-
"""东京塔夜场（2026-11-24）的近景草坪：和白宫场景同一块球场、同样的草叶（../u_lawn_near.py 拷来），光换成夜里的月亮和四根泛光灯杆，
树换成芝公园的 -> tokyo/out/near.npy。

    Blender -b --factory-startup -P n_lawn_near.py -- <每米像素> <采样> [名字] [x0 x1 y0 y1 只渲一角]

以下是原来的说明：
The pitch and the turf around it, blade by blade -> Pipeline/out/near.npy (scene-linear, oblique view).

    Blender -b --factory-startup -P u_lawn_near.py -- <px per metre> <samples> [tag] [x0 x1 y0 y1 test crop in metres]

Same recipe as the HD pack's White House lawn (7000 real blades per square metre, mown by leaning the
blades, lit by a photographed sky), but laid out in true world space: the pitch is a rectangle.  It is
rendered from the south at 40 degrees - the direction the game camera looks from by default - and
u_compose.py then lays that photograph flat on the ground."""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from n_world import *
import n_layout as L
import t_layout as T

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PPM = float(argv[0]) if len(argv) > 0 else 40.0
SAMPLES = int(argv[1]) if len(argv) > 1 else 48
TAG = argv[2] if len(argv) > 2 else 'near_test'
CROP = [float(v) for v in argv[3:7]] if len(argv) >= 7 else None
DENSITY = float(os.environ.get('GRASS_DENSITY', '7000'))
ELEV = 40.0
SIN, COS = math.sin(math.radians(ELEV)), math.cos(math.radians(ELEV))
X0, X1, Y0, Y1 = (CROP if CROP else (L.NEAR['x0'], L.NEAR['x1'], L.NEAR['y0'], L.NEAR['y1']))
MARGIN = 0.25           # metres of extra picture above and below (blade tips)
W = int(round((X1 - X0) * PPM)); H = int(round(((Y1 - Y0) * SIN + 2 * MARGIN) * PPM))
TPPM = 80.0
M = {k: np.load(os.path.join(L.TEX, k + '.npy')) for k in ('lines', 'band', 'vband', 'wear', 'patch', 'fine')}
TH, TW = M['lines'].shape

sc = reset()
daylight(sc)


def grow_grass(xr, yr, seed):
    rng = np.random.default_rng(seed)
    n = int((xr[1] - xr[0]) * (yr[1] - yr[0]) * DENSITY)
    x = rng.uniform(xr[0], xr[1], n).astype(np.float32)
    y = rng.uniform(yr[0], yr[1], n).astype(np.float32)
    px = np.clip(((x - L.NEAR['x0']) * TPPM).astype(np.int32), 0, TW - 1)
    py = np.clip(((L.NEAR['y1'] - y) * TPPM).astype(np.int32), 0, TH - 1)
    g = lambda k: M[k][py, px].astype(np.float32) / 255.0
    line, band, vband, wear, patch, fine = g('lines'), g('band'), g('vband'), g('wear'), g('patch'), g('fine')
    keep = rng.random(n) > 0.5 * wear
    x, y, line, band, vband, wear, patch, fine = [a[keep] for a in (x, y, line, band, vband, wear, patch, fine)]
    n = x.size
    h = 0.052 * np.clip(1 + 0.30 * rng.standard_normal(n), 0.45, 1.9).astype(np.float32) * (1 - 0.45 * wear)
    w = rng.uniform(0.006, 0.011, n).astype(np.float32)
    az = rng.uniform(0, 2 * np.pi, n).astype(np.float32)
    sgn = np.where(band > 0.5, -1.0, 1.0).astype(np.float32)
    lean = np.clip(0.42 + 0.20 * rng.standard_normal(n), 0.06, 1.0).astype(np.float32)
    ang = (np.pi / 2) * sgn + 0.55 * rng.standard_normal(n).astype(np.float32) + np.where(vband > 0.5, 0.22, -0.22)
    dx, dy = np.cos(ang), np.sin(ang)
    co = np.empty((n, 3, 3), np.float32)
    co[:, 0, 0] = x - w / 2 * np.cos(az); co[:, 0, 1] = y - w / 2 * np.sin(az); co[:, 0, 2] = 0
    co[:, 1, 0] = x + w / 2 * np.cos(az); co[:, 1, 1] = y + w / 2 * np.sin(az); co[:, 1, 2] = 0
    co[:, 2, 0] = x + h * np.sin(lean) * dx; co[:, 2, 1] = y + h * np.sin(lean) * dy; co[:, 2, 2] = h * np.cos(lean)
    base = np.array([0.074, 0.142, 0.022], np.float32)
    dry = np.array([0.185, 0.185, 0.040], np.float32)
    t = np.clip(0.55 * fine + 0.45 * rng.random(n).astype(np.float32) - 0.45, 0, 1)[:, None] * 0.8
    colr = base[None, :] * (1 - t) + dry[None, :] * t
    colr *= (0.78 + 0.44 * patch)[:, None] * (0.85 + 0.30 * rng.random(n).astype(np.float32))[:, None]
    colr *= (1.0 - 0.02 * band)[:, None] * (1.01 - 0.02 * vband)[:, None]
    colr = colr * (1 - 0.5 * wear[:, None]) + np.array([0.20, 0.17, 0.07], np.float32)[None, :] * (0.5 * wear[:, None])
    painted = rng.random(n).astype(np.float32) < line * 0.96
    colr[painted] = np.array([0.80, 0.80, 0.76], np.float32) * (0.85 + 0.15 * rng.random(int(painted.sum())).astype(np.float32))[:, None]
    rgba = np.ones((n, 3, 4), np.float32)
    rgba[:, 0, :3] = colr * 0.42; rgba[:, 1, :3] = colr * 0.42; rgba[:, 2, :3] = colr * 1.12
    return co, rgba


def grass_material():
    m = bpy.data.materials.new('grass'); m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes['Principled BSDF']; out = nt.nodes['Material Output']
    at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'col'
    nt.links.new(at.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.38
    tr = nt.nodes.new('ShaderNodeBsdfTranslucent')
    gain = nt.nodes.new('ShaderNodeMixRGB'); gain.blend_type = 'MULTIPLY'; gain.inputs['Fac'].default_value = 1.0
    gain.inputs['Color2'].default_value = (1.5, 1.9, 0.7, 1)
    nt.links.new(at.outputs['Color'], gain.inputs['Color1'])
    nt.links.new(gain.outputs['Color'], tr.inputs['Color'])
    mix = nt.nodes.new('ShaderNodeMixShader'); mix.inputs['Fac'].default_value = 0.32
    nt.links.new(b.outputs['BSDF'], mix.inputs[1]); nt.links.new(tr.outputs['BSDF'], mix.inputs[2])
    nt.links.new(mix.outputs['Shader'], out.inputs['Surface'])
    return m


GRASS = grass_material()
gx = (X0 - 0.4, X1 + 0.4); gy = (Y0 - 0.6, Y1 + 0.6)
strips = max(1, int((gy[1] - gy[0]) / 2.0))
edges = np.linspace(gy[0], gy[1], strips + 1)
total = 0
for i in range(strips):
    co, rgba = grow_grass(gx, (edges[i], edges[i + 1]), 100 + i)
    tri_mesh(f'grass_{i}', co, rgba, GRASS)
    total += co.shape[0]
print('blades:', total)
soil = pbr('soil', (0.030, 0.034, 0.014), 0.9)
quad('soil', [(gx[0] - 30, gy[0] - 30, 0), (gx[1] + 30, gy[0] - 30, 0), (gx[1] + 30, gy[1] + 30, 0), (gx[0] - 30, gy[1] + 30, 0)], soil)
if os.environ.get('NO_TREES') != '1':
    for (sp, x, y, h, v) in T.TREES:              # 芝公园的树（Unity 里同一批），只投影子
        if math.hypot(x, y) <= 75.0:
            place_asset(L.SPECIES[sp], x, y, h, rot_z=L.VARIANT_YAW[v], squash=1.1, visible_camera=False)
goals(); corner_flags(); masts()

# oblique orthographic camera looking north: px = W/2 + (x - xc) * PPM, py = H/2 - ((y - yc) * SIN + z * COS) * PPM
xc, yc = (X0 + X1) / 2, (Y0 + Y1) / 2
cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = max(W, H) / PPM; cd.clip_start = 1; cd.clip_end = 3000
co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
D = 400.0
co_.location = (xc, yc - D * COS, D * SIN)
co_.rotation_euler = (math.radians(90 - ELEV), 0, 0)
setup_cycles(sc, SAMPLES, W, H, transparent=False, denoise=True)
print('render', W, H)
render_npy(sc, TAG)
