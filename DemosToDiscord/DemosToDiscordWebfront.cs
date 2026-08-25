using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SharedLibraryCore.Helpers;
using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

public sealed class DemosToDiscordWebfront : IDisposable
{
    public const string InteractionKey = "Webfront::Nav::Admin::DemosToDiscord";
    public const string ReviewInteractionKey = "DemosToDiscord::ReviewCase";
    private const string WideStyles = """
        <style>
          .dtd-identity-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(11rem,1fr));gap:.75rem}
          .dtd-case-row{display:grid;grid-template-columns:minmax(0,2fr) minmax(13rem,1fr) auto;gap:1rem;align-items:center}
          .dtd-detail-layout{display:grid;grid-template-columns:minmax(0,1fr) 18rem;gap:1.25rem;align-items:start}
          .dtd-evidence-grid{display:grid;grid-template-columns:minmax(0,1.15fr) minmax(16rem,.85fr);gap:1.25rem;align-items:start}
          .dtd-actions{position:sticky;top:1.5rem}
          @media (min-width:1280px){.dtd-workspace{width:min(1600px,calc(100vw - 19rem));position:relative;left:50%;transform:translateX(-50%)}}
          @media (max-width:1023px){.dtd-case-row,.dtd-detail-layout,.dtd-evidence-grid{grid-template-columns:1fr}.dtd-actions{position:static}}
          @media (max-width:767px){.dtd-overview-metric:nth-child(2n){border-right:0}.dtd-overview-metric:nth-child(-n+2){border-bottom:1px solid var(--color-line)}}
        </style>
        """;

    private readonly IInteractionRegistration _interactions;
    private readonly IConfigurationHandlerV2<DemosToDiscordConfig> _configurationHandler;
    private readonly DemosToDiscordConfig _config;
    private readonly DemoUploadService _service;
    private readonly AntiCheatMetricsService _metrics;
    private readonly DiscordWebhookClient _discord;
    private readonly EvidenceReviewService _reviewService;
    private bool _disposed;

    public DemosToDiscordWebfront(
        IInteractionRegistration interactions,
        IConfigurationHandlerV2<DemosToDiscordConfig> configurationHandler,
        DemosToDiscordConfig config,
        DemoUploadService service,
        AntiCheatMetricsService metrics,
        DiscordWebhookClient discord,
        EvidenceReviewService reviewService)
    {
        _interactions = interactions;
        _configurationHandler = configurationHandler;
        _config = config;
        _service = service;
        _metrics = metrics;
        _discord = discord;
        _reviewService = reviewService;
        _configurationHandler.Updated += OnConfigurationUpdated;
    }

    public void Register()
    {
        _interactions.UnregisterInteraction(InteractionKey);
        _interactions.UnregisterInteraction(ReviewInteractionKey);

        if (_config.EnableWebfrontDashboard)
        {
            _interactions.RegisterInteraction(InteractionKey, (_, _, _) =>
            {
                var interaction = new InteractionData
                {
                    Enabled = true,
                    Name = "Demo Evidence",
                    Description = "Review reports, player metrics, anti-cheat evidence and demos",
                    DisplayMeta = "ph-film-strip",
                    InteractionId = InteractionKey,
                    MinimumPermission = _config.WebfrontMinimumPermission,
                    InteractionType = InteractionType.TemplateContent,
                    Source = "DemosToDiscord",
                    PermissionEntity = "Interaction",
                    PermissionAccess = "Read",
                    Action = (originId, _, _, meta, token) => RenderAsync(originId, meta, token)
                };
                return Task.FromResult<IInteractionData>(interaction);
            });
        }

        _interactions.RegisterInteraction(ReviewInteractionKey, (_, _, _) =>
        {
            var interaction = new InteractionData
            {
                Enabled = true,
                Name = "Review evidence case",
                Description = "Record an evidence decision or clear its reports",
                DisplayMeta = "ph-check-circle",
                InteractionId = ReviewInteractionKey,
                MinimumPermission = _config.WebfrontMinimumPermission,
                InteractionType = InteractionType.ActionButton,
                Source = "DemosToDiscord",
                PermissionEntity = "Interaction",
                PermissionAccess = "Write",
                Action = (originId, targetId, _, meta, token) =>
                    _reviewService.ExecuteAsync(originId, targetId, meta, token)
            };
            return Task.FromResult<IInteractionData>(interaction);
        });
    }

    private async Task<string> RenderAsync(int originId, IDictionary<string, string> meta, CancellationToken token)
    {
        if (meta.TryGetValue("case", out var caseId) && !string.IsNullOrWhiteSpace(caseId))
            return await RenderCaseAsync(caseId, token);
        return RenderDashboard(_service.GetSnapshot(), originId, meta);
    }

    private string RenderDashboard(EvidenceStoreSnapshot snapshot, int originId, IDictionary<string, string> meta)
    {
        meta.TryGetValue("view", out var requestedView);
        var view = NormalizeView(requestedView);
        var awaitingReview = snapshot.Cases.Count(item => item.ReviewDecision == EvidenceReviewDecision.Unreviewed);
        var confirmedCheating = snapshot.Cases.Count(item => item.ReviewDecision is
            EvidenceReviewDecision.CheatingActionTaken or EvidenceReviewDecision.CheatingNoAction);
        var cleared = snapshot.Cases.Count(item => item.ReviewDecision == EvidenceReviewDecision.NotCheatingNoAction);
        var followUp = snapshot.Cases.Count(item => item.ReviewDecision is
            EvidenceReviewDecision.NeedsMoreReview or EvidenceReviewDecision.Inconclusive);
        var cases = FilterCases(snapshot.Cases, view, originId, meta).Take(100).ToList();
        var builder = new StringBuilder(WideStyles);
        builder.Append("<div class=\"dtd-workspace space-y-5\">")
            .Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\">")
            .Append("<div class=\"flex flex-col gap-4 border-b border-line px-5 pt-5 pb-8 md:flex-row md:items-center md:justify-between md:px-6\"><div><h3 class=\"text-lg font-semibold text-foreground\">Evidence queue</h3><p class=\"mt-1 text-sm text-muted\">Reports and automated detections grouped by player and match.</p></div>")
            .Append($"<a data-enhance-nav=\"false\" class=\"inline-flex items-center justify-center gap-2 rounded-lg border border-line bg-surface-alt px-3 py-2 text-sm font-medium text-foreground transition-colors hover:bg-surface-hover\" href=\"{OverviewUrl(view)}\"><i class=\"ph ph-arrow-clockwise\"></i>Refresh</a></div>")
            .Append("<div class=\"grid grid-cols-2 border-b border-line md:grid-cols-4\">")
            .Append(OverviewMetric("Awaiting", awaitingReview, "ph-hourglass", "text-amber-400", "awaiting", view))
            .Append(OverviewMetric("Confirmed", confirmedCheating, "ph-shield-warning", "text-red-400", "cheating", view))
            .Append(OverviewMetric("Cleared", cleared, "ph-check-circle", "text-emerald-400", "cleared", view))
            .Append(OverviewMetric("Follow-up", followUp, "ph-magnifying-glass", "text-primary", "followup", view))
            .Append("</div>")
            .Append("<nav class=\"flex gap-1 overflow-x-auto border-b border-line bg-surface-alt/30 px-4 py-2\" aria-label=\"Evidence filters\">")
            .Append(FilterLink("All cases", "all", view, snapshot.Cases.Count))
            .Append(FilterLink("Awaiting", "awaiting", view, awaitingReview))
            .Append(FilterLink("Processing", "processing", view, snapshot.Queued))
            .Append(FilterLink("Follow-up", "followup", view, followUp))
            .Append(FilterLink("Cheating", "cheating", view, confirmedCheating))
            .Append(FilterLink("Cleared", "cleared", view, cleared))
            .Append(FilterLink("Failed", "failed", view, snapshot.Failed))
            .Append(FilterLink("Unassigned", "unassigned", view, snapshot.Cases.Count(item => item.AssignedToClientId is null)))
            .Append(FilterLink("Assigned to me", "mine", view, snapshot.Cases.Count(item => item.AssignedToClientId == originId)))
            .Append("</nav>")
            .Append(SearchFilters(meta, view, snapshot.Cases))
            .Append("<div class=\"divide-y divide-line\">");

        if (cases.Count == 0)
        {
            builder.Append("<div class=\"flex flex-col items-center justify-center px-6 py-16 text-center\"><div class=\"mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-surface-alt\"><i class=\"ph ph-check-circle text-3xl text-muted\"></i></div><h4 class=\"font-medium text-foreground\">No cases in this view</h4><p class=\"mt-1 text-sm text-muted\">Try another filter or wait for new evidence.</p></div>");
        }
        else
        {
            foreach (var item in cases)
                builder.Append(OverviewCaseRow(item));
        }

        builder.Append("</div><div class=\"flex flex-col gap-2 border-t border-line bg-surface-alt/20 px-5 py-3 text-xs text-muted md:flex-row md:items-center md:justify-between\">")
            .Append($"<span>Showing {cases.Count:N0} of {snapshot.Cases.Count:N0} retained cases</span><span>{snapshot.Uploaded:N0} uploaded · {snapshot.Unsupported:N0} metadata-only · {snapshot.NoDemo:N0} demo missing · {snapshot.Failed:N0} failed</span></div></section>")
            .Append("<section class=\"rounded-xl border border-line bg-surface px-5 py-4 shadow-sm\"><div class=\"flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between\"><div class=\"flex items-center gap-3\"><div class=\"flex h-10 w-10 items-center justify-center rounded-lg border border-line bg-surface-alt\"><i class=\"ph ph-gear text-lg text-primary\"></i></div><div><h3 class=\"font-medium text-foreground\">Evidence collection</h3>")
            .Append($"<p class=\"text-sm text-muted\">Reports {(_config.UploadOnReports ? "enabled" : "disabled")} · automated bans {Encode(_config.UploadOnAutomatedBans ? string.Join(", ", _config.AutomatedBanGames) : "disabled")} · {_config.CaseRetentionDays} day retention</p></div></div>")
            .Append("<a href=\"/configuration\" class=\"inline-flex items-center gap-2 text-sm font-medium text-primary hover:underline\"><i class=\"ph ph-sliders-horizontal\"></i>Open configuration</a></div></section></div>");
        return builder.ToString();
    }

    private async Task<string> RenderCaseAsync(string caseId, CancellationToken token)
    {
        var evidenceCase = _service.GetCase(caseId);
        if (evidenceCase is null)
            return "<div class=\"rounded-xl border border-red-500/30 bg-red-500/10 p-5 text-red-300\">Evidence case not found.</div>";

        var metricsTask = _metrics.GetAsync(evidenceCase, token);
        Task<IReadOnlyList<DiscordAttachment>> attachmentsTask =
            Task.FromResult<IReadOnlyList<DiscordAttachment>>([]);
        if (!string.IsNullOrWhiteSpace(evidenceCase.DiscordMessageId))
        {
            var webhook = _service.ResolveWebhook(evidenceCase);
            if (!string.IsNullOrWhiteSpace(webhook))
                attachmentsTask = _discord.GetAttachmentsAsync(webhook, evidenceCase.DiscordMessageId, token);
        }

        await Task.WhenAll(metricsTask, attachmentsTask);
        var metrics = await metricsTask;
        var attachments = await attachmentsTask;
        var orderedCases = _service.GetSnapshot().Cases;
        var playerCases = orderedCases
            .Where(item => item.TargetClientId == evidenceCase.TargetClientId &&
                           !item.Id.Equals(evidenceCase.Id, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
        var caseIndex = orderedCases.ToList().FindIndex(item => item.Id.Equals(evidenceCase.Id, StringComparison.OrdinalIgnoreCase));
        var newerCase = caseIndex > 0 ? orderedCases[caseIndex - 1] : null;
        var olderCase = caseIndex >= 0 && caseIndex < orderedCases.Count - 1 ? orderedCases[caseIndex + 1] : null;

        var builder = new StringBuilder(WideStyles);
        builder.Append("<div class=\"dtd-workspace space-y-5\"><div class=\"flex flex-wrap items-center justify-between gap-3\">")
            .Append($"<a data-enhance-nav=\"false\" class=\"inline-flex items-center gap-2 text-sm font-medium text-primary hover:underline\" href=\"{OverviewUrl("all")}\"><i class=\"ph ph-arrow-left\"></i>Evidence queue</a>")
            .Append("<div class=\"flex items-center gap-2\">")
            .Append(CasePager("Newer", "ph-caret-left", newerCase))
            .Append(CasePager("Older", "ph-caret-right", olderCase))
            .Append("</div></div>")
            .Append(HeroSection(evidenceCase))
            .Append("<div class=\"dtd-detail-layout\"><main class=\"min-w-0 space-y-5\">")
            .Append(ReviewBanner(evidenceCase))
            .Append(ReviewSummarySection(evidenceCase))
            .Append("<section id=\"evidence\" class=\"scroll-mt-4 overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"flex items-center justify-between gap-3 border-b border-line px-5 py-4\"><div><h3 class=\"font-semibold text-foreground\">Demo evidence</h3><p class=\"mt-0.5 text-sm text-muted\">Original match file and Discord delivery details.</p></div><i class=\"ph ph-film-strip text-2xl text-primary\"></i></div><div class=\"p-5\"><div class=\"dtd-evidence-grid\"><div class=\"min-w-0\">");

        if (attachments.Count == 0)
        {
            builder.Append("<div class=\"flex min-h-24 items-center justify-center rounded-lg border border-dashed border-line bg-surface-alt/20 p-4 text-center text-sm text-muted\">No downloadable Discord attachment is currently available.</div>");
        }
        else
        {
            foreach (var attachment in attachments)
            {
                builder.Append($"<a class=\"mb-2 flex min-w-0 items-center gap-3 rounded-lg border border-primary/30 bg-primary/10 p-3 text-primary transition-colors hover:bg-primary/20\" href=\"{Encode(attachment.Url)}\" target=\"_blank\" rel=\"noopener noreferrer\"><i class=\"ph ph-download-simple shrink-0 text-xl\"></i><span class=\"min-w-0 flex-1 truncate font-medium\" title=\"{Encode(attachment.FileName)}\">{Encode(attachment.FileName)}</span><span class=\"shrink-0 text-xs\">{Encode(FormatBytes(attachment.Size))}</span></a>");
            }
        }

        var discordUrl = DiscordMessageUrl(evidenceCase);
        if (discordUrl is not null)
            builder.Append($"<a class=\"mt-3 inline-flex items-center gap-2 text-sm font-medium text-primary hover:underline\" href=\"{Encode(discordUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\"><i class=\"ph ph-discord-logo\"></i>Open Discord message</a>");
        builder.Append("</div><dl class=\"grid min-w-0 grid-cols-1 gap-3 sm:grid-cols-2\">");
        if (!string.IsNullOrWhiteSpace(evidenceCase.DemoFileName))
        {
            builder.Append(InfoBlock("Source file", evidenceCase.DemoFileName, "ph-file"))
                .Append(InfoBlock("File size", evidenceCase.DemoFileSize is null ? "Unknown" : FormatBytes(evidenceCase.DemoFileSize.Value), "ph-hard-drive"))
                .Append(InfoBlock("Match started", evidenceCase.DemoStartedAtUtc?.ToString("u") ?? "Unknown", "ph-clock"))
                .Append(InfoBlock("Uploaded", evidenceCase.UploadedAtUtc?.ToString("u") ?? "Not uploaded", "ph-cloud-arrow-up"));
        }
        else
        {
            builder.Append(InfoBlock("Demo status", StatusLabel(evidenceCase.Status), "ph-info"));
            if (!string.IsNullOrWhiteSpace(evidenceCase.DemoSupportReason))
                builder.Append(InfoBlock("Capability", evidenceCase.DemoSupportReason, "ph-info"));
        }
        builder.Append("</dl></div>");
        if (!string.IsNullOrWhiteSpace(evidenceCase.LastError))
            builder.Append($"<div class=\"mt-4 rounded-lg border border-red-500/30 bg-red-500/10 p-3 text-sm text-red-300\">{Encode(evidenceCase.LastError)}</div>");
        builder.Append("</div></section>");

        builder.Append(PlayerMetricsSection(metrics.PlayerMetrics));
        builder.Append(ReportsSection(evidenceCase));
        builder.Append(AntiCheatSection(evidenceCase, metrics));
        builder.Append(PlayerHistorySection(playerCases));
        builder.Append(AuditHistorySection(evidenceCase));
        builder.Append("</main>").Append(ActionsSection(evidenceCase)).Append("</div></div>");
        return builder.ToString();
    }

    private static string ReviewBanner(EvidenceCase item)
    {
        if (item.ReviewDecision == EvidenceReviewDecision.Unreviewed)
            return "<div class=\"flex items-center gap-3 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-amber-300\"><i class=\"ph ph-warning-circle text-xl\"></i><div><div class=\"font-semibold\">Awaiting administrator review</div><div class=\"text-sm opacity-80\">Inspect the demo, reports and metrics before recording a decision.</div></div></div>";

        var css = item.ReviewDecision switch
        {
            EvidenceReviewDecision.CheatingActionTaken or EvidenceReviewDecision.CheatingNoAction =>
                "border-red-500/30 bg-red-500/10 text-red-300",
            EvidenceReviewDecision.NotCheatingNoAction =>
                "border-emerald-500/30 bg-emerald-500/10 text-emerald-300",
            _ => "border-primary/30 bg-primary/10 text-primary"
        };
        return $"<div class=\"flex items-center gap-3 rounded-xl border px-4 py-3 {css}\"><i class=\"ph ph-check-circle text-xl\"></i><div><div class=\"font-semibold\">{Encode(TitleCase(EvidenceReviewService.DecisionLabel(item.ReviewDecision)))}</div><div class=\"text-sm opacity-80\">Reviewed by {Encode(item.ReviewedByName ?? "an administrator")} · {Encode(item.ReviewedAtUtc?.ToString("u") ?? string.Empty)}</div></div></div>";
    }

    private static string HeroSection(EvidenceCase item)
    {
        var confidence = EvidenceAssessment.Confidence(item);
        var initial = string.IsNullOrWhiteSpace(item.TargetName)
            ? "?"
            : item.TargetName.Trim()[0].ToString().ToUpperInvariant();
        var profileUrl = $"/client/{item.TargetClientId}";
        return $"""
            <section class="overflow-hidden rounded-xl border border-line bg-surface shadow-sm">
              <div class="flex flex-col gap-5 border-b border-line bg-surface-alt/20 p-5 md:flex-row md:items-center md:px-6">
                <a href="{profileUrl}" class="flex h-16 w-16 shrink-0 items-center justify-center rounded-xl border border-line bg-surface-alt text-2xl font-bold text-muted shadow-sm">{Encode(initial)}</a>
                <div class="min-w-0 flex-1">
                  <div class="flex flex-wrap items-center gap-2">
                    <a href="{profileUrl}" class="min-w-0 break-words text-2xl font-bold text-foreground transition-colors hover:text-primary">{Encode(item.TargetName)}</a>
                    <span class="rounded border border-primary/30 bg-primary/10 px-2 py-0.5 text-xs font-semibold text-primary">{Encode(item.Game)}</span>
                    {StatusBadge(item.Status)}
                    {ReviewBadge(item.ReviewDecision)}
                  </div>
                  <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
                    <span><i class="ph ph-identification-card mr-1"></i>Client #{item.TargetClientId}</span>
                    <span class="break-all"><i class="ph ph-fingerprint mr-1"></i>{Encode(item.TargetNetworkId)}</span>
                    <span><i class="ph ph-hash mr-1"></i>{Encode(item.Id)}</span>
                  </div>
                </div>
                <a href="{profileUrl}" class="inline-flex shrink-0 items-center justify-center gap-2 rounded-lg border border-line bg-surface px-3 py-2 text-sm font-medium text-foreground transition-colors hover:bg-surface-hover"><i class="ph ph-user"></i>Open profile</a>
              </div>
              <dl class="dtd-identity-grid p-5 md:px-6">
                {InfoBlock("Server", item.ServerName, "ph-hard-drives")}
                {InfoBlock("Endpoint", item.ServerId, "ph-plugs-connected")}
                {InfoBlock("Map / mode", $"{item.Map} / {item.Mode}", "ph-map-trifold")}
                {InfoBlock("Captured", item.CreatedAtUtc.ToString("u"), "ph-clock")}
                {InfoBlock("Evidence", string.Join(", ", item.TriggerTypes), "ph-files")}
                {InfoBlock("Confidence", $"{confidence.Label} — {confidence.Detail}", "ph-shield-check")}
                {InfoBlock("Last updated", item.UpdatedAtUtc.ToString("u"), "ph-arrow-clockwise")}
              </dl>
            </section>
            """;
    }

    private static string ReviewSummarySection(EvidenceCase item)
    {
        var reviewer = item.ReviewedByClientId is > 0
            ? $"<a class=\"text-primary hover:underline\" href=\"/client/{item.ReviewedByClientId}\">{Encode(item.ReviewedByName)}</a>"
            : Encode(item.ReviewedByName ?? "Not reviewed");
        var notes = string.IsNullOrWhiteSpace(item.ReviewNotes)
            ? "<span class=\"text-muted\">No review notes recorded.</span>"
            : Encode(item.ReviewNotes);
        var cleared = item.ReportsClearedAtUtc is null
            ? "Active report state unchanged"
            : $"{item.ReportsClearedCount} report(s) cleared at {item.ReportsClearedAtUtc:u}";
        var assignment = item.AssignedToClientId is null
            ? "Unassigned"
            : $"{item.AssignedToName} since {item.AssignedAtUtc:u}";
        return $"""
            <section class="rounded-xl border border-line bg-surface p-5 shadow-sm">
              <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
                <div><h3 class="font-semibold text-foreground">Review decision</h3><p class="text-sm text-muted">The current administrative assessment for this evidence.</p></div>
                {ReviewBadge(item.ReviewDecision)}
              </div>
              <dl class="grid grid-cols-1 gap-3 md:grid-cols-4">
                <div class="min-w-0 rounded-lg border border-line bg-surface-alt/20 p-3"><dt class="text-xs uppercase tracking-wide text-muted">Reviewed by</dt><dd class="mt-1 break-words text-sm font-medium text-foreground">{reviewer}</dd></div>
                {InfoBlock("Reviewed", item.ReviewedAtUtc?.ToString("u") ?? "Not reviewed", "ph-calendar-check")}
                {InfoBlock("Reports", cleared, "ph-flag")}
                {InfoBlock("Assigned", assignment, "ph-user-focus")}
              </dl>
              <div class="mt-3 rounded-lg border border-line bg-surface-alt/20 p-3 text-sm"><div class="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">Notes</div><div class="whitespace-pre-line break-words text-foreground">{notes}</div></div>
            </section>
            """;
    }

    private static string PlayerMetricsSection(PlayerEvidenceMetrics? metrics)
    {
        var builder = new StringBuilder("<section id=\"metrics\" class=\"scroll-mt-4 rounded-xl border border-line bg-surface p-5 shadow-sm\"><div class=\"mb-5 flex items-center justify-between gap-3\"><div><h3 class=\"font-semibold text-foreground\">Player metrics</h3><p class=\"text-sm text-muted\">Aggregate statistics stored by IW4MAdmin for this player on the evidence server.</p></div><i class=\"ph ph-chart-line-up text-2xl text-emerald-400\"></i></div>");
        if (metrics is null)
            return builder.Append("<div class=\"rounded-lg border border-line bg-surface/30 p-4 text-sm text-muted\">No stored statistics were found for this player and server.</div></section>").ToString();

        builder.Append("<h4 class=\"mb-3 text-xs font-bold uppercase tracking-wider text-muted\">Game statistics</h4><div class=\"grid grid-cols-2 md:grid-cols-3 gap-3 mb-6\">")
            .Append(MetricTile("Total kills", metrics.Kills.ToString("N0"), "ph-crosshair", "text-primary"))
            .Append(MetricTile("Total deaths", metrics.Deaths.ToString("N0"), "ph-skull", "text-red-400"))
            .Append(MetricTile("Kills / death", metrics.KillDeathRatio.ToString("0.00"), "ph-divide", "text-amber-400"))
            .Append(MetricTile("Performance", metrics.Performance.ToString("0.00"), "ph-gauge", "text-emerald-400"))
            .Append(MetricTile("Score / minute", metrics.ScorePerMinute.ToString("0.0"), "ph-timer", "text-primary"))
            .Append(MetricTile("Play time", FormatDuration(metrics.TimePlayedSeconds), "ph-clock", "text-muted"))
            .Append("</div><h4 class=\"mb-3 text-xs font-bold uppercase tracking-wider text-muted\">Anti-cheat metrics</h4><div class=\"grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-3\">")
            .Append(MetricTile("Chest hits", $"{metrics.ChestHitPercent:0.0}%", "ph-target", "text-primary"))
            .Append(MetricTile("Abdomen hits", $"{metrics.AbdomenHitPercent:0.0}%", "ph-target", "text-primary"))
            .Append(MetricTile("Chest / abdomen", $"{metrics.ChestAbdomenRatioPercent:0.0}%", "ph-percent", "text-amber-400"))
            .Append(MetricTile("Head hits", $"{metrics.HeadHitPercent:0.0}%", "ph-crosshair-simple", "text-red-400"))
            .Append(MetricTile("Average hit offset", $"{metrics.AverageHitOffset:0.0000}°", "ph-angle", "text-muted"))
            .Append(MetricTile("Maximum strain", metrics.MaximumStrain.ToString("0.000"), "ph-wave-sine", "text-red-400"))
            .Append(MetricTile("Average snap", metrics.AverageSnapValue.ToString("0.000"), "ph-lightning", "text-amber-400"))
            .Append(MetricTile("Snap hits", metrics.SnapHitCount.ToString("N0"), "ph-cursor-click", "text-primary"))
            .Append("</div><p class=\"mt-4 text-xs text-muted\">Metrics provide context only. Review the demo and surrounding evidence before taking action.</p></section>");
        return builder.ToString();
    }

    private static string ActionsSection(EvidenceCase item)
    {
        var targetId = item.TargetClientId;
        var builder = new StringBuilder("<aside class=\"min-w-0\"><section class=\"dtd-actions rounded-xl border border-line bg-surface p-4 shadow-sm\"><h3 class=\"mb-3 text-xs font-bold uppercase tracking-wider text-muted\">Player actions</h3><div class=\"space-y-2\">");
        builder.Append(LinkAction("Open profile", "ph-user", $"/client/{targetId}"))
            .Append(LinkAction("View statistics", "ph-chart-bar", $"/client/{targetId}/stats"))
            .Append(NativeAction("Ban player", "ph-prohibit", "ban", targetId, "text-red-400"))
            .Append(NativeAction("Kick player", "ph-sign-out", "kick", targetId, "text-amber-400"))
            .Append(NativeAction("Flag player", "ph-flag", "flag", targetId, "text-primary"))
            .Append(NativeAction("Add admin note", "ph-note-pencil", "AddClientNote", targetId, "text-muted"))
            .Append("</div><div class=\"my-4 border-t border-line\"></div><h3 class=\"mb-3 text-xs font-bold uppercase tracking-wider text-muted\">Evidence decision</h3><div class=\"space-y-2\">")
            .Append(item.AssignedToClientId is null
                ? DynamicAction("Assign to me", "ph-user-focus", item, AssignmentInputs(item, "Assign"), "text-primary", "Assign evidence case", "Assign to me")
                : DynamicAction("Clear assignment", "ph-user-minus", item, AssignmentInputs(item, "Unassign"), "text-muted", "Clear case assignment", "Clear assignment"))
            .Append(DynamicAction("Complete review…", "ph-clipboard-text", item, BuildReviewInputs(item), "text-primary", "Complete evidence review", "Save review"))
            .Append(DynamicAction("Cheating — action taken", "ph-shield-warning", item, QuickReviewInputs(item, EvidenceReviewDecision.CheatingActionTaken, true), "text-red-400", "Confirm cheating decision", "Confirm decision"))
            .Append(DynamicAction("Not cheating — clear report", "ph-check-circle", item, QuickReviewInputs(item, EvidenceReviewDecision.NotCheatingNoAction, true), "text-emerald-400", "Clear player evidence", "Mark not cheating"))
            .Append(DynamicAction("Needs more review", "ph-magnifying-glass", item, QuickReviewInputs(item, EvidenceReviewDecision.NeedsMoreReview, false), "text-amber-400", "Queue for more review", "Save decision"))
            .Append(DynamicAction("Clear attached report(s)", "ph-eraser", item, ClearReportInputs(item), "text-muted", "Clear attached reports", "Clear reports"))
            .Append("</div><p class=\"mt-4 text-xs leading-relaxed text-muted\">Punishment buttons use IW4MAdmin's native permission checks and confirmation forms. Evidence decisions are retained with the case.</p></section></aside>");
        return builder.ToString();
    }

    private static string LinkAction(string label, string icon, string href) =>
        $"<a class=\"flex w-full items-center gap-3 rounded-lg border border-line bg-surface/30 px-3 py-2.5 text-sm text-foreground hover:bg-surface-hover\" href=\"{Encode(href)}\"><i class=\"ph {Encode(icon)} text-lg text-muted\"></i><span>{Encode(label)}</span><i class=\"ph ph-arrow-square-out ml-auto text-muted\"></i></a>";

    private static string NativeAction(string label, string icon, string action, int targetId, string color) =>
        $"<button type=\"button\" class=\"profile-action flex w-full cursor-pointer items-center gap-3 rounded-lg border border-line bg-surface/30 px-3 py-2.5 text-left text-sm text-foreground hover:bg-surface-hover\" data-action=\"{Encode(action)}\" data-action-id=\"{targetId}\"><i class=\"ph {Encode(icon)} text-lg {Encode(color)}\"></i><span>{Encode(label)}</span><i class=\"ph ph-caret-right ml-auto text-muted\"></i></button>";

    private static string DynamicAction(
        string label,
        string icon,
        EvidenceCase item,
        IReadOnlyList<Dictionary<string, object?>> inputs,
        string color,
        string modalTitle,
        string submitLabel)
    {
        var meta = new Dictionary<string, string>
        {
            ["InteractionId"] = ReviewInteractionKey,
            ["ActionButtonLabel"] = submitLabel,
            ["Name"] = modalTitle,
            ["ShouldRefresh"] = "true",
            ["Inputs"] = JsonSerializer.Serialize(inputs)
        };
        var encodedMeta = Uri.EscapeDataString(JsonSerializer.Serialize(meta));
        return $"<button type=\"button\" class=\"profile-action flex w-full cursor-pointer items-center gap-3 rounded-lg border border-line bg-surface/30 px-3 py-2.5 text-left text-sm text-foreground hover:bg-surface-hover\" data-action=\"DynamicAction\" data-action-id=\"{item.TargetClientId}\" data-action-meta=\"{Encode(encodedMeta)}\"><i class=\"ph {Encode(icon)} text-lg {Encode(color)}\"></i><span>{Encode(label)}</span><i class=\"ph ph-caret-right ml-auto text-muted\"></i></button>";
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildReviewInputs(EvidenceCase item)
    {
        var selected = item.ReviewDecision == EvidenceReviewDecision.Unreviewed
            ? EvidenceReviewDecision.NeedsMoreReview
            : item.ReviewDecision;
        var values = new Dictionary<string, string>();
        foreach (var decision in new[]
                 {
                     EvidenceReviewDecision.NeedsMoreReview,
                     EvidenceReviewDecision.CheatingActionTaken,
                     EvidenceReviewDecision.CheatingNoAction,
                     EvidenceReviewDecision.NotCheatingNoAction,
                     EvidenceReviewDecision.Inconclusive
                 })
        {
            var key = decision == selected ? $"!selected!{decision}" : decision.ToString();
            values[key] = TitleCase(EvidenceReviewService.DecisionLabel(decision));
        }

        return
        [
            Input("CaseId", "hidden", value: item.Id),
            Input("Operation", "hidden", value: "Review"),
            Input("Decision", "select", "Decision", values: values, required: true),
            Input("Notes", "textarea", "Review notes", "Optional reasoning or follow-up details", item.ReviewNotes),
            Input("ClearReports", "checkbox", "Clear active report(s) attached to this case")
        ];
    }

    private static IReadOnlyList<Dictionary<string, object?>> QuickReviewInputs(
        EvidenceCase item,
        EvidenceReviewDecision decision,
        bool clearReports) =>
    [
        Input("CaseId", "hidden", value: item.Id),
        Input("Operation", "hidden", value: "Review"),
        Input("Decision", "hidden", value: decision.ToString()),
        Input("Notes", "hidden", value: string.Empty),
        Input("ClearReports", "hidden", value: clearReports.ToString())
    ];

    private static IReadOnlyList<Dictionary<string, object?>> ClearReportInputs(EvidenceCase item) =>
    [
        Input("CaseId", "hidden", value: item.Id),
        Input("Operation", "hidden", value: "ClearReports")
    ];

    private static IReadOnlyList<Dictionary<string, object?>> AssignmentInputs(EvidenceCase item, string operation) =>
    [
        Input("CaseId", "hidden", value: item.Id),
        Input("Operation", "hidden", value: operation)
    ];

    private static Dictionary<string, object?> Input(
        string name,
        string type,
        string label = "",
        string placeholder = "",
        string? value = null,
        Dictionary<string, string>? values = null,
        bool required = false) => new()
    {
        ["Name"] = name,
        ["Label"] = label,
        ["Placeholder"] = placeholder,
        ["Type"] = type,
        ["Value"] = value,
        ["Values"] = values ?? [],
        ["Checked"] = false,
        ["Required"] = required
    };

    private static string ReportsSection(EvidenceCase evidenceCase)
    {
        var reportState = evidenceCase.ReportsClearedAtUtc is null
            ? "<span class=\"rounded-full border border-amber-500/30 bg-amber-500/10 px-2 py-1 text-xs font-semibold text-amber-300\">Active / unchanged</span>"
            : $"<span class=\"rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-1 text-xs font-semibold text-emerald-300\">{evidenceCase.ReportsClearedCount} cleared</span>";
        var builder = new StringBuilder($"<section id=\"reports\" class=\"scroll-mt-4 overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"px-5 py-4 border-b border-line flex items-center justify-between gap-3\"><div><h3 class=\"font-semibold text-foreground\">Player reports</h3><p class=\"text-sm text-muted\">Reports grouped into this evidence case.</p></div>{reportState}</div><div class=\"overflow-x-auto\"><table class=\"w-full text-left\"><thead class=\"text-xs uppercase text-muted border-b border-line bg-surface-alt/30\"><tr><th class=\"px-5 py-3\">Time</th><th class=\"px-5 py-3\">Reporter</th><th class=\"px-5 py-3\">Reason</th><th class=\"px-5 py-3\">Penalty</th></tr></thead><tbody class=\"divide-y divide-line\">");
        if (evidenceCase.Reports.Count == 0)
            builder.Append("<tr><td colspan=\"4\" class=\"px-5 py-6 text-center text-muted\">No player reports are attached to this case.</td></tr>");
        else
            foreach (var report in evidenceCase.Reports.OrderBy(item => item.WhenUtc))
                builder.Append("<tr class=\"transition-colors hover:bg-surface-hover/30\">").Append(Cell(report.WhenUtc.ToString("u"))).Append(Cell(report.ReporterName)).Append(Cell(report.Reason)).Append(Cell(report.PenaltyId is > 0 ? $"#{report.PenaltyId}" : "Legacy")).Append("</tr>");
        return builder.Append("</tbody></table></div></section>").ToString();
    }

    private static string AntiCheatSection(EvidenceCase evidenceCase, AntiCheatCaseMetrics? metrics)
    {
        var builder = new StringBuilder("<section id=\"detection\" class=\"scroll-mt-4 overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"px-5 py-4 border-b border-line\"><h3 class=\"font-semibold text-foreground\">Detection event</h3><p class=\"text-sm text-muted\">Case-specific snapshots captured around an automated ban.</p></div>");
        if (evidenceCase.AntiCheat is null)
            return builder.Append("<div class=\"p-5 text-sm text-muted\">This case was not triggered by an automated anti-cheat ban.</div></section>").ToString();

        builder.Append($"<div class=\"p-5 border-b border-line\"><div class=\"text-sm text-muted\">Detection</div><div class=\"mt-1 text-foreground break-words\">{Encode(evidenceCase.AntiCheat.Detection)}</div><div class=\"mt-2 text-xs text-muted\">Penalty #{Encode(metrics?.PenaltyId?.ToString() ?? "unresolved")} · {Encode(evidenceCase.AntiCheat.WhenUtc.ToString("u"))}</div></div>")
            .Append("<div class=\"overflow-x-auto\"><table class=\"w-full text-left\"><thead class=\"text-xs uppercase text-muted border-b border-line\"><tr><th class=\"px-5 py-3\">Time</th><th class=\"px-5 py-3\">K/D/H</th><th class=\"px-5 py-3\">Strain</th><th class=\"px-5 py-3\">Snap avg / hits</th><th class=\"px-5 py-3\">Weapon / location</th><th class=\"px-5 py-3\">Distance</th></tr></thead><tbody>");
        if (metrics is null || metrics.Snapshots.Count == 0)
            builder.Append("<tr><td colspan=\"6\" class=\"px-5 py-6 text-center text-muted\">No anti-cheat snapshots were found in the database window.</td></tr>");
        else
            foreach (var item in metrics.Snapshots)
            {
                builder.Append("<tr class=\"border-b border-line/60\">").Append(Cell(item.WhenUtc.ToString("u"))).Append(Cell($"{item.Kills}/{item.Deaths}/{item.Hits}"))
                    .Append(Cell(item.CurrentStrain.ToString("0.000"))).Append(Cell($"{item.SessionAverageSnapValue:0.000} / {item.SessionSnapHits}"))
                    .Append(Cell($"{item.Weapon} / {item.HitLocation}")).Append(Cell(item.Distance.ToString("0.0"))).Append("</tr>");
                builder.Append("<tr class=\"border-b border-line/60 bg-surface/20\"><td colspan=\"6\" class=\"px-5 py-3 text-xs text-muted\"><details><summary class=\"cursor-pointer text-primary\">All snapshot metrics</summary><dl class=\"mt-3 grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-2\">")
                    .Append(Definition("Session length", item.CurrentSessionLength))
                    .Append(Definition("Since last event", item.TimeSinceLastEvent))
                    .Append(Definition("Elo", item.EloRating.ToString("0.00")))
                    .Append(Definition("Score / SPM", $"{item.SessionScore} / {item.SessionSpm:0.00}"))
                    .Append(Definition("Strain angle", item.StrainAngleBetween.ToString("0.000")))
                    .Append(Definition("Session offset", item.SessionAngleOffset.ToString("0.000")))
                    .Append(Definition("Recoil offset", item.RecoilOffset.ToString("0.000")))
                    .Append(Definition("Hit type", item.HitType))
                    .Append(Definition("Current view", item.CurrentViewAngle))
                    .Append(Definition("Last strain angle", item.LastStrainAngle))
                    .Append(Definition("Hit origin", item.HitOrigin))
                    .Append(Definition("Hit destination", item.HitDestination))
                    .Append(Definition("Captured views", item.CapturedViewAngles))
                    .Append("</dl></details></td></tr>");
            }
        return builder.Append("</tbody></table></div></section>").ToString();
    }

    private static string PlayerHistorySection(IReadOnlyList<EvidenceCase> cases)
    {
        var builder = new StringBuilder("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">Player evidence history</h3><p class=\"text-sm text-muted\">Other retained evidence cases for this player.</p></div><div class=\"divide-y divide-line\">");
        if (cases.Count == 0)
            builder.Append("<div class=\"p-5 text-sm text-muted\">No other retained cases were found for this player.</div>");
        else
            foreach (var item in cases)
            {
                builder.Append($"<a data-enhance-nav=\"false\" href=\"{CaseUrl(item.Id)}\" class=\"flex flex-col gap-2 px-5 py-3 transition-colors hover:bg-surface-hover/30 md:flex-row md:items-center md:justify-between\"><div><div class=\"font-medium text-foreground\">{Encode(item.ServerName)} · {Encode(item.Map)} / {Encode(item.Mode)}</div><div class=\"mt-1 text-xs text-muted\">{Encode(item.CreatedAtUtc.ToString("u"))} · {Encode(string.Join(" + ", item.TriggerTypes.Select(TriggerLabel)))}</div></div><div class=\"flex flex-wrap gap-2\">{StatusBadge(item.Status)}{ReviewBadge(item.ReviewDecision)}</div></a>");
            }
        return builder.Append("</div></section>").ToString();
    }

    private static string AuditHistorySection(EvidenceCase evidenceCase)
    {
        var history = evidenceCase.History.OrderByDescending(item => item.WhenUtc).ToList();
        var builder = new StringBuilder("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">Case activity</h3><p class=\"text-sm text-muted\">Assignment, evidence and review changes retained with this case.</p></div><div class=\"divide-y divide-line\">");
        if (history.Count == 0)
            builder.Append("<div class=\"p-5 text-sm text-muted\">No audit entries have been recorded for this legacy case.</div>");
        else
            foreach (var entry in history)
            {
                var notes = string.IsNullOrWhiteSpace(entry.Notes)
                    ? string.Empty
                    : $"<div class=\"mt-1 whitespace-pre-line text-sm text-muted\">{Encode(entry.Notes)}</div>";
                builder.Append($"<div class=\"flex gap-3 px-5 py-3\"><div class=\"mt-1 h-2.5 w-2.5 shrink-0 rounded-full bg-primary\"></div><div class=\"min-w-0 flex-1\"><div class=\"font-medium text-foreground\">{Encode(entry.Summary)}</div>{notes}<div class=\"mt-1 text-xs text-muted\">{Encode(entry.AdminName)} · {Encode(entry.WhenUtc.ToString("u"))}</div></div></div>");
            }
        return builder.Append("</div></section>").ToString();
    }

    private static string SearchFilters(
        IDictionary<string, string> meta,
        string view,
        IReadOnlyList<EvidenceCase> cases)
    {
        var games = cases.Select(item => item.Game).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item);
        var servers = cases.Select(item => new { item.ServerId, item.ServerName }).DistinctBy(item => item.ServerId, StringComparer.OrdinalIgnoreCase).OrderBy(item => item.ServerName);
        var builder = new StringBuilder($"<form data-enhance-nav=\"false\" method=\"get\" action=\"/Interaction/Render/{InteractionKey}\" class=\"grid gap-3 border-b border-line bg-surface-alt/10 px-4 py-4 md:grid-cols-2 xl:grid-cols-4\"><input type=\"hidden\" name=\"view\" value=\"{Encode(view)}\"><label class=\"xl:col-span-2\"><span class=\"mb-1 block text-xs font-semibold uppercase tracking-wide text-muted\">Search</span><div class=\"flex items-center rounded-lg border border-line bg-surface px-3\"><i class=\"ph ph-magnifying-glass text-muted\"></i><input name=\"q\" value=\"{Encode(Meta(meta, "q"))}\" placeholder=\"Player, case, GUID, map or server\" class=\"w-full border-0 bg-transparent px-2 py-2 text-sm text-foreground outline-none\"></div></label>");
        builder.Append(SelectFilter("game", "Game", Meta(meta, "game"), new[] { ("", "All games") }.Concat(games.Select(item => (item, item)))))
            .Append(SelectFilter("server", "Server", Meta(meta, "server"), new[] { ("", "All servers") }.Concat(servers.Select(item => (item.ServerId, item.ServerName)))))
            .Append(SelectFilter("source", "Evidence source", Meta(meta, "source"), new[] { ("", "All sources"), ("report", "Player report"), ("anticheat", "Anti-cheat"), ("manual", "Manual ban") }))
            .Append(SelectFilter("demo", "Demo state", Meta(meta, "demo"), new[] { ("", "Any demo state"), ("uploaded", "Uploaded"), ("unsupported", "Not supported"), ("missing", "Expected but missing"), ("processing", "Processing"), ("failed", "Failed") }))
            .Append(SelectFilter("review", "Review state", Meta(meta, "review"), new[] { ("", "Any review state"), ("Unreviewed", "Unreviewed"), ("NeedsMoreReview", "Needs more review"), ("CheatingActionTaken", "Cheating, action taken"), ("CheatingNoAction", "Cheating, no action"), ("NotCheatingNoAction", "Not cheating"), ("Inconclusive", "Inconclusive") }))
            .Append(DateFilter("from", "Captured from", Meta(meta, "from")))
            .Append(DateFilter("to", "Captured to", Meta(meta, "to")))
            .Append($"<div class=\"flex items-end gap-2\"><button type=\"submit\" class=\"inline-flex flex-1 items-center justify-center gap-2 rounded-lg bg-action-primary px-3 py-2 text-sm font-medium text-white hover:bg-action-primary-hover\"><i class=\"ph ph-funnel\"></i>Apply filters</button><a data-enhance-nav=\"false\" href=\"{OverviewUrl(view)}\" class=\"rounded-lg border border-line bg-surface px-3 py-2 text-sm text-muted hover:bg-surface-hover\">Clear</a></div></form>");
        return builder.ToString();
    }

    private static string SelectFilter(string name, string label, string selected, IEnumerable<(string Value, string Label)> options)
    {
        var builder = new StringBuilder($"<label><span class=\"mb-1 block text-xs font-semibold uppercase tracking-wide text-muted\">{Encode(label)}</span><select name=\"{Encode(name)}\" class=\"w-full rounded-lg border border-line bg-surface px-3 py-2 text-sm text-foreground\">");
        foreach (var option in options)
            builder.Append($"<option value=\"{Encode(option.Value)}\"{(option.Value.Equals(selected, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty)}>{Encode(option.Label)}</option>");
        return builder.Append("</select></label>").ToString();
    }

    private static string DateFilter(string name, string label, string value) =>
        $"<label><span class=\"mb-1 block text-xs font-semibold uppercase tracking-wide text-muted\">{Encode(label)}</span><input type=\"date\" name=\"{Encode(name)}\" value=\"{Encode(value)}\" class=\"w-full rounded-lg border border-line bg-surface px-3 py-2 text-sm text-foreground\"></label>";

    private static string Meta(IDictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static string NormalizeView(string? view) => view?.ToLowerInvariant() switch
    {
        "awaiting" or "processing" or "followup" or "cheating" or "cleared" or "failed" or "unassigned" or "mine" =>
            view.ToLowerInvariant(),
        _ => "all"
    };

    private static IEnumerable<EvidenceCase> FilterCases(
        IEnumerable<EvidenceCase> source,
        string view,
        int originId,
        IDictionary<string, string> meta)
    {
        var cases = view switch
        {
            "awaiting" => source.Where(item => item.ReviewDecision == EvidenceReviewDecision.Unreviewed),
            "processing" => source.Where(item => item.Status is EvidenceCaseStatus.Queued or
                EvidenceCaseStatus.Searching or EvidenceCaseStatus.WaitingForDemo or EvidenceCaseStatus.Uploading),
            "followup" => source.Where(item => item.ReviewDecision is EvidenceReviewDecision.NeedsMoreReview or
                EvidenceReviewDecision.Inconclusive),
            "cheating" => source.Where(item => item.ReviewDecision is EvidenceReviewDecision.CheatingActionTaken or
                EvidenceReviewDecision.CheatingNoAction),
            "cleared" => source.Where(item => item.ReviewDecision == EvidenceReviewDecision.NotCheatingNoAction),
            "failed" => source.Where(item => item.Status is EvidenceCaseStatus.Failed or EvidenceCaseStatus.NoDemo),
            "unassigned" => source.Where(item => item.AssignedToClientId is null),
            "mine" => source.Where(item => item.AssignedToClientId == originId),
            _ => source
        };

        var query = Meta(meta, "q");
        if (!string.IsNullOrWhiteSpace(query))
            cases = cases.Where(item => new[] { item.Id, item.TargetName, item.TargetClientId.ToString(), item.TargetNetworkId.ToString(), item.ServerName, item.ServerId, item.Map, item.Mode }
                .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)));
        var game = Meta(meta, "game");
        if (!string.IsNullOrWhiteSpace(game))
            cases = cases.Where(item => item.Game.Equals(game, StringComparison.OrdinalIgnoreCase));
        var server = Meta(meta, "server");
        if (!string.IsNullOrWhiteSpace(server))
            cases = cases.Where(item => item.ServerId.Equals(server, StringComparison.OrdinalIgnoreCase));
        cases = Meta(meta, "source").ToLowerInvariant() switch
        {
            "report" => cases.Where(item => item.Reports.Count > 0),
            "anticheat" => cases.Where(item => item.AntiCheat is not null),
            "manual" => cases.Where(item => item.ManualBanObserved),
            _ => cases
        };
        cases = Meta(meta, "demo").ToLowerInvariant() switch
        {
            "uploaded" => cases.Where(item => item.Status == EvidenceCaseStatus.Uploaded),
            "unsupported" => cases.Where(item => item.Status == EvidenceCaseStatus.DemoUnsupported),
            "missing" => cases.Where(item => item.Status == EvidenceCaseStatus.NoDemo),
            "processing" => cases.Where(item => item.Status is EvidenceCaseStatus.Queued or EvidenceCaseStatus.Searching or EvidenceCaseStatus.WaitingForDemo or EvidenceCaseStatus.Uploading),
            "failed" => cases.Where(item => item.Status == EvidenceCaseStatus.Failed),
            _ => cases
        };
        var review = Meta(meta, "review");
        if (Enum.TryParse<EvidenceReviewDecision>(review, true, out var decision))
            cases = cases.Where(item => item.ReviewDecision == decision);
        if (DateTime.TryParse(Meta(meta, "from"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from))
            cases = cases.Where(item => item.CreatedAtUtc >= from.Date);
        if (DateTime.TryParse(Meta(meta, "to"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var to))
            cases = cases.Where(item => item.CreatedAtUtc < to.Date.AddDays(1));
        return cases;
    }

    private static string OverviewMetric(
        string label,
        int value,
        string icon,
        string color,
        string destinationView,
        string currentView)
    {
        var active = destinationView == currentView ? "bg-primary/5" : "hover:bg-surface-hover/30";
        return $"<a data-enhance-nav=\"false\" href=\"{OverviewUrl(destinationView)}\" class=\"dtd-overview-metric flex min-w-0 items-center gap-3 border-r border-line px-4 py-4 transition-colors last:border-r-0 {active}\"><i class=\"ph {Encode(icon)} shrink-0 text-xl {Encode(color)}\"></i><div class=\"min-w-0\"><div class=\"text-xl font-bold text-foreground\">{value:N0}</div><div class=\"truncate text-xs text-muted\">{Encode(label)}</div></div></a>";
    }

    private static string FilterLink(string label, string destinationView, string currentView, int count)
    {
        var active = destinationView == currentView
            ? "bg-primary text-white"
            : "text-muted hover:bg-surface-hover hover:text-foreground";
        return $"<a data-enhance-nav=\"false\" href=\"{OverviewUrl(destinationView)}\" class=\"inline-flex shrink-0 items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors {active}\"><span>{Encode(label)}</span><span class=\"rounded bg-black/10 px-1.5 py-0.5 text-xs\">{count:N0}</span></a>";
    }

    private static string OverviewCaseRow(EvidenceCase item)
    {
        var initial = string.IsNullOrWhiteSpace(item.TargetName)
            ? "?"
            : item.TargetName.Trim()[0].ToString().ToUpperInvariant();
        var triggers = string.Join(" + ", item.TriggerTypes.Select(TriggerLabel));
        var confidence = EvidenceAssessment.Confidence(item);
        var assignment = item.AssignedToClientId is null
            ? "Unassigned"
            : $"Assigned to {item.AssignedToName}";
        return $"""
            <article class="dtd-case-row px-5 py-4 transition-colors hover:bg-surface-hover/30 md:px-6">
              <div class="flex min-w-0 items-center gap-3">
                <a href="/client/{item.TargetClientId}" class="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg border border-line bg-surface-alt font-bold text-muted">{Encode(initial)}</a>
                <div class="min-w-0">
                  <div class="flex min-w-0 flex-wrap items-center gap-2">
                    <a href="/client/{item.TargetClientId}" class="max-w-full truncate font-semibold text-foreground transition-colors hover:text-primary">{Encode(item.TargetName)}</a>
                    <span class="rounded border border-primary/30 bg-primary/10 px-1.5 py-0.5 text-xs font-semibold text-primary">{Encode(item.Game)}</span>
                  </div>
                  <div class="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted">
                    <span class="font-mono">{Encode(item.Id)}</span><span>{Encode(item.CreatedAtUtc.ToString("u"))}</span><span>{Encode(triggers)}</span><span>{Encode(confidence.Label)}</span><span>{Encode(assignment)}</span>
                  </div>
                </div>
              </div>
              <div class="min-w-0 text-sm">
                <div class="truncate font-medium text-foreground" title="{Encode(item.ServerName)}">{Encode(item.ServerName)}</div>
                <div class="mt-1 truncate text-xs text-muted" title="{Encode(item.ServerId)} · {Encode(item.Map)} / {Encode(item.Mode)}">{Encode(item.ServerId)} · {Encode(item.Map)} / {Encode(item.Mode)}</div>
              </div>
              <div class="flex flex-wrap items-center gap-2 md:justify-end">
                {StatusBadge(item.Status)}{ReviewBadge(item.ReviewDecision)}
                <a data-enhance-nav="false" href="{CaseUrl(item.Id)}" class="inline-flex items-center justify-center gap-2 rounded-lg bg-action-primary px-3 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-action-primary-hover"><i class="ph ph-magnifying-glass"></i>Review case</a>
              </div>
            </article>
            """;
    }

    private static string CasePager(string label, string icon, EvidenceCase? item) => item is null
        ? $"<span class=\"inline-flex cursor-not-allowed items-center gap-1 rounded-lg border border-line px-2.5 py-1.5 text-xs text-muted opacity-40\"><i class=\"ph {Encode(icon)}\"></i>{Encode(label)}</span>"
        : $"<a data-enhance-nav=\"false\" href=\"{CaseUrl(item.Id)}\" class=\"inline-flex items-center gap-1 rounded-lg border border-line bg-surface px-2.5 py-1.5 text-xs font-medium text-foreground transition-colors hover:bg-surface-hover\"><i class=\"ph {Encode(icon)}\"></i>{Encode(label)}</a>";

    private static string InfoBlock(string label, object value, string icon) =>
        $"<div class=\"min-w-0 rounded-lg border border-line bg-surface-alt/20 p-3\"><dt class=\"flex items-center gap-1.5 text-xs uppercase tracking-wide text-muted\"><i class=\"ph {Encode(icon)}\"></i>{Encode(label)}</dt><dd class=\"mt-1 break-words text-sm font-medium text-foreground\">{Encode(value)}</dd></div>";

    private static string TriggerLabel(EvidenceTriggerType trigger) => trigger switch
    {
        EvidenceTriggerType.AutomatedBan => "Anti-cheat",
        EvidenceTriggerType.ManualBan => "Manual ban",
        _ => "Report"
    };

    internal static string CaseUrl(string caseId) =>
        $"/Interaction/Render/{InteractionKey}?case={WebUtility.UrlEncode(caseId)}";

    private static string OverviewUrl(string view) =>
        $"/Interaction/Render/{InteractionKey}?view={WebUtility.UrlEncode(view)}";

    private static string MetricTile(string label, string value, string icon, string color) =>
        $"<div class=\"rounded-lg border border-line bg-surface/30 p-3\"><div class=\"flex items-center justify-between gap-2\"><div class=\"text-lg font-bold text-foreground\">{Encode(value)}</div><i class=\"ph {Encode(icon)} {Encode(color)} text-lg\"></i></div><div class=\"mt-1 text-xs uppercase tracking-wide text-muted\">{Encode(label)}</div></div>";

    private static string Cell(object? value) => $"<td class=\"px-5 py-3 text-sm whitespace-pre-line\">{Encode(value)}</td>";
    private static string StatusBadge(EvidenceCaseStatus status) => $"<span class=\"rounded-full border px-2 py-1 text-xs font-semibold {StatusClass(status)}\">{Encode(StatusLabel(status))}</span>";
    private static string StatusLabel(EvidenceCaseStatus status) => status switch
    {
        EvidenceCaseStatus.WaitingForDemo => "Waiting for demo",
        EvidenceCaseStatus.NoDemo => "Demo missing",
        EvidenceCaseStatus.DemoUnsupported => "Demo unsupported",
        _ => status.ToString()
    };
    private static string ReviewBadge(EvidenceReviewDecision decision) =>
        $"<span class=\"rounded-full border px-2 py-1 text-xs font-semibold {ReviewClass(decision)}\">{Encode(TitleCase(EvidenceReviewService.DecisionLabel(decision)))}</span>";
    private static string StatusClass(EvidenceCaseStatus status) => status switch
    {
        EvidenceCaseStatus.Uploaded => "border-emerald-500/30 bg-emerald-500/10 text-emerald-300",
        EvidenceCaseStatus.Failed => "border-red-500/30 bg-red-500/10 text-red-300",
        EvidenceCaseStatus.NoDemo or EvidenceCaseStatus.DemoUnsupported => "border-amber-500/30 bg-amber-500/10 text-amber-300",
        _ => "border-primary/30 bg-primary/10 text-primary"
    };
    private static string ReviewClass(EvidenceReviewDecision decision) => decision switch
    {
        EvidenceReviewDecision.CheatingActionTaken or EvidenceReviewDecision.CheatingNoAction =>
            "border-red-500/30 bg-red-500/10 text-red-300",
        EvidenceReviewDecision.NotCheatingNoAction =>
            "border-emerald-500/30 bg-emerald-500/10 text-emerald-300",
        EvidenceReviewDecision.NeedsMoreReview or EvidenceReviewDecision.Inconclusive =>
            "border-primary/30 bg-primary/10 text-primary",
        _ => "border-amber-500/30 bg-amber-500/10 text-amber-300"
    };
    private static string Definition(string label, object value) => $"<div class=\"min-w-0\"><dt class=\"text-xs uppercase tracking-wide text-muted\">{Encode(label)}</dt><dd class=\"mt-1 break-words text-foreground\">{Encode(value)}</dd></div>";
    private static string? DiscordMessageUrl(EvidenceCase item) => string.IsNullOrWhiteSpace(item.DiscordGuildId) || string.IsNullOrWhiteSpace(item.DiscordChannelId) || string.IsNullOrWhiteSpace(item.DiscordMessageId) ? null : $"https://discord.com/channels/{item.DiscordGuildId}/{item.DiscordChannelId}/{item.DiscordMessageId}";
    private static string FormatBytes(long value) => value < 1024 * 1024 ? $"{value / 1024d:0.0} KB" : $"{value / 1024d / 1024d:0.0} MB";
    private static string FormatDuration(int seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}d {duration.Hours}h"
            : duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                : $"{duration.Minutes}m";
    }
    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);
    private static string Encode(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);

    private void OnConfigurationUpdated(DemosToDiscordConfig _) => Register();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _configurationHandler.Updated -= OnConfigurationUpdated;
        _interactions.UnregisterInteraction(InteractionKey);
        _interactions.UnregisterInteraction(ReviewInteractionKey);
    }
}

