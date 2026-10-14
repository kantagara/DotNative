using DotNative;
using WidgetSamples.Services;
using WidgetSamples.Widgets;

namespace WidgetSamples;

public sealed class App(Navigator navigator, DemoStore store, NavigationActions actions) : Component
{
#if DOTNATIVE_WINDOWS
    private readonly WindowsShell _shell = new(navigator, store, actions);

    public override Element Build(BuildContext context) => new Theme(store.Theme, _shell);

    private sealed class WindowsShell(
        Navigator navigator,
        DemoStore store,
        NavigationActions actions
    ) : Component
    {
        private readonly AdaptiveDockSample _dock = new(navigator, store, actions);

        public override Element Build(BuildContext context) =>
            _dock.Wrap(new NavigationHost(navigator), context);
    }
#else
    public override Element Build(BuildContext context) =>
        new Theme(store.Theme, new NavigationHost(navigator));
#endif
}
