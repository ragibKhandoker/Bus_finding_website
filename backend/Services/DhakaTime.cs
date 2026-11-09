using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartCommute.Server.Services;

// Bangladesh Standard Time (Asia/Dhaka, UTC+6, no daylight saving). Times are stored in UTC and shown and typed in
// Dhaka time, whatever time zone the server runs in.
public static partial class DhakaTime
{
    public static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Dhaka", TimeSpan.FromHours(6), "Bangladesh Standard Time", "Bangladesh Standard Time");
        }
    }

    public static DateTime ToUtc(DateTime dhakaLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dhakaLocal, DateTimeKind.Unspecified), Zone);

    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    // "2026-11-10T08:30+06:00": the Dhaka time with its offset, so clients never have to guess the zone.
    public static string Format(DateTime utc) =>
        FromUtc(utc).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) + "+06:00";

    // "08:30" in Dhaka time, for messages.
    public static string Clock(DateTime utc) => FromUtc(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetSuffix();

    private static readonly string[] LocalFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

    // A time typed by the user. Without an offset ("2026-11-10T08:30") it is Dhaka time; with one ("…Z",
    // "…+06:00") it is converted. Returns the UTC time, or null when the text is not a time.
    public static DateTime? ParseToUtc(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (OffsetSuffix().IsMatch(value))
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset) ? withOffset.UtcDateTime : null;
        }
        return DateTime.TryParseExact(value, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ? ToUtc(local) : null;
    }
}
