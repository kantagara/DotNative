namespace FaliNamIgrac;

public enum AppointmentRole
{
    Visitor,
    Pending,
    Accepted,
    Rejected,
    Organizer,
    Unknown,
}

public enum AppointmentAction
{
    Apply,
    Withdraw,
    Accept,
    Reject,
    Remove,
    Ban,
    Delete,
    Rate,
}

public sealed record AppointmentPlayer(int Id, string Name, string Status);

public sealed record AppointmentSnapshot(
    string Id,
    int OrganizerId,
    string OrganizerName,
    DateTime Date,
    TimeSpan Start,
    TimeSpan End,
    int SportId,
    bool AdminReviewed,
    bool? PlayerReviewed,
    IReadOnlyList<AppointmentPlayer> Players
)
{
    public AppointmentRole Role(int userId) =>
        userId == OrganizerId
            ? AppointmentRole.Organizer
            : Players.FirstOrDefault(p => p.Id == userId)?.Status switch
            {
                null => AppointmentRole.Visitor,
                "PENDING" => AppointmentRole.Pending,
                "ACCEPTED" => AppointmentRole.Accepted,
                "REJECTED" => AppointmentRole.Rejected,
                _ => AppointmentRole.Unknown,
            };

    // Match the reviews backend's appointment_date + end_time / Europe/Belgrade comparison.
    public bool Ended(DateTimeOffset now) =>
        Date.Date + End
        <= TimeZoneInfo
            .ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade"))
            .DateTime;

    public bool Started(DateTimeOffset now) =>
        Date.Date + Start
        <= TimeZoneInfo
            .ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade"))
            .DateTime;

    public IReadOnlyList<AppointmentPlayer> RatingTargets(int userId) =>
        Players
            .Where(p => p.Status == "ACCEPTED")
            .Append(new(OrganizerId, OrganizerName, "ACCEPTED"))
            .Where(p => p.Id != userId)
            .DistinctBy(p => p.Id)
            .ToArray();

    public bool Allows(AppointmentAction action, int userId, DateTimeOffset now, int? target = null)
    {
        if (userId <= 0)
            return false;
        var role = Role(userId);
        var player = Players.FirstOrDefault(p => p.Id == target);
        return action switch
        {
            AppointmentAction.Rate => Ended(now)
                && RatingTargets(userId).Count > 0
                && (
                    role == AppointmentRole.Organizer
                        ? !AdminReviewed
                        : role == AppointmentRole.Accepted && PlayerReviewed == false
                ),
            AppointmentAction.Apply => !Started(now) && role == AppointmentRole.Visitor,
            AppointmentAction.Withdraw => !Started(now)
                && role is AppointmentRole.Pending or AppointmentRole.Accepted,
            AppointmentAction.Delete => !Started(now) && role == AppointmentRole.Organizer,
            AppointmentAction.Accept => !Started(now)
                && role == AppointmentRole.Organizer
                && player?.Status == "PENDING"
                && target != OrganizerId,
            AppointmentAction.Reject => !Started(now)
                && role == AppointmentRole.Organizer
                && player?.Status == "PENDING"
                && target != OrganizerId,
            AppointmentAction.Remove => !Started(now)
                && role == AppointmentRole.Organizer
                && player?.Status == "ACCEPTED"
                && target != OrganizerId,
            AppointmentAction.Ban => !Started(now)
                && role == AppointmentRole.Organizer
                && player?.Status is "PENDING" or "ACCEPTED"
                && target != OrganizerId,
            _ => false,
        };
    }
}

public sealed record AppointmentRating(int RatedUserId, int Rating);

public sealed record AppointmentCommand(
    AppointmentAction Action,
    int? PlayerId = null,
    AppointmentRating[]? Ratings = null
)
{
    public bool NeedsConfirmation =>
        Action
            is AppointmentAction.Withdraw
                or AppointmentAction.Reject
                or AppointmentAction.Remove
                or AppointmentAction.Ban
                or AppointmentAction.Delete
                or AppointmentAction.Rate;
}

public interface IMyAppointmentsService
{
    Task<AppointmentSnapshot> LoadAsync(string gameId, int userId, CancellationToken token);
    Task ExecuteAsync(
        string gameId,
        int userId,
        AppointmentCommand command,
        CancellationToken token
    );
}

public sealed record AppointmentMutation(string GameId, int UserId, AppointmentAction Action);
