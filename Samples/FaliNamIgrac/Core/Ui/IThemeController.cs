using DotNative;

namespace FaliNamIgrac;

public interface IThemeController
{
    State<IThemeData> Current { get; }
    Task LoadAsync(CancellationToken token);
    Task SetAsync(ThemeMode mode, CancellationToken token);
}
