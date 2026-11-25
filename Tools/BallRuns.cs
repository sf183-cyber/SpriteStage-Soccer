// usage: ballruns rom movie.fm2 lastFrame  -> every run of ticks in which the ball is not the plain ball: first frame, length (frames), colours, tiles
using System; using System.IO; using System.Text; using System.Collections.Generic; using NesCore; using Soccer;
static class BallRuns {
  static int Main(string[] a) {
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
    var lines = new List<string>(); foreach (var l in File.ReadAllLines(a[1])) if (l.StartsWith("|")) lines.Add(l);
    var br = new SoccerBridge(nes); int last = int.Parse(a[2]);
    int start = -1, prevF = 0; var tiles = new SortedSet<string>(); var cols = new SortedSet<string>(); int z0 = 0, weather = 0;
    for (int f = 1; f <= lines.Count && f <= last; f++) {
      byte p1 = 0, p2 = 0; var ps = lines[f - 1].Split('|'); string s1 = ps[2], s2 = ps.Length > 3 ? ps[3] : "";
      for (int k = 0; k < 8 && k < s1.Length; k++) if (s1[k] != '.' && s1[k] != ' ') p1 |= (byte)(0x80 >> k);
      for (int k = 0; k < 8 && k < s2.Length; k++) if (s2[k] != '.' && s2[k] != ' ') p2 |= (byte)(0x80 >> k);
      nes.Pad1 = p1; nes.Pad2 = p2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (!br.Capture() || !br.InMatch) continue;
      var t = br.Cur; var o = t.Objects[12]; var pal = nes.Ppu.Palette;
      bool special = o.Active && o.PieceCount > 0 && o.PieceCount != 4 && o.PieceCount != 2;
      var ts = new List<string>(); var cs = new List<string>();
      for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) { var p = t.Pieces[k]; int low = p.Tile & 63;
        if (!(low == 3 || low == 4 || low == 5 || low == 0x13 || low == 0x14 || low == 0x15)) special = true;
        ts.Add(low.ToString("X2")); int pi = 0x11 + (p.Attr & 3) * 4; cs.Add(pal[pi].ToString("X2") + pal[pi + 1].ToString("X2") + pal[pi + 2].ToString("X2")); }
      if (!(o.Active && o.PieceCount > 0)) special = false;
      if (special) { if (start < 0) { start = f; tiles.Clear(); cols.Clear(); z0 = o.Z; weather = t.Weather; } foreach (var s in ts) tiles.Add(s); foreach (var s in cs) cols.Add(s); }
      else if (start >= 0) { Console.WriteLine("f{0} len{1} z{2} w{3} pal[{4}] tiles[{5}]", start, prevF - start + 4, z0, weather, string.Join(",", cols), string.Join(",", tiles)); start = -1; }
      prevF = f;
    }
    return 0; } }
