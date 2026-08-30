using Data.Models;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class ProactiveEvaluationTests
{
    [Fact]
    public void DuplicateEvaluationIsSuppressedWithinWindow()
    {
        var deduplicator = new ProactiveEvaluationDeduplicator(TimeSpan.FromMinutes(30));
        var request = Request();
        var now = DateTime.UtcNow;
        Assert.True(deduplicator.TryAcquire(request, now));
        Assert.False(deduplicator.TryAcquire(request, now.AddMinutes(29)));
        Assert.True(deduplicator.TryAcquire(request, now.AddMinutes(31)));
    }

    [Theory]
    [InlineData(Reference.Game.T6, true, true, true)]
    [InlineData(Reference.Game.IW5, true, true, false)]
    [InlineData(Reference.Game.T5, true, false, true)]
    [InlineData(Reference.Game.T4, true, false, false)]
    public void GameCapabilitiesDegradeWithoutPenalisingGames(
        Reference.Game game, bool supported, bool killingMetrics, bool demos)
    {
        var capability = ProactiveGameCapability.For(game);
        Assert.Equal(supported, capability.Supported);
        Assert.Equal(killingMetrics, capability.HasKillingHitMetrics);
        Assert.Equal(demos, capability.SupportsDemo);
    }

    [Fact]
    public void T5ZombiesAreExplicitlyExcluded()
    {
        var capability = ProactiveGameCapability.For(Reference.Game.T5, true);
        Assert.False(capability.Supported);
        Assert.Contains("separate population", capability.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static ProactiveEvaluationRequest Request() => new(
        1, 123, "Player", 2, "127.0.0.1:4976", "Server", Reference.Game.T6,
        "mp_raid", "dm", DateTime.UtcNow, "test");
}
