namespace FaliNamIgrac;

/// <summary>Backend transport. Callers own and dispose returned responses.</summary>
public interface IApiClient
{
    Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    );

    Task<HttpResponseMessage> GetResponseAsync<TQuery>(
        string path,
        TQuery query,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    );
    Task<TResponse> GetAsync<TResponse>(
        string path,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    );
    Task<TResponse> GetAsync<TQuery, TResponse>(
        string path,
        TQuery query,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    );
    Task<HttpResponseMessage> PostJsonAsync<T>(
        string path,
        T body,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    );
    Task<HttpResponseMessage> PutJsonAsync<T>(
        string path,
        T body,
        CancellationToken token,
        bool authenticated = false,
        TimeSpan? timeout = null
    );
}
