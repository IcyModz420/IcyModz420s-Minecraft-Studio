"""Assemble per-texture Java name histories ("name eras") from step links."""
import collections


def basename(name):
    return name.rsplit('/', 1)[-1]


class Chains(object):
    """versions: installed release names oldest first; steps[i]: StepResult for versions[i] -> versions[i+1]."""

    def __init__(self, versions, names_by_version, steps):
        self.versions = versions
        self.k = len(versions) - 1
        self.names = names_by_version  # list of sets
        self.succ = [collections.defaultdict(list) for _ in versions]
        self.pred = [collections.defaultdict(list) for _ in versions]
        for i, st in enumerate(steps):
            for l in st.links:
                self.succ[i][l.a].append((l.b, l))
                self.pred[i + 1][l.b].append((l.a, l))

    def canonicals(self):
        """{canonical: {(i, name)}} including 'java:<last name>' for removed textures."""
        out = {}
        covered = set()
        for n in sorted(self.names[self.k]):
            nodes = self._walk_back(self.k, n)
            out[basename(n)] = nodes
            covered |= nodes
        # removed textures: start from their last version
        for i in range(self.k - 1, -1, -1):
            for n in sorted(self.names[i]):
                if (i, n) in covered:
                    continue
                if self.succ[i].get(n):
                    continue  # it continues/links forward into something covered later
                nodes = self._walk_back(i, n)
                key = 'java:' + n
                out[key] = nodes
                covered |= nodes
        # any node still uncovered (links into a removed texture's chain) is covered by the walks above
        return out

    def _walk_back(self, i, n):
        nodes = {(i, n)}
        frontier = [(i, n)]
        while frontier:
            j, m = frontier.pop()
            if j == 0:
                continue
            for a, _l in self.pred[j].get(m, []):
                node = (j - 1, a)
                if node not in nodes:
                    nodes.add(node)
                    frontier.append(node)
        return nodes

    def eras(self, nodes, newest_open=True):
        """[(name, first_index, last_index)] contiguous runs, newest first."""
        by_name = collections.defaultdict(list)
        for i, n in nodes:
            by_name[n].append(i)
        runs = []
        for n, idx in by_name.items():
            idx.sort()
            start = prev = idx[0]
            for x in idx[1:]:
                if x != prev + 1:
                    runs.append((n, start, prev))
                    start = x
                prev = x
            runs.append((n, start, prev))
        runs.sort(key=lambda r: (-r[2], -r[1], r[0]))
        return runs

    def outgoing(self, i, n, nodes):
        """Links from (i, n) into the chain's nodes at i+1 (excluding same-name)."""
        return [(b, l) for b, l in self.succ[i].get(n, []) if (i + 1, b) in nodes and l.kind != 'same']

    def incoming(self, i, n, nodes):
        return [(a, l) for a, l in self.pred[i].get(n, []) if (i - 1, a) in nodes and l.kind != 'same']

    def name_at(self, nodes, i):
        return sorted(n for j, n in nodes if j == i)
