// 2D layer: the original status bar under the 3D view, and the untouched game picture for menus and cut-scenes.
// Drawn by a small orthographic camera of its own (so the headless test renders go through exactly the same path),
// plus the key help as on-screen text.
using UnityEngine;

namespace Soccer
{
    public sealed class Hud : MonoBehaviour
    {
        public const int Layer = 31;
        public const int CropLeft = 8;      // the game blanks the left 8 pixels (they would show the field's backdrop colour)
        public const int CropTop = 2;       // the scanline on which the game switches from field to status bar
        public GameHost Host;
        public OrbitCamera Orbit;
        public Camera HudCam;
        public bool ShowHelp;               // all the keys (H); the start shows one line of the main keys instead (FrontMenu)
        public FrontMenu Front;
        float helpUntil = 14f;
        Mesh quad;
        readonly Vector3[] v = new Vector3[4];
        readonly Vector2[] uv = new Vector2[4];
        GUIStyle style, shadowStyle;

        // the first line follows the player's keys (Controls); F1 changes them
        static string Pad()
        {
            var c = Controls.Current; if (c == null) return "";
            return Controls.Pretty(GameHost.KUp.Keys) + " " + Controls.Pretty(GameHost.KDown.Keys) + " " + Controls.Pretty(GameHost.KLeft.Keys) + " " + Controls.Pretty(GameHost.KRight.Keys) + "  移动      "
                 + c.Pretty(GameHost.KB) + "  手柄 B 键      " + c.Pretty(GameHost.KA) + "  手柄 A 键      " + c.Pretty(GameHost.KStart) + "  Start（开始·暂停）      "
                 + c.Pretty(GameHost.KSelect) + "  Select      F1  改按键      " + c.Pretty(GameHost.KView) + "  3D 场景开关（比赛时换成原版画面 / 换回来）      "
                 + c.Pretty(GameHost.KStage) + "  换场景（白宫 ⇄ 东京塔夜场）\n";
        }
        static string Help { get { return Pad() + HelpRest; } }
        const string HelpRest =
            "鼠标拖动  旋转镜头      滚轮  缩放      Q / E  左右转      R / F  俯仰      1–6  预设机位      C  自动环绕\n" +
            "Tab  方向键跟随镜头 / 固定      X  动作插值开关      M  静音      P  摄影后期开关      O  特效开关      V  场边观众开关      H  显示 / 隐藏本说明      Esc  退出\n" +
            "N  观战（两队都交给电脑）      B  必杀全开（每一脚射门都是必杀）      " + "一键必杀键（自己的球员拿球时按，默认空格）      Z  足球样式（像素立体 / 原版 / 立体）\n" +
            "Y  必杀展示（全部 24 种必杀轮流放一遍，再按一次停止）      L  必杀定格开关（放必杀的一瞬间世界停住、全场变暗）";

        public void Init(GameHost host)
        {
            Host = host;
            var camGo = new GameObject("Hud camera"); camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -10);
            HudCam = camGo.AddComponent<Camera>();
            HudCam.orthographic = true; HudCam.nearClipPlane = 1; HudCam.farClipPlane = 20; HudCam.depth = 10;
            HudCam.cullingMask = 1 << Layer; HudCam.allowMSAA = false; HudCam.allowHDR = false; HudCam.backgroundColor = Color.black;
            var go = new GameObject("Game picture"); go.transform.SetParent(transform, false); go.layer = Layer;
            quad = new Mesh(); quad.name = "Game picture"; quad.MarkDynamic(); quad.hideFlags = HideFlags.DontSave;
            quad.vertices = v; quad.uv = uv; quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(Resources.Load<Shader>("Shaders/G3Overlay")); mat.hideFlags = HideFlags.DontSave;
            if (host.FrameTexture != null) mat.mainTexture = host.FrameTexture;
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        void Update()
        {
            if (Front == null && Keys.Down(KeyCode.H)) { ShowHelp = !ShowHelp; helpUntil = float.MaxValue; }     // (with the menu: its key page)
            if (Front == null && Keys.Down(KeyCode.Escape)) Application.Quit();     // (with the menu, Esc opens it)
        }

        /// <summary>Where the status bar goes on a screen of sw x sh pixels (y measured from the top).  'rows' = picture lines below the split.</summary>
        public static Rect HudRect(int rows, int sw, int sh, out int scale)
        {
            scale = Mathf.Max(2, Mathf.RoundToInt(sh * 0.215f / 56f));
            while (scale > 2 && (256 - CropLeft) * scale > sw) scale--;
            int w = (256 - CropLeft) * scale, h = (rows - CropTop) * scale;
            return new Rect((sw - w) / 2, sh - h, w, h);
        }

        /// <summary>Lays the picture out for a screen (or render target) of sw x sh pixels.</summary>
        public void Layout(int sw, int sh)
        {
            if (Host == null || Host.Nes == null) { HudCam.clearFlags = CameraClearFlags.SolidColor; quad.bounds = new Bounds(new Vector3(0, -99999, 0), Vector3.one); return; }
            if (Front != null && Front.HidesPicture)                // the start screen: the 3D court, no game picture
            {
                HudCam.clearFlags = CameraClearFlags.Nothing; HudCam.orthographicSize = sh / 2f;
                quad.bounds = new Bounds(new Vector3(0, -99999, 0), Vector3.one); Front.Layout(sw, sh); return;
            }
            bool match = Host.Bridge.InMatch && !Host.Flat;          // (flat: the whole original picture)
            HudCam.clearFlags = match ? CameraClearFlags.Nothing : CameraClearFlags.SolidColor;
            HudCam.orthographicSize = sh / 2f;
            Rect r; float u0, v0;
            if (match)
            {
                int first = Host.Nes.SplitScanline + 1, rows = 240 - first, scale;
                r = HudRect(rows, sw, sh, out scale);
                u0 = CropLeft / 256f; v0 = (first + CropTop) / 240f;
            }
            else
            {
                float s = Mathf.Min(sw / 256f, sh / 240f);
                if (s >= 2f) s = Mathf.Floor(s);
                r = new Rect(Mathf.Round((sw - 256 * s) / 2), Mathf.Round((sh - 240 * s) / 2), 256 * s, 240 * s);
                u0 = 0; v0 = 0;
            }
            // pixels (origin top-left) -> camera units (origin centre, y up); the frame texture has scanline 0 at v = 0
            float x0 = r.x - sw / 2f, x1 = r.xMax - sw / 2f, yTop = sh / 2f - r.y, yBot = sh / 2f - r.yMax;
            v[0] = new Vector3(x0, yBot, 0); v[1] = new Vector3(x0, yTop, 0); v[2] = new Vector3(x1, yTop, 0); v[3] = new Vector3(x1, yBot, 0);
            uv[0] = new Vector2(u0, 1); uv[1] = new Vector2(u0, v0); uv[2] = new Vector2(1, v0); uv[3] = new Vector2(1, 1);
            quad.vertices = v; quad.uv = uv;
            quad.bounds = new Bounds(Vector3.zero, new Vector3(sw * 2, sh * 2, 10));
            if (Front != null) Front.Layout(sw, sh);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Host == null) return;
            UiFont.Apply();
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label); style.richText = false; style.wordWrap = true;
                shadowStyle = new GUIStyle(style);
            }
            int fs = Mathf.Max(12, Screen.height / 62);
            style.fontSize = shadowStyle.fontSize = fs; style.normal.textColor = Color.white; shadowStyle.normal.textColor = new Color(0, 0, 0, 0.85f);
            if (Host.Error != null) { GUI.Label(new Rect(40, 40, Screen.width - 80, 200), Host.Error, style); return; }
            if (ShowHelp && Time.unscaledTime < helpUntil)
            {
                string t = Help;
                if (Host.Bridge.InMatch) t += "\n方向键：" + (Host.CameraRelativeControls ? "跟随镜头" : "固定（原版）") + "      插值：" + (Host.Interpolate ? "开" : "关");
                var r = new Rect(fs, fs * 0.6f, Screen.width - fs * 2, fs * 9);
                var r2 = r; r2.x += 1.5f; r2.y += 1.5f;
                GUI.Label(r2, t, shadowStyle); GUI.Label(r, t, style);
            }
            var tw = Host.Tweaks;
            if (tw != null && Host.Nes != null)
            {
                // what the N / B / space keys have switched on, top right; the latest note, in the middle near the top
                string state = (tw.Spectate ? "观战中" : "") + (tw.Spectate && tw.AllSpecials ? " · " : "") + (tw.AllSpecials ? "必杀全开" : "");
                if (tw.Showing) state = (state.Length > 0 ? state + " · " : "") + "必杀展示中";
                if (state.Length > 0)
                {
                    var r = new Rect(Screen.width * 0.5f, fs * 0.6f, Screen.width * 0.5f - fs, fs * 2);
                    var r2 = r; r2.x += 1.5f; r2.y += 1.5f;
                    style.alignment = shadowStyle.alignment = TextAnchor.UpperRight;
                    GUI.Label(r2, state, shadowStyle); GUI.Label(r, state, style);
                    style.alignment = shadowStyle.alignment = TextAnchor.UpperLeft;
                }
                if (tw.Message != null && Host.Nes.FrameCount - tw.MessageFrame < 150)
                {
                    int big = fs * 2;
                    style.fontSize = shadowStyle.fontSize = big;
                    style.alignment = shadowStyle.alignment = TextAnchor.UpperCenter;
                    var r = new Rect(0, Screen.height * 0.18f, Screen.width, big * 2);
                    var r2 = r; r2.x += 2f; r2.y += 2f;
                    GUI.Label(r2, tw.Message, shadowStyle); GUI.Label(r, tw.Message, style);
                    style.alignment = shadowStyle.alignment = TextAnchor.UpperLeft;
                    style.fontSize = shadowStyle.fontSize = fs;
                }
            }
        }
    }
}
