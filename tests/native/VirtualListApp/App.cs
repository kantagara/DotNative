using DotNative;

namespace InteractionApp;

public static class Program
{
    public static void Main() => DotNativeApplication.CreateBuilder().Build().Run<App>();
}

public sealed class App : Component
{
    private int[] _items = Enumerable.Range(0, 10_000).ToArray();
    private readonly State<string> _status = new("Ready: 10000 rows");
    private readonly ListView<int> _list;
    private readonly State<bool> _expanded = new(false);
    private readonly State<int> _mutations = new(0);

    public App()
    {
        _list = new(
            _items,
            id =>
                new VStack(
                    new Button($"Row {id}", () => _status.Value = $"Selected {id}").Height(48),
                    new Text(
                        id == 5000 && _expanded.Value
                            ? "Expanded content\nSecond line\nThird line\nFourth line"
                            : ""
                    )
                ).Spacing(0),
            id => id.ToString()
        )
        {
            Height = 320,
            Overscan = 4,
        };
    }

    public override Element Build() => new VStack(
            new Text(_status.Value).FontSize(18),
            new HStack(
                new Button("Jump 5000", () => _list.ScrollToIndex(5000)).Padding(10),
                new Button("End", _list.ScrollToEnd).Padding(10)
            ),
            new HStack(
                new Button(
                    "Prepend",
                    () =>
                    {
                        _items = new[] { _items.Min() - 1 }.Concat(_items).ToArray();
                        _list.SetItems(_items);
                        _mutations.Value++;
                    }
                ).Padding(10),
                new Button(
                    "Grow",
                    () =>
                    {
                        _expanded.Value = !_expanded.Value;
                        _mutations.Value++;
                    }
                ).Padding(10),
                new Button(
                    "Reverse",
                    () =>
                    {
                        _items = _items.Reverse().ToArray();
                        _list.SetItems(_items);
                        _mutations.Value++;
                    }
                ).Padding(10),
                new Button(
                    "Clear",
                    () =>
                    {
                        _items = [];
                        _list.SetItems(_items);
                        _mutations.Value++;
                        _status.Value = "Empty";
                    }
                ).Padding(10)
            ),
            new Text($"Mutation {_mutations.Value}").Height(20),
            _list
        ).Spacing(8).Padding(16);
}
