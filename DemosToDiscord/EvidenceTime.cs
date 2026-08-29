using System.Globalization;

namespace DemosToDiscord;

internal static class EvidenceTime
{
    private static readonly TimeZoneInfo UkTimeZone = ResolveUkTimeZone();

    public static string FormatUk(DateTime value) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(value), UkTimeZone)
            .ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string FormatUk(DateTime? value, string fallback = "Unknown") =>
        value is null ? fallback : FormatUk(value.Value);

    public static string MatchOffset(DateTime value, DateTime? matchStartedAtUtc)
    {
        if (matchStartedAtUtc is null)
            return "Match position unavailable";

        var elapsed = AsUtc(value) - AsUtc(matchStartedAtUtc.Value);
        if (elapsed < TimeSpan.Zero)
            return "Present before match started";

        var totalHours = (int)elapsed.TotalHours;
        return totalHours > 0
            ? $"{totalHours}h {elapsed.Minutes}m {elapsed.Seconds}s into match"
            : elapsed.Minutes > 0
                ? $"{elapsed.Minutes}m {elapsed.Seconds}s into match"
                : $"{elapsed.Seconds}s into match";
    }

    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static TimeZoneInfo ResolveUkTimeZone()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
