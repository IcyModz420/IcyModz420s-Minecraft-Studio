"""Readers for the app's own block mapping tables (MinecraftStudio.Core/Data)."""
import csv
import gzip
import io
import os
import re


def parse_state(s):
    """'minecraft:oak_log[axis=y]' -> ('oak_log', {'axis': 'y'})."""
    s = s.strip()
    m = re.match(r'^(?:([a-z0-9_.-]+):)?([^\[]+)(?:\[(.*)\])?$', s)
    if not m:
        return s, {}
    name = m.group(2)
    props = {}
    if m.group(3):
        for part in m.group(3).split(','):
            if '=' in part:
                k, v = part.split('=', 1)
                props[k.strip()] = v.strip()
    return name, props


def read_pairs_tsv_gz(path):
    """[(left_state_str, right_state_str)] for j2b/b2j (no header)."""
    out = []
    with gzip.open(path, 'rt', encoding='utf-8') as f:
        for line in f:
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            parts = line.split('\t')
            if len(parts) >= 2:
                out.append((parts[0], parts[1]))
    return out


def expand_data_values(spec):
    """'0 8' / '0-5 8-13' / '0' -> sorted list of ints."""
    vals = set()
    for tok in spec.split():
        if '-' in tok:
            a, b = tok.split('-', 1)
            vals.update(range(int(a), int(b) + 1))
        elif tok.strip():
            vals.add(int(tok))
    return sorted(vals)


def read_flattening(path):
    """[{legacy_id, name12, data:[...], block13, props13:{}, note}]"""
    rows = []
    with open(path, 'r', encoding='utf-8-sig', newline='') as f:
        for r in csv.DictReader(f):
            props = {}
            for part in (r.get('first_state_properties') or '').split(','):
                if '=' in part:
                    k, v = part.split('=', 1)
                    props[k.strip()] = v.strip()
            rows.append({
                'legacy_id': int(r['legacy_id']),
                'name12': r['java_1_12_name'].strip(),
                'data': expand_data_values(r['data_values']),
                'data_spec': r['data_values'],
                'block13': parse_state(r['java_1_13_block'])[0],
                'props13': props,
                'note': (r.get('conversion_note') or '').strip(),
            })
    return rows


def read_java_versions(path):
    """{version: set(block names)} from java_versions.tsv.gz ('B' rows)."""
    out = {}
    cur = None
    with gzip.open(path, 'rt', encoding='utf-8') as f:
        for line in f:
            if line.startswith('#VERSION'):
                cur = line.split('\t')[1].strip()
                out[cur] = set()
            elif line.startswith('B\t') and cur is not None:
                out[cur].add(line.split('\t')[1])
    return out


def read_java_defaults(path, want_version=None):
    """{block: {prop: value}} default states from java_versions.tsv.gz.

    Uses the section named want_version, else the last (newest) section.
    Row layout: B, name, numeric id, default props, all props, ...
    """
    sections = {}
    order = []
    cur = None
    with gzip.open(path, 'rt', encoding='utf-8') as f:
        for line in f:
            if line.startswith('#VERSION'):
                cur = line.split('\t')[1].strip()
                sections[cur] = {}
                order.append(cur)
            elif line.startswith('B\t') and cur is not None:
                parts = line.rstrip('\n').split('\t')
                props = {}
                if len(parts) > 3 and parts[3]:
                    for kv in parts[3].split(','):
                        if '=' in kv:
                            k, v = kv.split('=', 1)
                            props[k] = v
                sections[cur][parts[1]] = props
    if not order:
        return {}, None
    v = want_version if want_version in sections else order[-1]
    return sections[v], v


def data_dir(repo_root):
    return os.path.join(repo_root, 'MinecraftStudio.Core', 'Data')
