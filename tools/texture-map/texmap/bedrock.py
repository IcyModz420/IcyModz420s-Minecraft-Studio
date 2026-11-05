"""Bedrock vanilla data merged over the version layers.

* blocks.json      - merged field by field per block (a later layer that only sets
                     "sound" keeps the earlier "textures"; observed in the files).
* terrain_texture  - texture_data merged per key (later layer replaces the key).
* flipbook         - merged per flipbook_texture path.
* texture sets     - <name>.texture_set.json (Vibrant Visuals PBR) give the colour
                     texture and its MER(S)/normal/heightmap companions.
* renamed ids      - from the app's bedrock_upgrade_schemas.json.gz, so that a block
                     name used by j2b (e.g. sea_lantern) can be found under the key
                     blocks.json still uses (seaLantern).
"""
import gzip
import json
import os

SIDES = ('north', 'east', 'south', 'west')
DIRS = ('up', 'down') + SIDES


def norm_path(p):
    """'textures/blocks/x' -> 'blocks/x'."""
    p = p.replace('\\', '/')
    if p.startswith('textures/'):
        p = p[len('textures/'):]
    for ext in ('.png', '.tga'):
        if p.endswith(ext):
            p = p[:-len(ext)]
    return p


class BedrockData(object):
    def __init__(self, layers, upgrade_schemas=None):
        self.layers = layers
        self.blocks = {}
        self.block_layers = {}
        for lname, j in layers.json_layers('blocks.json'):
            for k, v in j.items():
                if k == 'format_version' or not isinstance(v, dict):
                    continue
                self.blocks.setdefault(k, {}).update(v)
                self.block_layers.setdefault(k, []).append(lname)
        self.terrain = {}
        for lname, j in layers.json_layers('textures/terrain_texture.json'):
            for k, v in (j.get('texture_data') or {}).items():
                self.terrain[k] = (lname, v)
        self.flipbook = {}
        for lname, j in layers.json_layers('textures/flipbook_textures.json'):
            if isinstance(j, list):
                for e in j:
                    if isinstance(e, dict) and 'flipbook_texture' in e:
                        self.flipbook[norm_path(e['flipbook_texture'])] = (lname, e)
        self.pbr_role = {}     # rel -> (role, colour rel)
        self.texture_sets = {}  # colour rel -> {role: rel}
        for p in layers.packs:
            for rel in p.files:
                if not rel.endswith('.texture_set.json'):
                    continue
                try:
                    js = p.json(rel)
                except ValueError:
                    continue
                ts = (js or {}).get('minecraft:texture_set') or {}
                folder = norm_path(rel.rsplit('/', 1)[0])
                colour = ts.get('color')
                if not isinstance(colour, str):
                    continue
                crel = folder + '/' + colour
                roles = {}
                for role, val in ts.items():
                    if role == 'color' or not isinstance(val, str):
                        continue
                    r = folder + '/' + val
                    roles[role] = r
                    self.pbr_role[r] = (role, crel)
                self.texture_sets[crel] = roles
        self.renamed_to_old = {}
        if upgrade_schemas and os.path.isfile(upgrade_schemas):
            with gzip.open(upgrade_schemas, 'rt', encoding='utf-8') as f:
                schemas = json.load(f)
            for s in schemas if isinstance(schemas, list) else []:
                for old, new in (s.get('renamedIds') or {}).items():
                    o = old.split(':', 1)[-1]
                    n = new.split(':', 1)[-1]
                    self.renamed_to_old.setdefault(n, []).append(o)

    # ------------------------------------------------------------ terrain keys
    def terrain_entries(self, key):
        """[{'path': rel, 'index': i, 'overlay_color':..., 'tint_color':...}] for a terrain key."""
        ent = self.terrain.get(key)
        if ent is None:
            return None
        _lname, v = ent
        t = v.get('textures') if isinstance(v, dict) else None
        out = []

        def one(x, i):
            if isinstance(x, str):
                out.append({'path': norm_path(x), 'index': i})
            elif isinstance(x, dict):
                if 'path' in x:
                    d = {'path': norm_path(x['path']), 'index': i}
                    for k in ('overlay_color', 'tint_color'):
                        if k in x:
                            d[k] = x[k]
                    out.append(d)
                for var in x.get('variations') or []:
                    one(var, i)

        if isinstance(t, list):
            for i, x in enumerate(t):
                one(x, i)
        else:
            one(t, 0)
        return out

    def referenced_paths(self):
        refs = set()
        for key in self.terrain:
            for e in self.terrain_entries(key) or []:
                refs.add(e['path'])
        refs.update(self.flipbook)
        for crel, roles in self.texture_sets.items():
            refs.add(crel)
            refs.update(roles.values())
        return refs

    # ------------------------------------------------------------ blocks
    def block_entry(self, name):
        """(key used, merged entry) trying renamed ids when the modern name is absent."""
        if name in self.blocks:
            return name, self.blocks[name]
        for old in self.renamed_to_old.get(name, []):
            if old in self.blocks:
                return old, self.blocks[old]
            for older in self.renamed_to_old.get(old, []):
                if older in self.blocks:
                    return older, self.blocks[older]
        return None, None

    @staticmethod
    def _faces(spec):
        """blocks.json textures spec -> {dir: terrain key}."""
        out = {}
        if isinstance(spec, str):
            for d in DIRS:
                out[d] = spec
        elif isinstance(spec, dict):
            if 'side' in spec:
                for d in SIDES:
                    out[d] = spec['side']
            for d in DIRS:
                if d in spec:
                    out[d] = spec[d]
        return out

    def block_faces(self, name):
        """{'key': blocks.json key, 'faces': {dir: (terrain key, [entries])}, 'carried': {...}} or None."""
        key, ent = self.block_entry(name)
        if ent is None:
            return None
        res = {'key': key, 'faces': {}, 'carried': {}}
        for field, target in (('textures', 'faces'), ('carried_textures', 'carried')):
            if field not in ent:
                continue
            for d, tkey in self._faces(ent[field]).items():
                res[target][d] = (tkey, self.terrain_entries(tkey))
        return res
