// Turns an 8x8 NES sprite tile + palette into 16x16 "16-bit style" art.
// The shapes come from the HD pack project (sprite_hd.py / gen_sprites.py): every tile was up-scaled in the
// context of the whole figure and each art pixel classified as one of 9 tones (3 palette colours x shadow /
// mid / highlight).  Only that class table is shipped; colours are computed here for whatever palette the game uses.
// Pure C# (no UnityEngine).
using System;
using NesCore;

namespace Soccer
{
    public sealed class SpriteArt
    {
        public const int Size = 16;
        readonly byte[] classes;        // [variant][tile][y][x]
        readonly int firstTile, tileCount;
        readonly byte[] chr;
        readonly uint[] lut = new uint[10];
        readonly byte[] raw = new byte[64];

        public SpriteArt(byte[] table, byte[] chrRom)
        {
            chr = chrRom;
            if (table != null && table.Length > 16 && table[0] == 'G' && table[1] == '3' && table[2] == 'S' && table[3] == '1')
            {
                firstTile = BitConverter.ToInt32(table, 4); tileCount = BitConverter.ToInt32(table, 8);
                classes = new byte[table.Length - 16];
                Buffer.BlockCopy(table, 16, classes, 0, classes.Length);
            }
        }

        static void Tone(int nesColour, out uint shadow, out uint mid, out uint highlight)
        {
            uint v = Ppu.Rgb[nesColour & 0x3F];
            float r = ((v >> 16) & 255) / 255f, g = ((v >> 8) & 255) / 255f, b = (v & 255) / 255f;
            float l = 0.3f * r + 0.59f * g + 0.11f * b;
            mid = Pack(r, g, b);
            if (l < 0.2f) { shadow = mid; highlight = Pack(r + 0.10f, g + 0.10f, b + 0.10f); return; }
            float sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (sat < 0.08f)
            {
                shadow = Pack(r * 0.70f, g * 0.76f, b * 0.90f);
                highlight = Pack(r + 0.06f, g + 0.06f, b + 0.06f);
            }
            else
            {
                shadow = Pack(r * 0.68f + 0.02f, g * 0.62f, b * 0.78f + 0.06f);
                highlight = Pack(r + (1 - r) * 0.50f, g + (1 - g) * 0.44f, b + (1 - b) * 0.26f);
            }
        }

        static uint Pack(float r, float g, float b)
        {
            uint R = (uint)(Math.Max(0f, Math.Min(1f, r)) * 255f + 0.5f), G = (uint)(Math.Max(0f, Math.Min(1f, g)) * 255f + 0.5f), B = (uint)(Math.Max(0f, Math.Min(1f, b)) * 255f + 0.5f);
            return 0xFF000000u | (B << 16) | (G << 8) | R;       // RGBA32 in memory order
        }

        /// <summary>
        /// Renders one tile into dst (RGBA32, row 0 = top), 16x16 pixels starting at (x0, y0) of a picture 'stride' wide.
        /// c1..c3 are the NES colour numbers of the sprite palette; 'mirrored' picks the art drawn for the tile shown flipped,
        /// so the light keeps coming from the same side.
        /// </summary>
        public void Render(int absTile, int c1, int c2, int c3, bool mirrored, uint[] dst, int stride, int x0, int y0)
        {
            Tone(c1, out lut[1], out lut[2], out lut[3]);
            Tone(c2, out lut[4], out lut[5], out lut[6]);
            Tone(c3, out lut[7], out lut[8], out lut[9]);
            lut[0] = 0;
            int idx = absTile - firstTile;
            bool hd = classes != null && idx >= 0 && idx < tileCount;
            if (hd)
            {
                int off = ((mirrored ? 1 : 0) * tileCount + idx) * Size * Size;
                bool any = false;
                for (int i = 0; i < Size * Size; i++) if (classes[off + i] != 0) { any = true; break; }
                if (any)
                {
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                            dst[(y0 + y) * stride + x0 + x] = lut[classes[off + y * Size + x]];
                    return;
                }
            }
            // no HD art for this tile: plain pixels, doubled
            int p = absTile * 16;
            if (p + 16 > chr.Length) p = 0;
            for (int y = 0; y < 8; y++)
            {
                int lo = chr[p + y], hi = chr[p + y + 8];
                for (int x = 0; x < 8; x++)
                {
                    int c = ((lo >> (7 - x)) & 1) | (((hi >> (7 - x)) & 1) << 1);
                    uint col = c == 0 ? 0u : lut[(c - 1) * 3 + 2];
                    int o = (y0 + y * 2) * stride + x0 + x * 2;
                    dst[o] = col; dst[o + 1] = col; dst[o + stride] = col; dst[o + stride + 1] = col;
                }
            }
        }
    }
}
