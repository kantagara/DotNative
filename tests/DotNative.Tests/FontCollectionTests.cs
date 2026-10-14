using System.Buffers.Binary;
using DotNative;

internal static class FontCollectionTests
{
    internal static byte[] Fixture(string name)
    {
        using var stream = typeof(FontCollectionTests).Assembly.GetManifestResourceStream(name)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    public static void Run()
    {
        var bytes = Fixture("DotNativeTest.ttc");
        var original = bytes.ToArray();
        var regular = FontSource.FromCollectionBytes("DotNativeTest", bytes, 0);
        var bold = FontSource.FromCollectionBytes("DotNativeTest", bytes, 1);
        Check(bytes.SequenceEqual(original));
        VerifySfnt(regular.Data, 400);
        VerifySfnt(bold.Data, 700);
        var cff = FontSource.FromCollectionBytes("DotNativeCff", Fixture("DotNativeTest.otc"), 0);
        Check(cff.Data.AsSpan(0, 4).SequenceEqual("OTTO"u8));
        VerifySfnt(cff.Data, 400);
        using var stream = new MemoryStream(bytes);
        var streamed = FontSource
            .FromCollectionStreamAsync("DotNativeTest", stream, 1)
            .GetAwaiter()
            .GetResult();
        Check(stream.CanRead && streamed.Data.SequenceEqual(bold.Data));
        stream.Position = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            FontSource
                .FromCollectionStreamAsync("DotNativeTest", stream, 0, cancellation.Token)
                .GetAwaiter()
                .GetResult();
            throw new Exception("Canceled font read completed");
        }
        catch (OperationCanceledException)
        {
            Check(stream.CanRead);
        }
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Check(
                FontSource
                    .FromCollectionFileAsync("DotNativeTest", path, 0)
                    .GetAwaiter()
                    .GetResult()
                    .Data.SequenceEqual(regular.Data)
            );
        }
        finally
        {
            File.Delete(path);
        }
        Reject(() => FontSource.FromCollectionBytes("DotNativeTest", bytes, -1));
        Reject(() => FontSource.FromCollectionBytes("DotNativeTest", bytes, 2));
        Reject(() => FontSource.FromCollectionBytes("DotNativeTest", regular.Data, 0));
        // Every truncated selected table is rejected. Unreferenced trailing bytes need not exist.
        int directory = (int)U32(bytes, 12),
            records = U16(bytes, directory + 4);
        int used = directory + 12 + records * 16;
        for (int i = 0; i < records; i++)
        {
            int p = directory + 12 + i * 16;
            used = Math.Max(used, (int)(U32(bytes, p + 8) + U32(bytes, p + 12)));
        }
        for (int end = 0; end < used; end++)
        {
            int length = end;
            Reject(() =>
                FontSource.FromCollectionBytes("DotNativeTest", bytes.AsSpan(0, length), 0)
            );
        }
        void Corrupt(int offset, uint value)
        {
            var bad = bytes.ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(offset), value);
            Reject(() => FontSource.FromCollectionBytes("DotNativeTest", bad, 0));
        }
        Reject(() =>
            FontSource.FromCollectionBytes("DotNativeTest", new byte[32 * 1024 * 1024 + 1], 0)
        );
        var amplified = new byte[bytes.Length + 4 * 1024 * 1024 + 4];
        bytes.CopyTo(amplified, 0);
        int sharedTable = (bytes.Length + 3) & ~3;
        for (int i = 0; i < records; i++)
        {
            int p = directory + 12 + i * 16;
            BinaryPrimitives.WriteUInt32BigEndian(amplified.AsSpan(p + 8), (uint)sharedTable);
            BinaryPrimitives.WriteUInt32BigEndian(amplified.AsSpan(p + 12), 4 * 1024 * 1024);
        }
        Reject(() => FontSource.FromCollectionBytes("DotNativeTest", amplified, 0));
        Corrupt(8, 0);
        Corrupt(12, (uint)directory + 1);
        Corrupt(directory + 4, 0xffff0000);
        Corrupt(directory + 20, U32(bytes, directory + 20) + 1);
        Corrupt(4, 0x30000);
        Corrupt(8, uint.MaxValue);
        Corrupt(12, uint.MaxValue);
        Corrupt(directory + 20, uint.MaxValue);
        Corrupt(directory + 24, uint.MaxValue);
        Corrupt(directory + 28, U32(bytes, directory + 12)); // duplicate table tag
        // A v2 header can be larger without moving directories if offsets are rebased.
        var v2 = new byte[bytes.Length + 12];
        bytes.AsSpan(0, 20).CopyTo(v2);
        bytes.AsSpan(20).CopyTo(v2.AsSpan(32));
        BinaryPrimitives.WriteUInt32BigEndian(v2.AsSpan(4), 0x20000);
        for (int face = 0; face < 2; face++)
        {
            int oldDir = (int)U32(bytes, 12 + face * 4),
                newDir = oldDir + 12;
            BinaryPrimitives.WriteUInt32BigEndian(v2.AsSpan(12 + face * 4), (uint)newDir);
            for (int i = 0; i < U16(bytes, oldDir + 4); i++)
            {
                int p = newDir + 12 + i * 16;
                BinaryPrimitives.WriteUInt32BigEndian(v2.AsSpan(p + 8), U32(v2, p + 8) + 12);
            }
        }
        Check(FontSource.FromCollectionBytes("DotNativeTest", v2, 1).Data.SequenceEqual(bold.Data));
    }

    private static void VerifySfnt(byte[] data, int weight)
    {
        Check(Checksum(data) == 0xB1B0AFBA);
        uint previous = 0;
        int foundWeight = 0;
        for (int i = 0; i < U16(data, 4); i++)
        {
            int p = 12 + i * 16;
            uint tag = U32(data, p);
            int offset = (int)U32(data, p + 8),
                length = (int)U32(data, p + 12);
            Check(tag > previous && offset % 4 == 0 && offset + length <= data.Length);
            previous = tag;
            var table = data.AsSpan(offset, length).ToArray();
            if (tag == 0x68656164)
                table.AsSpan(8, 4).Clear();
            Check(Checksum(table) == U32(data, p + 4));
            if (tag == 0x4f532f32)
                foundWeight = U16(table, 4);
        }
        Check(foundWeight == weight);
    }

    private static uint Checksum(byte[] bytes)
    {
        var padded = new byte[(bytes.Length + 3) & ~3];
        bytes.CopyTo(padded, 0);
        uint total = 0;
        for (int i = 0; i < padded.Length; i += 4)
            total = unchecked(total + U32(padded, i));
        return total;
    }

    private static uint U32(byte[] bytes, int p) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p));

    private static ushort U16(byte[] bytes, int p) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p));

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Font collection contract failed");
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new Exception("Malformed font collection accepted");
    }
}
