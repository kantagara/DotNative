using System.Net.Http.Json;

namespace FaliNamIgrac;

public interface ICatalogSearchApi
{
    Task<IReadOnlyList<Game>> SearchAsync(
        PlayerProfile user,
        CatalogSearchQuery query,
        CancellationToken token
    );
}

public sealed class CatalogSearchApi(IApiClient backend) : ICatalogSearchApi
{
    public Task<IReadOnlyList<Game>> SearchAsync(
        PlayerProfile user,
        CatalogSearchQuery query,
        CancellationToken token
    ) => ReadAsync(query.Path(user), token);

    // sportId=0 asks the backend for one globally paginated, account-scoped result.
    public Task<IReadOnlyList<Game>> MyGamesAsync(
        PlayerProfile user,
        int sport = 0,
        int page = 1,
        bool passed = false,
        CancellationToken token = default
    )
    {
        new CatalogSearchQuery(Sport: sport, Page: page).Validate();
        return ReadAsync(MyPath(user.Id, sport, page, passed, 30), token);
    }

    private static string MyPath(string user, int sport, int page, bool passed, int limit) =>
        $"game-data/user-games/{Uri.EscapeDataString(user)}?sportId={sport}&page={page}&appointmentsPassed={(passed ? "true" : "false")}&limit={limit}";

    private async Task<IReadOnlyList<Game>> ReadAsync(string path, CancellationToken token)
    {
        using var response = await backend.GetAsync(path, token, authenticated: true);
        response.EnsureSuccessStatusCode();
        var rows =
            await response.Content.ReadFromJsonAsync(FilteringJson.Default.GameResponseArray, token)
            ?? throw new InvalidDataException("Missing games response.");
        token.ThrowIfCancellationRequested();
        return rows.Select(row => row.ToModel()).ToArray();
    }
}

public sealed class AllSportsCapacityException()
    : InvalidOperationException(
        "All-sports snapshot exceeds the supported capacity; select a sport."
    );
