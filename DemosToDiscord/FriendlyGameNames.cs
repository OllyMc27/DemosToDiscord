using System.Globalization;
using SharedLibraryCore;
using SharedLibraryCore.Configuration;

namespace DemosToDiscord;

public sealed class FriendlyGameNames(DefaultSettings defaults)
{
    public FriendlyMatchName Resolve(string? game, string? map, string? mode)
    {
        var rawMap = Clean(map);
        var rawMode = Clean(mode);
        if (!Enum.TryParse<Server.Game>(game, true, out var parsedGame))
            return new FriendlyMatchName(Humanize(rawMap), Humanize(rawMode), rawMap, rawMode);

        var friendlyMap = defaults.Maps?
            .FirstOrDefault(item => item.Game == parsedGame)?.Maps?
            .FirstOrDefault(item => item.Name.Equals(rawMap, StringComparison.OrdinalIgnoreCase))?.Alias;
        var friendlyMode = defaults.Gametypes?
            .FirstOrDefault(item => item.Game == parsedGame)?.Gametypes?
            .FirstOrDefault(item => item.Name.Equals(rawMode, StringComparison.OrdinalIgnoreCase))?.Alias;
        return new FriendlyMatchName(
            string.IsNullOrWhiteSpace(friendlyMap) ? Humanize(rawMap) : friendlyMap,
            string.IsNullOrWhiteSpace(friendlyMode) ? Humanize(rawMode) : friendlyMode,
            rawMap,
            rawMode);
    }

    private static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

    private static string Humanize(string value)
    {
        if (value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return value;
        var cleaned = value;
        foreach (var prefix in new[] { "mp_", "zm_", "zombie_" })
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..];
                break;
            }
        cleaned = cleaned.Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }
}

public sealed record FriendlyMatchName(
    string Map,
    string Mode,
    string RawMap,
    string RawMode)
{
    public string Display => $"{Map} · {Mode}";
    public string RawDisplay => $"{RawMap} / {RawMode}";
}
