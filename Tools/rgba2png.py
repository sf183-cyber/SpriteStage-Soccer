# Turns the raw dumps of the console tools into PNG.
#   python3 Tools/rgba2png.py <folder or file.rgba> ...
# Two kinds, told apart by size:
#   SpriteDump.cs  -> 64x64, one uint32 per pixel = NES colour number (0-63) | 0xFF000000   (ground line y = 56, centre x = 32)
#   FrameDump.cs   -> 256x240, real colours (bytes R, G, B, A) - the emulator's own picture
import sys, os, glob
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PAL = [int(h, 16) for h in open(os.path.join(os.path.dirname(HERE), 'Pipeline', 'nespal.txt')).read().split()]
LUT = np.array([[(p >> 16) & 255, (p >> 8) & 255, p & 255] for p in PAL], np.uint8)


def convert(path):
    raw = np.frombuffer(open(path, 'rb').read(), np.uint8)
    if raw.size == 64 * 64 * 4:
        a = raw.reshape(64, 64, 4)
        out = np.zeros((64, 64, 4), np.uint8)
        out[..., :3] = LUT[a[..., 0] & 63]
        out[..., 3] = np.where(a[..., 3] > 0, 255, 0)
        im = Image.fromarray(out, 'RGBA')
    elif raw.size == 256 * 240 * 4:
        im = Image.fromarray(raw.reshape(240, 256, 4)[..., :3].copy(), 'RGB')
    else:
        print('skipped (unknown size):', path)
        return
    im.save(path[:-5] + '.png')


files = []
for arg in sys.argv[1:]:
    files += sorted(glob.glob(os.path.join(arg, '*.rgba'))) if os.path.isdir(arg) else [arg]
for f in files:
    convert(f)
print('converted', len(files))
