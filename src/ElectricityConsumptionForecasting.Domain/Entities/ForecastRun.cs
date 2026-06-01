namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class ForecastRun
{
    public long Id { get; set; }
    public int CoCode { get; set; }
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "Started";
    public int TotalCustomers { get; set; }
    public int ForecastedCount { get; set; }
    public int NotForecastableCount { get; set; }
    public int FailedCount { get; set; }
    public string? ParametersJson { get; set; }
}
