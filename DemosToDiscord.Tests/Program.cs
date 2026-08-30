using System.Text.Json;
using Data.Abstractions;
using Data.Context;
using Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedLibraryCore.Dtos;
using SharedLibraryCore.Dtos.Meta.Responses;
using SharedLibraryCore.Interfaces;
using SharedLibraryCore.QueryHelper;

namespace DemosToDiscord.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("database migration and case graph round-trip", DatabaseRoundTripAsync),
            ("legacy JSON import and case merge", LegacyImportAndMergeAsync),
            ("minimum samples suppress tiny outliers", MinimumSampleSafeguardAsync),
            ("extreme and multi-signal risk scoring", RiskScoringAsync),
            ("baseline rebuild and live player evaluation", BaselineEvaluationAsync),
            ("T6, IW5, T4, T5 and Zombies capability fallbacks", CapabilityFallbacksAsync),
            ("native player note append preserves manual text", PlayerNoteIntegrationAsync)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task DatabaseRoundTripAsync()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var database = fixture.CreateRepository();
        await database.InitializeAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        var item = Case("case-roundtrip", now);
        item.Reports.Add(new ReportEvidence
        {
            WhenUtc = now,
            ReporterClientId = 7,
            ReporterName = "Admin",
            Reason = "test report"
        });
        item.History.Add(new EvidenceHistoryEntry
        {
            WhenUtc = now,
            Action = EvidenceHistoryAction.Created,
            AdminClientId = 7,
            AdminName = "Admin",
            Summary = "created"
        });
        item.ProactiveDetectionObserved = true;
        item.RiskScore = 82;
        item.RiskLevel = "Very High";
        item.DetectionSignals.Add(new DetectionSignal
        {
            ObservedAtUtc = now,
            MetricKey = "weapon_head_rate",
            DisplayName = "AN-94 head-only rate",
            Scope = "T6 + AN-94",
            Weapon = "an94_mp",
            ObservedValue = 0.287,
            ExpectedValue = 0.084,
            Percentile = 99.8,
            ExpectedMultiple = 3.4,
            SampleSize = 250,
            PopulationSize = 190,
            ConfidenceWeight = 0.95,
            RiskContribution = 42,
            Explanation = "explainable test signal"
        });

        await database.SaveCaseAsync(item, CancellationToken.None);
        var loaded = (await database.LoadCasesAsync(CancellationToken.None)).Single();
        Equal(item.Id, loaded.Id, "case id");
        Equal(1, loaded.Reports.Count, "report count");
        Equal(1, loaded.History.Count, "history count");
        Equal(1, loaded.DetectionSignals.Count, "signal count");
        Equal(82d, loaded.RiskScore, "risk score");
        Equal("AN-94 head-only rate", loaded.DetectionSignals[0].DisplayName, "signal name");

        loaded.Reports.Clear();
        loaded.ReviewDecision = EvidenceReviewDecision.NotCheatingNoAction;
        await database.SaveCaseAsync(loaded, CancellationToken.None);
        var replaced = (await database.LoadCasesAsync(CancellationToken.None)).Single();
        Equal(0, replaced.Reports.Count, "children are replaced transactionally");
        Equal(EvidenceReviewDecision.NotCheatingNoAction, replaced.ReviewDecision, "review update");

        await using var verify = new SqliteConnection(fixture.ConnectionString);
        await verify.OpenAsync();
        var tables = await ScalarAsync<long>(verify,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE 'DemosToDiscord%';");
        True(tables >= 5, "plugin tables were migrated");
        var indexes = await ScalarAsync<long>(verify,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'IX_DemosToDiscord%';");
        True(indexes >= 8, "plugin indexes were created");

        await database.DeleteCasesAsync([item.Id], CancellationToken.None);
        Equal(0, (await database.LoadCasesAsync(CancellationToken.None)).Count, "case delete");
        Equal(0L, await ScalarAsync<long>(verify, "SELECT COUNT(*) FROM DemosToDiscordDetectionSignals;"),
            "signal cascade delete");
    }

    private static async Task LegacyImportAndMergeAsync()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var legacyPath = Path.Combine(fixture.Directory, "DemosToDiscordCases.json");
        var legacy = Case("legacy-case", DateTime.UtcNow);
        await File.WriteAllTextAsync(
            legacyPath,
            JsonSerializer.Serialize(new[] { legacy }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var config = new DemosToDiscordConfig
        {
            StateFilePath = legacyPath,
            ImportLegacyStateFile = true,
            CaseRetentionDays = 90,
            MaxStoredCases = 500
        };
        var store = new EvidenceCaseStore(
            config,
            fixture.CreateRepository(),
            NullLogger<EvidenceCaseStore>.Instance);
        await store.InitializeAsync(CancellationToken.None);
        Equal(1, store.Snapshot().Cases.Count, "legacy case imported");
        True(File.Exists(legacyPath), "legacy source is preserved");

        var capture = new PenaltyCapture(
            EvidenceTriggerType.Report,
            legacy.CreatedAtUtc.AddMinutes(1),
            legacy.ServerId,
            legacy.ServerName,
            legacy.LegacyServerId,
            legacy.Game,
            legacy.Map,
            legacy.Mode,
            legacy.TargetClientId,
            legacy.TargetNetworkId,
            legacy.TargetName,
            7,
            "Admin",
            "report reason",
            string.Empty);
        var result = await store.AddOrMergeAsync(capture, CancellationToken.None);
        True(!result.Created, "report merged into recent case");
        Equal(1, store.Snapshot().Cases.Count, "merge did not duplicate case");
        Equal(1, store.Snapshot().Cases[0].Reports.Count, "merged report retained");
        var linked = await store.LinkManualBanAsync(
            legacy.TargetClientId,
            legacy.CreatedAtUtc.AddMinutes(2),
            5,
            CancellationToken.None);
        True(linked is not null && linked.ManualBanObserved, "manual ban linked to existing case");
        var penaltyEvent = linked!.History.Last(entry => entry.Action == EvidenceHistoryAction.PenaltyLinked);
        Equal(5, penaltyEvent.PenaltyId, "penalty id retained in audit history");
    }

    private static Task MinimumSampleSafeguardAsync()
    {
        var scorer = new RiskScorer(new DemosToDiscordConfig());
        var tiny = scorer.Score([
            Observation(sample: 3, positives: 2, population: 500, percentile: 1, observed: 0.667, expected: 0.08)
        ]);
        Equal(0d, tiny.Score, "tiny sample risk");
        True(!tiny.ShouldCreateCase, "tiny sample cannot create a case");

        var smallPopulation = scorer.Score([
            Observation(sample: 500, positives: 100, population: 10, percentile: 1, observed: 0.2, expected: 0.08)
        ]);
        Equal(0d, smallPopulation.Score, "small population risk");
        return Task.CompletedTask;
    }

    private static Task RiskScoringAsync()
    {
        var scorer = new RiskScorer(new DemosToDiscordConfig());
        var extreme = scorer.Score([
            Observation(sample: 370, positives: 127, population: 141, percentile: 1, observed: 0.3432, expected: 0.0746)
        ]);
        True(extreme.Score >= 50, "extreme signal reaches review threshold");
        True(extreme.ShouldCreateCase, "extreme signal creates review case");
        Equal("Review", extreme.Level, "extreme single-signal level");

        var multi = scorer.Score([
            Observation(sample: 400, positives: 80, population: 150, percentile: 0.996, observed: 0.2, expected: 0.09),
            new DetectionObservation(
                DateTime.UtcNow, "snap", "Average snap", "T6", 4.2, 2.0, 0.996,
                400, 80, 150)
        ], recentProactiveCases: 2);
        True(multi.Score >= 80, "independent signals and repeat history combine");
        Equal("Very High", multi.Level, "multi-signal level");
        Equal(2, multi.Signals.Count, "both signals retained for explanation");
        return Task.CompletedTask;
    }

    private static async Task BaselineEvaluationAsync()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var database = fixture.CreateRepository();
        await database.InitializeAsync(CancellationToken.None);
        await using (var connection = new SqliteConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE EFServers SET GameName=7 WHERE ServerId=100;
                INSERT INTO EFServers(ServerId, GameName) VALUES(200, 6);
                INSERT INTO EFClientStatistics(ClientId, ServerId, UpdatedAt)
                    VALUES(42, 100, '2026-08-30T18:00:00.0000000Z'),
                          (42, 200, '2026-08-30T18:00:00.0000000Z');
                WITH RECURSIVE players(id) AS (
                    SELECT 42 UNION ALL SELECT 100 UNION ALL
                    SELECT id+1 FROM players WHERE id>=100 AND id<200
                ), hits(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM hits WHERE n<400)
                INSERT INTO EFClientKills(
                    KillId, AttackerId, ServerId, HitLoc, IsKill, WeaponReference,
                    "When", Fraction, VisibilityPercentage, Map, Active)
                SELECT ROW_NUMBER() OVER (ORDER BY players.id, hits.n), players.id, 100,
                       CASE WHEN players.id=42 AND hits.n<=160 THEN 2
                            WHEN players.id<>42 AND hits.n<=30 THEN 2 ELSE 4 END,
                       CASE WHEN hits.n%4=0 THEN 1 ELSE 0 END,
                       'scar_mp_reflex', '2026-08-30T18:00:00.0000000Z', 0, 0, 0, 1
                FROM players CROSS JOIN hits;
                WITH RECURSIVE players(id) AS (
                    SELECT 42 UNION ALL SELECT 100 UNION ALL
                    SELECT id+1 FROM players WHERE id>=100 AND id<200
                )
                INSERT INTO EFHitLocationCounts(
                    EFClientStatisticsClientId, EFClientStatisticsServerId, Location, HitCount)
                SELECT id, 200, 2, CASE WHEN id=42 THEN 160 ELSE 30 END FROM players
                UNION ALL
                SELECT id, 200, 4, CASE WHEN id=42 THEN 240 ELSE 370 END FROM players;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var config = new DemosToDiscordConfig
        {
            ProactiveDetection = new ProactiveDetectionConfig
            {
                Enabled = true,
                MinimumTrackedHits = 100,
                MinimumPositiveEvents = 12,
                MinimumPopulationSize = 30,
                MinimumCaseRiskScore = 50
            }
        };
        var baselines = fixture.CreateBaselineService(config);
        var status = await baselines.RefreshAsync(true, CancellationToken.None);
        True(status.SourceEvents >= 40800, "source events were aggregated");
        True(status.CachedMembers >= 102, "player/weapon members were cached");
        Equal(0L, status.MapValues, "empty map telemetry is reported honestly");

        var target = new ProactiveEvaluationTarget(
            "127.0.0.1:4976", "Test", 100, "T6", "mp_raid", "tdm",
            42, 123456789, "Target", DateTime.UtcNow, "test");
        var capability = new DetectionCapabilityService(config).Resolve("T6", "mp_raid", "tdm");
        var evaluation = await baselines.EvaluateAsync(target, capability, CancellationToken.None);
        True(evaluation.HasNewData, "new player data is evaluated");
        Equal(1, evaluation.Observations.Count, "one normalized weapon signal was produced");
        Equal("scar_mp", evaluation.Observations[0].Weapon, "attachment is normalized to base weapon");
        True(evaluation.Observations[0].Percentile >= 0.99, "extreme player reaches the population tail");
        var assessment = new RiskScorer(config).Score(evaluation.Observations);
        True(assessment.ShouldCreateCase, "real baseline observation reaches review threshold");
        await baselines.RecordEvaluationAsync(target, evaluation, assessment, CancellationToken.None);
        var unchanged = await baselines.EvaluateAsync(target, capability, CancellationToken.None);
        True(!unchanged.HasNewData, "unchanged data is not evaluated twice");

        var t5Target = target with { ServerId = "127.0.0.1:28960", LegacyServerId = 200, Game = "T5" };
        var t5Capability = new DetectionCapabilityService(config).Resolve("T5", "mp_nuked", "tdm");
        var t5Evaluation = await baselines.EvaluateAsync(t5Target, t5Capability, CancellationToken.None);
        Equal(1, t5Evaluation.Observations.Count, "T5 cumulative fallback produces one labelled signal");
        Equal("overall_head_rate", t5Evaluation.Observations[0].MetricKey, "T5 uses overall rather than weapon-specific head rate");
        True(new RiskScorer(config).Score(t5Evaluation.Observations).ShouldCreateCase,
            "T5 cumulative fallback can create a review case with a strong population");
    }

    private static async Task PlayerNoteIntegrationAsync()
    {
        var meta = new FakeMetaService("Manual admin note");
        var service = new PlayerNoteService(meta, NullLogger<PlayerNoteService>.Instance);
        var metaId = await service.AppendCaseActionAsync(
            42,
            7,
            "Olly",
            "ABC123",
            "Cleared — no cheat confirmed",
            CancellationToken.None);
        Equal(99, metaId, "native EFMeta reference");
        True(meta.Note!.StartsWith("Manual admin note", StringComparison.Ordinal), "manual note is preserved");
        True(meta.Note.Contains("Case ABC123", StringComparison.Ordinal), "case id is appended");
        True(meta.Note.Contains("Cleared", StringComparison.Ordinal), "meaningful action is appended");
    }

    private static Task CapabilityFallbacksAsync()
    {
        var service = new DetectionCapabilityService(new DemosToDiscordConfig());
        var t6 = service.Resolve("T6", "mp_raid", "tdm");
        True(t6.EligibleForScoring && t6.HasJointWeaponHitEvents && t6.HasAntiCheatSnapshots,
            "T6 rich metrics");
        var iw5 = service.Resolve("IW5", "mp_dome", "war");
        True(iw5.HasJointWeaponHitEvents, "IW5 tracked hits");
        var t4 = service.Resolve("T4", "mp_castle", "tdm");
        True(t4.EligibleForScoring && !t4.HasJointWeaponHitEvents && !t4.HasAntiCheatSnapshots,
            "T4 marginal fallback");
        var t5 = service.Resolve("T5", "mp_nuked", "tdm");
        True(t5.EligibleForScoring && !t5.HasJointWeaponHitEvents, "T5 MP marginal fallback");
        var zombies = service.Resolve("T5", "zombie_theater", "zclassic");
        True(!zombies.EligibleForScoring && zombies.HasCumulativePlayerStatistics, "T5 Zombies statistics-only");
        True(!t6.HasExactShotsFired && !t4.HasExactShotsFired, "accuracy is never fabricated");
        var disabled = service.Resolve("T6", "mp_raid", "tdm", new DemosToDiscordServerOverride
        {
            EnableProactiveDetection = false
        });
        True(!disabled.EligibleForScoring, "server override disables proactive scoring");
        return Task.CompletedTask;
    }

    private static DetectionObservation Observation(
        int sample,
        int positives,
        int population,
        double percentile,
        double observed,
        double expected) => new(
        DateTime.UtcNow,
        "weapon_head_rate",
        "MP7 head-only rate",
        "T6 + MP7",
        observed,
        expected,
        percentile,
        sample,
        positives,
        population,
        "mp7_mp");

    private static EvidenceCase Case(string id, DateTime now) => new()
    {
        Id = id,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        Status = EvidenceCaseStatus.DemoUnsupported,
        ServerId = "127.0.0.1:4976",
        ServerName = "Test server",
        LegacyServerId = 100,
        Game = "T6",
        Map = "mp_raid",
        Mode = "tdm",
        TargetClientId = 42,
        TargetNetworkId = 123456789,
        TargetName = "Target",
        DemoSupport = DemoSupportStatus.UnsupportedGame
    };

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync() ?? default(T)!, typeof(T));
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}

internal sealed class DatabaseFixture : IAsyncDisposable
{
    private readonly TestDatabaseContextFactory _factory;
    public string Directory { get; }
    public string ConnectionString { get; }

    private DatabaseFixture(string directory, string connectionString)
    {
        Directory = directory;
        ConnectionString = connectionString;
        _factory = new TestDatabaseContextFactory(connectionString);
    }

    public static async Task<DatabaseFixture> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DemosToDiscord.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "Database.db")}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE EFClients (ClientId INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE EFServers (ServerId INTEGER NOT NULL PRIMARY KEY, GameName INTEGER NULL);
            CREATE TABLE EFPenalties (PenaltyId INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE EFMeta (MetaId INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE EFClientStatistics (
                ClientId INTEGER NOT NULL, ServerId INTEGER NOT NULL, UpdatedAt TEXT NULL,
                PRIMARY KEY(ClientId, ServerId));
            CREATE TABLE EFHitLocationCounts (
                EFClientStatisticsClientId INTEGER NOT NULL,
                EFClientStatisticsServerId INTEGER NOT NULL,
                Location INTEGER NOT NULL, HitCount INTEGER NOT NULL);
            CREATE TABLE EFClientKills (
                KillId INTEGER NOT NULL PRIMARY KEY, AttackerId INTEGER NOT NULL,
                ServerId INTEGER NOT NULL, HitLoc INTEGER NOT NULL, IsKill INTEGER NOT NULL,
                WeaponReference TEXT NULL, "When" TEXT NOT NULL, Fraction REAL NOT NULL DEFAULT 0,
                VisibilityPercentage REAL NOT NULL DEFAULT 0, Map INTEGER NOT NULL DEFAULT 0,
                Active INTEGER NOT NULL DEFAULT 1);
            INSERT INTO EFClients(ClientId) VALUES (7), (42);
            INSERT INTO EFServers(ServerId) VALUES (100);
            INSERT INTO EFPenalties(PenaltyId) VALUES (5);
            """;
        await command.ExecuteNonQueryAsync();
        return new DatabaseFixture(directory, connectionString);
    }

    public DemosToDiscordDatabase CreateRepository() =>
        new(_factory, NullLogger<DemosToDiscordDatabase>.Instance);

    public ProactiveBaselineService CreateBaselineService(DemosToDiscordConfig config) =>
        new(_factory, config, NullLogger<ProactiveBaselineService>.Instance);

    public ValueTask DisposeAsync()
    {
        try
        {
            System.IO.Directory.Delete(Directory, true);
        }
        catch
        {
            // Test cleanup must not obscure the test result on Windows file-lock races.
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class TestDatabaseContext(DbContextOptions options) : DatabaseContext(options);

internal sealed class TestDatabaseContextFactory : IDatabaseContextFactory
{
    private readonly DbContextOptions _options;

    public TestDatabaseContextFactory(string connectionString)
    {
        _options = new DbContextOptionsBuilder().UseSqlite(connectionString).Options;
    }

    public DatabaseContext CreateContext(bool? enableTracking = true) => new TestDatabaseContext(_options);
}

internal sealed class FakeMetaService : IMetaServiceV2
{
    private ClientNoteMetaResponse? _value;
    public string? Note => _value?.Note;

    public FakeMetaService(string note)
    {
        _value = new ClientNoteMetaResponse { Note = note };
    }

    public Task SetPersistentMetaValue<T>(string metaKey, T metaValue, int clientId, CancellationToken token = default)
        where T : class
    {
        _value = (ClientNoteMetaResponse)(object)metaValue;
        return Task.CompletedTask;
    }

    public Task<T> GetPersistentMetaValue<T>(string metaKey, int clientId, CancellationToken token = default)
        where T : class => Task.FromResult((T)(object)_value!);

    public Task<EFMeta> GetPersistentMeta(string metaKey, int clientId, CancellationToken token = default) =>
        Task.FromResult(new EFMeta { MetaId = 99, Key = metaKey, ClientId = clientId, Value = "{}" });

    public Task SetPersistentMeta(string metaKey, string metaValue, int clientId, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task SetPersistentMetaForLookupKey(string metaKey, string lookupKey, int lookupId, int clientId, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task IncrementPersistentMeta(string metaKey, int incrementAmount, int clientId, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task DecrementPersistentMeta(string metaKey, int decrementAmount, int clientId, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task<EFMeta> GetPersistentMetaByLookup(string metaKey, string lookupKey, int clientId, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task RemovePersistentMeta(string metaKey, int clientId, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task SetPersistentMeta(string metaKey, string metaValue, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task SetPersistentMetaValue<T>(string metaKey, T metaValue, CancellationToken token = default) where T : class =>
        throw new NotSupportedException();
    public Task RemovePersistentMeta(string metaKey, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task<EFMeta> GetPersistentMeta(string metaKey, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task<T> GetPersistentMetaValue<T>(string metaKey, CancellationToken token = default) where T : class =>
        throw new NotSupportedException();
    public void AddRuntimeMeta<T, TReturn>(MetaType metaKey, Func<T, CancellationToken, Task<IEnumerable<TReturn>>> metaAction)
        where TReturn : IClientMeta where T : PaginationRequest => throw new NotSupportedException();
    public Task<IEnumerable<IClientMeta>> GetRuntimeMeta(ClientPaginationRequest request, CancellationToken token = default) =>
        throw new NotSupportedException();
    public Task<IEnumerable<T>> GetRuntimeMeta<T>(ClientPaginationRequest request, MetaType metaType, CancellationToken token = default)
        where T : IClientMeta => throw new NotSupportedException();
}
