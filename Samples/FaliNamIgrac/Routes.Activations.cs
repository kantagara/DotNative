using DotNative;

namespace FaliNamIgrac;

public sealed partial class AppRoutes
{
    private IApplicationActivations? activationSource;
    private NotificationNavigationNotifier? notificationNavigation;
    private Action<ApplicationActivation>? activationHandler;
    private bool activationsDisposed;

    public void ConnectActivations(
        IApplicationActivations source,
        IChatService chats,
        IGamesApi games,
        IMyAppointmentsService appointmentsApi
    )
    {
        activationSource = source;
        var resolver = new AuthenticatedNotificationResolver(
            chats,
            games,
            UserId,
            home.OpenNotificationChat,
            details => catalog.ShowResolvedDetails(details),
            async (user, id, token) =>
            {
                var eligibility = await appointmentsApi.LoadAsync(id, user, token);
                if (!eligibility.Allows(AppointmentAction.Rate, user, DateTimeOffset.UtcNow))
                    return null;
                var details = await games.DetailsAsync(id, token);
                if (details.Game.Id != id)
                    return null;
                return () =>
                {
                    catalog.ShowResolvedDetails(details, canApply: false);
                    Host.Details.OpenRatings(id);
                };
            }
        );
        notificationNavigation = new(ui.Dispatcher, resolver);
        void Follow()
        {
            notificationNavigation.SetAccount(UserId());
            notificationNavigation.SetRouterReady(UserId() is not null);
            _ = notificationNavigation.DrainAsync(ui.Lifetime.Token);
        }
        activationHandler = activation =>
        {
            var receivedEpoch = notificationNavigation.Epoch;
            ui.Dispatcher.Post(() =>
            {
                if (activationsDisposed || ui.Lifetime.IsCancellationRequested)
                    return;
                NotificationIntent? intent;
                var valid =
                    activation.Kind == ApplicationActivationKind.Uri
                        ? NotificationPayload.TryParseUri(activation.Payload, out intent)
                        : NotificationPayload.TryParseLocal(activation.Payload, out intent);
                if (valid && notificationNavigation.Enqueue(intent!, receivedEpoch))
                    _ = notificationNavigation.DrainAsync(ui.Lifetime.Token);
            });
        };
        subscriptions.Add(
            notificationNavigation.Listen(() =>
            {
                if (
                    notificationNavigation.State.Status
                    is NotificationNavigationStatus.Error
                        or NotificationNavigationStatus.AccessDenied
                )
                    _ = ShowActivationError();
            })
        );
        source.Opened += activationHandler;
        subscriptions.Add(session.Listen(Follow));
        Follow();
        foreach (var activation in source.ReadPending())
            activationHandler(activation);
    }

    private async Task ShowActivationError()
    {
        try
        {
            await ui.Toast.ShowAsync(ui.Text.RequestFailed, ui.Lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Console.Error.WriteLine("Notification feedback: " + error.GetType().Name);
        }
    }
}
