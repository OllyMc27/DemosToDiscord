using Data.Models;

namespace DemosToDiscord;

public enum ProactiveRiskLevel
{
    Normal,
    Elevated,
    Review,
    High,
    VeryHigh
}

public sealed record ProactiveRiskSignal(
    ProactiveMetric Metric,
    string Label,
    string CorrelationGroup,
    double Observed,
    double Percentile,
    double PopulationMedian,
    double ExpectedMultiple,
    int SampleSize,
    int EligiblePopulation,
    string BaselineScope,
    double Contribution);

public sealed record ProactiveRiskAssessment(
    int ClientId,
    long ServerId,
    Reference.Game Game,
    int Score,
    ProactiveRiskLevel Level,
    bool Suppressed,
    string SuppressionReason,
    IReadOnlyList<ProactiveRiskSignal> Signals,
    int RepeatHistoryCount)
{
    public ProactiveRiskSignal? StrongestSignal => Signals.OrderByDescending(item => item.Contribution).FirstOrDefault();
    public bool RequiresHumanReview => !Suppressed && Level >= ProactiveRiskLevel.Review;
    public string RecommendedAction => "Human review only — no automatic punishment";
}

public sealed class ProactiveRiskScorer(IProactiveBaselineProvider baselines, DemosToDiscordConfig config)
{
    public ProactiveRiskAssessment Score(int clientId, long serverId, int repeatHistoryCount = 0)
    {
        if (!baselines.IsAvailable)
            return Suppressed(clientId, serverId, "Population baseline is unavailable.");
        var player = baselines.GetPlayer(clientId, serverId);
        if (player is null)
            return Suppressed(clientId, serverId, "Player statistics are not yet available.");
        if (player.Excluded)
            return Suppressed(clientId, serverId, "This game/server is excluded from proactive scoring.", player.Game);

        var signals = new List<ProactiveRiskSignal>();
        Add(signals, player, ProactiveMetric.TrackedHeadRate, player.TrackedHeadRate, player.TrackedHits, "tracked-hit head rate", "aim", 1.0);
        if (player.Game is Reference.Game.T6 or Reference.Game.IW5)
            Add(signals, player, ProactiveMetric.KillingHeadRate, player.KillingHeadRate, player.KillingHits, "killing-hit head rate", "aim", 1.0);
        Add(signals, player, ProactiveMetric.KillDeathRatio, player.KillDeathRatio, player.Kills + player.Deaths, "kill/death ratio", "performance", 0.55);
        Add(signals, player, ProactiveMetric.ScorePerMinute, player.ScorePerMinute, player.TimePlayedSeconds, "score per minute", "performance", 0.45);
        Add(signals, player, ProactiveMetric.Performance, player.Performance, player.TimePlayedSeconds, "performance", "performance", 0.45);
        if (player.Game is Reference.Game.T6 or Reference.Game.IW5)
        {
            Add(signals, player, ProactiveMetric.MaximumStrain, player.MaximumStrain, player.SnapHitCount, "maximum strain", "mechanics", 0.65);
            Add(signals, player, ProactiveMetric.AverageSnap, player.AverageSnap, player.SnapHitCount, "average snap", "mechanics", 0.65);
        }

        var positive = signals.Where(item => item.Contribution > 0).ToList();
        var groupScores = positive.GroupBy(item => item.CorrelationGroup)
            .Select(group => group.Max(item => item.Contribution)).OrderByDescending(value => value).ToList();
        var score = groupScores.Sum();
        if (groupScores.Count(value => value >= 8) >= 2)
            score += 6;
        score += Math.Min(12, Math.Max(0, repeatHistoryCount) * Math.Max(0, config.ProactiveRepeatHistoryWeight));
        var rounded = Math.Clamp((int)Math.Round(score), 0, 100);
        var level = Level(rounded);
        return new ProactiveRiskAssessment(
            clientId, serverId, player.Game, rounded, level, false, string.Empty,
            positive.OrderByDescending(item => item.Contribution).ToList(), repeatHistoryCount);
    }

    private void Add(
        ICollection<ProactiveRiskSignal> signals,
        ProactiveBaselineMember player,
        ProactiveMetric metric,
        double observed,
        int sampleSize,
        string label,
        string correlationGroup,
        double weight)
    {
        if (!double.IsFinite(observed) || observed <= 0)
            return;
        var population = baselines.GetPopulation(metric, player.Game, player.ServerId)
                         ?? baselines.GetPopulation(metric, player.Game);
        if (population is null)
            return;
        var percentile = population.Percentile(observed);
        var expectedMultiple = population.Median <= 0 ? 0 : observed / population.Median;
        var raw = percentile switch
        {
            >= 99.9 => 32,
            >= 99.7 => 27,
            >= 99.5 => 22,
            >= 99.0 => 16,
            >= 98.0 => 10,
            >= 97.0 => 5,
            _ => 0
        };
        if (raw == 0)
            return;
        var sampleConfidence = metric switch
        {
            ProactiveMetric.TrackedHeadRate => Math.Min(1, Math.Sqrt(sampleSize / (double)Math.Max(1, config.ProactiveMinimumTrackedHits))),
            ProactiveMetric.KillingHeadRate => Math.Min(1, Math.Sqrt(sampleSize / (double)Math.Max(100, config.ProactiveMinimumTrackedHits / 2))),
            _ => Math.Min(1, Math.Sqrt(sampleSize / 3600d))
        };
        var contribution = Math.Round(raw * weight * sampleConfidence, 2);
        if (contribution <= 0)
            return;
        signals.Add(new ProactiveRiskSignal(
            metric, label, correlationGroup, observed, percentile, population.Median,
            Math.Round(expectedMultiple, 2), sampleSize, population.EligiblePlayers,
            population.Scope, contribution));
    }

    private static ProactiveRiskLevel Level(int score) => score switch
    {
        >= 80 => ProactiveRiskLevel.VeryHigh,
        >= 65 => ProactiveRiskLevel.High,
        >= 50 => ProactiveRiskLevel.Review,
        >= 25 => ProactiveRiskLevel.Elevated,
        _ => ProactiveRiskLevel.Normal
    };

    private static ProactiveRiskAssessment Suppressed(
        int clientId,
        long serverId,
        string reason,
        Reference.Game game = Reference.Game.UKN) =>
        new(clientId, serverId, game, 0, ProactiveRiskLevel.Normal, true, reason, [], 0);
}
