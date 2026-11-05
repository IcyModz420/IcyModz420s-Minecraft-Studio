// MinecraftStudio.Core/LegacyConsole/XMemLzx.Encoder.cs: encoder for XMemLzx.
// Part of XMemLzx (main part in XMemLzx.cs).
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

public static partial class XMemLzx
{
    // ------------------------------------------------------------------ encoder

    sealed class BitWriter
    {
        readonly ByteWriter _out = new(4096);
        ulong _acc;
        int _n;

        public void Write(int value, int bits)
        {
            while (bits > 16)
            {
                bits -= 16;
                Write((value >> bits) & 0xFFFF, 16);
            }
            if (bits == 0) return;
            _acc = (_acc << bits) | ((ulong)value & ((1UL << bits) - 1));
            _n += bits;
            while (_n >= 16)
            {
                _n -= 16;
                _out.U16LE((ushort)((_acc >> _n) & 0xFFFF));
            }
            _acc &= _n == 0 ? 0 : (1UL << _n) - 1;
        }

        public void Flush16() { if (_n != 0) Write(0, 16 - _n); }
        public byte[] ToArray() => _out.ToArray();
    }

    /// <summary>Length-limited Huffman code lengths; always at least two used symbols and a complete code.</summary>
    static int[] CodeLengths(int[] freqsIn, int maxBits)
    {
        var freqs = (int[])freqsIn.Clone();
        var syms = new List<int>();
        for (int i = 0; i < freqs.Length; i++) if (freqs[i] > 0) syms.Add(i);
        for (int i = 0; syms.Count < 2 && i < freqs.Length; i++)
            if (freqs[i] == 0) { freqs[i] = 1; syms.Add(i); }
        var lengths = new int[freqs.Length];
        var pq = new PriorityQueue<(List<int> Members, long Uid), (long Freq, long Uid)>();
        long uid = 0;
        foreach (int s in syms) { pq.Enqueue(([s], uid), (freqs[s], uid)); uid++; }
        while (pq.Count > 1)
        {
            pq.TryDequeue(out var a, out var pa);
            pq.TryDequeue(out var b, out var pb);
            foreach (int s in a.Members) lengths[s]++;
            foreach (int s in b.Members) lengths[s]++;
            a.Members.AddRange(b.Members);
            pq.Enqueue((a.Members, uid), (pa.Freq + pb.Freq, uid));
            uid++;
        }
        if (syms.Max(s => lengths[s]) > maxBits)
        {
            foreach (int s in syms) lengths[s] = Math.Min(lengths[s], maxBits);
            long kraft = syms.Sum(s => 1L << (maxBits - lengths[s]));
            long cap = 1L << maxBits;
            var cheap = syms.OrderBy(s => freqs[s]).ToList();
            while (kraft > cap)
            {
                foreach (int s in cheap)
                {
                    if (lengths[s] < maxBits)
                    {
                        kraft -= 1L << (maxBits - lengths[s] - 1);
                        lengths[s]++;
                        if (kraft <= cap) break;
                    }
                }
            }
            var dear = syms.OrderByDescending(s => freqs[s]).ToList();
            bool changed = true;
            while (kraft < cap && changed)
            {
                changed = false;
                foreach (int s in dear)
                {
                    if (lengths[s] > 1 && kraft + (1L << (maxBits - lengths[s])) <= cap)
                    {
                        kraft += 1L << (maxBits - lengths[s]);
                        lengths[s]--;
                        changed = true;
                        if (kraft == cap) break;
                    }
                }
            }
        }
        return lengths;
    }

    static int[] CanonicalCodes(int[] lengths)
    {
        var codes = new int[lengths.Length];
        var count = new int[18];
        foreach (int l in lengths) if (l != 0) count[l]++;
        var next = new int[18];
        int c = 0;
        for (int b = 1; b <= 16; b++)
        {
            next[b] = c;
            c = (c + count[b]) << 1;
        }
        for (int s = 0; s < lengths.Length; s++) if (lengths[s] != 0) codes[s] = next[lengths[s]]++;
        return codes;
    }

    static void WriteLengths(BitWriter bw, int[] lens, int[] prev, int first, int last)
    {
        var toks = new List<(int Sym, int Extra, int Bits)>();
        int i = first;
        while (i < last)
        {
            if (lens[i] == 0)
            {
                int run = 1;
                while (i + run < last && lens[i + run] == 0 && run < 51) run++;
                if (run >= 20) { toks.Add((18, run - 20, 5)); i += run; continue; }
                if (run >= 4) { toks.Add((17, run - 4, 4)); i += run; continue; }
            }
            toks.Add((((prev[i] - lens[i]) % 17 + 17) % 17, 0, 0));
            i++;
        }
        var freqs = new int[20];
        foreach (var t in toks) freqs[t.Sym]++;
        var plen = CodeLengths(freqs, 15);
        var pcodes = CanonicalCodes(plen);
        foreach (int l in plen) bw.Write(l, 4);
        foreach (var (sym, extra, bits) in toks)
        {
            bw.Write(pcodes[sym], plen[sym]);
            if (bits != 0) bw.Write(extra, bits);
        }
    }

    static int SlotOf(int formatted)
    {
        int s = 0;
        while (s + 1 < PositionSlots && PositionBase[s + 1] <= formatted) s++;
        return s;
    }

    /// <summary>Compresses into XMemCompress framing (one verbatim block per 32 KiB frame).</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        const int HashBits = 16;
        const int MaxChain = 24;
        var head = new int[1 << HashBits];
        Array.Fill(head, -1);
        var prev = new int[WindowSize];
        var prevMain = new int[MainElements];
        var prevLen = new int[SecondaryLengths];
        var output = new ByteWriter(data.Length / 2 + 64);
        bool first = true;
        int pos = 0, n = data.Length;
        var toks = new List<(int A, int Off)>(FrameSize);

        static int Hash(ReadOnlySpan<byte> d, int p) => (int)(((uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16)) * 2654435761u) >> (32 - HashBits));

        void Insert(ReadOnlySpan<byte> d, int q)
        {
            if (q + 3 > d.Length) return;
            int h = Hash(d, q);
            prev[q & WindowMask] = head[h];
            head[h] = q;
        }

        while (pos < n)
        {
            int fend = Math.Min(pos + FrameSize, n);
            toks.Clear();
            int p = pos;
            while (p < fend)
            {
                int bestLen = 0, bestOff = 0;
                if (p + 3 <= fend)
                {
                    int cand = head[Hash(data, p)];
                    int depth = 0;
                    int maxl = Math.Min(257, fend - p);
                    while (cand >= 0 && depth < MaxChain && p - cand <= WindowSize - 3 && cand < p)
                    {
                        int l = 0;
                        while (l < maxl && data[cand + l] == data[p + l]) l++;
                        if (l > bestLen)
                        {
                            bestLen = l;
                            bestOff = p - cand;
                            if (l == maxl) break;
                        }
                        int nextCand = prev[cand & WindowMask];
                        if (nextCand >= cand) break;
                        cand = nextCand;
                        depth++;
                    }
                }
                int step = bestLen >= 3 ? bestLen : 1;
                for (int q = p; q < p + step; q++) Insert(data, q);
                if (bestLen >= 3) toks.Add((bestLen, bestOff));
                else { toks.Add((data[p], 0)); bestLen = 1; }
                p += bestLen;
            }

            var mf = new int[MainElements];
            var lf = new int[SecondaryLengths];
            var enc = new List<(int Sym, int Ls, int Slot, int Extra)>(toks.Count);
            foreach (var (a, off) in toks)
            {
                if (off == 0) { mf[a]++; enc.Add((a, -1, -1, 0)); continue; }
                int formatted = off + 2;
                int slot = SlotOf(formatted);
                int lh = a - 2;
                int sym = 256 + slot * 8 + Math.Min(lh, 7);
                mf[sym]++;
                int ls = lh >= 7 ? lh - 7 : -1;
                if (ls >= 0) lf[ls]++;
                enc.Add((sym, ls, slot, formatted - PositionBase[slot]));
            }
            var mlen = CodeLengths(mf, 16);
            var llen = CodeLengths(lf, 16);
            var mcode = CanonicalCodes(mlen);
            var lcode = CanonicalCodes(llen);

            var bw = new BitWriter();
            if (first) { bw.Write(0, 1); first = false; }
            int size = fend - pos;
            bw.Write(1, 3);
            bw.Write(size >> 8, 16);
            bw.Write(size & 0xFF, 8);
            WriteLengths(bw, mlen, prevMain, 0, 256);
            WriteLengths(bw, mlen, prevMain, 256, MainElements);
            WriteLengths(bw, llen, prevLen, 0, SecondaryLengths);
            prevMain = mlen;
            prevLen = llen;
            foreach (var (sym, ls, slot, extra) in enc)
            {
                bw.Write(mcode[sym], mlen[sym]);
                if (slot < 0) continue;
                if (ls >= 0) bw.Write(lcode[ls], llen[ls]);
                int eb = ExtraBits[slot];
                if (eb != 0) bw.Write(extra, eb);
            }
            bw.Flush16();
            byte[] comp = bw.ToArray();
            if (size == FrameSize) output.U16BE((ushort)comp.Length);
            else
            {
                output.U8(0xFF);
                output.U16BE((ushort)size);
                output.U16BE((ushort)comp.Length);
            }
            output.Bytes(comp);
            pos = fend;
        }
        output.Zeros(5);
        return output.ToArray();
    }
}
