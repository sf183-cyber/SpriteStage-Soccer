# -*- coding: utf-8 -*-
"""全景带冲洗成 Unity 的贴图：out/<前缀>i.npy -> Assets/Resources/World/tokyo/band_i.png（RGBA，非预乘），
外加一张铺在夜空上的预览 out/<前缀>_preview.jpg。曝光和白宫场景同一个（compose_state.json）。

    python3 t_compose.py [前缀] [unity]      (带 unity 才写进工程)"""
import os, sys, glob, json
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import t_layout as T
from t_dev import develop, to_u8
Image.MAX_IMAGE_PIXELS = None

BAND_PRINT = 1.0             # 全景带相对草坪的曝光


def tile(path):
    a = np.load(path).astype(np.float32)
    al = np.clip(a[:, :, 3:4], 0, 1)
    rgb = a[:, :, :3] / np.maximum(al, 1e-4)                      # 去掉预乘再冲洗（冲洗是非线性的）
    img = develop(rgb, BAND_PRINT, bloom=0.12)
    img = np.where(al > 1e-4, img, 0)
    return img, al[:, :, 0]


def sky_strip(h, w):
    """预览用：和 Unity 天空差不多的夜空（越往上越暗）"""
    el = np.linspace(T.BAND_HI, T.BAND_LO, h)[:, None]
    t = np.clip(el / 25.0, 0, 1)
    top = np.array([0.03, 0.04, 0.08]); hor = np.array([0.20, 0.15, 0.13])
    c = hor * (1 - t[:, :, None]) ** 2 + top * (1 - (1 - t[:, :, None]) ** 2)
    return np.repeat(c, w, 1)


def main(prefix='band', unity=False):
    files = sorted(glob.glob(os.path.join(T.OUT, prefix + '[0-9].npy')))
    previews = []
    for f in files:
        i = int(f[-5])
        img, al = tile(f)
        if unity:
            os.makedirs(T.UNITY_WORLD, exist_ok=True)
            Image.fromarray(np.dstack([to_u8(img), to_u8(al)]), 'RGBA').save(os.path.join(T.UNITY_WORLD, 'band_%d.png' % i), optimize=False, compress_level=6)
        sky = sky_strip(*img.shape[:2])
        comp = img * al[:, :, None] + sky * (1 - al[:, :, None])
        previews.append((i, comp))
        print('tile %d %s alpha %.3f' % (i, img.shape, float(al.mean())))
    if previews:
        previews.sort()
        strip = np.concatenate([p for _, p in previews], 1)
        Image.fromarray(to_u8(strip)).save(os.path.join(T.OUT, prefix + '_preview.jpg'), quality=92)
    print('COMPOSE_OK', prefix, len(files))




def far(tag='far', unity=False):
    """远景草坪：和白宫场景一样，在近景草坪的边上取一条草地，把远景的线性亮度对齐上去（两边的灯一样，只是草的材质不同）。"""
    import math, json
    U = T.U
    a = np.load(os.path.join(T.OUT, tag + '.npy')).astype(np.float32)
    N = a.shape[0]; size = 2 * T.FAR_HALF; ppm = N / size
    def px(x, y):
        return int((x - (T.FAR_CX - size / 2)) * ppm), int((T.FAR_CY + size / 2 - y) * ppm)
    nb = np.load(os.path.join(U.OUT, 'near.npy'), mmap_mode='r')
    nH, nW = nb.shape[:2]
    nppm = nW / (U.NEAR['x1'] - U.NEAR['x0'])
    sin40 = math.sin(math.radians(40.0))
    def near_px(x, y):
        return int((x - U.NEAR['x0']) * nppm), int(nH / 2 - y * sin40 * nppm)
    def strip(img, to_px):
        vals = []
        for (xa, xb, ya, yb) in ((U.NEAR['x0'] + 1.5, U.NEAR['x1'] - 1.5, U.NEAR['y1'] - 3.0, U.NEAR['y1'] - 1.5), (U.NEAR['x0'] + 1.5, U.NEAR['x1'] - 1.5, U.NEAR['y0'] + 1.5, U.NEAR['y0'] + 3.0)):
            (px0, py0), (px1, py1) = to_px(xa, yb), to_px(xb, ya)
            vals.append(np.asarray(img[py0:py1:2, px0:px1:2, :3], np.float32).reshape(-1, 3))
        return np.concatenate(vals).mean(0)
    n_lin, f_lin = strip(nb, near_px), strip(a, px)
    gain = np.clip(n_lin / np.maximum(f_lin, 1e-6), 0.2, 5.0).astype(np.float32)
    print('far: near strip %s far strip %s -> gain %s' % (np.round(n_lin, 5), np.round(f_lin, 5), np.round(gain, 3)))
    json.dump([float(v) for v in gain], open(os.path.join(T.OUT, 'far_gain.json'), 'w'))      # 公园外的地面（outer）也用它
    img = to_u8(develop(a[:, :, :3] * gain[None, None, :], 1.0, bloom=0.0, sat=1.04))
    im = Image.fromarray(img)
    if unity:
        os.makedirs(T.UNITY_WORLD, exist_ok=True)
        im.save(os.path.join(T.UNITY_WORLD, 'lawn_far.jpg'), quality=92, subsampling=0)
    im.resize((1024, 1024), Image.LANCZOS).save(os.path.join(T.OUT, tag + '_preview.jpg'), quality=88)
    print('FAR_OK', tag)


def near(unity=False):
    """近景草坪（n_lawn_near.py 的 out/near.npy）-> World/tokyo/lawn_near.jpg：和篮球的夜景草坪一样，用白宫场景的曝光"""
    import math
    from PIL import Image
    U = T.U
    a = np.load(os.path.join(T.OUT, 'near.npy'), mmap_mode='r')
    H, Wd = a.shape[:2]
    ppm = Wd / (U.NEAR['x1'] - U.NEAR['x0'])
    half = (U.NEAR['y1'] - U.NEAR['y0']) / 2 * math.sin(math.radians(40.0)) * ppm
    r0, r1 = int(round(H / 2 - half)), int(round(H / 2 + half))
    img = np.empty((r1 - r0, Wd, 3), np.uint8)
    for y in range(r0, r1, 512):
        y1 = min(r1, y + 512)
        img[y - r0:y1 - r0] = to_u8(develop(np.asarray(a[y:y1, :, :3], np.float32), 1.0, bloom=0.0, sat=1.04))
    im = Image.fromarray(img).resize((8192, 4096), Image.LANCZOS)
    if unity:
        os.makedirs(T.UNITY_WORLD, exist_ok=True)
        im.save(os.path.join(T.UNITY_WORLD, 'lawn_near.jpg'), quality=94, subsampling=0)
    im.resize((2048, 1024), Image.LANCZOS).save(os.path.join(T.OUT, 'near_preview.jpg'), quality=90)
    print('NEAR_OK')


def outer(tag='outer', unity=False):
    """公园外的地面（t_outer.py）-> World/tokyo/ground_outer.jpg：草和远景草坪同一个材质，用远景草坪对齐出来的增益"""
    a = np.load(os.path.join(T.OUT, tag + '.npy')).astype(np.float32)
    gp = os.path.join(T.OUT, 'far_gain.json')
    gain = np.array(json.load(open(gp)), np.float32) if os.path.exists(gp) else np.ones(3, np.float32)
    img = to_u8(develop(a[:, :, :3] * gain[None, None, :], 1.0, bloom=0.0, sat=1.04))
    im = Image.fromarray(img)
    if unity:
        im.save(os.path.join(T.UNITY_WORLD, 'ground_outer.jpg'), quality=92, subsampling=0)
    im.resize((1024, 1024), Image.LANCZOS).save(os.path.join(T.OUT, tag + '_preview.jpg'), quality=88)
    print('OUTER_OK gain', gain)


def nearbake(unity=False):
    """近处的楼的光照图集（t_nearbake.py）-> World/tokyo/near.jpg"""
    a = np.load(os.path.join(T.OUT, 'nearbake.npy')).astype(np.float32)
    img = to_u8(develop(a[:, :, :3], 1.0, bloom=0.0, sat=1.0))
    im = Image.fromarray(img)
    if unity:
        im.save(os.path.join(T.UNITY_WORLD, 'near.jpg'), quality=93, subsampling=0)
    im.resize((1024, 1024), Image.LANCZOS).save(os.path.join(T.OUT, 'nearbake_preview.jpg'), quality=88)
    print('NEARBAKE_OK')


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1].startswith('far'):
        far(sys.argv[1], 'unity' in sys.argv)
    elif len(sys.argv) > 1 and sys.argv[1] == 'near':
        near('unity' in sys.argv)
    elif len(sys.argv) > 1 and sys.argv[1].startswith('outer'):
        outer(sys.argv[1], 'unity' in sys.argv)
    elif len(sys.argv) > 1 and sys.argv[1] == 'nearbake':
        nearbake('unity' in sys.argv)
    else:
        main(sys.argv[1] if len(sys.argv) > 1 else 'band', 'unity' in sys.argv)
