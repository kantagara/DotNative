namespace FaliNamIgrac;

public interface IGamesApi
{
    Task<PlayerProfile> ProfileAsync(CancellationToken token);

    Task<IReadOnlyList<Game>> ListAsync(
        PlayerProfile user,
        int sport,
        int page,
        CancellationToken token
    );

    Task<IReadOnlyList<Game>> MyGamesAsync(
        PlayerProfile user,
        int sport,
        int page,
        bool passed,
        CancellationToken token
    );

    Task<GameDetails> DetailsAsync(string id, CancellationToken token);

    Task ApplyAsync(string id, string userId, CancellationToken token);
}
