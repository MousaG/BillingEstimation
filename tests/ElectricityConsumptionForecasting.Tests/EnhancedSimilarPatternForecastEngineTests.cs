using ElectricityConsumptionForecasting.Application.Configuration;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Services;
using ElectricityConsumptionForecasting.Domain.Entities;
using Microsoft.Extensions.Options;

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
        var result = await CreateEngine(store, new ForecastEngineOptions { MinimumSimilarSubscribersTemperate = 3, TopNSimilarSubscribers = 10, MinimumCandidateSimilarity = 0m })
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
        var batch = new ForecastBatchService(store, engine);

        var result = await batch.RunBatchAsync(new BatchForecastRequest(141, 1403, 8, 10, 1), CancellationToken.None);

        Assert.Equal("Completed", result.Status);
        Assert.Single(store.Runs);
        Assert.Single(store.Results);
        Assert.Equal(store.Runs.Single().Id, store.Results.Single().RunId);
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

        var wrappedOptions = Options.Create(configuredOptions);
        return new EnhancedSimilarPatternForecastEngine(
            store,
            new ConsumptionSimilarityService(),
            new TrendSimilarityService(),
            new SeasonalSimilarityService(),
            new ProfileSimilarityService(),
            new GeographicSimilarityService(),
            new IqrOutlierDetectionService(wrappedOptions),
            new ForecastConfidenceService(wrappedOptions),
            wrappedOptions);
    }

    private static FakeForecastDataStore SeedForecastableScenario(IReadOnlyList<decimal> targetMonthConsumptions, bool actualHistory = true, decimal candidateHistoryStart = 100m)
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        AddHistory(store, "target", 1403, 8, [100m, 105m, 110m, 115m, 118m, 120m], actualHistory);

        for (var i = 0; i < targetMonthConsumptions.Count; i++)
        {
            var id = $"candidate-{i + 1}";
            store.Profiles.Add(Profile(id));
            AddHistory(store, id, 1403, 8, Enumerable.Range(0, 6).Select(month => candidateHistoryStart + month * 4m).ToArray());
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

        public Task<IReadOnlyList<CustomerProfile>> GetCandidateProfilesAsync(CustomerProfile targetProfile, int maxCandidates, CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyList<CustomerProfile>)Profiles
                .Where(x => x.BillIdentifier != targetProfile.BillIdentifier)
                .Where(x => x.ActivityStatus == "Active")
                .Where(x => x.ClimateType == targetProfile.ClimateType && x.TariffType == targetProfile.TariffType)
                .Take(maxCandidates)
                .ToList());

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

        public Task<IReadOnlyDictionary<string, CustomerMonthlyConsumption>> GetActualConsumptionForCustomersAsync(IEnumerable<string> billIdentifiers, int year, int month, CancellationToken cancellationToken)
        {
            var ids = billIdentifiers.ToHashSet();
            var result = ValidConsumptions()
                .Where(x => ids.Contains(x.BillIdentifier) && x.Year == year && x.Month == month && (x.IsActualReading || x.IsSmartReading))
                .GroupBy(x => x.BillIdentifier)
                .ToDictionary(g => g.Key, g => g.First());
            return Task.FromResult((IReadOnlyDictionary<string, CustomerMonthlyConsumption>)result);
        }

        public Task SaveForecastResultAsync(ForecastResult result, CancellationToken cancellationToken)
        {
            result.Id = Results.Count + 1;
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
            var rows = Results.Where(x => x.CoCode == coCode && x.TargetYear == year && x.TargetMonth == month).ToList();
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
    }
}
