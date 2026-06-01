using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Interfaces;

/// <summary>Provides persistence operations required by the forecasting application services.</summary>
public interface IForecastDataStore
{
    Task<CustomerProfile?> GetCustomerProfileAsync(string billIdentifier, CancellationToken cancellationToken);
    Task<CustomerMonthlyConsumption?> GetActualConsumptionAsync(string billIdentifier, int year, int month, bool includeSmartReading, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerMonthlyConsumption>> GetValidHistoryAsync(string billIdentifier, int targetYear, int targetMonth, int maximumMonths, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerProfile>> GetCandidateProfilesAsync(CandidateSelectionCriteria criteria, int maxCandidates, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>>> GetValidHistoryForCustomersAsync(IEnumerable<string> billIdentifiers, int targetYear, int targetMonth, int maximumMonths, CancellationToken cancellationToken);
    Task SaveForecastResultAsync(ForecastResult result, CancellationToken cancellationToken);
    Task<ForecastResult?> GetForecastResultAsync(string billIdentifier, int year, int month, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerProfile>> GetBatchCustomersAsync(BatchForecastRequest request, CancellationToken cancellationToken);
    Task<ForecastRun> CreateForecastRunAsync(BatchForecastRequest request, CancellationToken cancellationToken);
    Task UpdateForecastRunAsync(ForecastRun run, CancellationToken cancellationToken);
    Task<ForecastDashboardSummary> GetDashboardSummaryAsync(int coCode, int year, int month, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> GetActiveForecastConfigValuesAsync(CancellationToken cancellationToken);
}
