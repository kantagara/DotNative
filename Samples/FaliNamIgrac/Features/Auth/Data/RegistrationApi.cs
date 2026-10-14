using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

public enum AccountAuthType
{
    Email,
    Google,
    Apple,
    Facebook,
}

public enum UserRegistrationStatus
{
    NotRegistered,
    TempUser,
    FullyRegistered,
    RegisteredButNotInTempOrUsers,
}

public sealed class RegistrationApi(IApiClient backend) : IRegistrationApi
{
    public async Task<UserRegistrationStatus> CheckAsync(string email, CancellationToken token)
    {
        using var response = await backend.GetResponseAsync(
            "user-data/v2/alreadyRegistered",
            new EmailQuery(email.Trim()),
            token
        );
        response.EnsureSuccessStatusCode();
        return ParseStatus(await response.Content.ReadAsStringAsync(token));
    }

    public async Task<UserRegistrationStatus> CheckAuthenticatedAsync(
        string email,
        CancellationToken token
    )
    {
        using var response = await backend.GetAsync(
            "user-data/v2/registration-state-auth",
            token,
            authenticated: true
        );
        response.EnsureSuccessStatusCode();
        return ParseStatus(await response.Content.ReadAsStringAsync(token));
    }

    public async Task<string?> GetTemporaryUserIdAsync(string email, CancellationToken token)
    {
        using var response = await backend.GetResponseAsync(
            "user-data/getTempUserIdByEmail",
            new EmailQuery(email.Trim()),
            token
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return ParseTemporaryId(await response.Content.ReadAsStringAsync(token))
            ?? throw new InvalidDataException("Missing temporary user ID.");
    }

    public async Task<string?> CreateTemporaryUserAsync(
        string email,
        AccountAuthType authType,
        CancellationToken token
    )
    {
        using var response = await backend.PostJsonAsync(
            "user-data/temp-users",
            new TemporaryUserRequest(email.Trim(), authType.ToString().ToUpperInvariant()),
            token
        );
        response.EnsureSuccessStatusCode();
        return ParseTemporaryId(await response.Content.ReadAsStringAsync(token))
            ?? throw new InvalidDataException("Missing temporary user ID.");
    }

    private static UserRegistrationStatus ParseStatus(string body)
    {
        var value = body.Trim();
        if (value.Length == 0)
            throw new FormatException("Prazan odgovor za registration status");
        if (value.StartsWith('"'))
            value = JsonSerializer.Deserialize(value, BackendJson.Default.String) ?? "";
        return
            Enum.TryParse<UserRegistrationStatus>(value, out var status)
            && Enum.IsDefined(status)
            && value == status.ToString()
            ? status
            : throw new FormatException("Unknown registration status");
    }

    private static string? ParseTemporaryId(string body) =>
        string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize(body, BackendJson.Default.TemporaryUserResponse)?.UserId;
}
