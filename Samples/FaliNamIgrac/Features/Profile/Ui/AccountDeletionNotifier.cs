using DotNative;
using DotNative.Composition;

namespace FaliNamIgrac;

public sealed record AccountDeletionState(
    bool Confirming = false,
    AccountDeletionStage Stage = AccountDeletionStage.None,
    bool Failed = false,
    bool Completed = false
);

public sealed class AccountDeletionNotifier(
    IUiDispatcher dispatcher,
    SessionNotifier session,
    AccountDeletionWorkflow workflow
) : Notifier<AccountDeletionState>(dispatcher, new()), IDisposable
{
    private readonly IUiDispatcher uiDispatcher = dispatcher;
    private string? confirmedId;
    private IDisposable? confirmationSubscription;

    public void Dispose()
    {
        confirmationSubscription?.Dispose();
        confirmationSubscription = null;
    }

    public void AskConfirmation()
    {
        if (Busy || session.State is not SessionState.SignedIn signed)
            return;
        Dispose();
        confirmedId = signed.Profile.Id;
        confirmationSubscription = session.Listen(() =>
        {
            if (
                session.State is not SessionState.SignedIn current
                || current.Profile.Id != signed.Profile.Id
            )
            {
                confirmedId = null;
                SetState(new());
            }
        });
        SetState(new(Confirming: true));
    }

    public void Cancel()
    {
        if (!Busy && State.Stage < AccountDeletionStage.BackendDeleted)
        {
            Dispose();
            confirmedId = null;
            SetState(new());
        }
    }

    public Task ConfirmAsync(CancellationToken token) =>
        RunExclusive(async () =>
        {
            if (
                !State.Confirming
                || session.State is not SessionState.SignedIn signed
                || signed.Profile.Id != confirmedId
            )
                return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            var subscription = session.Listen(() =>
            {
                if (
                    session.State is not SessionState.SignedIn current
                    || current.Profile.Id != signed.Profile.Id
                )
                {
                    linked.Cancel();
                    confirmedId = null;
                    SetState(new());
                }
            });
            SetState(State with { Failed = false });
            try
            {
                await workflow.RunAsync(
                    signed.Profile.Id,
                    stage =>
                        uiDispatcher.Post(() =>
                        {
                            if (!linked.IsCancellationRequested)
                                SetState(State with { Stage = stage });
                        }),
                    linked.Token
                );
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (!linked.IsCancellationRequested)
                        SetState(State with { Completed = true, Confirming = false });
                });
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
            catch
            {
                await uiDispatcher.InvokeAsync(() =>
                {
                    if (!linked.IsCancellationRequested)
                        SetState(State with { Failed = true });
                });
            }
            finally
            {
                await uiDispatcher.InvokeAsync(subscription.Dispose);
            }
        });
}
