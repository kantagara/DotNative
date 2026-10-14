using System.Globalization;

namespace FaliNamIgrac;

/// <summary>App display conventions; never use these formats for backend payloads.</summary>
public static class PresentationLocale
{
    public static string Normalize(string? locale) =>
        locale?.Trim().Replace('_', '-').Split('-')[0].ToLowerInvariant() switch
        {
            "sr" => "sr",
            "ru" => "ru",
            _ => "en",
        };

    public static AppLocalizations Text(string? locale) =>
        AppLocalizations.ForLocale(Normalize(locale));

    public static CultureInfo Culture(string? locale) =>
        CultureInfo.GetCultureInfo(
            Normalize(locale) switch
            {
                "sr" => "sr-Latn-RS",
                "ru" => "ru-RU",
                _ => "en-GB",
            }
        );

    public static string Date(DateOnly date, string? locale) => date.ToString("d", Culture(locale));

    // Caller chooses the event/device time zone explicitly. Do not shift date-only birthdays.
    public static string Timestamp(DateTimeOffset instant, TimeZoneInfo zone, string? locale) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("g", Culture(locale));

    public static string Price(decimal amount, string currency, string? locale) =>
        amount.ToString("#,0.############################", Culture(locale)) + " " + currency;
}
