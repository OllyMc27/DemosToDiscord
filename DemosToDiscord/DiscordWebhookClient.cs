using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SharedLibraryCore.Configuration;

namespace DemosToDiscord;

public sealed class DiscordWebhookClient : IDisposable
{
    private readonly ApplicationConfiguration _applicationConfiguration;
    private readonly DemosToDiscordConfig _config;
    private readonly ILogger<DiscordWebhookClient> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DiscordWebhookClient(
        ApplicationConfiguration applicationConfiguration,
        DemosToDiscordConfig config,
        ILogger<DiscordWebhookClient> logger)
    {
        _applicationConfiguration = applicationConfiguration;
        _config = config;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DemosToDiscord/2.0");
    }

    public async Task<DiscordMessageReceipt> SendCaseAsync(
        EvidenceCase evidenceCase,
        string webhook,
        string? demoPath,
        string? jsonPath,
        DiscordDeliveryOptions delivery,
        CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        var hasDemo = demoPath is not null;
        var payload = BuildPayload(evidenceCase, demoPath, jsonPath, delivery, hasDemo);
        form.Add(new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json"),
            "payload_json");

        var streams = new List<Stream>();
        try
        {
            var fileIndex = 0;
            if (demoPath is not null)
            {
                AddFile(form, streams, demoPath, UploadName(evidenceCase, demoPath), fileIndex++);
            }

            if (jsonPath is not null && File.Exists(jsonPath))
                AddFile(form, streams, jsonPath, UploadName(evidenceCase, jsonPath), fileIndex);

            using var response = await _http.PostAsync(WithWait(webhook), form, token);
            var responseJson = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Discord webhook returned {(int)response.StatusCode}: {responseJson}");

            var message = JsonSerializer.Deserialize<DiscordMessageDto>(responseJson, _jsonOptions)
                          ?? throw new InvalidOperationException("Discord returned an empty webhook message.");
            return ToReceipt(message);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    public async Task UpdateCaseAsync(EvidenceCase evidenceCase, string webhook, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(evidenceCase.DiscordMessageId))
            return;

        var uri = BuildMessageUri(webhook, evidenceCase.DiscordMessageId);
        using var getResponse = await _http.GetAsync(uri, token);
        var existingJson = await getResponse.Content.ReadAsStringAsync(token);
        if (!getResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Discord message lookup returned {(int)getResponse.StatusCode}: {existingJson}");

        var existing = JsonSerializer.Deserialize<DiscordMessageDto>(existingJson, _jsonOptions)
                       ?? throw new InvalidOperationException("Discord returned an empty webhook message.");
        var payload = new
        {
            content = MessageContent(evidenceCase, evidenceCase.Status == EvidenceCaseStatus.Uploaded),
            allowed_mentions = new { parse = Array.Empty<string>(), roles = Array.Empty<string>() },
            embeds = new[] { BuildEmbed(evidenceCase, null, null) },
            attachments = existing.Attachments.Select(item => new { id = item.Id, filename = item.FileName }).ToArray()
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri) { Content = content };
        using var response = await _http.SendAsync(request, token);
        var responseJson = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Discord webhook update returned {(int)response.StatusCode}: {responseJson}");
    }

    public async Task<IReadOnlyList<DiscordAttachment>> GetAttachmentsAsync(
        string webhook,
        string messageId,
        CancellationToken token)
    {
        var uri = BuildMessageUri(webhook, messageId);
        using var response = await _http.GetAsync(uri, token);
        var responseJson = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[DemosToDiscord] Could not refresh Discord attachment URLs: {Status}", response.StatusCode);
            return [];
        }

        var message = JsonSerializer.Deserialize<DiscordMessageDto>(responseJson, _jsonOptions);
        return message is null ? [] : ToReceipt(message).Attachments;
    }

    public async Task TestAsync(string webhook, CancellationToken token)
    {
        var payload = new
        {
            content = "✅ **DemosToDiscord v2** webhook test successful.",
            allowed_mentions = new { parse = Array.Empty<string>() }
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8,
            "application/json");
        using var response = await _http.PostAsync(WithWait(webhook), content, token);
        response.EnsureSuccessStatusCode();
    }

    internal object BuildEmbed(EvidenceCase evidenceCase, string? demoPath, string? jsonPath)
    {
        var baseUrl = _applicationConfiguration.Webfront?.ManualUrl?.TrimEnd('/') ?? string.Empty;
        var profileUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl}/Client/Profile/{evidenceCase.TargetClientId}";
        var reviewUrl = BuildReviewUrl(baseUrl, evidenceCase.Id);
        var fields = new List<object>
        {
            new
            {
                name = "Player",
                value = Limit($"**{DiscordText(evidenceCase.TargetName)}** (`#{evidenceCase.TargetClientId}`)\nGUID `{evidenceCase.TargetNetworkId}`", 1024),
                inline = true
            },
            new
            {
                name = "Match",
                value = Limit($"`{DiscordInlineCode(evidenceCase.Game)}` • `{DiscordInlineCode(evidenceCase.Map)}` • `{DiscordInlineCode(evidenceCase.Mode)}`", 1024),
                inline = true
            },
            new
            {
                name = "Server",
                value = Limit($"**{DiscordText(evidenceCase.ServerName)}**\n`{DiscordInlineCode(evidenceCase.ServerId)}`", 1024),
                inline = false
            }
        };

        var evidenceSources = new List<string>();
        if (evidenceCase.Reports.Count > 0)
            evidenceSources.Add($"{evidenceCase.Reports.Count} player report{(evidenceCase.Reports.Count == 1 ? string.Empty : "s")}");
        if (evidenceCase.AntiCheat is not null)
            evidenceSources.Add("automated anti-cheat ban");
        if (evidenceCase.ManualBanObserved)
            evidenceSources.Add("manual ban");
        fields.Add(new
        {
            name = "Evidence source",
            value = evidenceSources.Count == 0 ? "Unknown" : string.Join(" + ", evidenceSources),
            inline = true
        });

        var confidence = EvidenceAssessment.Confidence(evidenceCase);
        fields.Add(new
        {
            name = "Evidence confidence",
            value = $"**{DiscordText(confidence.Label)}**\n{DiscordText(confidence.Detail)}",
            inline = true
        });

        fields.Add(new
        {
            name = "Review status",
            value = ReviewSummary(evidenceCase),
            inline = true
        });

        fields.Add(new
        {
            name = "Demo file",
            value = DemoSummary(evidenceCase, demoPath, jsonPath),
            inline = true
        });

        if (evidenceCase.Reports.Count > 0)
        {
            var reports = string.Join("\n", evidenceCase.Reports.Take(5).Select(item =>
                $"• **{DiscordText(item.ReporterName)}:** {DiscordText(item.Reason)}"));
            if (evidenceCase.Reports.Count > 5)
                reports += $"\n*+{evidenceCase.Reports.Count - 5} more report(s)*";
            fields.Add(new { name = "Player reports", value = Limit(reports, 1024), inline = false });
        }

        if (evidenceCase.AntiCheat is not null)
        {
            fields.Add(new
            {
                name = "Anti-cheat detection",
                value = Limit(DiscordText(evidenceCase.AntiCheat.Detection), 1024),
                inline = false
            });
        }

        if (reviewUrl is not null || profileUrl is not null)
        {
            var links = new List<string>();
            if (reviewUrl is not null)
                links.Add($"[🔎 Review evidence case]({reviewUrl})");
            if (profileUrl is not null)
                links.Add($"[👤 Open player profile]({profileUrl})");
            fields.Add(new { name = "Admin links", value = string.Join("  •  ", links), inline = false });
        }

        var title = evidenceCase.AntiCheat is not null
            ? $"Anti-cheat evidence • {DiscordText(evidenceCase.TargetName)}"
            : $"Report evidence • {DiscordText(evidenceCase.TargetName)}";
        var embed = new Dictionary<string, object>
        {
            ["title"] = Limit(title, 256),
            ["description"] = $"Case `{evidenceCase.Id}` • captured <t:{new DateTimeOffset(evidenceCase.CreatedAtUtc).ToUnixTimeSeconds()}:R>",
            ["timestamp"] = evidenceCase.CreatedAtUtc.ToUniversalTime().ToString("O"),
            ["color"] = EmbedColor(evidenceCase),
            ["author"] = new { name = "IW4MAdmin • Demo Evidence" },
            ["footer"] = new { text = $"DemosToDiscord v{typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "2"} • {evidenceCase.Id}" },
            ["fields"] = fields
        };
        if (reviewUrl is not null)
            embed["url"] = reviewUrl;
        return embed;
    }

    private object BuildPayload(
        EvidenceCase evidenceCase,
        string? demoPath,
        string? jsonPath,
        DiscordDeliveryOptions delivery,
        bool hasDemo)
    {
        var validRole = delivery.MentionRole && IsDiscordId(delivery.RoleId) ? delivery.RoleId! : null;
        var message = MessageContent(evidenceCase, hasDemo);
        return new
        {
            content = validRole is null ? message : $"<@&{validRole}> {message}",
            allowed_mentions = new
            {
                parse = Array.Empty<string>(),
                roles = validRole is null ? Array.Empty<string>() : new[] { validRole }
            },
            embeds = new[] { BuildEmbed(evidenceCase, demoPath, jsonPath) }
        };
    }

    private static string MessageContent(EvidenceCase evidenceCase, bool hasDemo)
    {
        if (evidenceCase.ReviewDecision != EvidenceReviewDecision.Unreviewed)
            return $"✅ **Evidence review completed: {EvidenceReviewService.DecisionLabel(evidenceCase.ReviewDecision)}**";
        if (evidenceCase.AssignedToClientId is not null)
            return $"👤 **Evidence case assigned to {DiscordText(evidenceCase.AssignedToName)}**";
        if (evidenceCase.Status == EvidenceCaseStatus.DemoUnsupported)
            return "📋 **New metadata-only evidence is ready for review**";
        if (evidenceCase.AntiCheat is not null)
            return "🛡️ **New anti-cheat evidence is ready for review**";
        return hasDemo
            ? "🎬 **New report evidence is ready for review**"
            : "⚠️ **Evidence captured, but no matching demo was found**";
    }

    private static void AddFile(
        MultipartFormDataContent form,
        ICollection<Stream> streams,
        string path,
        string uploadName,
        int index)
    {
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        streams.Add(stream);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? "application/json"
                : "application/octet-stream");
        form.Add(content, $"files[{index}]", uploadName);
    }

    private string UploadName(EvidenceCase evidenceCase, string path)
    {
        if (!_config.RenameOnUpload)
            return Path.GetFileName(path);
        var target = string.Concat(evidenceCase.TargetName.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
        if (string.IsNullOrWhiteSpace(target))
            target = evidenceCase.TargetClientId.ToString();
        return $"{evidenceCase.Game}_{evidenceCase.Id}_{target}{Path.GetExtension(path)}";
    }

    private static string WithWait(string webhook)
    {
        var separator = webhook.Contains('?') ? '&' : '?';
        return webhook.Contains("wait=", StringComparison.OrdinalIgnoreCase) ? webhook : $"{webhook}{separator}wait=true";
    }

    private static string BuildMessageUri(string webhook, string messageId)
    {
        var builder = new UriBuilder(webhook) { Query = string.Empty };
        builder.Path = builder.Path.TrimEnd('/') + "/messages/" + Uri.EscapeDataString(messageId);
        return builder.Uri.ToString();
    }

    private static DiscordMessageReceipt ToReceipt(DiscordMessageDto message) => new(
        message.Id,
        message.ChannelId,
        message.GuildId,
        message.Attachments.Select(item => new DiscordAttachment(item.Id, item.FileName, item.Url, item.Size)).ToList());

    private string DemoSummary(EvidenceCase evidenceCase, string? demoPath, string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(demoPath))
        {
            if (evidenceCase.Status == EvidenceCaseStatus.DemoUnsupported)
                return Limit($"ℹ️ Demo unavailable for this server/mode\n{DiscordText(evidenceCase.DemoSupportReason)}", 1024);
            if (!string.IsNullOrWhiteSpace(evidenceCase.DemoFileName))
                return $"✅ `{DiscordInlineCode(evidenceCase.DemoFileName)}`\n{(evidenceCase.DemoFileSize is long storedSize ? FormatBytes(storedSize) : "Uploaded")}";
            return evidenceCase.Status == EvidenceCaseStatus.Failed
                ? "❌ Demo delivery failed"
                : "⚠️ No matching demo found";
        }

        var fileName = UploadName(evidenceCase, demoPath);
        var size = File.Exists(demoPath) ? FormatBytes(new FileInfo(demoPath).Length) : "unknown size";
        var metadata = !string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath)
            ? " • metadata attached"
            : string.Empty;
        return Limit($"✅ `{DiscordInlineCode(fileName)}`\n{size}{metadata}", 1024);
    }

    private static string ReviewSummary(EvidenceCase evidenceCase)
    {
        var lines = new List<string>();
        if (evidenceCase.ReviewDecision == EvidenceReviewDecision.Unreviewed)
            lines.Add("Awaiting review");
        else
            lines.Add($"**{DiscordText(EvidenceReviewService.DecisionLabel(evidenceCase.ReviewDecision))}**");
        if (evidenceCase.AssignedToClientId is not null)
            lines.Add($"Assigned to {DiscordText(evidenceCase.AssignedToName)}");
        if (evidenceCase.ReviewedByClientId is not null)
            lines.Add($"Reviewed by {DiscordText(evidenceCase.ReviewedByName)}");
        return Limit(string.Join("\n", lines), 1024);
    }

    private static int EmbedColor(EvidenceCase evidenceCase) => evidenceCase.ReviewDecision switch
    {
        EvidenceReviewDecision.CheatingActionTaken or EvidenceReviewDecision.CheatingNoAction => 15548997,
        EvidenceReviewDecision.NotCheatingNoAction => 5763719,
        EvidenceReviewDecision.NeedsMoreReview or EvidenceReviewDecision.Inconclusive => 16776960,
        _ => evidenceCase.AntiCheat is not null ? 15548997 : 5793266
    };

    private static bool IsDiscordId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(char.IsDigit) && value.Length is >= 17 and <= 20;

    private static string DiscordText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";
        return value.Replace("\\", "\\\\")
            .Replace("@", "@\u200b")
            .Replace("*", "\\*")
            .Replace("_", "\\_")
            .Replace("~", "\\~")
            .Replace("`", "\\`")
            .Replace("|", "\\|");
    }

    private static string DiscordInlineCode(string? value) =>
        (string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Replace("@", "@\u200b"))
        .Replace("`", "'")
        .Replace("\r", " ")
        .Replace("\n", " ");

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string FormatBytes(long value) => value < 1024 * 1024
        ? $"{value / 1024d:0.0} KB"
        : $"{value / 1024d / 1024d:0.0} MB";

    internal static string? BuildReviewUrl(string? baseUrl, string caseId) =>
        string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}{DemosToDiscordWebfront.CaseUrl(caseId)}";

    public void Dispose() => _http.Dispose();

    private sealed class DiscordMessageDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("channel_id")] public string? ChannelId { get; set; }
        [JsonPropertyName("guild_id")] public string? GuildId { get; set; }
        [JsonPropertyName("attachments")] public List<DiscordAttachmentDto> Attachments { get; set; } = [];
    }

    private sealed class DiscordAttachmentDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("filename")] public string FileName { get; set; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}

