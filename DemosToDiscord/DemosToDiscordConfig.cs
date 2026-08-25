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
    public bool RenameOnUpload { get; set; } = true;

    public bool EnableWebfrontDashboard { get; set; } = true;
    public EFClient.Permission WebfrontMinimumPermission { get; set; } = EFClient.Permission.Moderator;
    public bool StoreReportReasons { get; set; } = true;
    public int CaseRetentionDays { get; set; } = 90;
    public int MaxStoredCases { get; set; } = 500;
    public string StateFilePath { get; set; } = "Configuration/DemosToDiscordCases.json";

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
}

