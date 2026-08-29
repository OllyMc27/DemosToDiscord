using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharedLibraryCore.Configuration;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class WebfrontLinkTests
{
    [Fact]
    public void Discord_review_link_uses_public_webfront_url_and_case_id()
    {
        var result = DiscordWebhookClient.BuildReviewUrl("https://admin.example.test/", "abc123");

        Assert.Equal(
            "https://admin.example.test/Interaction/Render/Webfront::Nav::Admin::DemosToDiscord?case=abc123",
            result);
    }

    [Fact]
    public void Discord_review_link_is_omitted_without_public_webfront_url()
    {
        Assert.Null(DiscordWebhookClient.BuildReviewUrl(null, "abc123"));
    }

    [Fact]
    public void Discord_embed_is_compact_linked_and_mentions_are_suppressed()
    {
        var appConfig = new ApplicationConfiguration();
        appConfig.Webfront.ManualUrl = "https://admin.example.test";
        using var client = new DiscordWebhookClient(
            appConfig,
            new DemosToDiscordConfig(),
            NullLogger<DiscordWebhookClient>.Instance);
        var evidenceCase = new EvidenceCase
        {
            Id = "abc123",
            CreatedAtUtc = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
            TargetClientId = 27,
            TargetNetworkId = 123456,
            TargetName = "@suspect",
            Game = "T6",
            Map = "mp_nuketown_2020",
            Mode = "dm",
            ServerName = "Test Server",
            ServerId = "127.0.0.1:28960",
            Reports =
            [
                new ReportEvidence { ReporterName = "@reporter", Reason = "possible *aimbot*" }
            ]
        };

        var json = JsonSerializer.Serialize(client.BuildEmbed(evidenceCase, null, null));

        Assert.Contains("Review evidence case", json);
        Assert.Contains("https://admin.example.test/Interaction/Render/", json);
        Assert.Contains("\"url\":\"https://admin.example.test/Interaction/Render/", json);
        Assert.Contains("Admin links", json);
        Assert.DoesNotContain("@suspect", json);
        Assert.DoesNotContain("@reporter", json);
        Assert.Contains("@\\u200Bsuspect", json);
        Assert.Contains("13:00:00 25/08/2026 UK", json);
    }

    [Theory]
    [InlineData(@"D:\Plutonium\storage\t6\demos\dem_mp_nuketown_8_25_2026_12_00.demo", "dem_mp_nuketown_8_25_2026_12_00.demo")]
    [InlineData(@"D:\Plutonium\storage\t6\demos\dem_mp_nuketown_8_25_2026_12_00.json", "dem_mp_nuketown_8_25_2026_12_00.json")]
    public void Discord_attachments_keep_the_original_theatre_filename(string path, string expected)
    {
        Assert.Equal(expected, DiscordWebhookClient.AttachmentFileName(path));
    }
}

