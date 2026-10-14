using DotNative;

internal static class RowExtentIndexTests
{
    public static void Run()
    {
        var random = new Random(741);
        var heights = Enumerable
            .Range(0, 4097)
            .Select(_ => 1f + (float)random.NextDouble() * 160)
            .ToArray();
        var index = new RowExtentIndex(heights);
        for (int pass = 0; pass < 200; pass++)
        {
            int changed = random.Next(heights.Length);
            heights[changed] = 1f + (float)random.NextDouble() * 400;
            index.Update(changed, heights[changed]);
            double expected = 0;
            for (int row = 0; row < heights.Length; row++)
            {
                Check(index[row] == (float)expected, "Incremental prefix differs from full sum");
                Check(
                    index.IndexAt(index[row]) == row,
                    "Fractional prefix boundary maps to wrong row"
                );
                expected += heights[row];
            }
            Check(index[^1] == (float)expected, "Total extent differs from full sum");
            float offset = (float)(random.NextDouble() * expected);
            int reference = 0;
            double prefix = 0;
            while (reference < heights.Length - 1 && (float)(prefix + heights[reference]) <= offset)
                prefix += heights[reference++];
            Check(
                index.IndexAt(offset) == reference,
                "Offset search differs from linear reference"
            );
        }
        float before = index[^1];
        foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity, 16_000_001f })
        {
            try
            {
                index.Update(10, invalid);
                throw new Exception("Invalid extent accepted");
            }
            catch (ArgumentOutOfRangeException) { }
            Check(index[^1] == before, "Rejected extent changed the index");
        }
        var large = new RowExtentIndex(Enumerable.Repeat(56f, 100_000).ToArray());
        // Warm up, then guard against reintroducing arrays proportional to data size.
        large.Update(50_000, 57);
        large.IndexAt(200_000);
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            large.Update(i, 40 + i % 20);
            large.IndexAt(i * 50);
        }
        Check(
            GC.GetAllocatedBytesForCurrentThread() == allocation,
            "Height updates/searches must not allocate collection-sized arrays"
        );
        var empty = new RowExtentIndex([]);
        Check(empty[^1] == 0 && empty.IndexAt(0) == 0, "Empty index");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
