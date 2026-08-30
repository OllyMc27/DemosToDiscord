using Data.Models;

namespace DemosToDiscord;

public enum ProactiveMetric
{
    KillDeathRatio,
    ScorePerMinute,
    Performance,
    TrackedHeadRate,
    KillingHeadRate,
    MaximumStrain,
    AverageSnap
}

public sealed class ProactiveBaselineState
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastStatisticsUpdatedAtUtc { get; set; }
    public DateTime? LastHitStatisticsUpdatedAtUtc { get; set; }
    public long LastKillId { get; set; }
    public Dictionary<string, ProactiveBaselineMember> Members { get; set; } = [];
    public Dictionary<string, ProactiveWeaponBaselineMember> WeaponMembers { get; set; } = [];
}

public sealed class ProactiveBaselineMember
{
    public int ClientId { get; set; }
    public long ServerId { get; set; }
    public Reference.Game Game { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public bool Excluded { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public double ScorePerMinute { get; set; }
    public double Performance { get; set; }
    public int TimePlayedSeconds { get; set; }
    public double MaximumStrain { get; set; }
    public double AverageSnap { get; set; }
    public int SnapHitCount { get; set; }
    public int TrackedHits { get; set; }
    public int TrackedHeadHits { get; set; }
    public int KillingHits { get; set; }
    public int KillingHeadHits { get; set; }
    public DateTime? StatisticsUpdatedAtUtc { get; set; }

    public double KillDeathRatio => Deaths == 0 ? Kills : Kills / (double)Deaths;
    public double TrackedHeadRate => TrackedHits == 0 ? 0 : TrackedHeadHits / (double)TrackedHits;
    public double KillingHeadRate => KillingHits == 0 ? 0 : KillingHeadHits / (double)KillingHits;
}

public sealed class ProactiveWeaponBaselineMember
{
    public int ClientId { get; set; }
    public long ServerId { get; set; }
    public Reference.Game Game { get; set; }
    public string Weapon { get; set; } = string.Empty;
    public int KillingHits { get; set; }
    public int KillingHeadHits { get; set; }
    public double KillingHeadRate => KillingHits == 0 ? 0 : KillingHeadHits / (double)KillingHits;
}

public sealed record ProactivePopulationBaseline(
    ProactiveMetric Metric,
    string Scope,
    int EligiblePlayers,
    double Median,
    IReadOnlyList<double> SortedValues)
{
    public double Percentile(double value) => ProactiveBaselineMath.PercentileRank(SortedValues, value);
}

public static class ProactiveBaselineMath
{
    public static double Median(IEnumerable<double> source)
    {
        var values = source.Where(double.IsFinite).Order().ToArray();
        if (values.Length == 0)
            return 0;
        var middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }

    public static double PercentileRank(IReadOnlyList<double> sortedValues, double value)
    {
        if (sortedValues.Count == 0 || !double.IsFinite(value))
            return 0;
        var lowerOrEqual = UpperBound(sortedValues, value);
        return Math.Round(lowerOrEqual / (double)sortedValues.Count * 100d, 3);
    }

    private static int UpperBound(IReadOnlyList<double> values, double target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] <= target)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }
}

public static class ProactiveBaselineSelector
{
    public static ProactivePopulationBaseline? Build(
        IEnumerable<ProactiveBaselineMember> members,
        ProactiveMetric metric,
        Reference.Game game,
        long? serverId,
        int minimumPopulation,
        int minimumTrackedHits,
        int minimumHeadEvents)
    {
        var values = members
            .Where(item => !item.Excluded && item.Game == game && (serverId is null || item.ServerId == serverId))
            .Where(item => IsEligible(item, metric, minimumTrackedHits, minimumHeadEvents))
            .Select(item => Value(item, metric)).Where(double.IsFinite).Order().ToList();
        if (values.Count < Math.Max(10, minimumPopulation))
            return null;
        var scope = serverId is null ? game.ToString() : $"{game} + server";
        return new ProactivePopulationBaseline(metric, scope, values.Count, ProactiveBaselineMath.Median(values), values);
    }

    private static bool IsEligible(ProactiveBaselineMember item, ProactiveMetric metric, int minimumTrackedHits, int minimumHeadEvents) => metric switch
    {
        ProactiveMetric.KillDeathRatio => item.Kills + item.Deaths >= 100,
        ProactiveMetric.ScorePerMinute or ProactiveMetric.Performance => item.TimePlayedSeconds >= 3600 && item.Kills + item.Deaths >= 100,
        ProactiveMetric.TrackedHeadRate => item.TrackedHits >= minimumTrackedHits && item.TrackedHeadHits >= minimumHeadEvents,
        ProactiveMetric.KillingHeadRate => item.KillingHits >= Math.Max(100, minimumTrackedHits / 2) && item.KillingHeadHits >= minimumHeadEvents,
        ProactiveMetric.MaximumStrain or ProactiveMetric.AverageSnap => item.TimePlayedSeconds >= 3600 && item.SnapHitCount >= minimumHeadEvents,
        _ => false
    };

    private static double Value(ProactiveBaselineMember item, ProactiveMetric metric) => metric switch
    {
        ProactiveMetric.KillDeathRatio => item.KillDeathRatio,
        ProactiveMetric.ScorePerMinute => item.ScorePerMinute,
        ProactiveMetric.Performance => item.Performance,
        ProactiveMetric.TrackedHeadRate => item.TrackedHeadRate,
        ProactiveMetric.KillingHeadRate => item.KillingHeadRate,
        ProactiveMetric.MaximumStrain => item.MaximumStrain,
        ProactiveMetric.AverageSnap => item.AverageSnap,
        _ => 0
    };
}
