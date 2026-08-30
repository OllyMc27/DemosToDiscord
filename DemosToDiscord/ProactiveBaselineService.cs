using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed record ProactiveEvaluationTarget(
    string ServerId,
    string ServerName,
    long LegacyServerId,
    string Game,
    string Map,
    string Mode,
    int ClientId,
    long NetworkId,
    string ClientName,
    DateTime RequestedAtUtc,
    string Reason);

public sealed record BaselineEvaluation(
    IReadOnlyList<DetectionObservation> Observations,
    bool HasNewData,
    long LastKillId,
    DateTime? StatisticsUpdatedAtUtc,
    string Detail);

public sealed record ProactiveBaselineStatus(
    long HighWaterKillId,
    DateTime? LastFullRebuildUtc,
    DateTime? LastRefreshUtc,
    long SourceEvents,
    int CachedMembers,
    long MapValues,
    long VisibilityValues,
    string? LastError);

public sealed class ProactiveBaselineService(
    IDatabaseContextFactory contextFactory,
    DemosToDiscordConfig config,
    ILogger<ProactiveBaselineService> logger)
{
    private const string SourceName = "EFClientKills";
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTime _lastRefreshAttemptUtc;

    public Task<ProactiveBaselineStatus> RefreshAsync(bool forceFull, CancellationToken token) =>
        RefreshCoreAsync(forceFull, false, token);

    private Task<ProactiveBaselineStatus> RefreshForEvaluationAsync(CancellationToken token) =>
        RefreshCoreAsync(false, true, token);

    private async Task<ProactiveBaselineStatus> RefreshCoreAsync(
        bool forceFull,
        bool verifySourceHighWater,
        CancellationToken token)
    {
        await _refreshGate.WaitAsync(token);
        try
        {
            var minimumRefresh = TimeSpan.FromMinutes(Math.Max(1, config.ProactiveDetection.BaselineRefreshMinutes));
            if (!forceFull && !verifySourceHighWater && DateTime.UtcNow - _lastRefreshAttemptUtc < minimumRefresh)
                return await GetStatusAsync(token);
            _lastRefreshAttemptUtc = DateTime.UtcNow;

            await using var context = contextFactory.CreateContext();
            EnsureSqlite(context);
            await context.Database.OpenConnectionAsync(token);
            var connection = context.Database.GetDbConnection();
            var current = await ReadStateAsync(connection, token);
            if (!forceFull && verifySourceHighWater && current.CachedMembers > 0)
            {
                var sourceHighWater = await ScalarLongAsync(
                    connection,
                    "SELECT COALESCE(MAX(KillId), 0) FROM EFClientKills;",
                    token);
                if (sourceHighWater <= current.HighWaterKillId)
                    return current;
            }
            var fullInterval = TimeSpan.FromHours(Math.Max(1, config.ProactiveDetection.FullBaselineRebuildHours));
            var rebuild = forceFull || current.LastFullRebuildUtc is null ||
                          DateTime.UtcNow - current.LastFullRebuildUtc.Value >= fullInterval ||
                          current.CachedMembers == 0;
            return rebuild
                ? await FullRebuildAsync(connection, token)
                : await IncrementalRefreshAsync(connection, current, token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[DemosToDiscord] Proactive baseline refresh failed");
            await TryRecordErrorAsync(exception.Message);
            return (await GetStatusAsync(CancellationToken.None)) with { LastError = exception.Message };
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<BaselineEvaluation> EvaluateAsync(
        ProactiveEvaluationTarget target,
        DetectionCapability capability,
        CancellationToken token)
    {
        await RefreshForEvaluationAsync(token);
        await using var context = contextFactory.CreateContext(false);
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        var connection = context.Database.GetDbConnection();

        var lastKillId = capability.HasJointWeaponHitEvents
            ? await ScalarLongAsync(connection, """
                SELECT COALESCE(MAX(LastKillId), 0)
                FROM DemosToDiscordBaselineMembers
                WHERE ClientId=@client AND ServerId=@server;
                """, token, ("@client", target.ClientId), ("@server", target.LegacyServerId))
            : 0;
        var statsUpdated = await ScalarDateAsync(connection, """
            SELECT MAX(UpdatedAt) FROM EFClientStatistics
            WHERE ClientId=@client AND ServerId=@server;
            """, token, ("@client", target.ClientId), ("@server", target.LegacyServerId));
        var previous = await ReadEvaluationStateAsync(connection, target, token);
        var hasNewData = previous is null || lastKillId > previous.Value.LastKillId ||
                         (statsUpdated is not null &&
                          (previous.Value.StatisticsUpdatedAtUtc is null || statsUpdated > previous.Value.StatisticsUpdatedAtUtc));
        if (!hasNewData)
            return new BaselineEvaluation([], false, lastKillId, statsUpdated, "No statistics changed since the previous evaluation.");

        var observations = capability.HasJointWeaponHitEvents
            ? await WeaponHeadRateObservationsAsync(connection, target, token)
            : [];
        if (observations.Count == 0 && capability.HasHitLocationMarginals)
            observations = await OverallHeadRateObservationsAsync(connection, target, token);
        return new BaselineEvaluation(
            observations,
            true,
            lastKillId,
            statsUpdated,
            observations.Count == 0
                ? "No eligible population baseline met the configured sample safeguards."
                : $"Computed {observations.Count} explainable observation(s).");
    }

    public async Task RecordEvaluationAsync(
        ProactiveEvaluationTarget target,
        BaselineEvaluation baseline,
        RiskAssessment assessment,
        CancellationToken token)
    {
        await using var context = contextFactory.CreateContext();
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        await ExecuteAsync(context.Database.GetDbConnection(), """
            INSERT INTO DemosToDiscordEvaluationState(
                ServerId, ClientId, LastEvaluatedKillId, LastStatisticsUpdatedAtUtc,
                LastEvaluatedAtUtc, LastRiskScore, LastOutcome)
            VALUES(@server, @client, @kill, @stats, @when, @risk, @outcome)
            ON CONFLICT(ServerId, ClientId) DO UPDATE SET
                LastEvaluatedKillId=excluded.LastEvaluatedKillId,
                LastStatisticsUpdatedAtUtc=excluded.LastStatisticsUpdatedAtUtc,
                LastEvaluatedAtUtc=excluded.LastEvaluatedAtUtc,
                LastRiskScore=excluded.LastRiskScore,
                LastOutcome=excluded.LastOutcome;
            """, token,
            ("@server", target.LegacyServerId), ("@client", target.ClientId),
            ("@kill", baseline.LastKillId), ("@stats", DbDate(baseline.StatisticsUpdatedAtUtc)),
            ("@when", DbDate(DateTime.UtcNow)), ("@risk", assessment.Score),
            ("@outcome", assessment.ShouldCreateCase ? "CaseCreated" : assessment.Level));
    }

    public async Task<ProactiveBaselineStatus> GetStatusAsync(CancellationToken token)
    {
        await using var context = contextFactory.CreateContext(false);
        EnsureSqlite(context);
        await context.Database.OpenConnectionAsync(token);
        return await ReadStateAsync(context.Database.GetDbConnection(), token);
    }

    private async Task<ProactiveBaselineStatus> FullRebuildAsync(DbConnection connection, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        try
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM DemosToDiscordBaselineMembers;", token);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DemosToDiscordBaselineMembers(
                    Game, ServerId, ClientId, Weapon, TrackedHits, HeadHits, CriticalHits,
                    KillEvents, HeadKillEvents, LastKillId, LastEventAtUtc)
                SELECT CASE s.GameName WHEN 3 THEN 'IW5' WHEN 7 THEN 'T6' ELSE CAST(s.GameName AS TEXT) END,
                       k.ServerId, k.AttackerId, lower(trim(k.WeaponReference)), COUNT(*),
                       SUM(CASE WHEN k.HitLoc=2 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN k.HitLoc IN (1,2,3) THEN 1 ELSE 0 END),
                       SUM(CASE WHEN k.IsKill=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN k.IsKill=1 AND k.HitLoc=2 THEN 1 ELSE 0 END),
                       MAX(k.KillId), MAX(k."When")
                FROM EFClientKills k
                INNER JOIN EFServers s ON s.ServerId=k.ServerId
                WHERE k.Active=1 AND s.GameName IN (3,7)
                  AND k.WeaponReference IS NOT NULL AND trim(k.WeaponReference)<>''
                GROUP BY s.GameName, k.ServerId, k.AttackerId, lower(trim(k.WeaponReference));
                """, token);
            var quality = await ReadSourceQualityAsync(connection, transaction, 0, token);
            var members = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM DemosToDiscordBaselineMembers;", token);
            var now = DateTime.UtcNow;
            await UpsertStateAsync(connection, transaction, quality.MaxKillId, now, now,
                quality.Events, checked((int)Math.Min(int.MaxValue, members)), quality.MapValues,
                quality.VisibilityValues, null, token);
            await transaction.CommitAsync(token);
            logger.LogInformation(
                "[DemosToDiscord] Rebuilt proactive baseline: {Events} source events collapsed into {Members} members",
                quality.Events,
                members);
            return new ProactiveBaselineStatus(quality.MaxKillId, now, now, quality.Events,
                checked((int)Math.Min(int.MaxValue, members)), quality.MapValues, quality.VisibilityValues, null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProactiveBaselineStatus> IncrementalRefreshAsync(
        DbConnection connection,
        ProactiveBaselineStatus current,
        CancellationToken token)
    {
        var maximum = Math.Max(1, config.ProactiveDetection.MaximumIncrementalEvents);
        var targetHighWater = await ScalarLongAsync(connection, """
            SELECT COALESCE(MAX(KillId), @high) FROM (
                SELECT KillId FROM EFClientKills WHERE KillId>@high ORDER BY KillId LIMIT @maximum
            );
            """, token, ("@high", current.HighWaterKillId), ("@maximum", maximum));
        if (targetHighWater <= current.HighWaterKillId)
        {
            var now = DateTime.UtcNow;
            await UpsertStateAsync(connection, null, current.HighWaterKillId, current.LastFullRebuildUtc,
                now, current.SourceEvents, current.CachedMembers, current.MapValues, current.VisibilityValues, null, token);
            return current with { LastRefreshUtc = now, LastError = null };
        }

        await using var transaction = await connection.BeginTransactionAsync(token);
        try
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO DemosToDiscordBaselineMembers(
                    Game, ServerId, ClientId, Weapon, TrackedHits, HeadHits, CriticalHits,
                    KillEvents, HeadKillEvents, LastKillId, LastEventAtUtc)
                SELECT CASE s.GameName WHEN 3 THEN 'IW5' WHEN 7 THEN 'T6' ELSE CAST(s.GameName AS TEXT) END,
                       k.ServerId, k.AttackerId, lower(trim(k.WeaponReference)), COUNT(*),
                       SUM(CASE WHEN k.HitLoc=2 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN k.HitLoc IN (1,2,3) THEN 1 ELSE 0 END),
                       SUM(CASE WHEN k.IsKill=1 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN k.IsKill=1 AND k.HitLoc=2 THEN 1 ELSE 0 END),
                       MAX(k.KillId), MAX(k."When")
                FROM EFClientKills k
                INNER JOIN EFServers s ON s.ServerId=k.ServerId
                WHERE k.Active=1 AND s.GameName IN (3,7)
                  AND k.KillId>@high AND k.KillId<=@target
                  AND k.WeaponReference IS NOT NULL AND trim(k.WeaponReference)<>''
                GROUP BY s.GameName, k.ServerId, k.AttackerId, lower(trim(k.WeaponReference))
                ON CONFLICT(Game, ServerId, ClientId, Weapon) DO UPDATE SET
                    TrackedHits=TrackedHits+excluded.TrackedHits,
                    HeadHits=HeadHits+excluded.HeadHits,
                    CriticalHits=CriticalHits+excluded.CriticalHits,
                    KillEvents=KillEvents+excluded.KillEvents,
                    HeadKillEvents=HeadKillEvents+excluded.HeadKillEvents,
                    LastKillId=MAX(LastKillId, excluded.LastKillId),
                    LastEventAtUtc=MAX(LastEventAtUtc, excluded.LastEventAtUtc);
                """, token, ("@high", current.HighWaterKillId), ("@target", targetHighWater));
            var delta = await ReadSourceQualityAsync(connection, transaction, current.HighWaterKillId, targetHighWater, token);
            var members = await ScalarLongAsync(connection, transaction,
                "SELECT COUNT(*) FROM DemosToDiscordBaselineMembers;", token);
            var now = DateTime.UtcNow;
            await UpsertStateAsync(connection, transaction, targetHighWater, current.LastFullRebuildUtc,
                now, current.SourceEvents + delta.Events, checked((int)Math.Min(int.MaxValue, members)),
                current.MapValues + delta.MapValues, current.VisibilityValues + delta.VisibilityValues, null, token);
            await transaction.CommitAsync(token);
            return new ProactiveBaselineStatus(targetHighWater, current.LastFullRebuildUtc, now,
                current.SourceEvents + delta.Events, checked((int)Math.Min(int.MaxValue, members)),
                current.MapValues + delta.MapValues, current.VisibilityValues + delta.VisibilityValues, null);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<List<DetectionObservation>> WeaponHeadRateObservationsAsync(
        DbConnection connection,
        ProactiveEvaluationTarget target,
        CancellationToken token)
    {
        var rows = new List<Member>();
        await using var command = CreateCommand(connection, null, """
            SELECT ClientId, Weapon, TrackedHits, HeadHits
            FROM DemosToDiscordBaselineMembers WHERE Game=@game;
            """, ("@game", target.Game.ToUpperInvariant()));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            rows.Add(new Member(reader.GetInt32(0), NormalizeWeapon(reader.GetString(1)),
                Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)));

        var aggregated = rows.GroupBy(item => (item.ClientId, item.Weapon))
            .Select(group => new Member(group.Key.ClientId, group.Key.Weapon,
                group.Sum(item => item.Hits), group.Sum(item => item.HeadHits)))
            .ToList();
        var observations = new List<DetectionObservation>();
        foreach (var player in aggregated.Where(item => item.ClientId == target.ClientId))
        {
            var population = aggregated
                .Where(item => item.Weapon == player.Weapon &&
                               item.Hits >= config.ProactiveDetection.MinimumTrackedHits)
                .Select(item => item.HeadHits / (double)item.Hits)
                .OrderBy(item => item)
                .ToList();
            if (population.Count == 0)
                continue;
            var observed = player.HeadHits / (double)Math.Max(1, player.Hits);
            observations.Add(new DetectionObservation(
                DateTime.UtcNow,
                "tracked_hit_head_rate",
                $"{WeaponLabel(player.Weapon)} tracked-hit head rate",
                $"{target.Game.ToUpperInvariant()} + {WeaponLabel(player.Weapon)}",
                observed,
                Median(population),
                Percentile(population, observed),
                player.Hits,
                player.HeadHits,
                population.Count,
                player.Weapon));
        }
        return observations;
    }

    private async Task<List<DetectionObservation>> OverallHeadRateObservationsAsync(
        DbConnection connection,
        ProactiveEvaluationTarget target,
        CancellationToken token)
    {
        var gameCode = GameCode(target.Game);
        if (gameCode is null)
            return [];
        var members = new List<Member>();
        await using var command = CreateCommand(connection, null, """
            SELECT h.EFClientStatisticsClientId,
                   SUM(CASE WHEN h.Location<>0 THEN h.HitCount ELSE 0 END),
                   SUM(CASE WHEN h.Location=2 THEN h.HitCount ELSE 0 END)
            FROM EFHitLocationCounts h
            INNER JOIN EFServers s ON s.ServerId=h.EFClientStatisticsServerId
            WHERE s.GameName=@game
            GROUP BY h.EFClientStatisticsClientId;
            """, ("@game", gameCode.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            members.Add(new Member(reader.GetInt32(0), string.Empty,
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture)));
        var player = members.FirstOrDefault(item => item.ClientId == target.ClientId);
        if (player is null)
            return [];
        var population = members
            .Where(item => item.Hits >= config.ProactiveDetection.MinimumTrackedHits)
            .Select(item => item.HeadHits / (double)item.Hits)
            .OrderBy(item => item)
            .ToList();
        if (population.Count == 0)
            return [];
        var observed = player.HeadHits / (double)Math.Max(1, player.Hits);
        return [new DetectionObservation(
            DateTime.UtcNow,
            "overall_head_rate",
            "Overall tracked-hit head rate",
            $"{target.Game.ToUpperInvariant()} cumulative multiplayer",
            observed,
            Median(population),
            Percentile(population, observed),
            player.Hits,
            player.HeadHits,
            population.Count)];
    }

    private async Task<(long LastKillId, DateTime? StatisticsUpdatedAtUtc)?> ReadEvaluationStateAsync(
        DbConnection connection,
        ProactiveEvaluationTarget target,
        CancellationToken token)
    {
        await using var command = CreateCommand(connection, null, """
            SELECT LastEvaluatedKillId, LastStatisticsUpdatedAtUtc
            FROM DemosToDiscordEvaluationState WHERE ServerId=@server AND ClientId=@client;
            """, ("@server", target.LegacyServerId), ("@client", target.ClientId));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            return null;
        return (Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
            reader.IsDBNull(1) ? null : ParseDate(reader.GetString(1)));
    }

    private async Task<ProactiveBaselineStatus> ReadStateAsync(DbConnection connection, CancellationToken token)
    {
        await using var command = CreateCommand(connection, null, """
            SELECT HighWaterKillId, LastFullRebuildUtc, LastIncrementalRefreshUtc,
                   SourceEventCount, EligibleMemberCount, MapValueCount, VisibilityValueCount, LastError
            FROM DemosToDiscordBaselineState WHERE SourceName=@source;
            """, ("@source", SourceName));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            return new ProactiveBaselineStatus(0, null, null, 0, 0, 0, 0, null);
        return new ProactiveBaselineStatus(
            Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
            reader.IsDBNull(1) ? null : ParseDate(reader.GetString(1)),
            reader.IsDBNull(2) ? null : ParseDate(reader.GetString(2)),
            Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(5), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(6), CultureInfo.InvariantCulture),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static async Task<SourceQuality> ReadSourceQualityAsync(
        DbConnection connection,
        DbTransaction transaction,
        long afterKillId,
        CancellationToken token) => await ReadSourceQualityAsync(connection, transaction, afterKillId, long.MaxValue, token);

    private static async Task<SourceQuality> ReadSourceQualityAsync(
        DbConnection connection,
        DbTransaction transaction,
        long afterKillId,
        long throughKillId,
        CancellationToken token)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT COALESCE(MAX(k.KillId), @after), COUNT(*),
                   SUM(CASE WHEN k.Map<>0 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN k.VisibilityPercentage<>0 THEN 1 ELSE 0 END)
            FROM EFClientKills k INNER JOIN EFServers s ON s.ServerId=k.ServerId
            WHERE k.Active=1 AND s.GameName IN (3,7) AND k.KillId>@after AND k.KillId<=@through;
            """, ("@after", afterKillId), ("@through", throughKillId));
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return new SourceQuality(
            Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
            reader.IsDBNull(2) ? 0 : Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture));
    }

    private static Task UpsertStateAsync(
        DbConnection connection,
        DbTransaction? transaction,
        long highWater,
        DateTime? full,
        DateTime refresh,
        long sourceEvents,
        int members,
        long mapValues,
        long visibilityValues,
        string? error,
        CancellationToken token) => ExecuteAsync(connection, transaction, """
            INSERT INTO DemosToDiscordBaselineState(
                SourceName, HighWaterKillId, LastFullRebuildUtc, LastIncrementalRefreshUtc,
                SourceEventCount, EligibleMemberCount, MapValueCount, VisibilityValueCount, LastError)
            VALUES(@source, @high, @full, @refresh, @events, @members, @maps, @visibility, @error)
            ON CONFLICT(SourceName) DO UPDATE SET
                HighWaterKillId=excluded.HighWaterKillId,
                LastFullRebuildUtc=excluded.LastFullRebuildUtc,
                LastIncrementalRefreshUtc=excluded.LastIncrementalRefreshUtc,
                SourceEventCount=excluded.SourceEventCount,
                EligibleMemberCount=excluded.EligibleMemberCount,
                MapValueCount=excluded.MapValueCount,
                VisibilityValueCount=excluded.VisibilityValueCount,
                LastError=excluded.LastError;
            """, token,
            ("@source", SourceName), ("@high", highWater), ("@full", DbDate(full)),
            ("@refresh", DbDate(refresh)), ("@events", sourceEvents), ("@members", members),
            ("@maps", mapValues), ("@visibility", visibilityValues), ("@error", error));

    private async Task TryRecordErrorAsync(string message)
    {
        try
        {
            await using var context = contextFactory.CreateContext();
            await context.Database.OpenConnectionAsync();
            await ExecuteAsync(context.Database.GetDbConnection(), """
                UPDATE DemosToDiscordBaselineState SET LastError=@error WHERE SourceName=@source;
                """, CancellationToken.None, ("@error", message), ("@source", SourceName));
        }
        catch
        {
            // The original refresh exception is the useful failure.
        }
    }

    private static string NormalizeWeapon(string value)
    {
        var weapon = value.Trim().ToLowerInvariant().Split('+', 2)[0];
        var match = Regex.Match(weapon, "^(.+?_mp)(?:_.*)?$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : weapon;
    }

    private static string WeaponLabel(string weapon) =>
        weapon.Replace("_mp", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ').ToUpperInvariant();

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    private static double Percentile(IReadOnlyList<double> sorted, double value) =>
        sorted.Count == 0 ? 0 : sorted.Count(item => item <= value) / (double)sorted.Count;

    private static int? GameCode(string game) => game.ToUpperInvariant() switch
    {
        "IW5" => 3,
        "T4" => 5,
        "T5" => 6,
        "T6" => 7,
        _ => null
    };

    private static void EnsureSqlite(DbContext context)
    {
        if (!context.Database.IsSqlite())
            throw new NotSupportedException("DemosToDiscord proactive baselines currently require IW4MAdmin's SQLite provider.");
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
        string sql,
        CancellationToken token,
        params (string Name, object? Value)[] parameters) =>
        await ExecuteAsync(connection, null, sql, token, parameters);

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

    private static async Task<long> ScalarLongAsync(
        DbConnection connection,
        string sql,
        CancellationToken token,
        params (string Name, object? Value)[] parameters) =>
        await ScalarLongAsync(connection, null, sql, token, parameters);

    private static async Task<long> ScalarLongAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture);
    }

    private static async Task<DateTime?> ScalarDateAsync(
        DbConnection connection,
        string sql,
        CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, null, sql, parameters);
        var value = await command.ExecuteScalarAsync(token);
        return value is null or DBNull ? null : ParseDate(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    private static DateTime ParseDate(string value) => DateTime.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static string DbDate(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? DbDate(DateTime? value) => value is null ? null : DbDate(value.Value);

    private sealed record Member(int ClientId, string Weapon, int Hits, int HeadHits);
    private sealed record SourceQuality(long MaxKillId, long Events, long MapValues, long VisibilityValues);
}
