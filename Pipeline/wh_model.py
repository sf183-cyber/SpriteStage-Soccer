# -*- coding: utf-8 -*-
"""The White House (Executive Residence) rebuilt from the HABS measured drawings - pure Python, no bpy.

Source: Historic American Buildings Survey DC-37, "The White House", recorded 1988-1992 (public domain),
sheets 2 (site), 4-5 (plans), 33 (south elevation with levels), 44 (west elevation), 47-48 (south elevation
at 1/4" = 1'), 51 (south portico without colonnade).  Levels are the figures written on sheet 33; plan
dimensions are the dimension strings of sheets 4-5; everything else was scaled off the drawings
(71.8 px per metre on the 3840 px scans, so good to a few centimetres).

Local coordinates: x = east (0 on the centre line), y = north (0 on the face of the south wall above the
ground floor), z = up (0 = lawn at the south front), metres.  build() returns two sheets:
'main' gets its lighting baked into a texture, 'detail' (balusters, railings, glazing bars...) into vertex colours."""
import math
import numpy as np
from wh_meshlib import MeshBuilder, Path, Frame, v2, arc_points, unit

FT, IN = 0.3048, 0.0254
# ---- levels (sheet 33)
Z1 = 12 * FT + 8.5 * IN                 # first (State) floor above the south lawn      3.874
Z2 = Z1 + 20 * FT + 11.5 * IN           # second floor                                  10.262
ZE = Z1 + 34 * FT + 10.25 * IN          # underside of the entablature                  14.498
ZTOP = ZE + 12 * FT + 7 * IN            # top of the roof balustrade                    18.333
ZPROM = Z1 + 42 * FT                    # roof promenade behind the balustrade          16.676
ZROOF = Z1 + 55 * FT + 2.5 * IN         # top of roof                                   20.702
# ---- plan (sheets 4, 5)
HALF = 25.39                            # half length of the upper wall (168 ft 1 1/4 in overall incl. ground-floor projection)
G_OUT = 0.23                            # the ground floor stands this far proud of the wall above
DEPTH = 85 * FT + 5.5 * IN              # 26.05
BOW_C, BOW_R = v2(0.0, 2.07), 5.05      # the bow of the oval rooms: centre (behind the wall face) and radius
BOW_A = math.degrees(math.asin(4.58 / BOW_R))
POR_C = v2(0.0, 1.385)                  # centre of the south portico's circles
POR_R_COL, POR_R_FLOOR = 8.60, 9.35     # ring of the six columns, edge of the portico floor
COL_ANGLES = (59.7, 35.7, 11.7)         # degrees from the south axis (mirrored)
RET_X = 7.86                            # where the portico entablature returns to the wall
# ---- bays (sheet 47): centres of windows and pilasters, west half (mirrored)
WIN_X = (22.63, 18.61, 14.58, 10.47)
PORTICO_BAY_X = 6.50
PIL_X = (24.68, 20.63, 16.60, 12.56, 8.38)
BOW_WIN_A = 43.3                        # the two side windows of the bow, degrees from the axis
BOW_PIL_A = (22.3,)                     # pilasters on the bow between the windows
# ---- entablature / parapet heights above ZE (sheet 47)
E_ARCH, E_FRIEZE, E_CORN, E_PAR = 0.53, 1.06, 1.83, 2.61
E_TOP = ZTOP - ZE                       # 3.835

# materials
WALL, TRIM, GLASS, IRON, ROOF, PAVE, DARK, RED, BLUE, WHITE_FLAG, HEDGE, LAMP, WATER, FLOWER, SPRAY, STONE, SHADE, DRIVE, AWNING = range(19)
MATERIAL_NAMES = ['wall', 'trim', 'glass', 'iron', 'roof', 'paving', 'dark', 'flag_red', 'flag_blue', 'flag_white', 'hedge', 'lamp', 'water', 'flower', 'spray', 'stone', 'shade', 'drive', 'awning']

# ---- grounds (sheet 2, site plan; distances south of the south wall)
FOUNTAIN_SOUTH = 161.4                  # centre of the South Lawn fountain
FOUNTAIN_R = 5.9                        # radius of its basin


def polar(c, r, a_deg, side=1):
    """Point at angle a (from the south axis, positive = west when side = 1, east when side = -1)."""
    a = math.radians(a_deg)
    return v2(c[0] - side * r * math.sin(a), c[1] - r * math.cos(a))


def radial_frame(c, r, a_deg, side=1, z=0.0):
    p = polar(c, r, a_deg, side)
    return Frame(p, p - c, z)


# ----------------------------------------------------------------------------------------------- paths
def south_wall_path():
    """Face of the upper south wall incl. the bow, west to east."""
    pts = [v2(-HALF, 0.0)] + arc_points(BOW_C, BOW_R, BOW_A, -BOW_A, 28) + [v2(HALF, 0.0)]
    return Path(pts)


def ground_south_path():
    """Face of the ground floor on the south: flank walls and the round base of the portico."""
    yj = -G_OUT
    a = math.degrees(math.acos((POR_C[1] - yj) / POR_R_FLOOR))
    pts = [v2(-HALF - G_OUT, yj)] + arc_points(POR_C, POR_R_FLOOR, a, -a, 56) + [v2(HALF + G_OUT, yj)]
    return Path(pts), a


def crown_path():
    """The line of the entablature, closed: along the walls, stepping out round the portico."""
    r = POR_R_COL + 0.42
    a = math.degrees(math.asin(RET_X / r))
    pts = [v2(-HALF, 0.0), v2(-RET_X, 0.0)] + arc_points(POR_C, r, a, -a, 40) + [v2(RET_X, 0.0), v2(HALF, 0.0), v2(HALF, DEPTH), v2(-HALF, DEPTH)]
    return Path(pts, closed=True), r, a


def subpath(path, ua, ub):
    pts = [path.at(ua)[0]] + [path.p[i] for i in range(len(path.p)) if ua + 1e-6 < path.s[i] < ub - 1e-6] + [path.at(ub)[0]]
    return Path(pts)


def free_intervals(length, blocked, margin=0.0):
    out, u = [], 0.0
    for a, b in sorted(blocked):
        if a - margin > u + 1e-6: out.append((u, a - margin))
        u = max(u, b + margin)
    if length > u + 1e-6: out.append((u, length))
    return out


# ----------------------------------------------------------------------------------------------- pieces
def glazing(D, fr, w, z0, z1, depth, cols=3, rows=4, door=False, shade=True):
    """Sash frame and glazing bars just in front of the glass (which sits at `depth` behind the wall face)."""
    b0, b1 = -depth + 0.005, -depth + 0.06
    t = 0.075
    D.fbox(fr, -w / 2, -w / 2 + t, b0, b1, z0, z1, TRIM, skip=('back',))
    D.fbox(fr, w / 2 - t, w / 2, b0, b1, z0, z1, TRIM, skip=('back',))
    D.fbox(fr, -w / 2, w / 2, b0, b1, z1 - t, z1, TRIM, skip=('back',))
    D.fbox(fr, -w / 2, w / 2, b0, b1, z0, z0 + t, TRIM, skip=('back',))
    zb = z0
    if door:                                     # panelled lower part of the French doors
        zb = z0 + 0.62
        D.fbox(fr, -w / 2 + t, w / 2 - t, b0, b0 + 0.03, z0 + t, zb, TRIM, skip=('back',))
    m = 0.032
    for i in range(1, cols):
        a = -w / 2 + i * w / cols
        D.fbox(fr, a - m / 2, a + m / 2, b0, b1 - 0.02, zb, z1 - t, TRIM, skip=('back', 'top', 'bottom'))
    for j in range(1, rows):
        c = zb + j * (z1 - t - zb) / rows
        D.fbox(fr, -w / 2 + t, w / 2 - t, b0, b1 - 0.02, c - m / 2, c + m / 2, TRIM, skip=('back', 'left', 'right'))
    # the meeting rail of the sashes is a little heavier
    c = zb + (rows // 2) * (z1 - t - zb) / rows
    D.fbox(fr, -w / 2 + t, w / 2 - t, b0, b1, c - 0.03, c + 0.03, TRIM, skip=('back', 'left', 'right'))
    if shade:
        # what one sees through the glass from outside: a blind drawn part of the way down, curtains at the sides
        h = abs(math.sin(fr.p[0] * 12.9898 + fr.p[1] * 78.233 + z0 * 37.719) * 43758.5453) % 1.0
        drop = (0.20 + 0.28 * h) * (z1 - zb)
        D.quad(fr.pt(-w / 2 + t, b0 - 0.002, z1 - t - drop), fr.pt(w / 2 - t, b0 - 0.002, z1 - t - drop), fr.pt(w / 2 - t, b0 - 0.002, z1 - t), fr.pt(-w / 2 + t, b0 - 0.002, z1 - t), SHADE)
        cw = (0.22 + 0.10 * ((h * 7.3) % 1.0)) * w
        for sgn in (-1, 1):
            a0, a1 = sorted((sgn * (w / 2 - t), sgn * (w / 2 - t - cw)))
            D.quad(fr.pt(a0, b0 - 0.003, zb + 0.05), fr.pt(a1, b0 - 0.003, zb + 0.05), fr.pt(a1, b0 - 0.003, z1 - t - drop), fr.pt(a0, b0 - 0.003, z1 - t - drop), SHADE)


def pediment(M, fr, kind, w=2.89, z=9.15, rise=0.75, proj=0.38):
    """State-floor window head: cornice on two consoles with a triangular ('T') or segmental ('A') pediment."""
    h = 0.13
    M.fbox(fr, -w / 2, w / 2, 0.0, proj, z, z + h, TRIM)                                 # horizontal cornice
    M.fbox(fr, -w / 2 + 0.30, w / 2 - 0.30, 0.0, 0.10, z - 0.34, z, TRIM, skip=('back',))  # frieze under it
    for s in (-1, 1):                                                                   # consoles
        a = s * 1.15
        M.fbox(fr, a - 0.13, a + 0.13, 0.0, 0.26, z - 0.75, z, TRIM, skip=('back', 'top'))
        M.fbox(fr, a - 0.11, a + 0.11, 0.0, 0.15, z - 1.43, z - 0.75, TRIM, skip=('back', 'top'))
    zb = z + h
    n = 14
    if kind == 'T':
        outer = [(-w / 2, zb), (0.0, zb + rise), (w / 2, zb)]
    else:
        # circular segment through the two ends and the crown
        R = (rise * rise + (w / 2) ** 2) / (2 * rise)
        outer = []
        for i in range(n + 1):
            a = -w / 2 + w * i / n
            outer.append((a, zb + rise - R + math.sqrt(max(R * R - a * a, 0.0))))
    t = 0.14                                                                             # raking / curved cornice
    inner = []
    for (a, c) in outer:
        k = max(0.0, 1.0 - t * 2.2 / max(w / 2, 1e-6))
        inner.append((a * k, zb + (c - zb) * k - (0.0 if abs(a) < w / 2 - 1e-6 else 0.0)))
    # tympanum (recessed) and the moulded edge
    tym = [fr.pt(a, 0.10, c) for (a, c) in outer]
    M.poly(list(reversed([fr.pt(-w / 2, 0.10, zb)] + tym[1:-1] + [fr.pt(w / 2, 0.10, zb)])), TRIM)
    for i in range(len(outer) - 1):
        (a0, c0), (a1, c1) = outer[i], outer[i + 1]
        (b0, d0), (b1, d1) = inner[i], inner[i + 1]
        # top (sloping) face, front face of the moulding, soffit
        M.quad(fr.pt(a0, proj, c0), fr.pt(a1, proj, c1), fr.pt(a1, 0.0, c1), fr.pt(a0, 0.0, c0), TRIM)
        M.quad(fr.pt(b0, proj, d0), fr.pt(b1, proj, d1), fr.pt(a1, proj, c1), fr.pt(a0, proj, c0), TRIM)
        M.quad(fr.pt(b0, 0.10, d0), fr.pt(b1, 0.10, d1), fr.pt(b1, proj, d1), fr.pt(b0, proj, d0), TRIM)


def state_window(M, D, fr, kind, door=False, w=1.68):
    """Dressings of a State-floor opening (the hole and the glass are made by the wall)."""
    z0, z1 = (Z1 + 0.06 if door else 4.86), 8.62
    a = 0.24
    for s in (-1, 1):                                                                    # architrave
        M.fbox(fr, s * w / 2 if s > 0 else -w / 2 - a, w / 2 + a if s > 0 else -w / 2, 0.0, 0.08, z0, z1 + a, TRIM, skip=('back',))
    M.fbox(fr, -w / 2, w / 2, 0.0, 0.08, z1, z1 + a, TRIM, skip=('back', 'left', 'right'))
    pediment(M, fr, kind)
    if not door:
        M.fbox(fr, -1.10, 1.10, 0.0, 0.20, 4.65, 4.86, TRIM, skip=('back',))                 # sill
        for s in (-1, 1):
            M.fbox(fr, s * 0.92 - 0.11, s * 0.92 + 0.11, 0.0, 0.13, 4.33, 4.65, TRIM, skip=('back', 'top'))
    glazing(D, fr, w, z0, z1, 0.25, cols=3, rows=6 if door else 5, door=door)


def second_window(M, D, fr, w=1.68):
    z0, z1 = 10.95, 13.88
    a = 0.22
    for s in (-1, 1):
        M.fbox(fr, s * w / 2 if s > 0 else -w / 2 - a, w / 2 + a if s > 0 else -w / 2, 0.0, 0.07, z0, z1, TRIM, skip=('back',))
        # the "ears" at the head and the foot of the frame
        M.fbox(fr, s * (w / 2 + a) if s > 0 else -w / 2 - a - 0.08, w / 2 + a + 0.08 if s > 0 else -w / 2 - a, 0.0, 0.07, z1 - 0.30, z1 + a, TRIM, skip=('back',))
    M.fbox(fr, -w / 2 - a, w / 2 + a, 0.0, 0.07, z1, z1 + a, TRIM, skip=('back',))
    M.fbox(fr, -1.12, 1.12, 0.0, 0.18, 10.74, 10.95, TRIM, skip=('back',))                   # sill on two brackets
    for s in (-1, 1):
        M.fbox(fr, s * 0.95 - 0.11, s * 0.95 + 0.11, 0.0, 0.14, 10.38, 10.74, TRIM, skip=('back', 'top'))
    glazing(D, fr, w, z0, z1, 0.25, cols=3, rows=4)


def pilaster(M, fr, width=0.79):
    """Giant Ionic pilaster on its pedestal (fr at the wall face, z = 0)."""
    M.fbox(fr, -0.635, 0.635, 0.0, 0.30, Z1 + 0.10, 4.86, WALL, skip=('back',))              # pedestal
    M.fbox(fr, -0.66, 0.66, 0.0, 0.33, 4.78, 4.86, TRIM, skip=('back',))
    M.fbox(fr, -0.50, 0.50, 0.0, 0.27, 4.86, 5.02, TRIM, skip=('back',))                     # base
    M.fbox(fr, -0.455, 0.455, 0.0, 0.235, 5.02, 5.31, TRIM, skip=('back',))
    zc = ZE - 0.46
    M.fbox(fr, -width / 2, width / 2, 0.0, 0.18, 5.31, zc, WALL, skip=('back', 'top', 'bottom'))  # shaft
    ionic_capital(M, fr, zc, half=0.43, depth0=0.0, depth1=0.24)


def ionic_capital(M, fr, zc, half, depth0, depth1):
    """Echinus, the two scrolls and the abacus.  The capital occupies zc .. ZE."""
    M.fbox(fr, -half, half, depth0, depth1 - 0.02, zc, zc + 0.26, TRIM, skip=('back',) if depth0 == 0.0 else ())
    for s in (-1, 1):
        M.hcyl(fr, s * (half + 0.10), depth0, depth1, zc + 0.19, 0.20, TRIM, seg=14)
        M.hcyl(fr, s * (half + 0.10), depth1, depth1 + 0.02, zc + 0.19, 0.09, TRIM, seg=10)   # the eye of the scroll
    M.fbox(fr, -half - 0.16, half + 0.16, depth0 - (0.03 if depth0 else 0.0), depth1 + 0.03, zc + 0.33, zc + 0.46, TRIM, skip=('back',) if depth0 == 0.0 else ())
    M.fbox(fr, -half - 0.05, half + 0.05, depth0, depth1, zc + 0.26, zc + 0.33, TRIM, skip=('back',) if depth0 == 0.0 else ())


def column(M, c, a_deg, side):
    """One of the six Ionic columns of the south portico on its pedestal."""
    fr = radial_frame(c, POR_R_COL, a_deg, side)
    p = fr.p
    zp = Z1 + 1.03
    M.fbox(fr, -0.665, 0.665, -0.665, 0.665, Z1, zp - 0.10, WALL)                            # pedestal
    M.fbox(fr, -0.70, 0.70, -0.70, 0.70, zp - 0.10, zp, TRIM)
    M.lathe(p[0], p[1], [(0.62, zp), (0.62, zp + 0.10), (0.58, zp + 0.13), (0.55, zp + 0.16), (0.58, zp + 0.22), (0.53, zp + 0.27),
                         (0.50, zp + 0.31), (0.52, zp + 0.35), (0.475, zp + 0.38)], TRIM, seg=24, caps=(False, False))
    zc = ZE - 0.50
    prof = []
    for i in range(13):                                                                  # shaft with entasis: 0.92 m -> 0.80 m
        t = i / 12.0
        prof.append((0.46 - 0.06 * (t ** 1.7), zp + 0.38 + (zc - zp - 0.38) * t))
    M.lathe(p[0], p[1], prof, WALL, seg=28, caps=(False, False))
    M.cyl(p[0], p[1], zc, zc + 0.26, 0.42, TRIM, seg=24)
    for s in (-1, 1):
        M.hcyl(fr, s * 0.52, -0.42, 0.42, zc + 0.19, 0.20, TRIM, seg=14)
    M.fbox(fr, -0.58, 0.58, -0.47, 0.47, zc + 0.26, zc + 0.36, TRIM)
    M.fbox(fr, -0.62, 0.62, -0.50, 0.50, zc + 0.36, zc + 0.50, TRIM)


BALUSTER = [(0.085, 0.00), (0.085, 0.05), (0.055, 0.08), (0.075, 0.13), (0.100, 0.24), (0.092, 0.33), (0.050, 0.47),
            (0.042, 0.56), (0.060, 0.60), (0.045, 0.64), (0.070, 0.70), (0.085, 0.73), (0.085, 0.78)]


def balustrade(M, D, path, zb, zt, piers_u, o0=-0.32, o1=0.10, skip=()):
    """Stone balustrade along a path: plinth and coping (main sheet), piers at piers_u, turned balusters between them."""
    plinth = [(o0, zb), (o1, zb), (o1, zb + 0.21), (o0, zb + 0.21)]
    cope = [(o0 - 0.02, zt - 0.22), (o1 + 0.03, zt - 0.22), (o1 + 0.03, zt), (o0 - 0.02, zt)]
    M.sweep(path, plinth, TRIM)
    M.sweep(path, cope, TRIM)
    length = path.length
    pw = 0.52
    edges = []
    for u in sorted(piers_u):
        p, n, t = path.at(u % length) if not path.closed else _closed_at(path, u)
        fr = Frame(p, n)
        M.fbox(fr, -pw, pw, o0, o1, zb + 0.21, zt - 0.22, TRIM)
        edges.append(u)
    hb = zt - 0.22 - (zb + 0.21)
    prof = [(r, zb + 0.21 + z * hb / 0.78) for r, z in BALUSTER]
    us = sorted(edges)
    for k in range(len(us) - (0 if path.closed else 1)):
        a = us[k] + pw
        b = (us[(k + 1) % len(us)] + (length if k + 1 >= len(us) else 0.0)) - pw
        if b - a < 0.4 or any(sa <= (a + b) / 2 % length <= sb for sa, sb in skip): continue
        n = max(1, int(round((b - a) / 0.34)))
        for i in range(n):
            u = a + (i + 0.5) * (b - a) / n
            p, nn, t = _closed_at(path, u) if path.closed else path.at(u)
            q = p + nn * ((o0 + o1) / 2)
            D.lathe(q[0], q[1], prof, TRIM, seg=8, caps=(False, False))


def _closed_at(path, u):
    """Point / normal / tangent on a closed path at arc length u."""
    n = len(path.p)
    u = u % path.length
    s = path.s + [path.length]
    i = max(0, min(n - 1, int(np.searchsorted(s, u, side='right')) - 1))
    t = (u - s[i]) / max(s[i + 1] - s[i], 1e-9)
    p0, p1 = path.p[i], path.p[(i + 1) % n]
    p = p0 * (1 - t) + p1 * t
    m = path.seg_n[i]
    return p, m, np.array([-m[1], m[0]])


def u_on_closed(path, pt):
    """Arc length of the point of a closed path nearest to pt."""
    best, bu = 1e18, 0.0
    n = len(path.p)
    s = path.s + [path.length]
    for i in range(n):
        a, b = path.p[i], path.p[(i + 1) % n]
        d = b - a; L2 = float(d @ d)
        t = 0.0 if L2 < 1e-12 else min(1.0, max(0.0, float((pt - a) @ d) / L2))
        q = a + d * t
        dist = float(np.linalg.norm(q - pt))
        if dist < best: best, bu = dist, s[i] + t * (s[i + 1] - s[i])
    return bu


def iron_rail(D, pts, height=0.92, spacing=0.125, newel=True):
    """Wrought-iron railing along a 3D polyline (the line of the floor / the nosings)."""
    pts = [np.array(p, float) for p in pts]
    top = [p + np.array([0, 0, height]) for p in pts]
    low = [p + np.array([0, 0, 0.10]) for p in pts]
    for a, b in zip(top[:-1], top[1:]): D.tube(a, b, 0.028, IRON, seg=6)
    for a, b in zip(low[:-1], low[1:]): D.tube(a, b, 0.016, IRON, seg=4)
    mid = [p + np.array([0, 0, height - 0.13]) for p in pts]
    for a, b in zip(mid[:-1], mid[1:]): D.tube(a, b, 0.014, IRON, seg=4)
    # pickets, evenly along the whole run
    seg_len = [float(np.linalg.norm(pts[i + 1] - pts[i])) for i in range(len(pts) - 1)]
    total = sum(seg_len)
    n = max(1, int(total / spacing))
    acc, i = 0.0, 0
    for k in range(n + 1):
        u = total * k / n
        while i < len(seg_len) - 1 and u > acc + seg_len[i] + 1e-9:
            acc += seg_len[i]; i += 1
        t = (u - acc) / max(seg_len[i], 1e-9)
        p = pts[i] * (1 - t) + pts[i + 1] * t
        r = 0.022 if (newel and k in (0, n)) else 0.010
        D.tube(p + np.array([0, 0, 0.02]), p + np.array([0, 0, height + (0.06 if r > 0.02 else 0.0)]), r, IRON, seg=4)


def ring_strip(c, r_out, a0, a1, n, wall_path):
    """Pairs (inner, outer) for radial strips between the building's south wall (incl. the bow) and a circle round c."""
    out = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        o = polar(c, r_out, a)
        d = unit(c - o)
        # walk from the outer point towards the centre until the wall is hit
        best = None
        for k in range(len(wall_path.p) - 1):
            p, q = wall_path.p[k], wall_path.p[k + 1]
            e = q - p
            den = d[0] * (-e[1]) - d[1] * (-e[0])
            if abs(den) < 1e-12: continue
            w = p - o
            t = (w[0] * (-e[1]) - w[1] * (-e[0])) / den
            s = (d[0] * w[1] - d[1] * w[0]) / den
            if t > 0 and -1e-9 <= s <= 1 + 1e-9 and (best is None or t < best): best = t
        inner = o + d * (best if best is not None else r_out)
        out.append((inner, o))
    return out


def arc_band(M, fr, a_c, z_c, r0, r1, b, m, a0=0.0, a1=180.0, n=18, depth=0.0):
    """Flat moulded band following an arch (archivolt): between radii r0 and r1 about (a_c, z_c), standing b proud."""
    for k in range(n):
        t0, t1 = math.radians(a0 + (a1 - a0) * k / n), math.radians(a0 + (a1 - a0) * (k + 1) / n)
        p = lambda r, t, bb: fr.pt(a_c + r * math.cos(t), bb, z_c + r * math.sin(t))
        M.quad(p(r0, t1, b), p(r1, t1, b), p(r1, t0, b), p(r0, t0, b), m)                # face
        M.quad(p(r1, t1, b), p(r1, t1, depth), p(r1, t0, depth), p(r1, t0, b), m)        # outer edge
        M.quad(p(r0, t0, b), p(r0, t0, depth), p(r0, t1, depth), p(r0, t1, b), m)        # inner edge


def venetian(M, D, fr):
    """Middle bay of the east and west fronts: arched door between two side lights, and the lunette over it."""
    for a in (-2.08, -0.965, 0.965, 2.08):                                              # the four little pilasters
        M.fbox(fr, a - 0.16, a + 0.16, 0.0, 0.16, Z1 + 0.10, 8.34, TRIM, skip=('back',))
        M.fbox(fr, a - 0.20, a + 0.20, 0.0, 0.20, 8.34, 8.46, TRIM, skip=('back',))
        M.fbox(fr, a - 0.20, a + 0.20, 0.0, 0.20, Z1 + 0.10, Z1 + 0.30, TRIM, skip=('back',))
    for sg in (-1, 1):                                                                  # entablature, broken by the arch
        a0, a1 = sorted((sg * 0.80, sg * 2.34))
        M.fbox(fr, a0, a1, 0.0, 0.24, 8.46, 8.78, TRIM, skip=('back',))
    arc_band(M, fr, 0.0, 8.52, 0.715, 0.96, 0.12, TRIM)
    arc_band(M, fr, 0.0, 8.52, 1.02, 1.22, 0.26, TRIM)
    M.fbox(fr, -0.11, 0.11, 0.0, 0.30, 9.20, 9.62, TRIM, skip=('back',))
    glazing(D, fr, 1.43, Z1 + 0.06, 8.54, 0.25, cols=2, rows=5, door=True, shade=False)
    for k in range(1, 8):
        ang = math.pi * k / 8
        D.tube(fr.pt(0.0, -0.23, 8.52), fr.pt(0.68 * math.cos(ang), -0.23, 8.52 + 0.68 * math.sin(ang)), 0.014, TRIM, seg=4)
    for dd in (-1.57, 1.57):
        glazing(D, fr.shifted(dd), 0.72, 4.95, 8.40, 0.25, cols=2, rows=5, shade=False)
    # lunette
    zl = 10.96
    arc_band(M, fr, 0.0, zl, 1.90, 2.20, 0.10, TRIM)
    M.fbox(fr, -0.13, 0.13, 0.0, 0.22, zl + 1.86, zl + 2.42, TRIM, skip=('back',))
    M.fbox(fr, -2.38, 2.38, 0.0, 0.20, zl - 0.22, zl, TRIM, skip=('back',))
    for sg in (-1, 1):
        M.fbox(fr, sg * 2.1 - 0.11, sg * 2.1 + 0.11, 0.0, 0.14, zl - 0.58, zl - 0.22, TRIM, skip=('back', 'top'))
    for k in range(1, 12):
        ang = math.pi * k / 12
        D.tube(fr.pt(0.95 * math.cos(ang), -0.23, zl + 0.95 * math.sin(ang)), fr.pt(1.86 * math.cos(ang), -0.23, zl + 1.86 * math.sin(ang)), 0.016, TRIM, seg=4)
    for r in (0.95, 1.86):
        for k in range(16):
            t0, t1 = math.pi * k / 16, math.pi * (k + 1) / 16
            D.tube(fr.pt(r * math.cos(t0), -0.23, zl + r * math.sin(t0)), fr.pt(r * math.cos(t1), -0.23, zl + r * math.sin(t1)), 0.022, TRIM, seg=4)
    D.fbox(fr, -1.88, 1.88, -0.245, -0.19, zl, zl + 0.07, TRIM, skip=('back',))
    for a in (-0.95, 0.0, 0.95):
        D.fbox(fr, a - 0.02, a + 0.02, -0.245, -0.20, zl, zl + (0.95 if a else 0.95), TRIM, skip=('back',))


def shrub(M, x, y, rx, ry, h, seed=0, m=None, rings=11, seg=24):
    """Clipped evergreen: a lumpy dome."""
    m = HEDGE if m is None else m
    def bump(i, j):
        return 1.0 + 0.07 * math.sin(i * 1.3 + seed * 1.7) * math.cos(j * 1.05 + seed) + 0.04 * math.sin(i * 2.9 + j * 2.1 + seed * 0.3) + 0.025 * math.sin(j * 4.7 + i * 3.3 + seed)
    P = []
    for i in range(rings + 1):
        t = i / rings                                  # 0 = ground, 1 = top
        ring = []
        k = math.sqrt(max(1.0 - (max(t - 0.25, 0.0) / 0.75) ** 2, 0.0)) if t > 0.25 else 0.86 + 0.14 * (t / 0.25)
        for j in range(seg):
            a = 2 * math.pi * j / seg
            b = bump(i, j)
            ring.append((x + rx * k * b * math.cos(a), y + ry * k * b * math.sin(a), h * t * (0.94 + 0.06 * b)))
        P.append(ring)
    for i in range(rings):
        for j in range(seg):
            j2 = (j + 1) % seg
            if i == rings - 1:
                M.poly([P[i][j], P[i][j2], (x, y, h)], m)
            else:
                M.quad(P[i][j], P[i][j2], P[i + 1][j2], P[i + 1][j], m)


def sector_block(M, c, r0, r1, a0, a1, z0, z1, m, side=1, n=None, skip=()):
    """Solid annular sector (steps, landings).  Faces: top, outer, inner, start (at a0), end (at a1)."""
    n = n or max(1, int(abs(a1 - a0) / 4.0))
    A = [polar(c, r0, a0 + (a1 - a0) * i / n, side) for i in range(n + 1)]
    B = [polar(c, r1, a0 + (a1 - a0) * i / n, side) for i in range(n + 1)]
    ccw = (a1 > a0) != (side > 0)                 # does the sector advance counter-clockwise seen from above?

    def q(p, z): return (float(p[0]), float(p[1]), z)

    def face(pts):
        M.poly(pts if ccw else list(reversed(pts)), m)
    for i in range(n):
        j = i + 1
        if 'top' not in skip: face([q(A[i], z1), q(B[i], z1), q(B[j], z1), q(A[j], z1)])
        if 'outer' not in skip: face([q(B[i], z0), q(B[j], z0), q(B[j], z1), q(B[i], z1)])
        if 'inner' not in skip: face([q(A[j], z0), q(A[i], z0), q(A[i], z1), q(A[j], z1)])
    if 'start' not in skip: face([q(A[0], z0), q(B[0], z0), q(B[0], z1), q(A[0], z1)])
    if 'end' not in skip: face([q(B[n], z0), q(A[n], z0), q(A[n], z1), q(B[n], z1)])


# ----------------------------------------------------------------------------------------------- the house
def build():
    M, D = MeshBuilder('main'), MeshBuilder('detail')
    wall_path = south_wall_path()
    ux = lambda x: wall_path.u_of_x(x)
    u_bow0 = HALF - 4.58
    bow_u = lambda a_deg: u_bow0 + math.radians(BOW_A - a_deg) * BOW_R          # a from the axis, positive = west

    # ---------------- upper south wall with its openings
    ops, frames_state, frames_second = [], [], []
    kinds = {}
    seq = [(-x, 'f') for x in WIN_X] + [(-PORTICO_BAY_X, 'p')] + [(BOW_WIN_A, 'b'), (0.0, 'b'), (-BOW_WIN_A, 'b')] + [(PORTICO_BAY_X, 'p')] + [(x, 'f') for x in reversed(WIN_X)]
    for i, (v, where) in enumerate(seq):
        u = bow_u(v) if where == 'b' else ux(v)
        kind = 'A' if i % 2 == 0 else 'T'                       # pediments alternate, starting with a segmental one at the west end
        door = where == 'p' or (where == 'b' and v == 0.0) or (where == 'f' and abs(abs(v) - WIN_X[1]) < 1e-6)
        w = 1.68
        ops.append(dict(u0=u - w / 2, u1=u + w / 2, z0=(Z1 + 0.06 if door else 4.86), z1=8.62, depth=0.25, back=GLASS))
        ops.append(dict(u0=u - w / 2, u1=u + w / 2, z0=10.95, z1=13.88, depth=0.25, back=GLASS))
        p, n, t = wall_path.at(u)
        frames_state.append((Frame(p, n), kind, door, where, v))
    M.wall(wall_path, Z1 - 0.05, ZE + 0.05, WALL, ops, m_reveal=TRIM)
    for fr, kind, door, where, v in frames_state:
        state_window(M, D, fr, kind, door)
        second_window(M, D, fr)
        if where == 'f' and door:                               # the little iron balcony of the second bay
            fl = fr.shifted(0, 0, 0)
            M.fbox(fl, -1.13, 1.13, 0.0, 0.55, Z1 + 0.02, Z1 + 0.12, TRIM, skip=('back',))
            iron_rail(D, [fl.pt(-1.10, 0.02, Z1 + 0.12), fl.pt(-1.10, 0.52, Z1 + 0.12), fl.pt(1.10, 0.52, Z1 + 0.12), fl.pt(1.10, 0.02, Z1 + 0.12)])
    # dado course along the wall, broken at the doors
    blocked = [(o['u0'] - 0.26, o['u1'] + 0.26) for o in ops if o['z0'] < 4.5]
    for a, b in free_intervals(wall_path.s[-1], blocked):
        M.sweep(subpath(wall_path, a, b), [(0.0, Z1 - 0.02), (0.10, Z1 - 0.02), (0.10, Z1 + 0.42), (0.0, Z1 + 0.42)], TRIM)
    # pilasters: flanks, the junction of wall and bow, and on the bow
    for s in (-1, 1):
        for x in PIL_X:
            pilaster(M, Frame((s * x, 0.0), (0.0, -1.0)))
        for a in (BOW_A - 3.0,) + BOW_PIL_A:
            pilaster(M, radial_frame(BOW_C, BOW_R, a, s), width=0.72)

    # ---------------- side walls of the upper storeys (sheet 44): two windows either side of the middle bay,
    # which has a Venetian window on the State floor and a big lunette above
    for s in (-1, 1):
        side = Path([v2(s * HALF, 0.0), v2(s * HALF, DEPTH)] if s > 0 else [v2(-HALF, DEPTH), v2(-HALF, 0.0)])
        U = (lambda y: y) if s > 0 else (lambda y: DEPTH - y)
        sops, sfr = [], []
        for y, kind in ((2.93, 'A'), (7.42, 'T'), (DEPTH - 7.42, 'T'), (DEPTH - 2.93, 'A')):
            u = U(y)
            sops.append(dict(u0=u - 0.84, u1=u + 0.84, z0=4.86, z1=8.62, depth=0.25, back=GLASS))
            sops.append(dict(u0=u - 0.84, u1=u + 0.84, z0=10.95, z1=13.88, depth=0.25, back=GLASS))
            sfr.append((Frame((s * HALF, y), (s, 0.0)), kind))
        uc = U(DEPTH / 2)
        sops.append(dict(u0=uc - 0.715, u1=uc + 0.715, z0=Z1 + 0.06, z1=8.52, depth=0.25, arch=True, back=GLASS))
        for dd in (-1.57, 1.57):
            sops.append(dict(u0=uc + dd - 0.36, u1=uc + dd + 0.36, z0=4.95, z1=8.40, depth=0.25, back=GLASS))
        sops.append(dict(u0=uc - 1.90, u1=uc + 1.90, z0=10.96, z1=10.965, depth=0.25, arch=True, back=GLASS))
        M.wall(side, Z1 - 0.05, ZE + 0.05, WALL, sops, m_reveal=TRIM, arch_steps=16)
        for fr, kind in sfr:
            state_window(M, D, fr, kind)
            second_window(M, D, fr)
        venetian(M, D, Frame((s * HALF, DEPTH / 2), (s, 0.0)))
        M.sweep(side, [(0.0, Z1 - 0.02), (0.10, Z1 - 0.02), (0.10, Z1 + 0.42), (0.0, Z1 + 0.42)], TRIM)
        for y in (0.67, 5.22, 9.60, DEPTH - 9.60, DEPTH - 5.22, DEPTH - 0.67):
            pilaster(M, Frame((s * HALF, y), (s, 0.0)))
    M.strip(Path([v2(HALF, DEPTH), v2(-HALF, DEPTH)]), 0.0, 0.0, ZE + 0.05, WALL)

    # ---------------- ground floor: rusticated flanks and the round base of the portico
    gpath, g_a = ground_south_path()
    gu = lambda x: gpath.u_of_x(x) if abs(x) > 9.3 else None
    u_ring0 = gpath.s[1]
    ring_u = lambda a_deg: u_ring0 + math.radians(g_a - a_deg) * POR_R_FLOOR
    gops = []
    for s in (-1, 1):
        for x in WIN_X:
            u = gu(s * x)
            gops.append(dict(u0=u - 0.76, u1=u + 0.76, z0=1.00, z1=2.70, depth=0.30, back=GLASS, win=True))
    for a in (50.0, 23.0, 0.0, -23.0, -50.0):                   # arched doors and windows of the Diplomatic entrance
        u = ring_u(a)
        gops.append(dict(u0=u - 0.66, u1=u + 0.66, z0=(0.02 if abs(a) < 30 else 0.9), z1=2.34, depth=0.35, arch=True, back=GLASS, ring=True, a=a))
    zg1 = Z1 - 0.21                                             # underside of the belt course
    M.wall(gpath, 0.0, zg1, WALL, gops, o=-0.03, m_reveal=TRIM)
    # the courses of the ashlar: slabs standing 3 cm proud, so the joints read as channels
    course = (zg1 - 0.10) / 11.0
    for k in range(11):
        c0, c1 = 0.10 + k * course + 0.018, 0.10 + (k + 1) * course - 0.018
        blocked = []
        for o in gops:
            top = o['z1'] + ((o['u1'] - o['u0']) / 2 if o.get('arch') else 0.0)
            if o['z0'] < c1 and top > c0:
                if o.get('arch') and c0 > o['z1']:               # inside the arch: chord at this height
                    r = (o['u1'] - o['u0']) / 2; h = min(r, (c0 + c1) / 2 - o['z1'])
                    half = math.sqrt(max(r * r - h * h, 0.0)); mid = (o['u0'] + o['u1']) / 2
                    blocked.append((mid - half, mid + half))
                else:
                    blocked.append((o['u0'], o['u1']))
        for a, b in free_intervals(gpath.s[-1], blocked, margin=0.0):
            if b - a < 0.05: continue
            M.sweep(subpath(gpath, a, b), [(-0.03, c0), (0.0, c0 + 0.012), (0.0, c1 - 0.012), (-0.03, c1)], WALL, cap=True)
    M.sweep(gpath, [(-0.03, 0.0), (0.05, 0.0), (0.05, 0.10), (-0.03, 0.10)], WALL)           # plinth
    M.sweep(gpath, [(-0.03, zg1), (0.09, zg1), (0.12, zg1 + 0.10), (0.12, Z1 - 0.02), (0.06, Z1 + 0.02), (-0.03, Z1 + 0.02)], TRIM)  # belt course / floor edge
    for o in gops:
        mid = (o['u0'] + o['u1']) / 2
        p, n, t = gpath.at(mid)
        fr = Frame(p, n)
        if o.get('win'):
            glazing(D, fr, 1.52, 1.00, 2.70, 0.33, cols=3, rows=3, shade=False)
            M.fbox(fr, -0.95, 0.95, -0.03, 0.06, 0.90, 1.00, TRIM, skip=('back',))             # sill
            # flat arch of splayed voussoirs with a keystone
            pts = [fr.pt(-0.76, 0.035, 2.70), fr.pt(0.76, 0.035, 2.70), fr.pt(1.18, 0.035, 3.40), fr.pt(-1.18, 0.035, 3.40)]
            M.poly(pts, TRIM)
            M.quad(fr.pt(-0.76, -0.03, 2.70), fr.pt(0.76, -0.03, 2.70), fr.pt(0.76, 0.035, 2.70), fr.pt(-0.76, 0.035, 2.70), TRIM)
            M.fbox(fr, -0.16, 0.16, 0.0, 0.07, 2.70, 3.46, TRIM, skip=('back',))
        else:
            door = o['z0'] < 0.5
            glazing(D, fr, 1.32, o['z0'], o['z1'] + 0.02, 0.38, cols=4 if door else 3, rows=4 if door else 3, door=door, shade=False)
            for k in range(1, 8):                                # the fan of the fanlight
                ang = math.pi * k / 8
                D.tube(fr.pt(0.0, -0.36, o['z1']), fr.pt(0.62 * math.cos(ang), -0.36, o['z1'] + 0.62 * math.sin(ang)), 0.014, TRIM, seg=4)
            for k in range(9):                                   # moulded archivolt
                a0, a1 = math.pi * k / 9, math.pi * (k + 1) / 9
                r0, r1 = 0.66, 0.84
                M.quad(fr.pt(r0 * math.cos(a0), 0.03, o['z1'] + r0 * math.sin(a0)), fr.pt(r1 * math.cos(a0), 0.03, o['z1'] + r1 * math.sin(a0)),
                       fr.pt(r1 * math.cos(a1), 0.03, o['z1'] + r1 * math.sin(a1)), fr.pt(r0 * math.cos(a1), 0.03, o['z1'] + r0 * math.sin(a1)), TRIM)
    # sides of the ground floor
    for s in (-1, 1):
        side = Path([v2(s * (HALF + G_OUT), -G_OUT), v2(s * (HALF + G_OUT), DEPTH)] if s > 0 else [v2(-HALF - G_OUT, DEPTH), v2(-HALF - G_OUT, -G_OUT)])
        M.strip(side, 0.0, 0.0, Z1, WALL)
    # lanterns either side of the entrance (black iron), and the white canopy over the door
    for s in (-1, 1):
        fr = radial_frame(POR_C, POR_R_FLOOR, 12.2, s)
        D.fbox(fr, -0.03, 0.03, 0.0, 0.46, 3.40, 3.46, IRON)
        D.fbox(fr, -0.15, 0.15, 0.28, 0.58, 2.76, 3.26, LAMP)
        for (ca, cb) in ((-0.165, 0.265), (0.135, 0.265), (-0.165, 0.565), (0.135, 0.565)):
            D.fbox(fr, ca, ca + 0.03, cb, cb + 0.03, 2.70, 3.30, IRON)
        D.fbox(fr, -0.19, 0.19, 0.24, 0.62, 3.26, 3.32, IRON); D.fbox(fr, -0.17, 0.17, 0.26, 0.60, 2.70, 2.76, IRON)
        D.fbox(fr, -0.11, 0.11, 0.32, 0.54, 3.32, 3.44, IRON); D.fbox(fr, -0.04, 0.04, 0.39, 0.47, 3.44, 3.58, IRON)
    fr = radial_frame(POR_C, POR_R_FLOOR, 0.0, 1)
    hw, dep, zb_, zf_ = 1.55, 2.30, 3.42, 2.78
    n = 8
    prof = [(dep * i / n, zb_ - (zb_ - zf_) * (i / n) ** 1.7) for i in range(n + 1)]
    for (b0, c0), (b1, c1) in zip(prof[:-1], prof[1:]):
        # canvas, facing up (the game draws the house without back-face culling, so the underside shows too)
        M.quad(fr.pt(-hw, b1, c1), fr.pt(hw, b1, c1), fr.pt(hw, b0, c0), fr.pt(-hw, b0, c0), AWNING)
    for sg in (-1, 1):                                                                  # closed sides, facing outwards
        for (b0, c0), (b1, c1) in zip(prof[:-1], prof[1:]):
            q = [fr.pt(sg * hw, b0, zf_ - 0.26), fr.pt(sg * hw, b1, zf_ - 0.26), fr.pt(sg * hw, b1, c1), fr.pt(sg * hw, b0, c0)]
            M.poly(list(reversed(q)) if sg > 0 else q, AWNING)
    M.quad(fr.pt(-hw, dep, zf_ - 0.26), fr.pt(hw, dep, zf_ - 0.26), fr.pt(hw, dep, zf_), fr.pt(-hw, dep, zf_), AWNING)       # valance
    for sg in (-1, 1):
        D.tube(fr.pt(sg * hw, dep - 0.05, 0.0), fr.pt(sg * hw, dep - 0.05, zf_ - 0.2), 0.03, IRON, seg=5)

    # ---------------- entablature, parapet and balustrade all the way round
    crown, r_e, a_e = crown_path()
    ent = [(-0.84, ZE), (0.06, ZE), (0.06, ZE + E_ARCH - 0.06), (0.10, ZE + E_ARCH - 0.04), (0.10, ZE + E_ARCH), (0.0, ZE + E_ARCH),
           (0.0, ZE + E_FRIEZE), (0.10, ZE + E_FRIEZE), (0.14, ZE + E_FRIEZE + 0.20), (0.40, ZE + E_FRIEZE + 0.26), (0.42, ZE + E_FRIEZE + 0.46),
           (0.70, ZE + E_FRIEZE + 0.50), (0.74, ZE + E_FRIEZE + 0.62), (0.80, ZE + E_CORN - 0.03), (0.80, ZE + E_CORN), (-0.84, ZE + E_CORN)]
    M.sweep(crown, ent, TRIM)
    M.sweep(crown, [(-0.84, ZE + E_CORN), (0.10, ZE + E_CORN), (0.10, ZE + E_PAR), (-0.84, ZE + E_PAR)], WALL)
    n_mod = int(crown.length / 0.58)
    for i in range(n_mod):                                      # modillions under the corona
        p, n, t = _closed_at(crown, (i + 0.5) * crown.length / n_mod)
        D.fbox(Frame(p, n), -0.10, 0.10, 0.40, 0.68, ZE + E_FRIEZE + 0.30, ZE + E_FRIEZE + 0.47, TRIM, skip=('back', 'top'))
    n_den = int(crown.length / 0.135)
    for i in range(n_den):                                      # the dentil course
        u = (i + 0.5) * crown.length / n_den
        p, n, t = _closed_at(crown, u)
        if p[1] > DEPTH - 0.5: continue                         # not along the back
        D.fbox(Frame(p, n), -0.036, 0.036, 0.10, 0.165, ZE + E_FRIEZE + 0.07, ZE + E_FRIEZE + 0.17, TRIM, skip=('back', 'top'))
    piers = []
    for s in (-1, 1):
        for x in PIL_X: piers.append(u_on_closed(crown, v2(s * x, 0.0)))
        piers.append(u_on_closed(crown, v2(s * RET_X, -0.3)))
        for a in COL_ANGLES: piers.append(u_on_closed(crown, polar(POR_C, r_e, a, s)))
        for y in (0.67, 5.22, 9.60, 13.0, DEPTH - 9.60, DEPTH - 5.22, DEPTH - 0.67): piers.append(u_on_closed(crown, v2(s * HALF, y)))
    for k in range(13): piers.append(u_on_closed(crown, v2(-24.68 + k * 4.113, DEPTH)))
    balustrade(M, D, crown, ZE + E_PAR, ZTOP, piers)

    # ---------------- the south portico
    strips = ring_strip(POR_C, POR_R_FLOOR - 0.02, 80.0, -80.0, 64, wall_path)
    for (i0, o0), (i1, o1) in zip(strips[:-1], strips[1:]):       # floor
        M.quad((o0[0], o0[1], Z1 + 0.02), (o1[0], o1[1], Z1 + 0.02), (i1[0], i1[1], Z1 + 0.02), (i0[0], i0[1], Z1 + 0.02), PAVE)
    for s in (-1, 1):
        for a in COL_ANGLES: column(M, POR_C, a, s)
    # ceiling of the portico and the roof deck over it: everything between the wall (with its bow) and the inside of the entablature
    r_in, xr = r_e - 0.86, RET_X - 0.86
    def y_front(x): return POR_C[1] - math.sqrt(max(r_in * r_in - x * x, 0.0))
    def y_wall(x): return BOW_C[1] - math.sqrt(BOW_R * BOW_R - x * x) if abs(x) < 4.58 else 0.0
    xs = sorted(set([-xr + 2 * xr * i / 60 for i in range(61)] + [-4.58, 4.58]))
    for xa, xb in zip(xs[:-1], xs[1:]):
        A, B, C, Dq = (xa, y_front(xa)), (xb, y_front(xb)), (xb, y_wall(xb)), (xa, y_wall(xa))
        M.quad(A + (ZPROM,), B + (ZPROM,), C + (ZPROM,), Dq + (ZPROM,), ROOF)
        M.quad(Dq + (ZE + 0.02,), C + (ZE + 0.02,), B + (ZE + 0.02,), A + (ZE + 0.02,), TRIM)
    # Truman balcony (1948): a thin floor between the bow and the columns at second-floor level, iron railing
    zb0, zb1 = Z2 - 0.22, Z2 + 0.06
    rb = POR_R_COL - 0.50
    strips = ring_strip(POR_C, rb, a_e - 2.0, -(a_e - 2.0), 48, wall_path)
    for (i0, o0), (i1, o1) in zip(strips[:-1], strips[1:]):
        M.quad((o0[0], o0[1], zb1), (o1[0], o1[1], zb1), (i1[0], i1[1], zb1), (i0[0], i0[1], zb1), PAVE)
        M.quad((i0[0], i0[1], zb0), (i1[0], i1[1], zb0), (o1[0], o1[1], zb0), (o0[0], o0[1], zb0), TRIM)
        M.quad((o0[0], o0[1], zb0), (o1[0], o1[1], zb0), (o1[0], o1[1], zb1), (o0[0], o0[1], zb1), TRIM)
    for (i, o) in (strips[0], strips[-1]):                       # the two ends of the balcony
        pts = [(i[0], i[1], zb0), (o[0], o[1], zb0), (o[0], o[1], zb1), (i[0], i[1], zb1)]
        M.poly(pts if o[0] > 0 else list(reversed(pts)), TRIM)
    iron_rail(D, [(o[0], o[1], zb1) for (i, o) in strips], height=0.92)
    for (i, o) in (strips[0], strips[-1]):
        iron_rail(D, [(i[0], i[1], zb1), (o[0], o[1], zb1)], height=0.92)
    # railings round the portico floor between the pedestals (open where the stairs arrive)
    for s in (-1, 1):
        for a0, a1 in ((79.0, 62.5), (33.0, 14.4)):
            iron_rail(D, [tuple(polar(POR_C, POR_R_FLOOR - 0.30, a0 + (a1 - a0) * k / 10, s)) + (Z1 + 0.02,) for k in range(11)], height=0.95)
    iron_rail(D, [tuple(polar(POR_C, POR_R_FLOOR - 0.30, 9.0 - 18.0 * k / 8)) + (Z1 + 0.02,) for k in range(9)], height=0.95)

    # ---------------- the two curved stairs
    RISE_UP, N_UP = 1.16, 7
    z_land = Z1 - RISE_UP
    N_LOW = 17
    a_top, a_foot = 44.0, 17.4
    for s in (-1, 1):
        # upper flight: straight out from the portico floor
        for i in range(N_UP):
            r0 = POR_R_FLOOR + i * (11.20 - POR_R_FLOOR) / N_UP
            r1 = POR_R_FLOOR + (i + 1) * (11.20 - POR_R_FLOOR) / N_UP
            sector_block(M, POR_C, r0, r1 if i < N_UP - 1 else 11.35, 41.0, 55.5, 0.0, Z1 - (i + 1) * RISE_UP / N_UP + 0.0, PAVE, side=s, n=3, skip=('inner',) if i else ())
        sector_block(M, POR_C, 11.35, 14.25, a_top, 55.5, 0.0, z_land, PAVE, side=s, n=3)       # landing
        da = (a_top - a_foot) / N_LOW
        for j in range(N_LOW):                                                               # lower flight, sweeping round to the front
            sector_block(M, POR_C, 11.35, 14.25, a_top - j * da, a_top - (j + 1) * da, 0.0, z_land - (j + 1) * z_land / N_LOW, PAVE, side=s, n=1, skip=('start',))
        # railings: outer and inner
        def P(r, a, z): return tuple(polar(POR_C, r, a, s)) + (z,)
        outer = [P(POR_R_FLOOR, 55.2, Z1), P(11.30, 55.2, z_land), P(14.10, 55.0, z_land)]
        outer += [P(14.10, 55.0 - (55.0 - a_top) * k / 4, z_land) for k in range(1, 5)]
        outer += [P(14.10, a_top - (a_top - a_foot) * k / 20, z_land * (1 - k / 20.0)) for k in range(1, 21)]
        inner = [P(POR_R_FLOOR, 41.3, Z1), P(11.30, 41.3, z_land), P(11.50, a_top, z_land)]
        inner += [P(11.50, a_top - (a_top - a_foot) * k / 20, z_land * (1 - k / 20.0)) for k in range(1, 21)]
        iron_rail(D, outer, height=0.92)
        iron_rail(D, inner, height=0.92)

    # ---------------- roof: promenade, the set-back third floor, solarium, chimneys, flag
    M.quad((-HALF + 0.4, 0.4, ZPROM), (HALF - 0.4, 0.4, ZPROM), (HALF - 0.4, DEPTH - 0.4, ZPROM), (-HALF + 0.4, DEPTH - 0.4, ZPROM), ROOF)
    x3, y30, y31, z3 = 21.9, 3.0, DEPTH - 3.0, 19.22
    zk = ZPROM + 1.15                                           # the third-floor wall is hidden behind the balustrade ...
    M.box(-x3, x3, y30, y31, ZPROM, zk, WALL, skip=('bottom', 'top'))
    Lo = [(-x3, y30), (x3, y30), (x3, y31), (-x3, y31)]
    E = [(-x3 + 1.3, y30 + 1.3), (x3 - 1.3, y30 + 1.3), (x3 - 1.3, y31 - 1.3), (-x3 + 1.3, y31 - 1.3)]
    xd, yd0, yd1 = 15.4, 8.5, DEPTH - 8.5                       # the flat deck on top
    T = [(-xd, yd0), (xd, yd0), (xd, yd1), (-xd, yd1)]
    for i in range(4):                                          # ... what shows above it is slate: a steep slope, then the gentle hip
        j = (i + 1) % 4
        M.quad((Lo[i][0], Lo[i][1], zk), (Lo[j][0], Lo[j][1], zk), (E[j][0], E[j][1], z3), (E[i][0], E[i][1], z3), ROOF)
        M.quad((E[i][0], E[i][1], z3), (E[j][0], E[j][1], z3), (T[j][0], T[j][1], ZROOF), (T[i][0], T[i][1], ZROOF), ROOF)
    M.quad((T[0][0], T[0][1], ZROOF), (T[1][0], T[1][1], ZROOF), (T[2][0], T[2][1], ZROOF), (T[3][0], T[3][1], ZROOF), ROOF)
    # solarium over the bow
    rs, zs = 5.3, 19.64
    arc = arc_points(BOW_C, rs, 90.0, -90.0, 20)
    sol = Path([v2(-rs, y30)] + arc + [v2(rs, y30)])
    M.strip(sol, 0.0, ZPROM, ZPROM + 0.85, WALL)
    M.strip(sol, 0.0, ZPROM + 0.85, zs - 0.40, GLASS)
    M.sweep(sol, [(0.0, zs - 0.40), (0.20, zs - 0.40), (0.26, zs), (0.0, zs)], TRIM)
    for i in range(len(sol.p) - 1):
        a, b = sol.p[i], sol.p[i + 1]
        M.poly([(a[0], a[1], zs), (b[0], b[1], zs), (0.0, y30 + 0.5, zs + 0.35)], ROOF)
        D.fbox(Frame(a, sol.m[i]), -0.04, 0.04, 0.0, 0.05, ZPROM + 0.85, zs - 0.40, TRIM, skip=('back',))
    # chimneys
    def chimney(x, y, w, d, top):
        M.box(x - w / 2, x + w / 2, y - d / 2, y + d / 2, ZPROM, top - 0.32, WALL, skip=('bottom',))
        M.box(x - w / 2 - 0.10, x + w / 2 + 0.10, y - d / 2 - 0.10, y + d / 2 + 0.10, top - 0.32, top - 0.12, TRIM)
        M.box(x - w / 2 - 0.03, x + w / 2 + 0.03, y - d / 2 - 0.03, y + d / 2 + 0.03, top - 0.12, top, TRIM)
    for s in (-1, 1):
        chimney(s * 24.75, 4.3, 0.90, 1.30, 19.78); chimney(s * 24.75, DEPTH - 4.3, 0.90, 1.30, 19.78)
        chimney(s * 12.33, 9.3, 1.25, 1.05, 21.66); chimney(s * 12.33, DEPTH - 9.3, 1.25, 1.05, 21.66)
        chimney(s * 4.64, 10.2, 0.90, 0.90, 21.70)
    chimney(-6.50, 15.5, 0.85, 0.85, 21.60); chimney(6.94, 15.5, 0.85, 0.85, 21.60)
    M.box(-9.68, -6.99, 12.5, 17.0, ZROOF - 0.5, 22.14, WALL, skip=('bottom',))               # lift penthouse
    D.tube((0.0, 9.0, ZROOF), (0.0, 9.0, 27.6), 0.07, TRIM, seg=8)                            # flagpole and flag
    D.lathe(0.0, 9.0, [(0.0, 27.6), (0.10, 27.68), (0.0, 27.78)], LAMP, seg=8, caps=(False, False))
    fw, fh, fz = 3.0, 1.6, 25.85
    for k in range(13):
        c0, c1 = fz + fh * k / 13, fz + fh * (k + 1) / 13
        x0 = 0.08 + (fw * 0.4 if k >= 6 else 0.0)
        col = RED if k % 2 == 0 else WHITE_FLAG
        for flip in (False, True):
            q = [(x0, 9.0, c0), (0.08 + fw, 9.15, c0), (0.08 + fw, 9.15, c1), (x0, 9.0 + (0.06 if k >= 6 else 0.0), c1)]
            D.poly(list(reversed(q)) if flip else q, col)
    for flip in (False, True):
        q = [(0.08, 9.0, fz + fh * 6 / 13), (0.08 + fw * 0.4, 9.06, fz + fh * 6 / 13), (0.08 + fw * 0.4, 9.06, fz + fh), (0.08, 9.0, fz + fh)]
        D.poly(list(reversed(q)) if flip else q, BLUE)

    # ---------------- east and west terraces and the wings (footprints from the site plan, sheet 2; elevations simplified)
    for s, x_end, wing_x, wing_y0, wing_y1, wing_h in ((-1, 65.3, 108.3, -23.1, 15.0, 7.4), (1, 59.5, 85.1, -19.0, 21.5, 8.3)):
        xa, xb = sorted((s * (HALF + G_OUT), s * x_end))
        y0, y1 = 5.8, 14.9
        zt = Z1 + 0.02
        M.box(xa, xb, y0 + 2.2, y1, 0.0, zt - 0.40, WALL, skip=('bottom', 'top', 'left' if s < 0 else 'right'))     # back wall of the colonnade
        M.box(xa, xb, y0, y1, zt - 0.40, zt, TRIM, skip=())                                                    # roof slab = the terrace
        M.box(xa, xb, y0, y0 + 0.30, zt, zt + 0.85, WALL, skip=('bottom',))                                    # parapet
        M.box(xa, xb, y0 - 0.05, y0 + 0.35, zt + 0.85, zt + 0.97, TRIM)
        M.box(xa, xb, y0 - 0.02, y0 + 2.2, 0.0, 0.14, PAVE, skip=('bottom',))
        n = int((xb - xa) / 3.3)
        for k in range(n + 1):
            x = xa + 0.6 + k * (xb - xa - 1.2) / n
            M.cyl(x, y0 + 0.45, 0.14, zt - 0.62, 0.27, WALL, r1=0.235, seg=14, caps=(False, False))
            M.box(x - 0.36, x + 0.36, y0 + 0.09, y0 + 0.81, zt - 0.62, zt - 0.40, TRIM)
            M.box(x - 0.34, x + 0.34, y0 + 0.11, y0 + 0.79, 0.14, 0.30, TRIM)
            if k < n:                                                                                         # door / window behind each bay
                xm = x + (xb - xa - 1.2) / n / 2
                fr = Frame((xm, y0 + 2.2), (0.0, -1.0))
                D.fbox(fr, -0.75, 0.75, 0.005, 0.03, 0.35, 2.85, GLASS, skip=('back',))
                D.fbox(fr, -0.85, 0.85, 0.0, 0.06, 2.85, 2.97, TRIM, skip=('back',))
                for a in (-0.25, 0.25): D.fbox(fr, a - 0.02, a + 0.02, 0.03, 0.05, 0.35, 2.85, TRIM, skip=('back',))
                for c in (1.2, 2.05): D.fbox(fr, -0.75, 0.75, 0.03, 0.05, c - 0.02, c + 0.02, TRIM, skip=('back',))
        # the wing: a plain two-storey block with a cornice, a parapet and rows of windows
        wa, wb = sorted((s * x_end, s * wing_x))
        M.box(wa, wb, wing_y0, wing_y1, 0.0, wing_h, WALL, skip=('bottom', 'top'))
        ring = Path([v2(wa, wing_y0), v2(wb, wing_y0), v2(wb, wing_y1), v2(wa, wing_y1)], closed=True)
        M.sweep(ring, [(0.0, wing_h - 0.75), (0.10, wing_h - 0.75), (0.34, wing_h - 0.45), (0.34, wing_h - 0.35), (0.0, wing_h - 0.35)], TRIM)
        M.sweep(ring, [(-0.30, wing_h - 0.35), (0.04, wing_h - 0.35), (0.04, wing_h + 0.55), (-0.30, wing_h + 0.55)], WALL)
        M.quad((wa, wing_y0, wing_h), (wb, wing_y0, wing_h), (wb, wing_y1, wing_h), (wa, wing_y1, wing_h), ROOF)
        M.sweep(ring, [(0.0, 3.55), (0.07, 3.55), (0.07, 3.75), (0.0, 3.75)], TRIM)
        faces = [(Path([v2(wa, wing_y0), v2(wb, wing_y0)]), (0.0, -1.0)),
                 (Path([v2(wb, wing_y0), v2(wb, wing_y1)]) if s < 0 else Path([v2(wa, wing_y1), v2(wa, wing_y0)]), (-s, 0.0))]
        for pth, nrm in faces:
            nb = int(pth.length / 3.7)
            for k in range(nb):
                p, nn, t = pth.at((k + 0.5) * pth.length / nb)
                fr = Frame(p, nn)
                for (c0, c1) in ((0.95, 2.95), (4.35, wing_h - 1.35)):
                    D.fbox(fr, -0.60, 0.60, 0.005, 0.03, c0, c1, GLASS, skip=('back',))
                    D.fbox(fr, -0.72, 0.72, 0.0, 0.07, c1, c1 + 0.14, TRIM, skip=('back',))
                    D.fbox(fr, -0.70, 0.70, 0.0, 0.09, c0 - 0.10, c0, TRIM, skip=('back',))
                    D.fbox(fr, -0.02, 0.02, 0.03, 0.05, c0, c1, TRIM, skip=('back',))
                    D.fbox(fr, -0.60, 0.60, 0.03, 0.05, (c0 + c1) / 2 - 0.02, (c0 + c1) / 2 + 0.02, TRIM, skip=('back',))
    grounds(M, D)
    return M, D


def grounds(M, D):
    """What stands on the lawn: the paved forecourt below the stairs and the South Lawn fountain."""
    # forecourt: fans out from the foot of the stairs to the drive (same surface as the drive)
    n = 12
    inner = [polar(POR_C, POR_R_FLOOR + 0.05, 17.0 - 34.0 * i / n) for i in range(n + 1)]
    far = [polar(POR_C, 23.6, 27.0 - 54.0 * i / n) for i in range(n + 1)]
    for i in range(n):
        M.quad((inner[i][0], inner[i][1], 0.03), (far[i][0], far[i][1], 0.03), (far[i + 1][0], far[i + 1][1], 0.03), (inner[i + 1][0], inner[i + 1][1], 0.03), DRIVE)
    # planting (not on the drawings; from photographs): shrubs along the foot of the walls, big box masses hiding the
    # flanks of the stairs, and the clipped hedge on the house side of the South Drive
    k = 0
    for sg in (-1, 1):
        for i, x in enumerate((11.4, 13.7, 16.0, 18.3, 20.6, 22.9, 25.0)):
            k += 1
            shrub(M, sg * x, -1.55 - 0.25 * math.sin(k * 1.3), 1.15 + 0.12 * math.sin(k * 2.1), 0.95, 1.25 + 0.30 * abs(math.sin(k * 0.9)), seed=k)
        for (a, r, rr, h) in ((30.0, 16.0, 1.7, 1.9), (41.0, 16.1, 2.0, 2.4), (51.5, 16.0, 1.9, 2.2), (63.0, 13.6, 1.8, 2.5), (72.0, 11.6, 1.5, 2.0)):
            k += 1
            p = polar(POR_C, r, a, sg)
            shrub(M, p[0], p[1], rr, rr * 0.92, h, seed=k)
        # hedge following the drive (which is an ellipse round the oval lawn, 79 m south of the house)
        pts = []
        for i in range(25):
            x = 8.0 + (44.0 - 8.0) * i / 24
            yw = -79.0 + (55.5 + 2.5 + 1.3) * math.sqrt(max(1.0 - (x / (65.5 + 2.5 + 1.3)) ** 2, 0.0))
            pts.append(v2(sg * x, yw))
        if sg > 0: pts = pts[::-1]                               # walk east to west so that "outside" is the lawn side
        M.sweep(Path(pts), [(-0.55, 0.0), (0.55, 0.0), (0.58, 0.55), (0.52, 0.88), (0.30, 0.98), (-0.30, 0.98), (-0.52, 0.88), (-0.58, 0.55)], HEDGE)
    # fountain: stone coping, water, one tall jet and a ring of small ones, red flowers round it
    fx, fy = 0.0, -FOUNTAIN_SOUTH
    R = FOUNTAIN_R
    M.lathe(fx, fy, [(R + 0.45, 0.0), (R + 0.45, 0.38), (R + 0.38, 0.46), (R + 0.02, 0.46), (R, 0.30)], STONE, seg=64, caps=(False, False))
    M.cyl(fx, fy, 0.29, 0.30, R, WATER, seg=64, caps=(False, True))
    M.lathe(fx, fy, [(R + 2.6, 0.0), (R + 2.6, 0.22), (R + 2.3, 0.34), (R + 0.75, 0.34), (R + 0.45, 0.22)], FLOWER, seg=64, caps=(False, False))
    M.lathe(fx, fy, [(R + 3.1, 0.0), (R + 3.1, 0.30), (R + 2.6, 0.30)], HEDGE, seg=64, caps=(False, False))
    D.lathe(fx, fy, [(0.30, 0.30), (0.20, 2.5), (0.12, 5.0), (0.05, 7.2), (0.0, 7.6)], SPRAY, seg=8, caps=(False, False))
    for k in range(12):
        a = 2 * math.pi * k / 12
        D.lathe(fx + 3.6 * math.cos(a), fy + 3.6 * math.sin(a), [(0.12, 0.30), (0.07, 1.4), (0.0, 2.3)], SPRAY, seg=6, caps=(False, False))


if __name__ == '__main__':
    M, D = build()
    print('main: %d verts, %d faces   detail: %d verts, %d faces' % (M.stats() + D.stats()))
    print('levels: first floor %.3f, second %.3f, entablature %.3f, balustrade top %.3f, roof %.3f' % (Z1, Z2, ZE, ZTOP, ZROOF))
