# -*- coding: utf-8 -*-
"""近景草坪试渲冲洗 + 量几个点的线性亮度（草叶稀时露土，只能比分布）。  python3 n_dev.py <名字> [对照的 near.npy]"""
import os, sys, math
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from t_dev import develop, to_u8
import n_layout as L

def probe(a, ppm, label):
    H, W = a.shape[:2]; s40 = math.sin(math.radians(40))
    def at(x, y):
        px, py = int((x - L.NEAR['x0']) * ppm), int(H / 2 - y * s40 * ppm)
        r = max(1, int(ppm * 0.6))
        return float(np.asarray(a[max(0, py - r):py + r, max(0, px - r):px + r, :3], np.float32).mean(axis=(0, 1)) @ np.array([0.2126, 0.7152, 0.0722]))
    pts = [('centre', 0, 0), ('pen spot', 12.5, 0), ('corner NE', 15.5, 10), ('corner SW', -15.5, -10), ('touch N', 0, 10), ('touch S', 0, -10),
           ('lawn edge E', 24.5, 0), ('lawn edge N', 0, 12.0), ('lawn edge S', 0, -12.0), ('lawn corner', 24.5, 12.0)]
    print(label + '  ' + '  '.join('%s %.4f' % (n, at(x, y)) for n, x, y in pts))

tag = sys.argv[1]
a = np.load(os.path.join(HERE, 'out', tag + '.npy'), mmap_mode='r')
ppm = a.shape[1] / (L.NEAR['x1'] - L.NEAR['x0'])
probe(a, ppm, tag)
img = to_u8(develop(np.asarray(a[:, :, :3], np.float32), 1.0, 0.15))
Image.fromarray(img).save(os.path.join(HERE, 'out', tag + '.png'))
if len(sys.argv) > 2:
    b = np.load(sys.argv[2], mmap_mode='r')
    probe(b, b.shape[1] / (L.NEAR['x1'] - L.NEAR['x0']), 'reference')
