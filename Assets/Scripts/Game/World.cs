// Builds the 3D scenery at start-up: the baked lawns, the White House, trees, goals, corner flags, sky.
// Everything here is static except the trees, which are impostors and turn to face the camera.
// 2026-11-24：第二个场景“东京塔夜场”（Field.Stage == "tokyo"，按足球场摆的）：夜里的光（月亮、
// 四根泛光灯）、近处真实的楼（PLATEAU，烘焙好的三维）、公园外的地面、公园路灯和绿篱、远处的一圈全景带（东京塔和城市）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Soccer
{
    [Serializable] public class WorldInfo { public float[] grass; public float sunElevation; public TreeAtlasInfo trees; }

    public sealed class World : MonoBehaviour
    {
        public WorldInfo Info;
        public Vector3 ToSun, ToMoon;
        public bool Night;
        public Vector3[] Lamps = new Vector3[0];        // 泛光灯挂在哪（夜里）
        public Vector3[] ParkLamps = new Vector3[0];    // 公园路灯、大路的路灯的灯头
        Vector3[] redLights = new Vector3[0];           // 高楼顶上的红色航空障碍灯
        Mesh glowMesh; readonly List<Vector3> gv = new List<Vector3>(); readonly List<Vector2> guv = new List<Vector2>(), gpar = new List<Vector2>(); readonly List<Vector4> gcol = new List<Vector4>(); readonly List<int> gtri = new List<int>();
        Mesh treeMesh;
        Vector3[] treeVerts; Vector2[] treeUv0, treeUv1; Color32[] treeCol;
        readonly List<Material> owned = new List<Material>();

        static Shader Sh(string name) { return Resources.Load<Shader>("Shaders/" + name); }
        static Texture2D Tex(string name) { return Field.Load<Texture2D>(name); }

        Material Mat(string shader, Texture tex = null)
        {
            var m = new Material(Sh(shader)); m.hideFlags = HideFlags.DontSave;
            if (tex != null) m.mainTexture = tex;
            owned.Add(m);
            return m;
        }

        GameObject Obj(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        void OnDestroy()
        {
            foreach (var m in owned) if (m != null) Destroy(m);
        }

        public void Build()
        {
            var L = Field.L;
            var wi = Field.Load<TextAsset>("world");
            Info = wi != null ? JsonUtility.FromJson<WorldInfo>(wi.text) : new WorldInfo();
            if (Info.grass == null || Info.grass.Length < 3) Info.grass = new[] { 0.3f, 0.45f, 0.15f };
            if (Info.sunElevation <= 0) Info.sunElevation = 37.8f;
            float e = Info.sunElevation * Mathf.Deg2Rad;
            Vector2 st = new Vector2(L.sunX, L.sunY).normalized;
            ToSun = new Vector3(-st.x * Mathf.Cos(e), Mathf.Sin(e), -st.y * Mathf.Cos(e)).normalized;
            Night = L.night;
            if (Night)
            {
                // 夜里（同篮球的夜景）：月亮是少数实时受光的东西的“太阳”，其余靠四根泛光灯（G3Lit、G3Hedge 里按距离算）
                float ma = L.moonAz * Mathf.Deg2Rad, me = L.moonEl * Mathf.Deg2Rad;
                ToMoon = new Vector3(Mathf.Sin(ma) * Mathf.Cos(me), Mathf.Sin(me), Mathf.Cos(ma) * Mathf.Cos(me));
                Shader.SetGlobalVector("_G3SunDir", ToMoon);
                Shader.SetGlobalVector("_G3SunColor", new Vector4(0.075f, 0.098f, 0.165f, 1));
                Shader.SetGlobalVector("_G3Sky", new Vector4(0.027f, 0.036f, 0.063f, 1));
                Shader.SetGlobalVector("_G3Bounce", new Vector4(0.020f, 0.022f, 0.020f, 1));
                Shader.SetGlobalVector("_G3Haze", new Vector4(0.012f, 0.016f, 0.030f, 0.75f));
                int nf = L.floods != null ? L.floods.Length : 0;
                Lamps = new Vector3[nf]; var lv = new Vector4[4];
                for (int i = 0; i < nf && i < 4; i++) { Lamps[i] = Field.P(L.floods[i].x * 0.97f, L.floods[i].y * 0.97f, L.floods[i].h); lv[i] = Lamps[i]; }
                Shader.SetGlobalVectorArray("_G3Lamps", lv);
                // 立体足球的亮面朝哪：月亮在塔的左边、很低，默认机位看过去球会几乎全在暗面；改成朝左前方那盏主泛光灯（影子也是它投的）
                Vector3 main = Vector3.zero; float best = float.MaxValue;
                foreach (var l in Lamps) if (l.x + l.z < best) { best = l.x + l.z; main = l; }
                Shader.SetGlobalVector("_G3BallLight", nf > 0 ? new Vector4(main.x, main.y - 1f, main.z, 0f).normalized + new Vector4(0, 0, 0, 1) : Vector4.zero);
                Shader.SetGlobalVector("_G3LampColor", new Vector4(0.95f, 0.975f, 1.0f, 0.55f));
                Shader.SetGlobalVector("_G3Court", Vector4.zero);
            }
            else
            {
                Shader.SetGlobalVector("_G3SunDir", ToSun);
                Shader.SetGlobalVector("_G3SunColor", new Vector4(0.80f, 0.76f, 0.68f, 1));
                Shader.SetGlobalVector("_G3Sky", new Vector4(0.34f, 0.40f, 0.50f, 1));
                Shader.SetGlobalVector("_G3Bounce", new Vector4(0.16f, 0.22f, 0.12f, 1));
                Shader.SetGlobalVector("_G3Haze", new Vector4(0.74f, 0.80f, 0.86f, 0.55f));
                Shader.SetGlobalVector("_G3LampColor", Vector4.zero);
                Shader.SetGlobalVector("_G3Court", Vector4.zero);
                Shader.SetGlobalVector("_G3BallLight", Vector4.zero);
            }
            Color grass = new Color(Info.grass[0], Info.grass[1], Info.grass[2], 1);
            bool band = L.band != null && L.band.tiles > 0;

            // ---- ground
            var white = new Texture2D(1, 1, TextureFormat.RGBA32, false); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.DontSave;
            var skirt = Mat("G3Unlit", white); skirt.SetColor("_Tint", Night ? new Color(0.010f, 0.011f, 0.012f) : grass);
            // 东京塔：地平线的地面压到 1.5 m 以下（全景带里 200 m 外的路灯、楼脚在它上面），公园外 200 m 以内是一块俯拍的地面
            Obj("Horizon ground", Quad(Field.P(-4000, -4000, band ? -1.5f : -0.05f), Field.P(4000, 4000, band ? -1.5f : -0.05f)), skirt);
            var outerTex = band ? Tex("ground_outer") : null;
            if (outerTex != null && L.outerR > 0)
            {
                outerTex.wrapMode = TextureWrapMode.Clamp;
                var m = Mat("G3Ground", outerTex); m.renderQueue = 2000;
                Obj("Outer ground", Disc(L.outerR, -0.02f), m);
            }
            var farTex = Tex("lawn_far");
            if (farTex != null)
            {
                var m = Mat("G3Ground", farTex);
                var det = Tex("lawn_detail");
                if (det != null) { m.SetTexture("_Detail", det); m.SetFloat("_DetailScale", 1f / 3.2f); }
                float h = L.farSize / 2;
                m.SetVector("_Size", new Vector4(L.farSize, L.farSize, 0, 0)); m.SetFloat("_Feather", 6f);
                m.renderQueue = 2001;
                Obj("Far lawn", Quad(Field.P(L.farCx - h, L.farCy - h, 0), Field.P(L.farCx + h, L.farCy + h, 0)), m);
            }
            var nearTex = Tex("lawn_near");
            if (nearTex != null)
            {
                var m = Mat("G3Ground", nearTex);
                m.SetVector("_Size", new Vector4(L.nearX1 - L.nearX0, L.nearY1 - L.nearY0, 0, 0)); m.SetFloat("_Feather", 1.2f);
                m.renderQueue = 2002;
                Obj("Near lawn", Quad(Field.P(L.nearX0, L.nearY0, 0.004f), Field.P(L.nearX1, L.nearY1, 0.004f)), m);
            }

            // ---- the White House (lighting baked into its texture)
            if (!Field.IsTokyo)
            {
                var hb = Resources.Load<TextAsset>("World/house");
                var houseTex = Tex("house");
                if (hb != null && houseTex != null) Obj("White House", LoadMesh(hb.bytes, "White House"), Mat("G3Unlit", houseTex));
                var hd = Resources.Load<TextAsset>("World/house_detail");
                if (hd != null) Obj("White House detail", LoadColouredMesh(hd.bytes, "White House detail"), Mat("G3VertexColor"));
            }
            else
            {
                // 东京塔：近处真实的楼（PLATEAU，离球场中点 185 m 以内：王子公园塔、增上寺……），光照在 Blender 里烘好（Pipeline/tokyo/t_nearbake.py）
                var nearBytes = Resources.Load<TextAsset>("World/tokyo/near");
                var nt = Resources.Load<Texture2D>("World/tokyo/near");
                if (nearBytes != null && nt != null) Obj("Near buildings", LoadMesh(nearBytes.bytes, "Near buildings"), Mat("G3Unlit", nt));
                var nl = Resources.Load<TextAsset>("World/tokyo/near_lights");
                if (nl != null)
                {
                    var r = JsonUtility.FromJson<RedLights>(nl.text);
                    if (r != null && r.red != null) { redLights = new Vector3[r.red.Length]; for (int i = 0; i < r.red.Length; i++) redLights[i] = Field.P(r.red[i].x, r.red[i].y, r.red[i].z); }
                }
            }
            if (band) BuildBand(L.band);

            // ---- goals, corner flags, the Monument (lit in real time, same sun)
            var frame = Mat("G3Lit"); frame.SetColor("_Color", new Color(0.93f, 0.93f, 0.92f));
            var net = Mat("G3Net");
            var mb = new MeshBuilder(); var nb = new MeshBuilder();
            foreach (int s in new[] { -1, 1 })
            {
                float gx = s * L.halfLength, bx = s * (L.halfLength + L.goalD), y0 = L.goalY0, y1 = L.goalY1, H = L.goalH, r = 0.06f;
                mb.Tube(Field.P(gx, y0, 0), Field.P(gx, y0, H), r); mb.Tube(Field.P(gx, y1, 0), Field.P(gx, y1, H), r);
                mb.Tube(Field.P(gx, y0 - r, H), Field.P(gx, y1 + r, H), r);
                mb.Tube(Field.P(bx, y0, 0), Field.P(bx, y0, H), r * 0.6f); mb.Tube(Field.P(bx, y1, 0), Field.P(bx, y1, H), r * 0.6f);
                mb.Tube(Field.P(bx, y0, H), Field.P(bx, y1, H), r * 0.6f);
                mb.Tube(Field.P(gx, y0, H), Field.P(bx, y0, H), r * 0.6f); mb.Tube(Field.P(gx, y1, H), Field.P(bx, y1, H), r * 0.6f);
                mb.Tube(Field.P(bx, y0, 0.03f), Field.P(bx, y1, 0.03f), r * 0.5f);
                mb.Tube(Field.P(gx, y0, 0.03f), Field.P(bx, y0, 0.03f), r * 0.5f); mb.Tube(Field.P(gx, y1, 0.03f), Field.P(bx, y1, 0.03f), r * 0.5f);
                float d = L.goalD, w = y1 - y0;
                // the netting is a fine grid, so that a shot can make it bulge and tear (see G3Net and Effects)
                nb.Grid(Field.P(bx, y0, 0), Field.P(bx, y1, 0), Field.P(bx, y1, H), Field.P(bx, y0, H), w, H, 28, 20);    // back
                nb.Grid(Field.P(gx, y0, H), Field.P(gx, y1, H), Field.P(bx, y1, H), Field.P(bx, y0, H), w, d, 28, 8);     // roof
                nb.Grid(Field.P(gx, y0, 0), Field.P(bx, y0, 0), Field.P(bx, y0, H), Field.P(gx, y0, H), d, H, 8, 20);     // sides
                nb.Grid(Field.P(gx, y1, 0), Field.P(bx, y1, 0), Field.P(bx, y1, H), Field.P(gx, y1, H), d, H, 8, 20);
            }
            Obj("Goal frames", mb.ToMesh("Goal frames"), frame);
            var netMesh = nb.ToMesh("Goal nets");
            netMesh.bounds = new Bounds(netMesh.bounds.center, netMesh.bounds.size + new Vector3(8f, 6f, 8f));       // room to bulge
            Obj("Goal nets", netMesh, net);
            Shader.SetGlobalVector("_G3NetHit", new Vector4(0, 0, 0, -1f)); Shader.SetGlobalVector("_G3NetPush", Vector4.zero);
            var pole = new MeshBuilder(); var cloth = new MeshBuilder();
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                {
                    float x = sx * L.halfLength, y = sy * L.halfWidth;
                    pole.Tube(Field.P(x, y, 0), Field.P(x, y, 1.5f), 0.016f);
                    cloth.Tri(Field.P(x, y, 1.5f), Field.P(x - 0.40f * sx, y + 0.05f, 1.37f), Field.P(x, y, 1.22f));
                }
            Obj("Flag poles", pole.ToMesh("Flag poles"), frame);
            var red = Mat("G3Lit"); red.SetColor("_Color", new Color(0.72f, 0.05f, 0.04f));
            Obj("Corner flags", cloth.ToMesh("Corner flags"), red);
            if (L.monumentH > 0f)
            {
                var stone = Mat("G3Lit"); stone.SetColor("_Color", new Color(0.80f, 0.78f, 0.74f));
                var mon = new MeshBuilder();
                mon.Frustum(Field.P(L.monumentX, L.monumentY, 0), L.monumentBase, L.monumentBase * 0.62f, L.monumentH * 0.90f);
                mon.Frustum(Field.P(L.monumentX, L.monumentY, L.monumentH * 0.90f), L.monumentBase * 0.62f, 0.01f, L.monumentH * 0.10f);
                Obj("Monument", mon.ToMesh("Monument"), stone);
            }
            if (Night) BuildNight();
            BuildParkLamps();

            // ---- trees
            var treeTex = Tex("trees");
            if (treeTex != null && Info.trees != null && Info.trees.species != null && L.trees != null)
            {
                int n = L.trees.Length;
                treeMesh = new Mesh(); treeMesh.name = "Trees"; treeMesh.MarkDynamic(); treeMesh.hideFlags = HideFlags.DontSave;
                treeVerts = new Vector3[n * 4]; treeUv0 = new Vector2[n * 4]; treeUv1 = new Vector2[n * 4]; treeCol = new Color32[n * 4];
                var tris = new int[n * 6];
                for (int i = 0; i < n; i++) { int v = i * 4, t = i * 6; tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1; tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2; }
                treeMesh.vertices = treeVerts; treeMesh.uv = treeUv0; treeMesh.uv2 = treeUv1; treeMesh.colors32 = treeCol; treeMesh.triangles = tris;
                treeMesh.bounds = new Bounds(Vector3.zero, new Vector3(2000, 200, 2000));
                var tm = Mat("G3Impostor", treeTex);
                if (Night) { tm.SetColor("_Tint", new Color(TreeTintScale, TreeTintScale, TreeTintScale, 1f)); LightTrees(); }      // 白天拍的照片：夜里每棵树受多少光写进顶点色
                Obj("Trees", treeMesh, tm);
            }

            // ---- sky
            var skyTex = Tex("sky");
            if (skyTex != null)
            {
                var sm = Mat("G3Sky", skyTex);
                sm.SetColor("_Ground", Night ? new Color(0.010f, 0.011f, 0.012f) : grass * 0.9f);
                RenderSettings.skybox = sm;
            }
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.4f, 0.45f, 0.5f);
        }

        [Serializable] class RedPoint { public float x, y, z; }
        [Serializable] class RedLights { public RedPoint[] red; }

        // 东京塔：公园以外的一切（塔、楼、远处的树和路）是 Pipeline/tokyo/t_pano.py 从 (0, 0, eyeZ) 渲的一圈全景带。
        // 贴回一个“按方向变半径”的圆筒上：从中心看方向和渲染时完全一致；半径是那个方向上挡在前面的楼有多远（塔的方向就是塔的距离），
        // 所以镜头离开中心几十米，大的东西仍在差不多对的地方。
        void BuildBand(BandDef b)
        {
            float span = 360f / b.tiles;
            int nr = b.radius != null ? b.radius.Length : 0;
            float step = b.step > 0 ? b.step : 360f / Mathf.Max(nr, 1);
            int nu = Mathf.Max(48, Mathf.RoundToInt(span / Mathf.Max(step, 0.25f))), nv = 34;
            for (int i = 0; i < b.tiles; i++)
            {
                var tex = Tex("band_" + i);
                if (tex == null) continue;
                tex.wrapMode = TextureWrapMode.Clamp;
                var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int iv = 0; iv <= nv; iv++)
                    for (int iu = 0; iu <= nu; iu++)
                    {
                        float azd = -span / 2 + span * i + span * iu / nu, az = azd * Mathf.Deg2Rad, lat = (b.lo + (b.hi - b.lo) * iv / nv) * Mathf.Deg2Rad;
                        float r = b.r;
                        if (nr > 0)
                        {
                            float f = Mathf.Repeat(azd, 360f) / step; int k0 = Mathf.FloorToInt(f) % nr, k1 = (k0 + 1) % nr;
                            r = Mathf.Lerp(b.radius[k0], b.radius[k1], f - Mathf.Floor(f));
                        }
                        // 圆筒：水平距离 r，高度 r·tan(纬度)（竖直的楼面、塔身在圆筒上是直的；球面会让高处的东西离得近）
                        vs.Add(new Vector3(Mathf.Sin(az) * r, b.eyeZ + r * Mathf.Tan(lat), Mathf.Cos(az) * r));       // Unity：x 右、y 上、z 朝塔
                        uv.Add(new Vector2((float)iu / nu, (float)iv / nv));
                    }
                for (int iv = 0; iv < nv; iv++)
                    for (int iu = 0; iu < nu; iu++)
                    {
                        int a0 = iv * (nu + 1) + iu, a1 = a0 + 1, b0 = a0 + nu + 1, b1 = b0 + 1;
                        tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1);
                    }
                var m = new Mesh(); m.name = "Band " + i; m.hideFlags = HideFlags.DontSave;
                m.SetVertices(vs); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.bounds = new Bounds(Vector3.zero, Vector3.one * 6000f);
                Obj("Band " + i, m, Mat("G3Band", tex));
            }
        }

        // 公园的路灯、大路的路灯（东京塔）：深色灯柱、暖白 / 冷白的灯罩，镜头里有一点光晕（UpdateNight）；公园边一圈绿篱
        void BuildParkLamps()
        {
            var L = Field.L;
            if (L.parkLamps == null || L.parkLamps.Length == 0) return;
            var post = Mat("G3Lit"); post.SetColor("_Color", new Color(0.05f, 0.055f, 0.06f));
            var shade = Mat("G3Lit"); shade.SetColor("_Color", new Color(1.0f, 0.80f, 0.56f, 6f));
            var street = Mat("G3Lit"); street.SetColor("_Color", new Color(0.95f, 0.97f, 1.0f, 7f));
            var pb = new MeshBuilder(); var sb = new MeshBuilder(); var lb = new MeshBuilder();
            ParkLamps = new Vector3[L.parkLamps.Length];
            for (int i = 0; i < L.parkLamps.Length; i++)
            {
                var d = L.parkLamps[i];
                if (d.h < 6f)
                {
                    pb.Tube(Field.P(d.x, d.y, 0), Field.P(d.x, d.y, d.h - 0.25f), 0.07f, 8);
                    pb.Box(Field.P(d.x - 0.16f, d.y - 0.16f, 0), Field.P(d.x + 0.16f, d.y + 0.16f, 0.35f));
                    sb.Box(Field.P(d.x - 0.17f, d.y - 0.17f, d.h - 0.3f), Field.P(d.x + 0.17f, d.y + 0.17f, d.h + 0.12f));
                    pb.Box(Field.P(d.x - 0.24f, d.y - 0.24f, d.h + 0.12f), Field.P(d.x + 0.24f, d.y + 0.24f, d.h + 0.2f));
                    ParkLamps[i] = Field.P(d.x, d.y, d.h - 0.08f);
                }
                else
                {
                    // 大路的 LED 路灯：8 m 的杆，顶上一块朝下的灯板
                    pb.Tube(Field.P(d.x, d.y, 0), Field.P(d.x, d.y, d.h), 0.09f, 8);
                    pb.Box(Field.P(d.x - 0.35f, d.y - 0.18f, d.h), Field.P(d.x + 0.35f, d.y + 0.18f, d.h + 0.12f));
                    lb.Box(Field.P(d.x - 0.30f, d.y - 0.14f, d.h - 0.02f), Field.P(d.x + 0.30f, d.y + 0.14f, d.h));
                    ParkLamps[i] = Field.P(d.x, d.y, d.h - 0.06f);
                }
            }
            Obj("Park lamp posts", pb.ToMesh("Park lamp posts"), post); Obj("Park lamp shades", sb.ToMesh("Park lamp shades"), shade);
            Obj("Street lamp heads", lb.ToMesh("Street lamp heads"), street);
            // 公园边一圈 1.7 m 的修剪绿篱（Pipeline/tokyo/t_hedge.py 拍的叶子贴图：正面、背面、顶面三张面片，每 8 m 一拼）
            var front = Tex("hedge_front"); var top = Tex("hedge_top");
            if (front != null && top != null && L.parkBox != null && L.parkBox.Length == 4)
            {
                float x0 = L.parkBox[0], x1 = L.parkBox[1], y0 = L.parkBox[2], y1 = L.parkBox[3], t = 0.6f;
                var fm = Mat("G3Hedge", front); fm.SetColor("_Tint", new Color(0.9f, 0.9f, 0.9f));
                var tm = Mat("G3Hedge", top); tm.SetColor("_Tint", new Color(0.9f, 0.9f, 0.9f));
                var fb = new MeshBuilder(); var tb = new MeshBuilder();
                var sides = new[] { new Vector4(x0, y1, x1, y1), new Vector4(x1, y0, x0, y0), new Vector4(x0, y0, x0, y1), new Vector4(x1, y1, x1, y0) };
                foreach (var sd in sides)
                {
                    Vector2 a = new Vector2(sd.x, sd.y), b = new Vector2(sd.z, sd.w), dir = (b - a).normalized, nrm = new Vector2(-dir.y, dir.x);
                    float len = (b - a).magnitude;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector2 o = nrm * (t * k);
                        Vector3 p0 = Field.P(a.x + o.x, a.y + o.y, 0), p1 = Field.P(b.x + o.x, b.y + o.y, 0);
                        fb.Quad(p0, p1, p1 + Vector3.up * 2.0f, p0 + Vector3.up * 2.0f, len / 8f, 1f);
                    }
                    Vector3 q0 = Field.P(a.x - nrm.x * 0.62f, a.y - nrm.y * 0.62f, 1.72f), q1 = Field.P(b.x - nrm.x * 0.62f, b.y - nrm.y * 0.62f, 1.72f);
                    Vector3 q2 = Field.P(b.x + nrm.x * 0.62f, b.y + nrm.y * 0.62f, 1.72f), q3 = Field.P(a.x + nrm.x * 0.62f, a.y + nrm.y * 0.62f, 1.72f);
                    tb.Quad(q0, q1, q2, q3, len / 8f, 1f);
                }
                Obj("Hedges", fb.ToMesh("Hedges"), fm); Obj("Hedge tops", tb.ToMesh("Hedge tops"), tm);
            }
        }

        // 夜里才有的：四根泛光灯杆、灯头的光晕、月亮（和篮球的夜景同一套）
        void BuildNight()
        {
            var L = Field.L;
            var mast = Mat("G3Lit"); mast.SetColor("_Color", new Color(0.22f, 0.23f, 0.25f));
            var lampFace = Mat("G3Lit"); lampFace.SetColor("_Color", new Color(0.95f, 0.975f, 1f, 9f));     // alpha 大于 1：自己发光
            var mb = new MeshBuilder(); var lb = new MeshBuilder();
            if (L.floods != null)
                foreach (var f in L.floods)
                {
                    Vector3 foot = Field.P(f.x, f.y, 0), top = Field.P(f.x, f.y, f.h + 0.6f);
                    mb.Tube(foot, top, 0.16f, 10);
                    Vector3 aim = (Field.P(f.tx, f.ty, 0) - Field.P(f.x, f.y, f.h)).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, aim).normalized, upv = Vector3.Cross(aim, side).normalized;
                    Vector3 c = Field.P(f.x * 0.97f, f.y * 0.97f, f.h);
                    mb.Tube(c - side * 1.5f - aim * 0.25f, c + side * 1.5f - aim * 0.25f, 0.07f, 8);
                    for (int r = 0; r < 2; r++)
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 q = c + side * ((k - 1.5f) * 0.74f) + upv * ((r - 0.5f) * 0.62f);
                            float hw = 0.30f, hh = 0.24f;
                            lb.Quad(q - side * hw - upv * hh, q + side * hw - upv * hh, q + side * hw + upv * hh, q - side * hw + upv * hh, 1, 1);
                            mb.Quad(q - aim * 0.2f - side * hw - upv * hh, q - aim * 0.2f - side * hw + upv * hh, q - aim * 0.2f + side * hw + upv * hh, q - aim * 0.2f + side * hw - upv * hh, 1, 1);
                        }
                }
            Obj("Masts", mb.ToMesh("Masts"), mast); Obj("Lamp faces", lb.ToMesh("Lamp faces"), lampFace);
            glowMesh = new Mesh(); glowMesh.name = "Glows"; glowMesh.MarkDynamic(); glowMesh.hideFlags = HideFlags.DontSave;
            Obj("Glows", glowMesh, Mat("G3Glow"));
            var moonTex = Tex("moon");
            if (moonTex != null)
            {
                float dist = 4200f, half = dist * Mathf.Tan(L.moonSize * 0.5f * Mathf.Deg2Rad);
                Vector3 c = ToMoon * dist, r = Vector3.Cross(Vector3.up, ToMoon).normalized, u = Vector3.Cross(ToMoon, r).normalized;
                var m = new Mesh(); m.hideFlags = HideFlags.DontSave;
                m.vertices = new[] { c - r * half - u * half, c + r * half - u * half, c + r * half + u * half, c - r * half + u * half };
                m.uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 }; m.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
                var mm = Mat("G3Moon", moonTex); mm.SetColor("_Tint", new Color(1.25f, 1.22f, 1.12f, 1f));
                Obj("Moon", m, mm);
            }
        }

        /// <summary>每画一张调一次（夜里）：灯头的光晕（永远朝着镜头）。</summary>
        public void UpdateNight(Camera cam, float dt)
        {
            if (!Night || glowMesh == null) return;
            gv.Clear(); guv.Clear(); gpar.Clear(); gcol.Clear(); gtri.Clear();
            Vector3 right = cam.transform.right, up = cam.transform.up, cp = cam.transform.position;
            foreach (var lp in Lamps)
            {
                Vector3 toCam = (cp - lp).normalized;
                float facing = Mathf.Clamp01(0.35f + 0.65f * Mathf.Max(0f, Vector3.Dot(toCam, (Vector3.zero - lp).normalized)));
                var cool = new Vector4(0.93f, 0.97f, 1.0f, 1);
                AddGlow(lp, 1.5f, 1.5f, cool * (3.2f * facing), 2.0f, right, up);
                AddGlow(lp, 5.0f, 5.0f, cool * (0.30f * facing), 4.5f, right, up);
                AddGlow(lp, 5.2f, 0.10f, cool * (1.1f * facing * facing), 3.0f, right, up);
                AddGlow(lp, 0.08f, 2.6f, cool * (0.8f * facing * facing), 3.0f, right, up);
            }
            var L = Field.L;
            for (int i = 0; i < ParkLamps.Length; i++)
            {
                Vector3 lp = ParkLamps[i];
                bool street = L.parkLamps != null && i < L.parkLamps.Length && L.parkLamps[i].h >= 6f;
                var col = street ? new Vector4(0.92f, 0.96f, 1.0f, 1) : new Vector4(1.0f, 0.80f, 0.58f, 1);
                float d = Mathf.Max(1f, (cp - lp).magnitude), s = Mathf.Clamp(d / 60f, 0.6f, 2.2f);
                AddGlow(lp, 0.35f * s, 0.35f * s, col * 2.2f, 2.0f, right, up);
                AddGlow(lp, 1.6f * s, 1.6f * s, col * 0.18f, 4.0f, right, up);
            }
            foreach (var rp in redLights)
            {
                float d = Mathf.Max(1f, (cp - rp).magnitude), s = Mathf.Clamp(d / 80f, 0.8f, 3f);
                AddGlow(rp, 0.5f * s, 0.5f * s, new Vector4(1.0f, 0.05f, 0.03f, 1) * 3.0f, 2.0f, right, up);
            }
            glowMesh.Clear();
            glowMesh.SetVertices(gv); glowMesh.SetUVs(0, guv); glowMesh.SetUVs(1, gcol); glowMesh.SetUVs(2, gpar); glowMesh.SetTriangles(gtri, 0, false);
            glowMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
        }

        void AddGlow(Vector3 p, float sx, float sy, Vector4 colour, float tight, Vector3 right, Vector3 up)
        {
            int v0 = gv.Count;
            gv.Add(p - right * sx - up * sy); gv.Add(p + right * sx - up * sy); gv.Add(p + right * sx + up * sy); gv.Add(p - right * sx + up * sy);
            guv.Add(new Vector2(-1, -1)); guv.Add(new Vector2(1, -1)); guv.Add(new Vector2(1, 1)); guv.Add(new Vector2(-1, 1));
            for (int i = 0; i < 4; i++) { gcol.Add(colour); gpar.Add(new Vector2(tight, 1)); }
            gtri.Add(v0); gtri.Add(v0 + 2); gtri.Add(v0 + 1); gtri.Add(v0); gtri.Add(v0 + 3); gtri.Add(v0 + 2);
        }

        /// <summary>Turns every tree's picture towards the camera and picks the photographs for that direction.</summary>
        public void UpdateTrees(Vector3 camPos)
        {
            if (treeMesh == null) return;
            var L = Field.L; var A = Info.trees;
            int V = A.views;
            float iw = 1f / A.atlasW, ih = 1f / A.atlasH;
            for (int i = 0; i < L.trees.Length; i++)
            {
                var t = L.trees[i]; var sp = A.species[t.species];
                Vector3 p = Field.P(t.x, t.y, 0);
                Vector3 f = p - camPos; f.y = 0;
                float dist = f.magnitude; if (dist < 0.01f) dist = 0.01f;
                f /= dist;
                Vector3 r = new Vector3(f.z, 0, -f.x);
                float w = sp.widthPerHeight * t.h, h = sp.heightPerHeight * t.h, b = sp.@base * h;
                int v = i * 4;
                treeVerts[v] = p - r * (w / 2) + Vector3.up * -b;
                treeVerts[v + 1] = p + r * (w / 2) + Vector3.up * -b;
                treeVerts[v + 2] = p + r * (w / 2) + Vector3.up * (h - b);
                treeVerts[v + 3] = p - r * (w / 2) + Vector3.up * (h - b);
                float phi = Mathf.Atan2(camPos.z - p.z, camPos.x - p.x);              // direction tree -> camera (east = 0, north = 90 degrees)
                float fv = Mathf.Repeat(phi / (2 * Mathf.PI) * V, V);
                int k0 = Mathf.FloorToInt(fv) % V, k1 = (k0 + 1) % V;
                byte blend = (byte)Mathf.RoundToInt((fv - Mathf.Floor(fv)) * 255f);
                int row = t.species * 2 + t.variant;
                SetUv(treeUv0, v, k0, row, A, sp, iw, ih); SetUv(treeUv1, v, k1, row, A, sp, iw, ih);
                if (treeLight == null) { var c = new Color32(255, 255, 255, blend); treeCol[v] = treeCol[v + 1] = treeCol[v + 2] = treeCol[v + 3] = c; }
                else
                {
                    // 四个角：画面的左右、上下。朝球场的一侧受泛光灯
                    Vector3 toCourt = new Vector3(-p.x, 0, -p.z).normalized;
                    float sideR = Vector3.Dot(r, toCourt);
                    for (int q = 0; q < 4; q++)
                    {
                        bool rightEdge = q == 1 || q == 2, top = q >= 2;
                        float side = Mathf.Clamp01(0.62f + 0.55f * (rightEdge ? sideR : -sideR));
                        Vector3 l = treeLight[i * 2] + treeLight[i * 2 + 1] * side * (top ? 1f : 0.85f);
                        treeCol[v + q] = new Color32(ToByte(l.x), ToByte(l.y), ToByte(l.z), blend);
                    }
                }
            }
            treeMesh.vertices = treeVerts; treeMesh.uv = treeUv0; treeMesh.uv2 = treeUv1; treeMesh.colors32 = treeCol;
        }

        const float TreeTintScale = 0.6f;       // 夜里树材质的色调；顶点色以它为单位
        Vector3[] treeLight;                    // 每棵树：天空和月亮给的、泛光灯给的
        static byte ToByte(float x) { return (byte)Mathf.Clamp(Mathf.RoundToInt(x / TreeTintScale * 255f), 0, 255); }

        // 夜里每棵树按照到它的光着色（同篮球的“夜景光照调真实”）：天空和月光 + 四根泛光灯（按距离平方衰减，以球场中间受到的为单位；
        // 被泛光灯照到的东西大约是太阳下的六分之一，只给 0.6 倍的提示——夜里的树主要还是暗的形状）
        void LightTrees()
        {
            var L = Field.L;
            treeLight = new Vector3[L.trees.Length * 2];
            float atCentre = 0f;
            foreach (var f in L.floods) atCentre += 1f / (f.x * f.x + f.y * f.y + f.h * f.h);
            for (int i = 0; i < L.trees.Length; i++)
            {
                var t = L.trees[i];
                float e = 0f;
                foreach (var f in L.floods) { float dx = t.x - f.x, dy = t.y - f.y; e += 1f / (dx * dx + dy * dy + f.h * f.h); }
                float flood = 0.167f * 0.62f * Mathf.Pow(e / atCentre, 1.35f);
                treeLight[i * 2] = new Vector3(0.050f, 0.062f, 0.090f);
                treeLight[i * 2 + 1] = new Vector3(0.95f, 0.975f, 1.0f) * flood;
            }
        }

        static void SetUv(Vector2[] uv, int v, int col, int row, TreeAtlasInfo A, TreeSpeciesInfo sp, float iw, float ih)
        {
            // the atlas has 'cols' pictures per row; a species / variant takes rowsPerVariant rows
            int cols = A.cols > 0 ? A.cols : A.views, rpv = A.rowsPerVariant > 0 ? A.rowsPerVariant : 1;
            row = row * rpv + col / cols; col %= cols;
            float u0 = (col * A.cellW + 0.5f) * iw, u1 = (col * A.cellW + sp.frameW - 0.5f) * iw;
            float top = 1f - (row * A.cellH + 0.5f) * ih, bottom = 1f - (row * A.cellH + sp.frameH - 0.5f) * ih;
            uv[v] = new Vector2(u0, bottom); uv[v + 1] = new Vector2(u1, bottom); uv[v + 2] = new Vector2(u1, top); uv[v + 3] = new Vector2(u0, top);
        }

        static Mesh Quad(Vector3 a, Vector3 c)
        {
            var m = new Mesh(); m.hideFlags = HideFlags.DontSave;
            m.vertices = new[] { new Vector3(a.x, a.y, a.z), new Vector3(c.x, a.y, a.z), new Vector3(c.x, a.y, c.z), new Vector3(a.x, a.y, c.z) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A flat disc of radius r round the middle of the pitch, uv = the square [-r, r]^2 (a top-down photograph).</summary>
        static Mesh Disc(float r, float z, int n = 96, int rings = 6)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            v.Add(Field.P(0, 0, z)); uv.Add(new Vector2(0.5f, 0.5f));
            for (int k = 1; k <= rings; k++)
                for (int i = 0; i < n; i++)
                {
                    float a = 2 * Mathf.PI * i / n, rr = r * k / rings;
                    float x = rr * Mathf.Sin(a), y = rr * Mathf.Cos(a);
                    v.Add(Field.P(x, y, z)); uv.Add(new Vector2(0.5f + x / (2 * r), 0.5f + y / (2 * r)));
                }
            for (int i = 0; i < n; i++) { t.Add(0); t.Add(1 + (i + 1) % n); t.Add(1 + i); }
            for (int k = 1; k < rings; k++)
                for (int i = 0; i < n; i++)
                {
                    int a0 = 1 + (k - 1) * n + i, a1 = 1 + (k - 1) * n + (i + 1) % n, b0 = a0 + n, b1 = a1 + n;
                    t.Add(a0); t.Add(a1); t.Add(b0); t.Add(a1); t.Add(b1); t.Add(b0);
                }
            var m = new Mesh(); m.hideFlags = HideFlags.DontSave; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>'G3V1': count, positions (3 floats each), colours (RGBA bytes) - unindexed triangles.</summary>
        static Mesh LoadColouredMesh(byte[] d, string name)
        {
            int n = BitConverter.ToInt32(d, 4);
            var v = new Vector3[n]; var c = new Color32[n]; var idx = new int[n];
            int o = 8;
            for (int i = 0; i < n; i++, o += 12) v[i] = new Vector3(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8));
            for (int i = 0; i < n; i++, o += 4) { c[i] = new Color32(d[o], d[o + 1], d[o + 2], 255); idx[i] = i; }
            var m = new Mesh(); m.name = name; m.hideFlags = HideFlags.DontSave;
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.colors32 = c; m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }

        static Mesh LoadMesh(byte[] d, string name)
        {
            int n = BitConverter.ToInt32(d, 4);
            var v = new Vector3[n]; var uv = new Vector2[n]; var idx = new int[n];
            int o = 8;
            for (int i = 0; i < n; i++, o += 12) v[i] = new Vector3(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8));
            for (int i = 0; i < n; i++, o += 8) { uv[i] = new Vector2(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4)); idx[i] = i; }
            var m = new Mesh(); m.name = name; m.hideFlags = HideFlags.DontSave;
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.uv = uv; m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Tiny helper for the few procedural shapes.</summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>(), uv2 = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public void Tube(Vector3 a, Vector3 b, float r, int sides = 10)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 side2 = Vector3.Cross(axis, side);
            int v0 = v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float ang = 2 * Mathf.PI * i / sides;
                Vector3 d = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                v.Add(a + d * r); n.Add(d); uv.Add(Vector2.zero);
                v.Add(b + d * r); n.Add(d); uv.Add(Vector2.zero);
            }
            for (int i = 0; i < sides; i++)
            {
                int p = v0 + i * 2;
                t.Add(p); t.Add(p + 1); t.Add(p + 3); t.Add(p); t.Add(p + 3); t.Add(p + 2);
            }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uLen, float vLen)
        {
            int v0 = v.Count;
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int i = 0; i < 4; i++) n.Add(nn);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(uLen, 0)); uv.Add(new Vector2(uLen, vLen)); uv.Add(new Vector2(0, vLen));
            t.Add(v0); t.Add(v0 + 1); t.Add(v0 + 2); t.Add(v0); t.Add(v0 + 2); t.Add(v0 + 3);
        }

        /// <summary>The same rectangle as a grid of nu x nv cells.  The second UV set says how free a point is to move:
        /// 0 along the four edges (tied to the frame), 1 in the middle.</summary>
        public void Grid(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uLen, float vLen, int nu, int nv)
        {
            int v0 = v.Count;
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float s = i / (float)nu, tt = j / (float)nv;
                    v.Add(Vector3.Lerp(Vector3.Lerp(a, b, s), Vector3.Lerp(d, c, s), tt)); n.Add(nn);
                    uv.Add(new Vector2(uLen * s, vLen * tt));
                    float es = 2f * s - 1f, et = 2f * tt - 1f;
                    uv2.Add(new Vector2((1f - es * es * es * es) * (1f - et * et * et * et), 0f));
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int p = v0 + j * (nu + 1) + i;
                    t.Add(p); t.Add(p + 1); t.Add(p + nu + 2); t.Add(p); t.Add(p + nu + 2); t.Add(p + nu + 1);
                }
        }

        /// <summary>Axis-aligned box between two opposite corners.</summary>
        public void Box(Vector3 a, Vector3 b)
        {
            Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
            Vector3 p000 = lo, p111 = hi, p100 = new Vector3(hi.x, lo.y, lo.z), p010 = new Vector3(lo.x, hi.y, lo.z), p001 = new Vector3(lo.x, lo.y, hi.z),
                    p110 = new Vector3(hi.x, hi.y, lo.z), p101 = new Vector3(hi.x, lo.y, hi.z), p011 = new Vector3(lo.x, hi.y, hi.z);
            Quad(p001, p101, p111, p011, 1, 1); Quad(p100, p000, p010, p110, 1, 1);
            Quad(p101, p100, p110, p111, 1, 1); Quad(p000, p001, p011, p010, 1, 1);
            Quad(p011, p111, p110, p010, 1, 1); Quad(p000, p100, p101, p001, 1, 1);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int v0 = v.Count;
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
            uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero);
            t.Add(v0); t.Add(v0 + 1); t.Add(v0 + 2);
        }

        /// <summary>Square frustum standing on 'foot' (side lengths at the bottom and at the top).</summary>
        public void Frustum(Vector3 foot, float bottom, float top, float height)
        {
            float b = bottom / 2, tp = top / 2;
            Vector3[] lo = { foot + new Vector3(-b, 0, -b), foot + new Vector3(b, 0, -b), foot + new Vector3(b, 0, b), foot + new Vector3(-b, 0, b) };
            Vector3[] hi = { foot + new Vector3(-tp, height, -tp), foot + new Vector3(tp, height, -tp), foot + new Vector3(tp, height, tp), foot + new Vector3(-tp, height, tp) };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(lo[j], lo[i], hi[i], hi[j], 1, 1);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh(); m.name = name; m.hideFlags = HideFlags.DontSave;
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            if (uv2.Count == v.Count) m.SetUVs(1, uv2);
            m.RecalculateBounds();
            return m;
        }
    }
}
