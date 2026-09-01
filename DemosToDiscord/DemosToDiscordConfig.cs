using Data.Models.Client;

namespace DemosToDiscord;

public sealed class DemosToDiscordConfig
{
    public bool Enabled { get; set; } = true;
    public string Webhook { get; set; } = string.Empty;
    public string T5DemoPath { get; set; } =
        @"C:\Users\Administrator\AppData\Local\Plutonium\storage\t5\demos";
    public string T6DemoPath { get; set; } =
        @"C:\Users\Administrator\AppData\Local\Plutonium\storage\t6\demos";

    public bool UploadOnReports { get; set; } = true;
    public bool UploadOnAutomatedBans { get; set; } = true;
    public bool UploadOnManualBans { get; set; }
    public List<string> AutomatedBanGames { get; set; } = ["T6"];
    public List<string> SupportedDemoGames { get; set; } = ["T5", "T6"];
    public List<string> T5ZombieMapPrefixes { get; set; } = ["zombie_"];
    public List<string> T5ZombieModes { get; set; } = ["zclassic", "zstandard", "zombie"];

    public int MaxLookbackMinutes { get; set; } = 90;
    public int MaxWaitMinutes { get; set; } = 30;
    public int RetryIntervalSeconds { get; set; } = 10;
    public int PostMatchDelaySeconds { get; set; } = 10;
    public int FileStableChecks { get; set; } = 3;
    public int MaxConcurrentUploads { get; set; } = 2;
    public int DeduplicationWindowMinutes { get; set; } = 120;
    public bool EnableWebfrontDashboard { get; set; } = true;
    public EFClient.Permission WebfrontMinimumPermission { get; set; } = EFClient.Permission.Moderator;
    public bool StoreReportReasons { get; set; } = true;
    public int CaseRetentionDays { get; set; } = 90;
    public int MaxStoredCases { get; set; } = 500;
    public string StateFilePath { get; set; } = "Configuration/DemosToDiscordCases.json";
    public string TimeZone { get; set; } = EvidenceTime.DefaultTimeZoneId;

    // Proactive review uses live IW4MAdmin statistics. It never administers penalties.
    public bool EnableProactiveDetection { get; set; } = true;
    public int ProactiveBaselineRefreshMinutes { get; set; } = 5;
    public string ProactiveBaselineStateFilePath { get; set; } =
        "Configuration/DemosToDiscordProactiveBaselines.json";
    public int ProactiveMinimumPopulation { get; set; } = 100;
    public int ProactiveMinimumTrackedHits { get; set; } = 200;
    public int ProactiveMinimumHeadEvents { get; set; } = 10;
    public List<string> ProactiveExcludedGames { get; set; } = [];
    public List<long> ProactiveExcludedServerIds { get; set; } = [];
    public bool ProactiveExcludeT5Zombies { get; set; } = true;
    public int ProactiveCaseRiskThreshold { get; set; } = 50;
    public int ProactiveDiscordRiskThreshold { get; set; } = 65;
    public bool EnableProactiveDiscordNotifications { get; set; } = true;
    public int ProactiveRepeatHistoryWeight { get; set; } = 4;
    public int ProactiveEvaluationDelaySeconds { get; set; } = 20;
    public int ProactiveEvaluationDeduplicationMinutes { get; set; } = 30;
    public int ProactiveEvaluationQueueCapacity { get; set; } = 1000;

    // ServerPulse signals enter a human review workflow only. An inconclusive decision may apply IW4MAdmin's
    // native Flag level when explicitly enabled; statistical detection never calls this path by itself.
    public bool AcceptServerPulseCases { get; set; } = true;
    public bool FlagPlayerOnInconclusiveReview { get; set; } = true;
    public bool NotifyDiscordWhenFlaggedPlayerJoins { get; set; } = true;
    public int FlaggedPlayerJoinAlertCooldownMinutes { get; set; } = 15;
    public string FlaggedPlayerRoleId { get; set; } = string.Empty;

    public bool SendMetadataOnlyCasesToDiscord { get; set; } = true;
    public string ReportRoleId { get; set; } = string.Empty;
    public string AntiCheatRoleId { get; set; } = string.Empty;
    public bool MentionRolesOnlyWhenDemoReady { get; set; }
    public Dictionary<string, string> GameWebhooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Debug { get; set; }
    public Dictionary<string, DemosToDiscordServerOverride> ServerOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DemosToDiscordServerOverride
{
    public bool? Enabled { get; set; }
    public string? DemoPath { get; set; }
    public string? Webhook { get; set; }
    public bool? UploadOnReports { get; set; }
    public bool? UploadOnAutomatedBans { get; set; }
    public bool? UploadOnManualBans { get; set; }
    public bool? SupportsDemos { get; set; }
    public bool? SendMetadataOnlyCasesToDiscord { get; set; }
    public string? ReportRoleId { get; set; }
    public string? AntiCheatRoleId { get; set; }
    public bool? AcceptServerPulseCases { get; set; }
    public string? FlaggedPlayerRoleId { get; set; }
}

