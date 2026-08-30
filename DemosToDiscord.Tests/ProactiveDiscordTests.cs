using Data.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SharedLibraryCore.Configuration;
using System.Text.Json;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class ProactiveDiscordTests
{
    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    [InlineData(90, true)]
    public void SeparateDiscordThresholdIsRespected(int risk, bool expected)
    {
        var config = new DemosToDiscordConfig
        {
            ProactiveCaseRiskThreshold = 50,
            ProactiveDiscordRiskThreshold = 65,
            EnableProactiveDiscordNotifications = true
        };
        Assert.Equal(expected, ProactiveDiscordPolicy.ShouldNotify(Assessment(risk), config));
    }

    [Fact]
    public void ProactiveEmbedIsExplainableAndDoesNotClaimAccuracyOrPunishment()
    {
        using var client = new DiscordWebhookClient(
            new ApplicationConfiguration(), new DemosToDiscordConfig(), NullLogger<DiscordWebhookClient>.Instance);
        var evidenceCase = new EvidenceCase
        {
            Id = "abc123", CreatedAtUtc = DateTime.UtcNow, TargetClientId = 10, TargetNetworkId = 123,
            TargetName = "Example", Game = "T6", Map = "mp_raid", Mode = "dm", ServerName = "Server",
            ServerId = "127.0.0.1:4976", ProactiveDetections =
            [
                new ProactiveDetectionEvidence
                {
                    WhenUtc = DateTime.UtcNow, RiskScore = 74, RiskLevel = ProactiveRiskLevel.High,
                    Signals = Assessment(74).Signals.ToList()
                }
            ]
        };
        var json = JsonSerializer.Serialize(client.BuildEmbed(evidenceCase, null, null));
        Assert.Contains("Proactive detection", json);
        Assert.Contains("99.5", json);
        Assert.Contains("human review", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accuracy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("automatically banned", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingDiscordCaseIsUpdatedInsteadOfCreatingDuplicate()
    {
        var evidenceCase = new EvidenceCase { DiscordMessageId = "existing-message" };
        var action = ProactiveDiscordPolicy.Action(
            evidenceCase, Assessment(70), new DemosToDiscordConfig(), needsEvidenceProcessing: false);
        Assert.Equal(ProactiveDiscordAction.UpdateExisting, action);
    }

    [Fact]
    public void BelowDiscordThresholdStaysOnWebReviewPath()
    {
        var action = ProactiveDiscordPolicy.Action(
            new EvidenceCase(), Assessment(55), new DemosToDiscordConfig
            {
                ProactiveCaseRiskThreshold = 50,
                ProactiveDiscordRiskThreshold = 65
            }, needsEvidenceProcessing: false);
        Assert.Equal(ProactiveDiscordAction.None, action);
    }

    private static ProactiveRiskAssessment Assessment(int score) => new(
        10, 20, Reference.Game.T6, score, score >= 65 ? ProactiveRiskLevel.High : ProactiveRiskLevel.Review,
        false, string.Empty,
        [new(ProactiveMetric.TrackedHeadRate, "tracked-hit head rate", "aim", .194, 99.5, .084, 2.31, 252, 190, "T6 + weapon", 22)], 0);
}
