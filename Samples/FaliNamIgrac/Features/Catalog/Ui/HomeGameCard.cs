using System.Globalization;
using DotNative;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    public Func<int, Element>? FeedAd { get; set; }
    private readonly State<IReadOnlyDictionary<string, int>> feedAdIndices = new(
        new Dictionary<string, int>()
    );

    private Node GameCard(Game game) => GameCard(game, false);

    private Node MyGameCard(Game game) => GameCard(game, true);

    private Node GameCard(Game game, bool owned)
    {
        _ = ui.Themes.Current.Value;
        var r = Scale().R;
        var w = Scale().W;
        var dark = theme.Mode == ThemeMode.Dark;
        return new VStack(
            new VStack(
                new HStack(DateColumn(game, r), Gap(10 * w), Details(game, r, dark, owned))
                    .Padding(16 * r)
                    .BackgroundColor(dark ? theme.Background : Color.White)
                    .CornerRadius(24 * r),
                new Button(
                    "",
                    () => _ = catalog.OpenDetailsAsync(game.Id, lifetime.Token, canApply: !owned)
                )
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0))
                    .WithKey("open-game-" + game.Id)
            ),
            !owned && feedAdIndices.Value.TryGetValue(game.Id, out var adSlot)
                ? FeedAd?.Invoke(adSlot) ?? new VStack()
                : new VStack()
        ).Padding(0, 0, 0, 24 * Scale().H);
    }

    private Element DateColumn(Game game, float r)
    {
        var culture = Culture();
        return new VStack(
            new Image(FniAssets.Image("icon/clock.png"))
                .Width(12 * r)
                .Height(12 * r)
                .TintColor(Ink),
            new Text(game.Date.ToString("dddd", culture).ToUpper(culture))
                .TextStyle(TextStyleFor("Medium"))
                .FontSize(14 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center),
            new Text(game.Date.ToString("d.MM", culture).ToUpper(culture) + ".")
                .TextStyle(TextStyleFor("Bold"))
                .FontSize(24 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center),
            new Text(Clock(game.Start))
                .TextStyle(TextStyleFor("Bold"))
                .FontSize(24 * r)
                .ForegroundColor(Ink)
                .TextAlignment(TextAlignment.Center),
            Gap(16 * r),
            new Image(FniAssets.Image(Thumbnail(game.SportId)))
                .Fit(ImageFit.Cover)
                .Width(72 * r)
                .Height(83 * r)
                .CornerRadius(8 * r)
        )
            .CrossAlignment(CrossAxisAlignment.Center)
            .Width(80 * r)
            .Spacing(4 * r);
    }

    private Element Details(Game game, float r, bool dark, bool owned)
    {
        var sport = FniSports.Name(game.SportId, presentation.Text);
        return new VStack(
            new VStack(
                Detail(SportIcon(game.SportId), sport, r, true),
                Detail("icon/location.png", game.Address, r),
                Detail(
                    "icon/players.png",
                    $"{game.Total} ({Math.Max(0, game.Needed)} {Loc.Missing})",
                    r
                ),
                Detail("icon/clock.png", Duration(game), r),
                TerrainVenue(game) is { Length: > 0 } venue
                    ? Detail("icon/football_field.png", venue, r)
                    : new VStack(),
                Price(game, r)
            )
                .Spacing(8 * r)
                .Padding(16 * r)
                .BackgroundColor(dark ? theme.SecondaryBackground : theme.Surface)
                .CornerRadius(16 * r),
            Gap(8 * r),
            Join(game, r, dark, owned)
        )
            .Grow()
            .Shrink()
            .FlexBasis(0)
            .MinWidth(0);
    }

    private Element Detail(string? icon, string text, float r, bool sport = false)
    {
        var mark = icon is null
            ? Gap(24 * r)
            : new Image(FniAssets.Image(icon))
                .Width((sport ? 24 : 15) * r)
                .Height((sport ? 24 : 15) * r)
                .Fit(ImageFit.Contain)
                .TintColor(sport ? null : Color.White);
        return new HStack(
            new VStack(mark)
                .Width(24 * r)
                .Height(24 * r)
                .CornerRadius(12 * r)
                .BackgroundColor(theme.Mode == ThemeMode.Dark ? Color.Transparent : Color.Black)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center),
            Gap(4 * r),
            new Text(text)
                .TextStyle(TextStyleFor("Medium"))
                .FontSize(14 * r)
                .ForegroundColor(Ink)
                .TextOverflow(TextOverflow.Wrap)
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinWidth(0)
        ).CrossAlignment(CrossAxisAlignment.Center);
    }

    private Element Price(Game game, float r)
    {
        var green = new Color(48, 172, 69);
        return new HStack([
            .. game.TotalPrice is { } total
                ? new Element[]
                {
                    new Image(FniAssets.Image("icon/players.png"))
                        .Width(16 * r)
                        .Height(16 * r)
                        .TintColor(green),
                    new Text($"{total:0.##} {game.Currency}")
                        .TextStyle(TextStyleFor("Medium"))
                        .FontSize(14 * r)
                        .ForegroundColor(green),
                    Gap(3 * r),
                }
                : [],
            new Image(FniAssets.Image("icon/player.png"))
                .Width(16 * r)
                .Height(16 * r)
                .TintColor(green),
            new Text($"{game.Price} {game.Currency}")
                .TextStyle(TextStyleFor("Medium"))
                .FontSize(14 * r)
                .ForegroundColor(green),
        ])
            .Spacing(4 * r)
            .Padding(12 * r, 4 * r)
            .Height(32 * r)
            .BackgroundColor(new Color(210, 244, 206))
            .CornerRadius(16 * r)
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);
    }

    private string TerrainVenue(Game game)
    {
        var terrain = game.Terrain switch
        {
            "grass" => Loc.Grass,
            "concrete" => Loc.Concrete,
            "parquet" => Loc.Parquet,
            "clinker" => Loc.Clinker,
            "sand" => Loc.Sand,
            _ => "",
        };
        var venue = game.Venue switch
        {
            "indoor" => Loc.Indoor,
            "outdoor" => Loc.Outdoor,
            _ => "",
        };
        return string.Join(" - ", new[] { terrain, venue }.Where(value => value.Length > 0));
    }

    private Element Join(Game game, float r, bool dark, bool owned) =>
        new VStack(
            new HStack(
                new Text(owned ? Loc.Details : Loc.SignInAppointment)
                    .TextStyle(TextStyleFor("Medium"))
                    .FontSize(16 * r)
                    .ForegroundColor(Color.White),
                new Image(FniAssets.Image("icons/arrow_right.png"))
                    .Width(24 * r)
                    .Height(24 * r)
                    .TintColor(Color.White)
            )
                .Spacing(20 * r)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Button(
                "",
                () => _ = catalog.OpenDetailsAsync(game.Id, lifetime.Token, canApply: !owned)
            )
                .WidthPercent(100)
                .HeightPercent(100)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Height(36 * r)
            .BackgroundColor(dark ? new Color(37, 37, 37) : Color.Black)
            .CornerRadius(18 * r)
            .MainAlignment(MainAxisAlignment.Center);

    private string Clock(string time)
    {
        var parts = time.Split(':');
        var hour = parts.Length > 0 ? parts[0] : time;
        var minute = parts.Length > 1 ? parts[1] : "00";
        return Loc.TimeFormat(hour, minute);
    }

    private string Duration(Game game)
    {
        if (
            !TimeOnly.TryParse(game.Start, out var start)
            || !TimeOnly.TryParse(game.End, out var end)
        )
            return "";
        var minutes = (int)(end - start).TotalMinutes;
        if (minutes <= 0)
            return "";
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours > 0 && rest > 0)
            return Clock($"{hours}:{rest:00}");
        return hours > 0 ? hours + Loc.Hour : rest + Loc.Minutes;
    }

    private CultureInfo Culture() =>
        CultureInfo.GetCultureInfo(
            presentation.Locale.Value switch
            {
                "en" => "en-GB",
                "ru" => "ru-RU",
                _ => "sr-Latn-RS",
            }
        );

    private static string Thumbnail(int id) =>
        id switch
        {
            1 => "thumbnails/football_one.jpeg",
            2 => "thumbnails/basketball_parquete_one.jpeg",
            3 => "thumbnails/tennis_outdoor_grass_one.jpeg",
            4 => "thumbnails/table_tennis.jpeg",
            5 => "thumbnails/badmintonOutdoorOne.jpeg",
            6 => "thumbnails/volleyball_concrete_one.jpeg",
            7 => "thumbnails/padelOutdoor.jpeg",
            8 => "thumbnails/handballOutdoorOne.jpeg",
            9 => "thumbnails/running_1.jpeg",
            10 => "thumbnails/cycling_1.jpeg",
            11 => "thumbnails/hiking_1.jpeg",
            12 => "thumbnails/swimming_1.jpeg",
            13 => "thumbnails/egames_1.jpeg",
            14 => "thumbnails/board_games_1.jpeg",
            _ => "thumbnails/football_one.jpeg",
        };
}
