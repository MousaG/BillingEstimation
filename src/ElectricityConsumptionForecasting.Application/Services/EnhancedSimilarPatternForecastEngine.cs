using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ElectricityConsumptionForecasting.Application.Services;

/// <summary>Initial Enhanced Similar Pattern implementation using EF-selected candidates and in-memory scoring.</summary>
public sealed class EnhancedSimilarPatternForecastEngine : IEnhancedSimilarPatternForecastEngine
{
    private readonly IForecastDataStore dataStore;
    private readonly IConsumptionSimilarityService consumptionSimilarity;
    private readonly ITrendSimilarityService trendSimilarity;
    private readonly ISeasonalSimilarityService seasonalSimilarity;
    private readonly IProfileSimilarityService profileSimilarity;
    private readonly IGeographicSimilarityService geographicSimilarity;
    private readonly IOutlierDetectionService outlierDetection;
    private readonly IForecastConfidenceService confidenceService;
    private readonly ForecastEngineOptions options;

    public EnhancedSimilarPatternForecastEngine(
        IForecastDataStore dataStore,
        IConsumptionSimilarityService consumptionSimilarity,
        ITrendSimilarityService trendSimilarity,
        ISeasonalSimilarityService seasonalSimilarity,
        IProfileSimilarityService profileSimilarity,
        IGeographicSimilarityService geographicSimilarity,
        IOutlierDetectionService outlierDetection,
        IForecastConfidenceService confidenceService,
        IOptions<ForecastEngineOptions> options)
    {
        this.dataStore = dataStore;
        this.consumptionSimilarity = consumptionSimilarity;
        this.trendSimilarity = trendSimilarity;
        this.seasonalSimilarity = seasonalSimilarity;
        this.profileSimilarity = profileSimilarity;
        this.geographicSimilarity = geographicSimilarity;
        this.outlierDetection = outlierDetection;
        this.confidenceService = confidenceService;
        this.options = options.Value;
    }

    public async Task<ForecastResponse> ForecastAsync(ForecastCustomerRequest request, long? runId = null, CancellationToken cancellationToken = default)
    {
        var profile = await dataStore.GetCustomerProfileAsync(request.BillIdentifier, cancellationToken);
        if (profile is null)
        {
            return await SaveAndReturnAsync(NotForecastable(request, "Customer profile not found", "PROFILE_NOT_FOUND"), null, runId, cancellationToken);
        }

        var actual = await dataStore.GetActualConsumptionAsync(request.BillIdentifier, request.TargetYear, request.TargetMonth, request.UseSmartMeterIfAvailable, cancellationToken);
        if (actual is not null)
        {
            var actualResponse = new ForecastResponse(request.BillIdentifier, request.TargetYear, request.TargetMonth, actual.Consumption, 1m, "High", 0, 0, "ActualReading", true, "Low", false, "Valid actual reading exists", []);
            return await SaveAndReturnAsync(actualResponse, profile, runId, cancellationToken, actual.Consumption);
        }

        var targetHistory = await dataStore.GetValidHistoryAsync(request.BillIdentifier, request.TargetYear, request.TargetMonth, options.MaximumHistoryMonths, cancellationToken);
        if (targetHistory.Count < options.MinimumHistoryMonths)
        {
            return await SaveAndReturnAsync(NotForecastable(request, "Insufficient valid history", "INSUFFICIENT_HISTORY"), profile, runId, cancellationToken);
        }

        var candidates = await dataStore.GetCandidateProfilesAsync(profile, options.TopNSimilarSubscribers * 5, cancellationToken);
        var candidateIds = candidates.Select(x => x.BillIdentifier).ToArray();
        var histories = await dataStore.GetValidHistoryForCustomersAsync(candidateIds, request.TargetYear, request.TargetMonth, options.MaximumHistoryMonths, cancellationToken);
        var targetConsumptions = await dataStore.GetActualConsumptionForCustomersAsync(candidateIds, request.TargetYear, request.TargetMonth, cancellationToken);

        var scored = candidates.Select(candidate =>
            {
                histories.TryGetValue(candidate.BillIdentifier, out var candidateHistory);
                targetConsumptions.TryGetValue(candidate.BillIdentifier, out var targetConsumption);
                if (candidateHistory is null || candidateHistory.Count < options.MinimumHistoryMonths || targetConsumption is null)
                {
                    return null;
                }

                var consumptionScore = consumptionSimilarity.Calculate(targetHistory, candidateHistory);
                var trendScore = trendSimilarity.Calculate(targetHistory, candidateHistory);
                var seasonalScore = seasonalSimilarity.Calculate(targetHistory, candidateHistory, request.TargetMonth);
                var profileScore = profileSimilarity.Calculate(profile, candidate);
                var geographicScore = geographicSimilarity.Calculate(profile, candidate);
                var composite = Composite(consumptionScore, trendScore, seasonalScore, profileScore, geographicScore);

                return new ForecastCandidateScore(candidate.BillIdentifier, composite, consumptionScore, trendScore, seasonalScore, profileScore, geographicScore, targetConsumption.Consumption);
            })
            .Where(x => x is not null && x.SimilarityScore >= options.MinimumCandidateSimilarity)
            .Cast<ForecastCandidateScore>()
            .OrderByDescending(x => x.SimilarityScore)
            .Take(options.TopNSimilarSubscribers)
            .ToList();

        var minimumSimilar = MinimumSimilarSubscribers(profile.ClimateType);
        if (scored.Count < minimumSimilar)
        {
            return await SaveAndReturnAsync(NotForecastable(request, "Not enough similar subscribers", "LOW_SIMILAR_COUNT", scored.Count), profile, runId, cancellationToken);
        }

        var outlierResult = outlierDetection.RemoveOutliers(scored);
        var withoutOutliers = outlierResult.IncludedCandidates;
        var outlierCount = outlierResult.Outliers.Count;
        if (withoutOutliers.Count < minimumSimilar)
        {
            var response = NotForecastable(request, "Not enough similar subscribers after outlier removal", "LOW_SIMILAR_COUNT_AFTER_OUTLIERS", withoutOutliers.Count, outlierCount);
            return await SaveAndReturnAsync(response, profile, runId, cancellationToken, similarCandidates: scored, outliers: outlierResult.Outliers);
        }

        var weightTotal = withoutOutliers.Sum(x => Math.Max(x.SimilarityScore, 0.01m));
        var predicted = withoutOutliers.Sum(x => x.TargetMonthConsumption * Math.Max(x.SimilarityScore, 0.01m)) / weightTotal;
        var confidence = confidenceService.Calculate(withoutOutliers, targetHistory, profile);
        var level = ConfidenceLevel(confidence);
        var warnings = ValidatePrediction(targetHistory, predicted);
        var requiresReview = confidence < options.MediumConfidenceThreshold || warnings.Any(x => x.Severity == "High");
        var forecastResponse = new ForecastResponse(
            request.BillIdentifier,
            request.TargetYear,
            request.TargetMonth,
            Math.Round(predicted, 2),
            Math.Round(confidence, 2),
            level,
            withoutOutliers.Count,
            outlierCount,
            "EnhancedSimilarPattern",
            confidence >= options.LowConfidenceThreshold && !warnings.Any(x => x.Severity == "High"),
            RiskLevel(confidence, warnings),
            requiresReview,
            "Similar consumption pattern found in same climate, tariff, phase and consumption band",
            warnings);

        return await SaveAndReturnAsync(forecastResponse, profile, runId, cancellationToken, similarCandidates: scored, outliers: outlierResult.Outliers);
    }

    private decimal Composite(decimal consumption, decimal trend, decimal seasonal, decimal profile, decimal geographic) =>
        consumption * options.ConsumptionSimilarityWeight +
        trend * options.TrendSimilarityWeight +
        seasonal * options.SeasonalSimilarityWeight +
        profile * options.ProfileSimilarityWeight +
        geographic * options.GeographicSimilarityWeight;

    private int MinimumSimilarSubscribers(int climateType) =>
        climateType == 2 ? options.MinimumSimilarSubscribersTropical : options.MinimumSimilarSubscribersTemperate;

    private string ConfidenceLevel(decimal confidence) =>
        confidence >= options.HighConfidenceThreshold ? "High" :
        confidence >= options.MediumConfidenceThreshold ? "Medium" :
        confidence >= options.LowConfidenceThreshold ? "Low" : "VeryLow";

    private string RiskLevel(decimal confidence, IReadOnlyCollection<ForecastWarningDto> warnings)
    {
        if (warnings.Any(x => x.Severity == "High") || confidence < options.LowConfidenceThreshold)
        {
            return "High";
        }

        return confidence < options.MediumConfidenceThreshold ? "Medium" : "Low";
    }

    private IReadOnlyCollection<ForecastWarningDto> ValidatePrediction(IReadOnlyList<CustomerMonthlyConsumption> history, decimal predicted)
    {
        var warnings = new List<ForecastWarningDto>();
        var last = history.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();
        if (last is null || last.Consumption <= 0m)
        {
            return warnings;
        }

        var ratio = predicted / last.Consumption;
        if (ratio > options.MaxAllowedMonthlyGrowthRatio)
        {
            warnings.Add(new ForecastWarningDto("ABNORMAL_GROWTH", "Predicted consumption exceeds allowed monthly growth ratio.", "High"));
        }
        else if (ratio < options.MaxAllowedMonthlyDropRatio)
        {
            warnings.Add(new ForecastWarningDto("ABNORMAL_DROP", "Predicted consumption is below allowed monthly drop ratio.", "High"));
        }

        return warnings;
    }

    private static ForecastResponse NotForecastable(ForecastCustomerRequest request, string reason, string warningCode, int similarCount = 0, int outliersRemoved = 0) =>
        new(request.BillIdentifier, request.TargetYear, request.TargetMonth, null, 0m, "VeryLow", similarCount, outliersRemoved, "EnhancedSimilarPattern", false, "High", true, reason, [new ForecastWarningDto(warningCode, reason, "High")]);

    private async Task<ForecastResponse> SaveAndReturnAsync(
        ForecastResponse response,
        CustomerProfile? profile,
        long? runId,
        CancellationToken cancellationToken,
        decimal? actualConsumption = null,
        IReadOnlyCollection<ForecastCandidateScore>? similarCandidates = null,
        IReadOnlySet<ForecastCandidateScore>? outliers = null)
    {
        var result = new ForecastResult
        {
            RunId = runId,
            BillIdentifier = response.BillIdentifier,
            CoCode = profile?.CoCode ?? 0,
            TargetYear = response.TargetYear,
            TargetMonth = response.TargetMonth,
            PredictedConsumption = response.Method == "ActualReading" ? null : response.PredictedConsumption,
            ActualConsumption = actualConsumption,
            Confidence = response.Confidence,
            ConfidenceLevel = response.ConfidenceLevel,
            SimilarSubscribersCount = response.SimilarSubscribersCount,
            OutliersRemovedCount = response.OutliersRemoved,
            IsForecastable = response.IsForecastable,
            RequiresExpertReview = response.RequiresExpertReview,
            RiskLevel = response.RiskLevel,
            MethodName = response.Method,
            Reason = response.Reason,
            Warnings = response.Warnings.Select(x => new ForecastWarning { Code = x.Code, Message = x.Message, Severity = x.Severity }).ToList()
        };

        if (similarCandidates is not null)
        {
            foreach (var candidate in similarCandidates)
            {
                var isOutlier = outliers?.Contains(candidate) == true;
                result.SimilarSubscribers.Add(new ForecastSimilarSubscriber
                {
                    SimilarBillIdentifier = candidate.BillIdentifier,
                    SimilarityScore = candidate.SimilarityScore,
                    ConsumptionSimilarity = candidate.ConsumptionSimilarity,
                    TrendSimilarity = candidate.TrendSimilarity,
                    SeasonalSimilarity = candidate.SeasonalSimilarity,
                    ProfileSimilarity = candidate.ProfileSimilarity,
                    GeographicSimilarity = candidate.GeographicSimilarity,
                    SimilarMonthConsumption = candidate.TargetMonthConsumption,
                    Weight = candidate.SimilarityScore,
                    IsOutlier = isOutlier
                });
            }
        }

        await dataStore.SaveForecastResultAsync(result, cancellationToken);
        return response;
    }

}
