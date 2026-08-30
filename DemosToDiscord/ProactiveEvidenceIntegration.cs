using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed class ProactiveEvidenceIntegration(
    EvidenceCaseStore store,
    DemoUploadService uploads,
    DemosToDiscordConfig config,
    ILogger<ProactiveEvidenceIntegration> logger) : IProactiveAssessmentSink
{
    public async Task HandleAsync(
        ProactiveEvaluationRequest request,
        ProactiveRiskAssessment assessment,
        CancellationToken token)
    {
        if (assessment.Suppressed || assessment.Score < config.ProactiveCaseRiskThreshold)
            return;
        var result = await store.AddOrMergeProactiveAsync(request, assessment, token);
        var capability = uploads.ResolveDemoCapability(result.Case);
        var discordEligible = ProactiveDiscordPolicy.ShouldNotify(assessment, config);
        await store.UpdateAsync(result.Case.Id, item =>
        {
            item.DemoSupport = capability.Status;
            item.DemoSupportReason = capability.Reason;
            item.DiscordEligible |= discordEligible;
        }, token);
        var current = store.Get(result.Case.Id)!;
        switch (ProactiveDiscordPolicy.Action(current, assessment, config, result.NeedsUpload))
        {
            case ProactiveDiscordAction.QueueEvidence:
                await uploads.QueueCaseAsync(result.Case.Id, token);
                break;
            case ProactiveDiscordAction.UpdateExisting:
                await uploads.UpdateCaseDiscordAsync(result.Case.Id, token);
                break;
        }
        logger.LogInformation(
            "[DemosToDiscord] proactive {Level} risk ({Score}/100) retained as case {CaseId} for human review; no punishment was issued",
            assessment.Level, assessment.Score, result.Case.Id);
    }
}

public enum ProactiveDiscordAction
{
    None,
    QueueEvidence,
    UpdateExisting
}

public static class ProactiveDiscordPolicy
{
    public static bool ShouldNotify(ProactiveRiskAssessment assessment, DemosToDiscordConfig config) =>
        config.EnableProactiveDiscordNotifications && !assessment.Suppressed &&
        assessment.Score >= Math.Max(config.ProactiveCaseRiskThreshold, config.ProactiveDiscordRiskThreshold);

    public static ProactiveDiscordAction Action(
        EvidenceCase evidenceCase,
        ProactiveRiskAssessment assessment,
        DemosToDiscordConfig config,
        bool needsEvidenceProcessing)
    {
        if (needsEvidenceProcessing)
            return ProactiveDiscordAction.QueueEvidence;
        if (!string.IsNullOrWhiteSpace(evidenceCase.DiscordMessageId))
            return ProactiveDiscordAction.UpdateExisting;
        return ShouldNotify(assessment, config)
            ? ProactiveDiscordAction.QueueEvidence
            : ProactiveDiscordAction.None;
    }
}
