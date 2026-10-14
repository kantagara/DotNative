using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FaliNamIgrac;

public interface IGameEditorApi
{
    Task<GameEditorAppointment> LoadAsync(string id, CancellationToken token);
    Task<string> CreateAsync(GameEditorCreate request, CancellationToken token);
    Task UpdateAsync(string id, GameEditorUpdate request, CancellationToken token);
}

public sealed class GameEditorException(GameEditorIssue issue, int? status = null)
    : Exception(issue.ToString())
{
    public GameEditorIssue Issue { get; } = issue;
    public int? Status { get; } = status;
}

/// <summary>Caller owns HttpClient. BaseAddress must end with '/'. Uses its own AOT JSON context.</summary>
public sealed class GameEditorApi(
    HttpClient http,
    ISessionService session,
    Func<HttpRequestMessage, CancellationToken, Task> authorizeCreate
) : IGameEditorApi
{
    public async Task<GameEditorAppointment> LoadAsync(string id, CancellationToken token)
    {
        using var response = await Send(
            HttpMethod.Get,
            "game-data/games/" + Uri.EscapeDataString(id),
            null,
            token
        );
        var details = await response.Content.ReadFromJsonAsync(
            GameEditorJson.Default.GameEditorDetails,
            token
        );
        if (details?.Appointment is not { } result || result.Id != id)
            throw new GameEditorException(GameEditorIssue.InvalidResponse);
        return result;
    }

    public async Task<string> CreateAsync(GameEditorCreate request, CancellationToken token)
    {
        using var response = await Send(
            HttpMethod.Post,
            "game-data/create-game",
            JsonContent.Create(request, GameEditorJson.Default.GameEditorCreate),
            token
        );
        var result = await response.Content.ReadFromJsonAsync(
            GameEditorJson.Default.GameEditorCreated,
            token
        );
        if (string.IsNullOrWhiteSpace(result?.AppointmentId))
            throw new GameEditorException(GameEditorIssue.InvalidResponse);
        return result.AppointmentId;
    }

    public async Task UpdateAsync(string id, GameEditorUpdate request, CancellationToken token)
    {
        using var response = await Send(
            HttpMethod.Put,
            "game-data/" + Uri.EscapeDataString(id),
            JsonContent.Create(request, GameEditorJson.Default.GameEditorUpdate),
            token
        );
    }

    private async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken token
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (method == HttpMethod.Post)
        {
            // The deployed exact create route requires IAM/SigV4. The host supplies a package-backed signer.
            // Give it the absolute URI and the final source-generated JSON bytes before signing.
            request.RequestUri = new Uri(
                http.BaseAddress ?? throw new InvalidOperationException("BaseAddress required"),
                path
            );
            await authorizeCreate(request, timeout.Token);
        }
        else
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await session.AccessTokenAsync(timeout.Token)
            );
        timeout.Token.ThrowIfCancellationRequested();
        var response = await http.SendAsync(request, timeout.Token);
        if (
            response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Created
        )
            return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new GameEditorException(
            status is 401 or 403 ? GameEditorIssue.Unauthorized
                : status >= 500 ? GameEditorIssue.Server
                : GameEditorIssue.Rejected,
            status
        );
    }
}
