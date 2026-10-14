using DotNative;

namespace FaliNamIgrac;

public sealed partial class GameEditorScreen
{
    private static string[] AllowedTerrains(int sport, string venue) =>
        (sport, venue == "indoor") switch
        {
            (1, true) => ["parquet", "grass"],
            (1, false) => ["grass", "concrete"],
            (2, true) => ["parquet"],
            (2, false) => ["concrete"],
            (3, _) => ["concrete", "clinker", "grass"],
            (7, _) => ["concrete", "parquet", "clinker"],
            (6, true) => ["parquet"],
            (6, false) => ["sand", "concrete"],
            (8, _) => ["concrete", "parquet"],
            _ => [],
        };

    private Element? TerrainOptions(GameEditorDraft draft, float r)
    {
        var terrains = AllowedTerrains(draft.SportId, draft.Venue);
        if (terrains.Length == 0)
            return null;
        string Name(string terrain) =>
            terrain switch
            {
                "grass" => Loc.Grass,
                "concrete" => Loc.Concrete,
                "parquet" => Loc.Parquet,
                "clinker" => Loc.Clinker,
                _ => Loc.Sand,
            };
        return new VStack(
            new Text(Loc.FieldType)
                .TextStyle(TextStyleFor("Medium"))
                .FontSize(16 * r)
                .ForegroundColor(Ink),
            new HStack(
                terrains
                    .Select(terrain =>
                        (Element)
                            new VStack(
                                new HStack(
                                    new Text(draft.Terrain == terrain ? "\ue503" : "\ue504")
                                        .TextStyle(TextStyleFor("Icons"))
                                        .FontSize(22 * r)
                                        .ForegroundColor(Ink),
                                    new Text(Name(terrain))
                                        .TextStyle(TextStyleFor("Medium"))
                                        .FontSize(14 * r)
                                        .ForegroundColor(Ink)
                                )
                                    .Spacing(4 * r)
                                    .CrossAlignment(CrossAxisAlignment.Center),
                                new Button(
                                    "",
                                    () => editor.Change(d => d with { Terrain = terrain })
                                )
                                    .Position(PositionMode.Absolute)
                                    .Insets(new(0, 0, 0, 0))
                            )
                                .Height(40 * r)
                                .Grow()
                                .MainAlignment(MainAxisAlignment.Center)
                    )
                    .ToArray()
            )
        ).Spacing(8 * r);
    }

    private Element VenueTabs(GameEditorDraft draft, float r) => new HStack(
            new[] { "indoor", "outdoor" }
                .Select(venue =>
                    (Element)
                        new Button(
                            venue == "indoor" ? Loc.Indoor : Loc.Outdoor,
                            () =>
                                editor.Change(d =>
                                    d with
                                    {
                                        Venue = venue,
                                        Terrain =
                                            AllowedTerrains(d.SportId, venue).FirstOrDefault()
                                            ?? "",
                                    }
                                )
                        )
                            .TextStyle(TextStyleFor("Medium"))
                            .FontSize(16 * r)
                            .TextAlignment(TextAlignment.Center)
                            .Height(48 * r)
                            .CornerRadius(100 * r)
                            .Grow()
                            .FlexBasis(0)
                            .ForegroundColor(
                                draft.Venue == venue ? Color.White
                                : theme.Mode == ThemeMode.Dark ? new Color(128, 128, 128)
                                : Color.Black
                            )
                            .BackgroundColor(
                                draft.Venue == venue
                                    ? theme.Mode == ThemeMode.Dark
                                        ? new Color(128, 128, 128)
                                        : Color.Black
                                    : Color.Transparent
                            )
                )
                .ToArray()
        ).Padding(2 * r).BackgroundColor(theme.Surface).CornerRadius(100 * r);

    private Element PlayerCounters(GameEditorDraft draft, float r)
    {
        int.TryParse(draft.Total, out var total);
        int.TryParse(draft.Needed, out var needed);
        Element Counter(string label, int value, int minimum, int maximum, Action<int> change)
        {
            Element Step(string symbol, int delta) =>
                new Button(symbol, () => change(Math.Clamp(value + delta, minimum, maximum)))
                    .TextStyle(TextStyleFor())
                    .FontSize(22 * r)
                    .TextAlignment(TextAlignment.Center)
                    .ForegroundColor(theme.Background)
                    .BackgroundColor(Ink)
                    .Width(40 * r)
                    .Height(40 * r)
                    .CornerRadius(20 * r)
                    .Enabled(delta > 0 ? value < maximum : value > minimum);
            return new VStack(
                new Text(label)
                    .TextStyle(TextStyleFor("Medium"))
                    .FontSize(16 * r)
                    .ForegroundColor(Ink),
                new HStack(
                    Step("+", 1),
                    new Text(value.ToString())
                        .TextStyle(TextStyleFor("Medium"))
                        .FontSize(16 * r)
                        .ForegroundColor(Ink)
                        .Grow()
                        .TextAlignment(TextAlignment.Center),
                    Step("−", -1)
                )
                    .Height(56 * r)
                    .Width(140 * r)
                    .Padding(8 * r, 0)
                    .BackgroundColor(theme.Surface)
                    .CornerRadius(28 * r)
                    .CrossAlignment(CrossAxisAlignment.Center)
            )
                .Spacing(16 * r)
                .CrossAlignment(CrossAxisAlignment.Center);
        }
        return new HStack(
            Counter(
                Loc.TotalPlayers,
                total,
                needed,
                100,
                value => editor.Change(d => d with { Total = value.ToString() })
            ),
            new VStack().Grow(),
            Counter(
                Loc.MissingPlayers,
                needed,
                0,
                total,
                value => editor.Change(d => d with { Needed = value.ToString() })
            )
        );
    }
}
