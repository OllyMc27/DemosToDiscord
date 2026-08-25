using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class DemoLocatorTests
{
    [Fact]
    public void ParseFilename_extracts_mode_map_and_utc_start()
    {
        var result = DemoLocator.ParseFilename("dm_mp_nuketown_08_25_2026_14_30.demo");

        Assert.NotNull(result);
        Assert.Equal("dm", result.Mode);
        Assert.Equal("mp_nuketown", result.Map);
        Assert.Equal(
            new DateTime(2026, 8, 25, 14, 30, 0, DateTimeKind.Local).ToUniversalTime(),
            result.StartedAtUtc);
    }

    [Fact]
    public void FindBest_prefers_t6_sidecar_that_contains_target_guid()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-locator-");
        try
        {
            var first = Path.Combine(directory.FullName, "dm_mp_nuketown_08_25_2026_14_20.demo");
            var second = Path.Combine(directory.FullName, "dm_mp_nuketown_08_25_2026_14_25.demo");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [2]);
            File.WriteAllText(Path.ChangeExtension(first, ".json"), "{\"players\":[123456789]}");

            var locator = new DemoLocator(new DemosToDiscordConfig(), NullLogger<DemoLocator>.Instance);
            var evidenceCase = Case(new DateTime(2026, 8, 25, 14, 30, 0, DateTimeKind.Utc));
            var result = locator.FindBest(evidenceCase, directory.FullName);

            Assert.NotNull(result);
            Assert.Equal(first, result.DemoPath);
            Assert.True(result.TargetConfirmed);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static EvidenceCase Case(DateTime when) => new()
    {
        CreatedAtUtc = when,
        Game = "T6",
        Map = "mp_nuketown",
        Mode = "dm",
        TargetNetworkId = 123456789
    };
}

