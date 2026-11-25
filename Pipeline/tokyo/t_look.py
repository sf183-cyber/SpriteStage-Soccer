# -*- coding: utf-8 -*-
"""东京塔的试渲（只看塔本身的形状和灯光）。
    Blender -b --factory-startup -P t_look.py -- 距离 宽 高 采样 名字 [灯光倍数]
出 out/<名字>.npy（线性），再用 t_dev.py 冲洗。"""
import bpy, sys, os, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import t_tower as T
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
dist = float(argv[0]) if argv else 1000.0
w = int(argv[1]) if len(argv) > 1 else 900
h = int(argv[2]) if len(argv) > 2 else 1400
spp = int(argv[3]) if len(argv) > 3 else 128
tag = argv[4] if len(argv) > 4 else 'look'
lamp = float(argv[5]) if len(argv) > 5 else 1.0

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
coll = sc.collection
T.build(coll, origin=(0.0, dist, 0.0), rot=math.radians(18.0), lamp_scale=lamp)

# 地面
me = bpy.data.meshes.new('ground'); s = 4000
me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
g = bpy.data.objects.new('ground', me); coll.objects.link(g)
m = bpy.data.materials.new('asphalt'); m.use_nodes = True
m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.05, 0.05, 0.05, 1)
m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.95
me.materials.append(m)

# 夜空
world = bpy.data.worlds.new('night'); sc.world = world; world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.0025, 0.0035, 0.007, 1); bg.inputs['Strength'].default_value = 1.0

cam = bpy.data.cameras.new('cam')
cam.sensor_fit = 'VERTICAL'; cam.clip_start = 1.0; cam.clip_end = 30000.0
top = 345.0
cam.angle = 2 * math.atan((top / dist) / 2 * 1.08)
co = bpy.data.objects.new('cam', cam); coll.objects.link(co); sc.camera = co
co.location = (0, 0, 1.6)
look = Vector((0, dist, top * 0.5)) - co.location
co.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()

sc.render.engine = 'CYCLES'
prefs = bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type = 'METAL'; prefs.get_devices()
for d in prefs.devices: d.use = (d.type == 'METAL')
sc.cycles.device = 'GPU'
sc.cycles.samples = spp; sc.cycles.use_denoising = True
sc.cycles.max_bounces = 4
sc.render.resolution_x, sc.render.resolution_y = w, h
sc.render.image_settings.file_format = 'OPEN_EXR'; sc.render.image_settings.color_depth = '32'
exr = os.path.join(HERE, 'out', tag + '.exr')
sc.render.filepath = exr
bpy.ops.render.render(write_still=True)
import numpy as np
im = bpy.data.images.load(exr)
buf = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(buf)
np.save(os.path.join(HERE, 'out', tag + '.npy'), buf.reshape(h, w, 4)[::-1].astype(np.float32))
os.remove(exr)
print('RENDER_OK', tag)
