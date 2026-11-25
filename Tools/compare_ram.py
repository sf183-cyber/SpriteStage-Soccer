import sys, numpy as np
a = np.fromfile(sys.argv[1] + '/ram.bin', dtype=np.uint8).reshape(-1, 2048)   # mine
b = np.fromfile(sys.argv[2] + '/ram.bin', dtype=np.uint8).reshape(-1, 2048)   # mesen
print('frames', len(a), len(b))
# ignore the stack page for alignment (return addresses in flight differ with the sample point)
mask = np.ones(2048, bool); mask[0x100:0x200] = False
for off in range(-3, 4):
    # compare a[i] with b[i+off]
    n = min(len(a), len(b)) - 8
    ia = np.arange(4, n); ib = ia + off
    d = (a[ia][:, mask] != b[ib][:, mask])
    per = d.sum(1)
    first = np.argmax(per > 0) if (per > 0).any() else -1
    print('off %+d: identical frames %d/%d, first diff at my frame %s, mean diff bytes %.1f' % (off, (per == 0).sum(), len(per), first + 5 if first >= 0 else None, per.mean()))
