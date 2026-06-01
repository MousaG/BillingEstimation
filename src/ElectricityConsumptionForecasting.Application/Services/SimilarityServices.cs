using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Services;

public sealed class ConsumptionSimilarityService : IConsumptionSimilarityService
{
    public decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, IReadOnlyList<CustomerMonthlyConsumption> candidateHistory)
    {
        var pairs = PairByMonth(targetHistory, candidateHistory).ToList();
        if (pairs.Count == 0)
        {
            return 0m;
        }

        var averageDifference = pairs.Average(p =>
        {
            var denominator = Math.Max(Math.Abs(p.Target.Consumption), 1m);
            return Math.Min(Math.Abs(p.Target.Consumption - p.Candidate.Consumption) / denominator, 1m);
        });

        return Clamp01(1m - averageDifference);
    }

    internal static IEnumerable<(CustomerMonthlyConsumption Target, CustomerMonthlyConsumption Candidate)> PairByMonth(
        IReadOnlyList<CustomerMonthlyConsumption> targetHistory,
        IReadOnlyList<CustomerMonthlyConsumption> candidateHistory)
    {
        var candidateLookup = candidateHistory.ToDictionary(x => (x.Year, x.Month));
        foreach (var target in targetHistory.OrderBy(x => x.Year).ThenBy(x => x.Month))
        {
            if (candidateLookup.TryGetValue((target.Year, target.Month), out var candidate))
            {
                yield return (target, candidate);
            }
        }
    }

    internal static decimal Clamp01(decimal value) => Math.Max(0m, Math.Min(1m, value));
}

public sealed class TrendSimilarityService : ITrendSimilarityService
{
    public decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, IReadOnlyList<CustomerMonthlyConsumption> candidateHistory)
    {
        var pairs = ConsumptionSimilarityService.PairByMonth(targetHistory, candidateHistory).ToList();
        if (pairs.Count < 2)
        {
            return 0.5m;
        }

        var matchingDirections = 0;
        var slopeScores = new List<decimal>();
        for (var i = 1; i < pairs.Count; i++)
        {
            var targetDelta = pairs[i].Target.Consumption - pairs[i - 1].Target.Consumption;
            var candidateDelta = pairs[i].Candidate.Consumption - pairs[i - 1].Candidate.Consumption;
            if (Math.Sign(targetDelta) == Math.Sign(candidateDelta))
            {
                matchingDirections++;
            }

            var denominator = Math.Max(Math.Abs(targetDelta), 1m);
            slopeScores.Add(ConsumptionSimilarityService.Clamp01(1m - Math.Min(Math.Abs(targetDelta - candidateDelta) / denominator, 1m)));
        }

        var directionScore = (decimal)matchingDirections / (pairs.Count - 1);
        return ConsumptionSimilarityService.Clamp01((directionScore + slopeScores.Average()) / 2m);
    }
}

public sealed class SeasonalSimilarityService : ISeasonalSimilarityService
{
    public decimal Calculate(IReadOnlyList<CustomerMonthlyConsumption> targetHistory, IReadOnlyList<CustomerMonthlyConsumption> candidateHistory, int targetMonth)
    {
        var targetSeasonal = targetHistory.Where(x => x.Month == targetMonth).ToList();
        var candidateSeasonal = candidateHistory.Where(x => x.Month == targetMonth).ToList();
        if (targetSeasonal.Count == 0 || candidateSeasonal.Count == 0)
        {
            return 0.5m;
        }

        var targetAverage = targetSeasonal.Average(x => x.Consumption);
        var candidateAverage = candidateSeasonal.Average(x => x.Consumption);
        var denominator = Math.Max(Math.Abs(targetAverage), 1m);
        return ConsumptionSimilarityService.Clamp01(1m - Math.Min(Math.Abs(targetAverage - candidateAverage) / denominator, 1m));
    }
}

public sealed class ProfileSimilarityService : IProfileSimilarityService
{
    public decimal Calculate(CustomerProfile targetProfile, CustomerProfile candidateProfile)
    {
        decimal score = 0m;
        score += targetProfile.TariffType == candidateProfile.TariffType ? 0.25m : 0m;
        score += targetProfile.Phase == candidateProfile.Phase ? 0.20m : 0m;
        score += targetProfile.MeterType == candidateProfile.MeterType ? 0.20m : 0m;
        score += targetProfile.UsageType == candidateProfile.UsageType ? 0.15m : 0m;

        var ampereDifference = Math.Abs(targetProfile.Ampere - candidateProfile.Ampere);
        score += ampereDifference == 0m ? 0.20m : Math.Max(0m, 0.20m - (ampereDifference / Math.Max(targetProfile.Ampere, 1m) * 0.20m));
        return ConsumptionSimilarityService.Clamp01(score);
    }
}

public sealed class GeographicSimilarityService : IGeographicSimilarityService
{
    public decimal Calculate(CustomerProfile targetProfile, CustomerProfile candidateProfile)
    {
        decimal score = 0m;
        score += targetProfile.ClimateType == candidateProfile.ClimateType ? 0.35m : 0m;
        score += targetProfile.CoCode == candidateProfile.CoCode ? 0.20m : 0m;
        score += targetProfile.RegionCode == candidateProfile.RegionCode ? 0.20m : 0m;
        score += targetProfile.CityCode.HasValue && targetProfile.CityCode == candidateProfile.CityCode ? 0.20m : 0m;
        score += targetProfile.IsUrban == candidateProfile.IsUrban ? 0.05m : 0m;
        return ConsumptionSimilarityService.Clamp01(score);
    }
}
