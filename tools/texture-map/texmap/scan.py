"""Scan game files into texture records (names, sizes, hashes; never pixels)."""
import os

from . import pixels, jsonc


class TexRec(object):
    __slots__ = ('platform', 'source', 'name', 'path', 'ext', 'desc', 'anim', 'extra')

    def __init__(self, platform, source, name, path, ext, desc, anim=None, extra=None):
        self.platform = platform  # 'java' / 'bedrock'
        self.source = source      # java version or bedrock pack name
        self.name = name          # e.g. 'block/oak_planks' or 'blocks/planks_oak'
        self.path = path          # zip path or pack-relative path
        self.ext = ext
        self.desc = desc          # pixels.describe() dict (+ 'error')
        self.anim = anim          # mcmeta animation dict / flipbook entry
        self.extra = extra or {}

    @property
    def ok(self):
        return 'error' not in self.desc

    def size(self):
        return (self.desc.get('w'), self.desc.get('h'))

    def norm(self):
        return self.desc.get('norm')


def scan_java(jar, cache, folders):
    """{name: TexRec} for the jar's textures in the given top-level folders."""
    out = {}
    for name, zp in sorted(jar.textures_in(folders).items()):
        raw = jar.read(zp)
        desc = cache.describe(raw, '.png')
        meta = jar.mcmeta(zp)
        anim = None
        extra = {'mcmeta': meta is not None, 'mcmeta_keys': sorted(meta) if isinstance(meta, dict) else []}
        if isinstance(meta, dict):
            if isinstance(meta.get('animation'), dict):
                anim = meta['animation']
            if isinstance(meta.get('texture'), dict):
                extra['mcmeta_texture'] = meta['texture']
        out[name] = TexRec('java', jar.version, name, zp, '.png', desc, anim, extra)
    return out


def scan_java_atlas(jar, cache):
    """Pre-1.5 terrain.png as a TexRec (or None)."""
    for cand in ('terrain.png',):
        if jar.exists(cand):
            raw = jar.read(cand)
            desc = cache.describe(raw, '.png')
            return TexRec('java_atlas', jar.version, 'terrain', cand, '.png', desc), raw
    return None, None


EXT_ORDER = ('.png', '.tga')


def scan_bedrock(layers, cache, prefix, exclude=()):
    """Layered Bedrock images under prefix.

    Returns (current, history):
      current: {stem_rel: TexRec}   top-most layer wins (stem_rel = path under textures/, no ext)
      history: {stem_rel: [TexRec per layer occurrence, oldest first]}
    When one layer has the same stem with two extensions, both are recorded and the
    record gets extra['same_layer_exts']. `exclude`: path prefixes to skip.
    """
    hist = layers.image_history(prefix)
    current, history = {}, {}
    for stem, occ in sorted(hist.items()):
        if any(stem.startswith(x) for x in exclude):
            continue
        rel = stem[len('textures/'):] if stem.startswith('textures/') else stem
        recs = []
        per_layer = {}
        for pack_name, ext in occ:
            per_layer.setdefault(pack_name, []).append(ext)
        for pack_name, ext in occ:
            raw = layers.read_image(pack_name, stem, ext)
            if raw is None or len(raw) == 0:
                desc = {'error': 'empty file'}
            else:
                desc = cache.describe(raw, ext)
            extra = {}
            if len(per_layer[pack_name]) > 1:
                extra['same_layer_exts'] = sorted(per_layer[pack_name])
            recs.append(TexRec('bedrock', pack_name, rel, stem + ext, ext, desc, None, extra))
        history[rel] = recs
        # top-most layer; within that layer prefer the ext listed first in EXT_ORDER... both kept in history
        top_pack = recs[-1].source
        tops = [r for r in recs if r.source == top_pack]
        current[rel] = tops[0] if len(tops) == 1 else sorted(tops, key=lambda r: EXT_ORDER.index(r.ext) if r.ext in EXT_ORDER else 9)[0]
    return current, history
