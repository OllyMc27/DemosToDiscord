using System.Globalization;

namespace DemosToDiscord;

internal static class EvidenceTime
{
    public const string DefaultTimeZoneId = "Europe/London";
    private static readonly object Sync = new();
    private static TimeZoneInfo _displayTimeZone = ResolveTimeZone(DefaultTimeZoneId) ?? TimeZoneInfo.Utc;
    private static string _configuredId = DefaultTimeZoneId;
    private static bool _usingFallback;

    public static string Label => IsUkTimeZone(_configuredId) ? "UK" : _configuredId;
    public static string ConfigurationLabel => _usingFallback
        ? $"UK (fallback from {_configuredId})"
        : Label;

    public static bool Configure(string? timeZoneId)
    {
        var requestedId = string.IsNullOrWhiteSpace(timeZoneId)
            ? DefaultTimeZoneId
            : timeZoneId.Trim();
        var resolved = ResolveTimeZone(requestedId);

        lock (Sync)
        {
            _configuredId = requestedId;
            _usingFallback = resolved is null;
            _displayTimeZone = resolved ?? ResolveTimeZone(DefaultTimeZoneId) ?? TimeZoneInfo.Utc;
        }

        return resolved is not null;
    }

    public static string Format(DateTime value) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(value), _displayTimeZone)
            .ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Format(DateTime? value, string fallback = "Unknown") =>
        value is null ? fallback : Format(value.Value);

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

    private static TimeZoneInfo? ResolveTimeZone(string id)
    {
        var ids = IsUkTimeZone(id)
            ? new[] { id, DefaultTimeZoneId, "GMT Standard Time" }
            : new[] { id };

        foreach (var candidate in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return null;
    }

    private static bool IsUkTimeZone(string id) =>
        id.Equals(DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase) ||
        id.Equals("GMT Standard Time", StringComparison.OrdinalIgnoreCase);
}
