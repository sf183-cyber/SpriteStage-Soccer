# -*- coding: utf-8 -*-
"""Small mesh-building kit for the measured White House model (pure Python + numpy; no bpy here).

Everything is collected as plain vertex / face lists per "sheet" (MeshBuilder), with a material index per
face, and only turned into Blender meshes at the end.  Conventions: x = east, y = north, z = up, metres.
Plan paths are walked so that the OUTSIDE is on the right-hand side of the direction of travel
(east along a south wall), and profiles are (o, z) pairs: o = offset to the outside."""
import math
import numpy as np


def v2(x, y):
    return np.array([x, y], float)


def unit(v):
    n = math.hypot(v[0], v[1])
    return v / n if n > 1e-12 else v


def right_normal(d):
    """Outside normal for travel direction d."""
    return np.array([d[1], -d[0]], float)


def arc_points(c, r, a0, a1, n):
    """Points on a circle around c.  Angles in degrees measured from the SOUTH axis, positive towards the WEST
    (so a0 > a1 walks from west to east, i.e. with the outside (south) on the right)."""
    out = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        out.append(v2(c[0] - r * math.sin(a), c[1] - r * math.cos(a)))
    return out


class Path:
    """Plan polyline with mitred offsets."""

    def __init__(self, pts, closed=False):
        self.p = [np.array(q, float) for q in pts]
        self.closed = closed
        n = len(self.p)
        seg_n = []
        for i in range(n if closed else n - 1):
            d = unit(self.p[(i + 1) % n] - self.p[i])
            seg_n.append(right_normal(d))
        self.seg_n = seg_n
        self.m = []
        for i in range(n):
            if closed:
                a, b = seg_n[i - 1], seg_n[i]
            else:
                a = seg_n[i - 1] if i > 0 else seg_n[0]
                b = seg_n[i] if i < n - 1 else seg_n[-1]
            k = 1.0 + float(a @ b)
            self.m.append((a + b) / max(k, 0.25))
        # arc length
        self.s = [0.0]
        for i in range(1, n):
            self.s.append(self.s[-1] + float(np.linalg.norm(self.p[i] - self.p[i - 1])))
        self.length = self.s[-1] + (float(np.linalg.norm(self.p[0] - self.p[-1])) if closed else 0.0)

    def at(self, u):
        """Point, outside normal and tangent at arc length u (open paths)."""
        u = min(max(u, 0.0), self.s[-1])
        i = max(0, min(len(self.p) - 2, int(np.searchsorted(self.s, u, side='right')) - 1))
        t = (u - self.s[i]) / max(self.s[i + 1] - self.s[i], 1e-9)
        p = self.p[i] * (1 - t) + self.p[i + 1] * t
        m = unit(self.m[i] * (1 - t) + self.m[i + 1] * t)
        return p, m, np.array([-m[1], m[0]])

    def u_of_x(self, x):
        """Arc length of the first point with the given x (for mostly west-to-east paths)."""
        for i in range(len(self.p) - 1):
            x0, x1 = self.p[i][0], self.p[i + 1][0]
            if (x0 - x) * (x1 - x) <= 0 and abs(x1 - x0) > 1e-9:
                return self.s[i] + (x - x0) / (x1 - x0) * (self.s[i + 1] - self.s[i])
        return None


class Frame:
    """Local frame on a wall: origin p (x, y), t along the wall (to the right seen from outside), n to the outside."""

    def __init__(self, p, n, z=0.0):
        self.p = np.array(p, float); self.n = unit(np.array(n, float)); self.t = np.array([-self.n[1], self.n[0]]); self.z = z

    def pt(self, a, b, c):
        """a along the wall, b to the outside, c up."""
        q = self.p + self.t * a + self.n * b
        return (float(q[0]), float(q[1]), self.z + c)

    def shifted(self, a=0.0, b=0.0, c=0.0):
        q = self.p + self.t * a + self.n * b
        return Frame(q, self.n, self.z + c)


class MeshBuilder:
    def __init__(self, name):
        self.name = name
        self.v = []
        self.f = []
        self.fm = []

    def _add(self, pts):
        i0 = len(self.v)
        self.v.extend([(float(p[0]), float(p[1]), float(p[2])) for p in pts])
        return list(range(i0, i0 + len(pts)))

    def poly(self, pts, m):
        """Polygon, counter-clockwise seen from the outside."""
        self.f.append(tuple(self._add(pts))); self.fm.append(m)

    def quad(self, a, b, c, d, m):
        self.poly([a, b, c, d], m)

    # ---- boxes
    def fbox(self, fr, a0, a1, b0, b1, c0, c1, m, skip=()):
        """Box in a frame: a = along, b = outward, c = up.  skip: faces to leave out ('back', 'bottom', 'top', ...)."""
        P = lambda a, b, c: fr.pt(a, b, c)
        if 'front' not in skip: self.quad(P(a0, b1, c0), P(a1, b1, c0), P(a1, b1, c1), P(a0, b1, c1), m)
        if 'back' not in skip: self.quad(P(a1, b0, c0), P(a0, b0, c0), P(a0, b0, c1), P(a1, b0, c1), m)
        if 'left' not in skip: self.quad(P(a0, b0, c0), P(a0, b1, c0), P(a0, b1, c1), P(a0, b0, c1), m)
        if 'right' not in skip: self.quad(P(a1, b1, c0), P(a1, b0, c0), P(a1, b0, c1), P(a1, b1, c1), m)
        if 'top' not in skip: self.quad(P(a0, b1, c1), P(a1, b1, c1), P(a1, b0, c1), P(a0, b0, c1), m)
        if 'bottom' not in skip: self.quad(P(a0, b0, c0), P(a1, b0, c0), P(a1, b1, c0), P(a0, b1, c0), m)

    def box(self, x0, x1, y0, y1, z0, z1, m, skip=()):
        """World-aligned box.  Faces named as seen from the south: front = south (y0)."""
        fr = Frame((0.0, 0.0), (0.0, -1.0))
        # in this frame: along = +x, outward = -y
        self.fbox(fr, x0, x1, -y1, -y0, z0, z1, m, skip)

    # ---- round things
    def cyl(self, cx, cy, z0, z1, r0, m, r1=None, seg=16, caps=(True, True), a0=0.0, a1=360.0):
        r1 = r0 if r1 is None else r1
        full = abs(a1 - a0) >= 359.999
        n = seg
        ring = lambda r, z: [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n)), z) for i in range(n + (0 if full else 1))]
        A, B = ring(r0, z0), ring(r1, z1)
        k = len(A)
        for i in range(n):
            j = (i + 1) % k
            self.quad(A[i], A[j], B[j], B[i], m)
        if caps[0] and r0 > 1e-6: self.poly(list(reversed(A)), m)
        if caps[1] and r1 > 1e-6: self.poly(B, m)

    def lathe(self, cx, cy, profile, m, seg=12, caps=(True, True)):
        """Solid of revolution about a vertical axis.  profile: (r, z) from bottom to top."""
        rings = [[(cx + r * math.cos(2 * math.pi * i / seg), cy + r * math.sin(2 * math.pi * i / seg), z) for i in range(seg)] for r, z in profile]
        for a in range(len(rings) - 1):
            for i in range(seg):
                j = (i + 1) % seg
                self.quad(rings[a][i], rings[a][j], rings[a + 1][j], rings[a + 1][i], m)
        if caps[0] and profile[0][0] > 1e-6: self.poly(list(reversed(rings[0])), m)
        if caps[1] and profile[-1][0] > 1e-6: self.poly(rings[-1], m)

    def tube(self, p0, p1, r, m, seg=6):
        """Thin round bar between two points (railings)."""
        p0 = np.array(p0, float); p1 = np.array(p1, float)
        d = p1 - p0; L = float(np.linalg.norm(d))
        if L < 1e-6: return
        d /= L
        a = np.cross(d, [0, 0, 1.0])
        if np.linalg.norm(a) < 1e-4: a = np.cross(d, [1.0, 0, 0])
        a /= np.linalg.norm(a); b = np.cross(d, a)
        A = [p0 + r * (math.cos(2 * math.pi * i / seg) * a + math.sin(2 * math.pi * i / seg) * b) for i in range(seg)]
        B = [q + d * L for q in A]
        for i in range(seg):
            j = (i + 1) % seg
            self.quad(A[i], A[j], B[j], B[i], m)

    def hcyl(self, fr, a, b0, b1, c, r, m, seg=14):
        """Cylinder whose axis points out of the wall (volutes): centre (a, c) in the frame, from b0 to b1."""
        A = [fr.pt(a + r * math.cos(2 * math.pi * i / seg), b0, c + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
        B = [fr.pt(a + r * math.cos(2 * math.pi * i / seg), b1, c + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
        for i in range(seg):
            j = (i + 1) % seg
            self.quad(A[i], A[j], B[j], B[i], m)
        self.poly(B, m)
        self.poly(list(reversed(A)), m)

    # ---- sweeps
    def sweep(self, path, profile, m, cap=True, u0=None, u1=None):
        """Sweeps a closed profile [(o, z)...] (counter-clockwise in the o-z plane) along a Path."""
        idx = list(range(len(path.p)))
        P = [[(path.p[i][0] + o * path.m[i][0], path.p[i][1] + o * path.m[i][1], z) for (o, z) in profile] for i in idx]
        n = len(P); k = len(profile)
        last = n if path.closed else n - 1
        for i in range(last):
            a, b = P[i], P[(i + 1) % n]
            for j in range(k):
                j2 = (j + 1) % k
                self.quad(a[j], b[j], b[j2], a[j2], m)
        if cap and not path.closed:
            self.poly(P[0], m)
            self.poly(list(reversed(P[-1])), m)

    def strip(self, path, o, z0, z1, m, flip=False):
        """Single skin (no thickness) along a path at offset o."""
        P = [(path.p[i][0] + o * path.m[i][0], path.p[i][1] + o * path.m[i][1]) for i in range(len(path.p))]
        n = len(P); last = n if path.closed else n - 1
        for i in range(last):
            a, b = P[i], P[(i + 1) % n]
            q = [(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)]
            self.poly(list(reversed(q)) if flip else q, m)

    def wall(self, path, z0, z1, m, openings=(), o=0.0, m_reveal=None, arch_steps=10):
        """Wall skin along an open path with holes.  Each opening: dict(u0, u1, z0, z1, depth, arch=False, back=material or None).
        For arch=True, z1 is the springing line and the top is a semicircle of radius (u1-u0)/2.
        Returns nothing; reveals and the back plane (glass / dark) are added."""
        m_reveal = m if m_reveal is None else m_reveal
        us = set(path.s)
        for op in openings:
            us.add(op['u0']); us.add(op['u1'])
            if op.get('arch'):
                for k in range(1, arch_steps):
                    us.add(op['u0'] + (op['u1'] - op['u0']) * k / arch_steps)
        us = sorted(u for u in us if -1e-9 <= u <= path.s[-1] + 1e-9)

        def top(op, u):
            if not op.get('arch'): return op['z1']
            r = (op['u1'] - op['u0']) / 2; c = (op['u0'] + op['u1']) / 2
            return op['z1'] + math.sqrt(max(r * r - (u - c) ** 2, 0.0))

        def P(u, z, d=0.0):
            p, n, t = path.at(u)
            q = p + n * (o - d)
            return (float(q[0]), float(q[1]), z)

        for a, b in zip(us[:-1], us[1:]):
            if b - a < 1e-7: continue
            mid = (a + b) / 2
            holes = sorted([op for op in openings if op['u0'] - 1e-9 <= mid <= op['u1'] + 1e-9], key=lambda q: q['z0'])
            za = zb = z0
            for op in holes:
                if op['z0'] > za + 1e-6:
                    self.quad(P(a, za), P(b, zb), P(b, op['z0']), P(a, op['z0']), m)
                za, zb = top(op, a), top(op, b)
                d = op['depth']
                # head of the opening (follows the arch), facing down
                self.quad(P(a, za, d), P(b, zb, d), P(b, zb), P(a, za), m_reveal)
                # sill, facing up
                self.quad(P(a, op['z0']), P(b, op['z0']), P(b, op['z0'], d), P(a, op['z0'], d), m_reveal)
                if op.get('back') is not None:
                    self.quad(P(a, op['z0'], d), P(b, op['z0'], d), P(b, zb, d), P(a, za, d), op['back'])
            if z1 > max(za, zb) + 1e-6:
                self.quad(P(a, za), P(b, zb), P(b, z1), P(a, z1), m)
        for op in openings:
            d = op['depth']
            for u, left in ((op['u0'], True), (op['u1'], False)):
                zt = top(op, u)
                if zt - op['z0'] < 1e-6: continue
                q = [P(u, op['z0'], d), P(u, op['z0']), P(u, zt), P(u, zt, d)]
                self.poly(list(reversed(q)) if left else q, m_reveal)

    def merge(self, other):
        off = len(self.v)
        self.v.extend(other.v)
        self.f.extend(tuple(i + off for i in f) for f in other.f)
        self.fm.extend(other.fm)

    def stats(self):
        return len(self.v), len(self.f)
