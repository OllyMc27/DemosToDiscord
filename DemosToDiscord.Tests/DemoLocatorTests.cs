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

    [Fact]
    public void FindBest_accepts_t6_mode_mismatch_when_map_time_and_target_match()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-t6-mode-");
        try
        {
            var demo = Path.Combine(directory.FullName, "hq_mp_hijacked_08_29_2026_22_56.demo");
            File.WriteAllBytes(demo, [1]);
            File.WriteAllText(Path.ChangeExtension(demo, ".json"), "{\"players\":[123456789]}");
            var reportTime = new DateTime(2026, 8, 29, 23, 0, 40, DateTimeKind.Local).ToUniversalTime();
            var evidenceCase = Case(reportTime);
            evidenceCase.Map = "mp_hijacked";
            evidenceCase.Mode = "dm";

            var locator = new DemoLocator(new DemosToDiscordConfig(), NullLogger<DemoLocator>.Instance);
            var result = locator.FindBest(evidenceCase, directory.FullName);

            Assert.NotNull(result);
            Assert.Equal(demo, result.DemoPath);
            Assert.Equal("hq", result.Mode);
            Assert.True(result.TargetConfirmed);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void FindBest_keeps_non_t6_mode_matching_strict()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-t5-mode-");
        try
        {
            var demo = Path.Combine(directory.FullName, "hq_mp_hijacked_08_29_2026_22_56.demo");
            File.WriteAllBytes(demo, [1]);
            var reportTime = new DateTime(2026, 8, 29, 23, 0, 40, DateTimeKind.Local).ToUniversalTime();
            var evidenceCase = Case(reportTime);
            evidenceCase.Game = "T5";
            evidenceCase.Map = "mp_hijacked";
            evidenceCase.Mode = "dm";

            var locator = new DemoLocator(new DemosToDiscordConfig(), NullLogger<DemoLocator>.Instance);

            Assert.Null(locator.FindBest(evidenceCase, directory.FullName));
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

