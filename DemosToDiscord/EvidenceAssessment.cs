namespace DemosToDiscord;

public static class EvidenceAssessment
{
    public static EvidenceConfidence Confidence(EvidenceCase item)
    {
        if (item.AntiCheat is not null && item.Reports.Count > 0)
            return new EvidenceConfidence("Corroborated", "Report and automated detection", 5);
        if (item.CommunitySignals.Count > 0 && (item.AntiCheat is not null || item.Reports.Count > 0))
            return new EvidenceConfidence("Corroborated", "Administrator-resolved community signal plus independent evidence", 5);
        if (item.ProactiveDetections.Count > 0 && (item.AntiCheat is not null || item.Reports.Count > 0))
            return new EvidenceConfidence("Corroborated", "Statistical review plus independent evidence", 5);
        if (item.AntiCheat is not null)
            return new EvidenceConfidence("Automated detection", "Anti-cheat evidence captured", 4);
        if (item.Reports.Count > 1)
            return new EvidenceConfidence("Multiple reports", $"{item.Reports.Count} reports grouped", 3);
        if (item.CommunitySignals.Count > 0)
            return new EvidenceConfidence("Community signal", "Target resolved by an administrator; human demo review required", 2);
        if (item.ProactiveDetections.MaxBy(detection => detection.RiskScore) is { } proactive)
            return new EvidenceConfidence(
                $"Proactive {proactive.RiskLevel}",
                $"Explainable statistical risk {proactive.RiskScore}/100 — human review required",
                proactive.RiskScore >= 65 ? 4 : 3);
        if (item.DemoSupport is not (DemoSupportStatus.Supported or DemoSupportStatus.Unknown))
            return new EvidenceConfidence("Metadata only", "Demo recording unsupported", 1);
        return new EvidenceConfidence("Single report", "One player report", 2);
    }

    public static DemoCapability DemoCapability(
        EvidenceCase item,
        DemosToDiscordConfig config,
        DemosToDiscordServerOverride? serverOverride)
    {
        if (serverOverride?.SupportsDemos == false)
            return new DemoCapability(DemoSupportStatus.DisabledByServer, "Demo capture is disabled for this server.");
        if (serverOverride?.SupportsDemos == true)
            return new DemoCapability(DemoSupportStatus.Supported, "Demo capture is enabled by the server override.");

        if (!config.SupportedDemoGames.Any(game => game.Equals(item.Game, StringComparison.OrdinalIgnoreCase)))
            return new DemoCapability(DemoSupportStatus.UnsupportedGame, $"{item.Game} demo capture is not supported.");

        if (item.Game.Equals("T5", StringComparison.OrdinalIgnoreCase) &&
            (config.T5ZombieMapPrefixes.Any(prefix => item.Map.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
             config.T5ZombieModes.Any(mode => item.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))))
        {
            return new DemoCapability(DemoSupportStatus.UnsupportedMode, "T5 Zombies does not provide a supported demo recording.");
        }

        return new DemoCapability(DemoSupportStatus.Supported, "Demo recording is supported.");
    }
}

