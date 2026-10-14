using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNative.SecureStorage;

namespace FaliNamIgrac;

public sealed record OnboardingDraft(
    string Email,
    string TemporaryUserId,
    string FirstName,
    string LastName,
    string Nickname,
    string Phone,
    string Bio,
    string? Gender,
    DateTime? BirthDate,
    string Address,
    SelectedPlace? Place,
    byte[]? Photo,
    string? CreatedUserId,
    bool CreationStarted
);

public interface IOnboardingDraftStore
{
    Task<OnboardingDraft?> ReadAsync(RegistrationIdentity identity, CancellationToken token);
    Task WriteAsync(OnboardingDraft draft, CancellationToken token);
    Task DeleteAsync(RegistrationIdentity identity, CancellationToken token);
}

public sealed class OnboardingDraftStore(ISecureStorage storage) : IOnboardingDraftStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private static string Key(string email, string id) =>
        "onboarding-v1-"
        + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant() + "\n" + id))
        );

    public async Task<OnboardingDraft?> ReadAsync(
        RegistrationIdentity identity,
        CancellationToken token
    )
    {
        var value = await storage.ReadAsync(Key(identity.Email, identity.TemporaryUserId), token);
        if (value is null)
            return null;
        var draft = JsonSerializer.Deserialize(value, OnboardingDraftJson.Default.OnboardingDraft);
        return
            draft?.TemporaryUserId == identity.TemporaryUserId
            && string.Equals(draft.Email, identity.Email, StringComparison.OrdinalIgnoreCase)
            ? draft
            : null;
    }

    public async Task WriteAsync(OnboardingDraft draft, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await storage.WriteAsync(
                Key(draft.Email, draft.TemporaryUserId),
                JsonSerializer.Serialize(draft, OnboardingDraftJson.Default.OnboardingDraft),
                token
            );
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(RegistrationIdentity identity, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await storage.DeleteAsync(Key(identity.Email, identity.TemporaryUserId), token);
        }
        finally
        {
            gate.Release();
        }
    }
}

[JsonSerializable(typeof(OnboardingDraft))]
internal partial class OnboardingDraftJson : JsonSerializerContext;
