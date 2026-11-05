// MinecraftStudio.Core/Nbt/NbtFile.cs: An NBT file of any edition: compression, binary dialect, optional
// Bedrock 8-byte header and one or more root tags. writes it back in the same shape.
// Owns: NbtFile.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Nbt;

/// <summary>
/// An NBT file of any edition: compression, binary dialect, optional Bedrock 8-byte header
/// and one or more root tags. <see cref="ToBytes"/> writes it back in the same shape.
/// </summary>
public sealed class NbtFile
{
    public List<NamedTag> Roots { get; } = [];
    public NbtFormat Format { get; set; } = NbtFormat.Java;
    public CompressionKind Compression { get; set; }
    /// <summary>Bedrock level.dat header: storage version (null when the file has no header).</summary>
    public int? HeaderVersion { get; set; }
    /// <summary>Bytes after the last root tag that were not NBT (kept so the file round-trips).</summary>
    public byte[]? Trailing { get; set; }

    public NbtFile() { }
    public NbtFile(NbtCompound root, NbtFormat format, CompressionKind compression = CompressionKind.None, string rootName = "")
    {
        Roots.Add(new NamedTag(rootName, root));
        Format = format;
        Compression = compression;
    }

    public NbtTag Root { get => Roots[0].Tag; set => Roots[0].Tag = value; }
    public NbtCompound RootCompound => Roots.Count > 0 && Roots[0].Tag is NbtCompound c ? c : throw new SaveFormatException("The file has no root compound.");
    public string RootName { get => Roots[0].Name; set => Roots[0].Name = value; }

    public string Describe()
    {
        var parts = new List<string> { Format.Name };
        if (Compression != CompressionKind.None) parts.Add(Compression.ToString().ToLowerInvariant());
        if (HeaderVersion is int v) parts.Add($"Bedrock header v{v}");
        if (Roots.Count > 1) parts.Add($"{Roots.Count} root tags");
        return string.Join(", ", parts);
    }

    public static NbtFile Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Detects compression, header and dialect, then parses.</summary>
    public static NbtFile Parse(byte[] raw, NbtFormat? format = null)
    {
        var compression = CompressionKind.None;
        byte[] data = raw;
        var kind = IO.Compression.Detect(raw);
        if (kind is CompressionKind.Gzip or CompressionKind.Zlib)
        {
            try { data = IO.Compression.Decompress(raw, kind); compression = kind; }
            catch when (kind == CompressionKind.Zlib) { data = raw; }
        }

        int offset = 0;
        int? header = null;
        if (data.Length >= 9 && (data[8] == (byte)NbtType.Compound || data[8] == (byte)NbtType.List))
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            int ver = BinaryPrimitives.ReadInt32LittleEndian(data);
            bool exact = len == data.Length - 8 && ver is >= 0 and < 1000;
            // A raw compound also starts with 0x0A, so a non-exact length only counts when byte 0 is not a tag start.
            bool loose = len > 0 && len < data.Length - 8 && ver is >= 1 and < 100 && data[0] != (byte)NbtType.Compound;
            if (exact || loose)
            {
                header = ver;
                offset = 8;
            }
        }

        IEnumerable<NbtFormat> candidates = format != null ? [format]
            : header != null ? [NbtFormat.Bedrock, NbtFormat.Java, NbtFormat.BedrockNetwork]
            : compression == CompressionKind.Gzip ? [NbtFormat.Java, NbtFormat.Bedrock]
            : [NbtFormat.Java, NbtFormat.Bedrock, NbtFormat.BedrockNetwork];

        NbtFile? best = null;
        Exception? firstError = null;
        int bestConsumed = -1;
        foreach (var f in candidates)
        {
            try
            {
                var file = new NbtFile { Format = f, Compression = compression, HeaderVersion = header };
                var r = new ByteReader(data, offset, data.Length - offset);
                int consumed = 0;
                while (!r.AtEnd)
                {
                    int before = r.Position;
                    NamedTag? t;
                    try { t = NbtBinary.ReadNamed(r, f); }
                    catch when (file.Roots.Count > 0) { r.Position = before; break; }
                    if (t == null)
                    {
                        // TAG_End padding: keep going only if the rest is zeros
                        if (file.Roots.Count > 0) { r.Position = before; break; }
                        continue;
                    }
                    file.Roots.Add(t);
                    consumed = r.Position;
                }
                if (file.Roots.Count == 0) continue;
                if (r.Remaining > 0) file.Trailing = data.AsSpan(offset + consumed).ToArray();
                if (header != null && file.Trailing == null)
                {
                    int declared = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
                    if (declared != consumed) file.HeaderLengthMismatch = true;
                }
                if (r.Remaining == 0) return file;
                if (consumed > bestConsumed) { best = file; bestConsumed = consumed; }
            }
            catch (Exception ex) { firstError ??= ex; }
        }
        return best ?? throw new SaveFormatException("Not a readable NBT file: " + (firstError?.Message ?? "no root tag."), firstError ?? new SaveFormatException("no root tag"));
    }

    /// <summary>True when a Bedrock header declared a different length than the data (it is rewritten correctly on save).</summary>
    public bool HeaderLengthMismatch { get; private set; }

    public byte[] ToUncompressedBytes()
    {
        var w = new ByteWriter(4096);
        if (HeaderVersion is int v) { w.I32LE(v); w.I32LE(0); }
        int start = w.Length;
        foreach (var t in Roots) NbtBinary.WriteNamed(w, t.Name, t.Tag, Format);
        if (HeaderVersion != null) w.PatchI32LE(4, w.Length - start);
        if (Trailing != null) w.Bytes(Trailing);
        return w.ToArray();
    }

    public byte[] ToBytes()
    {
        byte[] data = ToUncompressedBytes();
        return Compression == CompressionKind.None ? data : IO.Compression.Compress(data, Compression);
    }

    public void Save(string path) => SafeFile.WriteAllBytes(path, ToBytes());
}
