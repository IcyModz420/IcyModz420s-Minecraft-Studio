"""Self-tests for the decoders (run: python -I selftest.py [--oracle DIR]).

* Synthetic PNGs written by a tiny independent encoder below: every colour type,
  every bit depth, every filter type, tRNS, Adam7 (including images so small that
  some passes are empty).
* Optional oracle: PNGs written by Windows' own WIC encoder together with their
  expected pixels (tools/png-oracle/make-pngs.ps1 -Out DIR writes them).
* Synthetic TGAs: types 2 and 10, 24/32-bit, all four origins.
* .brarchive and lenient-JSON round trips.
"""
import os
import random
import struct
import sys
import zlib

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from texmap import png, tga, brarchive, jsonc  # noqa: E402

FAILS = []


def check(cond, what):
    if not cond:
        FAILS.append(what)
        print('FAIL', what)


# ------------------------------------------------------------------ tiny PNG encoder
def _chunk(t, body):
    return struct.pack('>I', len(body)) + t + body + struct.pack('>I', zlib.crc32(t + body) & 0xffffffff)


def _paeth(a, b, c):
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def _filter_row(ft, row, prev, bpp):
    out = bytearray(len(row))
    for i in range(len(row)):
        a = row[i - bpp] if i >= bpp else 0
        b = prev[i]
        c = prev[i - bpp] if i >= bpp else 0
        if ft == 0:
            p = 0
        elif ft == 1:
            p = a
        elif ft == 2:
            p = b
        elif ft == 3:
            p = (a + b) // 2
        else:
            p = _paeth(a, b, c)
        out[i] = (row[i] - p) & 255
    return out


def _pack_samples(samples, depth):
    if depth == 8:
        return bytearray(samples)
    if depth == 16:
        out = bytearray()
        for s in samples:
            out += struct.pack('>H', s)
        return out
    out = bytearray()
    per = 8 // depth
    for i in range(0, len(samples), per):
        grp = list(samples[i:i + per]) + [0] * (per - len(samples[i:i + per]))
        v = 0
        for s in grp:
            v = (v << depth) | s
        out.append(v)
    return out


def encode_png(w, h, ctype, depth, samples_rows, interlace=0, plte=None, trns=None, filters=(0, 1, 2, 3, 4)):
    """samples_rows: list of rows, each a list of raw samples (channels interleaved)."""
    ch = png.CHANNELS[ctype]
    bits_pp = ch * depth
    bpp = max(1, bits_pp // 8)
    raw = bytearray()
    k = 0

    def emit(rows):
        nonlocal k, raw
        prev = None
        for r in rows:
            packed = _pack_samples(r, depth)
            if prev is None:
                prev = bytearray(len(packed))
            ft = filters[k % len(filters)]
            k += 1
            raw.append(ft)
            raw += _filter_row(ft, packed, prev, bpp)
            prev = packed

    if interlace == 0:
        emit(samples_rows)
    else:
        for (x0, y0, dx, dy) in png.ADAM7:
            sub = []
            for y in range(y0, h, dy):
                r = []
                for x in range(x0, w, dx):
                    r += samples_rows[y][x * ch:(x + 1) * ch]
                if r:
                    sub.append(r)
            if sub and sub[0]:
                emit(sub)
    out = png.SIGNATURE + _chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, depth, ctype, 0, 0, interlace))
    if plte is not None:
        out += _chunk(b'PLTE', plte)
    if trns is not None:
        out += _chunk(b'tRNS', trns)
    comp = zlib.compress(bytes(raw), 9)
    # split IDAT to exercise concatenation
    half = len(comp) // 2
    out += _chunk(b'IDAT', comp[:half]) + _chunk(b'IDAT', comp[half:]) + _chunk(b'IEND', b'')
    return out


def expected_rgba(w, h, ctype, depth, rows, palette=None, trns_vals=None):
    out = bytearray()
    ch = png.CHANNELS[ctype]
    for r in rows:
        for x in range(w):
            s = r[x * ch:(x + 1) * ch]
            if ctype == 3:
                out += palette[s[0]]
                continue
            if depth == 16:
                s8 = [v >> 8 for v in s]
            elif depth == 8:
                s8 = list(s)
            else:
                s8 = [v * (255 // ((1 << depth) - 1)) for v in s]
            if ctype == 0:
                a = 0 if (trns_vals is not None and s[0] == trns_vals[0]) else 255
                out += bytes((s8[0], s8[0], s8[0], a))
            elif ctype == 2:
                a = 0 if (trns_vals is not None and list(s) == list(trns_vals)) else 255
                out += bytes((s8[0], s8[1], s8[2], a))
            elif ctype == 4:
                out += bytes((s8[0], s8[0], s8[0], s8[1]))
            else:
                out += bytes(s8)
    return bytes(out)


def synthetic_png_tests():
    rnd = random.Random(42)
    sizes = [(1, 1), (2, 3), (3, 2), (5, 7), (8, 8), (9, 1), (1, 9), (17, 13), (33, 4)]
    n = 0
    for ctype, depths in png.LEGAL_DEPTHS.items():
        for depth in depths:
            ch = png.CHANNELS[ctype]
            for (w, h) in sizes:
                for interlace in (0, 1):
                    for use_trns in (False, True):
                        if use_trns and ctype in (4, 6):
                            continue
                        maxv = (1 << depth) - 1
                        palette = plte = trns = trns_vals = None
                        if ctype == 3:
                            count = min(1 << depth, 1 + rnd.randrange(1 << depth))
                            maxv = count - 1
                            cols = [bytes((rnd.randrange(256), rnd.randrange(256), rnd.randrange(256))) for _ in range(count)]
                            alphas = [rnd.choice((0, 255, rnd.randrange(256))) for _ in range(count)] if use_trns else []
                            plte = b''.join(cols)
                            if use_trns:
                                cut = rnd.randrange(count + 1)  # tRNS may be shorter than PLTE
                                alphas = alphas[:cut]
                                trns = bytes(alphas)
                            palette = [cols[i] + bytes((alphas[i] if i < len(alphas) else 255,)) for i in range(count)]
                        rows = [[rnd.randrange(maxv + 1) for _ in range(w * ch)] for _ in range(h)]
                        if use_trns and ctype in (0, 2):
                            pick = rows[rnd.randrange(h)][:ch]  # a value that really occurs
                            trns_vals = pick
                            trns = b''.join(struct.pack('>H', v) for v in pick)
                        for filters in ((0,), (1,), (2,), (3,), (4,), (0, 1, 2, 3, 4)):
                            data = encode_png(w, h, ctype, depth, rows, interlace, plte, trns, filters)
                            img = png.decode(data)
                            exp = expected_rgba(w, h, ctype, depth, rows, palette, trns_vals)
                            check(img.width == w and img.height == h and img.rgba == exp,
                                  'png ctype=%d depth=%d %dx%d il=%d trns=%s filters=%s' % (ctype, depth, w, h, interlace, use_trns, filters))
                            n += 1
    print('synthetic PNG cases:', n)


def oracle_tests(d):
    n = 0
    for nm in sorted(os.listdir(d)):
        if not nm.endswith('.png'):
            continue
        with open(os.path.join(d, nm), 'rb') as f:
            data = f.read()
        argb_path = os.path.join(d, nm[:-4] + '.argb')
        if not os.path.exists(argb_path):
            continue
        with open(argb_path, 'rb') as f:
            argb = f.read()
        img = png.decode(data)
        exp = bytearray(len(argb))
        exp[0::4] = argb[2::4]  # little-endian uint32 ARGB = bytes B,G,R,A
        exp[1::4] = argb[1::4]
        exp[2::4] = argb[0::4]
        exp[3::4] = argb[3::4]
        check(img.rgba == bytes(exp), 'oracle ' + nm + ' (info %r)' % (img.info,))
        n += 1
    print('oracle PNG files checked:', n)


# ------------------------------------------------------------------ TGA
def encode_tga(w, h, rgba_rows, bits, rle, top, right):
    rows = list(rgba_rows)
    if not top:
        rows = rows[::-1]
    if right:
        rows = [b''.join(r[i:i + 4] for i in range(len(r) - 4, -1, -4)) for r in rows]
    pix = bytearray()
    for r in rows:
        for i in range(0, len(r), 4):
            R, G, B, A = r[i:i + 4]
            pix += bytes((B, G, R, A)) if bits == 32 else bytes((B, G, R))
    bpp = bits // 8
    if rle:
        body = bytearray()
        i = 0
        npx = len(pix) // bpp
        while i < npx:
            # run-length packet when the next pixel repeats, else a raw packet
            j = i + 1
            while j < npx and j - i < 128 and pix[j * bpp:(j + 1) * bpp] == pix[i * bpp:(i + 1) * bpp]:
                j += 1
            if j - i > 1:
                body.append(0x80 | (j - i - 1))
                body += pix[i * bpp:(i + 1) * bpp]
                i = j
            else:
                j = i + 1
                while j < npx and j - i < 128 and pix[j * bpp:(j + 1) * bpp] != pix[(j - 1) * bpp:j * bpp]:
                    j += 1
                body.append(j - i - 1)
                body += pix[i * bpp:j * bpp]
                i = j
        pix = body
    desc = (8 if bits == 32 else 0) | (0x20 if top else 0) | (0x10 if right else 0)
    hdr = struct.pack('<BBBHHBHHHHBB', 3, 0, 10 if rle else 2, 0, 0, 0, 0, 0, w, h, bits, desc)
    return hdr + b'abc' + bytes(pix) + b'\x00' * 26  # 3-byte image id, fake footer area


def tga_tests():
    rnd = random.Random(7)
    n = 0
    for (w, h) in [(1, 1), (4, 3), (16, 16), (5, 9)]:
        for bits in (24, 32):
            rows = []
            for y in range(h):
                r = bytearray()
                for x in range(w):
                    if rnd.random() < 0.4 and r:
                        r += r[-4:]  # repeats exercise RLE runs
                    else:
                        r += bytes((rnd.randrange(256), rnd.randrange(256), rnd.randrange(256),
                                    rnd.randrange(256) if bits == 32 else 255))
                rows.append(bytes(r))
            for rle in (False, True):
                for top in (False, True):
                    for right in (False, True):
                        data = encode_tga(w, h, rows, bits, rle, top, right)
                        img = tga.decode(data)
                        check(img.width == w and img.height == h and img.rgba == b''.join(rows),
                              'tga %dx%d bits=%d rle=%s top=%s right=%s' % (w, h, bits, rle, top, right))
                        n += 1
    print('synthetic TGA cases:', n)


def brarchive_tests():
    files = [('a.png', b'\x01\x02\x03'), ('dir_name_' + 'x' * 200 + '.tga', b''), ('c.json', b'{"k": 1}')]
    count = len(files)
    index = bytearray()
    data = bytearray()
    for name, body in files:
        e = bytearray(256)
        nb = name.encode('utf-8')
        e[0] = len(nb)
        e[1:1 + len(nb)] = nb
        e[248:256] = struct.pack('<II', len(data), len(body))
        index += e
        data += body
    blob = brarchive.MAGIC + struct.pack('<II', count, 1) + bytes(index) + bytes(data)
    got = [(nm, blob[off:off + size]) for nm, off, size in brarchive.read_index(blob)]
    check(got == files, 'brarchive round trip')
    print('brarchive round trip done')


def jsonc_tests():
    text = '{\n // comment "x"\n "a": "http://x//y", /* block */ "b": [1, 2,],\n "c": {"d": "e\\"//f",},\n}'
    got = jsonc.loads(text)
    check(got == {'a': 'http://x//y', 'b': [1, 2], 'c': {'d': 'e"//f'}}, 'jsonc parse %r' % (got,))
    print('jsonc done')


def main(argv):
    synthetic_png_tests()
    if '--oracle' in argv:
        oracle_tests(argv[argv.index('--oracle') + 1])
    tga_tests()
    brarchive_tests()
    jsonc_tests()
    if FAILS:
        print('%d FAILURES' % len(FAILS))
        return 1
    print('ALL OK')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
