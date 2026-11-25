// The player's keys: which keyboard keys and pad buttons press the game controller's buttons.  They can be changed in the
// game (F1) or in the text file 按键设置.txt (next to the program, written on the first run).
//   Controls  - the bindings, loaded from / saved to the file
//   Keys      - what the rest of the program asks for its own fixed keys (camera, switches): nothing while the key menu is
//               open, and nothing for a key the player has given to one of the controller's buttons
//   KeysMenu  - the F1 screen
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Soccer
{
    public sealed class Controls
    {
        public sealed class Binding
        {
            public string Name;                 // as shown and as written in the file
            public KeyCode[] Defaults;
            public readonly List<KeyCode> Keys = new List<KeyCode>();
        }

        public const int MaxKeys = 3;
        public const string FileName = "按键设置.txt";
        public static Controls Current;
        public readonly List<Binding> All = new List<Binding>();
        public string FilePath { get; private set; }
        public string Title = "";

        public Binding Add(string name, params KeyCode[] defaults)
        {
            var b = new Binding { Name = name, Defaults = defaults }; b.Keys.AddRange(defaults); All.Add(b); return b;
        }

        public bool Held(Binding b) { if (KeysMenu.IsOpen) return false; foreach (var k in b.Keys) if (Input.GetKey(k)) return true; return false; }
        public bool Pressed(Binding b) { if (KeysMenu.IsOpen) return false; foreach (var k in b.Keys) if (Input.GetKeyDown(k)) return true; return false; }

        public bool IsBound(KeyCode k) { foreach (var b in All) if (b.Keys.Contains(k)) return true; return false; }

        /// <summary>Gives key k to binding b (first place); takes it away from any other binding; keeps at most MaxKeys.</summary>
        public void Assign(Binding b, KeyCode k)
        {
            foreach (var o in All) o.Keys.Remove(k);
            b.Keys.Insert(0, k);
            while (b.Keys.Count > MaxKeys) b.Keys.RemoveAt(b.Keys.Count - 1);
        }

        public void ResetAll() { foreach (var b in All) { b.Keys.Clear(); b.Keys.AddRange(b.Defaults); } }

        // ---------------------------------------------------------------- the file
        /// <summary>Where the file lives: next to the program when that folder can be written to, else in the program's data folder.</summary>
        static string ChooseFolder()
        {
            string beside = Places.BesideProgram();
            if (beside != null && Places.Writable(beside)) return beside;
            return Application.persistentDataPath;
        }

        public void Load()
        {
            FilePath = Path.Combine(ChooseFolder(), FileName);
            if (!File.Exists(FilePath)) { Save(); return; }
            try
            {
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    string name = line.Substring(0, eq).Trim();
                    var b = All.Find(x => x.Name == name); if (b == null) continue;
                    var keys = new List<KeyCode>();
                    foreach (var part in line.Substring(eq + 1).Split(','))
                    {
                        KeyCode k; string p = part.Trim();
                        if (p.Length > 0 && Enum.TryParse(p, true, out k) && k != KeyCode.None && !keys.Contains(k)) keys.Add(k);
                    }
                    if (keys.Count > 0) { b.Keys.Clear(); for (int i = 0; i < keys.Count && i < MaxKeys; i++) b.Keys.Add(keys[i]); }
                }
            }
            catch (Exception e) { Debug.LogWarning("[keys] " + e.Message); }
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + Title + " 的按键设置。");
            sb.AppendLine("# 可以在游戏里按 F1 修改；也可以直接改这个文件，改完重新打开游戏。");
            sb.AppendLine("# 每一行：手柄上的按钮 = 键1, 键2, 键3（最多三个）。键名的写法：字母 A–Z，数字 Alpha0–Alpha9，");
            sb.AppendLine("# 方向键 UpArrow / DownArrow / LeftArrow / RightArrow，Space 空格，Return 回车，LeftShift / RightShift，");
            sb.AppendLine("# 小键盘 Keypad0–Keypad9，手柄按钮 JoystickButton0–JoystickButton19。删掉这个文件就恢复默认。");
            sb.AppendLine();
            foreach (var b in All) sb.AppendLine(b.Name + " = " + string.Join(", ", b.Keys));
            try { File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception e) { Debug.LogWarning("[keys] cannot write " + FilePath + ": " + e.Message); }
        }

        // ---------------------------------------------------------------- names for the screen
        public static string Pretty(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.UpArrow: return "↑"; case KeyCode.DownArrow: return "↓"; case KeyCode.LeftArrow: return "←"; case KeyCode.RightArrow: return "→";
                case KeyCode.Return: return "回车"; case KeyCode.KeypadEnter: return "小键盘回车"; case KeyCode.Space: return "空格";
                case KeyCode.RightShift: return "右Shift"; case KeyCode.LeftShift: return "左Shift"; case KeyCode.Backspace: return "退格";
                case KeyCode.RightControl: return "右Ctrl"; case KeyCode.LeftControl: return "左Ctrl"; case KeyCode.Tab: return "Tab"; case KeyCode.BackQuote: return "`";
            }
            string s = k.ToString();
            if (s.StartsWith("JoystickButton")) return "手柄" + s.Substring(14);
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "小键盘" + s.Substring(6);
            return s;
        }
        public static string Pretty(List<KeyCode> keys) { if (keys.Count == 0) return "（没有设置）"; var p = new List<string>(); foreach (var k in keys) p.Add(Pretty(k)); return string.Join(" / ", p); }
        public string Pretty(Binding b) { return Pretty(b.Keys); }
    }

    /// <summary>The program's own fixed keys (camera, switches).  Quiet while the key menu is open, and for any key the player
    /// has given to one of the controller's buttons (so one key never does two things).</summary>
    public static class Keys
    {
        static bool Free(KeyCode k) { return !KeysMenu.IsOpen && (Controls.Current == null || !Controls.Current.IsBound(k)); }
        public static bool Down(KeyCode k) { return Free(k) && Input.GetKeyDown(k); }
        public static bool Held(KeyCode k) { return Free(k) && Input.GetKey(k); }
    }

    /// <summary>Folders the program uses.</summary>
    public static class Places
    {
        /// <summary>The folder the program's file (Windows .exe, macOS .app) is in; null in the editor, or when macOS runs the
        /// app from a hidden copy (an app straight out of a downloaded archive, before it has been moved).</summary>
        public static string BesideProgram()
        {
            if (Application.isEditor) return null;
            string data = Application.dataPath;                       // Windows: ...\X_Data   macOS: .../X.app/Contents
            string dir = Application.platform == RuntimePlatform.OSXPlayer ? Path.GetDirectoryName(Path.GetDirectoryName(data)) : Path.GetDirectoryName(data);
            if (dir == null || dir.Contains("/AppTranslocation/")) return null;
            return dir;
        }

        public static bool Writable(string dir)
        {
            try { string t = Path.Combine(dir, ".write_test"); File.WriteAllText(t, "x"); File.Delete(t); return true; }
            catch { return false; }
        }

        /// <summary>Shows a folder in Explorer / Finder (the folder names may be Chinese: no file:// URL).</summary>
        public static void Open(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                if (Application.platform == RuntimePlatform.WindowsPlayer) System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                else if (Application.platform == RuntimePlatform.OSXPlayer) System.Diagnostics.Process.Start("open", "\"" + dir + "\"");
                else Application.OpenURL(new Uri(dir).AbsoluteUri);
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
        }
    }

    /// <summary>The on-screen text is Chinese: use a Chinese font the system already has (nothing is shipped).</summary>
    public static class UiFont
    {
        static Font font; static bool tried;
        static readonly string[] Names = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "DengXian", "PingFang SC", "Hiragino Sans GB", "Heiti SC", "Noto Sans CJK SC", "Arial Unicode MS" };
        /// <summary>Call at the top of OnGUI.</summary>
        public static void Apply() { var f = Get(); if (f != null) GUI.skin.font = f; }

        /// <summary>A font with Chinese letters from those the computer has (null: none of them).</summary>
        public static Font Get()
        {
            if (!tried)
            {
                tried = true;
                try
                {
                    var have = new HashSet<string>(Font.GetOSInstalledFontNames());
                    var use = new List<string>(); foreach (var n in Names) if (have.Contains(n)) use.Add(n);
                    if (use.Count > 0) font = Font.CreateDynamicFontFromOSFont(use.ToArray(), 16);
                }
                catch (Exception e) { Debug.LogWarning("[font] " + e.Message); }
            }
            return font;
        }
    }

    /// <summary>The F1 screen: pick a button with ↑↓, Enter, then press the new key (or pad button).</summary>
    public sealed class KeysMenu : MonoBehaviour
    {
        static int closedFrame = -1, openedFrame = -1;
        static bool open;
        /// <summary>The menu is up (or was closed this very frame: its Esc must not reach anybody else).</summary>
        public static bool IsOpen { get { return open || closedFrame == Time.frameCount; } }

        public GameHost Host;
        int row, waiting = -1;                 // waiting: the row that is listening for its new key
        float repeat;
        bool pausedBefore;
        GUIStyle style, title, dim;
        static Array allKeys;

        void Update()
        {
            var c = Controls.Current; if (c == null) return;
            if (!open)
            {
                if (Input.GetKeyDown(KeyCode.F1)) OpenNow();
                return;
            }
            if (Time.frameCount == openedFrame) return;                // (the key that opened it from the start screen must not set a key)
            int rows = c.All.Count;
            if (waiting >= 0)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { waiting = -1; return; }
                if (allKeys == null) allKeys = Enum.GetValues(typeof(KeyCode));
                foreach (KeyCode k in allKeys)
                {
                    if (k == KeyCode.None || (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) || k == KeyCode.F1 || k == KeyCode.Escape) continue;
                    if (k >= KeyCode.Joystick1Button0) continue;          // (the same buttons again, per pad: JoystickButtonN covers them)
                    if (Input.GetKeyDown(k)) { c.Assign(c.All[waiting], k); c.Save(); waiting = -1; break; }
                }
                return;
            }
            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            // (the keyboard's arrows and W/S drive the "Vertical" axis too: the axis is only read for a pad's stick or cross)
            float v = Input.GetAxisRaw("Vertical");
            bool kb = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S);
            repeat -= Time.unscaledDeltaTime;
            if (Input.GetKeyDown(KeyCode.UpArrow) || (!kb && v > 0.5f && repeat <= 0)) { row = (row + rows) % (rows + 1); repeat = 0.25f; }
            if (Input.GetKeyDown(KeyCode.DownArrow) || (!kb && v < -0.5f && repeat <= 0)) { row = (row + 1) % (rows + 1); repeat = 0.25f; }
            if (Mathf.Abs(v) < 0.3f) repeat = 0;
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0);
            if (enter) { if (row == rows) { c.ResetAll(); c.Save(); } else waiting = row; }
            if (row < rows && (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)))
            {
                var b = c.All[row]; b.Keys.Clear(); b.Keys.AddRange(b.Defaults);
                foreach (var o in c.All) if (o != b) foreach (var k in b.Keys) o.Keys.Remove(k);
                c.Save();
            }
        }

        /// <summary>Opens the screen (F1, or 按键设置 on the start screen / the Esc menu).</summary>
        public void OpenNow()
        {
            if (open) return;
            open = true; row = 0; waiting = -1; openedFrame = Time.frameCount;
            if (Host != null) { pausedBefore = Host.Paused; Host.Paused = true; }
        }

        void Close() { open = false; closedFrame = Time.frameCount; waiting = -1; if (Host != null) Host.Paused = pausedBefore; }

        void OnGUI()
        {
            if (!open || Controls.Current == null) return;
            UiFont.Apply();
            var c = Controls.Current;
            int fs = Mathf.Max(14, Screen.height / 40);
            if (style == null) { style = new GUIStyle(GUI.skin.label); title = new GUIStyle(style); dim = new GUIStyle(style); style.richText = false; }
            style.fontSize = fs; title.fontSize = fs * 3 / 2; dim.fontSize = fs * 4 / 5;
            style.normal.textColor = Color.white; title.normal.textColor = Color.white; dim.normal.textColor = new Color(1, 1, 1, 0.7f);
            float w = Mathf.Min(Screen.width - 40, fs * 34), lh = fs * 1.6f, h = lh * (c.All.Count + 6.5f);
            var box = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.color = new Color(0, 0, 0, 0.82f); GUI.DrawTexture(box, Texture2D.whiteTexture); GUI.color = Color.white;
            float x = box.x + fs * 1.5f, y = box.y + fs;
            GUI.Label(new Rect(x, y, w, lh * 1.4f), "按键设置", title); y += lh * 1.6f;
            for (int i = 0; i <= c.All.Count; i++)
            {
                bool sel = i == row;
                if (sel) { GUI.color = new Color(1f, 0.8f, 0.2f, 0.25f); GUI.DrawTexture(new Rect(box.x + fs * 0.6f, y - fs * 0.1f, w - fs * 1.2f, lh), Texture2D.whiteTexture); GUI.color = Color.white; }
                if (i < c.All.Count)
                {
                    var b = c.All[i];
                    GUI.Label(new Rect(x, y, fs * 9, lh), b.Name, style);
                    string keys = waiting == i ? "请按新的键或手柄按钮……（Esc 取消）" : c.Pretty(b);
                    GUI.Label(new Rect(x + fs * 9, y, w - fs * 11, lh), keys, style);
                }
                else GUI.Label(new Rect(x, y, w, lh), "全部恢复默认", style);
                y += lh;
            }
            y += lh * 0.5f;
            GUI.Label(new Rect(x, y, w - fs * 3, lh * 3),
                "↑↓ 选择    回车 设置新的键（加在最前面，最多三个）    退格 恢复这一项的默认\nF1 / Esc 关闭（改动已自动保存）\n设置文件：" + c.FilePath, dim);
        }
    }
}
