// usage: ballforms rom movie.fm2   -> every distinct layout of the ball's pieces, how often, and a first frame
using System; using System.IO; using System.Text; using System.Collections.Generic; using NesCore; using Soccer;
static class BallForms {
  static int Main(string[] a) {
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
    var lines = new List<string>(); foreach (var l in File.ReadAllLines(a[1])) if (l.StartsWith("|")) lines.Add(l);
    var br = new SoccerBridge(nes); var seen = new Dictionary<string, int[]>();
    for (int f = 1; f <= lines.Count; f++) {
      byte p1 = 0, p2 = 0; var ps = lines[f - 1].Split('|'); string s1 = ps[2], s2 = ps.Length > 3 ? ps[3] : "";
      for (int k = 0; k < 8 && k < s1.Length; k++) if (s1[k] != '.' && s1[k] != ' ') p1 |= (byte)(0x80 >> k);
      for (int k = 0; k < 8 && k < s2.Length; k++) if (s2[k] != '.' && s2[k] != ' ') p2 |= (byte)(0x80 >> k);
      nes.Pad1 = p1; nes.Pad2 = p2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (!br.Capture() || !br.InMatch) continue;
      var t = br.Cur; var o = t.Objects[12]; var sb = new StringBuilder();
      sb.Append(o.PieceCount + ":");
      for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) { var p = t.Pieces[k]; sb.AppendFormat(" [{0},{1} t{2:X2} a{3:X2}]", p.Dx, p.Top, p.Tile & 63, p.Attr); }
      string key = sb.ToString(); int[] e;
      if (seen.TryGetValue(key, out e)) e[0]++; else seen[key] = new int[] { 1, f, o.Z };
    }
    var list = new List<KeyValuePair<string, int[]>>(seen); list.Sort((x, y) => y.Value[0].CompareTo(x.Value[0]));
    foreach (var kv in list) Console.WriteLine("{0,5}x  first f{1} z{2}   {3}", kv.Value[0], kv.Value[1], kv.Value[2], kv.Key);
    return 0; } }
