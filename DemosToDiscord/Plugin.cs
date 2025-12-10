using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharedLibraryCore;
using SharedLibraryCore.Configuration;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Interfaces;
using SharedLibraryCore.Interfaces.Events;

namespace DemosToDiscord;

public class Plugin : IPluginV2
{
    private readonly DemoUploadService _demoService;
    private readonly DemosToDiscordConfig _config;

    public string Name => "DemosToDiscord";
    public string Author => "OllyMc27";
    public string Version => "1.1.0";

    public Plugin(DemoUploadService demoService, DemosToDiscordConfig config)
    {
        _demoService = demoService;
        _config = config;

        if (string.IsNullOrWhiteSpace(_config.Webhook))
        {
            Console.WriteLine($"[{Name}] not loaded: Webhook is empty in DemosToDiscord.json");
            // We still register events, but actual HTTP calls will early-exit based on Webhook checks.
        }

        IManagementEventSubscriptions.ClientPenaltyAdministered += OnClientPenaltyAdministered;
        IManagementEventSubscriptions.Load += OnLoad;
    }

    public static void RegisterDependencies(IServiceCollection services)
    {
        services.AddSingleton<DemoUploadService>();
        services.AddConfiguration("DemosToDiscord", new DemosToDiscordConfig());
    }

    private async Task OnLoad(IManager manager, CancellationToken token)
    {
        Console.WriteLine($"[{Name}] by OllyMc27 loaded. Version: {Version}");
        Console.WriteLine($"[{Name}] Attempting Discord startup test...");

        await _demoService.SendStartupMessageAsync(token);

        Console.WriteLine($"[{Name}] Discord startup attempt complete.");
    }

    private async Task OnClientPenaltyAdministered(ClientPenaltyEvent penaltyEvent, CancellationToken token)
    {
        await _demoService.HandlePenaltyAsync(penaltyEvent, token);
    }
}
