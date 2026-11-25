# -*- coding: utf-8 -*-
"""PLATEAU（国土交通省 3D 都市模型，港区 2025 年度，CityGML v5）-> 场景坐标的建筑面片缓存 out/plateau_bldg.pkl。

    python3 t_plateau.py

读 plateau_raw/*_bldg_6697_op.gml：每栋楼（含 BuildingPart）取 LOD2 的墙、屋顶面（没有就用 LOD1 的实体），
坐标是 JGD2011 经纬度 + 高程（EPSG:6697），换成场景坐标（t_geo.scene），高度减去这栋楼自己的最低点
（场景是平的：芝公园和塔所在的小山的高差不要）。同时读楼高、地上层数、用途，给外墙材质用。
植被（*_veg_6697_op.gml）：单棵树的位置和高度 -> out/plateau_trees.pkl。

出典：国土交通省 3D都市モデル（Project PLATEAU）港区（2025年度）。PLATEAU サイトポリシーに基づき利用（CC BY 4.0 互換）。"""
import os, sys, glob, pickle, math
import xml.etree.ElementTree as ET
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import t_geo as G

RAW = os.path.join(HERE, 'plateau_raw')     # PLATEAU 的原始数据（约 480 MB）：自己下载放这里（见 SOURCES.md），不随源码发布
OUT = os.path.join(HERE, 'out')
NS = {
    'gml': 'http://www.opengis.net/gml',
    'bldg': 'http://www.opengis.net/citygml/building/2.0',
    'core': 'http://www.opengis.net/citygml/2.0',
    'veg': 'http://www.opengis.net/citygml/vegetation/2.0',
    'uro': 'https://www.geospatial.jp/iur/uro/3.1',
}
GML = '{http://www.opengis.net/gml}'
BLDG = '{http://www.opengis.net/citygml/building/2.0}'
VEG = '{http://www.opengis.net/citygml/vegetation/2.0}'


def poslist(text):
    v = [float(t) for t in text.split()]
    return [(v[i], v[i + 1], v[i + 2]) for i in range(0, len(v) - 2, 3)]


def rings_under(el, ids=None):
    """el 下面所有多边形的外环（经纬度高程的点列）。ids 是 list 时顺便记下每个多边形的 gml:id（对贴图用）"""
    out = []
    for poly in el.iter(GML + 'Polygon'):
        ext = poly.find(GML + 'exterior')
        if ext is None: continue
        pl = ext.find('.//' + GML + 'posList')
        if pl is not None and pl.text:
            pts = poslist(pl.text)
            if len(pts) >= 4:
                out.append(pts[:-1] if pts[0] == pts[-1] else pts)
                if ids is not None: ids.append(poly.get(GML + 'id'))
    return out


def parse_appearance(path):
    """文件开头的贴图表：多边形 id -> (贴图文件, 外环每个点的 UV)"""
    APP = '{http://www.opengis.net/citygml/appearance/2.0}'
    tex = {}
    for ev, el in ET.iterparse(path, events=('end',)):
        if el.tag == APP + 'ParameterizedTexture':
            uri = el.findtext(APP + 'imageURI')
            for tg in el.findall(APP + 'target'):
                tc = tg.find('.//' + APP + 'textureCoordinates')
                if tc is None or not tc.text: continue
                v = [float(t) for t in tc.text.split()]
                uv = [(v[i], v[i + 1]) for i in range(0, len(v) - 1, 2)]
                if len(uv) > 1 and uv[0] == uv[-1]: uv = uv[:-1]
                tex[tg.get('uri').lstrip('#')] = (uri, uv)
            el.clear()
        elif el.tag == BLDG + 'Building':
            break                       # 贴图表都在文件开头
    return tex


def text_of(el, tag):
    e = el.find(tag)
    return e.text if e is not None and e.text else None


def building_surfaces(b):
    """(kind, ring) 列表：kind 'wall' / 'roof' / 'ground'。LOD2 优先，没有就 LOD1 实体（按法线分墙和顶）。"""
    surfs = []
    for bb in b.findall(BLDG + 'boundedBy'):
        for child in bb:
            tag = child.tag.split('}')[1]
            kind = {'WallSurface': 'wall', 'RoofSurface': 'roof', 'GroundSurface': 'ground', 'OuterCeilingSurface': 'roof',
                    'OuterFloorSurface': 'roof', 'ClosureSurface': 'wall'}.get(tag)
            if kind is None: continue
            for ms in child:
                if ms.tag.endswith('lod2MultiSurface'):
                    ids = []
                    for r, pid in zip(rings_under(ms, ids), ids): surfs.append((kind, r, pid))
    if surfs:
        return surfs, 2
    s1 = b.find(BLDG + 'lod1Solid')
    if s1 is not None:
        for r in rings_under(s1):
            zs = [p[2] for p in r]
            kind = 'roof' if max(zs) - min(zs) < 0.05 and len(r) >= 3 else 'wall'
            surfs.append((kind, r, None))
        return surfs, 1
    return [], 0


def parse_bldg(path):
    res = []
    n = 0
    for ev, el in ET.iterparse(path, events=('end',)):
        if el.tag != BLDG + 'Building':
            continue
        n += 1
        parts = [el] + list(el.iter(BLDG + 'BuildingPart'))
        name = text_of(el, GML + 'name')
        mh = text_of(el, BLDG + 'measuredHeight')
        st = text_of(el, BLDG + 'storeysAboveGround')
        usage = text_of(el, BLDG + 'usage')
        for p in parts:
            surfs, lod = building_surfaces(p)
            if not surfs: continue
            zmin = min(pt[2] for _, r, _ in surfs for pt in r)
            conv = []; pids = []
            for kind, r, pid in surfs:
                conv.append((kind, [(*G.scene(lat, lon), h - zmin) for (lat, lon, h) in r])); pids.append(pid)
            xs = [q[0] for _, r in conv for q in r]; ys = [q[1] for _, r in conv for q in r]; zs = [q[2] for _, r in conv for q in r]
            res.append(dict(id=el.get(GML + 'id'), name=name, height=float(mh) if mh else max(zs), storeys=int(st) if st and st.isdigit() else None,
                            usage=usage, lod=lod, cx=(min(xs) + max(xs)) / 2, cy=(min(ys) + max(ys)) / 2, top=max(zs),
                            r=math.hypot(max(xs) - min(xs), max(ys) - min(ys)) / 2, surfs=conv, pids=pids,
                            mesh=os.path.basename(path)[:8]))
        el.clear()
    print('%s: %d buildings, %d parts' % (os.path.basename(path), n, len(res)))
    return res


def parse_veg(path):
    trees = []
    for ev, el in ET.iterparse(path, events=('end',)):
        if el.tag != VEG + 'SolitaryVegetationObject':
            continue
        h = text_of(el, VEG + 'height')
        pts = []
        for r in rings_under(el): pts += r
        if not pts:
            pl = el.find('.//' + GML + 'pos')
            if pl is not None: pts = poslist(pl.text + ' 0')[:1] if len(pl.text.split()) == 2 else poslist(pl.text)
        if pts:
            lat = sum(p[0] for p in pts) / len(pts); lon = sum(p[1] for p in pts) / len(pts)
            zmin = min(p[2] for p in pts); zmax = max(p[2] for p in pts)
            x, y = G.scene(lat, lon)
            trees.append((x, y, float(h) if h else max(4.0, zmax - zmin)))
        el.clear()
    print('%s: %d trees' % (os.path.basename(path), len(trees)))
    return trees


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    B = []; TEX = {}
    for f in sorted(glob.glob(os.path.join(RAW, '*_bldg_6697_op.gml'))):
        B += parse_bldg(f)
        TEX.update(parse_appearance(f))
    pickle.dump(B, open(os.path.join(OUT, 'plateau_bldg.pkl'), 'wb'))
    pickle.dump(TEX, open(os.path.join(OUT, 'plateau_tex.pkl'), 'wb'))
    print('textured polygons %d' % len(TEX))
    T = []
    for f in sorted(glob.glob(os.path.join(RAW, '*_veg_6697_op.gml'))):
        T += parse_veg(f)
    pickle.dump(T, open(os.path.join(OUT, 'plateau_trees.pkl'), 'wb'))
    named = sorted([(b['height'], b['name']) for b in B if b['name']], reverse=True)[:30]
    for h, n in named: print('  %6.1f  %s' % (h, n))
    tall = sorted(B, key=lambda b: -b['height'])[:15]
    for b in tall:
        print('  tall %6.1f m  storeys %s  at (%6.0f, %6.0f) lod%d  %s' % (b['height'], b['storeys'], b['cx'], b['cy'], b['lod'], b['name'] or ''))
    print('PLATEAU_OK buildings %d trees %d' % (len(B), len(T)))
