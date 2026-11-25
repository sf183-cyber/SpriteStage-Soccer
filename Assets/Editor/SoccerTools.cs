// Project set-up, headless test renders and the player build - all runnable from the command line:
//   Unity -batchmode -projectPath . -executeMethod SoccerTools.Setup -quit
//   Unity -batchmode -projectPath . -executeMethod SoccerTools.Shots -quit [-shotDir <folder>]
//   Unity -batchmode -projectPath . -executeMethod SoccerTools.Build -quit
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Soccer;

public static class SoccerTools
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    static void Import(string name, int max, bool mip, TextureWrapMode wrap, TextureImporterCompression comp, bool alpha = false, bool linear = false)
    {
        string path = null;
        foreach (var ext in new[] { ".jpg", ".png" }) if (File.Exists("Assets/Resources/World/" + name + ext)) path = "Assets/Resources/World/" + name + ext;
        if (path == null) { Debug.Log("[SpriteStage] texture not there yet: " + name); return; }
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = !linear; ti.maxTextureSize = max; ti.mipmapEnabled = mip;
        ti.wrapMode = wrap; ti.anisoLevel = mip ? 16 : 1; ti.filterMode = mip ? FilterMode.Trilinear : FilterMode.Bilinear;
        ti.textureCompression = comp; ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = false; ti.mipMapsPreserveCoverage = alpha; ti.alphaTestReferenceValue = 0.4f;
        ti.isReadable = false;
        var ps = ti.GetDefaultPlatformTextureSettings(); ps.maxTextureSize = max; ps.textureCompression = comp; ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
        Debug.Log("[SpriteStage] imported " + path);
    }

    [MenuItem("SpriteStage/Set up project")]
    public static void Setup()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.productName = "SpriteStage-Soccer-dev"; PlayerSettings.companyName = "Local";      // the development build: its own name, so it is not mistaken for the untouched 3D version
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "local.spritestage.soccer.dev");
        PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = false;
        int levels = QualitySettings.names.Length;
        for (int i = 0; i < levels; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.antiAliasing = 4; QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable; QualitySettings.vSyncCount = 1;
        }
        QualitySettings.SetQualityLevel(levels - 1, false);
        Import("lawn_near", 8192, true, TextureWrapMode.Clamp, TextureImporterCompression.CompressedHQ);
        Import("lawn_far", 8192, true, TextureWrapMode.Clamp, TextureImporterCompression.Compressed);
        Import("lawn_detail", 512, true, TextureWrapMode.Repeat, TextureImporterCompression.Uncompressed, false, true);      // a multiplier, not a colour: 0.5 must stay 0.5
        Import("house", 8192, true, TextureWrapMode.Clamp, TextureImporterCompression.CompressedHQ);
        Import("trees", 8192, true, TextureWrapMode.Clamp, TextureImporterCompression.CompressedHQ, true);
        Import("sky", 4096, false, TextureWrapMode.Repeat, TextureImporterCompression.Uncompressed);
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[SpriteStage] setup done");
    }

    static string Arg(string name, string fallback)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }

    static byte PadFor(int g) { return TestPad.For(g); }

    /// <summary>-size WxH：录像 / 出图的 3D 画面尺寸（默认 1280x720 / 1920x1080）。竖版对比视频用 1080x960。</summary>
    static void Size(ref int w, ref int h)
    {
        var v = Arg("-size", "").Split('x');
        if (v.Length == 2) { w = int.Parse(v[0]); h = int.Parse(v[1]); }
    }

    /// <summary>The N / B keys for the movie tools:  -spectateFrom <movie frame>  (both teams the computer's from there on,
    /// the movie's own buttons are then ignored)  and  -allSpecials 1  (every shot a special).</summary>
    static Action<Director, int> MovieTweaks()
    {
        int from = int.Parse(Arg("-spectateFrom", "-1")); bool all = Arg("-allSpecials", "0") == "1";
        int show = int.Parse(Arg("-showcaseAt", "-1"));         // the Y key at that movie frame (the movie's buttons for pad 1 still go in, but the showcase overrides them)
        var press = new HashSet<int>();                          // -pressAt f1,f2,...  the B key at those movie frames (spectating: the next shot by anyone is a special)
        foreach (var s in Arg("-pressAt", "").Split(',')) if (s.Length > 0) press.Add(int.Parse(s));
        if (from < 0 && !all && show < 0 && press.Count == 0) return (d, f) => { };
        Debug.Log("[SpriteStage] tweaks: spectate from frame " + from + ", all specials " + all + ", showcase at " + show + ", B at " + string.Join(",", press));
        return (d, f) => { d.Host.Tweaks.AllSpecials = all; if (from > 0) d.Host.Tweaks.Spectate = f >= from; if (f == show) d.Host.Tweaks.Showcase(); if (press.Contains(f)) d.Host.Tweaks.Press(); };
    }

    static void Shot(Director d, string path, float yaw, float pitch, float dist, float fov = 30f, bool house = false, int w = 1920, int h = 1080)
    {
        d.Orbit.Snap(yaw, pitch, dist, fov, house);
        File.WriteAllBytes(path, Snapshot.Png(d, w, h, true));
        Debug.Log("[SpriteStage] shot " + path);
    }

    /// <summary>Headless check: plays the game with a scripted pad and renders the 3D view from several camera positions.</summary>
    public static void Shots()
    {
        string dir = Arg("-shotDir", "Shots");
        string set = Arg("-shotSet", "full");
        Directory.CreateDirectory(dir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var d = Director.Create();
        if (Arg("-switchStage", "") != "") d.SwitchStage(Arg("-switchStage", "") == "whitehouse" ? "" : Arg("-switchStage", ""));      // 测换场景那条路（和按 0 一样）
        if (d.Host.Error != null) { Debug.LogError("[SpriteStage] " + d.Host.Error); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int f = 0;
        Action<int> run = until => { while (f < until) { f++; d.Host.StepFrame(PadFor(f)); } };
        // the way in: title screen, menu, then the match
        run(300); File.WriteAllBytes(Path.Combine(dir, "00a_title.png"), Snapshot.Png(d, 1920, 1080, true));
        run(520); File.WriteAllBytes(Path.Combine(dir, "00b_menu.png"), Snapshot.Png(d, 1920, 1080, true));
        run(1300);
        Debug.Log("[SpriteStage] 1300 frames in " + sw.ElapsedMilliseconds + " ms, in match: " + d.Host.Bridge.InMatch + ", ticks " + d.Host.Bridge.TickCount + ", pieces " + d.Host.Bridge.Cur.Pieces.Count);
        Shot(d, Path.Combine(dir, "01_classic.png"), 0, 38, 17);
        if (set == "tokyo")
        {
            // 东京塔夜场（加 -stage tokyo）：看塔、反打、球场各个角度
            Shot(d, Path.Combine(dir, "02_low_tower.png"), 0, 1, 27, 56);
            Shot(d, Path.Combine(dir, "03_whole_tower.png"), 0, -12, 30, 64);
            Shot(d, Path.Combine(dir, "04_yaw30_low.png"), 30, 4, 24, 50);
            Shot(d, Path.Combine(dir, "05_from_west.png"), 90, 22, 24);
            Shot(d, Path.Combine(dir, "06_reverse.png"), 180, 6, 30, 46);
            Shot(d, Path.Combine(dir, "07_close.png"), -30, 14, 7);
            Shot(d, Path.Combine(dir, "08_whole_pitch.png"), -20, 30, 44);
            Shot(d, Path.Combine(dir, "09_high_wide.png"), 0, 16, 70, 50);
            Shot(d, Path.Combine(dir, "10_east_low.png"), 90, 3, 26, 52);
            Shot(d, Path.Combine(dir, "11_west_low.png"), -90, 3, 26, 52);
            Shot(d, Path.Combine(dir, "12_top.png"), 0, 86, 34);
            run(2400);
            Shot(d, Path.Combine(dir, "13_later_classic.png"), 0, 38, 17);
            Shot(d, Path.Combine(dir, "14_later_yaw-35_low.png"), -35, 3, 22, 54);
        }
        else if (set != "quick")
        {
            Shot(d, Path.Combine(dir, "02_low_north.png"), 0, 5, 27, 52);
            Shot(d, Path.Combine(dir, "03_yaw45.png"), 45, 28, 20);
            Shot(d, Path.Combine(dir, "04_from_west.png"), 90, 22, 24);
            Shot(d, Path.Combine(dir, "05_from_house.png"), 180, 8, 32, 34);
            Shot(d, Path.Combine(dir, "06_top.png"), 0, 86, 34);
            Shot(d, Path.Combine(dir, "07_close.png"), -30, 14, 7);
            Shot(d, Path.Combine(dir, "08_whole_pitch.png"), -20, 30, 44);
            Shot(d, Path.Combine(dir, "11_house_front.png"), 0, 4, 62, 38, true);
            Shot(d, Path.Combine(dir, "12_house_from_east.png"), -32, 7, 46, 38, true);
            Shot(d, Path.Combine(dir, "13_house_portico.png"), 22, 3, 30, 40, true);
            Shot(d, Path.Combine(dir, "14_house_high.png"), 12, 24, 70, 34, true);
            Shot(d, Path.Combine(dir, "15_zoomed_out_from_north.png"), 180, 9, 64, 40);      // would stand inside the house: the camera must stop in front of it
            Shot(d, Path.Combine(dir, "16_zoomed_out_from_northeast.png"), 235, 10, 70, 40);    // ... and in front of the east wing
            run(2400);
            Shot(d, Path.Combine(dir, "09_later_classic.png"), 0, 38, 17);
            Shot(d, Path.Combine(dir, "10_later_yaw-60.png"), -60, 24, 22);
        }
        Debug.Log("[SpriteStage] shots done, sandbox failures " + d.Host.Bridge.SandboxFailures);
    }

    /// <summary>Headless demo reel: a scripted match while the camera goes once round the pitch.  Writes numbered JPEGs (60 per second) and the game's sound (audio_f32.raw).</summary>
    public static void Video()
    {
        string dir = Arg("-shotDir", "Video");
        Directory.CreateDirectory(dir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var d = Director.Create();
        if (d.Host.Error != null) { Debug.LogError("[SpriteStage] " + d.Host.Error); return; }
        int f = 0, n = 0;
        while (f < 1500) { f++; d.Host.StepFrame(TestPad.For(f)); }
        var audio = new FileStream(Path.Combine(dir, "audio_f32.raw"), FileMode.Create);
        d.Host.AudioDump = audio;
        Debug.Log("[SpriteStage] audio sample rate " + d.Host.Nes.Apu.SampleRate);
        bool house = false;
        Action<float, float, float, float> frame = (yaw, pitch, dist, fov) =>
        {
            f++; d.Host.StepFrame(TestPad.For(f));
            d.Orbit.Snap(yaw, pitch, dist, fov, house);
            File.WriteAllBytes(Path.Combine(dir, string.Format("f{0:D4}.jpg", n++)), Snapshot.Png(d, 1280, 720, false, 93));
        };
        Func<float, float> ease = t => t * t * (3 - 2 * t);
        Func<float, float, float, float> mix = (a, b, t) => a + (b - a) * t;
        for (int i = 0; i < 80; i++) frame(0, 38, 21, 30);                                // the original's view
        for (int i = 0; i < 110; i++) { float e = ease(i / 109f); frame(0, mix(38, 5, e), mix(21, 27, e), mix(30, 52, e)); }      // sink down: the White House comes into view
        for (int i = 0; i < 60; i++) frame(0, 5, 27, 52);
        house = true;                                                                       // a visit to the house: drift across its south front, closing in
        for (int i = 0; i < 300; i++) { float t = i / 299f, e = ease(t); frame(mix(-34, 30, e), mix(7, 3, e), mix(74, 34, e), 38); }
        house = false;
        for (int i = 0; i < 400; i++)                                                       // once round the pitch
        {
            float t = i / 399f, e = ease(t), s = Mathf.Sin(Mathf.PI * t);
            frame(360f * e, 5f + 17f * s, mix(27, 26, s), mix(52, 36, s));
        }
        for (int i = 0; i < 80; i++) { float e = ease(i / 79f); frame(0, mix(5, 38, e), mix(27, 21, e), mix(52, 30, e)); }
        for (int i = 0; i < 130; i++) { float e = ease(i / 129f); frame(-25f * e, 38f - 6f * e, 21f + 23f * e, 30f); }        // pull up to see the whole pitch
        d.Host.AudioDump = null; audio.Close();
        Debug.Log("[SpriteStage] video frames " + n);
    }

    /// <summary>Headless pictures at chosen moments of a recorded run (FCEUX .fm2), e.g. a special shot or a thunderstorm.
    ///   -movie file.fm2  -shotDir dir  -frames "4494:0:30:14:30;9752:40:20:12:30"     (frame:yaw:pitch:distance:fov[:east:north:height])
    /// Without the last three the camera follows the play; with them it looks at that fixed point (e.g. a goal).
    /// The last second before each picture is played with the 3D view running, so that trails and particles are in place.</summary>
    public static void MovieShots()
    {
        string dir = Arg("-shotDir", "Shots"), file = Arg("-movie", null), spec = Arg("-frames", "");
        Directory.CreateDirectory(dir);
        if (file == null || !File.Exists(file)) { Debug.LogError("[SpriteStage] no movie: " + file); return; }
        var movie = Movie.Parse(File.ReadAllLines(file));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var d = Director.Create();
        if (d.Host.Error != null) { Debug.LogError("[SpriteStage] " + d.Host.Error); return; }
        var shots = new System.Collections.Generic.List<float[]>();
        foreach (var part in spec.Split(';'))
        {
            var v = part.Split(':'); if (v.Length < 1 || v[0].Length == 0) continue;
            var a = new float[] { 0, 0, 30, 16, 30, float.NaN, 0, 0 };          // ... then optionally a fixed point to look at: east, north, height (metres)
            for (int i = 0; i < v.Length && i < 8; i++) a[i] = float.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture);
            shots.Add(a);
        }
        shots.Sort((x, y) => x[0].CompareTo(y[0]));
        int f = 0;
        var tweak = MovieTweaks();
        d.Host.SuperFreeze = false;         // (pictures of moments of the game: it is not held up)
        foreach (var a in shots)
        {
            int target = (int)a[0];
            while (f < target - 70) { f++; tweak(d, f); movie.Step(d.Host, f); }
            bool fixedPoint = !float.IsNaN(a[5]);
            Vector3 house = d.Orbit.HousePoint;
            if (fixedPoint) d.Orbit.HousePoint = Field.P(a[5], a[6], a[7]);
            d.Orbit.Snap(a[1], a[2], a[3], a[4], fixedPoint);
            while (f < target) { f++; tweak(d, f); movie.Step(d.Host, f); d.Frame(1f / 60f, true); }
            string path = Path.Combine(dir, string.Format("m{0:D6}_y{1}_p{2}.png", target, (int)a[1], (int)a[2]));
            int sw = 1920, sh = 1080; Size(ref sw, ref sh); Snapshot.NoHud = Arg("-nohud", "0") == "1";
            File.WriteAllBytes(path, Snapshot.Png(d, sw, sh, true));
            File.WriteAllBytes(Path.Combine(dir, string.Format("m{0:D6}_2d.png", target)), d.Host.FrameTexture.EncodeToPNG());
            d.Orbit.HousePoint = house;
            Debug.Log("[SpriteStage] movie shot " + path + " in match " + d.Host.Bridge.InMatch);
        }
        Debug.Log("[SpriteStage] movie shots done");
    }

    /// <summary>A stretch of a recorded run as a film: 1280x720 frames + the game's sound.
    ///   -movie file.fm2  -shotDir dir  -range 4380:4720  -cam yaw0:yaw1:pitch:distance:fov[:east:north:height]</summary>
    public static void MovieVideo()
    {
        string dir = Arg("-shotDir", "Video"), file = Arg("-movie", null);
        var range = Arg("-range", "0:0").Split(':'); var cam = Arg("-cam", "0:0:26:13:30").Split(':');
        Directory.CreateDirectory(dir);
        if (file == null || !File.Exists(file)) { Debug.LogError("[SpriteStage] no movie: " + file); return; }
        var movie = Movie.Parse(File.ReadAllLines(file));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        int from = int.Parse(range[0]), to = int.Parse(range[1]);
        float yaw0 = float.Parse(cam[0], inv), yaw1 = float.Parse(cam[1], inv), pitch = float.Parse(cam[2], inv), dist = float.Parse(cam[3], inv), fov = float.Parse(cam[4], inv);
        bool fixedPoint = cam.Length >= 8;          // ... :east:north:height = look at that point instead of following the play
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var d = Director.Create();
        if (d.Host.Error != null) { Debug.LogError("[SpriteStage] " + d.Host.Error); return; }
        int f = 0, n = 0;
        var tweak = MovieTweaks();
        bool freeze = d.Host.SuperFreeze; d.Host.SuperFreeze = false;      // (no standstills while nothing is drawn)
        while (f < from - 90) { f++; tweak(d, f); movie.Step(d.Host, f); }
        d.Host.SuperFreeze = freeze;
        if (fixedPoint) d.Orbit.HousePoint = Field.P(float.Parse(cam[5], inv), float.Parse(cam[6], inv), float.Parse(cam[7], inv));
        if (cam.Length == 6) d.Orbit.ScreenShift = float.Parse(cam[5], inv);   // close-ups: a sixth number moves the ground line (negative = lower, room for a ball in the air)
        d.Orbit.Snap(yaw0, pitch, dist, fov, fixedPoint);
        while (f < from) { if (!d.Host.HoldStill(1f / 60f)) { f++; tweak(d, f); movie.Step(d.Host, f); } d.Frame(1f / 60f, true); }
        var audio = new FileStream(Path.Combine(dir, "audio_f32.raw"), FileMode.Create);
        d.Host.AudioDump = audio;
        int vw = 1280, vh = 720; Size(ref vw, ref vh);
        bool dump2d = Arg("-dump2d", "0") == "1";
        Snapshot.NoHud = Arg("-nohud", "0") == "1";       // 同时存原版画面（256x240，PNG 是上下颠倒的：贴图的行从下往上），给上下对比的视频用
        string lastNote = d.Host.Tweaks.Message;
        int stills = 0, lastStill = d.Host.FreezeSerial;
        while (f < to)
        {
            // the standstill of a special shot: pictures go on, the game (and the movie) wait
            if (d.Host.HoldStill(1f / 60f)) stills++;
            else { f++; tweak(d, f); movie.Step(d.Host, f); }
            if (d.Host.FreezeSerial != lastStill) { lastStill = d.Host.FreezeSerial; Debug.Log("[SpriteStage] standstill at picture " + n + " (movie frame " + f + "): special " + d.Host.FreezeNo.ToString("X2") + " by " + d.Host.FreezeShooter); }
            float t = (f - from) / (float)Mathf.Max(1, to - from);
            d.Orbit.Aim(Mathf.Lerp(yaw0, yaw1, t), pitch, dist, fov);
            d.Frame(1f / 60f);                              // as in real play: the camera eases after the action, the figures move between ticks
            if (d.Host.Tweaks.Message != lastNote) { lastNote = d.Host.Tweaks.Message; Debug.Log("[SpriteStage] note at picture " + n + " (movie frame " + f + "): " + lastNote); }     // (to put captions on the right pictures)
            File.WriteAllBytes(Path.Combine(dir, string.Format("f{0:D4}.jpg", n)), Snapshot.Png(d, vw, vh, false, 93));
            if (dump2d) File.WriteAllBytes(Path.Combine(dir, string.Format("n{0:D4}.png", n)), d.Host.FrameTexture.EncodeToPNG());
            n++;
        }
        d.Host.AudioDump = null; audio.Close();
        Debug.Log("[SpriteStage] movie video frames " + n + " (" + stills + " of them standing still), audio rate " + d.Host.Nes.Apu.SampleRate + ", specials so far " + d.Host.Tweaks.Specials);
    }

    [MenuItem("SpriteStage/Build Mac app")]
    /// <summary>The release: Windows x64 and macOS players, without the game (the player brings it: RomFile).
    ///   -buildPath folder   (default Release): writes folder/Windows/SpriteStage-Soccer.exe and folder/Mac/SpriteStage-Soccer.app</summary>
    public static void BuildRelease()
    {
        Setup();
        PlayerSettings.companyName = "SpriteStage";
        PlayerSettings.productName = "SpriteStage-Soccer";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "org.spritestage.soccer");
        PlayerSettings.SplashScreen.show = false;
        AssetDatabase.SaveAssets();
        string root = Arg("-buildPath", "Release");
        foreach (var t in new[] { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneOSX })
        {
            string path = t == BuildTarget.StandaloneOSX ? Path.Combine(root, "Mac", "SpriteStage-Soccer.app") : Path.Combine(root, "Windows", "SpriteStage-Soccer.exe");
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = t, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[SpriteStage] release " + t + " " + report.summary.result + " size " + report.summary.totalSize + " -> " + path);
        }
    }

    public static void Build()
    {
        Setup();
        string outPath = Arg("-buildPath", "Build/SpriteStage-Soccer-dev.app");
        var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = outPath, target = BuildTarget.StandaloneOSX, options = BuildOptions.None };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("[SpriteStage] build " + report.summary.result + " size " + report.summary.totalSize + " -> " + outPath);
    }
}
