// usage: spritedump rom movie.fm2 outDir from to   -> every distinct player sprite seen (64x64 RGBA raw) + index.txt
using System; using System.IO; using System.Text; using System.Collections.Generic; using NesCore; using Soccer;
static class SpriteDump {
  static int Main(string[] a) {
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
    var lines = new List<string>(); foreach (var l in File.ReadAllLines(a[1])) if (l.StartsWith("|")) lines.Add(l);
    Directory.CreateDirectory(a[2]); int from = int.Parse(a[3]), to = int.Parse(a[4]);
    var br = new SoccerBridge(nes); var seen = new Dictionary<string, int[]>(); var idx = new StringBuilder(); int n = 0;
    for (int f = 1; f <= to && f <= lines.Count; f++) {
      byte p1 = 0, p2 = 0; var ps = lines[f - 1].Split('|'); string s1 = ps[2], s2 = ps.Length > 3 ? ps[3] : "";
      for (int k = 0; k < 8 && k < s1.Length; k++) if (s1[k] != '.' && s1[k] != ' ') p1 |= (byte)(0x80 >> k);
      for (int k = 0; k < 8 && k < s2.Length; k++) if (s2[k] != '.' && s2[k] != ' ') p2 |= (byte)(0x80 >> k);
      nes.Pad1 = p1; nes.Pad2 = p2; nes.RunFrame(); nes.Apu.OutCount = 0;
      bool tick = br.Capture();
      if (!tick || f < from || !br.InMatch) continue;
      var t = br.Cur; var pal = nes.Ppu.Palette;
      for (int i = 0; i < 12; i++) {
        var o = t.Objects[i]; if (!o.Active || o.PieceCount == 0) continue;
        var sb = new StringBuilder();
        for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) { var p = t.Pieces[k]; sb.AppendFormat("{0},{1},{2:X},{3:X};", p.Dx, p.Top, p.Tile, p.Attr); }
        for (int c = 16; c < 32; c++) sb.Append(pal[c].ToString("X") + ".");
        string key = sb.ToString(); int[] e;
        if (seen.TryGetValue(key, out e)) { e[1]++; continue; }
        seen[key] = new int[] { n, 1 };
        var img = new uint[64 * 64];
        for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) {
          var p = t.Pieces[k]; int bx = 32 + p.Dx, by = 56 - p.Top; int off = p.Tile * 16;
          for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) {
            int sx = (p.Attr & 0x40) != 0 ? 7 - x : x, sy = (p.Attr & 0x80) != 0 ? 7 - y : y;
            int c = ((nes.Chr[off + sy] >> (7 - sx)) & 1) | (((nes.Chr[off + 8 + sy] >> (7 - sx)) & 1) << 1);
            if (c == 0) continue; int X = bx + x, Y = by + y; if (X < 0 || X >= 64 || Y < 0 || Y >= 64) continue;
            img[Y * 64 + X] = pal[16 + (p.Attr & 3) * 4 + c] | 0xFF000000u;
          } }
        var b = new byte[img.Length * 4]; Buffer.BlockCopy(img, 0, b, 0, b.Length);
        File.WriteAllBytes(Path.Combine(a[2], "s" + n.ToString("D4") + ".rgba"), b);
        idx.AppendFormat("{0} f{1} slot{2} z{3} pieces{4}\n", n, f, i, o.Z, o.PieceCount); n++;
      } }
    var cnt = new int[n]; foreach (var e in seen.Values) cnt[e[0]] = e[1];
    var sbc = new StringBuilder(); for (int i = 0; i < n; i++) sbc.Append(cnt[i] + "\n");
    File.WriteAllText(Path.Combine(a[2], "index.txt"), idx.ToString()); File.WriteAllText(Path.Combine(a[2], "count.txt"), sbc.ToString());
    Console.WriteLine("distinct " + n); return 0; } }
