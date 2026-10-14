namespace FaliNamIgrac;

/// <summary>Integrator can supply generated localization through SettingsNotifier.Texts.</summary>
public sealed record SettingsTexts(string SaveFailed)
{
    public static SettingsTexts ForLocale(string? locale) =>
        PresentationLocale.Normalize(locale) switch
        {
            "sr" => new("Čuvanje podešavanja nije potvrđeno. Pokušaj ponovo."),
            "ru" => new("Сохранение настройки не подтверждено. Попробуйте ещё раз."),
            _ => new("Saving the setting could not be confirmed. Try again."),
        };
}
