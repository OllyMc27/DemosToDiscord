using Data.Models;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Configuration;
using SharedLibraryCore.Database.Models;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Interfaces;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DemosToDiscord;

public class DemoUploadService
{
    private readonly DemosToDiscordConfig _config;
    private readonly ApplicationConfiguration _appConfig;
    private readonly ILogger<DemoUploadService> _logger;
    private readonly HttpClient _http;

    public DemoUploadService(
        DemosToDiscordConfig config,
        ApplicationConfiguration appConfig,
        ILogger<DemoUploadService> logger)
    {
        _config = config;
        _appConfig = appConfig;
        _logger = logger;

        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", "DemosToDiscord");
    }

    // -----------------------------
    // STARTUP MESSAGE
    // -----------------------------
    public async Task SendStartupMessageAsync(CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.Webhook))
            {
                _logger.LogError("Webhook empty — startup skipped.");
                return;
            }

            var payload = new
            {
                content = "✅ **DemosToDiscord Loaded**\nDiscord API Test Complete."
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync(_config.Webhook, content, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogWarning("Startup message -> {Status} | {Body}", response.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup message failed");
        }
    }

    // -----------------------------
    // ENTRY
    // -----------------------------
    public async Task HandlePenaltyAsync(ClientPenaltyEvent evt, CancellationToken token)
    {
        _logger.LogWarning("REPORT EVENT FIRED for {Client}",
            evt.Client?.CurrentAlias?.Name ?? "UNKNOWN");

        if (evt.Client == null) return;

        if (evt.Penalty.Type != EFPenalty.PenaltyType.Report)
            return;

        var server = evt.Client.CurrentServer;
        if (server == null) return;

        var game = server.GameCode.ToString().ToUpperInvariant();
        bool isT6 = game == "T6";
        bool isT5 = game == "T5";
        if (!isT6 && !isT5) return;

        string demoFolder = isT6 ? _config.T6DemoPath : _config.T5DemoPath;

        if (!Directory.Exists(demoFolder))
        {
            await SendNoDemoAsync(server, evt.Penalty, evt.Client, CancellationToken.None);
            return;
        }

        DateTime reportTime = DateTime.UtcNow;
        string expectedMap = server.Map?.Name ?? "";
        string expectedMode = server.Gametype ?? "";

        _logger.LogWarning("Waiting for demo | Map={Map} | Mode={Mode} | From={Time}",
            expectedMap, expectedMode, reportTime.ToString("u"));

        var found = await WaitForDemoAsync(
            demoFolder,
            isT6,
            reportTime,
            expectedMap,
            expectedMode,
            token);

        if (found == null)
        {
            await SendNoDemoAsync(server, evt.Penalty, evt.Client, CancellationToken.None);
            return;
        }

        // LONG TASKS — NO TOKEN
        await WaitForMapChangeAsync(server);
        await Task.Delay(TimeSpan.FromSeconds(_config.PostMatchDelaySeconds));

        await WaitForFileReady(found.Value.demoPath);
        if (!string.IsNullOrEmpty(found.Value.jsonPath))
            await WaitForFileReady(found.Value.jsonPath!);

        await UploadAsync(found.Value.demoPath, found.Value.jsonPath,
            server, evt.Penalty, evt.Client, expectedMap, CancellationToken.None);
    }

    // -----------------------------
    // WAIT FOR MAP CHANGE
    // -----------------------------
    private async Task WaitForMapChangeAsync(IGameServer server)
    {
        string initialMap = server.Map?.Name ?? "UNKNOWN";

        for (int i = 0; i < 180; i++)
        {
            await Task.Delay(2000);

            string currentMap = server.Map?.Name ?? "UNKNOWN";
            if (currentMap != initialMap)
            {
                _logger.LogWarning("Match ended → new map: {Map}", currentMap);
                return;
            }
        }
    }

    // -----------------------------
    // WAIT FOR DEMO
    // -----------------------------
    private async Task<(string demoPath, string? jsonPath)?> WaitForDemoAsync(
        string folder,
        bool isT6,
        DateTime reportTimeUtc,
        string expectedMap,
        string expectedMode,
        CancellationToken token)
    {
        var expire = DateTime.UtcNow.AddMinutes(_config.MaxWaitMinutes);

        while (DateTime.UtcNow < expire && !token.IsCancellationRequested)
        {
            var found = FindDemo(folder, isT6, reportTimeUtc, expectedMap, expectedMode);

            if (found.demoPath != null)
                return found;

            await Task.Delay(TimeSpan.FromSeconds(_config.RetryIntervalSeconds), token);
        }

        return null;
    }

    // -----------------------------
    // DEMO FILTER LOGIC
    // -----------------------------
    private (string demoPath, string? jsonPath) FindDemo(
        string folder,
        bool isT6,
        DateTime reportTime,
        string expectedMap,
        string expectedMode)
    {
        var files = Directory.GetFiles(folder, "*.demo");

        var valid = files
            .Select(f => new FileInfo(f))
            .Select(f => new { File = f, Meta = ParseFilename(f.Name) })
            .Where(x => x.Meta != null)
            .Where(x =>
            {
                // Map must match
                if (!x.Meta!.Map.Contains(expectedMap, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Mode must match (tdm, sd, dom etc)
                if (!string.IsNullOrWhiteSpace(expectedMode) &&
                    !x.Meta.Mode.Equals(expectedMode, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Time window: demo start must be BEFORE report,
                // and not older than MaxLookbackMinutes
                var delta = (reportTime - x.Meta.StartTime).TotalMinutes;
                return delta >= 0 && delta <= _config.MaxLookbackMinutes;
            })
            .OrderByDescending(x => x.File.LastWriteTimeUtc)
            .FirstOrDefault();

        if (valid == null) return (null!, null);

        string? json = null;

        if (isT6)
        {
            var j = Path.ChangeExtension(valid.File.FullName, ".json");
            if (File.Exists(j))
                json = j;
        }

        _logger.LogWarning("Demo selected -> {File}", valid.File.FullName);
        return (valid.File.FullName, json);
    }

    // -----------------------------
    // FILENAME PARSER (FIXED)
    // -----------------------------
    private DemoFileMeta? ParseFilename(string name)
    {
        try
        {
            // Examples:
            //  tdm_mp_nuketown_2020_12_10_2025_4_4.demo
            //  sd_mp_firingrange_12_10_2025_3_50.demo
            //
            //  [0] = mode       -> tdm / sd
            //  [1..N-6] = map   -> mp_nuketown_2020 / mp_firingrange
            //  [N-5] = month
            //  [N-4] = day
            //  [N-3] = year
            //  [N-2] = hour (24h)
            //  [N-1] = minute
            var n = Path.GetFileNameWithoutExtension(name);
            var parts = n.Split('_');

            if (parts.Length < 7)
                return null;

            int len = parts.Length;

            var mode = parts[0];
            // join everything between index 1 and the last 5 numeric parts
            var map = string.Join("_", parts.Skip(1).Take(len - 6));

            var month = int.Parse(parts[len - 5]);
            var day = int.Parse(parts[len - 4]);
            var year = int.Parse(parts[len - 3]);
            var hour = int.Parse(parts[len - 2]);
            var minute = int.Parse(parts[len - 1]);

            var start = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

            return new DemoFileMeta
            {
                Mode = mode,
                Map = map,
                StartTime = start
            };
        }
        catch
        {
            return null;
        }
    }

    // -----------------------------
    // FILE UNLOCK
    // -----------------------------
    private async Task WaitForFileReady(string path)
    {
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(2000);
            try
            {
                using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException) { }
        }
    }

    // -----------------------------
    // UPLOAD TO DISCORD 
    // -----------------------------
    private async Task UploadAsync(
        string demoPath,
        string? jsonPath,
        IGameServer server,
        EFPenalty penalty,
        EFClient target,
        string mapAtReport,
        CancellationToken _)
    {
        try
        {
            string temp = Path.Combine(Path.GetTempPath(), Path.GetFileName(demoPath));
            File.Copy(demoPath, temp, true);

            var embed = BuildEmbed(server, penalty, target, true, mapAtReport);
            var payload = new { embeds = new[] { embed } };

            using var form = new MultipartFormDataContent();

            // ✅ PAYLOAD FIRST (forces embed first)
            form.Add(
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                "payload_json"
            );

            // ✅ FILE #1 (demo)
            var demoStream = File.OpenRead(temp);
            var demoContent = new StreamContent(demoStream);
            demoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(demoContent, "files[0]", Path.GetFileName(temp));

            // ✅ FILE #2 (json metadata - optional)
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                var js = File.OpenRead(jsonPath);
                var jc = new StreamContent(js);
                jc.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                form.Add(jc, "files[1]", Path.GetFileName(jsonPath));
            }

            var response = await _http.PostAsync(_config.Webhook, form, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogWarning("Discord response → {Status} | {Body}", response.StatusCode, body);

            File.Delete(temp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadAsync failed.");
        }
    }


    // -----------------------------
    // NO DEMO
    // -----------------------------
    private async Task SendNoDemoAsync(
        IGameServer server,
        EFPenalty penalty,
        EFClient target,
        CancellationToken _)
    {
        string mapAtReport = server.Map?.Name ?? "Unknown";
        var embed = BuildEmbed(server, penalty, target, false, mapAtReport);
        var payload = new { embeds = new[] { embed } };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        await _http.PostAsync(_config.Webhook, content, CancellationToken.None);
    }

    // -----------------------------
    // DISCORD EMBED MESSAGE
    // -----------------------------
    private object BuildEmbed(
        IGameServer server,
        EFPenalty penalty,
        EFClient target,
        bool hasDemo,
        string mapAtReport)
    {
        string reporter = penalty.Punisher?.CurrentAlias?.Name.StripColors() ?? "UNKNOWN";
        string suspect = target.CurrentAlias?.Name.StripColors() ?? "UNKNOWN";
        string map = string.IsNullOrWhiteSpace(mapAtReport) ? "Unknown" : mapAtReport;
        string game = server.GameCode.ToString();
        string guid = target.NetworkId.ToString();

        string baseUrl = _appConfig.ManualWebfrontUrl?.TrimEnd('/') ?? "";
        string profileUrl = !string.IsNullOrWhiteSpace(baseUrl)
            ? $"{baseUrl}/Client/Profile/{target.ClientId}"
            : "Unavailable";

        string version = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version?
            .ToString() ?? "DEV";

        return new
        {
            title = "🎬 Demo Uploaded for New Report",
            description =
                $"**Target Player:** `{suspect}`\n" +
                $"**Reported By:** `{reporter}`",

            timestamp = DateTime.UtcNow.ToString("o"),

            color = 3066993,

            footer = new
            {
                text = $"DemosToDiscord v{version}"
            },

            fields = new[]
            {
            new { name = "🖥 Server", value = $"**{server.ServerName.StripColors()}**", inline = false },

            new { name = "🎮 Game", value = game, inline = true },
            new { name = "🗺 Map", value = map, inline = true },

            new { name = "👤 Player GUID", value = $"`{guid}`", inline = false },

            new { name = "🔗 Player Profile", value = $"[View Web Profile]({profileUrl})", inline = false },

            new
            {
                name = "📎 Demo Status",
                value = hasDemo ? "✅ Demo file successfully attached" : "❌ No demo file found",
                inline = false
            }
        }
        };
    }


    private class DemoFileMeta
    {
        public string Mode { get; set; } = "";
        public string Map { get; set; } = "";
        public DateTime StartTime { get; set; }
    }
}
