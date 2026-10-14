namespace FrameworkBenchmarks;

public sealed record BenchItem(int Id, bool Expanded = false, int Version = 0)
{
    public string Title => $"Item {Id:00000} · {Version}";
    public string Detail =>
        Expanded ? "Expanded content\nSecond line\nThird line\nFourth line"
        : Id % 5 == 0 ? "Additional content"
        : "";
}

public static class Workload
{
    public const int Count = 10_000;
    public const string Ready = "Ready 10000 v4";
    public const int ViewportWidth = 320;
    public const int ViewportHeight = 320;
    public const int FixedRowHeight = 64;
    public const int TitleHeight = 48;
    public const int FontSize = 14;
    public const string Contract = "dotnative-maui-v4";

    // Controlled child extents keep the variable-height workload identical
    // across native font metrics. Multiples of eight also avoid fractional pixels
    // at the Pixel 7 density (2.625), where per-row vs cumulative rounding differs.
    // The containing rows still size automatically.
    public static int DetailHeight(BenchItem item, bool variable) =>
        !variable ? FixedRowHeight - TitleHeight
        : item.Expanded ? 80
        : item.Id % 5 == 0 ? 16
        : 0;

    public static BenchItem[] Initial() =>
        Enumerable.Range(0, Count).Select(id => new BenchItem(id)).ToArray();

    public static BenchItem[] Apply(string action, BenchItem[] items) =>
        action switch
        {
            "Reset" => Initial(),
            "Update" => items
                .Select(item => item.Id == 5000 ? item with { Version = item.Version + 1 } : item)
                .ToArray(),
            "Expand" => items
                .Select(item => item.Id == 5000 ? item with { Expanded = !item.Expanded } : item)
                .ToArray(),
            "Prepend" => Enumerable
                .Range(items.Length == 0 ? -100 : items.Min(item => item.Id) - 100, 100)
                .Select(id => new BenchItem(id))
                .Concat(items)
                .ToArray(),
            "Reverse" => items.Reverse().ToArray(),
            "Clear" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
}
