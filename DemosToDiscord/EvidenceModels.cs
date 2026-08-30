namespace DemosToDiscord;

public enum EvidenceTriggerType
{
    Report,
    AutomatedBan,
    ManualBan,
    ProactiveDetection
}

public enum EvidenceCaseStatus
{
    Queued,
    Searching,
    WaitingForDemo,
    Uploading,
    Uploaded,
    NoDemo,
    Failed,
    DemoUnsupported
}

public enum DemoSupportStatus
{
    Unknown,
    Supported,
    UnsupportedGame,
    UnsupportedMode,
    DisabledByServer
}

public enum EvidenceHistoryAction
{
    Created,
    EvidenceAdded,
    Assigned,
    Unassigned,
    ReviewChanged,
    ReportsCleared,
    DiscordSynced,
    ProactiveDetectionAdded,
    PlayerNoteAdded,
    PenaltyLinked
}

public enum EvidenceReviewDecision
{
    Unreviewed,
    NeedsMoreReview,
    CheatingActionTaken,
    CheatingNoAction,
    NotCheatingNoAction,
    Inconclusive
}

public sealed class EvidenceCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public EvidenceCaseStatus Status { get; set; } = EvidenceCaseStatus.Queued;
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public long? LegacyServerId { get; set; }
    public string Game { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int TargetClientId { get; set; }
    public long TargetNetworkId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public List<ReportEvidence> Reports { get; set; } = [];
    public AntiCheatEvidence? AntiCheat { get; set; }
    public bool ManualBanObserved { get; set; }
    public bool ProactiveDetectionObserved { get; set; }
    public double? RiskScore { get; set; }
    public string? RiskLevel { get; set; }
    public string? DetectionConfidence { get; set; }
    public string? StrongestSignal { get; set; }
    public DateTime? LastProactiveDetectionAtUtc { get; set; }
    public List<DetectionSignal> DetectionSignals { get; set; } = [];
    public string? DemoFileName { get; set; }
    public long? DemoFileSize { get; set; }
    public DateTime? DemoStartedAtUtc { get; set; }
    public DateTime? PlayerJoinedAtUtc { get; set; }
    public DateTime? PlayerLeftAtUtc { get; set; }
    public string? DiscordMessageId { get; set; }
    public string? DiscordChannelId { get; set; }
    public string? DiscordGuildId { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public string? LastError { get; set; }
    public int Attempts { get; set; }
    public DemoSupportStatus DemoSupport { get; set; }
    public string? DemoSupportReason { get; set; }
    public EvidenceReviewDecision ReviewDecision { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? ReviewedByClientId { get; set; }
    public string? ReviewedByName { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReportsClearedAtUtc { get; set; }
    public int ReportsClearedCount { get; set; }
    public int? AssignedToClientId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? DiscordLastSyncedAtUtc { get; set; }
    public string? DiscordSyncError { get; set; }
    public List<EvidenceHistoryEntry> History { get; set; } = [];

    public IReadOnlyList<EvidenceTriggerType> TriggerTypes
    {
        get
        {
            var triggers = new List<EvidenceTriggerType>();
            if (Reports.Count > 0)
                triggers.Add(EvidenceTriggerType.Report);
            if (AntiCheat is not null)
                triggers.Add(EvidenceTriggerType.AutomatedBan);
            if (ManualBanObserved)
                triggers.Add(EvidenceTriggerType.ManualBan);
            if (ProactiveDetectionObserved)
                triggers.Add(EvidenceTriggerType.ProactiveDetection);
            return triggers;
        }
    }
}

public sealed class EvidenceHistoryEntry
{
    public DateTime WhenUtc { get; set; }
    public EvidenceHistoryAction Action { get; set; }
    public int? AdminClientId { get; set; }
    public string AdminName { get; set; } = "System";
    public string Summary { get; set; } = string.Empty;
    public EvidenceReviewDecision? Decision { get; set; }
    public string? Notes { get; set; }
    public int ReportsCleared { get; set; }
    public int? PenaltyId { get; set; }
    public int? PlayerNoteMetaId { get; set; }
}

public sealed class DetectionSignal
{
    public long SignalId { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string? Weapon { get; set; }
    public string? Map { get; set; }
    public double ObservedValue { get; set; }
    public double? ExpectedValue { get; set; }
    public double? Percentile { get; set; }
    public double? ExpectedMultiple { get; set; }
    public int SampleSize { get; set; }
    public int PopulationSize { get; set; }
    public double ConfidenceWeight { get; set; }
    public double RiskContribution { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ReportEvidence
{
    public int? PenaltyId { get; set; }
    public DateTime WhenUtc { get; set; }
    public int ReporterClientId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class AntiCheatEvidence
{
    public DateTime WhenUtc { get; set; }
    public string Detection { get; set; } = string.Empty;
    public int? PenaltyId { get; set; }
}

public sealed record PenaltyCapture(
    EvidenceTriggerType Trigger,
    DateTime WhenUtc,
    string ServerId,
    string ServerName,
    long? LegacyServerId,
    string Game,
    string Map,
    string Mode,
    int TargetClientId,
    long TargetNetworkId,
    string TargetName,
    int ReporterClientId,
    string ReporterName,
    string Reason,
    string Detection,
    int? PenaltyId = null);

public sealed record DemoCandidate(
    string DemoPath,
    string? JsonPath,
    string Map,
    string Mode,
    DateTime StartedAtUtc,
    bool TargetConfirmed,
    double Score);

public sealed record PlayerMatchTimeline(DateTime? JoinedAtUtc, DateTime? LeftAtUtc);

public sealed record DiscordAttachment(string Id, string FileName, string Url, long Size);

public sealed record DiscordMessageReceipt(
    string MessageId,
    string? ChannelId,
    string? GuildId,
    IReadOnlyList<DiscordAttachment> Attachments);

public sealed record DiscordDeliveryOptions(string? RoleId, bool MentionRole);

public sealed record DemoCapability(DemoSupportStatus Status, string Reason)
{
    public bool Supported => Status == DemoSupportStatus.Supported;
}

public sealed record EvidenceConfidence(string Label, string Detail, int Rank);

public sealed record AntiCheatMetricSnapshot(
    DateTime WhenUtc,
    int CurrentSessionLength,
    int TimeSinceLastEvent,
    double EloRating,
    int SessionScore,
    double SessionSpm,
    int Hits,
    int Kills,
    int Deaths,
    double CurrentStrain,
    double StrainAngleBetween,
    double SessionAngleOffset,
    double RecoilOffset,
    double SessionAverageSnapValue,
    int SessionSnapHits,
    string Weapon,
    string HitLocation,
    int HitType,
    string CapturedViewAngles,
    string CurrentViewAngle,
    string LastStrainAngle,
    string HitOrigin,
    string HitDestination,
    double Distance);

public sealed record AntiCheatCaseMetrics(
    int? PenaltyId,
    string Detection,
    IReadOnlyList<AntiCheatMetricSnapshot> Snapshots,
    PlayerEvidenceMetrics? PlayerMetrics);

public sealed record PlayerEvidenceMetrics(
    int Kills,
    int Deaths,
    double KillDeathRatio,
    double Performance,
    double ScorePerMinute,
    int TimePlayedSeconds,
    double ChestHitPercent,
    double AbdomenHitPercent,
    double ChestAbdomenRatioPercent,
    double HeadHitPercent,
    double AverageHitOffset,
    double MaximumStrain,
    double AverageSnapValue,
    int SnapHitCount);

public sealed record EvidenceStoreSnapshot(
    DateTime StartedAtUtc,
    int Queued,
    int Uploaded,
    int NoDemo,
    int Failed,
    int Unsupported,
    int Reports,
    int AutomatedBans,
    IReadOnlyList<EvidenceCase> Cases);

