namespace ElectricityConsumptionForecasting.Application.Dtos;

public sealed record ForecastCustomerRequest(
    string BillIdentifier,
    int TargetYear,
    int TargetMonth,
    bool UseSmartMeterIfAvailable = true);

public sealed record BatchForecastRequest(
    int CoCode,
    int TargetYear,
    int TargetMonth,
    int? TariffType,
    int MaxCustomers = 100000);

public sealed record ForecastWarningDto(string Code, string Message, string Severity);

public sealed record ForecastResponse(
    string BillIdentifier,
    int TargetYear,
    int TargetMonth,
    decimal? PredictedConsumption,
    decimal Confidence,
    string ConfidenceLevel,
    int SimilarSubscribersCount,
    int OutliersRemoved,
    string Method,
    bool IsForecastable,
    string RiskLevel,
    bool RequiresExpertReview,
    string Reason,
    IReadOnlyCollection<ForecastWarningDto> Warnings);

public sealed record BatchForecastResponse(
    long RunId,
    int CoCode,
    int TargetYear,
    int TargetMonth,
    int TotalCustomers,
    int ForecastedCount,
    int NotForecastableCount,
    int FailedCount,
    string Status);

public sealed record ForecastDashboardSummary(
    int CoCode,
    int TargetYear,
    int TargetMonth,
    int TotalResults,
    int ForecastableCount,
    int ExpertReviewCount,
    decimal AverageConfidence);
