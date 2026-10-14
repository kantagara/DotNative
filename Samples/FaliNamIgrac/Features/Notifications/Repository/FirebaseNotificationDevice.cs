using DotNative.Firebase;

namespace FaliNamIgrac;

public enum NotificationPermission
{
    Authorized,
    Denied,
    Unsupported,
}

public interface INotificationDevice
{
    Task<NotificationPermission> RequestPermissionAsync(CancellationToken token);
    Task<string> GetTokenAsync(CancellationToken token);
}

/// <summary>The published plugin supports permission and token reads only reliably.
/// Its Messages stream lacks data/event kind; it must NOT be treated as a tap stream.</summary>
public sealed class FirebaseNotificationDevice(IFirebaseMessaging messaging) : INotificationDevice
{
    public Task<string> GetTokenAsync(CancellationToken token) => messaging.GetTokenAsync(token);

    public async Task<NotificationPermission> RequestPermissionAsync(CancellationToken token) =>
        (await messaging.RequestNotificationPermissionAsync(token)) switch
        {
            "authorized" or "provisional" or "ephemeral" => NotificationPermission.Authorized,
            "denied" or "notDetermined" => NotificationPermission.Denied,
            "notSupported" => NotificationPermission.Unsupported,
            _ => throw new InvalidDataException("Unknown notification permission status."),
        };
}
