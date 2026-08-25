using Microsoft.Extensions.Logging;

namespace DemosToDiscord;

public sealed class DemoLocator(DemosToDiscordConfig config, ILogger<DemoLocator> logger)
{
    public async Task<DemoCandidate?> WaitForCandidateAsync(
        EvidenceCase evidenceCase,
        string folder,
        CancellationToken token)
    {
        var expires = DateTime.UtcNow.AddMinutes(Math.Max(1, config.MaxWaitMinutes));
        while (DateTime.UtcNow < expires)
        {
            token.ThrowIfCancellationRequested();
            var candidate = FindBest(evidenceCase, folder);
            if (candidate is not null)
                return candidate;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, config.RetryIntervalSeconds)), token);
        }

        return null;
    }

    public DemoCandidate? FindBest(EvidenceCase evidenceCase, string folder)
    {
        if (!Directory.Exists(folder))
        {
            if (config.Debug)
                logger.LogWarning("[DemosToDiscord] Demo folder does not exist: {Folder}", folder);
            return null;
        }

        var earliest = evidenceCase.CreatedAtUtc.AddMinutes(-Math.Max(1, config.MaxLookbackMinutes));
        var latest = evidenceCase.CreatedAtUtc.AddMinutes(5);
        var parsedFiles = Directory.EnumerateFiles(folder, "*.demo", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Meta: ParseFilename(Path.GetFileName(path))))
            .ToList();

        if (config.Debug)
        {
            foreach (var item in parsedFiles
                         .OrderByDescending(item => File.GetLastWriteTimeUtc(item.Path))
                         .Take(25))
            {
                var reason = RejectionReason(item.Meta, evidenceCase, earliest, latest);
                logger.LogInformation(
                    "[DemosToDiscord] Demo scan case={CaseId} file={File} parsedUtc={Started:u} map={Map} mode={Mode} result={Result}",
                    evidenceCase.Id,
                    Path.GetFileName(item.Path),
                    item.Meta?.StartedAtUtc,
                    item.Meta?.Map ?? "unparsed",
                    item.Meta?.Mode ?? "unparsed",
                    reason ?? "candidate");
            }
        }

        var candidates = parsedFiles
            .Where(item => RejectionReason(item.Meta, evidenceCase, earliest, latest) is null)
            .Where(item => MapMatches(item.Meta!.Map, evidenceCase.Map) && ModeMatches(item.Meta.Mode, evidenceCase.Mode))
            .Select(item => Score(evidenceCase, item.Path, item.Meta!))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => File.GetLastWriteTimeUtc(item.DemoPath))
            .ToList();

        return candidates.FirstOrDefault();
    }

    public async Task<bool> WaitUntilReadyAsync(string path, CancellationToken token)
    {
        long previousSize = -1;
        var stable = 0;
        var expires = DateTime.UtcNow.AddMinutes(Math.Max(1, config.MaxWaitMinutes));
        while (DateTime.UtcNow < expires)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 0)
                {
                    stable = info.Length == previousSize ? stable + 1 : 0;
                    previousSize = info.Length;
                    if (stable >= Math.Max(1, config.FileStableChecks))
                    {
                        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                        return true;
                    }
                }
            }
            catch (IOException)
            {
                stable = 0;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, config.RetryIntervalSeconds)), token);
        }

        return false;
    }

    internal static DemoFileMeta? ParseFilename(string name)
    {
        try
        {
            var parts = Path.GetFileNameWithoutExtension(name).Split('_');
            if (parts.Length < 7)
                return null;
            var length = parts.Length;
            return new DemoFileMeta(
                parts[0],
                string.Join("_", parts.Skip(1).Take(length - 6)),
                new DateTime(
                    int.Parse(parts[length - 3]), int.Parse(parts[length - 5]), int.Parse(parts[length - 4]),
                    int.Parse(parts[length - 2]), int.Parse(parts[length - 1]), 0, DateTimeKind.Local).ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    private DemoCandidate Score(EvidenceCase evidenceCase, string path, DemoFileMeta meta)
    {
        var json = evidenceCase.Game.Equals("T6", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(path, ".json")
            : null;
        if (json is not null && !File.Exists(json))
            json = null;
        var confirmed = json is not null && JsonContainsTarget(json, evidenceCase.TargetNetworkId);
        var delta = Math.Abs((evidenceCase.CreatedAtUtc - meta.StartedAtUtc).TotalMinutes);
        var score = (confirmed ? 10_000 : 0) + Math.Max(0, 1_000 - delta);
        return new DemoCandidate(path, json, meta.Map, meta.Mode, meta.StartedAtUtc, confirmed, score);
    }

    private static bool JsonContainsTarget(string path, long networkId)
    {
        try
        {
            return File.ReadAllText(path).Contains(networkId.ToString(), StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool MapMatches(string actual, string expected) =>
        string.IsNullOrWhiteSpace(expected) || actual.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
        expected.Contains(actual, StringComparison.OrdinalIgnoreCase);

    private static bool ModeMatches(string actual, string expected) =>
        string.IsNullOrWhiteSpace(expected) || actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string? RejectionReason(
        DemoFileMeta? meta,
        EvidenceCase evidenceCase,
        DateTime earliest,
        DateTime latest)
    {
        if (meta is null)
            return "filename-unparsed";
        if (meta.StartedAtUtc < earliest)
            return "outside-lookback";
        if (meta.StartedAtUtc > latest)
            return "after-event";
        if (!MapMatches(meta.Map, evidenceCase.Map))
            return "map-mismatch";
        if (!ModeMatches(meta.Mode, evidenceCase.Mode))
            return "mode-mismatch";
        return null;
    }

    internal sealed record DemoFileMeta(string Mode, string Map, DateTime StartedAtUtc);
}

