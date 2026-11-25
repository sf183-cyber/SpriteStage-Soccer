// The start screen and the Esc menu.  Drawn by the HUD camera with 3D text and see-through boxes (not IMGUI), so the
// off-screen self-test pictures show them too.
//   Start screen (on every start, over the 3D court turning slowly):
//     直接开始比赛（3D）  - runs the original title and menus by itself (Start once a second, eight times as fast, sound off)
//                          until the first match is on, then hands over with the camera in the original's view;
//     从原版标题画面开始  - the original menus as they are (modes, two players, passwords);
//     按键设置 / 退出.
//   Esc during the game: continue / original picture or 3D scene / White House or Tokyo Tower / key settings / quit
//   (Esc again: continue).
//   After the start, one line of the main keys for a few seconds (H shows all of them).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Soccer
{
    public sealed class FrontMenu : MonoBehaviour
    {
        // ---- this game
        const string Sub = "研究项目 SpriteStage 的足球案例　·　非官方，免费";
        static string HintText()
        {
            return Arrows() + " 移动　　" + Two(GameHost.KB) + " B 键　　" + Two(GameHost.KA) + " A 键　　" + One(GameHost.KSpecial) + " 一键必杀　　"
                 + One(GameHost.KStart) + " 暂停　　Esc 菜单　　" + One(GameHost.KView) + " 原版画面 / 3D　　" + One(GameHost.KStage) + " 换场景　　H 按键说明";
        }

        /// <summary>Every key and what it does (the help page: H, or 按键说明 in the menus).  null = a heading.</summary>
        static List<string[]> HelpRows()
        {
            var l = new List<string[]>();
            Action<string, string> R2 = (k, w) => l.Add(new[] { k, w });
            Action<string> R1 = h => l.Add(new[] { h });
            R1("比赛");
            R2(Moves(), "移动");
            R2(Two(GameHost.KB), "手柄 B 键：射门");
            R2(Two(GameHost.KA), "手柄 A 键：传球");
            R2(One(GameHost.KB) + " + " + One(GameHost.KA), "同时按：跳起；带着球时把球挑起来");
            R2("跳起后按 " + One(GameHost.KB), "空中射门（容易踢出必杀）");
            R2(Two(GameHost.KSpecial), "一键必杀：自己的球员拿着球时按");
            R2(Two(GameHost.KStart), "开始键：开始游戏，比赛中暂停");
            R2(Two(GameHost.KSelect), "选择键（原版的 Select）");
            R1("玩法（原版没有）");
            R2("N", "观战：两队都交给电脑（再按恢复）");
            R2("B", "必杀全开：每一脚射门都是必杀");
            R2("Y", "必杀展示：24 种必杀轮流放（再按停止）");
            R2("L", "必杀定格：放必杀的一瞬间世界停住");
            R1("菜单和画面");
            R2("Esc", "菜单：继续、换画面、换场景、按键设置、退出");
            R2("F1", "改按键（键盘或手柄）");
            R2(Two(GameHost.KView), "比赛中切换 3D 场景 / 原版画面");
            R2(Two(GameHost.KStage), "换场景：白宫南草坪 ⇄ 东京塔夜场");
            R2("H", "打开 / 关闭这一页");
            R1("镜头");
            R2("鼠标拖动", "转动镜头（左键或右键）");
            R2("滚轮", "拉近 / 拉远");
            R2("T 或 =  /  G 或 -", "镜头拉近 / 拉远");
            R2("Q / E", "镜头向左 / 向右转");
            R2("R / F", "镜头抬高 / 压低");
            R2("1 – 6", "六个预设机位（1 是原版的侧面视角）");
            R2("C", "镜头自动环绕（再按停止）");
            R2("Tab", "方向键跟着镜头转 / 固定为原版方向");
            R1("开关");
            R2("O", "特效：足球发光拖尾、尘土、雨、落叶、闪电");
            R2("V", "场边观众和啦啦队");
            R2("Z", "足球样式：像素立体 / 原版 / 立体");
            R2("X", "动作插值（关掉是原版的一顿一顿）");
            R2("P", "画面后期：柔光、暗角");
            R2("M", "静音");
            return l;
        }

        enum State { None, Start, Entering, Pause, Help }
        struct Item { public string Label, Note; public Action Do; }

        public Director D;
        public KeysMenu KeyScreen;
        State state = State.None;
        readonly List<Item> items = new List<Item>();
        readonly List<Rect> itemRects = new List<Rect>();
        int sel, enterFrom;
        bool pausedBefore, mutedBefore, helpPaused;
        State helpFrom;
        float hintUntil = -1f, repeat;
        Vector3 lastMouse;

        Transform root;
        Font font;
        Mesh boxes;
        readonly List<Vector3> bv = new List<Vector3>();
        readonly List<Color> bc = new List<Color>();
        readonly List<int> bt = new List<int>();
        readonly List<TextMesh> texts = new List<TextMesh>();
        int used;
        float sw, sh;

        /// <summary>The 3D court is shown (and the game's own picture hidden): the start screen and the run through the menus.</summary>
        public bool HidesPicture { get { return state == State.Start || state == State.Entering || state == State.Help; } }   // (the help page: the 3D scene behind it, not the game's picture)
        public bool IsOpen { get { return state != State.None; } }
        /// <summary>Running through the original menus on its own.</summary>
        public bool Busy { get { return state == State.Entering; } }

        public void Init(Director d, KeysMenu keys, bool showStart)
        {
            D = d; KeyScreen = keys;
            root = new GameObject("Menu").transform; root.SetParent(d.Hud.transform, false); root.gameObject.layer = Hud.Layer;
            font = UiFont.Get();
            if (font != null) font.material.renderQueue = 4002;                    // over the boxes (Overlay+1)
            var go = new GameObject("Boxes"); go.layer = Hud.Layer; go.transform.SetParent(root, false); go.transform.localPosition = new Vector3(0, 0, -1);
            boxes = new Mesh(); boxes.name = "Menu boxes"; boxes.MarkDynamic(); boxes.hideFlags = HideFlags.DontSave;
            go.AddComponent<MeshFilter>().sharedMesh = boxes;
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(Resources.Load<Shader>("Shaders/G3Panel")); mat.hideFlags = HideFlags.DontSave;
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (showStart) OpenStart();
        }

        // ------------------------------------------------------------------ states
        void OpenStart()
        {
            state = State.Start; sel = 0;
            D.Host.Paused = true;
            D.Orbit.AutoOrbit = true;
            items.Clear();
            items.Add(new Item { Label = "直接开始比赛（3D）", Note = "自动跳过原版的标题和菜单，直接进入第一场比赛", Do = DirectStart });
            items.Add(new Item { Label = "从原版标题画面开始", Note = "原版的标题和菜单，自己选模式和设置；比赛开始后同样是 3D", Do = OriginalTitle });
            items.Add(new Item { Label = "按键说明", Note = "每个键是做什么的（游戏中按 H 也可以）", Do = ShowHelp });
            items.Add(new Item { Label = "按键设置", Note = "改键盘或手柄的按键（游戏中按 F1 也可以）", Do = OpenKeys });
            items.Add(new Item { Label = "退出", Note = "", Do = Application.Quit });
        }

        public void OpenPause()
        {
            if (state != State.None) return;
            state = State.Pause; sel = 0;
            pausedBefore = D.Host.Paused; D.Host.Paused = true;
            items.Clear();
            items.Add(new Item { Label = "继续游戏", Note = "", Do = CloseMenu });
            items.Add(D.Host.Flat
                ? new Item { Label = "换回 3D 场景", Note = "比赛画面回到 3D（" + One(GameHost.KView) + "）", Do = ToggleView }
                : new Item { Label = "换成原版画面", Note = "比赛时显示原版的平面画面（" + One(GameHost.KView) + "）", Do = ToggleView });
            items.Add(Field.IsTokyo
                ? new Item { Label = "换回白宫南草坪", Note = "换场景（" + One(GameHost.KStage) + "），比赛照常进行", Do = ToggleStage }
                : new Item { Label = "换成东京塔夜场", Note = "换场景（" + One(GameHost.KStage) + "），比赛照常进行", Do = ToggleStage });
            items.Add(new Item { Label = "按键说明", Note = "每个键是做什么的（H）", Do = ShowHelp });
            items.Add(new Item { Label = "按键设置", Note = "改键盘或手柄的按键（F1）", Do = OpenKeys });
            items.Add(new Item { Label = "退出游戏", Note = "", Do = Application.Quit });
        }

        /// <summary>Closes the Esc menu (the game goes on as it was).</summary>
        public void CloseMenu()
        {
            if (state != State.Pause) return;
            state = State.None; D.Host.Paused = pausedBefore; D.Host.PadQuietUntil = Time.unscaledTime + 0.3f;
        }

        /// <summary>The page of all the keys (from the menus, or H during the game).</summary>
        public void ShowHelp()
        {
            if (state == State.Help || state == State.Entering) return;
            helpFrom = state;
            if (state == State.None) { helpPaused = D.Host.Paused; D.Host.Paused = true; }
            state = State.Help;
        }

        public void HideHelp()
        {
            if (state != State.Help) return;
            state = helpFrom;
            if (helpFrom == State.None) { D.Host.Paused = helpPaused; D.Host.PadQuietUntil = Time.unscaledTime + 0.3f; }
        }

        void ToggleView() { D.Host.SetFlat(!D.Host.Flat); CloseMenu(); }

        void ToggleStage() { D.SwitchStage(Field.IsTokyo ? "" : "tokyo"); if (D.Host.Flat) D.Host.SetFlat(false); CloseMenu(); }

        void OpenKeys() { if (KeyScreen != null) KeyScreen.OpenNow(); }

        void OriginalTitle()
        {
            state = State.None; D.Host.Paused = false; D.Host.PadQuietUntil = Time.unscaledTime + 0.3f;
            D.Orbit.Preset(1);
            hintUntil = Time.unscaledTime + 15f;
        }

        void DirectStart()
        {
            state = State.Entering; enterFrom = D.Host.FrameNo;
            if (D.Host.Flat) D.Host.SetFlat(false);
            if (D.Host.Audio != null) { mutedBefore = D.Host.Audio.Mute; D.Host.Audio.Mute = true; }
            D.Host.Script = AutoPad; D.Host.Speed = 8f; D.Host.Paused = false;
            Debug.Log("[menu] direct start");
        }

        /// <summary>Start once a second from the first frames on: through the opening, the title and the line-up.</summary>
        static byte AutoPad(int f) { return (byte)(f > 100 && (f + 17) % 60 < 4 ? NesCore.Pad.Start : 0); }

        void FinishEntering()
        {
            var h = D.Host;
            h.Script = null; h.Speed = 1f;
            if (h.Audio != null) h.Audio.Mute = mutedBefore;
            state = State.None; h.PadQuietUntil = Time.unscaledTime + 0.3f;
            D.Orbit.Preset(1);
            hintUntil = Time.unscaledTime + 15f;
            Debug.Log("[menu] match reached at frame " + h.FrameNo + (h.Bridge.InMatch ? "" : " (gave up waiting)"));
        }

        /// <summary>For the self-test: does what picking item i does.</summary>
        public void Choose(int i) { if (i >= 0 && i < items.Count && items[i].Do != null) items[i].Do(); }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (D == null || D.Host == null || D.Host.Nes == null) return;
            if (state == State.Entering)
            {
                if (D.Host.Bridge.InMatch || D.Host.FrameNo - enterFrom > 6000) FinishEntering();
                return;
            }
            if (KeysMenu.IsOpen) return;
            if (state == State.None)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) OpenPause();
                else if (Keys.Down(KeyCode.H)) ShowHelp();
                return;
            }
            if (state == State.Help)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Keys.Down(KeyCode.H) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                    || Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetMouseButtonDown(0)) HideHelp();
                return;
            }
            if (Keys.Down(KeyCode.H)) { ShowHelp(); return; }
            if (state == State.Pause && Input.GetKeyDown(KeyCode.Escape)) { CloseMenu(); return; }
            var c = Controls.Current;
            bool up = Input.GetKeyDown(KeyCode.UpArrow) || (c != null && c.Pressed(GameHost.KUp));
            bool down = Input.GetKeyDown(KeyCode.DownArrow) || (c != null && c.Pressed(GameHost.KDown));
            // (the keyboard's arrows and W/S drive the "Vertical" axis too: it is only read for a pad's stick or cross)
            float v = Input.GetAxisRaw("Vertical");
            bool kb = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S);
            repeat -= Time.unscaledDeltaTime;
            if (!kb && v > 0.5f && repeat <= 0) { up = true; repeat = 0.25f; }
            if (!kb && v < -0.5f && repeat <= 0) { down = true; repeat = 0.25f; }
            if (Mathf.Abs(v) < 0.3f) repeat = 0;
            if (up) sel = (sel + items.Count - 1) % items.Count;
            if (down) sel = (sel + 1) % items.Count;
            bool ok = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0)
                   || Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.JoystickButton7)
                   || (c != null && (c.Pressed(GameHost.KStart) || c.Pressed(GameHost.KA) || c.Pressed(GameHost.KB)));
            // the mouse: pointing picks, a click does it
            Vector3 m = Input.mousePosition;
            int hit = -1;
            for (int i = 0; i < itemRects.Count; i++) if (itemRects[i].Contains(new Vector2(m.x, Screen.height - m.y))) hit = i;
            if (m != lastMouse && hit >= 0) sel = hit;
            lastMouse = m;
            if (Input.GetMouseButtonDown(0) && hit >= 0) { sel = hit; ok = true; }
            if (ok) Choose(sel);
        }

        // ------------------------------------------------------------------ drawing
        /// <summary>Lays the menu out for a screen (or picture) of w x h pixels: called by Hud.Layout.</summary>
        public void Layout(int w, int h)
        {
            sw = w; sh = h; used = 0; bv.Clear(); bc.Clear(); bt.Clear(); itemRects.Clear();
            if (font != null)
            {
                if (state == State.Start || state == State.Pause) DrawMenu();
                else if (state == State.Entering) DrawEntering();
                else if (state == State.Help) DrawHelp();
                else if (Time.unscaledTime < hintUntil && D.Host.Error == null) DrawHint();
            }
            for (int i = used; i < texts.Count; i++) if (texts[i].gameObject.activeSelf) texts[i].gameObject.SetActive(false);
            boxes.Clear(); boxes.SetVertices(bv); boxes.SetColors(bc); boxes.SetTriangles(bt, 0);
            boxes.bounds = new Bounds(Vector3.zero, new Vector3(w * 2, h * 2, 10));
        }

        static readonly Color Orange = new Color(1f, 0.62f, 0.14f, 0.96f), Ink = new Color(0.07f, 0.06f, 0.05f), Grey = new Color(0.72f, 0.74f, 0.78f);

        void DrawMenu()
        {
            bool start = state == State.Start;
            float W = Mathf.Min(sw * 0.92f, sh * 1.02f), x0 = (sw - W) / 2f, pad = sh * 0.035f;
            float bh = sh * (items.Count > 4 ? 0.088f : 0.098f), gap = sh * 0.014f;
            float top = start ? sh * 0.09f : (items.Count > 5 ? sh * 0.10f : sh * 0.16f);     // (six items: up a little, clear of the status bar)
            float titleSize = start ? sh * 0.078f : sh * 0.055f;
            float y = top + pad;
            float itemsTop = y + titleSize * 1.1f + (start ? sh * 0.085f : sh * 0.02f);
            float bottom = itemsTop + items.Count * (bh + gap) + sh * 0.045f;
            Box(x0, top, W, bottom - top + pad * 0.5f, new Color(0.02f, 0.03f, 0.05f, 0.76f));
            Box(x0, top, W, sh * 0.006f, Orange);
            Text(start ? RomFile.Program : "暂停", sw / 2f, y + titleSize * 0.55f, titleSize, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (start) Text(Sub, sw / 2f, y + titleSize * 1.5f, sh * 0.027f, Grey, TextAnchor.MiddleCenter);
            float yi = itemsTop;
            for (int i = 0; i < items.Count; i++)
            {
                bool on = i == sel;
                var r = new Rect(x0 + pad, yi, W - pad * 2, bh);
                itemRects.Add(r);
                Box(r.x, r.y, r.width, r.height, on ? Orange : new Color(1f, 1f, 1f, 0.07f));
                bool note = items[i].Note.Length > 0;
                float lx = r.x + bh * 0.32f;
                Text((on ? "▶  " : "") + items[i].Label, lx, r.y + (note ? bh * 0.36f : bh * 0.5f), sh * (i == 0 && start ? 0.040f : 0.034f), on ? Ink : Color.white, TextAnchor.MiddleLeft, i == 0 && start ? FontStyle.Bold : FontStyle.Normal);
                if (note) Text(items[i].Note, lx + (on ? sh * 0.05f : 0f), r.y + bh * 0.74f, sh * 0.025f, on ? new Color(0.22f, 0.17f, 0.1f) : Grey, TextAnchor.MiddleLeft);
                yi += bh + gap;
            }
            Text("↑↓ 选择　　回车 / " + One(GameHost.KB) + " / " + One(GameHost.KA) + " 确定　　鼠标也可以点" + (start ? "" : "　　Esc 继续"), sw / 2f, yi + sh * 0.014f, sh * 0.024f, Grey, TextAnchor.MiddleCenter);
        }

        void DrawHelp()
        {
            var rows = HelpRows();
            float W = Mathf.Min(sw * 0.96f, sh * 1.66f), x0 = (sw - W) / 2f, top = sh * 0.04f, H = sh * 0.92f, pad = sh * 0.035f;
            Box(x0, top, W, H, new Color(0.02f, 0.03f, 0.05f, 0.95f));       // (blended in linear light: 0.9 still lets a third of the brightness through)
            Box(x0, top, W, sh * 0.006f, Orange);
            Text("按键说明", sw / 2f, top + sh * 0.05f, sh * 0.05f, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Text("显示的是现在的按键设置（F1 可以改）", sw / 2f, top + sh * 0.098f, sh * 0.022f, Grey, TextAnchor.MiddleCenter);
            // two columns, split at a heading near the middle
            float lh = sh * 0.034f, head = 1.35f;
            float total = 0; foreach (var r in rows) total += r.Length == 1 ? head : 1f;
            int split = rows.Count; float run = 0;
            for (int i = 0; i < rows.Count; i++) { if (rows[i].Length == 1 && i > 0 && run >= total / 2f - 3f) { split = i; break; } run += rows[i].Length == 1 ? head : 1f; }
            float colW = (W - pad * 3f) / 2f, keyW = colW * 0.31f, y0 = top + sh * 0.135f;
            for (int c = 0; c < 2; c++)
            {
                float cx = x0 + pad + c * (colW + pad), y = y0;
                for (int i = c == 0 ? 0 : split; i < (c == 0 ? split : rows.Count); i++)
                {
                    var r = rows[i];
                    if (r.Length == 1)
                    {
                        if (y > y0) y += lh * 0.25f;
                        Text(r[0], cx, y + lh * 0.5f, sh * 0.026f, new Color(1f, 0.68f, 0.25f), TextAnchor.MiddleLeft, FontStyle.Bold);
                        y += lh * (head - 0.25f) + (y > y0 ? 0 : lh * 0.25f);
                        continue;
                    }
                    Text(r[0], cx + sh * 0.01f, y + lh * 0.5f, sh * 0.023f, new Color(1f, 0.88f, 0.62f), TextAnchor.MiddleLeft);
                    Text(r[1], cx + keyW, y + lh * 0.5f, sh * 0.023f, Color.white, TextAnchor.MiddleLeft);
                    y += lh;
                }
            }
            Text("Esc / H / 回车  返回", sw / 2f, top + H - sh * 0.032f, sh * 0.022f, Grey, TextAnchor.MiddleCenter);
        }

        void DrawEntering()
        {
            float W = sh * 0.62f, H = sh * 0.12f;
            Box((sw - W) / 2f, sh * 0.44f, W, H, new Color(0.02f, 0.03f, 0.05f, 0.76f));
            Box((sw - W) / 2f, sh * 0.44f, W, sh * 0.005f, Orange);
            int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
            Text("正在进入比赛" + new string('.', dots), sw / 2f, sh * 0.44f + H * 0.52f, sh * 0.036f, Color.white, TextAnchor.MiddleCenter);
        }

        void DrawHint()
        {
            float hh = sh * 0.055f;
            Box(0, 0, sw, hh, new Color(0f, 0f, 0f, 0.5f));
            // smaller letters when the line would not fit (a narrow window): Chinese letters about 1 em wide, the rest about 0.55
            string hint = HintText(); float ems = 0f;
            foreach (char ch in hint) ems += ch >= 0x2E80 ? 1f : 0.55f;
            float px = Mathf.Min(sh * 0.028f, (sw - sh * 0.044f) / Mathf.Max(ems, 1f));
            Text(hint, sh * 0.022f, hh * 0.52f, px, Color.white, TextAnchor.MiddleLeft);
        }

        /// <summary>A box in screen pixels (origin top-left).</summary>
        void Box(float x, float y, float w, float h, Color c)
        {
            int i = bv.Count;
            float X0 = x - sw / 2f, X1 = x + w - sw / 2f, Y0 = sh / 2f - y, Y1 = sh / 2f - y - h;
            bv.Add(new Vector3(X0, Y1, 0)); bv.Add(new Vector3(X0, Y0, 0)); bv.Add(new Vector3(X1, Y0, 0)); bv.Add(new Vector3(X1, Y1, 0));
            bc.Add(c); bc.Add(c); bc.Add(c); bc.Add(c);
            bt.Add(i); bt.Add(i + 1); bt.Add(i + 2); bt.Add(i); bt.Add(i + 2); bt.Add(i + 3);
        }

        /// <summary>A line of text, 'px' pixels high, at a point in screen pixels (origin top-left).</summary>
        void Text(string s, float x, float y, float px, Color c, TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            TextMesh t;
            if (used < texts.Count) t = texts[used];
            else
            {
                var go = new GameObject("Text"); go.layer = Hud.Layer; go.transform.SetParent(root, false);
                t = go.AddComponent<TextMesh>(); t.font = font;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = font.material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                texts.Add(t);
            }
            used++;
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            int size = Mathf.Clamp(Mathf.RoundToInt(px), 8, 200);
            if (t.text != s) t.text = s;
            t.fontSize = size; t.characterSize = 10f * px / size; t.anchor = anchor; t.alignment = TextAlignment.Left;
            t.color = c; t.fontStyle = style;
            t.transform.localPosition = new Vector3(x - sw / 2f, sh / 2f - y, -2f);
        }

        // ------------------------------------------------------------------ key names
        static string One(Controls.Binding b) { foreach (var k in b.Keys) if (k < KeyCode.JoystickButton0) return Controls.Pretty(k); return b.Keys.Count > 0 ? Controls.Pretty(b.Keys[0]) : "（无）"; }
        static string Two(Controls.Binding b)
        {
            var l = new List<string>();
            foreach (var k in b.Keys) if (k < KeyCode.JoystickButton0 && l.Count < 2) l.Add(Controls.Pretty(k));
            return l.Count > 0 ? string.Join("/", l.ToArray()) : One(b);
        }
        static string Arrows() { return One(GameHost.KUp) + One(GameHost.KDown) + One(GameHost.KLeft) + One(GameHost.KRight); }
        static string Second(Controls.Binding b) { int n = 0; foreach (var k in b.Keys) if (k < KeyCode.JoystickButton0) { if (n == 1) return Controls.Pretty(k); n++; } return null; }
        static string Moves()
        {
            string u = Second(GameHost.KUp), d = Second(GameHost.KDown), le = Second(GameHost.KLeft), ri = Second(GameHost.KRight);
            return Arrows() + (u != null && d != null && le != null && ri != null ? " / " + u + d + le + ri : "");
        }
    }
}
