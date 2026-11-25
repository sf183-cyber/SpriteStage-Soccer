// The effects layer: what a 3D picture needs on top of the original's sprites.
//
//  - the ball: a soft shadow straight below it (where it will come down - the sun's shadow lies metres away when the
//    ball is high), a streak behind a fast shot, a flash and dust where it is struck or lands
//  - special shots: the ball glows in its own colours behind a turning star of light, throws light on the turf, drags
//    after-images, a long trail, speed lines and sparks, and leaves a glowing wake in the grass; a burst when it is
//    let fly and when it hits someone
//  - the goal: the netting bulges and ripples when the ball goes in; a shot that the original lets tear through the
//    net does so here - a ragged hole, shreds flying; then confetti and fireworks for the goal
//  - the players: a little contact shadow under the feet, so that the flat figures sit on the photographed grass
//  - the weather, which the original draws with a handful of loose sprites inside its small window: wind (leaves),
//    rain (drops and splashes) and lightning (a bolt from the sky and a flash)
//  - the kinds of special shot: the game's 24 specials are sorted into eight kinds (fire, thunder, wind, water, blade,
//    dark, light, earth - see Styles), each with its own colours, its own aura round the ball, its own trail, its own
//    burst when it is let fly and its own sound
//  - the standstill ("super flash"): when a special is struck the game is not run for a moment (GameHost.Freeze);
//    the world goes dark (G3Night) but for the shooter and the ball, light gathers round him, and then it all lets go
//
// Nothing here changes the game: everything is read off the bridge's ticks.  Press O to switch the layer off / on.
using System.Collections.Generic;
using UnityEngine;
using NesCore;

namespace Soccer
{
    public sealed class Effects : MonoBehaviour
    {
        public GameHost Host;
        public Camera Cam;
        public SpriteField Sprites;
        public PostFx Post;
        public OrbitCamera Orbit;
        public bool On = true;

        const int Disc = 0, Ring = 1, LeafShape = 2, Streak = 3, Spark = 4, Shade = 5, Star = 6, Scrap = 7;
        const int PixFlame = 8, PixBurst = 9, PixRing = 10, PixBlock = 11;      // drawn as pixel art (see G3Fx): light ...
        const int PixPlain = 12, PixPuff = 13, PixRingPlain = 14, PixLeaf = 15; // ... and things
        const int PixGlint = 16;                                                // ... and one more light: a four-pointed star
        const float StepRate = 15f;         // the pixel art moves in steps, like the figures (the game redraws them 15-20 times a second)
        const float RainBox = 14f, RainTop = 13f;

        Mesh mesh, ghostMesh, superMesh;
        MeshRenderer nightRenderer;
        // (what is being drawn goes into these lists: the effects proper, or - see Into - those of the standstill, which are
        //  drawn after the darkness has fallen and so stay bright)
        List<Vector3> verts = new List<Vector3>(8192); List<Color> cols = new List<Color>(8192);
        List<Vector2> uv0 = new List<Vector2>(8192), uv1 = new List<Vector2>(8192); List<Vector4> uv2 = new List<Vector4>(8192); List<int> tris = new List<int>(12288);
        List<Vector3> vertsB = new List<Vector3>(2048); List<Color> colsB = new List<Color>(2048);
        List<Vector2> uv0B = new List<Vector2>(2048), uv1B = new List<Vector2>(2048); List<Vector4> uv2B = new List<Vector4>(2048); List<int> trisB = new List<int>(3072);
        readonly List<Vector3> gverts = new List<Vector3>(256);
        readonly List<Color> gcols = new List<Color>(256);
        readonly List<Vector2> guv = new List<Vector2>(256);
        readonly List<Vector4> gcell = new List<Vector4>(256);
        readonly List<int> gtris = new List<int>(384);
        Vector3 camRight, camUp, camPos;
        readonly System.Random rng = new System.Random(7);

        struct Particle
        {
            public Vector3 Pos, Vel;
            public float Age, Life, Size0, Size1, Gravity, Drag, Light;
            public float Stretch;       // > 0: drawn as a streak this many seconds of its own flight long
            public float Rot, Spin;     // turning in the picture plane
            public float Tumble;        // > 0: flips over and over (a scrap of paper)
            public float Twinkle;       // > 0: glitters
            public Color Col;
            public int Shape;
            public bool Flat;           // lies on the turf
        }
        readonly Particle[] ps = new Particle[3000];
        int count;

        // ---- ball
        struct Sample { public Vector3 Pos; public float Time; }
        readonly List<Sample> trail = new List<Sample>(64), wake = new List<Sample>(64);
        float clock, power, speedShown, sinceSpecial = 9f, sinceColoured = 9f, punch;
        float waveAge = -1f, rush, sinceBurst = 9f;          // the shock wave and the rush of the picture when a special shot is let fly
        Vector3 waveAt;
        Color glow = Color.white;           // the colour the ball is glowing in (follows the ball's own, smoothed)
        // A special shot proper keeps its special shape tick after tick; a ball that only flickers into one every other
        // tick is being charged up (or is just a hard shot drawn squashed).
        int specialTicks; bool flew;        // for how many ticks of the game the ball has been in a special shape without a break
        float sinceLaunch = 9f, sinceRing, sinceChargeRing;
        Vector3 lastScorch;
        static readonly Color Gold = new Color(1f, 0.80f, 0.30f).linear;       // the aura of a special shot that has no colour of its own (the yin-yang ball)

        // ---- the kinds of special shot.  The game has 24 specials ($04-$0F: the home-team members', $16-$21: the foreign
        // teams').  Each is given one of eight kinds here - by what its ball turns into - and two colours of its own, so that
        // no two look alike.
        const int Fire = 0, Thunder = 1, Wind = 2, Water = 3, Blade = 4, Dark = 5, Light = 6, Earth = 7;
        public static readonly string[] KindNames = { "火", "雷", "风", "水", "刃", "暗", "光", "土" };
        public struct Style { public int Kind; public Color A, B; }
        static Style St(int kind, float r, float g, float b, float r2, float g2, float b2)
        {
            Color a = new Color(r, g, b).linear, c = new Color(r2, g2, b2).linear; a.a = 1f; c.a = 1f;
            return new Style { Kind = kind, A = a, B = c };
        }
        public static readonly Dictionary<int, Style> Styles = new Dictionary<int, Style>
        {
            { 0x04, St(Light,   1.00f, 0.93f, 0.62f,  1.00f, 1.00f, 1.00f) },    // the ball squashed flat by sheer force: white-gold
            { 0x05, St(Blade,   0.62f, 0.82f, 1.00f,  1.00f, 1.00f, 1.00f) },    // a throwing star: steel
            { 0x06, St(Wind,    0.50f, 1.00f, 0.88f,  0.90f, 1.00f, 1.00f) },    // a ball spinning on its long axis: a whirl of air
            { 0x07, St(Earth,   0.98f, 0.66f, 0.28f,  0.52f, 0.34f, 0.17f) },    // a disc skimming the grass, mowing people down
            { 0x08, St(Earth,   1.00f, 0.48f, 0.60f,  0.60f, 0.26f, 0.30f) },    // a ball swollen to a boulder
            { 0x09, St(Light,   0.80f, 1.00f, 0.42f,  1.00f, 1.00f, 0.80f) },    // the ball running up and over the shooter: a trick of light
            { 0x0A, St(Fire,    1.00f, 0.42f, 0.10f,  1.00f, 0.85f, 0.30f) },    // the ball spitting flames
            { 0x0B, St(Water,   0.60f, 0.90f, 1.00f,  1.00f, 1.00f, 1.00f) },    // a turning crystal: ice
            { 0x0C, St(Thunder, 1.00f, 0.90f, 0.28f,  1.00f, 1.00f, 0.85f) },    // a box: yellow lightning
            { 0x0D, St(Water,   0.22f, 0.60f, 1.00f,  0.70f, 0.92f, 1.00f) },    // the fish
            { 0x0E, St(Wind,    0.70f, 1.00f, 0.32f,  1.00f, 1.00f, 0.60f) },    // a boomerang
            { 0x0F, St(Fire,    1.00f, 0.20f, 0.18f,  1.00f, 0.60f, 0.30f) },    // the net-breaker: crimson fire
            { 0x16, St(Light,   1.00f, 0.80f, 0.30f,  1.00f, 1.00f, 1.00f) },    // yin and yang: gold
            { 0x17, St(Fire,    1.00f, 0.64f, 0.06f,  1.00f, 0.92f, 0.45f) },    // the lion: golden fire
            { 0x18, St(Earth,   0.92f, 0.74f, 0.46f,  0.60f, 0.45f, 0.25f) },    // the horse: dust
            { 0x19, St(Fire,    1.00f, 0.30f, 0.46f,  1.00f, 0.90f, 0.90f) },    // a ring of fire that bursts: rose and white
            { 0x1A, St(Dark,    0.62f, 0.30f, 1.00f,  0.25f, 0.10f, 0.45f) },    // the ball that vanishes: violet
            { 0x1B, St(Wind,    1.00f, 1.00f, 1.00f,  0.82f, 0.90f, 1.00f) },    // a white bird: feathers
            { 0x1C, St(Wind,    1.00f, 0.95f, 0.38f,  1.00f, 1.00f, 0.80f) },    // Argentina's boomerang: yellow
            { 0x1D, St(Blade,   1.00f, 0.42f, 0.38f,  1.00f, 1.00f, 1.00f) },    // Brazil's ball of spikes: red steel
            { 0x1E, St(Thunder, 1.00f, 0.68f, 0.14f,  1.00f, 1.00f, 0.60f) },    // Mexico: it electrocutes whoever it hits
            { 0x1F, St(Dark,    0.95f, 0.18f, 0.55f,  0.20f, 0.05f, 0.20f) },    // England's black mine: magenta and black
            { 0x20, St(Thunder, 0.80f, 0.75f, 1.00f,  1.00f, 1.00f, 1.00f) },    // Italy's pulsing burst: pale violet lightning
            { 0x21, St(Thunder, 0.42f, 0.70f, 1.00f,  0.90f, 0.95f, 1.00f) },    // Germany's swarm of little balls: blue sparks
        };
        Style style; bool styled; int styleSerial = -1; float styleAge;
        float sinceSplash, sinceRumble, pillarAge = -1f; Vector3 pillarAt; Color pillarCol;
        // ---- the standstill ("super flash")
        int superSerial; bool superOn; float superT, superClock, night; int superSeed; Vector3 superAt;
        // ---- moods and blows, handed to the photographic finish (PostFx)
        float gloom, dim, lines;            // storm light; the hush round a special shot; impact lines
        Color punchTint = Color.white; Vector3 linesAt;
        float sheet = 9f, nextSheet = 2f;   // sheet lightning far off in the clouds: seconds since the last, seconds to the next
        float boltEnd = -9f, strikeFlash; int boltEchoSeed;
        float gust = 1f;
        readonly Vector3[] feetWas = new Vector3[SoccerBridge.Slots]; readonly bool[] feetSet = new bool[SoccerBridge.Slots];
        Vector3 ballVel;                    // metres per second, from the last two ticks
        readonly Vector3[] tickBall = new Vector3[3];
        readonly long[] tickFrame = new long[3];
        int ticksSeen, lastSerial = -1;

        // ---- goal
        int lastWeather, lastSub = -1;
        float netAge = -1f, netHole, sinceNet = 9f;
        Vector3 netAt, netPush;
        // the ball lying in a goal: the netting stays pushed out round it for as long as it is there (see G3Net)
        int netIn;                          // the goal the ball is in: -1 west, +1 east, 0 neither
        float netGrip, netDepth;            // how much of that push is on (eases in and out); how far into the netting the ball was in the last picture
        Vector3 netBall, netTouch, netFace; // where the netting last felt the ball; the spot of netting it lay against and the way that panel faces
        const float NetBallRadius = 0.44f;  // the ball as the netting feels it: a little more than it is drawn (0.32), so the cords stand clear of it
        const float NetGive = 1.5f;         // how far the middle of a panel can be pushed out, metres
        struct Shell { public float Time; public Vector3 Pos; public Color Col; }
        readonly List<Shell> shells = new List<Shell>(8);
        static readonly Color[] Festive =
        {
            new Color(1.00f, 0.80f, 0.22f).linear, new Color(0.95f, 0.22f, 0.20f).linear, new Color(0.25f, 0.50f, 1.00f).linear,
            new Color(1.00f, 1.00f, 1.00f).linear, new Color(0.30f, 0.90f, 0.45f).linear, new Color(1.00f, 0.45f, 0.80f).linear,
        };

        // ---- weather
        struct Leaf { public Vector3 Pos; public float Phase, Size, Pace; public int Tint; }
        struct Drop { public Vector3 Pos; public float Pace; }
        readonly Leaf[] leaves = new Leaf[420];
        readonly Drop[] drops = new Drop[2200];
        Vector3 rainAt;                     // the rain is kept in a box round this point (in front of the camera)
        bool slanted;
        float leafAmount, rainAmount, flash, flashAge, boltLife;
        Vector3 wind = new Vector3(5.5f, 0f, -1.5f), boltAt;
        int boltSeed;
        readonly List<Vector2> lastLeaves = new List<Vector2>(16);
        static readonly Color[] LeafTints =
        {
            new Color(0.62f, 0.33f, 0.12f).linear, new Color(0.74f, 0.47f, 0.14f).linear, new Color(0.52f, 0.24f, 0.10f).linear,
            new Color(0.70f, 0.58f, 0.18f).linear, new Color(0.45f, 0.42f, 0.14f).linear,
        };

        public void Init(GameHost host, Camera cam, SpriteField sprites, PostFx post, OrbitCamera orbit)
        {
            Host = host; Cam = cam; Sprites = sprites; Post = post; Orbit = orbit;
            mesh = NewMesh("Effects", gameObject, "Shaders/G3Fx", null);
            var ghosts = new GameObject("After-images"); ghosts.transform.SetParent(transform, false);
            ghostMesh = NewMesh("After-images", ghosts, "Shaders/G3Ghost", sprites.Atlas.Texture);
            // the standstill: a sheet of darkness over the whole picture, and after it what glows in the dark
            if (Resources.Load<Shader>("Shaders/G3Night") != null)
            {
                var dark = new GameObject("Night"); dark.transform.SetParent(transform, false);
                var nm = NewMesh("Night", dark, "Shaders/G3Night", null);
                nm.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
                nm.triangles = new[] { 0, 2, 1, 0, 3, 2 }; nm.bounds = new Bounds(Vector3.zero, new Vector3(6000, 3000, 6000));
                nightRenderer = dark.GetComponent<MeshRenderer>(); nightRenderer.enabled = false;
            }
            var sup = new GameObject("Standstill"); sup.transform.SetParent(transform, false);
            superMesh = NewMesh("Standstill", sup, "Shaders/G3Fx", null);
            sup.GetComponent<MeshRenderer>().sharedMaterial.renderQueue = 3020;         // (after the darkness, which is at 3015)
            if (host != null) { superSerial = host.FreezeSerial; if (host.Tweaks != null) styleSerial = host.Tweaks.SpecialSerial; }
            for (int i = 0; i < leaves.Length; i++)
            {
                leaves[i].Pos = new Vector3(Rand(-34f, 34f), Rand(0.15f, 4.5f), Rand(-21f, 21f));
                leaves[i].Phase = Rand(0f, 30f); leaves[i].Size = Rand(0.19f, 0.34f); leaves[i].Pace = Rand(0.65f, 1.3f); leaves[i].Tint = rng.Next(LeafTints.Length);
            }
            for (int i = 0; i < drops.Length; i++) { drops[i].Pos = new Vector3(Rand(-RainBox, RainBox), Rand(0f, RainTop), Rand(-RainBox, RainBox)); drops[i].Pace = Rand(0.85f, 1.2f); }
        }

        static Mesh NewMesh(string name, GameObject go, string shader, Texture tex)
        {
            var m = new Mesh(); m.name = name; m.MarkDynamic(); m.hideFlags = HideFlags.DontSave;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(Resources.Load<Shader>(shader)); mat.hideFlags = HideFlags.DontSave;
            if (tex != null) mat.mainTexture = tex;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return m;
        }

        float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
        Vector3 RandDir() { float a = Rand(0f, 6.2832f), z = Rand(-1f, 1f), r = Mathf.Sqrt(1f - z * z); return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a)); }
        static Color A(Color c, float a) { c.a = a; return c; }

        void Update()
        {
            if (Application.isPlaying && Keys.Down(KeyCode.O)) On = !On;
        }

        int Emit(Vector3 pos, Vector3 vel, float life, float size0, float size1, Color col, int shape, float light, float gravity = 0f, float drag = 0f, bool flat = false)
        {
            if (count >= ps.Length) return -1;
            ps[count] = new Particle { Pos = pos, Vel = vel, Life = life, Size0 = size0, Size1 = size1, Col = col, Shape = shape, Light = light, Gravity = gravity, Drag = drag, Flat = flat };
            return count++;
        }

        // an ember: one square pixel (or a block of them) that cools from white through its colour; 'blink' makes it glitter
        void EmitEmber(Vector3 pos, Vector3 vel, float life, Color colour, float gravity, float drag, float size = 0f, float blink = 0f)
        {
            float half = Px * (size > 0f ? size : rng.Next(3) == 0 ? 1f : 0.5f);
            int k = Emit(pos, vel, life, half, half, A(colour, 1f), PixBlock, 0f, gravity, drag);
            if (k >= 0) ps[k].Twinkle = blink;
        }

        // a chip of something solid in one flat colour: grass, netting, paper ('flip' makes it show its light and dark side in turn)
        int EmitChip(Vector3 pos, Vector3 vel, float life, Color colour, float gravity, float drag, float size = 0.5f, float flip = 0f)
        {
            int k = Emit(pos, vel, life, Px * size, Px * size, colour, PixPlain, 0f, gravity, drag);
            if (k >= 0) ps[k].Twinkle = flip;
            return k;
        }

        // a puff of dust or smoke, in pixels
        void EmitPuff(Vector3 pos, Vector3 vel, float life, float pixels0, float pixels1, Color colour, float gravity, float drag)
        {
            Emit(pos, vel, life, Px * pixels0, Px * pixels1, colour, PixPuff, 0f, gravity, drag);
        }

        static float Px { get { return Field.L.kx; } }      // one pixel of the original picture, in metres (the figures' art is drawn two to a pixel)

        void Knock(float shake, float flashAmount)
        {
            if (Orbit != null) Orbit.Shake = Mathf.Max(Orbit.Shake, shake);
            punch = Mathf.Max(punch, flashAmount);
        }

        // ------------------------------------------------------------------ what happened in the game

        void OnTick(Tick cur, Tick prev)
        {
            var L = Field.L;
            var o = cur.Objects[SoccerBridge.Ball];
            Vector3 b = Field.ToWorld(o.X, o.Y) + Vector3.up * (Mathf.Max(o.Z, 0) * L.kz);
            tickBall[0] = tickBall[1]; tickBall[1] = tickBall[2]; tickBall[2] = b;
            tickFrame[0] = tickFrame[1]; tickFrame[1] = tickFrame[2]; tickFrame[2] = cur.Frame;
            ticksSeen++;
            if (ticksSeen >= 2)
            {
                float dt12 = Mathf.Max(1, tickFrame[2] - tickFrame[1]) * (float)GameHost.FrameTime;
                Vector3 v = (tickBall[2] - tickBall[1]) / dt12;
                if (v.magnitude > 80f) { trail.Clear(); wake.Clear(); ticksSeen = 1; ballVel = Vector3.zero; }       // put somewhere else (kick-off, throw-in)
                else
                {
                    if (ticksSeen >= 3)
                    {
                        float dt01 = Mathf.Max(1, tickFrame[1] - tickFrame[0]) * (float)GameHost.FrameTime;
                        Vector3 before = (tickBall[1] - tickBall[0]) / dt01;
                        Vector3 at = tickBall[1], ground = new Vector3(at.x, 0f, at.z);
                        Vector3 dv = v - before; dv.y = 0f;
                        if (!InNet(at, before, v))
                        {
                            if (at.y < 0.15f && before.y < -2.5f && v.y > 0.8f) Landing(ground, Mathf.Clamp01(-before.y / 9f));
                            else if (dv.magnitude > 8f && at.y < 1.8f) Struck(ground + Cam.transform.up * (at.y + 0.36f), ground, Mathf.Clamp01(dv.magnitude / 22f), new Vector3(v.x, 0f, v.z), v.magnitude > 6f);     // (where the ball was drawn)
                        }
                    }
                    ballVel = v;
                }
            }
            // a shot that tears through the net (the original shows the net ripped and shreds flying)
            bool torn = cur.Weather == 5 || cur.Weather == 6, wasTorn = lastWeather == 5 || lastWeather == 6;       // (5: the east net, 6: the west)
            if (torn && !wasTorn)
            {
                Vector3 n; Vector3 at = NetPoint(b + Vector3.up * 0.36f, true, out n);
                Vector3 dir = ballVel.sqrMagnitude > 1f ? ballVel.normalized : n;
                NetBurst(at, (dir + n).normalized);
            }
            lastWeather = cur.Weather;
            // the wind, read off the leaves the original blows across its screen
            if (cur.Weather == 3 || cur.Weather == 4)
            {
                var now = new List<Vector2>(8);
                foreach (var e in cur.Extras) if (e.Tile == 0x207) now.Add(new Vector2(e.X, e.Y));
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var a in now)
                {
                    float best = 40f * 40f; Vector2 from = a; bool found = false;
                    foreach (var p in lastLeaves) { float d = (p - a).sqrMagnitude; if (d < best) { best = d; from = p; found = true; } }
                    if (found) { sum += Field.ToWorld(a.x, a.y) - Field.ToWorld(from.x, from.y); n++; }
                }
                float dt = Mathf.Max(1, cur.Frame - prev.Frame) * (float)GameHost.FrameTime;
                if (n >= 2)
                {
                    Vector3 w = sum / (n * dt); w.y = 0f;
                    if (w.magnitude > 1f && w.magnitude < 40f) wind = Vector3.Lerp(wind, Vector3.ClampMagnitude(w, 14f), 0.15f);
                }
                lastLeaves.Clear(); lastLeaves.AddRange(now);
            }
            else lastLeaves.Clear();
            if (cur.Weather == 2)
            {
                int straight = 0, slant = 0;
                foreach (var e in cur.Extras) { int low = e.Tile & 63; if (low == 0x33) straight++; else if (low == 0x34) slant++; }
                if (straight + slant > 0) slanted = slant > straight;
            }
            // lightning: the original draws a small bolt next to its victim; here it comes down from the sky to that spot
            if (cur.Weather == 1)
            {
                int lowest = -1; float sx = 0, n = 0;
                foreach (var e in cur.Extras)
                {
                    int bank = e.Tile >> 6;
                    if (bank != 9 && bank != 10) continue;
                    if (e.Y > lowest) lowest = e.Y;
                    sx += e.X; n++;
                }
                if (n >= 3)
                {
                    Vector3 at = Field.ToWorld(sx / n + 4f, lowest + 8f);
                    if (boltLife <= 0f) Strike(at);
                    boltAt = at; boltLife = 0.14f; boltSeed = cur.Frame.GetHashCode();
                }
            }
        }

        // ---- the goal

        // The spot of the netting nearest to p, and which way that panel faces.
        Vector3 NetPoint(Vector3 p, bool backOnly, out Vector3 normal)
        {
            var L = Field.L;
            float s = p.x >= 0f ? 1f : -1f, back = L.halfLength + L.goalD;
            float dBack = Mathf.Abs(back - Mathf.Abs(p.x)), dRoof = Mathf.Abs(L.goalH - p.y), dNear = Mathf.Abs(p.z - L.goalY0), dFar = Mathf.Abs(L.goalY1 - p.z);
            float ax = Mathf.Clamp(Mathf.Abs(p.x), L.halfLength + 0.15f, back - 0.15f);
            float y = Mathf.Clamp(p.y, 0.25f, L.goalH - 0.25f), z = Mathf.Clamp(p.z, L.goalY0 + 0.3f, L.goalY1 - 0.3f);
            if (backOnly || (dBack <= dRoof && dBack <= dNear && dBack <= dFar)) { normal = new Vector3(s, 0f, 0f); return new Vector3(s * back, y, z); }
            if (dRoof <= dNear && dRoof <= dFar) { normal = Vector3.up; return new Vector3(s * ax, L.goalH, z); }
            if (dNear <= dFar) { normal = new Vector3(0f, 0f, -1f); return new Vector3(s * ax, y, L.goalY0); }
            normal = new Vector3(0f, 0f, 1f); return new Vector3(s * ax, y, L.goalY1);
        }

        // The ball caught by the netting?  (Then the net moves, and the turf does not get a dust cloud.)
        bool InNet(Vector3 at, Vector3 before, Vector3 after)
        {
            var L = Field.L;
            float ax = Mathf.Abs(at.x);
            if (ax < L.halfLength + 0.2f || ax > L.halfLength + L.goalD + 0.8f || at.z < L.goalY0 - 0.4f || at.z > L.goalY1 + 0.4f || at.y > L.goalH + 0.4f) return false;
            float lost = before.magnitude - after.magnitude;
            if (netIn == 0 && netHole <= 0f && before.magnitude > 3.5f && lost > before.magnitude * 0.45f && sinceNet > 0.4f)
            {
                Vector3 n; Vector3 p = NetPoint(at + Vector3.up * 0.36f, false, out n);
                float strength = Mathf.Clamp01(before.magnitude / 20f);
                NetHit(p, (before.normalized + n * 1.5f).normalized * (0.25f + 0.75f * strength), 0f);
                Color white = new Color(1f, 1f, 0.95f, 1f);
                for (int i = 0; i < 6 + (int)(10 * strength); i++) EmitChip(p, (n + RandDir() * 0.8f) * Rand(1.5f, 4f), Rand(0.2f, 0.45f), white, 6f, 1.5f);
            }
            return true;
        }

        // How far the ball (as drawn) is into the netting of the goal on that side, from the inside; where, and the way that panel faces.
        float NetDepth(Vector3 c, int side, out Vector3 at, out Vector3 normal)
        {
            var L = Field.L;
            float back = L.halfLength + L.goalD, ax = Mathf.Abs(c.x), r = NetBallRadius;
            float x = Mathf.Clamp(ax, L.halfLength, back), y = Mathf.Clamp(c.y, 0f, L.goalH), z = Mathf.Clamp(c.z, L.goalY0, L.goalY1);
            float dBack = r + ax - back, dRoof = r + c.y - L.goalH, dNear = r + L.goalY0 - c.z, dFar = r + c.z - L.goalY1;
            if (dBack >= dRoof && dBack >= dNear && dBack >= dFar) { normal = new Vector3(side, 0f, 0f); at = new Vector3(side * back, y, z); return dBack; }
            if (dRoof >= dNear && dRoof >= dFar) { normal = Vector3.up; at = new Vector3(side * x, L.goalH, z); return dRoof; }
            if (dNear >= dFar) { normal = new Vector3(0f, 0f, -1f); at = new Vector3(side * x, y, L.goalY0); return dNear; }
            normal = new Vector3(0f, 0f, 1f); at = new Vector3(side * x, y, L.goalY1); return dFar;
        }

        // A ball that went in through the mouth of a goal is held by its netting until it is taken out again.
        void HoldNet(float dt)
        {
            var L = Field.L;
            if (Sprites.BallShown)
            {
                Vector3 t = Sprites.BallCentre;             // where it really is: the game decides what is a goal, not the picture
                float ax = Mathf.Abs(t.x); int side = t.x >= 0f ? 1 : -1;
                bool inside = ax >= L.halfLength && ax <= L.halfLength + L.goalD + 0.1f && t.z >= L.goalY0 - 0.05f && t.z <= L.goalY1 + 0.05f && t.y <= L.goalH + 0.2f;
                bool near = ax >= L.halfLength - 0.5f && ax <= L.halfLength + L.goalD + 0.9f && t.z >= L.goalY0 - 0.6f && t.z <= L.goalY1 + 0.6f && t.y <= L.goalH + 0.8f;
                if (netIn != 0 && (!near || side != netIn)) netIn = 0;
                if (netIn == 0 && inside) netIn = side;
            }
            else netIn = 0;
            bool held = netIn != 0 && netHole <= 0f;        // (a torn net hangs loose: nothing to hold the ball with)
            if (held)
            {
                netBall = Sprites.BallDrawn;
                Vector3 at, n; float depth = NetDepth(netBall, netIn, out at, out n);
                if (depth > 0.03f)
                {
                    if (netDepth <= 0.03f && sinceNet > 0.3f)
                    {
                        // it has just come up against the netting: the harder, the further the net is flung out before it settles round the ball
                        float speed = Vector3.Dot(ballVel, n);
                        if (speed > 1.5f)
                        {
                            float strength = Mathf.Clamp01(speed / 20f);
                            NetHit(at, (ballVel.normalized + n * 1.5f).normalized * (0.2f + 0.8f * strength), 0f);
                            Color white = new Color(1f, 1f, 0.95f, 1f);
                            for (int i = 0; i < 4 + (int)(12 * strength); i++) EmitChip(at, (n + RandDir() * 0.8f) * Rand(1.5f, 4f), Rand(0.2f, 0.45f), white, 6f, 1.5f);
                        }
                    }
                    netTouch = at; netFace = n;
                }
                netDepth = depth;
            }
            else
            {
                // taken out of the goal: the netting falls back, and swings a little
                if (netDepth > 0.08f && netGrip > 0.5f && netHole <= 0f && sinceNet > 0.5f) NetHit(netTouch, -netFace * Mathf.Min(0.22f, netDepth * 0.5f), 0f);
                netDepth = 0f;
            }
            float want = held ? 1f : 0f;
            if (dt > 0f) netGrip += (want - netGrip) * (1f - Mathf.Exp(-dt * (want > netGrip ? 30f : 9f)));
            if (netGrip < 0.002f && !held) netGrip = 0f;
            Shader.SetGlobalVector("_G3NetBall", new Vector4(netBall.x, netBall.y, netBall.z, netGrip > 0f ? NetBallRadius : 0f));
            Shader.SetGlobalVector("_G3NetGrip", new Vector4(netGrip, NetGive, 0f, 0f));
            Shader.SetGlobalVector("_G3NetBox", new Vector4(L.halfLength + L.goalD * 0.5f, L.goalH * 0.5f, (L.goalY0 + L.goalY1) * 0.5f, 0f));
        }

        void NetHit(Vector3 at, Vector3 push, float hole)
        {
            netAt = at; netPush = push; netHole = hole; netAge = 0f; sinceNet = 0f;
        }

        void NetBurst(Vector3 at, Vector3 dir)
        {
            NetHit(at, dir * 1.7f, 0.85f);
            Knock(0.75f, 0.34f); punchTint = new Color(1f, 1f, 0.95f); lines = Mathf.Max(lines, 0.9f); linesAt = at;
            Color white = new Color(1f, 1f, 0.96f, 1f);
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized, up = Vector3.Cross(side, dir);
            // the burst and the shock where it goes through
            int b = Emit(at, Vector3.zero, 0.30f, Px * 14f, Px * 56f, white, PixBurst, 0f);
            if (b >= 0) ps[b].Rot = Rand(0f, 0.8f);
            Emit(at, Vector3.zero, 0.30f, Px * 10f, Px * 48f, white, PixRing, 0f);
            waveAt = at; waveAge = 0f; rush = 0.8f;
            // shreds of netting
            for (int i = 0; i < 120; i++)
            {
                float a = Rand(0f, 6.2832f), spread = Rand(0.1f, 1.0f);
                Vector3 v = dir * Rand(4f, 15f) + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * spread * Rand(2f, 7f);
                Vector3 p = at + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * Rand(0f, 0.5f);
                EmitChip(p, v, Rand(0.7f, 1.8f), white, 7f, 1.2f, i % 4 == 0 ? 1f : 0.5f, i % 3 == 0 ? 1f : 0f);
            }
            Color hot = new Color(1f, 0.9f, 0.6f, 1f).linear;
            for (int i = 0; i < 40; i++) EmitEmber(at, dir * Rand(4f, 16f) + RandDir() * Rand(1f, 6f), Rand(0.3f, 0.8f), Color.Lerp(hot, glow, 0.5f), 5f, 1.6f);
        }

        // GOAL: paper from both posts, rockets over the stand side of that goal
        void Celebrate(float side)
        {
            var L = Field.L;
            Knock(0.25f, 0.12f);
            for (int post = 0; post < 2; post++)
            {
                Vector3 at = new Vector3(side * (L.halfLength + 0.4f), 0.3f, post == 0 ? L.goalY0 - 0.8f : L.goalY1 + 0.8f);
                Vector3 aim = new Vector3(-side * 0.35f, 1f, post == 0 ? 0.30f : -0.30f).normalized;
                for (int i = 0; i < 110; i++)
                {
                    Vector3 v = (aim + RandDir() * 0.42f) * Rand(7f, 15f);
                    int k = EmitChip(at, v, Rand(2.2f, 4.2f), A(Festive[rng.Next(Festive.Length)], 1f), 4.2f, 1.5f, i % 3 == 0 ? 1.5f : 1f, 1f);
                    if (k >= 0) ps[k].Tumble = 1f;                  // (flutters down like paper)
                }
                for (int i = 0; i < 26; i++) EmitEmber(at, (aim + RandDir() * 0.3f) * Rand(6f, 16f), Rand(0.4f, 0.9f), Festive[0], 5f, 1.4f, 0f, 1f);
                Emit(at + Vector3.up * 0.3f, Vector3.zero, 0.22f, Px * 8f, Px * 34f, A(Festive[0], 1f), PixBurst, 0f);
            }
            shells.Clear();
            for (int i = 0; i < 5; i++)
                shells.Add(new Shell { Time = clock + 0.35f + i * 0.42f + Rand(0f, 0.15f), Col = Festive[(i * 2 + rng.Next(2)) % Festive.Length],
                                       Pos = new Vector3(side * (L.halfLength + Rand(3f, 13f)), Rand(13f, 22f), Rand(-13f, 13f)) });
        }

        void Firework(Vector3 at, Color col)
        {
            int b = Emit(at, Vector3.zero, 0.34f, Px * 30f, Px * 150f, A(col, 1f), PixBurst, 0f);
            if (b >= 0) ps[b].Rot = Rand(0f, 0.8f);
            Color white = Color.Lerp(col, Color.white, 0.55f);
            for (int i = 0; i < 150; i++)
                EmitEmber(at, RandDir() * Rand(7.5f, 11f), Rand(1.2f, 1.9f), i % 4 == 0 ? white : col, 2.6f, 1.9f, i % 3 == 0 ? 3f : 2f, i % 3 == 0 ? 1f : 0f);
            for (int i = 0; i < 40; i++)        // the glitter that hangs and drifts down afterwards
                EmitEmber(at + RandDir() * Rand(0.5f, 3.5f), RandDir() * 1.2f, Rand(1.6f, 2.6f), white, 1.4f, 1.0f, 2f, 1f);
        }

        // ---- the ball

        void Landing(Vector3 ground, float strength)
        {
            int n = 6 + (int)(strength * 9f);
            Color dust = new Color(0.72f, 0.76f, 0.56f, 0.85f).linear; dust.a = 0.85f;
            for (int i = 0; i < n; i++)
            {
                float a = Rand(0f, 6.2832f); Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                EmitPuff(ground + dir * 0.15f + Vector3.up * 0.10f, dir * Rand(0.6f, 2.0f) * (0.6f + strength) + Vector3.up * Rand(0.3f, 1.0f), Rand(0.4f, 0.7f), 3f, Rand(5f, 8f), dust, 0.6f, 2.5f);
            }
            Color blade = new Color(0.30f, 0.46f, 0.12f, 1f).linear; blade.a = 1f;
            for (int i = 0; i < n + 4; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) + 0.6f;
                EmitChip(ground + Vector3.up * 0.05f, d * Rand(1.2f, 3.6f) * (0.5f + strength), Rand(0.3f, 0.6f), blade, 9.8f, 0.5f);
            }
            Emit(ground + Vector3.up * 0.03f, Vector3.zero, 0.26f, Px * 5f, Px * (18f + 14f * strength), new Color(1f, 1f, 0.92f, 0.7f), PixRingPlain, 0f, 0f, 0f, true);
            if (rainAmount > 0.3f) Splash(ground, 0.5f + strength, Vector3.zero);
        }

        static readonly Color WaterCol = new Color(0.80f, 0.90f, 1f, 0.9f);

        // water thrown up from the wet turf: a ring running out and a crown of drops ('along': the way the thing that threw it is going)
        void Splash(Vector3 ground, float strength, Vector3 along)
        {
            Emit(ground + Vector3.up * 0.03f, Vector3.zero, 0.30f, Px * 4f, Px * (16f + 22f * strength), A(WaterCol, 0.75f), PixRingPlain, 0f, 0f, 0f, true);
            int n = 8 + (int)(14f * strength);
            for (int i = 0; i < n; i++)
            {
                float a = Rand(0f, 6.2832f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(0.6f, 2.2f) + along * 0.25f + Vector3.up * Rand(2.0f, 4.6f) * (0.6f + 0.5f * strength);
                EmitChip(ground + Vector3.up * 0.06f, d, Rand(0.25f, 0.5f), WaterCol, 9.8f, 0.3f, i % 3 == 0 ? 1f : 0.5f);
            }
        }

        // kicked (sent off) or stopped dead
        void Struck(Vector3 centre, Vector3 ground, float strength, Vector3 away, bool sentOff)
        {
            bool mighty = power > 0.5f && sinceColoured < 0.5f && sinceBurst > 0.35f;         // a special shot hitting someone, or being hit on
            Vector3 behind = centre + (centre - camPos).normalized * 0.3f;
            if (mighty)
            {
                Knock(0.6f, 0.26f); punchTint = glow;
                sinceBurst = 0f;
                int kb = Emit(behind, Vector3.zero, 0.30f, Px * 14f, Px * 56f, A(glow, 1f), PixBurst, 0f);
                if (kb >= 0) ps[kb].Rot = Rand(0f, 0.8f);
                kb = Emit(behind, Vector3.zero, 0.14f, Px * 8f, Px * 30f, new Color(1f, 1f, 0.95f, 1f), PixBurst, 0f);
                if (kb >= 0) ps[kb].Rot = Rand(0f, 0.8f);
                Emit(new Vector3(centre.x, 0.03f, centre.z), Vector3.zero, 0.34f, Px * 8f, Px * 84f, A(glow, 1f), PixRing, 0f, 0f, 0f, true);
                for (int i = 0; i < 64; i++) EmitEmber(centre, RandDir() * Rand(4f, 14f), Rand(0.25f, 0.75f), glow, 5f, 1.8f, i % 4 == 0 ? 1f : 0f, i % 3 == 0 ? 1f : 0f);
                waveAt = centre; waveAge = 0f; lines = Mathf.Max(lines, 0.7f); linesAt = centre;
            }
            else if (sentOff)
            {
                Color white = new Color(1f, 0.98f, 0.9f, 1f);
                int k = Emit(behind, Vector3.zero, 0.14f, Px * 5f, Px * (12f + 14f * strength), white, PixBurst, 0f);     // the little star of a kick
                if (k >= 0) ps[k].Rot = Rand(0f, 0.8f);
                if (strength > 0.5f) for (int i = 0; i < 8; i++) EmitEmber(centre, (away.normalized * 0.6f + RandDir()) * Rand(2f, 5f), Rand(0.15f, 0.3f), white, 3f, 2f);
            }
            if (ground.y < 0.01f && centre.y < 1.0f)
            {
                Color dust = new Color(0.72f, 0.76f, 0.56f, 0.8f).linear; dust.a = 0.8f;
                Vector3 back = away.sqrMagnitude > 0.01f ? -away.normalized : Vector3.zero;
                for (int i = 0; i < 3 + (int)(strength * 4f); i++)
                    EmitPuff(ground + Vector3.up * 0.10f, back * Rand(0.4f, 1.4f) + RandDir() * 0.5f + Vector3.up * Rand(0.2f, 0.7f), Rand(0.3f, 0.5f), 2.5f, Rand(4f, 6f), dust, 0.5f, 2.5f);
            }
        }

        // the ball takes fire: the first instant it shows a colour of its own (the wind-up of a special shot, or its start)
        void Burst(Vector3 centre, Color colour)
        {
            Knock(0.30f, 0.16f); punchTint = colour;
            Color c = colour;
            Vector3 behind = centre + (centre - camPos).normalized * 0.3f;          // the ball itself stays in front of the light
            sinceBurst = 0f;
            int k = Emit(behind, Vector3.zero, 0.30f, Px * 12f, Px * 42f, A(c, 1f), PixBurst, 0f);       // the burst, in pixel art
            if (k >= 0) ps[k].Rot = Rand(0f, 0.8f);
            k = Emit(new Vector3(centre.x, 0.03f, centre.z), Vector3.zero, 0.36f, Px * 10f, Px * 70f, A(c, 1f), PixRing, 0f, 0f, 0f, true);
            for (int i = 0; i < 46; i++) EmitEmber(centre, RandDir() * Rand(4f, 12f), Rand(0.3f, 0.75f), c, 4.5f, 1.8f);
            waveAt = centre; waveAge = 0f; rush = Mathf.Max(rush, 0.8f);            // and the air: a shock wave, the picture rushing in
        }

        // A special shot is on its way: the picture is hit as hard as the ball.  A double burst and two rings on the turf,
        // a geyser of sparks, the turf torn out, the shock wave, the picture rushing in, impact lines from its edges.
        void Launch(Vector3 centre, Color colour)
        {
            sinceLaunch = 0f; sinceBurst = 0f;
            Knock(0.65f, 0.34f); punchTint = colour;
            Color white = new Color(1f, 1f, 0.95f, 1f);
            Vector3 behind = centre + (centre - camPos).normalized * 0.35f;
            int k = Emit(behind, Vector3.zero, 0.36f, Px * 16f, Px * 66f, A(colour, 1f), PixBurst, 0f);
            if (k >= 0) ps[k].Rot = Rand(0f, 0.8f);
            k = Emit(behind, Vector3.zero, 0.16f, Px * 8f, Px * 36f, white, PixBurst, 0f);
            if (k >= 0) ps[k].Rot = Rand(0f, 0.8f);
            Vector3 g = new Vector3(centre.x, 0.03f, centre.z);
            Emit(g, Vector3.zero, 0.40f, Px * 10f, Px * 124f, A(colour, 1f), PixRing, 0f, 0f, 0f, true);
            Emit(g, Vector3.zero, 0.58f, Px * 6f, Px * 92f, white, PixRing, 0f, 0f, 0f, true);
            for (int i = 0; i < 84; i++) EmitEmber(centre, RandDir() * Rand(5f, 15f), Rand(0.3f, 0.85f), colour, 4.5f, 1.8f, i % 4 == 0 ? 1f : 0f, i % 3 == 0 ? 1f : 0f);
            for (int i = 0; i < 18; i++)
            {
                int e = Emit(centre + new Vector3(Rand(-0.4f, 0.4f), 0f, Rand(-0.4f, 0.4f)), Vector3.up * Rand(12f, 24f), Rand(0.16f, 0.30f), Px * 0.5f, Px * 0.5f, A(Color.Lerp(Color.white, colour, 0.5f), 1f), PixBlock, 0f, 0f, 2f);
                if (e >= 0) ps[e].Stretch = Rand(0.05f, 0.10f);
            }
            if (centre.y < 2.5f)
            {
                Color blade = new Color(0.30f, 0.46f, 0.12f, 1f).linear; blade.a = 1f; Color earth = new Color(0.30f, 0.21f, 0.12f, 1f).linear; earth.a = 1f;
                Vector3 back = ballVel.sqrMagnitude > 1f ? -new Vector3(ballVel.x, 0f, ballVel.z).normalized : Vector3.zero;
                for (int i = 0; i < 26; i++)
                {
                    Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) + 0.5f;
                    EmitChip(g + Vector3.up * 0.05f, (d + back * 0.8f) * Rand(2f, 6f), Rand(0.35f, 0.8f), i % 3 == 0 ? earth : blade, 9.8f, 0.4f, i % 4 == 0 ? 1f : 0.5f);
                }
            }
            waveAt = centre; waveAge = 0f; rush = 1.5f; lines = 1f; linesAt = centre;
            if (styled) { LaunchKind(centre, g, colour); if (Host != null) Host.PlaySfx(Sfx.Theme + style.Kind, 0.75f, 0.02f); }
        }

        int Rate(float perSecond, float dt) { float want = perSecond * dt; return (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0); }

        // ... and each kind of special lets fly in its own way
        void LaunchKind(Vector3 centre, Vector3 g, Color colour)
        {
            Color b = style.B, white = new Color(1f, 1f, 0.95f, 1f);
            Vector3 fwd = ballVel.sqrMagnitude > 1f ? new Vector3(ballVel.x, 0f, ballVel.z).normalized : Vector3.right;
            switch (style.Kind)
            {
                case Fire:
                    // a wall of fire thrown up, and the grass burnt black
                    for (int i = 0; i < 60; i++)
                    {
                        Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 2f + 0.6f;
                        EmitEmber(g + new Vector3(Rand(-0.8f, 0.8f), 0.1f, Rand(-0.8f, 0.8f)), d * Rand(3f, 9f), Rand(0.5f, 1.1f), i % 3 == 0 ? b : colour, 2.5f, 1.4f, i % 3 == 0 ? 2f : 1f, i % 2 == 0 ? 1f : 0f);
                    }
                    Emit(g + Vector3.up * 0.002f, Vector3.zero, 5f, Px * 20f, Px * 20f, new Color(0.03f, 0.025f, 0.02f, 0.70f), PixPuff, 0f, 0f, 0f, true);
                    break;
                case Thunder:
                    // a bolt out of the sky onto the spot
                    // (just beyond the spot as the camera sees it: the shooter stays in front of it)
                    Vector3 beyond = g - camPos; beyond.y = 0f; beyond = beyond.sqrMagnitude > 1e-4f ? beyond.normalized * 1.3f : Vector3.zero;
                    boltAt = g + beyond; boltLife = 0.12f; boltSeed = rng.Next(); strikeFlash = Mathf.Max(strikeFlash, 0.35f);
                    for (int i = 0; i < 40; i++)
                    {
                        float a = Rand(0f, 6.2832f);
                        int k = Emit(g + Vector3.up * 0.15f, new Vector3(Mathf.Cos(a), Rand(0.02f, 0.3f), Mathf.Sin(a)) * Rand(8f, 20f), Rand(0.15f, 0.35f), Px * 0.5f, Px * 0.5f, A(i % 2 == 0 ? b : colour, 1f), PixBlock, 0f, 2f, 3.5f);
                        if (k >= 0) ps[k].Stretch = Rand(0.03f, 0.07f);
                    }
                    break;
                case Wind:
                    // a whirl of dust running out, leaves (or feathers) torn loose
                    for (int i = 0; i < 28; i++)
                    {
                        float a = i / 28f * 6.2832f;
                        Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), t = new Vector3(-o.z, 0f, o.x);
                        EmitPuff(g + o * 0.5f + Vector3.up * 0.2f, o * Rand(3f, 5f) + t * Rand(6f, 9f) + Vector3.up * Rand(0.5f, 2f), Rand(0.5f, 0.9f), 4f, Rand(8f, 12f), new Color(0.85f, 0.88f, 0.78f, 0.55f), -0.6f, 2.2f);
                    }
                    for (int i = 0; i < 46; i++)
                    {
                        Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) + 0.4f;
                        int k = Emit(centre + RandDir() * 0.5f, d * Rand(3f, 9f), Rand(0.7f, 1.4f), Px * 3f, Px * 3f, A(Color.Lerp(colour, b, Rand(0f, 1f)) * 0.85f, 1f), PixLeaf, 0f, 3f, 1.6f);
                        if (k >= 0) ps[k].Rot = rng.Next(4);
                    }
                    break;
                case Water:
                    // a column of water, and rings running out over the wet grass
                    Splash(g, 2.2f, fwd * 3f);
                    for (int i = 0; i < 70; i++)
                        EmitChip(g + new Vector3(Rand(-0.4f, 0.4f), 0.1f, Rand(-0.4f, 0.4f)), new Vector3(Rand(-2.2f, 2.2f), Rand(4f, 11f), Rand(-2.2f, 2.2f)), Rand(0.5f, 1.0f), A(i % 3 == 0 ? b : colour, 0.95f), 13f, 0.3f, i % 4 == 0 ? 1.5f : i % 2 == 0 ? 1f : 0.5f);
                    Emit(g, Vector3.zero, 0.55f, Px * 8f, Px * 110f, A(b, 0.8f), PixRingPlain, 0f, 0f, 0f, true);
                    Emit(g, Vector3.zero, 0.75f, Px * 4f, Px * 80f, A(colour, 0.8f), PixRingPlain, 0f, 0f, 0f, true);
                    break;
                case Blade:
                    // cuts flying out in all directions, and their glints
                    for (int i = 0; i < 10; i++)
                    {
                        float a = Rand(0f, 6.2832f);
                        Vector3 d = camRight * Mathf.Cos(a) + camUp * Mathf.Sin(a);
                        int k = Emit(centre + d * 0.3f, d * Rand(26f, 40f), Rand(0.10f, 0.16f), Px * 0.5f, Px * 0.5f, A(i % 2 == 0 ? white : colour, 1f), PixBlock, 0f, 0f, 2f);
                        if (k >= 0) ps[k].Stretch = Rand(0.05f, 0.08f);
                    }
                    for (int i = 0; i < 12; i++)
                    {
                        int k = Emit(centre + RandDir() * Rand(0.4f, 1.8f), Vector3.zero, Rand(0.15f, 0.35f), Px * 9f, Px * 12f, A(i % 2 == 0 ? white : colour, 1f), PixGlint, 0f);
                        if (k >= 0) ps[k].Rot = rng.Next(2);
                    }
                    break;
                case Dark:
                    // black smoke rolling out, the light drawn back in
                    for (int i = 0; i < 30; i++)
                    {
                        float a = Rand(0f, 6.2832f);
                        EmitPuff(g + Vector3.up * 0.3f, new Vector3(Mathf.Cos(a), Rand(0.1f, 0.8f), Mathf.Sin(a)) * Rand(2f, 6f), Rand(0.7f, 1.3f), 5f, Rand(12f, 18f), A(b, 0.9f), -0.4f, 2.0f);
                    }
                    Emit(g, Vector3.zero, 0.34f, Px * 120f, Px * 10f, A(colour, 1f), PixRing, 0f, 0f, 0f, true);
                    for (int i = 0; i < 40; i++) EmitEmber(centre + RandDir() * Rand(0.5f, 2f), Vector3.up * Rand(0.5f, 2.5f), Rand(0.6f, 1.3f), colour, -1.5f, 0.8f, 0f, 1f);
                    break;
                case Light:
                    // a pillar of light, and stars
                    pillarAt = g; pillarAge = 0f; pillarCol = colour;
                    for (int i = 0; i < 30; i++)
                    {
                        int k = Emit(centre + RandDir() * Rand(0.5f, 2.6f), RandDir() * 0.8f, Rand(0.3f, 0.7f), Px * 5f, Px * 7f, A(i % 2 == 0 ? white : colour, 1f), PixGlint, 0f);
                        if (k >= 0) ps[k].Rot = rng.Next(2);
                    }
                    break;
                case Earth:
                    // the ground heaves: clods, a ring of dust, and a harder jolt
                    Knock(0.95f, 0f);
                    Color earth = new Color(0.30f, 0.21f, 0.12f, 1f).linear; earth.a = 1f; Color turf = new Color(0.30f, 0.46f, 0.12f, 1f).linear; turf.a = 1f;
                    for (int i = 0; i < 70; i++)
                    {
                        Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 1.8f + 0.5f;
                        EmitChip(g + new Vector3(Rand(-0.5f, 0.5f), 0.1f, Rand(-0.5f, 0.5f)), d * Rand(3f, 10f), Rand(0.6f, 1.2f), i % 4 == 0 ? turf : i % 4 == 1 ? A(b, 1f) : earth, 12f, 0.4f, i % 3 == 0 ? 1.5f : 1f);
                    }
                    Color dust = new Color(0.72f, 0.66f, 0.50f, 0.7f).linear; dust.a = 0.7f;
                    for (int i = 0; i < 24; i++)
                    {
                        float a = i / 24f * 6.2832f;
                        EmitPuff(g + Vector3.up * 0.2f, new Vector3(Mathf.Cos(a), Rand(0.05f, 0.3f), Mathf.Sin(a)) * Rand(4f, 7f), Rand(0.6f, 1.0f), 5f, Rand(11f, 16f), dust, 0.3f, 2.6f);
                    }
                    Emit(g + Vector3.up * 0.002f, Vector3.zero, 6f, Px * 16f, Px * 16f, new Color(0.10f, 0.07f, 0.04f, 0.65f), PixPuff, 0f, 0f, 0f, true);
                    break;
            }
        }

        // What a special sheds along its way.  (c: the ball as drawn; dir: where it is going; side, up: across that.)
        void Shed(Vector3 c, Vector3 dir, Vector3 side, Vector3 up, float dt, bool flying)
        {
            Color a = glow, b = styled ? style.B : Color.Lerp(glow, Color.white, 0.5f);
            switch (styled ? style.Kind : Fire)
            {
                case Fire:
                    for (int i = Rate(170f * power, dt); i > 0; i--)
                        EmitEmber(c + RandDir() * 0.3f, -ballVel * 0.12f + RandDir() * Rand(0.8f, 3.4f), Rand(0.3f, 0.8f), styled && i % 4 == 0 ? b : a, 3.5f, 1.5f);
                    break;
                case Thunder:
                    // sparks that jump away and are gone
                    for (int i = Rate(90f, dt); i > 0; i--)
                    {
                        int k = Emit(c + RandDir() * 0.25f, RandDir() * Rand(6f, 14f), Rand(0.08f, 0.2f), Px * 0.5f, Px * 0.5f, A(i % 2 == 0 ? b : a, 1f), PixBlock, 0f, 0f, 3f);
                        if (k >= 0) ps[k].Stretch = Rand(0.03f, 0.06f);
                    }
                    for (int i = Rate(40f, dt); i > 0; i--) EmitEmber(c + RandDir() * 0.3f, -ballVel * 0.1f + RandDir() * Rand(1f, 3f), Rand(0.2f, 0.5f), a, 6f, 1.5f, 0f, 1f);
                    break;
                case Wind:
                {
                    // two strands of air wound round its path, and what the wind carries off
                    float turn = clock * 20f;
                    for (int h = 0; h < 2; h++)
                        for (int i = Rate(55f, dt); i > 0; i--)
                        {
                            float ang = turn + h * 3.1416f + Rand(-0.3f, 0.3f), cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                            int k = Emit(c + (side * cs + up * sn) * Rand(0.45f, 0.7f), -ballVel * 0.35f + (up * cs - side * sn) * 6f, Rand(0.16f, 0.28f), Px * 0.5f, Px * 0.5f, A(h == 0 ? a : b, 0.9f), PixPlain, 0f);
                            if (k >= 0) ps[k].Stretch = Rand(0.10f, 0.18f);
                        }
                    for (int i = Rate(22f, dt); i > 0; i--)
                    {
                        int k = Emit(c + RandDir() * 0.5f, -ballVel * 0.2f + RandDir() * 2.5f + Vector3.up * 1.5f, Rand(0.5f, 0.9f), Px * 3f, Px * 3f, A(Color.Lerp(a, b, Rand(0f, 1f)) * 0.85f, 1f), PixLeaf, 0f, 3f, 1.2f);
                        if (k >= 0) ps[k].Rot = rng.Next(4);
                    }
                    break;
                }
                case Water:
                    // drops flung off, bubbles left hanging, and rings on the grass below
                    for (int i = Rate(150f * power, dt); i > 0; i--)
                        EmitChip(c + RandDir() * 0.3f, -ballVel * 0.15f + RandDir() * Rand(1f, 3.5f) + Vector3.up * Rand(0.5f, 2.5f), Rand(0.3f, 0.6f), A(i % 3 == 0 ? b : a, 0.95f), 9.8f, 0.4f, i % 4 == 0 ? 1f : 0.5f);
                    for (int i = Rate(18f, dt); i > 0; i--)
                        Emit(c + RandDir() * 0.5f, -ballVel * 0.05f + Vector3.up * Rand(0.8f, 1.8f), Rand(0.35f, 0.6f), Px * 1.5f, Px * 3f, A(b, 0.8f), PixRingPlain, 0f);
                    if (Sprites.BallHeight < 1.4f && sinceSplash > 0.10f)
                    {
                        sinceSplash = 0f;
                        Emit(Sprites.BallGround + Vector3.up * 0.03f, Vector3.zero, 0.3f, Px * 4f, Px * 22f, A(b, 0.7f), PixRingPlain, 0f, 0f, 0f, true);
                    }
                    break;
                case Blade:
                    // glints, and long cuts of light along its way
                    for (int i = Rate(22f, dt); i > 0; i--)
                    {
                        int k = Emit(c + RandDir() * Rand(0.2f, 0.8f), -ballVel * 0.02f, Rand(0.12f, 0.2f), Px * 7f, Px * 7f, A(i % 2 == 0 ? b : a, 1f), PixGlint, 0f);
                        if (k >= 0) ps[k].Rot = rng.Next(2);
                    }
                    for (int i = Rate(70f, dt); i > 0; i--)
                    {
                        int k = Emit(c + side * Rand(-0.25f, 0.25f) + up * Rand(-0.25f, 0.25f), -dir * Rand(2f, 5f), Rand(0.10f, 0.18f), Px * 0.5f, Px * 0.5f, A(b, 1f), PixBlock, 0f);
                        if (k >= 0) ps[k].Stretch = Rand(0.25f, 0.5f);
                    }
                    break;
                case Dark:
                    // black smoke, and sparks that drift upwards
                    for (int i = Rate(55f, dt); i > 0; i--)
                        EmitPuff(c + RandDir() * 0.3f, -ballVel * 0.08f + RandDir() * 0.8f + Vector3.up * Rand(0.2f, 1.0f), Rand(0.5f, 0.9f), 3f, Rand(7f, 11f), A(b, 0.85f), -0.5f, 1.5f);
                    for (int i = Rate(70f * power, dt); i > 0; i--)
                        EmitEmber(c + RandDir() * 0.4f, -ballVel * 0.1f + RandDir() * Rand(0.5f, 2f), Rand(0.4f, 0.9f), a, -2.0f, 1.2f, 0f, i % 2 == 0 ? 1f : 0f);
                    break;
                case Light:
                    // stars, and a slow fall of glitter
                    for (int i = Rate(45f, dt); i > 0; i--)
                    {
                        int k = Emit(c + RandDir() * Rand(0.3f, 0.9f), RandDir() * 0.6f - ballVel * 0.03f, Rand(0.25f, 0.45f), Px * 4f, Px * 5f, A(i % 2 == 0 ? b : a, 1f), PixGlint, 0f);
                        if (k >= 0) ps[k].Rot = rng.Next(2);
                    }
                    for (int i = Rate(70f * power, dt); i > 0; i--)
                        EmitEmber(c + RandDir() * 0.3f, -ballVel * 0.1f + RandDir() * Rand(0.5f, 2f), Rand(0.5f, 1.0f), a, 1.5f, 1.2f, 0f, 1f);
                    break;
                case Earth:
                {
                    // the ground torn up under it, dust, and the picture jolted as it goes
                    float low = Mathf.Clamp01(1.4f - Sprites.BallHeight / 2.0f);
                    Color earth = new Color(0.30f, 0.21f, 0.12f, 1f).linear; earth.a = 1f; Color turf = new Color(0.30f, 0.46f, 0.12f, 1f).linear; turf.a = 1f;
                    for (int i = Rate(120f * low, dt); i > 0; i--)
                    {
                        Vector3 d = side * Rand(-1.2f, 1.2f) + Vector3.up * Rand(0.8f, 2.2f) - dir * 0.4f;
                        EmitChip(Sprites.BallGround + side * Rand(-0.4f, 0.4f) + Vector3.up * 0.05f, d * Rand(1.5f, 4f), Rand(0.4f, 0.8f), i % 3 == 0 ? turf : i % 3 == 1 ? earth : A(b, 1f), 9.8f, 0.4f, i % 3 == 0 ? 0.5f : 1f);
                    }
                    Color dust = new Color(0.72f, 0.66f, 0.50f, 0.6f).linear; dust.a = 0.6f;
                    for (int i = Rate(45f, dt); i > 0; i--)
                        EmitPuff((low > 0.3f ? Sprites.BallGround + Vector3.up * 0.15f : c) + RandDir() * 0.3f, -ballVel * 0.05f + RandDir() * 0.6f + Vector3.up * Rand(0.2f, 0.8f), Rand(0.5f, 0.9f), 4f, Rand(8f, 13f), dust, 0.3f, 1.5f);
                    for (int i = Rate(50f * power, dt); i > 0; i--) EmitEmber(c + RandDir() * 0.3f, -ballVel * 0.12f + RandDir() * Rand(0.8f, 3f), Rand(0.3f, 0.6f), a, 5f, 1.5f);
                    if (flying && low > 0.5f && sinceRumble > 0.16f) { sinceRumble = 0f; Knock(0.16f, 0f); }
                    break;
                }
            }
        }

        // jagged arcs of light jumping from a point (thunder): a new set for every seed
        void Arcs(Vector3 c, int seed, int n, float reach, Color col, Color core)
        {
            var r = new System.Random(seed);
            for (int k = 0; k < n; k++)
            {
                float ang = (float)r.NextDouble() * 6.2832f, len = reach * (0.5f + 0.5f * (float)r.NextDouble());
                Vector3 d = camRight * Mathf.Cos(ang) + camUp * Mathf.Sin(ang), perp = camUp * Mathf.Cos(ang) - camRight * Mathf.Sin(ang);
                Vector3 p = c;
                for (int j = 1; j <= 4; j++)
                {
                    Vector3 q = c + d * (len * j / 4f) + perp * ((float)(r.NextDouble() - 0.5) * len * 0.45f);
                    Line(p, q, Px * 1f, col, 0f, PixBlock, new Vector4(1f, 0f, 2f, 0f));
                    Line(p, q, Px * 0.5f, core, 0f, PixBlock, new Vector4(1f, 0f, 3f, 0f));
                    p = q;
                }
            }
        }

        // The light round a special ball: a comet of fire (also of water, of darkness), or what its kind calls for.
        void BallAura(Vector3 c)
        {
            Vector3 back = -ballVel;
            float bx = Vector3.Dot(back, camRight), by = Vector3.Dot(back, camUp);
            float rot = bx * bx + by * by > 1f ? Mathf.Atan2(by, bx) : 1.5708f;                 // standing still it burns upwards
            float tail = Mathf.Clamp01(Mathf.Sqrt(bx * bx + by * by) / 16f);
            float step = Mathf.Floor(clock * StepRate); int si = (int)step;
            Vector3 behind = c + (c - camPos).normalized * 0.25f;
            Color col = A(glow, Mathf.Clamp01(power * 1.6f)), second = A(styled ? style.B : Color.white, Mathf.Clamp01(power * 1.6f));
            float half;
            switch (styled ? style.Kind : Fire)
            {
                case Thunder:
                    half = Px * 30f;
                    Billboard(behind, half, half, 0f, col, PixBurst, 0f, new Vector4(30f, step, 0.14f, step * 1.7f));
                    Arcs(c, si * 31 + 7, 3, 1.5f, col, second);
                    break;
                case Wind:
                    half = Px * (18f + 6f * (si & 1));
                    Billboard(behind, half, half, 0f, col, PixRing, 0f, new Vector4(Mathf.Round(half / Px), step, 0.22f, 0f));
                    half = Px * (28f - 6f * (si & 1));
                    Billboard(behind, half, half, 0f, second, PixRing, 0f, new Vector4(Mathf.Round(half / Px), step, 0.12f, 0f));
                    break;
                case Blade:
                    half = Px * 34f;
                    Billboard(behind, half, half, 0f, col, PixGlint, 0f, new Vector4(34f, step, 0.05f + 0.12f * (si & 1), si & 1));
                    half = Px * 20f;
                    Billboard(behind, half, half, 0f, second, PixGlint, 0f, new Vector4(20f, step, 0.05f, 1 - (si & 1)));
                    break;
                case Light:
                    half = Px * 36f;
                    Billboard(behind, half, half, 0f, col, PixBurst, 0f, new Vector4(36f, step, 0.14f, step * 0.25f));
                    half = Px * 20f;
                    Billboard(behind, half, half, 0f, second, PixRing, 0f, new Vector4(20f, step, 0.16f, 0f));
                    break;
                case Earth:
                    half = Px * (16f + 4f * (si & 1));
                    Billboard(behind, half, half, 0f, col, PixRing, 0f, new Vector4(Mathf.Round(half / Px), step, 0.30f, 0f));
                    break;
                default:
                    // The flame: pixel art like the ball itself, on the same grid, moving in steps - a comet whose head sits
                    // behind the ball and whose tongues trail off where it came from.  Its heart is far brighter than white,
                    // so the bloom makes it glow; the light it throws on turf and figures is real (see Lighting).
                    half = Px * 34f * (0.75f + 0.25f * power);
                    Vector3 axis = camRight * Mathf.Cos(rot) + camUp * Mathf.Sin(rot);
                    Billboard(behind + axis * (0.45f * half), half, half, rot, col, PixFlame, 0f, new Vector4(Mathf.Round(half / Px), step, tail, 0f));
                    break;
            }
        }

        // ---- the standstill

        float Hash(int i, int k)
        {
            uint x = unchecked((uint)(i * 374761393 + k * 668265263) ^ (uint)superSeed);
            x = unchecked((x ^ (x >> 13)) * 1274126177u); x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        void BeginSuper()
        {
            superOn = true; superT = 0f; superSeed = rng.Next();
            Style st;
            if (Styles.TryGetValue(Host.FreezeNo, out st)) { style = st; styled = true; styleAge = 0f; }
            Host.PlaySfx(Sfx.Flash, 1f);
        }

        // it lets go: the burst of the shot, at once (not when the ball has been seen flying for two ticks, as otherwise)
        void EndSuper()
        {
            superOn = false;
            Host.PlaySfx(Sfx.Boom, 0.8f);
            if (Sprites.BallShown)
            {
                Launch(Sprites.BallDrawn, styled ? style.A : glow);
                sinceColoured = 0f; power = 1f; glow = styled ? style.A : glow;
            }
        }

        // What glows in the dark while the world stands still: light gathering round the shooter.  (All of it is worked out
        // from the time alone - nothing here is a particle - and drawn after the darkness, into the standstill's own mesh.)
        void DrawSuper()
        {
            int hero = Host.FreezeShooter;
            bool shown = hero >= 0 && hero < SoccerBridge.Ball && Sprites.Shown(hero);
            Vector3 g = shown ? Sprites.Ground(hero) : Sprites.BallGround;
            float lift = shown ? Sprites.HeightOf(hero) / Field.L.kz * Px : 0f;          // (a figure's height goes up the picture: see SpriteField.BallDrawn)
            Vector3 chest = g + camUp * (lift + 14f * Px);
            superAt = chest;
            float u = Mathf.Clamp01(superT / GameHost.FreezeTime), a = Mathf.Clamp01(night * 1.2f);
            float step = Mathf.Floor(superClock * StepRate); int si = (int)step;
            Color c1 = style.A, c2 = style.B, white = new Color(1f, 1f, 0.95f, 1f);
            Vector3 away = (chest - camPos).normalized, behind = chest + away * 0.5f, front = chest - away * 0.5f;
            if (!styled) { c1 = Gold; c2 = white; }

            // on the turf: a pool of light, rings closing in on him
            FlatQuad(g + Vector3.up * 0.02f, 2.3f, 2.3f, A(c1 * 1.3f, 0.26f * a), Disc, 1f);
            for (int k = 0; k < 3; k++)
            {
                // (flat colour, not light: three rings of light this size would glare the shooter away)
                float ph = Mathf.Floor(Mathf.Repeat(u * 1.8f + k / 3f, 1f) * 8f) / 8f;
                float r = Px * Mathf.Round(84f - 70f * ph);
                FlatQuad(g + Vector3.up * 0.03f, r, r, A(Color.Lerp(k == 1 ? c2 : c1, Color.white, 0.15f) * 0.9f, 0.85f * a * (0.35f + 0.65f * ph)), PixRingPlain, 0f, false, new Vector4(Mathf.Round(r / Px), step, 0.09f, 0f));
            }
            // behind him: a star of light that grows
            float grow = Mathf.Floor(Mathf.Min(1f, superT / 0.20f) * 5f) / 5f;
            float half = Px * (36f + 44f * grow);
            Billboard(behind, half, half, 0f, A(c1, a), PixBurst, 0f, new Vector4(Mathf.Round(half / Px), step, 0.14f, step * 0.7f));
            half = Px * (14f + 16f * grow);
            Billboard(behind - away * 0.1f, half, half, 0f, A(Color.Lerp(c1, white, 0.5f), a), PixBurst, 0f, new Vector4(Mathf.Round(half / Px), step, 0.10f, -step * 0.9f));
            // light drawn in from all round
            for (int i = 0; i < 28; i++)
            {
                float h1 = Hash(i, 1), h2 = Hash(i, 2), ang = Hash(i, 3) * 6.2832f;
                float ph = Mathf.Repeat(u * (1.6f + h2) + h1, 1f);
                Vector3 d = camRight * Mathf.Cos(ang) + camUp * Mathf.Sin(ang);
                float r0 = 1.0f + 6.0f * (1f - ph), len = 0.5f + 1.2f * (1f - ph);
                Line(behind + d * (r0 + len), behind + d * r0, Px * (h2 > 0.7f ? 1f : 0.5f), A(Color.Lerp(white, c1, h1), a), 0f, PixBlock, new Vector4(1f, step, ph > 0.6f ? 3f : 2f, 0f));
            }
            // his aura: light streaming up round him
            for (int i = 0; i < 16; i++)
            {
                float h1 = Hash(i, 5), h2 = Hash(i, 6);
                float ph = Mathf.Repeat(superClock * (1.4f + h2) + h1, 1f);
                Vector3 p = g + camRight * ((h1 - 0.5f) * 1.4f) + camUp * (ph * 2.6f) + away * (h2 > 0.5f ? 0.3f : -0.3f);
                Line(p, p + camUp * (0.25f + 0.4f * h2), Px * 0.5f, A(Color.Lerp(c1, white, ph), a), 0f, PixBlock, new Vector4(1f, step, ph < 0.4f ? 3f : ph < 0.8f ? 2f : 1f, 0f));
            }
            // and the sign of its kind
            switch (styled ? style.Kind : Light)
            {
                case Fire:
                    for (int k = -1; k <= 1; k++)
                    {
                        half = Px * (k == 0 ? 40f : 28f);
                        Billboard(behind + away * 0.1f + camRight * (k * 0.75f) + camUp * (half * 0.45f - 0.6f), half, half, 1.5708f, A(k == 0 ? c1 : c2, a), PixFlame, 0f, new Vector4(Mathf.Round(half / Px), step + k * 3f, 0.9f, 0f));
                    }
                    break;
                case Thunder:
                    Arcs(chest, si * 31 + superSeed, 6, 2.8f, A(c1, a), A(white, a));
                    break;
                case Wind:
                    for (int k = 0; k < 4; k++)
                    {
                        float ph = Mathf.Repeat(superClock * 1.5f + k / 4f, 1f);
                        float r = Px * Mathf.Round(30f - 14f * ph);
                        FlatQuad(g + Vector3.up * (0.1f + 2.6f * ph), r, r, A(k % 2 == 0 ? c1 : c2, a * (1f - ph * 0.5f)), PixRing, 0f, false, new Vector4(Mathf.Round(r / Px), step, 0.18f, 0f));
                    }
                    break;
                case Water:
                    for (int i = 0; i < 22; i++)
                    {
                        float h1 = Hash(i, 8), h2 = Hash(i, 9), ph = Mathf.Repeat(superClock * (0.9f + h2) + h1, 1f), ang = h1 * 6.2832f + i;
                        Vector3 p = g + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.5f + 0.8f * h2) + Vector3.up * (ph * 2.4f);
                        Billboard(p, Px * (h2 > 0.6f ? 1f : 0.5f), Px * (h2 > 0.6f ? 1.5f : 1f), 0f, A(i % 3 == 0 ? c2 : c1, a), PixBlock, 0f, new Vector4(1f, step, ph < 0.5f ? 3f : 2f, 0f));
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        float ph = Mathf.Floor(Mathf.Repeat(superClock * 1.2f + k * 0.5f, 1f) * 8f) / 8f, r = Px * Mathf.Round(10f + 60f * ph);
                        FlatQuad(g + Vector3.up * 0.035f, r, r, A(c2, a * (1f - ph)), PixRing, 0f, false, new Vector4(Mathf.Round(r / Px), step, 0.10f, 0f));
                    }
                    break;
                case Blade:
                    if (si % 3 != 2)
                        for (int k = 0; k < 2; k++)
                        {
                            float ang = (k == 0 ? 0.6f : 2.54f) + 0.15f * Mathf.Sin(si * 1.7f);
                            Vector3 d = camRight * Mathf.Cos(ang) + camUp * Mathf.Sin(ang);
                            Line(behind - d * 7f, behind + d * 7f, Px * 1.5f, A(c1, a), 0f, PixBlock, new Vector4(1f, step, 2f, 0f));
                            Line(behind - d * 7f, behind + d * 7f, Px * 0.5f, A(white, a), 0f, PixBlock, new Vector4(1f, step, 3f, 0f));
                        }
                    for (int i = 0; i < 4; i++)
                    {
                        float h1 = Hash(i + si * 13, 11), h2 = Hash(i + si * 13, 12);
                        half = Px * (6f + 6f * h2);
                        Billboard(front + camRight * ((h1 - 0.5f) * 5f) + camUp * ((h2 - 0.4f) * 3.4f), half, half, 0f, A(white, a), PixGlint, 0f, new Vector4(Mathf.Round(half / Px), step, 0.1f, i & 1));
                    }
                    break;
                case Dark:
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = i / 12f * 6.2832f - superClock * 2.4f, h1 = Hash(i, 14);
                        Vector3 p = g + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (1.5f - 0.7f * u) + Vector3.up * (0.25f + 0.5f * h1);
                        half = Px * (6f + 4f * h1);
                        Billboard(p, half, half, 0f, A(c2 * 1.5f, 0.9f * a), PixPuff, 0f, new Vector4(Mathf.Round(half / Px), step + i, 0.2f, 0f));
                    }
                    half = Px * Mathf.Round(70f - 44f * Mathf.Floor(u * 8f) / 8f);
                    Billboard(behind + away * 0.1f, half, half, 0f, A(c1, a), PixRing, 0f, new Vector4(Mathf.Round(half / Px), step, 0.10f, 0f));
                    break;
                case Light:
                    Segment(g + away * 0.6f, g + away * 0.6f + Vector3.up * 18f, 1.3f, 2.0f, A(c1 * 1.5f, 0.30f * a), A(c1 * 1.5f, 0.10f * a), 1f);
                    Segment(g + away * 0.6f, g + away * 0.6f + Vector3.up * 18f, 0.35f, 0.5f, A(Color.white * 1.8f, 0.34f * a), A(Color.white * 1.8f, 0.12f * a), 1f);
                    for (int i = 0; i < 7; i++)
                    {
                        float h1 = Hash(i + si * 13, 16), h2 = Hash(i + si * 13, 17);
                        half = Px * (4f + 5f * h2);
                        Billboard(front + camRight * ((h1 - 0.5f) * 4.4f) + camUp * ((h2 - 0.35f) * 4f), half, half, 0f, A(i % 2 == 0 ? white : c1, a), PixGlint, 0f, new Vector4(Mathf.Round(half / Px), step, 0.1f, i & 1));
                    }
                    break;
                case Earth:
                {
                    float rise = u * u * (3f - 2f * u);
                    Color stone = c2; stone.a = 1f; Color earth = new Color(0.30f, 0.21f, 0.12f, 1f).linear; earth.a = 1f;
                    for (int i = 0; i < 20; i++)
                    {
                        float h1 = Hash(i, 19), h2 = Hash(i, 20), ang = h1 * 6.2832f + i;
                        Vector3 p = g + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.5f + 1.4f * h2) + Vector3.up * (0.08f + (0.3f + 1.7f * h1) * rise);
                        half = Px * (h2 > 0.6f ? 1.5f : 1f);
                        Billboard(p, half, half, 0f, A(i % 3 == 0 ? earth : stone, a), PixPlain, 0f, new Vector4(Mathf.Round(half / Px), step, i % 2 == 0 ? 2f : 3f, 0f));
                    }
                    float r = Px * Mathf.Round(12f + 50f * Mathf.Floor(u * 8f) / 8f);
                    FlatQuad(g + Vector3.up * 0.035f, r, r, new Color(0.72f, 0.66f, 0.50f, 0.8f * a), PixRingPlain, 0f, false, new Vector4(Mathf.Round(r / Px), step, 0.22f, 0f));
                    break;
                }
            }
            // the first instant: a glint over him, as when a blade is drawn
            if (superT < 0.13f)
            {
                float t = Mathf.Floor(superT / 0.13f * 4f) / 4f;
                Vector3 at = front + camUp * (10f * Px);            // (over his head, so that he is not blotted out)
                half = Px * 60f;
                Billboard(at, half, half, 0f, A(white, a), PixGlint, 0f, new Vector4(60f, step, 0.2f + t * 0.75f, 0f));
                half = Px * 36f;
                Billboard(at, half, half, 0f, A(c1, a), PixGlint, 0f, new Vector4(36f, step, 0.2f + t * 0.75f, 1f));
            }
            // and the ball waits in a ring of its own light
            if (Sprites.BallShown)
            {
                Vector3 bc = Sprites.BallDrawn, bb = bc + (bc - camPos).normalized * 0.25f;
                half = Px * (14f + 3f * (si & 1));
                Billboard(bb, half, half, 0f, A(c2, a), PixRing, 0f, new Vector4(Mathf.Round(half / Px), step, 0.24f, 0f));
            }
        }

        // from here on the drawing goes into the standstill's mesh (true) or back into the effects' own (false)
        bool intoStandstill;
        void Into(bool standstill)
        {
            if (standstill == intoStandstill) return;
            intoStandstill = standstill;
            var v = verts; verts = vertsB; vertsB = v; var c = cols; cols = colsB; colsB = c;
            var a = uv0; uv0 = uv0B; uv0B = a; var b = uv1; uv1 = uv1B; uv1B = b; var d = uv2; uv2 = uv2B; uv2B = d; var t = tris; tris = trisB; trisB = t;
        }

        void Strike(Vector3 at)
        {
            Knock(1.25f, 0f); strikeFlash = 1f;
            Color c = new Color(0.62f, 0.78f, 1f, 1f).linear; c.a = 1f;
            Color white = new Color(0.92f, 0.96f, 1f, 1f);
            Vector3 away = at - camPos; away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized * 0.9f : Vector3.zero;      // the light sits behind whoever stands there
            Emit(at + Vector3.up * 0.04f, Vector3.zero, 0.46f, Px * 24f, Px * 270f, c, PixRing, 0f, 0f, 0f, true);
            Emit(at + Vector3.up * 0.05f, Vector3.zero, 0.30f, Px * 10f, Px * 140f, white, PixRing, 0f, 0f, 0f, true);
            int b = Emit(at + away + Vector3.up * 0.8f, Vector3.zero, 0.30f, Px * 34f, Px * 170f, c, PixBurst, 0f);
            if (b >= 0) ps[b].Rot = Rand(0f, 0.8f);
            b = Emit(at + away + Vector3.up * 0.8f, Vector3.zero, 0.16f, Px * 16f, Px * 80f, white, PixBurst, 0f);
            if (b >= 0) ps[b].Rot = Rand(0f, 0.8f);
            waveAt = at + Vector3.up; waveAge = 0f; lines = Mathf.Max(lines, 0.7f); linesAt = at + Vector3.up;
            for (int i = 0; i < 110; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 1.4f + 0.3f;
                EmitEmber(at + Vector3.up * 0.2f, d * Rand(4f, 16f), Rand(0.4f, 1.1f), c, 9f, 1.2f, i % 3 == 0 ? 2f : 1f, i % 2 == 0 ? 1f : 0f);
            }
            // sparks skittering out over the grass
            for (int i = 0; i < 44; i++)
            {
                float a = Rand(0f, 6.2832f);
                int k = Emit(at + Vector3.up * 0.15f, new Vector3(Mathf.Cos(a), Rand(0.02f, 0.22f), Mathf.Sin(a)) * Rand(9f, 22f), Rand(0.18f, 0.42f), Px * 0.5f, Px * 0.5f, A(Color.Lerp(white, c, 0.5f), 1f), PixBlock, 0f, 2f, 3.5f);
                if (k >= 0) ps[k].Stretch = Rand(0.03f, 0.07f);
            }
            // turf and earth thrown up
            Color blade = new Color(0.30f, 0.46f, 0.12f, 1f).linear; blade.a = 1f; Color earth = new Color(0.30f, 0.21f, 0.12f, 1f).linear; earth.a = 1f;
            for (int i = 0; i < 48; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 1.6f + 0.5f;
                EmitChip(at + Vector3.up * 0.1f, d * Rand(3f, 9f), Rand(0.5f, 1.1f), i % 3 == 0 ? blade : earth, 9.8f, 0.4f, i % 4 == 0 ? 1f : 0.5f);
            }
            Color smoke = new Color(0.50f, 0.50f, 0.47f, 0.8f).linear; smoke.a = 0.8f;
            for (int i = 0; i < 18; i++)
                EmitPuff(at + Vector3.up * 0.2f, RandDir() * 1.2f + Vector3.up * Rand(0.6f, 2.2f), Rand(0.9f, 1.8f), 6f, Rand(14f, 26f), smoke, -0.3f, 1.2f);
            // the scorched patch it leaves, and what goes on glowing in it
            Emit(at + Vector3.up * 0.022f, Vector3.zero, 9f, Px * 30f, Px * 30f, new Color(0.03f, 0.025f, 0.02f, 0.80f), PixPuff, 0f, 0f, 0f, true);
            Color hot = new Color(1f, 0.55f, 0.18f, 1f).linear;
            for (int i = 0; i < 16; i++)
                EmitEmber(at + new Vector3(Rand(-1f, 1f), 0.05f, Rand(-1f, 1f)) * 0.9f, Vector3.up * Rand(0.05f, 0.4f), Rand(1.2f, 3.5f), hot, 0f, 0f, 0.5f, 1f);
        }

        // ------------------------------------------------------------------ one picture

        /// <summary>Call once per picture, after the sprites were rebuilt.  dt = time since the last picture (0: only redraw).</summary>
        public void Frame(float dt)
        {
            Into(false);
            verts.Clear(); cols.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); tris.Clear();
            vertsB.Clear(); colsB.Clear(); uv0B.Clear(); uv1B.Clear(); uv2B.Clear(); trisB.Clear();
            gverts.Clear(); gcols.Clear(); guv.Clear(); gcell.Clear(); gtris.Clear();
            var br = Host != null ? Host.Bridge : null;
            bool match = br != null && br.InMatch && br.TickCount > 0;
            if (!match || !On)
            {
                count = 0; trail.Clear(); wake.Clear(); shells.Clear(); ticksSeen = 0; power = 0; leafAmount = rainAmount = flash = punch = boltLife = 0f; lastSerial = -1;
                netAge = -1f; netHole = 0f; lastWeather = 0; lastSub = -1; netIn = 0; netGrip = netDepth = 0f;
                Shader.SetGlobalVector("_G3NetHit", new Vector4(0, 0, 0, -1f)); Shader.SetGlobalVector("_G3NetPush", Vector4.zero); Shader.SetGlobalVector("_G3NetBall", Vector4.zero);
                if (Post != null) { Post.Flash = 0f; Post.Rain = 0f; Post.Wave = Post.Heat = Post.Zoom = Vector4.zero; }
                Shader.SetGlobalVector("_G3FxLight", Vector4.zero); waveAge = -1f; rush = 0f;
                gloom = dim = lines = strikeFlash = 0f; specialTicks = 0; flew = false; sheet = 9f; boltEnd = -9f;
                for (int i = 0; i < feetSet.Length; i++) feetSet[i] = false;
                if (Post != null) { Post.Gloom = Post.Dim = 0f; Post.Punch = Post.Lines = Post.Push = Vector4.zero; }
                // (no effects, no standstill either: the game is let go at once)
                if (Host != null) { Host.Freeze = 0f; superSerial = Host.FreezeSerial; if (Host.Tweaks != null) styleSerial = Host.Tweaks.SpecialSerial; }
                superOn = false; night = 0f; styled = false; pillarAge = -1f;
                Shader.SetGlobalVector("_G3Night", Vector4.zero); if (nightRenderer != null) nightRenderer.enabled = false;
                mesh.Clear(); ghostMesh.Clear(); superMesh.Clear();
                return;
            }
            dt = Mathf.Min(dt, 0.1f);
            camRight = Cam.transform.right; camUp = Cam.transform.up; camPos = Cam.transform.position;
            // which special this is (the game has just decided it): its kind and colours
            if (Host.Tweaks.SpecialSerial != styleSerial)
            {
                styleSerial = Host.Tweaks.SpecialSerial;
                Style st; styled = Styles.TryGetValue(Host.Tweaks.LastSpecial, out st);
                if (styled) { style = st; styleAge = 0f; }
            }
            // the standstill: the game is not run, and nothing of the world moves here either - only the light round the shooter
            if (Host.FreezeSerial != superSerial) { superSerial = Host.FreezeSerial; if (Host.Freeze > 0f) BeginSuper(); }
            if (superOn && Host.Freeze <= 0f) EndSuper();
            bool still = superOn;
            float real = dt;
            if (still) { superT += dt; dt = 0f; }
            superClock += real;
            night = still ? night + (1f - night) * (1f - Mathf.Exp(-real * 45f)) : night * Mathf.Exp(-real * 9f);
            if (night < 0.004f && !still) night = 0f;
            clock += dt;
            if (br.TickSerial != lastSerial)
            {
                if (lastSerial >= 0 && br.TickSerial - lastSerial > 1) ticksSeen = 0;       // missed some (paused): start over
                lastSerial = br.TickSerial;
                specialTicks = Sprites.BallShown && Sprites.BallSpecial ? specialTicks + 1 : 0;
                OnTick(br.Cur, br.Prev);
            }
            // GOAL (the status bar says so): celebrate at the goal the ball is in; a new kick-off mends the net
            int sub = br.SubScreen;
            if (sub != lastSub)
            {
                if (sub == 3 && lastSub == 2 && Sprites.BallShown) Celebrate(Sprites.BallGround.x >= 0f ? 1f : -1f);
                if (sub == 2 && lastSub != 2) { netHole = 0f; netAge = -1f; }
                lastSub = sub;
            }
            int weather = br.Cur.Weather;
            if (!still) Simulate(dt, weather);

            // ---- on the turf first: contact shadows, the ball's drop shadow, light from a glowing ball, its wake
            for (int i = 0; i < SoccerBridge.Ball; i++)
            {
                if (!Sprites.Shown(i)) continue;
                float h = Sprites.HeightOf(i);
                float k = Mathf.Clamp01(1f - h / 2.2f);
                FlatQuad(Sprites.Ground(i) + Vector3.up * 0.016f, 0.46f + 0.2f * (1f - k), 0.30f + 0.12f * (1f - k), new Color(0f, 0f, 0f, 0.10f + 0.20f * k), Shade, 0f, true);
            }
            if (Sprites.BallShown)
            {
                float h = Sprites.BallHeight, up = Mathf.Clamp01(h / 3f);
                FlatQuad(Sprites.BallGround + Vector3.up * 0.018f, 0.34f + 0.16f * up, 0.34f + 0.16f * up, new Color(0f, 0f, 0f, 0.34f - 0.10f * up), Shade, 0f);
            }
            for (int i = wake.Count - 1; i > 0; i--)                 // the glowing line a special shot leaves in the grass
            {
                float a0 = 1f - (clock - wake[i].Time) / 0.8f, a1 = 1f - (clock - wake[i - 1].Time) / 0.8f;
                if (a1 <= 0f) break;
                if ((wake[i].Pos - wake[i - 1].Pos).sqrMagnitude > 9f) continue;
                FlatSegment(wake[i].Pos, wake[i - 1].Pos, 0.66f, A(glow * 1.8f, 0.60f * a0 * a0), A(glow * 1.8f, 0.60f * a1 * a1));
                FlatSegment(wake[i].Pos, wake[i - 1].Pos, 0.18f, A(Color.white, 0.55f * a0 * a0 * a0), A(Color.white, 0.55f * a1 * a1 * a1));
            }
            for (int i = 0; i < count; i++) if (ps[i].Flat) Draw(ref ps[i], i);

            // ---- the ball: after-images, trail, glow
            if (Sprites.BallShown)
            {
                Vector3 c = Sprites.BallDrawn;
                // after-images: behind a special shot, and (fainter) behind any hard one
                if (power > 0.25f || speedShown > 11f) Ghosts(c, Mathf.Max(power, Mathf.Clamp01((speedShown - 11f) / 8f) * 0.7f));
                if (power > 0.02f) BallAura(c);
            }
            if (pillarAge >= 0f)
            {
                // the pillar of light a special of that kind goes up in
                float k = 1f - pillarAge / 0.5f; k *= k;
                Vector3 off = (pillarAt - camPos).normalized * 0.6f;
                Segment(pillarAt + off, pillarAt + off + Vector3.up * 20f, 1.5f, 2.4f, A(pillarCol * 1.5f, 0.34f * k), A(pillarCol * 1.5f, 0.10f * k), 1f);
                Segment(pillarAt + off, pillarAt + off + Vector3.up * 20f, 0.4f, 0.6f, A(Color.white * 1.8f, 0.40f * k), A(Color.white * 1.8f, 0.12f * k), 1f);
            }

            // ---- weather
            if (leafAmount > 0.01f)
                for (int i = 0; i < leaves.Length; i++)
                {
                    if (i > leaves.Length * leafAmount) break;
                    var l = leaves[i];
                    float half = Mathf.Round(l.Size * 1.25f / Px) * Px;
                    Billboard(l.Pos, half, half, 0f, A(LeafTints[l.Tint], 1f), PixLeaf, 0f, new Vector4(half / Px, 0f, 0f, Mathf.Floor(l.Phase * 0.9f) % 4f));     // tumbling in quarter turns
                }
            if (rainAmount > 0.01f)
            {
                Vector3 fall = Fall();
                Color rc = new Color(0.92f, 0.96f, 1f).linear;
                for (int i = 0; i < drops.Length; i++)
                {
                    Vector3 p = drops[i].Pos + rainAt;
                    Vector3 v = fall * drops[i].Pace;
                    float near = Mathf.Clamp01(((p - camPos).magnitude - 2.5f) / 2.5f);          // none right in front of the lens
                    if (near <= 0f) continue;
                    rc.a = (0.42f + 0.25f * (i & 3) / 3f) * rainAmount * near;
                    // a hard stroke, as the original draws its rain - but a downpour of them, the nearer ones longer and heavier
                    Line(p, p - v * (0.011f + 0.011f * ((i >> 2) & 3) / 3f), Px * ((i & 7) == 0 ? 0.5f : 0.25f), rc, 0f, PixPlain, new Vector4(1f, 0f, 3f, 0f));
                }
            }
            for (int i = 0; i < count; i++) if (!ps[i].Flat) Draw(ref ps[i], i);
            float echo = clock - boltEnd;        // the channel flickers on twice more after the stroke
            bool again = (echo > 0.07f && echo < 0.11f) || (echo > 0.19f && echo < 0.22f);
            if (boltLife > 0f) Bolt(boltSeed, 1f);
            else if (again) Bolt(boltEchoSeed + (echo > 0.15f ? 7 : 0), 0.6f);

            if (Post != null)
            {
                float far = sheet < 0.07f ? 0.16f : sheet > 0.13f && sheet < 0.20f ? 0.10f : 0f;        // sheet lightning: two quick pulses
                Post.Flash = Mathf.Max(Mathf.Max(flash, far), Mathf.Max(strikeFlash * 0.85f, again ? 0.30f : 0f)); Post.Rain = rainAmount;
                Post.Punch = new Vector4(punchTint.r, punchTint.g, punchTint.b, punch);
                Post.Gloom = gloom; Post.Dim = dim * (1f - 0.6f * gloom);       // (under storm light it is dark enough already)
                if (sinceLaunch < 0.5f && Sprites.BallShown) linesAt = Sprites.BallDrawn;
                Vector3 lv = Cam.WorldToViewportPoint(linesAt);
                Post.Lines = lines > 0.03f && lv.z > 0f ? new Vector4(lv.x, lv.y, Mathf.Clamp01(lines * 1.5f), Mathf.Floor(clock * StepRate)) : Vector4.zero;
            }
            Lighting();
            // the standstill: the darkness, the picture drawn in towards the shooter, and what glows in the dark
            if (night > 0f)
            {
                Into(true); DrawSuper(); Into(false);
                Shader.SetGlobalVector("_G3Night", new Vector4(superAt.x, superAt.y, superAt.z, night));
                if (Post != null)
                {
                    Vector3 sv = Cam.WorldToViewportPoint(superAt);
                    Post.Push = sv.z > 0f ? new Vector4(sv.x, sv.y, 0f, 0.10f * night * night * (3f - 2f * night)) : Vector4.zero;
                }
            }
            else
            {
                Shader.SetGlobalVector("_G3Night", Vector4.zero);
                if (Post != null) Post.Push = Vector4.zero;
            }
            if (nightRenderer != null) nightRenderer.enabled = night > 0f;
            superMesh.Clear();
            if (vertsB.Count > 0)
            {
                superMesh.SetVertices(vertsB); superMesh.SetColors(colsB); superMesh.SetUVs(0, uv0B); superMesh.SetUVs(1, uv1B); superMesh.SetUVs(2, uv2B); superMesh.SetTriangles(trisB, 0, false);
                superMesh.bounds = new Bounds(Vector3.zero, new Vector3(600, 300, 600));
            }
            HoldNet(dt);
            Shader.SetGlobalVector("_G3NetHit", new Vector4(netAt.x, netAt.y, netAt.z, netAge));
            Shader.SetGlobalVector("_G3NetPush", new Vector4(netPush.x, netPush.y, netPush.z, netHole));
            mesh.Clear();
            if (verts.Count > 0)
            {
                mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2); mesh.SetTriangles(tris, 0, false);
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(600, 300, 600));
            }
            ghostMesh.Clear();
            if (gverts.Count > 0)
            {
                ghostMesh.SetVertices(gverts); ghostMesh.SetColors(gcols); ghostMesh.SetUVs(0, guv); ghostMesh.SetUVs(1, gcell); ghostMesh.SetTriangles(gtris, 0, false);
                ghostMesh.bounds = new Bounds(Vector3.zero, new Vector3(600, 300, 600));
            }
        }

        // What a special shot does to the scene itself: real light on turf, goals and figures, hot air round the ball,
        // and for the instant it is let fly a shock wave through the air and the picture rushing towards it.
        void Lighting()
        {
            bool lit = Sprites.BallShown && power > 0.02f;
            float flicker = 0.85f + 0.15f * Mathf.Sin(Mathf.Floor(clock * StepRate) * 2.4f);
            Vector3 c = Sprites.BallCentre, drawn = Sprites.BallDrawn;
            float after = clock - boltEnd;
            bool echoing = (after > 0.07f && after < 0.11f) || (after > 0.19f && after < 0.22f);
            if (boltLife > 0f || echoing)
            {
                // the lightning lights the pitch round the spot it strikes (one light at a time: it outshines a glowing ball)
                Shader.SetGlobalVector("_G3FxLight", new Vector4(boltAt.x, 3.5f, boltAt.z, 22f));
                Shader.SetGlobalVector("_G3FxLightCol", new Vector4(0.62f, 0.78f, 1f, 1f) * ((boltLife > 0f ? 2.4f : 1.4f) * flicker));
            }
            else
            {
                Shader.SetGlobalVector("_G3FxLight", lit ? new Vector4(c.x, c.y + 0.2f, c.z, 6.5f) : Vector4.zero);
                Shader.SetGlobalVector("_G3FxLightCol", (Vector4)(glow * (2.6f * power * flicker)));
            }
            if (Post == null) return;
            Post.Clock = clock;
            float blaze = lit ? Mathf.Clamp01(power * 2f - 0.9f) : 0f;
            Post.Heat = blaze > 0f ? Disc2D(drawn, 1.5f, blaze) : Vector4.zero;
            Post.Wave = waveAge >= 0f ? Disc2D(waveAt, 0.6f + waveAge * 20f, 1f - waveAge / 0.45f) : Vector4.zero;
            Vector3 v = Cam.WorldToViewportPoint(waveAt);
            Post.Zoom = rush > 0f && v.z > 0f ? new Vector4(v.x, v.y, 0f, rush) : Vector4.zero;
        }

        // a sphere in the world as a disc in the picture: centre x, y (0..1), radius (in picture heights), strength
        Vector4 Disc2D(Vector3 at, float radius, float strength)
        {
            Vector3 v = Cam.WorldToViewportPoint(at);
            if (v.z <= 0.1f) return Vector4.zero;
            float r = Mathf.Abs(Cam.WorldToViewportPoint(at + camUp * radius).y - v.y);
            return new Vector4(v.x, v.y, r, strength);
        }

        // After-images: the ball's own tiles again at the places it has just been, as light.
        void Ghosts(Vector3 centre, float amount)
        {
            if (trail.Count < 2 || Sprites.BallQuads.Count == 0) return;
            const int N = 6; const float Gap = 0.042f;
            int at = trail.Count - 1;
            for (int k = 1; k <= N; k++)
            {
                float t = clock - k * Gap;
                while (at > 0 && trail[at - 1].Time > t) at--;
                if (at == 0) break;
                Sample a = trail[at - 1], b = trail[at];
                Vector3 p = Vector3.Lerp(a.Pos, b.Pos, Mathf.InverseLerp(a.Time, b.Time, t));
                if ((p - centre).sqrMagnitude < 0.02f) continue;
                Color c = A(glow * 1.5f, amount * (k <= 2 ? 0.60f : k <= 4 ? 0.36f : 0.18f));      // three flat steps, not a smooth fade
                foreach (var q in Sprites.BallQuads)
                {
                    int v = gverts.Count;
                    gverts.Add(p + camRight * q.X0 + camUp * q.Y0); gverts.Add(p + camRight * q.X1 + camUp * q.Y0);
                    gverts.Add(p + camRight * q.X1 + camUp * q.Y1); gverts.Add(p + camRight * q.X0 + camUp * q.Y1);
                    gcols.Add(c); gcols.Add(c); gcols.Add(c); gcols.Add(c);
                    guv.Add(new Vector2(q.Uv0.x, q.Uv0.y)); guv.Add(new Vector2(q.Uv1.x, q.Uv0.y)); guv.Add(new Vector2(q.Uv1.x, q.Uv1.y)); guv.Add(new Vector2(q.Uv0.x, q.Uv1.y));
                    gcell.Add(q.Cell); gcell.Add(q.Cell); gcell.Add(q.Cell); gcell.Add(q.Cell);
                    gtris.Add(v); gtris.Add(v + 2); gtris.Add(v + 1); gtris.Add(v); gtris.Add(v + 3); gtris.Add(v + 2);
                }
            }
        }

        void Simulate(float dt, int weather)
        {
            // the ball's glow: quick to light up, slower to fade (a squashed ball after a hard kick only gives a flicker)
            bool special = Sprites.BallShown && Sprites.BallSpecial;
            if (!special) specialTicks = 0;
            bool flying = special && specialTicks >= 2;  // a special shot proper, in black and white or in colour (a flicker lasts one tick)
            styleAge += dt;
            if (styled && styleAge > 0.6f && sinceSpecial > 1.2f) styled = false;        // that special is over
            if (pillarAge >= 0f) { pillarAge += dt; if (pillarAge > 0.5f) pillarAge = -1f; }
            sinceSplash += dt; sinceRumble += dt;
            float target = special ? (Sprites.BallColoured || sinceColoured < 0.4f || flying || (styled && styleAge < 4f) ? 1f : 0.38f) : 0f;
            power += (target - power) * (1f - Mathf.Exp(-dt * (target > power ? 28f : 7f)));
            speedShown = Mathf.Lerp(speedShown, ballVel.magnitude, 1f - Mathf.Exp(-dt * (ballVel.magnitude > speedShown ? 40f : 9f)));
            sinceSpecial += dt; sinceColoured += dt; sinceNet += dt; sinceBurst += dt; sinceLaunch += dt; sinceRing += dt; sinceChargeRing += dt; sheet += dt;
            lines *= Mathf.Exp(-dt * 9f); if (lines < 0.03f) lines = 0f;
            punch *= Mathf.Exp(-dt * 13f); if (punch < 0.01f) punch = 0f;
            rush *= Mathf.Exp(-dt * 16f); if (rush < 0.02f) rush = 0f;
            if (waveAge >= 0f) { waveAge += dt; if (waveAge > 0.45f) waveAge = -1f; }
            if (netAge >= 0f) { netAge += dt; if (netAge > 14f && netHole <= 0f) netAge = -1f; }
            if (Sprites.BallShown)
            {
                Vector3 c = Sprites.BallDrawn;
                // a special shot flickers between shapes and colours from tick to tick: the glow keeps the colour through the white frames
                // (a special whose kind is known burns in that kind's colour; otherwise in the ball's own, or gold)
                Color own = styled ? style.A : Sprites.BallColoured || !flying ? Sprites.BallGlow : Gold;
                Color colour = styled || Sprites.BallColoured || sinceColoured > 0.4f ? own : glow;
                glow = Color.Lerp(glow, colour, power < 0.05f ? 1f : 1f - Mathf.Exp(-dt * 14f));
                if (Sprites.BallSpecial)
                {
                    if (Sprites.BallColoured) { if (sinceColoured > 0.6f) Burst(c, styled ? style.A : Sprites.BallGlow); sinceColoured = 0f; }
                    sinceSpecial = 0f;
                }
                if (flying && !flew && sinceLaunch > 0.9f) Launch(c, styled ? style.A : Sprites.BallColoured ? Sprites.BallGlow : sinceColoured < 0.4f ? glow : Gold);
                if (dt > 0f)
                {
                    if (trail.Count > 0 && (trail[trail.Count - 1].Pos - c).sqrMagnitude > 16f) { trail.Clear(); wake.Clear(); }
                    trail.Add(new Sample { Pos = c, Time = clock });
                    while (trail.Count > 0 && clock - trail[0].Time > 0.6f) trail.RemoveAt(0);
                    bool blazing = power > 0.5f && (sinceColoured < 0.25f || flying);
                    bool charging = blazing && !flying && Sprites.BallHeight < 1.6f && ballVel.magnitude < 9f;
                    if (blazing)
                    {
                        Vector3 dir = ballVel.sqrMagnitude > 1f ? ballVel.normalized : Vector3.right;
                        Vector3 side = Vector3.Cross(dir, Vector3.up); side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.forward;
                        Vector3 up = Vector3.Cross(side, dir);
                        int kind = styled ? style.Kind : Fire;
                        // what it sheds along the way: each kind of special its own
                        Shed(c, dir, side, up, dt, flying);
                        float want; int n;
                        // speed lines streaming past
                        if (ballVel.magnitude > 7f)
                        {
                            want = 95f * dt; n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                            for (int i = 0; i < n; i++)
                            {
                                float a = Rand(0f, 6.2832f), r = Rand(0.35f, 1.3f);
                                Vector3 p = c + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r + dir * Rand(-0.3f, 1.2f);
                                int k = Emit(p, -dir * Rand(5f, 9f), Rand(0.12f, 0.2f), Px * 0.5f, Px * 0.5f, A(Color.Lerp(Color.white, glow, 0.4f), 1f), PixBlock, 0f);
                                if (k >= 0) ps[k].Stretch = Rand(0.16f, 0.30f);
                            }
                        }
                        // rings of pressure left hanging in the air along its way
                        if (flying && ballVel.magnitude > 6f && sinceRing > (kind == Light ? 0.07f : 0.10f) && (kind == Fire || kind == Light || kind == Earth || kind == Wind))
                        {
                            sinceRing = 0f;
                            Emit(c + (c - camPos).normalized * 0.3f, -ballVel * 0.06f, 0.26f, Px * 7f, Px * 32f, A(glow, 1f), PixRing, 0f);
                        }
                        // being wound up: rings closing in on it over the turf, sparks drawn in and up
                        if (charging)
                        {
                            Vector3 g0 = Sprites.BallGround;
                            if (sinceChargeRing > 0.20f)
                            {
                                sinceChargeRing = 0f;
                                Emit(new Vector3(g0.x, 0.03f, g0.z), Vector3.zero, 0.24f, Px * 52f, Px * 8f, A(glow, 1f), PixRing, 0f, 0f, 0f, true);
                            }
                            want = 60f * dt; n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                            for (int i = 0; i < n; i++)
                            {
                                float a = Rand(0f, 6.2832f), r = Rand(0.5f, 1.5f);
                                Vector3 from = g0 + new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r);
                                EmitEmber(from, (g0 - from) * 0.8f + Vector3.up * Rand(2.5f, 5.5f), Rand(0.35f, 0.7f), glow, -1.5f, 0.8f, 0f, i % 2 == 0 ? 1f : 0f);
                            }
                        }
                        // low over the grass it leaves a glowing wake and tears up the turf
                        if (Sprites.BallHeight < 2.4f)
                        {
                            Vector3 g = Sprites.BallGround + Vector3.up * 0.025f;
                            if (wake.Count == 0 || (wake[wake.Count - 1].Pos - g).sqrMagnitude > 0.01f) wake.Add(new Sample { Pos = g, Time = clock });
                            if (flying && Sprites.BallHeight < 1.0f && (lastScorch - g).sqrMagnitude > 0.36f && (kind == Fire || kind == Thunder || kind == Dark || kind == Earth))       // ... and a line of scorched grass
                            {
                                lastScorch = g;
                                Emit(new Vector3(g.x, 0.021f, g.z), Vector3.zero, 3.2f, Px * 7f, Px * 7f, new Color(0.03f, 0.025f, 0.02f, 0.55f), PixPuff, 0f, 0f, 0f, true);
                            }
                            want = 45f * dt * Mathf.Clamp01(1.3f - Sprites.BallHeight / 2.4f); n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                            Color blade = new Color(0.30f, 0.46f, 0.12f, 0.95f).linear;
                            for (int i = 0; i < n; i++)
                            {
                                Vector3 d = (side * Rand(-1f, 1f) + Vector3.up * Rand(0.6f, 1.4f) - dir * 0.3f);
                                EmitChip(g + side * Rand(-0.3f, 0.3f), d * Rand(1.5f, 3.5f), Rand(0.3f, 0.6f), A(blade, 1f), 9.8f, 0.5f);
                            }
                        }
                    }
                    while (wake.Count > 0 && clock - wake[0].Time > 0.8f) wake.RemoveAt(0);
                }
            }
            else { trail.Clear(); wake.Clear(); }
            {
                // the hush: while a special shot is wound up and while it flies, the world round it dims
                float hushed = Sprites.BallShown && power > 0.5f ? (flying ? 1f : sinceColoured < 0.3f ? 0.5f : 0f) : 0f;
                dim += (hushed - dim) * (1f - Mathf.Exp(-dt * (hushed > dim ? 12f : 4f)));
                if (dim < 0.01f && hushed <= 0f) dim = 0f;
                flew = flying;
            }

            for (int i = shells.Count - 1; i >= 0; i--)
                if (clock >= shells[i].Time) { Firework(shells[i].Pos, shells[i].Col); shells.RemoveAt(i); }

            for (int i = 0; i < count; )
            {
                ps[i].Age += dt;
                if (ps[i].Age >= ps[i].Life) { ps[i] = ps[--count]; continue; }
                ps[i].Vel.y -= ps[i].Gravity * dt;
                ps[i].Vel *= Mathf.Exp(-ps[i].Drag * dt);
                if (ps[i].Tumble > 0f) ps[i].Vel += new Vector3(Mathf.Sin(ps[i].Age * 5f + i), 0f, Mathf.Cos(ps[i].Age * 4.3f + i * 1.7f)) * (1.6f * dt);     // paper flutters
                ps[i].Pos += ps[i].Vel * dt;
                if (!ps[i].Flat && ps[i].Pos.y < 0.02f) { ps[i].Pos.y = 0.02f; ps[i].Vel = new Vector3(ps[i].Vel.x * 0.4f, 0f, ps[i].Vel.z * 0.4f); ps[i].Tumble = 0f; ps[i].Spin = 0f; }
                i++;
            }

            // ---- weather
            leafAmount = Mathf.MoveTowards(leafAmount, weather == 3 || weather == 4 ? 1f : 0f, dt / 1.5f);
            rainAmount = Mathf.MoveTowards(rainAmount, weather == 2 ? 1f : 0f, dt / 1.2f);
            // a downpour is grey; and when lightning is about to strike the sky blacks out in an instant, to clear again slowly
            float murk = weather == 1 ? 0.82f : weather == 2 ? 0.55f : 0f;
            gloom = Mathf.MoveTowards(gloom, murk, dt / (weather == 1 && murk > gloom ? 0.15f : murk > gloom ? 1.2f : 1.8f));
            if (leafAmount > 0f)
            {
                // the wind comes in gusts: what it carries speeds up and lifts, its lines sweep over the pitch
                gust = Mathf.Max(0.35f, 1f + 0.45f * Mathf.Sin(clock * 0.8f) + 0.30f * Mathf.Sin(clock * 2.3f + 1f));
                Vector3 across = new Vector3(-wind.z, 0f, wind.x).normalized;
                for (int i = 0; i < leaves.Length; i++)
                {
                    leaves[i].Phase += dt * (2.2f + leaves[i].Pace) * gust;
                    float ph = leaves[i].Phase;
                    Vector3 p = leaves[i].Pos + (wind * (leaves[i].Pace * 1.5f * gust) + across * (Mathf.Sin(ph * 0.9f) * 1.6f)) * dt;
                    p.y += (Mathf.Sin(ph * 1.3f) * 1.1f + (gust - 1f) * 1.4f - (p.y - 1.8f) * 0.30f) * dt;
                    if (p.y < 0.08f) p.y = 0.08f;
                    if (p.x > 36f) p.x -= 72f; else if (p.x < -36f) p.x += 72f;
                    if (p.z > 23f) p.z -= 46f; else if (p.z < -23f) p.z += 46f;
                    leaves[i].Pos = p;
                }
                Vector3 wdir = wind.normalized, mid = Sprites.Focus; float wsp = wind.magnitude;
                // the lines of the wind: hard pale strokes racing past, as a comic draws a gale
                float want = 60f * dt * gust * gust * leafAmount; int n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = mid + new Vector3(Rand(-20f, 20f), Rand(0.25f, 6f), Rand(-13f, 13f)) - wdir * 6f;
                    float thick = rng.Next(4) == 0 ? 1f : 0.5f;
                    int k = Emit(p, wdir * (wsp * Rand(2.4f, 3.6f) * gust) + Vector3.up * Rand(-0.4f, 0.6f), Rand(0.30f, 0.55f), Px * thick, Px * thick, new Color(1f, 1f, 0.96f, thick > 0.6f ? 0.42f : 0.60f), PixPlain, 0f);
                    if (k >= 0) ps[k].Stretch = Rand(0.12f, 0.28f);
                }
                if (Orbit != null && gust > 1.35f) Orbit.Shake = Mathf.Max(Orbit.Shake, 0.10f * (gust - 1.35f));      // the camera is buffeted a little at the height of a gust
                // dust bowled along the ground
                Color dust = new Color(0.78f, 0.74f, 0.56f, 0.38f).linear; dust.a = 0.38f;
                want = 14f * dt * gust * leafAmount; n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                for (int i = 0; i < n; i++)
                    EmitPuff(mid + new Vector3(Rand(-18f, 18f), 0.2f, Rand(-12f, 12f)), wdir * (wsp * Rand(0.9f, 1.5f) * gust) + Vector3.up * Rand(0.1f, 0.6f), Rand(0.8f, 1.4f), 8f, Rand(18f, 26f), dust, -0.2f, 0.3f);
                // and the grass laid over where a gust runs across it: a band of light sweeping the pitch
                want = 2.4f * dt * gust * leafAmount; n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
                for (int i = 0; i < n; i++)
                    Emit(mid + new Vector3(Rand(-22f, 22f), 0.02f, Rand(-14f, 14f)) - wdir * 8f, wdir * (wsp * 2.0f * gust), Rand(1.2f, 1.9f), Rand(1.6f, 2.6f), Rand(2.8f, 4.2f), new Color(1f, 1f, 0.75f, 0.12f), Disc, 1f, 0f, 0f, true);
            }
            else gust = 1f;
            if (rainAmount > 0f)
            {
                // the box of rain rides in front of the camera; a drop that leaves it on one side comes back in on the other
                Vector3 ahead = Cam.transform.forward; ahead.y = 0f;
                Vector3 at = Cam.transform.position + (ahead.sqrMagnitude > 1e-4f ? ahead.normalized : Vector3.forward) * (RainBox * 0.8f); at.y = 0f;
                Vector3 moved = at - rainAt; rainAt = at;
                Vector3 fall = Fall();
                Color ring = new Color(0.9f, 0.95f, 1f).linear; ring.a = 0.45f;
                for (int i = 0; i < drops.Length; i++)
                {
                    Vector3 p = drops[i].Pos + fall * (drops[i].Pace * dt) - moved;
                    if (p.x > RainBox) p.x -= 2 * RainBox; else if (p.x < -RainBox) p.x += 2 * RainBox;
                    if (p.z > RainBox) p.z -= 2 * RainBox; else if (p.z < -RainBox) p.z += 2 * RainBox;
                    if (p.y < 0f)
                    {
                        Vector3 hit = new Vector3(p.x + at.x, 0.03f, p.z + at.z);
                        if ((i & 1) == 0) Emit(hit, Vector3.zero, 0.22f, Px * 1f, Px * 4f, A(ring, 0.55f), PixRingPlain, 0f, 0f, 0f, true);
                        if (i % 5 == 0)             // every so often a little crown of drops jumps up
                            for (int k = 0; k < 2; k++) EmitChip(hit, new Vector3(Rand(-0.9f, 0.9f), Rand(1.4f, 2.6f), Rand(-0.9f, 0.9f)), Rand(0.16f, 0.28f), WaterCol, 9.8f, 0.2f);
                        p = new Vector3(Rand(-RainBox, RainBox), RainTop + p.y, Rand(-RainBox, RainBox));
                    }
                    drops[i].Pos = p;
                }
                if (dt > 0f && rainAmount > 0.3f)
                {
                    // boots and ball throw the water up from the wet turf
                    for (int i = 0; i < SoccerBridge.Ball; i++)
                    {
                        if (!Sprites.Shown(i)) { feetSet[i] = false; continue; }
                        Vector3 g = Sprites.Ground(i);
                        if (feetSet[i] && Sprites.HeightOf(i) < 0.25f)
                        {
                            Vector3 v = (g - feetWas[i]) / dt; float sp = v.magnitude;
                            if (sp > 2.2f && sp < 30f)
                            {
                                float w2 = 16f * dt * Mathf.Clamp01(sp / 7f); int m = (int)w2 + (rng.NextDouble() < w2 - (int)w2 ? 1 : 0);
                                for (int k = 0; k < m; k++)
                                    EmitChip(g + new Vector3(Rand(-0.2f, 0.2f), 0.05f, Rand(-0.2f, 0.2f)), -v / sp * Rand(0.4f, 1.6f) + new Vector3(Rand(-0.7f, 0.7f), Rand(1.3f, 3.0f), Rand(-0.7f, 0.7f)), Rand(0.18f, 0.34f), WaterCol, 9.8f, 0.3f);
                            }
                        }
                        feetWas[i] = g; feetSet[i] = true;
                    }
                    Vector3 bv = new Vector3(ballVel.x, 0f, ballVel.z); float bs = bv.magnitude;
                    if (Sprites.BallShown && Sprites.BallHeight < 0.25f && bs > 4f)
                    {
                        float w3 = 34f * dt * Mathf.Clamp01(bs / 12f); int m = (int)w3 + (rng.NextDouble() < w3 - (int)w3 ? 1 : 0);
                        for (int k = 0; k < m; k++)
                            EmitChip(Sprites.BallGround + Vector3.up * 0.05f, -bv / bs * Rand(0.5f, 2.2f) + new Vector3(Rand(-1f, 1f), Rand(1.5f, 3.4f), Rand(-1f, 1f)), Rand(0.2f, 0.36f), WaterCol, 9.8f, 0.3f);
                    }
                }
            }
            else for (int i = 0; i < feetSet.Length; i++) feetSet[i] = false;
            // the flash of lightning: the original turns its whole picture white and the figures black
            bool dark = true;
            if (weather == 1)
            {
                var pal = Host.Nes.Ppu.Palette;
                for (int k = 17; k < 32 && dark; k++)
                {
                    if ((k & 3) == 0) continue;
                    uint v = Ppu.Rgb[pal[k] & 0x3F];
                    if (((v >> 16) & 255) + ((v >> 8) & 255) + (v & 255) > 90) dark = false;
                }
            }
            else dark = false;
            // (a blinding first instant, then an eerie brightness for as long as the original keeps its screen white)
            // (the original flickers that white screen on and off: here it is a steady glare, bright enough to cut the black
            //  figures out of the dark - the blinding instant belongs to the stroke itself, see Strike)
            if (dark) { flashAge += dt; flash = Mathf.Max(flash, 0.24f * (0.92f + 0.08f * Mathf.Sin(clock * 75f))); }
            else { flashAge = 0f; flash *= Mathf.Exp(-dt * 16f); }
            if (flash < 0.01f) flash = 0f;
            strikeFlash *= Mathf.Exp(-dt / 0.07f); if (strikeFlash < 0.01f) strikeFlash = 0f;
            // sheet lightning far off in the clouds, every few seconds of a thunderstorm
            if (weather == 1) { nextSheet -= dt; if (nextSheet <= 0f) { sheet = 0f; nextSheet = Rand(2.2f, 5.5f); } }
            if (boltLife > 0f && boltLife - dt <= 0f) { boltEnd = clock; boltEchoSeed = rng.Next(); }
            boltLife -= dt;
        }

        // how the rain falls: straight down, or driven by the wind when the original draws its drops slanted
        Vector3 Fall() { return slanted ? new Vector3(-7f, -16f, -1.5f) : new Vector3(-0.8f, -17f, 0f); }

        // ------------------------------------------------------------------ drawing

        void Draw(ref Particle p, int index)
        {
            float t = p.Age / p.Life, size = Mathf.Lerp(p.Size0, p.Size1, t);
            if (p.Shape >= PixFlame)
            {
                // pixel art: whole pixels, flat tones, changing in steps and gone at the end without fading
                float st = Mathf.Floor(t * 6f) / 6f, step = Mathf.Floor(clock * StepRate);
                bool odd = (((int)step + index) & 1) != 0;
                size = Mathf.Max(Px * 0.5f, Mathf.Round(Mathf.Lerp(p.Size0, p.Size1, st) / (Px * 0.5f)) * (Px * 0.5f));
                float par;
                if (p.Shape == PixBlock) { if (p.Twinkle > 0f && odd) return; par = t < 0.3f ? 3f : t < 0.65f ? 2f : 1f; }
                else if (p.Shape == PixPlain) par = p.Twinkle > 0f ? (odd ? 3f : 1f) : 2f;
                else if (p.Shape == PixRing || p.Shape == PixRingPlain) par = Mathf.Max(0.10f, 0.30f * (1f - st));
                else par = st;
                var px = new Vector4(Mathf.Max(1f, Mathf.Round(size / Px)), step, par, p.Rot);
                if (p.Stretch > 0f) Line(p.Pos, p.Pos - p.Vel * p.Stretch, size, p.Col, p.Light, p.Shape, px);
                else if (p.Flat) FlatQuad(p.Pos, size, size, p.Col, p.Shape, p.Light, false, px);
                else Billboard(p.Pos, size, size, 0f, p.Col, p.Shape, p.Light, px);
                return;
            }
            Color c = p.Col; c.a *= p.Shape == Ring ? (1f - t) : (1f - t * t);
            if (p.Twinkle > 0f) c.a *= 0.55f + 0.45f * Mathf.Sin(p.Age * p.Twinkle + index * 1.7f);
            if (p.Stretch > 0f)
            {
                Vector3 tail = p.Vel * p.Stretch;
                if (tail.sqrMagnitude < size * size * 4f) tail = (tail.sqrMagnitude > 1e-6f ? tail.normalized : Vector3.up) * size * 2f;
                Line(p.Pos + tail * 0.3f, p.Pos - tail, size, c, p.Light);
            }
            else if (p.Flat) FlatQuad(p.Pos, size, size, c, p.Shape, p.Light);
            else
            {
                float h = p.Tumble > 0f ? size * (0.2f + 0.8f * Mathf.Abs(Mathf.Cos(p.Age * p.Tumble + index))) : size;
                Billboard(p.Pos, size, h, p.Rot + p.Spin * p.Age, c, p.Shape, p.Light);
            }
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, int shape, float light, float u0 = -1f, float u1 = 1f, Vector4 px = default(Vector4))
        {
            uv2.Add(px); uv2.Add(px); uv2.Add(px); uv2.Add(px);
            int v = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            cols.Add(ca); cols.Add(cb); cols.Add(cb); cols.Add(ca);
            uv0.Add(new Vector2(u0, -1f)); uv0.Add(new Vector2(u1, -1f)); uv0.Add(new Vector2(u1, 1f)); uv0.Add(new Vector2(u0, 1f));
            var k = new Vector2(shape, light); uv1.Add(k); uv1.Add(k); uv1.Add(k); uv1.Add(k);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 1); tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }

        void Billboard(Vector3 centre, float halfW, float halfH, float rot, Color col, int shape, float light, Vector4 px = default(Vector4))
        {
            Vector3 x = camRight, y = camUp;
            if (rot != 0f) { float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot); x = camRight * cs + camUp * sn; y = camUp * cs - camRight * sn; }
            x *= halfW; y *= halfH;
            Quad(centre - x - y, centre + x - y, centre + x + y, centre - x + y, col, col, shape, light, -1f, 1f, px);
        }

        // lies on the turf; 'facing' turns its long side across the camera's view (a figure's contact shadow)
        void FlatQuad(Vector3 centre, float halfX, float halfZ, Color col, int shape, float light, bool facing = false, Vector4 px = default(Vector4))
        {
            Vector3 x = new Vector3(halfX, 0f, 0f), z = new Vector3(0f, 0f, halfZ);
            if (facing)
            {
                Vector3 ax = new Vector3(camRight.x, 0f, camRight.z);
                if (ax.sqrMagnitude > 1e-4f) { ax.Normalize(); x = ax * halfX; z = new Vector3(-ax.z, 0f, ax.x) * halfZ; }
            }
            Quad(centre - x - z, centre + x - z, centre + x + z, centre - x + z, col, col, shape, light, -1f, 1f, px);
        }

        // a piece of ribbon from a to b that turns its face to the camera (soft across, colours blend along)
        void Segment(Vector3 a, Vector3 b, float halfA, float halfB, Color ca, Color cb, float light)
        {
            Vector3 dir = b - a; if (dir.sqrMagnitude < 1e-8f) return;
            Vector3 side = Vector3.Cross(dir, camPos - (a + b) * 0.5f).normalized;
            Quad(a - side * halfA, b - side * halfB, b + side * halfB, a + side * halfA, ca, cb, Streak, light, 0f, 0f);
        }

        // the same lying on the turf (always light)
        void FlatSegment(Vector3 a, Vector3 b, float half, Color ca, Color cb)
        {
            Vector3 dir = b - a; dir.y = 0f; if (dir.sqrMagnitude < 1e-8f) return;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x).normalized * half;
            Quad(a - side, b - side, b + side, a + side, ca, cb, Streak, 1f, 0f, 0f);
        }

        // a stroke from a to b: soft and fading out towards both ends, or (pixel shapes) hard
        void Line(Vector3 a, Vector3 b, float half, Color col, float light, int shape = Streak, Vector4 px = default(Vector4))
        {
            Vector3 dir = b - a;
            Vector3 side = Vector3.Cross(dir, camPos - a).normalized * half;
            Quad(a - side, b - side, b + side, a + side, col, col, shape, light, -1f, 1f, px);
        }

        void Bolt(int seed, float amount)
        {
            // A jagged line from the clouds down to the spot, drawn in big pixels on a grid that faces the camera:
            // a blue rim, a white-hot core, forks to both sides.  A new shape every few frames makes it flicker.
            // Round it the glow of the channel and a pool of light on the turf: those are light, and soft - and they
            // sit behind the bolt, away from the camera, so that the figures at the spot stay in front of them.
            var r = new System.Random(seed);
            Vector3 top = boltAt + new Vector3((float)(r.NextDouble() - 0.5) * 16f, 62f, (float)(r.NextDouble() - 0.5) * 16f);
            float g = Px * 7f;
            Vector3 ax = new Vector3(camRight.x, 0f, camRight.z); ax = ax.sqrMagnitude > 1e-4f ? ax.normalized : Vector3.right;
            Color blue = new Color(0.45f, 0.62f, 1f, 1f) * amount; blue.a = 1f;
            Color haze = new Color(0.30f, 0.50f, 1f, 1f);
            Vector3 away = boltAt - camPos; away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized * 0.9f : Vector3.zero;
            FlatQuad(boltAt + Vector3.up * 0.03f, 7f, 7f, A(haze * 1.6f, 0.26f * amount), Disc, 1f);
            Segment(boltAt + away, top + away, 2.4f, 4.5f, A(haze * 1.5f, 0.20f * amount), A(haze * 1.5f, 0.14f * amount), 1f);
            Segment(boltAt + away, top + away, 0.7f, 1.1f, A(Color.white * 1.8f, 0.30f * amount), A(Color.white * 1.8f, 0.22f * amount), 1f);
            Billboard(boltAt + away + Vector3.up * 1.0f, 3.6f, 3.6f, 0f, A(haze * 2.0f, 0.34f * amount), Disc, 1f);
            var one = new Vector4(1f, 0f, 1f, 0f);
            boltCells.Clear();
            const int N = 16;
            Vector3 prev = boltAt;
            for (int i = 1; i <= N; i++)
            {
                float t = i / (float)N;
                Vector3 p = Vector3.Lerp(boltAt, top, t);
                float j = 2.8f * Mathf.Sin(t * 3.1416f) + 0.4f;
                p += new Vector3((float)(r.NextDouble() - 0.5) * j * 2f, 0f, (float)(r.NextDouble() - 0.5) * j * 2f);
                Cells(prev, p, ax, g, 1f);
                if (i == 3 || i == 5 || i == 7 || i == 10 || i == 12)
                {
                    // a fork, and (mostly) a fork of the fork
                    Vector3 q = p + new Vector3((float)(r.NextDouble() - 0.5) * 12f, -(float)r.NextDouble() * 8f - 2.5f, (float)(r.NextDouble() - 0.5) * 12f);
                    Cells(p, q, ax, g, 0f);
                    if (i != 7) Cells(q, q + new Vector3((float)(r.NextDouble() - 0.5) * 6f, -(float)r.NextDouble() * 4f - 1.5f, (float)(r.NextDouble() - 0.5) * 6f), ax, g, 0f);
                }
                prev = p;
            }
            Vector3 x = ax * (g * 0.5f), y = Vector3.up * (g * 0.5f);
            foreach (var c in boltCells)        // the rim first, then the core on top
            {
                Vector3 at = boltAt + ax * (c.x * g) + Vector3.up * (c.y * g);
                float w = c.z > 0.5f ? 3.0f : 1.6f;
                Quad(at - x * w - y, at + x * w - y, at + x * w + y, at - x * w + y, blue, blue, PixBlock, 0f, -1f, 1f, one);
            }
            one.z = 3f;
            foreach (var c in boltCells)
            {
                Vector3 at = boltAt + ax * (c.x * g) + Vector3.up * (c.y * g);
                float w = c.z > 0.5f ? 1.4f : 0.7f;
                Quad(at - x * w - y, at + x * w - y, at + x * w + y, at - x * w + y, blue, blue, PixBlock, 0f, -1f, 1f, one);
            }
        }

        readonly List<Vector3> boltCells = new List<Vector3>(512);      // column, row, thick?

        // the grid cells a stroke from a to b passes through (one per row, so that it reads as a stepped line)
        void Cells(Vector3 a, Vector3 b, Vector3 ax, float g, float thick)
        {
            int r0 = Mathf.RoundToInt(a.y / g), r1 = Mathf.RoundToInt(b.y / g);
            int n = Mathf.Abs(r1 - r0);
            for (int k = 0; k <= n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, n == 0 ? 0f : k / (float)n);
                boltCells.Add(new Vector3(Mathf.Round(Vector3.Dot(p - boltAt, ax) / g), Mathf.Round(p.y / g), thick));
            }
        }
    }
}
