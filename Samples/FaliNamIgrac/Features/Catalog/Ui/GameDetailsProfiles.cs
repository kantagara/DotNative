using System.Globalization;
using DotNative;

namespace FaliNamIgrac;

public sealed partial class GameDetailsScreen
{
    public IApiClient? ProfileSource { get; set; }
    public Action<AccountData>? OpenPlayerChat { get; set; }
    private readonly State<int?> selectedPlayer = new(null);
    private readonly State<AccountData?> playerProfile = new(null);
    private readonly State<bool> profileError = new(false);
    private CancellationTokenSource? profileWork;
    private CancellationTokenSource? rosterWork;
    private readonly State<IReadOnlyDictionary<int, AccountData>> rosterProfiles = new(
        new Dictionary<int, AccountData>()
    );

    private void ClearProfiles()
    {
        profileWork?.Cancel();
        profileWork?.Dispose();
        profileWork = null;
        selectedPlayer.Value = null;
        playerProfile.Value = null;
        profileError.Value = false;
    }

    private void OpenPlayer(int id)
    {
        if (id <= 0)
            return;
        ClearProfiles();
        selectedPlayer.Value = id;
        profileWork = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = LoadPlayerAsync(id, profileWork.Token);
    }

    private async Task LoadPlayerAsync(int id, CancellationToken token)
    {
        try
        {
            if (ProfileSource is null)
                throw new InvalidOperationException();
            var rows = await ProfileSource.GetAsync<AccountResponse[]>(
                "user-data/usersByIds?userIds=" + id,
                token,
                authenticated: true
            );
            var profile =
                rows.FirstOrDefault(row =>
                    row.UserData.Id == id.ToString(CultureInfo.InvariantCulture)
                )?.UserData
                ?? throw new InvalidDataException();
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && selectedPlayer.Value == id)
                    playerProfile.Value = profile;
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && selectedPlayer.Value == id)
                    profileError.Value = true;
            });
        }
    }

    private Element ProfileActions(string game)
    {
        var id = selectedPlayer.Value;
        if (id is null || id == appointmentActions?.Model.UserId)
            return new VStack();
        return new HStack(
            new Button(
                Loc.ReportUser,
                () => appointmentActions?.Navigation.ReportPlayer?.Invoke(id.Value, game)
            )
                .TextStyle(TextStyleFor("Medium"))
                .FontSize(14 * W)
                .ForegroundColor(new Color(238, 109, 109))
                .Height(56),
            new Button(
                "\ue155",
                () =>
                {
                    if (playerProfile.Value is { } player)
                        OpenPlayerChat?.Invoke(player);
                }
            ).TextStyle(TextStyleFor("Icons")).FontSize(24).ForegroundColor(Ink).Width(48).Height(56).Enabled(playerProfile.Value is not null)
        );
    }

    private Element PlayerProfileView()
    {
        if (profileError.Value)
            return new VStack(
                Label(Loc.RequestFailed, 16),
                Link(
                    Loc.Retry,
                    () =>
                    {
                        if (selectedPlayer.Value is { } id)
                            OpenPlayer(id);
                    }
                )
            ).Padding(16 * R);
        if (playerProfile.Value is not { } user)
            return new VStack(Label(Loc.Loading, 16)).Padding(16 * R);
        var rows = new List<Element>
        {
            new HStack(
                Avatar((user.Firstname + " " + user.Lastname).Trim(), user.Photo, 65),
                new VStack(
                    Label((user.Firstname + " " + user.Lastname).Trim(), 24, "Bold"),
                    Gap(4),
                    Label(user.Username, 14, "Regular").ForegroundColor(theme.MutedForeground),
                    Gap(4),
                    new HStack(
                        Label(
                            (user.Rating ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                            13,
                            "ExtraBold"
                        ),
                        Stars(user.Rating ?? 0, 16)
                    ).Spacing(4 * W)
                )
                    .Grow()
                    .Shrink()
            )
                .Spacing(16 * W)
                .CrossAlignment(CrossAxisAlignment.Center),
            Gap(32),
            Label(Loc.Details, 24, "Bold"),
            Gap(24),
            Label(Loc.Biography, 22),
            Gap(8),
            Label(user.Bio ?? "-", 16, "Regular"),
            new VStack().Height(1).BackgroundColor(theme.MutedForeground),
            Gap(8),
            Label(Loc.NumberOfPlayedGames, 22),
            Gap(8),
            Label((user.GamesPlayed ?? 0).ToString()),
            Gap(32),
            Label(Loc.PlayerRatings, 22),
            Gap(24),
        };
        foreach (var rating in user.SportRatings)
        {
            var quality =
                rating.AverageRating >= 4.5 ? Loc.Excellent
                : rating.AverageRating > 3.5 ? Loc.Good
                : rating.AverageRating > 2.5 ? Loc.Average
                : rating.AverageRating > 0 ? Loc.BelowAverage
                : Loc.Excellent;
            rows.Add(
                new HStack(
                    Label(FniSports.Name(rating.SportId, Loc), 16).Grow(),
                    Label($"{quality} ({rating.NumberOfRatings} {Loc.Grades})", 16)
                )
            );
            rows.Add(Gap(8));
            rows.Add(Stars(rating.AverageRating, 32));
            rows.Add(Gap(16));
        }
        if (appointmentActions is not null && int.TryParse(user.Id, out var playerId))
            foreach (
                var action in new[]
                {
                    AppointmentAction.Accept,
                    AppointmentAction.Reject,
                    AppointmentAction.Remove,
                    AppointmentAction.Ban,
                }
            )
                if (appointmentActions.Model.Allows(action, playerId))
                    rows.Add(
                        Link(
                            appointmentActions.Labels.Action(action),
                            () => Execute(action, playerId)
                        )
                    );
        rows.Add(Feedback());
        return new VStack(rows.ToArray()).Padding(16 * W, 0);
    }

    private void LoadParticipantProfiles(IReadOnlyList<GameParticipant> participants)
    {
        rosterWork?.Cancel();
        rosterWork?.Dispose();
        rosterWork = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        rosterProfiles.Value = new Dictionary<int, AccountData>();
        _ = LoadRosterAsync(
            participants.Select(p => p.Id).Where(id => id > 0).Distinct().ToArray(),
            rosterWork.Token
        );
    }

    private async Task LoadRosterAsync(int[] ids, CancellationToken token)
    {
        if (ProfileSource is null)
            return;
        try
        {
            var loaded = new Dictionary<int, AccountData>();
            foreach (var batch in ids.Chunk(50))
            {
                var rows = await ProfileSource.GetAsync<AccountResponse[]>(
                    "user-data/usersByIds?" + string.Join("&", batch.Select(id => "userIds=" + id)),
                    token,
                    authenticated: true
                );
                foreach (var row in rows)
                    if (int.TryParse(row.UserData.Id, out var id))
                        loaded[id] = row.UserData;
            }
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested)
                    rosterProfiles.Value = loaded;
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        { /* Basic server-provided player information remains available for retry on reopening. */
        }
    }

    private Element ParticipantRating(int id) =>
        rosterProfiles.Value.TryGetValue(id, out var user)
            ? new HStack(
                Label(
                    (user.Rating ?? 0).ToString("0.0", CultureInfo.InvariantCulture),
                    13,
                    "ExtraBold"
                ),
                Stars(user.Rating ?? 0, 16)
            ).Spacing(4 * W)
            : new VStack();

    private Element Stars(double value, float size) =>
        new HStack(
            Enumerable
                .Range(0, 5)
                .Select(index => (Element)new Text(
                            index + 1 <= value ? "\ue5f9"
                            : index < value ? "\ue5fc"
                            : "\ue5fa"
                        ).TextStyle(TextStyleFor("Icons")).FontSize(size * R).ForegroundColor(new Color(255, 193, 7)))
                .ToArray()
        );
}
