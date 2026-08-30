using System.Text.Json;
using Data.Abstractions;
using Data.Models;
using Data.Models.Client;
using Data.Models.Client.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed class ProactiveBaselineService : IDisposable
{
    private const int HeadHitLocation = 2;
    private readonly IDatabaseContextFactory _databaseContextFactory;
    private readonly DemosToDiscordConfig _config;
    private readonly ILogger<ProactiveBaselineService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly CancellationTokenSource _shutdown = new();
    private ProactiveBaselineState _state = new();
    private Task? _refreshLoop;
    private bool _initialized;

    public ProactiveBaselineService(
        IDatabaseContextFactory databaseContextFactory,
        DemosToDiscordConfig config,
        ILogger<ProactiveBaselineService> logger)
    {
        _databaseContextFactory = databaseContextFactory;
        _config = config;
        _logger = logger;
    }

    public bool IsAvailable { get; private set; }
    public DateTime? LastSuccessfulRefreshUtc { get; private set; }

    public async Task StartAsync(CancellationToken token)
    {
        if (!_config.EnableProactiveDetection || _initialized)
            return;
        _initialized = true;
        await LoadStateAsync(token);
        await RefreshAsync(token);
        _refreshLoop = Task.Run(() => RefreshLoopAsync(_shutdown.Token), CancellationToken.None);
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (!_config.EnableProactiveDetection)
            return;
        if (!await _refreshGate.WaitAsync(0, token))
            return;

        try
        {
            await using var context = _databaseContextFactory.CreateContext(false);
            var sourceLastKillId = await context.ClientKills.AsNoTracking()
                .Select(item => (long?)item.KillId).MaxAsync(token) ?? 0;
            ProactiveBaselineState current;
            lock (_stateGate)
                current = _state;

            var requiresBootstrap = current.Members.Count == 0 || sourceLastKillId < current.LastKillId;
            var refreshed = requiresBootstrap
                ? await BootstrapAsync(context, token)
                : await IncrementalRefreshAsync(context, current, sourceLastKillId, token);
            refreshed.UpdatedAtUtc = DateTime.UtcNow;
            lock (_stateGate)
                _state = refreshed;
            await SaveStateAsync(refreshed, token);
            IsAvailable = refreshed.Members.Count > 0;
            LastSuccessfulRefreshUtc = DateTime.UtcNow;
            _logger.LogInformation(
                "[DemosToDiscord] proactive baselines refreshed: {Players} player/server members and {Weapons} weapon members",
                refreshed.Members.Count, refreshed.WeaponMembers.Count);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            _logger.LogError(exception, "[DemosToDiscord] proactive baseline refresh failed; proactive evaluation is suppressed");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public ProactiveBaselineMember? GetPlayer(int clientId, long serverId)
    {
        lock (_stateGate)
        {
            return _state.Members.TryGetValue(MemberKey(clientId, serverId), out var member)
                ? Clone(member)
                : null;
        }
    }

    public ProactivePopulationBaseline? GetPopulation(
        ProactiveMetric metric,
        Reference.Game game,
        long? serverId = null,
        string? weapon = null)
    {
        List<double> values;
        string scope;
        lock (_stateGate)
        {
            if (metric == ProactiveMetric.KillingHeadRate && !string.IsNullOrWhiteSpace(weapon))
            {
                values = _state.WeaponMembers.Values
                    .Where(item => item.Game == game &&
                                   (serverId is null || item.ServerId == serverId) &&
                                   item.Weapon.Equals(weapon, StringComparison.OrdinalIgnoreCase) &&
                                   item.KillingHits >= Math.Max(100, _config.ProactiveMinimumTrackedHits / 2) &&
                                   item.KillingHeadHits >= _config.ProactiveMinimumHeadEvents)
                    .Select(item => item.KillingHeadRate).Order().ToList();
                scope = serverId is null ? $"{game} + {weapon}" : $"{game} + server + {weapon}";
            }
            else
            {
                return ProactiveBaselineSelector.Build(
                    _state.Members.Values, metric, game, serverId, _config.ProactiveMinimumPopulation,
                    _config.ProactiveMinimumTrackedHits, _config.ProactiveMinimumHeadEvents);
            }
        }

        if (values.Count < Math.Max(10, _config.ProactiveMinimumPopulation))
            return null;
        return new ProactivePopulationBaseline(metric, scope, values.Count, ProactiveBaselineMath.Median(values), values);
    }

    private async Task<ProactiveBaselineState> BootstrapAsync(Data.Context.DatabaseContext context, CancellationToken token)
    {
        var servers = await context.Servers.AsNoTracking().ToDictionaryAsync(item => item.ServerId, token);
        var statistics = await context.Set<EFClientStatistics>().AsNoTracking()
            .Select(item => new
            {
                item.ClientId, item.ServerId, item.Kills, item.Deaths, item.SPM, item.EloRating, item.Skill,
                item.TimePlayed, item.MaxStrain, item.AverageSnapValue, item.SnapHitCount, item.UpdatedAt
            }).ToListAsync(token);
        var state = new ProactiveBaselineState();
        foreach (var item in statistics)
        {
            if (!servers.TryGetValue(item.ServerId, out var server) || server.GameName is null)
                continue;
            var game = server.GameName.Value;
            state.Members[MemberKey(item.ClientId, item.ServerId)] = new ProactiveBaselineMember
            {
                ClientId = item.ClientId,
                ServerId = item.ServerId,
                Game = game,
                ServerName = server.HostName ?? string.Empty,
                Excluded = IsExcluded(game, item.ServerId, server.HostName),
                Kills = item.Kills,
                Deaths = item.Deaths,
                ScorePerMinute = item.SPM,
                Performance = Math.Round(item.EloRating / 3d + item.Skill * 2d / 3d, 2),
                TimePlayedSeconds = item.TimePlayed,
                MaximumStrain = item.MaxStrain,
                AverageSnap = item.AverageSnapValue,
                SnapHitCount = item.SnapHitCount,
                StatisticsUpdatedAtUtc = item.UpdatedAt
            };
        }

        await PopulateHitStatisticsAsync(context, state, token);
        await PopulateKillsAsync(context, state, 0, token);
        state.LastStatisticsUpdatedAtUtc = statistics.Max(item => item.UpdatedAt);
        state.LastHitStatisticsUpdatedAtUtc = await context.HitStatistics.AsNoTracking()
            .Select(item => (DateTime?)(item.UpdatedDateTime ?? item.CreatedDateTime)).MaxAsync(token);
        state.LastKillId = await context.ClientKills.AsNoTracking().Select(item => (long?)item.KillId).MaxAsync(token) ?? 0;
        return state;
    }

    private async Task<ProactiveBaselineState> IncrementalRefreshAsync(
        Data.Context.DatabaseContext context,
        ProactiveBaselineState current,
        long sourceLastKillId,
        CancellationToken token)
    {
        var state = Clone(current);
        var statsWatermark = current.LastStatisticsUpdatedAtUtc ?? DateTime.MinValue;
        var changedStats = await context.Set<EFClientStatistics>().AsNoTracking()
            .Where(item => item.UpdatedAt != null && item.UpdatedAt > statsWatermark)
            .Select(item => new
            {
                item.ClientId, item.ServerId, item.Kills, item.Deaths, item.SPM, item.EloRating, item.Skill,
                item.TimePlayed, item.MaxStrain, item.AverageSnapValue, item.SnapHitCount, item.UpdatedAt
            }).ToListAsync(token);
        var servers = await context.Servers.AsNoTracking().ToDictionaryAsync(item => item.ServerId, token);
        foreach (var item in changedStats)
        {
            if (!servers.TryGetValue(item.ServerId, out var server) || server.GameName is null)
                continue;
            var key = MemberKey(item.ClientId, item.ServerId);
            if (!state.Members.TryGetValue(key, out var member))
            {
                member = new ProactiveBaselineMember { ClientId = item.ClientId, ServerId = item.ServerId };
                state.Members[key] = member;
            }
            member.Game = server.GameName.Value;
            member.ServerName = server.HostName ?? string.Empty;
            member.Excluded = IsExcluded(member.Game, member.ServerId, member.ServerName);
            member.Kills = item.Kills;
            member.Deaths = item.Deaths;
            member.ScorePerMinute = item.SPM;
            member.Performance = Math.Round(item.EloRating / 3d + item.Skill * 2d / 3d, 2);
            member.TimePlayedSeconds = item.TimePlayed;
            member.MaximumStrain = item.MaxStrain;
            member.AverageSnap = item.AverageSnapValue;
            member.SnapHitCount = item.SnapHitCount;
            member.StatisticsUpdatedAtUtc = item.UpdatedAt;
        }

        var hitWatermark = current.LastHitStatisticsUpdatedAtUtc ?? DateTime.MinValue;
        var changedHitPairs = await context.HitStatistics.AsNoTracking()
            .Where(item => (item.UpdatedDateTime ?? item.CreatedDateTime) > hitWatermark && item.ServerId != null)
            .Select(item => new { item.ClientId, ServerId = item.ServerId!.Value })
            .Distinct().Take(1000).ToListAsync(token);
        foreach (var pair in changedHitPairs)
            await RefreshHitPairAsync(context, state, pair.ClientId, pair.ServerId, token);

        if (sourceLastKillId > current.LastKillId)
            await PopulateKillsAsync(context, state, current.LastKillId, token);
        state.LastKillId = sourceLastKillId;
        if (changedStats.Count > 0)
            state.LastStatisticsUpdatedAtUtc = changedStats.Max(item => item.UpdatedAt);
        state.LastHitStatisticsUpdatedAtUtc = await context.HitStatistics.AsNoTracking()
            .Select(item => (DateTime?)(item.UpdatedDateTime ?? item.CreatedDateTime)).MaxAsync(token)
            ?? state.LastHitStatisticsUpdatedAtUtc;
        return state;
    }

    private static async Task PopulateHitStatisticsAsync(
        Data.Context.DatabaseContext context,
        ProactiveBaselineState state,
        CancellationToken token)
    {
        var rows = await context.HitStatistics.AsNoTracking()
            .Where(item => item.ServerId != null && item.HitLocationId != null && item.WeaponId == null && item.MeansOfDeathId == null)
            .GroupBy(item => new { item.ClientId, ServerId = item.ServerId!.Value })
            .Select(group => new
            {
                group.Key.ClientId,
                group.Key.ServerId,
                Hits = group.Sum(item => item.HitCount),
                Heads = group.Sum(item => item.HitLocation!.Name.ToLower() == "head" ? item.HitCount : 0)
            }).ToListAsync(token);
        foreach (var row in rows)
        {
            if (!state.Members.TryGetValue(MemberKey(row.ClientId, row.ServerId), out var member))
                continue;
            member.TrackedHits = row.Hits;
            member.TrackedHeadHits = row.Heads;
        }
    }

    private static async Task RefreshHitPairAsync(
        Data.Context.DatabaseContext context,
        ProactiveBaselineState state,
        int clientId,
        long serverId,
        CancellationToken token)
    {
        var rows = await context.HitStatistics.AsNoTracking()
            .Where(item => item.ClientId == clientId && item.ServerId == serverId && item.HitLocationId != null &&
                           item.WeaponId == null && item.MeansOfDeathId == null)
            .Select(item => new { item.HitCount, Name = item.HitLocation!.Name }).ToListAsync(token);
        if (!state.Members.TryGetValue(MemberKey(clientId, serverId), out var member))
            return;
        member.TrackedHits = rows.Sum(item => item.HitCount);
        member.TrackedHeadHits = rows.Where(item => item.Name.Equals("head", StringComparison.OrdinalIgnoreCase)).Sum(item => item.HitCount);
    }

    private static async Task PopulateKillsAsync(
        Data.Context.DatabaseContext context,
        ProactiveBaselineState state,
        long afterKillId,
        CancellationToken token)
    {
        var rows = await context.ClientKills.AsNoTracking()
            .Where(item => item.KillId > afterKillId && item.IsKill)
            .GroupBy(item => new { item.AttackerId, item.ServerId, item.WeaponReference })
            .Select(group => new
            {
                ClientId = group.Key.AttackerId,
                group.Key.ServerId,
                Weapon = group.Key.WeaponReference,
                Kills = group.Count(),
                Heads = group.Count(item => item.HitLoc == HeadHitLocation)
            }).ToListAsync(token);
        foreach (var row in rows)
        {
            if (!state.Members.TryGetValue(MemberKey(row.ClientId, row.ServerId), out var member))
                continue;
            member.KillingHits += row.Kills;
            member.KillingHeadHits += row.Heads;
            if (string.IsNullOrWhiteSpace(row.Weapon))
                continue;
            var key = WeaponKey(row.ClientId, row.ServerId, row.Weapon);
            if (!state.WeaponMembers.TryGetValue(key, out var weaponMember))
            {
                weaponMember = new ProactiveWeaponBaselineMember
                {
                    ClientId = row.ClientId,
                    ServerId = row.ServerId,
                    Game = member.Game,
                    Weapon = row.Weapon
                };
                state.WeaponMembers[key] = weaponMember;
            }
            weaponMember.KillingHits += row.Kills;
            weaponMember.KillingHeadHits += row.Heads;
        }
    }

    private bool IsExcluded(Reference.Game game, long serverId, string? serverName)
    {
        if (_config.ProactiveExcludedServerIds.Contains(serverId) ||
            _config.ProactiveExcludedGames.Any(value => value.Equals(game.ToString(), StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!_config.ProactiveExcludeT5Zombies || game != Reference.Game.T5)
            return false;
        var name = serverName ?? string.Empty;
        return name.Contains("zombie", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("kino", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("five", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _config.ProactiveBaselineRefreshMinutes)));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await RefreshAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task LoadStateAsync(CancellationToken token)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
            return;
        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<ProactiveBaselineState>(stream, _jsonOptions, token);
            if (loaded is { SchemaVersion: 1 })
            {
                loaded.Members ??= [];
                loaded.WeaponMembers ??= [];
                lock (_stateGate)
                    _state = loaded;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[DemosToDiscord] proactive baseline cache was invalid and will be rebuilt");
        }
    }

    private async Task SaveStateAsync(ProactiveBaselineState state, CancellationToken token)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, token);
        File.Move(temporary, path, true);
    }

    private string ResolvePath() => Path.IsPathRooted(_config.ProactiveBaselineStateFilePath)
        ? _config.ProactiveBaselineStateFilePath
        : Path.Combine(AppContext.BaseDirectory, _config.ProactiveBaselineStateFilePath);

    private ProactiveBaselineState Clone(ProactiveBaselineState value) =>
        JsonSerializer.Deserialize<ProactiveBaselineState>(JsonSerializer.Serialize(value, _jsonOptions), _jsonOptions)!;
    private ProactiveBaselineMember Clone(ProactiveBaselineMember value) =>
        JsonSerializer.Deserialize<ProactiveBaselineMember>(JsonSerializer.Serialize(value, _jsonOptions), _jsonOptions)!;
    private static string MemberKey(int clientId, long serverId) => $"{clientId}:{serverId}";
    private static string WeaponKey(int clientId, long serverId, string weapon) => $"{clientId}:{serverId}:{weapon.ToLowerInvariant()}";

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _refreshLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _shutdown.Dispose();
        _refreshGate.Dispose();
    }
}
