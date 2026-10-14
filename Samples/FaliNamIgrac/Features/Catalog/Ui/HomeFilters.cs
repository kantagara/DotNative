using DotNative;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    private CatalogSearchNotifier? filterSearch;
    public FilterText FilterLabels { get; set; } = FilterText.Serbian;

    // Call once from composition/constructor and dispose notifier with the screen.
    public void ConnectFilters(CatalogSearchNotifier search)
    {
        if (filterSearch is not null)
            throw new InvalidOperationException("Filters already connected.");
        filterSearch = search;
        subscriptions.Add(
            search.Listen(() =>
            {
                var items = search.State.Games;
                feedAdIndices.Value = items
                    .Select((game, index) => (game.Id, index))
                    .Where(item => item.index % 5 == 0 && item.index < items.Count - 1)
                    .ToDictionary(item => item.Id, item => item.index / 5);
                games.SetItems(items);
            })
        );
    }

    private void OpenFilterSheet(Sheet next)
    {
        if (sheet.Value == Sheet.Filter)
            filterSearch?.CancelEdit();
        if (next == Sheet.Filter && sheet.Value != Sheet.Filter)
            filterSearch?.BeginEdit();
        Toggle(next);
    }

    private Element Toolbar(int sport, (float W, float H, float R) scale)
    {
        var (w, h, r) = scale;
        return new HStack(
            SportButton(
                    tab.Value == 0 && filterSearch is not null
                        ? filterSearch.State.Query.Sport
                        : sport,
                    scale
                )
                .Grow(),
            Gap(8 * w),
            Square("icon/sort.png", scale, () => OpenFilterSheet(Sheet.Sort)),
            Gap(8 * w),
            Square("icon/filter.png", scale, () => OpenFilterSheet(Sheet.Filter))
        ).Height(56 * h);
    }

    private HStack SportButton(int sport, (float W, float H, float R) scale)
    {
        var (w, _, r) = scale;
        var label = sport == 0 ? Loc.AllSports : FniSports.Name(sport, Loc);
        return new HStack([
            .. sport == 0 ? new Element[] { Icon("svg/all_sports.svg", 20 * r, 20 * r, null) }
            : SportIcon(sport) is { } icon
                ? new Element[]
                {
                    new Image(FniAssets.Image(icon))
                        .Fit(ImageFit.Cover)
                        .Width(20 * r)
                        .Height(20 * r)
                        .CornerRadius(10 * r),
                }
            : [],
            new Text(label).TextStyle(TextStyleFor()).FontSize(16 * r).ForegroundColor(Ink).Grow(),
            new Image(FniAssets.Image("icons/arrow_down.png"))
                .Width(16 * r)
                .Height(16 * r)
                .TintColor(Ink),
            new Button("", () => Toggle(Sheet.Sports))
                .HeightPercent(100)
                .WidthPercent(100)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0),
        ]).Spacing(12 * w).Padding(14 * w, 0).Height(56 * r).BackgroundColor(Card(false)).CornerRadius(14 * r).CrossAlignment(CrossAxisAlignment.Center);
    }

    private Element SportMenu((float W, float H, float R) scale)
    {
        return new ScrollView(
            new VStack([
                SportChoice(Loc.AllSports, 0, "svg/all_sports.svg", scale),
                .. FniSports.All.Select(sport =>
                    SportChoice(FniSports.Name(sport.Id, Loc), sport.Id, SportIcon(sport.Id), scale)
                ),
            ])
        )
            .Height(260 * scale.R)
            .BackgroundColor(Card(false))
            .CornerRadius(14 * scale.R);
    }

    private Element SportChoice(
        string label,
        int id,
        string? icon,
        (float W, float H, float R) scale
    )
    {
        var (w, _, r) = scale;
        return new HStack([
            .. icon is not null ? new Element[] { Icon(icon, 20 * r) } : [],
            new Text(label).TextStyle(TextStyleFor()).FontSize(16 * r).ForegroundColor(Ink).Grow(),
            new Button(
                "",
                () =>
                {
                    if (tab.Value == 0 && filterSearch is { } search)
                    {
                        search.CancelEdit();
                        sheet.Value = Sheet.None;
                        _ = search.ChangeAsync(
                            search.State.Query with
                            {
                                Sport = id,
                            },
                            lifetime.Token
                        );
                    }
                    else
                        ChooseSport(id);
                }
            ).WidthPercent(100).HeightPercent(100).Position(PositionMode.Absolute).Left(0).Top(0),
        ])
            .Spacing(16 * w)
            .Height(44 * r)
            .Padding(14 * w, 0)
            .CrossAlignment(CrossAxisAlignment.Center);
    }

    private Element SortMenu((float W, float H, float R) scale)
    {
        if (tab.Value == 1)
            return FilterNote(FilterLabels.MyGamesLimit, scale.R);
        if (filterSearch is not { } search)
            return FilterNote(FilterLabels.Unavailable, scale.R);
        var values = new[]
        {
            CatalogOrder.Closest,
            CatalogOrder.PriceAscending,
            CatalogOrder.PriceDescending,
            CatalogOrder.TimeAscending,
            CatalogOrder.TimeDescending,
        };
        var labels = new[]
        {
            Loc.ClosestLocation,
            Loc.PriceUp,
            Loc.PriceDown,
            Loc.TimeClosest,
            Loc.TimeLatest,
        };
        return new VStack(
            values
                .Select(
                    (value, index) =>
                        Choice(
                            labels[index],
                            value == search.State.Query.Order,
                            scale.R,
                            () =>
                            {
                                sheet.Value = Sheet.None;
                                _ = search.ChangeAsync(
                                    search.State.Query with
                                    {
                                        Order = value,
                                    },
                                    lifetime.Token
                                );
                            }
                        )
                )
                .ToArray()
        ).Padding(14 * scale.W, 12 * scale.R).BackgroundColor(Card(false)).CornerRadius(14 * scale.R);
    }

    private Element FilterMenu((float W, float H, float R) scale)
    {
        var r = scale.R;
        if (tab.Value == 1)
            return FilterNote(FilterLabels.MyGamesLimit, r);
        if (filterSearch is not { } search)
            return FilterNote(FilterLabels.Unavailable, r);
        var view = search.State;
        var draft = view.Draft;
        return new ScrollView(new VStack(
                new Text(Loc.Filter)
                    .TextStyle(TextStyleFor("Bold"))
                    .FontSize(16 * r)
                    .ForegroundColor(Ink),
                FilterInput(
                    FilterLabels.MinPrice,
                    draft.MinPrice,
                    value => search.Edit(search.State.Draft with { MinPrice = value }),
                    r
                ),
                FilterInput(
                    FilterLabels.MaxPrice,
                    draft.MaxPrice,
                    value => search.Edit(search.State.Draft with { MaxPrice = value }),
                    r
                ),
                FilterInput(
                    FilterLabels.DistanceKm,
                    draft.DistanceKm,
                    value => search.Edit(search.State.Draft with { DistanceKm = value }),
                    r
                ),
                FilterNote(
                    view.InvalidDraft ? FilterLabels.InvalidRange : FilterLabels.DistanceHint,
                    r
                ),
                new HStack(
                    new Button(
                        FilterLabels.Apply,
                        () =>
                        {
                            _ = search.ApplyAsync(lifetime.Token);
                            if (!search.State.InvalidDraft)
                                sheet.Value = Sheet.None;
                        }
                    ),
                    new Button(FilterLabels.Reset, search.ResetDraft),
                    new Button(
                        FilterLabels.Cancel,
                        () =>
                        {
                            search.CancelEdit();
                            sheet.Value = Sheet.None;
                        }
                    )
                ).Spacing(8 * r)
            ).Spacing(8 * r).Padding(14 * scale.W, 12 * r)).Height(340 * r).BackgroundColor(Card(false)).CornerRadius(14 * r);
    }

    private Element FilterInput(string label, string value, Action<string> change, float r) =>
        new VStack(
            FilterNote(label, r),
            new TextField(value, change).Height(44 * r).ForegroundColor(Ink)
        ).Spacing(4 * r);

    private Element FilterNote(string text, float r) =>
        new Text(text).TextStyle(TextStyleFor()).FontSize(14 * r).ForegroundColor(Ink);

    // Integrator uses this in Available instead of the legacy CatalogState list/loading block.
    private Element FilteredResults((float W, float H, float R) scale)
    {
        if (filterSearch is not { } search)
            return FilterNote(FilterLabels.Unavailable, scale.R);
        var view = search.State;
        if (view.Loading)
            return Centered(Loc.Loading, scale.R);
        if (view.Error is { } error)
            return new VStack(
                FilterNote(FniErrors.UserMessage(presentation.Text, error), scale.R),
                new Button(FilterLabels.Retry, () => _ = search.RetryAsync(lifetime.Token))
            ).Spacing(8 * scale.R);
        return new VStack(
            view.Games.Count == 0 ? Centered(Loc.NoGames, scale.R) : games,
            new HStack([
                .. view.Query.Page > 1
                    ? new Element[]
                    {
                        new Button(
                            FilterLabels.Previous,
                            () => _ = search.PreviousAsync(lifetime.Token)
                        ),
                    }
                    : [],
                .. view.HasMore
                    ? new Element[]
                    {
                        new Button(FilterLabels.Next, () => _ = search.NextAsync(lifetime.Token)),
                    }
                    : [],
            ]).Spacing(12 * scale.R)
        ).Grow();
    }

    private Element Choice(string label, bool selected, float r, Action choose) =>
        new VStack(
            new Text(label)
                .TextStyle(TextStyleFor(selected ? "Bold" : "Regular"))
                .FontSize(16 * r)
                .ForegroundColor(selected ? Accent : Ink),
            new Button("", choose)
                .WidthPercent(100)
                .HeightPercent(100)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Height(40 * r)
            .MainAlignment(MainAxisAlignment.Center);

    private Element Square(string asset, (float W, float H, float R) scale, Action tap) =>
        IconButton(asset, 56 * scale.R, tap)
            .BackgroundColor(Card(false))
            .CornerRadius(14 * scale.R);
}
