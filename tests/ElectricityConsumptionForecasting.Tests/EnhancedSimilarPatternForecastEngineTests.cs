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
        Assert.True(result.SimilarSubscribersCount >= 3);
        Assert.InRange(result.PredictedConsumption!.Value, 110m, 120m);
        Assert.Equal(3, store.Results.Single().SimilarSubscribers.Select(x => x.SimilarBillIdentifier).Distinct().Count());
    }

    [Fact]
    public void Outliers_are_removed_before_weighted_average()
    {
        var service = new IqrOutlierDetectionService();
        var candidates = new[]
        {
            Score("a", 100m),
            Score("b", 102m),
            Score("c", 98m),
            Score("d", 500m)
        };

        var result = service.RemoveOutliers(candidates, new ForecastEngineOptions());

        Assert.Single(result.Outliers);
        Assert.DoesNotContain(result.IncludedCandidates, x => x.TargetMonthConsumption == 500m);
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
            MediumConfidenceThreshold = 0.95m,
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
    public void Positional_window_similarity_is_high_for_same_shape_from_different_year()
    {
        var service = new PositionalWindowSimilarityService();
        var target = Window("target", 1403, 4, [100m, 120m, 140m, 160m]);
        var candidate = Window("candidate", 1401, 9, [100m, 120m, 140m, 160m]);

        var score = service.Calculate(target, candidate);

        Assert.True(score.ConsumptionSimilarity > 0.95m);
        Assert.True(score.TrendSimilarity > 0.95m);
    }

    [Fact]
    public void Positional_window_similarity_is_low_for_opposite_level_and_shape()
    {
        var service = new PositionalWindowSimilarityService();
        var target = Window("target", 1403, 4, [100m, 120m, 140m, 160m]);
        var candidate = Window("candidate", 1401, 9, [300m, 280m, 260m, 240m]);

        var score = service.Calculate(target, candidate);

        Assert.True(score.ConsumptionSimilarity < 0.20m);
        Assert.True(score.TrendSimilarity < 0.20m);
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
    public async Task Older_rolling_windows_are_used_for_comparable_consumption()
    {
        var store = SeedForecastableScenario([130m, 132m, 134m]);

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.All(store.Results.Single().SimilarSubscribers, x =>
        {
            Assert.True(ToMonthKey(x.ComparableYear, x.ComparableMonth) < ToMonthKey(1403, 8));
            Assert.True(ToMonthKey(x.HistoryEndYear, x.HistoryEndMonth) < ToMonthKey(x.ComparableYear, x.ComparableMonth));
        });
    }

    [Fact]
    public void Multiple_windows_from_same_candidate_can_be_generated()
    {
        var provider = new SimilarPatternWindowProvider();
        var history = new Dictionary<string, IReadOnlyList<CustomerMonthlyConsumption>>
        {
            ["candidate"] = Enumerable.Range(0, 8)
                .Select(i =>
                {
                    var (year, month) = FromMonthKey(ToMonthKey(1403, 8) - 8 + i);
                    return Consumption("candidate", year, month, 100m + i, actual: true);
                })
                .ToList()
        };

        var windows = provider.BuildWindows(history, 1403, 8, 4);

        Assert.Equal(4, windows.Count);
        Assert.All(windows, x => Assert.Equal("candidate", x.BillIdentifier));
    }

    [Fact]
    public async Task Forecast_result_changes_based_on_best_matching_rolling_windows()
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        AddHistory(store, "target", 1403, 8, [100m, 102m, 104m, 106m, 108m, 110m, 112m, 114m, 116m, 118m, 119m, 120m]);

        for (var i = 0; i < 3; i++)
        {
            var id = $"candidate-{i + 1}";
            store.Profiles.Add(Profile(id));
            AddHistory(store, id, 1403, 8, [30m, 35m, 40m, 45m, 400m, 116m, 118m, 119m, 120m, 150m, 116m, 118m]);
        }

        var options = new ForecastEngineOptions
        {
            MinimumSimilarSubscribersTemperate = 1,
            TopNSimilarSubscribers = 1,
            MinimumCandidateSimilarity = 0m,
            ConsumptionBandToleranceRatio = 10m,
            SeasonalSimilarityWeight = 0m
        };
        var closeWindowResult = await CreateEngine(store, options).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));
        Assert.InRange(closeWindowResult.PredictedConsumption!.Value, 145m, 155m);

        store.Results.Clear();
        store.Consumptions.RemoveAll(x => x.BillIdentifier.StartsWith("candidate-", StringComparison.Ordinal));
        for (var i = 0; i < 3; i++)
        {
            var id = $"candidate-{i + 1}";
            AddHistory(store, id, 1403, 8, [30m, 35m, 40m, 45m, 400m, 116m, 118m, 119m, 120m, 250m, 116m, 118m]);
        }

        var changedWindowResult = await CreateEngine(store, options).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));
        Assert.InRange(changedWindowResult.PredictedConsumption!.Value, 245m, 255m);
    }

    [Fact]
    public async Task Forecast_engine_selects_rolling_windows_by_positional_shape_and_level()
    {
        var store = new FakeForecastDataStore();
        store.Profiles.Add(Profile("target"));
        AddHistory(store, "target", 1403, 8, [100m, 120m, 140m, 160m]);

        store.Profiles.Add(Profile("good-candidate"));
        AddHistory(store, "good-candidate", 1403, 8, [100m, 120m, 140m, 160m, 180m]);

        store.Profiles.Add(Profile("bad-candidate"));
        AddHistory(store, "bad-candidate", 1403, 8, [300m, 280m, 260m, 240m, 900m]);

        var options = new ForecastEngineOptions
        {
            MinimumHistoryMonths = 4,
            MaximumHistoryMonths = 12,
            MinimumSimilarSubscribersTemperate = 1,
            TopNSimilarSubscribers = 1,
            MinimumCandidateSimilarity = 0m,
            ConsumptionBandToleranceRatio = 10m
        };

        var result = await CreateEngine(store, options).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.InRange(result.PredictedConsumption!.Value, 175m, 185m);
        var similar = Assert.Single(store.Results.Single().SimilarSubscribers);
        Assert.Equal("good-candidate", similar.SimilarBillIdentifier);
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
        Assert.Equal(3, store.Results.Single().SimilarSubscribers.Select(x => x.SimilarBillIdentifier).Distinct().Count());
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
    public async Task Candidate_selection_falls_back_from_city_to_region()
    {
        var store = SeedForecastableScenario([110m, 112m, 114m]);
        foreach (var candidate in store.Profiles.Where(x => x.BillIdentifier.StartsWith("candidate-", StringComparison.Ordinal)))
        {
            candidate.CityCode = 200;
        }

        var result = await CreateEngine(store).ForecastAsync(new ForecastCustomerRequest("target", 1403, 8));

        Assert.True(result.IsForecastable);
        Assert.Equal(3, store.Results.Single().SimilarSubscribers.Select(x => x.SimilarBillIdentifier).Distinct().Count());
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
            new PositionalWindowSimilarityService(),
            new ComparableMonthSeasonalityService(),
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
            history[7] = 116m;
            history[8] = 118m;
            history[9] = 119m;
            history[10] = 120m;
            history[11] = targetMonthConsumptions[i];
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

    private static ForecastCandidateScore Score(string billIdentifier, decimal comparableConsumption) =>
        new(billIdentifier, 1m, 1m, 1m, 1m, 1m, 1m, comparableConsumption, 1403, 1, 1402, 9, 1402, 12);

    private static IReadOnlyList<CustomerMonthlyConsumption> Window(string billIdentifier, int startYear, int startMonth, IReadOnlyList<decimal> values)
    {
        var startKey = ToMonthKey(startYear, startMonth);
        return values.Select((value, index) =>
        {
            var (year, month) = FromMonthKey(startKey + index);
            return Consumption(billIdentifier, year, month, value, actual: true);
        }).ToList();
    }

    private static int ToMonthKey(int year, int month) => year * 12 + month;

    private static (int Year, int Month) FromMonthKey(int key)
    {
        var year = (key - 1) / 12;
        var month = key - year * 12;
        return (year, month);
    }

    [Fact]
    public void Customer_recent_consumption_feature_service_builds_expected_feature()
    {
        var store = new FakeForecastDataStore();
        var service = new CustomerRecentConsumptionFeatureService(store, new StaticForecastConfigProvider(new ForecastEngineOptions()));
        var profile = Profile("feature-target");
        var history = Window("feature-target", 1403, 1, [90m, 110m, 130m, 150m]);

        var feature = service.BuildFeature(profile, history, 1403, 5);

        Assert.Equal("feature-target", feature.BillIdentifier);
        Assert.Equal(4, feature.ValidMonthsCount);
        Assert.Equal(120m, feature.RecentAverageConsumption);
        Assert.Equal(90m, feature.RecentMinimumConsumption);
        Assert.Equal(150m, feature.RecentMaximumConsumption);
        Assert.Equal(20m, feature.TrendSlope);
        Assert.Equal(2, feature.ConsumptionBand);
    }

    [Fact]
    public void Batch_request_validation_enforces_max_batch_limit()
    {
        var validator = new ForecastRequestValidator(Microsoft.Extensions.Options.Options.Create(new ForecastEngineOptions { MaxBatchCustomersLimit = 10 }));

        var result = validator.Validate(new BatchForecastRequest(141, 1403, 8, 10, 11));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("MaxCustomers", StringComparison.Ordinal));
    }

    private sealed class FakeForecastDataStore : IForecastDataStore
    {
        public List<CustomerProfile> Profiles { get; } = [];
        public List<CustomerMonthlyConsumption> Consumptions { get; } = [];
        public List<ForecastResult> Results { get; } = [];
        public List<ForecastRun> Runs { get; } = [];
        public List<CustomerRecentConsumptionFeature> Features { get; } = [];
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

            var baseCandidates = Profiles
                .Where(x => x.BillIdentifier != targetProfile.BillIdentifier)
                .Where(x => x.ActivityStatus == "Active")
                .Where(x => x.ClimateType == targetProfile.ClimateType && x.TariffType == targetProfile.TariffType)
                .Where(x => x.CoCode == targetProfile.CoCode)
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
                });

            var selected = new List<CustomerProfile>();
            Add(baseCandidates.Where(x => targetProfile.CityCode.HasValue && x.CityCode == targetProfile.CityCode));
            Add(baseCandidates.Where(x => x.RegionCode == targetProfile.RegionCode));
            Add(baseCandidates);
            return Task.FromResult((IReadOnlyList<CustomerProfile>)selected.Take(maxCandidates).ToList());

            void Add(IEnumerable<CustomerProfile> candidates)
            {
                foreach (var candidate in candidates)
                {
                    if (selected.Count >= maxCandidates)
                    {
                        return;
                    }

                    if (selected.All(x => x.BillIdentifier != candidate.BillIdentifier))
                    {
                        selected.Add(candidate);
                    }
                }
            }
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

        public Task<IReadOnlyList<CustomerProfile>> GetFeatureBuildCustomersAsync(int coCode, int maximumCustomers, CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyList<CustomerProfile>)Profiles.Where(x => x.CoCode == coCode && x.ActivityStatus == "Active").Take(maximumCustomers).ToList());

        public Task UpsertCustomerRecentConsumptionFeatureAsync(CustomerRecentConsumptionFeature feature, CancellationToken cancellationToken)
        {
            Features.RemoveAll(x => x.BillIdentifier == feature.BillIdentifier && x.FeatureYear == feature.FeatureYear && x.FeatureMonth == feature.FeatureMonth);
            Features.Add(feature);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticForecastConfigProvider : IForecastConfigProvider
    {
        private readonly ForecastEngineOptions options;

        public StaticForecastConfigProvider(ForecastEngineOptions options) => this.options = options;

        public Task<ForecastEngineOptions> GetOptionsAsync(CancellationToken cancellationToken) => Task.FromResult(options);
    }
}
