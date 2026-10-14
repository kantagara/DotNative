using Microsoft.Extensions.DependencyInjection;

namespace MauiComparison;

public sealed class App : Application
{
    private readonly IServiceProvider _services;
    private NavigationPage? _navigation;

    public App(IServiceProvider services)
    {
        _services = services;
        ApplyTheme();
        RequestedThemeChanged += (_, _) => ApplyTheme();
    }

    public void SetTheme(AppTheme theme)
    {
        UserAppTheme = theme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        bool dark =
            (UserAppTheme == AppTheme.Unspecified ? RequestedTheme : UserAppTheme) == AppTheme.Dark;
        Resources["Background"] = Color.FromArgb(dark ? "#18191D" : "#FFFFFF");
        Resources["Foreground"] = Color.FromArgb(dark ? "#FFFFFF" : "#000000");
        Resources["Surface"] = Color.FromArgb(dark ? "#2D2F36" : "#F2F4F8");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _navigation = new NavigationPage(_services.GetRequiredService<HomePage>());
        _navigation.SetDynamicResource(NavigationPage.BarBackgroundColorProperty, "Background");
        _navigation.SetDynamicResource(NavigationPage.BarTextColorProperty, "Foreground");
        _navigation.Popped += (_, e) => (e.Page as CartPage)?.ReleaseScope();
        var window = new Window(_navigation)
        {
            Title = "Widget Samples — MAUI (Release)",
            Width = 640,
            Height = 720,
        };
        window.Destroying += (_, _) =>
        {
            foreach (var page in _navigation.Navigation.NavigationStack.OfType<CartPage>())
                page.ReleaseScope();
        };
        return window;
    }
}
