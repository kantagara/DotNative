using System.Globalization;

namespace FaliNamIgrac;

public sealed class GamesApi(IApiClient backend, ISessionService session) : IGamesApi
{
    public async Task<PlayerProfile> ProfileAsync(CancellationToken token)
    {
        var response = await backend.GetAsync<ProfileQuery, ProfileResponse>(
            "user-data/userByEmail",
            new ProfileQuery(session.Email),
            token,
            authenticated: true
        );
        var user = response.UserData;
        return new(
            user.Id,
            user.Firstname ?? "igraču",
            user.Latitude,
            user.Longitude,
            DateTime.TryParse(
                user.BirthDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var birth
            )
                ? Age(birth)
                : throw new InvalidOperationException(
                    "Dopuni datum rođenja u postojećoj aplikaciji pre pretrage termina."
                )
        );
    }

    public async Task<IReadOnlyList<Game>> ListAsync(
        PlayerProfile user,
        int sport,
        int page,
        CancellationToken token
    )
    {
        var query = new GamesQuery(
            user.Id,
            user.Latitude,
            user.Longitude,
            user.Age,
            page,
            SportId: sport > 0 ? sport : null
        );
        var games = await backend.GetAsync<GamesQuery, GameResponse[]>(
            "game-data/games",
            query,
            token,
            authenticated: true
        );
        return games.Select(game => game.ToModel()).ToArray();
    }

    public async Task<IReadOnlyList<Game>> MyGamesAsync(
        PlayerProfile user,
        int sport,
        int page,
        bool passed,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        var games = await backend.GetAsync<MyGamesQuery, GameResponse[]>(
            "game-data/user-games/" + Uri.EscapeDataString(user.Id),
            new MyGamesQuery(sport, page, passed),
            token,
            authenticated: true
        );
        return games.Select(game => game.ToModel()).ToArray();
    }

    public async Task<GameDetails> DetailsAsync(string id, CancellationToken token)
    {
        var details = await backend.GetAsync<GameDetailsResponse>(
            "game-data/games/" + Uri.EscapeDataString(id),
            token,
            authenticated: true
        );
        return new(
            details.Appointment.ToModel(),
            details.Players.Select(player => player.Firstname + " " + player.Lastname).ToArray()
        )
        {
            Participants = details
                .Players.Select(p => new GameParticipant(
                    p.Id,
                    p.Firstname,
                    p.Lastname,
                    p.Status,
                    p.Photo,
                    p.Phone
                ))
                .ToArray(),
        };
    }

    public async Task ApplyAsync(string id, string userId, CancellationToken token)
    {
        using var response = await backend.PostJsonAsync(
            "game-data/applications/" + Uri.EscapeDataString(id),
            new GameApplicationRequest(userId),
            token,
            authenticated: true
        );
        if ((int)response.StatusCode is not (200 or 201))
            throw new InvalidOperationException(
                $"Prijava na termin nije uspela ({(int)response.StatusCode})."
            );
    }

    private static int Age(DateTime birth)
    {
        var years = DateTime.Today.Year - birth.Year;
        return birth.Date > DateTime.Today.AddYears(-years) ? years - 1 : years;
    }
}
