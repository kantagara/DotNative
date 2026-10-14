using DotNative;

namespace FaliNamIgrac;

public sealed partial class AppRoutes
{
    private readonly UiContext ui;
    private readonly SessionNotifier session;
    private readonly CatalogNotifier catalog;
    private readonly HomeScreen home;
    private readonly MyAppointmentsNotifier? appointments;
    private readonly List<IDisposable> subscriptions = [];
    private string? lastGame;
    private long generation;
    private Task refresh = Task.CompletedTask;

    private int? UserId() =>
        session.State is SessionState.SignedIn signed
        && int.TryParse(signed.Profile.Id, out var id)
        && id > 0
            ? id
            : null;

    private void FollowDetails()
    {
        var id = catalog.State.Details?.Game.Id;
        if (id == lastGame)
            return;
        lastGame = id;
        platformActions?.Invalidate();
        if (id is null)
            appointments?.Clear();
        else if (appointments is not null)
            _ = appointments.OpenAsync(id, ui.Lifetime.Token);
    }

    private void QueueRefresh(string id, bool deleted)
    {
        var epoch = generation;
        var prior = refresh;
        refresh = RefreshAsync();
        async Task RefreshAsync()
        {
            try
            {
                await prior;
                while (catalog.Busy)
                    await Task.Delay(25, ui.Lifetime.Token);
                if (epoch != generation)
                    return;
                Task operation = Task.CompletedTask;
                await ui.Dispatcher.InvokeAsync(() =>
                {
                    if (epoch != generation)
                        return;
                    if (deleted)
                        catalog.BackToList();
                    else if (catalog.State.Details?.Game.Id == id)
                        operation = catalog.OpenDetailsAsync(id, ui.Lifetime.Token, false);
                });
                await operation;
                await ui.Dispatcher.InvokeAsync(() =>
                {
                    if (epoch != generation)
                        return;
                    var state = catalog.State;
                    operation = state.MyGames is null
                        ? Task.CompletedTask
                        : catalog.LoadMyGamesAsync(
                            state.MySport,
                            state.MyPage,
                            state.MyPassed,
                            ui.Lifetime.Token
                        );
                });
                await operation;
                await ui.Dispatcher.InvokeAsync(() =>
                {
                    operation =
                        epoch == generation ? home.RefreshSearchAsync() : Task.CompletedTask;
                });
                await operation;
            }
            catch (OperationCanceledException) when (ui.Lifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                await ui.Dispatcher.InvokeAsync(() =>
                {
                    if (epoch == generation)
                        catalog.ReportError(FniErrors.UserMessage(ui.Text, error));
                });
            }
        }
    }
}
