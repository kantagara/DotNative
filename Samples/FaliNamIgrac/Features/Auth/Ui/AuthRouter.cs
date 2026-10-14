using DotNative.Plugins;
using DotNative.Toast;

namespace FaliNamIgrac;

// Navigation commands. Screens call these; they do not assign callbacks on a context.
public sealed class AuthRouter
{
    private readonly IPresentationState _presentation;
    private readonly SessionNotifier _session;
    private readonly ISessionService _sessions;
    private readonly SocialAuthentication _social;
    private readonly IToast _toast;
    private readonly LoginFormNotifier _login;
    private readonly RegistrationFormNotifier _registration;
    private readonly IRecoveryController _recovery;
    private RouteHost? _routes;
    private Action? _restartLoginMotion;
    private Func<Task>? _initialize;

    public AuthRouter(
        IPresentationState presentation,
        SessionNotifier session,
        ISessionService sessions,
        SocialAuthentication social,
        IToast toast,
        LoginFormNotifier login,
        RegistrationFormNotifier registration,
        IRecoveryController recovery
    )
    {
        _presentation = presentation;
        _session = session;
        _sessions = sessions;
        _social = social;
        _toast = toast;
        _login = login;
        _registration = registration;
        _recovery = recovery;
    }

    public void Attach(RouteHost routes, Action restartLoginMotion, Func<Task> initialize)
    {
        _routes = routes;
        _restartLoginMotion = restartLoginMotion;
        _initialize = initialize;
    }

    public void Go(ApplicationScreen page)
    {
        if (
            _routes is null
            || AuthLoading
            || _routes.Transitioning
            || page == _presentation.CurrentScreen.Value
        )
            return;
        _routes.LeaveCurrent();
        _presentation.CurrentScreen.Value = page;
    }

    public void OpenLogin()
    {
        if (_routes is null || _routes.Transitioning || AuthLoading)
            return;
        _login.Reset();
        Go(ApplicationScreen.Login);
        _restartLoginMotion?.Invoke();
    }

    public void OpenRegistration()
    {
        if (_routes is null || _routes.Transitioning || AuthLoading)
            return;
        _registration.Reset();
        Go(ApplicationScreen.Registration);
        _restartLoginMotion?.Invoke();
    }

    public void OpenRecovery()
    {
        _recovery.Initialize();
        Go(ApplicationScreen.ResetPassword);
    }

    public Task RetryStartupAsync() => _initialize?.Invoke() ?? Task.CompletedTask;

    public async Task SocialSignInAsync(string provider)
    {
        if (_session.Busy || _registration.RegistrationAuthenticating)
            return;
        try
        {
            var authType = provider == "Google" ? AccountAuthType.Google : AccountAuthType.Apple;
            await _session.CompleteSocialSignInAsync(
                authType,
                token => _social.SignInAsync(_sessions, provider, token),
                CancellationToken.None
            );
        }
        catch (PluginException error)
            when (error.Code is "authentication_cancelled" or "authentication_busy") { }
        catch (Exception error)
        {
            await _toast.ShowAsync(error.Message);
        }
    }

    private bool AuthLoading =>
        (_session.Busy && _session.State is not SessionState.SignedIn)
        || _registration.RegistrationAuthenticating;
}
