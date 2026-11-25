# -*- coding: utf-8 -*-
"""把 out/<名字>.npy 按场景的曝光冲洗成 out/<名字>.png，并打印几个亮度。
    python3 t_dev.py 名字 [曝光倍数] [bloom]"""
import os, sys, json
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import u_layout as L
sys.path.insert(0, L.HD); sys.path.insert(0, os.path.join(L.HD, 'whitehouse'))
from common import to_u8
import post

EXPOSURE = json.load(open(os.path.join(L.OUT, 'compose_state.json')))['exposure']


def develop(lin, k=1.0, bloom=0.25, sat=1.0):
    img = post.develop(lin, EXPOSURE * k, bloom=bloom, sat=sat)
    return np.clip(0.5 + (img - 0.5) * 1.05, 0, 1) ** 0.86


if __name__ == '__main__':
    tag = sys.argv[1]
    k = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
    bl = float(sys.argv[3]) if len(sys.argv) > 3 else 0.25
    a = np.load(os.path.join(HERE, 'out', tag + '.npy'))[:, :, :3].astype(np.float32)
    lum = a @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    print('%s %s  exposure %.2f x %.2f  lum p50 %.4f p90 %.4f p99 %.4f p99.9 %.4f max %.2f' % (
        tag, a.shape, EXPOSURE, k, *np.percentile(lum, [50, 90, 99, 99.9]), lum.max()))
    Image.fromarray(to_u8(develop(a, k, bl))).save(os.path.join(HERE, 'out', tag + '.png'))
