using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ElectricityConsumptionForecasting.Application.Services;

public sealed class IqrOutlierDetectionService : IOutlierDetectionService
{
    private readonly ForecastEngineOptions options;

    public IqrOutlierDetectionService(IOptions<ForecastEngineOptions> options) => this.options = options.Value;

    public OutlierDetectionResult RemoveOutliers(IReadOnlyList<ForecastCandidateScore> candidates)
    {
        if (candidates.Count < 4)
        {
            return new OutlierDetectionResult(candidates, new HashSet<ForecastCandidateScore>());
        }

        var ordered = candidates.Select(x => x.TargetMonthConsumption).Order().ToArray();
        var q1 = Percentile(ordered, 0.25m);
        var q3 = Percentile(ordered, 0.75m);
        var iqr = q3 - q1;
        var lower = q1 - options.IqrOutlierMultiplier * iqr;
        var upper = q3 + options.IqrOutlierMultiplier * iqr;
        var included = candidates.Where(x => x.TargetMonthConsumption >= lower && x.TargetMonthConsumption <= upper).ToList();
        var outliers = candidates.Except(included).ToHashSet();
        return new OutlierDetectionResult(included, outliers);
    }

    private static decimal Percentile(decimal[] orderedValues, decimal percentile)
    {
        if (orderedValues.Length == 0)
        {
            return 0m;
        }

        var position = (orderedValues.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return orderedValues[lower];
        }

        return orderedValues[lower] + (orderedValues[upper] - orderedValues[lower]) * (position - lower);
    }
}

public sealed class ForecastConfidenceService : IForecastConfidenceService
{
    private readonly ForecastEngineOptions options;

    public ForecastConfidenceService(IOptions<ForecastEngineOptions> options) => this.options = options.Value;

    public decimal Calculate(IReadOnlyList<ForecastCandidateScore> candidates, IReadOnlyList<CustomerMonthlyConsumption> targetHistory, CustomerProfile targetProfile)
    {
        if (candidates.Count == 0)
        {
            return 0m;
        }

        var countScore = Math.Min((decimal)candidates.Count / MinimumSimilarSubscribers(targetProfile.ClimateType), 1m);
        var similarityScore = candidates.Average(x => x.SimilarityScore);
        var mean = candidates.Average(x => x.TargetMonthConsumption);
        var variance = candidates.Average(x => (x.TargetMonthConsumption - mean) * (x.TargetMonthConsumption - mean));
        var coefficientOfVariation = mean <= 0m ? 1m : (decimal)Math.Sqrt((double)variance) / mean;
        var varianceScore = ConsumptionSimilarityService.Clamp01(1m - Math.Min(coefficientOfVariation, 1m));
        var completenessScore = Math.Min((decimal)targetHistory.Count / options.MaximumHistoryMonths, 1m);
        var smartOrActualShare = (decimal)targetHistory.Count(x => x.IsActualReading || x.IsSmartReading) / targetHistory.Count;

        return ConsumptionSimilarityService.Clamp01(
            countScore * 0.20m +
            similarityScore * 0.35m +
            varianceScore * 0.25m +
            completenessScore * 0.10m +
            smartOrActualShare * 0.10m);
    }

    private int MinimumSimilarSubscribers(int climateType) =>
        climateType == 2 ? options.MinimumSimilarSubscribersTropical : options.MinimumSimilarSubscribersTemperate;
}
