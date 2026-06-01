namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class CustomerRecentConsumptionFeature
{
    public long Id { get; set; }
    public string BillIdentifier { get; set; } = string.Empty;
    public int FeatureYear { get; set; }
    public int FeatureMonth { get; set; }
    public int CoCode { get; set; }
    public int RegionCode { get; set; }
    public int? CityCode { get; set; }
    public int TariffType { get; set; }
    public int ClimateType { get; set; }
    public int Phase { get; set; }
    public decimal Ampere { get; set; }
    public int MeterType { get; set; }
    public int ValidMonthsCount { get; set; }
    public decimal RecentAverageConsumption { get; set; }
    public decimal RecentMinimumConsumption { get; set; }
    public decimal RecentMaximumConsumption { get; set; }
    public decimal RecentStdDevConsumption { get; set; }
    public decimal LastConsumption { get; set; }
    public decimal TrendSlope { get; set; }
    public int ConsumptionBand { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
