// Plays a versus match (Argentina v Germany) with two random "players" and reports what happened.
//   autoplay rom seed frames region(0-3) ground(0-2) condition(0-3) [out.fm2]
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using NesCore;
using Soccer;
static class AutoPlay
{
    const byte A = 1, B = 2, T = 8, U = 16, D = 32, L = 64, R = 128;
    static List<byte> p1 = new List<byte>(), p2 = new List<byte>();
    static void Wait(int n) { for (int i = 0; i < n; i++) { p1.Add(0); p2.Add(0); } }
    static void Tap(byte a, byte b) { for (int i = 0; i < 6; i++) { p1.Add(a); p2.Add(b); } Wait(14); }
    static void Menu(int region, int ground, int cond)
    {
        Wait(300); Tap(T, 0); Wait(120); Tap(T, 0); Wait(90);
        Tap(D, 0); Tap(A, 0); Tap(A, 0); Wait(40); Tap(A, 0); Wait(60);           // versus play, 2 players, default level
        Tap(D, 0); Tap(D, 0); Tap(D, 0); Tap(A, 0); Tap(0, D); Tap(0, D); Tap(0, A); Wait(60);     // Argentina, Germany
        for (int i = 0; i < region; i++) Tap(R, 0);
        Tap(D, 0); for (int i = 0; i < ground; i++) Tap(R, 0);
        Tap(D, 0); for (int i = 0; i < cond; i++) Tap(R, 0);
        Wait(10); Tap(A, 0); Wait(40); Tap(T, 0); Wait(90); Tap(T, 0); Wait(90); Tap(T, 0); Wait(90); Tap(T, 0); Wait(60);
    }
    // one random footballer: runs (often dashing) towards the other goal, shoots, passes, jumps and volleys
    sealed class Bot
    {
        readonly Random r; readonly byte fwd, back; byte dir; int dirLeft, tapPhase, act, actLeft, volleyIn = -1;
        public Bot(int seed, byte forward, byte backward) { r = new Random(seed); fwd = forward; back = backward; }
        public byte Next()
        {
            byte pad = 0;
            if (dirLeft <= 0)
            {
                int k = r.Next(100);
                dir = k < 42 ? fwd : k < 58 ? (byte)(fwd | U) : k < 74 ? (byte)(fwd | D) : k < 81 ? U : k < 88 ? D : k < 95 ? back : (byte)0;
                dirLeft = 18 + r.Next(60); tapPhase = (dir & (L | R)) != 0 && r.Next(100) < 60 ? 9 : 0;        // double tap = dash
            }
            dirLeft--;
            if (tapPhase > 0) { tapPhase--; pad |= (tapPhase >= 6 || tapPhase < 3) ? (byte)(dir & (L | R)) : (byte)0; }
            else pad |= dir;
            if (actLeft > 0) { actLeft--; pad |= (byte)act; }
            else if (volleyIn >= 0) { if (volleyIn-- == 0) { act = B; actLeft = 6; } }
            else
            {
                int k = r.Next(1000);
                if (k < 22) { act = B; actLeft = 4 + r.Next(8); }
                else if (k < 30) { act = A; actLeft = 4; }
                else if (k < 44) { act = A | B; actLeft = 5; if (r.Next(100) < 75) volleyIn = 8 + r.Next(10); }
            }
            return pad;
        }
    }
    static int Main(string[] a)
    {
        int seed = int.Parse(a[1]), frames = int.Parse(a[2]), region = int.Parse(a[3]), ground = int.Parse(a[4]), cond = int.Parse(a[5]);
        Menu(region, ground, cond);
        int menu = p1.Count;
        var b1 = new Bot(seed * 2 + 1, R, L); var b2 = new Bot(seed * 2 + 2, L, R);
        for (int i = 0; i < frames; i++) { p1.Add(b1.Next()); p2.Add(b2.Next()); }
        if (a.Length > 6)
        {
            var sb = new StringBuilder("version 3\nemuVersion 22020\nport0 1\nport1 1\ncomment generated: Argentina v Germany, seed " + seed + "\n");
            const string names = "RLDUTSBA";
            for (int i = 0; i < p1.Count; i++)
            {
                sb.Append("|0|");
                for (int k = 0; k < 8; k++) sb.Append((p1[i] & (0x80 >> k)) != 0 ? names[k] : '.');
                sb.Append('|');
                for (int k = 0; k < 8; k++) sb.Append((p2[i] & (0x80 >> k)) != 0 ? names[k] : '.');
                sb.Append("||\n");
            }
            File.WriteAllText(a[6], sb.ToString());
        }
        var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0]));
        var br = new SoccerBridge(nes); var R0 = nes.Ram;
        int lastSub = -1, lastW = 0, specialStart = 0, specialRun = 0, goals = 0, bursts = 0, bolts = 0, specials = 0, colour = 0; bool lastSpecial = false;
        var ev = new StringBuilder(); var weather = new SortedDictionary<int, int>();
        for (int f = 1; f <= p1.Count; f++)
        {
            nes.Pad1 = p1[f - 1]; nes.Pad2 = p2[f - 1]; nes.RunFrame(); nes.Apu.OutCount = 0;
            bool tick = br.Capture();
            if (!br.InMatch) continue;
            int sub = R0[0x59];
            if (sub != lastSub) { if (sub == 3 && lastSub == 2) { goals++; ev.AppendFormat(" GOAL@{0}({1})", f, (short)(R0[0x314 + 12] | (R0[0x327 + 12] << 8)) > 512 ? "E" : "W"); } lastSub = sub; }
            if (!tick) continue;
            var t = br.Cur; int w = t.Weather;
            int wc; weather.TryGetValue(w, out wc); weather[w] = wc + 1;
            if (w != lastW) { if (w == 5) { bursts++; ev.AppendFormat(" BURST@{0}", f); } else if (w == 1) { bolts++; ev.AppendFormat(" bolt@{0}", f); } lastW = w; }
            var o = t.Objects[12]; bool special = o.PieceCount != 4 && o.PieceCount != 2;
            for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++) { int low = t.Pieces[k].Tile & 63; if (!(low == 3 || low == 4 || low == 5 || low == 0x13 || low == 0x14 || low == 0x15)) special = true; }
            if (special && !lastSpecial) { specialStart = f; specialRun = 0; }
            if (special) specialRun++;
            if (!special && lastSpecial && specialRun >= 5) { specials++; ev.AppendFormat(" sp@{0}+{1}", specialStart, specialRun); }
            lastSpecial = special;
        }
        var ws = new StringBuilder(); foreach (var kv in weather) ws.AppendFormat("{0}:{1} ", kv.Key, kv.Value);
        Console.WriteLine("seed {0} r{1} g{2} c{3} menu {4} | goals {5} bursts {6} bolts {7} specials {8} | weather {9}|{10}", seed, region, ground, cond, menu, goals, bursts, bolts, specials, ws, ev);
        return 0;
    }
}
