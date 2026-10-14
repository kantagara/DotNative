namespace DotNative;

// Fenwick sums use doubles so distant fractional rows do not lose their extent.
// Mutation is owned by ListView on the UI thread, outside Build.
internal sealed class RowExtentIndex
{
    private const double MaximumExtent = 16_000_000;
    private readonly float[] _heights;
    private readonly double[] _tree;
    public int Length => _heights.Length + 1;
    public float this[Index index] => Prefix(index.GetOffset(Length));
    public float this[int index] => Prefix(index);

    public RowExtentIndex(float[] heights)
    {
        _heights = (float[])heights.Clone();
        _tree = new double[heights.Length + 1];
        double total = 0;
        for (int i = 1; i < _tree.Length; i++)
        {
            Validate(heights[i - 1]);
            total += heights[i - 1];
            _tree[i] += heights[i - 1];
            int parent = i + (i & -i);
            if (parent < _tree.Length)
                _tree[parent] += _tree[i];
        }
        if (total > MaximumExtent)
            throw new ArgumentOutOfRangeException(
                nameof(heights),
                "List exceeds the supported scroll extent."
            );
    }

    private static void Validate(float height)
    {
        if (!float.IsFinite(height) || height < 1)
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Row extent must be finite and at least one logical pixel."
            );
    }

    private double Sum(int count)
    {
        double result = 0;
        for (int i = count; i > 0; i -= i & -i)
            result += _tree[i];
        return result;
    }

    private float Prefix(int count)
    {
        if ((uint)count > (uint)_heights.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        return (float)Sum(count);
    }

    public void Update(int index, float height)
    {
        if ((uint)index >= (uint)_heights.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        Validate(height);
        double delta = (double)height - _heights[index];
        if (Sum(_heights.Length) + delta > MaximumExtent)
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "List exceeds the supported scroll extent."
            );
        _heights[index] = height;
        for (int i = index + 1; i < _tree.Length; i += i & -i)
            _tree[i] += delta;
    }

    public int IndexAt(float offset)
    {
        int index = 0;
        double sum = 0;
        // Binary lifting finds the last prefix at or before offset in O(log n).
        int bit = 1;
        while (bit <= _heights.Length / 2)
            bit <<= 1;
        for (; bit > 0; bit >>= 1)
        {
            int next = index + bit;
            if (next < _tree.Length && (float)(sum + _tree[next]) <= offset)
            {
                index = next;
                sum += _tree[next];
            }
        }
        return Math.Clamp(index, 0, Math.Max(0, _heights.Length - 1));
    }
}
