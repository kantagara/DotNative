using DotNative;

namespace FaliNamIgrac;

public enum ReportingLabel
{
    ReportTitle,
    Reason,
    Context,
    Send,
    Sent,
    Cancel,
    Retry,
    Loading,
    BannedTitle,
    TemporaryBan,
    PermanentBan,
    UnknownBan,
    Until,
    SignOut,
    ReportsTitle,
    Empty,
    Reporter,
    Reported,
    Pending,
    Approved,
    Dismissed,
    Revoked,
    UnknownStatus,
    Ban7,
    Ban30,
    Ban365,
    BanPermanent,
    Dismiss,
    Revoke,
    Unban,
    Confirm,
    ConfirmDecision,
    Applied,
    NoActiveBan,
    Refresh,
    CheckFailed,
    Back,
    PlayerProfile,
    Chat,
    GameDetails,
}

public interface IReportingText
{
    string Label(ReportingLabel label);
    string Error(ReportingIssue issue);
}

public abstract class ReportingScreen(UiContext ui, IReportingText text) : ScreenComponent(ui)
{
    protected string Error(ReportingIssue issue) => text.Error(issue);

    protected string L(ReportingLabel label) => text.Label(label);

    protected Element Line(string value) =>
        new Text(value).TextStyle(TextStyleFor()).ForegroundColor(Ink).FontSize(16);

    protected Element Action(string label, Action action, bool enabled = true) =>
        new Button(label, action)
            .TextStyle(TextStyleFor())
            .ForegroundColor(Accent)
            .Height(48)
            .Enabled(enabled);

    protected Element Page(IEnumerable<Element> elements) =>
        SafeArea(
            new ScrollView(new VStack(elements.ToArray()).Spacing(12).Padding(20))
                .HeightPercent(100)
                .BackgroundColor(theme.Background)
        );

    protected void Issue(List<Element> elements, ReportingIssue? issue)
    {
        if (issue is { } value)
            elements.Add(Line(text.Error(value)));
    }
}

/// <summary>Host owns route transitions. A fresh notifier/screen is required for every report entry.</summary>
public sealed class ReportUserScreen(
    UiContext ui,
    ReportUserNotifier report,
    IReportingText text,
    Action onClose
) : ReportingScreen(ui, text)
{
    public override void Leave() => report.Dispose();

    public override Element Build()
    {
        if (!report.IsCurrent)
            return Page([
                Line(Error(ReportingIssue.Unauthorized)),
                Action(
                    L(ReportingLabel.Back),
                    () =>
                    {
                        Leave();
                        onClose();
                    }
                ),
            ]);
        var state = report.State;
        var elements = new List<Element>
        {
            Line(L(ReportingLabel.ReportTitle)),
            Line(L(ReportingLabel.Reported) + ": " + report.Target.UserId),
        };
        if (state.Sent)
            elements.Add(Line(L(ReportingLabel.Sent)));
        else
        {
            elements.Add(Line(L(ReportingLabel.Reason)));
            elements.Add(
                new TextField(state.Reason, report.EditReason)
                {
                    Placeholder = L(ReportingLabel.Reason),
                    Input = new() { IsMultiline = true },
                }
                    .TextStyle(TextStyleFor())
                    .ForegroundColor(Ink)
                    .BackgroundColor(theme.Surface)
                    .Height(130)
                    .WithKey("report-reason")
            );
            elements.Add(
                Line(
                    L(
                        report.Target.Origin switch
                        {
                            ReportOrigin.Chat => ReportingLabel.Chat,
                            ReportOrigin.GameDetails => ReportingLabel.GameDetails,
                            _ => ReportingLabel.PlayerProfile,
                        }
                    ) + (report.Target.ContextId is { Length: > 0 } id ? ": " + id : "")
                )
            );
            elements.Add(Line(L(ReportingLabel.Context)));
            elements.Add(
                new TextField(state.Context, report.EditContext)
                {
                    Placeholder = L(ReportingLabel.Context),
                    Input = new() { IsMultiline = true },
                }
                    .TextStyle(TextStyleFor())
                    .ForegroundColor(Ink)
                    .BackgroundColor(theme.Surface)
                    .Height(100)
                    .WithKey("report-context")
            );
            elements.Add(
                Line(
                    $"{state.Reason.Trim().Length + (string.IsNullOrWhiteSpace(state.Context) ? 0 : state.Context.Trim().Length + 2)}/{ReportingRules.MaxReasonLength}"
                )
            );
            Issue(elements, state.Issue);
            elements.Add(
                Action(
                    L(
                        report.Busy ? ReportingLabel.Loading
                        : state.Issue is null ? ReportingLabel.Send
                        : ReportingLabel.Retry
                    ),
                    () => _ = report.SubmitAsync(),
                    !report.Busy
                        && report.IsCurrent
                        && state.Issue != ReportingIssue.AlreadyReported
                )
            );
        }
        elements.Add(
            Action(
                L(ReportingLabel.Back),
                () =>
                {
                    Leave();
                    onClose();
                }
            )
        );
        return Page(elements);
    }
}

/// <summary>Session-scoped guard survives this screen. Host handles logout; resume calls guard.RefreshAsync.</summary>
public sealed class ReportingBanScreen(
    UiContext ui,
    BanGuardNotifier guard,
    IReportingText text,
    Action signOut
) : ReportingScreen(ui, text)
{
    public override Element Build()
    {
        var state = guard.State;
        var elements = new List<Element> { Line(L(ReportingLabel.BannedTitle)) };
        if (state.Status?.Banned == true)
        {
            elements.Add(
                Line(
                    L(
                        state.Status.Reason switch
                        {
                            "permanent" => ReportingLabel.PermanentBan,
                            "temporary" => ReportingLabel.TemporaryBan,
                            _ => ReportingLabel.UnknownBan,
                        }
                    )
                )
            );
            if (
                DateTimeOffset.TryParse(
                    state.Status.UnbannedDate,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var until
                )
            )
                elements.Add(
                    Line(
                        L(ReportingLabel.Until)
                            + ": "
                            + until
                                .ToLocalTime()
                                .ToString(
                                    "g",
                                    System.Globalization.CultureInfo.GetCultureInfo(
                                        presentation.Locale.Value switch
                                        {
                                            "sr" => "sr-Latn-RS",
                                            "ru" => "ru-RU",
                                            _ => "en-US",
                                        }
                                    )
                                )
                    )
                );
        }
        Issue(elements, state.Issue);
        elements.Add(
            Action(
                L(guard.Busy ? ReportingLabel.Loading : ReportingLabel.Refresh),
                () => _ = guard.RefreshAsync(),
                !guard.Busy
            )
        );
        elements.Add(Action(L(ReportingLabel.SignOut), signOut));
        return Page(elements);
    }
}

public sealed class ModerationScreen(
    UiContext ui,
    ModerationNotifier moderation,
    IReportingText text,
    Action onClose,
    Action<int>? openPlayer = null,
    IApiClient? profiles = null
) : ReportingScreen(ui, text)
{
    private readonly CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(
        ui.Lifetime.Token
    );
    private readonly State<IReadOnlyDictionary<int, AccountData>> users = new(
        new Dictionary<int, AccountData>()
    );
    private readonly State<AccountData?> selected = new(null);
    private readonly State<bool> profileFailure = new(false);

    public async Task InitializeAsync()
    {
        if (
            !await moderation.LoadAsync(work.Token)
            || !moderation.State.IsAdmin
            || profiles is null
        )
            return;
        var ids = (moderation.State.Reports ?? [])
            .SelectMany(r => new[] { r.ReportingUserId, r.ReportedUserId })
            .Distinct()
            .ToArray();
        try
        {
            var loaded = new Dictionary<int, AccountData>();
            foreach (var batch in ids.Chunk(50))
            {
                var rows = await profiles.GetAsync<AccountResponse[]>(
                    "user-data/usersByIds?" + string.Join("&", batch.Select(id => "userIds=" + id)),
                    work.Token,
                    authenticated: true
                );
                foreach (var row in rows)
                    if (int.TryParse(row.UserData.Id, out var id))
                        loaded[id] = row.UserData;
            }
            await dispatcher.InvokeAsync(() =>
            {
                if (!work.IsCancellationRequested && moderation.State.IsAdmin)
                {
                    users.Value = loaded;
                    profileFailure.Value = false;
                }
            });
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (Exception)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (!work.IsCancellationRequested)
                    profileFailure.Value = true;
            });
        }
    }

    public override void Leave()
    {
        work.Cancel();
        moderation.Dispose();
    }

    private string Name(int id) =>
        users.Value.TryGetValue(id, out var user) ? Name(user) : "#" + id;

    private static string Name(AccountData user) =>
        string.IsNullOrWhiteSpace(user.Firstname + user.Lastname)
            ? "#" + user.Id
            : (user.Firstname + " " + user.Lastname).Trim();

    private void Open(int id)
    {
        if (openPlayer is not null)
            openPlayer(id);
        else if (users.Value.TryGetValue(id, out var user))
            selected.Value = user;
    }

    private Text Label(string value, float size = 14) =>
        new Text(value).TextStyle(TextStyleFor()).FontSize(size).ForegroundColor(Ink);

    private Element Avatar(AccountData? user, string name, float size)
    {
        Element content = ui.Images?.Get(MediaUrl.Profile(user?.Photo)) is { } source
            ? new Image(source).Width(size).Height(size).Fit(ImageFit.Cover).CornerRadius(size / 2)
            : Label(name.Length > 0 ? name[..1].ToUpperInvariant() : "?", size / 2);
        return new VStack(content)
            .Width(size)
            .Height(size)
            .CornerRadius(size / 2)
            .BackgroundColor(theme.Surface)
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);
    }

    private Element Chip(string label, Action tap, bool destructive = false) =>
        new Button(label, tap)
            .TextStyle(TextStyleFor())
            .FontSize(12)
            .TextAlignment(TextAlignment.Center)
            .ForegroundColor(destructive ? Color.White : Ink)
            .BackgroundColor(destructive ? theme.Error : theme.Surface)
            .Padding(12, 8)
            .MinHeight(40)
            .CornerRadius(12)
            .Grow()
            .Shrink()
            .FlexBasis(0)
            .Enabled(!moderation.Busy);

    private string Decision(ModerationDecision d) =>
        L(
            d.Action switch
            {
                ModerationAction.Dismiss => ReportingLabel.Dismiss,
                ModerationAction.Revoke => ReportingLabel.Revoke,
                ModerationAction.Unban => ReportingLabel.Unban,
                _ => d.Days switch
                {
                    7 => ReportingLabel.Ban7,
                    30 => ReportingLabel.Ban30,
                    365 => ReportingLabel.Ban365,
                    _ => ReportingLabel.BanPermanent,
                },
            }
        );

    private Element Tile(ModerationReport report)
    {
        users.Value.TryGetValue(report.ReportedUserId, out var user);
        var status =
            report.Pending ? Accent
            : report.Approved ? theme.Error
            : new Color(128, 128, 128);
        var rows = new List<Element>
        {
            new HStack(
                Avatar(user, Name(report.ReportedUserId), 44),
                new VStack(
                    new Button(Name(report.ReportedUserId), () => Open(report.ReportedUserId))
                        .TextStyle(TextStyleFor("Bold"))
                        .FontSize(16)
                        .ForegroundColor(Accent)
                        .TextAlignment(TextAlignment.Left),
                    Label(
                        "ID: " + report.ReportedUserId + " · " + L(ReportingLabel.PlayerProfile),
                        12
                    ),
                    user?.Rating is { } rating
                        ? Label(
                            $"★ {rating:0.0}"
                                + (
                                    user.GamesPlayed is { } games
                                        ? $" · {games} {Loc.AdminGamesPlayedLabel}"
                                        : ""
                                ),
                            12
                        )
                        : new VStack()
                )
                    .Spacing(4)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0),
                new Text(
                    L(
                        report.Status switch
                        {
                            "PENDING" => ReportingLabel.Pending,
                            "APPROVED" => ReportingLabel.Approved,
                            "DISMISSED" => ReportingLabel.Dismissed,
                            "REVOKED" => ReportingLabel.Revoked,
                            _ => ReportingLabel.UnknownStatus,
                        }
                    )
                ).TextStyle(TextStyleFor("Medium")).FontSize(12).ForegroundColor(status).Padding(8, 4).CornerRadius(8).BackgroundColor(theme.Surface)
            )
                .Spacing(12)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Button(
                L(ReportingLabel.Reporter)
                    + ": "
                    + Name(report.ReportingUserId)
                    + " (#"
                    + report.ReportingUserId
                    + ")",
                () => Open(report.ReportingUserId)
            )
                .TextStyle(TextStyleFor())
                .FontSize(12)
                .ForegroundColor(Accent)
                .TextAlignment(TextAlignment.Left)
                .MinHeight(32),
            report.CreatedAt is { } date
                ? Label(date.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), 12)
                : new VStack(),
            Label(report.Reason),
        };
        if (report.Pending)
        {
            rows.Add(
                new HStack(
                    Chip(
                        L(ReportingLabel.Ban7),
                        () => moderation.Ask(ModerationAction.Approve, report.Id, 7)
                    ),
                    Chip(
                        L(ReportingLabel.Ban30),
                        () => moderation.Ask(ModerationAction.Approve, report.Id, 30)
                    )
                ).Spacing(8)
            );
            rows.Add(
                new HStack(
                    Chip(
                        L(ReportingLabel.Ban365),
                        () => moderation.Ask(ModerationAction.Approve, report.Id, 365)
                    ),
                    Chip(
                        L(ReportingLabel.BanPermanent),
                        () => moderation.Ask(ModerationAction.Approve, report.Id, 0)
                    )
                ).Spacing(8)
            );
            rows.Add(
                Chip(
                    L(ReportingLabel.Dismiss),
                    () => moderation.Ask(ModerationAction.Dismiss, report.Id)
                )
            );
        }
        if (report.Approved)
            rows.Add(
                new HStack(
                    Chip(
                        L(ReportingLabel.Revoke),
                        () => moderation.Ask(ModerationAction.Revoke, report.Id),
                        true
                    ),
                    Chip(
                        L(ReportingLabel.Unban),
                        () => moderation.Ask(ModerationAction.Unban, report.Id)
                    )
                ).Spacing(8)
            );
        rows.Add(new VStack().Height(1).BackgroundColor(theme.Surface).Margin(0, 12));
        return new VStack(rows.ToArray()).Spacing(8).WithKey("report-" + report.Id);
    }

    public override Element Build()
    {
        var state = moderation.State;
        var body = new List<Element>();
        if (selected.Value is { } user && state.IsAdmin)
        {
            body.Add(
                new VStack(
                    Avatar(user, Name(user), 96),
                    Label(Name(user), 24),
                    Label(user.Nickname),
                    Label(user.City ?? ""),
                    Label(user.Bio ?? ""),
                    Label(user.Rating is { } rating ? $"★ {rating:0.0}" : ""),
                    Label(
                        user.GamesPlayed is { } games ? $"{games} {Loc.AdminGamesPlayedLabel}" : ""
                    ),
                    Label(
                        string.Join(
                            " · ",
                            user.Sports.Select(s =>
                                FniSports.Name(
                                    FniSports
                                        .All.FirstOrDefault(sport => sport.ApiName == s.SportName)
                                        .Id,
                                    Loc
                                )
                            )
                        )
                    )
                )
                    .Spacing(16)
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .Padding(16)
            );
        }
        else
        {
            Issue(body, state.Issue);
            if (moderation.Busy && !state.Loaded)
                body.Add(Label(L(ReportingLabel.Loading)).Padding(16, 80));
            if (state.Applied)
                body.Add(
                    Label(
                        L(
                            state.RevokedCount == 0
                                ? ReportingLabel.NoActiveBan
                                : ReportingLabel.Applied
                        )
                    )
                );
            if (state.IsAdmin && state.Loaded)
            {
                if (state.Reports is { Count: 0 })
                    body.Add(
                        Label(L(ReportingLabel.Empty))
                            .TextAlignment(TextAlignment.Center)
                            .Padding(16, 100)
                    );
                foreach (var report in state.Reports ?? [])
                    body.Add(Tile(report));
                if (profileFailure.Value)
                    body.Add(
                        Action(
                            L(ReportingLabel.Retry),
                            () => _ = InitializeAsync(),
                            !moderation.Busy
                        )
                    );
            }
        }
        var page = SafeArea(new VStack(new HStack(new Button(
                        "‹",
                        () =>
                        {
                            if (selected.Value is not null)
                                selected.Value = null;
                            else
                                onClose();
                        }
                    ).FontSize(32).ForegroundColor(Ink).Width(48).Height(56), new Text(L(selected.Value is null ? ReportingLabel.ReportsTitle : ReportingLabel.PlayerProfile)).TextStyle(TextStyleFor("Bold")).ForegroundColor(Ink).FontSize(22).Grow(), new Button("↻", () => _ = InitializeAsync()).FontSize(26).ForegroundColor(Accent).Width(48).Height(56).Enabled(!moderation.Busy)).CrossAlignment(CrossAxisAlignment.Center), new ScrollView(new VStack(body.ToArray()).Spacing(12).Padding(16, 12)).Grow().Shrink().FlexBasis(0).MinHeight(0)).HeightPercent(100));
        if (state.IsAdmin && state.Confirmation is { } decision)
            return new VStack(
                page,
                new VStack(
                    new Button("", moderation.CancelConfirmation)
                        .Position(PositionMode.Absolute)
                        .Insets(new(0, 0, 0, 0))
                        .BackgroundColor(new Color(0, 0, 0, 140)),
                    new VStack(
                        Label(L(ReportingLabel.ConfirmDecision), 20),
                        Label(Decision(decision) + " · " + Name(decision.UserId)),
                        new HStack(
                            Chip(L(ReportingLabel.Cancel), moderation.CancelConfirmation),
                            Chip(
                                L(ReportingLabel.Confirm),
                                () => _ = moderation.ConfirmAsync(),
                                true
                            )
                        ).Spacing(12)
                    )
                        .Spacing(20)
                        .Padding(24)
                        .BackgroundColor(Background)
                        .CornerRadius(20)
                        .WidthPercent(100)
                        .MaxWidth(420)
                )
                    .Padding(24)
                    .MainAlignment(MainAxisAlignment.Center)
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .WidthPercent(100)
                    .HeightPercent(100)
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Top(0)
            ).HeightPercent(100);
        return page;
    }
}
