using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record AccountPhotoPointer(int UserId, string Extension);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AccountPhotoPointer))]
internal partial class AccountPhotoJson : JsonSerializerContext;

/// <summary>Reuses existing storage but writes a new object before changing the profile pointer.
/// Failed uploads or pointer updates never overwrite the previous image object.</summary>
public sealed class AccountPhotoUpload(
    HttpClient http,
    ISessionService session,
    IProfilePhotoStorage storage
)
{
    public async Task UploadAsync(string userId, byte[] photo, CancellationToken token)
    {
        if (!int.TryParse(userId, out var id) || id <= 0)
            throw new ArgumentException("Invalid account ID.");
        var format = PhotoFormat.Detect(photo);
        var accessToken = await session.AccessTokenAsync(token);
        token.ThrowIfCancellationRequested();
        var revision = Guid.NewGuid().ToString("N");
        // Storage appends the detected extension; backend appends Extension to numeric UserId.
        await storage.UploadAsync(userId + "." + revision, photo, token);
        token.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "user-data/update-profile-picture"
        )
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new AccountPhotoPointer(id, "." + revision + format.Extension),
                    AccountPhotoJson.Default.AccountPhotoPointer
                ),
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
    }
}
