using ElectricityConsumptionForecasting.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElectricityConsumptionForecasting.Infrastructure.Persistence;

public sealed class ForecastingDbContext : DbContext
{
    public ForecastingDbContext(DbContextOptions<ForecastingDbContext> options) : base(options)
    {
    }

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerMonthlyConsumption> CustomerMonthlyConsumptions => Set<CustomerMonthlyConsumption>();
    public DbSet<ForecastRun> ForecastRuns => Set<ForecastRun>();
    public DbSet<ForecastResult> ForecastResults => Set<ForecastResult>();
    public DbSet<ForecastSimilarSubscriber> ForecastSimilarSubscribers => Set<ForecastSimilarSubscriber>();
    public DbSet<ForecastWarning> ForecastWarnings => Set<ForecastWarning>();
    public DbSet<ForecastConfig> ForecastConfigs => Set<ForecastConfig>();
    public DbSet<CustomerDataQualityIssue> CustomerDataQualityIssues => Set<CustomerDataQualityIssue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.BillIdentifier).IsUnique();
            entity.HasIndex(x => new { x.CoCode, x.ClimateType, x.TariffType, x.RegionCode, x.CityCode });
            entity.Property(x => x.BillIdentifier).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ActivityStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Ampere).HasPrecision(10, 2);
            entity.Property(x => x.ContractDemand).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CustomerMonthlyConsumption>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BillIdentifier, x.Year, x.Month });
            entity.HasIndex(x => new { x.Year, x.Month, x.ReadingType, x.DataQualityStatus });
            entity.Property(x => x.BillIdentifier).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Consumption).HasPrecision(18, 3);
            entity.Property(x => x.PeakConsumption).HasPrecision(18, 3);
            entity.Property(x => x.MidPeakConsumption).HasPrecision(18, 3);
            entity.Property(x => x.OffPeakConsumption).HasPrecision(18, 3);
            entity.Property(x => x.FridayConsumption).HasPrecision(18, 3);
            entity.Property(x => x.ReactiveConsumption).HasPrecision(18, 3);
            entity.Property(x => x.ReadingType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BillType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DataQualityStatus).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ForecastRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ForecastResult>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BillIdentifier, x.TargetYear, x.TargetMonth });
            entity.HasIndex(x => new { x.CoCode, x.TargetYear, x.TargetMonth, x.IsForecastable });
            entity.Property(x => x.BillIdentifier).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PredictedConsumption).HasPrecision(18, 3);
            entity.Property(x => x.ActualConsumption).HasPrecision(18, 3);
            entity.Property(x => x.Confidence).HasPrecision(5, 4);
            entity.Property(x => x.ConfidenceLevel).HasMaxLength(32).IsRequired();
            entity.Property(x => x.RiskLevel).HasMaxLength(32).IsRequired();
            entity.Property(x => x.MethodName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(512).IsRequired();
            entity.HasMany(x => x.SimilarSubscribers).WithOne(x => x.ForecastResult).HasForeignKey(x => x.ForecastResultId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Warnings).WithOne(x => x.ForecastResult).HasForeignKey(x => x.ForecastResultId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ForecastSimilarSubscriber>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ForecastResultId);
            entity.Property(x => x.SimilarBillIdentifier).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SimilarityScore).HasPrecision(5, 4);
            entity.Property(x => x.ConsumptionSimilarity).HasPrecision(5, 4);
            entity.Property(x => x.TrendSimilarity).HasPrecision(5, 4);
            entity.Property(x => x.SeasonalSimilarity).HasPrecision(5, 4);
            entity.Property(x => x.ProfileSimilarity).HasPrecision(5, 4);
            entity.Property(x => x.GeographicSimilarity).HasPrecision(5, 4);
            entity.Property(x => x.SimilarMonthConsumption).HasPrecision(18, 3);
            entity.Property(x => x.Weight).HasPrecision(8, 6);
        });

        modelBuilder.Entity<ForecastWarning>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ForecastConfig>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512);
        });

        modelBuilder.Entity<CustomerDataQualityIssue>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BillIdentifier, x.Year, x.Month });
            entity.Property(x => x.BillIdentifier).HasMaxLength(32).IsRequired();
            entity.Property(x => x.IssueCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IssueDescription).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
        });
    }
}
