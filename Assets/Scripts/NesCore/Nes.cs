// Minimal NES (NTSC) with MMC3 - just enough to run the soccer game of the case study faithfully.
// Scanline-accurate PPU, cycle-counted CPU, sound chip (Apu.cs).
using System;

namespace NesCore
{
    [Flags]
    public enum Pad : byte { A = 1, B = 2, Select = 4, Start = 8, Up = 16, Down = 32, Left = 64, Right = 128 }

    public sealed class Nes
    {
        public readonly byte[] Ram = new byte[0x800];
        public readonly byte[] PrgRam = new byte[0x2000];
        public byte[] Prg, Chr;
        public Cpu Cpu;
        public Ppu Ppu;
        public Apu Apu;
        public byte Pad1, Pad2;
        public int LastDmaPage = -1;                    // CPU page last copied to sprite memory through $4014
        public long FrameCount;

        // ---- MMC3 state
        readonly byte[] reg = new byte[8];
        byte bankSelect;
        bool horizontalMirroring;
        byte irqLatch, irqCounter;
        bool irqReload, irqEnabled, irqFlag;
        readonly int[] prgMap = new int[4];
        public readonly int[] ChrMap = new int[8];      // offset into Chr of each 1K page of the pattern tables
        /// <summary>CHR mapping as it was while the playfield was being drawn (before the status-bar split).</summary>
        public readonly int[] FieldChrMap = new int[8];
        public int SplitScanline = -1;                  // scanline at which the MMC3 IRQ fired this frame (-1: none)

        // ---- controllers
        bool strobe;
        byte shift1, shift2;

        public bool LoadRom(byte[] rom)
        {
            if (rom.Length < 16 || rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S') return false;
            int prgSize = rom[4] * 0x4000, chrSize = rom[5] * 0x2000;
            int mapper = (rom[6] >> 4) | (rom[7] & 0xF0);
            if (mapper != 4 || chrSize == 0) return false;
            Prg = new byte[prgSize]; Chr = new byte[chrSize];
            Array.Copy(rom, 16, Prg, 0, prgSize);
            Array.Copy(rom, 16 + prgSize, Chr, 0, chrSize);
            horizontalMirroring = (rom[6] & 1) == 0;
            Cpu = new Cpu(this); Ppu = new Ppu(this); Apu = new Apu(this);
            Cpu.IrqLine = () => irqFlag;
            Reset();
            return true;
        }

        public void Reset()
        {
            Array.Clear(Ram, 0, Ram.Length);
            for (int i = 0; i < 8; i++) reg[i] = 0;
            reg[6] = 0; reg[7] = 1; bankSelect = 0;
            irqLatch = irqCounter = 0; irqReload = irqEnabled = irqFlag = false;
            UpdateBanks();
            Ppu.Reset();
            Cpu.Reset();
            FrameCount = 0; scanline = 261; dots = Cpu.Cycles * 3;
            Apu.Reset();
        }

        void UpdateBanks()
        {
            int nPrg = Prg.Length / 0x2000;
            int last = nPrg - 1;
            if ((bankSelect & 0x40) == 0) { prgMap[0] = reg[6] % nPrg; prgMap[1] = reg[7] % nPrg; prgMap[2] = last - 1; prgMap[3] = last; }
            else { prgMap[0] = last - 1; prgMap[1] = reg[7] % nPrg; prgMap[2] = reg[6] % nPrg; prgMap[3] = last; }
            int nChr = Chr.Length / 0x400;
            int[] c = new int[8];
            c[0] = reg[0] & 0xFE; c[1] = reg[0] | 1; c[2] = reg[1] & 0xFE; c[3] = reg[1] | 1;
            c[4] = reg[2]; c[5] = reg[3]; c[6] = reg[4]; c[7] = reg[5];
            bool inv = (bankSelect & 0x80) != 0;
            for (int i = 0; i < 8; i++) ChrMap[inv ? (i ^ 4) : i] = (c[i] % nChr) * 0x400;
        }

        public byte ChrRead(int addr) => Chr[ChrMap[(addr >> 10) & 7] + (addr & 0x3FF)];

        public int MirrorNametable(int addr)
        {
            int a = addr & 0xFFF;
            int table = a >> 10;
            int t = horizontalMirroring ? (table >> 1) : (table & 1);
            return (t << 10) | (a & 0x3FF);
        }

        /// <summary>Called by the PPU once per rendered scanline (dot 260): the MMC3 scanline counter.</summary>
        public void ClockScanlineCounter(int scanline)
        {
            if (irqCounter == 0 || irqReload) { irqCounter = irqLatch; irqReload = false; }
            else irqCounter--;
            if (irqCounter == 0 && irqEnabled)
            {
                if (!irqFlag && SplitScanline < 0) { SplitScanline = scanline; Array.Copy(ChrMap, FieldChrMap, 8); }
                irqFlag = true;
            }
        }

        // ---- CPU bus
        /// <summary>Optional taps on work-RAM accesses (used by the game bridge to follow what the game is doing).</summary>
        public Action<int> RamReadHook;
        public Action<int, byte> RamWriteHook;
        /// <summary>Optional tap on PPU register writes ($2000-$2007 -> 0..7).</summary>
        public Action<int, byte> PpuWriteHook;

        public byte CpuRead(ushort a)
        {
            if (a < 0x2000) { if (RamReadHook != null) RamReadHook(a & 0x7FF); return Ram[a & 0x7FF]; }
            if (a >= 0x8000) return Prg[prgMap[(a >> 13) & 3] * 0x2000 + (a & 0x1FFF)];
            if (a < 0x4000) return Ppu.ReadRegister(a & 7);
            if (a == 0x4016) { byte r = (byte)(0x40 | (shift1 & 1)); if (!strobe) shift1 = (byte)((shift1 >> 1) | 0x80); return r; }
            if (a == 0x4017) { byte r = (byte)(0x40 | (shift2 & 1)); if (!strobe) shift2 = (byte)((shift2 >> 1) | 0x80); return r; }
            if (a == 0x4015) return Apu.ReadStatus();
            if (a >= 0x6000) return PrgRam[a - 0x6000];
            return 0;
        }

        public void CpuWrite(ushort a, byte v)
        {
            if (a < 0x2000) { Ram[a & 0x7FF] = v; if (RamWriteHook != null) RamWriteHook(a & 0x7FF, v); return; }
            if (a < 0x4000) { if (PpuWriteHook != null) PpuWriteHook(a & 7, v); Ppu.WriteRegister(a & 7, v); return; }
            if (a >= 0x8000) { MapperWrite(a, v); return; }
            if (a == 0x4014)
            {
                int page = v << 8; LastDmaPage = page;
                for (int i = 0; i < 256; i++) Ppu.Oam[(Ppu.OamAddr + i) & 0xFF] = CpuRead((ushort)(page + i));
                Cpu.Cycles += 513;
                return;
            }
            if (a == 0x4016)
            {
                strobe = (v & 1) != 0;
                if (strobe) { shift1 = Pad1; shift2 = Pad2; }
                return;
            }
            if (a >= 0x6000) { PrgRam[a - 0x6000] = v; return; }
            if (a <= 0x4017) Apu.Write(a, v);            // $4000-$4013, $4015, $4017: sound
        }

        void MapperWrite(ushort a, byte v)
        {
            switch (a & 0xE001)
            {
                case 0x8000: bankSelect = v; UpdateBanks(); break;
                case 0x8001: reg[bankSelect & 7] = v; UpdateBanks(); break;
                case 0xA000: horizontalMirroring = (v & 1) != 0; break;
                case 0xA001: break;
                case 0xC000: irqLatch = v; break;
                case 0xC001: irqCounter = 0; irqReload = true; break;
                case 0xE000: irqEnabled = false; irqFlag = false; break;
                case 0xE001: irqEnabled = true; break;
            }
        }

        // ---- calling into the game's own code "on the side"
        static bool NoIrq() { return false; }
        public const ushort Sentinel = 0x4FF0;

        /// <summary>
        /// Runs one of the game's subroutines as if it were a function: executes from <paramref name="pc"/> with the
        /// given PRG banks at $8000/$A000 until it returns, then puts the CPU, its clock and the bank mapping back.
        /// Work RAM is NOT restored - the caller snapshots it before and copies it back after reading the results.
        /// </summary>
        public bool CallRoutine(ushort pc, int bank8000, int bankA000, int maxSteps = 200000)
        {
            byte a = Cpu.A, x = Cpu.X, y = Cpu.Y, sp = Cpu.S, p = Cpu.P; ushort oldPc = Cpu.PC; long cyc = Cpu.Cycles;
            bool nmi = Cpu.NmiPending; var irq = Cpu.IrqLine; var rh = RamReadHook; var wh = RamWriteHook;
            int m0 = prgMap[0], m1 = prgMap[1];
            Cpu.NmiPending = false; Cpu.IrqLine = NoIrq; RamReadHook = null; RamWriteHook = null;
            prgMap[0] = bank8000; prgMap[1] = bankA000;
            Ram[0x1FF] = (byte)((Sentinel - 1) >> 8); Ram[0x1FE] = (byte)((Sentinel - 1) & 0xFF);
            Cpu.S = 0xFD; Cpu.PC = pc; Cpu.P = 0x24;
            bool ok = false;
            for (int i = 0; i < maxSteps; i++)
            {
                if (Cpu.PC == Sentinel) { ok = true; break; }
                Cpu.Step();
            }
            prgMap[0] = m0; prgMap[1] = m1;
            Cpu.A = a; Cpu.X = x; Cpu.Y = y; Cpu.S = sp; Cpu.P = p; Cpu.PC = oldPc; Cpu.Cycles = cyc;
            Cpu.NmiPending = nmi; Cpu.IrqLine = irq; RamReadHook = rh; RamWriteHook = wh;
            return ok;
        }

        /// <summary>Reads program ROM / RAM without side effects.</summary>
        public byte CpuPeek(ushort a)
        {
            if (a < 0x2000) return Ram[a & 0x7FF];
            if (a >= 0x8000) return Prg[prgMap[(a >> 13) & 3] * 0x2000 + (a & 0x1FFF)];
            return 0;
        }

        /// <summary>8K PRG bank currently mapped at $8000 + slot * $2000.</summary>
        public int PrgBank(int slot) { return prgMap[slot]; }

        // ---- one video frame
        const int DotsPerLine = 341;
        long dots;      // PPU dots elapsed since power-on (3 per CPU cycle)

        void RunTo(long targetDots)
        {
            while (Cpu.Cycles * 3 < targetDots) Cpu.Step();
        }

        int scanline = 261;     // next scanline to emulate (power-on starts at the pre-render line, like Mesen)

        void RunScanline(int sl)
        {
            long start = dots;
            if (sl < 240)
            {
                Ppu.RenderScanline(sl);
                RunTo(start + 256);
                if (Ppu.RenderingEnabled) { Ppu.IncrementY(); Ppu.CopyHorizontal(); }
                RunTo(start + 260);
                if (Ppu.RenderingEnabled) ClockScanlineCounter(sl);
            }
            else if (sl == 241)
            {
                RunTo(start + 1);
                Ppu.EnterVBlank();
            }
            else if (sl == 261)
            {
                RunTo(start + 1);
                Ppu.LeaveVBlank();
                RunTo(start + 256);
                if (Ppu.RenderingEnabled) Ppu.CopyHorizontal();
                RunTo(start + 260);
                if (Ppu.RenderingEnabled) ClockScanlineCounter(sl);
                RunTo(start + 280);
                if (Ppu.RenderingEnabled) Ppu.CopyVertical();
            }
            dots = start + DotsPerLine;
            RunTo(dots);
        }

        /// <summary>
        /// Emulates one video frame: the post-render line and vertical blank (where the game runs its logic
        /// and reads the pads), then the 240 visible lines.  Returns when the picture in Ppu.FrameBuffer is
        /// complete - the same point at which Mesen raises its end-of-frame event.
        /// </summary>
        public void RunFrame()
        {
            SplitScanline = -1;
            if (strobe) { shift1 = Pad1; shift2 = Pad2; }
            do
            {
                RunScanline(scanline);
                scanline = scanline == 261 ? 0 : scanline + 1;
            } while (scanline != 240);
            if (SplitScanline < 0) Array.Copy(ChrMap, FieldChrMap, 8);
            Apu.RunTo(Cpu.Cycles);
            FrameCount++;
        }
    }
}
