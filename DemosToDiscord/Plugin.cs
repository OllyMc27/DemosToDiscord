using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Events.Game;
using SharedLibraryCore.Interfaces;
using SharedLibraryCore.Interfaces.Events;

namespace DemosToDiscord;

public sealed class Plugin : IPluginV2
{
    private readonly DemoUploadService _service;
    private readonly ProactiveBaselineService _proactiveBaselines;
    private readonly ProactiveEvaluationScheduler _proactiveScheduler;
    private readonly DemosToDiscordWebfront _webfront;
    private readonly DemosToDiscordConfig _config;
    private readonly ILogger<Plugin> _logger;
    private bool _disposed;

    public string Name => "DemosToDiscord";
    public string Author => "OllyMc27";
    public string Version => Utilities.GetVersionAsString();

    public static void RegisterDependencies(IServiceCollection services)
    {
        services.AddConfiguration("DemosToDiscord", new DemosToDiscordConfig());
        services.AddSingleton<EvidenceCaseStore>();
        services.AddSingleton<DemoLocator>();
        services.AddSingleton<DiscordWebhookClient>();
        services.AddSingleton<FriendlyGameNames>();
        services.AddSingleton<AntiCheatMetricsService>();
        services.AddSingleton<ProactiveBaselineService>();
        services.AddSingleton<IProactiveBaselineProvider>(provider => provider.GetRequiredService<ProactiveBaselineService>());
        services.AddSingleton<ProactiveRiskScorer>();
        services.AddSingleton<ProactiveEvaluationScheduler>();
        services.AddSingleton<ProactiveEvidenceIntegration>();
        services.AddSingleton<IProactiveAssessmentSink>(provider => provider.GetRequiredService<ProactiveEvidenceIntegration>());
        services.AddSingleton<PlayerTimelineService>();
        services.AddSingleton<EvidenceReviewService>();
        services.AddSingleton<DemoUploadService>();
        services.AddSingleton<DemosToDiscordWebfront>();
    }

    public Plugin(
        DemoUploadService service,
        DemosToDiscordWebfront webfront,
        ProactiveBaselineService proactiveBaselines,
        ProactiveEvaluationScheduler proactiveScheduler,
        DemosToDiscordConfig config,
        ILogger<Plugin> logger)
    {
        _service = service;
        _proactiveBaselines = proactiveBaselines;
        _proactiveScheduler = proactiveScheduler;
        _webfront = webfront;
        _config = config;
        _logger = logger;
        NormalizeConfiguration(_config);
        if (!EvidenceTime.Configure(_config.TimeZone))
            _logger.LogWarning("[{Name}] time zone {TimeZone} was not recognised; using {Fallback}", Name, _config.TimeZone, EvidenceTime.DefaultTimeZoneId);

        IManagementEventSubscriptions.ClientPenaltyAdministered += OnClientPenaltyAdministered;
        IManagementEventSubscriptions.ClientStateDisposed += OnClientStateDisposed;
        IGameEventSubscriptions.MatchEnded += OnMatchEnded;
        IManagementEventSubscriptions.Load += OnLoad;
        _webfront.Register();

        _logger.LogInformation("[{Name}] {Version} by {Author} initialized", Name, Version, Author);
    }

    private async Task OnLoad(IManager _, CancellationToken token)
    {
        await _service.StartAsync(token);
        await _proactiveBaselines.StartAsync(token);
        Console.WriteLine($"[{Name}] by {Author} loaded. Version: {Version}");
        Console.WriteLine($"[{Name}] report evidence: {(_config.UploadOnReports ? "enabled" : "disabled")}; anti-cheat evidence: {(_config.UploadOnAutomatedBans ? string.Join(", ", _config.AutomatedBanGames) : "disabled")}");

        if (_config.Debug && !string.IsNullOrWhiteSpace(_config.Webhook))
        {
            try
            {
                await _service.TestWebhookAsync(token);
                _logger.LogInformation("[{Name}] startup webhook test succeeded", Name);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "[{Name}] startup webhook test failed", Name);
            }
        }
    }

    private Task OnClientPenaltyAdministered(ClientPenaltyEvent penaltyEvent, CancellationToken token) =>
        _service.HandlePenaltyAsync(penaltyEvent, token);

    private Task OnClientStateDisposed(ClientStateDisposeEvent clientEvent, CancellationToken token)
    {
        var client = clientEvent.Client;
        var server = client.CurrentServer;
        if (server is not null && !client.IsBot)
            _proactiveScheduler.Schedule(CreateEvaluation(client, server, "client disconnect"));
        return Task.CompletedTask;
    }

    private Task OnMatchEnded(MatchEndEvent matchEvent, CancellationToken token)
    {
        foreach (var client in matchEvent.Server.ConnectedClients.Where(client => !client.IsBot))
            _proactiveScheduler.Schedule(CreateEvaluation(client, matchEvent.Server, "match ended"));
        return Task.CompletedTask;
    }

    private static ProactiveEvaluationRequest CreateEvaluation(
        SharedLibraryCore.Database.Models.EFClient client,
        IGameServer server,
        string reason) => new(
        client.ClientId,
        client.NetworkId,
        client.CleanedName ?? client.Name ?? "Unknown",
        server.LegacyDatabaseId,
        server.Id,
        server.ServerName,
        server.GameCode,
        server.Map?.Name ?? string.Empty,
        server.Gametype ?? string.Empty,
        DateTime.UtcNow,
        reason,
        server.MatchStartTime);

    private static void NormalizeConfiguration(DemosToDiscordConfig config)
    {
        config.AutomatedBanGames ??= ["T6"];
        config.SupportedDemoGames ??= ["T5", "T6"];
        config.T5ZombieMapPrefixes ??= ["zombie_"];
        config.T5ZombieModes ??= ["zclassic", "zstandard", "zombie"];
        config.GameWebhooks ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (config.GameWebhooks.Comparer != StringComparer.OrdinalIgnoreCase)
            config.GameWebhooks = new Dictionary<string, string>(config.GameWebhooks, StringComparer.OrdinalIgnoreCase);
        config.ServerOverrides ??= new Dictionary<string, DemosToDiscordServerOverride>(StringComparer.OrdinalIgnoreCase);
        if (config.ServerOverrides.Comparer != StringComparer.OrdinalIgnoreCase)
            config.ServerOverrides = new Dictionary<string, DemosToDiscordServerOverride>(config.ServerOverrides, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(config.StateFilePath))
            config.StateFilePath = "Configuration/DemosToDiscordCases.json";
        if (string.IsNullOrWhiteSpace(config.TimeZone))
            config.TimeZone = EvidenceTime.DefaultTimeZoneId;
        config.ProactiveExcludedGames ??= [];
        config.ProactiveExcludedServerIds ??= [];
        if (string.IsNullOrWhiteSpace(config.ProactiveBaselineStateFilePath))
            config.ProactiveBaselineStateFilePath = "Configuration/DemosToDiscordProactiveBaselines.json";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        IManagementEventSubscriptions.ClientPenaltyAdministered -= OnClientPenaltyAdministered;
        IManagementEventSubscriptions.ClientStateDisposed -= OnClientStateDisposed;
        IGameEventSubscriptions.MatchEnded -= OnMatchEnded;
        IManagementEventSubscriptions.Load -= OnLoad;
        _webfront.Dispose();
        _service.Dispose();
        _proactiveScheduler.Dispose();
        _proactiveBaselines.Dispose();
        _logger.LogInformation("[{Name}] unloaded", Name);
    }
}

