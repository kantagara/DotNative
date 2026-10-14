using DotNative;
using DotNative.Plugins;
using DotNative.Preferences;

namespace FaliNamIgrac;

public sealed class ThemeController(
    IPlatformChannels channels,
    PresentationTarget target,
    ISystemAppearance appearance,
    IUiDispatcher dispatcher
) : IThemeController
{
    private IPreferences? storage;
    private IPreferences preferences => storage ??= Preferences.Create(channels, target);
    private const string PreferenceKey = "selected_theme";
    private int revision;

    public State<IThemeData> Current { get; } = new(FniTheme.Light);

    public async Task LoadAsync(CancellationToken token)
    {
        var request = ++revision;
        var systemMode = appearance.Theme;
        try
        {
            var stored = await preferences.GetAsync<string?>(PreferenceKey, null, token);
            var mode = stored switch
            {
                null => systemMode,
                "dark" => ThemeMode.Dark,
                _ => ThemeMode.Light,
            };
            await dispatcher.InvokeAsync(() => Apply(mode, request, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Failed to load theme: {error}");
        }
    }

    public async Task SetAsync(ThemeMode mode, CancellationToken token)
    {
        if (mode is not (ThemeMode.Light or ThemeMode.Dark))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var request = ++revision;
        await preferences.SetAsync(PreferenceKey, mode == ThemeMode.Dark ? "dark" : "light", token);
        await dispatcher.InvokeAsync(() => Apply(mode, request, CancellationToken.None));
    }

    private void Apply(ThemeMode mode, int request, CancellationToken token)
    {
        if (!token.IsCancellationRequested && request == revision)
            Current.Value = mode == ThemeMode.Dark ? FniTheme.Dark : FniTheme.Light;
    }
}
