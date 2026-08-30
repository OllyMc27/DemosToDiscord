namespace DemosToDiscord;

public sealed record DetectionObservation(
    DateTime ObservedAtUtc,
    string MetricKey,
    string DisplayName,
    string Scope,
    double ObservedValue,
    double ExpectedValue,
    double Percentile,
    int SampleSize,
    int PositiveEventCount,
    int PopulationSize,
    string? Weapon = null,
    string? Map = null);

public sealed record RiskAssessment(
    double Score,
    string Level,
    string Confidence,
    string? StrongestSignal,
    IReadOnlyList<DetectionSignal> Signals,
    bool ShouldCreateCase);

public sealed class RiskScorer(DemosToDiscordConfig config)
{
    public RiskAssessment Score(
        IEnumerable<DetectionObservation> observations,
        int recentProactiveCases = 0)
    {
        var settings = config.ProactiveDetection;
        var signals = observations
            .Select(item => ScoreSignal(item, settings))
            .Where(item => item is not null)
            .Cast<DetectionSignal>()
            .OrderByDescending(item => item.RiskContribution)
            .ToList();

        var scoringSignals = signals
            .GroupBy(item => item.MetricKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.RiskContribution).First())
            .ToList();
        var score = scoringSignals.Sum(item => item.RiskContribution);
        var independentSignals = scoringSignals.Count(item => item.RiskContribution >= 8);
        if (independentSignals >= 3)
            score += 18;
        else if (independentSignals == 2)
            score += 10;
        if (score >= 20 && recentProactiveCases > 0)
            score += Math.Min(15, recentProactiveCases * 5);
        score = Math.Round(Math.Clamp(score, 0, 100), 1);

        var level = score switch
        {
            >= 80 => "Very High",
            >= 65 => "High",
            >= 50 => "Review",
            >= 25 => "Elevated",
            _ => "Normal"
        };
        var confidence = Confidence(scoringSignals);
        return new RiskAssessment(
            score,
            level,
            confidence,
            signals.FirstOrDefault()?.DisplayName,
            signals,
            score >= settings.MinimumCaseRiskScore && signals.Count > 0);
    }

    private static DetectionSignal? ScoreSignal(
        DetectionObservation observation,
        ProactiveDetectionConfig settings)
    {
        if (observation.SampleSize < settings.MinimumTrackedHits ||
            observation.PositiveEventCount < settings.MinimumPositiveEvents ||
            observation.PopulationSize < settings.MinimumPopulationSize ||
            observation.ExpectedValue <= 0 ||
            observation.Percentile < settings.MinimumSignalPercentile)
        {
            return null;
        }

        var percentile = Math.Clamp(observation.Percentile, 0, 1);
        var multiple = observation.ObservedValue / observation.ExpectedValue;
        var percentilePoints = percentile switch
        {
            >= 0.999 => 35,
            >= 0.995 => 27,
            >= 0.99 => 20,
            >= 0.975 => 10,
            _ => 0
        };
        var multiplePoints = multiple switch
        {
            >= 3 => 15,
            >= 2.5 => 12,
            >= 2 => 8,
            >= 1.5 => 4,
            _ => 0
        };
        var fullConfidenceHits = Math.Max(1, settings.FullConfidenceTrackedHits);
        var fullConfidencePopulation = Math.Max(1, settings.FullConfidencePopulationSize);
        var sampleWeight = Math.Min(1, Math.Sqrt(observation.SampleSize / (double)fullConfidenceHits));
        var populationWeight = Math.Min(1, Math.Sqrt(observation.PopulationSize / (double)fullConfidencePopulation));
        var confidenceWeight = Math.Round(Math.Sqrt(sampleWeight * populationWeight), 3);
        var contribution = Math.Round((percentilePoints + multiplePoints) * confidenceWeight, 1);
        if (contribution <= 0)
            return null;

        return new DetectionSignal
        {
            ObservedAtUtc = observation.ObservedAtUtc,
            MetricKey = observation.MetricKey,
            DisplayName = observation.DisplayName,
            Scope = observation.Scope,
            Weapon = observation.Weapon,
            Map = observation.Map,
            ObservedValue = observation.ObservedValue,
            ExpectedValue = observation.ExpectedValue,
            Percentile = Math.Round(percentile * 100, 3),
            ExpectedMultiple = Math.Round(multiple, 3),
            SampleSize = observation.SampleSize,
            PopulationSize = observation.PopulationSize,
            ConfidenceWeight = confidenceWeight,
            RiskContribution = contribution,
            Explanation =
                $"{observation.DisplayName}: {observation.ObservedValue:0.###}; " +
                $"{percentile * 100:0.###}th percentile; {multiple:0.##}x expected; " +
                $"n={observation.SampleSize:N0}, population={observation.PopulationSize:N0}."
        };
    }

    private static string Confidence(IReadOnlyList<DetectionSignal> signals)
    {
        if (signals.Count == 0)
            return "Insufficient";
        var average = signals.Average(item => item.ConfidenceWeight);
        return average switch
        {
            >= 0.9 when signals.Count >= 2 => "High",
            >= 0.7 => "Moderate",
            _ => "Low"
        };
    }
}
