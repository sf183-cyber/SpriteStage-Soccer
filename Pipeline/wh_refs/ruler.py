# crop a sheet with a labelled pixel grid:  python3 ruler.py <sheet> x0 y0 x1 y1 out.png [zoom] [grid]
import sys, os
from PIL import Image, ImageDraw
n, x0, y0, x1, y1 = int(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
out = sys.argv[6]; z = float(sys.argv[7]) if len(sys.argv) > 7 else 1.0; g = int(sys.argv[8]) if len(sys.argv) > 8 else 100
im = Image.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'HABS_DC-37_sheet%02d.jpg' % n)).convert('RGB').crop((x0, y0, x1, y1))
if z != 1.0: im = im.resize((int(im.size[0] * z), int(im.size[1] * z)), Image.LANCZOS)
ov = Image.new('RGBA', im.size, (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
for x in range((x0 // g + 1) * g, x1, g):
    X = (x - x0) * z; d.line([(X, 0), (X, im.size[1])], fill=(255, 0, 0, 70)); d.text((X + 2, 2), str(x), fill=(220, 0, 0, 255))
for y in range((y0 // g + 1) * g, y1, g):
    Y = (y - y0) * z; d.line([(0, Y), (im.size[0], Y)], fill=(0, 0, 255, 70)); d.text((2, Y + 1), str(y), fill=(0, 0, 220, 255))
Image.alpha_composite(im.convert('RGBA'), ov).convert('RGB').save(out)
print(out, im.size)
