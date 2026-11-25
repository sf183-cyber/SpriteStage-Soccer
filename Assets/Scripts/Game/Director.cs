// Puts the pieces together and keeps their order each frame: game -> camera -> sprites -> crowd -> effects -> trees.
using UnityEngine;

namespace Soccer
{
    public sealed class Director : MonoBehaviour
    {
        public GameHost Host;
        public SpriteField Sprites;
        public OrbitCamera Orbit;
        public World World;
        public Camera Cam;
        public Hud Hud;
        public FrontMenu Front;             // the start screen and the Esc menu (not made by the tools)
        public Effects Fx;
        public Crowd People;

        public static Director Create()
        {
            Field.StageFromArgs();
            Field.Load();
            var root = new GameObject("Soccer");
            var d = root.AddComponent<Director>();
            d.Host = root.AddComponent<GameHost>();
            bool ok = d.Host.Init();
            var worldGo = new GameObject("World"); worldGo.transform.SetParent(root.transform, false);
            d.World = worldGo.AddComponent<World>();
            d.World.Build();
            var camGo = new GameObject("Camera"); camGo.transform.SetParent(root.transform, false);
            d.Cam = camGo.AddComponent<Camera>();
            d.Cam.clearFlags = CameraClearFlags.Skybox; d.Cam.allowMSAA = true; d.Cam.allowHDR = true;        // HDR: light sources may be brighter than white (they are what glows)
            d.Cam.backgroundColor = Color.black; d.Cam.tag = "MainCamera";
            d.Orbit = camGo.AddComponent<OrbitCamera>(); d.Orbit.Cam = d.Cam;
            var post = camGo.AddComponent<PostFx>();
            d.Post = post;
            d.Host.Orbit = d.Orbit;
            camGo.AddComponent<AudioListener>();
            d.Host.Audio = camGo.AddComponent<AudioOut>();
            d.Host.Audio.SampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            d.Host.Audio.Fx = d.Host.SfxLive;
            if (ok)
            {
                var sp = new GameObject("Players"); sp.transform.SetParent(root.transform, false);
                d.Sprites = sp.AddComponent<SpriteField>();
                d.Sprites.Init(d.Host, d.Cam);
                var crowd = new GameObject("Crowd"); crowd.transform.SetParent(root.transform, false);
                d.People = crowd.AddComponent<Crowd>();
                d.People.Init(d.Host, d.Cam, d.Sprites);
                var fx = new GameObject("Effects"); fx.transform.SetParent(root.transform, false);
                d.Fx = fx.AddComponent<Effects>();
                d.Fx.Init(d.Host, d.Cam, d.Sprites, post, d.Orbit);
            }
            d.Hud = root.AddComponent<Hud>(); d.Hud.Orbit = d.Orbit;
            d.Hud.Init(d.Host);
            d.Cam.cullingMask = ~(1 << Hud.Layer);
            d.ApplyStage();
            d.Frame(0f, true);
            return d;
        }

        public PostFx Post;

        /// <summary>场景里随场景变的设置：影子按太阳还是按泛光灯、夜里的暗角、镜头的预设和“别穿进白宫”。</summary>
        void ApplyStage()
        {
            var L = Field.L;
            Orbit.HousePoint = Field.P(0f, L.houseY - 5f, 7.5f);
            Orbit.HouseFront = L.houseY;
            Orbit.Tokyo = Field.IsTokyo;
            Orbit.ParkBox = Field.IsTokyo ? L.parkBox : null;
            if (Post != null) Post.Vignette = World.Night ? 0.26f : 0.16f;         // 夜里照片的四角暗得多（光圈开得大）
            if (People != null) People.Restage();
            if (Sprites != null) Sprites.SetLight(new Vector2(L.sunX, L.sunY), 1f / Mathf.Tan(World.Info.sunElevation * Mathf.Deg2Rad), World.Night ? World.Lamps : null);
        }

        /// <summary>换场景：白宫（""）⇄ 东京塔夜场（"tokyo"）。按“换场景”键（默认 0，F1 可改）、Esc 菜单，或 -stage tokyo 开局。比赛不受影响。</summary>
        public void SwitchStage(string stage)
        {
            Field.Stage = stage; Field.Load();
            var old = World;
            old.gameObject.SetActive(false);                 // Destroy 要到这一帧结束才生效：同一帧拍照时旧场景还会画出来
            if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            var worldGo = new GameObject("World"); worldGo.transform.SetParent(transform, false);
            World = worldGo.AddComponent<World>();
            World.Build();
            ApplyStage();
            if (Host.Nes != null) { Host.Tweaks.Message = Field.IsTokyo ? "场景：东京塔夜场（芝公园）" : "场景：白宫南草坪"; Host.Tweaks.MessageFrame = Host.Nes.FrameCount; }
        }

        void Update()
        {
            if (Controls.Current != null && Controls.Current.Pressed(GameHost.KStage)) SwitchStage(Field.IsTokyo ? "" : "tokyo");
        }

        /// <summary>One picture: place the camera, rebuild the sprite meshes, turn the trees.</summary>
        public void Frame(float dt, bool snap = false)
        {
            bool match = Host.Nes != null && Host.Bridge.InMatch;
            float blend = Host.Nes != null ? Host.TickBlend : 1f;
            if (snap) blend = 1f;
            Vector3 focus = Vector3.zero;
            if (Sprites != null) { Sprites.UpdateFocus(blend); if (match) focus = Sprites.Focus; }
            Orbit.Apply(focus, dt, snap);
            if (Sprites != null) Sprites.Rebuild(blend);
            if (People != null) People.Frame(Host.Freeze > 0f ? 0f : dt);        // (the world stands still for a special shot: the crowd too)
            if (Fx != null) Fx.Frame(dt);
            World.UpdateTrees(Cam.transform.position);
            World.UpdateNight(Cam, dt);
        }

        void LateUpdate()
        {
            bool match = Host.Nes != null && Host.Bridge.InMatch && !Host.Flat;
            if (Front != null && Front.HidesPicture) match = true;          // (the start screen shows the court)
            if (Cam.enabled != match) Cam.enabled = match;
            Frame(Time.unscaledDeltaTime);
            Hud.Layout(Screen.width, Screen.height);
        }
    }

    static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            QualitySettings.vSyncCount = 1; QualitySettings.antiAliasing = 4;
            Application.targetFrameRate = 120;
            if (Object.FindFirstObjectByType<Director>() != null) return;
            // the player's keys (F1 in the game, or 按键设置.txt)
            Controls.Current = GameHost.MakeControls(); Controls.Current.Load();
            var menu = new GameObject("Keys menu"); Object.DontDestroyOnLoad(menu); keysMenu = menu.AddComponent<KeysMenu>();
            // the game is not shipped: until the player's own copy is in the ROM folder, a screen says where it goes
            if (!RomFile.Find()) { var p = new GameObject("Game file").AddComponent<RomPrompt>(); p.Ready = Begin; return; }
            Begin();
        }

        static KeysMenu keysMenu;

        static void Begin()
        {
            var d = Director.Create();
            if (keysMenu != null) keysMenu.Host = d.Host;
            Debug.Log("[SpriteStage] started" + (d.Host.Error != null ? ": " + d.Host.Error : ""));
            var args = System.Environment.GetCommandLineArgs();
            // the start screen (a plain self-test goes straight on; -launchTest goes through the start screen)
            bool test = System.Array.IndexOf(args, "-selftest") >= 0, launchTest = System.Array.IndexOf(args, "-launchTest") >= 0;
            if (d.Host.Nes != null) { d.Front = d.gameObject.AddComponent<FrontMenu>(); d.Hud.Front = d.Front; d.Front.Init(d, keysMenu, !test || launchTest); }
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-selftest") { var st = d.gameObject.AddComponent<SelfTest>(); st.D = d; st.Dir = args[i + 1]; st.Spectate = System.Array.IndexOf(args, "-spectate") >= 0; }
        }
    }
}
