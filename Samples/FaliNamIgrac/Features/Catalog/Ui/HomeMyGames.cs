using DotNative;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    private Element MyDates((float W, float H, float R) scale)
    {
        var view = catalog.State;
        var r = scale.R;
        Element content =
            catalog.Busy && view.MyGames is null ? Centered(Loc.Loading, r)
            : view.Message.Length > 0
                ? new VStack(
                    new Text(view.Message).ForegroundColor(theme.Error),
                    new Button(
                        Loc.Retry,
                        () => LoadMyGames(view.MySport, view.MyPage, view.MyPassed)
                    )
                )
                    .Spacing(12 * r)
                    .Grow()
            : view.MyGames is not { Count: > 0 }
                ? Centered(view.MyPassed ? Loc.NoFinishedGames : Loc.NoUpcomingGames, r)
            : myGames;
        return new VStack([
            new HStack(
                MyPeriod(Loc.Upcoming, false, view, r),
                MyPeriod(Loc.Finished, true, view, r)
            )
                .Padding(2 * r)
                .CornerRadius(100 * r)
                .BackgroundColor(Card(false)),
            Gap(24 * scale.H),
            Toolbar(view.MySport, scale),
            .. sheet.Value switch
            {
                Sheet.Sports => new Element[] { SportMenu(scale) },
                Sheet.Sort => new Element[] { SortMenu(scale) },
                Sheet.Filter => new Element[] { FilterMenu(scale) },
                _ => [],
            },
            Gap(24 * scale.H),
            content,
            .. view.MyPage > 1 || view.MyGames?.Count == 30 ? new Element[] { new HStack(new Button(
                            Loc.PreviousPage,
                            () =>
                            {
                                if (!catalog.Busy && view.MyPage > 1)
                                    LoadMyGames(view.MySport, view.MyPage - 1, view.MyPassed);
                            }
                        ).ForegroundColor(Ink).Opacity(view.MyPage > 1 && !catalog.Busy ? 1 : 0.3f), new Text(view.MyPage.ToString()).ForegroundColor(Ink).Grow().TextAlignment(TextAlignment.Center), new Button(
                            Loc.NextPage,
                            () =>
                            {
                                if (!catalog.Busy && view.MyGames?.Count == 30)
                                    LoadMyGames(view.MySport, view.MyPage + 1, view.MyPassed);
                            }
                        ).ForegroundColor(Ink).Opacity(view.MyGames?.Count == 30 && !catalog.Busy ? 1 : 0.3f)).Padding(8 * r) } : [],
        ]).Grow().Shrink().FlexBasis(0).MinHeight(0);
    }

    private Element MyPeriod(string text, bool passed, CatalogState view, float r) =>
        new Button(
            text,
            () =>
            {
                if (view.MyPassed != passed)
                    LoadMyGames(view.MySport, 1, passed);
            }
        )
            .TextAlignment(TextAlignment.Center)
            .TextStyle(TextStyleFor("Medium"))
            .FontSize(16 * r)
            .ForegroundColor(view.MyPassed == passed ? Color.White : Ink)
            .BackgroundColor(
                view.MyPassed == passed
                    ? theme.Mode == ThemeMode.Dark
                        ? new Color(128, 128, 128)
                        : Color.Black
                    : Color.Transparent
            )
            .CornerRadius(100 * r)
            .Height(48 * r)
            .Grow();

    private DateTime nextMyRefresh;

    private void LoadMyGames(int sport, int page, bool passed)
    {
        nextMyRefresh = DateTime.UtcNow.AddSeconds(30);
        _ = catalog.LoadMyGamesAsync(sport, page, passed, lifetime.Token);
    }
}
