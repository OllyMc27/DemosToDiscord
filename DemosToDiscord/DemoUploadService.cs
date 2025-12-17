using Data.Models;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Configuration;
using SharedLibraryCore.Database.Models;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Interfaces;
using System;
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
    private const string LogPrefix = "[DemosToDiscord]";

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
                _logger.LogWarning("{Prefix} Webhook empty — startup skipped.", LogPrefix);
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

            _logger.LogInformation(
                "{Prefix} Startup message sent → {Status}",
                LogPrefix, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Prefix} Startup message failed", LogPrefix);
        }
    }

    // -----------------------------
    // ENTRY
    // -----------------------------
    public async Task HandlePenaltyAsync(ClientPenaltyEvent evt, CancellationToken token)
    {
        _logger.LogInformation(
            "{Prefix} REPORT EVENT FIRED for {Client}",
            LogPrefix,
            evt.Client?.CurrentAlias?.Name ?? "UNKNOWN");

        if (evt.Client == null)
            return;

        if (evt.Penalty.Type != EFPenalty.PenaltyType.Report)
            return;

        var server = evt.Client.CurrentServer;
        if (server == null)
            return;

        var game = server.GameCode.ToString().ToUpperInvariant();
        bool isT6 = game == "T6";
        bool isT5 = game == "T5";
        if (!isT6 && !isT5)
            return;

        string demoFolder = isT6 ? _config.T6DemoPath : _config.T5DemoPath;

        if (!Directory.Exists(demoFolder))
        {
            _logger.LogWarning("{Prefix} Demo folder missing → {Path}", LogPrefix, demoFolder);
            await SendNoDemoAsync(server, evt.Penalty, evt.Client, CancellationToken.None);
            return;
        }

        DateTime reportTime = DateTime.Now;
        string expectedMap = server.Map?.Name ?? "";
        string expectedMode = server.Gametype ?? "";

        _logger.LogInformation(
            "{Prefix} Waiting for demo | Map={Map} | Mode={Mode} | From={Time}",
            LogPrefix, expectedMap, expectedMode, reportTime.ToString("u"));

        var found = await WaitForDemoAsync(
            demoFolder,
            isT6,
            reportTime,
            expectedMap,
            expectedMode,
            token);

        if (found == null)
        {
            _logger.LogWarning("{Prefix} No demo found within timeout window", LogPrefix);
            await SendNoDemoAsync(server, evt.Penalty, evt.Client, CancellationToken.None);
            return;
        }

        await WaitForMapChangeAsync(server);
        await Task.Delay(TimeSpan.FromSeconds(_config.PostMatchDelaySeconds));

        await WaitForFileReady(found.Value.demoPath);
        if (!string.IsNullOrEmpty(found.Value.jsonPath))
            await WaitForFileReady(found.Value.jsonPath!);

        await UploadAsync(
            found.Value.demoPath,
            found.Value.jsonPath,
            server,
            evt.Penalty,
            evt.Client,
            expectedMap,
            CancellationToken.None);
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
                _logger.LogInformation(
                    "{Prefix} Match ended → new map: {Map}",
                    LogPrefix, currentMap);
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
                if (!x.Meta!.Map.Contains(expectedMap, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(expectedMode) &&
                    !x.Meta.Mode.Equals(expectedMode, StringComparison.OrdinalIgnoreCase))
                    return false;

                var delta = (reportTime - x.Meta.StartTime).TotalMinutes;
                return delta >= 0 && delta <= _config.MaxLookbackMinutes;
            })
            .OrderByDescending(x => x.File.LastWriteTimeUtc)
            .FirstOrDefault();

        if (valid == null)
            return (null!, null);

        string? json = null;

        if (isT6)
        {
            var j = Path.ChangeExtension(valid.File.FullName, ".json");
            if (File.Exists(j))
                json = j;
        }

        _logger.LogInformation(
            "{Prefix} Demo selected → {File}",
            LogPrefix, valid.File.FullName);

        return (valid.File.FullName, json);
    }

    // -----------------------------
    // FILENAME PARSER  (FIXED / PRESENT)
    // -----------------------------
    private DemoFileMeta? ParseFilename(string name)
    {
        try
        {
            var n = Path.GetFileNameWithoutExtension(name);
            var parts = n.Split('_');

            if (parts.Length < 7)
                return null;

            int len = parts.Length;

            var mode = parts[0];
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
    // FILE UNLOCK + SIZE STABILITY
    // -----------------------------
    private async Task WaitForFileReady(string path)
    {
        const int maxAttempts = 60;
        const int delayMs = 2000;

        long lastSize = -1;

        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(delayMs);

            try
            {
                var info = new FileInfo(path);

                if (!info.Exists)
                    continue;

                if (info.Length == 0)
                    continue;

                if (info.Length != lastSize)
                {
                    lastSize = info.Length;
                    continue;
                }

                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException) { }
        }

        _logger.LogWarning("{Prefix} Timed out waiting for file → {Path}", LogPrefix, path);
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
            string tempDemoPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(demoPath));
            File.Copy(demoPath, tempDemoPath, true);

            string? tempJsonPath = null;
            if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
            {
                tempJsonPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(jsonPath));
                File.Copy(jsonPath, tempJsonPath, true);
            }

            var embed = BuildEmbed(server, penalty, target, true, mapAtReport);
            var payload = new { embeds = new[] { embed } };

            using var form = new MultipartFormDataContent();

            form.Add(
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                "payload_json"
            );

            var demoStream = File.OpenRead(tempDemoPath);
            var demoContent = new StreamContent(demoStream);
            demoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(demoContent, "files[0]", Path.GetFileName(tempDemoPath));

            if (!string.IsNullOrEmpty(tempJsonPath))
            {
                var jsonStream = File.OpenRead(tempJsonPath);
                var jsonContent = new StreamContent(jsonStream);
                jsonContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                form.Add(jsonContent, "files[1]", Path.GetFileName(tempJsonPath));
            }

            var response = await _http.PostAsync(_config.Webhook, form, CancellationToken.None);

            _logger.LogInformation(
                "{Prefix} Discord upload completed → {Status}",
                LogPrefix, response.StatusCode);

            ScheduleTempCleanup(tempDemoPath, tempJsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Prefix} UploadAsync failed", LogPrefix);
        }
    }

    // -----------------------------
    // TEMP CLEANUP (NULLABLE FIXED)
    // -----------------------------
    private void ScheduleTempCleanup(string demoPath, string? jsonPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                TryDeleteFile(demoPath);
                TryDeleteFile(jsonPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "{Prefix} Temp file cleanup failed", LogPrefix);
            }
        });
    }

    private void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "DEV";

        return new
        {
            title = "🎬 Demo Uploaded for New Report",
            description = $"**Target Player:** `{suspect}`\n**Reported By:** `{reporter}`",
            timestamp = DateTime.UtcNow.ToString("o"),
            color = 3066993,
            footer = new { text = $"DemosToDiscord v{version}" },
            fields = new[]
            {
                new { name = "🖥 Server", value = $"**{server.ServerName.StripColors()}**", inline = false },
                new { name = "🎮 Game", value = game, inline = true },
                new { name = "🗺 Map", value = map, inline = true },
                new { name = "👤 Player GUID", value = $"`{guid}`", inline = false },
                new { name = "🔗 Player Profile", value = $"[View Profile]({profileUrl})", inline = false },
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
