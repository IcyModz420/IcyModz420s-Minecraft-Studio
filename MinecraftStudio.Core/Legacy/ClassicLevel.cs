// MinecraftStudio.Core/Legacy/ClassicLevel.cs: reads Classic/Indev-era levels (the serialized com.mojang.minecraft.level.Level).
// Owns: ClassicFormat, ClassicLevel.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Legacy;

public enum ClassicFormat
{
    /// <summary>rd-132211 … Classic 0.0.12a_03: gzip of the raw block array (256×256×64).</summary>
    Raw,
    /// <summary>0.0.13a: magic 0x271BB788, version 1, name/creator/time/dimensions header.</summary>
    Header,
    /// <summary>0.0.14a … 0.30 and Classic servers: magic, version 2, serialized com.mojang.minecraft.level.Level.</summary>
    Serialized,
}

/// <summary>A Pre-Classic or Classic level file (.dat, .mine, server_level.dat).</summary>
public sealed class ClassicLevel
{
    public const uint Magic = 0x271BB788;

    byte[] _stream = [];          // decompressed file
    int _blocksOffset = -1;       // for Serialized: offset of the blocks array payload

    public ClassicFormat Format { get; private set; }
    public int Width { get; set; } = 256;   // X
    public int Length { get; set; } = 256;  // Z ("height" in the file)
    public int Height { get; set; } = 64;   // Y ("depth" in the file)
    public byte[] Blocks { get; set; } = [];
    public string Name { get; set; } = "";
    public string Creator { get; set; } = "";
    public long CreateTime { get; set; }
    public int SpawnX { get; set; }
    public int SpawnY { get; set; }
    public int SpawnZ { get; set; }
    public JavaSerialization.JObject? LevelObject { get; private set; }
    public List<object?> SerializedRoots { get; private set; } = [];
    public string? Warning { get; private set; }

    public int Index(int x, int y, int z) => (y * Length + z) * Width + x;

    /// <summary>True when the file was not gzipped (some tools save Classic levels decompressed).</summary>
    public bool Uncompressed { get; private set; }

    public static bool LooksLikeClassic(byte[] raw)
    {
        try
        {
            if (raw.Length >= 5 && BinaryPrimitives.ReadUInt32BigEndian(raw) == Magic) return true;
            if (raw.Length < 2 || raw[0] != 0x1F || raw[1] != 0x8B) return false;
            byte[] d = Compression.Gunzip(raw);
            if (d.Length >= 5 && BinaryPrimitives.ReadUInt32BigEndian(d) == Magic) return true;
            // Raw early levels: exactly W*L*H bytes with no NBT header.
            return d.Length is 4_194_304 or 1_048_576 or 16_777_216 && d[0] != 0x0A;
        }
        catch { return false; }
    }

    public static ClassicLevel Load(string path) => Parse(File.ReadAllBytes(path));

    public static ClassicLevel Parse(byte[] raw)
    {
        bool gz = raw.Length >= 2 && raw[0] == 0x1F && raw[1] == 0x8B;
        byte[] d = gz ? Compression.Gunzip(raw) : raw;
        var lvl = new ClassicLevel { _stream = d, Uncompressed = !gz };
        if (d.Length >= 5 && BinaryPrimitives.ReadUInt32BigEndian(d) == Magic)
        {
            byte version = d[4];
            if (version == 1) lvl.ParseHeader(d);
            else if (version == 2) lvl.ParseSerialized(d);
            else throw new SaveFormatException($"Unknown Classic level version {version}.");
        }
        else
        {
            lvl.Format = ClassicFormat.Raw;
            lvl.Blocks = d;
            int n = d.Length;
            if (n == 256 * 256 * 64) { lvl.Width = 256; lvl.Length = 256; lvl.Height = 64; }
            else
            {
                // Unknown size: assume square with height 64.
                int side = (int)Math.Sqrt(n / 64.0);
                lvl.Width = lvl.Length = side;
                lvl.Height = n / (side * side);
            }
            lvl.SpawnX = lvl.Width / 2; lvl.SpawnZ = lvl.Length / 2; lvl.SpawnY = lvl.Height;
        }
        return lvl;
    }

    void ParseHeader(byte[] d)
    {
        Format = ClassicFormat.Header;
        var r = new ByteReader(d);
        r.Skip(5);
        Name = Nbt.NbtText.DecodeModifiedUtf8(r.Span(r.U16BE()));
        Creator = Nbt.NbtText.DecodeModifiedUtf8(r.Span(r.U16BE()));
        CreateTime = r.I64BE();
        Width = r.I16BE();
        Length = r.I16BE();
        Height = r.I16BE();
        int n = Width * Length * Height;
        var blocks = r.Bytes(Math.Min(n, r.Remaining));
        if (blocks.Length < n)
        {
            Warning = "Block data is truncated; the missing part is filled with air.";
            Array.Resize(ref blocks, n);
        }
        Blocks = blocks;
        SpawnX = Width / 2; SpawnZ = Length / 2; SpawnY = Height;
    }

    void ParseSerialized(byte[] d)
    {
        Format = ClassicFormat.Serialized;
        SerializedRoots = JavaSerialization.Parse(d, 5);
        var level = JavaSerialization.FindObject(SerializedRoots, o => o.Class.Name.EndsWith(".Level", StringComparison.Ordinal) || o.Fields.ContainsKey("blocks"))
            ?? throw new SaveFormatException("The serialized data does not contain a Level object.");
        LevelObject = level;
        Width = Convert.ToInt32(level["width"] ?? 256);
        Length = Convert.ToInt32(level["height"] ?? 256);
        Height = Convert.ToInt32(level["depth"] ?? 64);
        Name = level["name"] as string ?? "";
        Creator = level["creator"] as string ?? "";
        CreateTime = level["createTime"] is long ct ? ct : 0;
        SpawnX = level["xSpawn"] is int sx ? sx : Width / 2;
        SpawnY = level["ySpawn"] is int sy ? sy : Height;
        SpawnZ = level["zSpawn"] is int sz ? sz : Length / 2;
        if (level["blocks"] is JavaSerialization.JArray arr && arr.Values is byte[] blocks)
        {
            Blocks = blocks;
            _blocksOffset = arr.PayloadOffset;
        }
        else throw new SaveFormatException("The Level object has no block array.");
        if (Blocks.Length != Width * Length * Height) Warning = $"Block array has {Blocks.Length} bytes but the dimensions say {Width * Length * Height}.";
    }

    /// <summary>
    /// Whether the file keeps a spawn point that can be written back as it is: a level the game saved from 0.0.14a on (the
    /// serialized Level's xSpawn/ySpawn/zSpawn, patched in place). The oldest levels are only blocks, 0.0.13a's header has none.
    /// </summary>
    public bool KeepsSpawn => Format == ClassicFormat.Serialized && _blocksOffset >= 0 && LevelObject != null
        && LevelObject.Offsets.ContainsKey("xSpawn") && LevelObject.Offsets.ContainsKey("ySpawn") && LevelObject.Offsets.ContainsKey("zSpawn")
        && LevelObject["blocks"] is JavaSerialization.JArray blocks && blocks.Length == Blocks.Length;

    /// <summary>Writes the level in its original format (same-size edits are patched into the original stream).</summary>
    public byte[] ToBytes()
    {
        switch (Format)
        {
            case ClassicFormat.Raw:
                return Compression.Gzip(Blocks);
            case ClassicFormat.Serialized when _blocksOffset >= 0 && Blocks.Length == ((JavaSerialization.JArray)LevelObject!["blocks"]!).Length:
                {
                    var d = (byte[])_stream.Clone();
                    Blocks.CopyTo(d, _blocksOffset);
                    Patch(d, "xSpawn", SpawnX);
                    Patch(d, "ySpawn", SpawnY);
                    Patch(d, "zSpawn", SpawnZ);
                    return Uncompressed ? d : Compression.Gzip(d);
                }
            default:
                return BuildHeaderFormat();
        }
    }

    void Patch(byte[] d, string field, int value)
    {
        if (LevelObject != null && LevelObject.Offsets.TryGetValue(field, out int off) && LevelObject.Fields[field] is int)
            BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(off), value);
    }

    /// <summary>Format version 1 (accepted by every later Classic build): used for resized or new levels.</summary>
    public byte[] BuildHeaderFormat()
    {
        var w = new ByteWriter(Blocks.Length + 64);
        w.U32BE(Magic);
        w.U8(1);
        WriteUtf(w, string.IsNullOrEmpty(Name) ? "A Nice World" : Name);
        WriteUtf(w, string.IsNullOrEmpty(Creator) ? AppInfo.Name : Creator);
        w.I64BE(CreateTime != 0 ? CreateTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        w.I16BE((short)Width);
        w.I16BE((short)Length);
        w.I16BE((short)Height);
        w.Bytes(Blocks);
        return Compression.Gzip(w.WrittenSpan);
    }

    static void WriteUtf(ByteWriter w, string s)
    {
        int len = Nbt.NbtText.ModifiedUtf8Length(s);
        w.U16BE((ushort)len);
        Nbt.NbtText.EncodeModifiedUtf8(s, w.Reserve(len));
    }

    public string Describe() => Format switch
    {
        ClassicFormat.Raw => "Pre-Classic / early Classic raw level",
        ClassicFormat.Header => "Classic 0.0.13a level (format 1)",
        _ => $"Classic level (serialized {LevelObject?.Class.Name ?? "Level"})",
    };

    public string DescribeSerialized() => LevelObject == null ? "" : JavaSerialization.Describe(LevelObject);

    public static string Ascii(byte[] b) => Encoding.ASCII.GetString(b);
}
