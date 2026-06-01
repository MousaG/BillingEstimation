namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class CustomerProfile
{
    public long Id { get; set; }
    public string BillIdentifier { get; set; } = string.Empty;
    public int CoCode { get; set; }
    public int RegionCode { get; set; }
    public int? CityCode { get; set; }
    public int? VillageCode { get; set; }
    public int TariffType { get; set; }
    public int UsageType { get; set; }
    public int Phase { get; set; }
    public decimal Ampere { get; set; }
    public decimal? ContractDemand { get; set; }
    public int ClimateType { get; set; }
    public bool IsUrban { get; set; }
    public int MeterType { get; set; }
    public bool IsSmartMeter { get; set; }
    public bool IsMultiTariff { get; set; }
    public DateOnly? InstallDate { get; set; }
    public string ActivityStatus { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
