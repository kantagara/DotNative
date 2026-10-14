using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public enum DeviceTokenStatus
{
    SignedOut,
    Loading,
    Registered,
    PermissionDenied,
    Unsupported,
    Error,
}

public sealed record DeviceTokenState(
    DeviceTokenStatus Status = DeviceTokenStatus.SignedOut,
    bool CleanupPending = false
);

/// <summary>Call entry points on the UI dispatcher. Backend operations are serialized across epochs.
/// Await SetAccountAsync(null) before discarding logout credentials. Errors remain retryable.</summary>
public sealed class DeviceTokenNotifier(
    IUiDispatcher dispatcher,
    INotificationDevice device,
    IDeviceTokenApi api
) : Notifier<DeviceTokenState>(dispatcher, new()), IDisposable
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly Dictionary<(int User, string Token), NotificationAccount> possibleBindings =
        new();
    private CancellationTokenSource epoch = new();
    private NotificationAccount? account;
    private (int User, string Token)? registered;
    private string? refreshedToken;
    private int revision;
    private bool disposed;
    private Task? inFlight;
    private int inFlightRevision;
    private NotificationAccount? inFlightAccount;
    private string? inFlightToken;

    public Task SetAccountAsync(NotificationAccount? value, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (
            value is not null
            && (value.UserId <= 0 || string.IsNullOrWhiteSpace(value.AccessToken))
        )
            throw new ArgumentException("An authenticated account is required.", nameof(value));
        if (account?.UserId != value?.UserId)
        {
            epoch.Cancel();
            epoch.Dispose();
            epoch = new();
            revision++;
            refreshedToken = null;
        }
        account = value;
        return ScheduleAsync(token);
    }

    // Wire to a real native token-refresh event after the plugin API is released.
    public Task TokenRefreshedAsync(string value, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        DeviceTokenApi.ValidateToken(value);
        refreshedToken = value;
        return RetryAsync(token);
    }

    // Also call on authenticated foreground resume to detect token/permission changes.
    public Task RetryAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return ScheduleAsync(token);
    }

    public Task ResumeAsync(CancellationToken token = default)
    {
        refreshedToken = null;
        return RetryAsync(token);
    }

    private Task ScheduleAsync(CancellationToken token)
    {
        if (
            inFlight is { IsCompleted: false }
            && inFlightRevision == revision
            && inFlightAccount == account
            && inFlightToken == refreshedToken
        )
            return inFlight.WaitAsync(token);
        inFlightRevision = revision;
        inFlightAccount = account;
        inFlightToken = refreshedToken;
        return inFlight = ReconcileAsync(revision, epoch.Token, token);
    }

    private async Task ReconcileAsync(
        int version,
        CancellationToken sessionToken,
        CancellationToken caller
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(sessionToken, caller);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        var token = linked.Token;
        var entered = false;
        try
        {
            await serial.WaitAsync(token);
            entered = true;
            if (version != revision)
                return;
            NotificationAccount? current = null;
            string? refreshed = null;
            await uiDispatcher.InvokeAsync(() =>
            {
                current = account;
                refreshed = refreshedToken;
                if (version == revision && !disposed)
                    SetState(new(DeviceTokenStatus.Loading));
            });
            token.ThrowIfCancellationRequested();
            var permission = current is null
                ? NotificationPermission.Denied
                : await device.RequestPermissionAsync(token);
            token.ThrowIfCancellationRequested();
            string? desired = null;
            if (current is not null && permission == NotificationPermission.Authorized)
            {
                desired = refreshed ?? await device.GetTokenAsync(token);
                DeviceTokenApi.ValidateToken(desired);
            }
            token.ThrowIfCancellationRequested();
            var key = desired is null
                ? ((int User, string Token)?)null
                : (current!.UserId, desired);
            foreach (var item in possibleBindings.ToArray())
            {
                if (key == item.Key)
                    continue;
                var credential = current?.UserId == item.Key.User ? current : item.Value;
                await api.DeleteAsync(credential, item.Key.Token, token);
                possibleBindings.Remove(item.Key);
                if (registered == item.Key)
                    registered = null;
            }
            token.ThrowIfCancellationRequested();
            if (key is { } existing && possibleBindings.ContainsKey(existing))
                possibleBindings[existing] = current!;
            if (key is { } target && registered != target)
            {
                // A timed-out/cancelled request may have committed remotely; retain it for cleanup.
                possibleBindings[target] = current!;
                await api.RegisterAsync(current!, target.Token, token);
                registered = target;
            }
            token.ThrowIfCancellationRequested();
            await PublishAsync(
                version,
                new(
                    current is null ? DeviceTokenStatus.SignedOut
                    : permission == NotificationPermission.Denied
                        ? DeviceTokenStatus.PermissionDenied
                    : permission == NotificationPermission.Unsupported
                        ? DeviceTokenStatus.Unsupported
                    : DeviceTokenStatus.Registered
                )
            );
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await PublishAsync(version, new(DeviceTokenStatus.Error, possibleBindings.Count > 0));
        }
        catch (Exception)
        {
            await PublishAsync(version, new(DeviceTokenStatus.Error, possibleBindings.Count > 0));
        }
        finally
        {
            if (entered)
                serial.Release();
        }
    }

    private Task PublishAsync(int version, DeviceTokenState value) =>
        uiDispatcher.InvokeAsync(() =>
        {
            if (!disposed && revision == version)
                SetState(value);
        });

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        revision++;
        epoch.Cancel();
        epoch.Dispose();
    }
}
