# -*- coding: utf-8 -*-
"""东京的夜空（纯 Python）-> Assets/Resources/World/tokyo/sky.png。照SpriteStage-Basketball 的 Pipeline/b_night_sky.py 改的：
大城市的光污染更重——地平线一圈暖灰的天光更亮更高，星星只剩几百颗亮的，低空有几缕被城市灯光从下面照亮的薄云。
月亮的位置、大小和白宫场景一样（月面图 moon.png 沿用 World/ 下那张）。等距柱状投影，u = 0.5 - atan2(北, 东)/2π，第一行是天顶。"""
import os, math, sys
import numpy as np
from PIL import Image, ImageFilter
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import t_layout as T
U = T.U

W, H = 4096, 2048


def blur(a, r):
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r)), np.float32) / 255.0


def clouds(rng):
    """低频噪声叠几层，横向拉长（近地平线的云是扁的），只在仰角 1-35° 之间"""
    acc = np.zeros((H, W), np.float32)
    for scale, amp in ((48, 0.55), (20, 0.3), (8, 0.15)):
        n = rng.random((H // scale + 2, W // (scale * 5) + 2)).astype(np.float32)        # 横向拉长 5 倍：扁的层云
        im = Image.fromarray((n * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)
        acc += amp * np.asarray(im, np.float32) / 255.0
    acc = blur(acc, 6)
    el = (0.5 - (np.arange(H, dtype=np.float32) + 0.5) / H) * 180.0
    band = np.clip((el - 1.0) / 4.0, 0, 1) * np.clip((35.0 - el) / 20.0, 0, 1)
    c = np.clip((acc - 0.50) * 2.2, 0, 1) * band[:, None] * 0.6
    return c


def main():
    rng = np.random.default_rng(31)
    el = (0.5 - (np.arange(H, dtype=np.float32) + 0.5) / H) * np.pi
    t = np.clip(el / (np.pi / 2), 0, 1)
    zen = np.array([0.0045, 0.0058, 0.0120], np.float32); mid = np.array([0.0110, 0.0125, 0.0215], np.float32); hor = np.array([0.090, 0.066, 0.048], np.float32)
    k1 = np.exp(-t * 6.0)[:, None]; k2 = np.exp(-t * 2.0)[:, None]
    col = zen[None, :] * (1 - k2) + mid[None, :] * (k2 - k1 * k2) + hor[None, :] * (k1 * k2)
    sky = np.repeat(col[:, None, :], W, 1)
    # 月亮附近的光晕
    az = math.atan2(math.cos(math.radians(U.MOON_AZ)), math.sin(math.radians(U.MOON_AZ)))
    mu = (0.5 - az / (2 * math.pi)) % 1.0; mv = 0.5 - math.radians(U.MOON_EL) / math.pi
    cx, cy = mu * W, mv * H
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    dx = (xx - cx + W / 2) % W - W / 2
    ang = np.hypot(dx * (360.0 / W) * math.cos(math.radians(U.MOON_EL)), (yy - cy) * (180.0 / H))
    glow = 0.035 * np.exp(-(ang / 9.0) ** 1.3) + 0.20 * np.exp(-(ang / 2.0) ** 1.6)
    sky += glow[:, :, None] * np.array([0.55, 0.66, 0.90], np.float32)[None, None, :]
    # 薄云：被城市从下面照亮（暖灰），在月亮附近带一点月光
    c = clouds(rng)
    lit = np.array([0.050, 0.040, 0.034], np.float32)[None, None, :] * (0.4 + 0.6 * np.exp(-t * 5.0))[:, None, None]
    sky = sky * (1 - 0.35 * c[:, :, None]) + c[:, :, None] * (lit + 0.15 * glow[:, :, None])
    # 星星：几百颗，地平线附近和月亮附近没有
    n = 700
    su = rng.random(n); sz = rng.random(n)
    sel = np.arcsin(sz); sv = 0.5 - sel / np.pi
    mag = rng.pareto(2.4, n).astype(np.float32) * 0.045 + 0.015
    stars = np.zeros((H, W, 3), np.float32)
    px_ = (su * W).astype(np.int32) % W; py_ = np.clip((sv * H).astype(np.int32), 0, H - 1)
    fade = np.clip((sel - math.radians(14.0)) / math.radians(30.0), 0, 1) ** 1.5 * (1.0 - np.clip(1.0 - ang[py_, px_] / 18.0, 0, 1) * 0.9)
    fade *= 1.0 - c[py_, px_]
    np.add.at(stars, (py_, px_), (np.clip(mag, 0, 1.0) * fade)[:, None] * np.array([1.0, 1.0, 1.02], np.float32))
    soft = np.asarray(Image.fromarray((np.clip(stars, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.1)), np.float32) / 255.0
    sky += stars * 0.8 + soft * 0.8
    img = np.clip(sky, 0, 1) ** (1 / 2.2)
    out = (img * 255 + 0.5).astype(np.uint8)
    os.makedirs(T.UNITY_WORLD, exist_ok=True)
    Image.fromarray(out).save(os.path.join(T.UNITY_WORLD, 'sky.png'))
    Image.fromarray(out).resize((1024, 512), Image.LANCZOS).save(os.path.join(T.OUT, 'sky_preview.jpg'), quality=90)
    for e in (0, 2, 5, 10, 20):
        r = int((0.5 - e / 180) * H); print('el %2d' % e, (out[r].mean(0) / 255).round(3))
    print('SKY_OK')


if __name__ == '__main__':
    main()
