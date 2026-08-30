using Data.Models;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class ProactiveRiskScorerTests
{
    [Fact]
    public void CorrelatedHeadSignalsOnlyUseStrongestContribution()
    {
        var provider = Provider(new ProactiveBaselineMember
        {
            ClientId = 1, ServerId = 2, Game = Reference.Game.T6,
            Kills = 800, Deaths = 100, TimePlayedSeconds = 7200,
            TrackedHits = 1000, TrackedHeadHits = 400,
            KillingHits = 500, KillingHeadHits = 200
        });
        var score = new ProactiveRiskScorer(provider, Config()).Score(1, 2);
        var aimSum = score.Signals.Where(item => item.CorrelationGroup == "aim").Sum(item => item.Contribution);
        var aimMax = score.Signals.Where(item => item.CorrelationGroup == "aim").Max(item => item.Contribution);
        Assert.True(aimSum > aimMax);
        Assert.True(score.Score < aimSum + 10); // correlation protection prevents blindly summing both rates
    }

    [Fact]
    public void RepeatHistoryAddsBoundedWeight()
    {
        var member = new ProactiveBaselineMember
        {
            ClientId = 1, ServerId = 2, Game = Reference.Game.T6,
            Kills = 800, Deaths = 100, TimePlayedSeconds = 7200,
            TrackedHits = 1000, TrackedHeadHits = 400
        };
        var scorer = new ProactiveRiskScorer(Provider(member), Config());
        var first = scorer.Score(1, 2, 0);
        var repeat = scorer.Score(1, 2, 99);
        Assert.Equal(12, repeat.Score - first.Score);
    }

    [Fact]
    public void T5ZombiesOrExcludedServerIsSuppressed()
    {
        var member = new ProactiveBaselineMember { ClientId = 1, ServerId = 2, Game = Reference.Game.T5, Excluded = true };
        Assert.True(new ProactiveRiskScorer(Provider(member), Config()).Score(1, 2).Suppressed);
    }

    [Fact]
    public void AssessmentNeverRecommendsAutomaticPunishmentOrAccuracy()
    {
        var member = new ProactiveBaselineMember
        {
            ClientId = 1, ServerId = 2, Game = Reference.Game.IW5,
            Kills = 800, Deaths = 100, TimePlayedSeconds = 7200,
            TrackedHits = 1000, TrackedHeadHits = 400
        };
        var result = new ProactiveRiskScorer(Provider(member), Config()).Score(1, 2);
        Assert.Contains("no automatic punishment", result.RecommendedAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Signals, item => item.Label.Contains("accuracy", StringComparison.OrdinalIgnoreCase));
    }

    private static FakeProvider Provider(ProactiveBaselineMember member) => new(member);
    private static DemosToDiscordConfig Config() => new() { ProactiveMinimumPopulation = 100 };

    private sealed class FakeProvider(ProactiveBaselineMember member) : IProactiveBaselineProvider
    {
        public bool IsAvailable => true;
        public ProactiveBaselineMember? GetPlayer(int clientId, long serverId) => member;
        public ProactivePopulationBaseline? GetPopulation(ProactiveMetric metric, Reference.Game game, long? serverId = null, string? weapon = null)
        {
            var rateMetric = metric is ProactiveMetric.TrackedHeadRate or ProactiveMetric.KillingHeadRate;
            var values = Enumerable.Range(1, 100).Select(index => index / (rateMetric ? 1000d : 100d)).ToList();
            return new ProactivePopulationBaseline(metric, serverId is null ? game.ToString() : $"{game} + server", 100, values[49], values);
        }
    }
}
