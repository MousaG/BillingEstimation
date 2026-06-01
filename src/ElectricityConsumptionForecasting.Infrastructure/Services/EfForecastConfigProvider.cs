using System.Globalization;
using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElectricityConsumptionForecasting.Infrastructure.Services;

public sealed class EfForecastConfigProvider : IForecastConfigProvider
{
    private readonly IForecastDataStore dataStore;
    private readonly IOptions<ForecastEngineOptions> defaults;
    private readonly ILogger<EfForecastConfigProvider> logger;

    public EfForecastConfigProvider(IForecastDataStore dataStore, IOptions<ForecastEngineOptions> defaults, ILogger<EfForecastConfigProvider> logger)
    {
        this.dataStore = dataStore;
        this.defaults = defaults;
        this.logger = logger;
    }

    public async Task<ForecastEngineOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var options = AppSettingsForecastConfigProvider.Clone(defaults.Value);
        var values = await dataStore.GetActiveForecastConfigValuesAsync(cancellationToken);

        ApplyInt(values, nameof(options.MinimumHistoryMonths), value => options.MinimumHistoryMonths = value, value => value > 0);
        ApplyInt(values, nameof(options.MaximumHistoryMonths), value => options.MaximumHistoryMonths = value, value => value > 0);
        ApplyInt(values, nameof(options.MinimumSimilarSubscribersTemperate), value => options.MinimumSimilarSubscribersTemperate = value, value => value > 0);
        ApplyInt(values, nameof(options.MinimumSimilarSubscribersTropical), value => options.MinimumSimilarSubscribersTropical = value, value => value > 0);
        ApplyInt(values, nameof(options.TopNSimilarSubscribers), value => options.TopNSimilarSubscribers = value, value => value > 0);
        ApplyInt(values, nameof(options.MaxBatchCustomersLimit), value => options.MaxBatchCustomersLimit = value, value => value > 0);
        ApplyDecimal(values, nameof(options.HighConfidenceThreshold), value => options.HighConfidenceThreshold = value, IsRatio);
        ApplyDecimal(values, nameof(options.MediumConfidenceThreshold), value => options.MediumConfidenceThreshold = value, IsRatio);
        ApplyDecimal(values, nameof(options.LowConfidenceThreshold), value => options.LowConfidenceThreshold = value, IsRatio);
        ApplyDecimal(values, nameof(options.IqrOutlierMultiplier), value => options.IqrOutlierMultiplier = value, value => value > 0m);
        ApplyDecimal(values, nameof(options.MaxAllowedMonthlyGrowthRatio), value => options.MaxAllowedMonthlyGrowthRatio = value, value => value > 0m);
        ApplyDecimal(values, nameof(options.MaxAllowedMonthlyDropRatio), value => options.MaxAllowedMonthlyDropRatio = value, IsRatio);
        ApplyDecimal(values, nameof(options.ConsumptionSimilarityWeight), value => options.ConsumptionSimilarityWeight = value, IsRatio);
        ApplyDecimal(values, nameof(options.TrendSimilarityWeight), value => options.TrendSimilarityWeight = value, IsRatio);
        ApplyDecimal(values, nameof(options.SeasonalSimilarityWeight), value => options.SeasonalSimilarityWeight = value, IsRatio);
        ApplyDecimal(values, nameof(options.ProfileSimilarityWeight), value => options.ProfileSimilarityWeight = value, IsRatio);
        ApplyDecimal(values, nameof(options.GeographicSimilarityWeight), value => options.GeographicSimilarityWeight = value, IsRatio);
        ApplyDecimal(values, nameof(options.MinimumCandidateSimilarity), value => options.MinimumCandidateSimilarity = value, IsRatio);
        ApplyDecimal(values, nameof(options.ConsumptionBandToleranceRatio), value => options.ConsumptionBandToleranceRatio = value, value => value >= 0m);
        ApplyDecimal(values, nameof(options.AmpereToleranceRatio), value => options.AmpereToleranceRatio = value, value => value >= 0m);

        return options;
    }

    private void ApplyInt(IReadOnlyDictionary<string, string> values, string name, Action<int> apply, Func<int, bool> isValid)
    {
        if (!values.TryGetValue(name, out var raw))
        {
            return;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && isValid(parsed))
        {
            apply(parsed);
            return;
        }

        logger.LogWarning("Ignoring invalid ForecastConfig value {Name}={Value}", name, raw);
    }

    private void ApplyDecimal(IReadOnlyDictionary<string, string> values, string name, Action<decimal> apply, Func<decimal, bool> isValid)
    {
        if (!values.TryGetValue(name, out var raw))
        {
            return;
        }

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && isValid(parsed))
        {
            apply(parsed);
            return;
        }

        logger.LogWarning("Ignoring invalid ForecastConfig value {Name}={Value}", name, raw);
    }

    private static bool IsRatio(decimal value) => value >= 0m && value <= 1m;
}
