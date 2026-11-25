# -*- coding: utf-8 -*-
"""The 3D world of the Unity version - one place for every measurement (pure Python, no bpy).

World axes here: X = east (screen right in the original game), Y = north (away from the original
camera), Z = up, metres, origin = centre of the pitch.  Unity uses (x, z, y).

The original draws the pitch as a trapezoid (fake perspective: the goal lines lean 1:8).  Here the pitch
is a true rectangle and the game's field coordinates are un-projected onto it (field_to_world)."""
import math, json, os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
HD = os.path.join(os.path.dirname(os.path.dirname(HERE)), 'HD2D-草地球场', 'build')        # the HD pack project (shared helpers + assets)
ASSETS = os.path.join(HD, 'whitehouse', 'assets')
UNITY_WORLD = os.path.join(os.path.dirname(HERE), 'Assets', 'Resources', 'World')
OUT = os.path.join(HERE, 'out')
TEX = os.path.join(HERE, 'tex')

KX = 4.0 / 88.0                                        # metres per NES pixel across the pitch
KY = 4.0 / (88.0 * math.sin(math.radians(40.0)))       # metres per NES pixel into the screen
KZ = KX                                                # metres per NES pixel of height (sprites are drawn 1:1)
K_PERSP = 3.515e-4
FIELD_CX, FIELD_CY = 512.0, 197.5


def field_to_world(nx, ny):
    xt = (nx - FIELD_CX) / (1.0 + K_PERSP * (ny - 50.0))
    return xt * KX, (FIELD_CY - ny) * KY


HL = 355.5 * KX                    # half length of the pitch (goal line), 16.16 m
HW = (FIELD_CY - 49.0) * KY        # half width (touch line), 10.50 m
LINE_W = 0.11
CENTRE_Y = (FIELD_CY - 192.0) * KY
CENTRE_R = 1.70
PBOX_X = (512.0 - 247.3) * KX      # distance of the penalty-box front edge from the centre line
PBOX_Y0, PBOX_Y1 = (FIELD_CY - 290.0) * KY, (FIELD_CY - 97.0) * KY
GBOX_X = (512.0 - 197.7) * KX
GBOX_Y0, GBOX_Y1 = (FIELD_CY - 242.0) * KY, (FIELD_CY - 145.0) * KY
PSPOT_X = 12.51
PARC_R = 1.55
CORNER_R = 0.90
GOAL_Y0, GOAL_Y1 = (FIELD_CY - 228.0) * KY, (FIELD_CY - 171.0) * KY       # near post, far post
GOAL_H = 67.0 * KZ                 # 3.05 m (2.2 player heights, as drawn)
GOAL_D = 29.0 * KX                 # depth of the net behind the line

# baked ground textures
NEAR = dict(x0=-25.6, x1=25.6, y0=-12.8, y1=12.8, ppm=160)          # 8192 x 4096, every blade of grass

# light: late afternoon, sun in the west-south-west (same as the HD pack version)
SUN_TRAVEL = (0.86, 0.50)
HDRI = os.path.join(ASSETS, 'kloofendal_38d_partly_cloudy_puresky', 'kloofendal_38d_partly_cloudy_puresky_4k.hdr')

# Where the pitch lies on the South Lawn: HOUSE_Y is the distance from the centre of the pitch to the south front of
# the residence.  Everything of the site hangs on it, so changing this one number slides the pitch up and down the
# lawn.  79 would put the pitch in the middle of the oval lawn; 56 has it in the northern part, the north touch
# line 45 m from the house, with the South Drive passing between.  (Below about 45 the drive would run into the
# photographed turf round the pitch.)
HOUSE_Y = 56.0
# the South Grounds as on the HABS site plan (sheet 2): the great oval lawn inside the South Drive (its centre 79 m
# south of the house), the cross drive at its south end, and the fountain on the lower lawn 161 m south of the house
DRIVE = dict(cx=0.0, cy=HOUSE_Y - 79.0, rx=65.5, ry=55.5, w=5.0)
CROSS_DRIVE_Y = HOUSE_Y - 134.5
FOUNTAIN = (0.0, HOUSE_Y - 161.4)
LOWER_LAWN = dict(cy=HOUSE_Y - 134.5, r=74.0)        # the semicircle bounded by South Executive Avenue
FAR = dict(cx=0.0, cy=HOUSE_Y - 68.0, size=288.0, res=4096)         # baked ground: the whole south grounds, top-down
WINGS = dict(west=(-108.3, -65.3, HOUSE_Y - 23.1, HOUSE_Y + 15.0), east=(59.5, 85.1, HOUSE_Y - 19.0, HOUSE_Y + 21.5))   # x0, x1, y0, y1
MONUMENT = dict(x=6.0, y=-900.0, h=169.0, base=16.8)

# trees: (asset, x, y, height, variant).  variant = which pre-rotated copy (0/1); it fixes the tree's yaw.
SPECIES = {'jac': 'jacaranda_tree', 'small': 'tree_small_02'}
VARIANT_YAW = (0.4, 2.6)
VIEWS = 16                         # impostor directions per variant


def _trees():
    rng = np.random.default_rng(11)
    T = []
    def add(sp, x, y, h, v=None):
        T.append((sp, float(x), float(y), float(h), int(rng.integers(0, 2)) if v is None else v))
    # west: their afternoon shade reaches the west end of the pitch  (unchanged - the near lawn was baked with them)
    add('jac', -35.5, -6.0, 20, 0); add('jac', -42.0, 6.5, 24, 1); add('small', -33.0, -19.0, 16, 0); add('jac', -49.0, -15.0, 22, 1)
    # specimen trees on the east side of the oval lawn (kept with the pitch, like the four above)
    add('jac', 40.0, 8.0, 21); add('small', 46.0, -7.0, 15); add('jac', 38.5, -22.0, 19); add('jac', 51.0, -36.0, 23)
    # the masses of trees outside the South Drive, east and west (none in front of the house, none on the vista to the south)
    for deg in range(0, 360, 12):
        a = math.radians(deg)
        x, y = DRIVE['cx'] + (DRIVE['rx'] + 9.0) * math.cos(a), DRIVE['cy'] + (DRIVE['ry'] + 9.0) * math.sin(a)
        if (y > HOUSE_Y - 52 and abs(x) < 46) or (y < HOUSE_Y - 122 and abs(x) < 20):
            continue
        add('jac' if (deg // 12) % 3 else 'small', x + rng.uniform(-3, 3), y + rng.uniform(-3, 3), rng.uniform(18, 26))
        if deg % 24 == 0:
            add('jac' if (deg // 12) % 2 else 'small', DRIVE['cx'] + (DRIVE['rx'] + 26.0) * math.cos(a) + rng.uniform(-4, 4),
                DRIVE['cy'] + (DRIVE['ry'] + 24.0) * math.sin(a) + rng.uniform(-4, 4), rng.uniform(20, 28))
    # the magnolias by the south portico and the trees of the Rose and Kennedy gardens
    for (x, y, h, sp) in ((17.5, -6.5, 12.5, 'jac'), (31, -9.5, 15, 'jac'), (41, -4.0, 9, 'small'), (50, -11.0, 12, 'small')):      # dense dark crowns next to the portico, like the real magnolias
        for s in (-1, 1):
            add(sp, s * x + (1.5 if s > 0 else 0), HOUSE_Y + y + (0.8 if s > 0 else 0), h * rng.uniform(0.9, 1.05))
    # in front of the wings
    for (x, y, h, sp) in ((72, -30, 19, 'jac'), (90, -34, 23, 'jac'), (103, -29, 18, 'small'), (82, -46, 21, 'jac')):
        for s in (-1, 1):
            add(sp, s * x + rng.uniform(-2, 2), HOUSE_Y + y + rng.uniform(-2, 2), h * rng.uniform(0.9, 1.1))
    # the north grounds behind the house
    for k in range(22):
        add('jac' if k % 3 else 'small', -116 + k * 11.0 + rng.uniform(-2, 2), HOUSE_Y + 46 + rng.uniform(0, 12), rng.uniform(22, 29))
    # the lower lawn: either side of the fountain, and round its southern edge
    for (x, y, h, sp) in ((30, -152, 20, 'jac'), (44, -170, 22, 'jac'), (25, -188, 17, 'small'), (58, -158, 19, 'small'), (62, -184, 23, 'jac')):
        for s in (-1, 1):
            add(sp, s * x + rng.uniform(-2, 2), HOUSE_Y + y + rng.uniform(-3, 3), h * rng.uniform(0.9, 1.1))
    for deg in range(-78, 79, 13):
        a = math.radians(deg)
        x, y = LOWER_LAWN['r'] * math.sin(a), LOWER_LAWN['cy'] - LOWER_LAWN['r'] * math.cos(a)
        if abs(x) < 16:
            continue
        add('jac' if (deg // 13) % 2 else 'small', x + rng.uniform(-2, 2), y + rng.uniform(-2, 2), rng.uniform(18, 25))
    # nothing may stand inside the wings
    def clear(x, y):
        for (x0, x1, y0, y1) in WINGS.values():
            if x0 - 6 < x < x1 + 6 and y0 - 6 < y < y1 + 6:
                return False
        return not (abs(x) < 34 and HOUSE_Y - 2 < y < HOUSE_Y + 44)
    # ... nor on the cross drive: such a tree steps back onto the grass
    def off_drive(t):
        sp, x, y, h, v = t
        if abs(y - CROSS_DRIVE_Y) < 6.0 and abs(x) < 115:
            y = CROSS_DRIVE_Y + (7.0 if y >= CROSS_DRIVE_Y else -7.0)
        return (sp, x, y, h, v)
    return [off_drive(t) for t in T if clear(t[1], t[2])]


TREES = _trees()


def export_json():
    d = dict(kx=KX, ky=KY, kz=KZ, kPersp=K_PERSP, fieldCx=FIELD_CX, fieldCy=FIELD_CY, halfLength=HL, halfWidth=HW,
             goalY0=GOAL_Y0, goalY1=GOAL_Y1, goalH=GOAL_H, goalD=GOAL_D,
             nearX0=NEAR['x0'], nearX1=NEAR['x1'], nearY0=NEAR['y0'], nearY1=NEAR['y1'],
             farCx=FAR['cx'], farCy=FAR['cy'], farSize=FAR['size'],
             sunX=SUN_TRAVEL[0], sunY=SUN_TRAVEL[1], houseY=HOUSE_Y, fountainX=FOUNTAIN[0], fountainY=FOUNTAIN[1],
             monumentX=MONUMENT['x'], monumentY=MONUMENT['y'], monumentH=MONUMENT['h'], monumentBase=MONUMENT['base'],
             views=VIEWS,
             trees=[dict(species=0 if sp == 'jac' else 1, x=x, y=y, h=h, variant=v) for (sp, x, y, h, v) in TREES])
    os.makedirs(UNITY_WORLD, exist_ok=True)
    json.dump(d, open(os.path.join(UNITY_WORLD, 'layout.json'), 'w'), indent=1)
    return d


if __name__ == '__main__':
    d = export_json()
    print('pitch %.2f x %.2f m, goal %.2f wide %.2f high, %d trees' % (2 * HL, 2 * HW, GOAL_Y1 - GOAL_Y0, GOAL_H, len(TREES)))
