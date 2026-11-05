// IO/AndroidBackup.cs: reads an Android backup (.ab) stream — the format `adb backup` writes.
// Owns: Read, which checks the "ANDROID BACKUP" header, refuses a password-encrypted backup, inflates the zlib body
// when present, and hands each regular file in the inner tar to a callback (path + a read-only stream valid until the
// next file). Pure parsing of untrusted device data, kept in Core so it can be tested without a phone.
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace MinecraftStudio.Core.IO;

public static class AndroidBackup
{
    /// <summary>Thrown when the backup is password-encrypted (the caller asks the user to retry with no password).</summary>
    public sealed class EncryptedException(string message) : Exception(message);

    /// <summary>
    /// Reads a .ab stream and calls <paramref name="onFile"/> for each regular file in it, with the entry's path and a
    /// stream of its bytes (only valid until the callback returns). Throws <see cref="InvalidDataException"/> if it is
    /// not an Android backup, or <see cref="EncryptedException"/> if it is password-protected.
    /// </summary>
    public static void Read(Stream ab, Action<string, Stream> onFile)
    {
        if (ReadHeaderLine(ab) != "ANDROID BACKUP") throw new InvalidDataException("Not an Android backup file.");
        ReadHeaderLine(ab);                        // format version — not needed
        string compression = ReadHeaderLine(ab);   // "1" = zlib deflate, "0" = none
        string encryption = ReadHeaderLine(ab);    // "none", or a cipher when a password was set
        if (!string.Equals(encryption, "none", StringComparison.OrdinalIgnoreCase))
            throw new EncryptedException("The backup is password-protected.");

        Stream payload = string.Equals(compression, "1", StringComparison.Ordinal) ? new ZLibStream(ab, CompressionMode.Decompress) : ab;
        using (payload)
        using (var tar = new TarReader(payload))
            while (tar.GetNextEntry() is { } entry)
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.DataStream is { } data)
                    onFile(entry.Name.Replace('\\', '/'), data);
    }

    /// <summary>One header line of an .ab file: ASCII up to the next newline (the header is plain text).</summary>
    static string ReadHeaderLine(Stream s)
    {
        var bytes = new List<byte>(32);
        int b;
        while ((b = s.ReadByte()) != -1 && b != '\n')
        {
            bytes.Add((byte)b);
            if (bytes.Count > 4096) throw new InvalidDataException("Not an Android backup file.");
        }
        return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
    }
}
