namespace DotNative;

public enum BottomNavigationIcon
{
    Home,
    Search,
    Favorites,
    Profile,
    Settings,
}

public sealed record BottomNavigationBarItem(
    string Label,
    BottomNavigationIcon Icon = BottomNavigationIcon.Home
);

/// <summary>A real UIKit UITabBar / Android Material BottomNavigationView. Mobile only.</summary>
public sealed record BottomNavigationBar : Node, ILayoutable, IVisualBox, IInteractive
{
    public IReadOnlyList<BottomNavigationBarItem> Items { get; }
    public int CurrentIndex { get; }
    public Action<int> OnTap { get; }

    public BottomNavigationBar(
        IEnumerable<BottomNavigationBarItem> items,
        int currentIndex,
        Action<int> onTap
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (copy.Length is < 2 or > 5)
            throw new ArgumentOutOfRangeException(
                nameof(items),
                "Native bottom navigation supports 2–5 items."
            );
        foreach (var item in copy)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrWhiteSpace(item.Label) || !Enum.IsDefined(item.Icon))
                throw new ArgumentException("Invalid navigation item.", nameof(items));
        }
        if (currentIndex < 0 || currentIndex >= copy.Length)
            throw new ArgumentOutOfRangeException(nameof(currentIndex));
        Items = Array.AsReadOnly(copy);
        CurrentIndex = currentIndex;
        OnTap = onTap ?? throw new ArgumentNullException(nameof(onTap));
    }
}
