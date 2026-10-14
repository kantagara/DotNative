namespace FaliNamIgrac;

/// <summary>Composition binds this delegate to DotNative.PdfView.IPdfViewer.PresentAsync.</summary>
public sealed class AppointmentPdfViewerAdapter(Func<Uri, string, CancellationToken, Task> present)
    : IAppointmentPdfViewer
{
    public Task PresentAsync(Uri source, string title, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = PlatformActionLinks.Https(source.AbsoluteUri);
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A document title is required.");
        return PlatformPluginErrors.InvokeAsync(() => present(uri, title, token));
    }
}
