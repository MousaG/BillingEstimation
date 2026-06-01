using System.Globalization;
using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Services;
using Microsoft.Extensions.Options;

namespace ElectricityConsumptionForecasting.Infrastructure.Services;

public sealed class EfForecastConfigProvider : IForecastConfigProvider
{
    private readonly IForecastDataStore dataStore;
    private readonly IOptions<ForecastEngineOptions> defaults;

    public EfForecastConfigProvider(IForecastDataStore dataStore, IOptions<ForecastEngineOptions> defaults)
    {
        this.dataStore = dataStore;
        this.defaults = defaults;
    }

    public async Task<ForecastEngineOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var options = AppSettingsForecastConfigProvider.Clone(defaults.Value);
        var values = await dataStore.GetActiveForecastConfigValuesAsync(cancellationToken);

        ApplyInt(values, nameof(options.MinimumHistoryMonths), value => options.MinimumHistoryMonths = value);
        ApplyInt(values, nameof(options.MaximumHistoryMonths), value => options.MaximumHistoryMonths = value);
        ApplyInt(values, nameof(options.MinimumSimilarSubscribersTemperate), value => options.MinimumSimilarSubscribersTemperate = value);
        ApplyInt(values, nameof(options.MinimumSimilarSubscribersTropical), value => options.MinimumSimilarSubscribersTropical = value);
        ApplyInt(values, nameof(options.TopNSimilarSubscribers), value => options.TopNSimilarSubscribers = value);
        ApplyDecimal(values, nameof(options.HighConfidenceThreshold), value => options.HighConfidenceThreshold = value);
        ApplyDecimal(values, nameof(options.MediumConfidenceThreshold), value => options.MediumConfidenceThreshold = value);
        ApplyDecimal(values, nameof(options.LowConfidenceThreshold), value => options.LowConfidenceThreshold = value);
        ApplyDecimal(values, nameof(options.IqrOutlierMultiplier), value => options.IqrOutlierMultiplier = value);
        ApplyDecimal(values, nameof(options.MaxAllowedMonthlyGrowthRatio), value => options.MaxAllowedMonthlyGrowthRatio = value);
        ApplyDecimal(values, nameof(options.MaxAllowedMonthlyDropRatio), value => options.MaxAllowedMonthlyDropRatio = value);
        ApplyDecimal(values, nameof(options.ConsumptionSimilarityWeight), value => options.ConsumptionSimilarityWeight = value);
        ApplyDecimal(values, nameof(options.TrendSimilarityWeight), value => options.TrendSimilarityWeight = value);
        ApplyDecimal(values, nameof(options.SeasonalSimilarityWeight), value => options.SeasonalSimilarityWeight = value);
        ApplyDecimal(values, nameof(options.ProfileSimilarityWeight), value => options.ProfileSimilarityWeight = value);
        ApplyDecimal(values, nameof(options.GeographicSimilarityWeight), value => options.GeographicSimilarityWeight = value);
        ApplyDecimal(values, nameof(options.MinimumCandidateSimilarity), value => options.MinimumCandidateSimilarity = value);
        ApplyDecimal(values, nameof(options.ConsumptionBandToleranceRatio), value => options.ConsumptionBandToleranceRatio = value);
        ApplyDecimal(values, nameof(options.AmpereToleranceRatio), value => options.AmpereToleranceRatio = value);

        return options;
    }

    private static void ApplyInt(IReadOnlyDictionary<string, string> values, string name, Action<int> apply)
    {
        if (values.TryGetValue(name, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            apply(parsed);
        }
    }

    private static void ApplyDecimal(IReadOnlyDictionary<string, string> values, string name, Action<decimal> apply)
    {
        if (values.TryGetValue(name, out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            apply(parsed);
        }
    }
}
