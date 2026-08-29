using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed class EvidenceCaseStore
{
    private readonly DemosToDiscordConfig _config;
    private readonly ILogger<EvidenceCaseStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly List<EvidenceCase> _cases = [];
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private bool _loaded;

    public EvidenceCaseStore(DemosToDiscordConfig config, ILogger<EvidenceCaseStore> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_loaded)
                return;

            var path = ResolvePath();
            if (File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                var loaded = await JsonSerializer.DeserializeAsync<List<EvidenceCase>>(stream, _jsonOptions, token);
                if (loaded is not null)
                    _cases.AddRange(loaded);
            }

            PruneUnsafe();
            _loaded = true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[DemosToDiscord] Could not load evidence case metadata");
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(EvidenceCase Case, bool Created, bool NeedsUpload)> AddOrMergeAsync(
        PenaltyCapture capture,
        CancellationToken token)
    {
        await InitializeAsync(token);
        await _gate.WaitAsync(token);
        try
        {
            var cutoff = capture.WhenUtc.AddMinutes(-Math.Max(1, _config.DeduplicationWindowMinutes));
            var evidenceCase = _cases
                .Where(item => item.TargetClientId == capture.TargetClientId &&
                               item.ServerId.Equals(capture.ServerId, StringComparison.OrdinalIgnoreCase) &&
                               item.Map.Equals(capture.Map, StringComparison.OrdinalIgnoreCase) &&
                               item.Mode.Equals(capture.Mode, StringComparison.OrdinalIgnoreCase) &&
                               item.CreatedAtUtc >= cutoff &&
                               item.CreatedAtUtc <= capture.WhenUtc.AddMinutes(5))
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefault();

            var created = evidenceCase is null;
            if (evidenceCase is null)
            {
                evidenceCase = new EvidenceCase
                {
                    CreatedAtUtc = capture.WhenUtc,
                    UpdatedAtUtc = capture.WhenUtc,
                    ServerId = capture.ServerId,
                    ServerName = capture.ServerName,
                    LegacyServerId = capture.LegacyServerId,
                    Game = capture.Game,
                    Map = capture.Map,
                    Mode = capture.Mode,
                    TargetClientId = capture.TargetClientId,
                    TargetNetworkId = capture.TargetNetworkId,
                    TargetName = capture.TargetName
                };
                evidenceCase.History.Add(new EvidenceHistoryEntry
                {
                    WhenUtc = capture.WhenUtc,
                    Action = EvidenceHistoryAction.Created,
                    Summary = $"Case created from {TriggerLabel(capture.Trigger)}."
                });
                _cases.Add(evidenceCase);
            }

            evidenceCase.UpdatedAtUtc = DateTime.UtcNow;
            evidenceCase.ServerName = capture.ServerName;
            evidenceCase.TargetName = capture.TargetName;

            switch (capture.Trigger)
            {
                case EvidenceTriggerType.Report:
                    evidenceCase.Reports.Add(new ReportEvidence
                    {
                        PenaltyId = capture.PenaltyId,
                        WhenUtc = capture.WhenUtc,
                        ReporterClientId = capture.ReporterClientId,
                        ReporterName = capture.ReporterName,
                        Reason = _config.StoreReportReasons ? capture.Reason : "Reason storage disabled"
                    });
                    break;
                case EvidenceTriggerType.AutomatedBan:
                    evidenceCase.AntiCheat = new AntiCheatEvidence
                    {
                        WhenUtc = capture.WhenUtc,
                        Detection = capture.Detection,
                        PenaltyId = capture.PenaltyId
                    };
                    break;
                case EvidenceTriggerType.ManualBan:
                    evidenceCase.ManualBanObserved = true;
                    break;
            }

            if (!created)
            {
                evidenceCase.History.Add(new EvidenceHistoryEntry
                {
                    WhenUtc = capture.WhenUtc,
                    Action = EvidenceHistoryAction.EvidenceAdded,
                    Summary = $"{TriggerLabel(capture.Trigger)} added to the case."
                });
            }

            PruneUnsafe();
            await SaveUnsafeAsync(token);
            var needsUpload = created || evidenceCase.Status is EvidenceCaseStatus.NoDemo or EvidenceCaseStatus.Failed;
            return (Clone(evidenceCase), created, needsUpload);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(string id, Action<EvidenceCase> update, CancellationToken token = default)
    {
        await InitializeAsync(token);
        await _gate.WaitAsync(token);
        try
        {
            var evidenceCase = _cases.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (evidenceCase is null)
                return;

            update(evidenceCase);
            evidenceCase.UpdatedAtUtc = DateTime.UtcNow;
            await SaveUnsafeAsync(token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EvidenceCase?> LinkManualBanAsync(
        int targetClientId,
        DateTime whenUtc,
        int? penaltyId,
        CancellationToken token)
    {
        await InitializeAsync(token);
        await _gate.WaitAsync(token);
        try
        {
            var cutoff = whenUtc.AddMinutes(-Math.Max(1, _config.DeduplicationWindowMinutes));
            var evidenceCase = _cases
                .Where(item => item.TargetClientId == targetClientId &&
                               item.CreatedAtUtc >= cutoff &&
                               item.CreatedAtUtc <= whenUtc.AddMinutes(5))
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefault();
            if (evidenceCase is null)
                return null;

            evidenceCase.ManualBanObserved = true;
            evidenceCase.UpdatedAtUtc = DateTime.UtcNow;
            evidenceCase.History.Add(new EvidenceHistoryEntry
            {
                WhenUtc = whenUtc,
                Action = EvidenceHistoryAction.EvidenceAdded,
                Summary = penaltyId is > 0
                    ? $"Manual ban penalty #{penaltyId} linked to this case."
                    : "Manual ban linked to this case."
            });
            await SaveUnsafeAsync(token);
            return Clone(evidenceCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    public EvidenceCase? Get(string id)
    {
        _gate.Wait();
        try
        {
            var result = _cases.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return result is null ? null : Clone(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public EvidenceStoreSnapshot Snapshot()
    {
        _gate.Wait();
        try
        {
            var cases = _cases.OrderByDescending(item => item.UpdatedAtUtc).Select(Clone).ToList();
            return new EvidenceStoreSnapshot(
                _startedAtUtc,
                cases.Count(item => item.Status is EvidenceCaseStatus.Queued or EvidenceCaseStatus.Searching or EvidenceCaseStatus.WaitingForDemo or EvidenceCaseStatus.Uploading),
                cases.Count(item => item.Status == EvidenceCaseStatus.Uploaded),
                cases.Count(item => item.Status == EvidenceCaseStatus.NoDemo),
                cases.Count(item => item.Status == EvidenceCaseStatus.Failed),
                cases.Count(item => item.Status == EvidenceCaseStatus.DemoUnsupported),
                cases.Sum(item => item.Reports.Count),
                cases.Count(item => item.AntiCheat is not null),
                cases);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PruneUnsafe()
    {
        foreach (var item in _cases)
        {
            item.Reports ??= [];
            item.History ??= [];
        }

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _config.CaseRetentionDays));
        _cases.RemoveAll(item => item.UpdatedAtUtc < cutoff);
        var overflow = _cases.Count - Math.Max(1, _config.MaxStoredCases);
        if (overflow > 0)
        {
            var remove = _cases.OrderBy(item => item.UpdatedAtUtc).Take(overflow).Select(item => item.Id).ToHashSet();
            _cases.RemoveAll(item => remove.Contains(item.Id));
        }
    }

    private async Task SaveUnsafeAsync(CancellationToken token)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, _cases, _jsonOptions, token);
        File.Move(temporaryPath, path, true);
    }

    private string ResolvePath() => Path.IsPathRooted(_config.StateFilePath)
        ? _config.StateFilePath
        : Path.Combine(AppContext.BaseDirectory, _config.StateFilePath);

    private EvidenceCase Clone(EvidenceCase value) =>
        JsonSerializer.Deserialize<EvidenceCase>(JsonSerializer.Serialize(value, _jsonOptions), _jsonOptions)!;

    private static string TriggerLabel(EvidenceTriggerType trigger) => trigger switch
    {
        EvidenceTriggerType.Report => "a player report",
        EvidenceTriggerType.AutomatedBan => "an automated anti-cheat ban",
        EvidenceTriggerType.ManualBan => "a manual ban",
        _ => "evidence"
    };
}

