"""When was a 1.8.9 texture introduced? (to date 1.7-era names)

Uses the app's own MinecraftStudio.Core/Data/legacy_blocks.csv: `introduced_java`
per legacy block id, plus variant notes in `data_values_1_12` such as
"(1-6 since 1.8)" or "1 coarse_dirt (1.8)". A texture's introduction is the earliest
introduction of any block variant whose 1.8.9 model uses it.
"""
import csv
import re

_PRE_RELEASE = ('classic', 'indev', 'infdev', 'alpha', 'beta')


def version_rank(s):
    """Sortable key for introduced_java strings; None when unknown."""
    if not s:
        return None
    t = s.strip().lower()
    for i, pre in enumerate(_PRE_RELEASE):
        if t.startswith(pre):
            nums = [int(x) for x in re.findall(r'\d+', t[len(pre):])][:3]
            return (0, i) + tuple(nums)
    nums = [int(x) for x in re.findall(r'\d+', t)]
    if not nums:
        return None
    while len(nums) < 3:
        nums.append(0)
    return (1, 0) + tuple(nums[:3])


def read_intro(path):
    """{(legacy_id, data): rank} plus {legacy_id: rank} under key (id, None)."""
    out = {}
    with open(path, 'r', encoding='utf-8-sig', newline='') as f:
        for r in csv.DictReader(f):
            try:
                bid = int(r['id'])
            except (KeyError, ValueError):
                continue
            base = version_rank(r.get('introduced_java', ''))
            out[(bid, None)] = (base, r.get('introduced_java', ''))
            text = r.get('data_values_1_12') or ''
            for a, b, v in re.findall(r'\((\d+)-(\d+) since ([^)]+)\)', text):
                for d in range(int(a), int(b) + 1):
                    out[(bid, d)] = (version_rank(v), v.strip())
            for d, v in re.findall(r'(?:^|;)\s*(\d+) [a-z_/ ]+\((\d[\d.]*)\)', text):
                out[(bid, int(d))] = (version_rank(v), v.strip())
    return out


def intro_of(intro, legacy_id, data_values):
    """Earliest (rank, label) over the given data values of one legacy id."""
    best = None
    for d in data_values or [None]:
        v = intro.get((legacy_id, d)) or intro.get((legacy_id, None))
        if v and v[0] is not None and (best is None or v[0] < best[0]):
            best = v
    return best


R_1_7_2 = version_rank('1.7.2')
