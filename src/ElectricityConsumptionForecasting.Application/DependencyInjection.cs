using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElectricityConsumptionForecasting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForecastEngineOptions>(configuration.GetSection(ForecastEngineOptions.SectionName));
        services.AddScoped<IConsumptionSimilarityService, ConsumptionSimilarityService>();
        services.AddScoped<IPositionalWindowSimilarityService, PositionalWindowSimilarityService>();
        services.AddScoped<ITrendSimilarityService, TrendSimilarityService>();
        services.AddScoped<ISeasonalSimilarityService, SeasonalSimilarityService>();
        services.AddScoped<IComparableMonthSeasonalityService, ComparableMonthSeasonalityService>();
        services.AddScoped<IProfileSimilarityService, ProfileSimilarityService>();
        services.AddScoped<IGeographicSimilarityService, GeographicSimilarityService>();
        services.AddScoped<IOutlierDetectionService, IqrOutlierDetectionService>();
        services.AddScoped<IForecastConfidenceService, ForecastConfidenceService>();
        services.AddScoped<ISimilarPatternWindowProvider, SimilarPatternWindowProvider>();
        services.AddScoped<ICustomerRecentConsumptionFeatureService, CustomerRecentConsumptionFeatureService>();
        services.AddScoped<IForecastConfigProvider, AppSettingsForecastConfigProvider>();
        services.AddScoped<IForecastRequestValidator, ForecastRequestValidator>();
        services.AddScoped<IEnhancedSimilarPatternForecastEngine, EnhancedSimilarPatternForecastEngine>();
        services.AddScoped<IForecastBatchService, ForecastBatchService>();
        services.AddScoped<IForecastQueryService, ForecastQueryService>();
        return services;
    }
}
