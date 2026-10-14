using System.Buffers.Binary;

namespace DotNative;

// OpenType TTC/OTC offsets are relative to the collection, not the face directory.
// Rebuild a standalone SFNT so every native backend receives the same selected face.
internal static class FontCollections
{
    internal const int MaximumCollectionBytes = 32 * 1024 * 1024;
    private const int MaximumFaceBytes = 8 * 1024 * 1024;

    private readonly record struct Table(uint Tag, int Offset, int Length);

    internal static byte[] Extract(ReadOnlySpan<byte> source, int faceIndex)
    {
        if (faceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
        if (source.Length < 12 || source.Length > MaximumCollectionBytes)
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!source[..4].SequenceEqual("ttcf"u8))
            throw Invalid("Expected a TTC/OTC collection.");
        uint version = Read32(source, 4),
            count = Read32(source, 8);
        if (version is not (0x10000 or 0x20000) || count is 0 or > 256)
            throw Invalid("Unsupported collection version or face count.");
        int headerSize = 12 + (int)count * 4 + (version == 0x20000 ? 12 : 0);
        Range(source, 0, (uint)headerSize);
        if ((uint)faceIndex >= count)
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
        int directory = Range(source, Read32(source, 12 + faceIndex * 4), 12);
        if (directory < headerSize || directory % 4 != 0)
            throw Invalid("Invalid face directory offset.");
        uint signature = Read32(source, directory);
        if (signature is not (0x10000 or 0x4f54544f))
            throw Invalid("Unsupported face outline format.");
        int numTables = Read16(source, directory + 4);
        if (numTables is 0 or > 256)
            throw Invalid("Invalid font table count.");
        Range(source, (uint)directory, (uint)(12 + numTables * 16));
        var tables = new List<Table>(numTables);
        var tags = new HashSet<uint>();
        for (int i = 0; i < numTables; i++)
        {
            int record = directory + 12 + i * 16;
            uint tag = Read32(source, record),
                length = Read32(source, record + 12);
            int offset = Range(source, Read32(source, record + 8), length);
            if (offset % 4 != 0)
                throw Invalid("Unaligned font table.");
            if (!tags.Add(tag))
                throw Invalid("Duplicate font table.");
            // A signature over the original file is invalid after face extraction.
            if (tag != 0x44534947)
                tables.Add(new(tag, offset, (int)length));
        }
        if (tables.Count == 0)
            throw Invalid("No font tables in selected face.");
        tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        int total = 12 + tables.Count * 16;
        foreach (var table in tables)
        {
            long size = total + (((long)table.Length + 3) & ~3L);
            if (size > MaximumFaceBytes)
                throw Invalid("Extracted face exceeds 8 MiB.");
            total = (int)size;
        }
        var output = new byte[total];
        Write32(output, 0, signature);
        Write16(output, 4, (ushort)tables.Count);
        int power = 1,
            exponent = 0;
        while (power * 2 <= tables.Count)
        {
            power *= 2;
            exponent++;
        }
        Write16(output, 6, (ushort)(power * 16));
        Write16(output, 8, (ushort)exponent);
        Write16(output, 10, (ushort)(tables.Count * 16 - power * 16));
        int cursor = 12 + tables.Count * 16,
            head = -1;
        for (int i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            source.Slice(table.Offset, table.Length).CopyTo(output.AsSpan(cursor));
            if (table.Tag == 0x68656164)
            {
                if (table.Length < 54)
                    throw Invalid("Truncated head table.");
                head = cursor;
                Write32(output, head + 8, 0);
            }
            int record = 12 + i * 16;
            Write32(output, record, table.Tag);
            Write32(output, record + 4, Checksum(output.AsSpan(cursor, table.Length)));
            Write32(output, record + 8, (uint)cursor);
            Write32(output, record + 12, (uint)table.Length);
            cursor += (table.Length + 3) & ~3;
        }
        if (head < 0)
            throw Invalid("Missing head table.");
        Write32(output, head + 8, unchecked(0xB1B0AFBA - Checksum(output)));
        return output;
    }

    private static int Range(ReadOnlySpan<byte> bytes, uint offset, uint length)
    {
        if ((ulong)offset + length > (ulong)bytes.Length)
            throw Invalid("Font table lies outside the collection.");
        return (int)offset;
    }

    private static ushort Read16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);

    private static uint Read32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);

    private static void Write16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes[offset..], value);

    private static void Write32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes[offset..], value);

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            uint word = 0;
            for (int j = 0; j < 4; j++)
                word = (word << 8) | (i + j < bytes.Length ? bytes[i + j] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }

    private static ArgumentException Invalid(string message) => new(message, "encoded");
}
