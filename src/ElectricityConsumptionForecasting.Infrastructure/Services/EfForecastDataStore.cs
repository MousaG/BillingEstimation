using System.Text.Json;
using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Application.Models;
using ElectricityConsumptionForecasting.Domain.Entities;
using ElectricityConsumptionForecasting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ElectricityConsumptionForecasting.Infrastructure.Services;

public sealed class EfForecastDataStore : IForecastDataStore
{
    private readonly ForecastingDbContext dbContext;

    public EfForecastDataStore(ForecastingDbContext dbContext) => this.dbContext = dbContext;

    public Task<CustomerProfile?> GetCustomerProfileAsync(string billIdentifier, CancellationToken cancellationToken) =>
        dbContext.CustomerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.BillIdentifier == billIdentifier, cancellationToken);

    public Task<CustomerMonthlyConsumption?> GetActualConsumptionAsync(string billIdentifier, int year, int month, bool includeSmartReading, CancellationToken cancellationToken) =>
        dbContext.CustomerMonthlyConsumptions.AsNoTracking()
            .Where(x => x.BillIdentifier == billIdentifier && x.Year == year && x.Month == month && !x.HasCorrection && !x.HasMeterChange && x.DataQualityStatus == "Valid")
            .Where(x => !dbContext.CustomerDataQualityIssues.Any(issue =>
                issue.BillIdentifier == x.BillIdentifier &&
                issue.Year == x.Year &&
                issue.Month == x.Month &&
                issue.Severity == "Severe"))
            .Where(x => x.IsActualReading || (includeSmartReading && x.IsSmartReading))
            .OrderByDescending(x => x.IsSmartReading)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerMonthlyConsumption>> GetValidHistoryAsync(string billIdentifier, int targetYear, int targetMonth, int maximumMonths, CancellationToken cancellationToken)
    {
        var minKey = ToMonthKey(targetYear, targetMonth) - maximumMonths;
        var maxKey = ToMonthKey(targetYear, targetMonth) - 1;
        return await ValidConsumptions()
            .Where(x => x.BillIdentifier == billIdentifier && x.Year * 12 + x.Month >= minKey && x.Year * 12 + x.Month <= maxKey)
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerProfile>> GetCandidateProfilesAsync(CandidateSelectionCriteria criteria, int maxCandidates, CancellationToken cancellationToken)
    {
        var targetProfile = criteria.TargetProfile;
        var targetAverage = criteria.TargetRecentAverageConsumption;
        var lowerConsumption = targetAverage * (1m - criteria.ConsumptionBandToleranceRatio);
        var upperConsumption = targetAverage * (1m + criteria.ConsumptionBandToleranceRatio);
        var lowerAmpere = targetProfile.Ampere * (1m - criteria.AmpereToleranceRatio);
        var upperAmpere = targetProfile.Ampere * (1m + criteria.AmpereToleranceRatio);
        var targetBand = CalculateConsumptionBand(targetAverage);
        var baseQuery = dbContext.CustomerRecentConsumptionFeatures.AsNoTracking()
            .Where(x => x.FeatureYear == criteria.TargetYear && x.FeatureMonth == criteria.TargetMonth)
            .Where(x => x.BillIdentifier != targetProfile.BillIdentifier)
            .Where(x => x.RecentAverageConsumption >= lowerConsumption && x.RecentAverageConsumption <= upperConsumption)
            .Where(x => x.ConsumptionBand >= targetBand - 1 && x.ConsumptionBand <= targetBand + 1)
            .Where(x => x.CoCode == targetProfile.CoCode)
            .Where(x => x.Phase == targetProfile.Phase)
            .Where(x => x.Ampere >= lowerAmpere && x.Ampere <= upperAmpere)
            .Where(x => x.MeterType == targetProfile.MeterType)
            .Where(x => x.ClimateType == targetProfile.ClimateType && x.TariffType == targetProfile.TariffType)
            .Join(dbContext.CustomerProfiles.AsNoTracking(), feature => feature.BillIdentifier, profile => profile.BillIdentifier, (feature, profile) => new { Feature = feature, Profile = profile })
            .Select(x => x.Profile)
            .Where(x => x.ActivityStatus == "Active")
            .Where(x => x.ClimateType == targetProfile.ClimateType && x.TariffType == targetProfile.TariffType);

        var selected = new List<CustomerProfile>();
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (targetProfile.CityCode.HasValue)
        {
            await AddCandidatesAsync(baseQuery.Where(x => x.CityCode == targetProfile.CityCode), targetProfile, selected, selectedIds, maxCandidates, cancellationToken);
        }

        if (selected.Count < maxCandidates)
        {
            await AddCandidatesAsync(baseQuery.Where(x => x.RegionCode == targetProfile.RegionCode), targetProfile, selected, selectedIds, maxCandidates, cancellationToken);
        }

        if (selected.Count < maxCandidates)
        {
            await AddCandidatesAsync(baseQuery, targetProfile, selected, selectedIds, maxCandidates, cancellationToken);
        }

        return selected;
    }

    private static async Task AddCandidatesAsync(
        IQueryable<CustomerProfile> query,
        CustomerProfile targetProfile,
        List<CustomerProfile> selected,
        HashSet<string> selectedIds,
        int maxCandidates,
        CancellationToken cancellationToken)
    {
        var remaining = maxCandidates - selected.Count;
        if (remaining <= 0)
        {
            return;
        }

        var rows = await query
            .OrderByDescending(x => x.CityCode == targetProfile.CityCode)
            .ThenByDescending(x => x.RegionCode == targetProfile.RegionCode)
            .ThenByDescending(x => x.Phase == targetProfile.Phase)
            .ThenByDescending(x => x.MeterType == targetProfile.MeterType)
            .Take(remaining * 2)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (selected.Count >= maxCandidates)
            {
                return;
            }

            if (selectedIds.Add(row.BillIdentifier))
            {
                selected.Add(row);
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<CustomerMonthlyConsumption>>> GetValidHistoryForCustomersAsync(IEnumerable<string> billIdentifiers, int targetYear, int targetMonth, int maximumMonths, CancellationToken cancellationToken)
    {
        var ids = billIdentifiers.Distinct().ToArray();
        var minKey = ToMonthKey(targetYear, targetMonth) - maximumMonths;
        var maxKey = ToMonthKey(targetYear, targetMonth) - 1;
        var rows = await ValidConsumptions()
            .Where(x => ids.Contains(x.BillIdentifier) && x.Year * 12 + x.Month >= minKey && x.Year * 12 + x.Month <= maxKey)
            .ToListAsync(cancellationToken);

        return rows.GroupBy(x => x.BillIdentifier)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CustomerMonthlyConsumption>)g.OrderBy(x => x.Year).ThenBy(x => x.Month).ToList());
    }

    public async Task SaveForecastResultAsync(ForecastResult result, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var currentLatest = await dbContext.ForecastResults
            .Where(x =>
                x.BillIdentifier == result.BillIdentifier &&
                x.TargetYear == result.TargetYear &&
                x.TargetMonth == result.TargetMonth &&
                x.IsLatest)
            .ToListAsync(cancellationToken);

        foreach (var previous in currentLatest)
        {
            previous.IsLatest = false;
            previous.SupersededAt = now;
        }

        result.IsLatest = true;
        dbContext.ForecastResults.Add(result);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ForecastResult?> GetForecastResultAsync(string billIdentifier, int year, int month, CancellationToken cancellationToken) =>
        dbContext.ForecastResults.AsNoTracking()
            .Include(x => x.Warnings)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.BillIdentifier == billIdentifier && x.TargetYear == year && x.TargetMonth == month && x.IsLatest, cancellationToken);

    public async Task<IReadOnlyList<CustomerProfile>> GetBatchCustomersAsync(BatchForecastRequest request, CancellationToken cancellationToken)
    {
        var query = dbContext.CustomerProfiles.AsNoTracking()
            .Where(x => x.CoCode == request.CoCode && x.ActivityStatus == "Active");
        if (request.TariffType.HasValue)
        {
            query = query.Where(x => x.TariffType == request.TariffType);
        }

        return await query.Take(request.MaxCustomers).ToListAsync(cancellationToken);
    }

    public async Task<ForecastRun> CreateForecastRunAsync(BatchForecastRequest request, CancellationToken cancellationToken)
    {
        var run = new ForecastRun
        {
            CoCode = request.CoCode,
            TargetYear = request.TargetYear,
            TargetMonth = request.TargetMonth,
            ParametersJson = JsonSerializer.Serialize(request)
        };
        dbContext.ForecastRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task UpdateForecastRunAsync(ForecastRun run, CancellationToken cancellationToken)
    {
        dbContext.ForecastRuns.Update(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ForecastDashboardSummary> GetDashboardSummaryAsync(int coCode, int year, int month, CancellationToken cancellationToken)
    {
        var query = dbContext.ForecastResults.AsNoTracking().Where(x => x.CoCode == coCode && x.TargetYear == year && x.TargetMonth == month && x.IsLatest);
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
        {
            return new ForecastDashboardSummary(coCode, year, month, 0, 0, 0, 0m);
        }

        return new ForecastDashboardSummary(
            coCode,
            year,
            month,
            total,
            await query.CountAsync(x => x.IsForecastable, cancellationToken),
            await query.CountAsync(x => x.RequiresExpertReview, cancellationToken),
            await query.AverageAsync(x => x.Confidence, cancellationToken));
    }

    public async Task<IReadOnlyDictionary<string, string>> GetActiveForecastConfigValuesAsync(CancellationToken cancellationToken) =>
        await dbContext.ForecastConfigs.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public async Task<IReadOnlyList<CustomerProfile>> GetFeatureBuildCustomersAsync(int coCode, int maximumCustomers, CancellationToken cancellationToken) =>
        await dbContext.CustomerProfiles.AsNoTracking()
            .Where(x => x.CoCode == coCode && x.ActivityStatus == "Active")
            .Take(maximumCustomers)
            .ToListAsync(cancellationToken);

    public async Task UpsertCustomerRecentConsumptionFeatureAsync(CustomerRecentConsumptionFeature feature, CancellationToken cancellationToken)
    {
        var existing = await dbContext.CustomerRecentConsumptionFeatures
            .FirstOrDefaultAsync(x =>
                x.BillIdentifier == feature.BillIdentifier &&
                x.FeatureYear == feature.FeatureYear &&
                x.FeatureMonth == feature.FeatureMonth,
                cancellationToken);
        if (existing is null)
        {
            dbContext.CustomerRecentConsumptionFeatures.Add(feature);
        }
        else
        {
            existing.CoCode = feature.CoCode;
            existing.RegionCode = feature.RegionCode;
            existing.CityCode = feature.CityCode;
            existing.TariffType = feature.TariffType;
            existing.ClimateType = feature.ClimateType;
            existing.Phase = feature.Phase;
            existing.Ampere = feature.Ampere;
            existing.MeterType = feature.MeterType;
            existing.ValidMonthsCount = feature.ValidMonthsCount;
            existing.RecentAverageConsumption = feature.RecentAverageConsumption;
            existing.RecentMinimumConsumption = feature.RecentMinimumConsumption;
            existing.RecentMaximumConsumption = feature.RecentMaximumConsumption;
            existing.RecentStdDevConsumption = feature.RecentStdDevConsumption;
            existing.LastConsumption = feature.LastConsumption;
            existing.TrendSlope = feature.TrendSlope;
            existing.ConsumptionBand = feature.ConsumptionBand;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<CustomerMonthlyConsumption> ValidConsumptions() =>
        dbContext.CustomerMonthlyConsumptions.AsNoTracking()
            .Where(x => !x.HasCorrection && !x.HasMeterChange && x.DataQualityStatus == "Valid")
            .Where(x => !dbContext.CustomerDataQualityIssues.Any(issue =>
                issue.BillIdentifier == x.BillIdentifier &&
                issue.Year == x.Year &&
                issue.Month == x.Month &&
                issue.Severity == "Severe"));

    private static int ToMonthKey(int year, int month) => year * 12 + month;

    private static int CalculateConsumptionBand(decimal averageConsumption)
    {
        if (averageConsumption < 100m)
        {
            return 1;
        }

        if (averageConsumption < 200m)
        {
            return 2;
        }

        if (averageConsumption < 400m)
        {
            return 3;
        }

        if (averageConsumption < 800m)
        {
            return 4;
        }

        return 5;
    }
}
