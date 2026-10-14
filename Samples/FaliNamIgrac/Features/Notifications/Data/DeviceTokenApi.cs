using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

// Credential snapshot belongs to this account only. Never resolve a different session mid-request.
public sealed record NotificationAccount(int UserId, string AccessToken)
{
    public override string ToString() => $"NotificationAccount({UserId})";
}

public interface IDeviceTokenApi
{
    Task RegisterAsync(NotificationAccount account, string deviceToken, CancellationToken token);
    Task DeleteAsync(NotificationAccount account, string deviceToken, CancellationToken token);
}

public sealed record NotificationTokenRequest(
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("token")] string Token
);

public sealed record NotificationLocalPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("gameId")] string? GameId,
    [property: JsonPropertyName("chatId")] string? ChatId,
    [property: JsonPropertyName("deliveryId")] string DeliveryId
);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NotificationTokenRequest))]
[JsonSerializable(typeof(NotificationLocalPayload))]
internal partial class NotificationJsonContext : JsonSerializerContext;

/// <summary>Uses a caller-owned HttpClient with the configured REST base address.</summary>
public sealed class DeviceTokenApi(HttpClient http) : IDeviceTokenApi
{
    public Task RegisterAsync(
        NotificationAccount account,
        string deviceToken,
        CancellationToken token
    ) => SendAsync("device-tokens/register-token", account, deviceToken, token);

    public Task DeleteAsync(
        NotificationAccount account,
        string deviceToken,
        CancellationToken token
    ) => SendAsync("device-tokens/delete-token", account, deviceToken, token);

    private async Task SendAsync(
        string path,
        NotificationAccount account,
        string deviceToken,
        CancellationToken token
    )
    {
        if (account.UserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(account));
        ArgumentException.ThrowIfNullOrWhiteSpace(account.AccessToken);
        ValidateToken(deviceToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(
                new NotificationTokenRequest(account.UserId, deviceToken),
                NotificationJsonContext.Default.NotificationTokenRequest
            ),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            account.AccessToken
        );
        using var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        token.ThrowIfCancellationRequested();
    }

    internal static void ValidateToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl))
            throw new ArgumentException("Invalid device token.", nameof(value));
    }
}
