// MinecraftStudio.Core/LegacyConsole/XMemLzx.Decoder.cs: decoder for XMemLzx.
// Part of XMemLzx (main part in XMemLzx.cs).
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

public static partial class XMemLzx
{
    // ------------------------------------------------------------------ decoder

    sealed class BitReader(byte[] data, int pos, int end)
    {
        public int Pos = pos;
        ulong _buf;
        int _bits;

        void Fill()
        {
            while (_bits <= 16)
            {
                uint w = Pos + 1 < end ? (uint)(data[Pos] | (data[Pos + 1] << 8)) : 0u;
                Pos += 2;
                _buf = (_buf << 16) | w;
                _bits += 16;
            }
        }

        public int Peek(int n)
        {
            if (_bits < n) Fill();
            return (int)((_buf >> (_bits - n)) & ((1UL << n) - 1));
        }

        public void Drop(int n)
        {
            _bits -= n;
            _buf &= (1UL << _bits) - 1;
        }

        public int Read(int n)
        {
            if (n == 0) return 0;
            if (n > 16)
            {
                int hi = Read(16);
                return (hi << (n - 16)) | Read(n - 16);
            }
            int v = Peek(n);
            Drop(n);
            return v;
        }

        public int Bits => _bits;
        public void Reset() { _buf = 0; _bits = 0; }
        public void FillPublic() => Fill();
    }

    sealed class Huffman
    {
        readonly int[] _table;
        readonly int _tableBits;
        public readonly bool Empty;

        public Huffman(int[] lengths)
        {
            int maxBits = 1;
            foreach (int l in lengths) if (l > maxBits) maxBits = l;
            if (maxBits > 16) throw new SaveFormatException("LZX: code length over 16.");
            _tableBits = maxBits;
            int size = 1 << maxBits;
            _table = new int[size];
            var count = new int[18];
            foreach (int l in lengths) if (l != 0) count[l]++;
            var next = new int[18];
            int c = 0;
            for (int b = 1; b <= 16; b++)
            {
                next[b] = c;
                c = (c + count[b]) << 1;
            }
            long filled = 0;
            for (int sym = 0; sym < lengths.Length; sym++)
            {
                int l = lengths[sym];
                if (l == 0) continue;
                int code = next[l]++;
                int start = code << (maxBits - l);
                int n = 1 << (maxBits - l);
                if (start + n > size) throw new SaveFormatException("LZX: over-subscribed Huffman table.");
                for (int k = start; k < start + n; k++) _table[k] = (sym << 5) | l;
                filled += n;
            }
            Empty = filled == 0;
            if (filled != size && filled != 0) throw new SaveFormatException("LZX: incomplete Huffman table.");
        }

        public int Decode(BitReader br)
        {
            if (Empty) throw new SaveFormatException("LZX: decode from an empty tree.");
            int e = _table[br.Peek(_tableBits)];
            br.Drop(e & 31);
            return e >> 5;
        }
    }

    sealed class Decoder
    {
        readonly byte[] _window = new byte[WindowSize];
        int _posn;
        readonly int[] _r = [1, 1, 1];
        readonly int[] _mainLengths = new int[MainElements];
        readonly int[] _lengthLengths = new int[SecondaryLengths];
        bool _headerRead;
        int _blockType, _blockRemaining, _blockLength;
        Huffman? _main, _length, _aligned;

        void ReadLengths(BitReader br, int[] lens, int first, int last)
        {
            var pre = new int[20];
            for (int i = 0; i < 20; i++) pre[i] = br.Read(4);
            var tree = new Huffman(pre);
            int x = first;
            while (x < last)
            {
                int z = tree.Decode(br);
                if (z == 17)
                {
                    int y = br.Read(4) + 4;
                    for (int k = 0; k < y && x < lens.Length; k++) lens[x++] = 0;
                }
                else if (z == 18)
                {
                    int y = br.Read(5) + 20;
                    for (int k = 0; k < y && x < lens.Length; k++) lens[x++] = 0;
                }
                else if (z == 19)
                {
                    int y = br.Read(1) + 4;
                    z = tree.Decode(br);
                    int v = lens[x] - z;
                    if (v < 0) v += 17;
                    for (int k = 0; k < y && x < lens.Length; k++) lens[x++] = v;
                }
                else
                {
                    int v = lens[x] - z;
                    if (v < 0) v += 17;
                    lens[x++] = v;
                }
            }
            if (x != last) throw new SaveFormatException("LZX: code length run overflow.");
        }

        public void DecodeFrame(byte[] data, int start, int end, int outLen, ByteWriter output)
        {
            var br = new BitReader(data, start, end);
            if (!_headerRead)
            {
                if (br.Read(1) != 0) { br.Read(16); br.Read(16); }
                _headerRead = true;
            }
            int outStart = _posn;
            int togo = outLen;
            while (togo > 0)
            {
                if (_blockRemaining == 0)
                {
                    if (_blockType == 3)
                    {
                        if ((_blockLength & 1) != 0) br.Pos++;
                        br.Reset();
                    }
                    _blockType = br.Read(3);
                    int hi = br.Read(16), lo = br.Read(8);
                    _blockRemaining = _blockLength = (hi << 8) | lo;
                    if (_blockType == 2)
                    {
                        var al = new int[8];
                        for (int i = 0; i < 8; i++) al[i] = br.Read(3);
                        _aligned = new Huffman(al);
                    }
                    if (_blockType is 1 or 2)
                    {
                        ReadLengths(br, _mainLengths, 0, 256);
                        ReadLengths(br, _mainLengths, 256, MainElements);
                        _main = new Huffman(_mainLengths);
                        ReadLengths(br, _lengthLengths, 0, SecondaryLengths);
                        _length = _lengthLengths.Any(l => l != 0) ? new Huffman(_lengthLengths) : null;
                    }
                    else if (_blockType == 3)
                    {
                        int k = br.Bits % 16;
                        if (k == 0) k = 16;
                        if (br.Bits < k) br.FillPublic();
                        br.Drop(k);
                        br.Pos -= br.Bits / 8;
                        br.Reset();
                        int p = br.Pos;
                        for (int i = 0; i < 3; i++) _r[i] = BitConverter.ToInt32(data, p + 4 * i);
                        br.Pos = p + 12;
                    }
                    else throw new SaveFormatException($"LZX: bad block type {_blockType}.");
                }
                int run = Math.Min(_blockRemaining, togo);
                togo -= run;
                _blockRemaining -= run;
                if (_blockType == 3)
                {
                    int p = br.Pos;
                    for (int k = 0; k < run; k++)
                    {
                        _window[_posn] = data[p + k];
                        _posn = (_posn + 1) & WindowMask;
                    }
                    br.Pos = p + run;
                    continue;
                }
                var main = _main!;
                bool aligned = _blockType == 2;
                while (run > 0)
                {
                    int sym = main.Decode(br);
                    if (sym < 256)
                    {
                        _window[_posn] = (byte)sym;
                        _posn = (_posn + 1) & WindowMask;
                        run--;
                        continue;
                    }
                    sym -= 256;
                    int mlen = sym & 7;
                    if (mlen == 7)
                    {
                        if (_length == null) throw new SaveFormatException("LZX: length tree is empty.");
                        mlen += _length.Decode(br);
                    }
                    mlen += 2;
                    int slot = sym >> 3;
                    int off;
                    if (slot > 2)
                    {
                        int extra = ExtraBits[slot];
                        off = PositionBase[slot] - 2;
                        if (aligned && extra >= 3)
                        {
                            if (extra > 3) off += br.Read(extra - 3) << 3;
                            off += _aligned!.Decode(br);
                        }
                        else if (extra != 0) off += br.Read(extra);
                        else off = 1;
                        _r[2] = _r[1]; _r[1] = _r[0]; _r[0] = off;
                    }
                    else if (slot == 0) off = _r[0];
                    else if (slot == 1) { off = _r[1]; _r[1] = _r[0]; _r[0] = off; }
                    else { off = _r[2]; _r[2] = _r[0]; _r[0] = off; }
                    int src = (_posn - off) & WindowMask;
                    for (int k = 0; k < mlen; k++)
                    {
                        _window[_posn] = _window[src];
                        _posn = (_posn + 1) & WindowMask;
                        src = (src + 1) & WindowMask;
                    }
                    run -= mlen;
                }
                if (run < 0)
                {
                    if (-run > _blockRemaining) throw new SaveFormatException("LZX: match overran the block.");
                    if (togo == 0) throw new SaveFormatException("LZX: match crossed the frame boundary.");
                    _blockRemaining += run;
                    togo += run;
                }
            }
            if (outStart + outLen <= WindowSize) output.Bytes(_window.AsSpan(outStart, outLen));
            else
            {
                int first = WindowSize - outStart;
                output.Bytes(_window.AsSpan(outStart, first));
                output.Bytes(_window.AsSpan(0, outLen - first));
            }
        }
    }

    /// <summary>Decompresses framed LZX data starting at <paramref name="offset"/>.</summary>
    public static byte[] Decompress(byte[] data, int offset, int expectedSize, out int endOffset)
    {
        var dec = new Decoder();
        var output = new ByteWriter(Math.Max(1024, expectedSize));
        int p = offset;
        while (p < data.Length && (expectedSize <= 0 || output.Length < expectedSize))
        {
            int frameSize, blockSize, hdr;
            if (data[p] == 0xFF)
            {
                if (p + 5 > data.Length) break;
                frameSize = (data[p + 1] << 8) | data[p + 2];
                blockSize = (data[p + 3] << 8) | data[p + 4];
                hdr = 5;
            }
            else
            {
                if (p + 2 > data.Length) break;
                frameSize = FrameSize;
                blockSize = (data[p] << 8) | data[p + 1];
                hdr = 2;
            }
            if (blockSize == 0 || frameSize == 0) break;
            if (p + hdr + blockSize > data.Length) throw new SaveFormatException("LZX: frame runs past the end of the data.");
            dec.DecodeFrame(data, p + hdr, p + hdr + blockSize, frameSize, output);
            p += hdr + blockSize;
        }
        endOffset = p;
        if (expectedSize > 0 && output.Length < expectedSize)
            throw new SaveFormatException($"LZX: decoded {output.Length} bytes, expected {expectedSize}.");
        return expectedSize > 0 && output.Length > expectedSize ? output.WrittenSpan[..expectedSize].ToArray() : output.ToArray();
    }
}
