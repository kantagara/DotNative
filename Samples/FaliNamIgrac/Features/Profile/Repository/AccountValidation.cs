using System.Globalization;

namespace FaliNamIgrac;

public static class AccountValidation
{
    public static bool TryBirthDate(string? value, out DateOnly date)
    {
        var valid = DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed
        );
        date = DateOnly.FromDateTime(parsed);
        return valid;
    }

    public static bool Valid(AccountData data, DateOnly today) =>
        !string.IsNullOrWhiteSpace(data.Firstname)
        && !string.IsNullOrWhiteSpace(data.Lastname)
        && !string.IsNullOrWhiteSpace(data.Nickname)
        && TryBirthDate(data.BirthDate, out var date)
        && date >= new DateOnly(1930, 1, 1)
        && date <= today
        && double.IsFinite(data.Latitude)
        && data.Latitude is >= -90 and <= 90
        && double.IsFinite(data.Longitude)
        && data.Longitude is >= -180 and <= 180
        && (string.IsNullOrWhiteSpace(data.Phone) || ValidPhone(data.Phone))
        && data.Sports.Length > 0
        && data.Sports.Select(s => s.SportName).Distinct().Count() == data.Sports.Length
        && data.Sports.All(s =>
            !string.IsNullOrWhiteSpace(s.SportName)
            && s.SkillLevel is >= 1 and <= 5
            && s.NotificationType is "ON" or "OFF"
        );

    private static bool ValidPhone(string phone)
    {
        var digits = new string(phone.Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());
        if (digits.StartsWith('+'))
            digits = digits[1..];
        return digits.Length is >= 6 and <= 15 && digits.All(char.IsAsciiDigit);
    }
}
