using Data.Models;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class ProactiveBaselineTests
{
    [Fact]
    public void MedianAndPercentileAreEmpiricalAndDeterministic()
    {
        Assert.Equal(2.5, ProactiveBaselineMath.Median([1, 2, 3, 4]));
        Assert.Equal(75, ProactiveBaselineMath.PercentileRank([1, 2, 3, 4], 3));
    }

    [Fact]
    public void TinySamplesAreExcludedFromTrackedHeadPopulation()
    {
        var members = Enumerable.Range(1, 20).Select(index => Member(index, 500, 30)).ToList();
        members.Add(Member(99, 5, 3));
        var baseline = ProactiveBaselineSelector.Build(
            members, ProactiveMetric.TrackedHeadRate, Reference.Game.T6, null, 20, 200, 10);
        Assert.NotNull(baseline);
        Assert.Equal(20, baseline!.EligiblePlayers);
    }

    [Fact]
    public void InsufficientPopulationSuppressesBaseline()
    {
        var members = Enumerable.Range(1, 9).Select(index => Member(index, 500, 30));
        Assert.Null(ProactiveBaselineSelector.Build(
            members, ProactiveMetric.TrackedHeadRate, Reference.Game.T6, null, 10, 200, 10));
    }

    [Fact]
    public void ExcludedServerMembersNeverEnterPopulation()
    {
        var members = Enumerable.Range(1, 10).Select(index => Member(index, 500, 30)).ToList();
        members[0].Excluded = true;
        Assert.Null(ProactiveBaselineSelector.Build(
            members, ProactiveMetric.TrackedHeadRate, Reference.Game.T6, null, 10, 200, 10));
    }

    private static ProactiveBaselineMember Member(int id, int hits, int heads) => new()
    {
        ClientId = id,
        ServerId = 1,
        Game = Reference.Game.T6,
        Kills = 200,
        Deaths = 100,
        TimePlayedSeconds = 7200,
        TrackedHits = hits,
        TrackedHeadHits = heads
    };
}
