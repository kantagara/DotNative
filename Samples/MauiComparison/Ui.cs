using Microsoft.Maui.Controls.Shapes;

namespace MauiComparison;

internal static class Ui
{
    public static readonly Color Primary = Color.FromArgb("#345FE8");

    public static Label Text(string text, double size = 16)
    {
        var label = new Label { Text = text, FontSize = size };
        label.SetDynamicResource(Label.TextColorProperty, "Foreground");
        return label;
    }

    public static Label Bound(SampleStore store, string property, double size)
    {
        var label = Text("", size);
        label.SetBinding(Label.TextProperty, new Binding(property, source: store));
        return label;
    }

    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Padding = new Thickness(12),
            CornerRadius = 12,
            BackgroundColor = primary ? Primary : Colors.Transparent,
            TextColor = primary ? Colors.White : Primary,
            HorizontalOptions = LayoutOptions.Fill,
        };
        button.Clicked += (_, _) => action();
        return button;
    }

    public static VerticalStackLayout Stack(params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 16 };
        foreach (var child in children)
            stack.Children.Add(child);
        return stack;
    }

    public static Border Card(View content, double padding = 18)
    {
        var card = new Border
        {
            Content = content,
            Padding = padding,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        return card;
    }

    public static View Themes()
    {
        var group = new HorizontalStackLayout { Spacing = 16 };
        string groupName = Guid.NewGuid().ToString();
        foreach (
            var (title, theme) in new[]
            {
                ("System", AppTheme.Unspecified),
                ("Light", AppTheme.Light),
                ("Dark", AppTheme.Dark),
            }
        )
        {
            var radio = new RadioButton
            {
                Content = title,
                GroupName = groupName,
                IsChecked = Application.Current!.UserAppTheme == theme,
            };
            radio.SetDynamicResource(RadioButton.TextColorProperty, "Foreground");
            radio.CheckedChanged += (_, e) =>
            {
                if (e.Value)
                    ((App)Application.Current!).SetTheme(theme);
            };
            group.Children.Add(radio);
        }
        return Stack(Text("Appearance"), group);
    }

    public static Entry Input(string value, string placeholder)
    {
        var input = new Entry { Text = value, Placeholder = placeholder };
        input.SetDynamicResource(Entry.TextColorProperty, "Foreground");
        input.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        return input;
    }
}
