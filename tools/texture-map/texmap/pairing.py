"""Java (newest jar) <-> Bedrock (layered vanilla) block texture pairing.

Primary evidence: identical pixels (equal `norm` hashes; animated strips may also
match as identical frame sets, a first frame, or an exact nearest-neighbour scale).
Secondary evidence: model pairing. Every Java block state in the app's j2b table is
resolved to Java model faces (model space) and to the Bedrock block's blocks.json
faces (via terrain_texture.json); up/down pair directly, the four sides pair when a
unique rotation of the side ring makes the two patterns agree.
"""
import collections

from . import javahist, pixels
from .bedrock import SIDES

RING = ('north', 'east', 'south', 'west')


def when_matches(when, props):
    if when is None:
        return True
    if not isinstance(when, dict):
        return False
    if 'OR' in when:
        return any(when_matches(w, props) for w in when['OR'])
    if 'AND' in when:
        return all(when_matches(w, props) for w in when['AND'])
    for k, v in when.items():
        want = str(v).lower().split('|')
        if str(props.get(k, '')).lower() not in want:
            return False
    return True


def java_state_faces(models, block, props, _cache={}):
    key = (id(models), block, tuple(sorted(props.items())))
    if key in _cache:
        return _cache[key]
    pset = frozenset(props.items())
    faces = collections.OrderedDict()
    tinted = set()
    applied = 0
    for kind, k, specs in models.cases(block):
        ok = (k <= pset) if kind == 'variant' else when_matches(k, props)
        if not ok:
            continue
        m, _rot = javahist.first_model(models, specs)
        if not m:
            continue
        applied += 1
        for d, lst in m['faces'].items():
            cur = faces.setdefault(d, [])
            for t, tint in lst:
                if t and t not in cur:
                    cur.append(t)
                if t and tint:
                    tinted.add(t)
    res = (dict(faces), tinted, applied)
    _cache[key] = res
    return res


def ring_pairs(jr, br):
    """jr/br: lists of 4 single textures (N,E,S,W). Pairs consistent with every valid rotation."""
    valid = []
    for r in range(4):
        ok = True
        for i in range(4):
            for j in range(4):
                if (jr[i] == jr[j]) != (br[(i + r) % 4] == br[(j + r) % 4]):
                    ok = False
                    break
            if not ok:
                break
        if ok:
            valid.append(frozenset((jr[i], br[(i + r) % 4]) for i in range(4)))
    if not valid:
        return None
    if len(set(valid)) == 1:
        return set(valid[0])
    return None


def face_pairs(jfaces, bfaces):
    """jfaces: {dir: [java tex]}, bfaces: {dir: bedrock rel}. Returns (pairs, issues)."""
    pairs = set()
    issues = []
    for d in ('up', 'down'):
        jt, bt = jfaces.get(d), bfaces.get(d)
        if jt and bt:
            if len(jt) == 1:
                pairs.add((jt[0], bt))
            else:
                issues.append('%s: java has %d layers' % (d, len(jt)))
    jr = [jfaces.get(d) for d in RING]
    br = [bfaces.get(d) for d in RING]
    if all(jr) and all(br):
        if all(len(x) == 1 for x in jr):
            rp = ring_pairs([x[0] for x in jr], br)
            if rp is None:
                issues.append('sides: rotation ambiguous')
            else:
                pairs |= rp
        else:
            # overlay layers (e.g. grass side + tinted overlay): pair only if Bedrock has one texture all round
            if len(set(br)) == 1 and len(set(x[0] for x in jr)) == 1:
                pairs.add((jr[0][0], br[0]))
                issues.append('sides: java overlay layer not paired')
            else:
                issues.append('sides: layered')
    return pairs, issues


class ModelVotes(object):
    def __init__(self):
        self.votes = collections.defaultdict(set)          # (j, b) -> {java blocks}
        self.full = collections.defaultdict(set)           # subset of votes from full-cube Java models
        self.carried = collections.defaultdict(set)
        self.variants = collections.defaultdict(set)       # (j, b) from array entries > 0
        self.block_map = collections.defaultdict(set)      # java block -> {bedrock key}
        self.unresolved_bedrock = collections.Counter()    # bedrock block names without blocks.json entry
        self.issues = collections.defaultdict(set)
        self.array_faces = collections.Counter()           # terrain keys skipped (index not derivable)

    def candidates(self, j, carried=False):
        """[(bedrock, n_blocks, n_full_cube_blocks)] best first."""
        store = self.carried if carried else self.votes
        out = []
        for (a, b), s in store.items():
            if a == j:
                out.append((b, len(s), len(self.full.get((a, b), ())) if not carried else 0))
        out.sort(key=lambda x: (-x[2], -x[1], x[0]))
        return out


# Properties that only orient or connect a block; representative states keep them at default.
ORIENTATION = {'axis', 'facing', 'rotation', 'face', 'attachment', 'hinge', 'orientation', 'shape',
               'north', 'south', 'east', 'west', 'up', 'down', 'in_wall', 'open', 'waterlogged',
               'distance', 'persistent', 'power', 'note', 'instrument', 'mode', 'delay', 'locked',
               'triggered', 'enabled', 'snowy', 'bottom', 'conditional', 'tilt', 'vertical_direction',
               'thickness', 'leaves', 'flower_amount', 'segment_amount', 'hanging', 'unstable',
               'has_book', 'has_record', 'occupied', 'signal_fire', 'drag', 'attached', 'disarmed',
               'short'}


def _is_full_cube(models, block, props):
    for kind, k, specs in models.cases(block):
        ok = (k <= frozenset(props.items())) if kind == 'variant' else when_matches(k, props)
        if ok:
            m, _ = javahist.first_model(models, specs)
            if m and m['elements'] == 1:
                sig = set(m['faces'])
                if len(sig) == 6:
                    return True
            return False
    return False


def model_votes(models, bd, j2b_pairs, parse_state, defaults=None):
    """Vote Java texture <-> Bedrock texture pairs from blocks linked by j2b.

    defaults: {java block: default props}. Only representative states are used: the
    default state and states that differ from it in non-orientation properties
    (orientation-only states use rotated or horizontal models that do not match
    Bedrock's canonical blocks.json layout). Bedrock faces whose terrain key is an
    array of different textures are skipped: the array index is chosen in game code.
    """
    defaults = defaults or {}
    mv = ModelVotes()
    per_block = collections.defaultdict(lambda: collections.defaultdict(set))   # jb -> (j,b) -> {'faces'/'carried'}
    by_block_states = collections.defaultdict(list)
    for jstate, bstate in j2b_pairs:
        jb, jprops = parse_state(jstate)
        by_block_states[jb].append((jprops, parse_state(bstate)[0]))
    for jb, states in by_block_states.items():
        dflt = defaults.get(jb)
        if dflt is None and states:
            dflt = states[0][0]
        reps = []
        for jprops, bb in states:
            orient_same = all(jprops.get(k) == dflt.get(k) for k in set(jprops) | set(dflt) if k in ORIENTATION)
            if orient_same:
                reps.append((jprops, bb))
        if not reps:
            reps = states[:1]
        full_cube = _is_full_cube(models, jb, reps[0][0])
        block_pairs = collections.defaultdict(set)   # j -> {b} (faces), for consistency
        carried_pairs = set()
        for jprops, bb in reps:
            jfaces, _tinted, applied = java_state_faces(models, jb, jprops)
            if not jfaces:
                continue
            bf = bd.block_faces(bb)
            if bf is None:
                mv.unresolved_bedrock[bb] += 1
                continue
            mv.block_map[jb].add(bf['key'])
            for target in ('faces', 'carried'):
                primary = {}
                for d, (tkey, ents) in bf[target].items():
                    if not ents:
                        continue
                    distinct = []
                    for e in ents:
                        if e['path'] not in distinct:
                            distinct.append(e['path'])
                    if len(distinct) == 1:
                        primary[d] = distinct[0]
                    else:
                        mv.array_faces[tkey] += 1
                        if target == 'faces':
                            for jt in jfaces.get(d, [])[:1]:
                                for p in distinct:
                                    mv.variants[(jt, p)].add(jb)
                if not primary:
                    continue
                jdistinct = set(t for ts in jfaces.values() for t in ts)
                if target == 'carried' and len(set(primary.values())) == 1 and len(jdistinct) > 1:
                    # one inventory icon for the whole block: it does not stand for any single face
                    mv.issues[jb].add('carried icon %s is one file for a block with %d textures; not paired'
                                      % (next(iter(primary.values())), len(jdistinct)))
                    continue
                pairs, issues = face_pairs(jfaces, primary)
                if target == 'faces':
                    for a, b in pairs:
                        block_pairs[a].add(b)
                    if issues:
                        mv.issues[jb].update(issues)
                else:
                    carried_pairs |= pairs
        for a, bs in block_pairs.items():
            if len(bs) == 1:
                b = next(iter(bs))
                mv.votes[(a, b)].add(jb)
                if full_cube:
                    mv.full[(a, b)].add(jb)
            else:
                mv.issues[jb].add('%s pairs with %d Bedrock textures in one block; dropped' % (a, len(bs)))
        for a, b in carried_pairs:
            mv.carried[(a, b)].add(jb)
    return mv


# ------------------------------------------------------------------ pixel matching
def pixel_index(recs):
    by_norm = collections.defaultdict(list)
    by_frame = collections.defaultdict(list)
    for n, r in recs.items():
        if not r.ok or r.desc.get('uniform'):
            continue
        by_norm[r.norm()].append(n)
        fr = r.desc.get('frame_norm')
        if fr:
            for i, h in enumerate(fr):
                by_frame[h].append((n, i))
    return by_norm, by_frame


def frame_set(r):
    fr = r.desc.get('frame_norm')
    return frozenset(fr) if fr else frozenset([r.norm()])


def first_frame(r):
    fr = r.desc.get('frame_norm')
    return fr[0] if fr else r.norm()


def pixel_matches(jrecs, brecs):
    """{java name: [(bedrock name, level)]}; level in identical/frames/first-frame."""
    b_norm, b_frame = pixel_index(brecs)
    b_first = collections.defaultdict(list)
    b_sets = collections.defaultdict(list)
    for n, r in brecs.items():
        if r.ok and not r.desc.get('uniform'):
            b_first[first_frame(r)].append(n)
            b_sets[frame_set(r)].append(n)
    out = {}
    for jn, jr in jrecs.items():
        if not jr.ok or jr.desc.get('uniform'):
            continue
        hits = [(b, 'identical') for b in b_norm.get(jr.norm(), [])]
        if not hits:
            fs = frame_set(jr)
            if len(fs) > 1:
                hits = [(b, 'same frames') for b in b_sets.get(fs, [])]
                if not hits:
                    # every Bedrock frame is one of the Java frames (or vice versa)
                    cands = collections.Counter()
                    for h in fs:
                        for b, _i in b_frame.get(h, []):
                            cands[b] += 1
                    for b, c in cands.items():
                        bfs = frame_set(brecs[b])
                        if len(bfs) > 1 and (bfs <= fs or fs <= bfs):
                            hits.append((b, 'frame subset'))
        if not hits:
            ff = first_frame(jr)
            hits = [(b, 'first frame') for b in b_first.get(ff, []) if b_first and first_frame(brecs[b]) == ff]
        if hits:
            out[jn] = hits
    return out


# ------------------------------------------------------------------ decoding helper
class PixelSource(object):
    """Decodes images on demand (kept in memory for the run only)."""

    def __init__(self, loaders):
        self.loaders = loaders  # (platform, name) -> callable returning (raw bytes, ext)
        self.cache = {}

    def get(self, platform, name):
        key = (platform, name)
        if key not in self.cache:
            fn = self.loaders.get(key)
            if fn is None:
                self.cache[key] = None
            else:
                raw, ext = fn()
                img = pixels.decode(raw, ext)
                self.cache[key] = (img.width, img.height, img.rgba)
        return self.cache[key]


def tint_check(src, jname, bname):
    a = src.get('java', jname)
    b = src.get('bedrock', bname)
    if not a or not b:
        return None
    if (a[0], a[1]) != (b[0], b[1]):
        # compare first square frames of strips of equal width
        if a[0] == b[0]:
            fa = (a[0], a[0], a[2][:a[0] * a[0] * 4]) if a[1] > a[0] else a
            fb = (b[0], b[0], b[2][:b[0] * b[0] * 4]) if b[1] > b[0] else b
            if (fa[0], fa[1]) == (fb[0], fb[1]):
                r = pixels.tint_relation((fa[0], fa[1], pixels.normalized(fa[2])), (fb[0], fb[1], pixels.normalized(fb[2])))
                if r:
                    r['first_frames'] = True
                return r
        return None
    return pixels.tint_relation((a[0], a[1], pixels.normalized(a[2])), (b[0], b[1], pixels.normalized(b[2])))


def scale_match(src, jname, bname):
    """'java = bedrock downscaled xk' / 'bedrock = java downscaled xk' or None."""
    a = src.get('java', jname)
    b = src.get('bedrock', bname)
    if not a or not b:
        return None
    for big, small, label in ((a, b, 'java is a %dx nearest upscale of bedrock'), (b, a, 'bedrock is a %dx nearest upscale of java')):
        if big[0] > small[0] and big[0] % small[0] == 0:
            k = big[0] // small[0]
            if big[1] == small[1] * k:
                d = pixels.nearest_downscale(big[0], big[1], big[2], k)
                if d and pixels.normalized(d[2]) == pixels.normalized(small[2]):
                    return label % k
    return None


def describe_tint(r):
    """One-line note for a tint_relation() result (Java image = a, Bedrock image = b)."""
    if not r:
        return ''
    rel = r['relation']
    alpha = '' if r['same_alpha'] else '; alpha mask differs'
    base = rel.replace('-alpha-differs', '')
    if base == 'identical-colour':
        return 'same colours where both are visible; only transparency differs' if not r['same_alpha'] else 'identical colours'
    if base == 'near-identical':
        return 'near-identical colours (within 4 levels)%s' % alpha
    if base == 'tint':
        if r['direction'] == 'a_grey':
            return 'differs only by tint: Java grey (tinted in game), Bedrock pre-coloured x%s%s' % (_hex(r['tint']), alpha)
        if r['direction'] == 'b_grey':
            return 'differs only by tint: Bedrock grey (tinted in game), Java pre-coloured x%s%s' % (_hex(r['tint']), alpha)
        return 'differs only by tint: Bedrock = Java x%s%s' % (_hex(r['tint']), alpha)
    if base == 'partial-tint':
        return ('partial tint: %d of %d visible pixels differ by one tint x%s, the rest identical%s'
                % (r.get('tinted_px', 0), r.get('visible_px', 0), _hex(r['tint']), alpha))
    if base == 'luminance':
        return 'art differs slightly (luminance correlation %.3f)%s' % (r['corr'], alpha)
    return 'different art (luminance correlation %.2f%s)' % (r['corr'], alpha)


def _hex(t):
    return '#%02x%02x%02x' % tuple(max(0, min(255, int(x))) for x in t)
