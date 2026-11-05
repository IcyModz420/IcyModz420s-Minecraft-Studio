// MinecraftStudio.Core/Nbt/NbtTags.cs: the NBT tag types (byte through long-array, list, compound) and the NbtType enum.
// Owns: NbtType, NbtTypeInfo, NbtTag, NbtByte, NbtShort, NbtInt, NbtLong, NbtFloat, NbtDouble, NbtString, NbtByteArray, NbtIntArray, NbtLongArray, NbtList, NbtCompound.
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace MinecraftStudio.Core.Nbt;

public enum NbtType : byte
{
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12,
}

public static class NbtTypeInfo
{
    public static string Name(NbtType t) => t switch
    {
        NbtType.End => "End",
        NbtType.Byte => "Byte",
        NbtType.Short => "Short",
        NbtType.Int => "Int",
        NbtType.Long => "Long",
        NbtType.Float => "Float",
        NbtType.Double => "Double",
        NbtType.ByteArray => "Byte Array",
        NbtType.String => "String",
        NbtType.List => "List",
        NbtType.Compound => "Compound",
        NbtType.IntArray => "Int Array",
        NbtType.LongArray => "Long Array",
        _ => $"Type {(int)t}",
    };

    public static bool IsNumeric(NbtType t) => t is >= NbtType.Byte and <= NbtType.Double;
    public static bool IsArray(NbtType t) => t is NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray;
    public static bool IsContainer(NbtType t) => t is NbtType.List or NbtType.Compound;

    public static NbtTag CreateDefault(NbtType t) => t switch
    {
        NbtType.Byte => new NbtByte(0),
        NbtType.Short => new NbtShort(0),
        NbtType.Int => new NbtInt(0),
        NbtType.Long => new NbtLong(0),
        NbtType.Float => new NbtFloat(0),
        NbtType.Double => new NbtDouble(0),
        NbtType.ByteArray => new NbtByteArray([]),
        NbtType.String => new NbtString(""),
        NbtType.List => new NbtList(),
        NbtType.Compound => new NbtCompound(),
        NbtType.IntArray => new NbtIntArray([]),
        NbtType.LongArray => new NbtLongArray([]),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };
}

/// <summary>Base class of every NBT value.</summary>
public abstract class NbtTag
{
    public abstract NbtType Type { get; }
    public abstract NbtTag Clone();

    /// <summary>Numeric value as a long (for Byte/Short/Int/Long; floats are truncated).</summary>
    public virtual long AsLong => throw new InvalidCastException($"{NbtTypeInfo.Name(Type)} is not numeric.");
    public virtual double AsDouble => AsLong;
    public bool IsNumeric => NbtTypeInfo.IsNumeric(Type);

    public override string ToString() => Snbt.Write(this, SnbtStyle.Compact);

    /// <summary>Structural equality (same types, same values, same key order).</summary>
    public abstract bool ContentEquals(NbtTag other);
}

public sealed class NbtByte : NbtTag
{
    public NbtByte(sbyte value) => Value = value;
    public NbtByte(bool value) => Value = (sbyte)(value ? 1 : 0);
    public sbyte Value { get; set; }
    public override NbtType Type => NbtType.Byte;
    public override NbtTag Clone() => new NbtByte(Value);
    public override long AsLong => Value;
    public override bool ContentEquals(NbtTag o) => o is NbtByte b && b.Value == Value;
}

public sealed class NbtShort : NbtTag
{
    public NbtShort(short value) => Value = value;
    public short Value { get; set; }
    public override NbtType Type => NbtType.Short;
    public override NbtTag Clone() => new NbtShort(Value);
    public override long AsLong => Value;
    public override bool ContentEquals(NbtTag o) => o is NbtShort b && b.Value == Value;
}

public sealed class NbtInt : NbtTag
{
    public NbtInt(int value) => Value = value;
    public int Value { get; set; }
    public override NbtType Type => NbtType.Int;
    public override NbtTag Clone() => new NbtInt(Value);
    public override long AsLong => Value;
    public override bool ContentEquals(NbtTag o) => o is NbtInt b && b.Value == Value;
}

public sealed class NbtLong : NbtTag
{
    public NbtLong(long value) => Value = value;
    public long Value { get; set; }
    public override NbtType Type => NbtType.Long;
    public override NbtTag Clone() => new NbtLong(Value);
    public override long AsLong => Value;
    public override bool ContentEquals(NbtTag o) => o is NbtLong b && b.Value == Value;
}

public sealed class NbtFloat : NbtTag
{
    public NbtFloat(float value) => Value = value;
    public float Value { get; set; }
    public override NbtType Type => NbtType.Float;
    public override NbtTag Clone() => new NbtFloat(Value);
    public override long AsLong => (long)Value;
    public override double AsDouble => Value;
    public override bool ContentEquals(NbtTag o) => o is NbtFloat b && BitConverter.SingleToInt32Bits(b.Value) == BitConverter.SingleToInt32Bits(Value);
}

public sealed class NbtDouble : NbtTag
{
    public NbtDouble(double value) => Value = value;
    public double Value { get; set; }
    public override NbtType Type => NbtType.Double;
    public override NbtTag Clone() => new NbtDouble(Value);
    public override long AsLong => (long)Value;
    public override double AsDouble => Value;
    public override bool ContentEquals(NbtTag o) => o is NbtDouble b && BitConverter.DoubleToInt64Bits(b.Value) == BitConverter.DoubleToInt64Bits(Value);
}

public sealed class NbtString : NbtTag
{
    public NbtString(string value) => Value = value;
    public string Value { get; set; }
    public override NbtType Type => NbtType.String;
    public override NbtTag Clone() => new NbtString(Value);
    public override bool ContentEquals(NbtTag o) => o is NbtString b && b.Value == Value;
}

public sealed class NbtByteArray : NbtTag
{
    public NbtByteArray(byte[] value) => Value = value;
    public byte[] Value { get; set; }
    public override NbtType Type => NbtType.ByteArray;
    public override NbtTag Clone() => new NbtByteArray((byte[])Value.Clone());
    public override bool ContentEquals(NbtTag o) => o is NbtByteArray b && b.Value.AsSpan().SequenceEqual(Value);
}

public sealed class NbtIntArray : NbtTag
{
    public NbtIntArray(int[] value) => Value = value;
    public int[] Value { get; set; }
    public override NbtType Type => NbtType.IntArray;
    public override NbtTag Clone() => new NbtIntArray((int[])Value.Clone());
    public override bool ContentEquals(NbtTag o) => o is NbtIntArray b && b.Value.AsSpan().SequenceEqual(Value);
}

public sealed class NbtLongArray : NbtTag
{
    public NbtLongArray(long[] value) => Value = value;
    public long[] Value { get; set; }
    public override NbtType Type => NbtType.LongArray;
    public override NbtTag Clone() => new NbtLongArray((long[])Value.Clone());
    public override bool ContentEquals(NbtTag o) => o is NbtLongArray b && b.Value.AsSpan().SequenceEqual(Value);
}

/// <summary>Homogeneous list. The element type is kept even when the list is empty so files round-trip exactly.</summary>
public sealed class NbtList : NbtTag, IList<NbtTag>
{
    readonly List<NbtTag> _items;

    public NbtList() { _items = []; }
    public NbtList(NbtType elementType) { _items = []; ElementType = elementType; }
    public NbtList(NbtType elementType, int capacity) { _items = new List<NbtTag>(capacity); ElementType = elementType; }
    public NbtList(IEnumerable<NbtTag> items) { _items = []; foreach (var t in items) Add(t); }

    public NbtType ElementType { get; set; }
    public override NbtType Type => NbtType.List;
    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public NbtTag this[int index]
    {
        get => _items[index];
        set { Check(value); _items[index] = value; }
    }

    void Check(NbtTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (ElementType == NbtType.End || _items.Count == 0) ElementType = tag.Type;
        else if (tag.Type != ElementType)
            throw new ArgumentException($"This list holds {NbtTypeInfo.Name(ElementType)} values, not {NbtTypeInfo.Name(tag.Type)}.");
    }

    public void Add(NbtTag item) { Check(item); _items.Add(item); }
    /// <summary>Adds without type checks (used by the binary reader, which already knows the type).</summary>
    internal void AddUnchecked(NbtTag item) => _items.Add(item);
    public void Insert(int index, NbtTag item) { Check(item); _items.Insert(index, item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);
    public bool Remove(NbtTag item) => _items.Remove(item);
    public void Clear() => _items.Clear();
    public bool Contains(NbtTag item) => _items.Contains(item);
    public int IndexOf(NbtTag item) => _items.IndexOf(item);
    public void CopyTo(NbtTag[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public void Move(int from, int to) { var t = _items[from]; _items.RemoveAt(from); _items.Insert(to, t); }
    public IEnumerator<NbtTag> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public IEnumerable<T> OfType<T>() where T : NbtTag => _items.OfType<T>();

    public override NbtTag Clone()
    {
        var copy = new NbtList(ElementType, _items.Count);
        foreach (var t in _items) copy._items.Add(t.Clone());
        return copy;
    }

    public override bool ContentEquals(NbtTag o)
    {
        if (o is not NbtList l || l.Count != Count || (Count > 0 && l.ElementType != ElementType)) return false;
        for (int i = 0; i < Count; i++) if (!_items[i].ContentEquals(l._items[i])) return false;
        return true;
    }
}

/// <summary>Named values in their original order. Duplicate names read from damaged files are preserved; lookups see the last one.</summary>
public sealed class NbtCompound : NbtTag, IEnumerable<KeyValuePair<string, NbtTag>>
{
    readonly List<KeyValuePair<string, NbtTag>> _entries;
    Dictionary<string, int>? _index;

    public NbtCompound() => _entries = [];
    public NbtCompound(int capacity) => _entries = new(capacity);

    public override NbtType Type => NbtType.Compound;
    public int Count => _entries.Count;

    Dictionary<string, int> Index
    {
        get
        {
            if (_index == null)
            {
                _index = new Dictionary<string, int>(_entries.Count, StringComparer.Ordinal);
                for (int i = 0; i < _entries.Count; i++) _index[_entries[i].Key] = i;
            }
            return _index;
        }
    }

    int Find(string name)
    {
        if (_entries.Count <= 8 && _index == null)
        {
            for (int i = _entries.Count - 1; i >= 0; i--) if (_entries[i].Key == name) return i;
            return -1;
        }
        return Index.TryGetValue(name, out int idx) ? idx : -1;
    }

    public NbtTag? this[string name]
    {
        get { int i = Find(name); return i >= 0 ? _entries[i].Value : null; }
        set
        {
            if (value == null) { Remove(name); return; }
            int i = Find(name);
            if (i >= 0) _entries[i] = new(name, value);
            else { _entries.Add(new(name, value)); if (_index != null) _index[name] = _entries.Count - 1; }
        }
    }

    public string KeyAt(int index) => _entries[index].Key;
    public NbtTag TagAt(int index) => _entries[index].Value;
    public int IndexOf(string name) => Find(name);
    public bool ContainsKey(string name) => Find(name) >= 0;
    public IEnumerable<string> Keys => _entries.Select(e => e.Key);
    public IEnumerable<NbtTag> Values => _entries.Select(e => e.Value);

    public void Add(string name, NbtTag tag) => this[name] = tag;

    /// <summary>Appends even if the name already exists (binary reader only).</summary>
    internal void AddRaw(string name, NbtTag tag)
    {
        _entries.Add(new(name, tag));
        if (_index != null) _index[name] = _entries.Count - 1;
    }

    public void Insert(int position, string name, NbtTag tag)
    {
        Remove(name);
        position = Math.Clamp(position, 0, _entries.Count);
        _entries.Insert(position, new(name, tag));
        _index = null;
    }

    public bool Remove(string name)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
            if (_entries[i].Key == name) { _entries.RemoveAt(i); removed = true; }
        if (removed) _index = null;
        return removed;
    }

    public void RemoveAt(int index) { _entries.RemoveAt(index); _index = null; }

    public bool Rename(string oldName, string newName)
    {
        int i = Find(oldName);
        if (i < 0) return false;
        if (oldName == newName) return true;
        Remove(newName);
        i = Find(oldName);
        _entries[i] = new(newName, _entries[i].Value);
        _index = null;
        return true;
    }

    public void SetAt(int index, NbtTag tag) => _entries[index] = new(_entries[index].Key, tag);
    public void Move(int from, int to) { var e = _entries[from]; _entries.RemoveAt(from); _entries.Insert(to, e); _index = null; }
    public void Clear() { _entries.Clear(); _index = null; }

    public bool TryGet<T>(string name, [NotNullWhen(true)] out T? tag) where T : NbtTag
    {
        tag = this[name] as T;
        return tag != null;
    }

    public T? Get<T>(string name) where T : NbtTag => this[name] as T;
    public NbtCompound? GetCompound(string name) => this[name] as NbtCompound;
    public NbtList? GetList(string name) => this[name] as NbtList;

    public string? GetString(string name) => (this[name] as NbtString)?.Value;
    public long? GetLongOrNull(string name) => this[name] is { IsNumeric: true } t ? t.AsLong : null;
    public long GetLong(string name, long fallback = 0) => this[name] is { IsNumeric: true } t ? t.AsLong : fallback;
    public int GetInt(string name, int fallback = 0) => this[name] is { IsNumeric: true } t ? (int)t.AsLong : fallback;
    public double GetDouble(string name, double fallback = 0) => this[name] is { IsNumeric: true } t ? t.AsDouble : fallback;
    public bool GetBool(string name, bool fallback = false) => this[name] is { IsNumeric: true } t ? t.AsLong != 0 : fallback;
    public byte[]? GetBytes(string name) => (this[name] as NbtByteArray)?.Value;
    public int[]? GetInts(string name) => (this[name] as NbtIntArray)?.Value;
    public long[]? GetLongs(string name) => (this[name] as NbtLongArray)?.Value;

    /// <summary>Gets a child compound, creating it when missing.</summary>
    public NbtCompound GetOrAddCompound(string name)
    {
        if (this[name] is NbtCompound c) return c;
        c = new NbtCompound();
        this[name] = c;
        return c;
    }

    public void SetByte(string name, sbyte v) => this[name] = new NbtByte(v);
    public void SetBool(string name, bool v) => this[name] = new NbtByte(v);
    public void SetShort(string name, short v) => this[name] = new NbtShort(v);
    public void SetInt(string name, int v) => this[name] = new NbtInt(v);
    public void SetLong(string name, long v) => this[name] = new NbtLong(v);
    public void SetFloat(string name, float v) => this[name] = new NbtFloat(v);
    public void SetDouble(string name, double v) => this[name] = new NbtDouble(v);
    public void SetString(string name, string v) => this[name] = new NbtString(v);

    /// <summary>Sets a numeric value keeping the existing tag type when there is one.</summary>
    public void SetNumber(string name, double value, NbtType fallbackType = NbtType.Int)
    {
        var type = this[name] is { IsNumeric: true } t ? t.Type : fallbackType;
        this[name] = type switch
        {
            NbtType.Byte => new NbtByte((sbyte)value),
            NbtType.Short => new NbtShort((short)value),
            NbtType.Int => new NbtInt((int)value),
            NbtType.Long => new NbtLong((long)value),
            NbtType.Float => new NbtFloat((float)value),
            _ => new NbtDouble(value),
        };
    }

    public IEnumerator<KeyValuePair<string, NbtTag>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    public override NbtTag Clone()
    {
        var copy = new NbtCompound(_entries.Count);
        foreach (var e in _entries) copy._entries.Add(new(e.Key, e.Value.Clone()));
        return copy;
    }

    public override bool ContentEquals(NbtTag o)
    {
        if (o is not NbtCompound c || c.Count != Count) return false;
        for (int i = 0; i < Count; i++)
            if (_entries[i].Key != c._entries[i].Key || !_entries[i].Value.ContentEquals(c._entries[i].Value)) return false;
        return true;
    }
}
