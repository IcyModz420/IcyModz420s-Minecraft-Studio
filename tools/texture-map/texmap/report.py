"""Markdown report for the texture name table."""
import collections


def _md_list(items, empty='(none)'):
    items = list(items)
    if not items:
        return empty + '\n'
    return ''.join('- %s\n' % x for x in items)


def _code_list(names, per_line=6):
    names = list(names)
    if not names:
        return '(none)\n'
    out = []
    for i in range(0, len(names), per_line):
        out.append(', '.join('`%s`' % n for n in names[i:i + per_line]))
    return ''.join('- %s\n' % x for x in out)


class Report(object):
    def __init__(self):
        self.meta = {}
        self.java_missing = []
        self.java_versions = []
        self.atlas_versions = []
        self.java_counts = {}
        self.decode_errors = []
        self.bedrock_layers = []
        self.ignored_packs = []
        self.collisions = []
        self.unresolved_bedrock_blocks = collections.Counter()
        self.bridge = []
        self.steps = []
        self.atlas = None
        self.intro189 = {}
        self.notes = []
        self.bedrock_old_java = {}
        self.rows = []
        self.jb = None
        self.mv = None
        self.bd = None
        self.bcur = {}
        self.jt = {}
        self.newest = None
        self.order = None
        self.manual = []
        self.keep_gap_ev = False

    # ------------------------------------------------------------------
    def render(self):
        o = []
        w = o.append
        rows = self.rows
        w('# Block texture name table: generation report\n\n')
        w('Generated %s by `tools/texture-map/build.py` (Python %s) from the game files installed on this PC. '
          'Output: `out/texture_names.csv` (%d rows). Names, sizes and pixel hashes only; no image data is stored.\n\n'
          % (self.meta.get('generated'), self.meta.get('python'), len(rows)))
        w('Read this first:\n\n')
        w('- Newest Java release with a jar on this PC: **%s**. The launcher manifest lists %s as the latest release, '
          'but its jar is not downloaded (see Inputs), so "canonical" means the %s name.\n'
          % (self.newest, self.meta.get('manifest_latest', {}).get('release', '?'), self.newest))
        w('- No jars exist for 1.13-1.16.3. Every rename across 1.12.2 -> 1.16.4 is dated only to that range and its '
          'rows carry `evidence=uncertain` with the pairing method in the note (see "Uncertain dates").\n')
        w('- 1.5-1.6.4 names: not available (no jar installed). No rows were invented for that era.\n')
        w('- `from`/`to` are the first/last installed release in which the file was seen, except: 1.7-era names first '
          'seen in 1.8.9 use `from=1.7.2` (assumption stated in each note), and `manual` rows fill a few gap releases '
          'from the historical facts given in the brief.\n')
        w('- Item textures are included (canonical `item/<name>`) but paired by pixel identity only; see "Items".\n\n')

        # ---------------------------------------------------------- inputs
        w('## Inputs\n\n')
        w('### Java\n\n')
        w('| version | block textures | folder | pack format |\n|---|---|---|---|\n')
        for v in self.java_versions:
            n, folder, name, pv = self.java_counts[v]
            w('| %s | %d | `%s/` | %s |\n' % (v, n, folder, pv if pv is not None else '(no version.json)'))
        for v in self.atlas_versions:
            w('| %s | atlas `terrain.png` | - | (no version.json) |\n' % v)
        w('\nReleases named in the brief without a jar on disk (folder contents shown):\n\n')
        for v, contents in self.java_missing:
            w('- %s: %s\n' % (v, ', '.join(contents) if contents else 'no folder'))
        w('\nSnapshots, pre-releases, release candidates and modded folders were ignored as instructed.\n\n')
        w('### Bedrock\n\n')
        w('%d vanilla layers, applied in version order (later overrides earlier): %s.\n\n'
          % (len(self.bedrock_layers), ', '.join('`%s`' % x for x in self.bedrock_layers)))
        w('Packs not used, and what they hold under `textures/blocks/`:\n\n')
        w('| pack | block images | sample | block JSON |\n|---|---|---|---|\n')
        for nm, n, sample, js in self.ignored_packs:
            w('| %s | %d | %s | %s |\n' % (nm, n, ', '.join('`%s`' % s.rsplit('/', 1)[-1] for s in sample[:6]) or '-',
                                           ', '.join(js) or '-'))
        w('\nNone of these is part of the vanilla game for a normal world: `chemistry*` is the Education Edition chemistry '
          'content, `experimental_y_2026_drop_4` only holds experimental ice/icicle blocks, and the rest hold no block '
          'textures. They were left out of the table.\n\n')
        by_top = collections.Counter(c[1].split('/', 1)[0] if '/' in c[1] else c[1] for c in self.collisions)
        tex_coll = [c for c in self.collisions if c[1].startswith('textures/blocks/')]
        w('Paths present both loose and inside `__brarchive` in the same pack: %d (%s). None of them is a block image '
          '(%d under `textures/blocks/`). The JSON files used here (`terrain_texture.json`, `flipbook_textures.json`) '
          'were checked byte-identical in both places; where an archived copy is an empty placeholder (fonts, materials) '
          'the loose copy is read.\n\n' % (len(self.collisions), ', '.join('%s %d' % kv for kv in sorted(by_top.items())),
                                         len(tex_coll)))
        if self.unresolved_bedrock_blocks:
            w('Bedrock block names from `j2b` without a `blocks.json` entry (even after the app\'s upgrade-schema renames): %s\n\n'
              % ', '.join('`%s`' % k for k in sorted(self.unresolved_bedrock_blocks)))

        # ---------------------------------------------------------- counts
        w('## Counts\n\n')
        c = collections.Counter((_scope(r), r['platform'], r['evidence']) for r in rows)
        evs = ['pixels', 'pixels+model', 'model', 'manual', 'uncertain']
        w('| scope | platform | ' + ' | '.join(evs) + ' | total |\n|---|---|' + '---|' * (len(evs) + 1) + '\n')
        for s in ('block', 'item'):
            for p in ('java', 'java_atlas', 'bedrock'):
                tot = sum(c[(s, p, e)] for e in evs)
                if tot:
                    w('| %s | %s | %s | %d |\n' % (s, p, ' | '.join(str(c[(s, p, e)]) for e in evs), tot))
        w('| all | all | %s | %d |\n' % (' | '.join(str(sum(v for (s, p, e), v in c.items() if e == ev)) for ev in evs), len(rows)))
        canon = collections.Counter()
        for r in rows:
            k = 'bedrock-only' if r['canonical'].startswith('bedrock:') else ('removed Java texture' if r['canonical'].startswith('java:') else 'Java %s name' % self.newest)
            canon[k] += 0
        cs = {}
        for r in rows:
            cs.setdefault(r['canonical'], r['canonical'])
        kinds = collections.Counter('bedrock-only' if x.startswith('bedrock:') else ('removed Java texture' if x.startswith('java:') else 'Java %s texture' % self.newest)
                                    for x in cs)
        w('\nCanonical textures: %d (%s).\n\n' % (len(cs), ', '.join('%s %d' % (k, v) for k, v in sorted(kinds.items()))))

        # Java <-> Bedrock
        jb = self.jb
        if jb:
            newest_canons = set(n.rsplit('/', 1)[-1] for n in self.jt)
            per_c = collections.defaultdict(set)
            for r in rows:
                if r['platform'] == 'bedrock' and r['canonical'] in newest_canons:
                    per_c[r['canonical']].add(r['evidence'])
            jpairs = collections.Counter()
            for c, s in per_c.items():
                best = 'pixels+model' if 'pixels+model' in s else ('pixels' if 'pixels' in s else ('model' if 'model' in s else 'uncertain'))
                jpairs[best] += 1
            self.java_unpaired_final = sorted(c for c in newest_canons if c not in per_c)
            pbr = sum(1 for b in self.bcur if b in self.bd.pbr_role)
            w('### Java %s <-> Bedrock\n\n' % self.newest)
            w('Java block textures: %d. With at least one Bedrock row: %d; by strongest evidence: pixels+model %d, '
              'pixels %d, model %d, uncertain %d. Without any Bedrock row: %d.\n\n'
              % (len(self.jt), len(per_c), jpairs['pixels+model'], jpairs['pixels'], jpairs['model'], jpairs['uncertain'],
                 len(self.java_unpaired_final)))
            self.jpairs = jpairs
            pc = collections.Counter(info['evidence'] for info in jb.pairs.values())
            kc = collections.Counter(info['kind'] for info in jb.pairs.values())
            w('Pair rows: %s. By method: %s.\n\n' % (', '.join('%s %d' % kv for kv in sorted(pc.items())),
                                                    ', '.join('%s %d' % kv for kv in sorted(kc.items()))))
            w('Bedrock files under `textures/blocks/` (layered, current): %d, of which %d are Vibrant Visuals PBR maps '
              '(MER(S)/normal/heightmap, listed as `bedrock:` rows) and %d colour textures; %d colour textures have no Java '
              'pair.\n\n' % (len(self.bcur), pbr, len(self.bcur) - pbr, len(jb.bedrock_unpaired)))

        # ---------------------------------------------------------- Java history
        w('## Java name history\n\n')
        w('| step | gap releases (no jar) | renames | merges | splits | removed (no successor) | added |\n|---|---|---|---|---|---|---|\n')
        for st in self.steps:
            kinds = collections.Counter(l.kind for l in st.links)
            w('| %s -> %s | %s | %d | %d | %d | %d | %d |\n'
              % (st.a_ver, st.b_ver, (', '.join(st.between) if st.between and len(st.between) <= 4 else
                                       ('%s ... %s (%d)' % (st.between[0], st.between[-1], len(st.between)) if st.between else '-')),
                 kinds['rename'], kinds['merge'], kinds['split'], len(st.gone_unmatched), len(st.new_unmatched)))
        w('\nRenames, merges and splits (excluding the 1.12.2 -> 1.16.4 flattening, listed in the CSV):\n\n')
        for st in self.steps:
            if st.kind.startswith('flattening'):
                continue
            for l in st.links:
                if l.kind != 'same':
                    w('- %s -> %s: `%s` %s `%s` (%s%s)\n' % (st.a_ver, st.b_ver, l.a, l.kind, l.b, l.evidence,
                                                         ('; ' + l.note) if l.note else ''))
        for st in self.steps:
            if not st.kind.startswith('flattening'):
                continue
            ev = collections.Counter(l.evidence for l in st.links if l.kind != 'same')
            w('\n### %s -> %s (flattening and 1.14 Texture Update)\n\n' % (st.a_ver, st.b_ver))
            w('Pairs by evidence before the gap rule: %s. Block links used for model pairing: %s.\n\n'
              % (', '.join('%s %d' % kv for kv in sorted(ev.items())),
                 ', '.join('%s %d' % kv for kv in sorted(collections.Counter(v[1] for v in st.block_links.values()).items()))))
            w('Block links come from the app tables: a 1.12.2 recipe file named like the blockstate gives the item id:data, '
              'which `flattening.csv` maps to the 1.13 block (strong); a blockstate named like a 1.12 registry id whose data '
              'values all flatten to one block (strong); data value 0 of such an id (medium); a blockstate named like a 1.13 '
              'block (medium, validated by model structure); name-token similarity (weak, accepted only if a Bedrock file of '
              'the same name agrees).\n\n')
            w('Old textures with no successor after all evidence (%d):\n\n' % len(st.gone_unmatched))
            w(_code_list(st.gone_unmatched))
            if st.ambiguous:
                w('\nAmbiguous (left unlinked):\n\n')
                for d in st.ambiguous:
                    w('- `%s`: %s\n' % (d['a'], {k: v for k, v in d.items() if k != 'a' and k != 'support'}))
            if st.block_issues:
                w('\nBlock links that could not be aligned or had conflicting faces (their conflicting pairs were dropped):\n\n')
                for fa, fb, status, confl in st.block_issues:
                    w('- 1.12.2 `%s` -> 1.16.4 `%s`: %s%s\n' % (fa, fb, status, ('; ' + '; '.join('%s -> %s' % (x, y) for x, y in confl)) if confl else ''))
        w('\n### Bedrock file-name bridge\n\n')
        w('Bedrock still uses most pre-1.13 Java file names (e.g. `blocks/planks_oak`). Where an old Java texture has no '
          'pixel/model successor, the Java texture paired with the Bedrock file of the same name is proposed. These links '
          'are `uncertain` unless they only break a pixel or model tie.\n\n')
        for d in self.bridge:
            w('- %s: `%s` -> %s; already linked to %s; %s\n' % (d['step'], d['old'], ', '.join('`%s`' % x for x in d['bridge']),
                                                              ', '.join('`%s`' % x for x in d['linked']) or 'nothing', d['action']))
        if not self.bridge:
            w('(none)\n')

        # ---------------------------------------------------------- uncertain dates
        w('\n## Uncertain dates and gaps\n\n')
        w('Renames between two installed jars with releases in between cannot be dated exactly. Unless a historical fact '
          'from the brief pins the date (then `manual` rows cover the gap releases), the row of the old name gets '
          '`evidence=uncertain`, the range in its note and the pairing evidence after "pairing evidence:". %s\n\n'
          % ('(Run with --keep-pairing-evidence-in-gaps to keep pixels/model instead.)' if not self.keep_gap_ev else
             '(This run kept pixels/model evidence on such rows.)'))
        gaps = collections.Counter()
        for r in rows:
            if r['platform'] == 'java' and 'exact release unknown' in r['note']:
                gaps[r['note'].split(' between ', 1)[1].split(' (', 1)[0] if ' between ' in r['note'] else '?'] += 1
        w('| renamed between | rows |\n|---|---|\n')
        for k, v in sorted(gaps.items()):
            w('| %s | %d |\n' % (k, v))
        w('\nOpen items:\n\n')
        w('- 1.5-1.6.4 names: not available (no jar installed). Those releases used older camelCase names; no rows.\n')
        w('- 1.13-1.16.3: no jars. Only 1.12.2 and 1.16.4 names are verified; rows do not claim anything in between '
          '(except the `manual` rows for stone_slab_top -> smooth_stone in 1.14).\n')
        n172 = sum(1 for r in rows if r['platform'] == 'java' and r['from'] == '1.7.2')
        w('- %d Java rows use `from=1.7.2`: 1.7.2-1.8.8 are assumed to match the installed 1.8.9 (Java renamed block '
          'textures in 1.7, per the coordinator). Textures whose blocks were added in 1.8 (per the app\'s '
          '`legacy_blocks.csv`) keep `from=1.8.9`.\n' % n172)
        w('- Missing jars inside the covered range: %s. Renames inside those gaps are dated to a range.\n'
          % ', '.join(v for v, _c in self.java_missing))
        w('- Java %s is the newest installed jar; textures added or renamed in later releases are not covered. Bedrock '
          '1.26.x already ships blocks with no Java %s texture (see the Bedrock-only list); some of them may exist in '
          'Java 26.2/26.3.\n' % (self.newest, self.newest))
        w('- Pre-1.5 atlas: only b1.0 is installed (1.2.1 has no jar), so `java_atlas` rows are verified for b1.0 only.\n')

        # ---------------------------------------------------------- disagreements
        w('\n## Disagreements\n\n')
        hist_dis = [(st, d) for st in self.steps for d in st.disagreements]
        w('### Java history: pixels vs model\n\n')
        if hist_dis:
            for st, d in hist_dis:
                w('- %s -> %s: `%s` pixels say `%s`, models say `%s`\n' % (st.a_ver, st.b_ver, d['a'], d['pixels'], d['model']))
        else:
            w('(none: wherever both methods gave an answer they agreed)\n')
        if jb:
            w('\n### Java <-> Bedrock: pixel twin vs model faces\n\n')
            w('The pixel twin is kept (primary evidence). If the file Bedrock\'s blocks.json uses instead is not the pixel '
              'twin of another Java texture, it was added as a second `model` row.\n\n')
            for d in jb.disagreements:
                w('- `%s`: pixels -> %s; blocks.json faces -> %s (blocks: %s)\n'
                  % (d['java'], ', '.join('`%s`' % x for x in d['pixels']),
                     ', '.join('`%s` (%d blocks)' % (b, n) for b, n, _f in d['model']), ', '.join(d['blocks'])))
            if not jb.disagreements:
                w('(none)\n')
            w('\n### Model pairs rejected\n\n')
            w('Bedrock uses another Java texture\'s pixel twin on that face, so the model pairing is not a texture '
              'correspondence:\n\n')
            for d in jb.rejected:
                w('- `%s` -> `%s`: %s (blocks: %s)\n' % (d['java'], d['bedrock'], d['why'], ', '.join(d['blocks'])))
            if not jb.rejected:
                w('(none)\n')
            w('\n### Ambiguous\n\n')
            for d in jb.ambiguous:
                key = 'java' if 'java' in d else 'bedrock'
                w('- %s `%s`: %s\n' % (key, d[key], {k: v for k, v in d.items() if k != key}))
            if not jb.ambiguous:
                w('(none)\n')
            w('\n### Pixel duplicates\n\n')
            for d in jb.dup_bedrock:
                w('- `%s`: %s\n' % (d['bedrock'], {k: v for k, v in d.items() if k != 'bedrock'}))
            if not jb.dup_bedrock:
                w('(none)\n')

        # ---------------------------------------------------------- unmatched
        if jb:
            w('\n## Unmatched textures\n\n')
            un = getattr(self, 'java_unpaired_final', jb.java_unpaired)
            w('### Java %s block textures with no Bedrock row (%d)\n\n' % (self.newest, len(un)))
            w(_code_list('block/' + c for c in un))
            w('\n### Bedrock colour textures with no Java %s pair (%d)\n\n' % (self.newest, len(jb.bedrock_unpaired)))
            old = self.bedrock_old_java
            w('Of these, %d are pixel-identical to an older Java texture and were attached to that texture\'s canonical '
              '(note says which version):\n\n' % len([b for b in jb.bedrock_unpaired if b in old]))
            w(_md_list('`%s` = Java `%s` (%s)' % (b, old[b][1], old[b][0]) for b in jb.bedrock_unpaired if b in old))
            nb = getattr(self, 'bedrock_nonblock_java', {})
            w('\n%d are pixel twins of Java textures outside `textures/block/` (canonical keeps the Java folder):\n\n'
              % len([b for b in jb.bedrock_unpaired if b in nb]))
            w(_md_list('`%s` = Java `%s`' % (b, ', '.join(nb[b])) for b in jb.bedrock_unpaired if b in nb))
            rest = [b for b in jb.bedrock_unpaired if b not in old and b not in nb]
            w('\nThe remaining %d are Bedrock-only (`bedrock:` canonical):\n\n' % len(rest))
            w(_code_list(rest))
            w('\nPairs found only by comparing leftover images (no model link; `uncertain`):\n\n')
            w(_md_list('`%s` <-> `%s`: %s' % (j, b, '; '.join(x for x in info['notes'] if x))
                       for (j, b), info in sorted(jb.pairs.items()) if info['kind'] == 'discovery'))

        # ---------------------------------------------------------- tint / size
        w('\n## Tint, size and animation differences\n\n')
        keys = ('differs only by tint', 'partial tint')
        tint = [r for r in rows if r['platform'] == 'bedrock' and any(k in r['note'] for k in keys)]
        w('Pairs whose images differ only by tint, or by a tint on part of the image (alpha masks compared first, then '
          'one factor per channel fitted over pixels visible in both, max error 4 levels; luminance correlation is '
          'reported for the rest). "Java grey (tinted in game)" means Java stores a grey texture and colours it by '
          'biome or block; Bedrock ships it pre-coloured:\n\n')
        w(_md_list('`%s` <-> `%s` (%s): %s' % (r['canonical'], r['name'], r['evidence'],
                                                '; '.join(p for p in r['note'].split('; ') if any(k in p for k in keys)))
                   for r in tint))
        size = [r for r in rows if r['platform'] == 'bedrock' and 'size differs' in r['note']]
        w('\nSize differences (%d):\n\n' % len(size))
        w(_md_list('`%s` <-> `%s` (%s): %s' % (r['canonical'], r['name'], r['evidence'], _pick(r['note'], 'size differs')) for r in size))
        frames = [r for r in rows if r['platform'] == 'bedrock' and 'pixels: ' in r['note']]
        w('\nAnimated textures matched by frames rather than whole strips:\n\n')
        w(_md_list('`%s` <-> `%s`: %s' % (r['canonical'], r['name'], _pick(r['note'], 'pixels: ')) for r in frames))

        # ---------------------------------------------------------- atlas
        w('\n## Pre-1.5 atlas (`terrain.png`)\n\n')
        a = self.atlas
        if not a:
            w('No atlas jar installed.\n')
        else:
            for v, wd, ht in a['jars']:
                w('%s `terrain.png` is %dx%d (16x16 tiles of %dx%d). Matched by pixels against 1.8.9 `blocks/` textures '
                  '(whole image or any animation frame).\n\n' % (v, wd, ht, wd // 16, ht // 16))
            w('Identical tiles: %d (`pixels` rows). Unmatched tiles: %d, of which %d have one clear partial match '
              '(at least 80%% of visible pixels identical, next best at least 10 points lower; `uncertain` rows) and %d '
              'hold only one or two colours (unused or placeholder slots). Fully transparent tiles: %d.\n\n'
              % (len(a['matched']), len(a['unmatched']), len(a['partial']), len(a['uniform']), len(a['empty'])))
            w('Identical (index = y*16+x):\n\n')
            w(_md_list('%d (x=%d, y=%d) = %s' % (i, i % 16, i // 16, ', '.join('`%s`' % n for n in names))
                       for _v, i, names, _h in a['matched']))
            w('\nPartial matches written as `uncertain` rows:\n\n')
            w(_md_list('%d (x=%d, y=%d) ~ `%s` (%.0f%% of visible pixels identical)' % (i, i % 16, i // 16, n, 100 * s)
                       for _v, i, n, s in a['partial']))
            part = set(i for _v, i, _n, _s in a['partial'])
            uni = set(i for _v, i, _c in a['uniform'])
            w('\nUnmatched, no row (best overlap shown for orientation only):\n\n')
            w(_md_list('%d (x=%d, y=%d): %s' % (i, i % 16, i // 16, ', '.join('`%s` %.0f%%' % (n, 100 * s) for s, n in best) or '-')
                       for _v, i, best, _c in a['unmatched'] if i not in part and i not in uni))
            w('\nOne- or two-colour tiles (no texture): %s\n' % (', '.join(str(i) for i in sorted(uni)) or '(none)'))
            w('\nFully transparent tiles: %s\n' % (', '.join(str(i) for _v, i in a['empty']) or '(none)'))

        # ---------------------------------------------------------- items
        if getattr(self, 'item_steps', None):
            w('\n## Items (Part 4, pixel identity only)\n\n')
            w('Item textures (`textures/item(s)/`, Bedrock `textures/items/`) were paired by pixel identity only: no item '
              'model pairing was done. History links across 1.12.2 -> 1.16.4 without identical pixels come from the Bedrock '
              'file of the same name or, last, from the same file name; both are `uncertain`. Canonical: `item/<%s name>`.\n\n'
              % self.newest)
            w('Java item textures per installed release: %s. Bedrock `textures/items/` files (layered): %d.\n\n'
              % (', '.join('%s %d' % (v, n) for v, n in self.item_counts.items()), self.item_bedrock_count))
            w('| step | renames | merges | splits | removed | added |\n|---|---|---|---|---|---|\n')
            for st in self.item_steps:
                kinds = collections.Counter(l.kind for l in st.links)
                w('| %s -> %s | %d | %d | %d | %d | %d |\n' % (st.a_ver, st.b_ver, kinds['rename'], kinds['merge'],
                                                               kinds['split'], len(st.gone_unmatched), len(st.new_unmatched)))
            ev = collections.Counter(l.evidence for st in self.item_steps for l in st.links if l.kind != 'same')
            w('\nItem history links by evidence: %s.\n\n' % ', '.join('%s %d' % kv for kv in sorted(ev.items())))
            for st in self.item_steps:
                if st.gone_unmatched:
                    w('- %s -> %s, item textures with no successor: %s\n' % (st.a_ver, st.b_ver, ', '.join('`%s`' % x for x in st.gone_unmatched)))
            ijb = self.item_jb
            items_c = set(r['canonical'] for r in rows if r['platform'] == 'java' and r['canonical'].startswith('item/') and r['to'] == '')
            with_b = set(r['canonical'] for r in rows if r['platform'] == 'bedrock' and r['canonical'].startswith('item/'))
            w('\nJava %s item textures: %d; with a Bedrock pixel twin: %d; without: %d.\n\n'
              % (self.newest, len(items_c), len(items_c & with_b), len(items_c - with_b)))
            w('Java item textures without a Bedrock pixel twin:\n\n')
            w(_code_list(sorted(items_c - with_b)))
            ib_only = sorted(r['name'] for r in rows if r['platform'] == 'bedrock' and r['canonical'].startswith('bedrock:items/'))
            w('\nBedrock item files without a Java pixel twin (%d):\n\n' % len(ib_only))
            w(_code_list(ib_only))

        # ---------------------------------------------------------- verification
        w('\n## Verification and limits\n\n')
        w('- PNG decoder: zlib + all five filters, colour types 0/2/3/4/6, bit depths 1-16, tRNS, Adam7. `selftest.py` '
          'checks it on synthetic images from an independent encoder (every type/depth/filter, tRNS, Adam7 including '
          'images too small for some passes) and on PNGs written by the Windows WIC encoder (`tools/png-oracle/make-pngs.ps1`).\n')
        w('- TGA decoder: types 2/10 (and 1/3/9/11), 24/32-bit, bottom-left and top-left origin, right-to-left. '
          'Every Bedrock TGA here is type 2 or 10, bottom-left origin; hundreds of them are pixel-identical to the Java '
          'PNG of the same texture, which confirms the orientation handling.\n')
        w('- Decode errors: %d.%s\n' % (len(self.decode_errors), ''.join('\n  - %s `%s`: %s' % e for e in self.decode_errors[:30])))
        w('- Pixel identity compares RGBA8 after zeroing the colour of fully transparent pixels; single-colour images '
          '(e.g. fully transparent ones, many blank PBR maps) never count as evidence.\n')
        w('- Model evidence for Java <-> Bedrock uses only representative Java states (default orientation from the app\'s '
          '`java_versions.tsv`, section %s) and skips Bedrock faces whose terrain key is an array of different files, '
          'because the array index is chosen in game code.\n' % self.meta.get('java_defaults_section'))
        w('- Bedrock layering: a later `vanilla_x` layer replaces a file of the same path (either extension); '
          '`blocks.json` entries merge field by field (later layers often only change `sound`); terrain keys are replaced '
          'per key. The base `vanilla` pack has no version, so its files have an empty `from`.\n')
        for n in self.notes:
            w('- %s\n' % n)
        return ''.join(o)


def _scope(r):
    c = r['canonical']
    if c.startswith('item/') or c.startswith('java:item'):
        return 'item'
    if c.startswith('bedrock:') and r['name'].startswith('items/'):
        return 'item'
    return 'block'


def _pick(note, word):
    for part in note.split('; '):
        if word in part:
            return part
    return note
