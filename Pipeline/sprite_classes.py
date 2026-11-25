# The players' "16-bit style" art for the Unity version: packs the tone classes worked out by the HD pack project
# (HD2D-草地球场/build/gen_sprites.py -> build/out/sprites.npz) into Assets/Resources/Sprites/sprite_classes.bytes.
#   python3 sprite_classes.py [out.bytes]
#
# File layout (read by Assets/Scripts/Game/SpriteArt.cs):
#   "G3S1", int32 first tile, int32 tile count, int32 art size (16), then one byte per art pixel:
#   [variant 0 = as drawn, 1 = drawn for the tile shown mirrored][tile][y][x]
#   value 0 = transparent, otherwise 1 + (palette colour 1..3 - 1) * 3 + (0 shadow / 1 mid / 2 highlight)
# A tile is numbered as in the CHR ROM: 1K bank * 64 + index.  Tiles that were never seen in a complete pose are
# all zero; SpriteArt then falls back to the plain doubled pixels.
import os, sys, struct
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
NPZ = os.path.join(os.path.dirname(ROOT), 'HD2D-草地球场', 'build', 'out', 'sprites.npz')
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'Assets', 'Resources', 'Sprites', 'sprite_classes.bytes')

z = np.load(NPZ)
tiles, classes = z['tiles'], z['classes'].astype(np.uint8)
assert (np.diff(tiles) == 1).all(), 'tiles must be one unbroken run'
variants, count, size, _ = classes.shape
with open(OUT, 'wb') as f:
    f.write(b'G3S1' + struct.pack('<iii', int(tiles[0]), count, size))
    f.write(classes.tobytes())
print('wrote', OUT, '- tiles', int(tiles[0]), 'to', int(tiles[-1]), '- with art:', int((classes[0].reshape(count, -1).max(1) > 0).sum()))
