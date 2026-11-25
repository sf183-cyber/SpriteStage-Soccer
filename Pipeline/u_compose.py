# -*- coding: utf-8 -*-
"""Scene-linear renders -> the textures Unity loads (Assets/Resources/World).

    python3 u_compose.py [near] [far] [house] [trees] [sky]      (no argument = everything that has been rendered)

All of it is "developed" like one photograph: exposure set on the sunlit turf, filmic curve, sRGB.
The white building and the sky are printed darker than a single exposure would give - the same thing the
HD pack version does, otherwise both burn out."""
import os, sys, json, math
import numpy as np
from PIL import Image
import u_layout as L
sys.path.insert(0, L.HD); sys.path.insert(0, os.path.join(L.HD, 'whitehouse'))
from common import blur, to_u8
import post
Image.MAX_IMAGE_PIXELS = None

W = L.UNITY_WORLD
STATE = os.path.join(L.OUT, 'compose_state.json')
HOUSE_PRINT = 0.42          # exposure of the building relative to the turf
TREE_PRINT = 0.80


def state():
    return json.load(open(STATE)) if os.path.exists(STATE) else {}


def save_state(**kw):
    s = state(); s.update(kw); json.dump(s, open(STATE, 'w'), indent=1)


def develop(lin, exposure, sat=1.04):
    img = post.develop(lin, exposure, bloom=0.0, sat=sat)
    return np.clip(0.5 + (img - 0.5) * 1.05, 0, 1) ** 0.86


def near():
    a = np.load(os.path.join(L.OUT, 'near.npy'), mmap_mode='r')
    H, Wd = a.shape[:2]
    ppm = Wd / (L.NEAR['x1'] - L.NEAR['x0'])
    half = (L.NEAR['y1'] - L.NEAR['y0']) / 2 * math.sin(math.radians(40.0)) * ppm
    r0, r1 = int(round(H / 2 - half)), int(round(H / 2 + half))
    lin = np.asarray(a[r0:r1, :, :3], np.float32)
    g = lin[::4, ::4, 1]
    exposure = 0.31 / float(np.percentile(g, 65))
    # anchors for matching the far lawn: plain sunlit turf and plain shaded turf (linear)
    lum = lin[::8, ::8].mean(2)
    sun_px = lin[::8, ::8][(lum > np.percentile(lum, 55)) & (lum < np.percentile(lum, 80))].mean(0)
    shade_px = lin[::8, ::8][lum < np.percentile(lum, 4)].mean(0)
    save_state(exposure=exposure, near_sun=[float(v) for v in sun_px], near_shade=[float(v) for v in shade_px])
    print('near: exposure %.2f  sun %s shade %s' % (exposure, np.round(sun_px, 4), np.round(shade_px, 4)))
    TW, TH = 8192, 4096
    out = np.empty((TH, TW, 3), np.uint8)
    step = 512
    img = np.empty(lin.shape, np.uint8)
    for y in range(0, lin.shape[0], step):
        img[y:y + step] = to_u8(develop(lin[y:y + step], exposure))
    del lin
    im = Image.fromarray(img).resize((TW, TH), Image.LANCZOS)
    im.save(os.path.join(W, 'lawn_near.jpg'), quality=94, subsampling=0)
    im.resize((2048, 1024), Image.LANCZOS).save(os.path.join(L.OUT, 'lawn_near_preview.jpg'), quality=90)
    print('near ok')
    detail()


def detail():
    """Fine grain for the far lawn: a patch of plain sunlit turf (inside the pitch, clear of every line) with
    everything larger than a few blades removed.  Grey 0.5 = neutral; the far lawn is multiplied by twice this."""
    im = Image.open(os.path.join(W, 'lawn_near.jpg'))
    cx, cy = int((3.0 - L.NEAR['x0']) * 160), int((L.NEAR['y1'] + 5.5) * 160)
    assert cx + 640 <= im.size[0] and cy + 640 <= im.size[1]
    patch = np.asarray(im.crop((cx, cy, cx + 640, cy + 640)), np.float32) / 255.0
    lowf = blur(patch, 20.0)
    d = np.clip(0.5 * patch / np.maximum(lowf, 1e-3), 0, 1)
    n, f = 512, 64                                   # make it tile: cross-fade the 64 px borders
    t = d[:n + f, :n + f].copy()
    wgt = np.linspace(0, 1, f, dtype=np.float32)
    t[:f] = t[:f] * wgt[:, None, None] + t[n:n + f] * (1 - wgt[:, None, None])
    t[:, :f] = t[:, :f] * wgt[None, :, None] + t[:, n:n + f] * (1 - wgt[None, :, None])
    t = t[:n, :n]
    Image.fromarray(to_u8(t)).save(os.path.join(W, 'lawn_detail.png'))
    print('detail ok, mean %.3f' % float(t.mean()))


def far():
    s = state()
    a = np.load(os.path.join(L.OUT, 'far.npy'), mmap_mode='r')
    N = a.shape[0]
    ppm = N / L.FAR['size']
    def px(x, y):
        return int((x - (L.FAR['cx'] - L.FAR['size'] / 2)) * ppm), int(((L.FAR['cy'] + L.FAR['size'] / 2) - y) * ppm)
    # the same two anchors inside the near-lawn rectangle
    x0, y1 = px(L.NEAR['x0'], L.NEAR['y1']); x1, y0 = px(L.NEAR['x1'], L.NEAR['y0'])
    reg = np.asarray(a[y1:y0, x0:x1, :3], np.float32)
    lum = reg.mean(2)
    sun_px = reg[(lum > np.percentile(lum, 55)) & (lum < np.percentile(lum, 80))].mean(0)
    shade_px = reg[lum < np.percentile(lum, 4)].mean(0)
    ns, nh = np.array(s['near_sun']), np.array(s['near_shade'])
    p = np.log(ns / nh) / np.log(sun_px / shade_px)
    gain = ns / sun_px ** p
    print('far: sun %s shade %s -> power %s gain %s' % (np.round(sun_px, 4), np.round(shade_px, 4), np.round(p, 3), np.round(gain, 3)))
    out = np.empty((N, N, 3), np.uint8)
    step = 512
    for y in range(0, N, step):
        lin = np.asarray(a[y:y + step, :, :3], np.float32)
        lin = gain[None, None, :].astype(np.float32) * np.power(np.maximum(lin, 1e-6), p[None, None, :].astype(np.float32))
        out[y:y + step] = to_u8(develop(lin, s['exposure']))
    im = Image.fromarray(out)
    im.save(os.path.join(W, 'lawn_far.jpg'), quality=92, subsampling=0)
    edge = np.concatenate([out[:64].reshape(-1, 3), out[-64:].reshape(-1, 3)]).astype(np.float32)
    grass = [float(v) / 255.0 for v in np.median(edge, 0)]
    save_state(grass=grass)
    im.resize((1536, 1536), Image.LANCZOS).save(os.path.join(L.OUT, 'lawn_far_preview.jpg'), quality=88)
    print('far ok, grass', np.round(grass, 3))


def house():
    """The baked texture of the house, and the vertex colours of its fine detail (balusters, railings, glazing bars)."""
    import struct
    s = state()
    a = np.load(os.path.join(L.OUT, 'house_bake.npy')).astype(np.float32)
    img = develop(a, s['exposure'] * HOUSE_PRINT, sat=1.0)
    Image.fromarray(to_u8(img)).save(os.path.join(W, 'house.png'))
    print('house ok', a.shape)
    pp = os.path.join(L.OUT, 'house_detail_pos.npy')
    if os.path.exists(pp):
        P = np.load(pp).astype(np.float32)
        C = np.load(os.path.join(L.OUT, 'house_detail_col.npy')).astype(np.float32)
        rgb = develop(C[None, :, :], s['exposure'] * HOUSE_PRINT, sat=1.0)[0]
        rgba = np.concatenate([to_u8(rgb), np.full((rgb.shape[0], 1), 255, np.uint8)], 1)
        with open(os.path.join(W, 'house_detail.bytes'), 'wb') as f:
            f.write(b'G3V1'); f.write(struct.pack('<i', P.shape[0])); f.write(P.tobytes()); f.write(rgba.tobytes())
        print('house detail ok', P.shape[0], 'verts, mean colour', np.round(rgb.mean(0), 3))


def trees():
    """Atlas of the tree photographs: 8 columns; each species/variant takes two rows (16 directions)."""
    s = state()
    meta = json.load(open(os.path.join(L.OUT, 'trees.json')))
    order = [('jac', 0), ('jac', 1), ('small', 0), ('small', 1)]
    FW = meta['jac']['frameW']; CH = max(meta[k]['frameH'] for k in ('jac', 'small'))
    V = L.VIEWS; COLS = 8; RPV = (V + COLS - 1) // COLS
    atlas = np.zeros((CH * RPV * len(order), FW * COLS, 4), np.float32)
    for r, (sp, v) in enumerate(order):
        for k in range(V):
            a = np.load(os.path.join(L.OUT, 'tree_%s_%d_%02d.npy' % (sp, v, k))).astype(np.float32)
            al = np.clip(a[:, :, 3:4], 0, 1)
            rgb = develop(a[:, :, :3] / np.maximum(al, 1e-4), s['exposure'] * TREE_PRINT)
            # spread the leaf colour into the transparent surroundings (no dark fringes when the texture is filtered)
            pm = rgb * al
            for sig in (1.5, 4.0, 12.0):
                fill = blur(pm, sig) / np.maximum(blur(al, sig), 1e-4)
                rgb = np.where(al > 0.5, rgb, np.where(blur(al, sig) > 0.01, fill, rgb))
            fh = a.shape[0]
            y0 = (r * RPV + k // COLS) * CH; x0 = (k % COLS) * FW
            atlas[y0:y0 + fh, x0:x0 + FW, :3] = rgb
            atlas[y0:y0 + fh, x0:x0 + FW, 3:] = al
    Image.fromarray(to_u8(atlas), 'RGBA').save(os.path.join(W, 'trees.png'))
    info = dict(atlasW=FW * COLS, atlasH=atlas.shape[0], cellW=FW, cellH=CH, views=V, cols=COLS, rowsPerVariant=RPV,
                species=[dict(frameW=meta[k]['frameW'], frameH=meta[k]['frameH'], widthPerHeight=meta[k]['widthPerHeight'],
                              heightPerHeight=meta[k]['heightPerHeight'], base=meta[k]['base']) for k in ('jac', 'small')])
    save_state(trees=info)
    prev = atlas[:CH * 2, :, :3] * atlas[:CH * 2, :, 3:] + (1 - atlas[:CH * 2, :, 3:]) * np.array([0.5, 0.65, 0.9], np.float32)
    Image.fromarray(to_u8(prev)).resize((FW * COLS // 4, CH * 2 // 4)).save(os.path.join(L.OUT, 'trees_preview.jpg'), quality=88)
    print('trees ok', atlas.shape)


def sky():
    """Needs out/sky.npy (written by u_sky.py inside Blender: the panorama as scene-linear floats)."""
    a = np.load(os.path.join(L.OUT, 'sky.npy')).astype(np.float32)
    h, w = a.shape[:2]
    lum = a @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    y, x = divmod(int(np.argmax(lum)), w)
    # Unity side (G3Sky.shader): u = 0.5 - atan2(dir.z, dir.x) / 2pi.  The sun must stand opposite to where its light travels.
    az = math.atan2(-L.SUN_TRAVEL[1], -L.SUN_TRAVEL[0])
    u_target = (0.5 - az / (2 * math.pi)) % 1.0
    shift = int(round(u_target * w - (x + 0.5)))
    a = np.roll(a, shift, axis=1)
    elev = 90.0 - (y + 0.5) / h * 180.0
    up = a[:h // 2]
    exposure = 0.55 / float(np.percentile(up[:, :, 2], 50))
    img = post.develop(a, exposure, bloom=0.25, sat=1.10)
    Image.fromarray(to_u8(img)).save(os.path.join(W, 'sky.png'))
    save_state(sun_elevation=elev)
    Image.fromarray(to_u8(img)).resize((1024, 512), Image.LANCZOS).save(os.path.join(L.OUT, 'sky_preview.jpg'), quality=88)
    print('sky ok: sun elevation %.1f, exposure %.2f, rolled %d px' % (elev, exposure, shift))


def world_json():
    s = state()
    d = dict(grass=s.get('grass', [0.3, 0.45, 0.15]), sunElevation=s.get('sun_elevation', 37.8), trees=s.get('trees'))
    json.dump(d, open(os.path.join(W, 'world.json'), 'w'), indent=1)
    L.export_json()


if __name__ == '__main__':
    os.makedirs(W, exist_ok=True)
    todo = sys.argv[1:] or [k for k, f in (('near', 'near.npy'), ('far', 'far.npy'), ('house', 'house_bake.npy'), ('trees', 'trees.json'), ('sky', 'sky.npy')) if os.path.exists(os.path.join(L.OUT, f))]
    for k in todo:
        globals()[k]()
    world_json()
