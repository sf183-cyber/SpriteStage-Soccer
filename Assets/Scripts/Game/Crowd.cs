// People at the touchline: fans, cheerleaders, photographers.  Not part of the original game - drawn in its style
// (Art/Crowd) and shown the way the players are: pixel figures on quads that face the camera, with shadows on the turf.
// Key V switches them on and off.
using System.Collections.Generic;
using UnityEngine;

namespace Soccer
{
    public sealed class Crowd : MonoBehaviour
    {
        public const int FrameW = 64, FrameH = 72, FootRow = 64;     // one frame of the art; the feet stand on row 64
        public bool On = true;
        GameHost host; Camera cam; SpriteField sprites;
        Texture2D atlas; int atlasW, atlasH;
        readonly Dictionary<string, int[]> art = new Dictionary<string, int[]>();     // name -> first frame, number of frames
        Mesh mesh, shadowMesh;
        readonly List<Vector3> verts = new List<Vector3>(512), sverts = new List<Vector3>(512);
        readonly List<Vector2> uvs = new List<Vector2>(512), layers = new List<Vector2>(512);
        readonly List<Vector4> cells = new List<Vector4>(512);
        readonly List<int> tris = new List<int>(768);
        float clock, cheer;
        Material shadowMat, crowdMat; bool nightShadow; float silhouette = -1f;

        /// <summary>原版落雷时把精灵调色板全换成黑色，球员都成了黑剪影（全屏一白）。场边的人不走游戏的调色板，
        /// 这时也要变黑，不然在一片黑剪影里只有观众是彩色的。</summary>
        static bool SpritesBlack(byte[] pal)
        {
            int dark = 0;
            for (int p = 0; p < 4; p++)
                for (int k = 1; k < 4; k++)
                {
                    int c = pal[0x10 + p * 4 + k] & 0x3F;
                    if ((c & 0x0F) >= 0x0E || c == 0x0D || c == 0x1D) dark++;
                }
            return dark >= 10;
        }

        sealed class Person
        {
            public int First, Frames;
            public Vector3 Pos;
            public float Phase, Rate;       // of the two-frame loop
            public bool Seated;             // does not hop when a goal falls
            public float Walk;              // strolls this far to either side of Pos (the vendor)
            public bool Flip;
        }
        readonly List<Person> people = new List<Person>(64);

        public void Init(GameHost h, Camera c, SpriteField s)
        {
            host = h; cam = c; sprites = s;
            LoadArt();
            Place();
            mesh = NewMesh("Crowd"); shadowMesh = NewMesh("CrowdShadows");
            crowdMat = MakeChild("Crowd", mesh, "Shaders/G3Sprite").sharedMaterial;
            var sh = MakeChild("CrowdShadows", shadowMesh, "Shaders/G3SpriteShadow");
            shadowMat = sh.sharedMaterial;
            shadowMat.SetColor("_ShadowColor", new Color(0.36f, 0.44f, 0.42f, 1f));
        }

        void LoadArt()
        {
            var index = Resources.Load<TextAsset>("Crowd/index");
            var names = new List<string>(); var counts = new List<int>(); int total = 0;
            foreach (var line in index.text.Split('\n'))
            {
                var p = line.Trim().Split(' ');
                if (p.Length < 2) continue;
                names.Add(p[0]); counts.Add(int.Parse(p[1])); total += counts[counts.Count - 1];
            }
            const int perRow = 16;
            atlasW = perRow * FrameW; atlasH = Mathf.NextPowerOfTwo(((total + perRow - 1) / perRow) * FrameH);
            var px = new Color32[atlasW * atlasH];                  // transparent texels stay black, as the sprite shader expects
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            int n = 0;
            for (int i = 0; i < names.Count; i++)
            {
                var bytes = Resources.Load<TextAsset>("Crowd/" + names[i]);
                if (bytes == null || !tmp.LoadImage(bytes.bytes, false)) { Debug.LogError("[SpriteStage] crowd art missing: " + names[i]); continue; }
                var src = tmp.GetPixels32();
                art[names[i]] = new[] { n, counts[i] };
                for (int f = 0; f < counts[i]; f++, n++)
                {
                    int ax = (n % perRow) * FrameW, ay = (n / perRow) * FrameH;
                    for (int y = 0; y < FrameH; y++)
                        for (int x = 0; x < FrameW; x++)
                        {
                            Color32 c = src[y * tmp.width + f * FrameW + x];
                            px[(ay + y) * atlasW + ax + x] = c.a >= 128 ? new Color32(c.r, c.g, c.b, 255) : new Color32(0, 0, 0, 0);
                        }
                }
            }
            if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
            atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false, false);
            atlas.filterMode = FilterMode.Bilinear; atlas.wrapMode = TextureWrapMode.Clamp; atlas.name = "CrowdAtlas"; atlas.hideFlags = HideFlags.DontSave;
            atlas.SetPixels32(px); atlas.Apply(false, true);
        }

        void Add(string name, float east, float north, float rate = 2.2f, float phase = -1f, bool seated = false, float walk = 0f)
        {
            int[] a;
            if (!art.TryGetValue(name, out a)) return;
            // a little disorder, the same every run
            uint hsh = (uint)(people.Count * 2654435761u) ^ 0x9E3779B9u; hsh ^= hsh >> 15; hsh *= 2246822519u; hsh ^= hsh >> 13;
            float r1 = (hsh & 1023) / 1023f, r2 = ((hsh >> 10) & 1023) / 1023f, r3 = ((hsh >> 20) & 1023) / 1023f;
            people.Add(new Person
            {
                First = a[0], Frames = a[1], Seated = seated, Walk = walk,
                Pos = Field.P(east + (r1 - 0.5f) * 0.3f, north + (r2 - 0.5f) * 0.3f),
                Phase = phase >= 0f ? phase : r3 * 2f, Rate = rate * (phase >= 0f ? 1f : 0.85f + 0.3f * r1),
                Flip = east > 0f
            });
        }

        /// <summary>换场景以后重新站位（两个场景的球场一样大，位置不变）。</summary>
        public void Restage() { people.Clear(); Place(); }

        // Where everybody stands.  The pitch runs east-west; the far (north) touchline is the one the White House is behind.
        void Place()
        {
            var L = Field.L;
            float front = L.halfWidth + 1.5f, back = L.halfWidth + 3.0f;
            // the rows start `gap` either side of the half-way line
            float gap = 1.2f;
            // front row: a cheer squad either side of the middle, flags, drums and security towards the corner flags
            string[] left1 = { "agent", "flag", "drum", "cheer_red", "cheer_blue", "mega" };
            string[] right1 = { "baton", "cheer_yellow", "cheer_pink", "horn", "flag", "agent" };
            Row(left1, right1, gap, L.halfLength - 1.0f, (n, i, x) => Add(n, x, front, n.StartsWith("cheer_") ? 2.6f : 2.2f, n.StartsWith("cheer_") ? (x < 0 ? 0f : 1f) : -1f));
            // back row: the ones who came to eat, sleep and shout
            string[] left2 = { "wave", "popcorn", "grandpa", "horn" };
            string[] right2 = { "shock_pink", "cry_pink", "popcorn", "wave" };
            Row(left2, right2, gap + 1.0f, L.halfLength - 2.2f, (n, i, x) => Add(n, x, back, n == "grandpa" ? 0.9f : 2.0f, -1f, n == "grandpa" || n == "popcorn"));
            // the drinks vendors walk up and down behind them, one on each side
            Add("vendor", -9f, back + 1.3f, 3.0f, -1f, true, 3.5f);
            Add("vendor", 9f, back + 1.3f, 3.0f, -1f, true, 3.5f);
            // press photographers kneel beside both goals
            float gx = L.halfLength + 1.1f;
            foreach (float sx in new[] { -1f, 1f })
            {
                Add("photo", sx * gx, L.goalY1 + 2.6f, 1.2f, -1f, true);
                Add("photo", sx * gx, L.goalY0 - 2.6f, 1.2f, -1f, true);
            }
        }

        // Spreads one row over the two stretches from `inner` to `outer` either side of the half-way line (west then east).
        static void Row(string[] west, string[] east, float inner, float outer, System.Action<string, int, float> put)
        {
            for (int i = 0; i < west.Length; i++)
                put(west[i], i, -outer + (outer - inner) * (west.Length == 1 ? 1f : i / (float)(west.Length - 1)));
            for (int i = 0; i < east.Length; i++)
                put(east[i], i, inner + (outer - inner) * (east.Length == 1 ? 0f : i / (float)(east.Length - 1)));
        }

        static Mesh NewMesh(string name) { var m = new Mesh(); m.name = name; m.MarkDynamic(); m.hideFlags = HideFlags.DontSave; return m; }

        MeshRenderer MakeChild(string name, Mesh m, string shader)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(Resources.Load<Shader>(shader)); mat.mainTexture = atlas; mat.hideFlags = HideFlags.DontSave;
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return r;
        }

        void Update() { if (Application.isPlaying && Keys.Down(KeyCode.V)) On = !On; }

        /// <summary>Call once per picture, after the camera is placed.</summary>
        public void Frame(float dt)
        {
            verts.Clear(); sverts.Clear(); uvs.Clear(); cells.Clear(); layers.Clear(); tris.Clear();
            var br = host.Bridge;
            float sil = host.Nes != null && SpritesBlack(host.Nes.Ppu.Palette) ? 1f : 0f;
            if (sil != silhouette && crowdMat != null) { silhouette = sil; crowdMat.SetFloat("_Silhouette", sil); }
            if (On && br != null && br.InMatch && br.TickCount > 0)
            {
                // a goal: everybody speeds up and hops for as long as the game celebrates
                bool goal = br.SubScreen == 3;
                cheer = Mathf.MoveTowards(cheer, goal ? 1f : 0f, dt * (goal ? 4f : 1.2f));
                clock += dt * (1f + 1.6f * cheer);
                float s = Field.L.kx;
                Vector3 right = cam.transform.right, up = cam.transform.up;
                bool night = sprites != null && sprites.Lamps.Length > 0;
                if (night != nightShadow && shadowMat != null) { nightShadow = night; shadowMat.SetColor("_ShadowColor", night ? new Color(0.60f, 0.62f, 0.68f, 1f) : new Color(0.36f, 0.44f, 0.42f, 1f)); }
                Vector2 sun = sprites != null ? sprites.SunTravel : new Vector2(0.86f, 0.5f);
                float cot = sprites != null ? sprites.SunCot : 1.28f;
                Vector3 sunDir = new Vector3(sun.x, 0, sun.y).normalized, across = new Vector3(sunDir.z, 0, -sunDir.x);
                if (Vector3.Dot(across, right) < 0) across = -across;
                float iw = 1f / atlasW, ih = 1f / atlasH;
                for (int i = 0; i < people.Count; i++)
                {
                    var p = people[i];
                    Vector3 pos = p.Pos;
                    float turn;
                    if (p.Walk > 0f)
                    {
                        float a = clock * 0.22f / p.Walk * 2f + p.Phase * 3f;
                        pos.x += Mathf.Sin(a) * p.Walk;
                        turn = Mathf.Cos(a) * right.x;                           // faces the way he walks
                    }
                    else turn = Vector3.Dot(-pos, right) + (p.Flip ? -0.8f : 0.8f); // faces the middle of the pitch; the offset stops flickering
                    if (Mathf.Abs(turn) > 1e-3f) p.Flip = turn < 0f;
                    float t = clock * p.Rate + p.Phase;
                    // two frames: A, B, A, B ...  more: pairs of frames are separate actions, each kept for 8 beats;
                    // while a goal is celebrated he stays on the second action (shouting)
                    int f = p.First;
                    if (p.Frames > 2) f += (goal ? 1 : ((int)Mathf.Floor(t / 8f)) % (p.Frames / 2)) * 2 + (((int)Mathf.Floor(t)) & 1);
                    else if (p.Frames > 1) f += ((int)Mathf.Floor(t)) & 1;
                    float hop = p.Seated ? 0f : cheer * Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * 6f * s;
                    float xl = -FrameW * 0.5f * s, xr = FrameW * 0.5f * s, yb = -(FrameH - FootRow) * s + hop, yt = FootRow * s + hop;
                    int v0 = verts.Count;
                    verts.Add(pos + right * xl + up * yb); verts.Add(pos + right * xr + up * yb);
                    verts.Add(pos + right * xr + up * yt); verts.Add(pos + right * xl + up * yt);
                    if (night)
                    {
                        // 夜里：和球员一样，只按一盏泛光灯投影子
                        Vector3 lamp = sprites.MainLamp;
                        Vector3 away = new Vector3(pos.x - lamp.x, 0f, pos.z - lamp.z).normalized, side = new Vector3(away.z, 0f, -away.x);
                        if (Vector3.Dot(side, right) < 0f) side = -side;
                        float hb = Mathf.Max(yb, 0f), ht = Mathf.Max(yt, 0f);
                        sverts.Add(SpriteField.FromLamp(pos + side * xl + Vector3.up * hb, lamp)); sverts.Add(SpriteField.FromLamp(pos + side * xr + Vector3.up * hb, lamp));
                        sverts.Add(SpriteField.FromLamp(pos + side * xr + Vector3.up * ht, lamp)); sverts.Add(SpriteField.FromLamp(pos + side * xl + Vector3.up * ht, lamp));
                    }
                    else
                    {
                    Vector3 g = new Vector3(pos.x, 0.012f, pos.z);
                    float a0 = Mathf.Max(yb, 0) * cot, a1 = Mathf.Max(yt, 0) * cot;
                    sverts.Add(g + across * xl + sunDir * a0); sverts.Add(g + across * xr + sunDir * a0);
                    sverts.Add(g + across * xr + sunDir * a1); sverts.Add(g + across * xl + sunDir * a1);
                    }
                    // (LoadImage hands the art over bottom row first, so in the atlas v grows upwards like in the picture)
                    int ax = (f % 16) * FrameW, ay = (f / 16) * FrameH;
                    float u0 = ax * iw, u1 = (ax + FrameW) * iw, w0 = ay * ih, w1 = (ay + FrameH) * ih;
                    if (p.Flip) { float q = u0; u0 = u1; u1 = q; }
                    uvs.Add(new Vector2(u0, w0)); uvs.Add(new Vector2(u1, w0)); uvs.Add(new Vector2(u1, w1)); uvs.Add(new Vector2(u0, w1));
                    var cell = new Vector4((ax + 0.5f) * iw, (ay + 0.5f) * ih, (ax + FrameW - 0.5f) * iw, (ay + FrameH - 0.5f) * ih);
                    cells.Add(cell); cells.Add(cell); cells.Add(cell); cells.Add(cell);
                    var layer = new Vector2(1, 0); layers.Add(layer); layers.Add(layer); layers.Add(layer); layers.Add(layer);
                    tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 1); tris.Add(v0); tris.Add(v0 + 3); tris.Add(v0 + 2);
                }
            }
            Fill(mesh, verts); Fill(shadowMesh, sverts);
        }

        void Fill(Mesh m, List<Vector3> v)
        {
            m.Clear();
            if (v.Count == 0) return;
            m.SetVertices(v); m.SetUVs(0, uvs); m.SetUVs(1, cells); m.SetUVs(2, layers); m.SetTriangles(tris, 0, false);
            m.bounds = new Bounds(Vector3.zero, new Vector3(400, 100, 400));
        }
    }
}
