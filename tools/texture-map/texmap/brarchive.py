"""Reader for Bedrock Edition .brarchive files.

Layout: 8-byte magic 7d 27 25 b1 a0 52 70 26, u32 LE entry count, u32 LE version (1),
then `count` 256-byte entries: byte 0 = name length n (<= 247), bytes 1..n = UTF-8 name,
bytes 248-251 = u32 LE data offset, bytes 252-255 = u32 LE size. File data starts at
16 + count*256, plus the entry's offset.

The archive `__brarchive/<folder>.brarchive` holds the files directly in `<folder>/`.
"""
import os
import struct

MAGIC = bytes.fromhex('7d2725b1a0527026')
ENTRY = 256


class BrArchiveError(ValueError):
    pass


def read_index(data):
    """Return list of (name, offset_abs, size)."""
    if data[:8] != MAGIC:
        raise BrArchiveError('bad magic')
    count, version = struct.unpack('<II', data[8:16])
    if version != 1:
        raise BrArchiveError('unexpected archive version %d' % version)
    base = 16 + count * ENTRY
    if base > len(data):
        raise BrArchiveError('truncated index')
    out = []
    for i in range(count):
        e = data[16 + i * ENTRY:16 + (i + 1) * ENTRY]
        n = e[0]
        if n > 247:
            raise BrArchiveError('name too long in entry %d' % i)
        name = e[1:1 + n].decode('utf-8')
        off, size = struct.unpack('<II', e[248:256])
        if base + off + size > len(data):
            raise BrArchiveError('entry %r runs past end of archive' % name)
        out.append((name, base + off, size))
    return out


def read_archive(path):
    """Yield (name, bytes) for every file in the archive."""
    with open(path, 'rb') as f:
        data = f.read()
    for name, off, size in read_index(data):
        yield name, data[off:off + size]


def walk_pack(pack_dir):
    """Map 'relative/path/file.ext' -> loader for every file in a Bedrock pack.

    Loose files and archived files are both returned. Values are tuples
    (kind, source) where kind is 'loose' or 'archive'; use load() to read bytes.
    If the same path exists loose and archived, the archived copy is recorded
    under the same key with kind 'archive' and the loose copy under kind 'loose';
    both are returned in a list (collisions are reported by callers).
    """
    files = {}
    pack_dir = os.path.abspath(pack_dir)
    arch_root = os.path.join(pack_dir, '__brarchive')
    for root, dirs, names in os.walk(pack_dir):
        if os.path.abspath(root).startswith(arch_root):
            continue
        for nm in names:
            full = os.path.join(root, nm)
            rel = os.path.relpath(full, pack_dir).replace(os.sep, '/')
            files.setdefault(rel, []).append(('loose', full, None, None))
    if os.path.isdir(arch_root):
        for root, dirs, names in os.walk(arch_root):
            for nm in names:
                if not nm.endswith('.brarchive'):
                    continue
                full = os.path.join(root, nm)
                folder = os.path.relpath(full, arch_root)[:-len('.brarchive')].replace(os.sep, '/')
                with open(full, 'rb') as f:
                    data = f.read()
                for name, off, size in read_index(data):
                    rel = (folder + '/' + name) if folder else name
                    files.setdefault(rel, []).append(('archive', full, off, size))
    return files


_ARCH_CACHE = {}


def load(entry):
    kind, path, off, size = entry
    if kind == 'loose':
        with open(path, 'rb') as f:
            return f.read()
    data = _ARCH_CACHE.get(path)
    if data is None:
        with open(path, 'rb') as f:
            data = f.read()
        if len(_ARCH_CACHE) > 8:
            _ARCH_CACHE.clear()
        _ARCH_CACHE[path] = data
    return data[off:off + size]
