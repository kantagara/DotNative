using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FaliNamIgrac;

public sealed class ApiClient : IApiClient, IDisposable
{
    private readonly HttpClient http;
    private readonly ISessionService session;
    private readonly bool ownsHttp;

    public ApiClient(ISessionService session)
        : this(
            new HttpClient
            {
                BaseAddress = new Uri(BackendConfiguration.ApiUrl.TrimEnd('/') + "/"),
                Timeout = Timeout.InfiniteTimeSpan,
            },
            session
        )
    {
        ownsHttp = true;
    }

    // Allows a caller-owned transport for tests or custom hosts.
    public ApiClient(HttpClient http, ISessionService session) =>
        (this.http, this.session) = (http, session);

    public void Dispose()
    {
        if (ownsHttp)
            http.Dispose();
    }

    public Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) => SendAsync(HttpMethod.Get, path, null, authenticated, token, timeout);

    public Task<HttpResponseMessage> GetResponseAsync<TQuery>(
        string path,
        TQuery query,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) =>
        SendAsync(
            HttpMethod.Get,
            path + "?" + ApiJson.QueryString(query),
            null,
            authenticated,
            token,
            timeout
        );

    public async Task<TResponse> GetAsync<TResponse>(
        string path,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    )
    {
        using var response = await GetAsync(path, token, authenticated, timeout);
        EnsureSuccess(response);
        return await ApiJson.ReadAsync<TResponse>(response.Content, token);
    }

    public async Task<TResponse> GetAsync<TQuery, TResponse>(
        string path,
        TQuery query,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    )
    {
        using var response = await GetResponseAsync(path, query, token, authenticated, timeout);
        EnsureSuccess(response);
        return await ApiJson.ReadAsync<TResponse>(response.Content, token);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"API zahtev nije uspeo ({(int)response.StatusCode})."
            );
    }

    public Task<HttpResponseMessage> PostJsonAsync<T>(
        string path,
        T body,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) =>
        SendAsync(
            HttpMethod.Post,
            path,
            JsonContent.Create(body, ApiJson.Type<T>()),
            authenticated,
            token,
            timeout
        );

    public Task<HttpResponseMessage> PutJsonAsync<T>(
        string path,
        T body,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    ) =>
        SendAsync(
            HttpMethod.Put,
            path,
            JsonContent.Create(body, ApiJson.Type<T>()),
            authenticated,
            token,
            timeout
        );

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        bool authenticated,
        CancellationToken token,
        TimeSpan? timeout
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        token = deadline.Token;
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (authenticated)
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await session.AccessTokenAsync(token)
            );
        return await http.SendAsync(request, token);
    }
}
