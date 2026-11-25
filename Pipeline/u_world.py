# -*- coding: utf-8 -*-
"""Blender-side builders for the Unity world (run inside Blender): lighting, trees, goals, the White House."""
import bpy, bmesh, os, sys, math
import numpy as np
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import u_layout as L
sys.path.insert(0, os.path.join(L.HD, 'whitehouse'))
import wh_common as C
from wh_common import reset, col, new_mat, pbr, hexc, add_obj, box, cyl, quad, tri_mesh, setup_cycles, place_asset, load_asset

C.SUN_TRAVEL = Vector((L.SUN_TRAVEL[0], L.SUN_TRAVEL[1], 0.0)).normalized()
C.OUT = L.OUT


def daylight(sc, sky=1.0):
    return C.daylight_real(sc, sky_strength=sky, sun_strength=0.0)


def render_npy(sc, tag):
    C.OUT = L.OUT
    C.render_npy(sc, tag)


def trees(max_dist=None, cam_visible=False, only=None):
    """The scanned trees at their places (linked copies).  They are normally invisible to the camera:
    in the game they are drawn as impostors, here they only throw their shadows."""
    n = 0
    for i, (sp, x, y, h, v) in enumerate(L.TREES):
        if max_dist is not None and math.hypot(x, y) > max_dist:
            continue
        if only is not None and i not in only:
            continue
        place_asset(L.SPECIES[sp], x, y, h, rot_z=L.VARIANT_YAW[v], squash=1.1, visible_camera=cam_visible)
        n += 1
    print('trees placed', n)


def goals(cam_visible=False):
    """Posts, bar and a sparse net - enough to throw the right shadow on the turf."""
    white = pbr('goal', (0.9, 0.9, 0.9), 0.4)
    net = bpy.data.materials.new('net'); net.use_nodes = True
    nt = net.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (0.9, 0.9, 0.9, 1); b.inputs['Alpha'].default_value = 0.22
    r = 0.06
    for s in (-1, 1):
        gx = s * L.HL; bx = s * (L.HL + L.GOAL_D)
        for y in (L.GOAL_Y0, L.GOAL_Y1):
            cyl('goal_post', (gx, y, 0), r, L.GOAL_H, white, cam_visible=cam_visible)
            cyl('goal_back', (bx, y, 0), r * 0.6, L.GOAL_H, white, cam_visible=cam_visible)
            cyl('goal_top', ((gx + bx) / 2, y, L.GOAL_H), r * 0.6, L.GOAL_D, white, axis='X', cam_visible=cam_visible)
        cyl('goal_bar', (gx, (L.GOAL_Y0 + L.GOAL_Y1) / 2, L.GOAL_H), r, L.GOAL_Y1 - L.GOAL_Y0 + 0.12, white, axis='Y', cam_visible=cam_visible)
        cyl('goal_bar_b', (bx, (L.GOAL_Y0 + L.GOAL_Y1) / 2, L.GOAL_H), r * 0.6, L.GOAL_Y1 - L.GOAL_Y0, white, axis='Y', cam_visible=cam_visible)
        H = L.GOAL_H
        for pts in ([(bx, L.GOAL_Y0, 0), (bx, L.GOAL_Y1, 0), (bx, L.GOAL_Y1, H), (bx, L.GOAL_Y0, H)],
                    [(gx, L.GOAL_Y0, H), (gx, L.GOAL_Y1, H), (bx, L.GOAL_Y1, H), (bx, L.GOAL_Y0, H)],
                    [(gx, L.GOAL_Y0, 0), (bx, L.GOAL_Y0, 0), (bx, L.GOAL_Y0, H), (gx, L.GOAL_Y0, H)],
                    [(gx, L.GOAL_Y1, 0), (bx, L.GOAL_Y1, 0), (bx, L.GOAL_Y1, H), (gx, L.GOAL_Y1, H)]):
            quad('goal_net', pts, net, cam_visible=cam_visible)


def corner_flags(cam_visible=False):
    pole = pbr('flag_pole', (0.85, 0.85, 0.8), 0.4); red = pbr('flag', hexc('d8261c'), 0.7)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * L.HL, sy * L.HW
            cyl('flag_pole', (x, y, 0.0), 0.016, 1.5, pole, cam_visible=cam_visible)
            quad('flag', [(x, y, 1.5), (x - 0.40 * sx, y + 0.05, 1.37), (x, y, 1.22)], red, cam_visible=cam_visible)
