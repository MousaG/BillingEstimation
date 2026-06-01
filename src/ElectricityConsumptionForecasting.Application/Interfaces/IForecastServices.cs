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
    OutlierDetectionResult RemoveOutliers(IReadOnlyList<ForecastCandidateScore> candidates);
}

/// <summary>Calculates confidence for a forecast from selected candidates and target data completeness.</summary>
public interface IForecastConfidenceService
{
    decimal Calculate(IReadOnlyList<ForecastCandidateScore> candidates, IReadOnlyList<CustomerMonthlyConsumption> targetHistory, CustomerProfile targetProfile);
}

public sealed record OutlierDetectionResult(
    IReadOnlyList<ForecastCandidateScore> IncludedCandidates,
    IReadOnlySet<ForecastCandidateScore> Outliers);
