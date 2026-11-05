"""Regenerate the block texture name table for Minecraft Studio's Texture Studio.

    python -I tools/texture-map/build.py [--appdata DIR] [--bedrock DIR] [--out DIR]

Reads (never executes) the installed Java client jars and the Bedrock vanilla resource
packs, plus the app's own mapping tables, and writes:
    out/texture_names.csv   canonical,platform,name,from,to,evidence,note
    out/report.md           counts, disagreements, unmatched textures, gaps
    out/cache/images.json   pixel hashes keyed by file sha1 (speeds up re-runs)
No image data is written anywhere.
"""
import argparse
import collections
import csv
import datetime
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True  # keep the repo free of __pycache__ folders
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from texmap import (sources, scan, javamodels, javahist, mappings, pairing, jbpair, pixels,  # noqa: E402
                    legacy)
from texmap import bedrock as bedrockmod  # noqa: E402
from texmap import chains as chainsmod  # noqa: E402
from texmap import report as reportmod  # noqa: E402

DEFAULT_BEDROCK = r'C:\XboxGames\Minecraft for Windows\Content\data\resource_packs'
# Bedrock texture folders searched (by pixels only) for Java block textures that have no
# twin under textures/blocks/. UI folders are skipped: large and never block art.
OTHER_EXCLUDE = ('textures/blocks/', 'textures/ui/', 'textures/gui/', 'textures/persona')

# Known historical facts (given in the task brief; added only as `manual` rows).
MANUAL_FACTS = [
    # (canonical, old name, new name, last release with old name, first release with new name, source)
    ('smooth_stone', 'block/stone_slab_top', 'block/smooth_stone', '1.13.2', '1.14',
     'task brief: stone_slab_top -> smooth_stone in 1.14 (1.13 already used the block/ folder)'),
    ('dirt_path_top', 'block/grass_path_top', 'block/dirt_path_top', '1.16.5', '1.17',
     'task brief: grass_path -> dirt_path in 1.17'),
    ('dirt_path_side', 'block/grass_path_side', 'block/dirt_path_side', '1.16.5', '1.17',
     'task brief: grass_path -> dirt_path in 1.17'),
    ('short_grass', 'block/grass', 'block/short_grass', '1.20.2', '1.20.3',
     'task brief: grass -> short_grass in 1.20.3'),
]

T0 = time.time()


def log(*a):
    print('[%6.1fs]' % (time.time() - T0), *a, flush=True)


# ------------------------------------------------------------------ helpers
def load_release_order(vdir):
    p = os.path.join(vdir, 'version_manifest_v2.json')
    if not os.path.isfile(p):
        return None, {}
    with open(p, 'r', encoding='utf-8') as f:
        man = json.load(f)
    rel = [v for v in man.get('versions', []) if v.get('type') in ('release', 'old_beta', 'old_alpha')]
    rel.sort(key=lambda v: v.get('releaseTime', ''))
    return [v['id'] for v in rel], man.get('latest', {})


def releases_between(order, a, b):
    if not order or a not in order or b not in order:
        return None
    return order[order.index(a) + 1:order.index(b)]


def read_recipes(jar):
    out = {}
    for n in jar.names:
        if n.startswith('assets/minecraft/recipes/') and n.endswith('.json'):
            try:
                js = jar.json(n)
            except ValueError:
                continue
            res = (js or {}).get('result') or {}
            item = str(res.get('item', '')).split(':')[-1]
            if item:
                out[n.rsplit('/', 1)[1][:-5]] = (item, int(res.get('data', 0) or 0))
    return out


def renamed13_until(flat_rows, until_version):
    """{1.13 block: later name} for renames noted in flattening.csv up to until_version."""
    out = {}
    for r in flat_rows:
        note = r['note']
        m = re.match(r'^(\d+\.\d+(?:\.\d+)?) renamed this \w+ ([a-z0-9_]+)', note)
        if m and sources.version_key(m.group(1)) <= sources.version_key(until_version):
            out[r['block13']] = m.group(2)
    return out


def pack_label(name):
    return sources.pack_label(name)


# ------------------------------------------------------------------ main
def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--appdata', default=sources.default_appdata(), help='folder that holds .minecraft')
    ap.add_argument('--bedrock', default=DEFAULT_BEDROCK, help='Bedrock data/resource_packs folder')
    ap.add_argument('--repo', default=os.path.normpath(os.path.join(HERE, '..', '..')), help='Minecraft Studio repo root')
    ap.add_argument('--out', default=os.path.join(HERE, 'out'))
    ap.add_argument('--cache', default=None, help='pixel-hash cache file (default OUT/cache/images.json)')
    ap.add_argument('--keep-pairing-evidence-in-gaps', action='store_true',
                    help='keep pixels/model evidence on renames dated inside an installed-jar gap '
                         '(default: evidence=uncertain, pairing method in the note)')
    args = ap.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)
    cache = sources.ImageCache(args.cache or os.path.join(args.out, 'cache', 'images.json'))
    R = reportmod.Report()
    R.meta['generated'] = datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
    R.meta['python'] = sys.version.split()[0]

    # ---------------------------------------------------------------- Java jars
    vdir = os.path.join(args.appdata, '.minecraft', 'versions')
    order, latest = load_release_order(vdir)
    R.meta['manifest_latest'] = latest
    found, missing = sources.find_java_jars(vdir)
    R.java_missing = missing
    block_jars, atlas_jars = [], []
    for v, p in found:
        j = sources.JavaJar(v, p)
        if j.folder():
            block_jars.append(j)
        elif j.exists('terrain.png'):
            atlas_jars.append(j)
    vers = [j.version for j in block_jars]
    R.java_versions = vers
    R.atlas_versions = [j.version for j in atlas_jars]
    log('Java jars with block files:', ', '.join(vers))
    tex, models = {}, {}
    for j in block_jars:
        tex[j.version] = scan.scan_java(j, cache, ('block', 'blocks'))
        models[j.version] = javamodels.JavaModels(j)
        R.java_counts[j.version] = (len(tex[j.version]), j.folder(), j.info.get('name') or j.version,
                                     j.info.get('pack_version'))
        errs = [n for n, r in tex[j.version].items() if not r.ok]
        if errs:
            R.decode_errors.extend(('java ' + j.version, n, tex[j.version][n].desc['error']) for n in errs)
    cache.save()

    # ---------------------------------------------------------------- app mappings
    DD = mappings.data_dir(args.repo)
    flat = mappings.read_flattening(os.path.join(DD, 'flattening.csv'))
    j2b = mappings.read_pairs_tsv_gz(os.path.join(DD, 'mappings', 'j2b.tsv.gz'))
    newest = vers[-1]
    want = newest
    defaults, dver = {}, None
    while want:
        defaults, dver = mappings.read_java_defaults(os.path.join(DD, 'mappings', 'java_versions.tsv.gz'), want)
        if dver == want or '.' not in want:
            break
        want = want.rsplit('.', 1)[0]
    R.meta['java_defaults_section'] = dver
    intro = legacy.read_intro(os.path.join(DD, 'legacy_blocks.csv'))
    log('app tables: flattening %d rows, j2b %d states, default states from java_versions.tsv section %s'
        % (len(flat), len(j2b), dver))

    # ---------------------------------------------------------------- Java history steps
    steps = []
    for i in range(len(vers) - 1):
        A, B = vers[i], vers[i + 1]
        ja, jb = block_jars[i], block_jars[i + 1]
        if ja.folder() == jb.folder():
            bl = javahist.same_name_block_links(models[A], models[B])
            kind = 'same layout'
        else:
            bl = javahist.flattening_links(flat, set(models[A].blockstate_names()), read_recipes(ja),
                                           set(models[B].blockstate_names()), renamed13_until(flat, B))
            kind = 'flattening (blocks/ -> block/)'
        st = javahist.compare_step(A, B, tex[A], tex[B], models[A], models[B], bl)
        st.kind = kind
        st.between = releases_between(order, A, B)
        steps.append(st)
        log('step %s -> %s: %s' % (A, B, dict(collections.Counter((l.kind, l.evidence) for l in st.links if l.kind != 'same'))))

    # ---------------------------------------------------------------- Bedrock
    layers = sources.BedrockLayers(args.bedrock)
    bd = bedrockmod.BedrockData(layers, os.path.join(DD, 'mappings', 'bedrock_upgrade_schemas.json.gz'))
    R.bedrock_layers = layers.layer_names
    bcur, bhist = scan.scan_bedrock(layers, cache, 'textures/blocks/')
    ocur, _ohist = scan.scan_bedrock(layers, cache, 'textures/', exclude=OTHER_EXCLUDE)
    cache.save()
    for n, r in list(bcur.items()) + list(ocur.items()):
        if not r.ok:
            R.decode_errors.append(('bedrock ' + r.source, n, r.desc['error']))
    log('Bedrock: %d layers, %d block files (current), %d files in other folders'
        % (len(layers.layer_names), len(bcur), len(ocur)))
    # what do the ignored packs hold?
    for nm in layers.other_names:
        p = sources.BedrockPack(args.bedrock, nm)
        imgs = p.images_under('textures/blocks/')
        R.ignored_packs.append((nm, len(imgs), sorted(imgs)[:12],
                                sorted(r for r in p.files if r.endswith(('terrain_texture.json', 'blocks.json')))))
    for p in layers.packs:
        for rel, ents in p.collisions().items():
            sizes = [e[3] if e[0] == 'archive' else os.path.getsize(e[1]) for e in ents]
            R.collisions.append((p.name, rel, sizes))

    # PBR maps not declared by any texture set (seen: *_mers files): classify by suffix
    for n in bcur:
        if n not in bd.pbr_role and (n.endswith('_mers') or n.endswith('_mer')):
            stem = n.rsplit('_', 1)[0]
            bd.pbr_role[n] = ('metalness_emissive_roughness(_subsurface), by file name', stem if stem in bcur else '?')

    # ---------------------------------------------------------------- Java (newest) <-> Bedrock
    jt = tex[newest]
    jm = models[newest]
    jar_new = block_jars[-1]
    colour = {n: r for n, r in bcur.items() if n not in bd.pbr_role}
    loaders = {}
    for n, r in jt.items():
        loaders[('java', n)] = (lambda zp=r.path: (jar_new.read(zp), '.png'))
    for n, r in list(bcur.items()) + list(ocur.items()):
        stem, ext = os.path.splitext(r.path)
        loaders[('bedrock', n)] = (lambda pk=r.source, st=stem, ex=ext: (layers.read_image(pk, st, ex), ex))
    src = pairing.PixelSource(loaders)
    mv = pairing.model_votes(jm, bd, j2b, mappings.parse_state, defaults)
    R.unresolved_bedrock_blocks = mv.unresolved_bedrock
    other = {n: r for n, r in ocur.items()}
    # older Java art of each newest texture (art that has changed since), for Bedrock files that kept it
    chains_pre = chainsmod.Chains(vers, [set(tex[v]) for v in vers], steps)
    older = collections.defaultdict(list)
    kidx = len(vers) - 1
    for C, nodes in chains_pre.canonicals().items():
        if C.startswith('java:'):
            continue
        newest_name = [n for (i, n) in nodes if i == kidx][0]
        for i, n in nodes:
            r = tex[vers[i]][n]
            if i < kidx and r.ok and not r.desc.get('uniform') and r.norm() != jt[newest_name].norm():
                older[r.norm()].append((vers[i], n, newest_name))
    jb = jbpair.decide(jt, colour, other, mv, src, bd, log, older)
    log('Java<->Bedrock pairs: %s' % dict(collections.Counter(v['evidence'] for v in jb.pairs.values())))

    # ---------------------------------------------------------------- Bedrock bridge for leftovers
    R.bridge = bridge_steps(steps, vers, tex, jb, bhist)
    R.steps = steps

    # ---------------------------------------------------------------- chains and Java rows
    chains = chainsmod.Chains(vers, [set(tex[v]) for v in vers], steps)
    canon = chains.canonicals()
    rows = []
    atlas_hits_189 = set()

    # atlas first (it tells which 1.8.9 textures existed before 1.7)
    atlas_rows = []
    if atlas_jars and vers and vers[0] == '1.8.9':
        atlas_rows, atlas_info = match_atlas(atlas_jars, tex['1.8.9'], cache, block_jars[0])
        R.atlas = atlas_info
        atlas_hits_189 = set(atlas_info['matched_names'])

    # 1.8.9 texture introduction (1.7-era or 1.8-era) from the app's legacy_blocks.csv
    intro189 = texture_intro(models.get('1.8.9'), flat, read_recipes(block_jars[vers.index('1.12.2')]) if '1.12.2' in vers else {},
                             intro) if '1.8.9' in vers else {}
    R.intro189 = intro189

    manual_pins = {(f[1].rsplit('/', 1)[-1], f[2].rsplit('/', 1)[-1]) for f in MANUAL_FACTS}
    for C in sorted(canon):
        rows.extend(java_rows(C, canon[C], chains, vers, tex, steps, order, manual_pins, intro189, atlas_hits_189,
                              args.keep_pairing_evidence_in_gaps, R))
    # manual rows
    for C, old, new, last_old, first_new, why in MANUAL_FACTS:
        if C not in canon:
            R.notes.append('manual fact for %s skipped: canonical not found' % C)
            continue
        old_end_obs = [r for r in rows if r['canonical'] == C and r['name'] == old]
        new_start_obs = [r for r in rows if r['canonical'] == C and r['name'] == new]
        o_from = _next_release(order, old_end_obs[0]['to']) if old_end_obs and old_end_obs[0]['to'] else None
        if C == 'smooth_stone':
            o_from = '1.13'
        if o_from and sources.version_key(o_from) <= sources.version_key(last_old):
            rows.append(row(C, 'java', old, o_from, last_old, 'manual', why + '; fills versions without an installed jar'))
        n_to = _prev_release(order, new_start_obs[0]['from']) if new_start_obs else None
        if n_to and sources.version_key(first_new) <= sources.version_key(n_to):
            rows.append(row(C, 'java', new, first_new, n_to, 'manual', why + '; fills versions without an installed jar'))

    # ---------------------------------------------------------------- Bedrock rows
    java_canon_of = {}
    for C, nodes in canon.items():
        for i, n in nodes:
            java_canon_of.setdefault((i, n), set()).add(C)
    k = len(vers) - 1
    newest_canon = {n: next(iter(java_canon_of[(k, n)])) for n in jt}
    # Java textures outside textures/block/ (entity/, item/...) of the newest jar: some Bedrock
    # files under textures/blocks/ are their pixel twins (decorated pot patterns, conduit...)
    jfolders = sorted(set(n[len(jar_new.TEX):].split('/', 1)[0] for n in jar_new.names
                          if n.startswith(jar_new.TEX) and n.endswith('.png')) - {'block', 'gui', 'font'})
    jt_other = scan.scan_java(jar_new, cache, tuple(jfolders))
    cache.save()
    brows, nonblock = bedrock_rows(bcur, bhist, ocur, jb, bd, newest_canon, tex, vers, java_canon_of, jt_other, R)
    rows.extend(brows)
    for path in sorted(nonblock):
        if path.startswith('item/'):
            continue  # the item pass writes the Java rows of item textures
        first = newest
        for v in reversed(vers):
            if block_jars[vers.index(v)].exists(jar_new.TEX + path + '.png'):
                first = v
            else:
                break
        rec = jt_other[path]
        rows.append(row(path, 'java', path, first, '', 'pixels',
                        'Java texture outside textures/block/, listed because Bedrock keeps its pixel twin under '
                        'textures/blocks/; canonical keeps the folder; history tracked by file name only; %dx%d'
                        % rec.size()))
    rows.extend(atlas_rows_to_csv(atlas_rows, java_canon_of))

    # ---------------------------------------------------------------- items (pixel identity only)
    item_rows = build_items(block_jars, vers, cache, order, layers, bd, args.keep_pairing_evidence_in_gaps, R)
    rows.extend(item_rows)
    log('items: %d rows' % len(item_rows))

    # ---------------------------------------------------------------- canonical uniqueness
    # block textures: bare newest name; other Java textures: their path (item/..., entity/...);
    # removed Java textures: java:<last path>; Bedrock-only: bedrock:<path>. Check that no two
    # real textures share one canonical.
    problems = []
    newest_block = [n for n in jt]
    bare = collections.Counter(chainsmod.basename(n) for n in newest_block)
    problems += ['two newest block textures share the name %s' % b for b, c in bare.items() if c > 1]
    owners = collections.defaultdict(set)   # canonical -> identity texture(s)
    for r in rows:
        if r['platform'] == 'java' and r['evidence'] == 'pixels' and (r['note'].startswith('canonical name')
                                                                     or r['note'].startswith('last name before removal')
                                                                     or r['note'].startswith('Java texture outside')):
            owners[r['canonical']].add(r['name'])
    for c, names in owners.items():
        if len(names) > 1:
            problems.append('canonical %s owned by %s' % (c, sorted(names)))
    for r in rows:
        c = r['canonical']
        kind = ('bedrock-only' if c.startswith('bedrock:') else 'removed java' if c.startswith('java:')
                else 'java path' if '/' in c else 'block')
        if kind == 'block' and c not in bare:
            problems.append('block canonical %s is not a %s texture' % (c, newest))
        if kind == 'java path' and c not in owners:
            problems.append('canonical %s has no Java row' % c)
    if problems:
        for p in problems:
            log('CANONICAL PROBLEM: ' + p)
        R.notes.extend('canonical check failed: ' + p for p in problems)
    else:
        R.notes.append('canonical check passed: %d canonicals, each owned by exactly one texture '
                       '(block = bare %s name, other Java textures keep their folder such as item/ or entity/, '
                       'removed Java textures use java:<last path>, Bedrock-only use bedrock:<path>)'
                       % (len(set(r['canonical'] for r in rows)), newest))

    # ---------------------------------------------------------------- write
    plat_rank = {'java': 0, 'java_atlas': 1, 'bedrock': 2}

    def sort_key(r):
        fk = sources.version_key(r['from']) if r['platform'] != 'bedrock' and r['from'] else (
            (0,) if not r['from'] else (1,) + tuple(int(x) for x in r['from'].split('.')))
        return (r['canonical'], plat_rank[r['platform']], fk, r['name'])
    rows.sort(key=sort_key)
    path = os.path.join(args.out, 'texture_names.csv')
    with open(path, 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, lineterminator='\n')
        w.writerow(['canonical', 'platform', 'name', 'from', 'to', 'evidence', 'note'])
        for r in rows:
            w.writerow([r['canonical'], r['platform'], r['name'], r['from'], r['to'], r['evidence'], r['note']])
    log('wrote %s (%d rows)' % (path, len(rows)))
    R.rows = rows
    R.jb = jb
    R.mv = mv
    R.bd = bd
    R.bcur = bcur
    R.jt = jt
    R.newest = newest
    R.order = order
    R.manual = MANUAL_FACTS
    R.keep_gap_ev = args.keep_pairing_evidence_in_gaps
    rp = os.path.join(args.out, 'report.md')
    with open(rp, 'w', encoding='utf-8', newline='\n') as f:
        f.write(R.render())
    log('wrote %s' % rp)
    cache.save()
    return 0


# ------------------------------------------------------------------ items (Part 4: pixels only)
def build_items(block_jars, vers, cache, order, layers, bd, keep_gap_ev, R):
    """Item textures: history by pixel identity (plus the Bedrock-name bridge and same-name
    fallback, both uncertain) and Java <-> Bedrock by pixel identity only. No model pairing."""
    itex = {j.version: scan.scan_java(j, cache, ('item', 'items')) for j in block_jars}
    isteps = []
    for i in range(len(vers) - 1):
        A, B = vers[i], vers[i + 1]
        st = javahist.compare_step(A, B, itex[A], itex[B], None, None, {})
        st.kind = 'items (pixels only)'
        st.between = releases_between(order, A, B)
        isteps.append(st)
    newest = vers[-1]
    ibcur, ibhist = scan.scan_bedrock(layers, cache, 'textures/items/')
    cache.save()
    chains_pre = chainsmod.Chains(vers, [set(itex[v]) for v in vers], isteps)
    older = collections.defaultdict(list)
    k = len(vers) - 1
    jt = itex[newest]
    for C, nodes in chains_pre.canonicals().items():
        if C.startswith('java:'):
            continue
        nn = [n for (i, n) in nodes if i == k][0]
        for i, n in nodes:
            r = itex[vers[i]][n]
            if i < k and r.ok and not r.desc.get('uniform') and r.norm() != jt[nn].norm():
                older[r.norm()].append((vers[i], n, nn))
    colour = {n: r for n, r in ibcur.items() if n not in bd.pbr_role}
    ijb = jbpair.decide_pixels_only(jt, colour, older)
    R.item_bridge = bridge_steps(isteps, vers, itex, ijb, ibhist, what='item')
    chains = chainsmod.Chains(vers, [set(itex[v]) for v in vers], isteps)
    canon = {}
    for C, nodes in chains.canonicals().items():
        canon[C if C.startswith('java:') else 'item/' + C] = nodes
    rows = []
    for C in sorted(canon):
        rows.extend(java_rows(C, canon[C], chains, vers, itex, isteps, order, set(), {}, set(), keep_gap_ev, R, kind='item'))
    java_canon_of = {}
    for C, nodes in canon.items():
        for i, n in nodes:
            java_canon_of.setdefault((i, n), set()).add(C)
    newest_canon = {n: next(iter(java_canon_of[(k, n)])) for n in jt}
    brows, _nb = bedrock_rows(ibcur, ibhist, {}, ijb, bd, newest_canon, itex, vers, java_canon_of, {}, R, tag='item_')
    rows.extend(brows)
    R.item_steps = isteps
    R.item_jb = ijb
    R.item_counts = {v: len(itex[v]) for v in vers}
    R.item_bedrock_count = len(ibcur)
    return rows


# ------------------------------------------------------------------ Bedrock file-name bridge
def bridge_steps(steps, vers, tex, jb, bhist, what='block'):
    """Link old Java textures left without a successor through the Bedrock file of the same
    name (Bedrock kept most pre-1.13 names), then by name as a last resort. Mutates steps."""
    chains0 = chainsmod.Chains(vers, [set(tex[v]) for v in vers], steps)
    names_at = collections.defaultdict(lambda: collections.defaultdict(set))  # i -> newest -> {names}
    for C, nodes in chains0.canonicals().items():
        if C.startswith('java:'):
            continue
        newest_name = [n for (i, n) in nodes if i == len(vers) - 1][0]
        for i, n in nodes:
            names_at[i][newest_name].add(n)
    b_to_j = collections.defaultdict(list)
    for (j, b), info in jb.pairs.items():
        # only verified pairs, or discoveries that look identical (same shape, colours within 4 levels)
        if info['evidence'] != 'uncertain' or info.get('relation') in ('near-identical', 'identical-colour'):
            b_to_j[b].append((j, info['evidence'] if info['evidence'] != 'uncertain' else 'uncertain, visually identical'))
    report = []
    for i, st in enumerate(steps):
        A, B = vers[i], vers[i + 1]
        linked = collections.defaultdict(list)
        incoming = collections.defaultdict(list)
        for l in st.links:
            linked[l.a].append(l)
            incoming[l.b].append(l)
        gone = set(tex[A]) - set(tex[B])
        new_unmatched = set(st.new_unmatched)
        amb = {d['a']: d for d in st.ambiguous}
        for g in sorted(gone):
            if g not in bhist:
                continue
            targets, via = set(), []
            for jn, ev in b_to_j.get(g, []):
                for t in names_at[i + 1].get(jn, ()):
                    targets.add(t)
                    via.append('%s (%s)' % (jn, ev))
            if not targets:
                continue
            own = [l for l in linked.get(g, []) if l.kind in ('rename', 'split')]
            if own:
                agree = [l for l in own if l.b in targets]
                if agree:
                    for l in agree:
                        if l.evidence == 'uncertain' and 'name tokens' in l.note:
                            l.evidence = 'model'
                            l.note = 'block link by name tokens, corroborated by the Bedrock file of the same name (%s)' % g
                        else:
                            l.note = (l.note + '; ' if l.note else '') + 'agrees with Bedrock file %s' % g
                    continue
                extra = sorted(t for t in targets if t in new_unmatched and not incoming.get(t))
                report.append({'step': '%s -> %s' % (A, B), 'old': g, 'linked': [l.b for l in own],
                               'bridge': sorted(targets), 'action': ('added split' if extra else 'reported only')})
                for t in extra:
                    l = javahist.Link(g, t, 'split', 'uncertain',
                                      'Bedrock keeps a file named %s that pairs with Java %s; the rename found here was %s'
                                      % (g, ', '.join(via), ', '.join(x.b for x in own)))
                    st.links.append(l)
                    incoming[t].append(l)
                    new_unmatched.discard(t)
                continue
            # no rename of its own (unmatched, ambiguous, or only merged)
            avail = sorted(t for t in targets if not [x for x in incoming.get(t, []) if x.kind in ('rename', 'split')])
            if not avail:
                report.append({'step': '%s -> %s' % (A, B), 'old': g, 'linked': [l.b for l in linked.get(g, [])],
                               'bridge': sorted(targets), 'action': 'target already has a predecessor; reported only'})
                continue
            for t in avail:
                ev, why = 'uncertain', 'same file name in Bedrock'
                d = amb.get(g)
                if d and t in (d.get('pixel_candidates') or []):
                    ev, why = 'pixels', 'identical pixels shared by several old textures; Bedrock file name picks this one'
                elif d and t in (d.get('model_candidates') or []):
                    ev, why = 'model', 'model candidates %s; Bedrock file name picks this one' % d.get('model_candidates')
                l = javahist.Link(g, t, 'rename', ev, '%s: Bedrock %s pairs with Java %s' % (why, g, ', '.join(via)))
                st.links.append(l)
                incoming[t].append(l)
                report.append({'step': '%s -> %s' % (A, B), 'old': g, 'linked': [], 'bridge': [t], 'action': 'linked (%s)' % ev})
            st.gone_unmatched = [x for x in st.gone_unmatched if x != g]
            st.ambiguous = [x for x in st.ambiguous if x['a'] != g]
        st.new_unmatched = sorted(t for t in st.new_unmatched if not [x for x in incoming.get(t, []) if x.kind in ('rename', 'split')])
        # last resort for what is still unlinked (uncertain, name-based):
        #  * a pixel twin whose twin was linked: the leftover new texture named by its tokens
        #  * the same file name on both sides of the step
        for g in list(st.gone_unmatched):
            if [l for l in st.links if l.a == g]:
                continue
            d = amb.get(g)
            target, why = None, None
            if d and d.get('twins_in_a'):
                toks = set(chainsmod.basename(g).split('_'))
                cands = [t for t in st.new_unmatched
                         if len(chainsmod.basename(t).split('_')) >= 2 and set(chainsmod.basename(t).split('_')) <= toks]
                if len(cands) == 1:
                    target = cands[0]
                    why = ('pixel twin of %s (identical old files); target picked by file-name tokens, no pixel or model evidence'
                           % ', '.join(x for x in d['twins_in_a'] if x != g))
            if target is None:
                same = [t for t in st.new_unmatched if chainsmod.basename(t) == chainsmod.basename(g)]
                if len(same) == 1:
                    target = same[0]
                    why = 'same file name only; no pixel or model evidence (%s)' % (
                        'art and models differ' if what == 'block' else 'art differs; items are paired by pixels only')
            if target:
                l = javahist.Link(g, target, 'rename', 'uncertain', why)
                st.links.append(l)
                st.new_unmatched = [t for t in st.new_unmatched if t != target]
                st.gone_unmatched = [x for x in st.gone_unmatched if x != g]
                st.ambiguous = [x for x in st.ambiguous if x['a'] != g]
                report.append({'step': '%s -> %s' % (A, B), 'old': g, 'linked': [], 'bridge': [target],
                               'action': 'linked by name (uncertain): ' + why})
    return report


# ------------------------------------------------------------------ rows
def row(canonical, platform, name, frm, to, evidence, note):
    return {'canonical': canonical, 'platform': platform, 'name': name, 'from': frm or '', 'to': to or '',
            'evidence': evidence, 'note': note}


def _next_release(order, v):
    if not order or v not in order:
        return None
    i = order.index(v)
    return order[i + 1] if i + 1 < len(order) else None


def _prev_release(order, v):
    if not order or v not in order:
        return None
    i = order.index(v)
    return order[i - 1] if i > 0 else None


EV_RANK = {'pixels+model': 5, 'pixels': 4, 'model': 3, 'manual': 2, 'uncertain': 1}


def _span(between):
    if not between:
        return ''
    return between[0] if len(between) == 1 else '%s-%s' % (between[0], between[-1])


def java_rows(C, nodes, chains, vers, tex, steps, order, manual_pins, intro189, atlas_hits, keep_gap_ev, R, kind='block'):
    k = len(vers) - 1
    removed = C.startswith('java:')
    if removed:
        root_name = C[len('java:'):]
        root_i = max(i for i, n in nodes if n == root_name)
    else:
        root_i = k
        root_name = [n for i, n in nodes if i == k][0]
    out = []
    for (n, i0, i1) in chains.eras(nodes):
        notes = []
        ev = None
        is_root = (n == root_name and i0 <= root_i <= i1)
        if is_root:
            ev = 'pixels'
            notes.append('canonical name' if not removed else 'last name before removal; not in %s' % vers[root_i + 1])
        else:
            outs = chains.outgoing(i1, n, nodes)
            if not outs:
                ev = 'uncertain'
                notes.append('no link to the next name was found')
            else:
                outs.sort(key=lambda bl: -EV_RANK.get(bl[1].evidence, 0))
                b, l = outs[0]
                ev = l.evidence
                verb = {'rename': 'renamed to', 'merge': 'merged into', 'split': 'became (split)'}.get(l.kind, 'became')
                targets = ', '.join(x for x, _ in outs)
                txt = '%s %s' % (verb, targets)
                between = steps[i1].between
                pinned = (n.rsplit('/', 1)[-1], b.rsplit('/', 1)[-1]) in manual_pins
                if between:
                    if pinned:
                        txt += ' (date from known history, see manual rows)'
                    else:
                        txt += ' between %s and %s (exact release unknown: no installed jar for %s)' % (vers[i1], vers[i1 + 1], _span(between))
                        if not keep_gap_ev:
                            txt += '; pairing evidence: %s' % ev
                            ev = 'uncertain'
                else:
                    txt += ' in %s' % vers[i1 + 1]
                if l.note:
                    txt += ' [%s]' % l.note
                notes.append(txt)
        # start of the era
        frm = vers[i0]
        if i0 > 0:
            ins = chains.incoming(i0, n, nodes)
            between = steps[i0 - 1].between
            if ins:
                s = 'renamed from %s' % ', '.join(sorted(a for a, _ in ins))
                if between:
                    s += ' between %s and %s' % (vers[i0 - 1], vers[i0])
                notes.append(s)
            else:
                notes.append('first seen in %s%s' % (vers[i0], (' (added between %s and %s)' % (vers[i0 - 1], vers[i0])) if between else ''))
        elif vers[0] == '1.8.9' and kind != 'block':
            notes.append('first installed jar with separate %s files is 1.8.9; earlier names not verified' % kind)
        elif vers[0] == '1.8.9':
            info = intro189.get(n)
            if n in atlas_hits or (info and info[0] is not None and info[0] <= legacy.R_1_7_2):
                frm = '1.7.2'
                why = 'pre-1.7 texture (matches a b1.0 terrain.png tile)' if n in atlas_hits else 'block introduced %s (legacy_blocks.csv)' % info[1]
                notes.append('from=1.7.2: 1.7.2-1.8.8 assumed to match the installed 1.8.9 (Java renamed block textures in 1.7; %s)' % why)
            elif info and info[0] is not None:
                notes.append('block introduced %s (legacy_blocks.csv): name valid since then at the earliest; first installed jar is 1.8.9' % info[1])
            else:
                notes.append('first installed jar with separate block files is 1.8.9; introduction date not determined')
        to = '' if (is_root and not removed) else vers[i1]
        # art changes, size, animation
        changed = [vers[x] for x in range(i0 + 1, i1 + 1)
                   if tex[vers[x]][n].norm() != tex[vers[x - 1]][n].norm()]
        if changed:
            notes.append('vanilla art changed in %s' % ', '.join(changed))
        rec = tex[vers[i1]][n]
        w, h = rec.size()
        if rec.anim is not None:
            fr = rec.anim.get('frames')
            notes.append('animated (.mcmeta): %d frames of %dx%d in the strip%s%s' % (
                rec.desc.get('frames', 1), w, w if h > w else h,
                ', frametime %s' % rec.anim['frametime'] if 'frametime' in rec.anim else '',
                ', custom frame order of %d' % len(fr) if isinstance(fr, list) else ''))
        elif (w, h) != (16, 16):
            notes.append('%dx%d' % (w, h))
        if rec.extra.get('mcmeta_texture'):
            notes.append('.mcmeta texture section: %s' % ', '.join('%s=%s' % kv for kv in sorted(rec.extra['mcmeta_texture'].items())))
        if rec.desc.get('uniform'):
            notes.append('single-colour image (pixel identity not used as evidence)')
        out.append(row(C, 'java', n, frm, to, ev, '; '.join(notes)))
    return out


def texture_intro(models189, flat, recipes1122, intro):
    """{1.8.9 texture: (rank, label)} earliest introduction of a block variant using it."""
    if models189 is None:
        return {}
    bs = set(models189.blockstate_names())
    blocks13 = set(r['block13'] for r in flat)
    links = javahist.flattening_links(flat, bs, recipes1122, blocks13, {})
    rows_by_b13 = collections.defaultdict(list)
    for r in flat:
        rows_by_b13[r['block13']].append(r)
    out = {}
    for f in sorted(bs):
        lk = links.get(f)
        if not lk:
            continue
        best = None
        for r in rows_by_b13.get(lk[0], []):
            v = legacy.intro_of(intro, r['legacy_id'], r['data'])
            if v and (best is None or v[0] < best[0]):
                best = v
        if best is None:
            continue
        for c in models189.cases(f):
            m, _rot = javahist.first_model(models189, c[2])
            for ts in javahist.face_lists(m).values():
                for t in ts:
                    if t not in out or best[0] < out[t][0]:
                        out[t] = best
    return out


# ------------------------------------------------------------------ Bedrock rows
def bedrock_rows(bcur, bhist, ocur, jb, bd, newest_canon, tex, vers, java_canon_of, jt_other, R, tag=''):
    rows = []
    refs = bd.referenced_paths()
    pairs_by_b = collections.defaultdict(list)
    for (j, b), info in jb.pairs.items():
        pairs_by_b[b].append((j, info))
    leftovers = [b for b, r in sorted(bcur.items())
                 if b not in bd.pbr_role and not pairs_by_b.get(b) and r.ok and not r.desc.get('uniform')]
    # 1. leftovers that are pixel twins of Java textures outside textures/block/
    nb_index = collections.defaultdict(list)
    for n, r in jt_other.items():
        if r.ok and not r.desc.get('uniform'):
            nb_index[r.norm()].append(n)
    nonblock_pairs = {}
    for b in leftovers:
        hits = nb_index.get(bcur[b].norm(), [])
        if hits:
            nonblock_pairs[b] = sorted(hits)
    setattr(R, tag + 'bedrock_nonblock_java', nonblock_pairs)
    # 2. leftovers that are pixel twins of an older Java block texture (or of a newest one
    #    whose twin Bedrock file was already taken)
    old_index = collections.defaultdict(list)
    for i, v in enumerate(vers):
        for n, r in tex[v].items():
            if r.ok and not r.desc.get('uniform'):
                old_index[r.norm()].append((i, n))
    old_pairs = {}
    for b in leftovers:
        if b in nonblock_pairs:
            continue
        hits = old_index.get(bcur[b].norm(), [])
        if hits:
            i, n = max(hits)
            cs = sorted(java_canon_of.get((i, n), ()))
            if cs:
                old_pairs[b] = (vers[i], n, cs)
    setattr(R, tag + 'bedrock_old_java', old_pairs)
    all_b = dict(bcur)
    for b, lst in pairs_by_b.items():
        if b not in all_b and b in ocur:
            all_b[b] = ocur[b]
    for b in sorted(all_b):
        rec = all_b[b]
        hist = bhist.get(b) or [rec]
        first = hist[0].source
        frm = sources.pack_label(first)
        notes = []
        w, h = rec.size()
        notes.append('%s%s' % (rec.ext.lstrip('.'), '' if (w, h) == (16, 16) else ' %dx%d' % (w, h)))
        # one entry per layer (a layer may hold the same stem as .png and .tga)
        layers_seen = []
        per_layer = collections.OrderedDict()
        for x in hist:
            per_layer.setdefault(x.source, []).append(x)
        for src_name, recs in per_layer.items():
            layers_seen.append((src_name, '/'.join(sorted(r.ext.lstrip('.') for r in recs)), set(r.norm() for r in recs)))
        fmts = []
        for src_name, ext, _n in layers_seen:
            if not fmts or fmts[-1][1] != ext:
                fmts.append((src_name, ext))
        if len(fmts) > 1:
            notes.append('format: ' + ', '.join('%s from %s' % (e, sources.pack_label(s) or 'base pack') for s, e in fmts))
        changes = [b2[0] for b1, b2 in zip(layers_seen, layers_seen[1:]) if not (b1[2] & b2[2])]
        if changes:
            notes.append('art replaced in ' + ', '.join(sources.pack_label(c) or 'base pack' for c in changes))
        same_layer = [s for s, e, _n in layers_seen if '/' in e]
        if same_layer:
            notes.append('%s holds both .png and .tga of this file' % ', '.join(sources.pack_label(s) or 'base pack' for s in same_layer))
        fb = bd.flipbook.get(b)
        if fb:
            e = fb[1]
            notes.append('flipbook (flipbook_textures.json): atlas_tile=%s, %s%s'
                         % (e.get('atlas_tile'),
                            ('frame order of %d' % len(e['frames'])) if isinstance(e.get('frames'), list)
                            else '%d frames in the strip' % rec.desc.get('frames', 1),
                            (', ticks_per_frame=%s' % e['ticks_per_frame']) if 'ticks_per_frame' in e else ''))
        elif rec.desc.get('frames', 1) > 1:
            notes.append('strip of %d frames' % rec.desc['frames'])
        if b in bd.texture_sets:
            notes.append('PBR set: ' + ', '.join('%s=%s' % (k, v.rsplit('/', 1)[-1]) for k, v in sorted(bd.texture_sets[b].items())))
        if b.startswith('blocks/') and b not in refs:
            notes.append('not referenced by terrain_texture.json, flipbook_textures.json or a texture set')
        role = bd.pbr_role.get(b)
        if role:
            notes.append('PBR %s map of %s' % (role[0], role[1]))
            rows.append(row('bedrock:' + b, 'bedrock', b, frm, '', 'pixels', '; '.join(notes)))
            continue
        pl = pairs_by_b.get(b, [])
        if pl:
            for j, info in sorted(pl):
                C = newest_canon.get(j, 'java:' + j)
                jr = tex[vers[-1]][j]
                extra = [x for x in info['notes'] if x]
                if jr.size() != rec.size():
                    extra.append('size differs: Java %dx%d, Bedrock %dx%d' % (jr.size() + rec.size()))
                rows.append(row(C, 'bedrock', b, frm, '', info['evidence'], '; '.join(notes + extra)))
        elif b in nonblock_pairs:
            for path in nonblock_pairs[b]:
                rows.append(row(path, 'bedrock', b, frm, '', 'pixels',
                                '; '.join(notes + ['pixel twin of Java %s (outside textures/block/)' % path])))
        elif b in old_pairs:
            v, n, cs = old_pairs[b]
            for C in cs:
                if v != vers[-1]:
                    why = 'pixel-identical to Java %s as of %s (older art; the %s art differs)' % (n, v, vers[-1])
                else:
                    why = ('pixel-identical to Java %s, whose own Bedrock twin is another file: Bedrock reuses the same '
                           'art here' % n)
                rows.append(row(C, 'bedrock', b, frm, '', 'pixels', '; '.join(notes + [why])))
        else:
            rows.append(row('bedrock:' + b, 'bedrock', b, frm, '', 'pixels', '; '.join(notes + ['no Java pair found'])))
    used_nonblock = set(p for lst in nonblock_pairs.values() for p in lst)
    return rows, used_nonblock


# ------------------------------------------------------------------ pre-1.5 atlas
def match_atlas(atlas_jars, tex189, cache, jar189):
    """Match each 16x16 tile of terrain.png to 1.8.9 block textures by pixels."""
    idx = collections.defaultdict(list)
    images = {}
    for n, r in tex189.items():
        if not r.ok:
            continue
        raw = jar189.read(r.path)
        img = pixels.decode(raw, '.png')
        images[n] = img
        w, h, rgba = img.width, img.height, img.rgba
        if w != 16:
            continue
        for fi, fr in enumerate(pixels.frames(w, h, pixels.normalized(rgba))):
            idx[pixels.hash_rgba(16, 16, fr)].append((n, fi))
    out_rows = []
    info = {'jars': [], 'matched': [], 'unmatched': [], 'empty': [], 'matched_names': set(), 'partial': [],
            'uniform': []}
    firsts = {}
    for n, im in images.items():
        if im.width == 16 and im.height >= 16:
            firsts[n] = pixels.normalized(im.rgba[:16 * 16 * 4])
    for j in atlas_jars:
        raw = j.read('terrain.png')
        img = pixels.decode(raw, '.png')
        info['jars'].append((j.version, img.width, img.height))
        tw = img.width // 16
        th = img.height // 16
        for ty in range(16):
            for tx in range(16):
                index = ty * 16 + tx
                tile = pixels.crop(img.width, img.height, img.rgba, tx * tw, ty * th, tw, th)
                nt = pixels.normalized(tile)
                if tile[3::4].count(0) == tw * th:
                    info['empty'].append((j.version, index))
                    continue
                hits = idx.get(pixels.hash_rgba(tw, th, nt), []) if tw == 16 else []
                if hits:
                    names = sorted(set(n for n, _fi in hits))
                    info['matched'].append((j.version, index, names, hits))
                    info['matched_names'].update(names)
                    out_rows.append((j.version, index, hits, None))
                    continue
                colours = len(set(nt[p:p + 4] for p in range(0, len(nt), 4)))
                if tw != 16:
                    info['unmatched'].append((j.version, index, [], colours))
                    continue
                # share of identical pixels among pixels visible in either image (first frame)
                best = sorted(((pixels.union_identity(nt, f), n) for n, f in firsts.items()), reverse=True)[:3]
                info['unmatched'].append((j.version, index, best[:2], colours))
                if colours <= 2:
                    info['uniform'].append((j.version, index, colours))
                elif best and best[0][0] >= 0.8 and (len(best) == 1 or best[0][0] - best[1][0] >= 0.1):
                    info['partial'].append((j.version, index, best[0][1], best[0][0]))
                    out_rows.append((j.version, index, [(best[0][1], 0)], best[0][0]))
    return out_rows, info


def atlas_rows_to_csv(atlas_rows, java_canon_of):
    rows = []
    for ver, index, hits, partial in atlas_rows:
        by_name = collections.defaultdict(list)
        for n, fi in hits:
            by_name[n].append(fi)
        for n in sorted(by_name):
            cs = sorted(java_canon_of.get((0, n), ()))
            fis = sorted(by_name[n])
            for C in cs:
                if partial is None:
                    note = 'tile x=%d y=%d of terrain.png; identical to 1.8.9 %s%s' % (
                        index % 16, index // 16, n, '' if fis == [0] else ' (animation frame %s)' % ','.join(map(str, fis)))
                    if len(by_name) > 1:
                        note += '; tile also identical to %s' % ', '.join(x for x in sorted(by_name) if x != n)
                    ev = 'pixels'
                else:
                    note = ('tile x=%d y=%d of terrain.png; not identical: %.0f%% of its visible pixels equal 1.8.9 %s '
                            '(art edited between %s and 1.8.9; next best match at least 10 points lower)'
                            % (index % 16, index // 16, 100 * partial, n, ver))
                    ev = 'uncertain'
                rows.append(row(C, 'java_atlas', 'terrain:%d' % index, ver, ver, ev, note))
    return rows


if __name__ == '__main__':
    sys.exit(main())
