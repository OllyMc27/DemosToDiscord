using System.Data.Common;
using System.Globalization;
using Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

/// <summary>
/// Owns the plugin schema inside IW4MAdmin's configured database. IW4MAdmin's
/// compiled DatabaseContext cannot be extended by a runtime plugin, so this
/// repository deliberately uses its EF-managed connection and transactions.
/// </summary>
public sealed class DemosToDiscordDatabase(
    IDatabaseContextFactory contextFactory,
    ILogger<DemosToDiscordDatabase> logger)
{
    private const int CurrentSchemaVersion = 2;

    public async Task InitializeAsync(CancellationToken token)
    {
        await using var context = contextFactory.CreateContext();
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        var connection = context.Database.GetDbConnection();
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON;", token);
        await ExecuteAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS DemosToDiscordSchemaMigrations (
                MigrationId INTEGER NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );
            """, token);

        var version = await ScalarIntAsync(
            connection,
            null,
            "SELECT COALESCE(MAX(MigrationId), 0) FROM DemosToDiscordSchemaMigrations;",
            token);
        if (version < 1)
            await ApplyInitialMigrationAsync(connection, token);
        if (version < 2)
            await ApplyBaselineMigrationAsync(connection, token);
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"The DemosToDiscord database schema is newer than this plugin supports ({version} > {CurrentSchemaVersion}).");
    }

    public async Task<IReadOnlyList<EvidenceCase>> LoadCasesAsync(CancellationToken token)
    {
        await using var context = contextFactory.CreateContext(false);
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        var connection = context.Database.GetDbConnection();
        var cases = new Dictionary<string, EvidenceCase>(StringComparer.OrdinalIgnoreCase);

        await using (var command = CreateCommand(connection, null, """
            SELECT Id, CreatedAtUtc, UpdatedAtUtc, Status, ServerId, ServerName, LegacyServerId,
                   Game, Map, Mode, TargetClientId, TargetNetworkId, TargetName,
                   AntiCheatWhenUtc, AntiCheatDetection, AntiCheatPenaltyId, ManualBanObserved,
                   ProactiveDetectionObserved, RiskScore, RiskLevel, DetectionConfidence,
                   StrongestSignal, LastProactiveDetectionAtUtc, DemoFileName, DemoFileSize,
                   DemoStartedAtUtc, PlayerJoinedAtUtc, PlayerLeftAtUtc, DiscordMessageId,
                   DiscordChannelId, DiscordGuildId, UploadedAtUtc, LastError, Attempts,
                   DemoSupport, DemoSupportReason, ReviewDecision, ReviewedAtUtc,
                   ReviewedByClientId, ReviewedByName, ReviewNotes, ReportsClearedAtUtc,
                   ReportsClearedCount, AssignedToClientId, AssignedToName, AssignedAtUtc,
                   DiscordLastSyncedAtUtc, DiscordSyncError
            FROM DemosToDiscordCases
            ORDER BY UpdatedAtUtc DESC;
            """))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var evidenceCase = ReadCase(reader);
                cases[evidenceCase.Id] = evidenceCase;
            }
        }

        if (cases.Count == 0)
            return [];

        await LoadReportsAsync(connection, cases, token);
        await LoadHistoryAsync(connection, cases, token);
        await LoadSignalsAsync(connection, cases, token);
        return cases.Values.OrderByDescending(item => item.UpdatedAtUtc).ToList();
    }

    public async Task SaveCaseAsync(EvidenceCase evidenceCase, CancellationToken token)
    {
        await using var context = contextFactory.CreateContext();
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        var connection = context.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(token);
        try
        {
            await UpsertCaseAsync(connection, transaction, evidenceCase, token);
            await ReplaceReportsAsync(connection, transaction, evidenceCase, token);
            await ReplaceHistoryAsync(connection, transaction, evidenceCase, token);
            await ReplaceSignalsAsync(connection, transaction, evidenceCase, token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteCasesAsync(IEnumerable<string> caseIds, CancellationToken token)
    {
        var ids = caseIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
            return;

        await using var context = contextFactory.CreateContext();
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        var connection = context.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(token);
        try
        {
            foreach (var id in ids)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    "DELETE FROM DemosToDiscordCases WHERE Id = @id;",
                    token,
                    ("@id", id));
            }

            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ApplyInitialMigrationAsync(DbConnection connection, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        try
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE DemosToDiscordCases (
                    Id TEXT NOT NULL PRIMARY KEY,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    ServerId TEXT NOT NULL,
                    ServerName TEXT NOT NULL,
                    LegacyServerId INTEGER NULL,
                    Game TEXT NOT NULL,
                    Map TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    TargetClientId INTEGER NOT NULL,
                    TargetNetworkId INTEGER NOT NULL,
                    TargetName TEXT NOT NULL,
                    AntiCheatWhenUtc TEXT NULL,
                    AntiCheatDetection TEXT NULL,
                    AntiCheatPenaltyId INTEGER NULL,
                    ManualBanObserved INTEGER NOT NULL DEFAULT 0,
                    ProactiveDetectionObserved INTEGER NOT NULL DEFAULT 0,
                    RiskScore REAL NULL,
                    RiskLevel TEXT NULL,
                    DetectionConfidence TEXT NULL,
                    StrongestSignal TEXT NULL,
                    LastProactiveDetectionAtUtc TEXT NULL,
                    DemoFileName TEXT NULL,
                    DemoFileSize INTEGER NULL,
                    DemoStartedAtUtc TEXT NULL,
                    PlayerJoinedAtUtc TEXT NULL,
                    PlayerLeftAtUtc TEXT NULL,
                    DiscordMessageId TEXT NULL,
                    DiscordChannelId TEXT NULL,
                    DiscordGuildId TEXT NULL,
                    UploadedAtUtc TEXT NULL,
                    LastError TEXT NULL,
                    Attempts INTEGER NOT NULL DEFAULT 0,
                    DemoSupport INTEGER NOT NULL DEFAULT 0,
                    DemoSupportReason TEXT NULL,
                    ReviewDecision INTEGER NOT NULL DEFAULT 0,
                    ReviewedAtUtc TEXT NULL,
                    ReviewedByClientId INTEGER NULL,
                    ReviewedByName TEXT NULL,
                    ReviewNotes TEXT NULL,
                    ReportsClearedAtUtc TEXT NULL,
                    ReportsClearedCount INTEGER NOT NULL DEFAULT 0,
                    AssignedToClientId INTEGER NULL,
                    AssignedToName TEXT NULL,
                    AssignedAtUtc TEXT NULL,
                    DiscordLastSyncedAtUtc TEXT NULL,
                    DiscordSyncError TEXT NULL,
                    FOREIGN KEY (LegacyServerId) REFERENCES EFServers(ServerId) ON DELETE SET NULL,
                    FOREIGN KEY (TargetClientId) REFERENCES EFClients(ClientId) ON DELETE RESTRICT,
                    FOREIGN KEY (AntiCheatPenaltyId) REFERENCES EFPenalties(PenaltyId) ON DELETE SET NULL,
                    FOREIGN KEY (ReviewedByClientId) REFERENCES EFClients(ClientId) ON DELETE SET NULL,
                    FOREIGN KEY (AssignedToClientId) REFERENCES EFClients(ClientId) ON DELETE SET NULL
                );

                CREATE TABLE DemosToDiscordCaseReports (
                    ReportId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CaseId TEXT NOT NULL,
                    PenaltyId INTEGER NULL,
                    WhenUtc TEXT NOT NULL,
                    ReporterClientId INTEGER NULL,
                    ReporterName TEXT NOT NULL,
                    Reason TEXT NOT NULL,
                    FOREIGN KEY (CaseId) REFERENCES DemosToDiscordCases(Id) ON DELETE CASCADE,
                    FOREIGN KEY (PenaltyId) REFERENCES EFPenalties(PenaltyId) ON DELETE SET NULL,
                    FOREIGN KEY (ReporterClientId) REFERENCES EFClients(ClientId) ON DELETE SET NULL
                );

                CREATE TABLE DemosToDiscordCaseEvents (
                    EventId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CaseId TEXT NOT NULL,
                    WhenUtc TEXT NOT NULL,
                    Action INTEGER NOT NULL,
                    AdminClientId INTEGER NULL,
                    AdminName TEXT NOT NULL,
                    Summary TEXT NOT NULL,
                    Decision INTEGER NULL,
                    Notes TEXT NULL,
                    ReportsCleared INTEGER NOT NULL DEFAULT 0,
                    PenaltyId INTEGER NULL,
                    PlayerNoteMetaId INTEGER NULL,
                    FOREIGN KEY (CaseId) REFERENCES DemosToDiscordCases(Id) ON DELETE CASCADE,
                    FOREIGN KEY (AdminClientId) REFERENCES EFClients(ClientId) ON DELETE SET NULL,
                    FOREIGN KEY (PenaltyId) REFERENCES EFPenalties(PenaltyId) ON DELETE SET NULL,
                    FOREIGN KEY (PlayerNoteMetaId) REFERENCES EFMeta(MetaId) ON DELETE SET NULL
                );

                CREATE TABLE DemosToDiscordDetectionSignals (
                    SignalId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    CaseId TEXT NOT NULL,
                    ObservedAtUtc TEXT NOT NULL,
                    MetricKey TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Scope TEXT NOT NULL,
                    Weapon TEXT NULL,
                    Map TEXT NULL,
                    ObservedValue REAL NOT NULL,
                    ExpectedValue REAL NULL,
                    Percentile REAL NULL,
                    ExpectedMultiple REAL NULL,
                    SampleSize INTEGER NOT NULL,
                    PopulationSize INTEGER NOT NULL,
                    ConfidenceWeight REAL NOT NULL,
                    RiskContribution REAL NOT NULL,
                    Explanation TEXT NOT NULL,
                    FOREIGN KEY (CaseId) REFERENCES DemosToDiscordCases(Id) ON DELETE CASCADE
                );

                CREATE INDEX IX_DemosToDiscordCases_Target_Updated
                    ON DemosToDiscordCases(TargetClientId, UpdatedAtUtc DESC);
                CREATE INDEX IX_DemosToDiscordCases_Server_Created
                    ON DemosToDiscordCases(LegacyServerId, CreatedAtUtc DESC);
                CREATE INDEX IX_DemosToDiscordCases_Status_Review_Risk
                    ON DemosToDiscordCases(Status, ReviewDecision, RiskScore DESC);
                CREATE INDEX IX_DemosToDiscordCases_Updated
                    ON DemosToDiscordCases(UpdatedAtUtc DESC);
                CREATE INDEX IX_DemosToDiscordCaseReports_Case_When
                    ON DemosToDiscordCaseReports(CaseId, WhenUtc);
                CREATE INDEX IX_DemosToDiscordCaseReports_Penalty
                    ON DemosToDiscordCaseReports(PenaltyId);
                CREATE INDEX IX_DemosToDiscordCaseEvents_Case_When
                    ON DemosToDiscordCaseEvents(CaseId, WhenUtc DESC);
                CREATE INDEX IX_DemosToDiscordDetectionSignals_Case_Risk
                    ON DemosToDiscordDetectionSignals(CaseId, RiskContribution DESC);
                CREATE INDEX IX_DemosToDiscordDetectionSignals_Metric_Scope
                    ON DemosToDiscordDetectionSignals(MetricKey, Scope);
                """, token);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO DemosToDiscordSchemaMigrations(MigrationId, Name, AppliedAtUtc) VALUES (1, @name, @when);",
                token,
                ("@name", "Initial database-backed evidence schema"),
                ("@when", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(token);
            logger.LogInformation("[DemosToDiscord] Applied database schema migration 1");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ApplyBaselineMigrationAsync(DbConnection connection, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        try
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE DemosToDiscordBaselineState (
                    SourceName TEXT NOT NULL PRIMARY KEY,
                    HighWaterKillId INTEGER NOT NULL DEFAULT 0,
                    LastFullRebuildUtc TEXT NULL,
                    LastIncrementalRefreshUtc TEXT NULL,
                    SourceEventCount INTEGER NOT NULL DEFAULT 0,
                    EligibleMemberCount INTEGER NOT NULL DEFAULT 0,
                    MapValueCount INTEGER NOT NULL DEFAULT 0,
                    VisibilityValueCount INTEGER NOT NULL DEFAULT 0,
                    LastError TEXT NULL
                );

                CREATE TABLE DemosToDiscordBaselineMembers (
                    Game TEXT NOT NULL,
                    ServerId INTEGER NOT NULL,
                    ClientId INTEGER NOT NULL,
                    Weapon TEXT NOT NULL,
                    TrackedHits INTEGER NOT NULL,
                    HeadHits INTEGER NOT NULL,
                    CriticalHits INTEGER NOT NULL,
                    KillEvents INTEGER NOT NULL,
                    HeadKillEvents INTEGER NOT NULL,
                    LastKillId INTEGER NOT NULL,
                    LastEventAtUtc TEXT NOT NULL,
                    PRIMARY KEY (Game, ServerId, ClientId, Weapon)
                );

                CREATE TABLE DemosToDiscordEvaluationState (
                    ServerId INTEGER NOT NULL,
                    ClientId INTEGER NOT NULL,
                    LastEvaluatedKillId INTEGER NOT NULL DEFAULT 0,
                    LastStatisticsUpdatedAtUtc TEXT NULL,
                    LastEvaluatedAtUtc TEXT NOT NULL,
                    LastRiskScore REAL NULL,
                    LastOutcome TEXT NOT NULL,
                    PRIMARY KEY (ServerId, ClientId)
                );

                CREATE INDEX IX_DemosToDiscordBaselineMembers_Game_Weapon_Hits
                    ON DemosToDiscordBaselineMembers(Game, Weapon, TrackedHits DESC);
                CREATE INDEX IX_DemosToDiscordBaselineMembers_Client_Server
                    ON DemosToDiscordBaselineMembers(ClientId, ServerId);
                CREATE INDEX IX_DemosToDiscordEvaluationState_Evaluated
                    ON DemosToDiscordEvaluationState(LastEvaluatedAtUtc DESC);
                """, token);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO DemosToDiscordSchemaMigrations(MigrationId, Name, AppliedAtUtc) VALUES (2, @name, @when);",
                token,
                ("@name", "Proactive baseline cache and evaluation state"),
                ("@when", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(token);
            logger.LogInformation("[DemosToDiscord] Applied database schema migration 2");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task UpsertCaseAsync(
        DbConnection connection,
        DbTransaction transaction,
        EvidenceCase item,
        CancellationToken token)
    {
        const string sql = """
            INSERT INTO DemosToDiscordCases (
                Id, CreatedAtUtc, UpdatedAtUtc, Status, ServerId, ServerName, LegacyServerId,
                Game, Map, Mode, TargetClientId, TargetNetworkId, TargetName,
                AntiCheatWhenUtc, AntiCheatDetection, AntiCheatPenaltyId, ManualBanObserved,
                ProactiveDetectionObserved, RiskScore, RiskLevel, DetectionConfidence,
                StrongestSignal, LastProactiveDetectionAtUtc, DemoFileName, DemoFileSize,
                DemoStartedAtUtc, PlayerJoinedAtUtc, PlayerLeftAtUtc, DiscordMessageId,
                DiscordChannelId, DiscordGuildId, UploadedAtUtc, LastError, Attempts,
                DemoSupport, DemoSupportReason, ReviewDecision, ReviewedAtUtc,
                ReviewedByClientId, ReviewedByName, ReviewNotes, ReportsClearedAtUtc,
                ReportsClearedCount, AssignedToClientId, AssignedToName, AssignedAtUtc,
                DiscordLastSyncedAtUtc, DiscordSyncError)
            VALUES (
                @Id, @CreatedAtUtc, @UpdatedAtUtc, @Status, @ServerId, @ServerName, @LegacyServerId,
                @Game, @Map, @Mode, @TargetClientId, @TargetNetworkId, @TargetName,
                @AntiCheatWhenUtc, @AntiCheatDetection, @AntiCheatPenaltyId, @ManualBanObserved,
                @ProactiveDetectionObserved, @RiskScore, @RiskLevel, @DetectionConfidence,
                @StrongestSignal, @LastProactiveDetectionAtUtc, @DemoFileName, @DemoFileSize,
                @DemoStartedAtUtc, @PlayerJoinedAtUtc, @PlayerLeftAtUtc, @DiscordMessageId,
                @DiscordChannelId, @DiscordGuildId, @UploadedAtUtc, @LastError, @Attempts,
                @DemoSupport, @DemoSupportReason, @ReviewDecision, @ReviewedAtUtc,
                @ReviewedByClientId, @ReviewedByName, @ReviewNotes, @ReportsClearedAtUtc,
                @ReportsClearedCount, @AssignedToClientId, @AssignedToName, @AssignedAtUtc,
                @DiscordLastSyncedAtUtc, @DiscordSyncError)
            ON CONFLICT(Id) DO UPDATE SET
                CreatedAtUtc=excluded.CreatedAtUtc, UpdatedAtUtc=excluded.UpdatedAtUtc,
                Status=excluded.Status, ServerId=excluded.ServerId, ServerName=excluded.ServerName,
                LegacyServerId=excluded.LegacyServerId, Game=excluded.Game, Map=excluded.Map,
                Mode=excluded.Mode, TargetClientId=excluded.TargetClientId,
                TargetNetworkId=excluded.TargetNetworkId, TargetName=excluded.TargetName,
                AntiCheatWhenUtc=excluded.AntiCheatWhenUtc,
                AntiCheatDetection=excluded.AntiCheatDetection,
                AntiCheatPenaltyId=excluded.AntiCheatPenaltyId,
                ManualBanObserved=excluded.ManualBanObserved,
                ProactiveDetectionObserved=excluded.ProactiveDetectionObserved,
                RiskScore=excluded.RiskScore, RiskLevel=excluded.RiskLevel,
                DetectionConfidence=excluded.DetectionConfidence,
                StrongestSignal=excluded.StrongestSignal,
                LastProactiveDetectionAtUtc=excluded.LastProactiveDetectionAtUtc,
                DemoFileName=excluded.DemoFileName, DemoFileSize=excluded.DemoFileSize,
                DemoStartedAtUtc=excluded.DemoStartedAtUtc, PlayerJoinedAtUtc=excluded.PlayerJoinedAtUtc,
                PlayerLeftAtUtc=excluded.PlayerLeftAtUtc, DiscordMessageId=excluded.DiscordMessageId,
                DiscordChannelId=excluded.DiscordChannelId, DiscordGuildId=excluded.DiscordGuildId,
                UploadedAtUtc=excluded.UploadedAtUtc, LastError=excluded.LastError,
                Attempts=excluded.Attempts, DemoSupport=excluded.DemoSupport,
                DemoSupportReason=excluded.DemoSupportReason, ReviewDecision=excluded.ReviewDecision,
                ReviewedAtUtc=excluded.ReviewedAtUtc, ReviewedByClientId=excluded.ReviewedByClientId,
                ReviewedByName=excluded.ReviewedByName, ReviewNotes=excluded.ReviewNotes,
                ReportsClearedAtUtc=excluded.ReportsClearedAtUtc,
                ReportsClearedCount=excluded.ReportsClearedCount,
                AssignedToClientId=excluded.AssignedToClientId, AssignedToName=excluded.AssignedToName,
                AssignedAtUtc=excluded.AssignedAtUtc,
                DiscordLastSyncedAtUtc=excluded.DiscordLastSyncedAtUtc,
                DiscordSyncError=excluded.DiscordSyncError;
            """;

        await ExecuteAsync(connection, transaction, sql, token,
            ("@Id", item.Id), ("@CreatedAtUtc", DbDate(item.CreatedAtUtc)),
            ("@UpdatedAtUtc", DbDate(item.UpdatedAtUtc)), ("@Status", (int)item.Status),
            ("@ServerId", item.ServerId), ("@ServerName", item.ServerName),
            ("@LegacyServerId", item.LegacyServerId), ("@Game", item.Game), ("@Map", item.Map),
            ("@Mode", item.Mode), ("@TargetClientId", item.TargetClientId),
            ("@TargetNetworkId", item.TargetNetworkId), ("@TargetName", item.TargetName),
            ("@AntiCheatWhenUtc", DbDate(item.AntiCheat?.WhenUtc)),
            ("@AntiCheatDetection", item.AntiCheat?.Detection),
            ("@AntiCheatPenaltyId", item.AntiCheat?.PenaltyId),
            ("@ManualBanObserved", item.ManualBanObserved ? 1 : 0),
            ("@ProactiveDetectionObserved", item.ProactiveDetectionObserved ? 1 : 0),
            ("@RiskScore", item.RiskScore), ("@RiskLevel", item.RiskLevel),
            ("@DetectionConfidence", item.DetectionConfidence), ("@StrongestSignal", item.StrongestSignal),
            ("@LastProactiveDetectionAtUtc", DbDate(item.LastProactiveDetectionAtUtc)),
            ("@DemoFileName", item.DemoFileName), ("@DemoFileSize", item.DemoFileSize),
            ("@DemoStartedAtUtc", DbDate(item.DemoStartedAtUtc)),
            ("@PlayerJoinedAtUtc", DbDate(item.PlayerJoinedAtUtc)),
            ("@PlayerLeftAtUtc", DbDate(item.PlayerLeftAtUtc)),
            ("@DiscordMessageId", item.DiscordMessageId), ("@DiscordChannelId", item.DiscordChannelId),
            ("@DiscordGuildId", item.DiscordGuildId), ("@UploadedAtUtc", DbDate(item.UploadedAtUtc)),
            ("@LastError", item.LastError), ("@Attempts", item.Attempts),
            ("@DemoSupport", (int)item.DemoSupport), ("@DemoSupportReason", item.DemoSupportReason),
            ("@ReviewDecision", (int)item.ReviewDecision), ("@ReviewedAtUtc", DbDate(item.ReviewedAtUtc)),
            ("@ReviewedByClientId", item.ReviewedByClientId), ("@ReviewedByName", item.ReviewedByName),
            ("@ReviewNotes", item.ReviewNotes), ("@ReportsClearedAtUtc", DbDate(item.ReportsClearedAtUtc)),
            ("@ReportsClearedCount", item.ReportsClearedCount),
            ("@AssignedToClientId", item.AssignedToClientId), ("@AssignedToName", item.AssignedToName),
            ("@AssignedAtUtc", DbDate(item.AssignedAtUtc)),
            ("@DiscordLastSyncedAtUtc", DbDate(item.DiscordLastSyncedAtUtc)),
            ("@DiscordSyncError", item.DiscordSyncError));
    }

    private static async Task ReplaceReportsAsync(
        DbConnection connection,
        DbTransaction transaction,
        EvidenceCase item,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE FROM DemosToDiscordCaseReports WHERE CaseId = @caseId;", token, ("@caseId", item.Id));
        foreach (var report in item.Reports)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DemosToDiscordCaseReports
                    (CaseId, PenaltyId, WhenUtc, ReporterClientId, ReporterName, Reason)
                VALUES (@caseId, @penaltyId, @when, @reporterId, @reporter, @reason);
                """, token,
                ("@caseId", item.Id), ("@penaltyId", report.PenaltyId), ("@when", DbDate(report.WhenUtc)),
                ("@reporterId", report.ReporterClientId > 0 ? report.ReporterClientId : null),
                ("@reporter", report.ReporterName), ("@reason", report.Reason));
        }
    }

    private static async Task ReplaceHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        EvidenceCase item,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE FROM DemosToDiscordCaseEvents WHERE CaseId = @caseId;", token, ("@caseId", item.Id));
        foreach (var entry in item.History)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DemosToDiscordCaseEvents
                    (CaseId, WhenUtc, Action, AdminClientId, AdminName, Summary, Decision, Notes,
                     ReportsCleared, PenaltyId, PlayerNoteMetaId)
                VALUES (@caseId, @when, @action, @adminId, @admin, @summary, @decision, @notes,
                        @cleared, @penaltyId, @noteId);
                """, token,
                ("@caseId", item.Id), ("@when", DbDate(entry.WhenUtc)), ("@action", (int)entry.Action),
                ("@adminId", entry.AdminClientId), ("@admin", entry.AdminName), ("@summary", entry.Summary),
                ("@decision", entry.Decision is null ? null : (int)entry.Decision.Value),
                ("@notes", entry.Notes), ("@cleared", entry.ReportsCleared),
                ("@penaltyId", entry.PenaltyId), ("@noteId", entry.PlayerNoteMetaId));
        }
    }

    private static async Task ReplaceSignalsAsync(
        DbConnection connection,
        DbTransaction transaction,
        EvidenceCase item,
        CancellationToken token)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE FROM DemosToDiscordDetectionSignals WHERE CaseId = @caseId;", token, ("@caseId", item.Id));
        foreach (var signal in item.DetectionSignals)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DemosToDiscordDetectionSignals
                    (CaseId, ObservedAtUtc, MetricKey, DisplayName, Scope, Weapon, Map,
                     ObservedValue, ExpectedValue, Percentile, ExpectedMultiple, SampleSize,
                     PopulationSize, ConfidenceWeight, RiskContribution, Explanation)
                VALUES (@caseId, @observed, @metric, @display, @scope, @weapon, @map,
                        @value, @expected, @percentile, @multiple, @sample, @population,
                        @weight, @risk, @explanation);
                """, token,
                ("@caseId", item.Id), ("@observed", DbDate(signal.ObservedAtUtc)),
                ("@metric", signal.MetricKey), ("@display", signal.DisplayName), ("@scope", signal.Scope),
                ("@weapon", signal.Weapon), ("@map", signal.Map), ("@value", signal.ObservedValue),
                ("@expected", signal.ExpectedValue), ("@percentile", signal.Percentile),
                ("@multiple", signal.ExpectedMultiple), ("@sample", signal.SampleSize),
                ("@population", signal.PopulationSize), ("@weight", signal.ConfidenceWeight),
                ("@risk", signal.RiskContribution), ("@explanation", signal.Explanation));
        }
    }

    private static async Task LoadReportsAsync(
        DbConnection connection,
        IReadOnlyDictionary<string, EvidenceCase> cases,
        CancellationToken token)
    {
        await using var command = CreateCommand(connection, null, """
            SELECT CaseId, PenaltyId, WhenUtc, ReporterClientId, ReporterName, Reason
            FROM DemosToDiscordCaseReports ORDER BY WhenUtc;
            """);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var caseId = reader.GetString(reader.GetOrdinal("CaseId"));
            if (!cases.TryGetValue(caseId, out var item))
                continue;
            item.Reports.Add(new ReportEvidence
            {
                PenaltyId = NullableInt(reader, "PenaltyId"),
                WhenUtc = Date(reader, "WhenUtc")!.Value,
                ReporterClientId = NullableInt(reader, "ReporterClientId") ?? 0,
                ReporterName = Text(reader, "ReporterName") ?? string.Empty,
                Reason = Text(reader, "Reason") ?? string.Empty
            });
        }
    }

    private static async Task LoadHistoryAsync(
        DbConnection connection,
        IReadOnlyDictionary<string, EvidenceCase> cases,
        CancellationToken token)
    {
        await using var command = CreateCommand(connection, null, """
            SELECT CaseId, WhenUtc, Action, AdminClientId, AdminName, Summary, Decision, Notes,
                   ReportsCleared, PenaltyId, PlayerNoteMetaId
            FROM DemosToDiscordCaseEvents ORDER BY WhenUtc;
            """);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var caseId = reader.GetString(reader.GetOrdinal("CaseId"));
            if (!cases.TryGetValue(caseId, out var item))
                continue;
            var decision = NullableInt(reader, "Decision");
            item.History.Add(new EvidenceHistoryEntry
            {
                WhenUtc = Date(reader, "WhenUtc")!.Value,
                Action = (EvidenceHistoryAction)Int(reader, "Action"),
                AdminClientId = NullableInt(reader, "AdminClientId"),
                AdminName = Text(reader, "AdminName") ?? "System",
                Summary = Text(reader, "Summary") ?? string.Empty,
                Decision = decision is null ? null : (EvidenceReviewDecision)decision.Value,
                Notes = Text(reader, "Notes"),
                ReportsCleared = Int(reader, "ReportsCleared"),
                PenaltyId = NullableInt(reader, "PenaltyId"),
                PlayerNoteMetaId = NullableInt(reader, "PlayerNoteMetaId")
            });
        }
    }

    private static async Task LoadSignalsAsync(
        DbConnection connection,
        IReadOnlyDictionary<string, EvidenceCase> cases,
        CancellationToken token)
    {
        await using var command = CreateCommand(connection, null, """
            SELECT SignalId, CaseId, ObservedAtUtc, MetricKey, DisplayName, Scope, Weapon, Map,
                   ObservedValue, ExpectedValue, Percentile, ExpectedMultiple, SampleSize,
                   PopulationSize, ConfidenceWeight, RiskContribution, Explanation
            FROM DemosToDiscordDetectionSignals ORDER BY RiskContribution DESC;
            """);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var caseId = reader.GetString(reader.GetOrdinal("CaseId"));
            if (!cases.TryGetValue(caseId, out var item))
                continue;
            item.DetectionSignals.Add(new DetectionSignal
            {
                SignalId = Long(reader, "SignalId"),
                ObservedAtUtc = Date(reader, "ObservedAtUtc")!.Value,
                MetricKey = Text(reader, "MetricKey") ?? string.Empty,
                DisplayName = Text(reader, "DisplayName") ?? string.Empty,
                Scope = Text(reader, "Scope") ?? string.Empty,
                Weapon = Text(reader, "Weapon"),
                Map = Text(reader, "Map"),
                ObservedValue = Double(reader, "ObservedValue"),
                ExpectedValue = NullableDouble(reader, "ExpectedValue"),
                Percentile = NullableDouble(reader, "Percentile"),
                ExpectedMultiple = NullableDouble(reader, "ExpectedMultiple"),
                SampleSize = Int(reader, "SampleSize"),
                PopulationSize = Int(reader, "PopulationSize"),
                ConfidenceWeight = Double(reader, "ConfidenceWeight"),
                RiskContribution = Double(reader, "RiskContribution"),
                Explanation = Text(reader, "Explanation") ?? string.Empty
            });
        }
    }

    private static EvidenceCase ReadCase(DbDataReader reader)
    {
        var antiCheatWhen = Date(reader, "AntiCheatWhenUtc");
        return new EvidenceCase
        {
            Id = Text(reader, "Id")!,
            CreatedAtUtc = Date(reader, "CreatedAtUtc")!.Value,
            UpdatedAtUtc = Date(reader, "UpdatedAtUtc")!.Value,
            Status = (EvidenceCaseStatus)Int(reader, "Status"),
            ServerId = Text(reader, "ServerId") ?? string.Empty,
            ServerName = Text(reader, "ServerName") ?? string.Empty,
            LegacyServerId = NullableLong(reader, "LegacyServerId"),
            Game = Text(reader, "Game") ?? string.Empty,
            Map = Text(reader, "Map") ?? string.Empty,
            Mode = Text(reader, "Mode") ?? string.Empty,
            TargetClientId = Int(reader, "TargetClientId"),
            TargetNetworkId = Long(reader, "TargetNetworkId"),
            TargetName = Text(reader, "TargetName") ?? string.Empty,
            AntiCheat = antiCheatWhen is null ? null : new AntiCheatEvidence
            {
                WhenUtc = antiCheatWhen.Value,
                Detection = Text(reader, "AntiCheatDetection") ?? string.Empty,
                PenaltyId = NullableInt(reader, "AntiCheatPenaltyId")
            },
            ManualBanObserved = Bool(reader, "ManualBanObserved"),
            ProactiveDetectionObserved = Bool(reader, "ProactiveDetectionObserved"),
            RiskScore = NullableDouble(reader, "RiskScore"),
            RiskLevel = Text(reader, "RiskLevel"),
            DetectionConfidence = Text(reader, "DetectionConfidence"),
            StrongestSignal = Text(reader, "StrongestSignal"),
            LastProactiveDetectionAtUtc = Date(reader, "LastProactiveDetectionAtUtc"),
            DemoFileName = Text(reader, "DemoFileName"),
            DemoFileSize = NullableLong(reader, "DemoFileSize"),
            DemoStartedAtUtc = Date(reader, "DemoStartedAtUtc"),
            PlayerJoinedAtUtc = Date(reader, "PlayerJoinedAtUtc"),
            PlayerLeftAtUtc = Date(reader, "PlayerLeftAtUtc"),
            DiscordMessageId = Text(reader, "DiscordMessageId"),
            DiscordChannelId = Text(reader, "DiscordChannelId"),
            DiscordGuildId = Text(reader, "DiscordGuildId"),
            UploadedAtUtc = Date(reader, "UploadedAtUtc"),
            LastError = Text(reader, "LastError"),
            Attempts = Int(reader, "Attempts"),
            DemoSupport = (DemoSupportStatus)Int(reader, "DemoSupport"),
            DemoSupportReason = Text(reader, "DemoSupportReason"),
            ReviewDecision = (EvidenceReviewDecision)Int(reader, "ReviewDecision"),
            ReviewedAtUtc = Date(reader, "ReviewedAtUtc"),
            ReviewedByClientId = NullableInt(reader, "ReviewedByClientId"),
            ReviewedByName = Text(reader, "ReviewedByName"),
            ReviewNotes = Text(reader, "ReviewNotes"),
            ReportsClearedAtUtc = Date(reader, "ReportsClearedAtUtc"),
            ReportsClearedCount = Int(reader, "ReportsClearedCount"),
            AssignedToClientId = NullableInt(reader, "AssignedToClientId"),
            AssignedToName = Text(reader, "AssignedToName"),
            AssignedAtUtc = Date(reader, "AssignedAtUtc"),
            DiscordLastSyncedAtUtc = Date(reader, "DiscordLastSyncedAtUtc"),
            DiscordSyncError = Text(reader, "DiscordSyncError"),
            Reports = [],
            History = [],
            DetectionSignals = []
        };
    }

    private static void EnsureSqlite(DbContext context)
    {
        if (!context.Database.IsSqlite())
            throw new NotSupportedException(
                "DemosToDiscord database persistence currently targets IW4MAdmin's SQLite Database.db provider.");
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static async Task<int> ExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<int> ScalarIntAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken token)
    {
        await using var command = CreateCommand(connection, transaction, sql);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }

    private static string DbDate(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? DbDate(DateTime? value) => value is null ? null : DbDate(value.Value);

    private static string? Text(DbDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));

    private static int Int(DbDataReader reader, string name) =>
        Convert.ToInt32(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static int? NullableInt(DbDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : Int(reader, name);

    private static long Long(DbDataReader reader, string name) =>
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static long? NullableLong(DbDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : Long(reader, name);

    private static double Double(DbDataReader reader, string name) =>
        Convert.ToDouble(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static double? NullableDouble(DbDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : Double(reader, name);

    private static bool Bool(DbDataReader reader, string name) => Int(reader, name) != 0;

    private static DateTime? Date(DbDataReader reader, string name)
    {
        var text = Text(reader, name);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
