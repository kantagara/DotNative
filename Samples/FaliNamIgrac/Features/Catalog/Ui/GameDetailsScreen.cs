using System.Globalization;
using DotNative;

namespace FaliNamIgrac;

public sealed partial class GameDetailsScreen(
    UiContext ui,
    CatalogNotifier catalog,
    AppointmentActions? appointmentActions = null
) : ScreenComponent(ui)
{
    public Func<Element>? Advertisement { get; set; }
    public Func<string, Element>? ExtraActions { get; set; }
    public Action<string>? ShareGame { get; set; }
    public Action<string>? ShowMap { get; set; }
    private GameDetails? lastDetails;
    private readonly State<string?> playersGame = new(null);
    private readonly State<string> playerTab = new("PENDING");
    private float W => (presentation.Dimensions.Value?.Width ?? 375) / 375;
    private float H => (presentation.Dimensions.Value?.Height ?? 812) / 812;
    private float R => Math.Min(W, H);
    private bool Dark => theme.Mode == ThemeMode.Dark;
    private Color Card => Dark ? new Color(22, 22, 22) : new Color(246, 246, 246);

    private Text Label(string value, float size = 18, string face = "Medium") =>
        new Text(value)
            .TextStyle(TextStyleFor(face))
            .ForegroundColor(size == 18 ? theme.MutedForeground : Ink)
            .FontSize(size * W);

    private Element Gap(float height) => new VStack().Height(height * H);

    private Element Link(string label, Action action) =>
        new Button(label, action)
            .TextStyle(TextStyleFor("ExtraBold"))
            .FontSize(16 * W)
            .ForegroundColor(Accent)
            .TextAlignment(TextAlignment.Start)
            .Height(36 * H);

    public override void Leave()
    {
        playersGame.Value = null;
        ratingsGame.Value = null;
        ClearProfiles();
        rosterWork?.Cancel();
        rosterWork?.Dispose();
        rosterWork = null;
        rosterProfiles.Value = new Dictionary<int, AccountData>();
        lastDetails = null;
    }

    public override Element Build()
    {
        if (catalog.State.Details is { } loaded)
            lastDetails = loaded;
        if (lastDetails is not { } details)
            return new VStack();
        var game = details.Game;
        var showPlayers = playersGame.Value == game.Id;
        var showRatings = ratingsGame.Value == game.Id;
        return SafeArea(
            new VStack(
                new HStack(
                    AuthAppBar(() =>
                    {
                        if (selectedPlayer.Value is not null)
                            selectedPlayer.Value = null;
                        else if (showRatings)
                            ratingsGame.Value = null;
                        else if (showPlayers)
                            playersGame.Value = null;
                        else
                            catalog.BackToList();
                    }),
                    new VStack().Grow(),
                    selectedPlayer.Value is not null ? ProfileActions(game.Id)
                        : showPlayers ? new VStack()
                        : new Button("\ue593", () => ShareGame?.Invoke(game.Id))
                            .TextStyle(TextStyleFor("Icons"))
                            .FontSize(24)
                            .ForegroundColor(Ink)
                            .Width(56)
                            .Height(56)
                ).Height(56),
                new ScrollView(
                    selectedPlayer.Value is not null ? PlayerProfileView()
                    : showRatings ? Ratings(game.Id)
                    : showPlayers ? Players(details)
                    : Contents(details)
                ).Grow().Shrink().FlexBasis(0).MinHeight(0)
            ).HeightPercent(100)
        );
    }

    private Element Contents(GameDetails details)
    {
        var game = details.Game;
        var state =
            appointmentActions?.IsFor(game.Id) == true ? appointmentActions.Model.State : null;
        var role =
            state?.Details?.Role(appointmentActions!.Model.UserId) ?? AppointmentRole.Visitor;
        var participants = Participants(details);
        var culture = CultureInfo.GetCultureInfo(
            presentation.Locale.Value switch
            {
                "en" => "en-GB",
                "ru" => "ru-RU",
                _ => "sr-Latn-RS",
            }
        );
        var info = new VStack(
            Label(FniSports.Name(game.SportId, Loc), 16, "Bold"),
            Gap(4),
            Label(game.Date.ToString("d. MMMM yyyy", culture), 13)
                .ForegroundColor(theme.MutedForeground),
            Gap(8),
            Label(Clock(game.Start) + " - " + Clock(game.End), 13)
                .ForegroundColor(theme.MutedForeground),
            Gap(4),
            new VStack(
                new HStack(
                    Avatar(game.Organizer, game.OrganizerPhoto, 24),
                    Label(game.Organizer, 13)
                )
                    .Spacing(4 * W)
                    .CrossAlignment(CrossAxisAlignment.Center),
                new Button("", () => OpenPlayer(game.OrganizerId))
                    .Position(PositionMode.Absolute)
                    .Insets(new(0, 0, 0, 0))
            )
        )
            .Padding(8 * R)
            .BackgroundColor(Dark ? new Color(37, 37, 37) : Color.White)
            .CornerRadius(12 * R)
            .Position(PositionMode.Absolute)
            .Left(16 * R)
            .Bottom(16 * R);
        var rows = new List<Element>
        {
            Label(Loc.Details, 24, "Bold"),
            Gap(8),
            Item(
                "icon/players.png",
                Loc.WhoArePlayers,
                new VStack(
                    Avatars(participants),
                    Gap(16),
                    Link(
                        Loc.PlayersView,
                        () =>
                        {
                            playerTab.Value = "PENDING";
                            playersGame.Value = game.Id;
                            LoadParticipantProfiles(participants);
                        }
                    )
                )
            ),
            Gap(16),
            Item("icon/ball.png", Loc.SportQuestion, Label(FniSports.Name(game.SportId, Loc))),
            Gap(16),
            Item(
                "icon/map_pin.png",
                Loc.Where,
                new VStack(
                    Label(game.Address),
                    Gap(8),
                    Link(Loc.SeeOnMap, () => ShowMap?.Invoke(game.Id))
                )
            ),
            ExtraActions?.Invoke(game.Id) ?? new VStack(),
            Gap(16),
            Item("icon/football_field.png", Loc.FieldType, Label(TerrainVenue(game))),
            Gap(16),
            Item(
                "icon/players.png",
                Loc.TotalPrice,
                new VStack(
                    Label($"{game.TotalPrice ?? game.Price * game.Total:0.##} {game.Currency}"),
                    Gap(4),
                    Label($"{game.Price} {game.Currency} {Loc.PricePerPlayer}", 14).Opacity(.8f)
                )
            ),
            Gap(16),
            Item(
                "icon/players.png",
                Loc.NumberOfPlayers,
                Label(
                    $"{game.Total} ({game.Needed - participants.Count(p => p.Status == "ACCEPTED")} {Loc.Missing})"
                )
            ),
            Gap(16),
        };
        if (game.Info is { } comment)
        {
            rows.Add(Label(Loc.Comment, 20));
            rows.Add(Gap(16));
            rows.Add(Label(comment));
            rows.Add(Gap(16));
        }
        if (role is AppointmentRole.Organizer or AppointmentRole.Accepted)
        {
            if (!string.IsNullOrWhiteSpace(game.OrganizerPhone))
            {
                rows.Add(new VStack().Height(1).BackgroundColor(theme.MutedForeground));
                rows.Add(Gap(16));
                rows.Add(Label(Loc.PhoneNumberOwner, 16));
                rows.Add(Gap(8));
                rows.Add(Label(game.OrganizerPhone, 16));
                rows.Add(Gap(16));
            }
            rows.Add(
                ActionTile(
                    Loc.GoToChat,
                    () => appointmentActions!.Navigation.OpenGameChat(game.Id),
                    false,
                    true
                )
            );
        }
        if (state is not null && !state.Deleted)
        {
            var action =
                role == AppointmentRole.Organizer ? AppointmentAction.Delete
                : role is AppointmentRole.Pending or AppointmentRole.Accepted
                    ? AppointmentAction.Withdraw
                : AppointmentAction.Apply;
            if (appointmentActions!.Model.Allows(action))
            {
                rows.Add(Gap(24));
                rows.Add(
                    ActionTile(
                        action == AppointmentAction.Delete ? Loc.CancelGame
                            : action == AppointmentAction.Withdraw ? Loc.LeaveGame
                            : Loc.SignInAppointment,
                        () => Execute(action),
                        action != AppointmentAction.Apply
                    )
                );
            }
            rows.Add(Feedback());
        }
        else if (state?.Deleted == true)
            rows.Add(Label(appointmentActions!.Labels.Deleted));
        else
            rows.Add(Label(Loc.Loading, 16));
        return new VStack(
            new VStack(Advertisement?.Invoke() ?? new VStack(), info)
                .Height(((presentation.Dimensions.Value?.Width ?? 375) - 32 * R) / 1.2f)
                .CornerRadius(16 * R),
            Gap(24),
            new VStack(rows.ToArray()).Padding(16 * R).BackgroundColor(Card).CornerRadius(16 * R)
        ).Padding(16 * R);
    }

    private Element Item(string icon, string title, Element child) =>
        new VStack(
            new HStack(
                new Image(FniAssets.Image(icon)).Width(16 * R).Height(16 * R).TintColor(Ink),
                Label(title, 20)
            )
                .Spacing(16 * W)
                .CrossAlignment(CrossAxisAlignment.Center),
            Gap(8),
            new VStack(child).Padding(32 * W, 0, 0, 0)
        );

    private Element Avatar(string name, string? photo, float size)
    {
        var image = ui.Images?.Get(MediaUrl.Profile(photo));
        return new VStack(
            image is not null
                ? new Image(image).Fit(ImageFit.Cover).Width(size * R).Height(size * R)
                : Label(
                    string.Concat(
                        name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Take(2)
                            .Select(s => s[0])
                    ),
                    size * .36f
                )
        )
            .Width(size * R)
            .Height(size * R)
            .CornerRadius(size * R / 2)
            .BackgroundColor(Dark ? new Color(62, 69, 121) : new Color(224, 224, 255))
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);
    }

    private Element Avatars(IReadOnlyList<GameParticipant> participants)
    {
        var visible = participants.Where(p => p.Status != "REJECTED").ToArray();
        if (visible.Length == 0)
            return new VStack();
        var avatars = visible
            .Take(4)
            .Select(
                (p, index) =>
                    (Element)
                        new VStack(Avatar(p.Name, p.Photo, 50))
                            .Position(PositionMode.Absolute)
                            .Left(index * 35 * R)
                            .Top(0)
            )
            .ToList();
        if (visible.Length > 4)
            avatars.Add(
                new VStack(Avatar("+ " + (visible.Length - 4), null, 50))
                    .Position(PositionMode.Absolute)
                    .Left(140 * R)
                    .Top(0)
            );
        return new VStack(avatars.ToArray()).Height(50 * R);
    }

    private IReadOnlyList<GameParticipant> Participants(GameDetails details)
    {
        if (
            appointmentActions?.IsFor(details.Game.Id) == true
            && appointmentActions.Model.State.Details is { } current
        )
            return current
                .Players.Select(p =>
                {
                    var metadata = details.Participants.FirstOrDefault(x => x.Id == p.Id);
                    return metadata is null
                        ? new GameParticipant(p.Id, p.Name, "", p.Status)
                        : metadata with
                        {
                            Status = p.Status,
                        };
                })
                .ToArray();
        return details.Participants;
    }

    private Element Players(GameDetails details)
    {
        var owner =
            appointmentActions?.IsFor(details.Game.Id) == true
            && appointmentActions.Model.State.Details?.Role(appointmentActions.Model.UserId)
                == AppointmentRole.Organizer;
        var tabs = owner
            ? new[] { "PENDING", "ACCEPTED", "REJECTED" }
            : new[] { "PENDING", "ACCEPTED" };
        var selected = tabs.Contains(playerTab.Value) ? playerTab.Value : "PENDING";
        string TabLabel(string key) =>
            key switch
            {
                "PENDING" => Loc.SignedIn,
                "ACCEPTED" => Loc.Accepted,
                _ => Loc.Rejected,
            };
        var rows = new List<Element> { Label(Loc.PlayersView, 32, "Bold"), Gap(24), new HStack(
                tabs.Select(key =>
                        (Element)
                            new Button(TabLabel(key), () => playerTab.Value = key)
                                .TextStyle(TextStyleFor("Medium"))
                                .FontSize(16 * W)
                                .Height(40 * H)
                                .Grow()
                                .FlexBasis(0)
                                .TextAlignment(TextAlignment.Center)
                                .CornerRadius(32 * R)
                                .ForegroundColor(
                                    selected == key ? Color.White
                                    : Dark ? new Color(128, 128, 128)
                                    : Color.Black
                                )
                                .BackgroundColor(
                                    selected == key
                                        ? Dark
                                            ? new Color(128, 128, 128)
                                            : Color.Black
                                        : Color.Transparent
                                )
                    )
                    .ToArray()
            ).Padding(2 * R).BackgroundColor(Card).CornerRadius(32 * R), Gap(24) };
        var players = Participants(details).Where(p => p.Status == selected).ToArray();
        if (players.Length == 0)
            rows.Add(
                Label(
                    selected switch
                    {
                        "PENDING" => Loc.NoSignedIn,
                        "ACCEPTED" => Loc.NoAccepted,
                        _ => Loc.NoRejected,
                    },
                    16
                )
            );
        foreach (var player in players)
        {
            var actions = new List<Element>();
            foreach (
                var action in new[]
                {
                    AppointmentAction.Accept,
                    AppointmentAction.Reject,
                    AppointmentAction.Remove,
                }
            )
                if (appointmentActions?.Model.Allows(action, player.Id) == true)
                    actions.Add(
                        Link(
                            appointmentActions.Labels.Action(action),
                            () => Execute(action, player.Id)
                        )
                    );
            rows.Add(
                new HStack(
                    Avatar(player.Name, player.Photo, 40),
                    new VStack(
                        Label(player.Name, 16),
                        ParticipantRating(player.Id),
                        new Button("", () => OpenPlayer(player.Id))
                            .Position(PositionMode.Absolute)
                            .Insets(new(0, 0, 0, 0))
                    )
                        .Grow()
                        .Shrink(),
                    new VStack(actions.ToArray())
                )
                    .Spacing(16 * W)
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .Padding(0, 8 * H)
            );
            rows.Add(new VStack().Height(1).BackgroundColor(Card));
        }
        rows.Add(Feedback());
        return new VStack(rows.ToArray()).Padding(16 * R, 0);
    }

    private void Execute(AppointmentAction action, int? player = null)
    {
        if (appointmentActions is null)
            return;
        var command = new AppointmentCommand(action, player);
        if (command.NeedsConfirmation)
            appointmentActions.Model.Request(command);
        else
            _ = appointmentActions.Model.ExecuteAsync(command, lifetime.Token);
    }

    private Element Feedback()
    {
        if (appointmentActions is null)
            return new VStack();
        var state = appointmentActions.Model.State;
        var rows = new List<Element>();
        if (state.Working)
            rows.Add(Label(Loc.Loading, 16));
        if (state.Problem != AppointmentProblem.None)
        {
            rows.Add(Label(appointmentActions.Labels.Error(state.Problem), 16));
            rows.Add(
                Link(Loc.Retry, () => _ = appointmentActions.Model.ReloadAsync(lifetime.Token))
            );
        }
        if (state.Confirmation is { } command)
        {
            rows.Add(Label(appointmentActions.Labels.Confirm(command.Action), 16));
            if (command.PlayerId is { } id)
                rows.Add(
                    Label(state.Details?.Players.FirstOrDefault(p => p.Id == id)?.Name ?? "", 16)
                );
            rows.Add(
                Link(
                    appointmentActions.Labels.ConfirmButton,
                    () => _ = appointmentActions.Model.ConfirmAsync(lifetime.Token)
                )
            );
            rows.Add(
                Link(appointmentActions.Labels.Cancel, appointmentActions.Model.CancelConfirmation)
            );
        }
        return new VStack(rows.ToArray()).Spacing(8 * H);
    }

    private Element ActionTile(string label, Action action, bool warning, bool chat = false)
    {
        var color =
            warning ? new Color(238, 109, 109)
            : chat ? Accent
            : Color.White;
        return new VStack(
            new HStack(
                chat
                    ? new Image(FniAssets.Image("icons/chat.png")).Width(54 * W).Height(54 * H)
                    : new VStack(),
                Label(label, 16).ForegroundColor(color),
                new Image(FniAssets.Image("icons/arrow_right.png"))
                    .Width(24 * W)
                    .Height(24 * H)
                    .TintColor(color)
            )
                .Spacing(20 * W)
                .MainAlignment(MainAxisAlignment.Center)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Button("", action)
                .Enabled(appointmentActions?.Model.State.Working != true)
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0))
        )
            .Height((chat ? 70 : 56) * H)
            .MainAlignment(MainAxisAlignment.Center)
            .CornerRadius(16 * R)
            .BackgroundColor(
                warning ? new Color(255, 226, 226)
                : chat ? new Color(230, 234, 254)
                : Accent
            );
    }

    private string Clock(string value)
    {
        var parts = value.Split(':');
        return Loc.TimeFormat(parts[0], parts.Length > 1 ? parts[1] : "00");
    }

    private string TerrainVenue(Game game) =>
        string.Join(
            " - ",
            new[]
            {
                game.Terrain switch
                {
                    "grass" => Loc.Grass,
                    "concrete" => Loc.Concrete,
                    "parquet" => Loc.Parquet,
                    "clinker" => Loc.Clinker,
                    "sand" => Loc.Sand,
                    _ => "",
                },
                game.Venue switch
                {
                    "indoor" => Loc.Indoor,
                    "outdoor" => Loc.Outdoor,
                    _ => "",
                },
            }.Where(s => s.Length > 0)
        );
}
