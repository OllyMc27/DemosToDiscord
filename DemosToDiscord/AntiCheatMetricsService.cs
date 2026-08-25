using Data.Abstractions;
using Data.Models;
using Data.Models.Client.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed class AntiCheatMetricsService(
    IDatabaseContextFactory databaseContextFactory,
    ILogger<AntiCheatMetricsService> logger)
{
    private const int HitLocationNone = 0;
    private const int HitLocationHead = 2;
    private const int HitLocationChest = 4;
    private const int HitLocationAbdomen = 5;

    public async Task<AntiCheatCaseMetrics> GetAsync(EvidenceCase evidenceCase, CancellationToken token)
    {
        try
        {
            await using var context = databaseContextFactory.CreateContext(false);
            var playerMetrics = await GetPlayerMetricsAsync(context, evidenceCase, token);
            if (evidenceCase.AntiCheat is null)
                return new AntiCheatCaseMetrics(null, string.Empty, [], playerMetrics);

            var when = evidenceCase.AntiCheat.WhenUtc;
            var penaltyId = evidenceCase.AntiCheat.PenaltyId;
            if (penaltyId is null)
            {
                penaltyId = await context.Penalties
                    .Where(item => item.OffenderId == evidenceCase.TargetClientId &&
                                   item.Type == EFPenalty.PenaltyType.Ban &&
                                   item.PunisherId == 1 &&
                                   item.When >= when.AddMinutes(-2) && item.When <= when.AddMinutes(2))
                    .OrderByDescending(item => item.When)
                    .Select(item => (int?)item.PenaltyId)
                    .FirstOrDefaultAsync(token);
            }

            var entities = await context.ACSnapshots
                .AsNoTracking()
                .Include(item => item.PredictedViewAngles)
                .ThenInclude(item => item.Vector)
                .Where(item => item.ClientId == evidenceCase.TargetClientId &&
                               item.When >= when.AddMinutes(-15) && item.When <= when.AddMinutes(2))
                .OrderByDescending(item => item.When)
                .Take(50)
                .ToListAsync(token);
            var snapshots = entities.Select(item => new AntiCheatMetricSnapshot(
                    item.When,
                    item.CurrentSessionLength,
                    item.TimeSinceLastEvent,
                    item.EloRating,
                    item.SessionScore,
                    item.SessionSPM,
                    item.Hits,
                    item.Kills,
                    item.Deaths,
                    item.CurrentStrain,
                    item.StrainAngleBetween,
                    item.SessionAngleOffset,
                    item.RecoilOffset,
                    item.SessionAverageSnapValue,
                    item.SessionSnapHits,
                    item.WeaponReference ?? "Unknown",
                    item.HitLocationReference ?? "Unknown",
                    item.HitType,
                    item.CapturedViewAngles,
                    item.CurrentViewAngle.ToString(),
                    item.LastStrainAngle.ToString(),
                    item.HitOrigin.ToString(),
                    item.HitDestination.ToString(),
                    item.Distance))
                .ToList();

            return new AntiCheatCaseMetrics(penaltyId, evidenceCase.AntiCheat.Detection, snapshots, playerMetrics);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[DemosToDiscord] Could not query metrics for case {CaseId}", evidenceCase.Id);
            return new AntiCheatCaseMetrics(
                evidenceCase.AntiCheat?.PenaltyId,
                evidenceCase.AntiCheat?.Detection ?? string.Empty,
                [],
                null);
        }
    }

    private static async Task<PlayerEvidenceMetrics?> GetPlayerMetricsAsync(
        Data.Context.DatabaseContext context,
        EvidenceCase evidenceCase,
        CancellationToken token)
    {
        var query = context.Set<EFClientStatistics>()
            .AsNoTracking()
            .Include(item => item.HitLocations)
            .Where(item => item.ClientId == evidenceCase.TargetClientId);
        if (evidenceCase.LegacyServerId is not null)
            query = query.Where(item => item.ServerId == evidenceCase.LegacyServerId.Value);

        var statistics = await query.ToListAsync(token);
        if (statistics.Count == 0)
            return null;

        var kills = statistics.Sum(item => item.Kills);
        var deaths = statistics.Sum(item => item.Deaths);
        var validPerformance = statistics.Where(item => item.Performance > 0 && item.TimePlayed > 0).ToList();
        var performanceTime = validPerformance.Sum(item => item.TimePlayed);
        var performance = performanceTime == 0
            ? 0
            : validPerformance.Sum(item => item.Performance * item.TimePlayed) / performanceTime;
        var positiveSpm = statistics.Where(item => item.SPM > 0).Select(item => item.SPM).ToList();
        var hits = statistics.SelectMany(item => item.HitLocations ?? []).ToList();
        var countedHits = hits.Where(item => item.Location != HitLocationNone).Sum(item => item.HitCount);
        var chestHits = hits.Where(item => item.Location == HitLocationChest).Sum(item => item.HitCount);
        var abdomenHits = hits.Where(item => item.Location == HitLocationAbdomen).Sum(item => item.HitCount);
        var headHits = hits.Where(item => item.Location == HitLocationHead).Sum(item => item.HitCount);
        var offsetHitCount = hits.Where(item => item.HitCount > 0).Sum(item => item.HitCount);
        var averageOffset = offsetHitCount == 0
            ? 0
            : hits.Where(item => item.HitCount > 0).Sum(item => item.HitCount * item.HitOffsetAverage) / offsetHitCount;
        var snapValues = statistics.Where(item => item.AverageSnapValue > 0).Select(item => item.AverageSnapValue).ToList();

        return new PlayerEvidenceMetrics(
            kills,
            deaths,
            deaths == 0 ? kills : Math.Round(kills / (double)deaths, 2),
            Math.Round(performance, 2),
            positiveSpm.Count == 0 ? 0 : Math.Round(positiveSpm.Average(), 1),
            statistics.Sum(item => item.TimePlayed),
            Percent(chestHits, countedHits),
            Percent(abdomenHits, countedHits),
            Percent(chestHits, abdomenHits),
            Percent(headHits, countedHits),
            Math.Round(averageOffset, 4),
            Math.Round(statistics.Max(item => item.MaxStrain), 3),
            snapValues.Count == 0 ? 0 : Math.Round(snapValues.Average(), 3),
            statistics.Sum(item => item.SnapHitCount));
    }

    private static double Percent(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / (double)denominator * 100, 1);
}

