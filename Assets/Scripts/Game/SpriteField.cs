// Draws the players, the ball and their shadows in the 3D world from the bridge's ticks.
using System.Collections.Generic;
using UnityEngine;
using NesCore;

namespace Soccer
{
    public sealed class SpriteField : MonoBehaviour
    {
        public GameHost Host;
        public Camera Cam;
        public SpriteAtlas Atlas;
        public float SunCot = 1.28f;                    // shadow length per unit height
        public Vector2 SunTravel = new Vector2(0.86f, 0.5f);
        /// <summary>夜里（东京塔夜场）：泛光灯挂在哪（Unity 坐标）。空的时候按太阳投影子。只用一盏投影子（四盏灯会投出四道
        /// 交叉的影子，看着乱）：默认机位看过去左前方那盏，影子往右后方倒，和白天的太阳差不多。</summary>
        public Vector3[] Lamps = new Vector3[0];
        Material shadowMat;
        public Vector3 MainLamp
        {
            get
            {
                Vector3 best = Vector3.zero; float score = float.MaxValue;
                foreach (var l in Lamps) { float s = l.x + l.z; if (s < score) { score = s; best = l; } }
                return best;
            }
        }

        /// <summary>白天：太阳；夜里：泛光灯（lamps 不空）。换场景时调。</summary>
        public void SetLight(Vector2 sunTravel, float sunCot, Vector3[] lamps)
        {
            SunTravel = sunTravel; SunCot = sunCot; Lamps = lamps ?? new Vector3[0];
            if (shadowMat != null) shadowMat.SetColor("_ShadowColor", Lamps.Length > 0 ? new Color(0.60f, 0.62f, 0.68f, 1f) : new Color(0.36f, 0.44f, 0.42f, 1f));
        }

        /// <summary>灯把一点的影子投在地上的哪里</summary>
        public static Vector3 FromLamp(Vector3 p, Vector3 lamp)
        {
            float t = lamp.y / Mathf.Max(lamp.y - Mathf.Max(p.y, 0f), 0.5f);
            Vector3 g = lamp + (p - lamp) * t; g.y = 0.012f;
            return g;
        }
        Mesh spriteMesh, shadowMesh;
        readonly List<Vector3> verts = new List<Vector3>(1024), sverts = new List<Vector3>(1024);
        readonly List<Vector2> uvs = new List<Vector2>(1024);
        readonly List<Vector4> cells = new List<Vector4>(1024);
        readonly List<Vector2> layers = new List<Vector2>(1024);
        readonly List<int> tris = new List<int>(1536);
        Vector3[] worldPos = new Vector3[SoccerBridge.Slots];
        float[] height = new float[SoccerBridge.Slots];

        // ---- the ball in the round (G3Ball): the plain ball is a sphere that turns as it rolls, in place of its two drawings.
        // Squashed balls and special shots keep the original drawings.
        /// <summary>0: the original drawings, 1: a smooth sphere, 2: a sphere drawn in art pixels like the figures.  (U key, or -ballStyle n)</summary>
        public int BallStyle = 2;
        public const int BallStyles = 3;
        /// <summary>The ball is drawn as the sphere in this picture.</summary>
        public bool BallRound { get; private set; }
        Mesh ballMesh; Material ballMat; MeshRenderer ballRenderer;
        Quaternion ballSpin = Quaternion.identity;
        Vector3 ballWas; bool ballWasSet;
        const float BallRadiusPx = 7f;                  // the original drawing is 14 pixels across, in the middle of its 16 x 16 box
        readonly List<Vector3> bverts = new List<Vector3>(4);
        static readonly Vector2[] BallUv = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        static readonly int[] BallTris = { 0, 2, 1, 0, 3, 2 };

        public void Init(GameHost host, Camera cam)
        {
            Host = host; Cam = cam;
            Atlas = new SpriteAtlas(host.Art);
            spriteMesh = NewMesh("Sprites"); shadowMesh = NewMesh("SpriteShadows");
            MakeChild("Sprites", spriteMesh, "Shaders/G3Sprite");
            var sh = MakeChild("SpriteShadows", shadowMesh, "Shaders/G3SpriteShadow");
            shadowMat = sh.sharedMaterial;
            shadowMat.SetColor("_ShadowColor", new Color(0.36f, 0.44f, 0.42f, 1f));
            ballMesh = NewMesh("Ball");
            if (Resources.Load<Shader>("Shaders/G3Ball") != null)
            {
                ballRenderer = MakeChild("Ball", ballMesh, "Shaders/G3Ball"); ballMat = ballRenderer.sharedMaterial;
            }
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-ballStyle") { int n; if (int.TryParse(args[i + 1], out n)) BallStyle = Mathf.Clamp(n, 0, BallStyles - 1); }
        }

        void Update() { if (Application.isPlaying && Keys.Down(KeyCode.Z)) BallStyle = (BallStyle + 1) % BallStyles; }

        // The sphere in place of the ball's drawing: turned by as much as it has rolled since the last picture.
        void RoundBall(Vector3 at, Vector3 centre, Vector3 right, Vector3 up, Vector3 fwd, float s, byte[] pal, int palette)
        {
            if (ballWasSet)
            {
                Vector3 moved = at - ballWas; moved.y = 0f;
                float dist = moved.magnitude;
                if (dist > 1e-4f && dist < 1.5f)                 // (further: it was put somewhere else, not rolled there)
                {
                    float angle = Mathf.Min(dist / (BallRadiusPx * s), 0.55f);          // capped: a very fast ball would only flicker
                    ballSpin = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, moved / dist)) * ballSpin;
                }
            }
            ballWas = at; ballWasSet = true;
            float r = BallRadiusPx * s;
            bverts.Clear();
            bverts.Add(centre - right * r - up * r); bverts.Add(centre + right * r - up * r); bverts.Add(centre + right * r + up * r); bverts.Add(centre - right * r + up * r);
            ballMesh.Clear();
            ballMesh.SetVertices(bverts); ballMesh.uv = BallUv; ballMesh.triangles = BallTris;
            ballMesh.bounds = new Bounds(Vector3.zero, new Vector3(400, 100, 400));
            // the colours of the original drawing: its darkest for the outline and the patches, its lightest for the white;
            // the shaded white is toned the way the figures' whites are (SpriteArt.Tone)
            Color ink = NesColour(pal[palette]), body = ink;
            for (int k = 1; k < 3; k++)
            {
                Color c = NesColour(pal[palette + k]);
                if (c.grayscale < ink.grayscale) ink = c;
                if (c.grayscale > body.grayscale) body = c;
            }
            Color shade = new Color(body.r * 0.70f, body.g * 0.76f, body.b * 0.90f, 1f);
            ballMat.SetColor("_BallInk", ink.linear); ballMat.SetColor("_BallShade", shade.linear); ballMat.SetColor("_BallBody", body.linear);
            ballMat.SetMatrix("_BallSpin", Matrix4x4.Rotate(Quaternion.Inverse(ballSpin)));
            ballMat.SetVector("_BallRight", right); ballMat.SetVector("_BallUp", up); ballMat.SetVector("_BallBack", -fwd);
            ballMat.SetVector("_BallStyle", BallStyle == 2 ? new Vector4(4f * BallRadiusPx, 0f, 1.0f, -0.30f) : new Vector4(0f, 0.011f, 1.0267f, -0.22f));
        }

        static Mesh NewMesh(string name)
        {
            var m = new Mesh(); m.name = name; m.MarkDynamic(); m.hideFlags = HideFlags.DontSave;
            return m;
        }

        MeshRenderer MakeChild(string name, Mesh mesh, string shader)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(Resources.Load<Shader>(shader)); mat.mainTexture = Atlas.Texture; mat.hideFlags = HideFlags.DontSave;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return r;
        }

        /// <summary>Where the game's own camera is looking, on the ground (blended between ticks).</summary>
        public Vector3 Focus { get; private set; }
        public Vector3 BallPosition { get; private set; }

        // ---- what the effects layer wants to know (all as drawn in this picture, i.e. blended between ticks)
        public bool BallShown { get; private set; }
        public Vector3 BallGround { get; private set; }         // the spot on the turf under the ball
        public float BallHeight { get; private set; }           // metres above the turf
        public Vector3 BallCentre { get; private set; }          // where the ball really is (for light and shadow)
        /// <summary>Where the ball is drawn.  A figure's height goes up the picture, not up in the world (as in the original:
        /// the art keeps its proportions under any camera), so a ball in the air is drawn behind the spot it is really over.</summary>
        public Vector3 BallDrawn { get; private set; }
        /// <summary>The ball is drawn as something other than the plain rolling ball: squashed by a hard kick, or a special shot.</summary>
        public bool BallSpecial { get; private set; }
        public bool BallColoured { get; private set; }          // ... and in colours of its own (fire, a throwing star), not black and white
        public Color BallGlow { get; private set; }             // linear
        /// <summary>One tile of the ball as drawn: corners relative to the ball's centre (camera right / up, metres) and its place in the atlas.</summary>
        public struct BallQuad { public float X0, X1, Y0, Y1; public Vector2 Uv0, Uv1; public Vector4 Cell; }
        public readonly List<BallQuad> BallQuads = new List<BallQuad>(12);
        readonly bool[] shown = new bool[SoccerBridge.Slots];
        public bool Shown(int i) { return shown[i]; }
        public Vector3 Ground(int i) { return worldPos[i]; }
        public float HeightOf(int i) { return height[i] * Field.L.kz; }

        static Color NesColour(int c) { uint v = Ppu.Rgb[c & 0x3F]; return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f); }

        // how many pixels of a tile are drawn in each of its three colours
        readonly Dictionary<int, int[]> inkCache = new Dictionary<int, int[]>();
        int[] Ink(int tile)
        {
            int[] n;
            if (inkCache.TryGetValue(tile, out n)) return n;
            n = new int[4];
            var chr = Host.Nes.Chr; int at = tile * 16;
            if (at >= 0 && at + 16 <= chr.Length)
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++) n[((chr[at + y] >> x) & 1) | (((chr[at + y + 8] >> x) & 1) << 1)]++;
            inkCache[tile] = n;
            return n;
        }

        readonly float[] inkOf = new float[64];

        void DescribeBall(Tick cur, ObjectState o, byte[] pal)
        {
            bool special = o.PieceCount != 4 && o.PieceCount != 2;
            System.Array.Clear(inkOf, 0, 64);
            float total = 0f;
            for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++)
            {
                var pc = cur.Pieces[k];
                int low = pc.Tile & 63;
                if (!(low == 3 || low == 4 || low == 5 || low == 0x13 || low == 0x14 || low == 0x15)) special = true;
                int pi = 0x11 + (pc.Attr & 3) * 4;
                var ink = Ink(pc.Tile);
                for (int c = 0; c < 3; c++) { inkOf[pal[pi + c] & 63] += ink[c + 1]; total += ink[c + 1]; }
            }
            // the colour it is mostly painted in, if it is a colour at all (fire, a throwing star) and not black and white
            Color best = Color.white; float bestScore = 0f, coloured = 0f;
            for (int c = 0; c < 64 && total > 0f; c++)
            {
                if (inkOf[c] <= 0f) continue;
                Color col = NesColour(c);
                float mx = Mathf.Max(col.r, Mathf.Max(col.g, col.b)), mn = Mathf.Min(col.r, Mathf.Min(col.g, col.b));
                float sat = mx > 0.3f ? (mx - mn) / mx : 0f, share = inkOf[c] / total;
                if (sat > 0.3f) coloured += share;
                if (sat * share > bestScore) { bestScore = sat * share; best = col; }
            }
            // a player standing on the ball (he jumped and came down on it: the ball's movement $11 / $12) squashes it into
            // another shape for a moment - that is no special shot
            int mov = cur.Ram[0x465] & 0x7F;
            if (mov == 0x11 || mov == 0x12) special = false;
            BallSpecial = special; BallColoured = special && coloured > 0.2f;
            BallGlow = (BallColoured ? best : new Color(1f, 0.97f, 0.88f)).linear;
        }

        /// <summary>Call before placing the camera: works out where the action is.</summary>
        public void UpdateFocus(float blend)
        {
            var br = Host.Bridge;
            if (!br.InMatch || br.TickCount == 0) return;
            Tick cur = br.Cur, prev = br.TickCount > 1 ? br.Prev : br.Cur;
            Vector3 f1 = Field.ToWorld(cur.CamX + 128, cur.CamY + 96), f0 = Field.ToWorld(prev.CamX + 128, prev.CamY + 96);
            Focus = Vector3.Lerp(f0, f1, blend);
        }

        public void Rebuild(float blend)
        {
            var br = Host.Bridge;
            verts.Clear(); sverts.Clear(); uvs.Clear(); cells.Clear(); layers.Clear(); tris.Clear();
            for (int i = 0; i < shown.Length; i++) shown[i] = false;
            BallShown = false; BallSpecial = false; BallQuads.Clear(); BallRound = false;
            if (br.InMatch && br.TickCount > 0)
            {
                Tick cur = br.Cur, prev = br.TickCount > 1 ? br.Prev : br.Cur;
                var L = Field.L;
                Vector3 right = Cam.transform.right, up = Cam.transform.up, fwd = Cam.transform.forward;
                // shadow axes on the ground: along the sun's travel, and across it (on the side that matches what the camera sees)
                Vector3 sunDir = new Vector3(SunTravel.x, 0, SunTravel.y).normalized;
                Vector3 across = new Vector3(sunDir.z, 0, -sunDir.x);
                if (Vector3.Dot(across, right) < 0) across = -across;
                float s = L.kx;
                var pal = Host.Nes.Ppu.Palette;
                bool byLamp = Lamps.Length > 0; Vector3 lamp = byLamp ? MainLamp : Vector3.zero;
                for (int i = 0; i < SoccerBridge.Slots; i++)
                {
                    var o = cur.Objects[i];
                    if (!o.Active) continue;
                    // single-tile markers: the number over the controlled player lives on the pitch, the radar dot lives in the status bar
                    if (o.Kind == 3 && o.Y - cur.CamY >= 176) continue;
                    Vector3 p1 = Field.ToWorld(o.X, o.Y);
                    float h1 = Mathf.Max(o.Z, 0);
                    var q = prev.Objects[i];
                    if (q.Active && blend < 1f)
                    {
                        Vector3 p0 = Field.ToWorld(q.X, q.Y);
                        if ((p0 - p1).sqrMagnitude < 9f) { p1 = Vector3.Lerp(p0, p1, blend); h1 = Mathf.Lerp(Mathf.Max(q.Z, 0), h1, blend); }
                    }
                    worldPos[i] = p1; height[i] = h1; shown[i] = true;
                    if (i == SoccerBridge.Ball)
                    {
                        BallPosition = p1 + Vector3.up * (h1 * L.kz);
                        BallShown = o.PieceCount > 0; BallGround = p1; BallHeight = h1 * L.kz; BallCentre = p1 + Vector3.up * ((h1 + 8f) * L.kz); BallDrawn = p1 + up * ((h1 + 8f) * s);
                        DescribeBall(cur, o, pal);
                        BallRound = BallStyle != 0 && ballMat != null && BallShown && !BallSpecial && o.PieceCount == 4;
                        if (BallRound) RoundBall(BallPosition, BallDrawn, right, up, fwd, s, pal, 0x11 + (cur.Pieces[o.FirstPiece].Attr & 3) * 4);
                    }
                    bool shadow = i <= SoccerBridge.Ball;
                    for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++)
                    {
                        var pc = cur.Pieces[k];
                        int pi = 0x11 + (pc.Attr & 3) * 4;
                        bool flipX = (pc.Attr & 0x40) != 0, flipY = (pc.Attr & 0x80) != 0;
                        int cell = Atlas.Get(pc.Tile, pal[pi], pal[pi + 1], pal[pi + 2], flipX);
                        float x0 = pc.Dx * s, x1 = (pc.Dx + 8) * s;
                        float yTop = (pc.Top + h1) * s, yBot = (pc.Top - 8 + h1) * s;
                        Vector3 basePos = p1;
                        // (.y = 1: the ball and the shooter of a special shot, who stay lit while the world stands still - see G3Sprite)
                        var layer = new Vector2(pc.Layer + 1, i == SoccerBridge.Ball || i == Host.FreezeShooter ? 1 : 0); layers.Add(layer); layers.Add(layer); layers.Add(layer); layers.Add(layer);
                        int v0 = verts.Count;
                        if (i == SoccerBridge.Ball && BallRound) { Vector3 none = new Vector3(0, -50, 0); verts.Add(none); verts.Add(none); verts.Add(none); verts.Add(none); }      // (the sphere is drawn instead; the shadow below stays)
                        else
                        {
                        verts.Add(basePos + right * x0 + up * yBot);
                        verts.Add(basePos + right * x1 + up * yBot);
                        verts.Add(basePos + right * x1 + up * yTop);
                        verts.Add(basePos + right * x0 + up * yTop);
                        }
                        if (shadow && byLamp)
                        {
                            // 立着、转向那盏灯的人影（不是正对镜头的那张画的影子：从上往下看那张画是躺平的，影子会成一团）
                            Vector3 away = new Vector3(p1.x - lamp.x, 0f, p1.z - lamp.z).normalized, side = new Vector3(away.z, 0f, -away.x);
                            if (Vector3.Dot(side, right) < 0f) side = -side;
                            Vector3 b0 = p1 + side * x0, b1 = p1 + side * x1;
                            float hb = Mathf.Max(yBot, 0f), ht = Mathf.Max(yTop, 0f);
                            sverts.Add(FromLamp(b0 + Vector3.up * hb, lamp)); sverts.Add(FromLamp(b1 + Vector3.up * hb, lamp));
                            sverts.Add(FromLamp(b1 + Vector3.up * ht, lamp)); sverts.Add(FromLamp(b0 + Vector3.up * ht, lamp));
                        }
                        else if (shadow)
                        {
                            float gy = 0.012f;
                            float a0 = Mathf.Max(yBot, 0) * SunCot, a1 = Mathf.Max(yTop, 0) * SunCot;
                            Vector3 g = new Vector3(p1.x, gy, p1.z);
                            sverts.Add(g + across * x0 + sunDir * a0);
                            sverts.Add(g + across * x1 + sunDir * a0);
                            sverts.Add(g + across * x1 + sunDir * a1);
                            sverts.Add(g + across * x0 + sunDir * a1);
                        }
                        else { Vector3 z = new Vector3(0, -50, 0); sverts.Add(z); sverts.Add(z); sverts.Add(z); sverts.Add(z); }
                        float cu = (cell % SpriteAtlas.Cells) * SpriteAtlas.Cell, cv = (cell / SpriteAtlas.Cells) * SpriteAtlas.Cell;
                        const float inv = 1f / SpriteAtlas.Dim;
                        float u0 = cu * inv, u1 = (cu + SpriteAtlas.Cell) * inv, w0 = cv * inv, w1 = (cv + SpriteAtlas.Cell) * inv;
                        if (flipX) { float t = u0; u0 = u1; u1 = t; }
                        if (flipY) { float t = w0; w0 = w1; w1 = t; }
                        uvs.Add(new Vector2(u0, w0)); uvs.Add(new Vector2(u1, w0)); uvs.Add(new Vector2(u1, w1)); uvs.Add(new Vector2(u0, w1));
                        var cellRect = new Vector4((cu + 0.5f) * inv, (cv + 0.5f) * inv, (cu + SpriteAtlas.Cell - 0.5f) * inv, (cv + SpriteAtlas.Cell - 0.5f) * inv);
                        cells.Add(cellRect); cells.Add(cellRect); cells.Add(cellRect); cells.Add(cellRect);
                        if (i == SoccerBridge.Ball)
                        {
                            float cy = (h1 + 8f) * s;
                            BallQuads.Add(new BallQuad { X0 = x0, X1 = x1, Y0 = yBot - cy, Y1 = yTop - cy, Uv0 = new Vector2(u0, w0), Uv1 = new Vector2(u1, w1), Cell = cellRect });
                        }
                        tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 1); tris.Add(v0); tris.Add(v0 + 3); tris.Add(v0 + 2);
                    }
                }
            }
            Atlas.Upload();
            Fill(spriteMesh, verts); Fill(shadowMesh, sverts);
            if (ballRenderer != null) ballRenderer.enabled = BallRound;
            if (!BallShown) ballWasSet = false;
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
