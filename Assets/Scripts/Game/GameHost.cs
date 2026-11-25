// Runs the original game: the emulator core, the pads, and the bridge that watches what the game does.
using System.IO;
using UnityEngine;
using NesCore;

namespace Soccer
{
    public sealed class GameHost : MonoBehaviour
    {
        public const double FrameTime = 1.0 / 60.0988;
        public Nes Nes;
        public SoccerBridge Bridge;
        public Tweaks Tweaks;                        // computer v computer, special shots on demand
        public SpriteArt Art;
        public Texture2D FrameTexture;
        public OrbitCamera Orbit;
        public AudioOut Audio;
        public bool CameraRelativeControls = true;
        public float PadQuietUntil;                  // a menu has just closed: its keys must not reach the game
        public bool Flat;                            // the match shown as the original flat picture, not the 3D scene ("3D 场景开关": F2 / `)
        public bool Interpolate = true;
        public bool Paused;
        public float Speed = 1f;
        public string Error;
        public System.Func<int, byte> Script;       // scripted pad for the self-test (frame number -> buttons)
        public int FrameNo;
        public System.IO.Stream AudioDump;           // when set, every sample is also written here (float32 mono) - for the demo reel
        byte[] dumpBytes; float[] dumpMix; double dumpOwed; float dumpHold;
        double acc;
        float framesSinceTick;

        // ---- the standstill of a special shot ("super flash"): the game is simply not run for a moment, while the effects
        // layer darkens the world round the shooter and the ball.  L switches it off / on.
        public bool SuperFreeze = true;
        public const float FreezeTime = 0.62f;
        /// <summary>Seconds of standstill left (0: the game runs).</summary>
        public float Freeze;
        /// <summary>Goes up by one with every standstill; which special it is for, and who shot it (object slot).</summary>
        public int FreezeSerial, FreezeNo, FreezeShooter = -1;
        /// <summary>The sounds of the special shots: Play() on this reaches the loudspeakers and, when one is being written, the sound track.</summary>
        public Sfx SfxLive, SfxDump;
        int seenSpecial, freezeDue = -1; long lastFreezeFrame = -100000;
        // frames of the game: a special that is hit on (or goes into its next stage) soon after does not stop the world again -
        // the game writes its number once more then, with the same shooter.  Somebody else's special may follow sooner.
        const int FreezeGap = 150, FreezeGapOther = 45;

        public bool Init()
        {
            // the player's own copy of the game (RomFile: the ROM folder next to the program; it is not shipped)
            if (!RomFile.Find()) { Error = "找不到游戏文件：请把 " + RomFile.Expected + " 放进 " + RomFile.AskFolder(); return false; }
            Nes = new Nes();
            if (!Nes.LoadRom(RomFile.Bytes)) { Error = "无法载入游戏文件"; return false; }
            Bridge = new SoccerBridge(Nes);
            Tweaks = new Tweaks(Nes);
            Nes.Apu.SampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            SfxLive = new Sfx(Nes.Apu.SampleRate); SfxDump = new Sfx(Nes.Apu.SampleRate);
            if (Audio != null) Audio.Fx = SfxLive;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-superFreeze") SuperFreeze = args[i + 1] != "0";
            // (the sharpened sprites are made from the game's graphics: shipped locked with the game as the key, see RomFile)
            var table = Resources.Load<TextAsset>("Sprites/sprite_classes");
            Art = new SpriteArt(table != null ? RomFile.Open(table.bytes) : null, Nes.Chr);
            FrameTexture = new Texture2D(256, 240, TextureFormat.RGBA32, false, false);
            FrameTexture.filterMode = FilterMode.Point; FrameTexture.wrapMode = TextureWrapMode.Clamp; FrameTexture.hideFlags = HideFlags.DontSave;
            return true;
        }

        /// <summary>Advances the game by one video frame.</summary>
        public void StepFrame(byte pad1, byte pad2 = 0)
        {
            if (Tweaks != null) Tweaks.BeforeFrame(ref pad1, ref pad2);
            Nes.Pad1 = pad1; Nes.Pad2 = pad2;
            Nes.RunFrame();
            FrameNo++;
            if (Audio != null) Audio.Push(Nes.Apu.Out, Nes.Apu.OutCount);
            if (AudioDump != null)
            {
                int n = Nes.Apu.OutCount;
                if (dumpMix == null) dumpMix = new float[Nes.Apu.Out.Length];
                System.Array.Copy(Nes.Apu.Out, dumpMix, n);
                if (n > 0) dumpHold = dumpMix[n - 1];
                WriteDump(n);
            }
            Nes.Apu.OutCount = 0;
            framesSinceTick += 1f;
            bool tick = Bridge.Capture();
            if (tick) framesSinceTick = 0f;
            // A special shot has been decided ($050F): the world stops at the first picture that shows the kick and the
            // ball in its special shape (a few frames later), or after 9 frames at the latest.
            if (Tweaks.SpecialSerial != seenSpecial)
            {
                seenSpecial = Tweaks.SpecialSerial;
                long since = Nes.FrameCount - lastFreezeFrame;
                if (SuperFreeze && !Flat && Bridge.InMatch && (since > FreezeGap || (since > FreezeGapOther && Tweaks.LastShooter != FreezeShooter))) freezeDue = 0;
            }
            else if (freezeDue >= 0)
            {
                freezeDue++;
                if ((tick && freezeDue >= 2 && BallIsSpecial(Bridge.Cur)) || freezeDue >= 9)
                {
                    freezeDue = -1; lastFreezeFrame = Nes.FrameCount;
                    if (SuperFreeze && !Flat && Bridge.InMatch) { Freeze = FreezeTime; FreezeSerial++; FreezeNo = Tweaks.LastSpecial; FreezeShooter = Tweaks.LastShooter; }
                }
            }
        }

        void WriteDump(int n)
        {
            if (SfxDump != null) SfxDump.Mix(dumpMix, n, Audio != null ? Audio.FxVolume : 0.8f);
            for (int i = 0; i < n; i++) dumpMix[i] = Sfx.Soft(dumpMix[i]);
            if (dumpBytes == null) dumpBytes = new byte[Nes.Apu.Out.Length * 4];
            System.Buffer.BlockCopy(dumpMix, 0, dumpBytes, 0, n * 4);
            AudioDump.Write(dumpBytes, 0, n * 4);
        }

        /// <summary>The standstill: call once per picture with the time it lasts.  True = the game must not be run for this
        /// picture.  (The sound track of a recording gets its share of silence - and of the special's own sounds - here.)</summary>
        public bool HoldStill(float dt)
        {
            if (Freeze <= 0f) return false;
            Freeze = Mathf.Max(0f, Freeze - dt);
            if (AudioDump != null)
            {
                dumpOwed += dt * Nes.Apu.SampleRate;
                int n = Mathf.Min((int)dumpOwed, Nes.Apu.Out.Length); dumpOwed -= n;
                if (dumpMix == null) dumpMix = new float[Nes.Apu.Out.Length];
                for (int i = 0; i < n; i++) { dumpHold *= 0.99f; dumpMix[i] = dumpHold; }
                WriteDump(n);
            }
            return true;
        }

        /// <summary>A sound of the special shots (see Sfx), to the loudspeakers and to the sound track being written.</summary>
        /// <summary>3D scene or the original picture during a match (the original picture: no standstills, no effect sounds).</summary>
        public void SetFlat(bool on)
        {
            Flat = on; if (on) { Freeze = 0f; freezeDue = -1; }
            if (Nes != null) { Tweaks.Message = on ? "原版画面" : "3D 场景"; Tweaks.MessageFrame = Nes.FrameCount; }
            Debug.Log("[view] " + (on ? "original picture" : "3D scene"));
        }

        public void PlaySfx(int id, float vol = 1f, float delay = 0f)
        {
            if (Flat) return;                                   // (the effects' own sounds go with the 3D scene)
            if (SfxLive != null && !Application.isBatchMode) SfxLive.Play(id, vol, delay);
            if (SfxDump != null && AudioDump != null) SfxDump.Play(id, vol, delay);
        }

        // the ball is drawn as something other than the plain ball (the same test as SpriteField.DescribeBall's first half)
        static bool BallIsSpecial(Tick t)
        {
            var o = t.Objects[SoccerBridge.Ball];
            if (o.PieceCount == 0) return false;
            int mov = t.Ram[0x465] & 0x7F;
            if (mov == 0x11 || mov == 0x12) return false;
            bool special = o.PieceCount != 4 && o.PieceCount != 2;
            for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++)
            {
                int low = t.Pieces[k].Tile & 63;
                if (!(low == 3 || low == 4 || low == 5 || low == 0x13 || low == 0x14 || low == 0x15)) special = true;
            }
            return special;
        }

        public void UploadFrame()
        {
            FrameTexture.SetPixelData(Nes.Ppu.FrameBuffer, 0);
            FrameTexture.Apply(false, false);
        }

        /// <summary>How far the picture has moved from the previous tick to the current one (0..1).</summary>
        public float TickBlend
        {
            get
            {
                if (!Interpolate || Bridge.TickCount < 2) return 1f;
                float sub = (float)(acc / FrameTime);
                return Mathf.Clamp01((framesSinceTick + sub) / Mathf.Max(1, Bridge.LastInterval));
            }
        }

        void Update()
        {
            if (Nes == null) return;
            if (Keys.Down(KeyCode.Tab)) CameraRelativeControls = !CameraRelativeControls;
            if (Keys.Down(KeyCode.X)) Interpolate = !Interpolate;
            if (Keys.Down(KeyCode.M) && Audio != null) Audio.Mute = !Audio.Mute;
            if (Keys.Down(KeyCode.N)) Tweaks.ToggleSpectate();
            if (Keys.Down(KeyCode.B)) Tweaks.ToggleAllSpecials();
            if (Keys.Down(KeyCode.Y)) Tweaks.Showcase();
            if (Keys.Down(KeyCode.L))
            {
                SuperFreeze = !SuperFreeze; if (!SuperFreeze) { Freeze = 0f; freezeDue = -1; }
                Tweaks.Message = SuperFreeze ? "必杀定格：开" : "必杀定格：关"; Tweaks.MessageFrame = Nes.FrameCount;
            }
            if (Controls.Current != null && Controls.Current.Pressed(KSpecial)) Tweaks.Press();
            if (Controls.Current != null && Controls.Current.Pressed(KView)) SetFlat(!Flat);
            byte pad = ReadPad();
            if (!Paused && !HoldStill(Mathf.Min(Time.unscaledDeltaTime, 0.1f)))
            {
                acc += Mathf.Min(Time.unscaledDeltaTime, 0.1f) * Speed;
                int n = 0;
                while (acc >= FrameTime && n < 12 && Freeze <= 0f) { StepFrame(Script != null ? Script(FrameNo + 1) : pad); acc -= FrameTime; n++; }
                if (acc > FrameTime * 2) acc = 0;           // never try to catch up after a hitch
                if (n > 0) UploadFrame();
            }
        }

        static readonly byte[] DirBits = { (byte)Pad.Right, (byte)(Pad.Right | Pad.Up), (byte)Pad.Up, (byte)(Pad.Up | Pad.Left), (byte)Pad.Left, (byte)(Pad.Left | Pad.Down), (byte)Pad.Down, (byte)(Pad.Down | Pad.Right) };

        // ---- the controller's buttons, as the player has set them (F1 in the game, or 按键设置.txt; see Controls)
        public static Controls.Binding KUp, KDown, KLeft, KRight, KA, KB, KStart, KSelect, KSpecial, KView, KStage;
        public static Controls MakeControls()
        {
            var c = new Controls { Title = RomFile.Program };
            KUp = c.Add("上", KeyCode.UpArrow, KeyCode.W);
            KDown = c.Add("下", KeyCode.DownArrow, KeyCode.S);
            KLeft = c.Add("左", KeyCode.LeftArrow, KeyCode.A);
            KRight = c.Add("右", KeyCode.RightArrow, KeyCode.D);
            KB = c.Add("B 键", KeyCode.J, KeyCode.U, KeyCode.JoystickButton0);         // B and A as on the NES pad: B left, A right (J K, U I above)
            KA = c.Add("A 键", KeyCode.K, KeyCode.I, KeyCode.JoystickButton1);
            KStart = c.Add("开始（Start）", KeyCode.Return, KeyCode.KeypadEnter, KeyCode.JoystickButton7);
            KSelect = c.Add("选择（Select）", KeyCode.RightShift, KeyCode.Backspace, KeyCode.JoystickButton6);
            KSpecial = c.Add("一键必杀", KeyCode.Space, KeyCode.JoystickButton2);
            KView = c.Add("3D 场景开关", KeyCode.F2, KeyCode.BackQuote);
            KStage = c.Add("换场景", KeyCode.Alpha0, KeyCode.Keypad0);       // 白宫南草坪 ⇄ 东京塔夜场
            return c;
        }

        byte ReadPad()
        {
            var c = Controls.Current;
            if (c == null || Time.unscaledTime < PadQuietUntil) return 0;
            float h = 0, v = 0;
            if (c.Held(KLeft)) h -= 1;
            if (c.Held(KRight)) h += 1;
            if (c.Held(KDown)) v -= 1;
            if (c.Held(KUp)) v += 1;
            float jh = KeysMenu.IsOpen ? 0 : Input.GetAxisRaw("Horizontal"), jv = KeysMenu.IsOpen ? 0 : Input.GetAxisRaw("Vertical");
            if (h == 0 && Mathf.Abs(jh) > 0.4f) h = Mathf.Sign(jh);
            if (v == 0 && Mathf.Abs(jv) > 0.4f) v = Mathf.Sign(jv);
            byte pad = 0;
            if (h != 0 || v != 0)
            {
                // screen direction -> direction on the pitch.  With the camera turned, "up" still means "away from the camera".
                float ang = Mathf.Atan2(v, h) * Mathf.Rad2Deg;
                if (CameraRelativeControls && !Flat && Orbit != null && Bridge.InMatch && !Orbit.LookAtHouse) ang -= Orbit.Yaw;
                int oct = Mathf.RoundToInt(ang / 45f) & 7;
                pad |= DirBits[oct];
            }
            if (c.Held(KA)) pad |= (byte)Pad.A;
            if (c.Held(KB)) pad |= (byte)Pad.B;
            if (c.Held(KStart)) pad |= (byte)Pad.Start;
            if (c.Held(KSelect)) pad |= (byte)Pad.Select;
            return pad;
        }
    }
}
