"""Pixel decoding, hashing and comparison helpers.

Hashes: sha1 over struct.pack('>II', width, height) + RGBA8 bytes.
  exact  - the stored RGBA values.
  norm   - RGB of fully transparent pixels (alpha == 0) zeroed first. Two images
           with equal `norm` hashes look identical in game; `exact` may differ only
           in the invisible colour behind alpha 0. "Pixel identity" in this toolkit
           means equal `norm` hashes; the report says when `exact` differs too.
"""
import hashlib
import struct

from . import png, tga


def decode(data, ext):
    ext = ext.lower()
    if ext == '.png':
        return png.decode(data)
    if ext == '.tga':
        return tga.decode(data)
    raise ValueError('unsupported image extension %r' % ext)


def hash_rgba(w, h, rgba):
    return hashlib.sha1(struct.pack('>II', w, h) + bytes(rgba)).hexdigest()


def normalized(rgba):
    a = rgba[3::4]
    if 0 not in a:
        return rgba
    out = bytearray(rgba)
    i = a.find(0)
    while i >= 0:
        out[4 * i:4 * i + 3] = b'\x00\x00\x00'
        i = a.find(0, i + 1)
    return bytes(out)


def crop(w, h, rgba, x0, y0, cw, ch):
    stride = w * 4
    return b''.join(rgba[(y0 + y) * stride + x0 * 4:(y0 + y) * stride + (x0 + cw) * 4] for y in range(ch))


def frames(w, h, rgba):
    """Square frames of a vertical strip (h a multiple of w, h > w); else [whole image]."""
    if h > w and h % w == 0:
        n = h // w
        size = w * w * 4
        return [rgba[i * size:(i + 1) * size] for i in range(n)]
    return [rgba]


def describe(w, h, rgba):
    """Hashes and simple statistics for one decoded image."""
    norm = normalized(rgba)
    fr = frames(w, h, norm)
    fw = w
    fh = h if len(fr) == 1 else w
    alpha = rgba[3::4]
    r, g, b = rgba[0::4], rgba[1::4], rgba[2::4]
    opaque = alpha.count(255)
    clear = alpha.count(0)
    grey = (r == g == b) if len(rgba) else True
    if not grey:
        # grey if every visible pixel has r == g == b
        grey = all(r[i] == g[i] == b[i] for i in range(len(alpha)) if alpha[i])
    uniform = len(norm) >= 4 and norm[0:4] * (len(norm) // 4) == norm
    d = {
        'w': w, 'h': h,
        'exact': hash_rgba(w, h, rgba),
        'norm': hash_rgba(w, h, norm),
        'frames': len(fr),
        'frame_norm': [hash_rgba(fw, fh, f) for f in fr] if len(fr) > 1 else None,
        'alpha_mask': hash_rgba(w, h, bytes(255 if x else 0 for x in alpha)),
        'opaque_px': opaque, 'clear_px': clear,
        'grey': bool(grey),
        # every pixel the same (e.g. fully transparent): pixel identity proves nothing
        'uniform': bool(uniform),
    }
    return d


# ---------------------------------------------------------------- tint analysis
def _lum(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def tint_relation(a, b, tol=4):
    """Compare two decoded images of equal size that are not pixel-identical.

    Returns None if sizes differ, else a dict:
      same_alpha  - alpha channels equal
      relation    - 'tint' (every visible pixel of one is the other times a constant
                    per-channel factor; typical of grey-vs-precoloured pairs),
                    'partial-tint' (pixels are either identical or follow one tint),
                    'luminance' (same alpha and luminance correlation >= 0.98 but no
                    single tint), or 'different'
      direction   - 'a_grey' / 'b_grey' / 'both_colour'
      tint        - (r, g, b) factor 0..255 applied to the grey one, if any
      corr        - Pearson correlation of luminance over visible pixels
    """
    (wa, ha, ra), (wb, hb, rb) = a, b
    if (wa, ha) != (wb, hb):
        return None
    alpha_a = ra[3::4]
    alpha_b = rb[3::4]
    same_alpha = alpha_a == alpha_b
    vis = [i for i in range(len(alpha_a)) if alpha_a[i] and alpha_b[i]]
    if not vis:
        return {'same_alpha': same_alpha, 'relation': 'different', 'direction': None, 'tint': None, 'corr': 0.0}

    def chan(rgba, c):
        return [rgba[4 * i + c] for i in vis]

    A = [chan(ra, c) for c in range(3)]
    B = [chan(rb, c) for c in range(3)]
    a_grey = all(A[0][k] == A[1][k] == A[2][k] for k in range(len(vis)))
    b_grey = all(B[0][k] == B[1][k] == B[2][k] for k in range(len(vis)))
    la = [_lum(A[0][k], A[1][k], A[2][k]) for k in range(len(vis))]
    lb = [_lum(B[0][k], B[1][k], B[2][k]) for k in range(len(vis))]
    n = len(vis)
    ma, mb = sum(la) / n, sum(lb) / n
    va = sum((x - ma) ** 2 for x in la)
    vb = sum((x - mb) ** 2 for x in lb)
    cov = sum((la[k] - ma) * (lb[k] - mb) for k in range(n))
    corr = cov / (va * vb) ** 0.5 if va > 0 and vb > 0 else (1.0 if va == vb == 0 else 0.0)

    def fit(src, dst):
        """dst_c ~= src_c * t_c / 255 for each channel; returns (tint, max_err, n_identical, n_fit)."""
        tint = []
        maxerr = 0.0
        for c in range(3):
            s = src[c]
            d = dst[c]
            ss = sum(x * x for x in s)
            t = (sum(s[k] * d[k] for k in range(n)) / ss) if ss else 0.0
            tint.append(t)
        errs = []
        for k in range(n):
            e = max(abs(dst[c][k] - src[c][k] * tint[c]) for c in range(3))
            errs.append(e)
            maxerr = max(maxerr, e)
        return [round(255 * t) for t in tint], maxerr, errs

    res = {'same_alpha': same_alpha, 'corr': round(corr, 4), 'tint': None,
           'direction': 'a_grey' if a_grey and not b_grey else ('b_grey' if b_grey and not a_grey else 'both_colour')}
    diff = [k for k in range(n) if (A[0][k], A[1][k], A[2][k]) != (B[0][k], B[1][k], B[2][k])]
    suffix = '' if same_alpha else '-alpha-differs'
    if not diff:
        # colours equal wherever both are visible: only the alpha channel differs
        res['relation'] = 'identical-colour' + suffix
        return res
    if a_grey and not b_grey:
        tint, maxerr, errs = fit(A, B)
    elif b_grey and not a_grey:
        tint, maxerr, errs = fit(B, A)
    else:
        tint, maxerr, errs = fit(A, B)
    if maxerr <= tol:
        res['tint'] = tint
        res['relation'] = ('near-identical' if all(t >= 250 for t in tint) else 'tint') + suffix
        return res
    # partial tint: identical pixels plus pixels following one tint
    if diff and len(diff) < n:
        src, dst = (A, B) if not (b_grey and not a_grey) else (B, A)
        sub_src = [[src[c][k] for k in diff] for c in range(3)]
        sub_dst = [[dst[c][k] for k in diff] for c in range(3)]
        m = len(diff)
        tint2 = []
        for c in range(3):
            ss = sum(x * x for x in sub_src[c])
            tint2.append((sum(sub_src[c][k] * sub_dst[c][k] for k in range(m)) / ss) if ss else 0.0)
        maxerr2 = max(max(abs(sub_dst[c][k] - sub_src[c][k] * tint2[c]) for c in range(3)) for k in range(m))
        if maxerr2 <= tol:
            res['relation'] = 'partial-tint' + suffix
            res['tint'] = [round(255 * t) for t in tint2]
            res['tinted_px'] = m
            res['visible_px'] = n
            return res
    if corr >= 0.98:
        res['relation'] = 'luminance' + suffix
        return res
    res['relation'] = 'different'
    res['same_px'] = n - len(diff)
    res['visible_px'] = n
    return res


def colour_overlap(a, b):
    """Fast check on two normalised RGBA8 images of equal size.

    Returns (same, both, a_vis, b_vis): pixels visible in both with equal RGB,
    pixels visible in both, and visible pixels of each image."""
    aa = a[3::4]
    ba = b[3::4]
    same = both = 0
    for p in range(len(aa)):
        if aa[p] and ba[p]:
            both += 1
            o = 4 * p
            if a[o:o + 3] == b[o:o + 3]:
                same += 1
    return same, both, len(aa) - aa.count(0), len(ba) - ba.count(0)


def union_identity(a, b):
    """Share of pixels visible in either image that are identical (RGBA, normalised)."""
    aa = a[3::4]
    ba = b[3::4]
    same = union = 0
    for p in range(len(aa)):
        if aa[p] or ba[p]:
            union += 1
            o = 4 * p
            if a[o:o + 4] == b[o:o + 4]:
                same += 1
    return (same / float(union)) if union else 0.0


RELATED = ('identical-colour', 'near-identical', 'tint', 'partial-tint', 'luminance')


def related(rel):
    """True when a tint_relation result says the two images show the same art."""
    if not rel:
        return False
    r = rel['relation']
    return any(r == x or r == x + '-alpha-differs' for x in RELATED)


def nearest_downscale(w, h, rgba, k):
    """Return (w//k, h//k, rgba) if the image is an exact k-times nearest-neighbour upscale, else None."""
    if w % k or h % k:
        return None
    nw, nh = w // k, h // k
    stride = w * 4
    out = bytearray()
    for y in range(nh):
        for x in range(nw):
            px = rgba[(y * k) * stride + x * k * 4:(y * k) * stride + x * k * 4 + 4]
            for dy in range(k):
                row = (y * k + dy) * stride
                for dx in range(k):
                    o = row + (x * k + dx) * 4
                    if rgba[o:o + 4] != px:
                        return None
            out += px
    return nw, nh, bytes(out)
