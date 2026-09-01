using System.Collections.Concurrent;
using Data.Models.Client;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

public sealed class FlaggedPlayerReviewService(
    DemosToDiscordConfig config,
    EvidenceCaseStore store,
    DemoUploadService uploads,
    DiscordWebhookClient discord,
    ILogger<FlaggedPlayerReviewService> logger)
{
    private readonly ConcurrentDictionary<int, DateTime> _lastAlerts = new();

    public async Task<string> FlagAfterInconclusiveAsync(
        IManager manager,
        EvidenceCase evidenceCase,
        EFClient reviewer,
        CancellationToken token)
    {
        if (!config.FlagPlayerOnInconclusiveReview)
            return "Inconclusive-case flagging is disabled.";
        var target = await manager.GetClientService().Get(evidenceCase.TargetClientId);
        if (target is null)
            return "The player could not be loaded, so their IW4MAdmin level was not changed.";
        if (!ShouldFlag(EvidenceReviewDecision.Inconclusive, target.Level, target.IsPrivileged(), true))
        {
            if (target.Level is EFClient.Permission.Flagged or EFClient.Permission.Banned)
                return "The player was already flagged or banned.";
            return "A privileged IW4MAdmin account cannot be flagged by this workflow.";
        }

        var owner = manager.GetServers().FirstOrDefault(server =>
                        server.Id.Equals(evidenceCase.ServerId, StringComparison.OrdinalIgnoreCase))
                    ?? manager.GetServers().FirstOrDefault();
        if (owner is null)
            return "No monitored server was available to process the native flag event.";

        var reason = $"DemosToDiscord case {evidenceCase.Id} closed inconclusive — live review requested";
        var gameEvent = new GameEvent
        {
            Type = GameEvent.EventType.Flag,
            Origin = Utilities.IW4MAdminClient(owner),
            ImpersonationOrigin = (SharedLibraryCore.Database.Models.EFClient)reviewer,
            Target = target,
            Owner = owner,
            Data = reason,
            Message = reason
        };
        manager.AddEvent(gameEvent);
        var completed = await gameEvent.WaitAsync(TimeSpan.FromSeconds(15), token);
        if (completed.FailReason != GameEvent.EventFailReason.None)
            return $"IW4MAdmin rejected the flag event ({completed.FailReason}).";

        await store.UpdateAsync(evidenceCase.Id, item => item.History.Add(new EvidenceHistoryEntry
        {
            WhenUtc = DateTime.UtcNow,
            Action = EvidenceHistoryAction.PlayerFlagged,
            AdminClientId = reviewer.ClientId,
            AdminName = reviewer.CurrentAlias?.Name.StripColors() ?? $"Client #{reviewer.ClientId}",
            Summary = "Player level changed to Flagged after an inconclusive evidence review."
        }), token);
        return "The player was flagged for live review when they next join.";
    }

    internal static bool ShouldFlag(
        EvidenceReviewDecision decision,
        EFClient.Permission? targetLevel,
        bool isPrivileged,
        bool enabled) => enabled && decision == EvidenceReviewDecision.Inconclusive && !isPrivileged &&
                         targetLevel is not (EFClient.Permission.Flagged or EFClient.Permission.Banned);

    public async Task NotifyJoinAsync(ClientStateAuthorizeEvent clientEvent, CancellationToken token)
    {
        var client = clientEvent.Client;
        var server = client.CurrentServer;
        if (!config.Enabled || !config.NotifyDiscordWhenFlaggedPlayerJoins ||
            server is null || client.Level != EFClient.Permission.Flagged || client.IsBot)
            return;

        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(config.FlaggedPlayerJoinAlertCooldownMinutes, 1, 1440));
        if (_lastAlerts.TryGetValue(client.ClientId, out var previous) && now - previous < cooldown)
            return;

        var evidenceCase = store.Snapshot().Cases
            .Where(item => item.TargetClientId == client.ClientId)
            .OrderByDescending(item => item.ReviewDecision == EvidenceReviewDecision.Inconclusive)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();
        var routingCase = evidenceCase ?? new EvidenceCase
        {
            ServerId = server.Id,
            ServerName = server.ServerName.StripColors(),
            LegacyServerId = server.LegacyDatabaseId,
            Game = server.GameCode.ToString(),
            TargetClientId = client.ClientId,
            TargetNetworkId = client.NetworkId,
            TargetName = client.CurrentAlias?.Name.StripColors() ?? client.CleanedName.StripColors()
        };
        var webhook = uploads.ResolveWebhook(routingCase);
        if (string.IsNullOrWhiteSpace(webhook))
            return;

        _lastAlerts[client.ClientId] = now;
        try
        {
            await discord.SendFlaggedPlayerJoinAsync(
                webhook,
                uploads.ResolveFlaggedPlayerRoleId(server.Id, server.LegacyDatabaseId),
                client.ClientId,
                client.NetworkId,
                client.CurrentAlias?.Name.StripColors() ?? client.CleanedName.StripColors(),
                server.ServerName.StripColors(),
                server.Id,
                evidenceCase,
                token);
            logger.LogWarning(
                "[DemosToDiscord] Sent flagged-player join alert for client {ClientId} on {ServerId}",
                client.ClientId, server.Id);
        }
        catch (Exception exception)
        {
            _lastAlerts.TryRemove(client.ClientId, out _);
            logger.LogWarning(exception,
                "[DemosToDiscord] Could not send flagged-player join alert for client {ClientId}", client.ClientId);
        }
    }
}
