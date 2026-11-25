// usage: ballpal rom movie.fm2 frame  -> at the first tick at or after 'frame' with a plain 4-piece ball: its pieces, their palettes, and the 16x16 picture as colour indices
using System; using System.IO; using System.Text; using System.Collections.Generic; using NesCore; using Soccer;
static class BallPal {
  static int Main(string[] a) {
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
    var lines = new List<string>(); foreach (var l in File.ReadAllLines(a[1])) if (l.StartsWith("|")) lines.Add(l);
    var br = new SoccerBridge(nes); int want = int.Parse(a[2]); int shown = 0;
    for (int f = 1; f <= lines.Count; f++) {
      byte p1 = 0, p2 = 0; var ps = lines[f - 1].Split('|'); string s1 = ps[2], s2 = ps.Length > 3 ? ps[3] : "";
      for (int k = 0; k < 8 && k < s1.Length; k++) if (s1[k] != '.' && s1[k] != ' ') p1 |= (byte)(0x80 >> k);
      for (int k = 0; k < 8 && k < s2.Length; k++) if (s2[k] != '.' && s2[k] != ' ') p2 |= (byte)(0x80 >> k);
      nes.Pad1 = p1; nes.Pad2 = p2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (!br.Capture() || !br.InMatch || f < want) continue;
      var t = br.Cur; var o = t.Objects[12]; if (o.PieceCount != 4) continue;
      var pal = nes.Ppu.Palette;
      Console.Write("f{0} sprite palettes:", f); for (int c = 16; c < 32; c++) Console.Write(" {0:X2}", pal[c]); Console.WriteLine();
      var img = new string[16, 16]; for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) img[y, x] = "..";
      for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) {
        var p = t.Pieces[k]; Console.WriteLine("  piece dx{0} top{1} tile{2:X3} attr{3:X2} layer{4}", p.Dx, p.Top, p.Tile, p.Attr, p.Layer);
        int off = p.Tile * 16;
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) {
          int sx = (p.Attr & 0x40) != 0 ? 7 - x : x, sy = (p.Attr & 0x80) != 0 ? 7 - y : y;
          int c = ((nes.Chr[off + sy] >> (7 - sx)) & 1) | (((nes.Chr[off + 8 + sy] >> (7 - sx)) & 1) << 1);
          int X = 8 + p.Dx + x, Y = 16 - p.Top + y; if (c == 0 || X < 0 || X > 15 || Y < 0 || Y > 15) continue;
          img[Y, X] = pal[16 + (p.Attr & 3) * 4 + c].ToString("X2"); } }
      for (int y = 0; y < 16; y++) { var sb = new StringBuilder(); for (int x = 0; x < 16; x++) sb.Append(img[y, x] + " "); Console.WriteLine(sb); }
      if (++shown >= 2) break; want = f + 8;
    }
    return 0; } }
