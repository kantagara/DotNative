using DotNative;
using DotNative.Plugins;
using DotNative.Preferences;
using DotNative.Toast;
using DotNative.WheelDatePicker;

namespace FaliNamIgrac;

// Shared native presentation resources and navigation commands. No feature state.
public sealed class UiContext : IDisposable
{
    public IPresentationState Presentation { get; }
    public AppLocalizations Text => Presentation.Text;
    public IThemeController Themes { get; }
    public IUiDispatcher Dispatcher { get; }
    public PresentationTarget Target { get; }
    public IPlatformChannels Channels { get; }
    public IPreferences Preferences =>
        preferences ??= DotNative.Preferences.Preferences.Create(Channels, Target);
    private IPreferences? preferences;
    public IOverlayPresenter Overlays { get; }
    public IToast Toast { get; }
    public CancellationTokenSource Lifetime { get; } = new();
    public AnimationController LoginAnimation { get; }
    public AnimationController OrbitAnimation { get; }
    public State<ImageSource?> BusyImage { get; } = new(null);
    public AuthRouter Router { get; }
    public Func<CancellationToken, Task<byte[]?>>? PickPhoto { get; set; }
    public Func<string, byte[], CancellationToken, Task>? UploadPhoto { get; set; }
    public IChatCache? ChatCache { get; set; }
    public RemoteImages? Images { get; set; }

    public UiContext(
        IPresentationState presentation,
        IThemeController themes,
        IUiDispatcher dispatcher,
        PresentationTarget target,
        IPlatformChannels channels,
        IOverlayPresenter overlays,
        IToast toast,
        AuthRouter router
    )
    {
        Presentation = presentation;
        Themes = themes;
        Dispatcher = dispatcher;
        Target = target;
        Channels = channels;
        Overlays = overlays;
        Toast = toast ?? throw new ArgumentNullException(nameof(toast));
        Router = router ?? throw new ArgumentNullException(nameof(router));
        LoginAnimation = new(dispatcher);
        OrbitAnimation = new(dispatcher);
    }

    public Task<DateOnly?> ShowDatePickerAsync(
        DatePickerOptions options,
        CancellationToken token = default
    ) => WheelDatePicker.ShowAsync(Overlays, Dispatcher, Channels, Target, options, token);

    public void Dispose()
    {
        Lifetime.Cancel();
        LoginAnimation.Dispose();
        OrbitAnimation.Dispose();
        Lifetime.Dispose();
    }
}
