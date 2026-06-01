using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Interfaces;

/// <summary>Forecasts monthly consumption for a single subscriber.</summary>
public interface IEnhancedSimilarPatternForecastEngine
{
    Task<ForecastResponse> ForecastAsync(ForecastCustomerRequest request, long? runId = null, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates batch monthly forecast runs.</summary>
public interface IForecastBatchService
{
    Task<BatchForecastResponse> RunBatchAsync(BatchForecastRequest request, CancellationToken cancellationToken);
}

/// <summary>Reads previously stored forecast results and summary data.</summary>
public interface IForecastQueryService
{
    Task<ForecastResponse?> GetResultAsync(string billIdentifier, int year, int month, CancellationToken cancellationToken);
    Task<ForecastDashboardSummary> GetDashboardSummaryAsync(int coCode, int year, int month, CancellationToken cancellationToken);
}

/// <summary>Removes abnormal candidate readings while keeping the algorithm open for future detectors.</summary>
public interface IOutlierDetectionService
{
    OutlierDetectionResult RemoveOutliers(IReadOnlyList<ForecastCandidateScore> candidates, ForecastEngineOptions options);
}

/// <summary>Calculates confidence for a forecast from selected candidates and target data completeness.</summary>
public interface IForecastConfidenceService
{
    decimal Calculate(IReadOnlyList<ForecastCandidateScore> candidates, IReadOnlyList<CustomerMonthlyConsumption> targetHistory, CustomerProfile targetProfile, ForecastEngineOptions options);
}

/// <summary>Builds historical similar-pattern windows without leaking unavailable target-month readings.</summary>
public interface ISimilarPatternWindowProvider
{
    IReadOnlyList<SimilarPatternWindow> BuildWindows(
        IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>> candidateHistories,
        int targetYear,
        int targetMonth,
        int minimumHistoryMonths);
}

/// <summary>Scores seasonal agreement using the candidate window's comparable month.</summary>
public interface IComparableMonthSeasonalityService
{
    decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, SimilarPatternWindow candidateWindow);
}

/// <summary>Loads effective forecasting configuration from the database with appsettings defaults as fallback.</summary>
public interface IForecastConfigProvider
{
    Task<ForecastEngineOptions> GetOptionsAsync(CancellationToken cancellationToken);
}

/// <summary>Builds and stores recent consumption features used for fast candidate selection.</summary>
public interface ICustomerRecentConsumptionFeatureService
{
    CustomerRecentConsumptionFeature BuildFeature(CustomerProfile profile, IReadOnlyList<CustomerMonthlyConsumption> history, int featureYear, int featureMonth);
    Task<int> RebuildFeaturesAsync(int coCode, int featureYear, int featureMonth, int maximumCustomers, CancellationToken cancellationToken);
}

/// <summary>Validates API and application forecast requests.</summary>
public interface IForecastRequestValidator
{
    RequestValidationResult Validate(ForecastCustomerRequest request);
    RequestValidationResult Validate(BatchForecastRequest request);
}

public sealed record OutlierDetectionResult(
    IReadOnlyList<ForecastCandidateScore> IncludedCandidates,
    IReadOnlySet<ForecastCandidateScore> Outliers);
