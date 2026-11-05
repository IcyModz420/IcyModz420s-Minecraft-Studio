"""Turns the research table (out/texture_names.csv) into the one the app ships (MinecraftStudio.Core/Data/texture_names.csv).

Run with:  python -I tools/texture-map/ship.py   (from the repo folder, after build.py)

What it changes, and why:

* Version gaps. Names are only proven at the Java releases whose jars were installed. A range that starts or ends at
  an installed release next to releases without a jar is widened across those releases, so a pack made for such a
  release gets every name the texture may have had there. An extra file is ignored by the game; a missing one would
  silently show the default texture. Widening never crosses the 1.13 folder change: "blocks/" and "items/" names stop
  at 1.12.2, "block/" and "item/" names start at 1.13.
* Roles of Bedrock files. Bedrock keeps extra pictures for some blocks: "carried" ones (the inventory picture, often
  pre-coloured) and "opaque" ones (leaves for fast graphics). They get role=carried or role=opaque, and carried ones a
  tint where the research found the picture is exactly the Java texture times one colour. The app never reads these
  as a block's main texture, and writes them derived from it (tinted, or made solid), or not at all.
* Notes are left out (they are research detail, kept in out/report.md and the research table).
"""
import csv
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(HERE, 'out', 'texture_names.csv')
DST = os.path.join(REPO, 'MinecraftStudio.Core', 'Data', 'texture_names.csv')
DATAVERSIONS = os.path.join(REPO, 'MinecraftStudio.Core', 'Data', 'dataversions.csv')


def key(v):
    """Sort key of a release name: alpha < beta < releases, then the numbers."""
    era = 2
    if v[:1] in ('a', 'b') and v[1:2].isdigit():
        era = 0 if v[0] == 'a' else 1
        v = v[1:]
    parts = v.split('.')
    return [era] + [int(p) for p in parts] + [0] * (4 - len(parts))


def releases():
    """Every Java release name, oldest first: the app's data-version table plus the releases before it."""
    names = {'1.7.2', '1.7.4', '1.7.5', '1.7.6', '1.7.7', '1.7.8', '1.7.9', '1.7.10',
             '1.8', '1.8.1', '1.8.2', '1.8.3', '1.8.4', '1.8.5', '1.8.6', '1.8.7', '1.8.8', '1.8.9'}
    with open(DATAVERSIONS, encoding='utf-8') as f:
        for row in csv.DictReader(f):
            if row['type'] == 'release' and re.fullmatch(r'\d+(\.\d+)+', row['name']):
                names.add(row['name'])
    return sorted(names, key=key)


def main():
    rel = releases()
    with open(SRC, encoding='utf-8', newline='') as f:
        rows = list(csv.DictReader(f))
    # Installed releases: the versions the research saw files in (java rows proven by the files themselves).
    installed = sorted({v for r in rows if r['platform'] == 'java' and r['evidence'] != 'manual'
                        for v in (r['from'], r['to']) if v and re.fullmatch(r'\d+(\.\d+)+', v) and v != '1.7.2'}, key=key)
    index = {v: i for i, v in enumerate(rel)}

    def prev_installed(v):
        older = [x for x in installed if key(x) < key(v)]
        return older[-1] if older else None

    def next_installed(v):
        newer = [x for x in installed if key(x) > key(v)]
        return newer[0] if newer else None

    widened = 0
    out = []
    for r in rows:
        r = dict(r)
        if r['platform'] == 'java' and r['evidence'] != 'manual':
            flat = r['name'].split('/')[0] in ('block', 'item')
            low, high = ('1.13', None) if flat else (None, '1.12.2')
            if r['from'] in index and r['from'] in installed:
                p = prev_installed(r['from'])
                if p is not None and index.get(p) is not None:
                    first_after = rel[index[p] + 1]
                    if low is not None and key(first_after) < key(low):
                        first_after = low
                    if key(first_after) < key(r['from']):
                        r['from'] = first_after
                        widened += 1
            if r['to'] in index and r['to'] in installed:
                n = next_installed(r['to'])
                if n is not None and index.get(n) is not None:
                    last_before = rel[index[n] - 1]
                    if high is not None and key(last_before) > key(high):
                        last_before = high
                    if key(last_before) > key(r['to']):
                        r['to'] = last_before
                        widened += 1
        role, tint = '', ''
        if r['platform'] == 'bedrock':
            note = r['note']
            if 'carried (inventory) texture' in note:
                role = 'carried'
                m = re.search(r'differs only by tint.*?x#([0-9a-fA-F]{6})', note)
                if m:
                    tint = '#' + m.group(1).lower()
            elif r['name'].endswith('_opaque') and not r['canonical'].startswith('bedrock:'):
                role = 'opaque'
        out.append({'canonical': r['canonical'], 'platform': r['platform'], 'name': r['name'], 'from': r['from'],
                    'to': r['to'], 'evidence': r['evidence'], 'role': role, 'tint': tint})

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    with open(DST, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# Generated by tools/texture-map/ship.py from tools/texture-map/out/texture_names.csv; see tools/texture-map/README.md.\n')
        w = csv.DictWriter(f, fieldnames=['canonical', 'platform', 'name', 'from', 'to', 'evidence', 'role', 'tint'], lineterminator='\n')
        w.writeheader()
        w.writerows(out)
    roles = {}
    for o in out:
        if o['role']:
            roles[o['role']] = roles.get(o['role'], 0) + 1
    print(f'{len(out)} rows written to {DST}')
    print(f'installed releases seen: {", ".join(installed)}')
    print(f'{widened} range ends widened across releases without a jar')
    print('roles:', roles, '; tinted carried:', sum(1 for o in out if o['tint']))


if __name__ == '__main__':
    sys.exit(main())
