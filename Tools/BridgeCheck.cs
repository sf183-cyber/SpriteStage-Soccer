// Checks the bridge against the game itself: for every tick, the sprites obtained by calling the game's composer
// on the side must be exactly the ones the game wrote into its own sprite table - except those the original leaves
// out because it has run out of its 64 hardware sprites.   usage: BridgeCheck soccer.nes <frames>
using System;
using System.IO;
using System.Collections.Generic;
using NesCore;
using Soccer;

static class BridgeCheck
{
    static byte PadFor(int g)
    {
        byte pad = 0;
        if (g % 40 < 4 && g < 600) pad |= 8;
        if (g > 640)
        {
            int ph = (g / 90) % 6;
            if (ph == 0) pad |= 0x80; else if (ph == 1) pad |= 0x90; else if (ph == 2) pad |= 0x40; else if (ph == 3) pad |= 0x60; else if (ph == 4) pad |= 0xA0; else pad |= 0x10;
            if (g % 50 < 3) pad |= 1;
            if (g % 77 < 3) pad |= 2;
        }
        return pad;
    }

    static int Main(string[] a)
    {
        var nes = new Nes();
        if (!nes.LoadRom(File.ReadAllBytes(a[0]))) { Console.WriteLine("bad rom"); return 1; }
        var br = new SoccerBridge(nes);
        int target = int.Parse(a[1]);
        var R = nes.Ram;
        long ok = 0, miss = 0, missFull = 0, ticks = 0, maxPieces = 0, matchFrames = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int f = 1; f <= target; f++)
        {
            nes.Pad1 = PadFor(f);
            nes.RunFrame();
            nes.Apu.OutCount = 0;
            bool tick = br.Capture();
            if (br.InMatch) matchFrames++;
            if (!tick) continue;
            ticks++;
            var t = br.Cur; var S = t.Ram;
            maxPieces = Math.Max(maxPieces, t.Pieces.Count);
            // what the game itself put into its sprite table during this frame
            var real = new HashSet<long>();
            int used = 0;
            for (int s = 0; s < 64; s++) { int b = 0x200 + s * 4; if (R[b] < 0xEF) { used++; real.Add(((long)R[b] << 24) | ((long)R[b + 1] << 16) | ((long)R[b + 2] << 8) | R[b + 3]); } }
            bool full = used >= 52;         // weather keeps 12 slots: from here on the original starts dropping sprites
            int n = S[0xF4];
            for (int e = 0; e < n; e++)
            {
                int type = S[0xA1 + e] & 0xE0, i = S[0xA1 + e] & 0x1F;
                if (type == 0x20 || type == 0x40 || i >= SoccerBridge.Slots) continue;       // the original's blob shadows: not used in 3D
                var o = t.Objects[i];
                int sx = S[0x3AF + i], sy = S[0x3C2 + i], gy = S[0x3D5 + i];
                for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++)
                {
                    var p = t.Pieces[k];
                    int x = sx + p.Dx, y = (o.Z >= 0 ? sy : gy) - p.Top;
                    if (x < 0 || x > 255 || y < 0 || y >= S[0x56]) continue;                // outside the original's view
                    int tl = -1; for (int q = 0; q < 4; q++) if ((nes.FieldChrMap[4 + q] >> 10) == p.Tile / 64) tl = q * 64 + p.Tile % 64;
                    int attr = p.Attr | (type >= 0x60 ? 0x20 : 0);
                    long key = ((long)y << 24) | ((long)tl << 16) | ((long)attr << 8) | (long)x;
                    if (real.Contains(key)) ok++; else { miss++; if (full) missFull++; }
                }
            }
        }
        Console.WriteLine("{0} frames ({1} in a match), {2} ticks, {3} ms", target, matchFrames, ticks, sw.ElapsedMilliseconds);
        Console.WriteLine("sprite pieces identical to the game's own: {0}", ok);
        Console.WriteLine("pieces the game did not draw: {0}  (of which {1} in ticks where its sprite table was full)", miss, missFull);
        Console.WriteLine("most pieces in one tick: {0} (the original can show 64); side calls that failed: {1}", maxPieces, br.SandboxFailures);
        return 0;
    }
}
