using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;

namespace ElectricityConsumptionForecasting.Application.Services;

public sealed class ForecastBatchService : IForecastBatchService
{
    private readonly IForecastDataStore dataStore;
    private readonly IEnhancedSimilarPatternForecastEngine engine;

    public ForecastBatchService(IForecastDataStore dataStore, IEnhancedSimilarPatternForecastEngine engine)
    {
        this.dataStore = dataStore;
        this.engine = engine;
    }

    public async Task<BatchForecastResponse> RunBatchAsync(BatchForecastRequest request, CancellationToken cancellationToken)
    {
        var run = await dataStore.CreateForecastRunAsync(request, cancellationToken);
        var customers = await dataStore.GetBatchCustomersAsync(request, cancellationToken);
        run.TotalCustomers = customers.Count;

        foreach (var customer in customers)
        {
            try
            {
                var result = await engine.ForecastAsync(new ForecastCustomerRequest(customer.BillIdentifier, request.TargetYear, request.TargetMonth), run.Id, cancellationToken);
                if (result.IsForecastable)
                {
                    run.ForecastedCount++;
                }
                else
                {
                    run.NotForecastableCount++;
                }
            }
            catch
            {
                run.FailedCount++;
            }
        }

        run.Status = run.FailedCount == 0 ? "Completed" : "CompletedWithFailures";
        run.FinishedAt = DateTime.UtcNow;
        await dataStore.UpdateForecastRunAsync(run, cancellationToken);
        return new BatchForecastResponse(run.Id, run.CoCode, run.TargetYear, run.TargetMonth, run.TotalCustomers, run.ForecastedCount, run.NotForecastableCount, run.FailedCount, run.Status);
    }
}

public sealed class ForecastQueryService : IForecastQueryService
{
    private readonly IForecastDataStore dataStore;

    public ForecastQueryService(IForecastDataStore dataStore) => this.dataStore = dataStore;

    public async Task<ForecastResponse?> GetResultAsync(string billIdentifier, int year, int month, CancellationToken cancellationToken)
    {
        var result = await dataStore.GetForecastResultAsync(billIdentifier, year, month, cancellationToken);
        return result is null
            ? null
            : new ForecastResponse(
                result.BillIdentifier,
                result.TargetYear,
                result.TargetMonth,
                result.PredictedConsumption ?? result.ActualConsumption,
                result.Confidence,
                result.ConfidenceLevel,
                result.SimilarSubscribersCount,
                result.OutliersRemovedCount,
                result.MethodName,
                result.IsForecastable,
                result.RiskLevel,
                result.RequiresExpertReview,
                result.Reason,
                result.Warnings.Select(x => new ForecastWarningDto(x.Code, x.Message, x.Severity)).ToList());
    }

    public Task<ForecastDashboardSummary> GetDashboardSummaryAsync(int coCode, int year, int month, CancellationToken cancellationToken) =>
        dataStore.GetDashboardSummaryAsync(coCode, year, month, cancellationToken);
}
