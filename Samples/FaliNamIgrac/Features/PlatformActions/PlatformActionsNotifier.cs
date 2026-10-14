using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public enum PlatformActionStatus
{
    Idle,
    Loading,
    ChooseMap,
    Completed,
    InvalidInput,
    Unavailable,
    PermissionDenied,
    Error,
    Cancelled,
}

public sealed record PlatformActionState(
    PlatformActionStatus Status = PlatformActionStatus.Idle,
    IReadOnlyList<AppointmentMap>? Maps = null,
    bool CanOpenExternally = false
);

/// <summary>One instance per mounted screen. Entry points and Invalidate run on UI dispatcher.
/// Invalidate on every session identity change, logout, and account replacement (even same user).</summary>
public sealed class PlatformActionsNotifier(
    IUiDispatcher dispatcher,
    IAppointmentMaps maps,
    IPlatformShare share,
    IExternalLinks links,
    IAppointmentPdfViewer pdf,
    IAppointmentLocationSource locations
) : Notifier<PlatformActionState>(dispatcher, new()), IDisposable
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private CancellationTokenSource lifetime = new();
    private Task? running;
    private Func<CancellationToken, Task<PlatformActionState>>? retry;
    private AppointmentLocation? selectedLocation;
    private string? documentUrl;
    private int revision;
    private bool disposed;

    public Task OpenMapAsync(string appointmentId, CancellationToken token = default) =>
        Run(
            async ct =>
            {
                var location = await locations.LoadAsync(appointmentId, ct);
                ct.ThrowIfCancellationRequested();
                return await ChooseOrOpenAsync(location, ct);
            },
            token
        );

    public Task OpenMapAsync(AppointmentLocation location, CancellationToken token = default) =>
        Run(ct => ChooseOrOpenAsync(location, ct), token);

    private async Task<PlatformActionState> ChooseOrOpenAsync(
        AppointmentLocation location,
        CancellationToken token
    )
    {
        location.Validate();
        IReadOnlyList<AppointmentMap> installed;
        try
        {
            installed = await maps.InstalledAsync(token);
        }
        catch (PlatformNotSupportedException)
        {
            installed = [];
        }
        token.ThrowIfCancellationRequested();
        var choices = installed.Where(Enum.IsDefined).Distinct().ToArray();
        if (choices.Length == 0)
        {
            await links.OpenAsync(location.BrowserUri(), token);
            return new(PlatformActionStatus.Completed);
        }
        if (choices.Length == 1)
            return await OpenSelectedAsync(choices[0], location, token);
        selectedLocation = location;
        return new(PlatformActionStatus.ChooseMap, choices);
    }

    public Task SelectMapAsync(AppointmentMap map, CancellationToken token = default)
    {
        if (
            State.Status != PlatformActionStatus.ChooseMap
            || State.Maps?.Contains(map) != true
            || selectedLocation is null
        )
            return Task.CompletedTask;
        var location = selectedLocation;
        return Run(ct => OpenSelectedAsync(map, location, ct), token);
    }

    private async Task<PlatformActionState> OpenSelectedAsync(
        AppointmentMap map,
        AppointmentLocation location,
        CancellationToken token
    )
    {
        try
        {
            await maps.OpenAsync(map, location, token);
        }
        catch (PlatformNotSupportedException)
        {
            token.ThrowIfCancellationRequested();
            await links.OpenAsync(location.BrowserUri(), token);
        }
        return new(PlatformActionStatus.Completed);
    }

    public Task ShareGameAsync(string id, CancellationToken token = default) =>
        Run(
            async ct =>
            {
                await share.ShareAsync(PlatformActionLinks.Game(id), ct);
                // Completion acknowledges the plugin request, never that content was delivered.
                return new(PlatformActionStatus.Completed);
            },
            token
        );

    public Task OpenDocumentAsync(string url, string title, CancellationToken token = default) =>
        Run(
            async ct =>
            {
                var uri = PlatformActionLinks.Https(url);
                if (
                    string.IsNullOrWhiteSpace(title)
                    || title.Length > 200
                    || title.Any(char.IsControl)
                )
                    throw new ArgumentException("Invalid document title.");
                documentUrl = uri.AbsoluteUri;
                try
                {
                    await pdf.PresentAsync(uri, title, ct);
                }
                catch (PlatformNotSupportedException)
                {
                    return new(PlatformActionStatus.Unavailable, CanOpenExternally: true);
                }
                return new(PlatformActionStatus.Completed);
            },
            token
        );

    // Explicit user action, never silently substitute a browser for the PDF viewer.
    public Task OpenDocumentExternallyAsync(CancellationToken token = default)
    {
        if (!State.CanOpenExternally || documentUrl is null)
            return Task.CompletedTask;
        var url = documentUrl;
        return Run(ct => OpenExternalCoreAsync(url, ct), token);
    }

    public Task OpenExternalAsync(string url, CancellationToken token = default) =>
        Run(ct => OpenExternalCoreAsync(url, ct), token);

    private async Task<PlatformActionState> OpenExternalCoreAsync(
        string url,
        CancellationToken token
    )
    {
        await links.OpenAsync(PlatformActionLinks.Https(url), token);
        return new(PlatformActionStatus.Completed);
    }

    public Task RetryAsync(CancellationToken token = default) =>
        retry is null ? Task.CompletedTask : Run(retry, token);

    private Task Run(
        Func<CancellationToken, Task<PlatformActionState>> operation,
        CancellationToken caller
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (running is { IsCompleted: false })
            return running;
        retry = operation;
        return running = ExecuteAsync(operation, revision, caller);
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task<PlatformActionState>> operation,
        int version,
        CancellationToken caller
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
        // Native viewers/share sheets can stay open until the user closes them; no arbitrary timeout.
        SetState(new(PlatformActionStatus.Loading));
        PlatformActionState result;
        try
        {
            result = await operation(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            result = new(PlatformActionStatus.Cancelled);
        }
        catch (ArgumentException)
        {
            result = new(PlatformActionStatus.InvalidInput);
        }
        catch (UnauthorizedAccessException)
        {
            result = new(PlatformActionStatus.PermissionDenied);
        }
        catch (PlatformNotSupportedException)
        {
            result = new(PlatformActionStatus.Unavailable);
        }
        catch (Exception)
        {
            result = new(PlatformActionStatus.Error);
        }
        await uiDispatcher.InvokeAsync(() =>
        {
            if (!disposed && version == revision)
                SetState(result);
        });
    }

    public void Invalidate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        revision++;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new();
        retry = null;
        selectedLocation = null;
        documentUrl = null;
        SetState(new());
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        revision++;
        lifetime.Cancel();
        lifetime.Dispose();
        retry = null;
        selectedLocation = null;
        documentUrl = null;
    }
}
