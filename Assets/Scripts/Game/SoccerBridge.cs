// Reads what the game is doing out of the emulated machine: where every player and the ball are,
// and which sprite tiles make up each of them.  Pure C# (no UnityEngine) so it can be tested headless.
//
// The game moves its objects over several video frames and then, every 3-4 frames, lays out all sprites in
// one go ("composing").  That moment is a consistent picture of the match, so the bridge photographs work
// RAM right there (a "tick").  The sprites themselves are not read back from the picture: for every object
// the game's own composer routine is called on the side, with the object placed at a fixed spot.  That gives
// the complete figure even when the object is outside the original 256-pixel view or when the original would
// have run out of its 64 hardware sprites.
//
// A tick is published one frame after it was taken.  That is when the original shows it too: the sprite table goes
// to the picture chip at the start of the next frame, together with the pattern banks that belong to it - and the
// game animates special shots, knocked-out players and the like purely by swapping those banks from tick to tick.
using System;
using System.Collections.Generic;
using NesCore;

namespace Soccer
{
    public struct SpritePiece
    {
        public int Obj;         // owner (object slot 0..18)
        public int Dx;          // left edge of the 8x8 tile relative to the owner's ground point, NES pixels (+ = right)
        public int Top;         // top edge above the ground line, NES pixels (+ = up); the tile covers Top-8..Top
        public int Tile;        // absolute CHR tile: 1K bank * 64 + index
        public byte Attr;       // OAM attributes: bits 0-1 palette, 6 flip X, 7 flip Y
        public int Layer;       // 0 = rearmost piece of its owner
    }

    /// <summary>A sprite on the pitch that belongs to no object: a leaf in the wind, a splash, a piece of the lightning bolt.</summary>
    public struct FieldSprite
    {
        public int X, Y;        // field coordinates of the tile's top-left corner, NES pixels
        public int Tile;        // absolute CHR tile
        public byte Attr;
    }

    public struct ObjectState
    {
        public bool Active;
        public int X, Y, Z;     // field coordinates in NES pixels (Y grows towards the original camera), Z = height
        public int Kind;        // 0 player, 1 ball, 3 single-tile marker
        public int FirstPiece, PieceCount;
    }

    /// <summary>One consistent picture of the match.</summary>
    public sealed class Tick
    {
        public long Frame;                      // emulator frame it was taken in
        public int CamX, CamY;                  // top-left of the game's own 256x184 view on the field
        public readonly ObjectState[] Objects = new ObjectState[SoccerBridge.Slots];
        public readonly List<SpritePiece> Pieces = new List<SpritePiece>(256);
        public int Weather;                     // what the sky is doing ($0466): 0 nothing, 1 lightning, 2 rain, 3/4 wind, 5/6 a net bursting (east/west)
        public readonly List<FieldSprite> Extras = new List<FieldSprite>(16);
        public readonly byte[] Ram = new byte[0x800];       // work RAM as it was at that moment (for tools and debugging)
    }

    public sealed class SoccerBridge
    {
        public const int Slots = 19;
        public const int Ball = 0x0C;
        public readonly Nes Nes;
        public bool InMatch;                    // the match screen is up (field + status bar)
        public int Screen, SubScreen;           // $58 / $59
        public Tick Cur = new Tick(), Prev = new Tick();
        Tick next = new Tick();                 // taken, waiting for its pattern banks
        bool awaiting;
        readonly HashSet<int> explained = new HashSet<int>();
        public int TickCount;                   // ticks taken since the match screen came up
        public int TickSerial;                  // goes up by one with every tick
        public int LastInterval = 4;            // frames between the two latest ticks
        public int SandboxFailures;

        // the game's sprite composer (PRG bank pair 8/9; bank_04.asm of the disassembly: loc_8031 / bra_8036)
        const ushort ComposerEntry = 0x8036;
        const ushort ComposerProbePc = 0x803F;      // PC right after "LDA $F4" at $803D
        int bank8 = 8, bankA = 9;
        readonly byte[] snap = new byte[0x800], live = new byte[0x800];
        readonly bool[] listed = new bool[Slots];
        bool pending;

        public SoccerBridge(Nes nes)
        {
            Nes = nes;
            nes.RamReadHook += OnRamRead;
        }

        void OnRamRead(int adr)
        {
            if (adr != 0xF4 || Nes.Cpu.PC != ComposerProbePc) return;
            if (Nes.CpuPeek(0x803D) != 0xA5 || Nes.CpuPeek(0x803E) != 0xF4) return;
            // the game is about to compose this tick's sprites
            bank8 = Nes.PrgBank(0); bankA = Nes.PrgBank(1);
            Buffer.BlockCopy(Nes.Ram, 0, snap, 0, 0x800);
            for (int i = 0; i < Slots; i++) listed[i] = false;
            int n = snap[0xF4];
            for (int i = 0; i < n && i < 0x40; i++)
            {
                int e = snap[0xA1 + i] & 0x1F;
                if (e < Slots) listed[e] = true;
            }
            pending = true;
        }

        static int S16(int lo, int hi) { int v = lo | (hi << 8); return v >= 0x8000 ? v - 0x10000 : v; }

        /// <summary>Call once after every emulated frame.  Returns true when a new tick was published.</summary>
        public bool Capture()
        {
            var R = Nes.Ram;
            Screen = R[0x58]; SubScreen = R[0x59];
            bool match = Screen == 0 && Nes.SplitScanline >= 150 && Nes.SplitScanline <= 200;
            if (!match) { InMatch = false; TickCount = 0; pending = false; awaiting = false; return false; }
            InMatch = true;
            bool published = false;
            if (awaiting)
            {
                // the frame after: the banks now in place are the ones this tick's sprites are shown with
                awaiting = false;
                var t = next;
                for (int k = 0; k < t.Pieces.Count; k++) { var p = t.Pieces[k]; p.Tile = Absolute(p.Tile); t.Pieces[k] = p; }
                for (int k = 0; k < t.Extras.Count; k++) { var e = t.Extras[k]; e.Tile = Absolute(e.Tile); t.Extras[k] = e; }
                t.Frame = Nes.FrameCount;
                next = Prev; Prev = Cur; Cur = t;
                LastInterval = TickCount == 0 ? 4 : (int)Math.Max(1, Math.Min(12, t.Frame - Prev.Frame));
                TickCount++; TickSerial++;
                published = true;
            }
            if (pending) { pending = false; Take(); awaiting = true; }
            return published;
        }

        int Absolute(int raw) { return (Nes.FieldChrMap[4 + (raw >> 6)] >> 10) * 64 + (raw & 63); }

        // Photographs the match as it was when the game composed its sprites (tile numbers stay raw until the banks are known).
        void Take()
        {
            var R = Nes.Ram;
            var t = next;
            t.Pieces.Clear(); t.Extras.Clear();
            // work on the RAM as it was when the game composed its sprites
            Buffer.BlockCopy(R, 0, live, 0, 0x800);
            Buffer.BlockCopy(snap, 0, R, 0, 0x800);
            Buffer.BlockCopy(snap, 0, t.Ram, 0, 0x800);
            t.CamX = R[0xEC] | (R[0xED] << 8); t.CamY = R[0xEE] | (R[0xEF] << 8);
            t.Weather = R[0x466] & 0x7F;
            for (int i = 0; i < Slots; i++)
            {
                ObjectState o;
                o.X = S16(R[0x314 + i], R[0x327 + i]); o.Y = S16(R[0x34D + i], R[0x360 + i]); o.Z = S16(R[0x386 + i], R[0x399 + i]);
                o.Kind = R[0x61 + i];
                bool onScreen = i <= Ball && R[0xD9 + i] != 0;
                // players, keepers and the ball always exist - unless the game has them on screen and still leaves them out (hidden on purpose)
                o.Active = i <= Ball ? !(onScreen && !listed[i]) : listed[i];
                o.FirstPiece = t.Pieces.Count; o.PieceCount = 0;
                if (o.Active)
                {
                    o.PieceCount = Compose(i, o.Z, t.Pieces);
                    Buffer.BlockCopy(snap, 0, R, 0, 0x800);
                }
                t.Objects[i] = o;
            }
            Buffer.BlockCopy(live, 0, R, 0, 0x800);
            if (t.Weather != 0) Strays(t);
        }

        // What is in the game's own sprite table but belongs to none of the listed objects: the weather.
        void Strays(Tick t)
        {
            var S = snap; var R = Nes.Ram;
            explained.Clear();
            int n = S[0xF4], dx = 0, dy = 0; bool anchored = false;
            for (int e = 0; e < n && e < 0x40; e++)
            {
                int i = S[0xA1 + e] & 0x1F; if (i >= Slots) continue;
                var o = t.Objects[i];
                int sx = S[0x3AF + i], sy = S[0x3C2 + i], gy = S[0x3D5 + i];
                if (!anchored && i <= Ball && (S[0xA1 + e] & 0xE0) == 0) { dx = o.X - sx; dy = o.Y - gy; anchored = true; }      // screen -> field
                for (int k = o.FirstPiece; k < o.FirstPiece + o.PieceCount; k++)
                {
                    var p = t.Pieces[k];
                    explained.Add((((o.Z >= 0 ? sy : gy) - p.Top) & 0xFF) << 16 | p.Tile << 8 | ((sx + p.Dx) & 0xFF));
                }
            }
            if (!anchored) return;
            int split = Nes.SplitScanline;
            for (int s = 0; s < 64; s++)
            {
                int b = 0x200 + s * 4, y = R[b], tile = R[b + 1], x = R[b + 3];
                if (y >= split - 8 || explained.Contains(y << 16 | tile << 8 | x)) continue;
                FieldSprite f; f.X = x + dx; f.Y = y + dy; f.Tile = tile; f.Attr = R[b + 2];
                t.Extras.Add(f);
            }
        }

        // Lets the game's own routine lay out one object's sprites around a fixed point, then collects them.
        int Compose(int i, int z, List<SpritePiece> outList)
        {
            var R = Nes.Ram;
            const int ox = 128, oy = 128;
            int sink = z < 0 ? Math.Min(-z, 40) : 0;            // below ground: the routine itself clips the lower tiles
            R[0xF4] = 1; R[0xA1] = (byte)i;
            R[0x3AF + i] = ox; R[0x3D5 + i] = oy; R[0x3C2 + i] = (byte)(oy + sink);
            R[0x56] = 0xF0;                                      // no bottom-of-view clipping
            if (!Nes.CallRoutine(ComposerEntry, bank8, bankA, 20000)) { SandboxFailures++; return 0; }
            int n = 0x40 - R[0x5E3];
            if (n < 0 || n > 64) { SandboxFailures++; return 0; }
            int count = 0;
            for (int k = 0; k < n; k++)
            {
                int b = 0x200 + (63 - k) * 4;
                int y = R[b], tile = R[b + 1], x = R[b + 3];
                if (y >= 0xEF) continue;
                SpritePiece p;
                p.Obj = i; p.Dx = x - ox; p.Top = oy - y; p.Attr = R[b + 2]; p.Layer = k;
                p.Tile = tile;                                   // as written; Capture() turns it into an absolute tile once the banks are in place
                outList.Add(p);
                count++;
            }
            return count;
        }
    }
}
