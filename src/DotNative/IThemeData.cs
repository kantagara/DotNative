namespace DotNative;

/// <summary>Read-only theme values consumed by components.</summary>
public interface IThemeData
{
    ThemeMode Mode { get; }
    Color Primary { get; }
    Color OnPrimary { get; }
    Color Background { get; }
    Color Foreground { get; }
    Color Surface { get; }
    Color SecondaryBackground { get; }
    Color MutedForeground { get; }
    Color Error { get; }
    Color ErrorSurface { get; }
    Color Success { get; }
    TextTheme Text { get; }
    float FontSize { get; }
    float CornerRadius { get; }
}
