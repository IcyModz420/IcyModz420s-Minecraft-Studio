"""Pure-Python TGA decoder.

Handles image types 2 (true-colour) and 10 (RLE true-colour) at 24 and 32 bits,
which is what Bedrock Edition ships, plus 16-bit true-colour, 8-bit grey (3/11)
and colour-mapped (1/9) images for robustness. Image descriptor bit 5 set means
top-left origin; otherwise rows are stored bottom-up and are flipped here.
Bit 4 (right-to-left) is honoured too. Output is RGBA8, row-major, top row first.
"""
import struct

from .png import Image


class TGAError(ValueError):
    pass


def _rle(data, pos, count, bpp):
    """Decode RLE packets until `count` pixels were produced."""
    out = bytearray()
    need = count * bpp
    n = len(data)
    while len(out) < need:
        if pos >= n:
            raise TGAError('RLE data truncated')
        hdr = data[pos]
        pos += 1
        run = (hdr & 0x7f) + 1
        if hdr & 0x80:
            px = data[pos:pos + bpp]
            if len(px) != bpp:
                raise TGAError('RLE data truncated')
            pos += bpp
            out += px * run
        else:
            chunk = data[pos:pos + run * bpp]
            if len(chunk) != run * bpp:
                raise TGAError('RLE data truncated')
            pos += run * bpp
            out += chunk
    if len(out) > need:
        # A packet may not run past the end of the image; tolerate and record it.
        del out[need:]
    return out, pos


def _expand16(pix, npx):
    """A1R5G5B5 little-endian -> RGBA8."""
    out = bytearray(npx * 4)
    for i in range(npx):
        v = pix[2 * i] | (pix[2 * i + 1] << 8)
        r = (v >> 10) & 31
        g = (v >> 5) & 31
        b = v & 31
        out[4 * i] = (r << 3) | (r >> 2)
        out[4 * i + 1] = (g << 3) | (g >> 2)
        out[4 * i + 2] = (b << 3) | (b >> 2)
        out[4 * i + 3] = 255 if (v & 0x8000) else 0
    return out


def _to_rgba(pix, npx, depth):
    """Convert BGR/BGRA/grey samples to RGBA8."""
    out = bytearray(npx * 4)
    if depth == 32:
        out[0::4] = pix[2::4]
        out[1::4] = pix[1::4]
        out[2::4] = pix[0::4]
        out[3::4] = pix[3::4]
    elif depth == 24:
        out[0::4] = pix[2::3]
        out[1::4] = pix[1::3]
        out[2::4] = pix[0::3]
        out[3::4] = b'\xff' * npx
    elif depth == 16 or depth == 15:
        return _expand16(pix, npx)
    elif depth == 8:
        out[0::4] = pix
        out[1::4] = pix
        out[2::4] = pix
        out[3::4] = b'\xff' * npx
    else:
        raise TGAError('unsupported pixel depth %d' % depth)
    return out


def decode(data):
    if len(data) < 18:
        raise TGAError('file too short for a TGA header')
    id_len, cmap_type, img_type = data[0], data[1], data[2]
    cmap_first, cmap_len, cmap_depth = struct.unpack('<HHB', data[3:8])
    _x0, _y0, width, height, depth, desc = struct.unpack('<HHHHBB', data[8:18])
    if width == 0 or height == 0:
        raise TGAError('zero-sized image')
    pos = 18 + id_len
    cmap = None
    if cmap_type == 1:
        entry = (cmap_depth + 7) // 8
        raw_map = data[pos:pos + cmap_len * entry]
        pos += cmap_len * entry
        if img_type in (1, 9):
            cmap = _to_rgba(raw_map, cmap_len, cmap_depth if cmap_depth != 15 else 16)
    elif cmap_type not in (0,):
        raise TGAError('bad colour map type %d' % cmap_type)
    npx = width * height
    if img_type in (2, 10):
        if depth not in (15, 16, 24, 32):
            raise TGAError('true-colour TGA with %d bits per pixel' % depth)
    elif img_type in (3, 11):
        if depth != 8:
            raise TGAError('grey TGA with %d bits per pixel' % depth)
    elif img_type in (1, 9):
        if depth not in (8, 16) or cmap is None:
            raise TGAError('colour-mapped TGA not supported here (depth %d)' % depth)
    else:
        raise TGAError('unsupported TGA image type %d' % img_type)
    bpp = (depth + 7) // 8
    if img_type in (2, 3, 1):
        pix = data[pos:pos + npx * bpp]
        if len(pix) != npx * bpp:
            raise TGAError('pixel data truncated')
        pos += npx * bpp
    else:
        pix, pos = _rle(data, pos, npx, bpp)
    if img_type in (1, 9):
        rgba = bytearray(npx * 4)
        for i in range(npx):
            k = pix[i] if bpp == 1 else (pix[2 * i] | (pix[2 * i + 1] << 8))
            k -= cmap_first
            if not 0 <= k < cmap_len:
                raise TGAError('colour-map index out of range')
            rgba[4 * i:4 * i + 4] = cmap[4 * k:4 * k + 4]
    else:
        rgba = _to_rgba(pix, npx, depth)
    stride = width * 4
    rows = [rgba[y * stride:(y + 1) * stride] for y in range(height)]
    top_origin = bool(desc & 0x20)
    right_to_left = bool(desc & 0x10)
    if not top_origin:
        rows.reverse()
    if right_to_left:
        flipped = []
        for r in rows:
            px = [r[i:i + 4] for i in range(0, stride, 4)]
            px.reverse()
            flipped.append(b''.join(px))
        rows = flipped
    info = {
        'tga_type': img_type, 'bits': depth, 'alpha_bits': desc & 0x0f,
        'top_origin': top_origin, 'right_to_left': right_to_left,
        'trailing_bytes': len(data) - pos,
    }
    return Image(width, height, b''.join(rows), info)
