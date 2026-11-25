# -*- coding: utf-8 -*-
"""PLATEAU 的外墙照片（plateau_raw/*_appearance/*.jpg，白天的航拍）拼成图集，给 Blender 用一个材质贴。

    python3 t_atlas.py
出 out/plateau_atlas_<i>.jpg（8192 x 8192）和 out/plateau_atlas.json：
    贴图文件 -> [图集号, 左上 x, 左上 y, 宽, 高, 原宽, 原高]
最长边超过 MAXSIDE 的缩小；每张四周留 PAD 像素并把边缘像素往外复制一圈（防止采样时串色）。
出典：国土交通省 3D都市モデル（Project PLATEAU）港区（2025年度）。"""
import os, json
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, 'plateau_raw')     # PLATEAU 的原始数据：自己下载放这里（见 SOURCES.md），不随源码发布
OUT = os.path.join(HERE, 'out')
SIZE = 8192
MAXSIDE = 2048
PAD = 4


def main():
    need = json.load(open(os.path.join(OUT, 'tex_needed.json')))
    items = []
    for p in need:
        f = os.path.join(RAW, p)
        try:
            im = Image.open(f).convert('RGB')
        except Exception as e:
            print('skip', p, e); continue
        ow, oh = im.size
        s = min(1.0, MAXSIDE / max(ow, oh))
        if s < 1.0:
            im = im.resize((max(1, int(ow * s)), max(1, int(oh * s))), Image.LANCZOS)
        items.append((p, im, ow, oh))
    items.sort(key=lambda t: -t[1].size[1])
    atlases = [Image.new('RGB', (SIZE, SIZE), (90, 90, 90))]
    info = {}
    x = y = 0; shelf = 0; ai = 0
    for p, im, ow, oh in items:
        w, h = im.size
        W, H = w + 2 * PAD, h + 2 * PAD
        if x + W > SIZE:
            x = 0; y += shelf; shelf = 0
        if y + H > SIZE:
            atlases.append(Image.new('RGB', (SIZE, SIZE), (90, 90, 90))); ai += 1; x = y = 0; shelf = 0
        # 边缘外扩：先把放大一圈的图贴上，再贴原图
        big = im.resize((W, H), Image.NEAREST)
        atlases[ai].paste(big, (x, y)); atlases[ai].paste(im, (x + PAD, y + PAD))
        info[p] = [ai, x + PAD, y + PAD, w, h, ow, oh]
        x += W; shelf = max(shelf, H)
    for i, a in enumerate(atlases):
        a.save(os.path.join(OUT, 'plateau_atlas_%d.jpg' % i), quality=92)
    json.dump(info, open(os.path.join(OUT, 'plateau_atlas.json'), 'w'))
    # 每个多边形在照片上的平均亮度（线性），外墙材质用它判断哪里是玻璃（比平均暗的）
    import pickle
    import numpy as np
    TEX = pickle.load(open(os.path.join(OUT, 'plateau_tex.pkl'), 'rb'))
    lin = {}
    for p, im, ow, oh in items:
        lin[p] = (np.asarray(Image.open(os.path.join(RAW, p)).convert('RGB'), np.float32) / 255.0) ** 2.2
    pmean = {}; pcol = {}
    for pid, (img, uv) in TEX.items():
        if img not in lin: continue
        A = lin[img]; H_, W_ = A.shape[:2]; U = np.array(uv)
        c0, c1 = int(max(0, U[:, 0].min()) * W_), int(min(1, U[:, 0].max()) * W_) + 1
        r0, r1 = int(max(0, 1 - U[:, 1].max()) * H_), int(min(1, 1 - U[:, 1].min()) * H_) + 1
        if r1 > r0 and c1 > c0:
            c = A[r0:r1, c0:c1].reshape(-1, 3).mean(0)
            pcol[pid] = tuple(float(v) for v in c); pmean[pid] = float(c @ np.array([0.2126, 0.7152, 0.0722]))
    pickle.dump(pmean, open(os.path.join(OUT, 'plateau_pmean.pkl'), 'wb'))
    pickle.dump(pcol, open(os.path.join(OUT, 'plateau_pcolor.pkl'), 'wb'))
    print('photo colour for %d polygons' % len(pcol))
    print('ATLAS_OK %d images in %d atlases' % (len(info), len(atlases)))


if __name__ == '__main__':
    main()
