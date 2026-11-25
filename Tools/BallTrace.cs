// usage: balltrace rom movie.fm2 from to   -> per tick: frame, sub-screen, weather, ball east / north / height (metres), pieces
using System; using System.IO; using System.Collections.Generic; using NesCore; using Soccer;
static class BallTrace {
  static int Main(string[] a) {
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
    var lines = new List<string>(); foreach (var l in File.ReadAllLines(a[1])) if (l.StartsWith("|")) lines.Add(l);
    int from = int.Parse(a[2]), to = int.Parse(a[3]);
    var br = new SoccerBridge(nes);
    const double KX = 4.0 / 88.0, KY = 4.0 / (88.0 * 0.6427876), KP = 3.515e-4;
    for (int f = 1; f <= to && f <= lines.Count; f++) {
      byte p1 = 0, p2 = 0; var ps = lines[f - 1].Split('|'); string s1 = ps[2], s2 = ps.Length > 3 ? ps[3] : "";
      for (int k = 0; k < 8 && k < s1.Length; k++) if (s1[k] != '.' && s1[k] != ' ') p1 |= (byte)(0x80 >> k);
      for (int k = 0; k < 8 && k < s2.Length; k++) if (s2[k] != '.' && s2[k] != ' ') p2 |= (byte)(0x80 >> k);
      nes.Pad1 = p1; nes.Pad2 = p2; nes.RunFrame(); nes.Apu.OutCount = 0;
      bool tick = br.Capture();
      if (!tick || f < from || !br.InMatch) continue;
      var o = br.Cur.Objects[12];
      double x = (o.X - 512.0) / (1.0 + KP * (o.Y - 50.0)) * KX, y = (197.5 - o.Y) * KY, z = Math.Max(o.Z, 0) * KX;
      Console.WriteLine("{0} sub{1} w{2} e{3,7:F2} n{4,6:F2} h{5,5:F2} pieces{6} active{7}", f, nes.Ram[0x59], br.Cur.Weather, x, y, z, o.PieceCount, o.Active ? 1 : 0);
    }
    return 0; } }
