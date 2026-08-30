using Data.Abstractions;
using Data.Context;
using Data.Models;
using Data.Models.Client;
using Data.Models.Client.Stats;
using Data.Models.Client.Stats.Reference;
using Data.Models.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class ProactiveBaselineDatabaseTests
{
    [Fact]
    public async Task LiveRefreshBootstrapsThenAppliesChangedStatisticsAndNewKills()
    {
        var token = TestContext.Current.CancellationToken;
        var databaseName = $"dtd-{Guid.NewGuid():N}";
        var factory = new InMemoryFactory(databaseName);
        var now = DateTime.UtcNow.AddMinutes(-2);
        await using (var context = factory.CreateContext())
        {
            context.Servers.Add(new EFServer
            {
                ServerId = 20, EndPoint = "127.0.0.1:4976", Port = 4976,
                HostName = "T6 Multiplayer", GameName = Reference.Game.T6
            });
            context.ClientStatistics.Add(new EFClientStatistics
            {
                ClientId = 10, ServerId = 20, Kills = 200, Deaths = 100, SPM = 400,
                Skill = 100, EloRating = 100, TimePlayed = 7200, UpdatedAt = now
            });
            context.HitLocations.Add(new EFHitLocation
            {
                HitLocationId = 40, Name = "head", Game = Reference.Game.T6
            });
            context.HitStatistics.Add(new EFClientHitStatistic
            {
                ClientHitStatisticId = 1, ClientId = 10, ServerId = 20, HitLocationId = 40,
                HitCount = 20, CreatedDateTime = now
            });
            context.ClientKills.Add(new EFClientKill
            {
                KillId = 1, AttackerId = 10, VictimId = 11, ServerId = 20, IsKill = true,
                HitLoc = 2, WeaponReference = "an94_mp", When = now
            });
            await context.SaveChangesAsync(token);
        }

        var statePath = Path.Combine(Path.GetTempPath(), $"dtd-baseline-{Guid.NewGuid():N}.json");
        var config = new DemosToDiscordConfig
        {
            ProactiveBaselineStateFilePath = statePath,
            ProactiveMinimumPopulation = 1
        };
        using var service = new ProactiveBaselineService(factory, config, NullLogger<ProactiveBaselineService>.Instance);
        await service.RefreshAsync(token);
        Assert.Equal(200, service.GetPlayer(10, 20)!.Kills);
        Assert.Equal(1, service.GetPlayer(10, 20)!.KillingHits);

        await using (var context = factory.CreateContext())
        {
            var statistics = await context.ClientStatistics.SingleAsync(token);
            statistics.Kills = 240;
            statistics.UpdatedAt = DateTime.UtcNow;
            context.ClientKills.Add(new EFClientKill
            {
                KillId = 2, AttackerId = 10, VictimId = 12, ServerId = 20, IsKill = true,
                HitLoc = 2, WeaponReference = "an94_mp", When = DateTime.UtcNow
            });
            await context.SaveChangesAsync(token);
        }

        await service.RefreshAsync(token);
        var refreshed = service.GetPlayer(10, 20)!;
        Assert.Equal(240, refreshed.Kills);
        Assert.Equal(2, refreshed.KillingHits);
        Assert.True(File.Exists(statePath));
        File.Delete(statePath);
    }

    private sealed class InMemoryFactory(string databaseName) : IDatabaseContextFactory
    {
        public DatabaseContext CreateContext(bool? enableTracking = true)
        {
            var options = new DbContextOptionsBuilder<DatabaseContext>()
                .UseInMemoryDatabase(databaseName).Options;
            var context = new TestDatabaseContext(options);
            context.ChangeTracker.QueryTrackingBehavior = enableTracking == false
                ? QueryTrackingBehavior.NoTracking
                : QueryTrackingBehavior.TrackAll;
            return context;
        }
    }

    private sealed class TestDatabaseContext(DbContextOptions<DatabaseContext> options) : DatabaseContext(options);
}
