# -*- coding: utf-8 -*-
"""东京塔夜场（2026-11-24）：足球场的尺寸照旧（../u_layout.py），加上夜里的光（月亮、天空、四根泛光灯杆）。

夜里的光照抄篮球的白宫夜景（SpriteStage-Basketball/Pipeline/u_layout.py 的 NIGHT 部分），只把灯杆挪到足球场四角外面、
光束放宽到照满 32 x 21 m 的球场。东京塔场景的其余尺寸（公园、全景带、路灯、树）在 t_layout.py。
这里的产物都在 tokyo/out/，白宫场景的 Pipeline/out/ 不动。"""
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import u_layout as _base
from u_layout import *                     # 球场、球门、近景草坪的范围、树的品种 ...

HERE = os.path.dirname(os.path.abspath(__file__))     # （上面的 import * 把 u_layout 的 HERE 带进来了）
OUT = os.path.join(HERE, 'out')
TEX = _base.TEX                            # 球场画线、割草条纹（Pipeline/tex/*.npy，白宫场景烘的，同一块球场）
UNITY_WORLD = _base.UNITY_WORLD            # Assets/Resources/World（东京塔的东西在它下面的 tokyo/）
TREES = []                                 # 东京塔场景的树在 t_layout.TREES（近景烘焙自己去拿）

NIGHT = True
MOON_AZ, MOON_EL = -22.0, 13.2             # 月亮：塔的左边 22°、仰角 13°
MOON_SIZE = 2.3
# 泛光灯杆：四根，在足球场四角外面（球门线外 5 m、边线外 4.5 m：观众在远端边线外 1.5-3 m，摄影师在球门两侧 1.1 m，都让开了）
MAST_H = 18.0
FLOODS = [(sx * 21.5, sy * 15.0, MAST_H, sx * 7.5, sy * 2.5) for sx in (-1, 1) for sy in (-1, 1)]
# 每根杆两种光（同篮球的“夜景光照调真实”）：瞄准的主光束 + 往下和球场之间的外溢光。Blender 瓦数、光锥角度、边缘柔和度。
# 足球场很大，草的反照率只有 0.1：主光束放宽到 64°、加到 6800 W（试渲定的，见 n_lawn_near.py）
FLOOD_BEAM = (8500.0, 64.0, 0.9)
FLOOD_SPILL = (900.0, 165.0, 1.0)
FLOOD_WATTS = FLOOD_BEAM[0] + FLOOD_SPILL[0]
NIGHT_SKY = (0.0168, 0.0264, 0.0552)
MOON_LIGHT = 0.22
FLOOD_COLOUR = (0.95, 0.975, 1.0)
HOUSE_LIGHTS = []                          # 没有白宫
HOUSE_LIGHT_COLOUR = (1.0, 0.90, 0.74)
HOUSE_Y = 1.0e6


def moon_dir():
    """从场景指向月亮的单位向量（x 右、y 朝塔、z 上）"""
    a, e = math.radians(MOON_AZ), math.radians(MOON_EL)
    return (math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e))
