using Data.Models.Client;
using SharedLibraryCore;
using SharedLibraryCore.Commands;
using SharedLibraryCore.Configuration;
using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

public sealed class DemosToDiscordStatusCommand : Command
{
    private readonly DemoUploadService _service;

    public DemosToDiscordStatusCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        DemoUploadService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "dtdstatus";
        Alias = "dtds";
        Description = "shows DemosToDiscord evidence queue status";
        Permission = EFClient.Permission.Moderator;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent) => Reply(gameEvent, _service.StatusSummary());

    private static Task Reply(GameEvent gameEvent, params string[] messages) =>
        gameEvent.Origin.TellAsync(messages, gameEvent.Owner.Manager.CancellationToken);
}

public sealed class DemosToDiscordStatsCommand : Command
{
    private readonly DemoUploadService _service;

    public DemosToDiscordStatsCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        DemoUploadService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "dtdstats";
        Alias = "dtdst";
        Description = "shows recent DemosToDiscord evidence statistics";
        Permission = EFClient.Permission.Moderator;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent)
    {
        var snapshot = _service.GetSnapshot();
        var games = snapshot.Cases.GroupBy(item => item.Game).Select(group => $"{group.Key}={group.Count()}");
        return gameEvent.Origin.TellAsync(
            [$"DTD cases={snapshot.Cases.Count}, reports={snapshot.Reports}, auto-bans={snapshot.AutomatedBans}, uploaded={snapshot.Uploaded}, no-demo={snapshot.NoDemo}, unsupported={snapshot.Unsupported}, failed={snapshot.Failed}.",
                $"DTD games: {(games.Any() ? string.Join(", ", games) : "none yet")}."],
            gameEvent.Owner.Manager.CancellationToken);
    }
}

public sealed class DemosToDiscordTestCommand : Command
{
    private readonly DemoUploadService _service;

    public DemosToDiscordTestCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        DemoUploadService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "dtdtest";
        Alias = "dtdt";
        Description = "sends a DemosToDiscord webhook test";
        Permission = EFClient.Permission.SeniorAdmin;
        RequiresTarget = false;
    }

    public override async Task ExecuteAsync(GameEvent gameEvent)
    {
        try
        {
            await _service.TestWebhookAsync(gameEvent.Owner.Manager.CancellationToken);
            await Reply(gameEvent, "DemosToDiscord webhook test sent successfully.");
        }
        catch (Exception exception)
        {
            await Reply(gameEvent, $"DemosToDiscord webhook test failed: {exception.Message}");
        }
    }

    private static Task Reply(GameEvent gameEvent, params string[] messages) =>
        gameEvent.Origin.TellAsync(messages, gameEvent.Owner.Manager.CancellationToken);
}

public sealed class DemosToDiscordFindCommand : Command
{
    private readonly DemoUploadService _service;

    public DemosToDiscordFindCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        DemoUploadService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "dtdfind";
        Alias = "dtdf";
        Description = "previews the best demo match for an evidence case";
        Permission = EFClient.Permission.Moderator;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent)
    {
        var caseId = gameEvent.Data?.Trim() ?? string.Empty;
        if (caseId.Length == 0)
            return Reply(gameEvent, "Usage: !dtdfind <case-id>");
        var candidate = _service.FindCandidate(caseId);
        return candidate is null
            ? Reply(gameEvent, $"No current demo candidate found for case {caseId}.")
            : Reply(gameEvent, $"DTD candidate: {Path.GetFileName(candidate.DemoPath)}, target-confirmed={candidate.TargetConfirmed}, score={candidate.Score:0.0}.");
    }

    private static Task Reply(GameEvent gameEvent, params string[] messages) =>
        gameEvent.Origin.TellAsync(messages, gameEvent.Owner.Manager.CancellationToken);
}

public sealed class DemosToDiscordRetryCommand : Command
{
    private readonly DemoUploadService _service;

    public DemosToDiscordRetryCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        DemoUploadService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "dtdretry";
        Alias = "dtdr";
        Description = "requeues a DemosToDiscord evidence case";
        Permission = EFClient.Permission.SeniorAdmin;
        RequiresTarget = false;
    }

    public override async Task ExecuteAsync(GameEvent gameEvent)
    {
        var caseId = gameEvent.Data?.Trim() ?? string.Empty;
        if (caseId.Length == 0)
        {
            await Reply(gameEvent, "Usage: !dtdretry <case-id>");
            return;
        }

        var queued = await _service.RetryAsync(caseId, gameEvent.Owner.Manager.CancellationToken);
        await Reply(gameEvent, queued ? $"DTD case {caseId} requeued." : $"DTD case {caseId} was not found.");
    }

    private static Task Reply(GameEvent gameEvent, params string[] messages) =>
        gameEvent.Origin.TellAsync(messages, gameEvent.Owner.Manager.CancellationToken);
}

