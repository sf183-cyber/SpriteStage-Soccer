# -*- coding: utf-8 -*-
"""Blender 里用（东京塔夜场）：足球场的球门、角旗照旧（../u_world.py），光换成夜里的（月亮、夜空、四根泛光灯杆）。
night() 照抄篮球的 u_world.night()，去掉了照白宫的地灯。近景草坪、远景草坪都调它，所以两张图的光是一致的。"""
import bpy, os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(1, os.path.dirname(HERE))
import n_layout as L
import u_world as _uw
from u_world import *                      # reset, col, pbr, box, cyl, quad, tri_mesh, setup_cycles, place_asset, goals, corner_flags ...
import n_layout as L                       # （上一行把 u_world 里的 L = u_layout 也带进来了，这里换回来）
from mathutils import Vector

C = _uw.C
C.OUT = L.OUT


def daylight(sc, sky=1.0):
    return night(sc)


def night(sc):
    world = bpy.data.worlds.new('night'); sc.world = world; world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = tuple(L.NIGHT_SKY) + (1,); bg.inputs['Strength'].default_value = 1.0
    def aim(ob, target):
        d = Vector(target) - ob.location
        ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    md = Vector(L.moon_dir())
    ld = bpy.data.lights.new('moon', 'SUN'); ld.energy = L.MOON_LIGHT; ld.angle = math.radians(1.2); ld.color = (0.60, 0.72, 1.0)
    ob = bpy.data.objects.new('moon', ld); col().objects.link(ob)
    ob.rotation_euler = (-md).to_track_quat('-Z', 'Y').to_euler()
    for i, (x, y, h, tx, ty) in enumerate(L.FLOODS):
        for kind, (watts, cone, blend), target in (('beam', L.FLOOD_BEAM, (tx, ty, 0.0)), ('spill', L.FLOOD_SPILL, (x * 0.62, y * 0.62, 0.0))):
            ld = bpy.data.lights.new('flood_%s%d' % (kind, i), 'SPOT'); ld.energy = watts; ld.color = L.FLOOD_COLOUR
            ld.spot_size = math.radians(cone); ld.spot_blend = blend; ld.shadow_soft_size = 0.45
            ob = bpy.data.objects.new('flood_%s%d' % (kind, i), ld); col().objects.link(ob)
            ob.location = (x * 0.97, y * 0.97, h); aim(ob, target)
    return L.MOON_EL


def masts(cam_visible=False):
    """泛光灯杆（只投细细的影子；灯本身是 night() 里的聚光灯）"""
    grey = pbr('mast', (0.25, 0.26, 0.28), 0.5)
    for (x, y, h, tx, ty) in L.FLOODS:
        cyl('mast', (x, y, 0), 0.16, h + 0.6, grey, cam_visible=cam_visible)


def render_npy(sc, tag):
    C.OUT = L.OUT
    C.render_npy(sc, tag)
