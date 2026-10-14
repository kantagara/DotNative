namespace DotNative;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public interface ISystemAppearance
{
    // The OS preference, independent of this application's explicit override.
    ThemeMode Theme { get; }
}

internal sealed class SystemAppearance(ThemeMode initial) : ReactiveSource, ISystemAppearance
{
    private ThemeMode _theme = initial;
    public ThemeMode Theme
    {
        get
        {
            TrackRead();
            return _theme;
        }
    }

    internal void Update(ThemeMode theme)
    {
        VerifyCanWrite();
        if (theme is not (ThemeMode.Light or ThemeMode.Dark))
            throw new ArgumentOutOfRangeException(nameof(theme));
        if (_theme == theme)
            return;
        _theme = theme;
        PublishChange();
    }
}
