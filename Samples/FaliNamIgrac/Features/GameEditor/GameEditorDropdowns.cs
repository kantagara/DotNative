using DotNative;

namespace FaliNamIgrac;

public sealed partial class GameEditorScreen
{
    public Func<IReadOnlyList<Sport>>? AvailableSports { get; set; }
    private readonly State<bool> choosingCurrency = new(false);

    private static uint Argb(Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    private Color MenuColor =>
        theme.Mode == ThemeMode.Dark ? new Color(22, 22, 22) : new Color(246, 246, 246);

    private Element SportImage(int id, float size)
    {
        var sport = FniSports.All.FirstOrDefault(s => s.Id == id);
        if (sport.Id == 0)
            return new VStack();
        var file = sport.ApiName == "e_games" ? "egames" : sport.ApiName;
        return new Image(FniAssets.Image("sports/" + file + ".png"))
            .Width(size)
            .Height(size)
            .Fit(ImageFit.Cover)
            .CornerRadius(size / 2);
    }

    private Element SportDropdown(GameEditorDraft draft, float r)
    {
        Element Label(string title) =>
            new Text(title)
                .TextStyle(TextStyleFor())
                .FontSize(16 * r)
                .ForegroundColor(Ink)
                .Grow()
                .Shrink()
                .FlexBasis(0)
                .MinWidth(0)
                .MaxLines(1);
        var arrow = new VStack(
            new Image(FniAssets.Image("icons/arrow_down.png"))
                .Width(12 * r)
                .Height(12 * r)
                .TintColor(Color.White)
        )
            .Width(48 * r)
            .Height(48 * r)
            .CornerRadius(14 * r)
            .BackgroundColor(
                theme.Mode == ThemeMode.Dark ? new Color(128, 128, 128) : new Color(38, 38, 38)
            )
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);
        var selected = new HStack(
            SportImage(draft.SportId, 20 * r),
            Label(draft.SportId > 0 ? FniSports.Name(draft.SportId, Loc) : Loc.ChooseSport),
            arrow
        )
            .Spacing(16 * r)
            .Padding(14 * r, 2 * r, 2 * r, 2 * r)
            .Height(56 * r)
            .CrossAlignment(CrossAxisAlignment.Center)
            .BackgroundColor(MenuColor)
            .CornerRadius(14 * r);
        var rows = new List<Element> { selected, new Button(
                "",
                () =>
                {
                    if (editor.GameId is null)
                    {
                        choosingSport.Value = !choosingSport.Value;
                        choosingCurrency.Value = false;
                    }
                }
            ).Enabled(editor.GameId is null && !editor.Busy).Position(PositionMode.Absolute).Left(0).Right(0).Top(0).Height(56 * r).WithKey("game-editor-sport-open") };
        if (choosingSport.Value)
        {
            var sports = (AvailableSports?.Invoke() ?? FniSports.All).OrderBy(s => s.Id).ToArray();
            rows.Add(new ScrollView(
                    new VStack(
                        sports
                            .Select(sport =>
                                (Element)
                                    new VStack(
                                        new HStack(
                                            SportImage(sport.Id, 20 * r),
                                            Label(FniSports.Name(sport.Id, Loc))
                                        )
                                            .Spacing(16 * r)
                                            .CrossAlignment(CrossAxisAlignment.Center)
                                            .Padding(14 * r, 8 * r),
                                        new Button(
                                            "",
                                            () =>
                                            {
                                                editor.Change(d =>
                                                    d with
                                                    {
                                                        SportId = sport.Id,
                                                        Venue =
                                                            sport.Id == 11
                                                                ? "outdoor"
                                                                : draft.Venue,
                                                        Terrain =
                                                            AllowedTerrains(sport.Id, draft.Venue)
                                                                .FirstOrDefault()
                                                            ?? "",
                                                    }
                                                );
                                                choosingSport.Value = false;
                                            }
                                        ).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0)).WithKey("game-editor-sport-" + sport.Id)
                                    ).Height(48 * r)
                            )
                            .ToArray()
                    )
                ).Height(Math.Min(240, sports.Length * 48) * r).BackgroundColor(MenuColor).CornerRadius(14 * r).Position(PositionMode.Absolute).Top(66 * r).Left(0).Right(0).ZIndex(30));
        }
        var popupExtent = choosingSport.Value
            ? (10 + Math.Min(240, (AvailableSports?.Invoke() ?? FniSports.All).Count * 48)) * r
            : 0;
        // Native hit testing requires the popup inside its parent's bounds. Negative margin
        // preserves the anchor and following fields while the parent contains the open menu.
        return new VStack(rows.ToArray())
            .Height(56 * r + popupExtent)
            .Margin(0, 0, 0, -popupExtent)
            .ClipToBounds(false)
            .ZIndex(choosingSport.Value ? 30 : 0);
    }

    private Element PriceDropdown(GameEditorDraft draft, float r)
    {
        var green = new Color(48, 172, 69);
        var rows = new List<Element> { new HStack(
                new Image(FniAssets.Image("icon/players.png"))
                    .Width(18 * r)
                    .Height(18 * r)
                    .TintColor(green),
                new TextField(draft.Price, value => editor.Change(d => d with { Price = value }))
                {
                    Placeholder = Loc.TotalPrice,
                    Input = new() { Keyboard = TextKeyboard.Number },
                }
                    .TextStyle(TextStyleFor())
                    .FontSize(16 * r)
                    .ForegroundColor(green)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
                    .Height(56 * r)
                    .WithKey("game-editor-Price"),
                new VStack(
                    new HStack(
                        new Text(draft.Currency)
                            .TextStyle(TextStyleFor())
                            .FontSize(16 * r)
                            .ForegroundColor(green),
                        new Image(FniAssets.Image("icons/arrow_down.png"))
                            .Width(12 * r)
                            .Height(12 * r)
                            .TintColor(green)
                    )
                        .Spacing(4 * r)
                        .MainAlignment(MainAxisAlignment.Center)
                        .CrossAlignment(CrossAxisAlignment.Center),
                    new Button(
                        "",
                        () =>
                        {
                            choosingCurrency.Value = !choosingCurrency.Value;
                            choosingSport.Value = false;
                        }
                    ).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0)).WithKey("game-editor-currency-open")
                )
                    .Width(74 * r)
                    .Height(56 * r)
                    .MainAlignment(MainAxisAlignment.Center)
            ).Spacing(12 * r).Padding(16 * r, 0, 4 * r, 0).Height(56 * r).BackgroundColor(new Color(210, 244, 206)).CornerRadius(16 * r).CrossAlignment(CrossAxisAlignment.Center).Position(PositionMode.Absolute).Left(0).Right(0).Bottom(0) };
        if (choosingCurrency.Value)
            rows.Add(new VStack(
                    new[] { "EUR", "RSD", "KM" }
                        .Select(currency => (Element)new Button(
                                    currency,
                                    () =>
                                    {
                                        editor.Change(d => d with { Currency = currency });
                                        choosingCurrency.Value = false;
                                    }
                                ).TextStyle(TextStyleFor()).FontSize(16 * r).ForegroundColor(draft.Currency == currency ? green : Ink).TextAlignment(TextAlignment.Left).Padding(14 * r, 0).Height(48 * r).WithKey("game-editor-currency-" + currency))
                        .ToArray()
                ).Width(86 * r).Height(144 * r).BackgroundColor(MenuColor).CornerRadius(14 * r).Position(PositionMode.Absolute).Right(0).Bottom(66 * r).ZIndex(30));
        var popupExtent = choosingCurrency.Value ? 154 * r : 0;
        return new VStack(rows.ToArray())
            .Height(56 * r + popupExtent)
            .Margin(0, -popupExtent, 0, 0)
            .ClipToBounds(false)
            .ZIndex(choosingCurrency.Value ? 30 : 0);
    }
}
