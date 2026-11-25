// A texture that collects every sprite tile / palette combination the match uses, rendered as 16x16 art.
using System.Collections.Generic;
using UnityEngine;

namespace Soccer
{
    public sealed class SpriteAtlas
    {
        public const int Cells = 64, Cell = SpriteArt.Size, Dim = Cells * Cell;      // 1024 x 1024, 4096 entries
        public readonly Texture2D Texture;
        readonly uint[] pixels = new uint[Dim * Dim];
        readonly Dictionary<ulong, int> index = new Dictionary<ulong, int>();
        readonly SpriteArt art;
        bool dirty;
        int next;

        public SpriteAtlas(SpriteArt a)
        {
            art = a;
            Texture = new Texture2D(Dim, Dim, TextureFormat.RGBA32, false, false);
            Texture.filterMode = FilterMode.Bilinear; Texture.wrapMode = TextureWrapMode.Clamp; Texture.name = "SpriteAtlas";
            Texture.hideFlags = HideFlags.DontSave;
            dirty = true;
        }

        /// <summary>Cell number of a tile drawn with the given three palette colours.</summary>
        public int Get(int tile, int c1, int c2, int c3, bool mirrored)
        {
            ulong key = (ulong)(uint)tile | ((ulong)(c1 & 63) << 24) | ((ulong)(c2 & 63) << 32) | ((ulong)(c3 & 63) << 40) | (mirrored ? 1UL << 48 : 0UL);
            int cell;
            if (index.TryGetValue(key, out cell)) return cell;
            if (next >= Cells * Cells) { index.Clear(); next = 0; }         // full: start over (never happens within one match)
            cell = next++;
            // texture row 0 is the bottom: draw the art upside down in memory so that v grows upwards in the picture
            int cx = (cell % Cells) * Cell, cy = (cell / Cells) * Cell;
            art.Render(tile, c1, c2, c3, mirrored, scratch, Cell, 0, 0);
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    pixels[(cy + Cell - 1 - y) * Dim + cx + x] = scratch[y * Cell + x];
            index[key] = cell;
            dirty = true;
            return cell;
        }

        readonly uint[] scratch = new uint[Cell * Cell];

        public void Upload()
        {
            if (!dirty) return;
            Texture.SetPixelData(pixels, 0);
            Texture.Apply(false, false);
            dirty = false;
        }
    }
}
