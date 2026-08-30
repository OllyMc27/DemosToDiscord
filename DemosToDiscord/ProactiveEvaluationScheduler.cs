using System.Collections.Concurrent;
using System.Threading.Channels;
using Data.Models;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed record ProactiveEvaluationRequest(
    int ClientId,
    long NetworkId,
    string PlayerName,
    long ServerId,
    string ServerEndpoint,
    string ServerName,
    Reference.Game Game,
    string Map,
    string Mode,
    DateTime RequestedAtUtc,
    string Reason);

public interface IProactiveAssessmentSink
{
    Task HandleAsync(ProactiveEvaluationRequest request, ProactiveRiskAssessment assessment, CancellationToken token);
}

public sealed record ProactiveGameCapability(
    bool Supported,
    bool HasTrackedHitMetrics,
    bool HasKillingHitMetrics,
    bool HasMechanicsMetrics,
    bool SupportsDemo,
    string Reason)
{
    public static ProactiveGameCapability For(Reference.Game game, bool t5Zombies = false) => (game, t5Zombies) switch
    {
        (Reference.Game.T6, false) => new(true, true, true, true, true, string.Empty),
        (Reference.Game.IW5, false) => new(true, true, true, true, false, "Metadata-only evidence on the current IW5 setup."),
        (Reference.Game.T5, false) => new(true, true, false, false, true, string.Empty),
        (Reference.Game.T4, false) => new(true, true, false, false, false, "Metadata-only evidence on the current T4 setup."),
        (Reference.Game.T5, true) => new(false, false, false, false, false, "T5 Zombies requires a separate population model."),
        _ => new(false, false, false, false, false, "This game is not supported by proactive scoring.")
    };
}

public sealed class ProactiveEvaluationDeduplicator(TimeSpan window)
{
    private readonly ConcurrentDictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(ProactiveEvaluationRequest request, DateTime nowUtc)
    {
        var key = $"{request.ClientId}:{request.ServerId}";
        while (true)
        {
            if (!_recent.TryGetValue(key, out var previous))
                return _recent.TryAdd(key, nowUtc);
            if (nowUtc - previous < window)
                return false;
            if (_recent.TryUpdate(key, nowUtc, previous))
                return true;
        }
    }

    public void Prune(DateTime cutoffUtc)
    {
        foreach (var item in _recent.Where(item => item.Value < cutoffUtc))
            _recent.TryRemove(item.Key, out _);
    }
}

public sealed class ProactiveEvaluationScheduler : IDisposable
{
    private readonly ProactiveBaselineService _baselines;
    private readonly ProactiveRiskScorer _scorer;
    private readonly EvidenceCaseStore _caseStore;
    private readonly IReadOnlyList<IProactiveAssessmentSink> _sinks;
    private readonly DemosToDiscordConfig _config;
    private readonly ILogger<ProactiveEvaluationScheduler> _logger;
    private readonly Channel<ProactiveEvaluationRequest> _queue;
    private readonly ProactiveEvaluationDeduplicator _deduplicator;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    public ProactiveEvaluationScheduler(
        ProactiveBaselineService baselines,
        ProactiveRiskScorer scorer,
        EvidenceCaseStore caseStore,
        IEnumerable<IProactiveAssessmentSink> sinks,
        DemosToDiscordConfig config,
        ILogger<ProactiveEvaluationScheduler> logger)
    {
        _baselines = baselines;
        _scorer = scorer;
        _caseStore = caseStore;
        _sinks = sinks.ToList();
        _config = config;
        _logger = logger;
        _queue = Channel.CreateBounded<ProactiveEvaluationRequest>(new BoundedChannelOptions(Math.Max(10, config.ProactiveEvaluationQueueCapacity))
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _deduplicator = new ProactiveEvaluationDeduplicator(
            TimeSpan.FromMinutes(Math.Max(1, config.ProactiveEvaluationDeduplicationMinutes)));
        _worker = Task.Run(() => WorkerAsync(_shutdown.Token), CancellationToken.None);
    }

    public bool Schedule(ProactiveEvaluationRequest request)
    {
        if (!_config.EnableProactiveDetection || request.ClientId <= 0 || request.ServerId <= 0)
            return false;
        if (!ProactiveGameCapability.For(request.Game).Supported)
            return false;
        if (!_deduplicator.TryAcquire(request, DateTime.UtcNow))
            return false;
        if (_queue.Writer.TryWrite(request))
            return true;
        _logger.LogWarning("[DemosToDiscord] proactive evaluation queue is full; suppressed client {ClientId}", request.ClientId);
        return false;
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(token))
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _config.ProactiveEvaluationDelaySeconds)), token);
                    await _baselines.RefreshAsync(token);
                    var assessment = _scorer.Score(
                        request.ClientId, request.ServerId, _caseStore.CountProactiveHistory(request.ClientId));
                    if (assessment.Suppressed)
                    {
                        if (_config.Debug)
                            _logger.LogDebug("[DemosToDiscord] proactive evaluation suppressed for {ClientId}: {Reason}", request.ClientId, assessment.SuppressionReason);
                        continue;
                    }
                    foreach (var sink in _sinks)
                        await sink.HandleAsync(request, assessment, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "[DemosToDiscord] proactive evaluation failed for client {ClientId}; no action was taken", request.ClientId);
                }
                finally
                {
                    _deduplicator.Prune(DateTime.UtcNow.AddHours(-2));
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _shutdown.Dispose();
    }
}
