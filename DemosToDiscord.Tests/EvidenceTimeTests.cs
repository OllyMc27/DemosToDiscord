using Xunit;

namespace DemosToDiscord.Tests;

public sealed class EvidenceTimeTests
{
    [Theory]
    [InlineData(2026, 8, 25, 12, 0, 0, "13:00:00 25/08/2026")]
    [InlineData(2026, 1, 25, 12, 0, 0, "12:00:00 25/01/2026")]
    public void Uk_format_observes_daylight_saving(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        string expected)
    {
        var utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

        Assert.Equal(expected, EvidenceTime.FormatUk(utc));
    }

    [Fact]
    public void Match_offset_is_readable()
    {
        var started = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("6m 12s into match", EvidenceTime.MatchOffset(started.AddMinutes(6).AddSeconds(12), started));
    }
}
