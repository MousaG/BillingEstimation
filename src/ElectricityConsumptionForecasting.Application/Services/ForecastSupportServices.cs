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
    public IReadOnlyList<SimilarPatternWindow> BuildWindows(
        IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>> candidateHistories,
        int targetYear,
        int targetMonth,
        int minimumHistoryMonths)
    {
        var targetKey = ToMonthKey(targetYear, targetMonth);
        return candidateHistories
            .SelectMany(x => BuildWindows(x.Key, x.Value, targetKey, minimumHistoryMonths))
            .ToList();
    }

    private static IEnumerable<SimilarPatternWindow> BuildWindows(string billIdentifier, IReadOnlyList<CustomerMonthlyConsumption> history, int targetKey, int minimumHistoryMonths)
    {
        var ordered = history
            .Where(x => ToMonthKey(x.Year, x.Month) < targetKey)
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToList();

        for (var comparableIndex = minimumHistoryMonths; comparableIndex < ordered.Count; comparableIndex++)
        {
            var comparable = ordered[comparableIndex];
            var comparableKey = ToMonthKey(comparable.Year, comparable.Month);
            if (comparableKey >= targetKey)
            {
                continue;
            }

            var windowHistory = ordered
                .Skip(comparableIndex - minimumHistoryMonths)
                .Take(minimumHistoryMonths)
                .ToList();
            if (!IsConsecutive(windowHistory, comparable))
            {
                continue;
            }

            var first = windowHistory.First();
            var last = windowHistory.Last();
            yield return new SimilarPatternWindow(
                billIdentifier,
                windowHistory,
                comparable.Consumption,
                comparable.Year,
                comparable.Month,
                first.Year,
                first.Month,
                last.Year,
                last.Month,
                comparableKey == targetKey);
        }
    }

    private static bool IsConsecutive(IReadOnlyList<CustomerMonthlyConsumption> history, CustomerMonthlyConsumption comparable)
    {
        if (history.Count == 0)
        {
            return false;
        }

        for (var i = 1; i < history.Count; i++)
        {
            if (ToMonthKey(history[i].Year, history[i].Month) != ToMonthKey(history[i - 1].Year, history[i - 1].Month) + 1)
            {
                return false;
            }
        }

        return ToMonthKey(comparable.Year, comparable.Month) == ToMonthKey(history[^1].Year, history[^1].Month) + 1;
    }

    private static int ToMonthKey(int year, int month) => year * 12 + month;
}

public sealed class ComparableMonthSeasonalityService : IComparableMonthSeasonalityService
{
    public decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, SimilarPatternWindow candidateWindow)
    {
        var targetSameMonth = targetHistory
            .Where(x => x.Month == candidateWindow.ComparableMonth)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .FirstOrDefault();
        if (targetSameMonth is null)
        {
            return 0.5m;
        }

        var denominator = Math.Max(Math.Abs(targetSameMonth.Consumption), 1m);
        return ConsumptionSimilarityService.Clamp01(1m - Math.Min(Math.Abs(targetSameMonth.Consumption - candidateWindow.ComparableConsumption) / denominator, 1m));
    }
}

public sealed class CustomerRecentConsumptionFeatureService : ICustomerRecentConsumptionFeatureService
{
    private readonly IForecastDataStore dataStore;
    private readonly IForecastConfigProvider configProvider;

    public CustomerRecentConsumptionFeatureService(IForecastDataStore dataStore, IForecastConfigProvider configProvider)
    {
        this.dataStore = dataStore;
        this.configProvider = configProvider;
    }

    public CustomerRecentConsumptionFeature BuildFeature(CustomerProfile profile, IReadOnlyList<CustomerMonthlyConsumption> history, int featureYear, int featureMonth)
    {
        var ordered = history.OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();
        var consumptions = ordered.Select(x => x.Consumption).ToList();
        var average = consumptions.Count == 0 ? 0m : consumptions.Average();
        var variance = consumptions.Count == 0 ? 0m : consumptions.Average(x => (x - average) * (x - average));
        var now = DateTime.UtcNow;

        return new CustomerRecentConsumptionFeature
        {
            BillIdentifier = profile.BillIdentifier,
            FeatureYear = featureYear,
            FeatureMonth = featureMonth,
            CoCode = profile.CoCode,
            RegionCode = profile.RegionCode,
            CityCode = profile.CityCode,
            TariffType = profile.TariffType,
            ClimateType = profile.ClimateType,
            Phase = profile.Phase,
            Ampere = profile.Ampere,
            MeterType = profile.MeterType,
            ValidMonthsCount = consumptions.Count,
            RecentAverageConsumption = average,
            RecentMinimumConsumption = consumptions.Count == 0 ? 0m : consumptions.Min(),
            RecentMaximumConsumption = consumptions.Count == 0 ? 0m : consumptions.Max(),
            RecentStdDevConsumption = (decimal)Math.Sqrt((double)variance),
            LastConsumption = consumptions.Count == 0 ? 0m : consumptions[^1],
            TrendSlope = CalculateTrendSlope(consumptions),
            ConsumptionBand = CalculateConsumptionBand(average),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public async Task<int> RebuildFeaturesAsync(int coCode, int featureYear, int featureMonth, int maximumCustomers, CancellationToken cancellationToken)
    {
        var options = await configProvider.GetOptionsAsync(cancellationToken);
        var customers = await dataStore.GetFeatureBuildCustomersAsync(coCode, Math.Min(maximumCustomers, options.MaxBatchCustomersLimit), cancellationToken);
        var count = 0;
        foreach (var customer in customers)
        {
            var history = await dataStore.GetValidHistoryAsync(customer.BillIdentifier, featureYear, featureMonth, options.MaximumHistoryMonths, cancellationToken);
            if (history.Count < options.MinimumHistoryMonths)
            {
                continue;
            }

            await dataStore.UpsertCustomerRecentConsumptionFeatureAsync(BuildFeature(customer, history, featureYear, featureMonth), cancellationToken);
            count++;
        }

        return count;
    }

    private static decimal CalculateTrendSlope(IReadOnlyList<decimal> consumptions)
    {
        if (consumptions.Count < 2)
        {
            return 0m;
        }

        return (consumptions[^1] - consumptions[0]) / (consumptions.Count - 1);
    }

    private static int CalculateConsumptionBand(decimal averageConsumption)
    {
        if (averageConsumption < 100m)
        {
            return 1;
        }

        if (averageConsumption < 200m)
        {
            return 2;
        }

        if (averageConsumption < 400m)
        {
            return 3;
        }

        if (averageConsumption < 800m)
        {
            return 4;
        }

        return 5;
    }
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
        AmpereToleranceRatio = source.AmpereToleranceRatio,
        MaxBatchCustomersLimit = source.MaxBatchCustomersLimit
    };
}

public sealed class ForecastRequestValidator : IForecastRequestValidator
{
    private readonly ForecastEngineOptions options;

    public ForecastRequestValidator() => options = new ForecastEngineOptions();

    public ForecastRequestValidator(IOptions<ForecastEngineOptions> options) => this.options = options.Value;

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
        else if (request.MaxCustomers > options.MaxBatchCustomersLimit)
        {
            errors.Add($"MaxCustomers must be less than or equal to {options.MaxBatchCustomersLimit}.");
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
