using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Application.Services;
using ElectricityConsumptionForecasting.Domain.Entities;

namespace ElectricityConsumptionForecasting.Tests;

public sealed class EnhancedSimilarPatternForecastEngineTests
{
    [Fact]
    public async Task Actual_reading_exists_returns_actual_reading()
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        store.Consumptions.Add(Consumption("target", 1403, 8, 186.4m, actual: true));

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.Equal("ActualReading", result.Method);
        Assert.Equal(1m, result.Confidence);
        Assert.Equal(186.4m, result.PredictedConsumption);
        var savedResult = Assert.Single(store.Results);
        Assert.Equal(186.4m, savedResult.ActualConsumption);
    }

    [Fact]
    public async Task Insufficient_history_returns_not_forecastable()
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        AddHistory(store, "target", 1403, 8, [100m, 105m, 98m]);

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.False(result.IsForecastable);
        Assert.True(result.RequiresExpertReview);
        Assert.Equal("Insufficient valid history", result.Reason);
        Assert.Contains(result.Warnings, x => x.Code == "INSUFFICIENT_HISTORY");
    }

    [Fact]
    public async Task Enough_similar_subscribers_generates_forecast()
    {
        var store = SeedForecastableScenario([110m, 115m, 120m]);

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.Equal("EnhancedSimilarPattern", result.Method);
        Assert.Equal(3, result.SimilarSubscribersCount);
        Assert.InRange(result.PredictedConsumption!.Value, 110m, 120m);
        Assert.Equal(3, store.Results.Single().SimilarSubscribers.Count);
    }

    [Fact]
    public async Task Outliers_are_removed_before_weighted_average()
    {
        var store = SeedForecastableScenario([100m, 102m, 98m, 500m]);
        var result = await CreateEngine(store, new ForecastEngineOptions { MinimumSimilarSubscribersTemperate = 3, TopNSimilarSubscribers = 10, MinimumCandidateSimilarity = 0m, ConsumptionBandToleranceRatio = 10m })
            .ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.Equal(1, result.OutliersRemoved);
        Assert.True(result.PredictedConsumption < 150m);
        Assert.Contains(store.Results.Single().SimilarSubscribers, x => x.IsOutlier);
    }

    [Fact]
    public async Task Low_confidence_triggers_expert_review()
    {
        var store = SeedForecastableScenario([100m, 350m, 900m], actualHistory: false, candidateHistoryStart: 20m);
        var options = new ForecastEngineOptions
        {
            MinimumSimilarSubscribersTemperate = 3,
            TopNSimilarSubscribers = 10,
            MinimumCandidateSimilarity = 0m,
            MediumConfidenceThreshold = 0.70m,
            LowConfidenceThreshold = 0.10m
        };

        var result = await CreateEngine(store, options).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.RequiresExpertReview);
        Assert.NotEqual("Low", result.RiskLevel);
    }

    [Fact]
    public void Similarity_score_increases_when_profiles_match()
    {
        var service = new ProfileSimilarityService();
        var target = Profile("target");
        var matching = Profile("matching");
        var different = Profile("different");
        different.TariffType = 20;
        different.Phase = 3;
        different.Ampere = 50m;
        different.MeterType = 5;
        different.UsageType = 99;

        Assert.True(service.Calculate(target, matching) > service.Calculate(target, different));
    }

    [Fact]
    public async Task Batch_run_stores_forecast_run_and_results()
    {
        var store = SeedForecastableScenario([110m, 115m, 120m]);
        var engine = CreateEngine(store);
        var batch = new ForecastBatchService(store, engine, new ForecastRequestValidator());

        var result = await batch.RunBatchAsync(new BatchForecastRequest(141, 1403, 8, 10, 1), CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Single(store.Runs);
        Assert.Single(store.Results);
        Assert.Equal(store.Runs.Single().Id, store.Results.Single().RunId);
    }

    [Fact]
    public async Task Candidate_actual_target_month_data_is_not_used_for_prediction()
    {
        var store = SeedForecastableScenario([110m, 112m, 114m]);
        foreach (var candidate in store.Profiles.Where(x => x.BillIdentifier.StartsWith("candidate-", StringComparison.Ordinal)))
        {
            store.Consumptions.Add(Consumption(candidate.BillIdentifier, 1403, 8, 900m, actual: true));
        }

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.InRange(result.PredictedConsumption!.Value, 100m, 130m);
        Assert.DoesNotContain(store.Results.Single().SimilarSubscribers, x => x.SimilarMonthConsumption == 900m);
    }

    [Fact]
    public async Task Candidate_selection_filters_by_consumption_band()
    {
        var store = SeedForecastableScenario([110m, 112m, 114m]);
        var outOfBand = Profile("out-of-band");
        store.Profiles.Add(outOfBand);
        AddHistory(store, "out-of-band", 1403, 8, [900m, 920m, 930m, 940m, 950m, 960m, 970m, 980m, 990m, 1000m, 1010m, 1020m]);

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.Equal(3, result.SimilarSubscribersCount);
        Assert.DoesNotContain(store.Results.Single().SimilarSubscribers, x => x.SimilarBillIdentifier == "out-of-band");
        Assert.NotNull(store.LastCandidateCriteria);
    }

    [Fact]
    public async Task Missing_candidate_same_month_window_falls_back_without_target_leakage()
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        AddHistory(store, "target", 1403, 8, [100m, 102m, 104m, 106m, 108m, 110m]);

        for (var i = 0; i < 3; i++)
        {
            var id = $"candidate-{i + 1}";
            store.Profiles.Add(Profile(id));
            AddHistory(store, id, 1403, 8, [101m, 103m, 105m, 107m, 109m, 111m]);
        }

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.InRange(result.PredictedConsumption!.Value, 100m, 120m);
    }

    [Fact]
    public async Task Repeated_forecast_execution_supersedes_previous_latest_result()
    {
        var store = SeedForecastableScenario([110m, 112m, 114m]);
        var engine = CreateEngine(store);

        await engine.ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));
        await engine.ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.Equal(2, store.Results.Count);
        Assert.False(store.Results[0].IsLatest);
        Assert.NotNull(store.Results[0].SupersededAt);
        Assert.True(store.Results[1].IsLatest);
    }

    private static IEnhancedSimilarPatternForecastEngine CreateEngine(FakeForecastDataStore store, ForecastEngineOptions? options = null)
    {
        var configuredOptions = options ?? new ForecastEngineOptions
        {
            MinimumSimilarSubscribersTemperate = 3,
            MinimumSimilarSubscribersTropical = 2,
            TopNSimilarSubscribers = 10,
            MinimumCandidateSimilarity = 0m
        };

        return new EnhancedSimilarPatternForecastEngine(
            store,
            new ConsumptionSimilarityService(),
            new TrendSimilarityService(),
            new SeasonalSimilarityService(),
            new ProfileSimilarityService(),
            new GeographicSimilarityService(),
            new IqrOutlierDetectionService(),
            new ForecastConfidenceService(),
            new SimilarPatternWindowProvider(),
            new StaticForecastConfigProvider(configuredOptions),
            new ForecastRequestValidator());
    }

    private static FakeForecastDataStore SeedForecastableScenario(IReadOnlyList<decimal> targetMonthConsumptions, bool actualHistory = true, decimal candidateHistoryStart = 100m)
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        AddHistory(store, "target", 1403, 8, [100m, 102m, 104m, 106m, 108m, 110m, 112m, 114m, 116m, 118m, 119m, 120m], actualHistory);

        for (var i = 0; i < targetMonthConsumptions.Count; i++)
        {
            var id = $"candidate-{i + 1}";
            store.Profiles.Add(Profile(id));
            var history = Enumerable.Range(0, 12).Select(month => candidateHistoryStart + month * 2m).ToArray();
            history[0] = targetMonthConsumptions[i];
            AddHistory(store, id, 1403, 8, history);
            store.Consumptions.Add(Consumption(id, 1403, 8, targetMonthConsumptions[i], actual: true));
        }

        return store;
    }

    private static CustomerProfile Profile(string billIdentifier) => new()
    {
        BillIdentifier = billIdentifier,
        CoCode = 141,
        RegionCode = 10,
        CityCode = 100,
        TariffType = 10,
        UsageType = 1,
        Phase = 1,
        Ampere = 25m,
        ClimateType = 1,
        IsUrban = true,
        MeterType = 1,
        ActivityStatus = "Active"
    };

    private static void AddHistory(FakeForecastDataStore store, string billIdentifier, int targetYear, int targetMonth, IReadOnlyList<decimal> values, bool actual = true)
    {
        var targetKey = ToMonthKey(targetYear, targetMonth);
        for (var i = 0; i < values.Count; i++)
        {
            var key = targetKey - values.Count + i;
            var (year, month) = FromMonthKey(key);
            store.Consumptions.Add(Consumption(billIdentifier, year, month, values[i], actual));
        }
    }

    private static CustomerMonthlyConsumption Consumption(string billIdentifier, int year, int month, decimal value, bool actual) => new()
    {
        BillIdentifier = billIdentifier,
        Year = year,
        Month = month,
        Consumption = value,
        DaysCount = 30,
        ReadingType = actual ? "Actual" : "Estimated",
        BillType = "Normal",
        IsActualReading = actual,
        IsSmartReading = actual,
        DataQualityStatus = "Valid"
    };

    private static int ToMonthKey(int year, int month) => year * 12 + month;

    private static (int Year, int Month) FromMonthKey(int key)
    {
        var year = (key - 1) / 12;
        var month = key - year * 12;
        return (year, month);
    }

    private sealed class FakeForecastDataStore : IForecastDataStore
    {
        public List<CustomerProfile> Profiles { get; } = [];
        public List<CustomerMonthlyConsumption> Consumptions { get; } = [];
        public List<ForecastResult> Results { get; } = [];
        public List<ForecastRun> Runs { get; } = [];
        public CandidateSelectionCriteria? LastCandidateCriteria { get; private set; }

        public Task<CustomerProfile?> GetCustomerProfileAsync(string billIdentifier, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.FirstOrDefault(x => x.BillIdentifier == billIdentifier));

        public Task<CustomerMonthlyConsumption?> GetActualConsumptionAsync(string billIdentifier, int year, int month, bool includeSmartReading, CancellationToken cancellationToken) =>
            Task.FromResult(ValidConsumptions().FirstOrDefault(x =>
                x.BillIdentifier == billIdentifier &&
                x.Year == year &&
                x.Month == month &&
                (x.IsActualReading || includeSmartReading && x.IsSmartReading)));

        public Task<IReadOnlyList<CustomerMonthlyConsumption>> GetValidHistoryAsync(string billIdentifier, int targetYear, int targetMonth, int maximumMonths, CancellationToken cancellationToken)
        {
            var minKey = ToMonthKey(targetYear, targetMonth) - maximumMonths;
            var maxKey = ToMonthKey(targetYear, targetMonth) - 1;
            return Task.FromResult((IReadOnlyList<CustomerMonthlyConsumption>)ValidConsumptions()
                .Where(x => x.BillIdentifier == billIdentifier && ToMonthKey(x.Year, x.Month) >= minKey && ToMonthKey(x.Year, x.Month) <= maxKey)
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList());
        }

        public Task<IReadOnlyList<CustomerProfile>> GetCandidateProfilesAsync(CandidateSelectionCriteria criteria, int maxCandidates, CancellationToken cancellationToken)
        {
            LastCandidateCriteria = criteria;
            var targetProfile = criteria.TargetProfile;
            var lowerConsumption = criteria.TargetRecentAverageConsumption * (1m - criteria.ConsumptionBandToleranceRatio);
            var upperConsumption = criteria.TargetRecentAverageConsumption * (1m + criteria.ConsumptionBandToleranceRatio);
            var lowerAmpere = targetProfile.Ampere * (1m - criteria.AmpereToleranceRatio);
            var upperAmpere = targetProfile.Ampere * (1m + criteria.AmpereToleranceRatio);
            var minHistoryKey = ToMonthKey(criteria.TargetYear, criteria.TargetMonth) - criteria.MaximumHistoryMonths;
            var maxHistoryKey = ToMonthKey(criteria.TargetYear, criteria.TargetMonth) - 1;

            return Task.FromResult((IReadOnlyList<CustomerProfile>)Profiles
                .Where(x => x.BillIdentifier != targetProfile.BillIdentifier)
                .Where(x => x.ActivityStatus == "Active")
                .Where(x => x.ClimateType == targetProfile.ClimateType && x.TariffType == targetProfile.TariffType)
                .Where(x => x.CoCode == targetProfile.CoCode)
                .Where(x => x.RegionCode == targetProfile.RegionCode)
                .Where(x => !targetProfile.CityCode.HasValue || x.CityCode == targetProfile.CityCode)
                .Where(x => x.Phase == targetProfile.Phase)
                .Where(x => x.Ampere >= lowerAmpere && x.Ampere <= upperAmpere)
                .Where(x => x.MeterType == targetProfile.MeterType)
                .Where(x =>
                {
                    var average = ValidConsumptions()
                        .Where(c => c.BillIdentifier == x.BillIdentifier && ToMonthKey(c.Year, c.Month) >= minHistoryKey && ToMonthKey(c.Year, c.Month) <= maxHistoryKey)
                        .Select(c => c.Consumption)
                        .DefaultIfEmpty()
                        .Average();
                    return average >= lowerConsumption && average <= upperConsumption;
                })
                .Take(maxCandidates)
                .ToList());
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>>> GetValidHistoryForCustomersAsync(IEnumerable<string> billIdentifiers, int targetYear, int targetMonth, int maximumMonths, CancellationToken cancellationToken)
        {
            var ids = billIdentifiers.ToHashSet();
            var minKey = ToMonthKey(targetYear, targetMonth) - maximumMonths;
            var maxKey = ToMonthKey(targetYear, targetMonth) - 1;
            var result = ValidConsumptions()
                .Where(x => ids.Contains(x.BillIdentifier) && ToMonthKey(x.Year, x.Month) >= minKey && ToMonthKey(x.Year, x.Month) <= maxKey)
                .GroupBy(x => x.BillIdentifier)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<CustomerMonthlyConsumption>)g.OrderBy(x => x.Year).ThenBy(x => x.Month).ToList());
            return Task.FromResult((IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>>)result);
        }

        public Task SaveForecastResultAsync(ForecastResult result, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            foreach (var previous in Results.Where(x => x.BillIdentifier == result.BillIdentifier && x.TargetYear == result.TargetYear && x.TargetMonth == result.TargetMonth && x.IsLatest))
            {
                previous.IsLatest = false;
                previous.SupersededAt = now;
            }

            result.Id = Results.Count + 1;
            result.IsLatest = true;
            Results.Add(result);
            return Task.CompletedTask;
        }

        public Task<ForecastResult?> GetForecastResultAsync(string billIdentifier, int year, int month, CancellationToken cancellationToken) =>
            Task.FromResult(Results.LastOrDefault(x => x.BillIdentifier == billIdentifier && x.TargetYear == year && x.TargetMonth == month));

        public Task<IReadOnlyList<CustomerProfile>> GetBatchCustomersAsync(BatchForecastRequest request, CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyList<CustomerProfile>)Profiles
                .Where(x => x.CoCode == request.CoCode && x.ActivityStatus == "Active")
                .Where(x => !request.TariffType.HasValue || x.TariffType == request.TariffType)
                .Take(request.MaxCustomers)
                .ToList());

        public Task<ForecastRun> CreateForecastRunAsync(BatchForecastRequest request, CancellationToken cancellationToken)
        {
            var run = new ForecastRun
            {
                Id = Runs.Count + 1,
                CoCode = request.CoCode,
                TargetYear = request.TargetYear,
                TargetMonth = request.TargetMonth
            };
            Runs.Add(run);
            return Task.FromResult(run);
        }

        public Task UpdateForecastRunAsync(ForecastRun run, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ForecastDashboardSummary> GetDashboardSummaryAsync(int coCode, int year, int month, CancellationToken cancellationToken)
        {
            var rows = Results.Where(x => x.CoCode == coCode && x.TargetYear == year && x.TargetMonth == month && x.IsLatest).ToList();
            return Task.FromResult(new ForecastDashboardSummary(
                coCode,
                year,
                month,
                rows.Count,
                rows.Count(x => x.IsForecastable),
                rows.Count(x => x.RequiresExpertReview),
                rows.Count == 0 ? 0m : rows.Average(x => x.Confidence)));
        }

        private IEnumerable<CustomerMonthlyConsumption> ValidConsumptions() =>
            Consumptions.Where(x => !x.HasCorrection && !x.HasMeterChange && x.DataQualityStatus == "Valid");

        public Task<IReadOnlyDictionary<string, string>> GetActiveForecastConfigValuesAsync(CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
    }

    private sealed class StaticForecastConfigProvider : IForecastConfigProvider
    {
        private readonly ForecastEngineOptions options;

        public StaticForecastConfigProvider(ForecastEngineOptions options) => this.options = options;

        public Task<ForecastEngineOptions> GetOptionsAsync(CancellationToken cancellationToken) => Task.FromResult(options);
    }
}
