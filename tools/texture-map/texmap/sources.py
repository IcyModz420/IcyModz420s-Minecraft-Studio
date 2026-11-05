"""Read-only access to the game files: Java client jars and Bedrock vanilla packs."""
import hashlib
import json
import os
import re
import zipfile

from . import brarchive, jsonc, pixels

# Release versions named in the task, oldest first. Only those with a jar on disk are used.
JAVA_RELEASES = ['b1.0', '1.2.1', '1.8.9', '1.10.2', '1.12.2', '1.16.4', '1.17.1', '1.18', '1.18.1',
                 '1.18.2', '1.19', '1.19.1', '1.19.2', '1.19.3', '1.19.4', '1.20', '1.20.1', '1.21',
                 '1.21.1', '1.21.3', '1.21.11', '26.1.1', '26.1.2', '26.2', '26.3']

# Every Java release in order (for naming the releases that fall inside an installed-jar gap).
# Source: the app's own data version table (MinecraftStudio.Core/Data/dataversions.csv) when
# available; this list is only the fallback ordering key used by version_key().


def version_key(v):
    """Sort key for Java release names (b1.0 < 1.2.1 < ... < 1.21.11 < 26.1)."""
    m = re.match(r'^b(\d+)\.(\d+)(?:\.(\d+))?$', v)
    if m:
        return (0, int(m.group(1)), int(m.group(2)), int(m.group(3) or 0))
    parts = [int(x) for x in re.findall(r'\d+', v)]
    while len(parts) < 3:
        parts.append(0)
    return (1,) + tuple(parts[:3])


def default_appdata():
    return os.environ.get('APPDATA') or os.path.join(os.path.expanduser('~'), 'AppData', 'Roaming')


def find_java_jars(versions_dir, wanted=JAVA_RELEASES):
    found, missing = [], []
    for v in wanted:
        p = os.path.join(versions_dir, v, v + '.jar')
        if os.path.isfile(p):
            found.append((v, p))
        else:
            missing.append((v, os.listdir(os.path.join(versions_dir, v)) if os.path.isdir(os.path.join(versions_dir, v)) else None))
    return found, missing


class JavaJar(object):
    TEX = 'assets/minecraft/textures/'

    def __init__(self, version, path):
        self.version = version
        self.path = path
        self.zip = zipfile.ZipFile(path)
        self.names = self.zip.namelist()
        self.nameset = set(self.names)
        self.info = {}
        if 'version.json' in self.nameset:
            try:
                self.info = json.loads(self.zip.read('version.json').decode('utf-8'))
            except ValueError:
                self.info = {}

    def read(self, name):
        return self.zip.read(name)

    def exists(self, name):
        return name in self.nameset

    def folder(self):
        """'block' (1.13+), 'blocks' (1.6-1.12) or None (atlas era)."""
        if any(n.startswith(self.TEX + 'block/') for n in self.names):
            return 'block'
        if any(n.startswith(self.TEX + 'blocks/') for n in self.names):
            return 'blocks'
        return None

    def textures_in(self, folders):
        """{name: zip_path}; name = path under textures/ without .png (subfolders kept)."""
        out = {}
        for n in self.names:
            if not n.startswith(self.TEX) or not n.endswith('.png'):
                continue
            rel = n[len(self.TEX):-4]
            top = rel.split('/', 1)[0]
            if top in folders:
                out[rel] = n
        return out

    def block_textures(self):
        return self.textures_in(('block', 'blocks'))

    def item_textures(self):
        return self.textures_in(('item', 'items'))

    def mcmeta(self, zip_path):
        p = zip_path + '.mcmeta'
        if p not in self.nameset:
            return None
        try:
            return jsonc.loads(self.zip.read(p))
        except ValueError:
            return {'_unparsable': True}

    def json(self, path):
        if path not in self.nameset:
            return None
        return jsonc.loads(self.zip.read(path))


# ------------------------------------------------------------------ Bedrock
IGNORED_PACK_PREFIXES = ('chemistry', 'editor', 'experimental', 'beta', 'oreui', 'persona',
                         'script_ui', 'platform_', 'vanilla_base', 'vanilla_music', 'vanilla_trial')


def pack_version(label):
    """'vanilla' -> (); 'vanilla_1.16.100' -> (1, 16, 100)."""
    if label == 'vanilla':
        return ()
    m = re.match(r'^vanilla_(\d+(?:\.\d+)*)$', label)
    if not m:
        return None
    return tuple(int(x) for x in m.group(1).split('.'))


def list_packs(root):
    """(vanilla_layers sorted by version, other pack names)."""
    layers, others = [], []
    for nm in sorted(os.listdir(root)):
        if not os.path.isdir(os.path.join(root, nm)):
            continue
        v = pack_version(nm)
        if v is not None:
            layers.append((v, nm))
        else:
            others.append(nm)
    layers.sort()
    return [nm for _v, nm in layers], others


def pack_label(nm):
    """Version label used in the CSV: '' for the base pack, else the version suffix."""
    return '' if nm == 'vanilla' else nm[len('vanilla_'):]


IMG_EXTS = ('.png', '.tga', '.jpg', '.jpeg')


class BedrockPack(object):
    def __init__(self, root, name):
        self.name = name
        self.dir = os.path.join(root, name)
        self.files = brarchive.walk_pack(self.dir)  # rel -> [entries]

    def read(self, rel):
        ents = self.files.get(rel)
        if not ents:
            return None
        # prefer the loose copy if both exist (collisions are reported separately)
        ents = sorted(ents, key=lambda e: 0 if e[0] == 'loose' else 1)
        return brarchive.load(ents[0])

    def collisions(self):
        return {k: v for k, v in self.files.items() if len(v) > 1}

    def images_under(self, prefix):
        """{stem: [ext, ...]} for image files under prefix (e.g. 'textures/blocks/')."""
        out = {}
        for rel in self.files:
            if not rel.startswith(prefix):
                continue
            stem, ext = os.path.splitext(rel)
            if ext.lower() in IMG_EXTS:
                out.setdefault(stem, []).append(ext)
        return out

    def json(self, rel):
        data = self.read(rel)
        if data is None:
            return None
        return jsonc.loads(data)


class BedrockLayers(object):
    def __init__(self, root):
        self.root = root
        names, others = list_packs(root)
        self.layer_names = names
        self.other_names = others
        self.packs = [BedrockPack(root, nm) for nm in names]

    def image_history(self, prefix):
        """{stem: [(pack_name, ext), ...]} in layer order (oldest first)."""
        hist = {}
        for p in self.packs:
            for stem, exts in p.images_under(prefix).items():
                for ext in sorted(exts):
                    hist.setdefault(stem, []).append((p.name, ext))
        return hist

    def pack(self, name):
        for p in self.packs:
            if p.name == name:
                return p
        return None

    def read_image(self, pack_name, stem, ext):
        return self.pack(pack_name).read(stem + ext)

    def json_layers(self, rel):
        """[(pack_name, parsed_json)] for every layer that has rel."""
        out = []
        for p in self.packs:
            if rel in p.files:
                try:
                    out.append((p.name, p.json(rel)))
                except ValueError as e:
                    out.append((p.name, {'_error': str(e)}))
        return out


# ------------------------------------------------------------------ cached image descriptions
class ImageCache(object):
    """Caches pixels.describe() results keyed by sha1 of the raw file bytes."""

    VERSION = 4

    def __init__(self, path):
        self.path = path
        self.data = {}
        self.dirty = False
        if path and os.path.isfile(path):
            try:
                with open(path, 'r', encoding='utf-8') as f:
                    blob = json.load(f)
                if blob.get('version') == self.VERSION:
                    self.data = blob.get('entries', {})
            except ValueError:
                self.data = {}

    def save(self):
        if self.path and self.dirty:
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            tmp = self.path + '.tmp'
            with open(tmp, 'w', encoding='utf-8') as f:
                json.dump({'version': self.VERSION, 'entries': self.data}, f, separators=(',', ':'))
            os.replace(tmp, self.path)
            self.dirty = False

    def describe(self, raw, ext):
        key = hashlib.sha1(raw).hexdigest() + ext.lower()
        d = self.data.get(key)
        if d is None:
            try:
                img = pixels.decode(raw, ext)
                d = pixels.describe(img.width, img.height, img.rgba)
                d['info'] = {k: v for k, v in img.info.items() if k != 'chunks'}
            except Exception as e:  # recorded and reported, never fatal
                d = {'error': '%s: %s' % (type(e).__name__, e)}
            d['raw_sha1'] = key[:40]
            self.data[key] = d
            self.dirty = True
        return d
