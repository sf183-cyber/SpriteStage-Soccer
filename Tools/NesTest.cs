// Console harness: runs the core headless and dumps a RAM trace + raw frames for comparison with Mesen.
// usage: NesTest rom.nes frames outDir [inputShift] [shotEvery]
using System;
using System.IO;
using NesCore;

static class NesTest
{
    static int Main(string[] a)
    {
        var nes = new Nes();
        if (!nes.LoadRom(File.ReadAllBytes(a[0]))) { Console.WriteLine("bad rom"); return 1; }
        int frames = int.Parse(a[1]);
        string outDir = a[2];
        int shift = a.Length > 3 ? int.Parse(a[3]) : 0;
        int every = a.Length > 4 ? int.Parse(a[4]) : 120;
        int force = a.Length > 5 ? int.Parse(a[5]) : -1;
        Directory.CreateDirectory(outDir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var trace = new FileStream(Path.Combine(outDir, "ram.bin"), FileMode.Create))
        using (var shots = new FileStream(Path.Combine(outDir, "frames.bin"), FileMode.Create))
        using (var audio = new FileStream(Path.Combine(outDir, "audio_f32_48k.raw"), FileMode.Create))
        {
            byte[] ab = new byte[8192 * 4];
            byte[] buf = new byte[256 * 240 * 4];
            for (int f = 1; f <= frames; f++)
            {
                int g = f + shift;
                byte pad = 0;
                if (g % 40 < 4) pad |= (byte)Pad.Start;
                if (g > 640)
                {
                    int ph = (g / 90) % 6;
                    if (ph == 0) pad |= (byte)Pad.Right;
                    else if (ph == 1) pad |= (byte)(Pad.Up | Pad.Right);
                    else if (ph == 2) pad |= (byte)Pad.Left;
                    else if (ph == 3) pad |= (byte)(Pad.Down | Pad.Left);
                    else if (ph == 4) pad |= (byte)(Pad.Down | Pad.Right);
                    else pad |= (byte)Pad.Up;
                    if (g % 50 < 3) pad |= (byte)Pad.A;
                    if (g % 77 < 3) pad |= (byte)Pad.B;
                }
                nes.Pad1 = pad;
                nes.RunFrame();
                Buffer.BlockCopy(nes.Apu.Out, 0, ab, 0, nes.Apu.OutCount * 4); audio.Write(ab, 0, nes.Apu.OutCount * 4); nes.Apu.OutCount = 0;
                if (force >= 0) nes.Ram[0x5FB] = (byte)force;
                trace.Write(nes.Ram, 0, 0x800);
                if (f % every == 0)
                {
                    Buffer.BlockCopy(nes.Ppu.FrameBuffer, 0, buf, 0, buf.Length);
                    shots.Write(buf, 0, buf.Length);
                }
            }
        }
        Console.WriteLine("ran " + frames + " frames in " + sw.ElapsedMilliseconds + " ms; scr=" + nes.Ram[0x58].ToString("X2") + " sub=" + nes.Ram[0x59].ToString("X2"));
        return 0;
    }
}
