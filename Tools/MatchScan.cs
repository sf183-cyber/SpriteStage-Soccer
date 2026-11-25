// A whole match played through the core the way SoccerTools.MovieVideo plays it (the movie's pads, then idle pads;
// both teams the computer's from a chosen movie frame), with every player's state written down frame by frame,
// so that a highlight reel can be cut from it (Tools/reels/cpu_match_reel.py finds the moments in the log).
//
//   Tools/build_test.sh /tmp/scan.exe Tools/MatchScan.cs
//   mono /tmp/scan.exe ../soccer.nes <movie.fm2> <spectateFrom> <lastFrame> <allSpecials 0/1 | p:f1,f2,...> <log.bin> [dumpDir f1,f2,...]
//   (p:... = no all-specials, but the B key - Tweaks.Press(), "the next shot is a special" - at those movie frames, as -pressAt)
//
// log.bin: one record of 12 + 13*8 bytes per movie frame (frame 1 first):
//   $58 $59 holder($04D6) special-written-this-frame($050F, 0 = none) control($052A) control($052B) camX(2) camY(2) music($055A) 0
//   then for objects 0..12 (12 = the ball): x lo, x hi, y, z, movement($0459+i), state($0486+i), $0399+i (height, high byte), y high byte ($0360+i)
// With dumpDir the 256x240 pictures of the listed frames are saved as .rgba (Tools/rgba2png.py turns them into PNG).
using System; using System.IO; using System.Collections.Generic; using NesCore; using Soccer;
static class MatchScan {
  static byte Buttons(string s) { byte b = 0; for (int k = 0; k < 8 && k < s.Length; k++) if (s[k] != '.' && s[k] != ' ') b |= (byte)(0x80 >> k); return b; }
  static int Main(string[] a) {
    var p1 = new List<byte>(); var p2 = new List<byte>();
    foreach (var l in File.ReadAllLines(a[1])) { if (l.Length == 0 || l[0] != '|') continue; var ps = l.Split('|'); p1.Add(Buttons(ps.Length > 2 ? ps[2] : "")); p2.Add(Buttons(ps.Length > 3 ? ps[3] : "")); }
    int from = int.Parse(a[2]), last = int.Parse(a[3]); bool all = a[4] == "1";
    var press = new HashSet<int>(); if (a[4].StartsWith("p:")) foreach (var s in a[4].Substring(2).Split(',')) if (s.Length > 0) press.Add(int.Parse(s));
    var want = new HashSet<int>(); string dump = a.Length > 6 ? a[6] : null;
    if (dump != null) { Directory.CreateDirectory(dump); foreach (var s in a[7].Split(',')) if (s.Length > 0) want.Add(int.Parse(s)); }
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0])); var r = nes.Ram;
    var br = new SoccerBridge(nes); var tw = new Tweaks(nes); tw.AllSpecials = all;
    int special = 0;
    nes.RamWriteHook += (ad, v) => { if (ad == 0x50F && (v & 0x80) == 0 && v != 0) special = v; };
    var log = new BinaryWriter(File.Create(a[5]));
    int specials = 0;
    for (int f = 1; f <= last; f++) {
      byte q1 = f - 1 < p1.Count ? p1[f - 1] : (byte)0, q2 = f - 1 < p2.Count ? p2[f - 1] : (byte)0;
      tw.AllSpecials = all; if (from > 0) tw.Spectate = f >= from; if (press.Contains(f)) tw.Press();     // (as SoccerTools.MovieTweaks)
      special = 0;
      tw.BeforeFrame(ref q1, ref q2);
      nes.Pad1 = q1; nes.Pad2 = q2; nes.RunFrame(); nes.Apu.OutCount = 0;
      br.Capture();
      if (special != 0) specials++;
      int cx = br.Cur.CamX, cy = br.Cur.CamY;
      log.Write(new byte[] { r[0x58], r[0x59], r[0x4D6], (byte)special, r[0x52A], r[0x52B], (byte)cx, (byte)(cx >> 8), (byte)cy, (byte)(cy >> 8), r[0x55A], 0 });
      for (int i = 0; i < 13; i++) {
        int x = r[0x314 + i] | r[0x327 + i] << 8;
        log.Write(new byte[] { (byte)x, (byte)(x >> 8), r[0x34D + i], r[0x386 + i], r[0x459 + i], r[0x486 + i], r[0x399 + i], r[0x360 + i] });
      }
      if (want.Contains(f)) { var fb = nes.Ppu.FrameBuffer; var b = new byte[fb.Length * 4]; Buffer.BlockCopy(fb, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(dump, "f" + f.ToString("D6") + ".rgba"), b); }
    }
    log.Close();
    Console.WriteLine("{0} frames, spectate from {1}, all specials {2}, special shots {3}", last, from, all, specials);
    return 0;
  }
}
