using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ElectricityConsumptionForecasting.Application.Services;

public sealed class IqrOutlierDetectionService : IOutlierDetectionService
{
    public OutlierDetectionResult RemoveOutliers(IReadOnlyList<ForecastCandidateScore> candidates, ForecastEngineOptions options)
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
    public decimal Calculate(IReadOnlyList<ForecastCandidateScore> candidates, IReadOnlyList<CustomerMonthlyConsumption> targetHistory, CustomerProfile targetProfile, ForecastEngineOptions options)
    {
        if (candidates.Count == 0)
        {
            return 0m;
        }

        var countScore = Math.Min((decimal)candidates.Count / MinimumSimilarSubscribers(targetProfile.ClimateType, options), 1m);
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

    private static int MinimumSimilarSubscribers(int climateType, ForecastEngineOptions options) =>
        climateType == 2 ? options.MinimumSimilarSubscribersTropical : options.MinimumSimilarSubscribersTemperate;
}

public sealed class SimilarPatternWindowProvider : ISimilarPatternWindowProvider
{
    public IReadOnlyDictionary<string, SimilarPatternWindow> BuildWindows(
        IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>> candidateHistories,
        int targetYear,
        int targetMonth,
        int minimumHistoryMonths)
    {
        var targetKey = ToMonthKey(targetYear, targetMonth);
        return candidateHistories
            .Select(x => BuildWindow(x.Key, x.Value, targetKey, targetMonth, minimumHistoryMonths))
            .Where(x => x is not null)
            .Cast<SimilarPatternWindow>()
            .ToDictionary(x => x.BillIdentifier);
    }

    private static SimilarPatternWindow? BuildWindow(string billIdentifier, IReadOnlyList<CustomerMonthlyConsumption> history, int targetKey, int targetMonth, int minimumHistoryMonths)
    {
        var ordered = history
            .Where(x => ToMonthKey(x.Year, x.Month) < targetKey)
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToList();
        if (ordered.Count < minimumHistoryMonths)
        {
            return null;
        }

        var comparable = ordered
            .Where(x => x.Month == targetMonth)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefault() ?? ordered.Last();

        return new SimilarPatternWindow(
            billIdentifier,
            ordered,
            comparable.Consumption,
            comparable.Year,
            comparable.Month,
            ToMonthKey(comparable.Year, comparable.Month) == targetKey);
    }

    private static int ToMonthKey(int year, int month) => year * 12 + month;
}

public sealed class AppSettingsForecastConfigProvider : IForecastConfigProvider
{
    private readonly IOptions<ForecastEngineOptions> options;

    public AppSettingsForecastConfigProvider(IOptions<ForecastEngineOptions> options) => this.options = options;

    public Task<ForecastEngineOptions> GetOptionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Clone(options.Value));

    public static ForecastEngineOptions Clone(ForecastEngineOptions source) => new()
    {
        MinimumHistoryMonths = source.MinimumHistoryMonths,
        MaximumHistoryMonths = source.MaximumHistoryMonths,
        MinimumSimilarSubscribersTemperate = source.MinimumSimilarSubscribersTemperate,
        MinimumSimilarSubscribersTropical = source.MinimumSimilarSubscribersTropical,
        TopNSimilarSubscribers = source.TopNSimilarSubscribers,
        HighConfidenceThreshold = source.HighConfidenceThreshold,
        MediumConfidenceThreshold = source.MediumConfidenceThreshold,
        LowConfidenceThreshold = source.LowConfidenceThreshold,
        IqrOutlierMultiplier = source.IqrOutlierMultiplier,
        MaxAllowedMonthlyGrowthRatio = source.MaxAllowedMonthlyGrowthRatio,
        MaxAllowedMonthlyDropRatio = source.MaxAllowedMonthlyDropRatio,
        ConsumptionSimilarityWeight = source.ConsumptionSimilarityWeight,
        TrendSimilarityWeight = source.TrendSimilarityWeight,
        SeasonalSimilarityWeight = source.SeasonalSimilarityWeight,
        ProfileSimilarityWeight = source.ProfileSimilarityWeight,
        GeographicSimilarityWeight = source.GeographicSimilarityWeight,
        MinimumCandidateSimilarity = source.MinimumCandidateSimilarity,
        ConsumptionBandToleranceRatio = source.ConsumptionBandToleranceRatio,
        AmpereToleranceRatio = source.AmpereToleranceRatio
    };
}

public sealed class ForecastRequestValidator : IForecastRequestValidator
{
    public RequestValidationResult Validate(ForecastCustomerRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.BillIdentifier))
        {
            errors.Add("BillIdentifier is required.");
        }

        ValidateYearMonth(request.TargetYear, request.TargetMonth, errors);
        return errors.Count == 0 ? RequestValidationResult.Success : new RequestValidationResult(false, errors);
    }

    public RequestValidationResult Validate(BatchForecastRequest request)
    {
        var errors = new List<string>();
        if (request.CoCode <= 0)
        {
            errors.Add("CoCode must be greater than zero.");
        }

        if (request.TariffType.HasValue && request.TariffType <= 0)
        {
            errors.Add("TariffType must be greater than zero when provided.");
        }

        if (request.MaxCustomers <= 0)
        {
            errors.Add("MaxCustomers must be greater than zero.");
        }

        ValidateYearMonth(request.TargetYear, request.TargetMonth, errors);
        return errors.Count == 0 ? RequestValidationResult.Success : new RequestValidationResult(false, errors);
    }

    private static void ValidateYearMonth(int year, int month, List<string> errors)
    {
        if (year <= 0)
        {
            errors.Add("TargetYear must be greater than zero.");
        }

        if (month is < 1 or > 12)
        {
            errors.Add("TargetMonth must be between 1 and 12.");
        }
    }
}
