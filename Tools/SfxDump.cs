// usage: sfxdump <folder> [rate]   -> the special-shot sounds (Assets/Scripts/Game/Sfx.cs) as .wav files, and how long / loud each is
using System; using System.IO; using Soccer;
static class SfxDump {
  static void Wav(string path, float[] a, int rate) {
    using (var w = new BinaryWriter(File.Create(path))) {
      w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + a.Length * 2); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
      w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
      w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(a.Length * 2);
      foreach (var s in a) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, s)) * 32767)); } }
  static int Main(string[] a) {
    int rate = a.Length > 1 ? int.Parse(a[1]) : 48000; Directory.CreateDirectory(a[0]);
    var sw = System.Diagnostics.Stopwatch.StartNew(); var clips = Sfx.Clips(rate);
    Console.WriteLine("built in {0} ms", sw.ElapsedMilliseconds);
    for (int i = 0; i < Sfx.Count; i++) {
      float peak = 0; double sum = 0; foreach (var s in clips[i]) { peak = Math.Max(peak, Math.Abs(s)); sum += s * s; }
      Console.WriteLine("{0,-8} {1:F2} s   peak {2:F2}   rms {3:F3}   first {4:F4} last {5:F4}", Sfx.Names[i], clips[i].Length / (double)rate, peak, Math.Sqrt(sum / clips[i].Length), clips[i][0], clips[i][clips[i].Length - 1]);
      Wav(Path.Combine(a[0], Sfx.Names[i] + ".wav"), clips[i], rate); }
    return 0; } }
