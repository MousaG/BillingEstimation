using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Models;

public sealed record SimilarPatternWindow(
    string BillIdentifier,
    IReadOnlyList<CustomerMonthlyConsumption> History,
    decimal ComparableConsumption,
    int ComparableYear,
    int ComparableMonth,
    bool UsedActualForecastTargetMonth);
