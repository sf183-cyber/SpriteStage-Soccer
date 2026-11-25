# -*- coding: utf-8 -*-
"""Quick look at a scene-linear render: python3 u_dev.py tag [exposure]"""
import os, sys
import numpy as np
from PIL import Image
import u_layout as L
sys.path.insert(0, L.HD); sys.path.insert(0, os.path.join(L.HD, 'whitehouse'))
from common import to_u8
import post
tag = sys.argv[1]
a = np.load(os.path.join(L.OUT, tag + '.npy')).astype(np.float32)
g = a[:, :, 1]
exposure = float(sys.argv[2]) if len(sys.argv) > 2 else 0.31 / float(np.percentile(g, 65))
img = post.develop(a, exposure, bloom=0.0, sat=1.04)
img = np.clip(0.5 + (img - 0.5) * 1.05, 0, 1) ** 0.86
Image.fromarray(to_u8(img)).save(os.path.join(L.OUT, tag + '.png'))
print(tag, a.shape, 'exposure %.2f' % exposure)
