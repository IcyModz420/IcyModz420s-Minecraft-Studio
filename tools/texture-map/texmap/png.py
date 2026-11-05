"""Pure-Python PNG decoder.

Supports every colour type (0 grey, 2 RGB, 3 palette, 4 grey+alpha, 6 RGBA), every
legal bit depth (1, 2, 4, 8, 16), tRNS transparency and Adam7 interlacing.
Output is always RGBA8 (16-bit samples keep their high byte, sub-8-bit grey is
scaled to 0..255 exactly, palette indices are looked up). Ancillary chunks such
as gAMA/sRGB/iCCP are recorded but not applied: hashes describe stored values.
"""
import struct
import zlib
from itertools import accumulate, chain

SIGNATURE = b'\x89PNG\r\n\x1a\n'
ADAM7 = ((0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
         (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2))
CHANNELS = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
LEGAL_DEPTHS = {0: (1, 2, 4, 8, 16), 2: (8, 16), 3: (1, 2, 4, 8), 4: (8, 16), 6: (8, 16)}

_AND255 = (255).__and__


class PNGError(ValueError):
    pass


class Image(object):
    """Decoded image: width, height, rgba (bytes, row-major, 4 bytes per pixel)."""
    __slots__ = ('width', 'height', 'rgba', 'info')

    def __init__(self, width, height, rgba, info=None):
        self.width = width
        self.height = height
        self.rgba = bytes(rgba)
        self.info = info or {}
        if len(self.rgba) != width * height * 4:
            raise PNGError('internal: rgba length %d != %dx%dx4' % (len(self.rgba), width, height))


# ---------------------------------------------------------------- unpack tables
def _make_unpack(depth):
    per = 8 // depth
    mask = (1 << depth) - 1
    table = []
    for b in range(256):
        table.append(tuple((b >> (8 - depth * (i + 1))) & mask for i in range(per)))
    return table


_UNPACK = {1: _make_unpack(1), 2: _make_unpack(2), 4: _make_unpack(4)}
_MASKS = {}


def _masks(n):
    m = _MASKS.get(n)
    if m is None:
        m = (int.from_bytes(b'\x7f' * n, 'little'), int.from_bytes(b'\x80' * n, 'little'))
        if len(_MASKS) < 4096:
            _MASKS[n] = m
    return m


def _add_bytes(a, b):
    """Bytewise (a + b) mod 256 for equal-length byte strings, using big-int SWAR."""
    n = len(a)
    if n == 0:
        return bytearray()
    x = int.from_bytes(a, 'little')
    y = int.from_bytes(b, 'little')
    m7, m8 = _masks(n)
    s = ((x & m7) + (y & m7)) ^ ((x ^ y) & m8)
    return bytearray(s.to_bytes(n, 'little'))


# ---------------------------------------------------------------- chunks
def iter_chunks(data, check_crc=True):
    if data[:8] != SIGNATURE:
        raise PNGError('not a PNG file (bad signature)')
    pos = 8
    n = len(data)
    while True:
        if pos + 8 > n:
            raise PNGError('truncated: no IEND chunk')
        length, ctype = struct.unpack('>I4s', data[pos:pos + 8])
        start = pos + 8
        end = start + length
        if end + 4 > n:
            raise PNGError('truncated chunk %r' % ctype)
        body = data[start:end]
        if check_crc:
            crc = struct.unpack('>I', data[end:end + 4])[0]
            if (zlib.crc32(ctype + body) & 0xffffffff) != crc:
                raise PNGError('CRC mismatch in chunk %r' % ctype)
        yield ctype, body
        pos = end + 4
        if ctype == b'IEND':
            return


# ---------------------------------------------------------------- unfiltering
def _unfilter_pass(raw, pos, width, height, bits_pp):
    """Undo PNG filters for one (sub)image. Returns (rows, new_pos)."""
    bpp = max(1, bits_pp // 8)
    rowlen = (width * bits_pp + 7) // 8
    prev = bytearray(rowlen)
    rows = []
    for _y in range(height):
        if pos + 1 + rowlen > len(raw):
            raise PNGError('image data too short')
        ft = raw[pos]
        row = bytearray(raw[pos + 1:pos + 1 + rowlen])
        pos += 1 + rowlen
        if ft == 0:
            pass
        elif ft == 1:  # Sub
            for c in range(min(bpp, rowlen)):
                row[c::bpp] = bytearray(map(_AND255, accumulate(row[c::bpp])))
        elif ft == 2:  # Up
            row = _add_bytes(row, prev)
        elif ft == 3:  # Average
            for i in range(min(bpp, rowlen)):
                row[i] = (row[i] + (prev[i] >> 1)) & 255
            for i in range(bpp, rowlen):
                row[i] = (row[i] + ((row[i - bpp] + prev[i]) >> 1)) & 255
        elif ft == 4:  # Paeth
            for i in range(min(bpp, rowlen)):
                row[i] = (row[i] + prev[i]) & 255  # a = c = 0 -> predictor is b
            for i in range(bpp, rowlen):
                a = row[i - bpp]
                b = prev[i]
                c = prev[i - bpp]
                pa = b - c
                pb = a - c
                pc = pa + pb
                if pa < 0:
                    pa = -pa
                if pb < 0:
                    pb = -pb
                if pc < 0:
                    pc = -pc
                if pa <= pb and pa <= pc:
                    pr = a
                elif pb <= pc:
                    pr = b
                else:
                    pr = c
                row[i] = (row[i] + pr) & 255
        else:
            raise PNGError('bad filter type %d' % ft)
        rows.append(row)
        prev = row
    return rows, pos


# ---------------------------------------------------------------- sample conversion
def _samples(row, depth, count):
    """Samples of a scanline as a list of ints (16-bit kept full)."""
    if depth == 8:
        return row[:count]
    if depth == 16:
        return [(row[i] << 8) | row[i + 1] for i in range(0, count * 2, 2)]
    t = _UNPACK[depth]
    return list(chain.from_iterable(map(t.__getitem__, row)))[:count]


def _row_to_rgba(row, width, ctype, depth, palette, trns):
    ch = CHANNELS[ctype]
    out = bytearray(width * 4)
    if ctype == 3:
        idx = _samples(row, depth, width)
        try:
            return bytearray(b''.join(map(palette.__getitem__, idx)))
        except IndexError:
            raise PNGError('palette index out of range')
    if depth == 8:
        s8 = row
    elif depth == 16:
        s8 = row[0::2]  # high bytes, big-endian samples
    else:
        raw = _samples(row, depth, width * ch)  # only grey (ctype 0) has depth < 8
        scale = 255 // ((1 << depth) - 1)
        s8 = bytearray(v * scale for v in raw)
    if ctype == 0:
        g = s8[:width]
        out[0::4] = g
        out[1::4] = g
        out[2::4] = g
        out[3::4] = b'\xff' * width
        if trns is not None:
            key = struct.unpack('>H', trns[:2])[0]
            full = _samples(row, depth, width)
            for x, v in enumerate(full):
                if v == key:
                    out[x * 4 + 3] = 0
    elif ctype == 2:
        out[0::4] = s8[0::3][:width]
        out[1::4] = s8[1::3][:width]
        out[2::4] = s8[2::3][:width]
        out[3::4] = b'\xff' * width
        if trns is not None:
            kr, kg, kb = struct.unpack('>HHH', trns[:6])
            full = _samples(row, depth, width * 3)
            for x in range(width):
                if full[3 * x] == kr and full[3 * x + 1] == kg and full[3 * x + 2] == kb:
                    out[x * 4 + 3] = 0
    elif ctype == 4:
        g = s8[0::2][:width]
        out[0::4] = g
        out[1::4] = g
        out[2::4] = g
        out[3::4] = s8[1::2][:width]
    elif ctype == 6:
        out[:] = s8[:width * 4]
    else:
        raise PNGError('bad colour type %d' % ctype)
    return out


# ---------------------------------------------------------------- decode
def decode(data, check_crc=True):
    """Decode PNG bytes into an Image (RGBA8)."""
    ihdr = None
    plte = None
    trns = None
    idat = []
    seen = []
    for ctype, body in iter_chunks(data, check_crc):
        seen.append(ctype.decode('latin-1'))
        if ctype == b'IHDR':
            if len(body) != 13:
                raise PNGError('bad IHDR length')
            ihdr = struct.unpack('>IIBBBBB', body)
        elif ctype == b'PLTE':
            plte = body
        elif ctype == b'tRNS':
            trns = body
        elif ctype == b'IDAT':
            idat.append(body)
    if ihdr is None:
        raise PNGError('missing IHDR')
    width, height, depth, ctype, comp, filt, interlace = ihdr
    if ctype not in CHANNELS or depth not in LEGAL_DEPTHS[ctype]:
        raise PNGError('illegal colour type/bit depth %d/%d' % (ctype, depth))
    if comp != 0 or filt != 0 or interlace not in (0, 1):
        raise PNGError('unsupported compression/filter/interlace method')
    if width == 0 or height == 0:
        raise PNGError('zero-sized image')
    palette = None
    if ctype == 3:
        if plte is None or len(plte) % 3:
            raise PNGError('missing or bad PLTE')
        n = len(plte) // 3
        alphas = trns or b''
        palette = []
        for i in range(n):
            a = alphas[i] if i < len(alphas) else 255
            palette.append(bytes((plte[3 * i], plte[3 * i + 1], plte[3 * i + 2], a)))
        trns_used = trns
        trns = None  # handled through the palette
    else:
        trns_used = trns
    try:
        raw = zlib.decompress(b''.join(idat))
    except zlib.error as e:
        raise PNGError('zlib: %s' % e)
    bits_pp = CHANNELS[ctype] * depth
    if interlace == 0:
        rows, pos = _unfilter_pass(raw, 0, width, height, bits_pp)
        rgba = bytearray().join(_row_to_rgba(r, width, ctype, depth, palette, trns) for r in rows)
    else:
        out_rows = [bytearray(width * 4) for _ in range(height)]
        pos = 0
        for (x0, y0, dx, dy) in ADAM7:
            pw = (width - x0 + dx - 1) // dx if width > x0 else 0
            ph = (height - y0 + dy - 1) // dy if height > y0 else 0
            if pw == 0 or ph == 0:
                continue
            rows, pos = _unfilter_pass(raw, pos, pw, ph, bits_pp)
            for j, r in enumerate(rows):
                px = _row_to_rgba(r, pw, ctype, depth, palette, trns)
                target = out_rows[y0 + j * dy]
                for c in range(4):
                    start = x0 * 4 + c
                    stop = start + (pw - 1) * dx * 4 + 1
                    target[start:stop:dx * 4] = px[c::4]
        rgba = bytearray().join(out_rows)
    info = {
        'bit_depth': depth, 'colour_type': ctype, 'interlace': interlace,
        'trns': trns_used is not None, 'chunks': seen,
        'trailing_bytes': len(raw) - pos,
    }
    return Image(width, height, rgba, info)


def read_size(data):
    """(width, height) from IHDR without decoding."""
    if data[:8] != SIGNATURE or data[12:16] != b'IHDR':
        raise PNGError('not a PNG file')
    return struct.unpack('>II', data[16:24])
