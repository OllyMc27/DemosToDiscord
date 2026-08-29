using Data.Abstractions;
using Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed class PlayerTimelineService(
    IDatabaseContextFactory databaseContextFactory,
    ILogger<PlayerTimelineService> logger)
{
    public async Task<PlayerMatchTimeline> GetAsync(EvidenceCase evidenceCase, CancellationToken token)
    {
        if (evidenceCase.LegacyServerId is null)
            return new PlayerMatchTimeline(evidenceCase.PlayerJoinedAtUtc, evidenceCase.PlayerLeftAtUtc);

        try
        {
            var anchor = EvidenceMoment(evidenceCase);
            await using var context = databaseContextFactory.CreateContext(false);
            var connection = await context.ConnectionHistory
                .AsNoTracking()
                .Where(item => item.ClientId == evidenceCase.TargetClientId &&
                               item.ServerId == evidenceCase.LegacyServerId.Value &&
                               item.ConnectionType == Reference.ConnectionType.Connect &&
                               item.CreatedDateTime <= anchor)
                .OrderByDescending(item => item.CreatedDateTime)
                .Select(item => (DateTime?)item.CreatedDateTime)
                .FirstOrDefaultAsync(token);

            if (connection is null)
                return new PlayerMatchTimeline(evidenceCase.PlayerJoinedAtUtc, evidenceCase.PlayerLeftAtUtc);

            var nextConnection = await context.ConnectionHistory
                .AsNoTracking()
                .Where(item => item.ClientId == evidenceCase.TargetClientId &&
                               item.ServerId == evidenceCase.LegacyServerId.Value &&
                               item.ConnectionType == Reference.ConnectionType.Connect &&
                               item.CreatedDateTime > connection.Value)
                .OrderBy(item => item.CreatedDateTime)
                .Select(item => (DateTime?)item.CreatedDateTime)
                .FirstOrDefaultAsync(token);

            var disconnectQuery = context.ConnectionHistory
                .AsNoTracking()
                .Where(item => item.ClientId == evidenceCase.TargetClientId &&
                               item.ServerId == evidenceCase.LegacyServerId.Value &&
                               item.ConnectionType == Reference.ConnectionType.Disconnect &&
                               item.CreatedDateTime >= connection.Value);
            if (nextConnection is not null)
                disconnectQuery = disconnectQuery.Where(item => item.CreatedDateTime < nextConnection.Value);

            var disconnect = await disconnectQuery
                .OrderBy(item => item.CreatedDateTime)
                .Select(item => (DateTime?)item.CreatedDateTime)
                .FirstOrDefaultAsync(token);

            return new PlayerMatchTimeline(
                EvidenceTime.AsUtc(connection.Value),
                disconnect is null ? null : EvidenceTime.AsUtc(disconnect.Value));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[DemosToDiscord] Could not query the player timeline for case {CaseId}", evidenceCase.Id);
            return new PlayerMatchTimeline(evidenceCase.PlayerJoinedAtUtc, evidenceCase.PlayerLeftAtUtc);
        }
    }

    private static DateTime EvidenceMoment(EvidenceCase evidenceCase)
    {
        var moments = evidenceCase.Reports.Select(item => item.WhenUtc).ToList();
        if (evidenceCase.AntiCheat is not null)
            moments.Add(evidenceCase.AntiCheat.WhenUtc);
        return EvidenceTime.AsUtc(moments.Count == 0 ? evidenceCase.CreatedAtUtc : moments.Min());
    }
}
