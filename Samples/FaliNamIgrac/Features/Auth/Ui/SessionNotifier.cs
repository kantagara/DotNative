using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed class SessionNotifier(
    IUiDispatcher dispatcher,
    ISessionService session,
    IRegistrationService registration,
    IGamesApi api
) : Notifier<SessionState>(dispatcher, new SessionState.SignedOut())
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private CancellationTokenSource operations = new();

    private Task RunSession(CancellationToken token, Func<CancellationToken, Task> operation) =>
        RunExclusive(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                operations.Token
            );
            await operation(linked.Token);
        });

    private Task PublishAsync(SessionState destination, CancellationToken token) =>
        uiDispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            SetState(destination);
        });

    public Task RestoreAsync(CancellationToken token) =>
        RunSession(
            token,
            async token =>
            {
                if (await session.RestoreAsync(token))
                    await ResolveAuthenticatedCoreAsync(token, restoringSession: true);
                else
                {
                    token.ThrowIfCancellationRequested();
                    await PublishAsync(new SessionState.SignedOut(), token);
                }
            }
        );

    public Task SignInAsync(string email, string password, CancellationToken token) =>
        RunSession(
            token,
            async token =>
            {
                await session.SignInAsync(email, password, token);
                await ResolveAuthenticatedCoreAsync(token, authType: AccountAuthType.Email);
            }
        );

    public Task ResolveAuthenticatedAsync(CancellationToken token) =>
        RunSession(token, token => ResolveAuthenticatedCoreAsync(token));

    public Task CompleteSocialSignInAsync(
        AccountAuthType authType,
        Func<CancellationToken, Task<AccountInfo>> authenticate,
        CancellationToken token
    ) =>
        RunSession(
            token,
            async token =>
            {
                var account = await authenticate(token);
                await ResolveAuthenticatedCoreAsync(token, authType: authType, account: account);
            }
        );

    private async Task ResolveAuthenticatedCoreAsync(
        CancellationToken token,
        bool restoringSession = false,
        AccountAuthType? authType = null,
        AccountInfo? account = null
    )
    {
        account ??= await session.GetAccountInfoAsync(token);
        if (restoringSession && await registration.RestoreTemporaryUserAsync(token) is { } saved)
        {
            token.ThrowIfCancellationRequested();
            await PublishAsync(new SessionState.Onboarding(saved), token);
            return;
        }
        var identity = await registration.ResolveSignedInUserAsync(
            account,
            authType,
            restoringSession,
            token
        );
        SessionState destination = identity is not null
            ? new SessionState.Onboarding(identity)
            : new SessionState.SignedIn(await api.ProfileAsync(token));
        token.ThrowIfCancellationRequested();
        await PublishAsync(destination, token);
    }

    public async Task SignOutAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        operations.Cancel();
        operations.Dispose();
        operations = new();
        await PublishAsync(new SessionState.SignedOut(), CancellationToken.None);
        await session.SignOutAsync(CancellationToken.None);
    }

    public void UpdateProfile(PlayerProfile profile)
    {
        if (!Busy && State is SessionState.SignedIn current && current.Profile.Id == profile.Id)
            SetState(new SessionState.SignedIn(profile));
    }

    public void CompleteOnboarding(PlayerProfile profile)
    {
        if (State is SessionState.Onboarding && session.IsSignedIn)
            SetState(new SessionState.SignedIn(profile));
    }

    public void CompleteOnboarding(RegistrationIdentity identity, PlayerProfile profile)
    {
        if (
            State is SessionState.Onboarding current
            && current.Identity == identity
            && session.IsSignedIn
        )
            SetState(new SessionState.SignedIn(profile));
    }

    public Task ConfirmRegistrationAsync(
        Func<Task<RegistrationIdentity?>> confirm,
        CancellationToken token
    ) => ConfirmRegistrationAsync(_ => confirm(), token);

    public Task ConfirmRegistrationAsync(
        Func<CancellationToken, Task<RegistrationIdentity?>> confirm,
        CancellationToken token
    ) =>
        RunSession(
            token,
            async token =>
            {
                var identity = await confirm(token);
                token.ThrowIfCancellationRequested();
                if (identity is not null)
                    await uiDispatcher.InvokeAsync(() => PublishIdentity(identity, token));
            }
        );

    private void PublishIdentity(RegistrationIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SetState(new SessionState.Onboarding(identity));
    }
}
