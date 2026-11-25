#!/usr/bin/env python3
# Locks the data that was made from the game's own graphics, so it can be shipped without anything of the game in it: each
# piece is encrypted with the very graphics it was made from, so it only opens with a copy of the game that has the same
# graphics (RomFile.Open does that with the player's own file).  Two kinds:
#
#   tiles  - the sharpened-sprite table (Assets/Resources/Sprites/sprite_classes.bytes, "G3S1"): every tile's entry is locked
#            with that tile's own 16 bytes of CHR, so a version of the game whose tiles differ here and there (a translation
#            redraws the letters) still opens every tile it shares; the others fall back to the plain pixels.
#   region - one picture made from a stretch of CHR (the horse: the ranch court's background pages): locked with that stretch.
#
#   python3 Tools/rom_lock.py tiles  game.nes plain.bytes locked.bytes  salt
#   python3 Tools/rom_lock.py region game.nes plain.bytes locked.bytes  salt  chrOffset length
#   python3 Tools/rom_lock.py open   game.nes locked.bytes plain.bytes  salt          (to check: opens either kind)
# salt: "spritestage/soccer/" (SpriteStage-Soccer) or "spritestage/basketball/" (SpriteStage-Basketball).
#
# Formats (all integers little-endian):
#   "SPS2" + the table's own 16-byte header (G3S1, first tile, tile count, 0) + count x 4-byte tags + the entries, encrypted
#          in their usual order ([variant][tile][16x16]).  Tile t: key = SHA-256(salt + its 16 CHR bytes); tag = first 4
#          bytes of SHA-256(key + "tag"); the 256 bytes of variant v = XOR with SHA-256(key + v + n) for n = 0..7.
#   "SPSC" + chrOffset + length + 4-byte tag + data XOR SHA-256(key + n), n = 0, 1, ...; key = SHA-256(salt + that CHR).
import hashlib, struct, sys

def sha(b): return hashlib.sha256(b).digest()

def chr_of(rom):
    b = open(rom, "rb").read(); prg = b[4] * 16384
    return b[16 + prg:16 + prg + b[5] * 8192]

def tile_stream(key, v): return b"".join(sha(key + bytes([v]) + n.to_bytes(4, "little")) for n in range(8))

def region_stream(key, n):
    out = bytearray(); i = 0
    while len(out) < n: out += sha(key + i.to_bytes(4, "little")); i += 1
    return bytes(out[:n])

def xor(a, b): return bytes(x ^ y for x, y in zip(a, b))

mode, rom, src, dst, salt = sys.argv[1:6]
salt = salt.encode(); chr_ = chr_of(rom); data = open(src, "rb").read()

if mode == "tiles":
    assert data[:4] == b"G3S1"
    first, count = struct.unpack("<ii", data[4:12]); body = data[16:]
    tags, out = bytearray(), bytearray(body)
    keys = [sha(salt + chr_[(first + i) * 16:(first + i) * 16 + 16]) for i in range(count)]
    for i, k in enumerate(keys): tags += sha(k + b"tag")[:4]
    for v in range(2):
        for i, k in enumerate(keys):
            o = (v * count + i) * 256
            out[o:o + 256] = xor(body[o:o + 256], tile_stream(k, v))
    open(dst, "wb").write(b"SPS2" + data[:16] + bytes(tags) + bytes(out))
elif mode == "region":
    off, n = int(sys.argv[6]), int(sys.argv[7]); key = sha(salt + chr_[off:off + n])
    open(dst, "wb").write(b"SPSC" + struct.pack("<ii", off, n) + sha(key + b"tag")[:4] + xor(data, region_stream(key, len(data))))
elif mode == "open":
    if data[:4] == b"SPS2":
        hdr = data[4:20]; first, count = struct.unpack("<ii", hdr[4:12]); tags = data[20:20 + count * 4]; body = bytearray(data[20 + count * 4:])
        bad = 0
        for i in range(count):
            k = sha(salt + chr_[(first + i) * 16:(first + i) * 16 + 16])
            ok = sha(k + b"tag")[:4] == tags[i * 4:i * 4 + 4]; bad += not ok
            for v in range(2):
                o = (v * count + i) * 256
                body[o:o + 256] = xor(body[o:o + 256], tile_stream(k, v)) if ok else bytes(256)
        open(dst, "wb").write(hdr + bytes(body)); print("tiles: %d of %d open" % (count - bad, count))
    elif data[:4] == b"SPSC":
        off, n = struct.unpack("<ii", data[4:12]); key = sha(salt + chr_[off:off + n])
        assert sha(key + b"tag")[:4] == data[12:16], "this copy of the game has other graphics there"
        open(dst, "wb").write(xor(data[16:], region_stream(key, len(data) - 16))); print("region open")
    else: sys.exit("not a locked file")
else: sys.exit("mode: tiles | region | open")
print(mode, src, "->", dst)
