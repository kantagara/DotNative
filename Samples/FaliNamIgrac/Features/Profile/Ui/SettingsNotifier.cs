using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed class SettingsNotifier(
    IUiDispatcher dispatcher,
    IThemeController themes,
    IPresentationState presentation,
    Func<string, CancellationToken, Task> saveLanguage
) : Notifier<string>(dispatcher, "")
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private Func<CancellationToken, Task>? retry;
    public Func<SettingsTexts> Texts { get; set; } =
        () => SettingsTexts.ForLocale(presentation.Locale.Value);

    public Task SetLanguageAsync(string locale, CancellationToken token)
    {
        if (locale is not ("sr" or "en" or "ru"))
            throw new ArgumentOutOfRangeException(nameof(locale));
        return Save(
            async cancellation =>
            {
                await saveLanguage(locale, cancellation);
                // A successful write is the commit point. These are device preferences,
                // not account data: navigation/logout after commit must not leave the
                // running app different from the value restored on the next launch.
                await uiDispatcher.InvokeAsync(() => presentation.Locale.Value = locale);
            },
            token
        );
    }

    public Task SetThemeAsync(ThemeMode mode, CancellationToken token)
    {
        if (mode is not (ThemeMode.Light or ThemeMode.Dark))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return Save(
            async cancellation =>
            {
                await themes.SetAsync(mode, cancellation);
                // Existing controller suppresses Apply when cancellation arrives after
                // the write. Re-read the committed device preference in that case.
                if (cancellation.IsCancellationRequested)
                    await themes.LoadAsync(CancellationToken.None);
            },
            token
        );
    }

    public Task RetryAsync(CancellationToken token) =>
        retry is { } write ? Save(write, token) : Task.CompletedTask;

    private Task Save(Func<CancellationToken, Task> write, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return Task.CompletedTask;
        return RunExclusive(async () =>
        {
            SetState("");
            retry = write;
            try
            {
                await write(token);
                await uiDispatcher.InvokeAsync(() => retry = null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception)
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        SetState(Texts().SaveFailed);
                });
            }
        });
    }
}
