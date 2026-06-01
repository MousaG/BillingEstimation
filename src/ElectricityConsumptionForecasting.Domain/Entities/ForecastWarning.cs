namespace ElectricityConsumptionForecasting.Domain.Entities;

public sealed class ForecastWarning
{
    public long Id { get; set; }
    public long ForecastResultId { get; set; }
    public ForecastResult? ForecastResult { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
