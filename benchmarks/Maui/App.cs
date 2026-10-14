using System.Collections.ObjectModel;
using FrameworkBenchmarks;

namespace MauiBenchmark;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<BenchApp>().Build();
}

public sealed class BenchApp : Application
{
    public BenchApp() => UserAppTheme = AppTheme.Light;

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new BenchPage())
        {
            Title = "Framework Benchmark",
            Width = 640,
            Height = 720,
        };
}

public sealed class BenchPage : ContentPage
{
    private BenchItem[] _items = Workload.Initial();
    private ObservableCollection<BenchItem> _source = new();
    private readonly Label _status = new()
    {
        Text = Workload.Ready,
        FontSize = 16,
        HeightRequest = 32,
    };
    private readonly CollectionView _list = new()
    {
        HeightRequest = Workload.ViewportHeight,
        WidthRequest = Workload.ViewportWidth,
        HorizontalOptions = LayoutOptions.Start,
        SelectionMode = SelectionMode.None,
        ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepItemsInView,
    };
    private bool _variable;
    private int _generation;

    public BenchPage()
    {
        BackgroundColor = Colors.White;
        _status.TextColor = Colors.Black;
        ConfigureTemplate();
        _source = new(_items);
        _list.ItemsSource = _source;
        var layout = new VerticalStackLayout { Padding = 12, Spacing = 4 };
        layout.Add(_status);
        foreach (
            var names in new[]
            {
                new[] { "Jump", "End", "Variable" },
                new[] { "Update", "Expand", "Prepend" },
                new[] { "Reverse", "Clear", "Reset" },
            }
        )
        {
            var controls = new HorizontalStackLayout { HeightRequest = 40 };
            foreach (var name in names)
            {
                var button = new Button
                {
                    Text = name,
                    WidthRequest = 100,
                    HeightRequest = 40,
                    AutomationId = name,
                };
                button.Clicked += (_, _) => Apply(name);
                controls.Add(button);
            }
            layout.Add(controls);
        }
        layout.Add(_list);
        Content = layout;
    }

    private void ConfigureTemplate()
    {
        var variable = _variable;
        _list.ItemSizingStrategy = variable
            ? ItemSizingStrategy.MeasureAllItems
            : ItemSizingStrategy.MeasureFirstItem;
        _list.ItemTemplate = new DataTemplate(() =>
        {
            var title = new Button
            {
                HeightRequest = Workload.TitleHeight,
                FontSize = Workload.FontSize,
            };
            title.SetBinding(Button.TextProperty, nameof(BenchItem.Title));
            title.Clicked += (_, _) =>
            {
                if (title.BindingContext is BenchItem item)
                    _status.Text = $"Selected {item.Id}";
            };
            var detail = new Label
            {
                FontSize = Workload.FontSize,
                TextColor = Colors.Black,
                Text = "",
            };
            if (variable)
                detail.SetBinding(Label.TextProperty, nameof(BenchItem.Detail));
            detail.BindingContextChanged += (_, _) =>
            {
                if (detail.BindingContext is BenchItem item)
                    detail.HeightRequest = Workload.DetailHeight(item, variable);
            };
            var row = new VerticalStackLayout { Spacing = 0, Children = { title, detail } };
            if (!variable)
                row.HeightRequest = Workload.FixedRowHeight;
            return row;
        });
    }

    private void Apply(string action)
    {
        OperationProbe.Run(
            "maui",
            action,
            () =>
            {
                if (action == "Jump")
                {
                    if (_items.Length > 0)
                        _list.ScrollTo(
                            Math.Min(5000, _items.Length - 1),
                            position: ScrollToPosition.Start,
                            animate: false
                        );
                }
                else if (action == "End")
                {
                    if (_items.Length > 0)
                        _list.ScrollTo(
                            _items.Length - 1,
                            position: ScrollToPosition.End,
                            animate: false
                        );
                }
                else if (action == "Variable")
                {
                    _variable = !_variable;
                    ConfigureTemplate();
                }
                else
                {
                    _items = Workload.Apply(action, _items);
                    if (action is "Update" or "Expand")
                    {
                        var index = Array.FindIndex(_items, item => item.Id == 5000);
                        if (index >= 0)
                            _source[index] = _items[index];
                    }
                    else
                    {
                        _source = new(_items);
                        _list.ItemsSource = _source;
                    }
                }
            }
        );
        _status.Text = $"Done {++_generation} {action} {_items.Length}";
    }
}
