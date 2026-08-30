using Data.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class ProactiveCaseTests
{
    [Fact]
    public async Task ProactiveDetectionCreatesCaseAndPreservesExplanation()
    {
        var (store, path) = Store();
        try
        {
            var result = await store.AddOrMergeProactiveAsync(Request(), Assessment(), CancellationToken.None);
            Assert.True(result.Created);
            Assert.Single(result.Case.ProactiveDetections);
            Assert.Equal(74, result.Case.ProactiveDetections[0].RiskScore);
            Assert.Contains(EvidenceTriggerType.ProactiveDetection, result.Case.TriggerTypes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LaterReportAndAntiCheatMergeIntoSameCase()
    {
        var (store, path) = Store();
        try
        {
            var proactive = await store.AddOrMergeProactiveAsync(Request(), Assessment(), CancellationToken.None);
            var report = await store.AddOrMergeAsync(Capture(EvidenceTriggerType.Report), CancellationToken.None);
            var antiCheat = await store.AddOrMergeAsync(Capture(EvidenceTriggerType.AutomatedBan), CancellationToken.None);
            Assert.False(report.Created);
            Assert.False(antiCheat.Created);
            Assert.Equal(proactive.Case.Id, antiCheat.Case.Id);
            Assert.Single(antiCheat.Case.Reports);
            Assert.NotNull(antiCheat.Case.AntiCheat);
            Assert.True(antiCheat.Case.DiscordEligible);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CapabilityRoutesSupportedAndMetadataOnlyEvidence()
    {
        Assert.True(ProactiveGameCapability.For(Reference.Game.T6).SupportsDemo);
        Assert.True(ProactiveGameCapability.For(Reference.Game.T5).SupportsDemo);
        Assert.False(ProactiveGameCapability.For(Reference.Game.T4).SupportsDemo);
        Assert.False(ProactiveGameCapability.For(Reference.Game.IW5).SupportsDemo);
    }

    private static (EvidenceCaseStore Store, string Path) Store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dtd-{Guid.NewGuid():N}.json");
        var config = new DemosToDiscordConfig { StateFilePath = path, DeduplicationWindowMinutes = 120 };
        return (new EvidenceCaseStore(config, NullLogger<EvidenceCaseStore>.Instance), path);
    }

    private static ProactiveEvaluationRequest Request() => new(
        10, 1234, "Example", 20, "127.0.0.1:4976", "T6 Server", Reference.Game.T6,
        "mp_raid", "dm", DateTime.UtcNow, "disconnect");

    private static ProactiveRiskAssessment Assessment() => new(
        10, 20, Reference.Game.T6, 74, ProactiveRiskLevel.High, false, string.Empty,
        [new(ProactiveMetric.TrackedHeadRate, "tracked-hit head rate", "aim", .194, 99.5, .084, 2.31, 252, 190, "T6 + server", 22)], 0);

    private static PenaltyCapture Capture(EvidenceTriggerType trigger)
    {
        var request = Request();
        return new PenaltyCapture(
            trigger, request.RequestedAtUtc, request.ServerEndpoint, request.ServerName, request.ServerId,
            request.Game.ToString(), request.Map, request.Mode, request.ClientId, request.NetworkId,
            request.PlayerName, 1, "Admin", "cheating", "snap", 1);
    }
}
