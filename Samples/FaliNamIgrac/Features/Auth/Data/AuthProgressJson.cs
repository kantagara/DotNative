using System.Text.Json.Serialization;

namespace FaliNamIgrac;

internal sealed record PendingRegistration(string Email, string TemporaryUserId);

internal sealed record PendingConfirmation(string Email, bool Confirmed);

[JsonSerializable(typeof(PendingConfirmation))]
[JsonSerializable(typeof(PendingRegistration))]
internal partial class AuthProgressJson : JsonSerializerContext;
