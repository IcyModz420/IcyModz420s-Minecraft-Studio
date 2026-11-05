"""Java Edition blockstate + block model resolution (1.8 - 26.x formats).

For every blockstate file this yields "cases": (variant key or multipart condition,
[model specs]). For every model it resolves the parent chain, merges `textures`,
resolves `#var` references and lists the faces of the nearest `elements` with their
textures and tint flags, in model space (blockstate x/y rotation is not applied:
Bedrock's blocks.json also describes the unrotated block).
"""
DIRS = ('down', 'up', 'north', 'south', 'west', 'east')


def strip_ns(ref):
    return ref.split(':', 1)[1] if ':' in ref else ref


class ModelError(Exception):
    pass


class JavaModels(object):
    def __init__(self, jar):
        self.jar = jar
        self.legacy = jar.folder() == 'blocks'  # 1.6 - 1.12 layout
        self._raw = {}
        self._resolved = {}
        self.missing = set()

    # ------------------------------------------------------------ files
    def blockstate_names(self):
        p = 'assets/minecraft/blockstates/'
        return sorted(n[len(p):-5] for n in self.jar.names if n.startswith(p) and n.endswith('.json'))

    def blockstate(self, name):
        return self.jar.json('assets/minecraft/blockstates/%s.json' % name)

    def _model_path(self, ref, from_blockstate):
        ref = strip_ns(ref)
        if self.legacy and from_blockstate and not ref.startswith('block/'):
            ref = 'block/' + ref
        return 'assets/minecraft/models/%s.json' % ref

    def _load_raw(self, path):
        if path not in self._raw:
            try:
                self._raw[path] = self.jar.json(path)
            except ValueError:
                self._raw[path] = None
        return self._raw[path]

    # ------------------------------------------------------------ textures
    def tex_name(self, value):
        """Normalise a texture reference to 'block/x' / 'blocks/x' (path under textures/)."""
        return strip_ns(value)

    def resolve(self, ref, from_blockstate=True):
        """Resolved model dict: {'name', 'chain', 'textures', 'faces': {dir: [(tex, tinted)]}, 'elements': n}."""
        path = self._model_path(ref, from_blockstate)
        if path in self._resolved:
            return self._resolved[path]
        chain = []
        seen = set()
        cur = path
        models = []
        while cur and cur not in seen:
            seen.add(cur)
            raw = self._load_raw(cur)
            if raw is None:
                if not cur.startswith('assets/minecraft/models/builtin/'):
                    self.missing.add(cur)
                break
            models.append(raw)
            chain.append(cur[len('assets/minecraft/models/'):-5])
            parent = raw.get('parent')
            cur = self._model_path(parent, False) if parent else None
        textures = {}
        for raw in reversed(models):
            for k, v in (raw.get('textures') or {}).items():
                textures[k] = v
        elements = None
        for raw in models:
            if 'elements' in raw:
                elements = raw['elements']
                break

        def res(v, depth=0):
            if v is None:
                return None
            if isinstance(v, dict):  # 1.21.x allows {"sprite": ..., "force_translucent": ...}
                v = v.get('sprite')
                if v is None:
                    return None
            if v.startswith('#'):
                if depth > 16:
                    return None
                return res(textures.get(v[1:]), depth + 1)
            return self.tex_name(v)

        rtex = {k: res(v) for k, v in textures.items()}
        faces = {}
        for el in elements or []:
            for d, f in (el.get('faces') or {}).items():
                t = res(f.get('texture'))
                faces.setdefault(d, []).append((t, 'tintindex' in f))
        out = {'name': path, 'chain': chain, 'textures': rtex, 'faces': faces,
               'elements': len(elements or []), 'particle': rtex.get('particle')}
        self._resolved[path] = out
        return out

    # ------------------------------------------------------------ blockstates
    @staticmethod
    def _specs(v):
        if isinstance(v, list):
            return [x for x in v if isinstance(x, dict)]
        if isinstance(v, dict):
            return [v]
        return []

    def cases(self, name):
        """[(kind, key, [spec...])]; kind 'variant' (key = frozenset of (prop, value)) or 'multipart'."""
        bs = self.blockstate(name)
        out = []
        if not bs:
            return out
        if 'variants' in bs:
            for key, v in bs['variants'].items():
                props = parse_variant_key(key)
                out.append(('variant', props, self._specs(v)))
        if 'multipart' in bs:
            for part in bs['multipart']:
                out.append(('multipart', part.get('when'), self._specs(part.get('apply'))))
        return out

    def model_faces(self, spec):
        m = self.resolve(spec.get('model', ''), True)
        return m


def parse_variant_key(key):
    key = key.strip()
    if key in ('', 'normal', 'all', 'inventory'):
        return frozenset()
    props = []
    for part in key.split(','):
        if '=' in part:
            k, v = part.split('=', 1)
            props.append((k.strip(), v.strip()))
    return frozenset(props)


def face_signature(model):
    """{dir: tuple(textures)} for a resolved model (None entries dropped)."""
    out = {}
    for d, lst in model['faces'].items():
        ts = tuple(t for t, _tint in lst if t)
        if ts:
            out[d] = ts
    return out


def tinted_textures(model):
    return {t for lst in model['faces'].values() for t, tint in lst if t and tint}
