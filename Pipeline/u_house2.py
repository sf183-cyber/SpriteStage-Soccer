# -*- coding: utf-8 -*-
"""The measured White House (wh_model.py) in Blender: check renders, lighting bake, export for Unity.

    Blender -b --factory-startup -P u_house2.py -- elev  <px per metre> <samples> <tag> [x0 x1 z0 z1]   orthographic south elevation (to lay over the HABS sheets)
    Blender -b --factory-startup -P u_house2.py -- view  <width px> <samples> <tag> [yaw pitch dist]    perspective look from the lawn
    Blender -b --factory-startup -P u_house2.py -- bake  <texture size> <samples>                        bake + write World/house.bytes, house_detail.bytes, out/house_bake.npy

The model is built in its own coordinates (south wall on y = 0) and stood at y = HOUSE_Y of the world."""
import bpy, bmesh, sys, os, math, struct
import numpy as np
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import u_layout as L
import wh_model as WH
from u_world import *

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['elev']
MODE = argv[0]

sc = reset()
daylight(sc)


def paint(name, colr, rough, joints=0.0):
    """Old white paint on sandstone: a little uneven, grimy in the recesses and where rain runs down;
    with joints > 0 the courses of the ashlar underneath show faintly through the paint."""
    m, nt, b = new_mat(name)
    ao = nt.nodes.new('ShaderNodeAmbientOcclusion'); ao.inputs['Distance'].default_value = 0.9; ao.samples = 8
    tc = nt.nodes.new('ShaderNodeTexCoord')
    # rain streaks: noise stretched vertically
    mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1.3, 1.3, 0.10)
    streak = nt.nodes.new('ShaderNodeTexNoise'); streak.inputs['Scale'].default_value = 0.9; streak.inputs['Detail'].default_value = 9
    nt.links.new(tc.outputs['Object'], mp.inputs['Vector']); nt.links.new(mp.outputs['Vector'], streak.inputs['Vector'])
    # blotches: the paint is not one colour over a whole wall
    blot = nt.nodes.new('ShaderNodeTexNoise'); blot.inputs['Scale'].default_value = 0.22; blot.inputs['Detail'].default_value = 5
    nt.links.new(tc.outputs['Object'], blot.inputs['Vector'])
    def ramp(sock, lo, hi, c0, c1):
        r = nt.nodes.new('ShaderNodeValToRGB')
        r.color_ramp.elements[0].position = lo; r.color_ramp.elements[0].color = (*c0, 1)
        r.color_ramp.elements[1].position = hi; r.color_ramp.elements[1].color = (*c1, 1)
        nt.links.new(sock, r.inputs['Fac']); return r.outputs['Color']
    def mul(a_, b_, fac=1.0):
        x = nt.nodes.new('ShaderNodeMixRGB'); x.blend_type = 'MULTIPLY'; x.inputs['Fac'].default_value = fac
        nt.links.new(a_, x.inputs['Color1']); nt.links.new(b_, x.inputs['Color2']); return x.outputs['Color']
    base = ramp(streak.outputs['Fac'], 0.28, 0.72, (colr[0] * 0.86, colr[1] * 0.85, colr[2] * 0.815), colr)
    tone = ramp(blot.outputs['Fac'], 0.30, 0.70, (0.95, 0.945, 0.92), (1.0, 1.0, 1.0))
    c = mul(base, tone)
    c = mul(c, ao.outputs['Color'], 0.75)
    if joints > 0:
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Object'], sep.inputs['Vector'])
        add = nt.nodes.new('ShaderNodeMath'); add.operation = 'ADD'
        nt.links.new(sep.outputs['X'], add.inputs[0]); nt.links.new(sep.outputs['Y'], add.inputs[1])
        cmb = nt.nodes.new('ShaderNodeCombineXYZ')
        nt.links.new(add.outputs[0], cmb.inputs['X']); nt.links.new(sep.outputs['Z'], cmb.inputs['Y'])
        br = nt.nodes.new('ShaderNodeTexBrick')
        br.inputs['Scale'].default_value = 1.0; br.inputs['Mortar Size'].default_value = 0.010; br.inputs['Mortar Smooth'].default_value = 0.4
        br.inputs['Brick Width'].default_value = 1.30; br.inputs['Row Height'].default_value = 0.352
        br.inputs['Color1'].default_value = (1, 1, 1, 1); br.inputs['Color2'].default_value = (0.985, 0.985, 0.98, 1); br.inputs['Mortar'].default_value = (1 - joints, 1 - joints, 1 - joints, 1)
        nt.links.new(cmb.outputs['Vector'], br.inputs['Vector'])
        c = mul(c, br.outputs['Color'])
    nt.links.new(c, b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = rough
    return m


def leafy(name, colr):
    """Clipped box / yew: mottled dark green with a rough surface."""
    m, nt, b = new_mat(name)
    tc = nt.nodes.new('ShaderNodeTexCoord')
    n1 = nt.nodes.new('ShaderNodeTexNoise'); n1.inputs['Scale'].default_value = 9.0; n1.inputs['Detail'].default_value = 6
    nt.links.new(tc.outputs['Object'], n1.inputs['Vector'])
    r = nt.nodes.new('ShaderNodeValToRGB')
    r.color_ramp.elements[0].position = 0.30; r.color_ramp.elements[0].color = (colr[0] * 0.45, colr[1] * 0.50, colr[2] * 0.45, 1)
    r.color_ramp.elements[1].position = 0.72; r.color_ramp.elements[1].color = (colr[0] * 1.5, colr[1] * 1.45, colr[2] * 1.2, 1)
    nt.links.new(n1.outputs['Fac'], r.inputs['Fac']); nt.links.new(r.outputs['Color'], b.inputs['Base Color'])
    bump = nt.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.9; bump.inputs['Distance'].default_value = 0.08
    n2 = nt.nodes.new('ShaderNodeTexNoise'); n2.inputs['Scale'].default_value = 22.0; n2.inputs['Detail'].default_value = 4
    nt.links.new(tc.outputs['Object'], n2.inputs['Vector'])
    nt.links.new(n2.outputs['Fac'], bump.inputs['Height']); nt.links.new(bump.outputs['Normal'], b.inputs['Normal'])
    b.inputs['Roughness'].default_value = 0.6
    return m


def materials():
    mats = {
        'wall': paint('wall', (0.745, 0.728, 0.686), 0.55, joints=0.085),
        'trim': paint('trim', (0.780, 0.765, 0.725), 0.5),
        'glass': pbr('glass', (0.040, 0.050, 0.064), 0.08, coat=1.0),
        'iron': pbr('iron', (0.015, 0.015, 0.017), 0.45, 0.5),
        'roof': pbr('roof', (0.060, 0.065, 0.072), 0.55),
        'paving': pbr('paving', (0.42, 0.41, 0.38), 0.75),
        'dark': pbr('dark', (0.02, 0.02, 0.02), 0.8),
        'flag_red': pbr('flag_red', (0.45, 0.02, 0.03), 0.7),
        'flag_blue': pbr('flag_blue', (0.02, 0.03, 0.18), 0.7),
        'flag_white': pbr('flag_white', (0.75, 0.75, 0.75), 0.7),
        'hedge': leafy('hedge', (0.016, 0.038, 0.012)),
        'lamp': pbr('lamp', (0.20, 0.16, 0.08), 0.15, coat=1.0),
        'water': pbr('water', (0.02, 0.10, 0.11), 0.04, coat=1.0),
        'flower': pbr('flower', (0.42, 0.012, 0.016), 0.7),
        'spray': pbr('spray', (0.85, 0.88, 0.92), 0.5, emit=(0.85, 0.92, 1.0), strength=0.25),
        'stone': pbr('stone', (0.50, 0.49, 0.45), 0.7),
        'shade': pbr('shade', (0.52, 0.50, 0.45), 0.9),
        'drive': pbr('drive', (0.150, 0.140, 0.125), 0.85),
        'awning': pbr('awning', (0.70, 0.69, 0.66), 0.8),
    }
    return [mats[n] for n in WH.MATERIAL_NAMES]


def to_object(mb, mats, dy):
    me = bpy.data.meshes.new(mb.name)
    me.from_pydata(mb.v, [], mb.f)
    for m in mats: me.materials.append(m)
    me.polygons.foreach_set('material_index', np.array(mb.fm, np.int32))
    me.update()
    ob = bpy.data.objects.new(mb.name, me); col().objects.link(ob)
    ob.location = (0.0, dy, 0.0)
    return ob


MB_MAIN, MB_DETAIL = WH.build()
print('model: main %d faces, detail %d faces' % (len(MB_MAIN.f), len(MB_DETAIL.f)))
MATS = materials()
if os.environ.get('DEBUG_NORMALS') == '1':
    # check picture: faces seen from their back show up red
    dm = bpy.data.materials.new('dbg'); dm.use_nodes = True
    nt = dm.node_tree; nt.nodes.clear()
    geo = nt.nodes.new('ShaderNodeNewGeometry'); mix = nt.nodes.new('ShaderNodeMixShader'); out = nt.nodes.new('ShaderNodeOutputMaterial')
    e1 = nt.nodes.new('ShaderNodeBsdfDiffuse'); e1.inputs['Color'].default_value = (0.6, 0.6, 0.6, 1)
    e2 = nt.nodes.new('ShaderNodeEmission'); e2.inputs['Color'].default_value = (1, 0, 0, 1); e2.inputs['Strength'].default_value = 3.0
    nt.links.new(geo.outputs['Backfacing'], mix.inputs['Fac']); nt.links.new(e1.outputs['BSDF'], mix.inputs[1]); nt.links.new(e2.outputs['Emission'], mix.inputs[2])
    nt.links.new(mix.outputs['Shader'], out.inputs['Surface'])
    MATS = [dm for _ in MATS]
HOUSE_Y = L.HOUSE_Y if MODE == 'bake' or (MODE == 'view') else 0.0
main = to_object(MB_MAIN, MATS, HOUSE_Y)
detail = to_object(MB_DETAIL, MATS, HOUSE_Y)

if MODE == 'elev':
    PPM = float(argv[1]) if len(argv) > 1 else 40.0
    SAMPLES = int(argv[2]) if len(argv) > 2 else 24
    TAG = argv[3] if len(argv) > 3 else 'elev_test'
    X0, X1, Zb, Zt = [float(v) for v in argv[4:8]] if len(argv) >= 8 else (-28.0, 28.0, -0.5, 28.5)
    AXIS = argv[8] if len(argv) > 8 else 'south'
    quad('ground', [(-300, -300, 0), (300, -300, 0), (300, 300, 0), (-300, 300, 0)], pbr('lawn', (0.05, 0.09, 0.02), 0.8))
    W, H = int(round((X1 - X0) * PPM)), int(round((Zt - Zb) * PPM))
    cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = max(W, H) / PPM; cd.clip_start = 1; cd.clip_end = 2000
    co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
    if AXIS == 'south':
        co_.location = ((X0 + X1) / 2, -400.0, (Zb + Zt) / 2); co_.rotation_euler = (math.radians(90), 0, 0)
    else:                                   # from the west: x of the picture = south-to-north reversed (south on the right, like sheet 44)
        co_.location = (-400.0, -(X0 + X1) / 2, (Zb + Zt) / 2); co_.rotation_euler = (math.radians(90), 0, math.radians(-90))
    setup_cycles(sc, SAMPLES, W, H, transparent=False, denoise=True)
    render_npy(sc, TAG)

elif MODE == 'view':
    WPX = int(argv[1]) if len(argv) > 1 else 1280
    SAMPLES = int(argv[2]) if len(argv) > 2 else 32
    TAG = argv[3] if len(argv) > 3 else 'view_test'
    yaw, pitch, dist = [float(v) for v in argv[4:7]] if len(argv) >= 7 else (0.0, 4.0, 62.0)
    quad('ground', [(-400, -300, 0), (400, -300, 0), (400, 400, 0), (-400, 400, 0)], pbr('lawn', (0.05, 0.09, 0.02), 0.8))
    if os.environ.get('NO_TREES') != '1':
        trees(cam_visible=True)
    cd = bpy.data.cameras.new('cam'); cd.lens = float(os.environ.get('LENS', '50')); cd.clip_start = 0.5; cd.clip_end = 3000
    co_ = bpy.data.objects.new('cam', cd); col().objects.link(co_); sc.camera = co_
    target = Vector((0.0, L.HOUSE_Y - 3.0, 8.5))
    yr, pr = math.radians(yaw), math.radians(pitch)
    fwd = Vector((math.sin(yr) * math.cos(pr), math.cos(yr) * math.cos(pr), -math.sin(pr)))
    co_.location = target - fwd * dist
    co_.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
    setup_cycles(sc, SAMPLES, WPX, int(WPX * 9 / 16), transparent=False, denoise=True)
    sc.render.film_transparent = False
    render_npy(sc, TAG)

elif MODE == 'bake':
    SIZE = int(argv[1]) if len(argv) > 1 else 1024
    SAMPLES = int(argv[2]) if len(argv) > 2 else 32
    quad('lawn', [(-400, -300, 0), (400, -300, 0), (400, 400, 0), (-400, 400, 0)], pbr('lawn', (0.05, 0.09, 0.02), 0.8))
    if os.environ.get('NO_TREES') != '1':
        trees()
    # ---- main sheet: weld, drop what can never be seen, unwrap, bake to a texture
    me = main.data
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    # nobody ever gets behind the house: faces that look north from its rear half, and undersides on the ground
    dead = [f for f in bm.faces if (f.normal.y > 0.7 and f.calc_center_median().y > 8.0) or (f.normal.z < -0.7 and f.calc_center_median().z < 0.05)]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    me.update()
    nf = len(me.polygons)
    mi = np.empty(nf, np.int32); me.polygons.foreach_get('material_index', mi)
    me.polygons.foreach_set('use_smooth', (mi == WH.HEDGE))            # the clipped shrubs are round, everything else is cut stone
    nrm = np.empty(nf * 3, np.float32); me.polygons.foreach_get('normal', nrm); nrm = nrm.reshape(-1, 3)
    cen = np.empty(nf * 3, np.float32); me.polygons.foreach_get('center', cen); cen = cen.reshape(-1, 3)
    area = np.empty(nf, np.float32); me.polygons.foreach_get('area', area)
    # three classes of surface: the fronts that face the lawn (finest), the rest, and big dull planes (roofs, the far wings)
    front = ((nrm[:, 1] < -0.25) | ((np.abs(cen[:, 0]) < 16.0) & (cen[:, 1] < 6.0) & (cen[:, 2] < 18.5))) & (np.abs(cen[:, 0]) < 27.0) & (cen[:, 1] > -30.0)
    dull = (~front) & ((nrm[:, 2] > 0.7) | (np.abs(cen[:, 0]) > 60.0) | (cen[:, 1] < -100.0))
    group = np.where(front, 0, np.where(dull, 2, 1))
    target = (4.0, 1.0, 0.16)                           # relative texel density (by area)
    bpy.ops.object.select_all(action='DESELECT')
    main.select_set(True); bpy.context.view_layer.objects.active = main
    me.uv_layers.new(name='uv')
    for g in range(3):
        sel = group == g
        me.polygons.foreach_set('select', sel)
        me.update()
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
    li = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', li)
    uvs = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get('uv', uvs); uvs = uvs.reshape(-1, 2)
    ls = np.empty(nf, np.int32); me.polygons.foreach_get('loop_start', ls)
    tri = ls[:, None] + np.arange(3)[None, :]
    a, b, c = uvs[tri[:, 0]], uvs[tri[:, 1]], uvs[tri[:, 2]]
    uv_area = 0.5 * np.abs((b[:, 0] - a[:, 0]) * (c[:, 1] - a[:, 1]) - (b[:, 1] - a[:, 1]) * (c[:, 0] - a[:, 0]))
    for g in range(3):
        sel = group == g
        if not sel.any(): continue
        dens = float(uv_area[sel].sum() / max(float(area[sel].sum()), 1e-9))
        k = math.sqrt(target[g] / max(dens, 1e-12))
        uvs[tri[sel].reshape(-1)] = uvs[tri[sel].reshape(-1)] * k + np.array([g * 1000.0, 0.0], np.float32)     # keep the classes apart until they are packed
        print('uv class', g, 'faces', int(sel.sum()), 'area %.0f m2' % float(area[sel].sum()))
    me.uv_layers.active.data.foreach_set('uv', uvs.reshape(-1))
    me.polygons.foreach_set('select', np.ones(nf, bool))
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.0008)
    bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new('house_bake', SIZE, SIZE, alpha=False, float_buffer=True)
    for slot in main.material_slots:
        nt = slot.material.node_tree
        n = nt.nodes.new('ShaderNodeTexImage'); n.image = img
        nt.nodes.active = n
    setup_cycles(sc, SAMPLES, SIZE, SIZE, transparent=False, denoise=True)
    sc.cycles.use_adaptive_sampling = False
    sc.render.bake.margin = max(2, SIZE // 512)
    sc.render.bake.use_clear = True
    sc.render.bake.target = 'IMAGE_TEXTURES'
    print('baking texture', SIZE, SAMPLES)
    bpy.ops.object.bake(type='COMBINED')
    buf = np.empty(SIZE * SIZE * 4, np.float32); img.pixels.foreach_get(buf)
    arr = buf.reshape(SIZE, SIZE, 4)[::-1]
    np.save(os.path.join(L.OUT, 'house_bake.npy'), arr[:, :, :3].astype(np.float16))
    print('BAKE_OK mean', float(arr[:, :, :3].mean()))
    n = len(me.polygons)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    co[:, 1] += HOUSE_Y
    li = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', li)
    uvs = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get('uv', uvs); uvs = uvs.reshape(-1, 2)
    ls = np.empty(n, np.int32); me.polygons.foreach_get('loop_start', ls)
    order = (ls[:, None] + np.array([0, 2, 1], np.int32)[None, :]).reshape(-1)
    P = co[li[order]]; P = np.stack([P[:, 0], P[:, 2], P[:, 1]], 1).astype(np.float32)
    os.makedirs(L.UNITY_WORLD, exist_ok=True)
    with open(os.path.join(L.UNITY_WORLD, 'house.bytes'), 'wb') as f:
        f.write(b'G3M1'); f.write(struct.pack('<i', P.shape[0])); f.write(P.tobytes()); f.write(uvs[order].astype(np.float32).tobytes())
    print('MESH_OK main verts', P.shape[0])

    # ---- detail sheet: lighting baked per face corner into vertex colours
    for slot in main.material_slots:                    # the texture nodes must not stay active for the vertex bake
        nt = slot.material.node_tree
        nt.nodes.active = nt.nodes['Principled BSDF']
    me = detail.data
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    ca = me.color_attributes.new('bake', 'FLOAT_COLOR', 'CORNER')
    me.color_attributes.active_color = ca
    bpy.ops.object.select_all(action='DESELECT')
    detail.select_set(True); bpy.context.view_layer.objects.active = detail
    sc.render.bake.target = 'VERTEX_COLORS'
    sc.cycles.samples = max(16, SAMPLES // 2)
    print('baking vertex colours')
    bpy.ops.object.bake(type='COMBINED')
    n = len(me.polygons)
    colr = np.empty(len(me.loops) * 4, np.float32); me.color_attributes['bake'].data.foreach_get('color', colr); colr = colr.reshape(-1, 4)[:, :3]
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    co[:, 1] += HOUSE_Y
    li = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', li)
    ls = np.empty(n, np.int32); me.polygons.foreach_get('loop_start', ls)
    order = (ls[:, None] + np.array([0, 2, 1], np.int32)[None, :]).reshape(-1)
    P = co[li[order]]; P = np.stack([P[:, 0], P[:, 2], P[:, 1]], 1).astype(np.float32)
    np.save(os.path.join(L.OUT, 'house_detail_pos.npy'), P)
    np.save(os.path.join(L.OUT, 'house_detail_col.npy'), colr[order].astype(np.float16))
    print('DETAIL_OK verts', P.shape[0], 'mean', float(colr.mean()))
