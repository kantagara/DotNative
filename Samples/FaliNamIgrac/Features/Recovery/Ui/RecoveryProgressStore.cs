using System.Text.Json;
using System.Text.Json.Serialization;
using DotNative.SecureStorage;

namespace FaliNamIgrac;

/// <summary>Only the recipient is persisted. Codes and new passwords must be entered again.</summary>
public interface IRecoveryProgressStore
{
    Task<string?> ReadEmailAsync(CancellationToken token);
    Task SaveEmailAsync(string? email, CancellationToken token);
}

public sealed class RecoveryProgressStore(ISecureStorage storage) : IRecoveryProgressStore
{
    public async Task<string?> ReadEmailAsync(CancellationToken token)
    {
        var json = await storage.ReadAsync("recovery-v1", token);
        return json is null
            ? null
            : JsonSerializer.Deserialize(json, RecoveryProgressJson.Default.String);
    }

    public Task SaveEmailAsync(string? email, CancellationToken token) =>
        email is null
            ? storage.DeleteAsync("recovery-v1", token)
            : storage.WriteAsync(
                "recovery-v1",
                JsonSerializer.Serialize(email, RecoveryProgressJson.Default.String),
                token
            );
}

[JsonSerializable(typeof(string))]
internal partial class RecoveryProgressJson : JsonSerializerContext;
