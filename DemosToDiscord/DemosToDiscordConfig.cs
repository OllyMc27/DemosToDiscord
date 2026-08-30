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
    public bool ImportLegacyStateFile { get; set; } = true;
    public bool AddPlayerNotesOnReview { get; set; } = true;
    public bool AddPlayerNotesOnAssignment { get; set; }
    public bool AddPlayerNotesOnPenalty { get; set; } = true;
    public ProactiveDetectionConfig ProactiveDetection { get; set; } = new();
    // Used only for one-time import and emergency fallback; cases are stored in Database.db.
    public string StateFilePath { get; set; } = "Configuration/DemosToDiscordCases.json";
    public string TimeZone { get; set; } = EvidenceTime.DefaultTimeZoneId;

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
    public bool? EnableProactiveDetection { get; set; }
}

public sealed class ProactiveDetectionConfig
{
    public bool Enabled { get; set; }
    public bool EvaluateOnMatchEnd { get; set; } = true;
    public bool EvaluateOnDisconnect { get; set; } = true;
    public double MinimumCaseRiskScore { get; set; } = 50;
    public double MinimumSignalPercentile { get; set; } = 0.975;
    public int MinimumTrackedHits { get; set; } = 100;
    public int MinimumPositiveEvents { get; set; } = 12;
    public int MinimumPopulationSize { get; set; } = 30;
    public int FullConfidenceTrackedHits { get; set; } = 300;
    public int FullConfidencePopulationSize { get; set; } = 100;
    public int RepeatHistoryDays { get; set; } = 30;
    public int EvaluationDelaySeconds { get; set; } = 15;
    public int BaselineRefreshMinutes { get; set; } = 5;
    public int FullBaselineRebuildHours { get; set; } = 168;
    public int MaximumIncrementalEvents { get; set; } = 250000;
    public int MaxConcurrentEvaluations { get; set; } = 1;
}

