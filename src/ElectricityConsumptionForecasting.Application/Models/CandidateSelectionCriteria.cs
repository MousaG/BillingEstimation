using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Models;

public sealed record CandidateSelectionCriteria(
    CustomerProfile TargetProfile,
    int TargetYear,
    int TargetMonth,
    int MaximumHistoryMonths,
    decimal TargetRecentAverageConsumption,
    decimal ConsumptionBandToleranceRatio,
    decimal AmpereToleranceRatio);
