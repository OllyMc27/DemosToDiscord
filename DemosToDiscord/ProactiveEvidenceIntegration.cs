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
        await store.UpdateAsync(result.Case.Id, item =>
        {
            item.DemoSupport = capability.Status;
            item.DemoSupportReason = capability.Reason;
        }, token);
        if (result.NeedsUpload)
            await uploads.QueueCaseAsync(result.Case.Id, token);
        logger.LogInformation(
            "[DemosToDiscord] proactive {Level} risk ({Score}/100) retained as case {CaseId} for human review; no punishment was issued",
            assessment.Level, assessment.Score, result.Case.Id);
    }
}
