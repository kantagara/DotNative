namespace DotNative;

/// <summary>Windowed vertical list with native measurement, stable keys and scroll anchoring.</summary>
public class ListView<T> : Component
{
    private sealed record Data(
        T[] Items,
        string[] Keys,
        RowExtentIndex Starts,
        Dictionary<string, int> Indices,
        ulong MeasurementVersion = 0
    );

    private sealed class Row(T value, Func<T, Node> builder) : Component
    {
        internal Element? Container;
        internal uint Revision;
        internal float Extent;

        public override Element Build() => builder(value);
    }

    private Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private Dictionary<string, Row> _spareRows = new(StringComparer.Ordinal);
    private Element? _window;
    private int _first,
        _end;
    private Data? _renderedData;
    private readonly State<Data> _data;
    private readonly Func<T, Node> _builder;
    private readonly Func<T, string> _key;
    private readonly Func<T, float> _extent;
    private readonly bool _automatic;
    private uint _measurementRevision = 1;
    private float _measuredWidth;
    private readonly Dictionary<string, float> _measurements = new(StringComparer.Ordinal);
    public ScrollController Controller { get; } = new();
    public float Height { get; init; } = 200;
    public int Overscan { get; init; } = 4;

    /// <summary>Extra logical pixels prepared on either side; null uses one viewport. Overscan adds rows beyond this cache.</summary>
    public float? CacheExtent { get; init; }

    /// <summary>Fill the parent's available height; Height supplies the initial viewport estimate.</summary>
    public bool FillAvailableHeight { get; init; }
    public bool KeepEndVisible { get; init; }
    public NestedScrollMode NestedScrolling { get; init; } = NestedScrollMode.Contain;

    /// <summary>Automatically measures native row heights; unseen rows initially estimate 56 logical pixels.</summary>
    public ListView(IReadOnlyList<T> items, Func<T, Node> item, Func<T, string> key)
        : this(items, item, key, 56)
    {
        _automatic = true;
    }

    public ListView(
        IReadOnlyList<T> items,
        Func<T, Node> item,
        Func<T, string> key,
        float itemExtent
    )
        : this(items, item, key, _ => itemExtent) { }

    public ListView(
        IReadOnlyList<T> items,
        Func<T, Node> item,
        Func<T, string> key,
        Func<T, float> itemExtent
    )
    {
        _builder = item ?? throw new ArgumentNullException(nameof(item));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _extent = itemExtent ?? throw new ArgumentNullException(nameof(itemExtent));
        _data = new(CreateData(items));
    }

    private Data CreateData(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = values.ToArray();
        var keys = new string[items.Length];
        var heights = new float[items.Length];
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < items.Length; i++)
        {
            var key = _key(items[i]);
            if (string.IsNullOrEmpty(key) || !indices.TryAdd(key, i))
                throw new ArgumentException("ListView keys must be nonempty and unique.");
            keys[i] = key;
            heights[i] = _extent(items[i]);
        }
        return new(items, keys, new RowExtentIndex(heights), indices);
    }

    /// <summary>Replace the snapshot on the UI thread, retaining the first visible item's key and offset where possible.</summary>
    public void SetItems(IReadOnlyList<T> items)
    {
        var next = CreateData(items);
        var old = _data.Value;
        if (_automatic)
        {
            // Keep measurements only for unchanged items. Updated content is measured again.
            var retained = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < next.Items.Length; i++)
                if (
                    old.Indices.TryGetValue(next.Keys[i], out var prior)
                    && EqualityComparer<T>.Default.Equals(old.Items[prior], next.Items[i])
                    && _measurements.TryGetValue(next.Keys[i], out var measured)
                )
                    retained.Add(next.Keys[i], measured);
            next = WithMeasurements(next, retained);
            ReplaceData(old, next);
            _measurements.Clear();
            foreach (var pair in retained)
                _measurements.Add(pair.Key, pair.Value);
            _measurementRevision = checked(_measurementRevision + 1);
            return;
        }
        ReplaceData(old, next);
    }

    private void ReplaceData(Data old, Data next, bool measurement = false)
    {
        var offset = Controller.Offset;
        bool atEnd =
            (KeepEndVisible || (measurement && old.Starts[^1] > Viewport))
            && offset >= Math.Max(0, old.Starts[^1] - Viewport) - 1;
        if (old.Items.Length > 0 && !atEnd)
        {
            var index = IndexAt(old.Starts, offset);
            if (next.Indices.TryGetValue(old.Keys[index], out var newIndex))
                offset =
                    next.Starts[newIndex]
                    + Math.Min(
                        offset - old.Starts[index],
                        next.Starts[newIndex + 1] - next.Starts[newIndex]
                    );
        }
        // State rejects calls during Build before any scroll command is issued.
        _data.Value = next;
        var maximum = Math.Max(0, next.Starts[^1] - Viewport);
        var target = atEnd ? maximum : Math.Clamp(offset, 0, maximum);
        if (target != Controller.Offset)
            Controller.JumpTo(target);
    }

    private Data WithMeasurements(Data data, IReadOnlyDictionary<string, float> heights)
    {
        var values = new float[data.Items.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = heights.TryGetValue(data.Keys[i], out var height)
                ? height
                : _extent(data.Items[i]);
        return data with { Starts = new RowExtentIndex(values) };
    }

    private void Measure(uint revision, string key, float height, float width)
    {
        if (revision != _measurementRevision || !float.IsFinite(height) || height < 0 || width <= 0)
            return;
        height = Math.Max(1, height);
        var old = _data.Value;
        if (!old.Indices.ContainsKey(key))
            return;
        // A new available width changes wrapping even for rows outside the window.
        bool widthChanged = _measuredWidth > 0 && Math.Abs(width - _measuredWidth) > 0.5f;
        if (
            !widthChanged
            && _measurements.TryGetValue(key, out var previous)
            && Math.Abs(previous - height) < 0.1f
        )
            return;
        if (widthChanged)
        {
            var measurements = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [key] = height,
            };
            ReplaceData(old, WithMeasurements(old, measurements), measurement: true);
            _measurements.Clear();
        }
        else
        {
            var offset = Controller.Offset;
            var viewport = Viewport;
            bool atEnd =
                (KeepEndVisible || old.Starts[^1] > viewport)
                && offset >= Math.Max(0, old.Starts[^1] - viewport) - 1;
            int anchor = old.Starts.IndexAt(offset);
            float within = offset - old.Starts[anchor];
            old.Starts.Update(old.Indices[key], height);
            // Notify readers while retaining row identities; the index is never mutated during Build.
            _data.Value = old with
            {
                MeasurementVersion = checked(old.MeasurementVersion + 1),
            };
            var maximum = Math.Max(0, old.Starts[^1] - viewport);
            var target = atEnd
                ? maximum
                : Math.Clamp(
                    old.Starts[anchor]
                        + Math.Min(within, old.Starts[anchor + 1] - old.Starts[anchor]),
                    0,
                    maximum
                );
            if (target != Controller.Offset)
                Controller.JumpTo(target);
        }
        _measuredWidth = width;
        _measurements[key] = height;
    }

    public void ScrollToIndex(int index)
    {
        var data = _data.Value;
        if ((uint)index >= (uint)data.Items.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        Controller.JumpTo(Math.Min(data.Starts[index], Math.Max(0, data.Starts[^1] - Viewport)));
    }

    public void ScrollToEnd() => Controller.JumpTo(Math.Max(0, _data.Value.Starts[^1] - Viewport));

    private float Viewport => Controller.ViewportExtent > 0 ? Controller.ViewportExtent : Height;

    private static int IndexAt(RowExtentIndex starts, float offset) => starts.IndexAt(offset);

    public override Element Build(BuildContext context)
    {
        if (!float.IsFinite(Height) || Height <= 0 || Overscan < 0 || Overscan > 100)
            throw new ArgumentOutOfRangeException(nameof(Height));
        if (
            CacheExtent is { } requestedCache
            && (!float.IsFinite(requestedCache) || requestedCache < 0)
        )
            throw new ArgumentOutOfRangeException(nameof(CacheExtent));
        var data = _data.Value;
        var viewport = Viewport;
        var cache = CacheExtent ?? viewport;
        var offset = Math.Clamp(Controller.Offset, 0, Math.Max(0, data.Starts[^1] - viewport));
        int first = Math.Max(0, IndexAt(data.Starts, Math.Max(0, offset - cache)) - Overscan);
        int end = Math.Min(
            data.Items.Length,
            IndexAt(data.Starts, offset + viewport + cache) + 1 + Overscan
        );
        // Controller reads above remain tracked. Returning the same description
        // lets the resolver retain every clean row while still visiting dirty
        // reactive descendants and the controller's current native request.
        if (
            ReferenceEquals(data, _renderedData)
            && first == _first
            && end == _end
            && _window is not null
        )
            return _window;
        var children = new List<Element>
        {
            new VStack().Height(data.Starts[first]).WithKey("before"),
        };
        var rows = _spareRows;
        rows.Clear();
        for (int i = first; i < end; i++)
        {
            var key = data.Keys[i];
            var row =
                _renderedData is not null
                && _rows.TryGetValue(key, out var previous)
                && _renderedData.Indices.TryGetValue(key, out var previousIndex)
                && EqualityComparer<T>.Default.Equals(
                    data.Items[i],
                    _renderedData.Items[previousIndex]
                )
                    ? previous
                    : new Row(data.Items[i], _builder);
            rows.Add(key, row);
            var revision = _measurementRevision;
            var extent = data.Starts[i + 1] - data.Starts[i];
            if (
                row.Container is null
                || (_automatic ? row.Revision != revision : row.Extent != extent)
            )
            {
                row.Container = _automatic
                    ? new MeasuredItem(
                        row,
                        revision,
                        (height, width) => Measure(revision, key, height, width)
                    )
                        .MinHeight(1)
                        .WithKey("row:" + key)
                    : new VStack(row).Height(extent).ClipToBounds(true).WithKey("row:" + key);
                row.Revision = revision;
                row.Extent = extent;
            }
            children.Add(row.Container);
        }
        _spareRows = _rows;
        _rows = rows;
        // Do not keep departed row descriptions (or their captured models)
        // alive in the spare dictionary between frames.
        _spareRows.Clear();
        _renderedData = data;
        _first = first;
        _end = end;
        children.Add(new VStack().Height(data.Starts[^1] - data.Starts[end]).WithKey("after"));
        var scroll = new ScrollView(new VStack(children.ToArray()))
        {
            Virtualized = true,
            Controller = Controller,
            NestedScrolling = NestedScrolling,
        };
        return _window = FillAvailableHeight
            ? scroll.Grow().Shrink().FlexBasis(0).MinHeight(0)
            : scroll.Height(Height);
    }
}
