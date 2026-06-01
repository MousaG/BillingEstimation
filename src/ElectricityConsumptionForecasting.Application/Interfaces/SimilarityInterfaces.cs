using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Interfaces;

/// <summary>Compares historical consumption vectors using normalized distance.</summary>
public interface IConsumptionSimilarityService
{
    decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, IReadOnlyList<CustomerMonthlyConsumption> candidateHistory);
}

/// <summary>Compares aligned rolling windows by position rather than calendar month.</summary>
public interface IPositionalWindowSimilarityService
{
    WindowSimilarityScore Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetWindow, IReadOnlyList<CustomerMonthlyConsumption> candidateWindow);
}

public sealed record WindowSimilarityScore(decimal ConsumptionSimilarity, decimal TrendSimilarity);

/// <summary>Compares month-to-month movement and slope.</summary>
public interface ITrendSimilarityService
{
    decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, IReadOnlyList<CustomerMonthlyConsumption> candidateHistory);
}

/// <summary>Compares seasonal behavior for matching months when available.</summary>
public interface ISeasonalSimilarityService
{
    decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, IReadOnlyList<CustomerMonthlyConsumption> candidateHistory, int targetMonth);
}

/// <summary>Scores similarity between customer profile attributes.</summary>
public interface IProfileSimilarityService
{
    decimal Calculate(CustomerProfile targetProfile, CustomerProfile candidateProfile);
}

/// <summary>Scores geographic proximity between subscribers.</summary>
public interface IGeographicSimilarityService
{
    decimal Calculate(CustomerProfile targetProfile, CustomerProfile candidateProfile);
}
