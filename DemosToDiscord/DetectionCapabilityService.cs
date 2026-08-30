namespace DemosToDiscord;

public sealed record DetectionCapability(
    bool EligibleForScoring,
    bool HasCumulativePlayerStatistics,
    bool HasWeaponMarginals,
    bool HasHitLocationMarginals,
    bool HasJointWeaponHitEvents,
    bool HasAntiCheatSnapshots,
    bool HasExactShotsFired,
    string Reason);

public sealed class DetectionCapabilityService(DemosToDiscordConfig config)
{
    public DetectionCapability Resolve(
        string game,
        string map,
        string mode,
        DemosToDiscordServerOverride? serverOverride = null)
    {
        if (serverOverride?.EnableProactiveDetection == false)
            return Disabled("Proactive detection is disabled by the server override.");

        if (game.Equals("T5", StringComparison.OrdinalIgnoreCase) && IsT5Zombies(map, mode))
            return new DetectionCapability(
                false, true, true, true, false, false, false,
                "T5 Zombies is statistics-only and excluded from multiplayer cheat scoring.");

        if (game.Equals("T6", StringComparison.OrdinalIgnoreCase) ||
            game.Equals("IW5", StringComparison.OrdinalIgnoreCase))
        {
            return new DetectionCapability(
                true, true, true, true, true, true, false,
                "Tracked script-hit events support joint weapon/hit-location comparisons; exact shots fired are unavailable.");
        }

        if (game.Equals("T4", StringComparison.OrdinalIgnoreCase) ||
            game.Equals("T5", StringComparison.OrdinalIgnoreCase))
        {
            return new DetectionCapability(
                true, true, true, true, false, false, false,
                "Cumulative and marginal statistics are available; joint weapon/headshot and snapshot signals are unavailable.");
        }

        return Disabled($"{game} has no configured proactive metric capability.");
    }

    private bool IsT5Zombies(string map, string mode) =>
        config.T5ZombieMapPrefixes.Any(prefix => map.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
        config.T5ZombieModes.Any(item => mode.Equals(item, StringComparison.OrdinalIgnoreCase));

    private static DetectionCapability Disabled(string reason) =>
        new(false, false, false, false, false, false, false, reason);
}
