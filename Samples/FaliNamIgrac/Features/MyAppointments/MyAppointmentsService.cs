using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FaliNamIgrac;

// Caller owns HttpClient. BaseAddress must be the same trusted API root as ApiClient.
// Separate transport is required because IApiClient has no DELETE or per-feature JSON metadata API.
public sealed class MyAppointmentsService(HttpClient http, ISessionService session)
    : IMyAppointmentsService
{
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? body,
        CancellationToken token
    )
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await session.AccessTokenAsync(token)
        );
        var response = await http.SendAsync(request, token);
        if (response.IsSuccessStatusCode)
            return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException("Appointment request failed.", null, status);
    }

    public async Task<AppointmentSnapshot> LoadAsync(
        string gameId,
        int userId,
        CancellationToken token
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        using var response = await SendAsync(
            HttpMethod.Get,
            "game-data/games/" + Uri.EscapeDataString(gameId),
            null,
            token
        );
        var data =
            await response.Content.ReadFromJsonAsync(
                AppointmentJson.Default.AppointmentEnvelope,
                token
            ) ?? throw new JsonException("Missing appointment.");
        var a = data.Appointment;
        if (
            a is null
            || a.Id != gameId
            || a.OrganizerId <= 0
            || a.SportId <= 0
            || a.Date == default
            || a.Start < TimeSpan.Zero
            || a.Start >= TimeSpan.FromDays(1)
            || a.End < TimeSpan.Zero
            || a.End >= TimeSpan.FromDays(1)
            || data.Players is null
            || data.Players.Any(p => p.Id <= 0 || p.Status is null)
            || data.Players.Select(p => p.Id).Distinct().Count() != data.Players.Length
        )
            throw new JsonException("Invalid appointment identity.");
        bool? reviewed = null;
        // Details omit each player's left_review; retrieve it from the actual user-games contract.
        if (data.Players.Any(p => p.Id == userId && p.Status == "ACCEPTED"))
        {
            for (var page = 1; page <= 10000; page++)
            {
                using var own = await SendAsync(
                    HttpMethod.Get,
                    $"game-data/user-games/{userId.ToString(CultureInfo.InvariantCulture)}?sportId={a.SportId}&appointmentsPassed=true&limit=200&page={page}",
                    null,
                    token
                );
                var rows =
                    await own.Content.ReadFromJsonAsync(
                        AppointmentJson.Default.AppointmentReviewDtoArray,
                        token
                    ) ?? throw new JsonException("Missing appointments.");
                if (rows.FirstOrDefault(r => r.Id == gameId) is { } match)
                {
                    reviewed = match.Reviewed;
                    break;
                }
                if (rows.Length < 200)
                    break;
            }
        }
        return new(
            a.Id,
            a.OrganizerId,
            a.Firstname + " " + a.Lastname,
            a.Date,
            a.Start,
            a.End,
            a.SportId,
            a.AdminReviewed,
            reviewed,
            data.Players.Select(p => new AppointmentPlayer(
                    p.Id,
                    p.Firstname + " " + p.Lastname,
                    p.Status
                ))
                .ToArray()
        );
    }

    public async Task ExecuteAsync(
        string gameId,
        int userId,
        AppointmentCommand command,
        CancellationToken token
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var id = Uri.EscapeDataString(gameId);
        var method = HttpMethod.Put;
        string path;
        HttpContent? body = null;
        switch (command.Action)
        {
            case AppointmentAction.Apply:
                method = HttpMethod.Post;
                path = $"game-data/applications/{id}";
                body = JsonContent.Create(
                    new AppointmentApplyRequest(userId.ToString(CultureInfo.InvariantCulture)),
                    AppointmentJson.Default.AppointmentApplyRequest
                );
                break;
            case AppointmentAction.Withdraw:
                method = HttpMethod.Delete;
                path = $"game-data/{id}/applications/{userId}";
                break;
            case AppointmentAction.Delete:
                method = HttpMethod.Delete;
                path = $"game-data/delete/{id}";
                break;
            case AppointmentAction.Rate:
                method = HttpMethod.Post;
                path = "reviews-data/ratings";
                body = JsonContent.Create(
                    new AppointmentRatingsRequest(userId, gameId, command.Ratings ?? []),
                    AppointmentJson.Default.AppointmentRatingsRequest
                );
                break;
            case AppointmentAction.Accept:
            case AppointmentAction.Reject:
            case AppointmentAction.Remove:
            case AppointmentAction.Ban:
                if (command.PlayerId is not > 0)
                    throw new ArgumentException("Player id required.");
                path = $"game-data/{id}/players/{command.PlayerId}";
                body = JsonContent.Create(
                    new AppointmentStatusRequest(
                        command.Action == AppointmentAction.Accept ? "ACCEPTED" : "REJECTED",
                        command.Action == AppointmentAction.Ban
                    ),
                    AppointmentJson.Default.AppointmentStatusRequest
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
        using var response = await SendAsync(method, path, body, deadline.Token);
    }
}
