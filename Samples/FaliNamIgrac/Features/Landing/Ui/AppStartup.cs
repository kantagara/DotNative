namespace FaliNamIgrac;

internal sealed class AppStartup
{
    private readonly UiContext ui;
    private readonly SessionNotifier session;
    private readonly RegistrationFormNotifier registrationForm;
    private readonly OrbitPlayer orbit;
    private int startupAttempt;

    public AppStartup(
        UiContext ui,
        SessionNotifier session,
        RegistrationFormNotifier registrationForm,
        OrbitPlayer orbit
    )
    {
        this.ui = ui;
        this.session = session;
        this.registrationForm = registrationForm;
        this.orbit = orbit;
    }

    public async Task Run()
    {
        var attempt = ++startupAttempt;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        ui.Presentation.StartupRetry.Value = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ui.Lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var language = await ui.Preferences.GetAsync("selected_local", "", timeout.Token);
            Console.WriteLine(
                $"[FNI startup] preferences ready elapsedMs={elapsed.ElapsedMilliseconds}"
            );
            var needsLanguage = string.IsNullOrWhiteSpace(language);
            ui.Presentation.Locale.Value = PresentationLocale.Normalize(language);
            ui.Presentation.StartupMessage.Value = ui.Text.Loading;
#if DEBUG
            var preview = Environment.GetEnvironmentVariable("DOTNATIVE_FNI_PREVIEW_PAGE");
            var previewPage = preview switch
            {
                "onboarding" or "onboarding-error" => ApplicationScreen.Onboarding,
                "reset-password" => ApplicationScreen.ResetPassword,
                "new-password" => ApplicationScreen.NewPassword,
                "registration" => ApplicationScreen.Registration,
                "login" => ApplicationScreen.Login,
                _ => (ApplicationScreen?)null,
            };
            if (previewPage is { } page)
            {
                ui.Router.Go(page);
                if (preview == "onboarding-error")
                    registrationForm.RegistrationError =
                        "Preview: profile registration request failed.";
                return;
            }
#endif
            await session.RestoreAsync(timeout.Token);
            Console.WriteLine(
                $"[FNI startup] session ready elapsedMs={elapsed.ElapsedMilliseconds} state={session.State.GetType().Name}"
            );
            if (ui.Lifetime.IsCancellationRequested || attempt != startupAttempt)
                return;
            if (session.State is SessionState.SignedOut)
            {
                ui.Router.Go(ApplicationScreen.Landing);
                orbit.Start(ui);
                ui.Presentation.LanguagePopup.Value = needsLanguage;
            }
        }
        catch (OperationCanceledException) when (ui.Lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (!ui.Lifetime.IsCancellationRequested && attempt == startupAttempt)
            {
                ui.Presentation.StartupMessage.Value = ui.Text.StartupFailure;
                ui.Presentation.StartupRetry.Value = true;
            }
        }
        catch (Exception error)
        {
            if (!ui.Lifetime.IsCancellationRequested && attempt == startupAttempt)
            {
                ui.Presentation.StartupMessage.Value = ui.Text.StartupFailure;
                ui.Presentation.StartupRetry.Value = true;
                Console.Error.WriteLine(error);
            }
        }
    }
}
