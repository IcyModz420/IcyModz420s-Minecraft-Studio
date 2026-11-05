"""Decide Java (newest jar) <-> Bedrock pairs from pixel and model evidence.

Order of evidence (accuracy first):
 1. identical pixels (also identical animation frames / first frame)      -> pixels / pixels+model
 2. model votes from blocks.json faces, where the Bedrock file is not the
    pixel twin of another Java texture                                      -> model
 3. Bedrock terrain arrays (index chosen in game code) and carried
    (inventory) textures, accepted only when the images show the same art
    up to tint/alpha                                                        -> model
 4. other Bedrock texture folders (environment/, entity/...) by pixels      -> pixels
 5. same alpha mask + tint-only difference, no model support               -> uncertain
"""
import collections

from . import pairing, pixels

_REL_RANK = {'identical-colour': 5, 'near-identical': 4, 'tint': 3, 'partial-tint': 2, 'luminance': 1}


def _vk(v):
    import re
    return tuple(int(x) for x in re.findall(r'\d+', v))


class Result(object):
    def __init__(self):
        self.pairs = {}            # (java, bedrock) -> {'evidence', 'notes': [], 'kind'}
        self.disagreements = []
        self.ambiguous = []
        self.rejected = []
        self.java_unpaired = []
        self.bedrock_unpaired = []
        self.dup_bedrock = []

    def add(self, j, b, evidence, kind, notes=()):
        cur = self.pairs.get((j, b))
        if cur is None:
            self.pairs[(j, b)] = {'evidence': evidence, 'kind': kind, 'notes': list(notes)}
        else:
            for n in notes:
                if n not in cur['notes']:
                    cur['notes'].append(n)

    def bedrock_of(self, j):
        return [b for (jj, b) in self.pairs if jj == j]

    def java_of(self, b):
        return [j for (j, bb) in self.pairs if bb == b]


def decide_pixels_only(jt, colour, older=None):
    """Item textures: identical pixels (and Bedrock files that kept older Java art) only."""
    res = Result()
    pm = pairing.pixel_matches(jt, colour)
    b_pix = collections.defaultdict(set)
    for j, hits in sorted(pm.items()):
        for b, lvl in hits:
            notes = []
            if lvl != 'identical':
                notes.append('pixels: %s' % lvl)
            elif jt[j].desc.get('exact') != colour[b].desc.get('exact'):
                notes.append('identical except the colour stored under fully transparent pixels')
            if len(hits) > 1:
                notes.append('Java file is pixel-identical to %d Bedrock files: %s' % (len(hits), ', '.join(sorted(x for x, _ in hits))))
            res.add(j, b, 'pixels', 'pixels', notes)
            b_pix[b].add(j)
    for b, js in b_pix.items():
        if len(js) > 1:
            res.dup_bedrock.append({'bedrock': b, 'java': sorted(js), 'kept_java': sorted(js)})
            for j in js:
                res.add(j, b, 'pixels', 'pixels', ['Bedrock file is pixel-identical to %d Java files: %s' % (len(js), ', '.join(sorted(js)))])
    for b in sorted(colour):
        if b in b_pix or not colour[b].ok or colour[b].desc.get('uniform') or not older:
            continue
        latest = {}
        for v, old, newest in older.get(colour[b].norm(), []):
            if newest not in latest or _vk(v) > _vk(latest[newest][0]):
                latest[newest] = (v, old)
        for newest, (v, old) in sorted(latest.items()):
            if newest in jt:
                res.add(newest, b, 'pixels', 'older-art',
                        ['pixel-identical to Java %s as of %s (Bedrock kept older art; the Java file changed later)' % (old, v)])
                b_pix[b].add(newest)
    res.java_unpaired = sorted(j for j in jt if not res.bedrock_of(j))
    paired_b = set(b for _j, b in res.pairs)
    res.bedrock_unpaired = sorted(b for b in colour if b not in paired_b)
    return res


def decide(jt, colour, other, mv, src, bd, log=print, older=None):
    """older: {norm hash: [(version, old name, newest name)]} for Java textures of earlier
    releases, so a Bedrock file that kept old Java art can be paired by pixels."""
    res = Result()
    pm = pairing.pixel_matches(jt, colour)
    mc = {j: mv.candidates(j) for j in jt}
    mset = {j: set(b for b, _n, _f in c) for j, c in mc.items()}
    mvar = collections.defaultdict(set)   # Bedrock files in a terrain array used where Java uses j
    for (j, b) in mv.variants:
        mvar[j].add(b)
    mcar = collections.defaultdict(set)
    for (j, b) in mv.carried:
        mcar[j].add(b)

    # ---- 1. pixels
    b_pix = collections.defaultdict(set)
    for j, hits in sorted(pm.items()):
        cands = [b for b, _lvl in hits]
        keep = cands
        support = mset[j] | mvar[j] | mcar[j]
        if len(cands) > 1 and any(b in support for b in cands):
            keep = [b for b in cands if b in support]
        for b, lvl in hits:
            if b not in keep:
                continue
            notes = []
            if lvl != 'identical':
                notes.append('pixels: %s' % lvl)
            elif jt[j].desc.get('exact') != colour[b].desc.get('exact'):
                notes.append('identical except the colour stored under fully transparent pixels')
            if len(keep) > 1:
                notes.append('Java file is pixel-identical to %d Bedrock files: %s' % (len(keep), ', '.join(sorted(keep))))
            if len(cands) > len(keep):
                notes.append('also pixel-identical to %s (not used by the same blocks)' % ', '.join(sorted(set(cands) - set(keep))))
            res.add(j, b, 'pixels+model' if b in mset[j] else 'pixels', 'pixels', notes)
            b_pix[b].add(j)
    # Bedrock files pixel-identical to several Java files (Java duplicates): prune by model if possible
    for b, js in sorted(b_pix.items()):
        if len(js) < 2:
            continue
        withm = sorted(j for j in js if b in (mset[j] | mvar[j] | mcar[j]))
        if withm and len(withm) < len(js):
            for j in sorted(js - set(withm)):
                del res.pairs[(j, b)]
                res.dup_bedrock.append({'bedrock': b, 'dropped_java': j, 'kept_java': withm})
            b_pix[b] = set(withm)
        else:
            res.dup_bedrock.append({'bedrock': b, 'java': sorted(js), 'kept_java': sorted(js)})
            for j in js:
                res.add(j, b, res.pairs[(j, b)]['evidence'], 'pixels',
                        ['Bedrock file is pixel-identical to %d Java files: %s' % (len(js), ', '.join(sorted(js)))])

    # ---- 1b. Bedrock files that kept the art of an older Java release (pixels)
    for b in sorted(colour):
        if b in b_pix or not colour[b].ok or colour[b].desc.get('uniform') or not older:
            continue
        hits = older.get(colour[b].norm(), [])
        latest = {}
        for v, old, newest in hits:
            if newest not in latest or _vk(v) > _vk(latest[newest][0]):
                latest[newest] = (v, old)
        for newest, (v, old) in sorted(latest.items()):
            if newest not in jt:
                continue
            res.add(newest, b, 'pixels', 'older-art',
                    ['pixel-identical to Java %s as of %s (Bedrock kept older art; the Java file changed later)' % (old, v)])
            b_pix[b].add(newest)

    # ---- 2. model
    model_taken = collections.defaultdict(set)
    for j in sorted(jt):
        c = mc[j]
        if not c:
            continue
        top = c[0]
        clear = len(c) == 1 or (c[0][2], c[0][1]) > (c[1][2], c[1][1])
        pix = set(res.bedrock_of(j))
        if pix:
            if not (pix & mset[j]):
                res.disagreements.append({'java': j, 'pixels': sorted(pix), 'model': c[:3],
                                          'blocks': sorted(mv.votes.get((j, top[0]), ()))[:5]})
                if clear and not b_pix.get(top[0]):
                    rel = pairing.tint_check(src, j, top[0])
                    res.add(j, top[0], 'model', 'model',
                            ['Bedrock blocks.json uses this file where Java uses %s (blocks: %s); the pixel twin of %s is %s'
                             % (j, ', '.join(sorted(mv.votes.get((j, top[0]), ()))[:4]), j, ', '.join(sorted(pix))),
                             pairing.describe_tint(rel)])
                    model_taken[top[0]].add(j)
            continue
        if not clear:
            res.ambiguous.append({'java': j, 'model': c[:4]})
            continue
        b = top[0]
        if b_pix.get(b):
            res.rejected.append({'java': j, 'bedrock': b, 'why': 'Bedrock file is the pixel twin of Java %s' % ', '.join(sorted(b_pix[b])),
                                 'blocks': sorted(mv.votes.get((j, b), ()))[:5]})
            continue
        rel = pairing.tint_check(src, j, b)
        notes = ['model: %d block(s) e.g. %s' % (top[1], ', '.join(sorted(mv.votes.get((j, b), ()))[:4]))]
        sc = pairing.scale_match(src, j, b)
        if sc:
            notes.append(sc)
        notes.append(pairing.describe_tint(rel))
        res.add(j, b, 'model', 'model', notes)
        model_taken[b].add(j)

    paired_j = set(j for j, _b in res.pairs)
    paired_b = set(b for _j, b in res.pairs)

    # ---- 3a. terrain array entries (index chosen in game code): accept when the art matches
    variants = collections.defaultdict(set)
    for (j, b), blocks in mv.variants.items():
        variants[j].add(b)
    for j in sorted(jt):
        if j in paired_j or j not in variants:
            continue
        found = []
        scored = []
        for b in sorted(variants[j]):
            if b not in colour or b_pix.get(b) or b in paired_b:
                continue
            rel = pairing.tint_check(src, j, b)
            if pixels.related(rel):
                found.append((b, rel))
            elif rel:
                scored.append((rel['corr'] if rel['same_alpha'] else -1.0, b, rel))
        if found:
            # keep only the closest relation class (e.g. fancy + opaque leaves are both near-identical,
            # but of four dried-ghast states only one shows the same art)
            top = max(_REL_RANK.get(rel['relation'].replace('-alpha-differs', ''), 0) for _b, rel in found)
            found = [(b, rel) for b, rel in found if _REL_RANK.get(rel['relation'].replace('-alpha-differs', ''), 0) == top]
        for b, rel in found:
            keys = sorted(mv.variants.get((j, b), ()))
            res.add(j, b, 'model', 'array',
                    ['Bedrock terrain_texture array entry used by blocks %s (array index is chosen in game code)' % ', '.join(keys[:3]),
                     pairing.describe_tint(rel)])
        if not found and scored:
            # no entry shows the same art: take the clearly closest one, marked uncertain
            scored.sort(reverse=True)
            best = scored[0]
            if best[0] >= 0.9 and (len(scored) == 1 or best[0] - scored[1][0] >= 0.1):
                keys = sorted(mv.variants.get((j, best[1]), ()))
                res.add(j, best[1], 'uncertain', 'array',
                        ['Bedrock terrain_texture array entry used by blocks %s (array index is chosen in game code); '
                         'picked as the closest image of the array' % ', '.join(keys[:3]), pairing.describe_tint(best[2])])
                found.append((best[1], best[2]))
        if found:
            paired_j.add(j)
            paired_b.update(b for b, _ in found)

    # ---- 3b. carried (inventory) textures under blocks/ not paired otherwise: blocks.json names
    # them for the block, so they are model evidence even when the art is pre-coloured or redrawn
    carried = collections.defaultdict(list)
    for (j, b), blocks in mv.carried.items():
        carried[b].append((len(blocks), j))
    for b in sorted(carried):
        if b in paired_b or b not in colour:
            continue
        cands = sorted(carried[b], reverse=True)
        if len(cands) > 1 and cands[0][0] == cands[1][0]:
            res.ambiguous.append({'bedrock': b, 'carried_candidates': [j for _n, j in cands]})
            continue
        j = cands[0][1]
        rel = pairing.tint_check(src, j, b)
        res.add(j, b, 'model', 'carried',
                ['Bedrock carried (inventory) texture of block(s) %s' % ', '.join(sorted(mv.carried[(j, b)])[:3]),
                 pairing.describe_tint(rel)])
        paired_b.add(b)

    # ---- 4. other Bedrock folders by pixels, for Java files still unpaired
    left = {j: jt[j] for j in jt if j not in paired_j}
    pm2 = pairing.pixel_matches(left, other)
    for j, hits in sorted(pm2.items()):
        for b, lvl in hits:
            res.add(j, b, 'pixels', 'other-folder',
                    ['Bedrock keeps this file in textures/%s/' % b.split('/', 1)[0]] + ([] if lvl == 'identical' else ['pixels: %s' % lvl]))
        paired_j.add(j)

    # ---- 5. discovery among what is left (no model link): the same art up to tint or
    #         transparency; kept only when the match is unique on both sides -> uncertain
    left_j = [j for j in sorted(jt) if j not in paired_j and jt[j].ok and not jt[j].desc.get('uniform')]
    left_b = [b for b in sorted(colour) if b not in paired_b and colour[b].ok and not colour[b].desc.get('uniform')]
    by_size = collections.defaultdict(list)
    for b in left_b:
        by_size[(colour[b].desc['w'], colour[b].desc['h'])].append(b)
    found_j = collections.defaultdict(list)
    found_b = collections.defaultdict(list)
    for j in left_j:
        size = (jt[j].desc['w'], jt[j].desc['h'])
        cands = by_size.get(size, [])
        if not cands:
            continue
        ja = src.get('java', j)
        jn = pixels.normalized(ja[2])
        for b in cands:
            bb = src.get('bedrock', b)
            bn = pixels.normalized(bb[2])
            same, both, av, bv = pixels.colour_overlap(jn, bn)
            if both and same == both and both * 2 >= min(av, bv):
                # enough evidence: >= 30 overlapping pixels and overlap x distinct colours >= 96
                # (a few pixels of a shared grey ramp, e.g. redstone dust, prove nothing)
                cols = len(set(jn[4 * p:4 * p + 3] for p in range(len(jn) // 4) if jn[4 * p + 3] and bn[4 * p + 3]))
                if both < 30 or both * cols < 96:
                    continue
                rel = pixels.tint_relation((ja[0], ja[1], jn), (bb[0], bb[1], bn))
            elif (jt[j].desc.get('alpha_mask') == colour[b].desc.get('alpha_mask')
                  and (same * 4 >= both or jt[j].desc.get('grey') != colour[b].desc.get('grey'))):
                # same shape: a tint of the whole image (grey vs coloured) or of part of it
                rel = pixels.tint_relation((ja[0], ja[1], jn), (bb[0], bb[1], bn))
                if not rel or rel['relation'] not in ('tint', 'partial-tint', 'near-identical'):
                    continue
            else:
                continue
            if rel and pixels.related(rel) and not rel['relation'].startswith('luminance'):
                found_j[j].append((b, rel))
                found_b[b].append(j)
    for j, hits in sorted(found_j.items()):
        if len(hits) == 1 and len(found_b[hits[0][0]]) == 1:
            b, rel = hits[0]
            res.add(j, b, 'uncertain', 'discovery',
                    ['no model link; found by comparing leftover images of the same size', pairing.describe_tint(rel)])
            res.pairs[(j, b)]['relation'] = rel['relation']
            paired_j.add(j)
            paired_b.add(b)
        else:
            res.ambiguous.append({'java': j, 'same-art candidates': [b for b, _ in hits]})

    res.java_unpaired = sorted(j for j in jt if not res.bedrock_of(j))
    paired_b = set(b for _j, b in res.pairs)
    res.bedrock_unpaired = sorted(b for b in colour if b not in paired_b)
    return res
