using Microsoft.Extensions.DependencyInjection;

namespace MauiComparison;

public sealed class HomePage : ContentPage
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Label _error = Ui.Text("");
    private bool _opening;

    public HomePage(SampleStore store, IServiceScopeFactory scopes)
    {
        _scopes = scopes;
        Title = "Widget samples";
        SetDynamicResource(BackgroundColorProperty, "Background");
        ToolbarItems.Add(new ToolbarItem("Add item", null, store.Add));
        ToolbarItems.Add(new ToolbarItem("Cart", null, OpenCart));
        var picked = Ui.Text("Choose a file using the system picker.", 14);
        var hide = Ui.Button("Hide navigation bar", () => { });
        hide.Clicked += (_, _) =>
        {
            bool show = !NavigationPage.GetHasNavigationBar(this);
            NavigationPage.SetHasNavigationBar(this, show);
            hide.Text = show ? "Hide navigation bar" : "Show navigation bar";
        };
        var content = Ui.Stack(
            _error,
            Ui.Text("MAUI", 12),
            Ui.Text(".NET DI ready · MAUI native navigation"),
            Ui.Themes(),
            Ui.Button(
                "Choose a file",
                async () =>
                {
                    try
                    {
                        var file = await FilePicker.Default.PickAsync();
                        if (file is null)
                        {
                            picked.Text = "Selection cancelled.";
                            return;
                        }
                        await using var stream = await file.OpenReadAsync();
                        var bytes = new byte[64 * 1024];
                        int count = await stream.ReadAsync(bytes);
                        picked.Text = $"{file.FileName} · read {count} bytes";
                    }
                    catch (Exception error)
                    {
                        picked.Text = error.Message;
                    }
                },
                true
            ),
            picked,
            hide,
            Ui.Card(
                Ui.Stack(
                    Ui.Text("One cart. Two screens. Two OS!", 22),
                    Ui.Bound(store, nameof(store.Summary), 26),
                    Ui.Text("Add items here or on the next screen. Both share the same cart.", 15),
                    Ui.Button("Add item", store.Add),
                    Ui.Button("Open cart →", OpenCart, true),
                    Ui.Bound(store, nameof(store.VisitSummary), 14)
                )
            ),
            Inputs(),
            LayoutSample(),
            ListSample()
        );
        content.Padding = 24;
        Content = new ScrollView { Content = content };
    }

    private async void OpenCart()
    {
        if (_opening)
            return;
        _opening = true;
        var scope = _scopes.CreateScope();
        try
        {
            var page = scope.ServiceProvider.GetRequiredService<CartPage>();
            page.OwnScope(scope);
            await Navigation.PushAsync(page, animated: true);
        }
        catch (Exception error)
        {
            scope.Dispose();
            _error.Text = error.Message;
        }
        finally
        {
            _opening = false;
        }
    }

    private static View Inputs()
    {
        var name = Ui.Input("DotNative", "Your name");
        var hello = Ui.Text("Hello, DotNative!");
        name.TextChanged += (_, e) => hello.Text = $"Hello, {e.NewTextValue}!";
        var enabled = new Switch { IsToggled = true };
        var notifications = Ui.Text("Notifications on");
        enabled.Toggled += (_, e) =>
            notifications.Text = e.Value ? "Notifications on" : "Notifications off";
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(notifications);
        row.Add(enabled, 1);
        var volume = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = .5,
        };
        var value = Ui.Text("Volume · 50 %");
        volume.ValueChanged += (_, e) => value.Text = $"Volume · {e.NewValue:P0}";
        return Ui.Stack(
            Ui.Text("Inputs", 22),
            hello,
            Ui.Card(name, 14),
            new HorizontalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    Ui.Button("Focus", () => name.Focus()),
                    Ui.Button("Dismiss", name.Unfocus),
                },
            },
            row,
            value,
            volume
        );
    }

    private static View LayoutSample()
    {
        var text = Ui.Text("A styled container around ordinary content.");
        var card = Ui.Card(text);
        card.BackgroundColor = Ui.Primary;
        text.TextColor = Colors.White;
        var row = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(Ui.Text("✓"));
        row.Add(Ui.Text("HStack + Spacer"), 1);
        row.Add(Ui.Text("★"), 2);
        return Ui.Stack(Ui.Text("Layout & composition", 22), row, card);
    }

    private static View ListSample()
    {
        var list = new CollectionView
        {
            HeightRequest = 240,
            SelectionMode = SelectionMode.None,
            ItemsSource = Enumerable.Range(1, 30).Select(i => $"Sample item {i:00}").ToArray(),
            ItemTemplate = new DataTemplate(() =>
            {
                var label = Ui.Text("");
                label.Margin = 12;
                label.SetBinding(Label.TextProperty, ".");
                return label;
            }),
        };
        return Ui.Stack(Ui.Text("Scrolling", 22), Ui.Text("Scroll inside this list.", 14), list);
    }
}
