# -*- coding: utf-8 -*-
"""Blender -b --factory-startup -P u_sky.py : the sky panorama as scene-linear floats (out/sky.npy, 4096 x 2048)."""
import bpy, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import u_layout as L
im = bpy.data.images.load(L.HDRI)
w, h = im.size
buf = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(buf)
a = buf.reshape(h, w, 4)[::-1, :, :3]
np.save(os.path.join(L.OUT, 'sky.npy'), np.minimum(a, 60000.0).astype(np.float32))
print('SKY_OK', w, h)
