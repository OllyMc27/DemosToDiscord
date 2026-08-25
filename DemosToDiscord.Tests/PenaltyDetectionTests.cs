using Data.Models;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class PenaltyDetectionTests
{
    [Fact]
    public void System_ban_with_automated_offense_is_anti_cheat_ban()
    {
        var penalty = new EFPenalty { Type = EFPenalty.PenaltyType.Ban, PunisherId = 1 };

        Assert.True(DemoUploadService.IsAutomatedBan(penalty, "SnapThreshold="));
        Assert.False(DemoUploadService.IsAutomatedBan(penalty, string.Empty));
    }
}

