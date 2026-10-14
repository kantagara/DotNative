using DotNative;
using DotNative.Plugins;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

public sealed class LandingScreen : ScreenComponent
{
    private readonly SettingsNotifier languageSettings;

    public LandingScreen(UiContext ui)
        : base(ui)
    {
        languageSettings = new(
            ui.Dispatcher,
            ui.Themes,
            ui.Presentation,
            (locale, token) => ui.Preferences.SetAsync("selected_local", locale, token)
        );
        languageSettings.Texts = () => new SettingsTexts(ui.Text.SettingsSaveFailed);
    }

    public override Element Build()
    {
        var landing = SafeArea(LandingContent());
        if (
            !presentation.LanguagePopup.Value || presentation.Dimensions.Value is not { } dimensions
        )
            return landing;
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var r = Math.Min(w, h);
        var panelWidth = dimensions.Width - 20 * r;
        var textWidth = panelWidth - 80 * r;
        Element Language(string name, string code, string image)
        {
            var selected = presentation.Locale.Value == code;
            return new VStack(
                new VStack(
                    new Image(FniAssets.Image("flags/" + image + ".png"))
                        .Fit(ImageFit.Cover)
                        .Width(46 * r)
                        .Height(46 * r)
                        .CornerRadius(23 * r),
                    new Button("", () => _ = SelectLanguageAsync(code))
                        .Width(50 * r)
                        .Height(50 * r)
                        .Position(PositionMode.Absolute)
                        .Left(0)
                        .Top(0)
                )
                    .Width(50 * r)
                    .Height(50 * r)
                    .Padding(2 * r)
                    .BackgroundColor(selected ? (theme.Primary with { A = 38 }) : theme.Surface)
                    .CornerRadius(25 * r),
                new VStack().Height(8 * h),
                new Text(name)
                    .TextStyle(TextStyleFor())
                    .FontSize(14 * r)
                    .FontWeight(selected ? FontWeight.SemiBold : FontWeight.Normal)
                    .TextAlignment(TextAlignment.Center)
            ).CrossAlignment(CrossAxisAlignment.Center);
        }
        var panel = new VStack(
            new Image(FniAssets.Image("images/languages.png"))
                .Fit(ImageFit.Contain)
                .Width(Math.Min(310 * w, textWidth))
                .Height(310 * h),
            new VStack().Height(16 * h),
            new Text(Loc.ChooseLanguage)
                .TextStyle(TextStyleFor())
                .FontSize(24 * r)
                .TextAlignment(TextAlignment.Center),
            new Text(Loc.LanguageBody)
                .TextStyle(TextStyleFor())
                .FontSize(14 * r)
                .TextAlignment(TextAlignment.Center),
            new VStack().Height(32 * h),
            new HStack(
                Language("Srpski", "sr", "serbia"),
                Language("English", "en", "uk"),
                Language("Русский", "ru", "ru")
            )
                .MainAlignment(MainAxisAlignment.SpaceEvenly)
                .Spacing(32 * w),
            new VStack().Height(32 * h),
            new Text(languageSettings.Busy ? Loc.Loading : languageSettings.State).ForegroundColor(
                Ink
            ),
            languageSettings.State.Length > 0 && !languageSettings.Busy
                ? new Button(Loc.Retry, () => _ = languageSettings.RetryAsync(lifetime.Token))
                : new VStack(),
            new Button(
                Loc.Apply,
                () =>
                {
                    if (!languageSettings.Busy && languageSettings.State.Length == 0)
                        presentation.LanguagePopup.Value = false;
                }
            ).TextStyle(TextStyleFor()).TextAlignment(TextAlignment.Center).FontWeight(FontWeight.ExtraBold).FontSize(16 * r).ForegroundColor(theme.OnPrimary).BackgroundColor(Accent).CornerRadius(16 * r).Height(56 * h).Width(textWidth)
        )
            .CrossAlignment(CrossAxisAlignment.Center)
            .Width(panelWidth)
            .Padding(24 + 16 * r, 16 + 16 * r, 24 + 16 * r, 24 + 16 * r)
            .BackgroundColor(theme.Surface)
            .CornerRadius(28);
        return new VStack(
            landing,
            new VStack(
                new ScrollView(panel).MaxHeight(
                    Math.Max(200, dimensions.Height - Insets.Top - Insets.Bottom - 32)
                )
            )
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center)
                .BackgroundColor(new Color(0, 0, 0, 138))
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0))
        )
            .HeightPercent(100)
            .BackgroundColor(theme.Background);
    }

    private Task SelectLanguageAsync(string code) =>
        languageSettings.SetLanguageAsync(code, lifetime.Token);

    private Element LandingContent()
    {
        if (presentation.Dimensions.Value is not { } dimensions)
            return Splash();
        var w = dimensions.Width / 375;
        var h = Math.Max(dimensions.Height, 700) / 812;
        var sp = Math.Min(w, h);
        return new ScrollView(
            new VStack(
                OrbitMotion.Build(
                    dimensions.Width,
                    dimensions.Height,
                    orbitAnimation,
                    presentation.OrbitDecoration.Value
                ),
                new VStack(
                    new Text(Loc.LandingTitle)
                        .TextStyle(TextStyleFor("Bold"))
                        .FontWeight(FontWeight.Bold)
                        .FontSize(32 * sp)
                        .TextAlignment(TextAlignment.Center)
                        .ForegroundColor(Ink),
                    new VStack().Height(8 * h),
                    new Text(Loc.LandingBody)
                        .TextStyle(TextStyleFor())
                        .FontSize(14 * sp)
                        .TextAlignment(TextAlignment.Center)
                        .ForegroundColor(theme.MutedForeground),
                    new VStack().Height(32 * h),
                    new VStack(
                        new HStack(
                            new Text(Loc.SignIn)
                                .TextStyle(TextStyleFor("ExtraBold"))
                                .FontWeight(FontWeight.ExtraBold)
                                .FontSize(16 * sp)
                                .ForegroundColor(theme.OnPrimary),
                            new Image(FniAssets.Image("icons/arrow_right.png"))
                                .Width(24 * w)
                                .Height(24 * h)
                                .TintColor(Color.White)
                        )
                            .Spacing(20 * w)
                            .MainAlignment(MainAxisAlignment.Center)
                            .CrossAlignment(CrossAxisAlignment.Center)
                            .Height(56 * h),
                        new Button("", OpenLogin)
                            .Height(56 * h)
                            .Width(dimensions.Width - 32 * w)
                            .Position(PositionMode.Absolute)
                            .Left(0)
                            .Top(0)
                    )
                        .Height(56 * h)
                        .BackgroundColor(Accent)
                        .CornerRadius(16 * sp),
                    new VStack().Height(24 * h),
                    new HStack(
                        new Text(Loc.DontHaveProfile + " ")
                            .TextStyle(TextStyleFor())
                            .FontSize(16 * sp)
                            .LineHeight(20 * h)
                            .ForegroundColor(theme.MutedForeground),
                        new VStack(
                            new Text(Loc.Register)
                                .TextStyle(TextStyleFor("ExtraBold"))
                                .FontWeight(FontWeight.ExtraBold)
                                .FontSize(16 * sp)
                                .LineHeight(20 * h)
                                .ForegroundColor(Accent),
                            new Button("", OpenRegistration)
                                .Position(PositionMode.Absolute)
                                .Insets(new(0, 0, 0, 0))
                        )
                    )
                        .MainAlignment(MainAxisAlignment.Center)
                        .CrossAlignment(CrossAxisAlignment.Center)
                )
                    .Padding(16 * w, 24 * h)
                    .Width(dimensions.Width)
            ).Width(dimensions.Width)
        )
            .HeightPercent(100)
            .BackgroundColor(theme.Background);
    }
}
