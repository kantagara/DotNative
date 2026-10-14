using DotNative;
using DotNative.Plugins;
using DotNative.Preferences;
using DotNative.Toast;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public abstract class ScreenComponent(UiContext ui) : Component
{
    private IThemeData? _scopedTheme;
    private WindowInsets _insets;
    protected readonly UiContext ui = ui;
    protected IPresentationState presentation => ui.Presentation;

    // Renderer passes the Theme ancestor. Direct Build() calls (tests) use the controller.
    protected IThemeData theme => _scopedTheme ?? ui.Themes.Current.Value;
    protected IUiDispatcher dispatcher => ui.Dispatcher;
    protected PresentationTarget target => ui.Target;
    protected IPlatformChannels channels => ui.Channels;
    protected IPreferences preferences => ui.Preferences;
    protected IToast toast => ui.Toast;
    protected CancellationTokenSource lifetime => ui.Lifetime;
    protected bool disposed => lifetime.IsCancellationRequested;
    protected AnimationController loginAnimation => ui.LoginAnimation;
    protected AnimationController orbitAnimation => ui.OrbitAnimation;
    protected State<ImageSource?> busyImage => ui.BusyImage;
    protected Color Accent => theme.Primary;
    protected Color Ink => theme.Foreground;
    protected Color AuthWarning => new(238, 109, 109);
    protected Color AuthHint =>
        theme.Mode == ThemeMode.Dark ? new Color(197, 192, 204) : new Color(73, 69, 79);
    protected Color Background => theme.SecondaryBackground;

    protected AppLocalizations Loc => presentation.Text;
    protected WindowInsets Insets => _insets;

    protected TextStyle TextStyleFor(string face = "Regular") => FniChrome.Face(theme, face);

    public override Element Build(BuildContext context)
    {
        _scopedTheme = Theme.Of(context);
        if (context.TryGet(out WindowInsets insets))
            _insets = insets;
        if (context.TryGet(out ViewportSize viewport) && viewport.Width > 0 && viewport.Height > 0)
        {
            var size = (viewport.Width, viewport.Height);
            if (presentation.Dimensions.Value != size)
                dispatcher.Post(() =>
                {
                    if (!disposed)
                        presentation.Dimensions.Value = size;
                });
        }
        try
        {
            return Build();
        }
        finally
        {
            _scopedTheme = null;
        }
    }

    public virtual void Leave() { }

    protected void NavigateAuth(ApplicationScreen page) => ui.Router.Go(page);

    protected void OpenLogin() => ui.Router.OpenLogin();

    protected void OpenRegistration() => ui.Router.OpenRegistration();

    protected void OpenRecovery() => ui.Router.OpenRecovery();

    protected Task SocialSignInAsync(string provider) => ui.Router.SocialSignInAsync(provider);

    protected Task RetryStartupAsync() => ui.Router.RetryStartupAsync();

    protected Element Splash() => AppLoading(Loc.Loading);

    protected Element AppLoading(string message, bool showRetry = false)
    {
        if (presentation.Dimensions.Value is not { } dimensions)
            return new VStack(
                new Image(FniAssets.Image("splash.png"))
                    .Fit(ImageFit.Contain)
                    .Width(200)
                    .Height(200)
            )
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .BackgroundColor(theme.Background);
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        return new VStack([
            OrbitMotion.Build(
                dimensions.Width,
                dimensions.Height,
                orbitAnimation,
                presentation.OrbitDecoration.Value
            ),
            new VStack().Height(24 * h),
            new Text(message)
                .TextStyle(TextStyleFor())
                .FontSize(14 * Math.Min(w, h) * 1.7f)
                .TextAlignment(TextAlignment.Center)
                .ForegroundColor(Ink)
                .Padding(24 * w, 0),
            .. showRetry ? new Element[] { new VStack().Height(16 * h), new Button(
                        Loc.Retry,
                        () =>
                        {
                            presentation.StartupMessage.Value = Loc.RetryingAccountCheck;
                            _ = RetryStartupAsync();
                        }
                    ).BackgroundColor(Accent).ForegroundColor(theme.OnPrimary).CornerRadius(16 * Math.Min(w, h)).Height(56 * h) } : [],
        ])
            .MainAlignment(MainAxisAlignment.Center)
            .HeightPercent(100)
            .BackgroundColor(theme.Background);
    }

    protected Element SafeArea(Element content) => FniChrome.SafeArea(Insets, theme, content);

    protected Element AuthAppBar(Action back) => FniChrome.AppBar(target, theme, back);

    protected void ShowRecoveryToast(string message) => _ = PresentToastAsync(message);

    private async Task PresentToastAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        try
        {
            await toast.ShowAsync(message, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine("Toast: " + error.Message);
        }
    }

    protected Element SocialAuthenticationButtons(
        float w,
        float h,
        float r,
        Action google,
        Action apple
    ) => FniChrome.SocialButtons(theme, w, h, r, google, apple);

    protected Element BusyIndicator(Color? color = null) =>
        FniChrome.Busy(presentation, busyImage, orbitAnimation, color);

    protected Element RegistrationSubmit(
        string title,
        Action submit,
        bool enabled,
        bool busy,
        Element busyIndicator,
        float width,
        float w,
        float h,
        float r
    ) =>
        new VStack(
            new HStack(
                new Text(title)
                    .TextStyle(TextStyleFor("Medium"))
                    .FontSize(16 * w)
                    .FontWeight(FontWeight.Medium)
                    .ForegroundColor(theme.OnPrimary),
                busy
                    ? busyIndicator
                    : new Image(FniAssets.Image("icons/arrow_right.png"))
                        .Width(24 * w)
                        .Height(24 * h)
                        .TintColor(Color.White)
            )
                .Spacing(20 * w)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .Height(56 * h),
            new Button("", submit)
                .Enabled(enabled && !busy)
                .Width(width)
                .Height(56 * h)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Width(width)
            .Height(56 * h)
            .BackgroundColor(enabled || busy ? Accent : (theme.Primary with { A = 51 }))
            .CornerRadius(16 * r);
}
