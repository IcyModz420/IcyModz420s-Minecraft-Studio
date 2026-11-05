"""Java texture name history across installed releases.

For each pair of consecutive installed releases (A, B):
  * names present in both continue unchanged;
  * a vanished texture whose pixels reappear under a new name is a rename (pixels);
  * block models give face-by-face texture pairs for blocks linked across A and B
    (same blockstate file, or for 1.12.2 -> 1.16.4 the flattening table); these are
    the model votes;
  * both are fused, conflicts and leftovers are recorded for the report.
"""
import collections
import re

from . import javamodels

DIRS = javamodels.DIRS
STRENGTH_RANK = {'strong': 3, 'medium': 2, 'weak': 1}


# ------------------------------------------------------------------ model helpers
def first_model(models, specs):
    if not specs:
        return None, None
    spec = specs[0]
    return models.resolve(spec.get('model', ''), True), (spec.get('x', 0) % 360, spec.get('y', 0) % 360)


def face_lists(m):
    if not m:
        return {}
    out = {}
    for d, lst in m['faces'].items():
        ts = [t for t, _tint in lst if t]
        if ts:
            out[d] = ts
    return out


_YROT = {'north': 'east', 'east': 'south', 'south': 'west', 'west': 'north', 'up': 'up', 'down': 'down'}


def rotate_y(faces, deg):
    steps = (deg // 90) % 4
    out = dict(faces)
    for _ in range(steps):
        out = {_YROT[d]: v for d, v in out.items()}
    return out


def pair_faces(fa, fb):
    pairs = set()
    ambiguous = 0
    for d in DIRS:
        la, lb = fa.get(d), fb.get(d)
        if not la or not lb:
            continue
        if len(la) == len(lb):
            pairs.update(zip(la, lb))
        elif len(set(la)) == 1 and len(set(lb)) == 1:
            pairs.add((la[0], lb[0]))
        else:
            ambiguous += 1
    return pairs, ambiguous


def _canon_when(w):
    if w is None:
        return None
    if isinstance(w, dict):
        return tuple(sorted((k, _canon_when(v) if isinstance(v, (dict, list)) else str(v)) for k, v in w.items()))
    if isinstance(w, list):
        return tuple(sorted(_canon_when(x) for x in w))
    return str(w)


def align(models_a, cases_a, models_b, cases_b, constraint=None):
    """Pair A cases with B cases. constraint: {prop: value} the B state must have for
    properties A's variant does not define (from the flattening table, e.g. lit=true
    for 1.12 lit_furnace -> furnace)."""
    va = [c for c in cases_a if c[0] == 'variant']
    vb = [c for c in cases_b if c[0] == 'variant']
    ma = [c for c in cases_a if c[0] == 'multipart']
    mb = [c for c in cases_b if c[0] == 'multipart']
    constraint = constraint or {}
    out = []
    for a in va:
        pa = a[1]
        akeys = {k for k, _v in pa}

        def admissible(b):
            bd = dict(b[1])
            for k, v in constraint.items():
                if k not in akeys and k in bd and bd[k] != v:
                    return False
            return True

        if pa:
            cand = [b for b in vb if pa <= b[1] and admissible(b)]
            if not cand:
                vals = {v for _k, v in pa}
                cand = [b for b in vb if vals <= {v for _k, v in b[1]} and admissible(b)]
        else:
            # A has one appearance: align only if every admissible B variant looks the same
            cand = []
            pool = [b for b in vb if admissible(b)] or vb
            sigs = set()
            for b in pool:
                m, rot = first_model(models_b, b[2])
                sigs.add((m['name'] if m else None, rot))
            if len(sigs) == 1:
                cand = pool[:1]
        for b in cand:
            out.append((a, b))
    for a in ma:
        ka = _canon_when(a[1])
        for b in mb:
            if _canon_when(b[1]) == ka:
                out.append((a, b))
    return out


def all_textures(models, cases):
    out = set()
    for c in cases:
        m, _rot = first_model(models, c[2])
        for ts in face_lists(m).values():
            out.update(ts)
    return out


def block_pairs(models_a, name_a, models_b, name_b, constraint=None):
    """Texture pairs for one block linked across two versions.

    Returns dict(pairs=set, status=str, split=bool, conflicts=[...]).
    """
    ca = models_a.cases(name_a)
    cb = models_b.cases(name_b)
    if not ca or not cb:
        return {'pairs': set(), 'status': 'no-blockstate', 'conflicts': []}
    aligned = align(models_a, ca, models_b, cb, constraint)
    pairs = set()
    status = 'aligned'
    for a, b in aligned:
        ma, rot_a = first_model(models_a, a[2])
        mb, rot_b = first_model(models_b, b[2])
        if ma is None or mb is None:
            continue
        fa, fb = face_lists(ma), face_lists(mb)
        if rot_a != rot_b:
            if rot_a[0] != rot_b[0]:
                continue  # different x rotation: skip rather than guess
            fa = rotate_y(fa, rot_a[1])
            fb = rotate_y(fb, rot_b[1])
        p, _amb = pair_faces(fa, fb)
        pairs |= p
    texa = all_textures(models_a, ca)
    texb = all_textures(models_b, cb)
    if not aligned:
        status = 'fallback'
        if len(texa) == 1 and len(texb) == 1:
            pairs.add((next(iter(texa)), next(iter(texb))))
        elif len(texa) == 1 and len(texb) > 1:
            # one old texture on every face, several new ones: a split
            t = next(iter(texa))
            pairs.update((t, x) for x in texb)
        elif len(texa) == len(texb) and len(texa) > 1:
            ua, ub = collections.defaultdict(set), collections.defaultdict(set)
            for c in ca:
                m, _ = first_model(models_a, c[2])
                for d, ts in face_lists(m).items():
                    ua[d].update(ts)
            for c in cb:
                m, _ = first_model(models_b, c[2])
                for d, ts in face_lists(m).items():
                    ub[d].update(ts)
            co = collections.Counter()
            for d in DIRS:
                for x in ua.get(d, ()):
                    for y in ub.get(d, ()):
                        co[(x, y)] += 1
            chosen = {}
            for x in texa:
                row = sorted(((co[(x, y)], y) for y in texb), reverse=True)
                if row and row[0][0] > 0 and (len(row) == 1 or row[0][0] > row[1][0]):
                    chosen[x] = row[0][1]
            ys = list(chosen.values())
            if len(chosen) == len(texa) and len(set(ys)) == len(ys):
                # column uniqueness too
                ok = True
                for x, y in chosen.items():
                    col = sorted(((co[(x2, y)], x2) for x2 in texa), reverse=True)
                    if col[0][1] != x or (len(col) > 1 and col[0][0] == col[1][0]):
                        ok = False
                if ok:
                    pairs.update(chosen.items())
            if not pairs:
                status = 'no-align'
        else:
            status = 'no-align'
    # consistency
    fwd = collections.defaultdict(set)
    for x, y in pairs:
        fwd[x].add(y)
    conflicts = []
    split = False
    clean = set()
    for x, ys in fwd.items():
        if len(ys) == 1:
            clean.add((x, next(iter(ys))))
        elif len(texa) == 1:
            split = True
            clean.update((x, y) for y in ys)
        else:
            conflicts.append((x, sorted(ys)))
    return {'pairs': clean, 'status': status, 'split': split, 'conflicts': conflicts,
            'texa': texa, 'texb': texb}


# ------------------------------------------------------------------ 1.12 -> 1.13 block links
_COLOURS = ['white', 'orange', 'magenta', 'light_blue', 'yellow', 'lime', 'pink', 'gray', 'light_gray',
            'cyan', 'purple', 'blue', 'brown', 'green', 'red', 'black', 'silver']


def _tokens(name):
    toks = name.split('_')
    out = []
    for t in toks:
        if t.endswith('s') and len(t) > 3 and t not in ('grass', 'glass', 'moss', 'bricks'):
            t = t[:-1]
        if t == 'bricks':
            t = 'brick'
        out.append(t)
    return out


def flattening_links(flat_rows, bs12, recipes12, bs16, renamed13):
    """Link 1.12.2 blockstate files to 1.16.4 blockstate names.

    flat_rows: mappings.read_flattening(); bs12/bs16: sets of blockstate names;
    recipes12: {recipe name: (item, data)}; renamed13: {1.13 name: 1.16.4 name}.
    Returns {file12: (name16, strength, how)}.
    """
    iddata = {}
    by_reg = collections.defaultdict(list)
    for r in flat_rows:
        by_reg[r['name12']].append(r)
        for dv in r['data']:
            iddata[(r['name12'], dv)] = r
    blocks13 = set(r['block13'] for r in flat_rows)

    def to16(b13):
        b = renamed13.get(b13, b13)
        return b if b in bs16 else None

    # Properties that tell apart different 1.12 ids flattening into one block (furnace vs
    # lit_furnace -> lit, repeater vs powered_repeater -> powered). Only these become
    # alignment constraints: the other first_state_properties vary with the data value.
    regs_by_block = collections.defaultdict(lambda: collections.defaultdict(list))
    for r in flat_rows:
        regs_by_block[r['block13']][r['name12']].append(r)
    disc = {}
    for b13, regs in regs_by_block.items():
        keys = set()
        if len(regs) > 1:
            allk = set(k for rows in regs.values() for r in rows for k in r['props13'])
            for k in allk:
                vals = set()
                for rows in regs.values():
                    vs = set(r['props13'].get(k) for r in rows)
                    if len(vs) == 1:
                        vals |= vs
                    else:
                        vals = None
                        break
                if vals is not None and len(vals) > 1:
                    keys.add(k)
        disc[b13] = keys

    def common_props(rows):
        if not rows:
            return {}
        b13 = rows[0]['block13']
        out = {}
        for k in disc.get(b13, ()):
            vs = set(r['props13'].get(k) for r in rows)
            if len(vs) == 1 and None not in vs:
                out[k] = vs.pop()
        return out

    links = {}
    # 1. recipe whose file name equals the blockstate name: result item:data -> flattening
    for f in sorted(bs12):
        if f in recipes12:
            item, dv = recipes12[f]
            r = iddata.get((item, dv))
            if r and to16(r['block13']):
                links[f] = (to16(r['block13']), 'strong', 'recipe %s.json -> %s:%d -> %s' % (f, item, dv, r['block13']),
                            common_props([r]))
    # 2. file named like a 1.12 registry id whose every data value flattens to one block
    for f in sorted(bs12):
        if f in links:
            continue
        rows = by_reg.get(f)
        if rows and len(set(r['block13'] for r in rows)) == 1 and to16(rows[0]['block13']):
            links[f] = (to16(rows[0]['block13']), 'strong', 'registry id %s -> %s (all data values)' % (f, rows[0]['block13']),
                        common_props(rows))
    # 3. file named like a multi-variant registry id -> data value 0 (the "normal" variant)
    for f in sorted(bs12):
        if f in links:
            continue
        r0 = iddata.get((f, 0))
        if r0 and to16(r0['block13']):
            links[f] = (to16(r0['block13']), 'medium', 'registry id %s data 0 -> %s' % (f, r0['block13']), common_props([r0]))
    # 4. file named exactly like a 1.13 block (which 1.12 id it came from is unknown: no constraint)
    claimed = set(v[0] for v in links.values())
    for f in sorted(bs12):
        if f in links:
            continue
        if f in blocks13 and to16(f) and to16(f) not in claimed:
            links[f] = (to16(f), 'medium', 'same name as 1.13 block %s' % f, {})
    # 5. double slabs share the single slab's 1.13 block (type=double)
    for f in sorted(bs12):
        if f in links or '_double_slab' not in f:
            continue
        single = f.replace('_double_slab', '_slab')
        if single in links:
            links[f] = (links[single][0], links[single][1], 'double slab of %s' % single, {'type': 'double'})
    # 6. token elimination among what is left (weak): weighted Jaccard of name tokens,
    #    accepted only when both sides prefer each other by a clear margin
    claimed = set(v[0] for v in links.values())
    left13 = sorted(b for b in blocks13 if to16(b) and to16(b) not in claimed)
    df = collections.Counter(t for b in blocks13 for t in set(_tokens(b)))
    left12 = [f for f in sorted(bs12) if f not in links]

    def toks12(f):
        tf = _tokens(f)
        if tf and tf[0] == 'silver':  # 1.12 colour name of light gray (it is in the 1.12 file names)
            tf = ['light', 'gray'] + tf[1:]
        return set(tf)

    def score(f, b):
        tf, tb = toks12(f), set(_tokens(b))
        w = lambda ts: sum(1.0 / df.get(t, 1) for t in ts)
        u = w(tf | tb)
        return (w(tf & tb) / u) if u else 0.0

    prefs12 = {}
    for f in left12:
        sc = sorted(((score(f, b), b) for b in left13), reverse=True)
        if sc and sc[0][0] > 0.2 and (len(sc) == 1 or sc[0][0] > sc[1][0] * 1.25):
            prefs12[f] = sc[0][1]
    prefs13 = {}
    for b in left13:
        sc = sorted(((score(f, b), f) for f in left12), reverse=True)
        if sc and sc[0][0] > 0.2 and (len(sc) == 1 or sc[0][0] > sc[1][0] * 1.25):
            prefs13[b] = sc[0][1]
    for f, b in prefs12.items():
        if prefs13.get(b) == f:
            links[f] = (to16(b), 'weak', 'name tokens %s ~ %s' % (f, b), {})
    return links


# ------------------------------------------------------------------ history
class Link(object):
    __slots__ = ('a', 'b', 'kind', 'evidence', 'note', 'support')

    def __init__(self, a, b, kind, evidence, note='', support=None):
        self.a, self.b, self.kind, self.evidence, self.note = a, b, kind, evidence, note
        self.support = support or []

    def __repr__(self):
        return 'Link(%s -> %s, %s, %s)' % (self.a, self.b, self.kind, self.evidence)


class StepResult(object):
    def __init__(self, a_ver, b_ver):
        self.a_ver, self.b_ver = a_ver, b_ver
        self.links = []           # Link
        self.gone_unmatched = []  # names in A with no successor
        self.new_unmatched = []   # names in B with no predecessor
        self.disagreements = []   # dicts
        self.ambiguous = []       # dicts
        self.art_changed = []     # names (same name, different pixels)
        self.block_links = {}     # fileA -> (fileB, strength, how)
        self.block_issues = []    # (fileA, fileB, status, conflicts)
        self.votes = {}           # (ta, tb) -> [(fileA, fileB, strength)]


def _usable(rec):
    return rec.ok and not rec.desc.get('uniform')


def compare_step(A, B, tex_a, tex_b, models_a, models_b, block_links):
    """A/B: version names; tex_*: {name: TexRec}; block_links: {fileA: (fileB, strength, how)}."""
    res = StepResult(A, B)
    res.block_links = block_links
    names_a, names_b = set(tex_a), set(tex_b)
    same = names_a & names_b
    gone = sorted(names_a - names_b)
    new = sorted(names_b - names_a)
    for n in sorted(same):
        res.links.append(Link(n, n, 'same', 'same-name'))
        if tex_a[n].norm() != tex_b[n].norm():
            res.art_changed.append(n)

    # ---- model votes
    votes = collections.defaultdict(list)
    for fa, link in sorted(block_links.items()):
        fb, strength, how = link[0], link[1], link[2]
        constraint = link[3] if len(link) > 3 else None
        r = block_pairs(models_a, fa, models_b, fb, constraint)
        if r['conflicts'] or r['status'] in ('no-align', 'no-blockstate'):
            res.block_issues.append((fa, fb, r['status'], r['conflicts']))
        for (ta, tb) in r['pairs']:
            if ta in tex_a and tb in tex_b:
                votes[(ta, tb)].append((fa, fb, strength, r.get('split')))
    res.votes = votes
    by_a = collections.defaultdict(dict)
    by_b = collections.defaultdict(dict)
    for (ta, tb), sup in votes.items():
        by_a[ta][tb] = sup
        by_b[tb][ta] = sup

    def model_choice(ta, pool=None):
        """(best tb, support, margin_ok, alternatives)."""
        cands = by_a.get(ta, {})
        if pool is not None:
            cands = {k: v for k, v in cands.items() if k in pool}
        if not cands:
            return None, [], False, []
        ranked = sorted(cands.items(), key=lambda kv: (-max(STRENGTH_RANK[s[2]] for s in kv[1]), -len(kv[1]), kv[0]))
        best, sup = ranked[0]
        total = sum(len(v) for v in cands.values())
        ok = len(sup) * 3 >= total * 2 or len(ranked) == 1
        if len(ranked) > 1:
            b2 = ranked[1]
            if (max(STRENGTH_RANK[s[2]] for s in b2[1]) == max(STRENGTH_RANK[s[2]] for s in sup)
                    and len(b2[1]) == len(sup)):
                ok = False
        return best, sup, ok, [k for k, _ in ranked[1:]]

    # ---- pixel candidates
    new_by_hash = collections.defaultdict(list)
    for n in new:
        if _usable(tex_b[n]):
            new_by_hash[tex_b[n].norm()].append(n)
    all_b_by_hash = collections.defaultdict(list)
    for n in names_b:
        if _usable(tex_b[n]):
            all_b_by_hash[tex_b[n].norm()].append(n)
    gone_by_hash = collections.defaultdict(list)
    for g in gone:
        if _usable(tex_a[g]):
            gone_by_hash[tex_a[g].norm()].append(g)
    # animated: identical frame sets also count as pixel identity
    new_by_frames = collections.defaultdict(list)
    for n in new:
        fr = tex_b[n].desc.get('frame_norm')
        if fr and _usable(tex_b[n]):
            new_by_frames[frozenset(fr)].append(n)

    linked_new = set()
    linked_gone = set()
    for g in gone:
        rec = tex_a[g]
        if not _usable(rec):
            continue
        h = rec.norm()
        cands = list(new_by_hash.get(h, []))
        how = 'identical pixels'
        if not cands and rec.desc.get('frame_norm'):
            cands = list(new_by_frames.get(frozenset(rec.desc['frame_norm']), []))
            how = 'identical animation frames'
        if cands:
            twins = gone_by_hash.get(h, [g])
            if len(cands) == 1 and len(twins) == 1:
                n = cands[0]
                best, sup, ok, alts = model_choice(g)
                ev = 'pixels+model' if n in by_a.get(g, {}) else 'pixels'
                res.links.append(Link(g, n, 'rename', ev, how, by_a.get(g, {}).get(n, [])))
                linked_gone.add(g)
                linked_new.add(n)
                if best and best != n and ok:
                    res.disagreements.append({'a': g, 'pixels': n, 'model': best, 'support': sup})
            else:
                best, sup, ok, alts = model_choice(g, pool=set(cands))
                if best and ok:
                    res.links.append(Link(g, best, 'rename', 'pixels+model',
                                          '%s (shared by %d old / %d new textures; model chose)' % (how, len(twins), len(cands)), sup))
                    linked_gone.add(g)
                    linked_new.add(best)
                else:
                    res.ambiguous.append({'a': g, 'pixel_candidates': cands, 'twins_in_a': twins,
                                          'model': best, 'model_ok': ok})
            continue
        exist = [n for n in all_b_by_hash.get(h, []) if n not in new]
        if exist:
            if len(exist) == 1:
                n = exist[0]
                ev = 'pixels+model' if n in by_a.get(g, {}) else 'pixels'
                res.links.append(Link(g, n, 'merge', ev, 'identical pixels to existing %s' % n, by_a.get(g, {}).get(n, [])))
                linked_gone.add(g)
            else:
                best, sup, ok, alts = model_choice(g, pool=set(exist))
                if best and ok:
                    res.links.append(Link(g, best, 'merge', 'pixels+model', 'identical pixels to existing textures %s; model chose' % exist, sup))
                    linked_gone.add(g)
                else:
                    res.ambiguous.append({'a': g, 'pixel_candidates_existing': exist, 'model': best})
    # ---- model-only links for what is left
    for g in gone:
        if g in linked_gone:
            continue
        best, sup, ok, alts = model_choice(g)
        if best is None:
            continue
        if best in names_a and best in names_b:
            # the old texture's faces now use a texture that already existed: a merge by model
            kind = 'merge'
        else:
            kind = 'rename'
        # one old texture on every face of a block that now has several textures: a split
        # (only through a strong block link: a wrong name-based link also looks like a split)
        splits = [tb for tb, s in by_a[g].items() if any(x[3] and x[2] == 'strong' for x in s) and tb not in names_a]
        if len(splits) > 1 and all(set(by_b.get(tb, {})) == {g} for tb in splits):
            for tb in splits:
                res.links.append(Link(g, tb, 'split', 'model',
                                      'one texture became %d (block %s)' % (len(splits), by_a[g][tb][0][0]), by_a[g][tb]))
                linked_new.add(tb)
            linked_gone.add(g)
            continue
        if not ok:
            res.ambiguous.append({'a': g, 'model_candidates': [best] + alts, 'support': sup})
            continue
        strength = max(STRENGTH_RANK[s[2]] for s in sup)
        ev = 'model' if strength >= 2 else 'uncertain'
        note = '' if strength >= 2 else 'block link by name tokens only'
        res.links.append(Link(g, best, kind, ev, note, sup))
        linked_gone.add(g)
        if kind != 'merge':
            linked_new.add(best)
    # several old textures renamed into one new texture: keep one rename, the rest are merges
    incoming = collections.defaultdict(list)
    for l in res.links:
        if l.kind in ('rename',):
            incoming[l.b].append(l)
    rank = {'pixels+model': 4, 'pixels': 3, 'model': 2, 'uncertain': 1}
    for tb, ls in incoming.items():
        if len(ls) < 2:
            continue
        ls.sort(key=lambda l: (-rank.get(l.evidence, 0), 0 if l.a.rsplit('/', 1)[-1] == tb.rsplit('/', 1)[-1] else 1,
                               -len(l.support), l.a))
        for l in ls[1:]:
            l.kind = 'merge'
            l.note = (l.note + '; ' if l.note else '') + 'the faces it covered now use %s (primary predecessor %s)' % (tb, ls[0].a)
    res.gone_unmatched = [g for g in gone if g not in linked_gone]
    res.new_unmatched = [n for n in new if n not in linked_new]
    return res


def same_name_block_links(models_a, models_b):
    a = set(models_a.blockstate_names())
    b = set(models_b.blockstate_names())
    return {n: (n, 'strong', 'same blockstate file') for n in sorted(a & b)}
