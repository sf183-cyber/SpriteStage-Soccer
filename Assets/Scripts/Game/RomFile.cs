// The game itself is not part of this program.  The player puts their own copy of it (a .nes file, any name) into the ROM
// folder next to the program; this finds it and decides whether it will do:
//   - a version that has been tried (by the SHA-1 of the file without its 16-byte header): yes;
//   - another version whose program is the same where this program relies on it (the "code fingerprint": the SHA-1 of the
//     8 KB program pages that hold what the bridge reads and patches - for this game all of them): yes;
//   - anything else (a hack that changed the program): the player is told, and may still try it (Enter).
// The header is replaced by the one the program was made for, so a differently written header does not matter.
// It also opens the data that was made from the game's own graphics (the sharpened sprites), which is shipped
// locked with those very graphics (Tools/rom_lock.py): every sprite tile opens with its own pixels, so a version whose
// letters were redrawn still opens all the rest; a tile that does not open is drawn with the plain pixels.
//   RomPrompt: the screen shown until a file is there.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Soccer
{
    public static class RomFile
    {
        // ---- this game
        public const string Program = "SpriteStage-Soccer";
        public const string GameTitle = "热血足球联盟（くにおくんの熱血サッカーリーグ）";
        public const string Expected = "日文原版（.nes，262,160 字节）";
        static readonly Dictionary<string, string> Known = new Dictionary<string, string> {
            { "c29da974d39a76b7d1134a81cadaf00b51208151", "日文原版" } };
        static readonly int[] CodePages = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        const string CodeSha1 = "61ab2dac2439d27c559780878ff54759a2d62f7a";
        const string Salt = "spritestage/soccer/";
        static readonly byte[] Header = { 0x4E, 0x45, 0x53, 0x1A, 0x08, 0x10, 0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        const int PrgSize = 128 * 1024, ChrSize = 128 * 1024;

        public static byte[] Bytes;            // the game, with the header the program was made for
        public static string FoundAt, Version;
        public static string Problem;          // shown to the player: why a file that was found is not used (yet)
        public static string Pending;          // a file of the game whose program differs: used once the player says so
        public static bool Force;

        /// <summary>Where to look: the command line (-rom file), the ROM folder next to the program, the ROM folder in the
        /// program's data folder, and in the editor the project's own ROM folder.</summary>
        public static List<string> Folders()
        {
            var l = new List<string>();
            string beside = Places.BesideProgram();
            if (beside != null) l.Add(Path.Combine(beside, "ROM"));
            l.Add(Path.Combine(Application.persistentDataPath, "ROM"));
            if (Application.isEditor) l.Add(Path.Combine(Path.GetDirectoryName(Application.dataPath), "ROM"));
            return l;
        }

        /// <summary>The folder the player is asked to put the file in (made if it is not there).</summary>
        public static string AskFolder()
        {
            string beside = Places.BesideProgram();
            string dir = beside != null && Places.Writable(beside) ? Path.Combine(beside, "ROM") : Path.Combine(Application.persistentDataPath, "ROM");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        public static bool Find()
        {
            if (Bytes != null) return true;
            Problem = null; string pendingNow = null;
            var files = new List<string>();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-rom") files.Add(args[i + 1]);
            // a file dropped on the program's icon (Windows) comes as a plain argument: used, and copied into the ROM folder
            dropped.Clear();
            for (int i = 1; i < args.Length; i++)
                if (args[i - 1] != "-rom" && IsGameFile(args[i]) && File.Exists(args[i])) { files.Add(args[i]); dropped.Add(args[i]); }
            if (Array.IndexOf(args, "-forceRom") >= 0) Force = true;
            foreach (var dir in Folders())
                try { if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir)) if (IsGameFile(f)) files.Add(f); }
                catch { }
            // .nes files, and .nes files inside .zip files (as downloaded: no need to unpack them)
            var found = new List<KeyValuePair<string, byte[]>>();
            foreach (var f in files)
            {
                if (!f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try { found.Add(new KeyValuePair<string, byte[]>(f, File.ReadAllBytes(f))); } catch { }
                    continue;
                }
                try
                {
                    using (var z = new ZipArchive(File.OpenRead(f), ZipArchiveMode.Read))
                        foreach (var e in z.Entries)
                        {
                            if (!e.FullName.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) || e.Length > 4 << 20) continue;
                            var b = new byte[e.Length];
                            using (var s = e.Open()) { int n = 0, k; while (n < b.Length && (k = s.Read(b, n, b.Length - n)) > 0) n += k; }
                            found.Add(new KeyValuePair<string, byte[]>(f + "|" + e.FullName, b));
                        }
                }
                catch (Exception ex) { Problem = Path.GetFileName(f) + " 打不开，请先解压，把里面的 .nes 文件放进来。"; Debug.LogWarning("[rom] " + f + ": " + ex.Message); }
            }
            foreach (var kv in found)
            {
                string f = kv.Key; byte[] b = kv.Value;
                string name = f.Contains("|") ? Path.GetFileName(f.Substring(0, f.IndexOf('|'))) + " 里的 " + Path.GetFileName(f.Substring(f.IndexOf('|') + 1)) : Path.GetFileName(f);
                bool nes = b.Length >= 16 && b[0] == 'N' && b[1] == 'E' && b[2] == 'S' && b[3] == 0x1A;
                int mapper = nes ? (b[6] >> 4) | (b[7] & 0xF0) : -1;
                if (!nes || b[4] * 16384 != PrgSize || b[5] * 8192 != ChrSize || mapper != 4 || b.Length < 16 + PrgSize + ChrSize)
                { Problem = name + " 不是《" + GameTitle + "》的游戏文件（大小或卡带类型不对）。"; continue; }
                string body; using (var s = SHA1.Create()) body = Hex(s.ComputeHash(b, 16, PrgSize + ChrSize));
                string ver;
                if (Known.TryGetValue(body, out ver)) { Accept(b, f, ver); return true; }
                string code;
                using (var s = SHA1.Create())
                {
                    var buf = new byte[CodePages.Length * 8192];
                    for (int i = 0; i < CodePages.Length; i++) Array.Copy(b, 16 + CodePages[i] * 8192, buf, i * 8192, 8192);
                    code = Hex(s.ComputeHash(buf));
                }
                if (code == CodeSha1) { Accept(b, f, "没试过的版本（游戏程序和已知版本相同）"); return true; }
                if (Force && f == Pending) { Accept(b, f, "没有适配过的版本"); return true; }
                pendingNow = f;
                Problem = "找到了 " + name + "，但它的游戏程序和本程序适配过的版本不同（可能是改版），可能显示不正常或无法运行。\n按回车仍然用它试试，或者换一个版本。";
            }
            Pending = pendingNow;
            return false;
        }

        static readonly List<string> dropped = new List<string>();
        static bool IsGameFile(string f) { return f.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase); }

        static void Accept(byte[] b, string f, string version)
        {
            // dropped on the icon: kept in the ROM folder, so that next time a double click is enough
            string file = f.Contains("|") ? f.Substring(0, f.IndexOf('|')) : f;
            if (dropped.Contains(file))
                try { string to = Path.Combine(AskFolder(), Path.GetFileName(file)); if (!File.Exists(to)) File.Copy(file, to); } catch { }
            Bytes = new byte[16 + PrgSize + ChrSize]; Array.Copy(Header, Bytes, 16); Array.Copy(b, 16, Bytes, 16, PrgSize + ChrSize);
            FoundAt = f; Version = version; Problem = null; Pending = null;
            Debug.Log("[rom] " + version + ": " + Path.GetFileName(file) + (f.Contains("|") ? " | " + f.Substring(f.IndexOf('|') + 1) : ""));   // (no path functions on "file.zip|entry": | is not allowed in Windows paths)
        }

        // ---------------------------------------------------------------- the locked data
        /// <summary>A shipped data file made from the game's graphics, opened with the player's own copy (see Tools/rom_lock.py).
        /// "SPS2": the sprite table, tile by tile (a tile that does not open comes out empty: plain pixels).  "SPSC": one picture
        /// locked with a stretch of the graphics (null if that stretch differs).  Anything else is returned as it is.</summary>
        public static byte[] Open(byte[] data)
        {
            if (data == null || data.Length < 4 || data[0] != 'S' || data[1] != 'P' || data[2] != 'S') return data;
            if (Bytes == null) return null;
            var salt = Encoding.UTF8.GetBytes(Salt);
            int chr0 = 16 + PrgSize;
            using (var s = SHA256.Create())
            {
                if (data[3] == '2')
                {
                    int first = BitConverter.ToInt32(data, 8), count = BitConverter.ToInt32(data, 12);
                    int tags = 20, body = tags + count * 4;
                    var o = new byte[16 + count * 2 * 256];
                    Array.Copy(data, 4, o, 0, 16);
                    var keyIn = new byte[salt.Length + 16]; Array.Copy(salt, keyIn, salt.Length);
                    var tagIn = new byte[32 + 3]; tagIn[32] = (byte)'t'; tagIn[33] = (byte)'a'; tagIn[34] = (byte)'g';
                    var blockIn = new byte[32 + 1 + 4];
                    int opened = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int t = first + i, p = chr0 + t * 16;
                        if (p + 16 > Bytes.Length) continue;
                        Array.Copy(Bytes, p, keyIn, salt.Length, 16);
                        var key = s.ComputeHash(keyIn);
                        Array.Copy(key, tagIn, 32);
                        var tag = s.ComputeHash(tagIn);
                        bool ok = tag[0] == data[tags + i * 4] && tag[1] == data[tags + i * 4 + 1] && tag[2] == data[tags + i * 4 + 2] && tag[3] == data[tags + i * 4 + 3];
                        if (!ok) continue;                                   // other pixels in this copy: no sharpened art for this tile
                        opened++;
                        Array.Copy(key, blockIn, 32);
                        for (int v = 0; v < 2; v++)
                        {
                            int off = (v * count + i) * 256;
                            blockIn[32] = (byte)v;
                            for (int n = 0; n < 8; n++)
                            {
                                blockIn[33] = (byte)n; blockIn[34] = 0; blockIn[35] = 0; blockIn[36] = 0;
                                var ks = s.ComputeHash(blockIn);
                                for (int j = 0; j < 32; j++) o[16 + off + n * 32 + j] = (byte)(data[body + off + n * 32 + j] ^ ks[j]);
                            }
                        }
                    }
                    Debug.Log("[rom] sharpened tiles: " + opened + " of " + count);
                    return o;
                }
                if (data[3] == 'C')
                {
                    int off = BitConverter.ToInt32(data, 4), len = BitConverter.ToInt32(data, 8);
                    if (off < 0 || chr0 + off + len > Bytes.Length) return null;
                    var keyIn = new byte[salt.Length + len]; Array.Copy(salt, keyIn, salt.Length); Array.Copy(Bytes, chr0 + off, keyIn, salt.Length, len);
                    var key = s.ComputeHash(keyIn);
                    var tagIn = new byte[35]; Array.Copy(key, tagIn, 32); tagIn[32] = (byte)'t'; tagIn[33] = (byte)'a'; tagIn[34] = (byte)'g';
                    var tag = s.ComputeHash(tagIn);
                    for (int j = 0; j < 4; j++) if (tag[j] != data[12 + j]) { Debug.Log("[rom] locked picture does not open with this copy"); return null; }
                    var o = new byte[data.Length - 16];
                    var blockIn = new byte[36]; Array.Copy(key, blockIn, 32);
                    for (int i = 0, n = 0; i < o.Length; n++)
                    {
                        blockIn[32] = (byte)n; blockIn[33] = (byte)(n >> 8); blockIn[34] = (byte)(n >> 16); blockIn[35] = (byte)(n >> 24);
                        var ks = s.ComputeHash(blockIn);
                        for (int j = 0; j < 32 && i < o.Length; j++, i++) o[i] = (byte)(data[16 + i] ^ ks[j]);
                    }
                    return o;
                }
            }
            return null;
        }

        static string Hex(byte[] h) { var sb = new StringBuilder(); foreach (var x in h) sb.Append(x.ToString("x2")); return sb.ToString(); }
    }

    /// <summary>Shown until the game file is there: where to put it; looks again every second and starts the game by itself.</summary>
    public sealed class RomPrompt : MonoBehaviour
    {
        public Action Ready;
        string folder; float next; GUIStyle style, big;
        Camera cam;

        void Start()
        {
            folder = RomFile.AskFolder();
            cam = gameObject.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f); cam.cullingMask = 0;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.O)) Places.Open(folder);
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            bool go = RomFile.Pending != null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter));
            if (go) RomFile.Force = true;
            if (!go && Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            if (RomFile.Find()) { var r = Ready; Destroy(gameObject); if (r != null) r(); }
        }

        void OnGUI()
        {
            UiFont.Apply();
            int fs = Mathf.Max(14, Screen.height / 38);
            if (style == null) { style = new GUIStyle(GUI.skin.label) { wordWrap = true }; big = new GUIStyle(style); }
            style.fontSize = fs; big.fontSize = fs * 2; style.normal.textColor = Color.white; big.normal.textColor = Color.white;
            float x = fs * 2, w = Screen.width - fs * 4, y = Screen.height * 0.16f;
            GUI.Label(new Rect(x, y, w, fs * 3), RomFile.Program, big); y += fs * 3.4f;
            string t = "本程序不带游戏本身。请把你自己的《" + RomFile.GameTitle + "》FC 游戏文件（.nes，或者装着它的 .zip，文件名随意）放进下面这个文件夹，程序会自动认出来并开始：\n\n"
                     + "    " + folder + "\n\n需要的版本：" + RomFile.Expected + "\n"
                     + (Application.platform == RuntimePlatform.WindowsPlayer ? "也可以把游戏文件直接拖到程序图标（.exe）上打开。\n" : "");
            if (RomFile.Problem != null) t += "\n" + RomFile.Problem + "\n";
            t += "\n按 O 打开这个文件夹        按 Esc 退出";
            GUI.Label(new Rect(x, y, w, Screen.height - y), t, style);
        }
    }
}
