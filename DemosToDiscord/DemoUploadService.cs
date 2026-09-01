using System.Threading.Channels;
using Data.Models;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Events.Management;

namespace DemosToDiscord;

public sealed class DemoUploadService : IDisposable
{
    private readonly DemosToDiscordConfig _config;
    private readonly EvidenceCaseStore _store;
    private readonly DemoLocator _locator;
    private readonly DiscordWebhookClient _discord;
    private readonly PlayerTimelineService _timeline;
    private readonly ILogger<DemoUploadService> _logger;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _workers = [];
    private bool _started;

    public DemoUploadService(
        DemosToDiscordConfig config,
        EvidenceCaseStore store,
        DemoLocator locator,
        DiscordWebhookClient discord,
        PlayerTimelineService timeline,
        ILogger<DemoUploadService> logger)
    {
        _config = config;
        _store = store;
        _locator = locator;
        _discord = discord;
        _timeline = timeline;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken token)
    {
        if (_started)
            return;
        _started = true;
        await _store.InitializeAsync(token);
        for (var index = 0; index < Math.Max(1, _config.MaxConcurrentUploads); index++)
            _workers.Add(Task.Run(() => WorkerAsync(_shutdown.Token), _shutdown.Token));

        var interruptedCases = _store.Snapshot().Cases
            .Where(item => item.Status is EvidenceCaseStatus.Queued or EvidenceCaseStatus.Searching or
                EvidenceCaseStatus.WaitingForDemo or EvidenceCaseStatus.Uploading)
            .ToList();
        foreach (var evidenceCase in interruptedCases)
        {
            await _store.UpdateAsync(evidenceCase.Id, item => item.Status = EvidenceCaseStatus.Queued, token);
            await _queue.Writer.WriteAsync(evidenceCase.Id, token);
        }

        if (interruptedCases.Count > 0)
            _logger.LogInformation("[DemosToDiscord] Resumed {Count} interrupted evidence case(s)", interruptedCases.Count);
    }

    public async Task HandlePenaltyAsync(ClientPenaltyEvent evt, CancellationToken token)
    {
        if (!_config.Enabled || evt.Client?.CurrentServer is null)
            return;

        var server = evt.Client.CurrentServer;
        var game = server.GameCode.ToString().ToUpperInvariant();
        var serverOverride = ResolveOverride(server.Id, server.LegacyDatabaseId);
        if (serverOverride?.Enabled == false)
            return;

        var automatedOffense = evt.Penalty.AutomatedOffense;
        if (string.IsNullOrWhiteSpace(automatedOffense))
        {
            automatedOffense = evt.Penalty.Punisher?.AdministeredPenalties?
                .Select(item => item.AutomatedOffense)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        }

        var trigger = ResolveTrigger(evt.Penalty, automatedOffense, game, serverOverride);
        if (trigger is null)
            return;

        var whenUtc = evt.Penalty.When == default
            ? DateTime.UtcNow
            : EvidenceTime.AsUtc(evt.Penalty.When);
        if (trigger == EvidenceTriggerType.ManualBan)
        {
            var linkedCase = await _store.LinkManualBanAsync(
                evt.Client.ClientId,
                whenUtc,
                evt.Penalty.PenaltyId > 0 ? evt.Penalty.PenaltyId : null,
                token);
            if (linkedCase is null)
            {
                _logger.LogInformation(
                    "[DemosToDiscord] Ignored manual ban for client {ClientId}; no recent evidence case was found",
                    evt.Client.ClientId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(linkedCase.DiscordMessageId))
                await UpdateCaseDiscordAsync(linkedCase.Id, token);
            _logger.LogInformation(
                "[DemosToDiscord] Manual ban linked to existing evidence case {CaseId}; no new case was created",
                linkedCase.Id);
            return;
        }

        var capture = new PenaltyCapture(
            trigger.Value,
            whenUtc,
            server.Id,
            server.ServerName.StripColors(),
            server.LegacyDatabaseId,
            game,
            server.Map?.Name ?? "Unknown",
            server.Gametype ?? "Unknown",
            evt.Client.ClientId,
            evt.Client.NetworkId,
            evt.Client.CurrentAlias?.Name.StripColors() ?? "Unknown",
            evt.Penalty.Punisher?.ClientId ?? 0,
            evt.Penalty.Punisher?.CurrentAlias?.Name.StripColors() ??
            (trigger == EvidenceTriggerType.AutomatedBan ? "IW4MAdmin anti-cheat" : "Unknown"),
            evt.Penalty.Offense ?? string.Empty,
            automatedOffense ?? evt.Penalty.Offense ?? string.Empty,
            evt.Penalty.PenaltyId > 0 ? evt.Penalty.PenaltyId : null);

        var result = await _store.AddOrMergeAsync(capture, token);
        var capability = EvidenceAssessment.DemoCapability(result.Case, _config, serverOverride);
        await _store.UpdateAsync(result.Case.Id, item =>
        {
            item.DemoSupport = capability.Status;
            item.DemoSupportReason = capability.Reason;
        }, token);
        if (evt.Penalty.PenaltyId > 0 && result.Case.AntiCheat is not null)
            await _store.UpdateAsync(result.Case.Id, item => item.AntiCheat!.PenaltyId = evt.Penalty.PenaltyId, token);

        if (result.NeedsUpload)
        {
            await _store.UpdateAsync(result.Case.Id, item =>
            {
                item.Status = EvidenceCaseStatus.Queued;
                item.LastError = null;
            }, token);
            await _queue.Writer.WriteAsync(result.Case.Id, token);
        }
        else if (!string.IsNullOrWhiteSpace(result.Case.DiscordMessageId))
        {
            await UpdateCaseDiscordAsync(result.Case.Id, token);
        }

        _logger.LogInformation(
            "[DemosToDiscord] {Trigger} added to evidence case {CaseId} for {Target} on {Game} {Server}",
            trigger, result.Case.Id, result.Case.TargetName, game, result.Case.ServerName);
    }

    public async Task<string> CaptureServerPulseAsync(ServerPulseCaseRequest request, CancellationToken token)
    {
        if (!_config.Enabled || !_config.AcceptServerPulseCases)
            throw new InvalidOperationException("ServerPulse case intake is disabled in DemosToDiscord configuration.");
        if (request.TargetClientId <= 0 || request.TargetNetworkId <= 0 || string.IsNullOrWhiteSpace(request.TargetName))
            throw new ArgumentException("ServerPulse did not provide a valid resolved target.");
        var serverOverride = ResolveOverride(request.ServerId, request.LegacyServerId);
        if (serverOverride?.Enabled == false || serverOverride?.AcceptServerPulseCases == false)
            throw new InvalidOperationException("ServerPulse case intake is disabled for this server.");

        var result = await _store.AddOrMergeCommunitySignalAsync(request, token);
        var capability = EvidenceAssessment.DemoCapability(result.Case, _config, serverOverride);
        await _store.UpdateAsync(result.Case.Id, item =>
        {
            item.DemoSupport = capability.Status;
            item.DemoSupportReason = capability.Reason;
            item.DiscordEligible = true;
        }, token);
        var current = _store.Get(result.Case.Id)!;
        if (result.NeedsUpload)
            await QueueCaseAsync(current.Id, token);
        else if (!string.IsNullOrWhiteSpace(current.DiscordMessageId))
            await UpdateCaseDiscordAsync(current.Id, token);

        _logger.LogWarning(
            "[DemosToDiscord] ServerPulse event {SourceEventId} created or updated review case {CaseId} for client {ClientId}",
            request.SourceEventId, current.Id, request.TargetClientId);
        return current.Id;
    }

    public EvidenceStoreSnapshot GetSnapshot() => _store.Snapshot();
    public EvidenceCase? GetCase(string id) => _store.Get(id);

    public DemoCandidate? FindCandidate(string caseId)
    {
        var evidenceCase = _store.Get(caseId);
        if (evidenceCase is null || !ResolveDemoCapability(evidenceCase).Supported)
            return null;
        return _locator.FindBest(evidenceCase, ResolveDemoFolder(evidenceCase));
    }

    public async Task<bool> RetryAsync(string caseId, CancellationToken token)
    {
        if (_store.Get(caseId) is null)
            return false;
        await _store.UpdateAsync(caseId, item =>
        {
            item.Status = EvidenceCaseStatus.Queued;
            item.LastError = null;
        }, token);
        await _queue.Writer.WriteAsync(caseId, token);
        return true;
    }

    public async Task QueueCaseAsync(string caseId, CancellationToken token)
    {
        await _store.UpdateAsync(caseId, item =>
        {
            item.Status = EvidenceCaseStatus.Queued;
            item.LastError = null;
        }, token);
        await _queue.Writer.WriteAsync(caseId, token);
    }

    public Task TestWebhookAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_config.Webhook))
            throw new InvalidOperationException("Webhook is empty in DemosToDiscord.json.");
        return _discord.TestAsync(_config.Webhook, token);
    }

    public string StatusSummary()
    {
        var snapshot = _store.Snapshot();
        return $"DemosToDiscord v2: enabled={_config.Enabled}, queued={snapshot.Queued}, uploaded={snapshot.Uploaded}, no-demo={snapshot.NoDemo}, unsupported={snapshot.Unsupported}, failed={snapshot.Failed}, reports={snapshot.Reports}, auto-bans={snapshot.AutomatedBans}.";
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        try
        {
            await foreach (var caseId in _queue.Reader.ReadAllAsync(token))
                await ProcessAsync(caseId, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(string caseId, CancellationToken token)
    {
        var evidenceCase = _store.Get(caseId);
        if (evidenceCase is null)
            return;

        try
        {
            var capability = ResolveDemoCapability(evidenceCase);
            await _store.UpdateAsync(caseId, item =>
            {
                item.DemoSupport = capability.Status;
                item.DemoSupportReason = capability.Reason;
                item.Attempts++;
                if (!capability.Supported)
                    item.Status = EvidenceCaseStatus.DemoUnsupported;
            }, token);

            if (!capability.Supported)
            {
                evidenceCase = await EnrichCaseAsync(caseId, null, token);
                if (!evidenceCase.DiscordEligible)
                    return;
                if (!ShouldSendMetadataOnly(evidenceCase))
                {
                    await _store.UpdateAsync(caseId, item =>
                    {
                        item.Status = EvidenceCaseStatus.DemoUnsupported;
                        item.LastError = null;
                    }, token);
                    return;
                }

                var metadataWebhook = ResolveWebhook(evidenceCase);
                if (string.IsNullOrWhiteSpace(metadataWebhook))
                {
                    await _store.UpdateAsync(caseId, item =>
                    {
                        item.Status = EvidenceCaseStatus.DemoUnsupported;
                        item.LastError = "No Discord webhook is configured; the metadata-only case remains available in the webfront.";
                    }, token);
                    return;
                }

                var unsupportedReceipt = await _discord.SendCaseAsync(
                    evidenceCase,
                    metadataWebhook,
                    null,
                    null,
                    ResolveDeliveryOptions(evidenceCase, false),
                    token);
                await CompleteAsync(caseId, EvidenceCaseStatus.DemoUnsupported, unsupportedReceipt, null, token);
                return;
            }

            var webhook = ResolveWebhook(evidenceCase);
            if (evidenceCase.DiscordEligible && string.IsNullOrWhiteSpace(webhook))
                throw new InvalidOperationException("No Discord webhook is configured for this server.");

            var folder = ResolveDemoFolder(evidenceCase);
            await _store.UpdateAsync(caseId, item =>
            {
                item.Status = EvidenceCaseStatus.Searching;
            }, token);

            DemoCandidate? candidate = null;
            if (Directory.Exists(folder))
                candidate = await _locator.WaitForCandidateAsync(evidenceCase, folder, token);

            if (candidate is null)
            {
                evidenceCase = await EnrichCaseAsync(caseId, null, token);
                if (!evidenceCase.DiscordEligible)
                {
                    await _store.UpdateAsync(caseId, item => item.Status = EvidenceCaseStatus.NoDemo, token);
                    return;
                }
                var receipt = await _discord.SendCaseAsync(
                    evidenceCase,
                    webhook,
                    null,
                    null,
                    ResolveDeliveryOptions(evidenceCase, false),
                    token);
                await CompleteAsync(caseId, EvidenceCaseStatus.NoDemo, receipt, null, token);
                return;
            }

            await _store.UpdateAsync(caseId, item => item.Status = EvidenceCaseStatus.WaitingForDemo, token);
            if (!await _locator.WaitUntilReadyAsync(candidate.DemoPath, token))
                throw new IOException($"Demo file did not become stable and readable: {candidate.DemoPath}");

            if (_config.PostMatchDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(_config.PostMatchDelaySeconds), token);

            await _store.UpdateAsync(caseId, item => item.Status = EvidenceCaseStatus.Uploading, token);
            evidenceCase = await EnrichCaseAsync(caseId, candidate, token);
            if (!evidenceCase.DiscordEligible)
            {
                await _store.UpdateAsync(caseId, item => item.Status = EvidenceCaseStatus.DemoReady, token);
                return;
            }
            var receiptWithDemo = await _discord.SendCaseAsync(
                evidenceCase,
                webhook,
                candidate.DemoPath,
                candidate.JsonPath,
                ResolveDeliveryOptions(evidenceCase, true),
                token);
            await CompleteAsync(caseId, EvidenceCaseStatus.Uploaded, receiptWithDemo, candidate, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[DemosToDiscord] Evidence case {CaseId} failed", caseId);
            await _store.UpdateAsync(caseId, item =>
            {
                item.Status = EvidenceCaseStatus.Failed;
                item.LastError = exception.Message;
            }, CancellationToken.None);
        }
    }

    private Task CompleteAsync(
        string caseId,
        EvidenceCaseStatus status,
        DiscordMessageReceipt receipt,
        DemoCandidate? candidate,
        CancellationToken token) => _store.UpdateAsync(caseId, item =>
    {
        item.Status = status;
        item.DiscordMessageId = receipt.MessageId;
        item.DiscordChannelId = receipt.ChannelId;
        item.DiscordGuildId = receipt.GuildId;
        item.UploadedAtUtc = DateTime.UtcNow;
        item.LastError = null;
        item.DiscordLastSyncedAtUtc = DateTime.UtcNow;
        item.DiscordSyncError = null;
        if (candidate is null)
            return;
        var info = new FileInfo(candidate.DemoPath);
        item.DemoFileName = Path.GetFileName(candidate.DemoPath);
        item.DemoFileSize = info.Exists ? info.Length : null;
        item.DemoStartedAtUtc = candidate.StartedAtUtc;
    }, token);

    private EvidenceTriggerType? ResolveTrigger(
        EFPenalty penalty,
        string? automatedOffense,
        string game,
        DemosToDiscordServerOverride? serverOverride)
    {
        if (penalty.Type == EFPenalty.PenaltyType.Report)
            return (serverOverride?.UploadOnReports ?? _config.UploadOnReports) ? EvidenceTriggerType.Report : null;
        if (penalty.Type != EFPenalty.PenaltyType.Ban)
            return null;

        var automated = IsAutomatedBan(penalty, automatedOffense);
        if (automated)
        {
            var gameAllowed = _config.AutomatedBanGames.Any(item => item.Equals(game, StringComparison.OrdinalIgnoreCase));
            return gameAllowed && (serverOverride?.UploadOnAutomatedBans ?? _config.UploadOnAutomatedBans)
                ? EvidenceTriggerType.AutomatedBan
                : null;
        }

        return (serverOverride?.UploadOnManualBans ?? _config.UploadOnManualBans)
            ? EvidenceTriggerType.ManualBan
            : null;
    }

    internal static bool IsAutomatedBan(EFPenalty penalty, string? automatedOffense) =>
        penalty.Type == EFPenalty.PenaltyType.Ban &&
        penalty.PunisherId == 1 &&
        !string.IsNullOrWhiteSpace(automatedOffense);

    private string ResolveDemoFolder(EvidenceCase evidenceCase)
    {
        var serverOverride = ResolveOverride(evidenceCase.ServerId, evidenceCase.LegacyServerId);
        if (!string.IsNullOrWhiteSpace(serverOverride?.DemoPath))
            return serverOverride.DemoPath;
        return evidenceCase.Game.Equals("T6", StringComparison.OrdinalIgnoreCase)
            ? _config.T6DemoPath
            : _config.T5DemoPath;
    }

    internal string ResolveWebhook(EvidenceCase evidenceCase)
    {
        var serverOverride = ResolveOverride(evidenceCase.ServerId, evidenceCase.LegacyServerId);
        if (!string.IsNullOrWhiteSpace(serverOverride?.Webhook))
            return serverOverride.Webhook;
        return _config.GameWebhooks.TryGetValue(evidenceCase.Game, out var gameWebhook) &&
               !string.IsNullOrWhiteSpace(gameWebhook)
            ? gameWebhook
            : _config.Webhook;
    }

    internal string? ResolveFlaggedPlayerRoleId(string serverId, long? legacyServerId)
    {
        var serverOverride = ResolveOverride(serverId, legacyServerId);
        return !string.IsNullOrWhiteSpace(serverOverride?.FlaggedPlayerRoleId)
            ? serverOverride.FlaggedPlayerRoleId
            : !string.IsNullOrWhiteSpace(_config.FlaggedPlayerRoleId)
                ? _config.FlaggedPlayerRoleId
                : null;
    }

    public async Task<bool> UpdateCaseDiscordAsync(string caseId, CancellationToken token)
    {
        var evidenceCase = await EnrichCaseAsync(caseId, null, token);
        if (evidenceCase is null || string.IsNullOrWhiteSpace(evidenceCase.DiscordMessageId))
            return false;

        try
        {
            var webhook = ResolveWebhook(evidenceCase);
            if (string.IsNullOrWhiteSpace(webhook))
                throw new InvalidOperationException("No Discord webhook is configured for this server.");
            await _discord.UpdateCaseAsync(evidenceCase, webhook, token);
            await _store.UpdateAsync(caseId, item =>
            {
                item.DiscordLastSyncedAtUtc = DateTime.UtcNow;
                item.DiscordSyncError = null;
            }, token);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[DemosToDiscord] Could not sync case {CaseId} to Discord", caseId);
            await _store.UpdateAsync(caseId, item => item.DiscordSyncError = exception.Message, CancellationToken.None);
            return false;
        }
    }

    private async Task<EvidenceCase> EnrichCaseAsync(
        string caseId,
        DemoCandidate? candidate,
        CancellationToken token)
    {
        if (candidate is not null)
        {
            await _store.UpdateAsync(caseId, item =>
            {
                var info = new FileInfo(candidate.DemoPath);
                item.DemoFileName = Path.GetFileName(candidate.DemoPath);
                item.DemoFileSize = info.Exists ? info.Length : null;
                item.DemoStartedAtUtc = candidate.StartedAtUtc;
            }, token);
        }

        var evidenceCase = _store.Get(caseId)
                           ?? throw new InvalidOperationException($"Evidence case {caseId} no longer exists.");
        var timeline = await _timeline.GetAsync(evidenceCase, token);
        await _store.UpdateAsync(caseId, item =>
        {
            item.PlayerJoinedAtUtc = timeline.JoinedAtUtc;
            item.PlayerLeftAtUtc = timeline.LeftAtUtc;
        }, token);
        return _store.Get(caseId)!;
    }

    internal DemoCapability ResolveDemoCapability(EvidenceCase evidenceCase)
    {
        var serverOverride = ResolveOverride(evidenceCase.ServerId, evidenceCase.LegacyServerId);
        return EvidenceAssessment.DemoCapability(evidenceCase, _config, serverOverride);
    }

    private bool ShouldSendMetadataOnly(EvidenceCase evidenceCase)
    {
        var serverOverride = ResolveOverride(evidenceCase.ServerId, evidenceCase.LegacyServerId);
        return serverOverride?.SendMetadataOnlyCasesToDiscord ?? _config.SendMetadataOnlyCasesToDiscord;
    }

    private DiscordDeliveryOptions ResolveDeliveryOptions(EvidenceCase evidenceCase, bool hasDemo)
    {
        var serverOverride = ResolveOverride(evidenceCase.ServerId, evidenceCase.LegacyServerId);
        var roleId = evidenceCase.AntiCheat is not null
            ? serverOverride?.AntiCheatRoleId ?? _config.AntiCheatRoleId
            : serverOverride?.ReportRoleId ?? _config.ReportRoleId;
        var mention = !string.IsNullOrWhiteSpace(roleId) && (!_config.MentionRolesOnlyWhenDemoReady || hasDemo);
        return new DiscordDeliveryOptions(roleId, mention);
    }

    private DemosToDiscordServerOverride? ResolveOverride(string serverId, long? legacyServerId)
    {
        if (_config.ServerOverrides.TryGetValue(serverId, out var exact))
            return exact;
        if (legacyServerId is not null && _config.ServerOverrides.TryGetValue(legacyServerId.Value.ToString(), out var legacy))
            return legacy;
        return _config.ServerOverrides.TryGetValue("*", out var fallback) ? fallback : null;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}

