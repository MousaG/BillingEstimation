namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class CustomerMonthlyConsumption
{
    public long Id { get; set; }
    public string BillIdentifier { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Consumption { get; set; }
    public decimal? PeakConsumption { get; set; }
    public decimal? MidPeakConsumption { get; set; }
    public decimal? OffPeakConsumption { get; set; }
    public decimal? FridayConsumption { get; set; }
    public decimal? ReactiveConsumption { get; set; }
    public int DaysCount { get; set; }
    public string ReadingType { get; set; } = string.Empty;
    public string BillType { get; set; } = string.Empty;
    public bool HasCorrection { get; set; }
    public bool HasMeterChange { get; set; }
    public bool IsActualReading { get; set; }
    public bool IsSmartReading { get; set; }
    public string DataQualityStatus { get; set; } = "Valid";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
