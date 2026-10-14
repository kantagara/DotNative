using System.Globalization;
using DotNative.Plugins;

namespace FaliNamIgrac;

public enum AppointmentMap
{
    Apple,
    Google,
}

public sealed record AppointmentLocation(double Latitude, double Longitude, string Title)
{
    public void Validate()
    {
        if (
            !double.IsFinite(Latitude)
            || !double.IsFinite(Longitude)
            || Latitude is < -90 or > 90
            || Longitude is < -180 or > 180
        )
            throw new ArgumentException("Invalid appointment coordinates.");
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 200 || Title.Any(char.IsControl))
            throw new ArgumentException("Invalid location title.");
    }

    public Uri BrowserUri()
    {
        Validate();
        var query = string.Create(CultureInfo.InvariantCulture, $"{Latitude},{Longitude}");
        return new(
            "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(query)
        );
    }
}

public static class PlatformActionLinks
{
    public static string AppointmentId(string id)
    {
        if (
            string.IsNullOrWhiteSpace(id)
            || id.Length > 200
            || id is "." or ".."
            || id.Any(c => char.IsControl(c) || c is '/' or '\\' or '?' or '#' or '%')
        )
            throw new ArgumentException("Invalid appointment ID.");
        return Uri.EscapeDataString(id);
    }

    public static Uri Game(string id) =>
        new("https://app.falinamigrac.com/game/" + AppointmentId(id));

    public static Uri Https(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > 4096
            || value != value.Trim()
            || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.UserInfo.Length != 0
            || string.IsNullOrEmpty(uri.Host)
            || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns
            || uri.IsLoopback
        )
            throw new ArgumentException("A public HTTPS URL is required.");
        return uri;
    }
}

// App-facing boundaries. Bind these to released sibling plugins in composition;
// there is deliberately no native channel or platform implementation in the sample.
public interface IAppointmentMaps
{
    Task<IReadOnlyList<AppointmentMap>> InstalledAsync(CancellationToken token);
    Task OpenAsync(AppointmentMap map, AppointmentLocation location, CancellationToken token);
}

public interface IPlatformShare
{
    Task ShareAsync(Uri publicLink, CancellationToken token);
}

public interface IExternalLinks
{
    Task OpenAsync(Uri uri, CancellationToken token);
}

public interface IAppointmentLocationSource
{
    Task<AppointmentLocation> LoadAsync(string id, CancellationToken token);
}

public interface IAppointmentPdfViewer
{
    // The sibling plugin owns download/cache/native viewer and requests close on cancellation.
    // Normal completion releases native resources; cancellation cleanup may finish shortly afterward.
    Task PresentAsync(Uri source, string title, CancellationToken token);
}

public sealed class UnavailablePdfViewer : IAppointmentPdfViewer
{
    public Task PresentAsync(Uri source, string title, CancellationToken token) =>
        Task.FromException(
            new PlatformNotSupportedException("A released PDF viewer plugin is required.")
        );
}

// Typed delegates allow composition with packages not yet referenced by the shared sample project.
public sealed class AppointmentMapsAdapter(
    Func<CancellationToken, Task<IReadOnlyList<AppointmentMap>>> installed,
    Func<AppointmentMap, AppointmentLocation, CancellationToken, Task> open
) : IAppointmentMaps
{
    public Task<IReadOnlyList<AppointmentMap>> InstalledAsync(CancellationToken token) =>
        PlatformPluginErrors.InvokeAsync(() => installed(token));

    public Task OpenAsync(AppointmentMap map, AppointmentLocation location, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        location.Validate();
        return PlatformPluginErrors.InvokeAsync(() => open(map, location, token));
    }
}

public sealed class PlatformShareAdapter(Func<Uri, CancellationToken, Task> share) : IPlatformShare
{
    public Task ShareAsync(Uri link, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return PlatformPluginErrors.InvokeAsync(() => share(link, token));
    }
}

public sealed class ExternalLinksAdapter(Func<Uri, CancellationToken, Task> open) : IExternalLinks
{
    public Task OpenAsync(Uri uri, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return PlatformPluginErrors.InvokeAsync(() =>
            open(PlatformActionLinks.Https(uri.AbsoluteUri), token)
        );
    }
}

internal static class PlatformPluginErrors
{
    public static async Task InvokeAsync(Func<Task> action) =>
        await InvokeAsync(async () =>
        {
            await action();
            return true;
        });

    public static async Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (PluginException e)
            when (e.Code
                    is "unavailable"
                        or "map_unavailable"
                        or "share_unavailable"
                        or "not_implemented"
                        or "unsupported"
            )
        {
            throw new PlatformNotSupportedException(e.Message, e);
        }
        catch (PluginException e) when (e.Code is "permission_denied" or "access_denied")
        {
            throw new UnauthorizedAccessException(e.Message, e);
        }
    }
}
