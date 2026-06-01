using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Application.Services;

/// <summary>Initial Enhanced Similar Pattern implementation using EF-selected candidates and in-memory scoring.</summary>
public sealed class EnhancedSimilarPatternForecastEngine : IEnhancedSimilarPatternForecastEngine
{
    private readonly IForecastDataStore dataStore;
    private readonly IPositionalWindowSimilarityService positionalWindowSimilarity;
    private readonly IComparableMonthSeasonalityService seasonalityService;
    private readonly IProfileSimilarityService profileSimilarity;
    private readonly IGeographicSimilarityService geographicSimilarity;
    private readonly IOutlierDetectionService outlierDetection;
    private readonly IForecastConfidenceService confidenceService;
    private readonly ISimilarPatternWindowProvider windowProvider;
    private readonly IForecastConfigProvider configProvider;
    private readonly IForecastRequestValidator requestValidator;

    public EnhancedSimilarPatternForecastEngine(
        IForecastDataStore dataStore,
        IPositionalWindowSimilarityService positionalWindowSimilarity,
        IComparableMonthSeasonalityService seasonalityService,
        IProfileSimilarityService profileSimilarity,
        IGeographicSimilarityService geographicSimilarity,
        IOutlierDetectionService outlierDetection,
        IForecastConfidenceService confidenceService,
        ISimilarPatternWindowProvider windowProvider,
        IForecastConfigProvider configProvider,
        IForecastRequestValidator requestValidator)
    {
        this.dataStore = dataStore;
        this.positionalWindowSimilarity = positionalWindowSimilarity;
        this.seasonalityService = seasonalityService;
        this.profileSimilarity = profileSimilarity;
        this.geographicSimilarity = geographicSimilarity;
        this.outlierDetection = outlierDetection;
        this.confidenceService = confidenceService;
        this.windowProvider = windowProvider;
        this.configProvider = configProvider;
        this.requestValidator = requestValidator;
    }

    public async Task<ForecastResponse> ForecastAsync(ForecastCustomerRequest request, long? runId = null, CancellationToken cancellationToken = default)
    {
        var validation = requestValidator.Validate(request);
        if (!validation.IsValid)
        {
            return NotForecastable(request, string.Join(" ", validation.Errors), "INVALID_REQUEST");
        }

        var options = await configProvider.GetOptionsAsync(cancellationToken);
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

        var targetAverage = targetHistory.Average(x => x.Consumption);
        var criteria = new CandidateSelectionCriteria(profile, request.TargetYear, request.TargetMonth, options.MaximumHistoryMonths, targetAverage, options.ConsumptionBandToleranceRatio, options.AmpereToleranceRatio);
        var candidates = await dataStore.GetCandidateProfilesAsync(criteria, options.TopNSimilarSubscribers * 5, cancellationToken);
        var candidateIds = candidates.Select(x => x.BillIdentifier).ToArray();
        var histories = await dataStore.GetValidHistoryForCustomersAsync(candidateIds, request.TargetYear, request.TargetMonth, options.MaximumHistoryMonths, cancellationToken);
        var windows = windowProvider.BuildWindows(histories, request.TargetYear, request.TargetMonth, options.MinimumHistoryMonths);
        var candidateLookup = candidates.ToDictionary(x => x.BillIdentifier);

        var scored = windows.Select(window =>
            {
                if (window.UsedActualForecastTargetMonth || !candidateLookup.TryGetValue(window.BillIdentifier, out var candidate))
                {
                    return null;
                }

                var comparableTargetHistory = targetHistory
                    .OrderBy(x => x.Year)
                    .ThenBy(x => x.Month)
                    .TakeLast(window.History.Count)
                    .ToList();
                var positionalScore = positionalWindowSimilarity.Calculate(comparableTargetHistory, window.History);
                var consumptionScore = positionalScore.ConsumptionSimilarity;
                var trendScore = positionalScore.TrendSimilarity;
                var seasonalScore = seasonalityService.Calculate(comparableTargetHistory, window);
                var profileScore = profileSimilarity.Calculate(profile, candidate);
                var geographicScore = geographicSimilarity.Calculate(profile, candidate);
                var composite = Composite(consumptionScore, trendScore, seasonalScore, profileScore, geographicScore, options);

                return new ForecastCandidateScore(
                    candidate.BillIdentifier,
                    composite,
                    consumptionScore,
                    trendScore,
                    seasonalScore,
                    profileScore,
                    geographicScore,
                    window.ComparableConsumption,
                    window.ComparableYear,
                    window.ComparableMonth,
                    window.HistoryStartYear,
                    window.HistoryStartMonth,
                    window.HistoryEndYear,
                    window.HistoryEndMonth);
            })
            .Where(x => x is not null && x.SimilarityScore >= options.MinimumCandidateSimilarity)
            .Cast<ForecastCandidateScore>()
            .OrderByDescending(x => x.SimilarityScore)
            .Take(options.TopNSimilarSubscribers)
            .ToList();

        var minimumSimilar = MinimumSimilarSubscribers(profile.ClimateType, options);
        if (scored.Count < minimumSimilar)
        {
            return await SaveAndReturnAsync(NotForecastable(request, "Not enough similar subscribers", "LOW_SIMILAR_COUNT", scored.Count), profile, runId, cancellationToken);
        }

        var outlierResult = outlierDetection.RemoveOutliers(scored, options);
        var withoutOutliers = outlierResult.IncludedCandidates;
        var outlierCount = outlierResult.Outliers.Count;
        if (withoutOutliers.Count < minimumSimilar)
        {
            var response = NotForecastable(request, "Not enough similar subscribers after outlier removal", "LOW_SIMILAR_COUNT_AFTER_OUTLIERS", withoutOutliers.Count, outlierCount);
            return await SaveAndReturnAsync(response, profile, runId, cancellationToken, similarCandidates: scored, outliers: outlierResult.Outliers);
        }

        var weightTotal = withoutOutliers.Sum(x => Math.Max(x.SimilarityScore, 0.01m));
        var predicted = withoutOutliers.Sum(x => x.TargetMonthConsumption * Math.Max(x.SimilarityScore, 0.01m)) / weightTotal;
        var confidence = confidenceService.Calculate(withoutOutliers, targetHistory, profile, options);
        var level = ConfidenceLevel(confidence, options);
        var warnings = ValidatePrediction(targetHistory, predicted, options);
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
            RiskLevel(confidence, warnings, options),
            requiresReview,
            "Similar consumption pattern found in same climate, tariff, phase and consumption band",
            warnings);

        return await SaveAndReturnAsync(forecastResponse, profile, runId, cancellationToken, similarCandidates: scored, outliers: outlierResult.Outliers);
    }

    private static decimal Composite(decimal consumption, decimal trend, decimal seasonal, decimal profile, decimal geographic, ForecastEngineOptions options) =>
        consumption * options.ConsumptionSimilarityWeight +
        trend * options.TrendSimilarityWeight +
        seasonal * options.SeasonalSimilarityWeight +
        profile * options.ProfileSimilarityWeight +
        geographic * options.GeographicSimilarityWeight;

    private static int MinimumSimilarSubscribers(int climateType, ForecastEngineOptions options) =>
        climateType == 2 ? options.MinimumSimilarSubscribersTropical : options.MinimumSimilarSubscribersTemperate;

    private static string ConfidenceLevel(decimal confidence, ForecastEngineOptions options) =>
        confidence >= options.HighConfidenceThreshold ? "High" :
        confidence >= options.MediumConfidenceThreshold ? "Medium" :
        confidence >= options.LowConfidenceThreshold ? "Low" : "VeryLow";

    private static string RiskLevel(decimal confidence, IReadOnlyCollection<ForecastWarningDto> warnings, ForecastEngineOptions options)
    {
        if (warnings.Any(x => x.Severity == "High") || confidence < options.LowConfidenceThreshold)
        {
            return "High";
        }

        return confidence < options.MediumConfidenceThreshold ? "Medium" : "Low";
    }

    private static IReadOnlyCollection<ForecastWarningDto> ValidatePrediction(IReadOnlyList<CustomerMonthlyConsumption> history, decimal predicted, ForecastEngineOptions options)
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
                    ComparableYear = candidate.ComparableYear,
                    ComparableMonth = candidate.ComparableMonth,
                    HistoryStartYear = candidate.HistoryStartYear,
                    HistoryStartMonth = candidate.HistoryStartMonth,
                    HistoryEndYear = candidate.HistoryEndYear,
                    HistoryEndMonth = candidate.HistoryEndMonth,
                    Weight = candidate.SimilarityScore,
                    IsOutlier = isOutlier
                });
            }
        }

        await dataStore.SaveForecastResultAsync(result, cancellationToken);
        return response;
    }

}
