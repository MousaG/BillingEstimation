using ElectricityConsumptionForecasting.Application.Dtos;
using ElectricityConsumptionForecasting.Application.Interfaces;
using ElectricityConsumptionForecasting.Domain.Entities;
using ElectricityConsumptionForecasting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElectricityConsumptionForecasting.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly ForecastingDbContext dbContext;
    private readonly ICustomerRecentConsumptionFeatureService featureService;

    public AdminController(ForecastingDbContext dbContext, ICustomerRecentConsumptionFeatureService featureService)
    {
        this.dbContext = dbContext;
        this.featureService = featureService;
    }

    [HttpPost("sample-data/seed")]
    public async Task<ActionResult<SeedSampleDataResponse>> SeedSampleData([FromBody] SeedSampleDataRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalize();
        var sampleIds = BuildSampleIds(normalized.TargetBillIdentifier, normalized.CandidateCount);

        await RemoveExistingSampleRows(sampleIds, cancellationToken);

        var profiles = BuildProfiles(normalized, sampleIds);
        var consumptions = BuildConsumptions(normalized, sampleIds);

        dbContext.CustomerProfiles.AddRange(profiles);
        dbContext.CustomerMonthlyConsumptions.AddRange(consumptions);
        await dbContext.SaveChangesAsync(cancellationToken);

        var featuresBuilt = 0;
        foreach (var profile in profiles)
        {
            var history = consumptions
                .Where(x => x.BillIdentifier == profile.BillIdentifier && ToMonthKey(x.Year, x.Month) < ToMonthKey(normalized.TargetYear, normalized.TargetMonth))
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToList();
            if (history.Count >= 4)
            {
                await featureService.BuildAndSaveFeatureAsync(dbContext, profile, history, normalized.TargetYear, normalized.TargetMonth, cancellationToken);
                featuresBuilt++;
            }
        }

        return Ok(new SeedSampleDataResponse(
            normalized.TargetBillIdentifier,
            normalized.TargetYear,
            normalized.TargetMonth,
            profiles.Count,
            consumptions.Count,
            featuresBuilt));
    }

    [HttpPost("features/rebuild")]
    public async Task<ActionResult<RebuildFeaturesResponse>> RebuildFeatures([FromBody] RebuildFeaturesRequest request, CancellationToken cancellationToken)
    {
        if (request.CoCode <= 0 || request.FeatureMonth is < 1 or > 12 || request.FeatureYear <= 0 || request.MaximumCustomers <= 0)
        {
            return BadRequest(new { errors = new[] { "CoCode, FeatureYear, FeatureMonth and MaximumCustomers must be valid positive values." } });
        }

        var count = await featureService.RebuildFeaturesAsync(request.CoCode, request.FeatureYear, request.FeatureMonth, request.MaximumCustomers, cancellationToken);
        return Ok(new RebuildFeaturesResponse(count));
    }

    [HttpGet("configs")]
    public async Task<ActionResult<IReadOnlyList<ForecastConfigDto>>> GetConfigs(CancellationToken cancellationToken) =>
        Ok(await dbContext.ForecastConfigs.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ForecastConfigDto(x.Name, x.Value, x.Description, x.IsActive))
            .ToListAsync(cancellationToken));

    [HttpPost("configs")]
    public async Task<ActionResult<ForecastConfigDto>> UpsertConfig([FromBody] ForecastConfigDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Value))
        {
            return BadRequest(new { errors = new[] { "Name and Value are required." } });
        }

        var config = await dbContext.ForecastConfigs.FirstOrDefaultAsync(x => x.Name == dto.Name, cancellationToken);
        if (config is null)
        {
            config = new ForecastConfig
            {
                Name = dto.Name.Trim(),
                Value = dto.Value.Trim(),
                Description = dto.Description,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.ForecastConfigs.Add(config);
        }
        else
        {
            config.Value = dto.Value.Trim();
            config.Description = dto.Description;
            config.IsActive = dto.IsActive;
            config.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new ForecastConfigDto(config.Name, config.Value, config.Description, config.IsActive));
    }

    private async Task RemoveExistingSampleRows(IReadOnlyList<string> sampleIds, CancellationToken cancellationToken)
    {
        await dbContext.ForecastWarnings
            .Where(x => dbContext.ForecastResults.Any(result => sampleIds.Contains(result.BillIdentifier) && result.Id == x.ForecastResultId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ForecastSimilarSubscribers
            .Where(x => dbContext.ForecastResults.Any(result => sampleIds.Contains(result.BillIdentifier) && result.Id == x.ForecastResultId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ForecastResults.Where(x => sampleIds.Contains(x.BillIdentifier)).ExecuteDeleteAsync(cancellationToken);
        await dbContext.CustomerRecentConsumptionFeatures.Where(x => sampleIds.Contains(x.BillIdentifier)).ExecuteDeleteAsync(cancellationToken);
        await dbContext.CustomerMonthlyConsumptions.Where(x => sampleIds.Contains(x.BillIdentifier)).ExecuteDeleteAsync(cancellationToken);
        await dbContext.CustomerProfiles.Where(x => sampleIds.Contains(x.BillIdentifier)).ExecuteDeleteAsync(cancellationToken);
    }

    private static List<CustomerProfile> BuildProfiles(SeedSampleDataRequest request, IReadOnlyList<string> sampleIds) =>
        sampleIds.Select((billIdentifier, index) => new CustomerProfile
        {
            BillIdentifier = billIdentifier,
            CoCode = request.CoCode,
            RegionCode = request.RegionCode,
            CityCode = request.CityCode,
            TariffType = request.TariffType,
            UsageType = 1,
            Phase = request.Phase,
            Ampere = request.Ampere,
            ClimateType = request.ClimateType,
            IsUrban = true,
            MeterType = request.MeterType,
            IsSmartMeter = index % 3 == 0,
            IsMultiTariff = false,
            ActivityStatus = "Active",
            CreatedAt = DateTime.UtcNow
        }).ToList();

    private static List<CustomerMonthlyConsumption> BuildConsumptions(SeedSampleDataRequest request, IReadOnlyList<string> sampleIds)
    {
        var rows = new List<CustomerMonthlyConsumption>();
        var targetPattern = new[] { 100m, 120m, 140m, 160m };
        AddSeries(rows, sampleIds[0], request.TargetYear, request.TargetMonth, targetPattern, comparableConsumption: null, offsetMonths: -4);

        for (var i = 1; i < sampleIds.Count; i++)
        {
            var spread = (i % 7) - 3;
            var baseWindow = new[]
            {
                100m + spread,
                120m + spread,
                140m + spread,
                160m + spread
            };
            var comparable = 178m + spread;
            AddSeries(rows, sampleIds[i], request.TargetYear, request.TargetMonth, baseWindow, comparable, offsetMonths: -9);

            var recentWindow = new[] { 102m + spread, 122m + spread, 142m + spread, 162m + spread };
            AddSeries(rows, sampleIds[i], request.TargetYear, request.TargetMonth, recentWindow, comparableConsumption: null, offsetMonths: -4);
        }

        return rows;
    }

    private static void AddSeries(
        List<CustomerMonthlyConsumption> rows,
        string billIdentifier,
        int targetYear,
        int targetMonth,
        IReadOnlyList<decimal> values,
        decimal? comparableConsumption,
        int offsetMonths)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var (year, month) = AddMonths(targetYear, targetMonth, offsetMonths + i);
            rows.Add(CreateConsumption(billIdentifier, year, month, values[i]));
        }

        if (comparableConsumption.HasValue)
        {
            var (year, month) = AddMonths(targetYear, targetMonth, offsetMonths + values.Count);
            rows.Add(CreateConsumption(billIdentifier, year, month, comparableConsumption.Value));
        }
    }

    private static CustomerMonthlyConsumption CreateConsumption(string billIdentifier, int year, int month, decimal consumption) => new()
    {
        BillIdentifier = billIdentifier,
        Year = year,
        Month = month,
        Consumption = consumption,
        DaysCount = 30,
        ReadingType = "Actual",
        BillType = "Normal",
        IsActualReading = true,
        IsSmartReading = false,
        DataQualityStatus = "Valid",
        CreatedAt = DateTime.UtcNow
    };

    private static IReadOnlyList<string> BuildSampleIds(string targetBillIdentifier, int candidateCount)
    {
        var ids = new List<string> { targetBillIdentifier };
        for (var i = 1; i <= candidateCount; i++)
        {
            ids.Add($"DEMO-CAND-{i:000}");
        }

        return ids;
    }

    private static (int Year, int Month) AddMonths(int year, int month, int offset)
    {
        var zeroBased = year * 12 + month - 1 + offset;
        return (zeroBased / 12, zeroBased % 12 + 1);
    }

    private static int ToMonthKey(int year, int month) => year * 12 + month;
}

public sealed record SeedSampleDataRequest(
    string TargetBillIdentifier,
    int TargetYear,
    int TargetMonth,
    int CoCode,
    int RegionCode,
    int? CityCode,
    int TariffType,
    int ClimateType,
    int Phase,
    decimal Ampere,
    int MeterType,
    int CandidateCount)
{
    public SeedSampleDataRequest Normalize() => this with
    {
        TargetBillIdentifier = string.IsNullOrWhiteSpace(TargetBillIdentifier) ? "DEMO-TARGET" : TargetBillIdentifier.Trim(),
        TargetYear = TargetYear <= 0 ? 1403 : TargetYear,
        TargetMonth = TargetMonth is < 1 or > 12 ? 8 : TargetMonth,
        CoCode = CoCode <= 0 ? 1 : CoCode,
        RegionCode = RegionCode <= 0 ? 10 : RegionCode,
        CityCode = CityCode <= 0 ? 100 : CityCode,
        TariffType = TariffType <= 0 ? 1 : TariffType,
        ClimateType = ClimateType <= 0 ? 1 : ClimateType,
        Phase = Phase <= 0 ? 1 : Phase,
        Ampere = Ampere <= 0 ? 25m : Ampere,
        MeterType = MeterType <= 0 ? 1 : MeterType,
        CandidateCount = CandidateCount < 20 ? 24 : Math.Min(CandidateCount, 200)
    };
}

public sealed record SeedSampleDataResponse(
    string TargetBillIdentifier,
    int TargetYear,
    int TargetMonth,
    int ProfilesCreated,
    int ConsumptionRowsCreated,
    int FeaturesBuilt);

public sealed record RebuildFeaturesRequest(int CoCode, int FeatureYear, int FeatureMonth, int MaximumCustomers);

public sealed record RebuildFeaturesResponse(int FeaturesBuilt);

public sealed record ForecastConfigDto(string Name, string Value, string? Description, bool IsActive);

internal static class CustomerRecentConsumptionFeatureServiceExtensions
{
    public static async Task BuildAndSaveFeatureAsync(
        this ICustomerRecentConsumptionFeatureService service,
        ForecastingDbContext dbContext,
        CustomerProfile profile,
        IReadOnlyList<CustomerMonthlyConsumption> history,
        int featureYear,
        int featureMonth,
        CancellationToken cancellationToken)
    {
        dbContext.CustomerRecentConsumptionFeatures.Add(service.BuildFeature(profile, history, featureYear, featureMonth));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
