using Microsoft.Extensions.Logging;
using SharedLibraryCore.Dtos.Meta.Responses;
using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

public sealed class PlayerNoteService(
    IMetaServiceV2 metaService,
    ILogger<PlayerNoteService> logger)
{
    private const string MetaKey = "ClientNotes";
    private const string Prefix = "[DemosToDiscord]";
    private const int MaximumNoteLength = 4000;

    public async Task<int?> AppendCaseActionAsync(
        int targetClientId,
        int actorClientId,
        string actorName,
        string caseId,
        string action,
        CancellationToken token)
    {
        try
        {
            var existing = await metaService.GetPersistentMetaValue<ClientNoteMetaResponse>(
                MetaKey,
                targetClientId,
                token);
            var timestamp = EvidenceTime.Format(DateTime.UtcNow);
            var entry = $"{Prefix} {timestamp} — Case {caseId} — {action} — {actorName}";
            var note = AppendWithoutDestroyingManualText(existing?.Note, entry);
            if (note is null)
            {
                logger.LogWarning(
                    "[DemosToDiscord] Player note for client {ClientId} is already at the safe size limit; case {CaseId} was not appended",
                    targetClientId,
                    caseId);
                return null;
            }

            await metaService.SetPersistentMetaValue(
                MetaKey,
                new ClientNoteMetaResponse
                {
                    Note = note,
                    OriginEntityId = actorClientId,
                    OriginEntityName = actorName,
                    ModifiedDate = DateTime.UtcNow
                },
                targetClientId,
                token);
            return (await metaService.GetPersistentMeta(MetaKey, targetClientId, token))?.MetaId;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "[DemosToDiscord] Could not append native player note for client {ClientId}, case {CaseId}",
                targetClientId,
                caseId);
            return null;
        }
    }

    internal static string? AppendWithoutDestroyingManualText(string? existing, string entry)
    {
        var lines = (existing ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
        while (string.Join(Environment.NewLine, lines.Append(entry)).Length > MaximumNoteLength)
        {
            var removable = lines.FindIndex(line => line.StartsWith(Prefix, StringComparison.Ordinal));
            if (removable < 0)
                return null;
            lines.RemoveAt(removable);
        }

        if (lines.Count == 1 && string.IsNullOrWhiteSpace(lines[0]))
            lines.Clear();
        lines.Add(entry);
        return string.Join(Environment.NewLine, lines);
    }
}
