// Self-test of the built app:   SpriteStage-Soccer-dev.app/Contents/MacOS/SpriteStage-Soccer-dev -selftest <folder>      (add -batchmode to run it without a window)
// Add -stage tokyo to start in the Tokyo Tower night scene; -stageSwitch changes the scene during the match (pictures 2
// and 3 in the other scene, 4 back), the way the 换场景 key does.
// Add -spectate to have the match played computer v computer with every shot a special (the N and B keys) once it starts.
// Plays with a scripted pad (fast-forwards through the menus), then lets the match run at normal speed while
// the camera circles, saves real screenshots (including the status bar) and the measured frame rate, and quits.
using System;
using System.IO;
using UnityEngine;

namespace Soccer
{
    public static class TestPad
    {
        /// <summary>Start through the menus, then run about and kick.</summary>
        public static byte For(int g)
        {
            byte pad = 0;
            if (g % 40 < 4 && g < 600) pad |= 8;
            if (g > 640)
            {
                int ph = (g / 90) % 6;
                if (ph == 0) pad |= 0x80; else if (ph == 1) pad |= 0x90; else if (ph == 2) pad |= 0x40; else if (ph == 3) pad |= 0x60; else if (ph == 4) pad |= 0xA0; else pad |= 0x10;
                if (g % 50 < 3) pad |= 1;
                if (g % 77 < 3) pad |= 2;
            }
            return pad;
        }
    }

    public sealed class SelfTest : MonoBehaviour
    {
        public Director D;
        public string Dir;
        public bool Spectate;
        int shot;
        float t0 = -1, worst;
        int frames;
        // -launchTest: through the start screen (picture app_0_start), "直接开始比赛", and the Esc menu in picture 2
        bool launchTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-launchTest") >= 0, launched;
        float tLaunch = -1f;
        bool viewSwitch = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-viewSwitch") >= 0;
        bool stageSwitch = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-stageSwitch") >= 0;
        string firstStage;
        readonly System.Text.StringBuilder stages = new System.Text.StringBuilder();
        readonly float[] shotAt = { 2f, 5f, 8f, 11f };

        void Start()
        {
            Directory.CreateDirectory(Dir);
            Application.runInBackground = true;         // the test must not stall when the window is not in front
            if (!launchTest) { D.Host.Script = TestPad.For; D.Host.Speed = 8f; }
            D.Hud.ShowHelp = false;
            if (Application.isBatchMode) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }
            Debug.Log("[SpriteStage] self-test -> " + Dir);
        }

        /// <summary>Measures what one picture costs: n off-screen renders, each forced to finish by reading a pixel back.</summary>
        string RenderCost(int w, int h, int n)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.antiAliasing = 4;
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            D.Cam.targetTexture = rt; D.Cam.ResetAspect();
            D.Host.SuperFreeze = false; D.Host.Freeze = 0f;         // (the game is stepped by hand here: no standstills)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                if (i == 10) sw.Restart();
                D.Host.StepFrame(TestPad.For(D.Host.FrameNo + 1));
                D.Frame(1f / 60f);
                D.Cam.Render();
                RenderTexture.active = rt; one.ReadPixels(new Rect(w / 2, h / 2, 1, 1), 0, 0); RenderTexture.active = null;
            }
            double ms = sw.Elapsed.TotalMilliseconds / (n - 10);
            D.Cam.targetTexture = null; D.Cam.ResetAspect();
            sw.Restart();
            for (int i = 0; i < 600; i++) D.Host.StepFrame(TestPad.For(D.Host.FrameNo + 1));
            double emu = sw.Elapsed.TotalMilliseconds / 600;
            return string.Format("one frame at {0}x{1}, 4x MSAA, incl. game step: {2:F2} ms\ngame step alone (emulator + bridge): {3:F2} ms\n", w, h, ms, emu);
        }

        void Update()
        {
            var h = D.Host;
            if (h.Nes == null) { File.WriteAllText(Path.Combine(Dir, "result.txt"), "error: " + h.Error); Application.Quit(); return; }
            if (launchTest && !launched)
            {
                if (tLaunch < 0) tLaunch = Time.unscaledTime;
                if (Time.unscaledTime - tLaunch < 2f) return;
                File.WriteAllBytes(Path.Combine(Dir, "app_0_start.png"), Snapshot.Png(D, 1600, 900));
                D.Front.Choose(0); launched = true; return;
            }
            if (launchTest && t0 < 0 && (D.Front.Busy || !h.Bridge.InMatch)) return;
            if (t0 < 0)
            {
                if (!launchTest && h.FrameNo < 1300) return;
                h.Speed = 1f; t0 = Time.unscaledTime; D.Orbit.AutoOrbit = true;
                if (Spectate) { h.Tweaks.Spectate = true; h.Tweaks.AllSpecials = true; }
                Debug.Log("[SpriteStage] self-test: match reached, in match = " + h.Bridge.InMatch);
                return;
            }
            float t = Time.unscaledTime - t0;
            if (firstStage == null) firstStage = Field.Stage;
            if (stageSwitch)
            {
                string want = t >= 3.5f && t < 9.5f ? (firstStage == "tokyo" ? "" : "tokyo") : firstStage;
                if (Field.Stage != want) { var sw = System.Diagnostics.Stopwatch.StartNew(); D.SwitchStage(want); stages.AppendFormat("switched to {0} at {1:F1} s in {2} ms\n", want == "" ? "whitehouse" : want, t, sw.ElapsedMilliseconds); }
            }
            if (viewSwitch) { bool flat = t >= 3.5f && t < 9.5f; if (h.Flat != flat) h.SetFlat(flat); }   // -viewSwitch: pictures 2 and 3 in the original picture, 4 back in 3D
            bool switching = stageSwitch && (Mathf.Abs(t - 3.5f) < 0.6f || Mathf.Abs(t - 9.5f) < 0.6f);   // (the scene change itself is timed on its own, below)
            if (t > 0.5f) { frames++; if (!switching) worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            if (shot < shotAt.Length && t >= shotAt[shot])
            {
                string png = Path.Combine(Dir, "app_" + (shot + 1) + ".png");
                if (launchTest && shot == 1) D.Front.OpenPause();
                if (launchTest && shot == 2) D.Front.ShowHelp();
                if (Application.isBatchMode) File.WriteAllBytes(png, Snapshot.Png(D, 1600, 900));       // no window: draw off-screen
                else ScreenCapture.CaptureScreenshot(png);
                if (launchTest && shot == 1) D.Front.CloseMenu();
                if (launchTest && shot == 2) D.Front.HideHelp();
                if (shot == 1) D.Orbit.Preset(2);
                if (shot == 2) { D.Orbit.Preset(3); }
                shot++;
            }
            if (t >= shotAt[shotAt.Length - 1] + 1f)
            {
                float fps = frames / (t - 0.5f);
                var tw = h.Tweaks; var ram = h.Nes.Ram;
                string tweaks = string.Format("spectate {0}, all specials {1}, specials so far {2}, pads {3:X2} {4:X2}, team bytes {5:X2} {6:X2}\n",
                    tw.Spectate, tw.AllSpecials, tw.Specials, ram[0x568], ram[0x569], ram[0x52C], ram[0x52D]);
                string cost = Application.isBatchMode ? RenderCost(2560, 1440, 120) : "";
                File.WriteAllText(Path.Combine(Dir, "result.txt"),
                    string.Format("ok\nwindow {8}\nscreen {0}x{1}\naverage fps {2:F1}\nworst frame {3:F1} ms\nin match {4}\nticks {5}\nsandbox failures {6}\ngpu {7}\n" + cost,
                        Screen.width, Screen.height, fps, worst * 1000f, h.Bridge.InMatch, h.Bridge.TickSerial, h.Bridge.SandboxFailures, SystemInfo.graphicsDeviceName, Application.isBatchMode ? "none (batch mode, off-screen)" : "yes") + tweaks
                    + "stage at start " + (firstStage == "" ? "whitehouse" : firstStage) + ", at the end " + (Field.Stage == "" ? "whitehouse" : Field.Stage) + "\n" + stages);
                Application.Quit();
            }
        }
    }
}
