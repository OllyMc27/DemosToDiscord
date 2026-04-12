using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharedLibraryCore;
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
    public string Version => "1.1.2.4";

    public Plugin(DemoUploadService demoService, DemosToDiscordConfig config)
    {
        _demoService = demoService;
        _config = config;

        EnsureConfigUpToDate(_config);

        if (string.IsNullOrWhiteSpace(_config.Webhook))
        {
            Console.WriteLine($"[{Name}] not loaded: Webhook is empty in DemosToDiscord.json");
        }

        IManagementEventSubscriptions.ClientPenaltyAdministered += OnClientPenaltyAdministered;
        IManagementEventSubscriptions.Load += OnLoad;
    }

    public static void RegisterDependencies(IServiceCollection services)
    {
        services.AddSingleton<DemoUploadService>();
        services.AddConfiguration<DemosToDiscordConfig>("DemosToDiscord", new DemosToDiscordConfig());
    }

    private static void EnsureConfigUpToDate(DemosToDiscordConfig config)
    {
        var defaults = new DemosToDiscordConfig();
        var updated = false;

        foreach (var prop in typeof(DemosToDiscordConfig).GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite)
            {
                continue;
            }

            var currentValue = prop.GetValue(config);
            var defaultValue = prop.GetValue(defaults);

            if (currentValue is null)
            {
                prop.SetValue(config, defaultValue);
                updated = true;
                continue;
            }

            if (prop.PropertyType == typeof(string) &&
                string.IsNullOrWhiteSpace(currentValue as string) &&
                !string.IsNullOrWhiteSpace(defaultValue as string))
            {
                prop.SetValue(config, defaultValue);
                updated = true;
            }
        }

        if (updated)
        {
            Console.WriteLine("[DemosToDiscord] Config updated with new default values.");
        }
    }

    private async Task OnLoad(IManager manager, CancellationToken token)
    {
        Console.WriteLine($"[{Name}] by {Author} loaded. Version: {Version}");

        if (_config.Debug)
        {
            Console.WriteLine($"[{Name}] Debug enabled - sending Discord startup test...");
            await _demoService.SendStartupMessageAsync(token);
            Console.WriteLine($"[{Name}] Discord startup test complete.");
        }
    }

    private Task OnClientPenaltyAdministered(ClientPenaltyEvent penaltyEvent, CancellationToken token)
    {
        return _demoService.HandlePenaltyAsync(penaltyEvent, token);
    }

    public void Dispose()
    {
        IManagementEventSubscriptions.ClientPenaltyAdministered -= OnClientPenaltyAdministered;
        IManagementEventSubscriptions.Load -= OnLoad;
    }
}