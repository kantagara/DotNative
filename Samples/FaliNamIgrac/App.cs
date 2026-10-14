using DotNative;
using DotNative.Plugins;

namespace FaliNamIgrac;

public sealed partial class App : ScreenComponent, IDisposable
{
    private readonly SessionNotifier session;
    private readonly AppRoutes routes;
    private readonly OrbitPlayer orbit = new();
    private readonly AppStartup startup;
    private bool appDisposed;

    public App(
        UiContext ui,
        AuthRouter router,
        SessionNotifier session,
        RegistrationFormNotifier registrationForm,
        AppRoutes routes
    )
        : base(ui)
    {
        this.session = session;
        this.routes = routes;
        startup = new(ui, session, registrationForm, orbit);
        router.Attach(routes.Host, () => LoginPlayer.Restart(ui), () => startup.Run());
        _ = ui.Themes.LoadAsync(lifetime.Token);
        if (target.Platform is NativePlatform.IOS or NativePlatform.Android)
        {
            orbit.Start(ui);
            _ = startup.Run();
        }
    }

    public override Element Build(BuildContext context) => Compose();

    public override Element Build() => Compose();

    private Element Compose()
    {
        _ = session.State;
        return new Theme(ui.Themes.Current.Value, routes.Gates);
    }

    public void Dispose()
    {
        if (appDisposed)
            return;
        appDisposed = true;
        lifetime.Cancel();
        try
        {
            routes.Dispose();
        }
        finally
        {
            orbit.Dispose();
            ownedUi?.Dispose();
        }
    }
}
