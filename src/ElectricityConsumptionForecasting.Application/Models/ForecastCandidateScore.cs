namespace ElectricityConsumptionForecasting.Application.Models;

public sealed record ForecastCandidateScore(
    string BillIdentifier,
    decimal SimilarityScore,
    decimal ConsumptionSimilarity,
    decimal TrendSimilarity,
    decimal SeasonalSimilarity,
    decimal ProfileSimilarity,
    decimal GeographicSimilarity,
    decimal TargetMonthConsumption,
    int ComparableYear,
    int ComparableMonth,
    int HistoryStartYear,
    int HistoryStartMonth,
    int HistoryEndYear,
    int HistoryEndMonth);
