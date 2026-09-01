using Data.Abstractions;
using Data.Models;
using Data.Models.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

public sealed class EvidenceReviewService(
    IServiceProvider serviceProvider,
    IDatabaseContextFactory databaseContextFactory,
    DemosToDiscordConfig config,
    EvidenceCaseStore store,
    DemoUploadService uploadService,
    FlaggedPlayerReviewService flaggedPlayers,
    ILogger<EvidenceReviewService> logger)
{
    public async Task<bool> CanDeleteAsync(int originId)
    {
        var manager = serviceProvider.GetRequiredService<IManager>();
        var origin = await manager.GetClientService().Get(originId);
        return origin?.Level >= EFClient.Permission.Owner;
    }

    public async Task<string> DeleteAsync(
        int originId,
        int? targetId,
        IDictionary<string, string> input,
        CancellationToken token)
    {
        var manager = serviceProvider.GetRequiredService<IManager>();
        var origin = await manager.GetClientService().Get(originId)
                     ?? throw new UnauthorizedAccessException("The administrator could not be resolved.");
        if (origin.Level < EFClient.Permission.Owner)
            throw new UnauthorizedAccessException("Owner permission is required to delete evidence cases.");

        if (!input.TryGetValue("CaseId", out var caseId) || string.IsNullOrWhiteSpace(caseId))
            throw new ArgumentException("Evidence case ID is required.");
        var evidenceCase = store.Get(caseId) ?? throw new ArgumentException($"Evidence case {caseId} was not found.");
        if (targetId is not null && targetId != evidenceCase.TargetClientId)
            throw new UnauthorizedAccessException("The requested target does not match the evidence case.");
        if (!input.TryGetValue("ConfirmDelete", out var confirmation) ||
            !bool.TryParse(confirmation, out var confirmed) || !confirmed)
        {
            throw new ArgumentException("Confirm permanent deletion before continuing.");
        }

        if (!await store.DeleteAsync(caseId, token))
            throw new ArgumentException($"Evidence case {caseId} was not found.");

        logger.LogWarning(
            "[DemosToDiscord] Case {CaseId} permanently deleted by Owner {OwnerId}",
            caseId, origin.ClientId);
        return "Evidence case permanently deleted. Player penalties and Discord messages were not changed.";
    }

    public async Task<string> ExecuteAsync(
        int originId,
        int? targetId,
        IDictionary<string, string> input,
        CancellationToken token)
    {
        var manager = serviceProvider.GetRequiredService<IManager>();
        var origin = await manager.GetClientService().Get(originId)
                     ?? throw new UnauthorizedAccessException("The reviewing administrator could not be resolved.");
        var requiredLevel = (EFClient.Permission)Math.Max(
            (int)EFClient.Permission.Moderator,
            (int)config.WebfrontMinimumPermission);
        if (origin.Level < requiredLevel)
            throw new UnauthorizedAccessException($"{requiredLevel} permission is required to review evidence cases.");

        if (!input.TryGetValue("CaseId", out var caseId) || string.IsNullOrWhiteSpace(caseId))
            throw new ArgumentException("Evidence case ID is required.");
        var evidenceCase = store.Get(caseId) ?? throw new ArgumentException($"Evidence case {caseId} was not found.");
        if (targetId is not null && targetId != evidenceCase.TargetClientId)
            throw new UnauthorizedAccessException("The requested target does not match the evidence case.");

        input.TryGetValue("Operation", out var operation);
        var originName = origin.CurrentAlias?.Name.StripColors() ?? $"Client #{origin.ClientId}";
        if (operation?.Equals("Assign", StringComparison.OrdinalIgnoreCase) == true)
        {
            await store.UpdateAsync(caseId, item =>
            {
                item.AssignedToClientId = origin.ClientId;
                item.AssignedToName = originName;
                item.AssignedAtUtc = DateTime.UtcNow;
                item.History.Add(History(EvidenceHistoryAction.Assigned, origin.ClientId, originName,
                    $"Case assigned to {originName}."));
            }, token);
            await uploadService.UpdateCaseDiscordAsync(caseId, token);
            return "Case assigned to you.";
        }

        if (operation?.Equals("Unassign", StringComparison.OrdinalIgnoreCase) == true)
        {
            await store.UpdateAsync(caseId, item =>
            {
                item.AssignedToClientId = null;
                item.AssignedToName = null;
                item.AssignedAtUtc = null;
                item.History.Add(History(EvidenceHistoryAction.Unassigned, origin.ClientId, originName,
                    "Case assignment cleared."));
            }, token);
            await uploadService.UpdateCaseDiscordAsync(caseId, token);
            return "Case assignment cleared.";
        }

        if (operation?.Equals("ClearReports", StringComparison.OrdinalIgnoreCase) == true)
        {
            var cleared = await ClearReportsAsync(manager, evidenceCase, origin.ClientId, originName, token);
            await uploadService.UpdateCaseDiscordAsync(caseId, token);
            return cleared == 1 ? "One active report was cleared." : $"{cleared} active reports were cleared.";
        }

        if (!input.TryGetValue("Decision", out var decisionText) ||
            !Enum.TryParse<EvidenceReviewDecision>(decisionText, true, out var decision) ||
            decision == EvidenceReviewDecision.Unreviewed)
        {
            throw new ArgumentException("Select a valid review decision.");
        }

        input.TryGetValue("Notes", out var notes);
        notes = notes?.Trim();
        if (notes?.Length > 1000)
            throw new ArgumentException("Review notes must be 1,000 characters or fewer.");

        var clearReports = input.TryGetValue("ClearReports", out var clearText) &&
                           bool.TryParse(clearText, out var clear) && clear;
        var clearedCount = clearReports
            ? await ClearReportsAsync(manager, evidenceCase, origin.ClientId, originName, token)
            : 0;
        var flagResult = decision == EvidenceReviewDecision.Inconclusive
            ? await flaggedPlayers.FlagAfterInconclusiveAsync(manager, evidenceCase, origin, token)
            : null;
        await store.UpdateAsync(caseId, item =>
        {
            item.ReviewDecision = decision;
            item.ReviewedAtUtc = DateTime.UtcNow;
            item.ReviewedByClientId = origin.ClientId;
            item.ReviewedByName = originName;
            item.ReviewNotes = notes;
            item.History.Add(new EvidenceHistoryEntry
            {
                WhenUtc = DateTime.UtcNow,
                Action = EvidenceHistoryAction.ReviewChanged,
                AdminClientId = origin.ClientId,
                AdminName = originName,
                Summary = $"Review decision changed to {DecisionLabel(decision)}.",
                Decision = decision,
                Notes = notes,
                ReportsCleared = clearedCount
            });
        }, token);
        await uploadService.UpdateCaseDiscordAsync(caseId, token);

        logger.LogInformation(
            "[DemosToDiscord] Case {CaseId} reviewed as {Decision} by {Reviewer}; cleared {ReportCount} report(s)",
            caseId, decision, origin.ClientId, clearedCount);
        var response = clearReports
            ? $"Case marked {DecisionLabel(decision)} and {clearedCount} active report(s) cleared."
            : $"Case marked {DecisionLabel(decision)}.";
        return string.IsNullOrWhiteSpace(flagResult) ? response : $"{response} {flagResult}";
    }

    private async Task<int> ClearReportsAsync(
        IManager manager,
        EvidenceCase evidenceCase,
        int originId,
        string originName,
        CancellationToken token)
    {
        var clearedDatabaseReports = 0;
        await using (var context = databaseContextFactory.CreateContext())
        {
            var penaltyIds = evidenceCase.Reports
                .Where(item => item.PenaltyId is > 0)
                .Select(item => item.PenaltyId!.Value)
                .ToHashSet();
            var reportQuery = context.Penalties.Where(item =>
                item.Active && item.Type == EFPenalty.PenaltyType.Report &&
                item.OffenderId == evidenceCase.TargetClientId);

            List<EFPenalty> reports;
            if (penaltyIds.Count > 0)
            {
                reports = await reportQuery.Where(item => penaltyIds.Contains(item.PenaltyId)).ToListAsync(token);
            }
            else if (evidenceCase.Reports.Count > 0)
            {
                var earliest = evidenceCase.Reports.Min(item => item.WhenUtc).AddMinutes(-1);
                var latest = evidenceCase.Reports.Max(item => item.WhenUtc).AddMinutes(1);
                reports = await reportQuery.Where(item => item.When >= earliest && item.When <= latest).ToListAsync(token);
            }
            else
            {
                reports = [];
            }

            foreach (var report in reports)
            {
                report.Active = false;
                report.Expires = DateTime.UtcNow;
            }

            clearedDatabaseReports = reports.Count;
            if (reports.Count > 0)
                await context.SaveChangesAsync(token);
        }

        var clearedLiveReports = 0;
        var server = manager.GetServers().FirstOrDefault(item =>
            item.Id.Equals(evidenceCase.ServerId, StringComparison.OrdinalIgnoreCase));
        if (server is not null)
        {
            lock (server.Reports)
            {
                clearedLiveReports = server.Reports.RemoveAll(report =>
                    report.Target.ClientId == evidenceCase.TargetClientId &&
                    evidenceCase.Reports.Any(item =>
                        Math.Abs((item.WhenUtc - report.ReportedOn.ToUniversalTime()).TotalSeconds) <= 60));
            }
        }

        var clearedCount = Math.Max(clearedDatabaseReports, clearedLiveReports);
        await store.UpdateAsync(evidenceCase.Id, item =>
        {
            item.ReportsClearedAtUtc = DateTime.UtcNow;
            item.ReportsClearedCount += clearedCount;
            item.History.Add(new EvidenceHistoryEntry
            {
                WhenUtc = DateTime.UtcNow,
                Action = EvidenceHistoryAction.ReportsCleared,
                AdminClientId = originId,
                AdminName = originName,
                Summary = $"{clearedCount} active report(s) cleared.",
                ReportsCleared = clearedCount
            });
        }, token);
        return clearedCount;
    }

    internal static string DecisionLabel(EvidenceReviewDecision decision) => decision switch
    {
        EvidenceReviewDecision.NeedsMoreReview => "needs more review",
        EvidenceReviewDecision.CheatingActionTaken => "cheating — action taken",
        EvidenceReviewDecision.CheatingNoAction => "cheating — no action taken",
        EvidenceReviewDecision.NotCheatingNoAction => "not cheating — no action taken",
        EvidenceReviewDecision.Inconclusive => "inconclusive",
        _ => "unreviewed"
    };

    private static EvidenceHistoryEntry History(
        EvidenceHistoryAction action,
        int adminId,
        string adminName,
        string summary) => new()
    {
        WhenUtc = DateTime.UtcNow,
        Action = action,
        AdminClientId = adminId,
        AdminName = adminName,
        Summary = summary
    };
}

