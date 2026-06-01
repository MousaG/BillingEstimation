using ElectricityConsumptionForecasting.Application.Dtos;

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
