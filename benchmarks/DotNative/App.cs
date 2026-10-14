using DotNative;
using FrameworkBenchmarks;

namespace DotNativeBenchmark;

public static class Program
{
    public static void Main() => DotNativeApplication.CreateBuilder().Build().Run<BenchApp>();
}

public sealed class BenchApp : Component
{
    private BenchItem[] _items = Workload.Initial();
    private readonly State<string> _status = new(Workload.Ready);
    private readonly State<bool> _variable = new(false);
    private ListView<BenchItem>? _list;
    private int _generation;

    private ListView<BenchItem> CreateList(bool variable)
    {
        Node BuildRow(BenchItem item) =>
            new VStack(
                new Button(item.Title, () => _status.Value = $"Selected {item.Id}")
                    .FontSize(Workload.FontSize)
                    .Height(Workload.TitleHeight),
                new Text(variable ? item.Detail : "")
                    .FontSize(Workload.FontSize)
                    .Height(Workload.DetailHeight(item, variable))
            ).Spacing(0);
        return variable
            ? new ListView<BenchItem>(_items, BuildRow, item => item.Id.ToString())
            {
                Height = Workload.ViewportHeight,
                Overscan = 4,
            }
            : new ListView<BenchItem>(
                _items,
                BuildRow,
                item => item.Id.ToString(),
                Workload.FixedRowHeight
            )
            {
                Height = Workload.ViewportHeight,
                Overscan = 4,
            };
    }

    private void Apply(string action)
    {
        OperationProbe.Run(
            "dotnative",
            action,
            () =>
            {
                if (action == "Jump")
                {
                    if (_items.Length > 0)
                        _list!.ScrollToIndex(Math.Min(5000, _items.Length - 1));
                }
                else if (action == "End")
                    _list!.ScrollToEnd();
                else if (action == "Variable")
                {
                    _variable.Value = !_variable.Value;
                    _list = CreateList(_variable.Value);
                }
                else
                {
                    _items = Workload.Apply(action, _items);
                    _list!.SetItems(_items);
                }
            }
        );
        _status.Value = $"Done {++_generation} {action} {_items.Length}";
    }

    public override Element Build()
    {
        _list ??= CreateList(_variable.Value);
        return new Theme(
            ThemeData.Light,
            new VStack(
                new Text(_status.Value).FontSize(16).Height(32),
                new HStack(Buttons("Jump", "End", "Variable")).Height(40),
                new HStack(Buttons("Update", "Expand", "Prepend")).Height(40),
                new HStack(Buttons("Reverse", "Clear", "Reset")).Height(40),
                new Container(_list).Width(Workload.ViewportWidth)
            )
                .Spacing(4)
                .Padding(12)
                .BackgroundColor(Color.White)
        );
    }

    private Element[] Buttons(params string[] names) =>
        names
            .Select(name => (Element)new Button(name, () => Apply(name)).Width(100).Height(40))
            .ToArray();
}
