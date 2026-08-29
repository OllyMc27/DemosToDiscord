using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DemosToDiscord.Tests;

public sealed class EvidenceCaseStoreTests
{
    [Fact]
    public async Task Report_and_automated_ban_merge_into_one_case()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-store-");
        try
        {
            var config = new DemosToDiscordConfig
            {
                StateFilePath = Path.Combine(directory.FullName, "cases.json"),
                DeduplicationWindowMinutes = 120
            };
            var store = new EvidenceCaseStore(config, NullLogger<EvidenceCaseStore>.Instance);
            var when = new DateTime(2026, 8, 25, 14, 30, 0, DateTimeKind.Utc);

            var report = await store.AddOrMergeAsync(Capture(EvidenceTriggerType.Report, when), CancellationToken.None);
            var ban = await store.AddOrMergeAsync(Capture(EvidenceTriggerType.AutomatedBan, when.AddMinutes(5)), CancellationToken.None);

            Assert.True(report.Created);
            Assert.False(ban.Created);
            var snapshot = store.Snapshot();
            var evidenceCase = Assert.Single(snapshot.Cases);
            Assert.Single(evidenceCase.Reports);
            Assert.NotNull(evidenceCase.AntiCheat);
            Assert.Equal(1, snapshot.Reports);
            Assert.Equal(1, snapshot.AutomatedBans);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Review_and_penalty_metadata_survive_reload()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-review-");
        try
        {
            var statePath = Path.Combine(directory.FullName, "cases.json");
            var config = new DemosToDiscordConfig
            {
                StateFilePath = statePath,
                DeduplicationWindowMinutes = 120
            };
            var when = DateTime.UtcNow.AddMinutes(-5);
            var store = new EvidenceCaseStore(config, NullLogger<EvidenceCaseStore>.Instance);
            var capture = Capture(EvidenceTriggerType.Report, when) with { PenaltyId = 42 };
            var token = TestContext.Current.CancellationToken;
            var result = await store.AddOrMergeAsync(capture, token);

            await store.UpdateAsync(result.Case.Id, item =>
            {
                item.ReviewDecision = EvidenceReviewDecision.NotCheatingNoAction;
                item.ReviewedAtUtc = when.AddMinutes(2);
                item.ReviewedByClientId = 7;
                item.ReviewedByName = "Moderator";
                item.ReviewNotes = "Demo reviewed.";
                item.ReportsClearedAtUtc = when.AddMinutes(3);
                item.ReportsClearedCount = 1;
                item.DemoSupport = DemoSupportStatus.Supported;
                item.AssignedToClientId = 7;
                item.AssignedToName = "Moderator";
                item.AssignedAtUtc = when.AddMinutes(1);
                item.History.Add(new EvidenceHistoryEntry
                {
                    WhenUtc = when.AddMinutes(2),
                    Action = EvidenceHistoryAction.ReviewChanged,
                    AdminClientId = 7,
                    AdminName = "Moderator",
                    Summary = "Reviewed."
                });
            }, token);

            var reloaded = new EvidenceCaseStore(config, NullLogger<EvidenceCaseStore>.Instance);
            await reloaded.InitializeAsync(token);
            var evidenceCase = Assert.Single(reloaded.Snapshot().Cases);

            Assert.Equal(42, Assert.Single(evidenceCase.Reports).PenaltyId);
            Assert.Equal(EvidenceReviewDecision.NotCheatingNoAction, evidenceCase.ReviewDecision);
            Assert.Equal(7, evidenceCase.ReviewedByClientId);
            Assert.Equal("Moderator", evidenceCase.ReviewedByName);
            Assert.Equal("Demo reviewed.", evidenceCase.ReviewNotes);
            Assert.Equal(1, evidenceCase.ReportsClearedCount);
            Assert.Equal(DemoSupportStatus.Supported, evidenceCase.DemoSupport);
            Assert.Equal(7, evidenceCase.AssignedToClientId);
            Assert.Equal("Moderator", evidenceCase.AssignedToName);
            Assert.Equal(2, evidenceCase.History.Count);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Manual_ban_links_to_recent_case_without_creating_a_case_on_another_server()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-manual-ban-");
        try
        {
            var config = new DemosToDiscordConfig
            {
                StateFilePath = Path.Combine(directory.FullName, "cases.json"),
                DeduplicationWindowMinutes = 120
            };
            var store = new EvidenceCaseStore(config, NullLogger<EvidenceCaseStore>.Instance);
            var when = DateTime.UtcNow.AddMinutes(-10);
            var report = await store.AddOrMergeAsync(Capture(EvidenceTriggerType.Report, when), CancellationToken.None);

            var linked = await store.LinkManualBanAsync(27, when.AddMinutes(8), 1234, CancellationToken.None);

            Assert.NotNull(linked);
            Assert.Equal(report.Case.Id, linked.Id);
            Assert.True(linked.ManualBanObserved);
            Assert.Single(store.Snapshot().Cases);
            Assert.Contains(linked.History, item => item.Summary.Contains("#1234"));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Manual_ban_without_recent_evidence_is_ignored()
    {
        var directory = Directory.CreateTempSubdirectory("dtd-manual-ban-none-");
        try
        {
            var config = new DemosToDiscordConfig
            {
                StateFilePath = Path.Combine(directory.FullName, "cases.json"),
                DeduplicationWindowMinutes = 120
            };
            var store = new EvidenceCaseStore(config, NullLogger<EvidenceCaseStore>.Instance);

            var linked = await store.LinkManualBanAsync(27, DateTime.UtcNow, 1234, CancellationToken.None);

            Assert.Null(linked);
            Assert.Empty(store.Snapshot().Cases);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static PenaltyCapture Capture(EvidenceTriggerType trigger, DateTime when) => new(
        trigger, when, "127.0.0.1:28960", "Test Server", 1, "T6", "mp_nuketown", "dm",
        27, 123456789, "Suspect", 9, "Reporter", "aimbot", "snap detection");
}

