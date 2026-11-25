# -*- coding: utf-8 -*-
"""渲全景带：从对战线上方（BAND_EYE）往四周看，公园以外的一切（东京塔、城市、远处的树、路灯）。

    Blender -b --factory-startup -P t_pano.py -- <每度像素> <采样> <第几块(0-3 或 all)> [名字前缀]

第 i 块覆盖方位 -45+90i .. 45+90i 度（第 0 块正中是正北、东京塔），纬度 BAND_LO..BAND_HI。
背景透明（天空由 Unity 的天空盒画），输出 out/<前缀><i>.npy（线性，带 alpha，已预乘）。"""
import bpy, sys, os, math
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import t_layout as T
import t_tower, t_city
U = T.U
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
PPD = int(argv[0]) if len(argv) > 0 else 12
SPP = int(argv[1]) if len(argv) > 1 else 32
WHICH = argv[2] if len(argv) > 2 else '0'
PREFIX = argv[3] if len(argv) > 3 else 'band'

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
coll = sc.collection

# ---- 东京塔
tower = t_tower.build(coll, origin=(T.TOWER_X, T.TOWER_D, 0.0), rot=math.radians(T.TOWER_ROT))
for m in tower.data.materials:
    t_city.haze_wrap(m)
# ---- 城市、地面、远处的树
t_city.build(coll)
t_city.ground(coll)
t_city.trees(coll)

# ---- 光：夜空、月光（和白宫场景同一个月亮）
world = bpy.data.worlds.new('night'); sc.world = world; world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = tuple(U.NIGHT_SKY) + (1,); bg.inputs['Strength'].default_value = 1.0
md = Vector(U.moon_dir())
ld = bpy.data.lights.new('moon', 'SUN'); ld.energy = U.MOON_LIGHT; ld.angle = math.radians(1.2); ld.color = (0.60, 0.72, 1.0)
ob = bpy.data.objects.new('moon', ld); coll.objects.link(ob)
ob.rotation_euler = (-md).to_track_quat('-Z', 'Y').to_euler()

# ---- 全景相机
cam = bpy.data.cameras.new('pano'); cam.type = 'PANO'; cam.panorama_type = 'EQUIRECTANGULAR'
cam.latitude_min = math.radians(T.BAND_LO); cam.latitude_max = math.radians(T.BAND_HI)
cam.clip_start = 0.5; cam.clip_end = 20000.0
co = bpy.data.objects.new('pano', cam); coll.objects.link(co); sc.camera = co
co.location = T.BAND_EYE; co.rotation_euler = (math.radians(90.0), 0.0, 0.0)        # 图的正中是正北，往右是东（测过：t_layout 旁的 out/pano_test.py）

sc.render.engine = 'CYCLES'
prefs = bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type = 'METAL'; prefs.get_devices()
for d in prefs.devices: d.use = (d.type == 'METAL')
sc.cycles.device = 'GPU'
sc.cycles.samples = SPP; sc.cycles.use_denoising = True
sc.cycles.use_adaptive_sampling = True; sc.cycles.adaptive_threshold = 0.02
sc.cycles.max_bounces = 4; sc.cycles.sample_clamp_indirect = 6.0
sc.cycles.pixel_filter_type = 'BLACKMAN_HARRIS'
sc.render.film_transparent = True
sc.render.image_settings.file_format = 'OPEN_EXR'; sc.render.image_settings.color_mode = 'RGBA'; sc.render.image_settings.color_depth = '32'
if WHICH.startswith('view'):
    # 试看：普通透视相机，从同一点朝某个方向拍（view:方位:仰角:视角），1600 x 900
    _, az, el, fov = WHICH.split(':')
    cam.type = 'PERSP'; cam.sensor_fit = 'HORIZONTAL'; cam.angle = math.radians(float(fov))
    co.rotation_euler = (math.radians(90.0 + float(el)), 0.0, -math.radians(float(az)))
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    exr = os.path.join(T.OUT, PREFIX + '_view.exr'); sc.render.filepath = exr
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(exr)
    buf = np.empty(1600 * 900 * 4, np.float32); im.pixels.foreach_get(buf)
    np.save(os.path.join(T.OUT, PREFIX + '_view.npy'), buf.reshape(900, 1600, 4)[::-1].astype(np.float32))
    os.remove(exr)
    print('RENDER_OK view')
    raise SystemExit
tiles = range(T.BAND_TILES) if WHICH == 'all' else [int(x) for x in WHICH.split(',')]
span = 360.0 / T.BAND_TILES
h = int(round((T.BAND_HI - T.BAND_LO) * PPD)); w = int(round(span * PPD))
sc.render.resolution_x, sc.render.resolution_y = w, h
for i in tiles:
    a0 = -span / 2 + span * i
    cam.longitude_min = math.radians(a0); cam.longitude_max = math.radians(a0 + span)
    exr = os.path.join(T.OUT, '%s%d.exr' % (PREFIX, i))
    sc.render.filepath = exr
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(exr)
    buf = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(buf)
    np.save(os.path.join(T.OUT, '%s%d.npy' % (PREFIX, i)), buf.reshape(h, w, 4)[::-1].astype(np.float16))
    bpy.data.images.remove(im); os.remove(exr)
    print('RENDER_OK tile %d  az %.0f..%.0f  %dx%d' % (i, a0, a0 + span, w, h))
