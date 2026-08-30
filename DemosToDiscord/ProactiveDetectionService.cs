using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Database.Models;
using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

public sealed class ProactiveDetectionService : IDisposable
{
    private readonly DemosToDiscordConfig _config;
    private readonly DetectionCapabilityService _capabilities;
    private readonly ProactiveBaselineService _baselines;
    private readonly RiskScorer _scorer;
    private readonly EvidenceCaseStore _store;
    private readonly DemoUploadService _uploads;
    private readonly ILogger<ProactiveDetectionService> _logger;
    private readonly Channel<ProactiveEvaluationTarget> _queue = Channel.CreateUnbounded<ProactiveEvaluationTarget>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly ConcurrentDictionary<string, DateTime> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _workers = [];
    private bool _started;

    public ProactiveDetectionService(
        DemosToDiscordConfig config,
        DetectionCapabilityService capabilities,
        ProactiveBaselineService baselines,
        RiskScorer scorer,
        EvidenceCaseStore store,
        DemoUploadService uploads,
        ILogger<ProactiveDetectionService> logger)
    {
        _config = config;
        _capabilities = capabilities;
        _baselines = baselines;
        _scorer = scorer;
        _store = store;
        _uploads = uploads;
        _logger = logger;
    }

    public void Start()
    {
        if (_started || !_config.Enabled || !_config.ProactiveDetection.Enabled)
            return;
        _started = true;
        for (var index = 0; index < Math.Max(1, _config.ProactiveDetection.MaxConcurrentEvaluations); index++)
            _workers.Add(Task.Run(() => WorkerAsync(_shutdown.Token), _shutdown.Token));
        _ = Task.Run(() => WarmBaselineAsync(_shutdown.Token), _shutdown.Token);
        _logger.LogInformation("[DemosToDiscord] Proactive review evaluation enabled");
    }

    public Task QueueMatchEndedAsync(IGameServer server, CancellationToken token)
    {
        if (!_started || !_config.ProactiveDetection.EvaluateOnMatchEnd)
            return Task.CompletedTask;
        foreach (var client in server.ConnectedClients.Where(EligibleClient))
            Queue(CreateTarget(server, client, "match end"));
        return Task.CompletedTask;
    }

    public Task QueueDisconnectAsync(EFClient client, CancellationToken token)
    {
        if (!_started || !_config.ProactiveDetection.EvaluateOnDisconnect ||
            !EligibleClient(client) || client.CurrentServer is not IGameServer server)
            return Task.CompletedTask;
        Queue(CreateTarget(server, client, "player disconnect"));
        return Task.CompletedTask;
    }

    private void Queue(ProactiveEvaluationTarget target)
    {
        var key = Key(target);
        _latest[key] = target.RequestedAtUtc;
        if (!_queue.Writer.TryWrite(target))
            _logger.LogWarning("[DemosToDiscord] Could not queue proactive evaluation for client {ClientId}", target.ClientId);
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        try
        {
            await foreach (var target in _queue.Reader.ReadAllAsync(token))
            {
                var delay = TimeSpan.FromSeconds(Math.Max(0, _config.ProactiveDetection.EvaluationDelaySeconds));
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, token);
                var key = Key(target);
                if (!_latest.TryGetValue(key, out var latest) || latest != target.RequestedAtUtc)
                    continue;
                _latest.TryRemove(new KeyValuePair<string, DateTime>(key, latest));
                await EvaluateAsync(target, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task EvaluateAsync(ProactiveEvaluationTarget target, CancellationToken token)
    {
        try
        {
            var serverOverride = ResolveOverride(target.ServerId, target.LegacyServerId);
            var capability = _capabilities.Resolve(target.Game, target.Map, target.Mode, serverOverride);
            if (!capability.EligibleForScoring)
            {
                _logger.LogDebug(
                    "[DemosToDiscord] Skipped proactive evaluation for {ClientId}: {Reason}",
                    target.ClientId,
                    capability.Reason);
                return;
            }

            var baseline = await _baselines.EvaluateAsync(target, capability, token);
            if (!baseline.HasNewData)
                return;
            var repeatCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _config.ProactiveDetection.RepeatHistoryDays));
            var recentCases = _store.CountRecentProactiveCases(target.ClientId, repeatCutoff);
            var assessment = _scorer.Score(baseline.Observations, recentCases);
            if (assessment.ShouldCreateCase)
                await _uploads.CaptureProactiveAsync(target, assessment, token);
            await _baselines.RecordEvaluationAsync(target, baseline, assessment, token);
            _logger.LogInformation(
                "[DemosToDiscord] Proactive evaluation for client {ClientId}: {Risk:0.0}/100 {Level}; {Detail}",
                target.ClientId,
                assessment.Score,
                assessment.Level,
                baseline.Detail);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "[DemosToDiscord] Proactive evaluation failed for client {ClientId} on server {ServerId}",
                target.ClientId,
                target.ServerId);
        }
    }

    private async Task WarmBaselineAsync(CancellationToken token)
    {
        try
        {
            await _baselines.RefreshAsync(false, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private DemosToDiscordServerOverride? ResolveOverride(string serverId, long legacyServerId)
    {
        if (_config.ServerOverrides.TryGetValue(serverId, out var byId))
            return byId;
        return _config.ServerOverrides.TryGetValue(legacyServerId.ToString(), out var byLegacyId) ? byLegacyId : null;
    }

    private static ProactiveEvaluationTarget CreateTarget(IGameServer server, EFClient client, string reason) => new(
        server.Id,
        server.ServerName.StripColors(),
        server.LegacyDatabaseId,
        server.GameCode.ToString().ToUpperInvariant(),
        server.Map?.Name ?? "Unknown",
        server.Gametype ?? "Unknown",
        client.ClientId,
        client.NetworkId,
        client.CurrentAlias?.Name.StripColors() ?? "Unknown",
        DateTime.UtcNow,
        reason);

    private static bool EligibleClient(EFClient client) => client.ClientId > 1 && !client.IsBot;
    private static string Key(ProactiveEvaluationTarget target) => $"{target.LegacyServerId}:{target.ClientId}";

    public void Dispose()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        try
        {
            Task.WaitAll(_workers.ToArray(), TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Shutdown cancellation is expected.
        }
        _shutdown.Dispose();
    }
}
