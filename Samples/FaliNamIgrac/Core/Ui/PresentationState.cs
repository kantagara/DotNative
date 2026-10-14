using DotNative;

namespace FaliNamIgrac;

public interface IPresentationState
{
    State<string> Locale { get; }
    AppLocalizations Text { get; }
    State<bool> LanguagePopup { get; }
    State<ApplicationScreen> CurrentScreen { get; }
    State<ImageSource?> OrbitDecoration { get; }
    State<bool> StartupRetry { get; }
    State<string> StartupMessage { get; }
    State<(float Width, float Height)?> Dimensions { get; }
}

public sealed class PresentationState : IPresentationState
{
    public State<string> Locale { get; } = new("en");
    public AppLocalizations Text => PresentationLocale.Text(Locale.Value);
    public State<bool> LanguagePopup { get; } = new(false);
    public State<ApplicationScreen> CurrentScreen { get; } = new(ApplicationScreen.Splash);
    public State<ImageSource?> OrbitDecoration { get; } = new(null);
    public State<bool> StartupRetry { get; } = new(false);
    public State<string> StartupMessage { get; } = new("");
    public State<(float Width, float Height)?> Dimensions { get; } = new(null);
}
