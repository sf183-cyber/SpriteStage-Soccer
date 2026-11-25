// usage: framedump rom movie.fm2 outDir f1 f2 ...   (raw 256x240 frames as .rgba)
using System; using System.IO; using System.Collections.Generic; using NesCore;
static class FrameDump {
  static int Main(string[] a) {
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
    var lines = new List<string>(); foreach (var l in File.ReadAllLines(a[1])) if (l.StartsWith("|")) lines.Add(l);
    Directory.CreateDirectory(a[2]);
    var want = new HashSet<int>(); int last = 0; for (int i = 3; i < a.Length; i++) { int v = int.Parse(a[i]); want.Add(v); last = Math.Max(last, v); }
    for (int f = 1; f <= last; f++) {
      byte p1 = 0, p2 = 0; int li = f - 1;
      if (li < lines.Count) { var ps = lines[li].Split('|'); string s1 = ps[2], s2 = ps.Length > 3 ? ps[3] : "";
        for (int k = 0; k < 8 && k < s1.Length; k++) if (s1[k] != '.' && s1[k] != ' ') p1 |= (byte)(0x80 >> k);
        for (int k = 0; k < 8 && k < s2.Length; k++) if (s2[k] != '.' && s2[k] != ' ') p2 |= (byte)(0x80 >> k); }
      nes.Pad1 = p1; nes.Pad2 = p2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (want.Contains(f)) { var fb = nes.Ppu.FrameBuffer; var b = new byte[fb.Length * 4]; Buffer.BlockCopy(fb, 0, b, 0, b.Length); File.WriteAllBytes(Path.Combine(a[2], "f" + f.ToString("D6") + ".rgba"), b); }
    }
    return 0; } }
