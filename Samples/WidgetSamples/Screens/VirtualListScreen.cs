using DotNative;

namespace WidgetSamples.Screens;

public sealed class VirtualListScreen : Component, INavigationScreen
{
    private int[] _items = Enumerable.Range(0, 10_000).ToArray();
    private readonly State<string> _status = new("10,000 items · only the visible window is built");
    private ListView<int>? _list;
    private readonly State<int> _expanded = new(-1);

    public ScreenOptions BuildOptions(BuildContext context) => new() { Title = "Virtual list" };

    public override Element Build(BuildContext context)
    {
        _list ??= new(
            _items,
            id =>
                new VStack(
                    new Button(
                        $"Item {id:00000} · tap to expand",
                        () =>
                        {
                            _expanded.Value = _expanded.Value == id ? -1 : id;
                            _status.Value = $"Selected item {id}";
                        }
                    ).Padding(12),
                    new Text(
                        _expanded.Value == id
                            ? "Automatically measured content.\nRows grow and shrink with their content.\nThe visible item keeps its position."
                        : id % 5 == 0 ? "A row with additional content."
                        : ""
                    )
                ).Spacing(4),
            id => id.ToString()
        )
        {
            Height = 400,
            Overscan = 8,
            CacheExtent = 1200,
            FillAvailableHeight = true,
        };
        return new VStack(
            new Text(_status.Value).FontSize(16),
            new HStack(
                new Button(
                    "Jump 5000",
                    () => _list.ScrollToIndex(Math.Min(5000, _items.Length - 1))
                ).Padding(10),
                new Button("End", _list.ScrollToEnd).Padding(10)
            ),
            new Button(
                "Prepend item; keep position",
                () =>
                {
                    _items = new[] { _items.Min() - 1 }.Concat(_items).ToArray();
                    _list.SetItems(_items);
                    _status.Value = $"{_items.Length:N0} items · anchor retained";
                }
            ).Padding(10),
            _list
        ).Spacing(12).Padding(16).Grow().Shrink().MinHeight(0);
    }
}
