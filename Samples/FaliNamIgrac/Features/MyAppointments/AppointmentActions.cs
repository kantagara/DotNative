using DotNative;

namespace FaliNamIgrac;

// Agent 11 supplies localized strings; no generated localization files are edited here.
public interface IAppointmentText
{
    string Action(AppointmentAction action);
    string Status(AppointmentRole role);
    string Confirm(AppointmentAction action);
    string Loading { get; }
    string Retry { get; }
    string Cancel { get; }
    string ConfirmButton { get; }
    string Error(AppointmentProblem problem);
    string Edit { get; }
    string Chat { get; }
    string Deleted { get; }
    string Rated { get; }
    string Report => "Report";
}

public sealed record AppointmentNavigation(
    Action<string> EditGame,
    Action<string> OpenGameChat,
    Action<int, string>? ReportPlayer = null
);

public sealed class AppointmentActions(
    MyAppointmentsNotifier notifier,
    IAppointmentText text,
    AppointmentNavigation navigation,
    CancellationToken lifetime
) : Component
{
    public MyAppointmentsNotifier Model => notifier;
    public AppointmentNavigation Navigation => navigation;
    public IAppointmentText Labels => text;
    private sealed record Selection(
        AppointmentSnapshot? Details,
        IReadOnlyDictionary<int, int> Values
    );

    private readonly State<Selection> selection = new(new(null, new Dictionary<int, int>()));

    public bool IsFor(string gameId) => notifier.GameId == gameId;

    public override Element Build()
    {
        var state = notifier.State;
        var ratings = ReferenceEquals(selection.Value.Details, state.Details)
            ? selection.Value.Values
            : new Dictionary<int, int>();
        var rows = new List<Element>();
        if (state.Working)
            rows.Add(new Text(text.Loading));
        if (state.Deleted)
            rows.Add(new Text(text.Deleted));
        if (state.RatingsSubmitted)
            rows.Add(new Text(text.Rated));
        if (state.Problem != AppointmentProblem.None)
        {
            rows.Add(new Text(text.Error(state.Problem)));
            rows.Add(
                new Button(
                    text.Retry,
                    () =>
                    {
                        _ = notifier.ReloadAsync(lifetime);
                    }
                ).Enabled(!state.Working)
            );
        }
        if (state.Details is { } game)
        {
            var role = game.Role(notifier.UserId);
            rows.Add(new Text(text.Status(role)));
            void Add(AppointmentAction action, int? target = null)
            {
                if (!notifier.Allows(action, target))
                    return;
                rows.Add(
                    new Button(
                        text.Action(action),
                        () =>
                        {
                            var command = new AppointmentCommand(action, target);
                            if (command.NeedsConfirmation)
                                notifier.Request(command);
                            else
                                _ = notifier.ExecuteAsync(command, lifetime);
                        }
                    )
                );
            }
            if (notifier.Allows(AppointmentAction.Delete))
                rows.Add(new Button(text.Edit, () => navigation.EditGame(game.Id)));
            if (!state.Working && role is AppointmentRole.Organizer or AppointmentRole.Accepted)
                rows.Add(new Button(text.Chat, () => navigation.OpenGameChat(game.Id)));
            if (game.OrganizerId != notifier.UserId && navigation.ReportPlayer is not null)
                rows.Add(
                    new Button(
                        text.Report + ": " + game.OrganizerName,
                        () => navigation.ReportPlayer(game.OrganizerId, game.Id)
                    )
                );
            Add(AppointmentAction.Apply);
            Add(AppointmentAction.Withdraw);
            Add(AppointmentAction.Delete);
            foreach (var player in game.Players)
            {
                rows.Add(new Text(player.Name + " · " + text.Status(game.Role(player.Id))));
                if (player.Id != notifier.UserId && navigation.ReportPlayer is not null)
                    rows.Add(
                        new Button(text.Report, () => navigation.ReportPlayer(player.Id, game.Id))
                    );
                Add(AppointmentAction.Accept, player.Id);
                Add(AppointmentAction.Reject, player.Id);
                Add(AppointmentAction.Remove, player.Id);
                Add(AppointmentAction.Ban, player.Id);
            }
            if (notifier.Allows(AppointmentAction.Rate))
            {
                foreach (var player in game.RatingTargets(notifier.UserId))
                {
                    rows.Add(new Text(player.Name));
                    rows.Add(
                        new HStack(
                            Enumerable
                                .Range(1, 5)
                                .Select(value =>
                                    (Element)
                                        new Button(
                                            ratings.GetValueOrDefault(player.Id) == value
                                                ? $"[{value}]"
                                                : value.ToString(),
                                            () =>
                                            {
                                                selection.Value = new(
                                                    game,
                                                    new Dictionary<int, int>(ratings)
                                                    {
                                                        [player.Id] = value,
                                                    }
                                                );
                                            }
                                        )
                                )
                                .ToArray()
                        )
                    );
                }
                rows.Add(
                    new Button(
                        text.Action(AppointmentAction.Rate),
                        () =>
                            notifier.Request(
                                new(
                                    AppointmentAction.Rate,
                                    Ratings: ratings
                                        .Select(r => new AppointmentRating(r.Key, r.Value))
                                        .ToArray()
                                )
                            )
                    ).Enabled(ratings.Count > 0)
                );
            }
        }
        if (state.Confirmation is { } pending)
        {
            rows.Add(new Text(text.Confirm(pending.Action)));
            if (pending.PlayerId is { } target)
                rows.Add(
                    new Text(
                        state.Details?.Players.FirstOrDefault(p => p.Id == target)?.Name
                            ?? target.ToString()
                    )
                );
            if (pending.Ratings is { } selection)
                foreach (var rating in selection)
                    rows.Add(
                        new Text(
                            $"{state.Details?.RatingTargets(notifier.UserId).FirstOrDefault(p => p.Id == rating.RatedUserId)?.Name}: {rating.Rating}/5"
                        )
                    );
            rows.Add(
                new Button(
                    text.ConfirmButton,
                    () =>
                    {
                        _ = notifier.ConfirmAsync(lifetime);
                    }
                ).Enabled(!state.Working)
            );
            rows.Add(new Button(text.Cancel, notifier.CancelConfirmation).Enabled(!state.Working));
        }
        return new VStack(rows.ToArray()).Spacing(12);
    }
}
