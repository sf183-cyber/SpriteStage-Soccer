# -*- coding: utf-8 -*-
"""World-space control maps for the near lawn (numpy + Pillow) -> Pipeline/tex/*.npy, 80 px per metre.
Sampled by u_lawn_near.py when it grows the grass: painted lines, mowing bands, wear, colour variation."""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw
import u_layout as L
sys.path.insert(0, L.HD)
from common import vnoise, blur

PPM = 80
X0, X1, Y0, Y1 = L.NEAR['x0'], L.NEAR['x1'], L.NEAR['y0'], L.NEAR['y1']
W, H = int(round((X1 - X0) * PPM)), int(round((Y1 - Y0) * PPM))
SS = 2                                  # supersampling for the line art


def px(x, y):
    return ((x - X0) * PPM * SS, (Y1 - y) * PPM * SS)


def u8(a):
    return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)


def arc(cx, cy, r, a0, a1, n=96):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def strokes():
    P = []
    HLx, HWy = L.HL, L.HW
    P.append([(-HLx, HWy), (HLx, HWy), (HLx, -HWy), (-HLx, -HWy), (-HLx, HWy)])
    P.append([(0, HWy), (0, -HWy)])
    P.append(arc(0, L.CENTRE_Y, L.CENTRE_R, 0, 360, 160))
    for s in (-1, 1):
        gx = s * HLx
        P.append([(gx, L.PBOX_Y1), (s * L.PBOX_X, L.PBOX_Y1), (s * L.PBOX_X, L.PBOX_Y0), (gx, L.PBOX_Y0)])
        P.append([(gx, L.GBOX_Y1), (s * L.GBOX_X, L.GBOX_Y1), (s * L.GBOX_X, L.GBOX_Y0), (gx, L.GBOX_Y0)])
        a = math.degrees(math.acos(min(1.0, (L.PSPOT_X - L.PBOX_X) / L.PARC_R)))
        pts = arc(s * L.PSPOT_X, L.CENTRE_Y, L.PARC_R, -a, a) if s < 0 else arc(s * L.PSPOT_X, L.CENTRE_Y, L.PARC_R, 180 - a, 180 + a)
        P.append(pts)
        P.append(arc(gx, HWy, L.CORNER_R, 270 if s < 0 else 180, 360 if s < 0 else 270, 24))
        P.append(arc(gx, -HWy, L.CORNER_R, 0 if s < 0 else 90, 90 if s < 0 else 180, 24))
    return P


def main():
    os.makedirs(L.TEX, exist_ok=True)
    im = Image.new('L', (W * SS, H * SS), 0)
    d = ImageDraw.Draw(im)
    lw = int(round(L.LINE_W * PPM * SS))
    for pts in strokes():
        d.line([px(x, y) for x, y in pts], fill=255, width=lw, joint='curve')
    def dot(x, y, r):
        (cx, cy) = px(x, y); rr = r * PPM * SS
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=255)
    dot(0, L.CENTRE_Y, 0.14); dot(-L.PSPOT_X, L.CENTRE_Y, 0.11); dot(L.PSPOT_X, L.CENTRE_Y, 0.11)
    lines = np.asarray(im.resize((W, H), Image.LANCZOS), np.float32) / 255.0
    ys = Y1 - (np.arange(H, dtype=np.float32) + 0.5) / PPM
    xs = X0 + (np.arange(W, dtype=np.float32) + 0.5) / PPM
    # mowing: bands across the pitch 1.5 m wide (14 between the touch lines) and cross bands 2 m wide
    # (the original widens its bands towards the camera - fake perspective; in a real 3D world they are simply equal)
    band1 = (np.floor((ys + L.HW) / 1.5) % 2).astype(np.float32)
    band = blur(np.repeat(band1[:, None], W, 1), 1.2)
    vband = blur(np.repeat((np.floor(xs / 2.0) % 2).astype(np.float32)[None, :], H, 0), 1.2)
    X, Y = np.meshgrid(xs, ys)
    def blob(cx, cy, rx, ry):
        return np.exp(-(((X - cx) / rx) ** 2 + ((Y - cy) / ry) ** 2))
    gy = (L.GOAL_Y0 + L.GOAL_Y1) / 2
    wear = 0.8 * (blob(-L.HL + 0.7, gy, 1.2, 2.3) + blob(L.HL - 0.7, gy, 1.2, 2.3)) + 0.35 * blob(0, L.CENTRE_Y, 1.0, 1.2)
    wear = np.clip(wear * (0.4 + 1.1 * vnoise((H, W), 8, 21, 4)), 0, 1)
    patch = vnoise((H, W), 64, 31, 4)
    fine = vnoise((H, W), 8, 32, 3)
    for name, a in (('lines', lines), ('band', band), ('vband', vband), ('wear', wear), ('patch', patch), ('fine', fine)):
        np.save(os.path.join(L.TEX, name + '.npy'), u8(a))
    Image.fromarray(u8(np.clip(lines + 0.25 * band + 0.12 * vband, 0, 1))).resize((W // 4, H // 4), Image.LANCZOS).save(os.path.join(L.OUT, 'tex_preview.png'))
    print('control maps', W, H)


if __name__ == '__main__':
    main()
