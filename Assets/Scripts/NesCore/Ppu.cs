// NES PPU (2C02), rendered a scanline at a time.
using System;

namespace NesCore
{
    public sealed class Ppu
    {
        public readonly byte[] Vram = new byte[0x800];
        public readonly byte[] Palette = new byte[32];
        public readonly byte[] Oam = new byte[256];
        public readonly uint[] FrameBuffer = new uint[256 * 240];       // 0xAABBGGRR (little-endian RGBA32)
        public byte OamAddr;
        public byte Ctrl, Mask, Status;
        public int V, T, X;             // loopy scroll registers
        public bool W;
        byte readBuffer;
        readonly Nes nes;
        /// <summary>Scroll (coarse + fine) that was latched for each scanline - used by the 3D bridge.</summary>
        public int FrameScrollX, FrameScrollY;

        public static readonly uint[] Rgb =
        {
            0x666666,0x002A88,0x1412A7,0x3B00A4,0x5C007E,0x6E0040,0x6C0600,0x561D00,0x333500,0x0B4800,0x005200,0x004F08,0x00404D,0x000000,0x000000,0x000000,
            0xADADAD,0x155FD9,0x4240FF,0x7527FE,0xA01ACC,0xB71E7B,0xB53120,0x994E00,0x6B6D00,0x388700,0x0C9300,0x008F32,0x007C8D,0x000000,0x000000,0x000000,
            0xFFFEFF,0x64B0FF,0x9290FF,0xC676FF,0xF36AFF,0xFE6ECC,0xFE8170,0xEA9E22,0xBCBE00,0x88D800,0x5CE430,0x45E082,0x48CDDE,0x4F4F4F,0x000000,0x000000,
            0xFFFEFF,0xC0DFFF,0xD3D2FF,0xE8C8FF,0xFAC2FF,0xFEC4EA,0xFECCC5,0xF7D8A5,0xE4E594,0xCFEF96,0xBDF4AB,0xB3F3CC,0xB5EBF2,0xB8B8B8,0x000000,0x000000
        };
        static readonly uint[] Abgr = new uint[64];

        static Ppu()
        {
            for (int i = 0; i < 64; i++)
            {
                uint c = Rgb[i];
                Abgr[i] = 0xFF000000u | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF);
            }
        }

        public Ppu(Nes n) { nes = n; }

        public void Reset()
        {
            Ctrl = Mask = Status = 0; V = T = X = 0; W = false; readBuffer = 0; OamAddr = 0;
            Array.Clear(Vram, 0, Vram.Length); Array.Clear(Oam, 0, Oam.Length); Array.Clear(Palette, 0, Palette.Length);
        }

        public bool RenderingEnabled => (Mask & 0x18) != 0;

        // ---- memory
        byte ReadVram(int addr)
        {
            addr &= 0x3FFF;
            if (addr < 0x2000) return nes.ChrRead(addr);
            if (addr < 0x3F00) return Vram[nes.MirrorNametable(addr)];
            int p = addr & 0x1F;
            if ((p & 0x13) == 0x10) p &= 0x0F;
            return Palette[p];
        }

        void WriteVram(int addr, byte v)
        {
            addr &= 0x3FFF;
            if (addr < 0x2000) return;                       // CHR ROM
            if (addr < 0x3F00) { Vram[nes.MirrorNametable(addr)] = v; return; }
            int p = addr & 0x1F;
            if ((p & 0x13) == 0x10) p &= 0x0F;
            Palette[p] = (byte)(v & 0x3F);
        }

        // ---- registers
        public byte ReadRegister(int r)
        {
            switch (r)
            {
                case 2:
                    {
                        byte s = (byte)(Status | (readBuffer & 0x1F));
                        Status &= 0x7F; W = false;
                        return s;
                    }
                case 4: return Oam[OamAddr];
                case 7:
                    {
                        byte result;
                        int a = V & 0x3FFF;
                        if (a < 0x3F00) { result = readBuffer; readBuffer = ReadVram(a); }
                        else { result = ReadVram(a); readBuffer = Vram[nes.MirrorNametable(a)]; }
                        V = (V + ((Ctrl & 4) != 0 ? 32 : 1)) & 0x7FFF;
                        return result;
                    }
            }
            return 0;
        }

        public void WriteRegister(int r, byte v)
        {
            switch (r)
            {
                case 0:
                    {
                        bool wasOff = (Ctrl & 0x80) == 0;
                        Ctrl = v;
                        T = (T & 0x73FF) | ((v & 3) << 10);
                        if (wasOff && (v & 0x80) != 0 && (Status & 0x80) != 0) nes.Cpu.NmiPending = true;
                        break;
                    }
                case 1: Mask = v; break;
                case 3: OamAddr = v; break;
                case 4: Oam[OamAddr++] = v; break;
                case 5:
                    if (!W) { T = (T & 0x7FE0) | (v >> 3); X = v & 7; }
                    else { T = (T & 0x0C1F) | ((v & 7) << 12) | ((v & 0xF8) << 2); }
                    W = !W; break;
                case 6:
                    if (!W) { T = (T & 0x00FF) | ((v & 0x3F) << 8); }
                    else { T = (T & 0x7F00) | v; V = T; }
                    W = !W; break;
                case 7:
                    WriteVram(V, v);
                    V = (V + ((Ctrl & 4) != 0 ? 32 : 1)) & 0x7FFF;
                    break;
            }
        }

        // ---- frame timing hooks
        public void EnterVBlank()
        {
            Status |= 0x80;
            if ((Ctrl & 0x80) != 0) nes.Cpu.NmiPending = true;
        }

        public void LeaveVBlank() { Status &= 0x1F; }

        public void IncrementY()
        {
            if ((V & 0x7000) != 0x7000) { V += 0x1000; return; }
            V &= ~0x7000;
            int y = (V & 0x3E0) >> 5;
            if (y == 29) { y = 0; V ^= 0x800; }
            else if (y == 31) y = 0;
            else y++;
            V = (V & ~0x3E0) | (y << 5);
        }

        public void CopyHorizontal() { V = (V & ~0x41F) | (T & 0x41F); }
        public void CopyVertical() { V = (V & ~0x7BE0) | (T & 0x7BE0); }

        // ---- rendering
        readonly byte[] bgIndex = new byte[256 + 16];   // colour index 0..3 of the background pixel
        readonly byte[] lineColor = new byte[256];      // palette RAM index
        readonly int[] sprList = new int[8];

        public void RenderScanline(int sl)
        {
            int rowStart = sl * 256;
            if (sl == 0) { FrameScrollX = ((V & 0x1F) << 3) | X | ((V & 0x400) != 0 ? 256 : 0); FrameScrollY = (((V >> 5) & 0x1F) << 3) | ((V >> 12) & 7) | 0; if ((V & 0x800) != 0) FrameScrollY += 240; }
            if (!RenderingEnabled)
            {
                uint c = Abgr[Palette[0] & 0x3F];
                for (int i = 0; i < 256; i++) FrameBuffer[rowStart + i] = c;
                return;
            }
            bool showBg = (Mask & 0x08) != 0, showSpr = (Mask & 0x10) != 0;
            bool bgLeft = (Mask & 0x02) != 0, sprLeft = (Mask & 0x04) != 0;
            // --- background
            if (showBg)
            {
                int v = V;
                int fineY = (v >> 12) & 7;
                int patBase = (Ctrl & 0x10) != 0 ? 0x1000 : 0;
                int px = -X;
                for (int tile = 0; tile < 33; tile++)
                {
                    int ntAddr = 0x2000 | (v & 0x0FFF);
                    int atAddr = 0x23C0 | (v & 0x0C00) | ((v >> 4) & 0x38) | ((v >> 2) & 0x07);
                    int id = Vram[nes.MirrorNametable(ntAddr)];
                    int at = Vram[nes.MirrorNametable(atAddr)];
                    int shift = ((v >> 4) & 4) | (v & 2);
                    int pal = ((at >> shift) & 3) << 2;
                    int addr = patBase + id * 16 + fineY;
                    int lo = nes.ChrRead(addr), hi = nes.ChrRead(addr + 8);
                    for (int b = 0; b < 8; b++, px++)
                    {
                        if (px < 0 || px > 255) continue;
                        int c = ((lo >> (7 - b)) & 1) | (((hi >> (7 - b)) & 1) << 1);
                        if (px < 8 && !bgLeft) c = 0;
                        bgIndex[px] = (byte)c;
                        lineColor[px] = (byte)(c == 0 ? 0 : (pal | c));
                    }
                    if ((v & 0x1F) == 31) { v &= ~0x1F; v ^= 0x400; } else v++;
                }
            }
            else
            {
                for (int i = 0; i < 256; i++) { bgIndex[i] = 0; lineColor[i] = 0; }
            }
            // --- sprites (front-to-back in OAM order; lower index wins)
            if (showSpr)
            {
                int h = (Ctrl & 0x20) != 0 ? 16 : 8;
                int count = 0;
                int[] list = sprList;
                for (int i = 0; i < 64 && count < 8; i++)
                {
                    int y = Oam[i * 4] + 1;
                    if (sl >= y && sl < y + h) list[count++] = i;
                }
                for (int k = count - 1; k >= 0; k--)
                {
                    int i = list[k];
                    int y = Oam[i * 4] + 1, id = Oam[i * 4 + 1], attr = Oam[i * 4 + 2], x = Oam[i * 4 + 3];
                    int row = sl - y;
                    if ((attr & 0x80) != 0) row = h - 1 - row;
                    int addr;
                    if (h == 16) { addr = ((id & 1) * 0x1000) + ((id & 0xFE) + (row >> 3)) * 16 + (row & 7); }
                    else addr = ((Ctrl & 0x08) != 0 ? 0x1000 : 0) + id * 16 + row;
                    int lo = nes.ChrRead(addr), hi = nes.ChrRead(addr + 8);
                    for (int b = 0; b < 8; b++)
                    {
                        int sx = x + b;
                        if (sx > 255) break;
                        int bit = (attr & 0x40) != 0 ? b : 7 - b;
                        int c = ((lo >> bit) & 1) | (((hi >> bit) & 1) << 1);
                        if (c == 0) continue;
                        if (sx < 8 && !sprLeft) continue;
                        if (i == 0 && bgIndex[sx] != 0 && sx != 255 && showBg) Status |= 0x40;
                        if ((attr & 0x20) != 0 && bgIndex[sx] != 0) continue;       // behind background
                        lineColor[sx] = (byte)(0x10 | ((attr & 3) << 2) | c);
                    }
                }
            }
            for (int i = 0; i < 256; i++)
            {
                int p = lineColor[i];
                if ((p & 3) == 0) p = 0;
                FrameBuffer[rowStart + i] = Abgr[Palette[p] & 0x3F];
            }
        }
    }
}
