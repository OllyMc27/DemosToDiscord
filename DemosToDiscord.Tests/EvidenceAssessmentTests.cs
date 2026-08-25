using Xunit;

namespace DemosToDiscord.Tests;

public sealed class EvidenceAssessmentTests
{
    private readonly DemosToDiscordConfig _config = new();

    [Theory]
    [InlineData("T4")]
    [InlineData("IW5")]
    public void Games_without_recorded_demos_are_metadata_only(string game)
    {
        var result = EvidenceAssessment.DemoCapability(Case(game, "mp_crash", "dm"), _config, null);

        Assert.False(result.Supported);
        Assert.Equal(DemoSupportStatus.UnsupportedGame, result.Status);
    }

    [Theory]
    [InlineData("zombie_theater", "zclassic")]
    [InlineData("zombie_coast", "tdm")]
    [InlineData("mp_nuked", "zstandard")]
    public void T5_zombies_are_metadata_only(string map, string mode)
    {
        var result = EvidenceAssessment.DemoCapability(Case("T5", map, mode), _config, null);

        Assert.False(result.Supported);
        Assert.Equal(DemoSupportStatus.UnsupportedMode, result.Status);
    }

    [Theory]
    [InlineData("T5", "mp_nuked", "dm")]
    [InlineData("T6", "mp_nuketown_2020", "tdm")]
    public void Recorded_multiplayer_games_support_demos(string game, string map, string mode)
    {
        Assert.True(EvidenceAssessment.DemoCapability(Case(game, map, mode), _config, null).Supported);
    }

    [Fact]
    public void Server_override_can_force_capability()
    {
        Assert.True(EvidenceAssessment.DemoCapability(
            Case("IW5", "mp_dome", "dm"), _config, new DemosToDiscordServerOverride { SupportsDemos = true }).Supported);
        Assert.Equal(DemoSupportStatus.DisabledByServer, EvidenceAssessment.DemoCapability(
            Case("T6", "mp_dockside", "dm"), _config, new DemosToDiscordServerOverride { SupportsDemos = false }).Status);
    }

    [Fact]
    public void Report_and_detection_are_corroborated()
    {
        var item = Case("T6", "mp_dockside", "dm");
        item.Reports.Add(new ReportEvidence());
        item.AntiCheat = new AntiCheatEvidence();

        Assert.Equal("Corroborated", EvidenceAssessment.Confidence(item).Label);
    }

    private static EvidenceCase Case(string game, string map, string mode) => new()
    {
        Game = game,
        Map = map,
        Mode = mode
    };
}

