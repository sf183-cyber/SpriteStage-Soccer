// Checks the N / B / space keys of the 3D version (Assets/Scripts/Game/Tweaks.cs, the same code) headless.
//
//   cpuvscpu rom match <frames> <spectateFrom|-1> <allSpecials 0/1> [movie.fm2]
//       Versus match Argentina v Germany (the menu of Tools/AutoPlay.cs, region 3, grass: the first 1510 frames of
//       Tools/movies/argentina_germany*.fm2).  After the menu the pads are idle, or play <movie.fm2> on from frame 1511.
//       spectateFrom = match frame (1 = first after the menu) from which both teams are the computer's
//       (SPECT_TO=<frame> in the environment hands them back to the pads at that frame).
//       Prints goals, special balls seen (same test as AutoPlay), and every special written to $050F with its shooter.
//       With CLIP=from:to:dir in the environment it also dumps those match frames (256x240 RGBA) and the sound (f32, 48 kHz).
//
//   cpuvscpu rom onekey <movie.fm2> <count> [maxFrame] [minFrame]
//       The space key in real play: replays the movie (e.g. the league walkthrough, a person playing player 0) and finds
//       moments when player 0 has the ball in open play; for each of <count> of them, replays from power-on to there,
//       "presses space", and watches whether a special ball shows up within 130 frames.  The movie's own buttons go on
//       meanwhile (except while the key is pressing B), so a pass or jump the player started himself can still get in the way.
//
//   cpuvscpu rom pause
//       Spectating, does Start still pause and unpause the match?
//
//   cpuvscpu rom showcase <team: 0 home team, 3 Argentina ...> <startFrame> <frames> [spectate 0/1]
//       The Y key: versus match <team> v Germany, the pads idle (or spectating); at match frame <startFrame> the showcase
//       (all 24 specials) starts.  Prints every special written to $050F with its shooter and the showcase's notes, and whether the teams
//       are handed back afterwards (who is on the pads, whose bit 7 is set).  SPECT_FROM=<match frame>: when spectating starts.
using System; using System.IO; using System.Text; using System.Collections.Generic; using NesCore; using Soccer;
static class CpuVsCpu {
  const byte A = 1, B = 2, T = 8, U = 16, D = 32, L = 64, R = 128;
  static List<byte> p1 = new List<byte>(), p2 = new List<byte>();
  static void Wait(int n) { for (int i = 0; i < n; i++) { p1.Add(0); p2.Add(0); } }
  static void Tap(byte a, byte b) { for (int i = 0; i < 6; i++) { p1.Add(a); p2.Add(b); } Wait(14); }
  static void Menu() { Menu(3); }
  static void Menu(int team) {
    Wait(300); Tap(T, 0); Wait(120); Tap(T, 0); Wait(90);
    Tap(D, 0); Tap(A, 0); Tap(A, 0); Wait(40); Tap(A, 0); Wait(60);
    for (int i = 0; i < team; i++) Tap(D, 0);
    Tap(A, 0); Tap(0, D); Tap(0, D); Tap(0, A); Wait(60);
    for (int i = 0; i < 3; i++) Tap(R, 0);
    Tap(D, 0); Tap(D, 0);
    Wait(10); Tap(A, 0); Wait(40); Tap(T, 0); Wait(90); Tap(T, 0); Wait(90); Tap(T, 0); Wait(90); Tap(T, 0); Wait(60);
  }
  static bool IsSpecial(Tick t) {
    var o = t.Objects[12]; bool special = o.PieceCount != 4 && o.PieceCount != 2;
    for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) { int low = t.Pieces[k].Tile & 63; if (!(low == 3 || low == 4 || low == 5 || low == 0x13 || low == 0x14 || low == 0x15)) special = true; }
    return special;
  }
  // the pads of an FCEUX .fm2 ("RLDUTSBA" per pad; Assets/Scripts/Game/Movie.cs needs Unity, so a copy here)
  sealed class Fm2 { public List<byte> Pad1 = new List<byte>(), Pad2 = new List<byte>(); public int Length { get { return Pad1.Count; } } }
  static byte Buttons(string s) { byte b = 0; for (int k = 0; k < 8 && k < s.Length; k++) if (s[k] != '.' && s[k] != ' ') b |= (byte)(0x80 >> k); return b; }
  static Fm2 Load(string f) {
    var m = new Fm2();
    foreach (var l in File.ReadAllLines(f)) { if (l.Length == 0 || l[0] != '|') continue; var ps = l.Split('|'); m.Pad1.Add(Buttons(ps.Length > 2 ? ps[2] : "")); m.Pad2.Add(Buttons(ps.Length > 3 ? ps[3] : "")); }
    return m;
  }

  static int Match(string rom, string[] a) {
    int frames = int.Parse(a[2]), spectFrom = int.Parse(a[3]); bool all = a[4] == "1";
    Fm2 mv = a.Length > 5 ? Load(a[5]) : null;
    string st = Environment.GetEnvironmentVariable("SPECT_TO"); int spectTo = st != null ? int.Parse(st) : int.MaxValue;     // hand the teams back at this frame
    Menu(); int menu = p1.Count;
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(rom)); var r = nes.Ram;
    var br = new SoccerBridge(nes); var tw = new Tweaks(nes); tw.AllSpecials = all;
    string clip = Environment.GetEnvironmentVariable("CLIP"); int c0 = -1, c1 = -1; Stream raw = null, snd = null;
    if (clip != null) { var cp = clip.Split(':'); c0 = int.Parse(cp[0]); c1 = int.Parse(cp[1]); Directory.CreateDirectory(cp[2]);
      raw = File.Create(Path.Combine(cp[2], "video.rgba")); snd = File.Create(Path.Combine(cp[2], "audio.f32")); nes.Apu.SampleRate = 48000; }
    var supers = new StringBuilder(); int superWrites = 0;
    nes.RamWriteHook += (ad, v) => { if (ad == 0x50F && (v & 0x80) == 0 && v != 0) { superWrites++; if (superWrites <= 30) supers.AppendFormat("{0:X2}@{1}(by {2:X2}) ", v, nes.FrameCount - menu, r[0x4D6]); } };
    int lastSub = -1, goals = 0, specials = 0, specialStart = 0, specialRun = 0; bool lastSpecial = false;
    var ev = new StringBuilder(); var subs = new StringBuilder();
    for (int f = 1; f <= menu + frames; f++) {
      int m = f - menu;
      byte q1, q2;
      if (f <= menu) { q1 = p1[f - 1]; q2 = p2[f - 1]; }
      else if (mv != null && f - 1 < mv.Length) { q1 = mv.Pad1[f - 1]; q2 = mv.Pad2[f - 1]; }
      else { q1 = 0; q2 = 0; }
      tw.Spectate = spectFrom > 0 && m >= spectFrom && m < spectTo;
      tw.BeforeFrame(ref q1, ref q2);
      nes.Pad1 = q1; nes.Pad2 = q2; nes.RunFrame();
      if (raw != null && m >= c0 && m < c1) {
        var fb = nes.Ppu.FrameBuffer; var bb = new byte[fb.Length * 4]; Buffer.BlockCopy(fb, 0, bb, 0, bb.Length); raw.Write(bb, 0, bb.Length);
        var ab = new byte[nes.Apu.OutCount * 4]; Buffer.BlockCopy(nes.Apu.Out, 0, ab, 0, ab.Length); snd.Write(ab, 0, ab.Length);
      }
      nes.Apu.OutCount = 0;
      bool tick = br.Capture();
      if (m < 1) continue;
      int sub = r[0x59];
      if (sub != lastSub) {
        if (subs.Length < 1000) subs.AppendFormat("{0}:{1:X2}/{2:X2}/{3:X2} ", m, r[0x58], sub, r[0x5C]);
        if (sub == 3 && lastSub == 2 && r[0x58] == 0) { goals++; ev.AppendFormat(" GOAL@{0}", m); }
        lastSub = sub;
      }
      if (!tick || !br.InMatch) continue;
      bool special = IsSpecial(br.Cur);
      if (special && !lastSpecial) { specialStart = m; specialRun = 0; }
      if (special) specialRun++;
      if (!special && lastSpecial && specialRun >= 5) { specials++; ev.AppendFormat(" sp@{0}+{1}", specialStart, specialRun); }
      lastSpecial = special;
    }
    if (raw != null) { raw.Close(); snd.Close(); }
    Console.WriteLine("spectate from {0}, all specials {1}, {2} frames{3} | goals {4} specials seen {5} special shots {6}",
      spectFrom, all, frames, mv != null ? " (pads from movie)" : " (idle pads)", goals, specials, superWrites);
    Console.WriteLine("  events:" + ev);
    Console.WriteLine("  specials: " + supers);
    Console.WriteLine("  screens ($58/$59/$5C): " + subs);
    Console.WriteLine("  end: team {0:X2},{1:X2} pads {2:X2},{3:X2} ctl {4:X2} {5:X2}", r[0x52C], r[0x52D], r[0x568], r[0x569], r[0x650], r[0x651]);
    return 0;
  }

  static int OneKey(string rom, string[] a) {
    var mv = Load(a[2]); int count = int.Parse(a[3]); int maxF = a.Length > 4 ? int.Parse(a[4]) : 40000, minF = a.Length > 5 ? int.Parse(a[5]) : 0;
    // 1) where does player 0 have the ball in open play?
    var cand = new List<int>();
    { var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(rom)); var r = nes.Ram; int last = -100000, run = 0;
      for (int f = 1; f <= Math.Min(maxF, mv.Length); f++) {
        nes.Pad1 = mv.Pad1[f - 1]; nes.Pad2 = mv.Pad2[f - 1]; nes.RunFrame(); nes.Apu.OutCount = 0;
        bool has = r[0x58] == 0 && r[0x59] == 2 && r[0x568] == 0 && (r[0x52A] & 0x80) != 0 && r[0x4D6] == 0;
        run = has ? run + 1 : 0;
        if (run == 6 && f - last > 600 && f >= minF) { cand.Add(f); last = f; }      // held it for a few frames; moments at least 10 s apart
      } }
    Console.WriteLine("player 0 has the ball at: " + string.Join(" ", cand));
    // 2) press space there
    int hits = 0, tried = 0, gotBall = 0; var sb = new StringBuilder();
    foreach (int at in cand) {
      if (tried >= count) break; tried++;
      var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(rom)); var r = nes.Ram;
      var br = new SoccerBridge(nes); var tw = new Tweaks(nes);
      int seen = -1, shooter = -1, superNo = -1; long shotFrame = -1;
      var writes = new StringBuilder();
      nes.RamWriteHook += (ad, v) => {
        if (ad != 0x50F || nes.FrameCount < at) return;
        if (writes.Length < 60) writes.AppendFormat("{0:X2} ", v);
        if ((v & 0x80) == 0 && v != 0 && superNo < 0) { superNo = v; shooter = r[0x4D6]; shotFrame = nes.FrameCount; } };
      for (int f = 1; f <= at + 130; f++) {
        if (f == at) tw.Press();
        byte q1 = f - 1 < mv.Length ? mv.Pad1[f - 1] : (byte)0, q2 = f - 1 < mv.Length ? mv.Pad2[f - 1] : (byte)0;
        tw.BeforeFrame(ref q1, ref q2);
        nes.Pad1 = q1; nes.Pad2 = q2; nes.RunFrame(); nes.Apu.OutCount = 0;
        if (br.Capture() && f > at && br.InMatch && seen < 0 && IsSpecial(br.Cur)) seen = f - at;
      }
      if (tw.Message == "必杀射门！") gotBall++;
      bool ok = seen >= 0 && superNo > 0 && shooter == 0;
      if (ok) hits++;
      sb.AppendFormat("  frame {0}: note \"{1}\", $050F after the key: {2}| special #{3:X2} by {4:X2} {5} frames after the key, special ball on screen after {6} frames -> {7}\n",
        at, tw.Message, writes, superNo, shooter, superNo > 0 ? shotFrame - at : -1, seen, ok ? "OK" : "no");
    }
    Console.Write(sb);
    Console.WriteLine("space pressed {0} times: player shot {1} times, special shot by player 0 {2} times", tried, gotBall, hits);
    return 0;
  }

  static int Pause(string rom) {
    Menu(); int menu = p1.Count;
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(rom)); var r = nes.Ram; var tw = new Tweaks(nes);
    var sb = new StringBuilder(); int last5C = -1;
    for (int f = 1; f <= menu + 1400; f++) {
      int m = f - menu; byte q1 = f <= menu ? p1[f - 1] : (byte)0, q2 = f <= menu ? p2[f - 1] : (byte)0;
      if (m == 600 || m == 900) q1 = T;                               // Start held one frame... and again later
      if (m == 601 || m == 602 || m == 901 || m == 902) q1 = T;
      tw.Spectate = m >= 1;
      tw.BeforeFrame(ref q1, ref q2);
      nes.Pad1 = q1; nes.Pad2 = q2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (m >= 1 && r[0x5C] != last5C) { sb.AppendFormat("{0}:$5C={1:X2}/$59={2:X2} ", m, r[0x5C], r[0x59]); last5C = r[0x5C]; }
    }
    Console.WriteLine("spectating, Start at match frames 600 and 900: " + sb);
    return 0;
  }

  static int Main(string[] a) {
    Console.OutputEncoding = Encoding.UTF8;
    if (a[1] == "match") return Match(a[0], a);
    if (a[1] == "onekey") return OneKey(a[0], a);
    if (a[1] == "pause") return Pause(a[0]);
    if (a[1] == "showcase") return Showcase(a[0], a);
    Console.WriteLine("modes: match, onekey, pause, showcase"); return 1;
  }

  static int Showcase(string rom, string[] a) {
    int team = int.Parse(a[2]), start = int.Parse(a[3]), frames = int.Parse(a[4]); bool spect = a.Length > 5 && a[5] == "1";
    p1.Clear(); p2.Clear(); Menu(team); int menu = p1.Count;
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(rom)); var r = nes.Ram;
    var tw = new Tweaks(nes);
    string sf = Environment.GetEnvironmentVariable("SPECT_FROM"); int spectFrom = sf != null ? int.Parse(sf) : 1;     // (match frame from which spectating is on)
    string tr = Environment.GetEnvironmentVariable("TRACE"); int t0 = -1, t1 = -1; if (tr != null) { var tp = tr.Split(':'); t0 = int.Parse(tp[0]); t1 = int.Parse(tp[1]); }
    tw.Trace = s => { long mm = nes.FrameCount - menu; if (mm >= t0 && mm <= t1) Console.WriteLine("    {0} {1}", mm, s); };
    string lastMsg = null; bool wasShowing = false; int ended = -1;
    nes.RamWriteHook += (ad, v) => { if (ad == 0x50F && (v & 0x80) == 0 && v != 0 && v <= 0x21) Console.WriteLine("  special {0:X2} by player {1:X2} at {2}", v, r[0x4D6], nes.FrameCount - menu); };
    for (int f = 1; f <= menu + frames; f++) {
      int m = f - menu; byte q1 = f <= menu ? p1[f - 1] : (byte)0, q2 = f <= menu ? p2[f - 1] : (byte)0;
      if (spect && m == spectFrom) tw.Spectate = true;
      if (m == start) { tw.Showcase(); var sb0 = new StringBuilder(); for (int i = 0; i < 12; i++) sb0.AppendFormat("{0:X2} ", r[0x532 + i]); Console.WriteLine("characters $0532: " + sb0); }
      tw.BeforeFrame(ref q1, ref q2);
      nes.Pad1 = q1; nes.Pad2 = q2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (tw.Message != lastMsg) { lastMsg = tw.Message; if (m > 0) Console.WriteLine("{0}: {1}", m, lastMsg); }
      if (wasShowing && !tw.Showing) ended = m;
      wasShowing = tw.Showing;
    }
    var sb = new StringBuilder();
    for (int i = 0; i < 12; i++) sb.Append((r[0x650 + i] & 0x80) != 0 ? "C" : "p");
    var mem = new StringBuilder(); for (int i = 0; i < 12; i++) mem.AppendFormat("{0:X2} ", r[0x532 + i]); Console.WriteLine("members $0532 at the end: " + mem);
    Console.WriteLine("ended at {0}; pads {1:X2} {2:X2} {3:X2} {4:X2}; teams {5:X2} {6:X2}; players (C = computer, p = pad) {7}; screen {8:X2}/{9:X2}",
      ended, r[0x568], r[0x569], r[0x56A], r[0x56B], r[0x52C], r[0x52D], sb, r[0x58], r[0x59]);
    return 0;
  }
}
